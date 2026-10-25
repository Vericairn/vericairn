// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkerSettleCommand : Command
{
    public WorkerSettleCommand(CommandContext context)
        : base("settle", "Settle an exited run whose settlement could not be recorded, using the worker's retained observation on this machine. Missing facts remain unresolved.")
    {
        var operations = new WorkerOperations(context);
        var runArgument = new Argument<string>("run") { Description = "The run's id, any operation revision id, or its identity." };
        var byOption = new Option<string>("--by") { Description = "The principal asking; the same authority as stopping this run.", Required = true };
        Arguments.Add(runArgument);
        Options.Add(byOption);
        SetAction(parseResult => operations.WorkerSettle(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(runArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
