// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics.Tracing;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>A test that counts the process's open TLS sessions runs alone, so that no other test's sessions move the count.</summary>
[CollectionDefinition("tls sessions", DisableParallelization = true)]
public sealed class TlsSessionsIsolation;

/// <summary>
/// From 0.9.5 (FIX-164): a pairing whose TLS handshake failed or was refused closes its TLS stream at once, and its connection,
/// leaving no TLS session open. The socket's <c>using</c> closes the connection whether or not the TLS stream is closed, so the
/// test counts the process's open TLS sessions with .NET's own telemetry, which a TLS stream leaves only when it is closed: by its
/// dispose, or else much later by its finalizer. A handshake refused after it completed is the case that leaves a session to close.
/// </summary>
[Collection("tls sessions")]
public sealed class FailedPairingTlsTests : IDisposable
{
    private readonly string scratch = Directory.CreateTempSubdirectory("vericairn-pairing-tls-").FullName;

    public FailedPairingTlsTests()
    {
        DaemonTestHome.Ensure();
    }

    [Theory]
    [InlineData("Android")]
    [InlineData("Windows")]
    public async Task ARefusedPairingHandshakeClosesItsTlsStreamAndConnectionAtOnce(string platformName)
    {
        LocalNetworkTls.Platform platform = Enum.Parse<LocalNetworkTls.Platform>(platformName);
        // Windows' TLS takes no list from an application, so no server on a Windows test machine can be made to choose another suite.
        if (OperatingSystem.IsWindows()) return;

        using X509Certificate2 certificate = RuntimeCertificate.Load(Path.Combine(scratch, "stranger"));
        using TlsSessions sessions = await TlsSessions.StartAsync();
        // TLS streams that earlier tests left to their finalizers are finalized first, so that none leaves the count during this one.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = await sessions.OpenAsync();

        // A stranger posing as the runtime completes the TLS handshake on a suite outside the local network's list, and the
        // client, which offered no list, refuses it.
        await using (var stranger = new LocalNetworkAndroidTlsTests.Stranger(certificate,
            new CipherSuitesPolicy([TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256, TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256])))
        {
            ClientRefusedException failure = await Assert.ThrowsAsync<ClientRefusedException>(() => RuntimePairing.PairAsync(stranger.Address, PairingProtocol.NewCode(),
                new PairingOptions("Phone") { Tls = platform, Timeout = TestTiming.HangTimeout }, TestContext.Current.CancellationToken));
            AuthenticationException refused = Assert.IsType<AuthenticationException>(failure.InnerException);
            Assert.Contains("which the local network does not allow, so this device sent it no request", refused.Message, StringComparison.Ordinal);

            // The connection: the stranger read to its end, so the client closed it, and it sent no request.
            Assert.Equal(0, await stranger.ReceivedAsync());
        }

        // The TLS stream: the stranger closed its own session before its read ended, so a session still open is the client's.
        Assert.Equal(before, await sessions.OpenAsync());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(scratch, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The process's open TLS sessions, as .NET's System.Net.Security telemetry counts them: a TLS stream joins the count when its
    /// handshake completes while the telemetry is listened to, and leaves it when it is closed.
    /// </summary>
    private sealed class TlsSessions : EventListener
    {
        // Initialized before the base constructor, which may already report the source.
        private readonly Channel<double> reports = Channel.CreateUnbounded<double>();

        private TlsSessions()
        {
        }

        /// <summary>
        /// Listens to the telemetry. Its source is made by the process's first TLS handshake, so one that fails at once, over a stream
        /// with nothing to read, makes it here if no test has; it opens no session.
        /// </summary>
        public static async Task<TlsSessions> StartAsync()
        {
            var sessions = new TlsSessions();
            await using var none = new SslStream(new MemoryStream(), leaveInnerStreamOpen: false);
            try
            {
                await none.AuthenticateAsClientAsync(LocalNetworkTls.TargetHost);
            }
            catch (Exception exception) when (exception is IOException or AuthenticationException)
            {
                // Expected: the stream ends before any answer.
            }

            return sessions;
        }

        /// <summary>The count read after this call: the second report from now, since the first may have been read before it.</summary>
        public async Task<long> OpenAsync()
        {
            while (reports.Reader.TryRead(out _))
            {
                // Reports read before this call.
            }

            await reports.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            return (long)await reports.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "System.Net.Security")
            {
                EnableEvents(eventSource, EventLevel.Informational, EventKeywords.All, new Dictionary<string, string?> { ["EventCounterIntervalSec"] = "1" });
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName == "EventCounters" && eventData.Payload is [IDictionary<string, object> counter]
                && counter.TryGetValue("Name", out object? name) && "all-tls-sessions-open".Equals(name) && counter.TryGetValue("Mean", out object? open) && open is double count)
            {
                reports.Writer.TryWrite(count);
            }
        }
    }
}
