// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DevicesRenameCommand : Command
{
    public DevicesRenameCommand(CommandContext context)
        : base("rename", "Rename a device whose key is held here; it keeps its identity, key and admission (record format 2).")
    {
        var operations = new DeviceOperations(context);
        var deviceArgument = new Argument<string>("device") { Description = "Device record id." };
        var newNameOption = new Option<string>("--name") { Description = "The device's new descriptive name, never a machine path.", Required = true };
        Arguments.Add(deviceArgument);
        Options.Add(newNameOption);
        SetAction(parseResult => operations.DevicesRename(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(deviceArgument)!, parseResult.GetValue(newNameOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
