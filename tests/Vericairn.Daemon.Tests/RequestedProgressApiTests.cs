// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class RequestedProgressApiTests
{
    public RequestedProgressApiTests() => DaemonTestHome.Ensure();

    [Theory]
    [InlineData("{\"type\":\"accurate\",\"done\":7,\"total\":20,\"unit\":\"files\"}")]
    [InlineData("{\"type\":\"best-effort\",\"percent\":60}")]
    [InlineData("{\"type\":\"not-measurable\"}")]
    [InlineData("{\"type\":\"accurate\",\"done\":7,\"total\":20,\"unit\":\"files\",\"estimatePercent\":60}")]
    public async Task HttpEndpointKeepsTypedClaimsAndRefusesMalformedClaimsBeforeAnyWrite(string value)
    {
        using var fixture = new ProgressFixture();
        fixture.Set("brief", ProgressRequestTests.FullRequest);
        await using DaemonHost host = await DaemonTestHome.StartAsync(fixture.Estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        using JsonDocument progress = JsonDocument.Parse(value);
        var request = new WorkProgressRequest(fixture.Attempt.Id, fixture.Estate.BuilderId, "review", "Reading the widget.")
            { Progress = progress.RootElement.Clone(), StatusText = "Read seven files." };
        int records = fixture.Estate.Load().Records.Count;
        WorkProgressResponse live = await client.WorkProgressAsync(request, TestContext.Current.CancellationToken);
        Assert.True(live.Ok, live.Problem?.Message);
        Assert.True(JsonElement.DeepEquals(progress.RootElement, live.Claim!.Progress!.Value));
        Assert.Equal(request.StatusText, live.Claim.StatusText);
        Assert.Null(live.Written);
        Assert.Equal(records, fixture.Estate.Load().Records.Count);
        Assert.False(client.WorkProgress(request with { StatusText = null }).Ok);
        using JsonDocument malformed = JsonDocument.Parse("""{"type":"best-effort","percent":101}""");
        Assert.False(client.WorkProgress(request with { Progress = malformed.RootElement, Completed = true }).Ok);
        Assert.Equal(records, fixture.Estate.Load().Records.Count);
        WorkProgressResponse completed = client.WorkProgress(request with { Completed = true });
        Assert.True(completed.Ok, completed.Problem?.Message);
        Assert.Equal(completed.Written, (await client.WorkProgressAsync(request with { Completed = true }, TestContext.Current.CancellationToken)).Written);
        Assert.Equal(records + 1, fixture.Estate.Load().Records.Count);
        JsonElement body = client.Record(completed.Written!.Id)!.Envelope.GetProperty("body");
        Assert.True(JsonElement.DeepEquals(progress.RootElement, body.GetProperty("progress")));
        Assert.Equal(request.StatusText, body.GetProperty("statusText").GetString());
    }
}
