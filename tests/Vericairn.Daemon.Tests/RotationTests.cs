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
/// The runtime certificate and its rotation (design/identity-and-connectivity.md § 6): on a schedule and on demand, paired devices
/// keep working without pairing again, since they pin the runtime identity, which signs the certificate in use; a rotation on
/// compromise closes open connections and leaves the old certificate useless to whoever holds its key.
/// </summary>
[Collection("daemon")]
public class RotationTests
{
    public RotationTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public async Task ARotationKeepsAPairedDeviceWorkingWithoutPairingAgain()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        using VericairnClient client = harness.Connections.Connect(connection);
        Assert.Equal(harness.Estate.Estate.Id, client.Health().Estate);
        string before = harness.Host.Endpoint.Loopback!.Certificate;

        (CertificateRotated? rotated, Problem? problem) = harness.Socket.RotateCertificate(new CertificateRotateRequest(Reason: "A test of the schedule's on-demand twin."));
        Assert.True(rotated is not null, problem?.Message);
        Assert.Equal(before, rotated.Previous);
        Assert.NotEqual(before, rotated.Certificate);
        Assert.False(rotated.Compromised);
        Assert.Equal(0, rotated.Closed);
        Assert.Null(rotated.Identity);
        Assert.Equal(rotated.Certificate, harness.Host.Endpoint.Loopback!.Certificate);
        Assert.Equal(rotated.Certificate, DaemonHost.Discover(harness.Estate.Root)!.Loopback!.Certificate);

        // The client keeps working, a new client is proved over the new certificate, and a check finds the connection current.
        Assert.Equal(harness.Estate.Estate.Id, client.Health().Estate);
        using (VericairnClient fresh = harness.Connections.Connect(connection))
        {
            Assert.Equal(harness.Estate.Estate.Id, fresh.Status().Json.GetProperty("estate").GetProperty("id").GetString());
        }

        Assert.Equal(ConnectionStates.Current, harness.Connections.Probe(connection).State);

        // New connections are shown the new certificate; the replaced one is no longer presented at all.
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        using (var current = new MutualTls(harness, credential, rotated.Certificate))
        {
            Assert.Equal(HttpStatusCode.OK, (await current.SendSignedAsync(signer, HttpMethod.Get, "/v1/health")).Status);
        }

        using (var old = new MutualTls(harness, credential, before))
        {
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => old.SendSignedAsync(signer, HttpMethod.Get, "/v1/health"));
        }

        // A loopback client reads the new fingerprint from the endpoint file, as every command does.
        using VericairnClient loopback = VericairnClient.Loopback(DaemonHost.Discover(harness.Estate.Root)!, DeviceRequestSigner.Find(harness.OperatorDevice));
        Assert.Equal(harness.Estate.Estate.Id, loopback.Health().Estate);
    }

    [Fact]
    public async Task TheCertificateIsReplacedOnItsScheduleBeforeItExpires()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        string before = harness.Host.Endpoint.Loopback!.Certificate;
        DateTimeOffset now = harness.Time.GetUtcNow();
        Assert.False(harness.Host.RotateIfDue(now));
        Assert.Equal(before, harness.Host.Endpoint.Loopback!.Certificate);

        // Within the renewal window before its expiry, the schedule replaces it.
        DateTimeOffset due = now + RuntimeCertificate.Validity - RuntimeCertificate.RenewBefore + TimeSpan.FromDays(1);
        Assert.True(harness.Host.RotateIfDue(due));
        Assert.NotEqual(before, harness.Host.Endpoint.Loopback!.Certificate);
        Assert.False(harness.Host.RotateIfDue(now));
        Assert.Equal(ConnectionStates.Current, harness.Connections.Probe(connection).State);
    }

    [Fact]
    public async Task ARotationOnCompromiseClosesOpenConnectionsAndLeavesTheOldCertificateUseless()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        string before = harness.Host.Endpoint.Loopback!.Certificate;
        using var open = new MutualTls(harness, credential, before);
        Assert.Equal(HttpStatusCode.OK, (await open.SendSignedAsync(signer, HttpMethod.Get, "/v1/health")).Status);

        // Someone copied the certificate and its key, as from the file beside the live store.
        string file = Path.Combine(Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!, RuntimeCertificate.FileName);
        byte[] stolen = File.ReadAllBytes(file);

        (CertificateRotated? rotated, Problem? problem) = harness.Socket.RotateCertificate(new CertificateRotateRequest(Compromised: true, Reason: "The certificate file was copied."));
        Assert.True(rotated is not null, problem?.Message);
        Assert.True(rotated.Compromised);
        Assert.True(rotated.Closed >= 1, $"closed {rotated.Closed}");
        Assert.NotEqual(stolen, File.ReadAllBytes(file));

        // The open connection was closed: nothing continues under the old certificate, which the runtime no longer presents.
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => open.SendSignedAsync(signer, HttpMethod.Get, "/v1/health"));

        // Whoever holds the old certificate and its key, and even relays the runtime's genuine proofs, cannot pass for the runtime:
        // every proof names the new certificate, never the old one a client would be shown.
        using VericairnClient genuine = harness.Connections.Connect(connection);
        using X509Certificate2 old = X509CertificateLoader.LoadPkcs12(stolen, null);
        await using var impostor = new Impostor(old, nonce => JsonSerializer.Serialize(genuine.RuntimeIdentity(nonce), ProtocolJson.Options));
        ConnectionStatus misled = harness.Connections.Probe(connection with { Id = Ulid.NewUlid().Value, Address = impostor.Address, LastCurrent = null }, TestTiming.HangTimeout);
        Assert.Equal(ConnectionStates.Unavailable, misled.State);
        Assert.Contains("did not prove the runtime identity", misled.Message, StringComparison.Ordinal);

        // The paired device itself keeps working with the runtime, without pairing again.
        Assert.Equal(ConnectionStates.Current, harness.Connections.Probe(connection).State);
    }

    [Fact]
    public async Task ReplacingTheRuntimeIdentityMakesEveryPairedDevicePairAgain()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        ConnectionStatus first = harness.Connections.Probe(harness.Pair(harness.Estate.BuilderId));
        Assert.Equal(ConnectionStates.Current, first.State);
        RuntimeConnection connection = first.Connection;
        string identity = harness.Host.IdentityPublicKey!;

        (CertificateRotated? rotated, Problem? problem) = harness.Socket.RotateCertificate(new CertificateRotateRequest(Reason: "The identity's key may be known.", Identity: true));
        Assert.True(rotated is not null, problem?.Message);
        Assert.True(rotated.Compromised);
        Assert.NotEqual(identity, harness.Host.IdentityPublicKey);
        Assert.Equal(RuntimeIdentityScheme.Fingerprint(harness.Host.IdentityPublicKey!), rotated.Identity);
        Assert.Equal(rotated.Identity, DaemonHost.Discover(harness.Estate.Root)!.LocalNetwork!.Identity);

        // The device pinned the old identity: it sends nothing to a runtime that cannot prove it, and shows what it last saw as cached.
        ConnectionStatus check = harness.Connections.Probe(connection);
        Assert.Equal(ConnectionStates.Cached, check.State);
        Assert.Contains("did not prove the runtime identity", check.Message, StringComparison.Ordinal);
        Assert.Contains("Last current at", check.Message, StringComparison.Ordinal);

        // Pairing again pins the new identity.
        RuntimeConnection again = harness.Pair(harness.Estate.BuilderId, "Phone, paired again");
        Assert.Equal(harness.Host.IdentityPublicKey, again.Identity);
        Assert.Equal(ConnectionStates.Current, harness.Connections.Probe(again).State);
    }
}
