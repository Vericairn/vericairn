// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class OperatorRecordApiTests
{
    public OperatorRecordApiTests() => DaemonTestHome.Ensure();

    [Fact(Timeout = 30000)]
    public async Task TheSynchronousSdkOperationRoundTripsARefusalWithoutAuthoringAnything()
    {
        using var estate = new TestEstate(3);
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var api = new VericairnClient(host.Endpoint);
        int count = host.Service.Session.Load().Records.Count;
        var request = new OperatorRecordRequest("acceptance", estate.OperatorId, JsonSerializer.SerializeToElement(new { statement = "Inspect first." }));
        OperatorRecordResponse response = api.OperatorRecord(request);
        Assert.False(response.Ok);
        Assert.Equal(count, host.Service.Session.Load().Records.Count);
        // The asynchronous counterpart reaches the same route and refuses the same way.
        OperatorRecordResponse asynchronous = await api.OperatorRecordAsync(request, TestContext.Current.CancellationToken);
        Assert.False(asynchronous.Ok);
        Assert.Equal(count, host.Service.Session.Load().Records.Count);
        Assert.Contains("operator record acceptance", response.NextStep, StringComparison.Ordinal);
        Assert.True(RequestScope.Covers("POST /v1/operator/record"));
        Assert.True(RequestScope.Covers(typeof(OperatorRecordRequest)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheNewRouteRefusesANamedAuthorDifferentFromItsAuthenticatedPrincipal(bool localNetwork)
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-operator-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var scope = new RequestScope(service, new LocalNetworkLedger(scratch));
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/v1/operator/record";
            context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", estate.BuilderId, "public-key", localNetwork);
            var request = new OperatorRecordRequest("acceptance", estate.OperatorId, JsonSerializer.SerializeToElement(new { }));
            bool reached = false;
            object? response = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ =>
            { reached = true; return ValueTask.FromResult<object?>(null); });
            Assert.False(reached);
            Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
            Assert.Empty(service.Session.Load().OfType("acceptance"));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }
}
