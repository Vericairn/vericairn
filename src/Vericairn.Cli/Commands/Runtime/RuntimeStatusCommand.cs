// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class RuntimeStatusCommand : Command
{
    public RuntimeStatusCommand(CommandContext context)
        : base("status", "Whether a daemon serves the estate and where, or whether commands run the embedded runtime.")
    {
        var operations = new DaemonLifecycle(context);
        SetAction(parseResult => operations.RuntimeStatus(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
