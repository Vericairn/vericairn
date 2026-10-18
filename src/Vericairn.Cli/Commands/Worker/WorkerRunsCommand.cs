// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkerRunsCommand : Command
{
    public WorkerRunsCommand(CommandContext context)
        : base("runs", "List the agent runs with their caps, their observed usage, what each is charged (its cap for what was not reported, and while in flight) and their stops, and the ledgers of their goals.")
    {
        var operations = new WorkerOperations(context);
        SetAction(parseResult => operations.WorkerRuns(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
