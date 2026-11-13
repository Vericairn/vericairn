// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Sdk;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class McpServeCommand : Command, ITextOutputCommand
{
    public string WhyNotJson => "its standard output carries the MCP protocol's messages, one JSON-RPC message per line.";

    public McpServeCommand(CommandContext context)
        : base("serve", "Serve the estate's tools over standard input and output until the input ends; a client of the daemon or the embedded runtime, never a second owner. With --connection, serve a remote estate's network MCP over a paired connection instead.")
    {
        Option<string> asOption = context.McpPrincipalOption;
        var run = new Option<string?>("--run") { Description = "The agent run this server was configured for by its worker: each search it serves is kept in that run's own machine-local directory, and the run's settlement records them. Set by the worker; never needed by hand." };

        var connectionOption = new Option<string?>("--connection")
        {
            Description = "From 0.9.2: bridge to the estate a paired connection reaches ('vericairn connections list'), by its id or its estate's name: this machine's device signs every request and, in two steps, every record it writes; the remote runtime judges each call as its API operation for that device. Not with --as.",
        };
        Options.Add(asOption);
        Options.Add(run);
        Options.Add(connectionOption);
        SetAction(parseResult => parseResult.GetValue(connectionOption) is string connection
            ? Bridge(context, connection, parseResult.GetValue(asOption))
            : Execute(context, parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(asOption), parseResult.GetValue(run)));
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

        // From 0.9.2 (MCP-05) the session subscribes to resources when its runtime keeps an event journal: the daemon does.
        var server = new McpServer(registry, CommandContext.VersionText())
        {
            Subscriptions = new McpResourceWatch(new McpApiEventSource(api, actingAs)),
        };
        context.Error.WriteLine($"vericairn mcp: serving {Path.GetFullPath(root)} ({(string.IsNullOrWhiteSpace(actingAs) ? "read-only" : "acting as " + actingAs)})");
        server.RunAsync(Console.In, context.Output, context.Error, context.CancellationToken).GetAwaiter().GetResult();
        return Clean;
    });

    /// <summary>
    /// Serves a remote estate's network MCP to a local client (MCP-03): each message is forwarded to its /mcp over the paired
    /// connection, signed by this machine's device, which also signs each record a write prepares.
    /// </summary>
    private static int Bridge(CommandContext context, string named, string? actingAs)
    {
        if (!string.IsNullOrWhiteSpace(actingAs))
        {
            return context.Fail("--as names a principal of an estate on this machine; over a connection the paired device's own principal acts. Give one or the other.");
        }

        try
        {
            var connections = new RuntimeConnections();
            List<RuntimeConnection> matching = connections.Find(named) is RuntimeConnection byId
                ? [byId]
                : [.. connections.List().Where(c => string.Equals(c.EstateName, named, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Estate, named, StringComparison.Ordinal))];
            if (matching.Count != 1)
            {
                return context.Fail(matching.Count == 0
                    ? $"No connection {named} on this machine; 'vericairn connections list' shows them, and 'vericairn connections add' pairs one."
                    : $"{matching.Count} connections reach {named}; name one by its id ('vericairn connections list').");
            }

            RuntimeConnection connection = matching[0];
            DeviceRequestSigner signer = connections.Signer(connection)
                ?? throw new InvalidOperationException($"This machine no longer holds the key of device {connection.Device}; pair again.");
            var utf8 = new System.Text.UTF8Encoding(false);
            Console.InputEncoding = utf8;
            Console.OutputEncoding = utf8;
            using HttpClient http = VericairnClient.SignedHttp(connection, signer);
            using var bridge = new McpBridge(http, connection.Principal, digest => signer.Sign(digest), context.Error);
            context.Error.WriteLine($"vericairn mcp: bridging to {connection.EstateName ?? connection.Estate} at {connection.Address} as device {connection.Device} of {connection.PrincipalName ?? connection.Principal}");
            bridge.RunAsync(Console.In, context.Output, context.CancellationToken).GetAwaiter().GetResult();
            return Clean;
        }
        catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or HttpRequestException) && exception is not IClientOperationFailure)
        {
            return context.Fail(exception.Message);
        }
    }
}
