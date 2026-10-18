// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Types;

namespace Vericairn.Cli;

internal static class RunConsole
{
    /// <summary>
    /// Waits in the foreground until a run settles, or no worker observes it any longer, and returns its row; a Ctrl+C asks for its stop
    /// rather than abandoning it. The run is observed by the runtime, never by this wait.
    /// </summary>
    public static RunRow? Follow(IEstateApi api, string run, string by)
    {
        int stopping = 0;
        void Interrupt(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            if (Interlocked.Exchange(ref stopping, 1) == 0)
            {
                ThreadPool.QueueUserWorkItem(_ => api.RunStop(run, new RunStopRequest(by, "interrupted at the command line")));
            }
        }

        Console.CancelKeyPress += Interrupt;
        try
        {
            RunRow? row = null;
            int unobserved = 0;
            while (unobserved < 5)
            {
                Thread.Sleep(1000);
                row = api.Runs().Runs.FirstOrDefault(r => r.Run == run);
                if (row is null || row.Status is "observed-success" or "known-failure" or "canceled")
                {
                    break;
                }

                unobserved = row.Observed ? 0 : unobserved + 1;
            }

            return row;
        }
        finally
        {
            Console.CancelKeyPress -= Interrupt;
        }
    }

    /// <summary>Prints a followed run as it ended, or as it was last observed.</summary>
    public static void PrintSettled(CommandContext context, RunRow? row)
    {
        if (row is null)
        {
            return;
        }

        PrintRun(context, row);
        if (row.Status is "dispatched" or "not-started" or "uncertain")
        {
            context.Output.WriteLine("  The run is no longer observed by a worker here; it stays in flight, charged at its cap, until a worker observes its end. A lost connection is not a stopped run.");
        }
    }

    public static void PrintRun(CommandContext context, RunRow row)
    {
        context.Output.WriteLine($"run {row.Run}  {row.Status}  {row.Launcher} as {row.Agent}  assignment {row.Assignment}  goal {row.Goal}");
        context.Output.WriteLine($"  workspace {row.Workspace}");
        context.Output.WriteLine($"  cap {Units(row.Cap)}");
        context.Output.WriteLine(row.Usage is JsonElement usage ? $"  usage {Units(usage)}" : "  usage: not settled yet, so charged at its cap");
        context.Output.WriteLine($"  charged {Units(row.Charged)}");
        foreach (RunSignal signal in row.Signals ?? [])
        {
            context.Output.WriteLine($"  worker {signal.Severity} {signal.Code}: {signal.Summary} ({(signal.Attention is null ? "attention recording pending" : "ask " + signal.Attention)})");
        }

        if (row.Stop is not null)
        {
            PrintStop(context, row.Stop);
        }

        if (row.Heartbeat is not null)
        {
            context.Output.WriteLine(row.Observed ? $"  observed by a worker on this machine, its last heartbeat at {row.Heartbeat}" : $"  not observed by a worker now; its last heartbeat here was at {row.Heartbeat}");
        }

        if (row.Generic)
        {
            context.Output.WriteLine("  " + Vericairn.Runtime.Workers.IAgentLauncher.GenericLabel);
        }
    }

    public static void PrintStop(CommandContext context, RunStop stop)
    {
        context.Output.WriteLine($"  stop requested at {stop.RequestedAt} by {stop.RequestedBy}{(stop.Reason is null ? string.Empty : ": " + stop.Reason)}");
        context.Output.WriteLine(stop.TransmittedAt is null ? "    transmitted: not observed" : $"    transmitted at {stop.TransmittedAt}: {stop.Transmission}");
        context.Output.WriteLine(stop.AcknowledgedAt is null ? "    acknowledged: not observed" : $"    acknowledged at {stop.AcknowledgedAt}: {stop.Acknowledgment}");
        if (stop.EscalatedAt is not null)
        {
            context.Output.WriteLine($"    escalated at {stop.EscalatedAt}: {stop.Escalation}");
        }

        context.Output.WriteLine(stop.ContainedAt is null ? "    contained: not observed" : $"    contained at {stop.ContainedAt}");
    }

    /// <summary>Caps, usage or charges in words: each unit and its amount, and the units not reported.</summary>
    public static string Units(JsonElement? caps)
    {
        if (caps is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return "none";
        }

        var parts = new List<string>();
        foreach (JsonProperty unit in element.EnumerateObject().OrderBy(p => Array.IndexOf(Caps.UnitNames, p.Name) is int at and >= 0 ? at : Caps.UnitNames.Length))
        {
            if (unit.Name == "unreported")
            {
                List<string> names = unit.Value.ValueKind == JsonValueKind.Array ? unit.Value.EnumerateArray().Select(v => v.GetString() ?? "?").ToList() : [];
                if (names.Count > 0)
                {
                    parts.Add($"not reported: {string.Join(", ", names)} (charged at the cap)");
                }

                continue;
            }

            parts.Add(unit.Value.ValueKind == JsonValueKind.Object
                ? $"{unit.Name} {(unit.Value.TryGetProperty("amount", out JsonElement amount) ? amount.GetRawText() : "?")} {(unit.Value.TryGetProperty("unit", out JsonElement code) ? code.GetString() : string.Empty)}"
                : $"{unit.Name} {unit.Value.GetRawText()}");
        }

        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }
}
