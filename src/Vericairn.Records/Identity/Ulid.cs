// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Numerics;
using System.Security.Cryptography;

namespace Vericairn.Records.Identity;

/// <summary>
/// A universally unique, lexicographically sortable identifier: 48 bits of millisecond
/// timestamp followed by 80 random bits, rendered as 26 upper-case Crockford base32 characters.
/// </summary>
public readonly record struct Ulid : IComparable<Ulid>
{
    /// <summary>Crockford base32 alphabet (no I, L, O or U).</summary>
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Length of the textual form.</summary>
    public const int Length = 26;

    private readonly string? value;

    private Ulid(string value) => this.value = value;

    /// <summary>The 26-character textual form.</summary>
    public string Value => value ?? string.Empty;

    /// <summary>The millisecond timestamp encoded in the first 48 bits.</summary>
    public DateTimeOffset Timestamp
    {
        get
        {
            byte[] bytes = ToBytes();
            long milliseconds = 0;
            for (int i = 0; i < 6; i++)
            {
                milliseconds = (milliseconds << 8) | bytes[i];
            }

            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }
    }

    /// <summary>Creates a new identifier stamped with the current UTC time.</summary>
    public static Ulid NewUlid() => NewUlid(DateTimeOffset.UtcNow);

    /// <summary>Creates a new identifier stamped with the given time.</summary>
    public static Ulid NewUlid(DateTimeOffset at)
    {
        long milliseconds = at.ToUnixTimeMilliseconds();
        if (milliseconds < 0 || milliseconds > 0xFFFF_FFFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(at), "The timestamp does not fit in 48 bits.");
        }

        Span<byte> bytes = stackalloc byte[16];
        for (int i = 5; i >= 0; i--)
        {
            bytes[i] = (byte)(milliseconds & 0xFF);
            milliseconds >>= 8;
        }

        RandomNumberGenerator.Fill(bytes[6..]);
        return FromBytes(bytes);
    }

    /// <summary>Encodes 16 bytes as a ULID.</summary>
    public static Ulid FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16)
        {
            throw new ArgumentException("A ULID has exactly 16 bytes.", nameof(bytes));
        }

        var number = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        Span<char> chars = stackalloc char[Length];
        for (int i = Length - 1; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(number & 31)];
            number >>= 5;
        }

        return new Ulid(new string(chars));
    }

    /// <summary>Decodes the identifier to its 16 bytes.</summary>
    public byte[] ToBytes()
    {
        BigInteger number = BigInteger.Zero;
        foreach (char c in Value)
        {
            number = (number << 5) | Alphabet.IndexOf(c, StringComparison.Ordinal);
        }

        byte[] bytes = number.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length == 16)
        {
            return bytes;
        }

        var padded = new byte[16];
        bytes.CopyTo(padded, 16 - bytes.Length);
        return padded;
    }

    /// <summary>Parses the textual form; upper-case only, 26 characters, first character 0–7.</summary>
    public static bool TryParse(string? text, out Ulid ulid)
    {
        ulid = default;
        if (text is null || text.Length != Length || text[0] > '7')
        {
            return false;
        }

        foreach (char c in text)
        {
            if (Alphabet.IndexOf(c, StringComparison.Ordinal) < 0)
            {
                return false;
            }
        }

        ulid = new Ulid(text);
        return true;
    }

    /// <summary>Parses the textual form or throws.</summary>
    public static Ulid Parse(string text) =>
        TryParse(text, out Ulid ulid) ? ulid : throw new FormatException($"'{text}' is not a ULID.");

    /// <summary>True when the text is a well-formed ULID.</summary>
    public static bool IsValid(string? text) => TryParse(text, out _);

    /// <inheritdoc />
    public int CompareTo(Ulid other) => string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Ordinal ordering.</summary>
    public static bool operator <(Ulid left, Ulid right) => left.CompareTo(right) < 0;

    /// <summary>Ordinal ordering.</summary>
    public static bool operator >(Ulid left, Ulid right) => left.CompareTo(right) > 0;

    /// <summary>Ordinal ordering.</summary>
    public static bool operator <=(Ulid left, Ulid right) => left.CompareTo(right) <= 0;

    /// <summary>Ordinal ordering.</summary>
    public static bool operator >=(Ulid left, Ulid right) => left.CompareTo(right) >= 0;
}
