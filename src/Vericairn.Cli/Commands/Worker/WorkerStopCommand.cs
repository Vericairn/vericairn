// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkerStopCommand : Command
{
    public WorkerStopCommand(CommandContext context)
        : base("stop", "Ask a run to stop and report what was observed: the request, its transmission to the harness, the harness's acknowledgment, an escalation after the grace period and the containment, each only once observed. A stop that is not observed is reported as requested.")
    {
        var operations = new WorkerOperations(context);
        var runArgument = new Argument<string>("run") { Description = "The run: its id, the id of any of its operation's revisions, or its identity." };
        var stopByOption = new Option<string>("--by") { Description = "The principal asking for the stop.", Required = true };
        var reasonOption = new Option<string?>("--reason") { Description = "Why." };
        var graceOption = new Option<int?>("--grace") { Description = "Seconds the harness has to stop once the stop is transmitted before its process tree is killed; the worker's default, ten, when omitted." };
        Arguments.Add(runArgument);
        Options.Add(stopByOption);
        Options.Add(reasonOption);
        Options.Add(graceOption);
        SetAction(parseResult => operations.WorkerStop(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(runArgument)!, parseResult.GetValue(stopByOption)!, parseResult.GetValue(reasonOption), parseResult.GetValue(graceOption), parseResult.GetValue(context.JsonOption)));
    }
}
