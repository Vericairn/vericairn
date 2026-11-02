// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class AttentionApiTests
{
    public AttentionApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task PresenceRequiredSignersHoldRecordWritesButNotUnsignedAcknowledgment()
    {
        using var estate = new TestEstate(3);
        string source = SeedFailure(estate);
        using EstateService service = EstateService.Open(estate.Root);
        WriteOutcome authority = service.DeviceCreate(new(estate.OperatorId, "Operator test device", "file"));
        Assert.True(authority.Ok, authority.Problem?.Message);
        Assert.True(service.DeviceAdmit(new(authority.Written!.Id, estate.OperatorId, "First authority.")).Ok);
        string person = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Phone reader","roles":["contributor"]}""").Id;
        var keys = new FileKeyStore(VericairnHome.Path()); // DaemonTestHome created the test process's isolated home.
        string device = Ulid.NewUlid(estate.Clock.AddSeconds(1)).Value;
        byte[] publicKey = keys.Create(device);
        try
        {
            var body = DeviceRecords.Body(person, "Presence fixture", "macos", SecureEnclaveCustody.Kind, RecordSigning.EncodePublicKey(publicKey), ["embedded", "local-network"], 3, "required");
            estate.Add("device", person, body.ToJsonString(), id: device, signer: new StoreSigner(keys, device));
            Assert.True(service.DeviceAdmit(new(device, estate.OperatorId, "Test admission.")).Ok);
            Assert.NotNull(service.PresenceRefusal(person));
            DeviceStanding standing = service.Standing(device)!;
            AttentionNotice notice = Assert.Single(service.AttentionGet(person).Items, i => i.SubjectId == source);
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/v1/attention/acknowledge";
            context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice(standing.Identity, person, standing.PublicKey, LocalNetwork: true);
            var scope = new RequestScope(service, new LocalNetworkLedger(estate.Root));
            int records = estate.Load().Records.Count;
            AttentionAcknowledgeRequest request = Request(person, notice);
            object? acknowledged = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ =>
                ValueTask.FromResult<object?>(Results.Json(service.AttentionAcknowledge(request), ProtocolJson.Options)));
            Assert.True(Assert.IsType<AttentionAcknowledgeResponse>(Assert.IsAssignableFrom<IValueHttpResult>(acknowledged).Value).Ok);
            context.Request.Path = "/v1/records";
            var record = new NewRecordRequest("work-order", person, JsonSerializer.Deserialize<JsonElement>("""{"key":"HELD","kind":"ordinary","title":"Held write","intent":"Confirm presence.","scope":["tests"],"exitQuestion":"Held?","inputs":[],"deliverables":["evidence"]}"""));
            object? denied = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, record), _ =>
                throw new InvalidOperationException("A held record write must not reach its handler."));
            Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
            Assert.Contains("presence", Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult>(denied).ResponseContent, StringComparison.Ordinal);
            LocalNetworkWrite kept = Assert.Single(LocalNetworkLedger.Read(estate.Root));
            Assert.Equal("written", kept.State);
            Assert.Null(kept.Record);
            Assert.Equal(records, estate.Load().Records.Count);
        }
        finally { keys.Remove(device); }
    }

    [Fact]
    public void ContractCoversPresentationWritesAndEveryInterfaceAdditionHasADefault()
    {
        Assert.True(RequestScope.Covers("GET /v1/attention"));
        Assert.True(RequestScope.Covers("POST /v1/attention/acknowledge"));
        Assert.True(RequestScope.Covers(typeof(AttentionAcknowledgeRequest)));
        foreach (string member in new[] { "AttentionGet", "AttentionGetAsync", "AttentionAcknowledge", "AttentionAcknowledgeAsync" })
            Assert.False(typeof(IEstateApi).GetMethod(member)!.IsAbstract);
        using JsonDocument api = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement post = api.RootElement.GetProperty("paths").GetProperty("/v1/attention/acknowledge").GetProperty("post");
        Assert.Contains("AttentionAcknowledgeRequest", post.GetProperty("requestBody").GetRawText(), StringComparison.Ordinal);
        Assert.Contains("AttentionAcknowledgeResponse", post.GetProperty("responses").GetProperty("200").GetRawText(), StringComparison.Ordinal);
        Assert.Contains("never resolves or hides", post.GetProperty("responses").GetProperty("200").GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("written", api.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("AttentionAcknowledgeResponse").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyAttentionIsOptionalForExistingClientsAndDescribesNoWrite()
    {
        var ordinary = new AttentionNotice("information", "Subject", "Inspect it.", "open", "2026-11-01T12:00:00Z",
            "Open the subject.", "work-order", "subject", "revision");
        string legacy = JsonSerializer.Serialize(ordinary, ProtocolJson.Options);
        using JsonDocument notice = JsonDocument.Parse(legacy);
        Assert.False(notice.RootElement.TryGetProperty("policy", out _));
        Assert.Null(JsonSerializer.Deserialize<AttentionNotice>(legacy, ProtocolJson.Options)!.Policy);
        using JsonDocument api = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement property = api.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("AttentionNotice").GetProperty("properties").GetProperty("policy");
        Assert.True(property.GetProperty("nullable").GetBoolean());
        Assert.Equal("object", property.GetProperty("properties").GetProperty("finding").GetProperty("type").GetString());
        Assert.Contains("no approval", property.GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealRoutesAndBothSdkBridgesKeepTheSameUnsignedState()
    {
        using var estate = new TestEstate(3);
        string source = SeedFailure(estate);
        using EstateService service = EstateService.Open(estate.Root);
        using var http = new HttpClient(new PolicyAdoptionApiTests.RouteHandler(service)) { BaseAddress = new Uri("http://fixture.invalid") };
        using var sdk = new VericairnClient(http);
        AttentionNotice notice = Assert.Single(sdk.AttentionGet(estate.OperatorId).Items, i => i.SubjectId == source);
        var request = Request(estate.OperatorId, notice);
        int before = estate.Load().Records.Count;
        AttentionAcknowledgeResponse response = await sdk.AttentionAcknowledgeAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.Ok, response.Problem?.Message);
        Assert.Equal(response, sdk.AttentionAcknowledge(request));
        var bound = new CancellationBoundApi(sdk, TestContext.Current.CancellationToken);
        Assert.Equal(response, bound.AttentionAcknowledge(request));
        Assert.Equal(response, await bound.AttentionAcknowledgeAsync(request, TestContext.Current.CancellationToken));
        AttentionResponse current = await bound.AttentionGetAsync(estate.OperatorId, TestContext.Current.CancellationToken);
        Assert.Equal("acknowledged", Assert.Single(current.Items, i => i.SubjectId == source).AttentionState);
        Assert.Equal("acknowledged", Assert.Single(bound.AttentionGet(estate.OperatorId).Items, i => i.SubjectId == source).AttentionState);
        Assert.Equal("unacknowledged", Assert.Single(sdk.AttentionGet(estate.BuilderId).Items, i => i.SubjectId == source).AttentionState);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, Assert.Throws<HttpRequestException>(() => sdk.AttentionGet("unknown-principal")).StatusCode);
        Assert.Equal(before, estate.Load().Records.Count);
    }

    [Fact]
    public async Task DeviceScopeRefusesImpersonationAndKeepsUnsignedWritesAndConflictsInTheLedger()
    {
        using var estate = new TestEstate(3);
        string source = SeedFailure(estate);
        using EstateService service = EstateService.Open(estate.Root);
        WriteOutcome authority = service.DeviceCreate(new(estate.OperatorId, "Operator test device", "file"));
        Assert.True(authority.Ok, authority.Problem?.Message);
        WriteOutcome first = service.DeviceAdmit(new(authority.Written!.Id, estate.OperatorId, "First authority."));
        Assert.True(first.Ok, first.Problem?.Message);
        WriteOutcome created = service.DeviceCreate(new(estate.BuilderId, "Paired test device", "file", ["local-network"]));
        Assert.True(created.Ok, created.Problem?.Message);
        Assert.True(service.DeviceAdmit(new(created.Written!.Id, estate.OperatorId, "Test access.")).Ok);
        DeviceStanding standing = service.Standing(created.Written.Id)!;
        AttentionNotice notice = Assert.Single(service.AttentionGet(estate.BuilderId).Items, i => i.SubjectId == source);
        var scope = new RequestScope(service, new LocalNetworkLedger(estate.Root));
        int before = estate.Load().Records.Count;
        var read = new DefaultHttpContext();
        read.Request.Method = "GET";
        read.Request.Path = "/v1/attention";
        read.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice(standing.Identity, standing.Principal, standing.PublicKey, LocalNetwork: true);
        object? refusedRead = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(read, estate.OperatorId), _ => throw new InvalidOperationException("A denied read must not reach its handler."));
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(refusedRead).StatusCode);
        async Task<object?> Send(AttentionAcknowledgeRequest request)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/v1/attention/acknowledge";
            context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice(standing.Identity, standing.Principal, standing.PublicKey, LocalNetwork: true);
            return await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ =>
                ValueTask.FromResult<object?>(Results.Json(service.AttentionAcknowledge(request), ProtocolJson.Options)));
        }
        object? denied = await Send(Request(estate.OperatorId, notice));
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
        Assert.Empty(LocalNetworkLedger.Read(estate.Root));
        object? accepted = await Send(Request(estate.BuilderId, notice));
        Assert.True(Assert.IsType<AttentionAcknowledgeResponse>(Assert.IsAssignableFrom<IValueHttpResult>(accepted).Value).Ok);
        LocalNetworkWrite kept = Assert.Single(LocalNetworkLedger.Read(estate.Root));
        Assert.Equal((standing.Identity, standing.Principal, "POST /v1/attention/acknowledge", "written"), (kept.Device, kept.Principal, kept.Operation, kept.State));
        Assert.Null(kept.Record);
        Assert.Null(kept.Hash);
        Assert.Null(kept.Path);
        _ = await Send(Request(estate.BuilderId, notice) with { Expected = "stale" });
        Assert.Equal("failed", LocalNetworkLedger.Read(estate.Root)[^1].State);
        Assert.Equal(before, estate.Load().Records.Count);
    }

    internal static AttentionAcknowledgeRequest Request(string principal, AttentionNotice item) => new(principal, item.Kind, item.SubjectType, item.SubjectId, item.Revision);

    internal static string SeedFailure(TestEstate estate)
    {
        var subject = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Widget","role":"source","location":{"github":"Example/widget"},"visibility":"private"}""");
        return estate.Add("verification", estate.BuilderId, """{"method":"test","commit":"0123456789abcdef0123456789abcdef01234567","environment":{"runner":"fixture"},"role":"automation","outcome":"failed","summary":"A check failed.","evidence":[],"limitations":[]}""", subject.Reference).Id;
    }
}
