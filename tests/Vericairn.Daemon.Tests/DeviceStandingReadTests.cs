// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// Request authentication reads a device's standing from a model kept for the estate's records as they are: requests that prove
/// nothing never make the runtime read the records again or wait for its operations, and a revocation recorded by another runtime
/// still counts from the next request.
/// </summary>
[Collection("daemon")]
public class DeviceStandingReadTests
{
    [Fact]
    public async Task RequestsThatProveNothingNeverReadTheRecordsAgain()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        EstateService service = harness.Host.Service;
        Assert.Equal("admitted", service.Standing(harness.OperatorDevice)!.Admission);
        long loads = service.StandingLoads;
        using X509Certificate2 stranger = Stranger();
        for (int i = 0; i < 25; i++)
        {
            // An unknown device is refused before its body, and a known one whose signature does not verify after it.
            Assert.Equal("denied", (await harness.Host.Authenticator.VerifyAsync(Request(harness, Ulid.NewUlid().Value)))?.Code);
            Assert.Equal("denied", (await harness.Host.Authenticator.VerifyAsync(Request(harness, harness.OperatorDevice)))?.Code);
            DefaultHttpContext local = Request(harness, Ulid.NewUlid().Value);
            local.Connection.ClientCertificate = stranger;
            Assert.Equal("denied", (await harness.Host.Authenticator.VerifyLocalNetworkAsync(local))?.Code);
            Assert.Null(service.AdmittedDevicePublicKey(Ulid.NewUlid().Value));
        }

        Assert.Equal(loads, service.StandingLoads);
    }

    [Fact]
    public async Task AnUnchangedEstateIsReadWithoutWaitingForTheRuntimesOperations()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        EstateService service = harness.Host.Service;
        Assert.Equal("admitted", service.Standing(harness.OperatorDevice)!.Admission);
        long loads = service.StandingLoads;
        TaskCompletionSource signing = TestTiming.Signal();
        using var release = new ManualResetEventSlim();
        service.BeforeSigningRecord = () =>
        {
            service.BeforeSigningRecord = null;
            signing.TrySetResult();
            release.Wait(TestTiming.HangTimeout);
        };
        Task<WriteOutcome> write = Task.Run(() => harness.Socket.NewRecord(Order(harness, "W-HELD")), TestContext.Current.CancellationToken);
        try
        {
            await signing.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);

            // The write holds the runtime's gate while it signs: an operation that needs the gate waits for it.
            Task<HealthResponse> gated = Task.Run(service.Health, TestContext.Current.CancellationToken);
            await Task.WhenAny(gated, Task.Delay(200, TestContext.Current.CancellationToken));
            Assert.False(gated.IsCompleted);

            // Authentication does not: the records have not changed, so it answers from the model it keeps.
            Problem? refused = await harness.Host.Authenticator.VerifyAsync(Request(harness, Ulid.NewUlid().Value)).WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            Assert.Equal("denied", refused?.Code);
            Assert.Equal("admitted", await Task.Run(() => service.Standing(harness.OperatorDevice)!.Admission, TestContext.Current.CancellationToken).WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken));
            Assert.False(gated.IsCompleted);
        }
        finally
        {
            release.Set();
        }

        Assert.True((await write.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken)).Ok);
        Assert.Equal(loads, service.StandingLoads);
    }

    [Fact]
    public async Task ARevocationRecordedByAnotherRuntimeCountsFromTheNextRequest()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        EstateService service = harness.Host.Service;
        WriteOutcome created = harness.Socket.DeviceCreate(new DeviceCreateRequest(harness.Estate.BuilderId, "Standing test device", "file"));
        Assert.True(created.Ok, created.Problem?.Message);
        string device = created.Written!.Id;
        Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(device, harness.Estate.OperatorId, "A standing test device.")).Ok);
        DeviceRequestSigner signer = DeviceRequestSigner.Find(device)!;
        var time = new ManualTime(DateTimeOffset.UtcNow);
        var authenticator = new RequestAuthenticator(service, 100, () => null, time);
        authenticator.StartAccepting();
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Null(await authenticator.VerifyAsync(Signed(signer, time, "before")));
        long loads = service.StandingLoads;
        Assert.Null(await authenticator.VerifyAsync(Signed(signer, time, "again")));
        Assert.Equal(loads, service.StandingLoads);

        // Another runtime on the same working tree, with a home of its own, records the revocation: this one wrote nothing.
        string otherHome = Path.Combine(Path.GetTempPath(), "vericairn-other-runtime-" + Ulid.NewUlid().Value);
        try
        {
            using (EstateService other = EstateService.Open(harness.Estate.Root, null, otherHome))
            {
                WriteOutcome revoked = other.DeviceRevoke(new RevokeRequest(device, harness.Estate.OperatorId, "Revoked by another runtime."));
                Assert.True(revoked.Ok, revoked.Problem?.Message);
            }

            Problem? refused = await authenticator.VerifyAsync(Signed(signer, time, "after"));
            Assert.Equal("denied", refused?.Code);
            Assert.Contains("not admitted now", refused!.Message, StringComparison.Ordinal);
            Assert.Equal("revoked", service.Standing(device)!.Admission);
            Assert.Equal(loads + 1, service.StandingLoads);
        }
        finally
        {
            try
            {
                Directory.Delete(otherHome, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }

    private static DefaultHttpContext Request(LocalNetworkHarness harness, string device)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/objects";
        context.Request.Body = new MemoryStream(new byte[64]);
        context.Response.Body = new MemoryStream();
        context.Request.Headers[RequestSigning.DeviceHeader] = device;
        context.Request.Headers[RequestSigning.SignedAtHeader] = RequestSigning.FormatTime(harness.Time.GetUtcNow());
        context.Request.Headers[RequestSigning.SignatureHeader] = Base64Url.EncodeToString(new byte[64]);
        return context;
    }

    private static DefaultHttpContext Signed(DeviceRequestSigner signer, TimeProvider time, string text)
    {
        byte[] body = Encoding.UTF8.GetBytes(text);
        string signedAt = RequestSigning.FormatTime(time.GetUtcNow());
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/objects";
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/v1/objects";
        context.Request.ContentType = "text/plain";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();
        context.Request.Headers[RequestSigning.DeviceHeader] = signer.DeviceId;
        context.Request.Headers[RequestSigning.SignedAtHeader] = signedAt;
        context.Request.Headers[RequestSigning.SignatureHeader] = Base64Url.EncodeToString(signer.Sign(RequestSigning.Digest("POST", "/v1/objects", signedAt, body)));
        return context;
    }

    private static NewRecordRequest Order(LocalNetworkHarness harness, string key)
    {
        using System.Text.Json.JsonDocument body = System.Text.Json.JsonDocument.Parse("{\"key\":\"" + key + "\",\"kind\":\"ordinary\",\"title\":\"Held write\",\"intent\":\"Hold the gate.\",\"scope\":[\"test\"],\"exitQuestion\":\"Held?\",\"inputs\":[],\"deliverables\":[\"test\"]}");
        return new NewRecordRequest("work-order", harness.Estate.OperatorId, body.RootElement.Clone());
    }

    // A client certificate no device issued, so the request reaches the records' account of its device and is refused there.
    private static X509Certificate2 Stranger()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=stranger", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
    }
}
