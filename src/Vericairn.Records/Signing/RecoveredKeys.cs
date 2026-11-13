// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;

namespace Vericairn.Records.Signing;

/// <summary>
/// The keys each signature verifies under, recovered from the signature (<see cref="P256KeyRecovery"/>) and relied on only where they are
/// certain to be every such key, so that the key-closing check reads a closed key's signature from them without verifying under each
/// closed key (FIX-121). A recovered key counts only when it is a canonical point on the curve, its coordinates below p and satisfying the
/// curve's equation, and the platform's verification confirms it: by verifying under it, or because it is the point of a key the caller
/// has already seen the platform verify the signature with. The keys are relied on only when none was dropped, when one of them is each
/// key known to verify the signature, and when there are as many as the signature can verify under (<see cref="P256KeyRecovery.MostKeys"/>):
/// a set of that many distinct keys the signature verifies under is all of them, whatever the arithmetic. Anything else, such as a
/// signature that is not 64 bytes, r or s outside [1, n-1], or a recovery that misses a key, falls back to the platform's verification
/// under each key, as the check verified before. Not safe for concurrent use; the estate's trust calls it under its lock.
/// </summary>
internal sealed class RecoveredKeys
{
    private static readonly AsyncLocal<Func<byte[], byte[], byte[][]?>?> Substitute = new();
    private readonly Func<byte[], byte[], byte[][]?> recover;
    private readonly Dictionary<string, byte[]?> points = new(StringComparer.Ordinal);

    /// <summary>Recovers with <see cref="P256KeyRecovery.Candidates"/>, or with the recovery given or substituted by a test.</summary>
    internal RecoveredKeys(Func<byte[], byte[], byte[][]?>? recover = null) =>
        this.recover = recover ?? Substitute.Value ?? ((digest, signature) => P256KeyRecovery.Candidates(digest, signature));

    /// <summary>
    /// A recovery that replaces the arithmetic for every set of recovered keys made in this asynchronous flow, set only by tests to show
    /// that a wrong recovery changes no answer; null in use.
    /// </summary>
    internal static Func<byte[], byte[], byte[][]?>? SubstituteForTests
    {
        get => Substitute.Value;
        set => Substitute.Value = value;
    }

    /// <summary>How many signatures had their keys recovered.</summary>
    internal long Recoveries { get; private set; }

    /// <summary>How many recovered keys were not canonical points on the curve or the platform did not confirm, each dropped and never relied on.</summary>
    internal long Dropped { get; private set; }

    /// <summary>How many signatures fell back to the platform's verification under each key, their recovered keys not certain.</summary>
    internal long Fallbacks { get; private set; }

    /// <summary>
    /// The point X‖Y of an encoded key, as the platform imports it; null when the key is not usable or its point is not on the curve, so
    /// that the platform's verification under it answers. Kept per key.
    /// </summary>
    internal byte[]? Point(string encodedKey)
    {
        if (points.TryGetValue(encodedKey, out byte[]? known)) return known;
        byte[]? point = RecordSigning.PublicPoint(encodedKey);
        if (point is not null && !P256KeyRecovery.OnCurve(point)) point = null;
        points[encodedKey] = point;
        return point;
    }

    /// <summary>
    /// Every key a signature over a digest verifies under, each confirmed by the platform; null when that is not certain, and the
    /// platform's verification under each key answers instead. <paramref name="signingKeys"/> are encoded keys the caller has seen the
    /// platform verify the signature with: at least one is needed, and each must be among the keys recovered.
    /// </summary>
    internal byte[][]? Confirmed(ReadOnlySpan<byte> digest, string encodedSignature, IReadOnlyList<string> signingKeys)
    {
        ArgumentNullException.ThrowIfNull(signingKeys);
        if (!RecordSigning.IsSignature(encodedSignature)) return Fallback();
        byte[] signature = Base64Url.DecodeFromChars(encodedSignature);
        Recoveries++;
        byte[][]? candidates = recover(digest.ToArray(), signature);
        if (candidates is null) return Fallback();
        var signing = new List<byte[]>(signingKeys.Count);
        foreach (string key in signingKeys)
        {
            if (Point(key) is not { } point) return Fallback();
            signing.Add(point);
        }

        var confirmed = new List<byte[]>(candidates.Length);
        bool dropped = false;
        foreach (byte[] candidate in candidates)
        {
            if (confirmed.Exists(c => c.AsSpan().SequenceEqual(candidate))) continue;
            // A canonical point on the curve, checked before the platform is asked, and then one the platform verifies the signature with.
            if (P256KeyRecovery.OnCurve(candidate) && (Among(signing, candidate) || RecordSigning.VerifyPoint(candidate, digest, signature)))
            {
                confirmed.Add(candidate);
                continue;
            }

            Dropped++;
            dropped = true;
        }

        if (dropped || signing.Count == 0 || confirmed.Count != P256KeyRecovery.MostKeys(signature) || !signing.TrueForAll(point => Among(confirmed, point)))
            return Fallback();
        return [.. confirmed];
    }

    /// <summary>
    /// Whether a signature verifies under a key, from the keys recovered from it; null when they cannot answer it (<see cref="Point"/>,
    /// <see cref="Confirmed"/>) and the platform's verification under the key answers instead.
    /// </summary>
    internal bool? VerifiesUnder(string encodedKey, ReadOnlySpan<byte> digest, string encodedSignature, IReadOnlyList<string> signingKeys) =>
        Point(encodedKey) is not { } point || Confirmed(digest, encodedSignature, signingKeys) is not { } keys ? null : Among(keys, point);

    /// <summary>Whether a point is among keys.</summary>
    internal static bool Among(IEnumerable<byte[]> keys, byte[] point) => keys.Any(key => key.AsSpan().SequenceEqual(point));

    private byte[][]? Fallback()
    {
        Fallbacks++;
        return null;
    }
}
