// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Runtime.Portability;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class RecoverInspectCommand : Command
{
    public RecoverInspectCommand(CommandContext context)
        : base("inspect", "What this copy knows, which devices can author from here, work in progress, unresolved effects and reconcile tasks.")
    {
        SetAction(parseResult => Execute(context, parseResult.GetValue(context.EstateOption)!));
    }

    private static int Execute(CommandContext context, string root) => context.WithApi(root, api =>
    {
        context.Output.Write(api.RecoverInspect());
        return Clean;
    });
}
