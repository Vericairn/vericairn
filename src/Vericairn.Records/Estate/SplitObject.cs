// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;

namespace Vericairn.Records.Estate;

/// <summary>Why a split object cannot be read.</summary>
public enum SplitObjectFault
{
    /// <summary>The root is not a split object of the supported format, or its fields, or the lengths of its parts, break its layout.</summary>
    Invalid,

    /// <summary>A part's reference is not a supported content hash.</summary>
    InvalidReference,

    /// <summary>A part is not held.</summary>
    Missing,

    /// <summary>The bytes held for a part do not match its hash.</summary>
    Altered,

    /// <summary>The parts, joined in the root's order, do not match the root's length and digest.</summary>
    Mismatch,
}

/// <summary>One reason a split object cannot be read.</summary>
/// <param name="Fault">What is wrong.</param>
/// <param name="Part">The part it concerns, counted from 1 in the root's order; 0 for the root itself or the whole content.</param>
/// <param name="Reference">The part's hash; for a mismatch the whole content's digest, and for the root itself its reference or nothing.</param>
/// <param name="Message">What is wrong, in words.</param>
public sealed record SplitObjectProblem(SplitObjectFault Fault, int Part, string Reference, string Message);

/// <summary>Content read back by its reference: the whole content, verified, or every problem that keeps it from being read.</summary>
/// <param name="Content">The whole content; null when a problem is reported.</param>
/// <param name="Problems">Every problem found; empty when the content is returned.</param>
public sealed record SplitObjectContent(byte[]? Content, IReadOnlyList<SplitObjectProblem> Problems);

/// <summary>What storing content writes.</summary>
/// <param name="Reference">The hash that names the content: its one object's, or its root's when it is split.</param>
/// <param name="Objects">The objects to store, in order: the one object, or every part and then the root; empty when refused.</param>
/// <param name="Refusal">Why the content cannot be stored within the object limit; null when it can.</param>
public sealed record SplitObjectPlan(string Reference, IReadOnlyList<byte[]> Objects, string? Refusal)
{
    /// <summary>True when the content is stored as parts that a root names.</summary>
    public bool Split => Objects.Count > 1;
}

/// <summary>
/// From 0.9.4 (FIX-163), a content-addressed split object: content larger than the estate's object limit is stored as ordered parts,
/// each an object within the limit, and a small root object, of format <c>vericairn.split-object/1</c>, names the parts by hash in
/// order with the whole content's byte length and SHA-256. The root's hash names the content. Content that fits one object is stored as
/// that one object, as before, and its own hash names it. Every part but the last holds the same number of bytes and the last holds at
/// least one and no more, so a content's split under one limit is unique. A reader verifies every part and the whole content's length
/// and digest, and refuses a missing, extra, reordered or altered part.
/// </summary>
public static class SplitObject
{
    /// <summary>The root object's format.</summary>
    public const string Format = "vericairn.split-object/1";

    private static readonly string[] RootFields = ["bytes", "digest", "format", "parts"];

    /// <summary>The objects that store <paramref name="content"/>, each at most <paramref name="maxObjectBytes"/> bytes, or why none can.</summary>
    /// <param name="content">The content to store.</param>
    /// <param name="maxObjectBytes">The largest object, the estate's object limit unless a caller narrows it.</param>
    public static SplitObjectPlan Plan(byte[] content, long maxObjectBytes = EstateLayout.MaxObjectBytes)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxObjectBytes);
        if (content.LongLength <= maxObjectBytes) return new(ContentHash.Sha256(content), [content], null);
        var parts = new List<byte[]>();
        for (long offset = 0; offset < content.LongLength; offset += maxObjectBytes)
            parts.Add(content.AsSpan((int)offset, (int)Math.Min(maxObjectBytes, content.LongLength - offset)).ToArray());
        byte[] root = CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(new JsonObject
        {
            ["format"] = Format, ["bytes"] = content.LongLength, ["digest"] = ContentHash.Sha256(content),
            ["parts"] = new JsonArray([.. parts.Select(part => (JsonNode)ContentHash.Sha256(part))]),
        }));
        // Each part fits by construction; only a root naming more parts than one object holds cannot.
        return root.LongLength <= maxObjectBytes ? new(ContentHash.Sha256(root), [.. parts, root], null)
            : new(ContentHash.Sha256(root), [], $"Split into {parts.Count} parts, its root object would be {root.LongLength} bytes; the object limit is {maxObjectBytes} bytes.");
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

    /// <summary>True when <paramref name="bytes"/> are a JSON object whose format is <see cref="Format"/>, a split object's root.</summary>
    /// <param name="bytes">An object's bytes.</param>
    public static bool IsRoot(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("format", out JsonElement format) &&
                format.ValueKind == JsonValueKind.String && format.GetString() == Format;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The content <paramref name="reference"/> names: its object's bytes, or when that object is a split object's root, its parts joined
    /// and verified as <see cref="Join"/> does.
    /// </summary>
    /// <param name="reference">The content's reference.</param>
    /// <param name="read">Returns the bytes held under a hash, or null when none are; it may throw <see cref="InvalidDataException"/> when the bytes held do not match the hash.</param>
    public static SplitObjectContent Read(string reference, Func<string, byte[]?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (!ContentHash.IsValid(reference)) return Refused(new(SplitObjectFault.InvalidReference, 0, reference ?? string.Empty, "The reference is not a supported content hash."));
        (byte[]? bytes, SplitObjectProblem? problem) = Held(reference, 0, read);
        if (problem is not null) return Refused(problem);
        return IsRoot(bytes) ? Join(bytes, read) : new(bytes, []);
    }

    /// <summary>
    /// The whole content a split object's root names: every part read in the root's order and checked against its hash, the parts' lengths
    /// checked against the layout, and the joined bytes checked against the root's length and digest. Every problem found is reported.
    /// </summary>
    /// <param name="root">The root object's bytes.</param>
    /// <param name="read">Returns the bytes held under a hash, or null when none are; it may throw <see cref="InvalidDataException"/> when the bytes held do not match the hash.</param>
    public static SplitObjectContent Join(ReadOnlyMemory<byte> root, Func<string, byte[]?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (!Fields(root, out long length, out string digest, out string[] parts))
            return Refused(new(SplitObjectFault.Invalid, 0, string.Empty, $"The root is not a split object: it has exactly a format of {Format}, a positive length, a SHA-256 digest and at least two parts named by hash."));
        var problems = new List<SplitObjectProblem>();
        var contents = new byte[parts.Length][];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!ContentHash.IsValid(parts[i]))
            {
                problems.Add(new(SplitObjectFault.InvalidReference, i + 1, parts[i], "The part's reference is not a supported content hash."));
                continue;
            }

            (byte[]? bytes, SplitObjectProblem? problem) = Held(parts[i], i + 1, read);
            if (problem is not null) problems.Add(problem);
            else contents[i] = bytes!;
        }

        if (problems.Count > 0) return new(null, problems);
        long size = contents[0].LongLength;
        for (int i = 0; i < parts.Length; i++)
        {
            long held = contents[i].LongLength;
            if (held == 0 || held > EstateLayout.MaxObjectBytes || (i < parts.Length - 1 ? held != size : held > size))
                problems.Add(new(SplitObjectFault.Invalid, i + 1, parts[i], $"The part holds {held} bytes; every part but the last holds the same number of bytes, at most the object limit, and the last at least one and no more."));
        }

        if (problems.Count > 0) return new(null, problems);
        SplitObjectProblem mismatch = new(SplitObjectFault.Mismatch, 0, digest, "The parts, joined in order, do not match the root's length and digest.");
        if (contents.Sum(c => c.LongLength) != length) return Refused(mismatch);
        byte[] whole = new byte[length];
        long offset = 0;
        foreach (byte[] content in contents)
        {
            content.CopyTo(whole, offset);
            offset += content.LongLength;
        }

        return ContentHash.Sha256(whole) == digest ? new(whole, []) : Refused(mismatch);
    }

    // The bytes held under a hash and verified against it, or why they cannot be.
    private static (byte[]? Bytes, SplitObjectProblem? Problem) Held(string hash, int part, Func<string, byte[]?> read)
    {
        byte[]? bytes;
        try
        {
            bytes = read(hash);
        }
        catch (InvalidDataException)
        {
            return (null, new(SplitObjectFault.Altered, part, hash, "The bytes held do not match the hash."));
        }

        if (bytes is null) return (null, new(SplitObjectFault.Missing, part, hash, "No object is held under the hash."));
        return ContentHash.Sha256(bytes) == hash ? (bytes, null) : (null, new(SplitObjectFault.Altered, part, hash, "The bytes held do not match the hash."));
    }

    private static bool Fields(ReadOnlyMemory<byte> root, out long length, out string digest, out string[] parts)
    {
        (length, digest, parts) = (0, string.Empty, []);
        try
        {
            using JsonDocument document = JsonDocument.Parse(root);
            JsonElement element = document.RootElement;
            if (element.ValueKind != JsonValueKind.Object || !element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(RootFields) ||
                element.GetProperty("format").ValueKind != JsonValueKind.String || element.GetProperty("format").GetString() != Format ||
                element.GetProperty("bytes").ValueKind != JsonValueKind.Number || !element.GetProperty("bytes").TryGetInt64(out length) || length <= 0 || length > Array.MaxLength ||
                element.GetProperty("digest").ValueKind != JsonValueKind.String || !ContentHash.IsValid(element.GetProperty("digest").GetString()) ||
                element.GetProperty("parts").ValueKind != JsonValueKind.Array || element.GetProperty("parts").GetArrayLength() < 2 ||
                element.GetProperty("parts").EnumerateArray().Any(p => p.ValueKind != JsonValueKind.String)) return false;
            digest = element.GetProperty("digest").GetString()!;
            parts = [.. element.GetProperty("parts").EnumerateArray().Select(p => p.GetString()!)];
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static SplitObjectContent Refused(SplitObjectProblem problem) => new(null, [problem]);
}
