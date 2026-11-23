// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// From 0.9.5 (FIX-164): an Android client of the local network. .NET 10's Android TLS takes no cipher-suite list, and when given one
/// it refuses it only after making its native stream, whose release then ends the process; so an Android client offers none, keeps
/// every other option, refuses before the handshake an option that would end the process, reports a TLS setup failure as a refused
/// handshake, and refuses a handshake that ended on a suite outside the local network's list before it sends anything. The runtime
/// chooses from its own list. The Android option set is made here by naming the platform, so it runs on every test machine.
/// </summary>
[Collection("daemon")]
public sealed class LocalNetworkAndroidTlsTests : IDisposable
{
    private static readonly RemoteCertificateValidationCallback Accept = (_, certificate, _, _) => certificate is not null;
    private readonly string scratch = Directory.CreateTempSubdirectory("vericairn-android-tls-").FullName;

    public LocalNetworkAndroidTlsTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public void AnAndroidClientOffersNoCipherSuiteListAndKeepsEveryOtherOption()
    {
        Assert.Equal(OperatingSystem.IsWindows() ? LocalNetworkTls.Platform.Windows : LocalNetworkTls.Platform.Listed, LocalNetworkTls.Current);
        Assert.False(LocalNetworkTls.OffersCipherSuites(LocalNetworkTls.Platform.Android));
        Assert.False(LocalNetworkTls.OffersCipherSuites(LocalNetworkTls.Platform.Windows));
        Assert.True(LocalNetworkTls.OffersCipherSuites(LocalNetworkTls.Platform.Listed));
        Assert.True(LocalNetworkTls.ChecksNegotiatedSuite(LocalNetworkTls.Platform.Android));
        Assert.False(LocalNetworkTls.ChecksNegotiatedSuite(LocalNetworkTls.Platform.Windows));
        Assert.False(LocalNetworkTls.ChecksNegotiatedSuite(LocalNetworkTls.Platform.Listed));
        Assert.Equal([TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384, TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256], TlsScreenStream.AllowedSuites);

        using X509Certificate2 own = RuntimeCertificate.Load(Path.Combine(scratch, "device"), "vericairn device");
        foreach (LocalNetworkTls.Platform platform in Enum.GetValues<LocalNetworkTls.Platform>())
        {
            foreach (X509Certificate2? client in new[] { null, own })
            {
                SslClientAuthenticationOptions options = LocalNetworkTls.ClientOptions(platform, Accept, client);
                Assert.Equal("vericairn-runtime", options.TargetHost);
                Assert.Equal(SslProtocols.Tls12, options.EnabledSslProtocols);
                Assert.Equal([SslApplicationProtocol.Http11], options.ApplicationProtocols);
                Assert.False(options.AllowTlsResume);
                Assert.False(options.AllowRenegotiation);
                Assert.Equal(X509RevocationMode.NoCheck, options.CertificateRevocationCheckMode);
                Assert.True(options.CertificateChainPolicy!.DisableCertificateDownloads);
                Assert.Equal(X509RevocationMode.NoCheck, options.CertificateChainPolicy.RevocationMode);
                Assert.Equal(EncryptionPolicy.RequireEncryption, options.EncryptionPolicy);
                Assert.Same(Accept, options.RemoteCertificateValidationCallback);
                Assert.Same(client, options.LocalCertificateSelectionCallback?.Invoke(this, "vericairn-runtime", new X509CertificateCollection(), null, []));
                if (platform == LocalNetworkTls.Platform.Listed)
                {
                    Assert.Same(TlsScreenStream.CipherSuites, options.CipherSuitesPolicy);
                }
                else
                {
                    Assert.Null(options.CipherSuitesPolicy);
                }
            }
        }

        // The runtime and every client off Windows and Android apply the list; Windows' TLS takes none.
        if (OperatingSystem.IsWindows())
        {
            Assert.Null(TlsScreenStream.CipherSuites);
        }
        else
        {
            Assert.Equal(TlsScreenStream.AllowedSuites, TlsScreenStream.CipherSuites!.AllowedCipherSuites);
        }
    }

    [Fact]
    public async Task OnAndroidAnOptionItsTlsWouldRefuseAfterMakingItsStreamNeverReachesIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            SslClientAuthenticationOptions listed = LocalNetworkTls.ClientOptions(LocalNetworkTls.Platform.Android, Accept);
            listed.CipherSuitesPolicy = new CipherSuitesPolicy(TlsScreenStream.AllowedSuites);
            await Refused(listed, "cipher-suite list");
        }

        SslClientAuthenticationOptions unencrypted = LocalNetworkTls.ClientOptions(LocalNetworkTls.Platform.Android, Accept);
#pragma warning disable SYSLIB0040 // The obsolete policy is the point: Android's TLS refuses it after making its stream.
        unencrypted.EncryptionPolicy = EncryptionPolicy.NoEncryption;
#pragma warning restore SYSLIB0040
        await Refused(unencrypted, "requires encryption");

        static async Task Refused(SslClientAuthenticationOptions options, string reason)
        {
            using var underneath = new CountingStream();
            using var tls = new SslStream(underneath, leaveInnerStreamOpen: true);
            AuthenticationException refused = await Assert.ThrowsAsync<AuthenticationException>(() =>
                LocalNetworkTls.AuthenticateAsync(tls, options, LocalNetworkTls.Platform.Android, TestContext.Current.CancellationToken));
            Assert.Contains(reason, refused.Message, StringComparison.Ordinal);
            Assert.Equal(0, underneath.Touched);
        }
    }

    [Fact]
    public async Task ATlsSetupFailureIsARefusedHandshakeNeverAnUnexpectedException()
    {
        // A stream the platform's TLS cannot write its hello to fails the setup with an exception TLS does not expect.
        using var refusing = new CountingStream { RefuseWrites = true };
        using var tls = new SslStream(refusing, leaveInnerStreamOpen: true);
        AuthenticationException refused = await Assert.ThrowsAsync<AuthenticationException>(() => LocalNetworkTls.AuthenticateAsync(
            tls, LocalNetworkTls.ClientOptions(LocalNetworkTls.Platform.Android, Accept), LocalNetworkTls.Platform.Android, TestContext.Current.CancellationToken));
        Assert.StartsWith("This device's TLS could not set up a connection to the runtime: ", refused.Message, StringComparison.Ordinal);
        Assert.IsType<NotSupportedException>(refused.InnerException);
        Assert.Equal("refused", Vericairn.Protocol.ClientFailure.FromException(refused)!.Code);
    }

    [Fact]
    public async Task AnAndroidClientPairsAndConnectsAndTheRuntimeChoosesFromItsOwnList()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        string code = harness.Issue(harness.Estate.BuilderId).Code;
        RuntimeConnection connection = await harness.Connections.AddAsync(harness.Address, code,
            new PairingOptions("Android phone") { Tls = LocalNetworkTls.Platform.Android }, TestContext.Current.CancellationToken);
        Assert.Equal(harness.Estate.Estate.Id, connection.Estate);

        // Mutual TLS with the device's credential, then the runtime identity's proof, with the Android option set.
        DeviceRequestSigner signer = harness.Signer(connection);
        using var credential = new LocalNetworkCredential(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey));
        await using Stream stream = await LocalNetworkTransport.ConnectAsync(connection, signer, credential, TestContext.Current.CancellationToken, platform: LocalNetworkTls.Platform.Android);
        var tls = Assert.IsType<SslStream>(stream);
        Assert.Equal(SslProtocols.Tls12, tls.SslProtocol);
        Assert.Contains(tls.NegotiatedCipherSuite, TlsScreenStream.AllowedSuites);
        Assert.Equal(SslApplicationProtocol.Http11, tls.NegotiatedApplicationProtocol);
        Assert.True(tls.IsMutuallyAuthenticated);
    }

    [Fact]
    public async Task AnAndroidClientRefusesASuiteOutsideTheLocalNetworksListBeforeSendingAnything()
    {
        // Windows' TLS takes no list from an application, so no server here can be made to choose another suite.
        if (OperatingSystem.IsWindows()) return;

        using X509Certificate2 certificate = RuntimeCertificate.Load(Path.Combine(scratch, "stranger"));
        // Suites outside the local network's list, each with ECDSA, as a stranger posing as the runtime could choose.
        var outside = new CipherSuitesPolicy([TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256, TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256]);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new MemoryStream();
        Task server = Task.Run(async () =>
        {
            using Socket accepted = await listener.AcceptSocketAsync(TestContext.Current.CancellationToken);
            await using var tls = new SslStream(new NetworkStream(accepted, ownsSocket: false));
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                EnabledSslProtocols = SslProtocols.Tls12,
                CipherSuitesPolicy = outside,
            }, TestContext.Current.CancellationToken);
            try
            {
                await tls.CopyToAsync(received, TestContext.Current.CancellationToken);
            }
            catch (IOException)
            {
                // The client closes without a TLS close notification.
            }
        }, TestContext.Current.CancellationToken);

        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, port, TestContext.Current.CancellationToken);
        AuthenticationException refused;
        await using (var client = new SslStream(new TlsScreenStream(new NetworkStream(socket, ownsSocket: false)), leaveInnerStreamOpen: false))
        {
            refused = await Assert.ThrowsAsync<AuthenticationException>(() => LocalNetworkTls.AuthenticateAsync(
                client, LocalNetworkTls.ClientOptions(LocalNetworkTls.Platform.Android, Accept), LocalNetworkTls.Platform.Android, TestContext.Current.CancellationToken));
        }

        socket.Dispose();
        await server.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        Assert.Contains("which the local network does not allow", refused.Message, StringComparison.Ordinal);
        Assert.True(refused.Message.Contains("CHACHA20", StringComparison.Ordinal) || refused.Message.Contains("AES_128_CBC", StringComparison.Ordinal), refused.Message);
        Assert.Equal(0, received.Length);
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

    /// <summary>A stream that counts every read and write, has nothing to read, and refuses writes when told to.</summary>
    private sealed class CountingStream : Stream
    {
        public int Touched { get; private set; }

        public bool RefuseWrites { get; init; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => Touched++;

        public override int Read(byte[] buffer, int offset, int count)
        {
            Touched++;
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Touched++;
            if (RefuseWrites) throw new NotSupportedException("This stream takes no writes.");
        }
    }
}
