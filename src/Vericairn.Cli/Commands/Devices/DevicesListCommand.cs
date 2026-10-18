// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DevicesListCommand : Command
{
    public DevicesListCommand(CommandContext context)
        : base("list", "List device records with their admission state and whether their key is held here.")
    {
        var operations = new DeviceOperations(context);
        SetAction(parseResult => operations.DevicesList(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
