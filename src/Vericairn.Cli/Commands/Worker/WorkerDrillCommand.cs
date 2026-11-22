// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

/// <summary>From 0.9.4 (OPS-21): the timed stop drill, which a recurring duty runs before the carrier runs work unattended and monthly after.</summary>
internal sealed class WorkerDrillCommand : Command
{
    public WorkerDrillCommand(CommandContext context)
        : base("drill", "From 0.9.4: the timed stop drill. The worker on the runtime's machine starts a scratch run that ignores the stop it is sent, asks it to stop as 'worker stop' does, and records the time from the stop to the observed containment and to the run's last write as a stop-drill verification of the estate. A run not contained within the limit fails the drill, which is attention.")
    {
        var by = new Option<string>("--by") { Description = "The principal running the drill, whose admitted device on this machine signs its verification.", Required = true };
        var grace = new Option<int?>("--grace") { Description = "Seconds the scratch run has to stop once the stop is transmitted before its process tree is killed, 0 to 3600; the worker's default, ten, when omitted." };
        var limit = new Option<int?>("--limit") { Description = "Seconds from the stop to containment that pass the drill, 1 to 3600; 60 when omitted." };
        Options.Add(by);
        Options.Add(grace);
        Options.Add(limit);
        SetAction(parse => context.WithApi(parse.GetValue(context.EstateOption)!, api =>
        {
            StopDrillResponse response = api.StopDrill(new StopDrillRequest(parse.GetValue(by)!, parse.GetValue(grace), parse.GetValue(limit)));
            if (parse.GetValue(context.JsonOption))
            {
                context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
                return response.Ok && response.Passed ? CommandContext.Clean : CommandContext.HasErrors;
            }

            if (response.Verification is { } verification) context.Output.WriteLine($"{verification.Path}  {verification.Id}  {verification.Hash}");
            if (response.RequestedAt is not null)
            {
                context.Output.WriteLine($"  stop requested at {response.RequestedAt}, grace {response.GraceSeconds} s, limit {response.LimitSeconds} s");
                context.Output.WriteLine(response.StopToContainedMilliseconds is long contained ? $"  contained {contained} ms after the stop" : "  containment not observed");
                context.Output.WriteLine(response.StopToLastWriteMilliseconds is long written ? $"  last write {written} ms after the stop, at {response.LastWriteAt}" : "  last write not read");
                context.Output.WriteLine("  drill " + (response.Passed ? "passed" : "failed"));
            }

            context.Output.WriteLine(response.Message);
            return response.Ok && response.Passed ? CommandContext.Clean : CommandContext.HasErrors;
        }));
    }
}
