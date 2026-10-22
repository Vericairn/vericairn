// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class ReleaseCheckApiTests
{
    [Fact]
    public async Task CheckUsesTheMappedReadRouteAndPreservesItsExactRequestAndAllObjections()
    {
        DaemonTestHome.Ensure();
        using var f = new PolicyFixture();
        using var service = EstateService.Open(f.Estate.Root);
        using var client = new VericairnClient(new HttpClient(new PolicyAdoptionApiTests.RouteHandler(service)) { BaseAddress = new Uri("http://fixture.invalid") });
        var request = new ReleaseSubmitRequest(f.Estate.Estate.Id, "1.0.0", null, f.Estate.OperatorId, Commit: new string('a', 40), Expected: "stale") { ClientBuild = "unknown" };
        string[] before = Snapshot(f.Estate.Root);
        ReleaseResponse response = await client.ReleaseCheckAsync(request, TestContext.Current.CancellationToken);
        Assert.False(response.Ok);
        Assert.Equal(request.Commit, response.Check!.Commit);
        Assert.Equal(request.Expected, response.Check.Expected);
        Assert.Contains(response.Check.Objections, o => o.Code == "release.product");
        Assert.Contains(response.Check.Objections, o => o.Code == "release.client-build");
        Assert.All(response.Check.Objections, o => Assert.NotEmpty(o.NextAction));
        Assert.Equal(before, Snapshot(f.Estate.Root));
        Assert.Empty(service.Records("operation"));
        using JsonDocument api = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement route = api.RootElement.GetProperty("paths").GetProperty("/v1/releases/check").GetProperty("post");
        Assert.Contains("ReleaseSubmitRequest", route.GetProperty("requestBody").GetRawText(), StringComparison.Ordinal);
        Assert.Contains("ReleaseResponse", route.GetProperty("responses").GetProperty("200").GetRawText(), StringComparison.Ordinal);
        JsonElement schemas = api.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal("#/components/schemas/ReleaseCheckDetails", schemas.GetProperty("ReleaseResponse").GetProperty("properties").GetProperty("check").GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/ReleaseObjection", schemas.GetProperty("ReleaseCheckDetails").GetProperty("properties").GetProperty("objections").GetProperty("items").GetProperty("$ref").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckByAnAuthenticatedDeviceNeedsNoWriteLedgerOrPresence(bool localNetwork)
    {
        DaemonTestHome.Ensure();
        using var f = new PolicyFixture();
        using var service = EstateService.Open(f.Estate.Root);
        string scratch = Path.Combine(f.Estate.Root, ".vericairn", "check-ledger");
        int writes = 0;
        var scope = new RequestScope(service, new LocalNetworkLedger(scratch), beforeWrite: () => writes++);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/releases/check";
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice(f.OperatorDevice.Id, f.Estate.OperatorId, f.OperatorDevice.Body.GetProperty("publicKey").GetString()!, localNetwork);
        var request = new ReleaseSubmitRequest(f.Estate.Estate.Id, "1.0.0", null, f.Estate.OperatorId, Commit: new string('a', 40), Expected: "stale") { ClientBuild = EstateService.Version };
        string[] before = Snapshot(f.Estate.Root);
        object? result = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ => ValueTask.FromResult<object?>(service.ReleaseCheck(request)));
        Assert.NotNull(Assert.IsType<ReleaseResponse>(result).Check);
        Assert.Equal(0, writes);
        Assert.False(Directory.Exists(scratch));
        Assert.Equal(before, Snapshot(f.Estate.Root));
    }

    private static string[] Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(root, file) + " " + ContentHash.Sha256File(file)).Order(StringComparer.Ordinal).ToArray();
}
