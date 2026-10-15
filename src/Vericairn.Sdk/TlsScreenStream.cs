// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Vericairn.Sdk;

/// <summary>
/// A stream to put under a TLS stream on the local network. It reads the other end's handshake while it travels in the clear,
/// and refuses a certificate before this machine's TLS sees it, unless the certificate stands alone and names no address to
/// fetch: no authority or subject information access, no CRL distribution points and no freshest CRL. On macOS the platform's
/// TLS asks the system's trust service about a peer's certificate before any validation the caller configures, and that service
/// fetches the issuer a certificate names, so no chain policy can keep a stranger's certificate from making this machine connect
/// where the stranger says. Under TLS 1.2, which is all macOS serves, the certificate travels in the clear and the screen reads
/// it. TLS 1.3 encrypts it, and the platforms that negotiate TLS 1.3 build chains under the caller's offline policy instead.
/// Screening ends at the other end's change of cipher spec, after which its records are encrypted and pass through untouched.
/// </summary>
public sealed class TlsScreenStream : Stream
{
    // Record content types (RFC 5246 § 6.2.1, RFC 8446 § 5.1) and the handshake messages the screen reads.
    private const byte ChangeCipherSpec = 20;
    private const byte Alert = 21;
    private const byte Handshake = 22;
    private const byte ApplicationData = 23;
    private const byte CertificateMessage = 11;
    private const byte CertificateUrlMessage = 21;

    private const int RecordHeader = 5;
    private const int MaxRecord = (1 << 14) + 2048;
    private const int MaxMessages = 64 * 1024;
    private const int MaxBuffered = MaxMessages + (2 * (RecordHeader + MaxRecord));

    // The extensions whose addresses a platform may fetch, by object identifier and by their DER encoding, which the screen also
    // looks for in a certificate's bytes so that no difference between parsers can hide one.
    private static readonly string[] AddressExtensions = ["1.3.6.1.5.5.7.1.1", "1.3.6.1.5.5.7.1.11", "2.5.29.31", "2.5.29.46"];

    private static readonly byte[][] AddressEncodings =
    [
        [0x06, 0x08, 0x2B, 0x06, 0x01, 0x05, 0x05, 0x07, 0x01, 0x01],
        [0x06, 0x08, 0x2B, 0x06, 0x01, 0x05, 0x05, 0x07, 0x01, 0x0B],
        [0x06, 0x03, 0x55, 0x1D, 0x1F],
        [0x06, 0x03, 0x55, 0x1D, 0x2E],
    ];

    private readonly Stream input;
    private readonly Stream output;
    private readonly bool leaveOpen;
    private byte[] buffer = new byte[16 * 1024];
    private int start;
    private int released;
    private int parsed;
    private int end;
    private byte[] messages = new byte[4 * 1024];
    private int pending;
    private bool screening = true;
    private bool ended;

    /// <summary>Screens what <paramref name="stream"/> reads, and writes to it unchanged.</summary>
    public TlsScreenStream(Stream stream, bool leaveOpen = false)
        : this(stream, stream, leaveOpen)
    {
    }

    /// <summary>Screens what <paramref name="input"/> reads, and writes to <paramref name="output"/> unchanged.</summary>
    public TlsScreenStream(Stream input, Stream output, bool leaveOpen = false)
    {
        this.input = input ?? throw new ArgumentNullException(nameof(input));
        this.output = output ?? throw new ArgumentNullException(nameof(output));
        this.leaveOpen = leaveOpen;
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanWrite => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Flush() => output.Flush();

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => output.Write(buffer, offset, count);

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer) => output.Write(buffer);

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        output.WriteAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        output.WriteAsync(buffer, cancellationToken);

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        while (true)
        {
            if (Take(buffer, out int taken))
            {
                return taken;
            }

            if (!screening)
            {
                return input.Read(buffer);
            }

            Received(input.Read(Room().Span));
        }
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (Take(buffer.Span, out int taken))
            {
                return taken;
            }

            if (!screening)
            {
                return await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }

            Received(await input.ReadAsync(Room(), cancellationToken).ConfigureAwait(false));
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen)
        {
            input.Dispose();
            if (!ReferenceEquals(output, input))
            {
                output.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        if (!leaveOpen)
        {
            await input.DisposeAsync().ConfigureAwait(false);
            if (!ReferenceEquals(output, input))
            {
                await output.DisposeAsync().ConfigureAwait(false);
            }
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    private static int Length24(ReadOnlySpan<byte> bytes) => (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];

    private static IOException Refused(string reason) => new($"Vericairn refused the TLS handshake: the other end {reason}.");

    /// <summary>
    /// Refuses the certificates of one TLS 1.2 Certificate message (RFC 5246 § 7.4.2) unless there is at most one, it parses, and
    /// it names no address to fetch.
    /// </summary>
    private static void Certificates(ReadOnlySpan<byte> message)
    {
        if (message.Length < 3 || Length24(message) != message.Length - 3)
        {
            throw Refused("presents a certificate list that does not parse");
        }

        ReadOnlySpan<byte> list = message[3..];
        int count = 0;
        while (!list.IsEmpty)
        {
            int length = list.Length < 3 ? -1 : Length24(list);
            if (length <= 0 || length > list.Length - 3)
            {
                throw Refused("presents a certificate list that does not parse");
            }

            if (++count > 1)
            {
                throw Refused("presents more than one certificate");
            }

            Certificate(list.Slice(3, length));
            list = list[(3 + length)..];
        }
    }

    private static void Certificate(ReadOnlySpan<byte> der)
    {
        foreach (byte[] encoding in AddressEncodings)
        {
            if (der.IndexOf(encoding) >= 0)
            {
                throw Refused("presents a certificate that names an address to fetch");
            }
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadCertificate(der);
        }
        catch (CryptographicException)
        {
            throw Refused("presents a certificate that does not parse");
        }

        using (certificate)
        {
            foreach (X509Extension extension in certificate.Extensions)
            {
                if (Array.IndexOf(AddressExtensions, extension.Oid?.Value) >= 0)
                {
                    throw Refused("presents a certificate that names an address to fetch");
                }
            }
        }
    }

    /// <summary>Gives the reader what the screen has passed, or, once screening is over, what it still holds; false when there is nothing to give yet.</summary>
    private bool Take(Span<byte> destination, out int taken)
    {
        taken = 0;
        if (released > start)
        {
            taken = Math.Min(destination.Length, released - start);
            buffer.AsSpan(start, taken).CopyTo(destination);
            start += taken;
            return true;
        }

        // A read at the end of the stream returns nothing, as it would from the stream underneath.
        return ended;
    }

    /// <summary>Where the next read from underneath goes: after what the screen holds, in a buffer it grows within its limit.</summary>
    private Memory<byte> Room()
    {
        if (start == end)
        {
            start = released = parsed = end = 0;
        }
        else if (start > 0 && end == buffer.Length)
        {
            buffer.AsSpan(start, end - start).CopyTo(buffer);
            released -= start;
            parsed -= start;
            end -= start;
            start = 0;
        }

        if (end == buffer.Length)
        {
            if (buffer.Length >= MaxBuffered)
            {
                throw Refused("sends more handshake in the clear than the screen reads");
            }

            Array.Resize(ref buffer, Math.Min(buffer.Length * 2, MaxBuffered));
        }

        return buffer.AsMemory(end);
    }

    private void Received(int read)
    {
        if (read == 0)
        {
            // What is left is part of a record the other end never finished; it is never passed on.
            ended = true;
            return;
        }

        end += read;
        Screen();
    }

    /// <summary>Reads every whole record the screen holds, and passes each once no handshake message is left part way.</summary>
    private void Screen()
    {
        while (screening && end - parsed >= RecordHeader)
        {
            byte type = buffer[parsed];
            int length = (buffer[parsed + 3] << 8) | buffer[parsed + 4];
            if (buffer[parsed + 1] != 3 || type is not (ChangeCipherSpec or Alert or Handshake or ApplicationData))
            {
                throw Refused("does not speak TLS");
            }

            if (length > MaxRecord)
            {
                throw Refused("sends a record longer than TLS allows");
            }

            if (end - parsed < RecordHeader + length)
            {
                return;
            }

            int fragment = parsed + RecordHeader;
            parsed = fragment + length;
            if (type == Handshake)
            {
                Collect(buffer.AsSpan(fragment, length));
            }
            else if (type == ChangeCipherSpec)
            {
                // From here the other end's records are encrypted: TLS 1.2's Finished, or TLS 1.3's handshake.
                if (pending > 0)
                {
                    throw Refused("changes its cipher part way through a handshake message");
                }

                screening = false;
                released = end;
                return;
            }

            if (pending == 0)
            {
                released = parsed;
            }
        }
    }

    /// <summary>Adds a handshake fragment and inspects every message it completes.</summary>
    private void Collect(ReadOnlySpan<byte> fragment)
    {
        if (pending + fragment.Length > MaxMessages)
        {
            throw Refused("sends a handshake message longer than the screen reads");
        }

        if (pending + fragment.Length > messages.Length)
        {
            Array.Resize(ref messages, Math.Min(Math.Max(messages.Length * 2, pending + fragment.Length), MaxMessages));
        }

        fragment.CopyTo(messages.AsSpan(pending));
        pending += fragment.Length;
        int offset = 0;
        while (pending - offset >= 4)
        {
            int length = Length24(messages.AsSpan(offset + 1));
            if (length > MaxMessages)
            {
                throw Refused("sends a handshake message longer than the screen reads");
            }

            if (pending - offset - 4 < length)
            {
                break;
            }

            byte kind = messages[offset];
            if (kind == CertificateUrlMessage)
            {
                throw Refused("offers addresses to fetch its certificates from");
            }

            if (kind == CertificateMessage)
            {
                Certificates(messages.AsSpan(offset + 4, length));
            }

            offset += 4 + length;
        }

        messages.AsSpan(offset, pending - offset).CopyTo(messages);
        pending -= offset;
    }
}
