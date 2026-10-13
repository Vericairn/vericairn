// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.Text.Json;

namespace Vericairn.Records.Model;

/// <summary>A reference to another record, optionally pinned to an exact hash.</summary>
/// <param name="Type">Record type.</param>
/// <param name="Id">Record ULID.</param>
/// <param name="Hash">Optional exact content hash the referrer relied on.</param>
public sealed record RecordReference(string Type, string Id, string? Hash)
{
    /// <inheritdoc />
    public override string ToString() => Hash is null ? $"{Type}:{Id}" : $"{Type}:{Id}@{Hash}";
}

/// <summary>A parsed record: the common envelope plus the type-specific body.</summary>
public sealed class RecordEnvelope
{
    /// <summary>Format identifier for record format 1.</summary>
    public const string Format1 = "vericairn.record/1";

    /// <summary>Creates an envelope.</summary>
    public RecordEnvelope(string type, string id, string at, string by, RecordReference? subject, string? supersedes, JsonElement body, string declaredHash, string computedHash, bool hasSignatures)
    {
        Type = type;
        Id = id;
        At = at;
        By = by;
        Subject = subject;
        Supersedes = supersedes;
        Body = body;
        DeclaredHash = declaredHash;
        ComputedHash = computedHash;
        HasSignatures = hasSignatures;
    }

    /// <summary>Record type, kebab-case.</summary>
    public string Type { get; }

    /// <summary>Record ULID.</summary>
    public string Id { get; }

    /// <summary>Authoring time, RFC 3339 UTC with second precision.</summary>
    public string At { get; }

    /// <summary>Authoring time parsed.</summary>
    public DateTimeOffset AtTime => DateTimeOffset.ParseExact(At, "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

    /// <summary>Authoring principal ULID.</summary>
    public string By { get; }

    /// <summary>What the record is about, when it is about another record.</summary>
    public RecordReference? Subject { get; }

    /// <summary>Predecessor record of the same type that this record replaces.</summary>
    public string? Supersedes { get; }

    /// <summary>Type-specific content (an independent clone, safe after the source document is disposed).</summary>
    public JsonElement Body { get; }

    /// <summary>Hash written in the file.</summary>
    public string DeclaredHash { get; }

    /// <summary>Hash computed from the canonical envelope.</summary>
    public string ComputedHash { get; }

    /// <summary>Whether the record carries signatures (not verified in format 1 without keys).</summary>
    public bool HasSignatures { get; }

    /// <summary>The repository-relative path this record must live at.</summary>
    public string ExpectedPath => $"records/{Type}/{AtTime:yyyy-MM}/{Id}.json";

    /// <summary>Reference to this record pinned at its computed hash.</summary>
    public RecordReference Reference => new(Type, Id, ComputedHash);
}
