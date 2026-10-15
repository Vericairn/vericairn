// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Vericairn.Protocol;

/// <summary>Ask the runtime for a pairing code: an administrator's authority to pair one new device for a principal; from 0.6.0.</summary>
/// <param name="Principal">The principal the new device will act for.</param>
/// <param name="By">The administrator: the operator, or a principal holding the 'administer' scope, whose admitted device on the runtime's machine signs the admission.</param>
/// <param name="Profiles">The connectivity profiles the device is permitted; local-network when omitted.</param>
public sealed record PairingIssueRequest(string Principal, string By, IReadOnlyList<string>? Profiles = null);

/// <summary>A pairing code, shown to the administrator and entered on the new device: short-lived, single-use and limited in attempts.</summary>
/// <param name="Code">The code, five groups of four characters; the first group names the pairing and the other four are its secret.</param>
/// <param name="Principal">The principal the new device will act for.</param>
/// <param name="PrincipalName">That principal's name.</param>
/// <param name="Profiles">The connectivity profiles the device will be permitted.</param>
/// <param name="ExpiresAt">When the code stops working.</param>
/// <param name="Attempts">How many exchanges the code allows.</param>
/// <param name="Address">Where the device reaches the runtime: the local-network profile's address and port.</param>
/// <param name="Estate">The estate's id.</param>
/// <param name="Identity">The fingerprint of the runtime identity the device will pin (sha256:hex of its public key), to compare on the device.</param>
public sealed record PairingIssued(string Code, string Principal, string? PrincipalName, IReadOnlyList<string> Profiles, string ExpiresAt, int Attempts, string Address, string Estate, string Identity);

/// <summary>The new device's first message: the code's handle and a fresh nonce. Nothing in it depends on the code's secret.</summary>
/// <param name="Handle">The first four characters of the code, which name the pairing.</param>
/// <param name="Nonce">32 random bytes, base64url.</param>
public sealed record PairingHello(string Handle, string Nonce);

/// <summary>
/// The runtime's answer to a hello: who it is, the certificate it presented on this connection, what it will record, and a MAC over
/// all of it with the key the code gives the runtime. Each answer uses one of the code's attempts.
/// </summary>
/// <param name="Handle">The pairing's handle.</param>
/// <param name="DeviceNonce">The device's nonce, echoed.</param>
/// <param name="RuntimeNonce">32 random bytes, base64url; names this exchange.</param>
/// <param name="Estate">The estate's id.</param>
/// <param name="EstateName">The estate's name.</param>
/// <param name="Identity">The runtime identity's public key, base64url SubjectPublicKeyInfo, which the device pins.</param>
/// <param name="Certificate">The fingerprint of the TLS certificate the runtime presented on this connection, sha256:hex.</param>
/// <param name="Principal">The principal the device will act for.</param>
/// <param name="PrincipalName">That principal's name.</param>
/// <param name="PrincipalKind">human, agent or service.</param>
/// <param name="Profiles">The connectivity profiles the device will be permitted.</param>
/// <param name="Record">The id the device record will have, which is the device's identity and the id its key is held under.</param>
/// <param name="At">The device record's time.</param>
/// <param name="Format">The record format the device record is written in.</param>
/// <param name="ExpiresAt">When the code stops working.</param>
/// <param name="Mac">HMAC-SHA256 with the runtime's key over the challenge transcript, base64url.</param>
public sealed record PairingChallenge(string Handle, string DeviceNonce, string RuntimeNonce, string Estate, string? EstateName, string Identity, string Certificate, string Principal, string? PrincipalName, string PrincipalKind, IReadOnlyList<string> Profiles, string Record, string At, long Format, string ExpiresAt, string Mac);

/// <summary>The new device as it presents itself: its descriptive identity and its public key.</summary>
/// <param name="Name">Descriptive name, never a machine path.</param>
/// <param name="Platform">Operating system family.</param>
/// <param name="Custody">How its key is held.</param>
/// <param name="PublicKey">Base64url SubjectPublicKeyInfo of its P-256 key.</param>
/// <param name="Presence">required or none from record format 2; null for format 1.</param>
public sealed record PairingDevice(string Name, string Platform, string Custody, string PublicKey, string? Presence = null);

/// <summary>The device's confirmation: its key and descriptive identity, its signature of the device record, and a MAC with the key the code gives the device.</summary>
/// <param name="Handle">The pairing's handle.</param>
/// <param name="RuntimeNonce">The runtime's nonce, which names the exchange.</param>
/// <param name="Device">The device as it presents itself.</param>
/// <param name="Signature">The device key's signature of the device record's content hash, base64url.</param>
/// <param name="Mac">HMAC-SHA256 with the device's key over the confirmation transcript, base64url.</param>
public sealed record PairingConfirm(string Handle, string RuntimeNonce, PairingDevice Device, string Signature, string Mac);

/// <summary>The runtime's last message: the records it wrote, with a MAC over the whole exchange.</summary>
/// <param name="Device">The device record's id: the device's identity.</param>
/// <param name="Admission">The admission's id.</param>
/// <param name="Hash">The device record's content hash.</param>
/// <param name="Mac">HMAC-SHA256 with the runtime's key over the completion transcript, base64url.</param>
public sealed record PairingCompleted(string Device, string Admission, string Hash, string Mac);

/// <summary>
/// The pairing exchange, version 1: a code of 20 characters from Crockford's base32 alphabet, the first four naming the pending
/// pairing and the other sixteen its 80-bit secret; HKDF-SHA256 over the secret gives the runtime and the device a key each, and
/// every message after the hello carries an HMAC-SHA256 over the transcript so far. Standard primitives only: no new cryptography.
/// </summary>
public static class PairingProtocol
{
    /// <summary>The scheme name bound into every key and transcript.</summary>
    public const string Scheme = "vericairn-pairing/1";

    /// <summary>Crockford's base32 alphabet: digits and upper-case letters without I, L, O and U.</summary>
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Characters naming the pairing; not secret.</summary>
    public const int HandleLength = 4;

    /// <summary>Characters of the secret: 16 characters of 5 bits, 80 bits.</summary>
    public const int SecretLength = 16;

    /// <summary>Characters of a whole code.</summary>
    public const int CodeLength = HandleLength + SecretLength;

    /// <summary>How many exchanges one code allows: each answer the runtime gives to a hello uses one.</summary>
    public const int Attempts = 5;

    /// <summary>How long a code works after it is issued.</summary>
    public static TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(10);

    /// <summary>A new code from the system's cryptographic random number generator, ungrouped.</summary>
    public static string NewCode() => new(RandomNumberGenerator.GetItems<char>(Alphabet, CodeLength));

    /// <summary>A code as shown: five groups of four characters.</summary>
    public static string Display(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return string.Join('-', Enumerable.Range(0, (code.Length + 3) / 4).Select(i => code.Substring(i * 4, Math.Min(4, code.Length - (i * 4)))));
    }

    /// <summary>Reads a code as a person typed it: case is ignored, as are hyphens and spaces, and I and L read as 1, O as 0.</summary>
    public static bool TryParse(string? text, out string handle, out string secret)
    {
        handle = string.Empty;
        secret = string.Empty;
        if (text is null)
        {
            return false;
        }

        var normalized = new StringBuilder(CodeLength);
        foreach (char c in text)
        {
            if (c is '-' or ' ')
            {
                continue;
            }

            char upper = char.ToUpperInvariant(c) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                char other => other,
            };
            if (Alphabet.IndexOf(upper, StringComparison.Ordinal) < 0 || normalized.Length == CodeLength)
            {
                return false;
            }

            normalized.Append(upper);
        }

        if (normalized.Length != CodeLength)
        {
            return false;
        }

        handle = normalized.ToString(0, HandleLength);
        secret = normalized.ToString(HandleLength, SecretLength);
        return true;
    }

    /// <summary>The two keys a code gives: HKDF-SHA256 extracts from the secret with the scheme as salt, and expands once for the runtime and once for the device.</summary>
    public static (byte[] Runtime, byte[] Device) Keys(string handle, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(handle);
        ArgumentException.ThrowIfNullOrEmpty(secret);
        byte[] material = Encoding.UTF8.GetBytes(secret);
        byte[] prk = HKDF.Extract(HashAlgorithmName.SHA256, material, Encoding.UTF8.GetBytes(Scheme));
        try
        {
            return (
                HKDF.Expand(HashAlgorithmName.SHA256, prk, 32, Encoding.UTF8.GetBytes(Scheme + " runtime " + handle)),
                HKDF.Expand(HashAlgorithmName.SHA256, prk, 32, Encoding.UTF8.GetBytes(Scheme + " device " + handle)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prk);
            CryptographicOperations.ZeroMemory(material);
        }
    }

    /// <summary>A fresh nonce: 32 random bytes, base64url.</summary>
    public static string NewNonce() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>True when the text is a nonce of 16 to 64 bytes in base64url.</summary>
    public static bool IsNonce(string? text) =>
        !string.IsNullOrEmpty(text) && text.Length <= 88 && Base64Url.IsValid(text) && Base64Url.DecodeFromChars(text).Length is >= 16 and <= 64;

    /// <summary>The runtime's MAC over a challenge, every field but the MAC.</summary>
    public static string ChallengeMac(byte[] runtimeKey, PairingChallenge challenge) => Mac(runtimeKey, ChallengeTranscript(challenge));

    /// <summary>The device's MAC over the challenge as received, with its MAC, and the confirmation, every field but its MAC.</summary>
    public static string ConfirmMac(byte[] deviceKey, PairingChallenge challenge, PairingConfirm confirm) => Mac(deviceKey, ConfirmTranscript(challenge, confirm));

    /// <summary>The runtime's MAC over the whole exchange and what it recorded.</summary>
    public static string CompletedMac(byte[] runtimeKey, PairingChallenge challenge, PairingConfirm confirm, PairingCompleted completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        var transcript = new Transcript("completed");
        transcript.Add(ConfirmTranscript(challenge, confirm));
        transcript.Add(confirm.Mac);
        transcript.Add(completed.Device);
        transcript.Add(completed.Admission);
        transcript.Add(completed.Hash);
        return Mac(runtimeKey, transcript.ToArray());
    }

    /// <summary>Compares a MAC with the expected one in constant time; anything that is not base64url of the right length does not match.</summary>
    public static bool Matches(string expected, string? actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (string.IsNullOrEmpty(actual) || !Base64Url.IsValid(actual))
        {
            return false;
        }

        byte[] left = Base64Url.DecodeFromChars(expected);
        byte[] right = Base64Url.DecodeFromChars(actual);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string Mac(byte[] key, byte[] transcript)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Base64Url.EncodeToString(HMACSHA256.HashData(key, transcript));
    }

    private static byte[] ChallengeTranscript(PairingChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        var transcript = new Transcript("challenge");
        transcript.Add(challenge.Handle);
        transcript.Add(challenge.DeviceNonce);
        transcript.Add(challenge.RuntimeNonce);
        transcript.Add(challenge.Estate);
        transcript.AddOptional(challenge.EstateName);
        transcript.Add(challenge.Identity);
        transcript.Add(challenge.Certificate);
        transcript.Add(challenge.Principal);
        transcript.AddOptional(challenge.PrincipalName);
        transcript.Add(challenge.PrincipalKind);
        transcript.Add(challenge.Profiles ?? []);
        transcript.Add(challenge.Record);
        transcript.Add(challenge.At);
        transcript.Add(challenge.Format.ToString(CultureInfo.InvariantCulture));
        transcript.Add(challenge.ExpiresAt);
        return transcript.ToArray();
    }

    private static byte[] ConfirmTranscript(PairingChallenge challenge, PairingConfirm confirm)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(confirm.Device);
        var transcript = new Transcript("confirm");
        transcript.Add(ChallengeTranscript(challenge));
        transcript.Add(challenge.Mac);
        transcript.Add(confirm.Handle);
        transcript.Add(confirm.RuntimeNonce);
        transcript.Add(confirm.Device.Name);
        transcript.Add(confirm.Device.Platform);
        transcript.Add(confirm.Device.Custody);
        transcript.Add(confirm.Device.PublicKey);
        transcript.AddOptional(confirm.Device.Presence);
        transcript.Add(confirm.Signature);
        return transcript.ToArray();
    }

    /// <summary>An unambiguous encoding: the scheme and a label, then every field as a 4-byte big-endian length and its bytes; an optional field is preceded by a byte saying whether it is present, a list by its count.</summary>
    private sealed class Transcript
    {
        private readonly ArrayBufferWriter<byte> buffer = new();

        public Transcript(string label)
        {
            Add(Scheme);
            Add(label);
        }

        public void Add(string? value) => Add(Encoding.UTF8.GetBytes(value ?? string.Empty));

        public void AddOptional(string? value)
        {
            buffer.Write([value is null ? (byte)0 : (byte)1]);
            if (value is not null)
            {
                Add(value);
            }
        }

        public void Add(IReadOnlyList<string> values)
        {
            Span<byte> count = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(count, values.Count);
            buffer.Write(count);
            foreach (string value in values)
            {
                Add(value);
            }
        }

        public void Add(byte[] bytes)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            buffer.Write(length);
            buffer.Write(bytes);
        }

        public byte[] ToArray() => buffer.WrittenSpan.ToArray();
    }
}
