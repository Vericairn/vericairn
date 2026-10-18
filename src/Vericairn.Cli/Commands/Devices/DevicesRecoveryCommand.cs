// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DevicesRecoveryCommand : Command
{
    public DevicesRecoveryCommand(CommandContext context)
        : base("recovery", "Make the operator's recovery device for emergency access: its key is written once to a file, encrypted with a passphrase you type, and never held by this machine; an admitted device of the operator held here admits it (record format 2).")
    {
        var operations = new DeviceOperations(context);
        var passphraseStdinOption = new Option<bool>("--passphrase-stdin") { Description = "Read the recovery key's passphrase from the first line of standard input instead of the terminal." };
        var recoveryPrincipalOption = new Option<string>("--principal") { Description = "The operator the recovery device acts for.", Required = true };
        var recoveryNameOption = new Option<string>("--name") { Description = "Descriptive name, for example where the key is kept; never a machine path.", Required = true };
        var outputOption = new Option<string>("--output") { Description = "New file to write the encrypted key to, outside the estate's repository; move it off this machine at once.", Required = true };
        Options.Add(recoveryPrincipalOption);
        Options.Add(recoveryNameOption);
        Options.Add(outputOption);
        Options.Add(passphraseStdinOption);
        SetAction(parseResult => operations.DevicesRecovery(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(recoveryPrincipalOption)!, parseResult.GetValue(recoveryNameOption)!, parseResult.GetValue(outputOption)!, parseResult.GetValue(passphraseStdinOption), parseResult.GetValue(context.JsonOption)));
    }
}
