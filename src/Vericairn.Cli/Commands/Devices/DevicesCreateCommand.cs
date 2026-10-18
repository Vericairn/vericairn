// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DevicesCreateCommand : Command
{
    public DevicesCreateCommand(CommandContext context)
        : base("create", "Generate a key on this machine and record the device, signed by its own key.")
    {
        var operations = new DeviceOperations(context);
        var principalOption = new Option<string>("--principal") { Description = "The principal this device acts for.", Required = true };
        var nameOption = new Option<string>("--name") { Description = "Descriptive device name, never a machine path.", Required = true };
        var custodyOption = new Option<string?>("--custody") { Description = "Key custody: default, the strongest this machine offers (CNG on Windows; the Secure Enclave on a Mac that has one; the secret service on Linux where one runs; else an owner-only file), or file, cng, secure-enclave or secret-service." };
        var presenceOption = new Option<bool?>("--presence") { Description = "Every signature needs its owner's presence (Touch ID or the login password; Secure Enclave custody only, never for an agent). Default: a person's device needs it wherever its custody enforces it; '--presence false' declines." };
        var profilesOption = new Option<string[]>("--profile") { Description = "Permitted connectivity profile; repeatable (default: embedded).", AllowMultipleArgumentsPerToken = true };
        Options.Add(principalOption);
        Options.Add(nameOption);
        Options.Add(custodyOption);
        Options.Add(presenceOption);
        Options.Add(profilesOption);
        SetAction(parseResult => operations.DevicesCreate(
            parseResult.GetValue(context.EstateOption)!,
            parseResult.GetValue(principalOption)!,
            parseResult.GetValue(nameOption)!,
            parseResult.GetValue(custodyOption),
            parseResult.GetValue(presenceOption),
            parseResult.GetValue(profilesOption) ?? [],
            parseResult.GetValue(context.JsonOption)));
    }
}
