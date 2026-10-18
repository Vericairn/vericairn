// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DevicesCommand : Command
{
    public DevicesCommand(CommandContext context)
        : base("devices", "Device keys: create a device for a principal on this machine, pair one on another machine, list, admit, revoke, rename and rotate, and the operator's recovery device.")
    {
        Subcommands.Add(new DevicesCreateCommand(context));
        Subcommands.Add(new DevicesListCommand(context));
        Subcommands.Add(new DevicesAdmitCommand(context));
        Subcommands.Add(new DevicesRevokeCommand(context));
        Subcommands.Add(new DevicesRecoveryCommand(context));
        Subcommands.Add(new DevicesPairCommand(context));
        Subcommands.Add(new DevicesWritesCommand(context));
        Subcommands.Add(new DevicesRenameCommand(context));
        Subcommands.Add(new DevicesRotateCommand(context));
    }
}
