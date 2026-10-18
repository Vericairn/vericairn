// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkerLaunchersCommand : Command
{
    public WorkerLaunchersCommand(CommandContext context)
        : base("launchers", "List the coding-agent launchers available to the runtime, providers of vericairn.launcher.agent/1, with their settings and limitations.")
    {
        var operations = new WorkerOperations(context);
        SetAction(parseResult => operations.WorkerLaunchers(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
