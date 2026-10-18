// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class ConnectionsAddCommand : Command
{
    public ConnectionsAddCommand(CommandContext context)
        : base("add", "Pair this machine with the runtime at an address using a code an administrator issued: the runtime proves it knows the code and who it is, this machine makes its device key and presents it, and the runtime records and admits the device. The connection pins the runtime's identity.")
    {
        var operations = new ConnectionOperations(context);
        var addressArgument = new Argument<string>("address") { Description = "Where the runtime listens: its address and port, as the pairing code's administrator gave them, for example 192.168.1.20:7443." };
        var codeOption = new Option<string>("--code") { Description = "The pairing code, as shown: four groups of four characters (16 in all).", Required = true };
        var nameOption = new Option<string>("--name") { Description = "This device's descriptive name, never a machine path.", Required = true };
        var custodyOption = new Option<string?>("--custody") { Description = "Key custody: default, the strongest this machine offers, or file, cng, secure-enclave or secret-service." };
        var presenceOption = new Option<bool?>("--presence") { Description = "Every signature needs its owner's presence (Secure Enclave custody only, never for an agent). Default: a person's device needs it wherever its custody enforces it; '--presence false' declines." };
        Arguments.Add(addressArgument);
        Options.Add(codeOption);
        Options.Add(nameOption);
        Options.Add(custodyOption);
        Options.Add(presenceOption);
        SetAction(parseResult => operations.ConnectionsAdd(parseResult.GetValue(addressArgument)!, parseResult.GetValue(codeOption)!, parseResult.GetValue(nameOption)!, parseResult.GetValue(custodyOption), parseResult.GetValue(presenceOption), parseResult.GetValue(context.JsonOption)));
    }
}
