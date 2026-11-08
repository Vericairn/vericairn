// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class SearchApiTests
{
    public SearchApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public void TheContractCoversTheRoutesTheirRequestAndResponseFieldsAndEveryImplementation()
    {
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (Type type in new[] { typeof(SearchRequest), typeof(SearchResponse), typeof(SearchCandidate), typeof(SearchExcerpt), typeof(SearchIndexRequest), typeof(SearchIndexResponse),
            typeof(SearchSegment), typeof(SearchReplayRequest), typeof(SearchReplayResponse), typeof(SearchReplayOutcome), typeof(ContextRequest), typeof(BriefContextRequest),
            typeof(ContextResponse), typeof(BriefResponse) })
            Assert.Equal(type.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(),
                schemas.GetProperty(type.Name).GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        foreach (Type type in new[] { typeof(RunStartRequest), typeof(GoalStepRequest) })
            Assert.True(schemas.GetProperty(type.Name).GetProperty("properties").TryGetProperty("search", out _), type.Name);
        foreach (string route in new[] { "/v1/search", "/v1/search/index", "/v1/search/replay", "/v1/context/prepare", "/v1/context/brief" })
        {
            Assert.Contains(("POST", route), OpenApiDocument.Routes);
            Assert.True(RequestScope.Covers("POST " + route), route);
        }
        foreach (Type type in new[] { typeof(SearchRequest), typeof(SearchIndexRequest), typeof(SearchReplayRequest), typeof(ContextRequest), typeof(BriefContextRequest) })
            Assert.True(RequestScope.Covers(type), type.Name);
        foreach (string method in new[] { "Search", "SearchAsync", "SearchIndex", "SearchIndexAsync", "SearchReplay", "SearchReplayAsync" })
        {
            Assert.False(typeof(IEstateApi).GetMethod(method)!.IsAbstract);
            Assert.NotNull(typeof(CancellationBoundApi).GetMethod(method));
            Assert.NotNull(typeof(VericairnClient).GetMethod(method));
        }
    }

    [Fact]
    public async Task RoutesTheSdkAndTheBoundApiAnswerAsTheEmbeddedRuntimeDoes()
    {
        using var estate = new SearchEstate();
        using EstateService api = estate.Open();
        using var http = new HttpClient(new WorkNewApiTests.WorkRouteHandler(api)) { BaseAddress = new Uri("http://fixture.invalid") };
        using var sdk = new VericairnClient(http);
        var bound = new CancellationBoundApi(sdk, TestContext.Current.CancellationToken);
        SearchResponse embedded = api.Search(new SearchRequest("keyword index originals"));
        foreach (SearchResponse served in new[] { sdk.Search(new SearchRequest("keyword index originals")), await bound.SearchAsync(new SearchRequest("keyword index originals"), TestContext.Current.CancellationToken) })
        {
            Assert.Equal(CanonicalJson.Serialize(embedded.Selection!.Value), CanonicalJson.Serialize(served.Selection!.Value));
            Assert.Equal(embedded.Text, served.Text);
            Assert.Equal(embedded.Candidates.Select(c => c.Excerpt?.Text), served.Candidates.Select(c => c.Excerpt?.Text));
        }

        Assert.Equal("search-query-oversize", sdk.Search(new SearchRequest(new string('a', 2000))).Problem!.Code);
        Assert.Equal("search-method-unavailable", sdk.Search(new SearchRequest("keyword") { Method = "semantic" }).Problem!.Code);
        SearchIndexResponse index = sdk.SearchIndex(new SearchIndexRequest());
        Assert.Null(index.Problem);
        Assert.True(index.Current);
        Assert.Equal(["Internal", "Members only", "Public"], index.Segments.Select(s => s.Audience));

        ContextResponse package = sdk.Context(new ContextRequest("W-1", null, ["original bytes"]));
        Assert.Null(package.Problem);
        Assert.Equal(api.Context(new ContextRequest("W-1", null, ["original bytes"])).Package!.Hash, package.Package!.Hash);
        Assert.Equal(api.Context("W-1").Package!.Hash, sdk.Context(new ContextRequest("W-1")).Package!.Hash);
        BriefResponse brief = sdk.BriefContext(new BriefContextRequest("W-1", Search: ["original bytes"]))!;
        Assert.Null(brief.Problem);
        Assert.Null(sdk.BriefContext(new BriefContextRequest("NO-SUCH-WORK", Search: ["original bytes"])));
        Assert.Null(await bound.BriefContextAsync(new BriefContextRequest("NO-SUCH-WORK", Search: ["original bytes"]), TestContext.Current.CancellationToken));

        SearchReplayResponse replay = sdk.SearchReplay(new SearchReplayRequest(embedded.Selection));
        Assert.True(Assert.Single(replay.Outcomes).Matches);
    }

    [Fact]
    public async Task ADeviceSignedSearchNamesOnlyItsOwnPrincipalAndIsAReadWithoutTheWriteLedger()
    {
        using var writer = new PromptWriter();
        var device = writer.Api.Devices().Single(d => d.Principal == writer.Builder && d.Name == "Prompt fixture writer");
        var standing = writer.Api.Standing(device.Id)!;
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-search-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            int writes = 0;
            var scope = new RequestScope(writer.Api, new LocalNetworkLedger(scratch), beforeWrite: () => writes++);
            foreach ((string route, object request, bool allowed) in new (string, object, bool)[]
            {
                ("/v1/search", new SearchRequest("prompt", Principal: writer.Operator), false),
                ("/v1/search", new SearchRequest("prompt", Principal: writer.Builder), true),
                ("/v1/search", new SearchRequest("prompt"), true),
                ("/v1/context/prepare", new ContextRequest("NONE", null, ["prompt"]) { Principal = writer.Operator }, false),
                ("/v1/search/index", new SearchIndexRequest(), true),
            })
            {
                var context = new DefaultHttpContext();
                context.Request.Method = "POST";
                context.Request.Path = route;
                context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice(standing.Identity, writer.Builder, standing.PublicKey, false);
                object? result = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ => ValueTask.FromResult<object?>(request switch
                {
                    SearchRequest search => writer.Api.Search(search),
                    SearchIndexRequest rebuild => writer.Api.SearchIndex(rebuild),
                    ContextRequest prepare => writer.Api.Context(prepare),
                    _ => null,
                }));
                if (allowed) Assert.True(result is SearchResponse { Problem: null } or SearchIndexResponse { Problem: null }, route);
                else Assert.IsNotType<SearchResponse>(result);
            }

            Assert.Equal(0, writes);
            Assert.Empty(Directory.GetFiles(scratch, "*", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }
}
