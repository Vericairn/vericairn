// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DevicesPairCommand : Command
{
    public DevicesPairCommand(CommandContext context)
        : base("pair", "Issue a pairing code for a new device on another machine: 16 characters, valid ten minutes, for one device and five attempts. Entered there with 'vericairn connections add', it records the device with the key it presents and admits it under your authority (needs the runtime serving the local-network profile).")
    {
        var operations = new DeviceOperations(context);
        var pairPrincipalOption = new Option<string>("--principal") { Description = "The principal the new device will act for.", Required = true };
        var pairByOption = new Option<string>("--by") { Description = "The administrator: the operator, or a principal holding the 'administer' scope, whose admitted device on this machine signs the admission.", Required = true };
        var pairProfilesOption = new Option<string[]>("--profile") { Description = "A connectivity profile the device will be permitted; repeatable (default: local-network).", AllowMultipleArgumentsPerToken = true };
        Options.Add(pairPrincipalOption);
        Options.Add(pairByOption);
        Options.Add(pairProfilesOption);
        SetAction(parseResult => operations.DevicesPair(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(pairPrincipalOption)!, parseResult.GetValue(pairByOption)!, parseResult.GetValue(pairProfilesOption) ?? [], parseResult.GetValue(context.JsonOption)));
    }
}
