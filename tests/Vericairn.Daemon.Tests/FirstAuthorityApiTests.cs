// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// <c>POST /v1/estate/first-authority</c> through the daemon (CODE-72): the SDK lists, checks and names over the local socket, the running
/// daemon reads the manifest it wrote at once, and no device-signed request, on loopback or over the local network, reaches it.
/// </summary>
[Collection("daemon")]
public class FirstAuthorityApiTests
{
    public FirstAuthorityApiTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public async Task TheDaemonNamesTheFirstAuthorityAndReadsTheManifestItWrote()
    {
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("DFA-1"), signer: f.OperatorSigner);
        RecordEnvelope first = SignatureChecks.FirstAuthority(f.Load(), f.Estate.OperatorId)!;
        RecordEnvelope device = f.Device(f.Estate.OperatorId, "Another device");
        RecordEnvelope stray = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Itself."), device.Reference, signer: f.Signer(device));
        await using DaemonHost host = await DaemonTestHome.StartAsync(f.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(DaemonHost.Discover(f.Root)!);
        Assert.DoesNotContain(client.Records("work-order"), r => r.Id == real.Id);

        (FirstAuthorityResponse? listed, Problem? problem) = client.FirstAuthority(new FirstAuthorityRequest());
        Assert.Null(problem);
        Assert.Equal(new[] { first.Id, stray.Id }.Order(StringComparer.Ordinal), listed!.Candidates.Select(c => c.Admission));
        Assert.Contains(listed.Findings, x => x.Code == "manifest.first-authority");

        (FirstAuthorityResponse? check, _) = await client.FirstAuthorityAsync(new FirstAuthorityRequest(Admission: first.Id, Check: true), TestContext.Current.CancellationToken);
        (FirstAuthorityResponse? refused, Problem? wrong) = client.FirstAuthority(new FirstAuthorityRequest(f.Estate.BuilderId, Admission: first.Id, ExpectPlan: check!.PlanHash));
        Assert.Null(refused);
        Assert.Equal("refused", wrong!.Code);
        (FirstAuthorityResponse? named, Problem? none) = client.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Admission: first.Id, ExpectPlan: check.PlanHash));
        Assert.Null(none);
        Assert.True(named!.Applied);
        Assert.Equal(first.Id, named.FirstAuthority);
        Assert.Equal("signature.self-admission", Assert.Single(named.Remaining).Code);

        // The running daemon reads the new manifest on its next read: the operator's device is admitted and its records count.
        Assert.Contains(client.Records("work-order"), r => r.Id == real.Id);
        Assert.Equal(first.Id, host.Service.Session.Load().Manifest!.FirstAuthority);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoDeviceSignedRequestListsOrNamesAndTheContractDescribesTheRoute(bool localNetwork)
    {
        using var f = new HistoryFixture();
        using Runtime.Service.EstateService service = Runtime.Service.EstateService.Open(f.Root);
        var scope = new RequestScope(service, new LocalNetworkLedger(f.Root));
        string publicKey = HistoryFixture.Body(f.OperatorDevice)["publicKey"]!.GetValue<string>();
        foreach (FirstAuthorityRequest request in new[] { new FirstAuthorityRequest(), new FirstAuthorityRequest(f.Estate.OperatorId, Device: f.OperatorDevice.Id, ExpectPlan: "sha256:" + new string('0', 64)) })
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/v1/estate/first-authority";
            context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice(f.OperatorDevice.Id, f.Estate.OperatorId, publicKey, localNetwork);
            object? denied = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ =>
                throw new InvalidOperationException("A device-signed first-authority request must not reach its handler."));
            Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
            Assert.Contains("device-signed request", Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult>(denied).ResponseContent, StringComparison.Ordinal);
        }

        Assert.Contains("POST /v1/estate/first-authority", RequestAuthenticator.MachineLocal);
        Assert.True(RequestScope.Covers("POST /v1/estate/first-authority"));
        Assert.True(RequestScope.Covers(typeof(FirstAuthorityRequest)));
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement path = contract.RootElement.GetProperty("paths").GetProperty("/v1/estate/first-authority").GetProperty("post");
        Assert.Equal("postEstateFirst-authority", path.GetProperty("operationId").GetString());
        Assert.Equal("#/components/schemas/FirstAuthorityRequest", path.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/FirstAuthorityResponse", path.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.True(path.GetProperty("responses").TryGetProperty("409", out _));
        Assert.Null(f.Load().Manifest!.FirstAuthority);
    }
}
