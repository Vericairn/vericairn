// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class ChangesOpenCommand : Command
{
    public ChangesOpenCommand(CommandContext context)
        : base("open", "Open a pull request from an exact head with a recorded successful scanned push.")
    {
        var productOption = new Option<string>("--product") { Description = "Product record id declaring the repository, provider, target branch and required checks.", Required = true };
        var branchOption = new Option<string>("--branch") { Description = "The branch to push or open, in the declared repository.", Required = true };
        var headOption = new Option<string>("--head") { Description = "Exact commit expected at the branch or pull request head.", Required = true };
        var titleOption = new Option<string>("--title") { Description = "Title of the pull request.", Required = true };
        var bodyOption = new Option<string>("--body") { Description = "Body of the pull request.", Required = true };
        var byOption = new Option<string>("--by") { Description = "Principal id with repository-write authority in force.", Required = true };
        var reconciliationOption = new Option<string?>("--reconciliation") { Description = "How the previous known failure under this identity was settled; required for a retry." };
        Options.Add(productOption);
        Options.Add(branchOption);
        Options.Add(headOption);
        Options.Add(titleOption);
        Options.Add(bodyOption);
        Options.Add(byOption);
        Options.Add(reconciliationOption);
        SetAction(parseResult => context.WithApi(parseResult.GetValue(context.EstateOption)!, api =>
            ChangesOutput.Write(context, api.ChangesOpen(new ChangesOpenRequest(
                parseResult.GetValue(productOption)!, parseResult.GetValue(branchOption)!, parseResult.GetValue(headOption)!, parseResult.GetValue(titleOption)!, parseResult.GetValue(bodyOption)!, parseResult.GetValue(byOption)!, parseResult.GetValue(reconciliationOption))),
                parseResult.GetValue(context.JsonOption))));
    }
}
