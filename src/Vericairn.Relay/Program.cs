// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Vericairn.Relay;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseKestrelHttpsConfiguration();
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxConcurrentConnections = 4096;
});
WebApplication app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15), KeepAliveTimeout = TimeSpan.FromSeconds(15) });
// Relay:Registrations and Relay:RegistrationsPerClient, from the command line (--Relay:Registrations=512) or the environment
// (Relay__Registrations=512), raise or lower the registration limits.
var relay = new RelayServer(TimeProvider.System, new RelayLimits(
    builder.Configuration.GetValue("Relay:Registrations", 256), builder.Configuration.GetValue("Relay:RegistrationsPerClient", 32)));
app.Map("/v1/relay", async context =>
{
    if (!context.Request.IsHttps || !context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    using System.Net.WebSockets.WebSocket socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
    await relay.HandleAsync(socket, RelayServer.PeerOf(context.Connection.RemoteIpAddress), context.RequestAborted).ConfigureAwait(false);
});
await app.RunAsync().ConfigureAwait(false);
