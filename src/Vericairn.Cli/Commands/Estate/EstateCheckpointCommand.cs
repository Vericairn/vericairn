// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateCheckpointCommand : Command
{
    public EstateCheckpointCommand(CommandContext context)
        : base("checkpoint", "Write a history checkpoint now (trigger manual), signed by your admitted device on this machine, in an estate that has adopted history checkpoints; witnessing grants nothing. With --adopt, adopt them: the estate's first checkpoint witnesses every record present and anchors the unsigned records that count today, and only the root operator or a person holding administer signs it, never an agent. --check shows what would be witnessed and anchored, writing nothing. The runtime also writes checkpoints by itself at upgrades, release submissions, charter revisions and once the unwitnessed tail is due.")
    {
        var operations = new EstateOperations(context);
        var byOption = new Option<string?>("--by") { Description = "Principal id whose admitted device on this machine signs (required unless --check); for --adopt, the root operator or a person holding administer." };
        var adoptOption = new Option<bool>("--adopt") { Description = "Adopt history checkpoints: once, after the upgrade to a release that writes them, as the root operator's step. Lists each unsigned record it would anchor with its type, date and adding commit, records signed by revoked devices and record files deleted in history." };
        var checkOption = new Option<bool>("--check") { Description = "Show what the checkpoint would witness and, with --adopt, anchor, signing and writing nothing." };
        Options.Add(byOption);
        Options.Add(adoptOption);
        Options.Add(checkOption);
        SetAction(parseResult => operations.Checkpoint(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(byOption), parseResult.GetValue(adoptOption),
            parseResult.GetValue(checkOption), parseResult.GetValue(context.JsonOption)));
    }
}
