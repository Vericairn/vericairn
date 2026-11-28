// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Http;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon.Tests;

public sealed class McpBrowserApprovalTests
{
    [Fact]
    public async Task ApprovalFormCarriesItsOriginAndKeepsThePageUrlFromTheClient()
    {
        var http = new DefaultHttpContext();

        await McpBrowserAuthorization.PageAsync(http, McpTokenStore.RandomValue(18));

        // A no-referrer policy makes Chromium send Origin: null on this same-origin form POST.
        Assert.Equal("same-origin", http.Response.Headers["Referrer-Policy"].ToString());
    }

    [Theory]
    [InlineData("https://bücher.example:8443/client.json", "https://client.example/callback", "https://xn--bcher-kva.example:8443")]
    [InlineData("dynamic-id", "https://bücher.example/callback", "https://xn--bcher-kva.example")]
    public async Task ConsentShowsTheClientOriginWithAnAsciiHost(string clientId, string redirect, string expectedOrigin)
    {
        var http = new DefaultHttpContext();
        using var body = new MemoryStream();
        http.Response.Body = body;
        var estate = new PairingPlan("record", "at", 3, "estate", "Test estate", "principal", "Test principal", "agent");
        var client = new McpClientMetadata(clientId, "A <client>", [redirect], null);
        var issuer = new PairingAuthority("administrator", "device", "key", "admission");
        var approval = new McpApproval(estate.Principal, issuer, "read", DateTimeOffset.UnixEpoch, null);

        await McpBrowserAuthorization.PageAsync(http, McpTokenStore.RandomValue(18), estate, approval, client, redirect);

        body.Position = 0;
        using var reader = new StreamReader(body);
        string consent = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Contains("<dt>Client origin</dt><dd>" + expectedOrigin + "</dd>", consent, StringComparison.Ordinal);
        Assert.Contains("<bdi>A &lt;client&gt;</bdi>", consent, StringComparison.Ordinal);
        Assert.DoesNotContain("bücher", consent, StringComparison.Ordinal);
    }
}
