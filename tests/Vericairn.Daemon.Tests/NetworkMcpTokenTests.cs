// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed partial class NetworkMcpTokenTests
{
    private const string Legacy = "2025-11-25";
    private const string Modern = "2026-07-28";
    private const string Redirect = "https://client.example/redirect?from=mcp";
    private const string DocumentId = "https://client.example/metadata.json";
    private static readonly string[] Redirects = [Redirect];
    private static readonly string[] Revisions = [Modern, Legacy];
    private static CancellationToken Stop => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OAuthClientWorksBothMcpRevisionsAndRevocationEndsItsAccess(bool metadata)
    {
        await using PublicHarness fixture = await PublicHarness.StartAsync();
        HttpClient client = fixture.Http;
        using HttpResponseMessage unavailable = await client.GetAsync("/v1/health", Stop);
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
        using HttpResponseMessage unsigned = await client.GetAsync("/mcp", Stop);
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Contains(fixture.Issuer + McpOAuth.ResourceMetadata, unsigned.Headers.WwwAuthenticate.Single().ToString(), StringComparison.Ordinal);
        using HttpResponseMessage protectedResource = await client.GetAsync(McpOAuth.ResourceMetadata, Stop);
        JsonObject resource = await JsonAsync(protectedResource);
        Assert.Equal(fixture.Resource, resource["resource"]!.GetValue<string>());
        Assert.Equal(fixture.Issuer, resource["authorization_servers"]![0]!.GetValue<string>());
        using HttpResponseMessage server = await client.GetAsync("/.well-known/oauth-authorization-server", Stop);
        JsonObject advertised = await JsonAsync(server);
        Assert.Equal(fixture.Issuer, advertised["issuer"]!.GetValue<string>());
        Assert.True(advertised["client_id_metadata_document_supported"]!.GetValue<bool>());
        Assert.True(advertised["authorization_response_iss_parameter_supported"]!.GetValue<bool>());
        Assert.DoesNotContain(fixture.Estate.OperatorId, advertised.ToJsonString(), StringComparison.Ordinal);

        using (var foreignHost = new HttpRequestMessage(HttpMethod.Get, "/mcp"))
        {
            foreignHost.Headers.Host = "unrelated.example";
            using HttpResponseMessage answer = await client.SendAsync(foreignHost, Stop);
            Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
        }

        using (var foreignOrigin = new HttpRequestMessage(HttpMethod.Get, "/mcp"))
        {
            foreignOrigin.Headers.Add("Origin", "https://unrelated.example");
            using HttpResponseMessage answer = await client.SendAsync(foreignOrigin, Stop);
            Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
        }

        string clientId = metadata ? DocumentId : await RegisterAsync(client);
        Assert.DoesNotContain(fixture.Socket.Devices(), d => d.Profiles?.Contains("token", StringComparer.Ordinal) == true);
        TokenPair tokens = await AuthorizeAsync(fixture, clientId, "contribute");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access);

        using var wire = new McpWire(client);
        await wire.InitializeAsync(Legacy);
        JsonObject discovery = await ModernAsync(wire, "server/discover");
        Assert.Equal(Revisions, discovery["supportedVersions"]!.AsArray().Take(2).Select(v => v!.GetValue<string>()));
        JsonObject tools = await ModernAsync(wire, "tools/list");
        Assert.Contains(tools["tools"]!.AsArray(), tool => Name(tool) == "work_attempt");
        Assert.DoesNotContain(tools["tools"]!.AsArray(), tool => Name(tool) == "work_review");

        JsonObject call = await wire.CallAsync("work_attempt", NetworkMcp.Args(
            ("assignment", fixture.Assignment), ("workspace", "Scratch fixture"), ("note", "Build the widget")));
        WriteOutcome outcome = McpWire.Outcome(call);
        Assert.True(outcome.Ok, outcome.Problem?.Message);
        DeviceRow device = Assert.Single(fixture.Socket.Devices(), d => d.Profiles?.Contains("token", StringComparer.Ordinal) == true);
        Assert.Equal(fixture.Estate.BuilderId, device.Principal);
        Assert.Equal("admitted", device.Admission);
        Assert.Equal("none", device.Presence);
        JsonElement record = fixture.Socket.Record(outcome.Written!.Id)!.Envelope;
        Assert.Equal(fixture.Estate.BuilderId, record.GetProperty("by").GetString());
        Assert.Equal(device.Id, record.GetProperty("signatures")[0].GetProperty("device").GetString());

        Assert.True(fixture.Socket.DeviceRevoke(new RevokeRequest(device.Id, fixture.Estate.OperatorId, "End this fixture device.")).Ok);
        Assert.Equal(HttpStatusCode.Unauthorized, (await wire.PostAsync(McpWire.Request("tools/list"))).Status);
        Assert.Equal(0, fixture.Host.Mcp.Sessions.CountFor(device.Id));
        client.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage refreshed = await RefreshAsync(fixture, clientId, tokens.Refresh);
        Assert.Equal(HttpStatusCode.BadRequest, refreshed.StatusCode);
    }

    [Fact]
    public async Task ReadScopeHidesWritesAndRefreshReuseEndsItsFamily()
    {
        await using PublicHarness fixture = await PublicHarness.StartAsync();
        string clientId = await RegisterAsync(fixture.Http);
        TokenPair first = await AuthorizeAsync(fixture, clientId, "read");
        fixture.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.Access);
        using var wire = new McpWire(fixture.Http);
        JsonObject tools = await ModernAsync(wire, "tools/list");
        Assert.Contains(tools["tools"]!.AsArray(), tool => Name(tool) == "estate_status");
        Assert.DoesNotContain(tools["tools"]!.AsArray(), tool => Name(tool) == "work_attempt");
        Assert.False((await ModernAsync(wire, "tools/call", new JsonObject { ["name"] = "estate_status", ["arguments"] = new JsonObject() }))["isError"]!.GetValue<bool>());
        fixture.Http.DefaultRequestHeaders.Authorization = null;

        using HttpResponseMessage refreshed = await RefreshAsync(fixture, clientId, first.Refresh);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        TokenPair second = Pair(await JsonAsync(refreshed));
        Assert.NotEqual(first.Refresh, second.Refresh);
        using HttpResponseMessage reused = await RefreshAsync(fixture, clientId, first.Refresh);
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        using HttpResponseMessage ended = await RefreshAsync(fixture, clientId, second.Refresh);
        Assert.Equal(HttpStatusCode.BadRequest, ended.StatusCode);

        fixture.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.Access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await wire.PostAsync(McpWire.Request("tools/list"))).Status);
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        string badJson = JsonSerializer.Serialize(new { client_name = "Test client", redirect_uris = Redirects });
        using HttpResponseMessage invalid = await client.PostAsync("/oauth/register", new StringContent(badJson, Encoding.UTF8, "application/json"), Stop);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        string json = JsonSerializer.Serialize(new { client_name = "Test client", redirect_uris = Redirects, application_type = "web" });
        using HttpResponseMessage registered = await client.PostAsync("/oauth/register", new StringContent(json, Encoding.UTF8, "application/json"), Stop);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        return (await JsonAsync(registered))["client_id"]!.GetValue<string>();
    }

    private static async Task<TokenPair> AuthorizeAsync(PublicHarness fixture, string clientId, string wanted)
    {
        (McpApprovalIssued? issued, Problem? problem) = await fixture.Socket.IssueMcpApprovalAsync(
            new McpAuthorizeRequest(fixture.Estate.BuilderId, fixture.Estate.OperatorId, wanted), Stop);
        Assert.True(issued is not null, problem?.Message);
        Assert.Equal(fixture.Resource, issued.Endpoint);
        string verifier = McpTokenStore.RandomValue(32);
        string state = McpTokenStore.RandomValue(16);
        var parameters = new Dictionary<string, string?>
        {
            ["client_id"] = clientId, ["redirect_uri"] = Redirect, ["code_challenge"] = McpTokenStore.Hash(verifier),
            ["code_challenge_method"] = "S256", ["state"] = state, ["response_type"] = "code", ["resource"] = fixture.Resource,
        };
        HttpClient client = fixture.Http;
        using HttpResponseMessage landing = await client.GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", parameters), Stop);
        Assert.Equal(HttpStatusCode.OK, landing.StatusCode);
        string page = await landing.Content.ReadAsStringAsync(Stop);
        Assert.DoesNotContain(fixture.Estate.BuilderId, page, StringComparison.Ordinal);
        string transaction = Transaction().Match(page).Groups[1].Value;
        Assert.NotEmpty(transaction);
        using HttpResponseMessage proved = await PostFormAsync(client, "/oauth/authorize",
            ("transaction", transaction), ("action", "prove"), ("approval_code", issued.Code));
        Assert.Equal(HttpStatusCode.OK, proved.StatusCode);
        Assert.Contains("Tool results go to this client", await proved.Content.ReadAsStringAsync(Stop), StringComparison.Ordinal);
        using HttpResponseMessage approved = await PostFormAsync(client, "/oauth/authorize",
            ("transaction", transaction), ("action", "approve"));
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query = QueryHelpers.ParseQuery(approved.Headers.Location!.Query);
        Assert.Equal(fixture.Issuer, query["iss"].Single());
        Assert.Equal(state, query["state"].Single());
        Assert.Equal("mcp", query["from"].Single());
        string code = query["code"].Single()!;
        using HttpResponseMessage wrong = await PostFormAsync(client, "/oauth/token", ("grant_type", "authorization_code"), ("client_id", clientId),
            ("code", code), ("redirect_uri", Redirect), ("code_verifier", verifier), ("resource", fixture.Issuer + "/other"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using HttpResponseMessage exchanged = await PostFormAsync(client, "/oauth/token", ("grant_type", "authorization_code"), ("client_id", clientId),
            ("code", code), ("redirect_uri", Redirect), ("code_verifier", verifier), ("resource", fixture.Resource));
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
        JsonObject token = await JsonAsync(exchanged);
        Assert.Equal(900, token["expires_in"]!.GetValue<int>());
        Assert.Equal(wanted, token["scope"]!.GetValue<string>());
        return Pair(token);
    }

    private static async Task<JsonObject> ModernAsync(McpWire wire, string method, JsonObject? parameters = null)
    {
        parameters ??= new JsonObject();
        parameters["_meta"] = new JsonObject
        {
            ["io.modelcontextprotocol/protocolVersion"] = Modern,
            ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject(),
        };
        var headers = new Dictionary<string, string?> { ["MCP-Protocol-Version"] = Modern, ["Mcp-Method"] = method };
        if (parameters["name"] is { } name) headers["Mcp-Name"] = name.GetValue<string>();
        (HttpStatusCode status, JsonObject? response, string body, string? session) = await wire.PostAsync(McpWire.Request(method, parameters), headers);
        Assert.True(status == HttpStatusCode.OK, body);
        Assert.Null(session);
        return response!["result"]!.AsObject();
    }

    private static string Name(JsonNode? tool) => tool!["name"]!.GetValue<string>();

    private static Task<HttpResponseMessage> RefreshAsync(PublicHarness fixture, string clientId, string secret) =>
        PostFormAsync(fixture.Http, "/oauth/token", ("grant_type", "refresh_token"), ("client_id", clientId), ("refresh_token", secret), ("resource", fixture.Resource));

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, params (string Name, string Value)[] values)
    {
        using var form = new FormUrlEncodedContent(values.Select(pair => KeyValuePair.Create(pair.Name, pair.Value)));
        return await client.PostAsync(path, form, Stop);
    }

    private static async Task<JsonObject> JsonAsync(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync(Stop))!.AsObject();

    private static TokenPair Pair(JsonObject body) => new(body["access_token"]!.GetValue<string>(), body["refresh_token"]!.GetValue<string>());

    [GeneratedRegex("name=\"transaction\" value=\"([A-Za-z0-9_-]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex Transaction();

    private sealed record TokenPair(string Access, string Refresh);

    private sealed class PublicHarness(TestEstate estate, DaemonHost host, VericairnClient socket, HttpClient http, string assignment) : IAsyncDisposable
    {
        public TestEstate Estate => estate;
        public DaemonHost Host => host;
        public VericairnClient Socket => socket;
        public HttpClient Http => http;
        public string Assignment => assignment;
        public string Issuer => host.OAuth!.Issuer;
        public string Resource => host.OAuth!.Resource;

        internal static async Task<PublicHarness> StartAsync()
        {
            DaemonTestHome.Ensure();
            TestPrerequisites.RequireLoopback();
            var estate = new TestEstate(3);
            DaemonHost? host = null;
            VericairnClient? socket = null;
            HttpClient? client = null;
            try
            {
                var reservation = new TcpListener(IPAddress.Loopback, 0);
                reservation.Start();
                int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
                reservation.Stop();

                string certificate = Path.Combine(estate.Root, "stand-in-server.pem");
                string key = Path.Combine(estate.Root, "stand-in-key.pem");
                using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var request = new CertificateRequest("CN=localhost", ecdsa, HashAlgorithmName.SHA256);
                var names = new SubjectAlternativeNameBuilder();
                names.AddDnsName("localhost");
                request.CertificateExtensions.Add(names.Build());
                using X509Certificate2 made = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(2));
                File.WriteAllText(certificate, made.ExportCertificatePem());
                File.WriteAllText(key, ecdsa.ExportPkcs8PrivateKeyPem());
                string fingerprint = RuntimeCertificate.Fingerprint(made);
                host = await DaemonTestHome.StartAsync(estate.Root, options: new DaemonOptions
                {
                    Mcp = true, PublicMcp = new McpPublicEndpoint("localhost:" + port, certificate, key, "127.0.0.1:" + port),
                    McpTokenStore = new FileKeyStore(VericairnHome.Path()),
                    McpClientMetadata = (id, _) => Task.FromResult<McpClientMetadata?>(id == DocumentId ? new(id, "Document client", [Redirect], null) : null),
                }, cancellationToken: Stop);
                socket = new VericairnClient(host.Endpoint);
                WriteOutcome first = socket.DeviceCreate(new DeviceCreateRequest(estate.OperatorId, "Fixture operator's device", "file"));
                Assert.True(first.Ok, first.Problem?.Message);
                WriteOutcome admission = socket.DeviceAdmit(new AdmitRequest(first.Written!.Id, estate.OperatorId, "The first authority."));
                Assert.True(admission.Ok, admission.Problem?.Message);

                estate.Add("work-order", estate.OperatorId, NetworkMcp.WorkOrder);
                using JsonDocument brief = JsonDocument.Parse(NetworkMcp.Brief);
                WriteOutcome assignment = socket.Assign(new AssignRequest("W-1", estate.BuilderId, estate.OperatorId, brief.RootElement));
                Assert.True(assignment.Ok, assignment.Problem?.Message);

                client = new HttpClient(new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                        presented is not null && RuntimeCertificate.Fingerprint(presented) == fingerprint,
                }) { BaseAddress = new Uri(host.OAuth!.Issuer) };
                return new PublicHarness(estate, host, socket, client, assignment.Written!.Id);
            }
            catch
            {
                client?.Dispose();
                socket?.Dispose();
                if (host is not null) await host.DisposeAsync();
                estate.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            http.Dispose();
            socket.Dispose();
            await host.DisposeAsync();
            estate.Dispose();
        }
    }
}
