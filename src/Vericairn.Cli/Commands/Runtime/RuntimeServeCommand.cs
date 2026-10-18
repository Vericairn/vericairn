// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class RuntimeServeCommand : Command
{
    public RuntimeServeCommand(CommandContext context)
        : base("serve", "Serve the estate in this process until stopped (Ctrl+C or 'runtime stop'); refuses when a daemon already serves it.")
    {
        var operations = new DaemonLifecycle(context);
        var noLoopbackOption = new Option<bool>("--no-loopback") { Description = "Disable the pinned loopback TLS endpoint. The local socket and any configured local-network listener remain available." };
        var detachedOption = new Option<bool>("--detached") { Description = "Write the daemon's output to its log beside the estate's live store instead of the standard streams; 'runtime start' serves this way." };
        Options.Add(noLoopbackOption);
        Options.Add(detachedOption);
        SetAction(parseResult => operations.RuntimeServe(parseResult.GetValue(context.EstateOption)!, !parseResult.GetValue(noLoopbackOption), parseResult.GetValue(detachedOption)));
    }
}
