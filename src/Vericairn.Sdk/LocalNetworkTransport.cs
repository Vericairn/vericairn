// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

/// <summary>
/// A connection to a runtime on the local network: TCP to the address, mutual TLS with the device's credential, and then, before
/// any other request, the runtime identity's proof over the certificate this handshake showed. A connection whose proof fails is
/// closed with nothing else sent.
/// </summary>
internal static class LocalNetworkTransport
{
    /// <summary>
    /// The chain policy of every TLS handshake the SDK makes with a runtime: nothing is fetched to build a chain and no revocation is
    /// checked, since the certificate is accepted by its fingerprint and the runtime identity's proof, never by a chain.
    /// </summary>
    public static X509ChainPolicy OfflineChainPolicy() => new() { DisableCertificateDownloads = true, RevocationMode = X509RevocationMode.NoCheck };

    public static async ValueTask<Stream> ConnectAsync(RuntimeConnection connection, IRequestSigner signer, LocalNetworkCredential credential, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        SslStream? tls = null;
        try
        {
            await socket.ConnectAsync(connection.Host, connection.Port, cancellationToken).ConfigureAwait(false);
            // The screen refuses a runtime certificate that names an address to fetch before this machine's TLS reads it.
            tls = new SslStream(new TlsScreenStream(new NetworkStream(socket, ownsSocket: true)), leaveInnerStreamOpen: false);
            X509Certificate2 own = credential.Current();
            byte[]? presented = null;
            await tls.AuthenticateAsClientAsync(
                CreateAuthenticationOptions(own, (_, certificate, _, _) =>
                {
                    presented = certificate?.GetRawCertData();
                    return certificate is not null;
                }),
                cancellationToken).ConfigureAwait(false);
            await ProveAsync(tls, connection, signer, RuntimeIdentityScheme.CertificateFingerprint(presented ?? []), RuntimeIdentityScheme.CertificateFingerprint(own.RawData), cancellationToken).ConfigureAwait(false);
            return tls;
        }
        catch
        {
            if (tls is not null)
            {
                await tls.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                socket.Dispose();
            }

            throw;
        }
    }

    private static SslClientAuthenticationOptions CreateAuthenticationOptions(X509Certificate2 own, RemoteCertificateValidationCallback validate) =>
        new SslClientAuthenticationOptions
        {
            TargetHost = "vericairn-runtime",
            AllowTlsResume = false,
            AllowRenegotiation = false,
            ApplicationProtocols = [SslApplicationProtocol.Http11],

            // TLS 1.2 only, so the runtime's certificate travels in the clear, where the screen reads it first.
            EnabledSslProtocols = TlsScreenStream.Protocols,
            CipherSuitesPolicy = TlsScreenStream.CipherSuites,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            CertificateChainPolicy = OfflineChainPolicy(),
            LocalCertificateSelectionCallback = (_, _, _, _, _) => own,

            // Any certificate completes the handshake: the runtime identity's proof that follows decides whether it is the runtime's.
            RemoteCertificateValidationCallback = validate,
        };

    /// <summary>Asks for the runtime identity's proof of this connection's certificates with a fresh nonce, and verifies it with the pinned identity.</summary>
    private static async Task ProveAsync(SslStream tls, RuntimeConnection connection, IRequestSigner signer, string certificate, string client, CancellationToken cancellationToken)
    {
        string nonce = PairingProtocol.NewNonce();
        string target = RuntimeIdentityScheme.Path + "?nonce=" + nonce;
        string signedAt = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
        // Signed as on loopback (version 1): nothing has proven yet which runtime answers, and the proof it asks for is bound to this
        // connection's own client certificate. Every request after the proof is signed for the identity it proved (version 2).
        string signature = Base64Url.EncodeToString(signer.Sign(RequestSigning.Digest("GET", target, signedAt, [])));
        string request = $"GET {target} HTTP/1.1\r\nHost: {connection.Address}\r\n{RequestSigning.DeviceHeader}: {signer.DeviceId}\r\n{RequestSigning.SignedAtHeader}: {signedAt}\r\n{RequestSigning.SignatureHeader}: {signature}\r\n\r\n";
        await tls.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken).ConfigureAwait(false);
        await tls.FlushAsync(cancellationToken).ConfigureAwait(false);
        (int status, byte[] body) = await RawHttp.ReadResponseAsync(tls, cancellationToken).ConfigureAwait(false);
        if (status == 401)
        {
            throw new RuntimeConnectionException(ConnectionStates.Denied, RawHttp.ProblemOf(body)?.Message ?? $"The runtime at {connection.Address} refused device {signer.DeviceId}.");
        }

        if (status != 200)
        {
            throw new RuntimeConnectionException(ConnectionStates.Unavailable, $"The runtime at {connection.Address} answered {status} instead of proving its identity{(RawHttp.ProblemOf(body)?.Message is string said ? ": " + said : ".")}");
        }

        RuntimeIdentityProof? proof;
        try
        {
            proof = JsonSerializer.Deserialize<RuntimeIdentityProof>(body, ProtocolJson.Options);
        }
        catch (JsonException)
        {
            proof = null;
        }

        if (!RuntimeIdentityScheme.Verify(proof, connection.Identity, connection.Estate, certificate, client, nonce))
        {
            throw new RuntimeConnectionException(ConnectionStates.Unavailable, $"What answered at {connection.Address} did not prove the runtime identity this device paired with ({RuntimeIdentityScheme.Fingerprint(connection.Identity)}) for the certificate it presented, so nothing else was sent to it. If the runtime replaced its identity, this device pairs again.");
        }
    }

}
