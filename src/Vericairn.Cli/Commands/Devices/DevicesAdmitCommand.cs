// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DevicesAdmitCommand : Command
{
    public DevicesAdmitCommand(CommandContext context)
        : base("admit", "Admit a device to author records in this estate (operator or 'administer' scope).")
    {
        var operations = new DeviceOperations(context);
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var deviceArgument = new Argument<string>("device") { Description = "Device record id." };
        var noteOption = new Option<string>("--note") { Description = "Why the device is admitted.", Required = true };
        var recoveryOption = new Option<string?>("--recovery") { Description = "Sign with the operator's recovery key in this file, when no admitted device of the operator is left; asks for its passphrase. Every use stays in 'status' until the operator reviews it." };
        var passphraseStdinOption = new Option<bool>("--passphrase-stdin") { Description = "Read the recovery key's passphrase from the first line of standard input instead of the terminal." };
        Arguments.Add(deviceArgument);
        Options.Add(byOption);
        Options.Add(noteOption);
        Options.Add(recoveryOption);
        Options.Add(passphraseStdinOption);
        SetAction(parseResult => operations.DevicesAdmit(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(deviceArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(noteOption)!, parseResult.GetValue(recoveryOption), parseResult.GetValue(passphraseStdinOption), parseResult.GetValue(context.JsonOption)));
    }
}
