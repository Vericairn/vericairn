// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class CheckObserveCommand : Command
{
    public CheckObserveCommand(CommandContext context)
        : base("observe", "Observe a workflow run at its host and record it as automation's verification of the commit it built.")
    {
        var operations = new VerificationOperations(context);
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var resourceOption = new Option<string>("--resource") { Description = "Resource record id of the repository checked.", Required = true };
        var repositoryOption = new Option<string?>("--repository") { Description = "Where the run is (for GitHub, owner/name); the resource's recorded location when omitted." };
        var providerOption = new Option<string?>("--provider") { Description = "The provider that observes the run; the one serving the resource's recorded location when omitted." };
        var runOption = new Option<string>("--run") { Description = "Workflow run id.", Required = true };
        var workflowOption = new Option<string>("--workflow") { Description = "Workflow file the run belongs to.", DefaultValueFactory = _ => "ci.yml" };
        Options.Add(resourceOption);
        Options.Add(repositoryOption);
        Options.Add(providerOption);
        Options.Add(runOption);
        Options.Add(workflowOption);
        Options.Add(byOption);
        SetAction(parseResult => operations.CheckObserve(
            parseResult.GetValue(context.EstateOption)!,
            parseResult.GetValue(resourceOption)!,
            parseResult.GetValue(repositoryOption),
            parseResult.GetValue(providerOption),
            parseResult.GetValue(runOption)!,
            parseResult.GetValue(workflowOption)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(context.JsonOption)));
    }
}
