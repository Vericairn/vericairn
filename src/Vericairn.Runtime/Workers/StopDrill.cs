// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>What a stop drill measured of its scratch run.</summary>
/// <param name="Contained">Whether the worker observed the scratch run's whole boundary exit.</param>
/// <param name="WithinLimit">Whether that containment came within the drill's limit, counted from the stop request.</param>
/// <param name="StartedAt">When the drill started its scratch run.</param>
/// <param name="RequestedAt">When the stop was requested, RFC 3339 UTC seconds.</param>
/// <param name="ContainedAt">When containment was observed, or null.</param>
/// <param name="StopToContained">How long from the stop request to the observed containment, or null when none was observed.</param>
/// <param name="LastWriteAt">When the scratch run last wrote, to the millisecond, or null when it wrote nothing that could be read.</param>
/// <param name="StopToLastWrite">How long from the stop request to the scratch run's last write; zero when it wrote nothing after the stop.</param>
/// <param name="Stop">The stop's facts as the worker observed them.</param>
/// <param name="Hurried">Whether the drill escalated the stop itself once its limit passed.</param>
/// <param name="FinishedAt">When the drill finished.</param>
/// <param name="Problem">Why the drill could not measure, or null.</param>
internal sealed record DrillMeasurement(bool Contained, bool WithinLimit, string StartedAt, string? RequestedAt, string? ContainedAt, TimeSpan? StopToContained,
    string? LastWriteAt, TimeSpan? StopToLastWrite, RunStop? Stop, bool Hurried, string FinishedAt, string? Problem);

/// <summary>
/// The timed stop drill (OPS-21, decision 01M7DH3HRQH21S1G8A5EYA5FBT): the worker starts a scratch run on this machine, under the same
/// supervision as an agent's run, waits until it is writing, asks it to stop through its run directory exactly as `worker stop` does, and
/// measures the time from that request to the observed containment of its whole boundary and to its last write. The scratch run is the
/// vericairn executable with a private argument: it appends the time to a file in its own scratch directory every fifty milliseconds and
/// ignores the stop it is sent, the stop file and, on Linux and macOS, SIGTERM, as an unresponsive agent would, so it is contained only by
/// the worker's escalation after the grace: the longest path a stop takes. It records nothing itself and is never an agent run.
/// </summary>
internal static class StopDrill
{
    /// <summary>The private argument that makes the vericairn executable the drill's scratch run.</summary>
    internal const string Marker = "--vericairn-worker-stop-drill-run";

    /// <summary>The verification method a drill records.</summary>
    internal const string Method = "stop-drill";

    /// <summary>How often the scratch run writes.</summary>
    internal static readonly TimeSpan WriteInterval = TimeSpan.FromMilliseconds(50);

    // A scratch run whose drill was lost ends by itself.
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    // How long the drill waits for the scratch run's first write, and for containment after it escalates past its limit: hang guards only.
    private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ContainWait = TimeSpan.FromSeconds(60);

    private const string WritesFile = "writes";

    /// <summary>Runs as the scratch run for the private invocation; null for an ordinary command line.</summary>
    internal static int? TryRun(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != 2 || arguments[0] != Marker || Path.GetFileName(arguments[1]) != WritesFile) return null;
        using PosixSignalRegistration? ignored = IgnoreTerminate();
        var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.ReadWrite | FileShare.Delete };
        using var stream = new FileStream(arguments[1], options);
        long deadline = Stopwatch.GetTimestamp() + (long)(Lifetime.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            stream.Write(Encoding.ASCII.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + "\n"));
            stream.Flush();
            Thread.Sleep(WriteInterval);
        }

        return 0;
    }

    /// <summary>
    /// Starts a scratch run, stops it once it is writing, and measures. The stop names its grace; when containment is not observed within
    /// the limit, the drill escalates it at once and still waits for containment, so a scratch run is never left behind unobserved.
    /// </summary>
    internal static DrillMeasurement Run(string estate, string by, WorkerOptions options, TimeSpan limit, int graceSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(estate);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        ArgumentNullException.ThrowIfNull(options);
        string run = Ulid.NewUlid().Value;
        string startedAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
        RunDirectory directory = options.OpenRunDirectory(estate, run);
        RunSupervisor? supervisor = null;
        Thread? supervising = null;
        RunOutcome? outcome = null;
        long containedTick = 0;
        using var observed = new ManualResetEventSlim();
        using var abandon = new CancellationTokenSource();
        try
        {
            directory.Create();
            string scratch = Path.Combine(directory.Path, "scratch");
            Directory.CreateDirectory(scratch);
            string writes = Path.Combine(scratch, WritesFile);
            Dictionary<string, string> environment = WorkerEnvironment.Build(options.Environment ?? WorkerEnvironment.Current());
            environment["VERICAIRN_STOP_FILE"] = directory.StopFile;
            environment["TMPDIR"] = environment["TMP"] = environment["TEMP"] = directory.Temporary;
            Directory.CreateDirectory(directory.Temporary);
            try
            {
                supervisor = RunSupervisor.Start(run, [.. options.VericairnCommand(), Marker, writes], scratch, environment, new ScratchLauncher(), directory, options,
                    wallMinutes: 60, tokenCap: null, stallLimit: TimeSpan.FromHours(1));
            }
            catch (Exception exception) when (exception is ProviderRefusedException or RunLaunchException)
            {
                return Failed(startedAt, "the scratch run could not be started here: " + exception.Message);
            }

            RunSupervisor started = supervisor;
            supervising = new Thread(() =>
            {
                try { outcome = started.Supervise(abandon.Token); }
                finally
                {
                    containedTick = Stopwatch.GetTimestamp();
                    observed.Set();
                }
            }) { IsBackground = true, Name = "vericairn-stop-drill" };
            supervising.Start();

            // The precondition: the scratch run is writing. Only then is the stop asked.
            long until = Stopwatch.GetTimestamp() + (long)(StartWait.TotalSeconds * Stopwatch.Frequency);
            while (LastWrite(writes) is null)
            {
                if (observed.IsSet) return Failed(startedAt, "the scratch run exited before it wrote");
                if (Stopwatch.GetTimestamp() > until) return Failed(startedAt, "the scratch run did not start writing within " + StartWait.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " seconds");
                Thread.Sleep(10);
            }

            DateTimeOffset requested = DateTimeOffset.UtcNow;
            long requestedTick = Stopwatch.GetTimestamp();
            StopRequest asked = directory.RequestStop(new StopRequest(RecordParser.FormatTimestamp(requested), by, "the timed stop drill", graceSeconds));
            bool withinLimit = observed.Wait(limit);
            bool hurried = false;
            if (!withinLimit)
            {
                // Past its limit the drill contains the scratch run itself, and still records that it was not contained in time.
                hurried = true;
                started.EscalateNow();
                if (!observed.Wait(ContainWait)) abandon.Cancel();
                observed.Wait(ContainWait);
            }

            bool contained = outcome is not null;
            TimeSpan? toContained = contained ? Stopwatch.GetElapsedTime(requestedTick, containedTick) : null;
            DateTimeOffset? lastWrite = LastWrite(writes);
            TimeSpan? toLastWrite = lastWrite is null ? null : lastWrite.Value > requested ? lastWrite.Value - requested : TimeSpan.Zero;
            string? problem = contained ? null
                : $"the worker did not observe the scratch run contained (its process {started.ProcessId}); it may still run and must be ended by hand";
            return new(contained, withinLimit && contained, startedAt, asked.RequestedAt, outcome?.Stop?.ContainedAt ?? (outcome is null ? null : RecordParser.FormatTimestamp(outcome.ExitedAt)),
                toContained, lastWrite?.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture), toLastWrite, outcome?.Stop, hurried,
                RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), problem);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return Failed(startedAt, "the scratch run's directory could not be used: " + exception.GetType().Name);
        }
        finally
        {
            if (supervisor is not null && !observed.IsSet)
            {
                // A drill that stops early still contains what it started.
                supervisor.EscalateNow();
                try { directory.RequestStop(new StopRequest(RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), by, "the timed stop drill ended", 0)); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    // The supervisor treats an unreadable request as a stop; escalation is already asked for.
                }

                if (!observed.Wait(ContainWait)) abandon.Cancel();
            }

            supervising?.Join(ContainWait);
            if (outcome is not null)
            {
                try { Directory.Delete(directory.Path, recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A scratch directory that cannot be removed now is left beside the runs; it holds no record.
                }
            }
        }
    }

    // The last complete line the scratch run wrote: the time of its last write, to the millisecond. A line cut short by the kill is ignored.
    private static DateTimeOffset? LastWrite(string writes)
    {
        try
        {
            if (!File.Exists(writes)) return null;
            using var stream = new FileStream(writes, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.ASCII);
            string text = reader.ReadToEnd();
            int end = text.LastIndexOf('\n');
            if (end < 0) return null;
            int start = text.LastIndexOf('\n', end - 1) + 1;
            return long.TryParse(text.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out long milliseconds)
                ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DrillMeasurement Failed(string startedAt, string problem) =>
        new(false, false, startedAt, null, null, null, null, null, null, false, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), problem);

    private static PosixSignalRegistration? IgnoreTerminate()
    {
        try
        {
            return PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => context.Cancel = true);
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The scratch run's launcher: it reads nothing from the run's output and never stops it for silence.</summary>
    private sealed class ScratchLauncher : IAgentLauncher
    {
        public string Launcher => "stop-drill";

        public ProviderDescriptor Descriptor { get; } = new("stop-drill", IAgentLauncher.Contract, "implemented", [],
            ["Starts only the stop drill's scratch run, the vericairn executable with a private argument; never an agent."]);

        public bool Generic => true;

        public TimeSpan StallLimit => TimeSpan.FromHours(1);

        public IReadOnlyList<string> Command(LaunchContext context) => throw new ProviderRefusedException("The stop drill's launcher starts only its own scratch run.");
    }
}
