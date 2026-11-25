// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Vericairn.Sdk;

/// <summary>
/// From 0.9.5 (FIX-164): the TLS options of the SDK's local-network clients, pairing's and a paired device's, made for the platform's
/// TLS, and the checks around their handshake. Every platform speaks TLS 1.2 alone (<see cref="TlsScreenStream.Protocols"/>), offers
/// ALPN http/1.1, names the runtime <see cref="TargetHost"/>, never resumes or renegotiates, and builds no chain online. The local
/// network's cipher suites are offered as a list only where .NET's TLS takes one from an application. Windows' does not, and puts them
/// first in its own order. Android's refuses one, and in .NET 10 it refuses it only after making its native TLS stream, whose release
/// then calls a null function pointer and ends the process; the same follows any option it refuses at that point. There the runtime
/// chooses from its own list, which it always applies where .NET lets it, and a client that offered no list, on Windows or Android,
/// refuses a handshake that ended on any other suite before it sends any request: the TLS 1.2 handshake itself, the client
/// certificate included, has completed by then.
/// </summary>
internal static class LocalNetworkTls
{
    /// <summary>The name every local-network client gives the runtime; its certificate is accepted by fingerprint and proof, never by name.</summary>
    public const string TargetHost = "vericairn-runtime";

    /// <summary>The TLS a client's options are made for.</summary>
    public enum Platform
    {
        /// <summary>Linux, macOS and iOS, whose .NET TLS takes the local network's cipher-suite list.</summary>
        Listed,

        /// <summary>Windows, whose TLS takes no list from an application and puts the local network's suites first in its own order.</summary>
        Windows,

        /// <summary>Android, whose .NET TLS takes no list and ends the process when given one.</summary>
        Android,
    }

    /// <summary>The TLS of the platform this process runs on.</summary>
    public static Platform Current { get; } = OperatingSystem.IsAndroid() ? Platform.Android : OperatingSystem.IsWindows() ? Platform.Windows : Platform.Listed;

    /// <summary>Whether a client on <paramref name="platform"/> offers the local network's cipher suites as a list.</summary>
    public static bool OffersCipherSuites(Platform platform) => platform == Platform.Listed;

    /// <summary>Whether a client on <paramref name="platform"/> checks the suite its handshake ended on before it sends any request: wherever it offered no list.</summary>
    public static bool ChecksNegotiatedSuite(Platform platform) => !OffersCipherSuites(platform);

    /// <summary>
    /// The options of a local-network client on <paramref name="platform"/>: <paramref name="validate"/> sees the runtime's certificate,
    /// and <paramref name="own"/>, when given, is the client certificate a paired device presents.
    /// </summary>
    public static SslClientAuthenticationOptions ClientOptions(Platform platform, RemoteCertificateValidationCallback validate, X509Certificate2? own = null)
    {
        ArgumentNullException.ThrowIfNull(validate);
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = TargetHost,
            AllowTlsResume = false,
            AllowRenegotiation = false,
            ApplicationProtocols = [SslApplicationProtocol.Http11],

            // TLS 1.2 only, so the runtime's certificate travels in the clear, where the screen reads it first.
            EnabledSslProtocols = TlsScreenStream.Protocols,
            CipherSuitesPolicy = OffersCipherSuites(platform) ? TlsScreenStream.CipherSuites : null,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            CertificateChainPolicy = LocalNetworkTransport.OfflineChainPolicy(),

            // The caller decides what the certificate is worth: pairing's MAC covers it, and a paired device asks the identity's proof.
            RemoteCertificateValidationCallback = validate,
        };
        if (own is not null)
        {
            options.LocalCertificateSelectionCallback = (_, _, _, _, _) => own;
        }

        return options;
    }

    /// <summary>
    /// Authenticates as a local-network client. An option the platform's TLS refuses after making its native stream is refused here
    /// first; whatever else keeps the platform's TLS from setting up the connection is an <see cref="AuthenticationException"/>, as a
    /// refused handshake is; and on a platform that offered no list, a handshake that ended on a suite outside the local network's is
    /// refused before any request is sent.
    /// </summary>
    public static async Task AuthenticateAsync(SslStream tls, SslClientAuthenticationOptions options, Platform platform, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tls);
        ArgumentNullException.ThrowIfNull(options);
        Refuse(platform, options);
        try
        {
            await tls.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or IOException or SocketException or AuthenticationException or OutOfMemoryException))
        {
            throw new AuthenticationException($"This device's TLS could not set up a connection to the runtime: {exception.Message}", exception);
        }

        if (ChecksNegotiatedSuite(platform) && !TlsScreenStream.AllowedSuites.Contains(tls.NegotiatedCipherSuite))
        {
            throw new AuthenticationException($"The other end chose the cipher suite {tls.NegotiatedCipherSuite}, which the local network does not allow, so this device sent it no request.");
        }
    }

    /// <summary>
    /// On Android, .NET 10's TLS makes its native stream before it reads the options, and an option it refuses then leaves a stream
    /// whose release ends the process: a cipher-suite list, or an encryption policy other than requiring encryption. Neither reaches it.
    /// </summary>
    private static void Refuse(Platform platform, SslClientAuthenticationOptions options)
    {
        if (platform != Platform.Android)
        {
            return;
        }

        if (options.CipherSuitesPolicy is not null)
        {
            throw new AuthenticationException("This device's TLS takes no cipher-suite list on Android, so none is offered; the runtime chooses from its own.");
        }

        if (options.EncryptionPolicy != EncryptionPolicy.RequireEncryption)
        {
            throw new AuthenticationException("This device's TLS requires encryption on Android, and the local network never goes without it.");
        }
    }
}
