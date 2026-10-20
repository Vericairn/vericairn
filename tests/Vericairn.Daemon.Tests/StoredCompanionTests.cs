// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.IO.Pipes;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Runtime.Store;
using Vericairn.Sdk;
using Vericairn.TestSupport;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class StoredCompanionTests
{
    private static readonly string[] OutcomeProperties = ["written", "problem"];
    private static readonly string[] ReceiptProperties = ["path", "id", "hash"];

    public StoredCompanionTests() => DaemonTestHome.Ensure();

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EmbeddedAndDaemonSdkKeepTheStoredRecordAndItsReconciliation(bool daemon, bool asynchronous)
    {
        await using var fixture = await CompanionWriteFixture.StartAsync(daemon);
        RecordWritten assignment = fixture.Assign();
        fixture.DeclareCompanions("attempt");
        fixture.InterruptPublication();
        var request = new AttemptRequest(assignment.Id, fixture.Author, "test repository", "Store once.");
        WriteOutcome outcome = asynchronous
            ? await fixture.Api.AttemptAsync(request, TestContext.Current.CancellationToken)
            : fixture.Api.Attempt(request);
        RecordWritten written = fixture.AssertInterrupted(outcome);
        Assert.Equal(written.Id, Assert.Single(fixture.Api.Records("attempt")).Id);
        Assert.Equal(written.Hash, fixture.Api.Record(written.Id)!.Hash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpConfirmsStorageAndKeepsTheExistingStatusesForGenuineRefusals(bool daemon)
    {
        await using var fixture = await CompanionWriteFixture.StartAsync(daemon);
        RecordWritten assignment = fixture.Assign();
        fixture.DeclareCompanions("attempt");
        using HttpClient http = daemon ? LocalHttp(fixture.Endpoint)
            : new(new MappedRoutesHandler(fixture.Api)) { BaseAddress = new Uri("http://fixture.invalid/") };
        await Refused("/v1/records", new NewRecordRequest("unknown-type", fixture.Author, CompanionWriteFixture.Json("{}")), HttpStatusCode.UnprocessableEntity, "refused");
        await Refused("/v1/records", new NewRecordRequest("work-order", fixture.Author, fixture.Api.Record(fixture.Order.Id)!.Envelope.GetProperty("body")), HttpStatusCode.UnprocessableEntity, "rejected");
        await Refused("/v1/work/attempt", new AttemptRequest(fixture.Estate.Estate.Id, fixture.Author, "test repository", "Missing assignment."), HttpStatusCode.NotFound, "not-found");
        CompanionWriteFixture.Stored(fixture.Service.CommunicationSet(new(fixture.Author, "person", new(Status: "standard"), fixture.Author)));
        await Refused("/v1/communication", new CommunicationSetRequest(fixture.Author, "person", new(Status: "short"), fixture.Author), HttpStatusCode.Conflict, "conflict");

        fixture.InterruptPublication();
        var request = new AttemptRequest(assignment.Id, fixture.Author, "test repository", "Store once.");
        using HttpResponseMessage response = await http.PostAsJsonAsync("/v1/work/attempt", request, ProtocolJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        WriteOutcome outcome = (await response.Content.ReadFromJsonAsync<WriteOutcome>(ProtocolJson.Options, TestContext.Current.CancellationToken))!;
        fixture.AssertInterrupted(outcome);

        // A later write blocked by unresolved outputs is a real refusal, despite sharing the problem code.
        string pending = Assert.Single(Assert.Single(BatchJournal.All(fixture.Service.Session).Journals).Outputs).Path;
        string target = Path.Combine(fixture.Estate.Root, pending);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "Unexpected content must survive.\n");
        await Refused("/v1/work/attempt", request, HttpStatusCode.UnprocessableEntity, "reconcile-required");
        WriteOutcome refused = await fixture.Api.AttemptAsync(request, TestContext.Current.CancellationToken);
        Assert.Null(refused.Written);
        Assert.False(refused.Ok);
        Assert.Equal("reconcile-required", refused.Problem!.Code);
        Assert.Single(fixture.Api.Records("attempt"));
        Assert.Equal("Unexpected content must survive.\n", File.ReadAllText(target));

        async Task Refused<T>(string path, T body, HttpStatusCode status, string code)
        {
            int count = fixture.Service.Session.Load().Records.Count;
            using HttpResponseMessage reply = await http.PostAsJsonAsync(path, body, ProtocolJson.Options, TestContext.Current.CancellationToken);
            Assert.Equal(status, reply.StatusCode);
            WriteOutcome result = (await reply.Content.ReadFromJsonAsync<WriteOutcome>(ProtocolJson.Options, TestContext.Current.CancellationToken))!;
            Assert.False(result.Ok);
            Assert.Null(result.Written);
            Assert.Equal(code, result.Problem!.Code);
            Assert.Equal(count, fixture.Service.Session.Load().Records.Count);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmbeddedAndDaemonMcpKeepTheReceiptAndRecoveryInTheirExistingShape(bool daemon)
    {
        await using var fixture = await CompanionWriteFixture.StartAsync(daemon);
        RecordWritten assignment = fixture.Assign();
        fixture.DeclareCompanions("attempt");
        var registry = new ToolRegistry(fixture.Api, fixture.Author);
        var arguments = new JsonObject { ["assignment"] = fixture.Estate.Estate.Id, ["workspace"] = "test repository", ["note"] = "Store once." };
        ToolCallResult refused = await registry.CallAsync("work_attempt", arguments, TestContext.Current.CancellationToken);
        Assert.True(refused.IsError);
        Assert.False(refused.Unknown);
        WriteOutcome refusal = JsonSerializer.Deserialize<WriteOutcome>(refused.Text, ProtocolJson.Options)!;
        Assert.Null(refusal.Written);
        Assert.Equal("not-found", refusal.Problem!.Code);

        fixture.InterruptPublication();
        arguments["assignment"] = assignment.Id;
        var call = new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = "work_attempt", ["arguments"] = arguments },
        };
        using var input = new StringReader(call.ToJsonString());
        using var output = new StringWriter();
        new McpServer(registry, "test").Run(input, output);
        using JsonDocument reply = JsonDocument.Parse(output.ToString());
        Assert.False(reply.RootElement.TryGetProperty("error", out _));
        JsonElement result = reply.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean()); // Recovery is incomplete; this is the embedded path's existing flag.
        JsonElement content = Assert.Single(result.GetProperty("content").EnumerateArray());
        Assert.Equal("text", content.GetProperty("type").GetString());
        string text = content.GetProperty("text").GetString()!;
        using JsonDocument json = JsonDocument.Parse(text);
        Assert.Equal(OutcomeProperties, json.RootElement.EnumerateObject().Select(property => property.Name));
        WriteOutcome outcome = JsonSerializer.Deserialize<WriteOutcome>(text, ProtocolJson.Options)!;
        RecordWritten written = fixture.AssertInterrupted(outcome);
        Assert.Equal(ReceiptProperties, json.RootElement.GetProperty("written").EnumerateObject().Select(property => property.Name));
        Assert.Equal(written.Id, Assert.Single(fixture.Api.Records("attempt")).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SdkAndMcpRetainTheStoredOutcomeFromTheMappedDaemonRoutes(bool mcp)
    {
        await using var fixture = await CompanionWriteFixture.StartAsync(daemon: false);
        RecordWritten assignment = fixture.Assign();
        fixture.DeclareCompanions("attempt");
        using var client = new VericairnClient(new HttpClient(new MappedRoutesHandler(fixture.Api)) { BaseAddress = new Uri("http://fixture.invalid/") });
        fixture.InterruptPublication();
        WriteOutcome outcome;
        if (mcp)
        {
            var registry = new ToolRegistry(client, fixture.Author);
            ToolCallResult result = await registry.CallAsync("work_attempt", new JsonObject
            {
                ["assignment"] = assignment.Id, ["workspace"] = "test repository", ["note"] = "Store once.",
            }, TestContext.Current.CancellationToken);
            Assert.True(result.IsError);
            Assert.False(result.Unknown);
            outcome = JsonSerializer.Deserialize<WriteOutcome>(result.Text, ProtocolJson.Options)!;
        }
        else outcome = await client.AttemptAsync(new(assignment.Id, fixture.Author, "test repository", "Store once."), TestContext.Current.CancellationToken);
        RecordWritten written = fixture.AssertInterrupted(outcome);
        Assert.Equal(written.Id, Assert.Single(fixture.Service.Records("attempt")).Id);
    }

    [Fact]
    public void ApiDescriptionDistinguishesStoredRecoveryFromARefusal()
    {
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        foreach (string route in new[] { "/v1/records", "/v1/work/attempt", "/v1/work/result", "/v1/documents/{id}/reviews" })
        {
            JsonElement responses = contract.RootElement.GetProperty("paths").GetProperty(route).GetProperty("post").GetProperty("responses");
            string stored = responses.GetProperty("200").GetProperty("description").GetString()!;
            Assert.Contains("record was stored", stored, StringComparison.Ordinal);
            Assert.Contains("reconcile-required", stored, StringComparison.Ordinal);
            Assert.Contains("never resubmit", stored, StringComparison.Ordinal);
            Assert.Contains("written null", responses.GetProperty("422").GetProperty("description").GetString(), StringComparison.Ordinal);
        }
    }

    private static HttpClient LocalHttp(DaemonEndpoint endpoint) => new(new SocketsHttpHandler
    {
        ConnectCallback = async (_, token) =>
        {
            if (endpoint.Kind == "pipe")
            {
                var pipe = new NamedPipeClientStream(".", endpoint.Address, PipeDirection.InOut, PipeOptions.Asynchronous);
                try { await pipe.ConnectAsync(token); return pipe; }
                catch { pipe.Dispose(); throw; }
            }
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.Address), token); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        },
    }) { BaseAddress = new Uri("http://vericairn/") };

    // Execute the actual route binding and response delegates in memory as well as over the local transport.
    private sealed class MappedRoutesHandler : HttpMessageHandler
    {
        private readonly ServiceProvider services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        private readonly RouteEndpoint[] endpoints;

        public MappedRoutesHandler(IEstateApi api)
        {
            var routes = new MemoryRoutes(services);
            ApiRoutes.Map(routes, api, () => throw new InvalidOperationException("The test cannot shut down a host."));
            endpoints = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellationToken };
            context.Request.Method = request.Method.Method;
            context.Request.Path = request.RequestUri!.AbsolutePath;
            context.Request.QueryString = new QueryString(request.RequestUri.Query);
            byte[] bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            using var input = new MemoryStream(bytes);
            context.Request.Body = input;
            context.Request.ContentLength = bytes.Length;
            context.Request.ContentType = request.Content?.Headers.ContentType?.ToString();
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyFeature(request.Content is not null));
            using var output = new MemoryStream();
            context.Response.Body = output;
            RouteEndpoint endpoint = Assert.Single(endpoints, route => route.RoutePattern.RawText == context.Request.Path
                && route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(request.Method.Method, StringComparer.Ordinal));
            context.SetEndpoint(endpoint);
            await endpoint.RequestDelegate!(context);
            return new((HttpStatusCode)context.Response.StatusCode) { Content = new ByteArrayContent(output.ToArray()) };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) services.Dispose();
            base.Dispose(disposing);
        }

        private sealed class MemoryRoutes(IServiceProvider services) : IEndpointRouteBuilder
        {
            public IServiceProvider ServiceProvider => services;
            public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
            public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(services);
        }

        private sealed class BodyFeature(bool canHaveBody) : IHttpRequestBodyDetectionFeature
        {
            public bool CanHaveBody => canHaveBody;
        }
    }
}
