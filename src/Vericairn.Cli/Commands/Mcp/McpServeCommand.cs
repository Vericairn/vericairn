// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Mcp;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class McpServeCommand : Command
{
    public McpServeCommand(CommandContext context)
        : base("serve", "Serve the estate's tools over standard input and output until the input ends; a client of the daemon or the embedded runtime, never a second owner.")
    {
        var asOption = new Option<string>("--as") { Description = "Principal id the agent acts for; its admitted device on this machine signs the writes. Without it only the read-only tools are listed." };
        Options.Add(asOption);
        SetAction(parseResult => Execute(context, parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(asOption)));
    }

    /// <summary>Serves the estate over the Model Context Protocol on standard input and output; diagnostics go to standard error.</summary>
    private static int Execute(CommandContext context, string root, string? actingAs) => context.WithApi(root, api =>
    {
        var utf8 = new System.Text.UTF8Encoding(false);
        Console.InputEncoding = utf8;
        Console.OutputEncoding = utf8;
        var server = new McpServer(new ToolRegistry(api, actingAs), CommandContext.VersionText());
        context.Error.WriteLine($"vericairn mcp: serving {Path.GetFullPath(root)} ({(string.IsNullOrWhiteSpace(actingAs) ? "read-only" : "acting as " + actingAs)})");
        server.Run(Console.In, context.Output, context.Error);
        return Clean;
    });
}
