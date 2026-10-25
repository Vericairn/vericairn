// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class ReleaseReviewApiTests
{
    public ReleaseReviewApiTests() => DaemonTestHome.Ensure();

    [Theory(Timeout = TestTiming.HangTimeoutMilliseconds)]
    [InlineData("sync", "focused")]
    [InlineData("async", "full")]
    [InlineData("bound", "focused")]
    public Task SdkAndBoundClientsTraverseBothMappedRoutesWithoutASocket(string surface, string scope) => Task.Run(async () =>
    {
        using var f = new ReleaseReviewFixture(scope);
        using var handler = new PolicyAdoptionApiTests.RouteHandler(f.Api);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid") };
        using var sdk = new VericairnClient(http);
        IEstateApi api = surface == "bound" ? new CancellationBoundApi(sdk, TestContext.Current.CancellationToken) : sdk;
        int before = f.Model.Records.Count;
        ReleaseReviewBriefResponse prepared = surface == "async"
            ? await api.ReleaseReviewBriefAsync(ReleaseReviewFixture.Version, f.Product.Id, f.Decision.Id, TestContext.Current.CancellationToken)
            : api.ReleaseReviewBrief(ReleaseReviewFixture.Version, f.Product.Id, f.Decision.Id);
        Assert.True(prepared.Ok, prepared.Problem?.Message);
        Assert.Equal(f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version).Text, prepared.Text);
        Assert.Equal(before, f.Model.Records.Count);
        var request = new ReleaseReviewAssignRequest(f.Operator, prepared.Hash!, f.Product.Id, f.Decision.Id);
        ReleaseReviewBriefResponse assigned = surface == "async"
            ? await api.ReleaseReviewAssignAsync(ReleaseReviewFixture.Version, request, TestContext.Current.CancellationToken)
            : api.ReleaseReviewAssign(ReleaseReviewFixture.Version, request);
        Assert.True(assigned.Ok, assigned.Problem?.Message);
        Assert.Equal(prepared.Text, assigned.Text);
        Assert.Equal(prepared.Hash, assigned.Hash);
        Assert.Equal(f.Reviewer.Id, EstateModel.BodyString(f.Model.ById[assigned.Assignment!.Id], "contributor"));
        ReleaseReviewBriefResponse stale = await api.ReleaseReviewAssignAsync(ReleaseReviewFixture.Version, request with { Expected = "stale" }, TestContext.Current.CancellationToken);
        Assert.Equal("conflict", stale.Problem!.Code);
        Assert.Null(stale.Assignment);
        Assert.Equal(before + 1, f.Model.Records.Count);
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssignmentAuthorMustMatchTheAuthenticatedPrincipal(bool localNetwork)
    {
        using var f = new ReleaseReviewFixture();
        const string operation = "POST /v1/releases/{version}/review-brief";
        Assert.True(RequestScope.Covers(operation));
        Assert.True(RequestScope.Covers(typeof(ReleaseReviewAssignRequest)));
        var context = Context(f, localNetwork);
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", f.Reviewer.Id, "public-key", localNetwork);
        var filter = new RequestScope(f.Api, new LocalNetworkLedger(f.Records.Estate.Root));
        bool reached = false;
        object? response = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, new ReleaseReviewAssignRequest(f.Operator, "prepared")), _ =>
        { reached = true; return ValueTask.FromResult<object?>(null); });
        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
        Assert.Empty(LocalNetworkLedger.Read(f.Records.Estate.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PairedDeviceLedgerKeepsAssignmentReceiptsAndRefusals(bool stale)
    {
        using var f = new ReleaseReviewFixture();
        ReleaseReviewBriefResponse prepared = f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version);
        var request = new ReleaseReviewAssignRequest(f.Operator, stale ? "stale" : prepared.Hash!);
        ReleaseReviewBriefResponse response = f.Api.ReleaseReviewAssign(ReleaseReviewFixture.Version, request);
        var filter = new RequestScope(f.Api, new LocalNetworkLedger(f.Records.Estate.Root));
        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(Context(f, true), request), _ => ValueTask.FromResult<object?>(Results.Ok(response)));
        LocalNetworkWrite kept = Assert.Single(LocalNetworkLedger.Read(f.Records.Estate.Root));
        Assert.Equal(stale ? "failed" : "written", kept.State);
        Assert.Equal(response.Assignment?.Id, kept.Record);
        Assert.Equal(response.Assignment?.Hash, kept.Hash);
    }

    private static DefaultHttpContext Context(ReleaseReviewFixture fixture, bool localNetwork)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/releases/{version}/review-brief";
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", fixture.Operator, "public-key", localNetwork);
        return context;
    }
}
