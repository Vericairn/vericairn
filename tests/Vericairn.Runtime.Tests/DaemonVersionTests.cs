// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class DaemonVersionTests
{
    private static string ClientRelease => typeof(VericairnClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    [Theory]
    [InlineData("0.8.0")]
    [InlineData("99.0.0")]
    public async Task HealthNamesBothReleasesAndRestartWithoutRefusingSupportedOperations(string release)
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath == "/v1/health" ? Json(Health(release)) : Json(Array.Empty<ObjectRow>()));
        Assert.Null(client.DaemonVersionWarning);
        Assert.Equal(release, client.Health().Vericairn);
        AssertWarning(client.DaemonVersionWarning, release);
        Assert.Empty(client.Objects());
        Assert.Equal(release, (await client.HealthAsync(TestContext.Current.CancellationToken)).Vericairn);
        Assert.Empty(await client.ObjectsAsync(TestContext.Current.CancellationToken));
        AssertWarning(new CancellationBoundApi(client, TestContext.Current.CancellationToken).DaemonVersionWarning, release);
    }

    [Fact]
    public async Task BuildMetadataDoesNotWarnAndHealthRefreshClearsAnOldWarning()
    {
        string release = "0.8.0";
        using var client = Client(_ => Json(Health(release)));
        client.Health();
        Assert.NotNull(client.DaemonVersionWarning);
        release = ClientRelease + "+another-build";
        await client.HealthAsync(TestContext.Current.CancellationToken);
        Assert.Null(client.DaemonVersionWarning);
        release = ClientRelease + "-other";
        client.Health();
        AssertWarning(client.DaemonVersionWarning, release);
    }

    [Theory]
    [InlineData("sync", "read")]
    [InlineData("async", "read")]
    [InlineData("bound", "read")]
    [InlineData("sync", "write")]
    [InlineData("async", "write")]
    [InlineData("bound", "write")]
    [InlineData("sync", "upgrade")]
    [InlineData("async", "upgrade")]
    [InlineData("bound", "upgrade")]
    [InlineData("sync", "object")]
    [InlineData("async", "object")]
    [InlineData("bound", "object")]
    public async Task AnUnknownRouteIdentifiesTheDaemonAndOperationWithoutRetrying(string surface, string operation)
    {
        int healthReads = 0;
        var requests = new List<string>();
        using var client = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/health")
            {
                healthReads++;
                return Json(Health("0.8.0"));
            }
            requests.Add(request.Method + " " + request.RequestUri.AbsolutePath);
            return new(HttpStatusCode.NotFound);
        });
        var attempt = new AttemptRequest("assignment", "contributor", "workspace", "note");
        var upgrade = new UpgradeRequest("0.8.2", null, true);
        ClientRefusedException failure;
        if (surface == "async")
        {
            failure = await Assert.ThrowsAsync<ClientRefusedException>(async () =>
            {
                switch (operation)
                {
                    case "read": await client.ObjectsAsync(TestContext.Current.CancellationToken); break;
                    case "write": await client.AttemptAsync(attempt, TestContext.Current.CancellationToken); break;
                    case "upgrade": await client.UpgradeAsync(upgrade, TestContext.Current.CancellationToken); break;
                    default: await client.ObjectPutAsync([1], TestContext.Current.CancellationToken); break;
                }
            });
        }
        else
        {
            IEstateApi api = surface == "bound" ? new CancellationBoundApi(client, TestContext.Current.CancellationToken) : client;
            failure = Assert.Throws<ClientRefusedException>(() =>
            {
                switch (operation)
                {
                    case "read": api.Objects(); break;
                    case "write": api.Attempt(attempt); break;
                    case "upgrade": api.Upgrade(upgrade); break;
                    default: api.ObjectPut([1]); break;
                }
            });
        }
        Assert.Equal(HttpStatusCode.NotFound, failure.StatusCode);
        Assert.False(failure.OutcomeUnknown);
        AssertWarning(failure.Message, "0.8.0");
        Assert.Contains("does not provide " + Assert.Single(requests), failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("diagnostic could not be read", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, healthReads);
    }

    [Fact]
    public async Task KnownDaemonHealthIsReusedAndStructuredNotFoundRetainsItsMeaning()
    {
        int healthReads = 0;
        var refused = WriteOutcome.Fail(new("not-found", "No assignment has that id.", []));
        using var client = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/health")
            {
                healthReads++;
                return Json(Health("0.8.0"));
            }
            return request.Method == HttpMethod.Post ? Json(refused, HttpStatusCode.NotFound) : new(HttpStatusCode.NotFound);
        });
        client.Health();
        Assert.Null(client.Record("missing"));
        Assert.Null(await client.RecordAsync("missing", TestContext.Current.CancellationToken));
        Assert.Null(await client.ObjectGetAsync("missing", TestContext.Current.CancellationToken));
        var request = new AttemptRequest("missing", "contributor", "workspace", "note");
        Assert.Equal(JsonSerializer.Serialize(refused), JsonSerializer.Serialize(client.Attempt(request)));
        Assert.Equal(JsonSerializer.Serialize(refused), JsonSerializer.Serialize(await client.AttemptAsync(request, TestContext.Current.CancellationToken)));
        ClientRefusedException failure = await Assert.ThrowsAsync<ClientRefusedException>(() => client.ObjectsAsync(TestContext.Current.CancellationToken));
        AssertWarning(failure.Message, "0.8.0");
        Assert.Equal(1, healthReads);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task AnUnknownWriteRouteCannotLookLikeAnEmptyWriteOutcome(string body)
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath == "/v1/health" ? Json(Health("0.8.0"))
            : new(HttpStatusCode.NotFound) { Content = new StringContent(body) });
        client.Health();
        var request = new AttemptRequest("assignment", "contributor", "workspace", "note");
        AssertWarning(Assert.Throws<ClientRefusedException>(() => client.Attempt(request)).Message, "0.8.0");
        AssertWarning((await Assert.ThrowsAsync<ClientRefusedException>(() => client.AttemptAsync(request, TestContext.Current.CancellationToken))).Message, "0.8.0");
    }

    [Fact]
    public async Task CommunicationFallbackKeepsTheMissingOperationDiagnostic()
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath == "/v1/health" ? Json(Health("0.8.0")) : new(HttpStatusCode.NotFound));
        AssertWarning(client.Communication().Unavailable, "0.8.0");
        CommunicationProfile asynchronous = await client.CommunicationAsync(cancellationToken: TestContext.Current.CancellationToken);
        AssertWarning(asynchronous.Unavailable, "0.8.0");
        Assert.Contains("does not provide GET /v1/communication", asynchronous.Unavailable!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sync")]
    [InlineData("async")]
    [InlineData("bound")]
    public async Task TheOlderDaemonsRoutesDistinguishMissingLookupsFromAbsentResources(string surface)
    {
        int descriptions = 0;
        using var client = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/health") return Json(Health("0.8.0"));
            if (request.RequestUri.AbsolutePath == OpenApiDocument.Path)
            {
                descriptions++;
                return Json(new { openapi = "3.1.0", paths = new Dictionary<string, object>
                {
                    ["/v1/records/{id}"] = new { get = new { } },
                    ["/v1/objects/{hash}"] = new { get = new { } },
                    ["/v1/estate-policies/{key}"] = new { get = new { } },
                } });
            }
            return new(HttpStatusCode.NotFound);
        });
        await client.HealthAsync(TestContext.Current.CancellationToken);
        ClientRefusedException failure;
        if (surface == "async")
        {
            Assert.Null(await client.RecordAsync("missing/record", TestContext.Current.CancellationToken));
            Assert.Null(await client.ObjectGetAsync("missing", TestContext.Current.CancellationToken));
            failure = await Assert.ThrowsAsync<ClientRefusedException>(() => client.PolicyAsync("missing", TestContext.Current.CancellationToken));
        }
        else
        {
            IEstateApi api = surface == "bound" ? new CancellationBoundApi(client, TestContext.Current.CancellationToken) : client;
            Assert.Null(api.Record("missing/record"));
            Assert.Null(api.ObjectGet("missing"));
            failure = Assert.Throws<ClientRefusedException>(() => api.Policy("missing"));
        }
        AssertWarning(failure.Message, "0.8.0");
        Assert.Contains("does not provide GET /v1/estate-policies/missing/view", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, descriptions);
    }

    [Fact]
    public async Task AnUnavailableRouteDescriptionDoesNotInventAMissingOperation()
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath == "/v1/health" ? Json(Health("0.8.0"))
            : request.RequestUri.AbsolutePath == OpenApiDocument.Path ? Json(Array.Empty<string>()) : new(HttpStatusCode.NotFound));
        client.Health();
        Assert.Null(client.Record("missing"));
        Assert.Null(await client.PolicyAsync("missing", TestContext.Current.CancellationToken));
        AssertWarning(client.DaemonVersionWarning, "0.8.0");
    }

    [Fact]
    public void AChangedDaemonHealthRefreshesItsCachedRoutes()
    {
        int process = 1;
        int descriptions = 0;
        using var client = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/health") return Json(Health("0.8.0") with { ProcessId = process });
            if (request.RequestUri.AbsolutePath == OpenApiDocument.Path)
            {
                descriptions++;
                var paths = new Dictionary<string, object>();
                if (process == 1) paths.Add("/v1/records/{id}", new { get = new { } });
                return Json(new { openapi = "3.1.0", paths });
            }
            return new(HttpStatusCode.NotFound);
        });
        client.Health();
        Assert.Null(client.Record("missing"));
        process = 2;
        client.Health();
        AssertWarning(Assert.Throws<ClientRefusedException>(() => client.Record("missing")).Message, "0.8.0");
        Assert.Equal(2, descriptions);
    }

    [Fact]
    public async Task UnreadableHealthCannotEraseTheOriginalMissingOperationRefusal()
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath == "/v1/health"
            ? new(HttpStatusCode.OK) { Content = new StringContent("not json") } : new(HttpStatusCode.NotFound));
        ClientRefusedException failure = await Assert.ThrowsAsync<ClientRefusedException>(() => client.AttemptAsync(
            new("assignment", "contributor", "workspace", "note"), TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, failure.StatusCode);
        Assert.False(failure.OutcomeUnknown);
        Assert.Contains("daemon of unknown release", failure.Message, StringComparison.Ordinal);
        Assert.Contains("does not provide POST /v1/work/attempt", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Vericairn " + ClientRelease, failure.Message, StringComparison.Ordinal);
        Assert.Contains("vericairn runtime stop", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task McpKeepsTheResultJsonAndAddsTheDaemonWarningAsSeparateContent()
    {
        using var client = Client(_ => Json(Health("0.8.0")));
        var registry = new ToolRegistry(new CancellationBoundApi(client, TestContext.Current.CancellationToken), null);
        var server = new McpServer(registry, ClientRelease);
        using var input = new StringReader("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"estate_health","arguments":{}}}""");
        using var output = new StringWriter();
        await server.RunAsync(input, output, cancellationToken: TestContext.Current.CancellationToken);
        using JsonDocument message = JsonDocument.Parse(output.ToString());
        JsonElement result = message.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        JsonElement content = result.GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        using JsonDocument health = JsonDocument.Parse(content[0].GetProperty("text").GetString()!);
        Assert.Equal("0.8.0", health.RootElement.GetProperty("vericairn").GetString());
        AssertWarning(content[1].GetProperty("text").GetString(), "0.8.0");
    }

    private static void AssertWarning(string? message, string daemon)
    {
        Assert.NotNull(message);
        Assert.Contains("Vericairn " + ClientRelease, message, StringComparison.Ordinal);
        Assert.Contains("Vericairn " + daemon, message, StringComparison.Ordinal);
        Assert.Contains("vericairn runtime stop", message, StringComparison.Ordinal);
        Assert.Contains("vericairn runtime start", message, StringComparison.Ordinal);
        Assert.Contains("daemon's machine", message, StringComparison.Ordinal);
    }

    private static HealthResponse Health(string release) => new(ApiVersion.Current, release, "estate", "root", "repository-synced", 3, "now", 1);
    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value, options: ProtocolJson.Options) };
    private static VericairnClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) => new(new HttpClient(new Handler(response)) { BaseAddress = new("http://fixture.invalid") });

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage result = response(request);
            result.RequestMessage = request;
            return Task.FromResult(result);
        }
    }
}
