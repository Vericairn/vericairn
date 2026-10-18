// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class RuntimeStartCommand : Command
{
    public RuntimeStartCommand(CommandContext context)
        : base("start", "Start a daemon for the estate in the background and wait until it answers.")
    {
        var operations = new DaemonLifecycle(context);
        SetAction(parseResult => operations.RuntimeStart(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
