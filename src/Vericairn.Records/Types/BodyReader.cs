// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>Reads typed fields out of a record body, turning every problem into a finding.</summary>
public sealed class BodyReader
{
    private readonly JsonElement element;
    private readonly string location;
    private readonly FindingList findings;
    private readonly HashSet<string> declared = new(StringComparer.Ordinal);

    /// <summary>Creates a reader over an object element.</summary>
    public BodyReader(JsonElement element, string location, FindingList findings)
    {
        this.element = element;
        this.location = location;
        this.findings = findings ?? throw new ArgumentNullException(nameof(findings));
    }

    /// <summary>The element being read.</summary>
    public JsonElement Element => element;

    /// <summary>Where findings are reported.</summary>
    public string Location => location;

    /// <summary>True when the key exists.</summary>
    public bool Has(string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out _);

    /// <summary>Reads a string.</summary>
    public string? Text(string name, bool required, int maxLength = 4000, Regex? pattern = null, string? patternHint = null)
    {
        JsonElement? value = Get(name, required, JsonValueKind.String);
        if (value is null)
        {
            return null;
        }

        string text = value.Value.GetString() ?? string.Empty;
        if (text.Length == 0 || text.Length > maxLength)
        {
            findings.Error("body.length", Field(name), $"'{name}' must be 1 to {maxLength} characters.");
            return null;
        }

        if (pattern is not null && !pattern.IsMatch(text))
        {
            findings.Error("body.pattern", Field(name), $"'{name}' must match {patternHint ?? pattern.ToString()}.");
            return null;
        }

        return text;
    }

    /// <summary>Reads a safe integer.</summary>
    public long? Number(string name, bool required, long min = long.MinValue, long max = long.MaxValue)
    {
        JsonElement? value = Get(name, required, JsonValueKind.Number);
        if (value is null)
        {
            return null;
        }

        if (!value.Value.TryGetInt64(out long number) || number < min || number > max)
        {
            findings.Error("body.range", Field(name), $"'{name}' must be an integer between {min} and {max}.");
            return null;
        }

        return number;
    }

    /// <summary>Reads a boolean.</summary>
    public bool? Flag(string name, bool required)
    {
        if (!TryGetRaw(name, required, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        findings.Error("body.type", Field(name), $"'{name}' must be true or false.");
        return null;
    }

    /// <summary>Reads a string restricted to a set of values.</summary>
    public string? Enum(string name, bool required, params string[] allowed)
    {
        string? text = Text(name, required, 200);
        if (text is null)
        {
            return null;
        }

        if (Array.IndexOf(allowed, text) < 0)
        {
            findings.Error("body.enum", Field(name), $"'{name}' must be one of: {string.Join(", ", allowed)}.");
            return null;
        }

        return text;
    }

    /// <summary>Reads a ULID.</summary>
    public string? Ulid(string name, bool required)
    {
        string? text = Text(name, required, Identity.Ulid.Length);
        if (text is null)
        {
            return null;
        }

        if (!Identity.Ulid.IsValid(text))
        {
            findings.Error("body.ulid", Field(name), $"'{name}' must be a ULID.");
            return null;
        }

        return text;
    }

    /// <summary>Reads a content hash.</summary>
    public string? Hash(string name, bool required)
    {
        string? text = Text(name, required, ContentHash.TextLength);
        if (text is null)
        {
            return null;
        }

        if (!ContentHash.IsValid(text))
        {
            findings.Error("body.hash", Field(name), $"'{name}' must look like sha256:<64 hex digits>.");
            return null;
        }

        return text;
    }

    /// <summary>Reads a record timestamp.</summary>
    public string? Timestamp(string name, bool required)
    {
        string? text = Text(name, required, 20);
        if (text is null)
        {
            return null;
        }

        if (!RecordParser.IsTimestamp(text))
        {
            findings.Error("body.timestamp", Field(name), $"'{name}' must look like 2026-10-13T14:00:00Z.");
            return null;
        }

        return text;
    }

    /// <summary>Reads an array of strings.</summary>
    public IReadOnlyList<string>? StringArray(string name, bool required, bool allowEmpty = true, int maxItemLength = 4000, Regex? itemPattern = null, string? patternHint = null)
    {
        JsonElement? value = Get(name, required, JsonValueKind.Array);
        if (value is null)
        {
            return null;
        }

        var items = new List<string>();
        int index = 0;
        foreach (JsonElement item in value.Value.EnumerateArray())
        {
            string itemLocation = $"{Field(name)}[{index++}]";
            if (item.ValueKind != JsonValueKind.String)
            {
                findings.Error("body.type", itemLocation, "Items must be strings.");
                continue;
            }

            string text = item.GetString() ?? string.Empty;
            if (text.Length == 0 || text.Length > maxItemLength)
            {
                findings.Error("body.length", itemLocation, $"Items must be 1 to {maxItemLength} characters.");
                continue;
            }

            if (itemPattern is not null && !itemPattern.IsMatch(text))
            {
                findings.Error("body.pattern", itemLocation, $"Items must match {patternHint ?? itemPattern.ToString()}.");
                continue;
            }

            items.Add(text);
        }

        if (!allowEmpty && items.Count == 0)
        {
            findings.Error("body.empty", Field(name), $"'{name}' must have at least one item.");
            return null;
        }

        return items;
    }

    /// <summary>Reads a nested object as a new reader.</summary>
    public BodyReader? Nested(string name, bool required)
    {
        JsonElement? value = Get(name, required, JsonValueKind.Object);
        return value is null ? null : new BodyReader(value.Value, Field(name), findings);
    }

    /// <summary>Reads an array of objects as readers.</summary>
    public IReadOnlyList<BodyReader>? NestedArray(string name, bool required, bool allowEmpty = true)
    {
        JsonElement? value = Get(name, required, JsonValueKind.Array);
        if (value is null)
        {
            return null;
        }

        var readers = new List<BodyReader>();
        int index = 0;
        foreach (JsonElement item in value.Value.EnumerateArray())
        {
            string itemLocation = $"{Field(name)}[{index++}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                findings.Error("body.type", itemLocation, "Items must be objects.");
                continue;
            }

            readers.Add(new BodyReader(item, itemLocation, findings));
        }

        if (!allowEmpty && readers.Count == 0)
        {
            findings.Error("body.empty", Field(name), $"'{name}' must have at least one item.");
            return null;
        }

        return readers;
    }

    /// <summary>Declares an optional key as known without reading it.</summary>
    public void Allow(string name) => declared.Add(name);

    /// <summary>Reports every key that no reader call declared.</summary>
    public void NoOtherKeys()
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!declared.Contains(property.Name))
            {
                findings.Error("body.unknown-key", Field(property.Name), $"'{property.Name}' is not a field of this record type.");
            }
        }
    }

    /// <summary>Adds an error about a field.</summary>
    public void Error(string code, string name, string message) => findings.Error(code, Field(name), message);

    /// <summary>Adds a warning about a field.</summary>
    public void Warning(string code, string name, string message) => findings.Warning(code, Field(name), message);

    private string Field(string name) => $"{location}.{name}";

    private bool TryGetRaw(string name, bool required, out JsonElement value)
    {
        declared.Add(name);
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        if (required)
        {
            findings.Error("body.missing", Field(name), $"'{name}' is required.");
        }

        return false;
    }

    private JsonElement? Get(string name, bool required, JsonValueKind kind)
    {
        if (!TryGetRaw(name, required, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind != kind)
        {
            findings.Error("body.type", Field(name), $"'{name}' must be {Describe(kind)}.");
            return null;
        }

        return value;
    }

    private static string Describe(JsonValueKind kind) => kind switch
    {
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "an integer",
        JsonValueKind.Array => "an array",
        JsonValueKind.Object => "an object",
        _ => kind.ToString(),
    };
}
