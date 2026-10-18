// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DevicesRotateCommand : Command
{
    public DevicesRotateCommand(CommandContext context)
        : base("rotate", "Replace a device's key with a new one, signed by both keys; the device keeps its identity and admission, and its earlier records stay valid (record format 2).")
    {
        var operations = new DeviceOperations(context);
        var deviceArgument = new Argument<string>("device") { Description = "Device record id." };
        var rotateCustodyOption = new Option<string?>("--custody") { Description = "Custody of the new key: file, cng, secure-enclave, secret-service, or default for the strongest this machine offers (when omitted: the custody the device has)." };
        var rotatePresenceOption = new Option<bool?>("--presence") { Description = "Every signature with the new key needs its owner's presence (Secure Enclave custody only, never for an agent); '--presence false' removes it. Default: the device's presence, where the new custody enforces it." };
        Arguments.Add(deviceArgument);
        Options.Add(rotateCustodyOption);
        Options.Add(rotatePresenceOption);
        SetAction(parseResult => operations.DevicesRotate(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(deviceArgument)!, parseResult.GetValue(rotateCustodyOption), parseResult.GetValue(rotatePresenceOption), parseResult.GetValue(context.JsonOption)));
    }
}
