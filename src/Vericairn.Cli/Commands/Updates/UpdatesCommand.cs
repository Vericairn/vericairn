// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class UpdatesCommand : Command
{
    public UpdatesCommand(CommandContext context)
        : base("updates", "Machine-local release check settings shared by the CLI and Workbench; nothing installs or upgrades automatically.")
    {
        Subcommands.Add(new UpdatesConfigureCommand(context));
    }
}
