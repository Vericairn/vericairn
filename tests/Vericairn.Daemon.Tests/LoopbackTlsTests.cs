// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Vericairn.Protocol;

namespace Vericairn.Daemon.Tests;

/// <summary>Loopback accepts TLS 1.2 alone, exercised by the same real handshake offers as the local-network profile.</summary>
[Collection("daemon")]
public class LoopbackTlsTests
{
    [Theory]
#pragma warning disable SYSLIB0039, CA5397 // Obsolete protocols are offered only to prove their refusal.
    [InlineData(SslProtocols.Tls)]
    [InlineData(SslProtocols.Tls11)]
#pragma warning restore SYSLIB0039, CA5397
    [InlineData(SslProtocols.Tls13)]
    public async Task OffersWithoutTls12AreRefused(SslProtocols protocols)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        // Where the platform cannot offer a version, it refuses the offer itself, as in the local-network tests.
        await Assert.ThrowsAnyAsync<Exception>(async () => await (await HandshakeAsync(harness.Host.Endpoint.Loopback!, protocols)).DisposeAsync());
    }

    [Theory]
    [InlineData(SslProtocols.None)]
    [InlineData(SslProtocols.Tls12)]
    public async Task CompatibleOffersNegotiateTls12(SslProtocols protocols)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        await using SslStream tls = await HandshakeAsync(harness.Host.Endpoint.Loopback!, protocols);
        Assert.Equal(SslProtocols.Tls12, tls.SslProtocol);
    }

    private static async Task<SslStream> HandshakeAsync(LoopbackEndpoint endpoint, SslProtocols protocols)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        SslStream? tls = null;
        try
        {
            await socket.ConnectAsync(endpoint.Host, endpoint.Port, deadline.Token);
            tls = new SslStream(new NetworkStream(socket, ownsSocket: true));
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "vericairn-runtime",
                AllowTlsResume = false,
                EnabledSslProtocols = protocols,
                RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is not null && RuntimeIdentityScheme.CertificateFingerprint(certificate.GetRawCertData()) == endpoint.Certificate,
            }, deadline.Token);
            return tls;
        }
        catch
        {
            if (tls is not null)
            {
                await tls.DisposeAsync();
            }
            else
            {
                socket.Dispose();
            }

            throw;
        }
    }
}
