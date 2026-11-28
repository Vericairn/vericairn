// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>Single-use browser and code-exchange state. A browser must retain its secure transaction cookie across the two forms.</summary>
internal sealed class McpBrowserAuthorization(TimeProvider clock, string issuer)
{
    private const int Limit = 256;
    private const int PendingPeerLimit = 8;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly object gate = new();
    private readonly Dictionary<string, McpBrowserFlow> flows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, McpCodeExchange> exchanges = new(StringComparer.Ordinal);

    internal string? Begin(HttpContext http, McpAuthorizationRequest request)
    {
        lock (gate)
        {
            Prune();
            IPAddress peer = McpPublicPeer.Of(http.Connection.RemoteIpAddress);
            if (flows.Count >= Limit || flows.Values.Count(f => f.Peer.Equals(peer)) >= PendingPeerLimit) return null;

            string id = McpTokenStore.RandomValue(18);
            string cookie = McpTokenStore.RandomValue(24);
            flows.Add(id, new McpBrowserFlow(request, McpTokenStore.Hash(cookie), clock.GetUtcNow() + Lifetime, peer));
            http.Response.Cookies.Append(Cookie(id), cookie, new CookieOptions
            {
                Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, MaxAge = Lifetime, Path = "/",
            });
            return id;
        }
    }

    internal McpBrowserFlow? Flow(HttpContext context, string id, bool consume)
    {
        if (id.Length != 24 || !McpTokenStore.IsUrlValue(id)) return null;
        lock (gate)
        {
            Prune();
            if (!flows.TryGetValue(id, out McpBrowserFlow? flow) || !context.Request.Cookies.TryGetValue(Cookie(id), out string? cookie)
                || flow.Cookie != McpTokenStore.Hash(cookie))
            {
                return null;
            }

            if (consume) flows.Remove(id);
            return flow;
        }
    }

    internal bool Confirm(string id, McpBrowserFlow flow, McpApproval approval)
    {
        lock (gate)
        {
            Prune();
            if (!flows.TryGetValue(id, out McpBrowserFlow? current) || current != flow || current.Approval is not null) return false;
            flows[id] = current with { Approval = approval };
            return true;
        }
    }

    internal string? Complete(McpAuthorizationRequest request, McpTokenGrant grant)
    {
        lock (gate)
        {
            Prune();
            if (exchanges.Count >= Limit) return null;
            string secret = McpTokenStore.RandomValue(32);
            exchanges.Add(McpTokenStore.Hash(secret), new McpCodeExchange(request, grant, clock.GetUtcNow() + TimeSpan.FromMinutes(5)));
            return secret;
        }
    }

    internal McpTokenGrant? Exchange(string secret, string client, string redirect, string resource, string verifier)
    {
        if (secret.Length != 43 || verifier.Length is < 43 or > 128 || verifier.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or '~')))
        {
            return null;
        }

        string hash = McpTokenStore.Hash(secret);
        lock (gate)
        {
            Prune();
            if (!exchanges.TryGetValue(hash, out McpCodeExchange? found) || found.Grant.Client != client || found.Grant.Resource != resource
                || found.Request.Redirect != redirect || found.Request.Challenge != McpTokenStore.Hash(verifier))
            {
                return null;
            }

            exchanges.Remove(hash);
            return found.Grant;
        }
    }

    internal Task RedirectAsync(HttpContext http, McpAuthorizationRequest request, string? code, string? error)
    {
        var values = new Dictionary<string, string?> { ["iss"] = issuer };
        if (request.State is not null) values["state"] = request.State;
        if (code is not null) values["code"] = code;
        if (error is not null) values["error"] = error;
        http.Response.Redirect(QueryHelpers.AddQueryString(request.Redirect, values));
        return Task.CompletedTask;
    }

    internal static Task PageAsync(HttpContext http, string id, PairingPlan? estate = null, McpApproval? approval = null, McpClientMetadata? client = null, string? redirect = null, bool denied = false)
    {
        static string Escape(string value) => WebUtility.HtmlEncode(value);
        Uri? clientUri = client is not null && Uri.TryCreate(client.Id, UriKind.Absolute, out Uri? identifier)
            ? identifier
            : redirect is null ? null : new Uri(redirect);
        string? origin = clientUri is null ? null : new UriBuilder(clientUri) { Host = clientUri.IdnHost }.Uri.GetLeftPart(UriPartial.Authority);
        string message = denied ? "<p>The approval code could not be used.</p>" : string.Empty;
        string prompt = estate is null || approval is null || client is null
            ? "<label>Approval code <input name=\"approval_code\" required autocomplete=\"off\"></label> <button name=\"action\" value=\"prove\">Continue</button>"
            : "<dl><dt>Estate</dt><dd>" + Escape(estate.EstateName ?? estate.Estate) + "</dd><dt>Principal</dt><dd>"
                + Escape(estate.PrincipalName ?? estate.Principal) + "</dd><dt>Client</dt><dd><bdi>" + Escape(client.Name)
                + "</bdi></dd><dt>Client origin</dt><dd>" + Escape(origin ?? string.Empty) + "</dd><dt>Scope</dt><dd>" + Escape(approval.Scope) + "</dd><dt>Expires</dt><dd>" + Escape(RecordParser.FormatTimestamp(approval.ExpiresAt))
                + "</dd></dl><p>Tool results go to this client and any model it uses.</p>"
                + "<button name=\"action\" value=\"approve\">Approve</button> <button name=\"action\" value=\"deny\">Deny</button>";
        http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        // A same-origin form POST must carry the issuer Origin. Never send the page URL to the client callback.
        http.Response.Headers["Referrer-Policy"] = "same-origin";
        http.Response.ContentType = "text/html; charset=utf-8";
        return http.Response.WriteAsync("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Authorize MCP client</title>"
            + "<h1>Authorize MCP client</h1>" + message + "<form method=\"post\" action=\"/oauth/authorize\"><input type=\"hidden\" name=\"transaction\" value=\""
            + id + "\">" + prompt + "</form></html>", http.RequestAborted);
    }

    private void Prune()
    {
        DateTimeOffset now = clock.GetUtcNow();
        foreach ((string id, McpBrowserFlow flow) in flows)
        {
            if (flow.Until <= now) flows.Remove(id);
        }

        foreach ((string id, McpCodeExchange exchange) in exchanges)
        {
            if (exchange.Until <= now) exchanges.Remove(id);
        }
    }

    private static string Cookie(string id) => "__Host-vc-mcp-" + id;
}

internal sealed record McpBrowserFlow(McpAuthorizationRequest Request, string Cookie, DateTimeOffset Until, IPAddress Peer, McpApproval? Approval = null);
internal sealed record McpCodeExchange(McpAuthorizationRequest Request, McpTokenGrant Grant, DateTimeOffset Until);
internal sealed record McpAuthorizationRequest(
    McpClientMetadata Client, string Redirect, string Challenge, string? Scope, string? State, bool IncludesRead = false);
