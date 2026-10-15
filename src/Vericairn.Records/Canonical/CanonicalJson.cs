// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Vericairn.Records.Canonical;

/// <summary>Thrown when a document cannot be represented in canonical form.</summary>
public sealed class CanonicalJsonException : Exception
{
    /// <summary>Creates the exception.</summary>
    public CanonicalJsonException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public CanonicalJsonException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and cause.</summary>
    public CanonicalJsonException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Canonical JSON for the record formats (format 2 keeps format 1's): RFC 8785 member ordering and string escaping, restricted to
/// objects, arrays, strings, booleans, null and plain safe integers (|n| ≤ 2^53 − 1).
/// The compact form is what gets hashed; the pretty form is what gets stored in files.
/// </summary>
public static class CanonicalJson
{
    /// <summary>Largest integer magnitude that round-trips through every JSON implementation.</summary>
    public const long MaxSafeInteger = 9_007_199_254_740_991;

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64,
    };

    /// <summary>Parses UTF-8 JSON strictly (no comments, no trailing commas, no invalid UTF-8 anywhere).</summary>
    public static JsonDocument Parse(ReadOnlyMemory<byte> utf8)
    {
        if (!System.Text.Unicode.Utf8.IsValid(utf8.Span))
        {
            throw new CanonicalJsonException("The document is not valid UTF-8.");
        }

        try
        {
            return JsonDocument.Parse(utf8, ParseOptions);
        }
        catch (JsonException exception)
        {
            throw new CanonicalJsonException("The document is not valid JSON: " + exception.Message, exception);
        }
    }

    /// <summary>Serializes the compact canonical form used for hashing.</summary>
    public static string Serialize(JsonElement element, IReadOnlySet<string>? skipTopLevelKeys = null)
    {
        var builder = new StringBuilder();
        WriteValue(builder, element, indent: -1, depth: 0, skipTopLevelKeys);
        return builder.ToString();
    }

    /// <summary>Serializes the readable canonical form used for files: two-space indent, LF, trailing newline.</summary>
    public static string SerializePretty(JsonElement element)
    {
        var builder = new StringBuilder();
        WriteValue(builder, element, indent: 2, depth: 0, skipTopLevelKeys: null);
        builder.Append('\n');
        return builder.ToString();
    }

    /// <summary>UTF-8 bytes of <see cref="Serialize"/>.</summary>
    public static byte[] SerializeUtf8(JsonElement element, IReadOnlySet<string>? skipTopLevelKeys = null) =>
        Encoding.UTF8.GetBytes(Serialize(element, skipTopLevelKeys));

    /// <summary>UTF-8 bytes of <see cref="SerializePretty"/>.</summary>
    public static byte[] SerializePrettyUtf8(JsonElement element) => Encoding.UTF8.GetBytes(SerializePretty(element));

    private static void WriteValue(StringBuilder builder, JsonElement element, int indent, int depth, IReadOnlySet<string>? skipTopLevelKeys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(builder, element, indent, depth, depth == 0 ? skipTopLevelKeys : null);
                break;
            case JsonValueKind.Array:
                WriteArray(builder, element, indent, depth);
                break;
            case JsonValueKind.String:
                WriteString(builder, element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Number:
                WriteNumber(builder, element);
                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            case JsonValueKind.Null:
                builder.Append("null");
                break;
            default:
                throw new CanonicalJsonException($"Unsupported JSON value kind {element.ValueKind}.");
        }
    }

    private static void WriteObject(StringBuilder builder, JsonElement element, int indent, int depth, IReadOnlySet<string>? skipKeys)
    {
        var properties = new List<JsonProperty>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new CanonicalJsonException($"Duplicate object key '{property.Name}'.");
            }

            if (skipKeys is null || !skipKeys.Contains(property.Name))
            {
                properties.Add(property);
            }
        }

        properties.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        if (properties.Count == 0)
        {
            builder.Append("{}");
            return;
        }

        builder.Append('{');
        for (int i = 0; i < properties.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            NewLine(builder, indent, depth + 1);
            WriteString(builder, properties[i].Name);
            builder.Append(':');
            if (indent >= 0)
            {
                builder.Append(' ');
            }

            WriteValue(builder, properties[i].Value, indent, depth + 1, null);
        }

        NewLine(builder, indent, depth);
        builder.Append('}');
    }

    private static void WriteArray(StringBuilder builder, JsonElement element, int indent, int depth)
    {
        int count = element.GetArrayLength();
        if (count == 0)
        {
            builder.Append("[]");
            return;
        }

        builder.Append('[');
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (index++ > 0)
            {
                builder.Append(',');
            }

            NewLine(builder, indent, depth + 1);
            WriteValue(builder, item, indent, depth + 1, null);
        }

        NewLine(builder, indent, depth);
        builder.Append(']');
    }

    private static void NewLine(StringBuilder builder, int indent, int depth)
    {
        if (indent < 0)
        {
            return;
        }

        builder.Append('\n');
        builder.Append(' ', indent * depth);
    }

    private static void WriteNumber(StringBuilder builder, JsonElement element)
    {
        string raw = element.GetRawText();
        if (!element.TryGetInt64(out long value) || raw.Contains('.', StringComparison.Ordinal) || raw.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            throw new CanonicalJsonException($"Number '{raw}' is not a plain integer; records allow only safe integers.");
        }

        if (value > MaxSafeInteger || value < -MaxSafeInteger)
        {
            throw new CanonicalJsonException($"Integer {raw} is outside the safe range ±{MaxSafeInteger}.");
        }

        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteString(StringBuilder builder, string text)
    {
        builder.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
