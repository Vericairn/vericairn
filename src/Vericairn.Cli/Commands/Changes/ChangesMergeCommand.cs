// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class ChangesMergeCommand : Command
{
    public ChangesMergeCommand(CommandContext context)
        : base("merge", "Observe every declared check at the exact head, merge pinned to that head, then observe the target run.")
    {
        var productOption = new Option<string>("--product") { Description = "Product record id declaring the repository, provider, target branch and required checks.", Required = true };
        var pullrequestOption = new Option<string>("--pull-request") { Description = "Pull request number at the declared destination.", Required = true };
        var headOption = new Option<string>("--head") { Description = "Exact commit expected at the branch or pull request head.", Required = true };
        var byOption = new Option<string>("--by") { Description = "Principal id with repository-write authority in force.", Required = true };
        var reconciliationOption = new Option<string?>("--reconciliation") { Description = "How the previous known failure under this identity was settled; required for a retry." };
        Options.Add(productOption);
        Options.Add(pullrequestOption);
        Options.Add(headOption);
        Options.Add(byOption);
        Options.Add(reconciliationOption);
        SetAction(parseResult => context.WithApi(parseResult.GetValue(context.EstateOption)!, api =>
            ChangesOutput.Write(context, api.ChangesMerge(new ChangesMergeRequest(
                parseResult.GetValue(productOption)!, parseResult.GetValue(pullrequestOption)!, parseResult.GetValue(headOption)!, parseResult.GetValue(byOption)!, parseResult.GetValue(reconciliationOption))),
                parseResult.GetValue(context.JsonOption))));
    }
}
