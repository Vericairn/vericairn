// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class RuntimeStopCommand : Command
{
    public RuntimeStopCommand(CommandContext context)
        : base("stop", "Ask the daemon serving the estate to stop; closing a client never stops it.")
    {
        var operations = new DaemonLifecycle(context);
        SetAction(parseResult => operations.RuntimeStop(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
