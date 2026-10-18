// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class ChangesObserveCommand : Command
{
    public ChangesObserveCommand(CommandContext context)
        : base("observe", "Record an observation of the original change; uncertain effects remain uncertain until confirmed.")
    {
        var operationOption = new Option<string>("--operation") { Description = "Recorded push, opening or merge operation id to observe under its original pins.", Required = true };
        var byOption = new Option<string>("--by") { Description = "Principal id with repository-write authority in force.", Required = true };
        var cancelOption = new Option<string>("--cancel") { Description = "Operator's explicit reconciliation that the original intent had no effect; cancels only the recorded intent." };
        Options.Add(operationOption);
        Options.Add(byOption);
        Options.Add(cancelOption);
        SetAction(parseResult => context.WithApi(parseResult.GetValue(context.EstateOption)!, api =>
            ChangesOutput.Write(context, api.ChangesObserve(new ChangesObserveRequest(
                parseResult.GetValue(operationOption)!, parseResult.GetValue(byOption)!, parseResult.GetValue(cancelOption))),
                parseResult.GetValue(context.JsonOption))));
    }
}
