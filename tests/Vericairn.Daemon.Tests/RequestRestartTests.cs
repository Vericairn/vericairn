// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public class RequestRestartTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ARequestAcceptedByOneInstanceIsRefusedAfterRestart(bool localNetwork, bool proof)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(systemClock: true);
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2? credential = localNetwork
            ? LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow)
            : null;
        string target = proof ? RuntimeIdentityScheme.Path + "?nonce=" + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)) : "/v1/status";
        string at = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
        byte[] request = Signed(harness, signer, target, at, localNetwork);
        string identity = harness.Host.IdentityPublicKey!;

        Assert.StartsWith("HTTP/1.1 200", await SendAsync(request), StringComparison.Ordinal);
        await harness.RestartAsync();
        Assert.Equal(identity, harness.Host.IdentityPublicKey);
        string replay = await SendAsync(request);
        Assert.StartsWith("HTTP/1.1 401", replay, StringComparison.Ordinal);
        Assert.Contains("earlier than this runtime instance", replay, StringComparison.Ordinal);
        Assert.Contains("sign the request afresh", replay, StringComparison.Ordinal);
        Assert.StartsWith("HTTP/1.1 200", await SendAsync(Signed(harness, signer, target, RequestSigning.FormatTime(DateTimeOffset.UtcNow), localNetwork)), StringComparison.Ordinal);

        // The existing SDK signs each send, including the local-network identity proof; no client change is needed.
        using var sdk = localNetwork
            ? VericairnClient.LocalNetwork(connection with { Address = harness.Address }, signer)
            : VericairnClient.Loopback(harness.Host.Endpoint, signer);
        Assert.Equal(harness.Host.Service.Health().Estate, sdk.Health().Estate);

        Task<string> SendAsync(byte[] payload) => Raw.ExchangeAsync(
            localNetwork ? harness.Address : $"{harness.Host.Endpoint.Loopback!.Host}:{harness.Host.Endpoint.Loopback.Port}",
            credential, harness.Host.Endpoint.Loopback!.Certificate, payload, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheCutoffIsNotRoundedDownAndTimesTooFarAheadAreRefused(bool localNetwork)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        DateTimeOffset second = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var time = new ManualTime(second);
        var first = new RequestAuthenticator(harness.Host.Service, 10, () => harness.Host.IdentityPublicKey, time);
        first.StartAccepting();
        string at = RequestSigning.FormatTime(second);
        string signature = Base64Url.EncodeToString(signer.Sign(Digest(harness, "/v1/status", at, localNetwork)));
        Assert.Null(await VerifyAsync(first, at, signature));

        // A restart within the very same second must not accept the signature the previous instance accepted.
        time.Advance(TimeSpan.FromMilliseconds(100));
        var restarted = new RequestAuthenticator(harness.Host.Service, 10, () => harness.Host.IdentityPublicKey, time);
        restarted.StartAccepting();
        Assert.Contains("sign the request afresh", (await VerifyAsync(restarted, at, signature))!.Message, StringComparison.Ordinal);

        // A client clock may run up to five seconds fast; a time further ahead is refused until the runtime's clock comes within that lead.
        string ahead = RequestSigning.FormatTime(second.AddSeconds(3));
        Assert.Null(await VerifyAsync(restarted, ahead, Base64Url.EncodeToString(signer.Sign(Digest(harness, "/v1/status", ahead, localNetwork)))));
        string future = RequestSigning.FormatTime(second.AddSeconds(7));
        string freshSignature = Base64Url.EncodeToString(signer.Sign(Digest(harness, "/v1/status", future, localNetwork)));
        Assert.Contains("ahead of the runtime's clock", (await VerifyAsync(restarted, future, freshSignature))!.Message, StringComparison.Ordinal);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(await VerifyAsync(restarted, future, freshSignature));

        // Lag is permitted only after startup, up to the existing five-minute age bound.
        time.Advance(TimeSpan.FromMinutes(4));
        Assert.Null(await VerifyAsync(restarted, future, Base64Url.EncodeToString(signer.Sign(Digest(harness, "/v1/status", future, localNetwork)))));
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Contains("five minutes", (await VerifyAsync(restarted, future, freshSignature))!.Message, StringComparison.Ordinal);

        Task<Problem?> VerifyAsync(RequestAuthenticator authenticator, string signedAt, string signed)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "GET";
            context.Request.Path = "/v1/status";
            context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/v1/status";
            context.Request.Body = new MemoryStream();
            context.Request.Headers[RequestSigning.DeviceHeader] = signer.DeviceId;
            context.Request.Headers[RequestSigning.SignedAtHeader] = signedAt;
            context.Request.Headers[RequestSigning.SignatureHeader] = signed;
            context.Connection.ClientCertificate = credential;
            return localNetwork ? authenticator.VerifyLocalNetworkAsync(context) : authenticator.VerifyAsync(context);
        }
    }

    private static byte[] Digest(LocalNetworkHarness harness, string target, string at, bool localNetwork) =>
        localNetwork ? harness.Digest("GET", target, at, []) : RequestSigning.Digest("GET", target, at, []);

    private static byte[] Signed(LocalNetworkHarness harness, DeviceRequestSigner signer, string target, string at, bool localNetwork) =>
        Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\nHost: x\r\nConnection: close\r\n{RequestSigning.DeviceHeader}: {signer.DeviceId}\r\n{RequestSigning.SignedAtHeader}: {at}\r\n{RequestSigning.SignatureHeader}: {Base64Url.EncodeToString(signer.Sign(Digest(harness, target, at, localNetwork)))}\r\n\r\n");
}
