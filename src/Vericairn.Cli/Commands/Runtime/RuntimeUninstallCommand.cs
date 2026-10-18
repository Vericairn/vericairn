// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class RuntimeUninstallCommand : Command
{
    public RuntimeUninstallCommand(CommandContext context)
        : base("uninstall", "Remove the per-user service installed by 'runtime install'.")
    {
        var operations = new DaemonLifecycle(context);
        SetAction(parseResult => operations.RuntimeUninstall(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
