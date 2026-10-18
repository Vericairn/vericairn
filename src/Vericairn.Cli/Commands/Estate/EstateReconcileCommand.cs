// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateReconcileCommand : Command
{
    public EstateReconcileCommand(CommandContext context)
        : base("reconcile", "List what two diverged copies left to settle: divergent revisions, duplicate keys, duplicate results.")
    {
        var operations = new EstateOperations(context);
        SetAction(parseResult => operations.Reconcile(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
