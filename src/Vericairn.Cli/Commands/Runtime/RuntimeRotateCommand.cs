// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class RuntimeRotateCommand : Command
{
    public RuntimeRotateCommand(CommandContext context)
        : base("rotate", "Replace the runtime certificate now (it is also replaced on a schedule before it expires). Paired devices keep working, since they pin the runtime identity, which signs the certificate in use; loopback clients read the new one from the endpoint file.")
    {
        var operations = new RuntimeNetworkConfiguration(context);
        var compromisedOption = new Option<bool>("--compromised") { Description = "The certificate's key may be known to someone else: also close every open connection, so nothing continues under it." };
        var identityOption = new Option<bool>("--identity") { Description = "The runtime identity's key may be known to someone else: replace the identity too; every paired device then pairs again." };
        var reasonOption = new Option<string?>("--reason") { Description = "Why, for the runtime's log." };
        Options.Add(compromisedOption);
        Options.Add(identityOption);
        Options.Add(reasonOption);
        SetAction(parseResult => operations.RuntimeRotate(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(compromisedOption), parseResult.GetValue(identityOption), parseResult.GetValue(reasonOption), parseResult.GetValue(context.JsonOption)));
    }
}
