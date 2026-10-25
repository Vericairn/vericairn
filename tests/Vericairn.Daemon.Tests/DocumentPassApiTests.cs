// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class DocumentPassApiTests
{
    public DocumentPassApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public Task SdkReadsThePassAndAssignsDisplayedDocumentsThroughTheServedRoutesWithoutASocket() => Task.Run(async () =>
    {
        using var f = new DocumentPassFixture();
        using var handler = new PolicyAdoptionApiTests.RouteHandler(f.Service);
        using var client = new VericairnClient(new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid") });
        DocumentPassResponse pass = await client.DocumentPassAsync("0.2.0", "widget", TestContext.Current.CancellationToken);
        Assert.True(pass.Ok, pass.Problem?.Message);
        Assert.Equal(4, pass.Documents.Count);
        Assert.Equal(3, pass.Updates.Count);
        var request = new DocumentPassAssignRequest(f.Estate.OperatorId, f.Estate.BuilderId, "DOCS-REVIEW-1", pass.Release!, [pass.Documents[0].Declaration]);
        DocumentPassAssignResponse assigned = await client.DocumentPassAssignAsync("0.2.0", request, TestContext.Current.CancellationToken);
        Assert.True(assigned.Ok, assigned.Problem?.Message);
        Assert.Equal(2, assigned.Written.Count);
        DocumentPassResponse after = client.DocumentPass("0.2.0", f.Product.Id);
        Assert.Equal(pass.Documents.Select(d => d.Declaration), after.Documents.Select(d => d.Declaration));
        Assert.Equal(f.Estate.BuilderId, Assert.Single(after.Documents.SelectMany(d => d.Assignments)).Reviewer);
        Assert.False(client.DocumentPassAssign("0.2.0", request).Ok);
        Assert.Single(client.Records("work-order"));
        Assert.Single(client.Records("assignment"));
        Assert.Empty(f.Git("status", "--porcelain"));
        Assert.Equal(f.SecondCommit, f.Git("rev-parse", "HEAD").Trim());
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssignmentRequestsBindTheAuthorToTheAuthenticatedPrincipal(bool localNetwork)
    {
        using var f = new DocumentPassFixture();
        DocumentPassResponse pass = f.Service.DocumentPass("0.2.0");
        var request = new DocumentPassAssignRequest(f.Estate.OperatorId, f.Estate.BuilderId, "DOCS-REVIEW-1", pass.Release!, [pass.Documents[0].Declaration]);
        const string Route = "/v1/documents/pass/{version}/assignments";
        Assert.True(RequestScope.Covers("POST " + Route));
        Assert.True(RequestScope.Covers(typeof(DocumentPassAssignRequest)));
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = Route;
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", f.Estate.BuilderId, "public-key", localNetwork);
        var scope = new RequestScope(f.Service, new LocalNetworkLedger(f.Estate.Root));
        bool reached = false;
        object? response = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ =>
        { reached = true; return ValueTask.FromResult<object?>(null); });
        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
        Assert.Empty(f.Service.Records("assignment"));
        Assert.Empty(f.Service.Records("work-order"));
        Assert.Empty(LocalNetworkLedger.Read(f.Estate.Root));
    }
}
