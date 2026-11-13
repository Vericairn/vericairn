// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Sdk;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class VersionCommand : Command
{
    public VersionCommand(CommandContext context, ReleaseUpdates updates)
        : base("version", "Print the version; with --json, one object whose version field holds it. " + UpdateNotices.Help)
    {
        SetAction(parseResult => UpdateNotices.Run(context, updates, () => Execute(context, parseResult.GetValue(context.JsonOption)), parseResult.GetValue(context.EstateOption)!));
    }

    private static int Execute(CommandContext context, bool json)
    {
        context.Output.WriteLine(json ? JsonSerializer.Serialize(new { version = CommandContext.VersionText() }, ProtocolJson.Options) : CommandContext.VersionText());
        return Clean;
    }
}
