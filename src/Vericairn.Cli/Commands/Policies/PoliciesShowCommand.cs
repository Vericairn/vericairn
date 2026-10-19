// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class PoliciesShowCommand : Command
{
    public PoliciesShowCommand(CommandContext context)
        : base("show", "Show a policy by key or record id with exact evidence.")
    {
        var key = new Argument<string>("key");
        Arguments.Add(key);
        var operations = new PolicyOperations(context);
        SetAction(p => operations.Show(p.GetValue(context.EstateOption)!, p.GetValue(key)!, p.GetValue(context.JsonOption)));
    }
}
