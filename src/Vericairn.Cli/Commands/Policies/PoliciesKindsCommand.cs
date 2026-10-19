// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class PoliciesKindsCommand : Command
{
    public PoliciesKindsCommand(CommandContext context)
        : base("kinds", "List installed policy kind versions, sources and bounds.")
    {
        var operations = new PolicyOperations(context);
        SetAction(p => operations.Kinds(p.GetValue(context.EstateOption)!, p.GetValue(context.JsonOption)));
    }
}
