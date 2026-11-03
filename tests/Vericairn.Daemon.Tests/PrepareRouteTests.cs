// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// POST /v1/runs/prepare (FIX-86) on every surface a route has: the contract, the request scope, the machine-local list, the client
/// deadline and the SDK; refused over the local network and served on the runtime's own machine.
/// </summary>
[Collection("daemon")]
public class PrepareRouteTests
{
    private const string Route = "/v1/runs/prepare";

    [Fact]
    public void ThePreparationRouteIsDescribedScopedMachineLocalAndObservedAsLongAsARunsStart()
    {
        Assert.Contains(("POST", Route), OpenApiDocument.Routes);
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement operation = contract.RootElement.GetProperty("paths").GetProperty(Route).GetProperty("post");
        Assert.Equal("#/components/schemas/RunStartRequest", operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Contains("no setup command, no record, no dispatch", operation.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("RunResponse", operation.GetProperty("responses").GetProperty("200").GetProperty("description").GetString(), StringComparison.Ordinal);

        Assert.True(RequestScope.Covers("POST " + Route));
        Assert.Contains("POST " + Route, RequestAuthenticator.MachineLocal);
        Assert.Equal(ClientOperation.Deadline("RunStart"), ClientOperation.Deadline("RunPrepare"));
        Assert.Equal(TimeSpan.FromMinutes(15), ClientOperation.Deadline("RunPrepare", mayHaveEffect: true));
        Assert.NotNull(typeof(VericairnClient).GetMethod(nameof(VericairnClient.RunPrepareAsync), [typeof(RunStartRequest), typeof(CancellationToken)]));
        Assert.NotNull(typeof(CancellationBoundApi).GetMethod(nameof(CancellationBoundApi.RunPrepare), [typeof(RunStartRequest)]));
    }

    [Fact]
    public async Task APreparationIsRefusedOverTheLocalNetworkAndAnsweredOnTheRuntimesMachine()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        string by = harness.Estate.OperatorId;
        string missing = Ulid.NewUlid().Value;
        var request = new RunStartRequest(missing, "generic", harness.Estate.BuilderId, by, missing);
        RuntimeConnection paired = harness.Pair(by);
        DeviceRequestSigner signer = harness.Signer(paired);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(paired.DevicePublicKey), DateTimeOffset.UtcNow);
        using var lan = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);

        (HttpStatusCode status, string body) = await lan.SendSignedAsync(signer, HttpMethod.Post, Route, JsonSerializer.Serialize(request, ProtocolJson.Options));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Contains("runtime's own machine", JsonSerializer.Deserialize<Problem>(body, ProtocolJson.Options)!.Message, StringComparison.Ordinal);

        // On the runtime's machine the SDK reaches the route, and the run's own refusals answer before anything is prepared.
        RunResponse refused = await harness.Socket.RunPrepareAsync(request, TestContext.Current.CancellationToken);
        Assert.False(refused.Ok);
        Assert.Equal("not-started", refused.Status);
        Assert.NotNull(refused.Problem);
        Assert.Null(refused.Workspace);
        Assert.Contains("nothing was recorded", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.Socket.Records("operation"));
    }
}
