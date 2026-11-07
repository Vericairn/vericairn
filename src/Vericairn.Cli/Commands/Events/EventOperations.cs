// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Sdk;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class EventOperations(CommandContext context)
{
    /// <summary>
    /// Follows the daemon's event stream over the local socket, where the operating-system user reads for the principal named (the
    /// operator by default). Only a running daemon keeps the journal; without one the watch is refused. An interruption ends it cleanly.
    /// </summary>
    public int Watch(string root, string? after, EventFilter filter, string? principal, int? count, bool json)
    {
        if (count is <= 0)
        {
            return context.Fail("--count is a positive number of events.");
        }

        if (!IsEstate(root))
        {
            return context.Fail(NotAnEstate(root));
        }

        DaemonEndpoint? endpoint = DaemonHost.Discover(root);
        using VericairnClient? client = endpoint is null ? null : new VericairnClient(endpoint);
        if (client is null || !context.IsAlive(endpoint!))
        {
            return context.Fail("No daemon is serving this estate, and following events needs one: the daemon keeps the event journal. Start it with 'vericairn runtime start'.");
        }

        try
        {
            return FollowAsync(client, after, filter, principal, count, json).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            return Clean;
        }
    }

    private async Task<int> FollowAsync(VericairnClient client, string? after, EventFilter filter, string? principal, int? count, bool json)
    {
        int delivered = 0;
        bool opened = false;
        await foreach (EventStreamItem item in client.WatchEventsAsync(after, filter, principal, context.CancellationToken).ConfigureAwait(false))
        {
            if (item.Event is EventEnvelope envelope)
            {
                context.Output.WriteLine(json ? JsonSerializer.Serialize(envelope, ProtocolJson.Options) : Line(envelope));
                context.Output.Flush();
                if (count is int limit && ++delivered >= limit)
                {
                    return Clean;
                }
            }
            else if (item.Reset)
            {
                context.Output.WriteLine(json ? JsonSerializer.Serialize(new { reset = true, cursor = item.Cursor }, ProtocolJson.Options)
                    : "reset: the cursor could not be honored; re-read the state you follow, then continue after " + item.Cursor);
                context.Output.Flush();
            }
            else if (json && !opened)
            {
                // Where the stream starts, so a script can resume there even if no event arrives before it stops.
                context.Output.WriteLine(JsonSerializer.Serialize(new { cursor = item.Cursor }, ProtocolJson.Options));
                context.Output.Flush();
            }

            opened = true;
        }

        return Clean;
    }

    private static string Line(EventEnvelope envelope) =>
        $"{envelope.At}  {envelope.Kind} {envelope.Change}  {envelope.Subject.Type} {envelope.Subject.Id}"
        + (envelope.To is null ? string.Empty : "  to " + envelope.To)
        + (envelope.Category is null ? string.Empty : "  " + envelope.Category);
}
