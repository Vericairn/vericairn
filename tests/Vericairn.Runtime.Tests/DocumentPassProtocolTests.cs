// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class DocumentPassProtocolTests
{
    private static readonly string[] RequiredAssignmentFields = ["by", "reviewer", "key", "release", "documents"];
    [Fact]
    public async Task SdkAndCancellationWrapperCarryExactSelectionsOverMatchingSyncAndAsyncRoutes()
    {
        using var f = new DocumentPassFixture();
        DocumentPassResponse pass = f.Service.DocumentPass("0.2.0");
        var assignment = new DocumentPassAssignRequest(f.Estate.OperatorId, f.Estate.BuilderId, "DOCS-REVIEW-1", pass.Release!, [pass.Documents[0].Declaration]);
        var seen = new List<string>();
        using var client = new VericairnClient(new HttpClient(new Handler(async (request, token) =>
        {
            seen.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            if (request.Method == HttpMethod.Get)
            {
                Assert.Null(request.Content);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(pass, options: ProtocolJson.Options) };
            }
            DocumentPassAssignRequest body = (await request.Content!.ReadFromJsonAsync<DocumentPassAssignRequest>(ProtocolJson.Options, token))!;
            Assert.Equal(assignment.Release, body.Release);
            Assert.Equal(assignment.Documents, body.Documents);
            Assert.Equal(assignment.Reviewer, body.Reviewer);
            var refusal = new DocumentPassAssignResponse([], new("conflict", "Fixture stale selection.", []));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(refusal, options: ProtocolJson.Options) };
        })) { BaseAddress = new Uri("https://fixture.invalid") });
        Assert.Equal(pass.Release, client.DocumentPass("0.2.0", "product with spaces").Release);
        Assert.Equal(pass.Release, (await client.DocumentPassAsync("0.2.0", "product with spaces", TestContext.Current.CancellationToken)).Release);
        Assert.Equal("conflict", client.DocumentPassAssign("0.2.0", assignment).Problem!.Code);
        Assert.Equal("conflict", (await client.DocumentPassAssignAsync("0.2.0", assignment, TestContext.Current.CancellationToken)).Problem!.Code);
        var bound = new CancellationBoundApi(client, TestContext.Current.CancellationToken);
        Assert.Equal(pass.Release, bound.DocumentPass("0.2.0", "product with spaces").Release);
        Assert.False(bound.DocumentPassAssign("0.2.0", assignment).Ok);
        Assert.Equal("GET /v1/documents/pass/0.2.0?product=product%20with%20spaces", seen[0]);
        Assert.Equal(seen[0], seen[1]);
        Assert.Equal("POST /v1/documents/pass/0.2.0/assignments", seen[2]);
        Assert.Equal(seen[2], seen[3]);
        Assert.Equal(seen[0], seen[4]);
        Assert.Equal(seen[2], seen[5]);
    }

    [Fact]
    public void AddedInterfaceMembersHaveDefaultsAndOpenApiDescribesRequiredPinsAndProductSelection()
    {
        foreach (string member in new[] { "DocumentPass", "DocumentPassAssign", "DocumentPassAsync", "DocumentPassAssignAsync" })
            Assert.False(typeof(IEstateApi).GetMethod(member, BindingFlags.Public | BindingFlags.Instance)!.IsAbstract);
        Assert.False(typeof(Vericairn.Records.Estate.IDocumentSource).GetMethod("Changes")!.IsAbstract);
        using JsonDocument api = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement paths = api.RootElement.GetProperty("paths");
        JsonElement get = paths.GetProperty("/v1/documents/pass/{version}").GetProperty("get");
        Assert.Contains(get.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "version" && p.GetProperty("required").GetBoolean());
        Assert.Contains(get.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "product");
        Assert.Equal("#/components/schemas/DocumentPassResponse", get.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        JsonElement schema = api.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("DocumentPassAssignRequest");
        Assert.Equal(RequiredAssignmentFields, schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDistinguishesAReadFromAnUncertainAssignment(bool write)
    {
        var entered = TestTiming.Signal();
        using var stop = new CancellationTokenSource();
        using var client = new VericairnClient(new HttpClient(new Handler(async (_, token) =>
        {
            entered.TrySetResult();
            await TestTiming.UntilCancelled(token);
            return new(HttpStatusCode.OK);
        })) { BaseAddress = new Uri("https://fixture.invalid") });
        Task pending = write ? client.DocumentPassAssignAsync("0.2.0", new("author", "reviewer", "KEY-1", new("release", "hash"), []), stop.Token)
            : client.DocumentPassAsync("0.2.0", cancellationToken: stop.Token);
        await entered.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        stop.Cancel();
        ClientCancelledException exception = await Assert.ThrowsAsync<ClientCancelledException>(() => pending);
        Assert.Equal(ClientFailure.FromException(new ClientCancelledException(write, default))!.Message, ClientFailure.FromException(exception)!.Message);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"written\":[],\"problem\":null}")]
    [InlineData("{\"written\":[{\"id\":\"receipt\",\"hash\":\"hash\",\"path\":\"record.json\"}],\"problem\":null}")]
    [InlineData("{\"written\":[{},{}],\"problem\":null}")]
    [InlineData("{\"written\":[{\"id\":\"receipt\",\"hash\":\"hash\",\"path\":\"record.json\"},{\"id\":\"receipt\",\"hash\":\"hash\",\"path\":\"record.json\"}],\"problem\":null}")]
    public async Task AnIncompleteSuccessResponseCannotClaimThatTheAssignmentWasStored(string body)
    {
        using var client = new VericairnClient(new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") }))) { BaseAddress = new Uri("https://fixture.invalid") });
        ClientUnavailableException failure = await Assert.ThrowsAsync<ClientUnavailableException>(() =>
            client.DocumentPassAssignAsync("0.2.0", new("author", "reviewer", "KEY-1", new("release", "hash"), []), TestContext.Current.CancellationToken));
        Assert.True(failure.OutcomeUnknown);
        Assert.Throws<JsonException>(() => client.DocumentPassAssign("0.2.0", new("author", "reviewer", "KEY-1", new("release", "hash"), [])));
    }

    [Fact]
    public async Task AnOlderServerMissingTheAssignmentRouteProducesADefiniteRefusalWithoutRetrying()
    {
        int writes = 0;
        using var client = new VericairnClient(new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Post) writes++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") });
        })) { BaseAddress = new Uri("https://fixture.invalid") });
        await Assert.ThrowsAsync<ClientRefusedException>(() => client.DocumentPassAssignAsync("0.2.0",
            new("author", "reviewer", "KEY-1", new("release", "hash"), []), TestContext.Current.CancellationToken));
        Assert.Equal(1, writes);
        Assert.Throws<ClientRefusedException>(() => client.DocumentPassAssign("0.2.0", new("author", "reviewer", "KEY-1", new("release", "hash"), [])));
        Assert.Equal(2, writes);
    }
}
