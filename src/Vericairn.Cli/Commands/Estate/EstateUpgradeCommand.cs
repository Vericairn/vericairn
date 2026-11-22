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
        : base("upgrade", "Pin the estate to a published release and apply record-format migrations as one reviewable change with a migration record. From 0.9.4 it first says whether the previous release can still read the estate after it, refuses a one-way upgrade without --one-way, records a restore point (the estate's commit and this machine's local state) before it writes anything, and states how to return to the previous release. Then a daemon of an older release serving the estate on this machine is stopped, and the pinned release's started in its place when this command line is that release, or the commands to run are named; --check leaves the runtime alone.")
    {
        var operations = new EstateOperations(context);
        var toVersionOption = new Option<string>("--to") { Description = "Published version within the followed line and channel or more stable, using that release's command line.", Required = true };
        var lineOption = new Option<string?>("--line") { Description = "Explicitly change the followed release line; omitted, stay on the manifest's line." };
        var channelOption = new Option<string?>("--channel") { Description = "Explicitly change the followed channel: stable, lts, prerelease or nightly; omitted, preserve it." };
        var upgradeByOption = new Option<string?>("--by") { Description = "Principal id applying the upgrade (required unless --check)." };
        var checkOnlyOption = new Option<bool>("--check") { Description = "Show the plan without changing anything." };
        var oneWayOption = new Option<bool>("--one-way") { Description = "From 0.9.4: confirm a one-way upgrade, after which the previous release cannot read the estate; refused without it. The restore point is recorded either way." };
        var fromOption = new Option<string?>("--from") { Description = "Verify downloaded release responses, SHA256SUMS and platform archive in this directory on the runtime's machine, without network access." };
        Options.Add(toVersionOption);
        Options.Add(upgradeByOption);
        Options.Add(checkOnlyOption);
        Options.Add(fromOption);
        Options.Add(lineOption);
        Options.Add(channelOption);
        Options.Add(oneWayOption);
        SetAction(parseResult => operations.Upgrade(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(toVersionOption)!, parseResult.GetValue(upgradeByOption), parseResult.GetValue(checkOnlyOption), parseResult.GetValue(context.JsonOption), parseResult.GetValue(fromOption), versionText, parseResult.GetValue(lineOption), parseResult.GetValue(channelOption), parseResult.GetValue(oneWayOption)));
    }
}
