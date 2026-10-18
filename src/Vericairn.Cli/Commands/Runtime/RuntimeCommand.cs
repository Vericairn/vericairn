// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class RuntimeCommand : Command
{
    public RuntimeCommand(CommandContext context)
        : base("runtime", "The daemon that owns an estate's live state on this machine: serve, start, stop, status, install, uninstall, configure (the local-network profile) and rotate (its certificate).")
    {
        Subcommands.Add(new RuntimeServeCommand(context));
        Subcommands.Add(new RuntimeStartCommand(context));
        Subcommands.Add(new RuntimeStopCommand(context));
        Subcommands.Add(new RuntimeStatusCommand(context));
        Subcommands.Add(new RuntimeInstallCommand(context));
        Subcommands.Add(new RuntimeUninstallCommand(context));
        Subcommands.Add(new RuntimeConfigureCommand(context));
        Subcommands.Add(new RuntimeRotateCommand(context));
    }
}
