// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Mcp;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class McpServeCommand : Command
{
    public McpServeCommand(CommandContext context)
        : base("serve", "Serve the estate's tools over standard input and output until the input ends; a client of the daemon or the embedded runtime, never a second owner.")
    {
        Option<string> asOption = context.McpPrincipalOption;
        var run = new Option<string?>("--run") { Description = "The agent run this server was configured for by its worker: each search it serves is kept in that run's own machine-local directory, and the run's settlement records them. Set by the worker; never needed by hand." };
        Options.Add(asOption);
        Options.Add(run);
        SetAction(parseResult => Execute(context, parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(asOption), parseResult.GetValue(run)));
    }

    /// <summary>Serves the estate over the Model Context Protocol on standard input and output; diagnostics go to standard error.</summary>
    private static int Execute(CommandContext context, string root, string? actingAs, string? run) => context.WithApi(root, api =>
    {
        var utf8 = new System.Text.UTF8Encoding(false);
        Console.InputEncoding = utf8;
        Console.OutputEncoding = utf8;
        if (run is not null && !Records.Identity.Ulid.IsValid(run)) return context.Fail("--run names a run by its ULID.");
        string? estate = run is null ? null : api.Health().Estate;
        var registry = new ToolRegistry(api, actingAs)
        {
            SearchObserved = estate is null ? null : selection => Vericairn.Runtime.Search.ObservedSearches.Append(estate, run!, selection),
        };
        var server = new McpServer(registry, CommandContext.VersionText());
        context.Error.WriteLine($"vericairn mcp: serving {Path.GetFullPath(root)} ({(string.IsNullOrWhiteSpace(actingAs) ? "read-only" : "acting as " + actingAs)})");
        server.RunAsync(Console.In, context.Output, context.Error, context.CancellationToken).GetAwaiter().GetResult();
        return Clean;
    });
}
