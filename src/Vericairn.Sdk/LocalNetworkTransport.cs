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
    private const int MaxHeader = 16 * 1024;
    private const int MaxBody = 64 * 1024;

    public static async ValueTask<Stream> ConnectAsync(RuntimeConnection connection, IRequestSigner signer, LocalNetworkCredential credential, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        SslStream? tls = null;
        try
        {
            await socket.ConnectAsync(connection.Host, connection.Port, cancellationToken).ConfigureAwait(false);
            tls = new SslStream(new NetworkStream(socket, ownsSocket: true), leaveInnerStreamOpen: false);
            X509Certificate2 own = credential.Current();
            byte[]? presented = null;
            await tls.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = "vericairn-runtime",
                    AllowTlsResume = false,
                    ApplicationProtocols = [SslApplicationProtocol.Http11],
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    LocalCertificateSelectionCallback = (_, _, _, _, _) => own,

                    // Any certificate completes the handshake: the runtime identity's proof that follows decides whether it is the runtime's.
                    RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    {
                        presented = certificate?.GetRawCertData();
                        return certificate is not null;
                    },
                },
                cancellationToken).ConfigureAwait(false);
            await ProveAsync(tls, connection, signer, RuntimeIdentityScheme.CertificateFingerprint(presented ?? []), cancellationToken).ConfigureAwait(false);
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

    /// <summary>Asks for the runtime identity's proof of this connection's certificate with a fresh nonce, and verifies it with the pinned identity.</summary>
    private static async Task ProveAsync(SslStream tls, RuntimeConnection connection, IRequestSigner signer, string certificate, CancellationToken cancellationToken)
    {
        string nonce = PairingProtocol.NewNonce();
        string target = RuntimeIdentityScheme.Path + "?nonce=" + nonce;
        string signedAt = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
        string signature = Base64Url.EncodeToString(signer.Sign(RequestSigning.Digest("GET", target, signedAt, [])));
        string request = $"GET {target} HTTP/1.1\r\nHost: {connection.Address}\r\n{RequestSigning.DeviceHeader}: {signer.DeviceId}\r\n{RequestSigning.SignedAtHeader}: {signedAt}\r\n{RequestSigning.SignatureHeader}: {signature}\r\n\r\n";
        await tls.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken).ConfigureAwait(false);
        await tls.FlushAsync(cancellationToken).ConfigureAwait(false);
        (int status, byte[] body) = await ReadResponseAsync(tls, cancellationToken).ConfigureAwait(false);
        if (status == 401)
        {
            throw new RuntimeConnectionException(ConnectionStates.Denied, ProblemMessage(body) ?? $"The runtime at {connection.Address} refused device {signer.DeviceId}.");
        }

        if (status != 200)
        {
            throw new RuntimeConnectionException(ConnectionStates.Unavailable, $"The runtime at {connection.Address} answered {status} instead of proving its identity{(ProblemMessage(body) is string said ? ": " + said : ".")}");
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

        if (!RuntimeIdentityScheme.Verify(proof, connection.Identity, connection.Estate, certificate, nonce))
        {
            throw new RuntimeConnectionException(ConnectionStates.Unavailable, $"What answered at {connection.Address} did not prove the runtime identity this device paired with ({RuntimeIdentityScheme.Fingerprint(connection.Identity)}) for the certificate it presented, so nothing else was sent to it. If the runtime replaced its identity, this device pairs again.");
        }
    }

    private static string? ProblemMessage(byte[] body)
    {
        try
        {
            return body.Length == 0 ? null : JsonSerializer.Deserialize<Problem>(body, ProtocolJson.Options)?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads one HTTP/1.1 response exactly, so the connection is left clean for the requests that follow.</summary>
    private static async Task<(int Status, byte[] Body)> ReadResponseAsync(Stream stream, CancellationToken cancellationToken)
    {
        string head = await ReadHeadAsync(stream, cancellationToken).ConfigureAwait(false);
        string[] lines = head.Split("\r\n");
        string[] statusLine = lines[0].Split(' ', 3);
        if (statusLine.Length < 2 || !statusLine[0].StartsWith("HTTP/1.", StringComparison.Ordinal) || !int.TryParse(statusLine[1], NumberStyles.None, CultureInfo.InvariantCulture, out int status))
        {
            throw new RuntimeConnectionException(ConnectionStates.Unavailable, "The runtime's answer is not HTTP/1.1.");
        }

        long? length = null;
        bool chunked = false;
        foreach (string line in lines.Skip(1))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            string name = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed))
            {
                length = parsed;
            }
            else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) && value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            {
                chunked = true;
            }
        }

        if (chunked)
        {
            using var body = new MemoryStream();
            while (true)
            {
                string size = (await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false)).Split(';')[0].Trim();
                if (!int.TryParse(size, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int chunk) || chunk < 0 || body.Length + chunk > MaxBody)
                {
                    throw new RuntimeConnectionException(ConnectionStates.Unavailable, "The runtime's answer is malformed.");
                }

                if (chunk == 0)
                {
                    while ((await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false)).Length > 0)
                    {
                        // Trailers are ignored.
                    }

                    return (status, body.ToArray());
                }

                body.Write(await ReadExactlyAsync(stream, chunk, cancellationToken).ConfigureAwait(false));
                await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }

        if (length is null)
        {
            return (status, []);
        }

        if (length > MaxBody)
        {
            throw new RuntimeConnectionException(ConnectionStates.Unavailable, "The runtime's answer is too long for an identity proof.");
        }

        return (status, await ReadExactlyAsync(stream, (int)length.Value, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<string> ReadHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var head = new List<byte>(512);
        byte[] one = new byte[1];
        while (head.Count < MaxHeader)
        {
            if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new RuntimeConnectionException(ConnectionStates.Unavailable, "The runtime closed the connection before answering.");
            }

            head.Add(one[0]);
            int n = head.Count;
            if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n')
            {
                return Encoding.ASCII.GetString(head.ToArray(), 0, n - 4);
            }
        }

        throw new RuntimeConnectionException(ConnectionStates.Unavailable, "The runtime's answer has no end of its headers.");
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var line = new List<byte>(16);
        byte[] one = new byte[1];
        while (line.Count < MaxHeader)
        {
            if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new RuntimeConnectionException(ConnectionStates.Unavailable, "The runtime closed the connection while answering.");
            }

            if (one[0] == '\n')
            {
                return Encoding.ASCII.GetString(line.ToArray()).TrimEnd('\r');
            }

            line.Add(one[0]);
        }

        throw new RuntimeConnectionException(ConnectionStates.Unavailable, "The runtime's answer is malformed.");
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        byte[] bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }
}
