// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Sockets;

namespace Vericairn.Daemon.Tests;

public sealed partial class NetworkMcpTokenTests
{
    [Fact]
    public async Task PublicConnectionLimitClosesExcessAndIncompleteHandshakes()
    {
        await using PublicHarness fixture = await PublicHarness.StartAsync();
        int port = new Uri(fixture.Issuer).Port;
        var idle = new List<TcpClient>();
        try
        {
            // No client sends a TLS ClientHello. They still count before authentication.
            for (int index = 0; index < 4; ++index)
                idle.Add(await ConnectIdleAsync(port));

            using TcpClient excess = await ConnectIdleAsync(port);
            await ClosedAsync(excess, TimeSpan.FromSeconds(3));
            await Task.WhenAll(idle.Select(connection => ClosedAsync(connection, TimeSpan.FromSeconds(7))));

            using HttpResponseMessage recovered = await fixture.Http.GetAsync(McpOAuth.ResourceMetadata, Stop);
            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        }
        finally
        {
            foreach (TcpClient connection in idle) connection.Dispose();
        }
    }

    [Fact]
    public async Task PublicDiscoveryBudgetLeavesTokenAndMcpPathsAvailable()
    {
        await using PublicHarness fixture = await PublicHarness.StartAsync(time: new FixedPublicClock());
        for (int index = 0; index < 24; ++index)
        {
            using HttpResponseMessage admitted = await fixture.Http.GetAsync(McpOAuth.ResourceMetadata, Stop);
            Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        }

        using HttpResponseMessage limited = await fixture.Http.GetAsync(McpOAuth.ResourceMetadata, Stop);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(1), limited.Headers.RetryAfter?.Delta);

        using var empty = new FormUrlEncodedContent([]);
        using HttpResponseMessage token = await fixture.Http.PostAsync("/oauth/token", empty, Stop);
        Assert.Equal(HttpStatusCode.BadRequest, token.StatusCode);
        using HttpResponseMessage mcp = await fixture.Http.GetAsync("/mcp", Stop);
        Assert.Equal(HttpStatusCode.Unauthorized, mcp.StatusCode);
    }

    private static async Task<TcpClient> ConnectIdleAsync(int port)
    {
        var connection = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await connection.ConnectAsync(IPAddress.Loopback, port, Stop);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static async Task ClosedAsync(TcpClient connection, TimeSpan within)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Stop);
        deadline.CancelAfter(within);
        try
        {
            byte[] bytes = new byte[1];
            Assert.Equal(0, await connection.GetStream().ReadAsync(bytes, deadline.Token));
        }
        catch (IOException exception) when (exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset })
        {
            // Refusing excess connections may reset the transport instead of reporting EOF.
        }
    }

    private sealed class FixedPublicClock : TimeProvider
    {
        private readonly DateTimeOffset now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => now;
    }
}
