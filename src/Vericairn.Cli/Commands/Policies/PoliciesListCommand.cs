// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class PoliciesListCommand : Command
{
    public PoliciesListCommand(CommandContext context)
        : base("list", "List declarations, exceptions and findings at recorded time.")
    {
        var operations = new PolicyOperations(context);
        SetAction(p => operations.List(p.GetValue(context.EstateOption)!, p.GetValue(context.JsonOption)));
    }
}
