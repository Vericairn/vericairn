// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public class TlsHandshakeLogTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedHandshakesReachTheDaemonLog(bool localNetwork, bool rejectServerCertificate)
    {
        var messages = new ConcurrentQueue<string>();
        var logged = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(log: message =>
        {
            messages.Enqueue(message);
            if (message.StartsWith("TLS handshake failed: ", StringComparison.Ordinal))
            {
                logged.TrySetResult(message);
            }
        });
        X509Certificate2 certificate = harness.Host.Certificates!.Current;
        if (rejectServerCertificate)
        {
            // Force the server-side validation failure from FIX-21 without relying on the race. The certificate on disk
            // and the client are untouched; only Kestrel's view of this server certificate's EKU is made unsuitable.
            certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single()
                .CopyFrom(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.2")], false));
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync("127.0.0.1", localNetwork ? harness.Host.Endpoint.LocalNetwork!.Port : harness.Host.Endpoint.Loopback!.Port, deadline.Token);
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        if (rejectServerCertificate)
        {
            await using var tls = new SslStream(stream, leaveInnerStreamOpen: true);
            await Assert.ThrowsAnyAsync<IOException>(() => tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "vericairn-runtime",
                AllowTlsResume = false,
            }, deadline.Token));
        }
        else
        {
            // A TLS handshake_failure alert before ClientHello: Kestrel handles this exception internally, so an
            // outer connection catch alone cannot report it. No certificate or request is sent by this peer.
            await stream.WriteAsync(new byte[] { 21, 3, 3, 0, 2, 2, 40 }, deadline.Token);
            socket.Shutdown(SocketShutdown.Send);
            var buffer = new byte[256];
            while (await stream.ReadAsync(buffer, deadline.Token) != 0)
            {
                // Consume the server's alert, if any, through connection closure.
            }
        }

        string message = await logged.Task.WaitAsync(deadline.Token);
        if (rejectServerCertificate)
        {
            Assert.StartsWith($"TLS handshake failed: System.InvalidOperationException: Certificate {certificate.Thumbprint} cannot be used as an SSL server certificate.", message, StringComparison.Ordinal);
            Assert.Contains("usages do not include Server Authentication", message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Matches(@"^TLS handshake failed: System\.(Security\.Authentication\.AuthenticationException|IO\.IOException): .+$", message);
        }

        Assert.DoesNotContain('\n', message);
        Assert.DoesNotContain('\r', message);
        Assert.DoesNotContain(harness.Estate.Root, message, StringComparison.Ordinal);
        Assert.Single(messages, item => item.StartsWith("TLS handshake failed: ", StringComparison.Ordinal));
    }
}
