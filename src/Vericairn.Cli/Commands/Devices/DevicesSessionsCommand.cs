// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DevicesSessionsCommand : Command
{
    public DevicesSessionsCommand(CommandContext context)
        : base("sessions", "From 0.9.4: list the presence sessions this machine's runtime opened or tried to open for the estate, from its owner-only ledger: each session's action, device, fingerprint, when and why it opened and ended, the records signed under it by id and hash, and each refusal. The ledger is a diagnostic in this user's machine state, never a record, an export or a support bundle; no daemon is needed.")
    {
        var operations = new DeviceOperations(context);
        var deviceArgument = new Argument<string?>("device") { Description = "A device's id, to list its sessions only.", Arity = ArgumentArity.ZeroOrOne };
        Arguments.Add(deviceArgument);
        SetAction(parseResult => operations.DevicesSessions(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(deviceArgument), parseResult.GetValue(context.JsonOption)));
    }
}
