// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DevicesWritesCommand : Command
{
    public DevicesWritesCommand(CommandContext context)
        : base("writes", "List what paired devices wrote over the local network through this machine's runtime: when, which device, for which principal, the operation and the record it produced. A record a paired device asks for is signed by a device of the same principal held here, so this is where the paired device behind it is kept.")
    {
        var operations = new DeviceOperations(context);
        var writesDeviceArgument = new Argument<string?>("device") { Description = "A paired device's id, to list its writes only.", Arity = ArgumentArity.ZeroOrOne };
        Arguments.Add(writesDeviceArgument);
        SetAction(parseResult => operations.DevicesWrites(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(writesDeviceArgument), parseResult.GetValue(context.JsonOption)));
    }
}
