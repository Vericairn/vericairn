// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Workers;

/// <summary>A stop asked of a run on this machine: when, by whom, why, and how long its harness has before the stop is escalated.</summary>
/// <param name="RequestedAt">When it was asked, RFC 3339 UTC seconds.</param>
/// <param name="RequestedBy">The principal that asked, or worker for a cap the run reached.</param>
/// <param name="Reason">Why, when given.</param>
/// <param name="GraceSeconds">How long the harness has to stop after the stop is transmitted before its process tree is killed; the worker's default when null.</param>
public sealed record StopRequest(string RequestedAt, string RequestedBy, string? Reason = null, int? GraceSeconds = null);

/// <summary>What the worker supervising a run on this machine has observed, written as it observes it.</summary>
public sealed class RunObservedState
{
    /// <summary>The run's id.</summary>
    public string Run { get; set; } = string.Empty;

    /// <summary>The process id of the worker supervising it.</summary>
    public int Supervisor { get; set; }

    /// <summary>How often the worker observes the harness, in seconds.</summary>
    public double HeartbeatSeconds { get; set; }

    /// <summary>The harness's process id, once it started.</summary>
    public int? ProcessId { get; set; }

    /// <summary>When the harness started.</summary>
    public string? LaunchedAt { get; set; }

    /// <summary>When the worker last observed the harness running.</summary>
    public string? Heartbeat { get; set; }

    /// <summary>How many times the worker observed it running.</summary>
    public long Heartbeats { get; set; }

    /// <summary>When the harness last wrote to its output.</summary>
    public string? Output { get; set; }

    /// <summary>The worker's event-stream observation.</summary>
    public RunLiveness? Liveness { get; set; }

    /// <summary>The worker's bounded command, loop and spending observations; never raw invocation inputs.</summary>
    public List<RunSignal> Signals { get; set; } = [];

    /// <summary>Reported execution facts and unknowns, kept without raw event values.</summary>
    public JsonElement? Execution { get; set; }

    /// <summary>The tokens the harness reported so far, when it reported any.</summary>
    public long? Tokens { get; set; }

    /// <summary>The harness's exit code, once its exit was observed.</summary>
    public int? ExitCode { get; set; }

    /// <summary>When its exit was observed.</summary>
    public string? ExitedAt { get; set; }

    /// <summary>The stop's facts as observed so far.</summary>
    public RunStop? Stop { get; set; }

    /// <summary>The status the settlement recorded, once it was recorded.</summary>
    public string? Settled { get; set; }

    /// <summary>A failed containment escalation, retained even after a successful retry.</summary>
    public string? EscalationFailure { get; set; }

    /// <summary>Why the settlement could not be recorded, when it could not.</summary>
    public string? Problem { get; set; }
}

/// <summary>
/// The per-machine state of one run, beside the estate's per-machine state under the Vericairn home and never in a record or a
/// repository: what the supervising worker observed, the stop asked of it, the stop file the harness is told of, the harness's
/// output and the MCP configuration the harness was given.
/// </summary>
public sealed class RunDirectory
{
    /// <summary>The directory of a run of an estate on this machine.</summary>
    public RunDirectory(string estate, string run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(estate);
        ArgumentException.ThrowIfNullOrWhiteSpace(run);
        if (!Records.Identity.Ulid.IsValid(run))
        {
            throw new ArgumentException("A run is named by a ULID.", nameof(run));
        }

        Path = System.IO.Path.Combine(VericairnHome.Path(), "estates", estate, "runs", run);
    }

    /// <summary>The directory.</summary>
    public string Path { get; }

    /// <summary>What the supervising worker observed.</summary>
    public string StateFile => System.IO.Path.Combine(Path, "state.json");

    /// <summary>The stop asked of the run.</summary>
    public string StopRequestFile => System.IO.Path.Combine(Path, "stop-request.json");

    /// <summary>The file the worker creates to transmit a stop, which the harness is told of in VERICAIRN_STOP_FILE.</summary>
    public string StopFile => System.IO.Path.Combine(Path, "stop");

    /// <summary>The harness's output, as it wrote it.</summary>
    public string OutputLog => System.IO.Path.Combine(Path, "output.log");

    /// <summary>The MCP configuration the harness was given.</summary>
    public string McpConfiguration => System.IO.Path.Combine(Path, "mcp.json");

    /// <summary>True when this machine holds the run's directory.</summary>
    public bool Exists => Directory.Exists(Path);

    /// <summary>Creates the directory, readable by its owner only on Linux and macOS.</summary>
    public void Create()
    {
        Directory.CreateDirectory(Path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>What the supervising worker observed, or null when nothing was written or it cannot be read.</summary>
    public RunObservedState? Read() => ReadJson<RunObservedState>(StateFile);

    /// <summary>Writes what the supervising worker observed, whole, so that a reader never sees half of it.</summary>
    public void Write(RunObservedState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        WriteWhole(StateFile, state);
    }

    /// <summary>Publishes the stop file whole, replacing an existing entry without opening it for writing.</summary>
    internal void TransmitStop(StopRequest request) => WriteWhole(StopFile, request);

    /// <summary>The stop asked of the run, or null.</summary>
    public StopRequest? ReadStop() => ReadJson<StopRequest>(StopRequestFile);

    /// <summary>Asks the run to stop; the first request stands and a later one changes nothing. Returns the request that stands.</summary>
    public StopRequest RequestStop(StopRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Create();
        try
        {
            using FileStream stream = new(StopRequestFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, request, ProtocolJson.Options);
            return request;
        }
        catch (IOException) when (File.Exists(StopRequestFile))
        {
            return ReadStop() ?? request;
        }
    }

    private static void WriteWhole<T>(string target, T value)
    {
        // CREATE_ALWAYS refuses an existing hidden/system file on Windows. Exclusive staging plus rename also avoids
        // writing through an existing link, and a unique staging name leaves a stranded earlier write untouched.
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, ProtocolJson.Options);
            }

            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static T? ReadJson<T>(string file)
        where T : class
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file), ProtocolJson.Options) : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
