// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Http;

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
}
