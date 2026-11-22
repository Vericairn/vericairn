// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>
/// From 0.9.4 (DOCS-46): one member of a fixed-shape object and the rule its validator reads it by. The validator reads the member
/// through the rule, and <c>records describe</c> and the record reference describe it from the same rule, so the two cannot drift.
/// </summary>
/// <param name="Name">The member's name.</param>
/// <param name="Kind">Its kind, in the words the record reference uses: string, string[], ulid, ulid[], boolean, integer, object, object[], or the values of an enumeration joined by |.</param>
/// <param name="Required">Whether the member is required in the formats that have it.</param>
/// <param name="Description">What the member means.</param>
/// <param name="SinceFormat">The record format that introduced it.</param>
internal sealed record MemberRule(string Name, string Kind, bool Required, string Description, long SinceFormat = RecordFormats.First)
{
    /// <summary>For a string, its longest length; for an array of strings or ids, each item's.</summary>
    public int MaxLength { get; init; }

    /// <summary>For an array, whether it may be empty.</summary>
    public bool AllowEmpty { get; init; }

    /// <summary>For an array, the most items it holds, when bounded.</summary>
    public int? MaxItems { get; init; }

    /// <summary>For an integer, the least value.</summary>
    public long? Min { get; init; }

    /// <summary>A limit the shape alone does not state, in words, such as the distinct values an array's items take.</summary>
    public string? Limit { get; init; }

    /// <summary>For an object or an array of objects of fixed members: each member.</summary>
    public IReadOnlyList<MemberRule> Members { get; init; } = [];

    private bool IsArray => Kind.EndsWith("[]", StringComparison.Ordinal);

    /// <summary>The member's description, as <c>records describe</c> and the record reference show it.</summary>
    public FieldDescription Describe() => new(Name, Kind, Required, Description, SinceFormat)
    {
        EmptyAllowed = IsArray ? AllowEmpty : null,
        Limits = Limits(),
        Members = [.. Members.Select(m => m.Describe())],
    };

    /// <summary>Reads a string member: 1 to <see cref="MaxLength"/> characters.</summary>
    public string? Text(BodyReader reader) => Reader(reader).Text(Name, Required, MaxLength);

    /// <summary>Reads an array of strings: each item 1 to <see cref="MaxLength"/> characters, empty only when <see cref="AllowEmpty"/>.</summary>
    public IReadOnlyList<string>? Strings(BodyReader reader) => Reader(reader).StringArray(Name, Required, AllowEmpty, MaxLength);

    /// <summary>Reads an array of objects, empty only when <see cref="AllowEmpty"/>.</summary>
    public IReadOnlyList<BodyReader>? Objects(BodyReader reader) => Reader(reader).NestedArray(Name, Required, AllowEmpty);

    /// <summary>Reads an object.</summary>
    public BodyReader? Object(BodyReader reader) => Reader(reader).Nested(Name, Required);

    /// <summary>Reads a ULID.</summary>
    public string? Id(BodyReader reader) => Reader(reader).Ulid(Name, Required);

    /// <summary>Reads one of an enumeration's values.</summary>
    public string? OneOf(BodyReader reader) => Reader(reader).Enum(Name, Required, Kind.Split('|'));

    /// <summary>Reads a boolean.</summary>
    public bool? Flag(BodyReader reader) => Reader(reader).Flag(Name, Required);

    /// <summary>Reads an integer of at least <see cref="Min"/>.</summary>
    public long? Number(BodyReader reader) => Reader(reader).Number(Name, Required, Min ?? long.MinValue, int.MaxValue);

    private static BodyReader Reader(BodyReader reader) => reader ?? throw new ArgumentNullException(nameof(reader));

    private string? Limits()
    {
        var parts = new List<string>();
        string characters = MaxLength > 0 ? "1 to " + MaxLength.ToString("N0", CultureInfo.InvariantCulture) + " characters" : string.Empty;
        if (MaxLength > 0 && Kind is "string") parts.Add(characters);
        if (MaxLength > 0 && Kind is "string[]") parts.Add("each item " + characters);
        if (MaxItems is int items) parts.Add("at most " + items.ToString(CultureInfo.InvariantCulture) + " items");
        if (Min is long min) parts.Add("at least " + min.ToString(CultureInfo.InvariantCulture));
        if (Limit is not null) parts.Add(Limit);
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }
}
