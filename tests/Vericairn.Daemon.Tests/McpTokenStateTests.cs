// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon.Tests;

public sealed class McpTokenStateTests
{
    private const string Issuer = "https://runtime.example";
    private const string Resource = Issuer + "/mcp";
    private const string Client = "https://client.example/client.json";
    private const string Redirect = "https://client.example/return?profile=work";

    [Fact]
    public void AccessIsBoundToTheResourceAndExpiresAfterFifteenMinutes()
    {
        using var scratch = new Scratch();
        var clock = new TestClock();
        var store = new McpTokenStore(scratch.Path, clock);
        McpTokenGrant grant = Grant(clock);
        McpTokenResponse issued = Assert.IsType<McpTokenResponse>(store.Create(grant));
        Assert.Equal(900, issued.ExpiresIn);
        McpTokenAccess access = Assert.IsType<McpTokenAccess>(store.Authenticate(issued.AccessToken, Resource));
        Assert.Equal(grant, access.Grant);
        Assert.Null(store.Authenticate(issued.AccessToken, Issuer + "/another-resource"));
        Assert.Null(store.RefreshGrant(issued.RefreshToken, "another-client", Resource));

        clock.Advance(TimeSpan.FromMinutes(14));
        Assert.True(store.Current(access));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(store.Current(access));
        Assert.Null(store.Authenticate(issued.AccessToken, Resource));

        clock.Advance(TimeSpan.FromDays(90));
        Assert.Null(store.RefreshGrant(issued.RefreshToken, Client, Resource));
    }

    [Fact]
    public void RefreshRotationPersistsOnlyDigestsAndAReusedRefreshEndsItsFamily()
    {
        using var scratch = new Scratch();
        var clock = new TestClock();
        var store = new McpTokenStore(scratch.Path, clock);
        McpTokenGrant grant = Grant(clock);
        McpTokenResponse issued = Assert.IsType<McpTokenResponse>(store.Create(grant));
        McpTokenRefresh original = Assert.IsType<McpTokenRefresh>(store.RefreshGrant(issued.RefreshToken, Client, Resource));
        McpTokenResponse rotated = Assert.IsType<McpTokenResponse>(store.Rotate(original).Response);
        Assert.NotEqual(issued.RefreshToken, rotated.RefreshToken);

        string persisted = File.ReadAllText(System.IO.Path.Combine(scratch.Path, McpTokenStore.FileName));
        foreach (string secret in new[] { issued.AccessToken, issued.RefreshToken, rotated.AccessToken, rotated.RefreshToken })
        {
            Assert.False(persisted.Contains(secret, StringComparison.Ordinal));
            Assert.Contains(McpTokenStore.Hash(secret), persisted, StringComparison.Ordinal);
        }

        store = new McpTokenStore(scratch.Path, clock);
        McpTokenAccess access = Assert.IsType<McpTokenAccess>(store.Authenticate(rotated.AccessToken, Resource));
        McpTokenRefresh reuse = Assert.IsType<McpTokenRefresh>(store.RefreshGrant(issued.RefreshToken, Client, Resource));
        Assert.Equal((null, true), store.Rotate(reuse));
        Assert.False(store.Current(access));
        Assert.Null(store.Authenticate(rotated.AccessToken, Resource));
        Assert.Null(store.RefreshGrant(rotated.RefreshToken, Client, Resource));
        store = new McpTokenStore(scratch.Path, clock);
        Assert.Null(store.Authenticate(issued.AccessToken, Resource));
    }

    [Fact]
    public void RenewalReplacesTheGrantForOnlyTheNamedDevice()
    {
        using var scratch = new Scratch();
        var clock = new TestClock();
        var store = new McpTokenStore(scratch.Path, clock);
        McpTokenGrant grant = Grant(clock);
        McpTokenResponse first = Assert.IsType<McpTokenResponse>(store.Create(grant));
        McpTokenResponse other = Assert.IsType<McpTokenResponse>(store.Create(Grant(clock)));
        McpTokenAccess active = Assert.IsType<McpTokenAccess>(store.Authenticate(first.AccessToken, Resource));
        clock.Advance(TimeSpan.FromMinutes(1));
        McpTokenGrant renewed = grant with { Scope = "read", ExpiresAt = clock.GetUtcNow().AddDays(1) };
        McpTokenResponse replacement = Assert.IsType<McpTokenResponse>(store.Create(renewed));

        Assert.False(store.Current(active));
        Assert.Null(store.RefreshGrant(first.RefreshToken, Client, Resource));
        Assert.Equal(renewed, Assert.IsType<McpTokenAccess>(store.Authenticate(replacement.AccessToken, Resource)).Grant);
        Assert.NotNull(store.Authenticate(other.AccessToken, Resource));
    }

    [Fact]
    public void AdministratorApprovalExpiresAndCanBeProvedOnlyOnce()
    {
        var clock = new TestClock();
        var codes = new McpApprovalCodes(clock);
        McpTokenGrant grant = Grant(clock);
        var issuer = new PairingAuthority(grant.By, Ulid.NewUlid().Value, Ulid.NewUlid().Value, Ulid.NewUlid().Value);
        var approval = new McpApproval(grant.Principal, issuer, grant.Scope, grant.ExpiresAt, null);

        McpApprovalIssued single = Assert.IsType<McpApprovalIssued>(codes.Issue(approval, Resource));
        Assert.Equal(approval, codes.Prove(single.Code));
        Assert.Null(codes.Prove(single.Code));

        McpApprovalIssued expired = Assert.IsType<McpApprovalIssued>(codes.Issue(approval, Resource));
        clock.Advance(PairingRegistry.Lifetime);
        Assert.Null(codes.Prove(expired.Code));
    }

    [Fact]
    public async Task CodeExchangeChecksItsBindingAndAuthorizationResponsesIdentifyTheIssuer()
    {
        var clock = new TestClock();
        var server = new McpBrowserAuthorization(clock, Issuer);
        McpTokenGrant grant = Grant(clock);
        string verifier = McpTokenStore.RandomValue(32);
        var client = new McpClientMetadata(Client, "Test client", [Redirect], null);
        var request = new McpAuthorizationRequest(client, Redirect, McpTokenStore.Hash(verifier), grant.Scope, "test-state");
        string code = Assert.IsType<string>(server.Complete(request, grant));
        Assert.Null(server.Exchange(code, "different-client", Redirect, Resource, verifier));
        Assert.Null(server.Exchange(code, Client, Redirect, Resource, McpTokenStore.RandomValue(32)));
        Assert.Equal(grant, server.Exchange(code, Client, Redirect, Resource, verifier));
        Assert.Null(server.Exchange(code, Client, Redirect, Resource, verifier));

        foreach (string? error in new[] { null, "access_denied" })
        {
            var http = new DefaultHttpContext();
            await server.RedirectAsync(http, request, error is null ? code : null, error);
            Assert.Equal(StatusCodes.Status302Found, http.Response.StatusCode);
            var values = QueryHelpers.ParseQuery(new Uri(http.Response.Headers.Location.ToString()).Query);
            Assert.Equal(Issuer, values["iss"].ToString());
            Assert.Equal("test-state", values["state"].ToString());
            Assert.Equal("work", values["profile"].ToString());
            Assert.Equal(error is null ? code : error, values[error is null ? "code" : "error"].ToString());
        }
    }

    [Fact]
    public void MetadataUsesTheExactHttpsIdentifierAndDynamicRegistrationNamesItsApplicationType()
    {
        static JsonElement Document(string? id, string? application) => JsonSerializer.SerializeToElement(new
        {
            client_id = id,
            client_name = "Test client",
            redirect_uris = new[] { Redirect },
            application_type = application,
        });

        Assert.True(McpClientMetadata.IsDocumentId(Client));
        Assert.True(McpClientMetadata.IsDocumentId("https://client.example:8443/client.json"));
        Assert.False(McpClientMetadata.IsDocumentId("https://client.example/registration/../client.json"));
        Assert.False(McpClientMetadata.IsDocumentId("https://client.example/registration/%2e/client.json"));
        Assert.False(McpClientMetadata.IsDocumentId("https://client.example"));
        Assert.False(McpClientMetadata.IsDocumentId("http://client.example/client.json"));
        Assert.False(McpClientMetadata.IsPublic(IPAddress.Loopback));
        Assert.False(McpClientMetadata.IsPublic(IPAddress.IPv6Loopback));

        McpClientMetadata metadata = Assert.IsType<McpClientMetadata>(McpClientMetadata.Parse(Document(Client, null), Client, dynamic: false));
        Assert.Equal((Client, Redirect), (metadata.Id, Assert.Single(metadata.Redirects)));
        Assert.Null(McpClientMetadata.Parse(Document(Client + ".other", null), Client, dynamic: false));
        Assert.Null(McpClientMetadata.Parse(Document(null, null), Client, dynamic: true));
        Assert.Equal("web", McpClientMetadata.Parse(Document(null, "web"), Client, dynamic: true)?.ApplicationType);
    }

    [Fact]
    public void TokenDeviceRecordsItsProfileAndPriorGrantStaysWithdrawnAfterReadmission()
    {
        DaemonTestHome.Ensure();
        using var estate = new TestEstate(recordFormat: 3);
        using var scratch = new Scratch();
        using EstateService service = EstateService.Open(estate.Root);
        var custody = new FileKeyStore(scratch.Path);
        WriteOutcome administrator = service.DeviceCreate(new DeviceCreateRequest(estate.OperatorId, "Test administrator", "file"));
        Assert.True(administrator.Ok, administrator.Problem?.Message);
        WriteOutcome first = service.DeviceAdmit(new AdmitRequest(administrator.Written!.Id, estate.OperatorId, "Test first authority."));
        Assert.True(first.Ok, first.Problem?.Message);

        Assert.Null(service.McpTokenIssuer(estate.OperatorId, estate.OperatorId).Authority);
        Assert.Null(service.McpTokenIssuer(estate.BuilderId, estate.BuilderId).Authority);
        (PairingAuthority? approval, string? issueFailure) = service.McpTokenIssuer(estate.BuilderId, estate.OperatorId);
        Assert.True(approval is not null, issueFailure);
        (McpTokenDevice? enrolled, Problem? problem) = service.CreateMcpTokenDevice(estate.BuilderId, approval!, custody);
        Assert.True(enrolled is not null, problem?.Message);
        McpTokenDevice device = enrolled!;
        Assert.True(custody.Has(device.Device));
        Assert.Equal(device, service.CurrentMcpTokenDevice(device.Device, device.Admission));

        var model = estate.Load();
        RecordEnvelope record = model.ById[device.Device];
        Assert.Equal("none", EstateModel.BodyString(record, "presence"));
        Assert.Equal("token", Assert.Single(record.Body.GetProperty("profiles").EnumerateArray()).GetString());
        Assert.Equal(device.Device, Assert.Single(record.Signatures).Device);
        Assert.Equal(administrator.Written.Id, Assert.Single(model.ById[device.Admission].Signatures).Device);
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));

        WriteOutcome revoked = service.DeviceRevoke(new RevokeRequest(device.Device, estate.OperatorId, "Retire the original grant."));
        Assert.True(revoked.Ok, revoked.Problem?.Message);
        Assert.Null(service.CurrentMcpTokenDevice(device.Device, device.Admission));
        WriteOutcome readmitted = service.DeviceAdmit(new AdmitRequest(device.Device, estate.OperatorId, "Approve a future grant."));
        Assert.True(readmitted.Ok, readmitted.Problem?.Message);
        Assert.NotNull(service.CurrentMcpTokenDevice(device.Device));
        Assert.Null(service.CurrentMcpTokenDevice(device.Device, device.Admission));
    }

    private static McpTokenGrant Grant(TimeProvider clock) =>
        new(Ulid.NewUlid().Value, Ulid.NewUlid().Value, "test-public-key", Ulid.NewUlid().Value, Ulid.NewUlid().Value, Client, Resource,
            "contribute", clock.GetUtcNow().AddDays(90));

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch.AddYears(50);

        public override DateTimeOffset GetUtcNow() => now;

        internal void Advance(TimeSpan by) => now += by;
    }

    private sealed class Scratch : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("vc-mcp-token-test-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
