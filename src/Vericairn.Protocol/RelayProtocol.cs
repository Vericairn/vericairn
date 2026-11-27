// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>A route supplied by the runtime over an authenticated connection.</summary>
public sealed record RelayRoute(string Url, RelayTicket Ticket);

/// <summary>A registration-key signature granting one device access to one relay address until expiry.</summary>
public sealed record RelayTicket(string Address, string Device, long Issued, long Expires, long Counter, string Signature);

/// <summary>The control messages exchanged before an opaque relay stream starts.</summary>
public sealed record RelayMessage(string Kind, string? Address = null, string? PublicKey = null, string? Nonce = null,
    string? Signature = null, RelayTicket? Ticket = null, string? Device = null, long Counter = 0);

/// <summary>Wire encoding of the relay's control channel. The end-to-end transport following it is opaque.</summary>
public static class RelayProtocol
{
    /// <summary>The most a control message can contain.</summary>
    public const int MessageLimit = 4096;

    /// <summary>The runtime path at which an authenticated device refreshes its own ticket.</summary>
    public const string TicketPath = "/v1/relay/ticket";

    /// <summary>Registration and ticket deadlines are expressed in whole seconds.</summary>
    public const int Lifetime = 24 * 60 * 60;

    /// <summary>Generates a relay address or challenge, independent of an estate or a device.</summary>
    public static string RandomId() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Checks the canonical encoding of a relay address or challenge.</summary>
    public static bool IsId(string? value) => IsEncoding(value, 32);

    /// <summary>Checks the canonical encoding of a P-256 public key and whether it imports successfully.</summary>
    public static bool IsKey(string? value)
    {
        if (!IsEncoding(value, 91))
        {
            return false;
        }

        using ECDsa verifier = ECDsa.Create();
        try
        {
            byte[] bytes = Base64Url.DecodeFromChars(value);
            verifier.ImportSubjectPublicKeyInfo(bytes, out int read);
            return read == bytes.Length && verifier.ExportParameters(false).Curve.Oid.Value == "1.2.840.10045.3.1.7";
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Reads an HTTPS relay URL, with no credentials or query parameters.</summary>
    public static bool TryUrl(string? text, out Uri? url)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? parsed) && parsed.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrEmpty(parsed.Host) && parsed.Port > 0 && parsed.PathAndQuery == "/"
            && parsed.UserInfo.Length == 0 && parsed.Fragment.Length == 0)
        {
            url = parsed;
            return true;
        }

        url = null;
        return false;
    }

    /// <summary>Creates a ticket for a new counter. The payload is bound to the registration by its address and signing key.</summary>
    public static RelayTicket Issue(ECDsa key, string address, string device, long now, long counter)
    {
        var ticket = new RelayTicket(address, device, now, checked(now + Lifetime), counter, "");
        return ticket with { Signature = Sign(key, TicketBytes(ticket)) };
    }

    /// <summary>Checks a ticket's scope, time, counter and registration-key signature.</summary>
    public static bool Verify(RelayTicket? ticket, string address, string publicKey, long now)
    {
        return ticket is not null && IsId(ticket.Address) && ticket.Address == address && IsDevice(ticket.Device)
            && ticket.Counter > 0 && ticket.Issued >= Math.Max(0, now - Lifetime) && ticket.Issued <= now
            && ticket.Expires > now && ticket.Expires - ticket.Issued <= Lifetime
            && Verify(publicKey, TicketBytes(ticket), ticket.Signature);
    }

    /// <summary>A relay sees the paired device's opaque record identifier.</summary>
    public static bool IsDevice(string? value) => value is { Length: 26 } && value.All(c => PairingProtocol.Alphabet.Contains(c, StringComparison.Ordinal));

    /// <summary>Signs a fresh relay challenge, with a domain distinct from tickets.</summary>
    public static string Prove(ECDsa key, string address, string nonce) => Sign(key, RegistrationBytes(address, nonce));

    /// <summary>Verifies a registration without any estate key.</summary>
    public static bool VerifyProof(string address, string nonce, string publicKey, string? signature) =>
        IsId(address) && IsId(nonce) && Verify(publicKey, RegistrationBytes(address, nonce), signature);

    /// <summary>Opens an authenticated outer TLS websocket to a relay.</summary>
    public static async Task<ClientWebSocket> ConnectAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(15);
        try
        {
            await socket.ConnectAsync(new UriBuilder(url) { Scheme = "wss", Port = url.Port, Path = "/v1/relay" }.Uri, cancellationToken).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Writes exactly one bounded control message.</summary>
    public static async Task SendAsync(WebSocket socket, RelayMessage message, CancellationToken cancellationToken)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJson.Options);
        if (bytes.Length > MessageLimit)
        {
            throw new IOException("The relay control message exceeds its limit.");
        }

        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one bounded control message. Binary messages are accepted only once a stream is joined.</summary>
    public static async Task<RelayMessage?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        byte[] bytes = new byte[MessageLimit];
        int length = 0;
        while (length < bytes.Length)
        {
            ValueWebSocketReceiveResult read = await socket.ReceiveAsync(bytes.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (read.MessageType != WebSocketMessageType.Text)
            {
                throw new IOException("Expected a relay control message.");
            }

            length += read.Count;
            if (read.EndOfMessage)
            {
                return JsonSerializer.Deserialize<RelayMessage>(bytes.AsSpan(0, length), ProtocolJson.Options);
            }
        }

        throw new IOException("The relay control message exceeds its limit.");
    }

    private static bool IsEncoding(string? text, int length) =>
        text?.Length == ((length * 8) + 5) / 6 && Base64Url.IsValid(text)
        && Base64Url.EncodeToString(Base64Url.DecodeFromChars(text)) == text;

    private static byte[] RegistrationBytes(string address, string nonce) =>
        Encoding.ASCII.GetBytes($"vericairn-relay/1 registration\n{address}\n{nonce}");

    private static byte[] TicketBytes(RelayTicket ticket) =>
        Encoding.ASCII.GetBytes(FormattableString.Invariant($"vericairn-relay/1 ticket\n{ticket.Address}\n{ticket.Device}\n{ticket.Issued}\n{ticket.Expires}\n{ticket.Counter}"));

    private static string Sign(ECDsa key, byte[] bytes) => Base64Url.EncodeToString(key.SignData(bytes, HashAlgorithmName.SHA256));

    private static bool Verify(string key, byte[] bytes, string? signature)
    {
        if (!IsEncoding(signature, 64) || !IsEncoding(key, 91))
        {
            return false;
        }

        using ECDsa verifier = ECDsa.Create();
        try
        {
            byte[] spki = Base64Url.DecodeFromChars(key);
            verifier.ImportSubjectPublicKeyInfo(spki, out int read);
            return read == spki.Length && verifier.ExportParameters(false).Curve.Oid.Value == "1.2.840.10045.3.1.7"
                && verifier.VerifyData(bytes, Base64Url.DecodeFromChars(signature), HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

/// <summary>A duplex stream that exchanges opaque bytes in binary websocket messages.</summary>
public sealed class RelayStream(WebSocket socket) : Stream
{
    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <inheritdoc />
    public override void Flush() { }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (!buffer.IsEmpty)
        {
            ValueWebSocketReceiveResult read = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read.MessageType == WebSocketMessageType.Close)
            {
                return 0;
            }

            if (read.MessageType != WebSocketMessageType.Binary)
            {
                throw new IOException("Expected bytes on the joined relay stream.");
            }

            if (read.Count != 0)
            {
                return read.Count;
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        socket.SendAsync(buffer, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            socket.Dispose();
        }

        base.Dispose(disposing);
    }
}
