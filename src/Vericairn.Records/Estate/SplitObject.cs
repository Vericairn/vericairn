// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Hashing;

namespace Vericairn.Records.Estate;

/// <summary>Why a split object cannot be read.</summary>
public enum SplitObjectFault
{
    /// <summary>
    /// The object is past the object limit, or the root is not a split object's canonical root: too large, a field missing or invalid,
    /// a parts count that does not follow from its length, a part of another length than its place holds, or a one-part root over
    /// content that is no root.
    /// </summary>
    Invalid,

    /// <summary>A part's reference is not a supported content hash.</summary>
    InvalidReference,

    /// <summary>A part is not held.</summary>
    Missing,

    /// <summary>The bytes held for a part do not match its hash.</summary>
    Altered,

    /// <summary>The parts, joined in the root's order, do not match the root's digest.</summary>
    Mismatch,

    /// <summary>The content is longer than the reader accepts.</summary>
    TooLarge,
}

/// <summary>One reason a split object cannot be read.</summary>
/// <param name="Fault">What is wrong.</param>
/// <param name="Part">The part it concerns, counted from 1 in the root's order; 0 for the object or root itself or the whole content.</param>
/// <param name="Reference">The part's hash; for a mismatch the whole content's digest; for the object itself its reference, or nothing for a root read from bytes.</param>
/// <param name="Message">What is wrong, in words.</param>
public sealed record SplitObjectProblem(SplitObjectFault Fault, int Part, string Reference, string Message);

/// <summary>Content read back by its reference: the whole content, verified, or every problem that keeps it from being read.</summary>
/// <param name="Content">The whole content; null when a problem is reported.</param>
/// <param name="Problems">Every problem found; empty when the content is returned.</param>
public sealed record SplitObjectContent(byte[]? Content, IReadOnlyList<SplitObjectProblem> Problems)
{
    /// <summary>True when the reference named a split object's root, whose parts were joined.</summary>
    public bool Split { get; init; }
}

/// <summary>What storing content writes.</summary>
/// <param name="Reference">The hash that names the content: its one object's, or its root's when it is split.</param>
/// <param name="Objects">The objects to store, in order: the one object, or every part and then the root; empty when refused.</param>
/// <param name="Refusal">Why the content cannot be stored within the object limit; null when it can.</param>
public sealed record SplitObjectPlan(string Reference, IReadOnlyList<byte[]> Objects, string? Refusal)
{
    /// <summary>True when the content is stored as parts that a root names.</summary>
    public bool Split => Objects.Count > 1;
}

/// <summary>The objects a split object is read from, each measured before any of its bytes are read.</summary>
public interface ISplitObjectSource
{
    /// <summary>The number of bytes held under a hash, found without reading them; null when no object is held there.</summary>
    /// <param name="hash">The object's content hash.</param>
    /// <param name="cancellationToken">Stops the measurement.</param>
    long? Length(string hash, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the bytes held under a hash into <paramref name="destination"/>, which is exactly as long as <see cref="Length"/> said;
    /// false, with nothing promised of the destination, when the object is no longer held or no longer that long. The bytes are checked
    /// against the hash by the reader, not here.
    /// </summary>
    /// <param name="hash">The object's content hash.</param>
    /// <param name="destination">Where the bytes go, exactly as long as the object.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    bool Read(string hash, Span<byte> destination, CancellationToken cancellationToken);
}

/// <summary>
/// From 0.9.4 (FIX-163), a content-addressed split object. Content larger than the object limit is stored as ordered parts, each an
/// object, every part but the last exactly the limit long and the last at least one byte and no longer, and a root object, the canonical
/// JSON <c>{"bytes": length, "digest": sha256, "format": "vericairn.split-object/1", "parts": [part hashes]}</c>, names them. The root's
/// hash names the content. Content within the limit is stored as that one object, as before, unless it would itself read as a root: such
/// content is wrapped in a root of one part, so that reading what was written always returns it. The count, the parts' lengths and the
/// root's bytes all follow from the content's length and the limit, so one content under one limit has one reference.
/// </summary>
/// <remarks>
/// A reader treats a root as untrusted. Before it reads any part it bounds the root's own size by <see cref="MaxRootBytes"/>, refuses a
/// length past the caller's maximum, requires the count to follow from the length and the root to be canonical, and measures every part
/// against the length its place holds. It then allocates the whole once, at the checked length, reads each part straight into it,
/// checks each part's hash, and checks the whole digest. A part may repeat, as identical stretches of content give identical parts; the
/// work is bounded by the checked length either way, and each distinct part is measured once. Every reader states the longest content it
/// accepts: there is no default.
/// </remarks>
public static class SplitObject
{
    /// <summary>The root object's format.</summary>
    public const string Format = "vericairn.split-object/1";

    private const int HashLength = 71;

    /// <summary>
    /// The exact size of the largest root of content up to <paramref name="maxBytes"/> bytes split at <paramref name="maxObjectBytes"/>:
    /// a reader refuses a larger root before it parses it.
    /// </summary>
    /// <param name="maxBytes">The longest content, at most the longest array.</param>
    /// <param name="maxObjectBytes">The part length, the estate's object limit unless a caller narrows it.</param>
    public static long MaxRootBytes(long maxBytes, long maxObjectBytes = EstateLayout.MaxObjectBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxObjectBytes);
        long length = Math.Clamp(maxBytes, 1, Array.MaxLength);
        return RootLength(length, Count(length, maxObjectBytes));
    }

    /// <summary>The objects that store <paramref name="content"/>, each at most <paramref name="maxObjectBytes"/> bytes, or why none can.</summary>
    /// <param name="content">The content to store.</param>
    /// <param name="maxObjectBytes">The largest object, the estate's object limit unless a caller narrows it.</param>
    public static SplitObjectPlan Plan(byte[] content, long maxObjectBytes = EstateLayout.MaxObjectBytes)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxObjectBytes);
        if (content.LongLength <= maxObjectBytes && !IsRoot(content)) return new(ContentHash.Sha256(content), [content], null);
        int count = Count(content.LongLength, maxObjectBytes);
        var parts = new List<byte[]>(count);
        for (int i = 0; i < count; i++)
        {
            long offset = i * maxObjectBytes;
            parts.Add(content.AsSpan((int)offset, (int)Math.Min(maxObjectBytes, content.LongLength - offset)).ToArray());
        }

        byte[] root = Root(content.LongLength, ContentHash.Sha256(content), [.. parts.Select(part => ContentHash.Sha256(part))]);
        return root.LongLength <= maxObjectBytes ? new(ContentHash.Sha256(root), [.. parts, root], null)
            : new(ContentHash.Sha256(root), [], $"Split into {count} parts, its root object would be {root.LongLength} bytes; the object limit is {maxObjectBytes} bytes.");
    }

    /// <summary>
    /// Stores <paramref name="content"/> through <paramref name="put"/>, which stores one object and returns its content hash: the parts
    /// first and the root last, so a root is stored only once every part it names is. Returns the reference that names the content.
    /// </summary>
    /// <param name="content">The content to store.</param>
    /// <param name="put">Stores one object and returns its content hash.</param>
    /// <param name="maxObjectBytes">The largest object, the estate's object limit unless a caller narrows it.</param>
    /// <exception cref="InvalidOperationException">The content cannot be stored within the limit, or the store named an object by another hash.</exception>
    public static string Write(byte[] content, Func<byte[], string> put, long maxObjectBytes = EstateLayout.MaxObjectBytes)
    {
        ArgumentNullException.ThrowIfNull(put);
        SplitObjectPlan plan = Plan(content, maxObjectBytes);
        if (plan.Refusal is not null) throw new InvalidOperationException(plan.Refusal);
        foreach (byte[] stored in plan.Objects)
        {
            if (put(stored) != ContentHash.Sha256(stored)) throw new InvalidOperationException("The store named an object by another hash than its content's.");
        }

        return plan.Reference;
    }

    /// <summary>
    /// True when <paramref name="bytes"/> are one JSON object with a top-level <c>format</c> member of <see cref="Format"/>, as a root
    /// is. Such bytes are read as a root, so <see cref="Plan"/> never stores content of this shape as itself.
    /// </summary>
    /// <param name="bytes">An object's bytes.</param>
    public static bool IsRoot(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;
            bool root = false;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                bool format = reader.ValueTextEquals("format"u8);
                if (!reader.Read()) return false;
                if (format && reader.TokenType == JsonTokenType.String && reader.ValueTextEquals(Format)) root = true;
                reader.Skip();
            }

            return reader.TokenType == JsonTokenType.EndObject && !reader.Read() && root;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The content <paramref name="reference"/> names: its object's bytes, or when that object is a root, its parts joined and verified as
    /// <see cref="Join"/> does. The object is measured first, and one past the object limit is not read.
    /// </summary>
    /// <param name="reference">The content's reference.</param>
    /// <param name="source">Where the objects are read from.</param>
    /// <param name="maxBytes">The longest content the caller accepts.</param>
    /// <param name="maxObjectBytes">The object limit and part length, the estate's unless a caller narrows it.</param>
    /// <param name="cancellationToken">Stops the read between objects.</param>
    public static SplitObjectContent Read(string reference, ISplitObjectSource source, long maxBytes, long maxObjectBytes = EstateLayout.MaxObjectBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxObjectBytes);
        if (!ContentHash.IsValid(reference)) return Refused(new(SplitObjectFault.InvalidReference, 0, reference ?? string.Empty, "The reference is not a supported content hash."));
        long? length = source.Length(reference, cancellationToken);
        if (length is null) return Refused(new(SplitObjectFault.Missing, 0, reference, "No object is held under the hash."));
        if (length > maxObjectBytes)
            return Refused(new(SplitObjectFault.Invalid, 0, reference, $"The object is {length} bytes, past the {maxObjectBytes}-byte object limit, and is not read."));
        byte[] bytes = new byte[length.Value];
        if (!source.Read(reference, bytes, cancellationToken)) return Refused(new(SplitObjectFault.Missing, 0, reference, "No object of the measured length is held under the hash."));
        if (ContentHash.Sha256(bytes) != reference) return Refused(new(SplitObjectFault.Altered, 0, reference, "The bytes held do not match the hash."));
        if (IsRoot(bytes)) return Join(bytes, source, maxBytes, maxObjectBytes, cancellationToken) with { Split = true };
        return bytes.LongLength > maxBytes
            ? Refused(new(SplitObjectFault.TooLarge, 0, reference, $"The object is {bytes.LongLength} bytes, more than the {maxBytes} the reader accepts."))
            : new(bytes, []);
    }

    /// <summary>
    /// The whole content a root names. The root is bounded, parsed, checked to be canonical and its count checked against its length, and
    /// every part measured, before any part is read; the whole is then allocated once and each part read into it and checked against its
    /// hash, and the whole against the root's digest. Every problem found is reported.
    /// </summary>
    /// <param name="root">The root object's bytes.</param>
    /// <param name="source">Where the parts are read from.</param>
    /// <param name="maxBytes">The longest content the caller accepts.</param>
    /// <param name="maxObjectBytes">The part length, the estate's object limit unless a caller narrows it.</param>
    /// <param name="cancellationToken">Stops the read between parts.</param>
    public static SplitObjectContent Join(ReadOnlySpan<byte> root, ISplitObjectSource source, long maxBytes, long maxObjectBytes = EstateLayout.MaxObjectBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxObjectBytes);
        long cap = Math.Clamp(maxBytes, 1, Array.MaxLength);
        if (Shape(root, cap, maxObjectBytes, source, cancellationToken, out Layout? layout) is { } refused) return new(null, refused) { Split = true };
        byte[] whole = new byte[layout!.Bytes];
        var problems = new List<SplitObjectProblem>();
        for (int i = 0; i < layout.Parts.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> part = whole.AsSpan((int)(i * maxObjectBytes), (int)layout.PartLength(i));
            if (Held(layout.Parts[i], i + 1, part, source, cancellationToken) is { } problem) problems.Add(problem);
        }

        if (problems.Count > 0) return new(null, problems) { Split = true };
        if (ContentHash.Sha256(whole) != layout.Digest) return new(null, [layout.Mismatch]) { Split = true };
        if (layout.Parts.Length == 1 && !IsRoot(whole)) return new(null, [Layout.Unwrapped]) { Split = true };
        return new(whole, []) { Split = true };
    }

    /// <summary>
    /// What an estate's check judges of a canonical root found in its objects, with work bounded by the root's own bytes: every distinct part
    /// it names is measured once, and none is read. A part missing or of another length than its place holds, or a part reference that is
    /// no content hash, is a problem; each part's bytes are the store's own hash check's to judge, and the whole digest a reader's. Null
    /// when the bytes are no canonical root, which a reader refuses as one and this check does not judge.
    /// </summary>
    internal static IReadOnlyList<SplitObjectProblem>? Verify(ReadOnlySpan<byte> root, ISplitObjectSource source, long maxObjectBytes = EstateLayout.MaxObjectBytes)
    {
        if (Shape(root, Array.MaxLength, maxObjectBytes, source, CancellationToken.None, out _) is { } refused)
            return refused.Any(p => p.Part > 0) ? refused : null;
        return [];
    }

    // The root's layout once every check that reads no part has passed, or every problem found before any part would be read.
    private static IReadOnlyList<SplitObjectProblem>? Shape(ReadOnlySpan<byte> root, long cap, long maxObjectBytes, ISplitObjectSource source,
        CancellationToken cancellationToken, out Layout? layout)
    {
        layout = null;
        long largest = MaxRootBytes(cap, maxObjectBytes);
        if (root.Length > largest)
            return [new(SplitObjectFault.Invalid, 0, string.Empty, $"The root is {root.Length} bytes; the root of content up to {cap} bytes is at most {largest}.")];
        if (!Fields(root, out long length, out string digest, out string[] parts))
            return [new(SplitObjectFault.Invalid, 0, string.Empty, $"The root is not a split object: it has exactly a format of {Format}, a positive length, a SHA-256 digest and parts named by hash.")];
        if (length > cap)
            return [new(SplitObjectFault.TooLarge, 0, string.Empty, $"The root names {length} bytes, more than the {cap} the reader accepts.")];
        int count = Count(length, maxObjectBytes);
        if (parts.Length != count)
            return [new(SplitObjectFault.Invalid, 0, string.Empty, $"The root names {parts.Length} parts; content of {length} bytes split at {maxObjectBytes} has {count}.")];
        if (!root.SequenceEqual(Root(length, digest, parts)))
            return [new(SplitObjectFault.Invalid, 0, string.Empty, "The root is not the canonical JSON of its fields.")];
        SplitObjectProblem[] references = [.. parts.Select((part, i) => (part, i)).Where(p => !ContentHash.IsValid(p.part))
            .Select(p => new SplitObjectProblem(SplitObjectFault.InvalidReference, p.i + 1, p.part, "The part's reference is not a supported content hash."))];
        if (references.Length > 0) return references;
        var shape = new Layout(length, digest, parts, maxObjectBytes);
        // Every part is measured before any is read or the whole allocated, each distinct part once: one missing, or of another length
        // than its place holds, stops the read here.
        var measured = new List<SplitObjectProblem>();
        var lengths = new Dictionary<string, long?>(StringComparer.Ordinal);
        for (int i = 0; i < parts.Length; i++)
        {
            bool first = !lengths.TryGetValue(parts[i], out long? held);
            if (first)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lengths[parts[i]] = held = source.Length(parts[i], cancellationToken);
            }

            // A missing part is named once, at its first place.
            if (held is null) { if (first) measured.Add(new(SplitObjectFault.Missing, i + 1, parts[i], "No object is held under the hash.")); }
            else if (held != shape.PartLength(i))
                measured.Add(new(SplitObjectFault.Invalid, i + 1, parts[i], $"The part holds {held} bytes; part {i + 1} of this content holds {shape.PartLength(i)}."));
        }

        if (measured.Count > 0) return measured;
        layout = shape;
        return null;
    }

    // Reads one measured part into its place and checks it against its hash.
    private static SplitObjectProblem? Held(string hash, int part, Span<byte> destination, ISplitObjectSource source, CancellationToken cancellationToken)
    {
        if (!source.Read(hash, destination, cancellationToken)) return new(SplitObjectFault.Missing, part, hash, "No object of the measured length is held under the hash.");
        return ContentHash.Sha256(destination) == hash ? null : new(SplitObjectFault.Altered, part, hash, "The bytes held do not match the hash.");
    }

    private static bool Fields(ReadOnlySpan<byte> root, out long length, out string digest, out string[] parts)
    {
        (length, digest, parts) = (0, string.Empty, []);
        try
        {
            var reader = new Utf8JsonReader(root);
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement element = document.RootElement;
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("format", out JsonElement format) || format.ValueKind != JsonValueKind.String || format.GetString() != Format ||
                !element.TryGetProperty("bytes", out JsonElement bytes) || bytes.ValueKind != JsonValueKind.Number || !bytes.TryGetInt64(out length) || length <= 0 ||
                !element.TryGetProperty("digest", out JsonElement hash) || hash.ValueKind != JsonValueKind.String || !ContentHash.IsValid(hash.GetString()) ||
                !element.TryGetProperty("parts", out JsonElement named) || named.ValueKind != JsonValueKind.Array || named.GetArrayLength() == 0 ||
                named.EnumerateArray().Any(p => p.ValueKind != JsonValueKind.String)) return false;
            digest = hash.GetString()!;
            parts = [.. named.EnumerateArray().Select(p => p.GetString()!)];
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The canonical root: members in ordinal order, no whitespace, the hashes as they are (each a lower-case content hash or refused).
    private static byte[] Root(long length, string digest, string[] parts)
    {
        var text = new StringBuilder("{\"bytes\":").Append(length.ToString(CultureInfo.InvariantCulture)).Append(",\"digest\":\"").Append(digest)
            .Append("\",\"format\":\"").Append(Format).Append("\",\"parts\":[");
        for (int i = 0; i < parts.Length; i++) text.Append(i == 0 ? "\"" : ",\"").Append(parts[i]).Append('"');
        return Encoding.UTF8.GetBytes(text.Append("]}").ToString());
    }

    private static long RootLength(long length, int count) => "{\"bytes\":".Length + length.ToString(CultureInfo.InvariantCulture).Length +
        ",\"digest\":\"".Length + HashLength + "\",\"format\":\"".Length + Format.Length + "\",\"parts\":[".Length + count * (HashLength + 2L) + (count - 1) + "]}".Length;

    // The number of parts content of this length is split into: one for content within the limit, which is split only to wrap it.
    private static int Count(long length, long maxObjectBytes) => (int)Math.Max(1, (length + maxObjectBytes - 1) / maxObjectBytes);

    private static SplitObjectContent Refused(SplitObjectProblem problem) => new(null, [problem]);

    private sealed record Layout(long Bytes, string Digest, string[] Parts, long PartBytes)
    {
        internal long PartLength(int index) => index < Parts.Length - 1 ? PartBytes : Bytes - (Parts.Length - 1) * PartBytes;

        internal SplitObjectProblem Mismatch => new(SplitObjectFault.Mismatch, 0, Digest, "The parts, joined in order, do not match the root's digest.");

        internal static SplitObjectProblem Unwrapped => new(SplitObjectFault.Invalid, 0, string.Empty, "A root of one part wraps only content that would itself read as a root.");
    }
}
