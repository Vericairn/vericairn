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
/// <param name="Stop">A stop's facts, when one was asked.</param>
public sealed record RunOutcome(int ExitCode, DateTimeOffset LaunchedAt, DateTimeOffset ExitedAt, long Heartbeats, long? Tokens, long? Price, string? PriceUnit, RunStop? Stop);

/// <summary>
/// The worker's supervision of one harness process (the estate's design/goals.md § 7, steps 4 and 6). It observes the process at
/// every heartbeat, reads its output through its launcher for usage and for an acknowledged stop, and settles nothing itself: only
/// the process's exit ends the supervision, so output that closes or a connection that is lost is not a stopped run. A stop asked of
/// the run is transmitted once (the stop file the harness is told of, and SIGTERM on Linux and macOS); if the harness has not
/// stopped within the grace period, the stop is escalated by killing its process tree; containment is the exit observed. The run's
/// cap of wall minutes, and of tokens when the harness reports them, is enforced the same way, with the worker as the stop's author.
/// </summary>
public sealed class RunSupervisor
{
    /// <summary>The line a harness or its wrapper prints on its output to acknowledge a stop.</summary>
    public const string AcknowledgmentLine = """{"vericairn":"stop-acknowledged"}""";

    private const long OutputLimit = 16L * 1024 * 1024;
    private readonly object gate = new();
    private readonly Process process;
    private readonly IAgentLauncher launcher;
    private readonly RunDirectory directory;
    private readonly WorkerOptions options;
    private readonly RunObservedState state;
    private readonly DateTimeOffset? deadline;
    private readonly long wallMinutes;
    private readonly long? tokenCap;
    private readonly Thread[] readers;
    private long written;
    private long? tokens;
    private long? price;
    private string? priceUnit;
    private DateTimeOffset? transmitted;
    private TimeSpan grace;

    private RunSupervisor(Process process, IAgentLauncher launcher, RunDirectory directory, WorkerOptions options, RunObservedState state, DateTimeOffset launchedAt, long wallMinutes, long? tokenCap)
    {
        this.process = process;
        this.launcher = launcher;
        this.directory = directory;
        this.options = options;
        this.state = state;
        this.wallMinutes = wallMinutes;
        this.tokenCap = tokenCap;
        LaunchedAt = launchedAt;
        deadline = launchedAt + (options.Minute * wallMinutes);
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
    public static RunSupervisor Start(string run, IReadOnlyList<string> command, string workspace, IReadOnlyDictionary<string, string> environment, IAgentLauncher launcher, RunDirectory directory, WorkerOptions options, long wallMinutes, long? tokenCap)
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

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new ProviderRefusedException($"{command[0]} did not start.");
        }
        catch (Win32Exception exception)
        {
            throw new ProviderRefusedException($"{command[0]} could not be started here: {exception.Message}", exception);
        }

        DateTimeOffset launchedAt = DateTimeOffset.UtcNow;
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // A harness that already exited closed it first.
        }

        var state = new RunObservedState
        {
            Run = run,
            Supervisor = Environment.ProcessId,
            HeartbeatSeconds = options.Heartbeat.TotalSeconds,
            ProcessId = process.Id,
            LaunchedAt = RecordParser.FormatTimestamp(launchedAt),
            Heartbeat = RecordParser.FormatTimestamp(launchedAt),
        };
        directory.Write(state);
        return new RunSupervisor(process, launcher, directory, options, state, launchedAt, wallMinutes, tokenCap);
    }

    /// <summary>
    /// Observes the harness until its exit is observed, and returns what it ended with; null when the worker gives up observing it
    /// first, which leaves the run in flight, since a supervision that ends is not a stopped run.
    /// </summary>
    public RunOutcome? Supervise(CancellationToken abandon)
    {
        while (!process.WaitForExit(options.Heartbeat))
        {
            if (abandon.IsCancellationRequested)
            {
                return null;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            lock (gate)
            {
                state.Heartbeat = RecordParser.FormatTimestamp(now);
                state.Heartbeats++;
                state.Tokens = tokens;
                if (state.Stop is null)
                {
                    if (directory.ReadStop() is StopRequest asked)
                    {
                        Begin(asked, now);
                    }
                    else if (now >= deadline)
                    {
                        Begin(new StopRequest(RecordParser.FormatTimestamp(now), "worker", $"the run reached its cap of {wallMinutes.ToString(CultureInfo.InvariantCulture)} wall minutes"), now);
                    }
                    else if (tokenCap is long cap && tokens > cap)
                    {
                        Begin(new StopRequest(RecordParser.FormatTimestamp(now), "worker", $"the tokens the harness reported crossed the run's cap of {cap.ToString(CultureInfo.InvariantCulture)}"), now);
                    }
                }
                else if (transmitted is DateTimeOffset at && state.Stop.EscalatedAt is null && now - at >= grace)
                {
                    Escalate(now);
                }

                Save();
            }
        }

        DateTimeOffset exited = DateTimeOffset.UtcNow;
        foreach (Thread reader in readers)
        {
            reader.Join(TimeSpan.FromSeconds(5));
        }

        lock (gate)
        {
            state.ExitCode = process.ExitCode;
            state.ExitedAt = RecordParser.FormatTimestamp(exited);
            state.Tokens = tokens;
            if (state.Stop is not null)
            {
                state.Stop = state.Stop with { ContainedAt = RecordParser.FormatTimestamp(exited) };
            }

            Save();
            return new RunOutcome(process.ExitCode, LaunchedAt, exited, state.Heartbeats, tokens, price, priceUnit, state.Stop);
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

    private void Begin(StopRequest request, DateTimeOffset now)
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
            string? failure = Terminate(process.Id);
            transmission.Append(failure is null ? ", and SIGTERM to the harness" : $"; SIGTERM was not sent ({failure})");
        }

        transmitted = now;
        state.Stop = state.Stop with { TransmittedAt = RecordParser.FormatTimestamp(now), Transmission = transmission.ToString() };
    }

    private void Escalate(DateTimeOffset now)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // It exited meanwhile; the exit is observed next.
        }

        state.Stop = state.Stop! with { EscalatedAt = RecordParser.FormatTimestamp(now), Escalation = $"the harness had not stopped {grace.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds after the stop was transmitted, so its process tree was killed" };
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
                    // A line the launcher cannot read reports nothing.
                }

                lock (gate)
                {
                    state.Output = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
                    if (signal?.Tokens is long total)
                    {
                        tokens = total;
                    }

                    if (signal?.TokensAdded is long added)
                    {
                        tokens = (tokens ?? 0) + added;
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
            // The output closed; only the process's exit ends the run.
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

    /// <summary>Sends SIGTERM to a process with the kill command; null when it was sent, else why not.</summary>
    private static string? Terminate(int processId)
    {
        try
        {
            var info = new ProcessStartInfo("kill") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            info.ArgumentList.Add("-s");
            info.ArgumentList.Add("TERM");
            info.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
            using Process kill = Process.Start(info) ?? throw new InvalidOperationException("kill did not start");
            string error = kill.StandardError.ReadToEnd();
            if (!kill.WaitForExit(5000))
            {
                kill.Kill();
                return "kill did not answer";
            }

            return kill.ExitCode == 0 ? null : error.Trim().Length > 0 ? error.Trim() : $"kill exited {kill.ExitCode.ToString(CultureInfo.InvariantCulture)}";
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return "the kill command could not be started here";
        }
    }
}
