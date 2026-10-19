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

    /// <summary>Why settlement or subsequent local branch retention failed, when it did.</summary>
    public string? Problem { get; set; }
}

/// <summary>
/// The per-machine state of one run, beside the estate's per-machine state under the Vericairn home and never in a record or a
/// repository: what the supervising worker observed, the stop asked of it, the stop file the harness is told of, the harness's
/// output and the MCP configuration the harness was given.
/// </summary>
public sealed class RunDirectory
{
    private static readonly TimeSpan FileRetryLimit = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FileRetryDelay = TimeSpan.FromMilliseconds(10);

    internal Action? BeforeStopRequestSerialization { get; init; }

    internal Action? AfterReadOpen { get; init; }

    internal Action? BeforeReadOpen { get; init; }

    internal Action? BeforeMove { get; init; }

    internal TimeProvider RetryClock { get; init; } = TimeProvider.System;

    internal Action<TimeSpan> RetryWait { get; init; } = Thread.Sleep;

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

    /// <summary>The exact dispatched machine brief, containing its context package and originals.</summary>
    public string ContextFile => System.IO.Path.Combine(Path, "context.json");

    /// <summary>The exact human rendering exported to the workspace for this run.</summary>
    public string BriefFile => System.IO.Path.Combine(Path, "brief.md");

    /// <summary>Retains dispatch context in this run's local custody, never in estate objects or records.</summary>
    internal void RetainContext(JsonElement context, string brief)
    {
        File.WriteAllBytes(ContextFile, Records.Canonical.CanonicalJson.SerializePrettyUtf8(context));
        File.WriteAllText(BriefFile, brief);
    }

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

    /// <summary>What the supervising worker observed, or null when absent. An unreadable snapshot throws after bounded retries of sharing or access failures.</summary>
    public RunObservedState? Read() => ReadJson<RunObservedState>(StateFile);

    /// <summary>Writes what the supervising worker observed, whole, so that a reader never sees half of it.</summary>
    public void Write(RunObservedState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        WriteWhole(StateFile, state);
    }

    /// <summary>Publishes the stop file whole, replacing an existing entry without opening it for writing.</summary>
    internal void TransmitStop(StopRequest request) => WriteWhole(StopFile, request);

    /// <summary>The stop asked of the run, or null when absent. An unreadable request throws after bounded retries of sharing or access failures.</summary>
    public StopRequest? ReadStop() => ReadJson<StopRequest>(StopRequestFile);

    /// <summary>Asks the run to stop; the first completely published request stands and a later one changes nothing. Returns the request that stands.</summary>
    public StopRequest RequestStop(StopRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Create();
        try
        {
            WriteWhole(StopRequestFile, request, overwrite: false, BeforeStopRequestSerialization);
            return request;
        }
        catch (IOException) when (File.Exists(StopRequestFile))
        {
            return ReadStop() ?? throw new IOException("The existing stop request could not be read.");
        }
    }

    private void WriteWhole<T>(string target, T value, bool overwrite = true, Action? beforeSerialization = null)
    {
        // CREATE_ALWAYS refuses an existing hidden/system file on Windows. Exclusive staging plus rename also avoids
        // writing through an existing link, and a unique staging name leaves a stranded earlier write untouched.
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                beforeSerialization?.Invoke();
                JsonSerializer.Serialize(stream, value, ProtocolJson.Options);
            }

            // A stop request becomes visible only when complete. A non-overwriting move elects one
            // winner across processes; competing callers read that winner, never their own unkept request.
            WithFileRetries(() =>
            {
                BeforeMove?.Invoke();
                File.Move(temporary, target, overwrite);
                return true;
            });
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private T? ReadJson<T>(string file) where T : class => WithFileRetries<T?>(() =>
    {
        FileStream stream;
        try
        {
            // Delete sharing permits overlapping access, but Windows MoveFileEx can still refuse
            // replacement until this handle closes. The writer retries that contention separately.
            BeforeReadOpen?.Invoke();
            stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        using (stream)
        {
            AfterReadOpen?.Invoke();
            return JsonSerializer.Deserialize<T>(stream, ProtocolJson.Options)
                ?? throw new JsonException("A run snapshot must contain an object.");
        }
    });

    private T WithFileRetries<T>(Func<T> operation)
    {
        long started = RetryClock.GetTimestamp();
        while (true)
        {
            try
            {
                return operation();
            }
            catch (Exception exception) when (IsSharingOrAccessFailure(exception))
            {
                TimeSpan remaining = FileRetryLimit - RetryClock.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero) throw;
                RetryWait(remaining < FileRetryDelay ? remaining : FileRetryDelay);
            }
        }
    }

    // Access denied can be temporary on Windows as well as a permanent permissions error. Retry it
    // briefly, then preserve the error. Missing files, bad JSON and other storage faults are distinct.
    private static bool IsSharingOrAccessFailure(Exception exception) => exception is UnauthorizedAccessException
        || (exception is IOException && (exception.HResult & 0xffff) is 5 or 32 or 33);
}
