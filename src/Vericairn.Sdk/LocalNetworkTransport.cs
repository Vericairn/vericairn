// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
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

    public static async ValueTask<Stream> ConnectAsync(RuntimeConnection connection, IRequestSigner signer, LocalNetworkCredential credential, CancellationToken cancellationToken, bool classifyRefusal = false,
        LocalNetworkTls.Platform? platform = null, RelayRoute? route = null, Func<Uri, CancellationToken, Task<WebSocket>>? relayConnect = null)
    {
        LocalNetworkTls.Platform tlsPlatform = platform ?? LocalNetworkTls.Current;
        Stream? transport = null;
        SslStream? tls = null;
        TlsScreenStream? screen = null;
        try
        {
            transport = route is null
                ? await ConnectDirectAsync(connection, cancellationToken).ConfigureAwait(false)
                : await ConnectRelayAsync(connection, route, relayConnect, cancellationToken).ConfigureAwait(false);
            // The screen refuses a runtime certificate that names an address to fetch before this machine's TLS reads it.
            screen = new TlsScreenStream(transport);
            tls = new SslStream(screen, leaveInnerStreamOpen: false);
            X509Certificate2 own = credential.Current();
            byte[]? presented = null;
            // Any certificate completes the handshake: the runtime identity's proof that follows decides whether it is the runtime's.
            SslClientAuthenticationOptions options = LocalNetworkTls.ClientOptions(tlsPlatform, (_, certificate, _, _) =>
            {
                presented = certificate?.GetRawCertData();
                return certificate is not null;
            }, own);
            await LocalNetworkTls.AuthenticateAsync(tls, options, tlsPlatform, cancellationToken).ConfigureAwait(false);
            await ProveAsync(tls, connection, signer, RuntimeIdentityScheme.CertificateFingerprint(presented ?? []), RuntimeIdentityScheme.CertificateFingerprint(own.RawData), cancellationToken).ConfigureAwait(false);
            return tls;
        }
        catch (Exception exception)
        {
            if (tls is not null)
            {
                await tls.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                transport?.Dispose();
            }

            if (classifyRefusal && (screen?.RefusedPeer == true || exception is System.Security.Authentication.AuthenticationException or RuntimeConnectionException))
                throw new ClientRefusedException(exception.Message, exception);
            throw;
        }
    }

    private static async Task<Stream> ConnectDirectAsync(RuntimeConnection connection, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(connection.Host, connection.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<Stream> ConnectRelayAsync(RuntimeConnection connection, RelayRoute route, Func<Uri, CancellationToken, Task<WebSocket>>? connect, CancellationToken cancellationToken)
    {
        if (!RelayProtocol.TryUrl(route.Url, out Uri? url) || route.Ticket.Device != connection.Device)
        {
            throw new RuntimeConnectionException("The runtime's relay route is unavailable for this device.");
        }

        WebSocket socket = connect is null
            ? await RelayProtocol.ConnectAsync(url!, cancellationToken).ConfigureAwait(false)
            : await connect(url!, cancellationToken).ConfigureAwait(false);
        try
        {
            await RelayProtocol.SendAsync(socket, new("join", Ticket: route.Ticket), cancellationToken).ConfigureAwait(false);
            if (await RelayProtocol.ReceiveAsync(socket, cancellationToken).ConfigureAwait(false) is not { Kind: "joined" })
            {
                throw new RuntimeConnectionException("The relay refused the ticket or the runtime is unavailable.");
            }

            return new RelayStream(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

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
