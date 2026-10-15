// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>What a supervised run ended with, as the worker observed it.</summary>
/// <param name="ExitCode">The harness's exit code.</param>
/// <param name="LaunchedAt">When it started.</param>
/// <param name="ExitedAt">When its exit was observed.</param>
/// <param name="Heartbeats">How many times the worker observed it running.</param>
/// <param name="Tokens">The tokens it reported, when it reported any.</param>
/// <param name="Price">The price it reported, in minor units, when its launcher reads one.</param>
/// <param name="PriceUnit">The price's currency.</param>
/// <param name="Liveness">Worker-observed silence and stall handling.</param>
/// <param name="EscalationFailure">An escalation failure retained after retry.</param>
/// <param name="Stop">A stop's facts, when one was asked.</param>
public sealed record RunOutcome(int ExitCode, DateTimeOffset LaunchedAt, DateTimeOffset ExitedAt, long Heartbeats, long? Tokens, long? Price, string? PriceUnit, RunStop? Stop, string? EscalationFailure = null, RunLiveness? Liveness = null);

/// <summary>
/// The worker's supervision of one harness process (the estate's design/goals.md § 7, steps 4 and 6). It observes the process at
/// every heartbeat, reads its output through its launcher for usage and for an acknowledged stop, and settles nothing itself: only
/// the whole boundary's observed exit ends supervision, so output that closes or a connection lost is not a stopped run. A stop asked of
/// the run is transmitted once (the stop file the harness is told of, and SIGTERM on Linux and macOS); if the harness has not
/// stopped within the grace period, escalation terminates its launch boundary and is retried after failure. Containment requires
/// observing the whole boundary exited. Operational wall, token and currency thresholds leave headroom below the reserved soft
/// accounting caps, with the worker as the stop's author; stdout silence raises attention at one stall limit and stops at two.
/// </summary>
public sealed class RunSupervisor
{
    /// <summary>The line a harness or its wrapper prints on its output to acknowledge a stop.</summary>
    public const string AcknowledgmentLine = """{"vericairn":"stop-acknowledged"}""";

    private const long OutputLimit = 16L * 1024 * 1024;
    private readonly object gate = new();
    private readonly ContainedProcess process;
    private readonly IAgentLauncher launcher;
    private readonly RunDirectory directory;
    private readonly WorkerOptions options;
    private readonly RunObservedState state;
    private readonly DateTimeOffset? deadline;
    private readonly long wallMinutes;
    private readonly long? tokenCap;
    private readonly RunLimits? limits;
    private readonly Thread[] readers;
    private long written;
    private long? tokens;
    private bool tokensIncomplete;
    private bool usageInFlight;
    private long? price;
    private string? priceUnit;
    private DateTimeOffset? transmitted;
    private TimeSpan grace;
    private readonly Func<RunLiveness, string?>? attention;
    private long lastEventTick = Stopwatch.GetTimestamp();

    private RunSupervisor(ContainedProcess process, IAgentLauncher launcher, RunDirectory directory, WorkerOptions options, RunObservedState state, DateTimeOffset launchedAt, long wallMinutes, long? tokenCap, RunLimits? limits, TimeSpan stallLimit, Func<RunLiveness, string?>? attention)
    {
        this.process = process;
        this.launcher = launcher;
        this.directory = directory;
        this.options = options;
        this.state = state;
        this.wallMinutes = wallMinutes;
        this.tokenCap = tokenCap;
        this.limits = limits;
        this.attention = attention;
        state.Liveness = new RunLiveness((int)stallLimit.TotalSeconds, RecordParser.FormatTimestamp(launchedAt));
        LaunchedAt = launchedAt;
        deadline = launchedAt + (limits?.WallTime ?? (options.Minute * wallMinutes));
        grace = options.Grace;
        readers =
        [
            new Thread(() => Pump(process.StandardOutput, false)) { IsBackground = true, Name = "vericairn-run-output" },
            new Thread(() => Pump(process.StandardError, true)) { IsBackground = true, Name = "vericairn-run-errors" },
        ];
        foreach (Thread reader in readers)
        {
            reader.Start();
        }
    }

    /// <summary>When the harness started.</summary>
    public DateTimeOffset LaunchedAt { get; }

    /// <summary>The harness's process id.</summary>
    public int ProcessId => process.Id;

    /// <summary>
    /// Starts a harness: the command line in the workspace with the environment given and nothing else, its standard input closed so
    /// that it never waits on one. Throws <see cref="ProviderRefusedException"/> when it cannot be started.
    /// </summary>
    public static RunSupervisor Start(string run, IReadOnlyList<string> command, string workspace, IReadOnlyDictionary<string, string> environment, IAgentLauncher launcher, RunDirectory directory, WorkerOptions options, long wallMinutes, long? tokenCap, RunLimits? limits = null, TimeSpan? stallLimit = null, Func<RunLiveness, string?>? attention = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(options);
        if (command.Count == 0)
        {
            throw new ProviderRefusedException("The launcher named no command.");
        }

        var info = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workspace,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (string argument in command.Skip(1))
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment.Clear();
        foreach ((string name, string value) in environment)
        {
            info.Environment[name] = value;
        }

        ContainedProcess process;
        try
        {
            process = ContainedProcess.Start(info);
        }
        catch (Win32Exception exception)
        {
            throw new ProviderRefusedException($"{command[0]} could not be started here: {exception.Message}", exception);
        }

        DateTimeOffset launchedAt = DateTimeOffset.UtcNow;
        var state = new RunObservedState
        {
            Run = run,
            Supervisor = Environment.ProcessId,
            HeartbeatSeconds = options.Heartbeat.TotalSeconds,
            ProcessId = process.Id,
            LaunchedAt = RecordParser.FormatTimestamp(launchedAt),
            Heartbeat = RecordParser.FormatTimestamp(launchedAt),
        };
        try
        {
            var supervisor = new RunSupervisor(process, launcher, directory, options, state, launchedAt, wallMinutes, tokenCap, limits, stallLimit ?? launcher.StallLimit, attention);
            supervisor.Save();
            return supervisor;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            throw new RunLaunchException(launchedAt, exception);
        }
    }

    /// <summary>
    /// Observes the harness until its exit is observed, and returns what it ended with; null when the worker gives up observing it
    /// first, which leaves the run in flight, since a supervision that ends is not a stopped run.
    /// </summary>
    public RunOutcome? Supervise(CancellationToken abandon)
    {
        while (true)
        {
            bool rootExited = process.WaitForExit(options.Heartbeat);
            if (rootExited && process.BoundaryExited())
            {
                break;
            }

            if (rootExited)
            {
                Thread.Sleep(options.Heartbeat);
            }

            if (abandon.IsCancellationRequested)
            {
                return null;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            lock (gate)
            {
                state.Heartbeat = RecordParser.FormatTimestamp(now);
                state.Heartbeats++;
                state.Tokens = tokensIncomplete || usageInFlight ? null : tokens;
                TimeSpan silence = Stopwatch.GetElapsedTime(lastEventTick);
                RunLiveness liveness = state.Liveness!;
                if (silence.TotalSeconds >= liveness.StallSeconds)
                {
                    liveness = liveness with { AttentionAt = liveness.AttentionAt ?? RecordParser.FormatTimestamp(now) };
                    if (liveness.Attention is null)
                    {
                        try
                        {
                            liveness = liveness with { Attention = attention?.Invoke(liveness) };
                        }
                        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
                        {
                            // Attention recording is retried; a failed record must never prevent containment.
                        }
                    }

                    state.Liveness = liveness;
                }

                if (state.Stop is null)
                {
                    if (rootExited)
                    {
                        Begin(new StopRequest(RecordParser.FormatTimestamp(now), "worker", "the harness exited while descendants remained in its containment boundary"));
                    }
                    else if (directory.ReadStop() is StopRequest asked)
                    {
                        Begin(asked);
                    }
                    else if (silence.TotalSeconds >= 2L * liveness.StallSeconds)
                    {
                        state.Liveness = liveness with { StoppedAt = RecordParser.FormatTimestamp(now) };
                        Begin(new StopRequest(RecordParser.FormatTimestamp(now), "worker", $"the worker observed the event stream silent for twice the stall limit of {liveness.StallSeconds.ToString(CultureInfo.InvariantCulture)} seconds"));
                    }
                    else if (now >= deadline)
                    {
                        Begin(new StopRequest(RecordParser.FormatTimestamp(now), "worker", $"the run reached its wall-time stop threshold below its allowance of {wallMinutes.ToString(CultureInfo.InvariantCulture)} wall minutes, reserving shutdown headroom"));
                    }
                    else if ((limits?.Tokens ?? tokenCap) is long cap && tokens >= cap)
                    {
                        Begin(new StopRequest(RecordParser.FormatTimestamp(now), "worker", $"the tokens the harness reported reached the operational threshold of {cap.ToString(CultureInfo.InvariantCulture)}, below the reserved cap of {tokenCap?.ToString(CultureInfo.InvariantCulture)}"));
                    }
                    else if (limits?.Currency is long currency && priceUnit == limits.CurrencyUnit && price >= currency)
                    {
                        Begin(new StopRequest(RecordParser.FormatTimestamp(now), "worker", "the reported currency reached its operational threshold below the reserved cap"));
                    }
                }
                else if (transmitted is DateTimeOffset at && now - at >= grace)
                {
                    Escalate(now);
                }

                Save();
            }
        }

        foreach (Thread reader in readers)
        {
            reader.Join(TimeSpan.FromSeconds(5));
        }

        lock (gate)
        {
            DateTimeOffset exited = DateTimeOffset.UtcNow;
            state.ExitCode = process.ExitCode;
            state.ExitedAt = RecordParser.FormatTimestamp(exited);
            state.Tokens = tokensIncomplete || usageInFlight ? null : tokens;
            if (state.Stop is not null)
            {
                state.Stop = state.Stop with { ContainedAt = RecordParser.FormatTimestamp(exited) };
            }

            Save();
            var outcome = new RunOutcome(process.ExitCode, LaunchedAt, exited, state.Heartbeats, tokensIncomplete || usageInFlight ? null : tokens, price, priceUnit, state.Stop, state.EscalationFailure, state.Liveness);
            process.Close();
            return outcome;
        }
    }

    /// <summary>Records in the run's state on this machine that its settlement was recorded, or why it was not.</summary>
    public void Settled(string? status, string? problem)
    {
        lock (gate)
        {
            state.Settled = status;
            state.Problem = problem;
            Save();
        }
    }

    private void Begin(StopRequest request)
    {
        grace = request.GraceSeconds is int seconds and >= 0 ? TimeSpan.FromSeconds(seconds) : options.Grace;
        state.Stop = new RunStop(request.RequestedAt, request.RequestedBy, request.Reason);
        var transmission = new StringBuilder();
        try
        {
            File.WriteAllText(directory.StopFile, JsonSerializer.Serialize(request, ProtocolJson.Options));
            transmission.Append("the stop file the harness is told of in VERICAIRN_STOP_FILE");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The reason can name a path on this machine, which never enters a record.
            transmission.Append("no stop file, which could not be written here");
        }

        if (OperatingSystem.IsWindows())
        {
            transmission.Append("; Windows has no signal a windowless process receives, so a harness that does not watch the file is contained by escalation");
        }
        else
        {
            string? failure = Signal(force: false);
            transmission.Append(failure is null ? ", and SIGTERM to the whole process group" : $"; SIGTERM was not sent ({failure})");
        }

        transmitted = DateTimeOffset.UtcNow;
        state.Stop = state.Stop with { TransmittedAt = RecordParser.FormatTimestamp(transmitted.Value), Transmission = transmission.ToString() };
    }

    private void Escalate(DateTimeOffset now)
    {
        string? failure = Signal(force: true);
        if (failure is not null)
        {
            state.EscalationFailure = "Containment escalation failed; it will be retried: " + failure;
            return;
        }

        if (state.Stop!.EscalatedAt is not null)
        {
            return;
        }

        state.Stop = state.Stop with { EscalatedAt = RecordParser.FormatTimestamp(now), Escalation = $"the harness had not stopped {grace.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds after the stop was transmitted, so termination of its containment boundary was requested; containment requires observing the whole boundary empty" };
    }

    private void Acknowledge(string how)
    {
        lock (gate)
        {
            if (state.Stop is { TransmittedAt: not null, AcknowledgedAt: null })
            {
                state.Stop = state.Stop with { AcknowledgedAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), Acknowledgment = how };
                Save();
            }
        }
    }

    private void Pump(StreamReader reader, bool errors)
    {
        try
        {
            while (reader.ReadLine() is string line)
            {
                Log(errors ? "[stderr] " + line : line);
                if (errors)
                {
                    continue;
                }

                lock (gate)
                {
                    lastEventTick = Stopwatch.GetTimestamp();
                    state.Liveness = state.Liveness! with { LastEventAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) };
                }

                if (IsAcknowledgment(line))
                {
                    Acknowledge("the harness printed its acknowledgment on its output");
                    continue;
                }

                HarnessSignal? signal = null;
                try
                {
                    signal = launcher.Read(line);
                }
                catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
                {
                    lock (gate)
                    {
                        tokensIncomplete = true;
                    }
                }

                lock (gate)
                {
                    state.Output = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
                    tokensIncomplete |= signal?.UsageIncomplete == true;
                    if (signal?.UsageStarted == true)
                    {
                        usageInFlight = true;
                    }

                    if (signal?.UsageCompleted == true)
                    {
                        usageInFlight = false;
                    }

                    try
                    {
                        if (signal?.Tokens is long total)
                        {
                            if (total < 0 || total < tokens)
                            {
                                tokensIncomplete = true;
                            }
                            else
                            {
                                tokens = total;
                            }
                        }

                        if (signal?.TokensAdded is long added)
                        {
                            if (added < 0)
                            {
                                tokensIncomplete = true;
                            }
                            else
                            {
                                tokens = checked((tokens ?? 0) + added);
                            }
                        }
                    }
                    catch (OverflowException)
                    {
                        tokensIncomplete = true;
                    }

                    if (signal?.Price is long cost)
                    {
                        price = cost;
                        priceUnit = signal.PriceUnit;
                    }
                }

                if (signal?.Acknowledged == true)
                {
                    Acknowledge("the harness reported it, as its launcher reads its output");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Output closure proves no exit; the whole launch boundary must be observed exited.
        }
    }

    private void Log(string line)
    {
        lock (gate)
        {
            if (written > OutputLimit)
            {
                return;
            }

            try
            {
                File.AppendAllText(directory.OutputLog, line + "\n");
                written += line.Length + 1;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The log is a convenience on this machine; the run goes on without it.
            }
        }
    }

    private void Save()
    {
        try
        {
            directory.Write(state);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next heartbeat writes it again.
        }
    }

    private static bool IsAcknowledgment(string line)
    {
        string trimmed = line.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.Contains("\"vericairn\"", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("vericairn", out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() == "stop-acknowledged";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private string? Signal(bool force)
    {
        try
        {
            options.BeforeSignal?.Invoke(force);
            process.Signal(force);
            return null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return "the boundary could not be signalled";
        }
    }
}
