// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
if (Limit("Relay:Registrations", 256) is not { } total || Limit("Relay:RegistrationsPerClient", 32) is not { } perClient)
{
    return 2;
}

var relay = new RelayServer(TimeProvider.System, new RelayLimits(total, perClient));
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
return 0;

int? Limit(string name, int fallback)
{
    string? value = builder.Configuration[name];
    if (value is null) return fallback;
    if (int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int limit) && limit > 0) return limit;
    Console.Error.WriteLine($"{name} must be a whole number above zero; it is \"{value}\". The relay did not start.");
    return null;
}
