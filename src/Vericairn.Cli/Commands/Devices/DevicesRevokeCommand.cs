// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DevicesRevokeCommand : Command
{
    public DevicesRevokeCommand(CommandContext context)
        : base("revoke", "Revoke a device's admission from now on; its earlier records stay attributable.")
    {
        var operations = new DeviceOperations(context);
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var deviceArgument = new Argument<string>("device") { Description = "Device record id." };
        var recoveryOption = new Option<string?>("--recovery") { Description = "Sign with the operator's recovery key in this file, when no admitted device of the operator is left; asks for its passphrase. Every use stays in 'status' until the operator reviews it." };
        var passphraseStdinOption = new Option<bool>("--passphrase-stdin") { Description = "Read the recovery key's passphrase from the first line of standard input instead of the terminal." };
        var reasonOption = new Option<string>("--reason") { Description = "Why the admission ends.", Required = true };
        Arguments.Add(deviceArgument);
        Options.Add(byOption);
        Options.Add(reasonOption);
        Options.Add(recoveryOption);
        Options.Add(passphraseStdinOption);
        SetAction(parseResult => operations.DevicesRevoke(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(deviceArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(reasonOption)!, parseResult.GetValue(recoveryOption), parseResult.GetValue(passphraseStdinOption), parseResult.GetValue(context.JsonOption)));
    }
}
