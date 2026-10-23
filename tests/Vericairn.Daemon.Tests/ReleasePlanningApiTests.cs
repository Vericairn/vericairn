// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class ReleasePlanningApiTests
{
    public ReleasePlanningApiTests() => DaemonTestHome.Ensure();

    [Fact(Timeout = TestTiming.HangTimeoutMilliseconds)]
    public Task BothOperationsTraverseTheActualMappedRoutesWithoutASocket() => Task.Run(async () =>
    {
        using var f = new ReleasePlanningFixture();
        using var handler = new PolicyAdoptionApiTests.RouteHandler(f.Api);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid") };
        using var api = new VericairnClient(http);
        int before = f.Model.Records.Count;
        WorkMovePlanResponse moved = api.WorkMovePlan("PLAN-1", f.Move);
        Assert.True(moved.Ok, moved.Problem?.Message);
        ReleaseResponse withdrawn = await api.ReleaseWithdrawAsync(f.Withdraw, TestContext.Current.CancellationToken);
        Assert.True(withdrawn.Ok, withdrawn.Message);
        WorkMovePlanResponse stale = await api.WorkMovePlanAsync("PLAN-1", f.Move, TestContext.Current.CancellationToken);
        Assert.Equal("conflict", stale.Problem!.Code);
        Assert.Empty(stale.Written);
        Assert.Equal("conflict", api.ReleaseWithdraw(f.Withdraw).Problem!.Code);
        Assert.Equal(before + 4, f.Model.Records.Count);
        ReleaseResponse pending = api.ReleaseObserve(new("1.0.0", null, f.Operator) { Withdrawal = withdrawn.Operation!.Id });
        Assert.Equal("refused", pending.Problem!.Code);
        Assert.Equal(before + 4, f.Model.Records.Count);
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ThePairedDeviceLedgerRetainsPlanningReceiptsAndRefusals(bool move, bool refused)
    {
        using var f = new ReleasePlanningFixture();
        object request = move ? f.Move : f.Withdraw;
        string route = move ? "/v1/work/{key}/move-plan" : "/v1/releases/withdraw";
        RecordWritten? expected;
        object answered;
        if (move)
        {
            WorkMovePlanResponse response = f.Api.WorkMovePlan("PLAN-1", refused ? f.Move with { Expected = "stale" } : f.Move);
            expected = response.Written.Count == 0 ? null : response.Written[^1]; answered = response;
        }
        else
        {
            ReleaseResponse response = f.Api.ReleaseWithdraw(refused ? f.Withdraw with { Expected = "stale" } : f.Withdraw);
            expected = response.Operation; answered = response;
        }
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = route;
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", f.Operator, "public-key", LocalNetwork: true);
        var scope = new RequestScope(f.Api, new LocalNetworkLedger(f.Records.Estate.Root));
        await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ => ValueTask.FromResult<object?>(Results.Ok(answered)));
        LocalNetworkWrite kept = Assert.Single(LocalNetworkLedger.Read(f.Records.Estate.Root));
        Assert.Equal(refused ? "failed" : "written", kept.State);
        Assert.Equal(expected?.Id, kept.Record);
        Assert.Equal(expected?.Hash, kept.Hash);
        Assert.Equal(expected?.Path, kept.Path);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PlanningWritesBindTheAuthorToTheAuthenticatedPrincipal(bool move, bool localNetwork)
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        string route = move ? "/v1/work/{key}/move-plan" : "/v1/releases/withdraw";
        object request = move ? new WorkMovePlanRequest(estate.OperatorId, "stale", new Dictionary<string, string>(), Version: "1.1.0")
            : new ReleaseWithdrawRequest("candidate", estate.OperatorId, "stale", "fixture-releases", "Replace the candidate.");
        Assert.True(RequestScope.Covers("POST " + route));
        Assert.True(RequestScope.Covers(request.GetType()));
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = route;
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", estate.BuilderId, "public-key", localNetwork);
        var scope = new RequestScope(service, new LocalNetworkLedger(estate.Root));
        int before = service.Session.Load().Records.Count;
        bool reached = false;
        object? response = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ =>
        { reached = true; return ValueTask.FromResult<object?>(null); });
        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
        Assert.Equal(before, service.Session.Load().Records.Count);
        Assert.Empty(LocalNetworkLedger.Read(estate.Root));
    }
}
