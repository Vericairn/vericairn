// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Sdk;
using Vericairn.TestSupport;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class PackagesApiTests
{
    public PackagesApiTests() => DaemonTestHome.Ensure();

    [Theory]
    [InlineData("observe", false)]
    [InlineData("unlist", false)]
    [InlineData("deprecate", false)]
    [InlineData("observe", true)]
    [InlineData("unlist", true)]
    [InlineData("deprecate", true)]
    public async Task EveryPackageRouteBindsTheAuthorToTheAuthenticatedPrincipal(string operation, bool localNetwork)
    {
        using var f = new PackageTestEstate();
        string route = "/v1/packages/" + operation;
        object request = operation switch { "observe" => f.Observe, "unlist" => f.Unlist, _ => f.Deprecate };
        Assert.True(RequestScope.Covers("POST " + route));
        Assert.True(RequestScope.Covers(request.GetType()));
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = route;
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", f.Estate.BuilderId, "public-key", localNetwork);
        var scope = new RequestScope(f.Service, new LocalNetworkLedger(f.Estate.Root));
        bool reached = false;
        object? response = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ =>
        { reached = true; return ValueTask.FromResult<object?>(null); });
        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
        Assert.Empty(f.Handler.Requests);
        Assert.Empty(f.Service.Session.Load().OfType("operation"));
        Assert.Empty(LocalNetworkLedger.Read(f.Estate.Root));
    }

    [Theory]
    [InlineData("intent")]
    [InlineData("observation")]
    [InlineData("outcome")]
    [InlineData("refusal")]
    public async Task PairedDeviceLedgerKeepsPackageRecordPointersAndRefusals(string kind)
    {
        using var f = new PackageTestEstate();
        object request = f.Observe;
        string route = "/v1/packages/observe";
        PackagesResponse answered;
        RecordWritten? expected;
        if (kind == "intent")
        {
            request = f.Unlist;
            route = "/v1/packages/unlist";
            answered = f.Service.PackagesUnlist(f.Unlist);
            expected = answered.Intent;
        }
        else if (kind == "outcome")
        {
            PackagesResponse intent = f.Service.PackagesUnlist(f.Unlist);
            var observe = new PackagesObserveRequest(f.Estate.OperatorId, Operation: intent.Intent!.Id);
            request = observe;
            f.Handler.Registration = "unlisted";
            answered = await f.Service.PackagesObserveAsync(observe, TestContext.Current.CancellationToken);
            expected = answered.Operation;
        }
        else
        {
            if (kind == "refusal") f.Handler.Registration = "malformed";
            answered = await f.Service.PackagesObserveAsync(f.Observe, TestContext.Current.CancellationToken);
            expected = kind == "refusal" ? null : Assert.Single(answered.Packages).Observation;
        }
        Assert.Equal(kind != "refusal", answered.Ok);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = route;
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", f.Estate.OperatorId, "public-key", LocalNetwork: true);
        var scope = new RequestScope(f.Service, new LocalNetworkLedger(f.Estate.Root));
        // Isolate response accounting from authentication and record authoring, exercised above and through the mapped routes.
        await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ => ValueTask.FromResult<object?>(Results.Ok(answered)));
        LocalNetworkWrite kept = Assert.Single(LocalNetworkLedger.Read(f.Estate.Root));
        Assert.Equal(kind == "refusal" ? "failed" : "written", kept.State);
        Assert.Equal(expected?.Id, kept.Record);
        Assert.Equal(expected?.Hash, kept.Hash);
        Assert.Equal(expected?.Path, kept.Path);
    }

    [Fact(Timeout = 30000)]
    public Task SdkOperationsTraverseActualMappedRoutesWithRecordedRegistryResponsesAndNoSocket() => Task.Run(async () =>
    {
        DaemonTestHome.Ensure();
        using var f = new PackageTestEstate();
        TestContext.Current.TestOutputHelper!.WriteLine("The isolated package estate is open.");
        using var handler = new PolicyAdoptionApiTests.RouteHandler(f.Service);
        TestContext.Current.TestOutputHelper!.WriteLine("The in-process routes are mapped.");
        using var transport = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid") };
        using var api = new VericairnClient(transport);
        PackagesResponse published = await api.PackagesObserveAsync(f.Observe, TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper!.WriteLine("The publication observation returned through the API.");
        Assert.True(published.Ok, published.Message);
        Assert.Equal("listed", Assert.Single(published.Packages).State.Listing);
        PackagesResponse intent = api.PackagesUnlist(f.Unlist);
        TestContext.Current.TestOutputHelper!.WriteLine("The unlisting intent returned through the API.");
        Assert.Equal("not-started", intent.Status);
        PackagesResponse rerun = await api.PackagesUnlistAsync(f.Unlist, TestContext.Current.CancellationToken);
        Assert.Equal(intent.Intent, rerun.Intent);
        f.Handler.Registration = "unlisted";
        PackagesResponse unlisted = api.PackagesObserve(new(f.Estate.OperatorId, Operation: intent.Intent!.Id));
        Assert.True(unlisted.Ok, unlisted.Message);
        PackagesResponse deprecation = api.PackagesDeprecate(f.Deprecate);
        Assert.Equal("not-started", deprecation.Status);
        Assert.Equal(deprecation.Intent, (await api.PackagesDeprecateAsync(f.Deprecate, TestContext.Current.CancellationToken)).Intent);
        f.Handler.Registration = "deprecated";
        PackagesResponse deprecated = await api.PackagesObserveAsync(new(f.Estate.OperatorId, Operation: deprecation.Intent!.Id), TestContext.Current.CancellationToken);
        Assert.True(deprecated.Ok, deprecated.Message);
        Assert.Equal("Example.Replacement", Assert.Single(deprecated.Packages).State.Details!.AlternatePackage);
        Assert.Equal(0, f.Service.Validate().Errors);
    }, TestContext.Current.CancellationToken);
}
