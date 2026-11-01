// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Formats.Asn1;

namespace Vericairn.Records.Signing;

/// <summary>
/// The encodings a phone's platform keystore needs to meet the device-key contract: a P-256 public key as SubjectPublicKeyInfo, and
/// an ECDSA signature in the fixed-field IEEE P1363 form, from the X9.63 point and the DER signature the platforms return.
/// </summary>
public static class PhoneKeyEncoding
{
    // SEQUENCE { SEQUENCE { id-ecPublicKey, prime256v1 }, BIT STRING (no unused bits) } before the uncompressed point.
    private static readonly byte[] P256Prefix = Convert.FromHexString("3059301306072A8648CE3D020106082A8648CE3D030107034200");

    /// <summary>The SubjectPublicKeyInfo of a P-256 public key given as its 65-byte uncompressed X9.63 point.</summary>
    public static byte[] SubjectPublicKeyInfo(ReadOnlySpan<byte> point)
    {
        if (point.Length != 65 || point[0] != 0x04) throw new ArgumentException("A P-256 public key is a 65-byte uncompressed point.", nameof(point));
        return [.. P256Prefix, .. point];
    }

    /// <summary>The 64-byte IEEE P1363 form of a DER-encoded P-256 ECDSA signature.</summary>
    public static byte[] P1363FromDer(ReadOnlySpan<byte> der)
    {
        var reader = new AsnReader(der.ToArray(), AsnEncodingRules.DER);
        AsnReader sequence = reader.ReadSequence();
        ReadOnlySpan<byte> r = sequence.ReadIntegerBytes().Span;
        ReadOnlySpan<byte> s = sequence.ReadIntegerBytes().Span;
        sequence.ThrowIfNotEmpty();
        reader.ThrowIfNotEmpty();
        var signature = new byte[64];
        Field(r, signature.AsSpan(0, 32));
        Field(s, signature.AsSpan(32, 32));
        return signature;
    }

    // A DER integer is big-endian, minimal and signed: drop a leading zero, then right-align in the 32-byte field.
    private static void Field(ReadOnlySpan<byte> integer, Span<byte> field)
    {
        while (integer.Length > 1 && integer[0] == 0) integer = integer[1..];
        if (integer.Length > field.Length) throw new FormatException("The signature is not a P-256 ECDSA signature.");
        integer.CopyTo(field[(field.Length - integer.Length)..]);
    }
}
