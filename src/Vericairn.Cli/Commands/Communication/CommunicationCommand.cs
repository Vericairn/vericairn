// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class CommunicationCommand : Command
{
    public CommunicationCommand(CommandContext context)
        : base("communication", "Inspect or change attributable personal communication settings. Remote is not built.")
    {
        Subcommands.Add(new CommunicationShowCommand(context));
        Subcommands.Add(new CommunicationSetCommand(context));
        Subcommands.Add(new CommunicationResetCommand(context));
        Subcommands.Add(new CommunicationDelegateCommand(context));
    }
}
