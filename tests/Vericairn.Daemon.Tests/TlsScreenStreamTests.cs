// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// The TLS certificate screen over records made by hand: what it passes arrives unchanged, and a certificate it refuses is never
/// passed, not even in part.
/// </summary>
public class TlsScreenStreamTests
{
    private const byte ChangeCipherSpec = 20;
    private const byte Alert = 21;
    private const byte Handshake = 22;
    private const byte ApplicationData = 23;

    private static byte[] Record(byte type, byte[] fragment)
    {
        var record = new byte[5 + fragment.Length];
        record[0] = type;
        record[1] = 3;
        record[2] = 3;
        record[3] = (byte)(fragment.Length >> 8);
        record[4] = (byte)fragment.Length;
        fragment.CopyTo(record, 5);
        return record;
    }

    private static byte[] Length24(int length) => [(byte)(length >> 16), (byte)(length >> 8), (byte)length];

    private static byte[] Message(byte kind, byte[] body) => [kind, .. Length24(body.Length), .. body];

    private static byte[] CertificateMessage(params byte[][] certificates)
    {
        byte[] list = [.. certificates.SelectMany(der => (byte[])[.. Length24(der.Length), .. der])];
        return Message(11, [.. Length24(list.Length), .. list]);
    }

    private static byte[] ClientHello() => Record(Handshake, Message(1, RandomNumberGenerator.GetBytes(180)));

    private static byte[] Clean()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(LocalNetworkCredential.SubjectName, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.2")], false));
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        return certificate.RawData;
    }

    private static byte[] Addressed(X509Extension extension)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=stranger", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(extension);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        return certificate.RawData;
    }

    private static IEnumerable<byte[]> AddressedCertificates() =>
    [
        Addressed(new X509AuthorityInformationAccessExtension(null, ["http://127.0.0.1:1/ca.cer"])),
        Addressed(new X509AuthorityInformationAccessExtension(["http://127.0.0.1:1/ocsp"], null)),
        Addressed(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension(["http://127.0.0.1:1/crl"])),
        Addressed(new X509Extension("2.5.29.46", CertificateRevocationListBuilder.BuildCrlDistributionPointExtension(["http://127.0.0.1:1/delta"]).RawData, false)),
        Addressed(new X509Extension("1.3.6.1.5.5.7.1.11", new X509AuthorityInformationAccessExtension(null, ["http://127.0.0.1:1/sia"]).RawData, false)),
    ];

    /// <summary>Reads everything through the screen, a few bytes from underneath at a time, and what it passed before a refusal.</summary>
    private static async Task<(byte[] Passed, IOException? Refusal)> ScreenAsync(byte[] wire, int chunk)
    {
        await using var screen = new TlsScreenStream(new ChunkedStream(wire, chunk));
        using var passed = new MemoryStream();
        var buffer = new byte[37];
        try
        {
            int read;
            while ((read = await screen.ReadAsync(buffer, TestContext.Current.CancellationToken)) > 0)
            {
                passed.Write(buffer, 0, read);
            }
        }
        catch (IOException refusal)
        {
            return (passed.ToArray(), refusal);
        }

        return (passed.ToArray(), null);
    }

    /// <summary>Nothing the screen passed lies beyond what it should have: a refusal may come before it passes all of that.</summary>
    private static void NothingBeyond(byte[] expected, byte[] passed) =>
        Assert.True(expected.AsSpan().StartsWith(passed), $"The screen passed {passed.Length} bytes, {expected.Length} at most expected.");

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(4096)]
    public async Task AFlightWithACertificateThatNamesNoAddressPassesUnchanged(int chunk)
    {
        byte[] certificate = CertificateMessage(Clean());
        byte[] keyExchange = Message(16, RandomNumberGenerator.GetBytes(66));

        // The certificate message split across three records, then a key exchange, an alert-free change of cipher spec, and records
        // that are encrypted from there on, one of them looking like a plain handshake record.
        byte[] wire =
        [
            .. ClientHello(),
            .. Record(Handshake, certificate[..10]),
            .. Record(Handshake, certificate[10..200]),
            .. Record(Handshake, [.. certificate[200..], .. keyExchange]),
            .. Record(ChangeCipherSpec, [1]),
            .. Record(Handshake, RandomNumberGenerator.GetBytes(40)),
            .. Record(ApplicationData, RandomNumberGenerator.GetBytes(3000)),
        ];

        (byte[] passed, IOException? refusal) = await ScreenAsync(wire, chunk);
        Assert.Null(refusal);
        Assert.Equal(wire, passed);
    }

    [Fact]
    public async Task AnEmptyCertificateListPasses()
    {
        byte[] wire = [.. ClientHello(), .. Record(Handshake, CertificateMessage()), .. Record(ChangeCipherSpec, [1])];
        (byte[] passed, IOException? refusal) = await ScreenAsync(wire, 3);
        Assert.Null(refusal);
        Assert.Equal(wire, passed);
    }

    [Fact]
    public async Task ACertificateThatNamesAnAddressIsNeverPassedEvenInPart()
    {
        foreach (byte[] addressed in AddressedCertificates())
        {
            byte[] hello = ClientHello();
            byte[] certificate = CertificateMessage(addressed);
            byte[] wire = [.. hello, .. Record(Handshake, certificate[..50]), .. Record(Handshake, certificate[50..]), .. Record(ChangeCipherSpec, [1])];
            foreach (int chunk in new[] { 1, 64, 8192 })
            {
                (byte[] passed, IOException? refusal) = await ScreenAsync(wire, chunk);
                Assert.NotNull(refusal);
                Assert.Contains("names an address to fetch", refusal.Message, StringComparison.Ordinal);
                NothingBeyond(hello, passed);
            }
        }
    }

    [Fact]
    public async Task OnlyOneCertificateIsPresented()
    {
        byte[] hello = ClientHello();
        (byte[] passed, IOException? refusal) = await ScreenAsync([.. hello, .. Record(Handshake, CertificateMessage(Clean(), Clean()))], 512);
        Assert.Contains("more than one certificate", refusal?.Message, StringComparison.Ordinal);
        NothingBeyond(hello, passed);
    }

    [Fact]
    public async Task ACertificateThatDoesNotParseIsRefused()
    {
        (_, IOException? garbage) = await ScreenAsync([.. ClientHello(), .. Record(Handshake, CertificateMessage(RandomNumberGenerator.GetBytes(300)))], 512);
        Assert.Contains("does not parse", garbage?.Message, StringComparison.Ordinal);

        // A list whose length disagrees with its message.
        byte[] certificate = CertificateMessage(Clean());
        certificate[6]++;
        (_, IOException? list) = await ScreenAsync([.. ClientHello(), .. Record(Handshake, certificate)], 512);
        Assert.Contains("does not parse", list?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddressesToFetchCertificatesFromAreRefused()
    {
        (_, IOException? refusal) = await ScreenAsync([.. ClientHello(), .. Record(Handshake, Message(21, RandomNumberGenerator.GetBytes(40)))], 512);
        Assert.Contains("offers addresses", refusal?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatIsNotTlsIsRefused()
    {
        (byte[] passed, IOException? refusal) = await ScreenAsync(Encoding.ASCII.GetBytes("GET /v1/health HTTP/1.1\r\nHost: x\r\n\r\n"), 512);
        Assert.Contains("does not speak TLS", refusal?.Message, StringComparison.Ordinal);
        Assert.Empty(passed);

        // An SSL 2.0 style hello, and a record longer than TLS allows.
        (_, IOException? old) = await ScreenAsync([0x80, 0x2E, 0x01, 0x03, 0x01, .. RandomNumberGenerator.GetBytes(45)], 512);
        Assert.NotNull(old);
        (_, IOException? longer) = await ScreenAsync([Handshake, 3, 3, 0xFF, 0xFF, .. RandomNumberGenerator.GetBytes(100)], 512);
        Assert.Contains("longer than TLS allows", longer?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplicationDataBeforeTheChangeOfCipherSpecDoesNotEndTheScreen()
    {
        byte[] before = [.. ClientHello(), .. Record(ApplicationData, RandomNumberGenerator.GetBytes(30)), .. Record(Alert, [1, 0])];
        (byte[] passed, IOException? refusal) = await ScreenAsync([.. before, .. Record(Handshake, CertificateMessage(AddressedCertificates().First()))], 16);
        Assert.NotNull(refusal);
        NothingBeyond(before, passed);
    }

    [Fact]
    public async Task ChangingCipherPartWayThroughAMessageIsRefused()
    {
        byte[] certificate = CertificateMessage(Clean());
        (_, IOException? refusal) = await ScreenAsync([.. ClientHello(), .. Record(Handshake, certificate[..20]), .. Record(ChangeCipherSpec, [1]), .. Record(Handshake, certificate[20..])], 512);
        Assert.Contains("part way", refusal?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnfinishedRecordIsNeverPassed()
    {
        byte[] hello = ClientHello();
        byte[] certificate = Record(Handshake, CertificateMessage(Clean()));
        (byte[] passed, IOException? refusal) = await ScreenAsync([.. hello, .. certificate[..^1]], 64);
        Assert.Null(refusal);
        Assert.Equal(hello, passed);
    }

    [Fact]
    public void ReadsWithoutAwaitingAndWritesPassThrough()
    {
        byte[] wire = [.. ClientHello(), .. Record(Handshake, CertificateMessage(Clean())), .. Record(ChangeCipherSpec, [1]), .. RandomNumberGenerator.GetBytes(500)];
        var underneath = new ChunkedStream(wire, 11);
        using var screen = new TlsScreenStream(underneath);
        using var passed = new MemoryStream();
        var buffer = new byte[100];
        int read;
        while ((read = screen.Read(buffer, 0, buffer.Length)) > 0)
        {
            passed.Write(buffer, 0, read);
        }

        Assert.Equal(wire, passed.ToArray());
        screen.Write([1, 2, 3]);
        screen.Flush();
        Assert.Equal([1, 2, 3], underneath.Written.ToArray());
    }

    /// <summary>A stream that gives at most a few bytes a read from a buffer, and keeps what is written to it.</summary>
    private sealed class ChunkedStream(byte[] content, int chunk) : Stream
    {
        private int position;

        public MemoryStream Written { get; } = new();

        public override bool CanRead => true;

        public override bool CanWrite => true;

        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int count = Math.Min(Math.Min(chunk, buffer.Length), content.Length - position);
            content.AsSpan(position, count).CopyTo(buffer);
            position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(Read(buffer, offset, count));

        public override void Write(byte[] buffer, int offset, int count) => Written.Write(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
