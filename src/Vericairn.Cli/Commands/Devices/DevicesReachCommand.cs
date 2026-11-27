// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DevicesReachCommand : Command
{
    public DevicesReachCommand(CommandContext context)
        : base("reach", "List a device's available signatures with cutoff evidence, its presence session lists and observed revocations.")
    {
        var operations = new DeviceOperations(context);
        var device = new Argument<string>("device") { Description = "Device id, or one of its revision ids." };
        var since = new Option<string>("--since") { Description = "Inclusive timestamp, for example 2026-11-16T10:00:00Z.", Required = true };
        Arguments.Add(device);
        Options.Add(since);
        SetAction(result => operations.DevicesReach(result.GetValue(context.EstateOption)!, result.GetValue(device)!, result.GetValue(since)!, result.GetValue(context.JsonOption)));
    }
}
