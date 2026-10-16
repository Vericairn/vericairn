// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Workers;

/// <summary>How the worker paces and places the runs it supervises on this machine.</summary>
public sealed record WorkerOptions
{
    internal Action? AfterEvent { get; init; }

    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    internal Action? BeforeTransmission { get; init; }

    internal Action<bool>? BeforeSignal { get; init; }

    internal Action? BeforeDispatchRecord { get; init; }

    internal Action? BeforeAttentionRecord { get; init; }

    internal Func<uint>? WindowsJobInventory { get; init; }

    /// <summary>Named local launcher configurations, with explicit defaults and admitted inventories.</summary>
    public IReadOnlyDictionary<string, RunConfiguration> Configurations { get; init; } = new Dictionary<string, RunConfiguration>(StringComparer.Ordinal);

    /// <summary>The worker's defaults.</summary>
    public static WorkerOptions Default { get; } = new();

    /// <summary>How often the worker observes a running harness: its heartbeat, and how soon it sees a stop asked of it.</summary>
    public TimeSpan Heartbeat { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long a harness has to stop once a stop is transmitted before its process tree is killed, unless the stop names another.</summary>
    public TimeSpan Grace { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The longest a stop request waits for what the worker observes before it answers with what it has, whatever grace the stop
    /// names: a request holds nothing for a grace, and what is observed later the run's settlement records.
    /// </summary>
    public TimeSpan StopWait { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The length of one of a cap's wall minutes; a minute, and shorter only where a test paces a run.</summary>
    public TimeSpan Minute { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The environment a harness's is built from, through the allowlist; this process's own when null.</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    /// <summary>The vericairn command the agent's MCP server is started with; the one beside this runtime when null, else vericairn on the path.</summary>
    public IReadOnlyList<string>? Vericairn { get; init; }

    /// <summary>The vericairn command: the one configured, the executable beside this runtime, this process when it is vericairn, else vericairn on the path.</summary>
    public IReadOnlyList<string> VericairnCommand()
    {
        if (Vericairn is { Count: > 0 })
        {
            return Vericairn;
        }

        string name = OperatingSystem.IsWindows() ? "vericairn.exe" : "vericairn";
        string beside = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(beside))
        {
            return [beside];
        }

        string? process = System.Environment.ProcessPath;
        return process is not null && Path.GetFileNameWithoutExtension(process) == "vericairn" ? [process] : ["vericairn"];
    }
}
