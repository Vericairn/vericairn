// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// Just enough of a TLS 1.2 client (RFC 5246, RFC 5289, RFC 5746) to hold a handshake with the runtime's local-network listener and
/// then send, inside the encrypted channel, what a client renegotiating to present a certificate sends, which no platform's TLS lets
/// an application send: ECDHE on P-256, ECDSA, AES-128-GCM with SHA-256, and no client certificate in the first handshake, as the
/// pairing exchange allows. The server's Finished is verified, so a handshake that did not happen fails here. Test code only.
/// </summary>
internal sealed class Tls12Probe : IDisposable
{
    private const byte ChangeCipherSpec = 20;
    private const byte Alert = 21;
    private const byte Handshake = 22;

    private readonly TcpClient client = new() { NoDelay = true };
    private readonly List<byte> transcript = [];
    private readonly ECDiffieHellman ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] clientRandom = RandomNumberGenerator.GetBytes(32);
    private readonly List<byte> pending = [];
    private NetworkStream? stream;
    private byte[] serverRandom = [];
    private byte[] clientKey = [];
    private byte[] serverKey = [];
    private byte[] clientIv = [];
    private byte[] serverIv = [];
    private ulong clientSequence;
    private ulong serverSequence;

    /// <summary>The client's Finished verify data, which a renegotiating ClientHello carries.</summary>
    public byte[] ClientVerifyData { get; private set; } = [];

    /// <summary>Whether the server's hello offered secure renegotiation (the renegotiation_info extension).</summary>
    public bool SecureRenegotiation { get; private set; }

    /// <summary>Connects and holds the first handshake to its end, the server's Finished verified.</summary>
    public static async Task<Tls12Probe> ConnectAsync(string host, int port)
    {
        var probe = new Tls12Probe();
        await probe.client.ConnectAsync(host, port);
        probe.stream = probe.client.GetStream();
        await probe.HandshakeAsync();
        return probe;
    }

    /// <summary>
    /// Sends, sealed, a new ClientHello as a renegotiating client does, then lists the types of the records the server answers with
    /// until it closes the connection or the wait ends. A handshake record (22) among them is a new handshake under way.
    /// </summary>
    public async Task<IReadOnlyList<byte>> RenegotiateAsync(TimeSpan wait)
    {
        await WriteSealedAsync(Handshake, ClientHello(RandomNumberGenerator.GetBytes(32), ClientVerifyData));
        var answered = new List<byte>();
        using var timeout = new CancellationTokenSource(wait);
        try
        {
            while (true)
            {
                (byte type, _) = await ReadRecordAsync(timeout.Token);
                answered.Add(type);
            }
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or OperationCanceledException or SocketException)
        {
        }

        return answered;
    }

    /// <summary>Sends a record sealed under the session's keys, as the rest of a renegotiation would be; false when the server has closed.</summary>
    public async Task<bool> TrySendSealedAsync(byte type, byte[] plaintext)
    {
        try
        {
            await WriteSealedAsync(type, plaintext);
            return true;
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>A TLS 1.2 Certificate message holding one certificate.</summary>
    public static byte[] CertificateMessage(byte[] der) => [11, .. Length24(6 + der.Length), .. Length24(3 + der.Length), .. Length24(der.Length), .. der];

    public void Dispose()
    {
        ecdh.Dispose();
        client.Dispose();
    }

    private static byte[] Length24(int length) => [(byte)(length >> 16), (byte)(length >> 8), (byte)length];

    private static byte[] Length16(int length) => [(byte)(length >> 8), (byte)length];

    private static byte[] Extension(int type, byte[] data) => [.. Length16(type), .. Length16(data.Length), .. data];

    private static byte[] Record(byte type, byte[] fragment) => [type, 3, 3, .. Length16(fragment.Length), .. fragment];

    /// <summary>The TLS 1.2 PRF with SHA-256 (RFC 5246 § 5).</summary>
    private static byte[] Prf(byte[] secret, string label, byte[] seed, int length)
    {
        byte[] labelSeed = [.. Encoding.ASCII.GetBytes(label), .. seed];
        var output = new List<byte>();
        byte[] a = labelSeed;
        while (output.Count < length)
        {
            a = HMACSHA256.HashData(secret, a);
            byte[] block = [.. a, .. labelSeed];
            output.AddRange(HMACSHA256.HashData(secret, block));
        }

        return [.. output.Take(length)];
    }

    private static byte[] ClientHello(byte[] random, byte[] renegotiated)
    {
        byte[] name = Encoding.ASCII.GetBytes("vericairn-runtime");
        byte[] extensions =
        [
            .. Extension(0x0000, [.. Length16(name.Length + 3), 0, .. Length16(name.Length), .. name]),
            .. Extension(0x000a, [0, 2, 0x00, 0x17]),
            .. Extension(0x000b, [1, 0]),
            .. Extension(0x000d, [0, 8, 0x04, 0x03, 0x05, 0x03, 0x08, 0x04, 0x04, 0x01]),
            .. Extension(0xff01, [(byte)renegotiated.Length, .. renegotiated]),
        ];

        // TLS 1.2, the random, no session, TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256 only, no compression, the extensions.
        byte[] body = [3, 3, .. random, 0, 0, 2, 0xC0, 0x2B, 1, 0, .. Length16(extensions.Length), .. extensions];
        return [1, .. Length24(body.Length), .. body];
    }

    private async Task HandshakeAsync()
    {
        byte[] hello = ClientHello(clientRandom, []);
        transcript.AddRange(hello);
        await WritePlainAsync(Handshake, hello);

        // ServerHello, Certificate, ServerKeyExchange, CertificateRequest, ServerHelloDone.
        byte[]? serverPoint = null;
        bool done = false;
        while (!done)
        {
            foreach (byte[] message in await ReadMessagesAsync())
            {
                transcript.AddRange(message);
                switch (message[0])
                {
                    case 2:
                        ServerHello(message);
                        break;
                    case 12:
                        // named_curve (3), secp256r1, then the public point.
                        if (message[4] != 3 || message[5] != 0x00 || message[6] != 0x17)
                        {
                            throw new IOException("The server's key exchange is not on P-256.");
                        }

                        serverPoint = message[8..(8 + message[7])];
                        break;
                    case 14:
                        done = true;
                        break;
                }
            }
        }

        // No certificate, then the key exchange, the change of cipher spec and the Finished, sealed.
        ECParameters own = ecdh.ExportParameters(false);
        byte[] point = [4, .. own.Q.X!, .. own.Q.Y!];
        byte[] certificate = [11, 0, 0, 3, 0, 0, 0];
        byte[] keyExchange = [16, .. Length24(1 + point.Length), (byte)point.Length, .. point];
        transcript.AddRange(certificate);
        transcript.AddRange(keyExchange);
        await WritePlainAsync(Handshake, [.. certificate, .. keyExchange]);

        using ECDiffieHellman server = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = serverPoint![1..33], Y = serverPoint[33..65] } });
        byte[] premaster = ecdh.DeriveRawSecretAgreement(server.PublicKey);
        byte[] master = Prf(premaster, "master secret", [.. clientRandom, .. serverRandom], 48);
        byte[] block = Prf(master, "key expansion", [.. serverRandom, .. clientRandom], 40);
        clientKey = block[0..16];
        serverKey = block[16..32];
        clientIv = block[32..36];
        serverIv = block[36..40];

        await WritePlainAsync(ChangeCipherSpec, [1]);
        ClientVerifyData = Prf(master, "client finished", SHA256.HashData([.. transcript]), 12);
        byte[] finished = [20, 0, 0, 12, .. ClientVerifyData];
        transcript.AddRange(finished);
        await WriteSealedAsync(Handshake, finished);

        (byte type, byte[] fragment) = await ReadRecordAsync(CancellationToken.None);
        if (type != ChangeCipherSpec)
        {
            throw new IOException($"The server answered the client's Finished with a record of type {type}, not a change of cipher spec.");
        }

        (type, fragment) = await ReadRecordAsync(CancellationToken.None);
        byte[] serverFinished = Open(type, fragment);
        byte[] expected = Prf(master, "server finished", SHA256.HashData([.. transcript]), 12);
        if (type != Handshake || serverFinished[0] != 20 || !serverFinished.AsSpan(4).SequenceEqual(expected))
        {
            throw new IOException("The server's Finished does not verify: no handshake was held.");
        }
    }

    private void ServerHello(byte[] message)
    {
        ReadOnlySpan<byte> body = message.AsSpan(4);
        serverRandom = body[2..34].ToArray();
        int at = 34 + 1 + body[34];
        if (body[at] != 0xC0 || body[at + 1] != 0x2B)
        {
            throw new IOException($"The server chose suite {body[at]:x2}{body[at + 1]:x2}.");
        }

        at += 3;
        if (at + 2 <= body.Length)
        {
            int end = at + 2 + BinaryPrimitives.ReadUInt16BigEndian(body[at..]);
            for (int position = at + 2; position + 4 <= end;)
            {
                int type = BinaryPrimitives.ReadUInt16BigEndian(body[position..]);
                int length = BinaryPrimitives.ReadUInt16BigEndian(body[(position + 2)..]);
                SecureRenegotiation |= type == 0xff01;
                position += 4 + length;
            }
        }
    }

    /// <summary>Reads plain handshake records until at least one whole message is in, and returns the whole ones.</summary>
    private async Task<List<byte[]>> ReadMessagesAsync()
    {
        var messages = new List<byte[]>();
        while (messages.Count == 0)
        {
            (byte type, byte[] fragment) = await ReadRecordAsync(CancellationToken.None);
            if (type == Alert)
            {
                throw new IOException($"The server sent an alert during the handshake: level {fragment[0]}, description {fragment[1]}.");
            }

            pending.AddRange(fragment);
            while (pending.Count >= 4)
            {
                int length = (pending[1] << 16) | (pending[2] << 8) | pending[3];
                if (pending.Count < 4 + length)
                {
                    break;
                }

                messages.Add([.. pending.GetRange(0, 4 + length)]);
                pending.RemoveRange(0, 4 + length);
            }
        }

        return messages;
    }

    private async Task<(byte Type, byte[] Fragment)> ReadRecordAsync(CancellationToken cancellationToken)
    {
        byte[] header = new byte[5];
        await stream!.ReadExactlyAsync(header, cancellationToken);
        byte[] fragment = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(3))];
        await stream.ReadExactlyAsync(fragment, cancellationToken);
        return (header[0], fragment);
    }

    private async Task WritePlainAsync(byte type, byte[] fragment)
    {
        await stream!.WriteAsync(Record(type, fragment));
        await stream.FlushAsync();
    }

    /// <summary>AES-128-GCM sealing: an explicit nonce of the sequence number, and the sequence, type, version and length as data.</summary>
    private async Task WriteSealedAsync(byte type, byte[] plaintext)
    {
        byte[] explicitNonce = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(explicitNonce, clientSequence);
        byte[] data = new byte[13];
        BinaryPrimitives.WriteUInt64BigEndian(data, clientSequence);
        data[8] = type;
        data[9] = 3;
        data[10] = 3;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(11), (ushort)plaintext.Length);
        byte[] sealedText = new byte[plaintext.Length];
        byte[] tag = new byte[16];
        byte[] nonce = [.. clientIv, .. explicitNonce];
        using (var gcm = new AesGcm(clientKey, 16))
        {
            gcm.Encrypt(nonce, plaintext, sealedText, tag, data);
        }

        clientSequence++;
        await WritePlainAsync(type, [.. explicitNonce, .. sealedText, .. tag]);
    }

    private byte[] Open(byte type, byte[] fragment)
    {
        byte[] sealedText = fragment[8..^16];
        byte[] data = new byte[13];
        BinaryPrimitives.WriteUInt64BigEndian(data, serverSequence);
        data[8] = type;
        data[9] = 3;
        data[10] = 3;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(11), (ushort)sealedText.Length);
        byte[] plaintext = new byte[sealedText.Length];
        byte[] nonce = [.. serverIv, .. fragment[0..8]];
        byte[] tag = fragment[^16..];
        using (var gcm = new AesGcm(serverKey, 16))
        {
            gcm.Decrypt(nonce, sealedText, tag, plaintext, data);
        }

        serverSequence++;
        return plaintext;
    }
}
