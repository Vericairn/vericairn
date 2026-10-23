// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateUpgradeCommand : Command
{
    public EstateUpgradeCommand(CommandContext context)
        : this(context, CommandContext.VersionText())
    {
    }

    internal EstateUpgradeCommand(CommandContext context, string versionText)
        : base("upgrade", "Pin the estate to a published release and apply record-format migrations as one reviewable change with a migration record.")
    {
        var operations = new EstateOperations(context);
        var toVersionOption = new Option<string>("--to") { Description = "Published Vericairn version to verify and pin, using that release's command line.", Required = true };
        var upgradeByOption = new Option<string?>("--by") { Description = "Principal id applying the upgrade (required unless --check)." };
        var checkOnlyOption = new Option<bool>("--check") { Description = "Show the plan without changing anything." };
        var fromOption = new Option<string?>("--from") { Description = "Verify downloaded release responses, SHA256SUMS and platform archive in this directory on the runtime's machine, without network access." };
        Options.Add(toVersionOption);
        Options.Add(upgradeByOption);
        Options.Add(checkOnlyOption);
        Options.Add(fromOption);
        SetAction(parseResult => operations.Upgrade(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(toVersionOption)!, parseResult.GetValue(upgradeByOption), parseResult.GetValue(checkOnlyOption), parseResult.GetValue(context.JsonOption), parseResult.GetValue(fromOption), versionText));
    }
}
