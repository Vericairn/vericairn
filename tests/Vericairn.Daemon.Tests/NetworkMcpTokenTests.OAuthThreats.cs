// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.WebUtilities;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

public sealed partial class NetworkMcpTokenTests
{
    [Fact]
    public async Task McpOriginAllowlistDoesNotOpenOAuthEndpointsToOtherOrigins()
    {
        const string allowed = "https://agent.example";
        await using PublicHarness fixture = await PublicHarness.StartAsync(allowedOrigins: [allowed]);
        using var mcp = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        mcp.Headers.Add("Origin", allowed);
        using HttpResponseMessage challenge = await fixture.Http.SendAsync(mcp, Stop);
        Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);

        (HttpMethod Method, string Path)[] endpoints =
        [
            (HttpMethod.Get, McpOAuth.ResourceMetadata),
            (HttpMethod.Get, "/.well-known/oauth-authorization-server"),
            (HttpMethod.Get, "/oauth/authorize"),
            (HttpMethod.Post, "/oauth/authorize"),
            (HttpMethod.Post, "/oauth/register"),
            (HttpMethod.Post, "/oauth/token"),
        ];
        foreach ((HttpMethod method, string path) in endpoints)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("Origin", allowed);
            using HttpResponseMessage refused = await fixture.Http.SendAsync(request, Stop);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using var opaque = new HttpRequestMessage(HttpMethod.Post, "/oauth/authorize");
        opaque.Headers.Add("Origin", "null");
        opaque.Headers.Add("Sec-Fetch-Site", "same-origin");
        using HttpResponseMessage badOrigin = await fixture.Http.SendAsync(opaque, Stop);
        Assert.Equal(HttpStatusCode.Forbidden, badOrigin.StatusCode);
    }

    [Fact]
    public async Task SixWrongGuessesExhaustAnApprovalWithoutUsingAnotherApproval()
    {
        await using PublicHarness fixture = await PublicHarness.StartAsync();
        var request = new McpAuthorizeRequest(fixture.Estate.BuilderId, fixture.Estate.OperatorId, "read");
        (McpApprovalIssued? issued, Problem? problem) = await fixture.Socket.IssueMcpApprovalAsync(request, Stop);
        Assert.True(issued is not null, problem?.Message);
        (McpApprovalIssued? other, Problem? otherProblem) = await fixture.Socket.IssueMcpApprovalAsync(request, Stop);
        Assert.True(other is not null, otherProblem?.Message);
        var parameters = new Dictionary<string, string?>
        {
            ["client_id"] = DocumentId, ["redirect_uri"] = Redirect,
            ["code_challenge"] = McpTokenStore.Hash(McpTokenStore.RandomValue(32)),
            ["code_challenge_method"] = "S256", ["response_type"] = "code", ["resource"] = fixture.Resource,
        };
        using HttpResponseMessage landing = await fixture.Http.GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", parameters), Stop);
        string transaction = Transaction().Match(await landing.Content.ReadAsStringAsync(Stop)).Groups[1].Value;
        Assert.NotEmpty(transaction);

        for (int attempt = 0; attempt < 6; attempt++)
        {
            // Keep the selector and change the secret, so each failure counts against this approval.
            string wrong = issued.Code[..^1] + (issued.Code[^1] == '0' ? '1' : '0');
            await AssertRefusedAsync(wrong);
        }

        await AssertRefusedAsync(issued.Code);
        using HttpResponseMessage unaffected = await PostBrowserFormAsync(fixture.Http, landing,
            ("transaction", transaction), ("action", "prove"), ("approval_code", other.Code));
        Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
        Assert.Contains("Tool results go to this client", await unaffected.Content.ReadAsStringAsync(Stop), StringComparison.Ordinal);

        async Task AssertRefusedAsync(string code)
        {
            using HttpResponseMessage refused = await PostBrowserFormAsync(fixture.Http, landing,
                ("transaction", transaction), ("action", "prove"), ("approval_code", code));
            Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
            string page = await refused.Content.ReadAsStringAsync(Stop);
            Assert.Contains("The approval code could not be used.", page, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.Estate.BuilderId, page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PublicBearerTokenDoesNotAuthenticateOnEitherSignedListener()
    {
        await using PublicHarness fixture = await PublicHarness.StartAsync();
        TokenPair tokens = await AuthorizeAsync(fixture, DocumentId, "read");
        var bearer = new AuthenticationHeaderValue("Bearer", tokens.Access);
        fixture.Http.DefaultRequestHeaders.Authorization = bearer;
        using (var wire = new McpWire(fixture.Http))
        {
            Assert.Contains((await ModernAsync(wire, "tools/list"))["tools"]!.AsArray(), tool => Name(tool) == "estate_status");
        }

        LoopbackEndpoint endpoint = fixture.Host.Endpoint.Loopback!;
        using var loopback = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                presented is not null && RuntimeCertificate.Fingerprint(presented) == endpoint.Certificate,
        }) { BaseAddress = new UriBuilder("https", endpoint.Host, endpoint.Port).Uri };
        loopback.DefaultRequestHeaders.Authorization = bearer;
        foreach (string path in new[] { "/mcp", "/v1/health", "/oauth/token" })
        {
            using HttpResponseMessage response = await loopback.GetAsync(path, Stop);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        await using LocalNetworkHarness network = await LocalNetworkHarness.StartAsync(mcp: true);
        RuntimeConnection paired = network.Pair(network.Estate.BuilderId, "Signed client");
        using X509Certificate2 credential = LocalNetworkCredential.Issue(network.Signer(paired),
            Base64Url.DecodeFromChars(paired.DevicePublicKey), DateTimeOffset.UtcNow);
        using var local = new MutualTls(network, credential, network.Host.Endpoint.Loopback!.Certificate);
        Assert.Equal(HttpStatusCode.Unauthorized, (await local.SendAsync(HttpMethod.Get, "/mcp", null,
            extra: new Dictionary<string, string> { ["Authorization"] = bearer.ToString() })).Status);
    }

    [Fact]
    public async Task ATokenForAnotherAudienceIsRefusedEvenWithCurrentStanding()
    {
        await using PublicHarness fixture = await PublicHarness.StartAsync();
        (PairingAuthority? authority, string? refusal) = fixture.Host.Service.McpTokenIssuer(fixture.Estate.BuilderId, fixture.Estate.OperatorId);
        Assert.True(authority is not null, refusal);
        (McpTokenDevice? enrolled, Problem? problem) = fixture.Host.Service.CreateMcpTokenDevice(
            fixture.Estate.BuilderId, authority, new FileKeyStore(VericairnHome.Path()));
        Assert.True(enrolled is not null, problem?.Message);

        // Seed the same token store before the listener reads it, as if its published address had changed.
        string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(fixture.Estate.Root))!;
        var store = new McpTokenStore(directory, fixture.Host.Time);
        var grant = new McpTokenGrant(enrolled.Device, enrolled.Principal, enrolled.PublicKey, enrolled.Admission, authority.Principal,
            DocumentId, "https://former-address.example/mcp", "read", fixture.Host.Time.GetUtcNow().AddDays(1));
        McpTokenResponse access = Assert.IsType<McpTokenResponse>(store.Create(grant));
        Assert.NotNull(store.Authenticate(access.AccessToken, grant.Resource));

        fixture.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access.AccessToken);
        using HttpResponseMessage answer = await fixture.Http.GetAsync("/mcp", Stop);
        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Contains("invalid_token", answer.Headers.WwwAuthenticate.Single().ToString(), StringComparison.Ordinal);
    }
}
