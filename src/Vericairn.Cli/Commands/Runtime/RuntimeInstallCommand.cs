// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class RuntimeInstallCommand : Command
{
    public RuntimeInstallCommand(CommandContext context)
        : base("install", "Install the daemon as a per-user service that starts at login (launchd agent, systemd user unit or a logon task).")
    {
        var operations = new DaemonLifecycle(context);
        var planOption = new Option<bool>("--plan") { Description = "Show the unit and the commands without installing anything." };
        Options.Add(planOption);
        SetAction(parseResult => operations.RuntimeInstall(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(planOption), parseResult.GetValue(context.JsonOption)));
    }
}
