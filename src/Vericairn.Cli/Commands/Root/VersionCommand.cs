// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Sdk;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class VersionCommand : Command
{
    public VersionCommand(CommandContext context, ReleaseUpdates updates)
        : base("version", "Print the version. " + UpdateNotices.Help)
    {
        SetAction(_ => UpdateNotices.Run(context, updates, () => Execute(context)));
    }

    private static int Execute(CommandContext context)
    {
        context.Output.WriteLine(CommandContext.VersionText());
        return Clean;
    }
}
