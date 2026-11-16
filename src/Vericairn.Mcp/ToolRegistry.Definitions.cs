// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vericairn.Mcp;

/// <summary>
/// From 0.9.4 (MCP-07): the tool definitions this build publishes and their SHA-256, so a client can tell whether the tool set changed
/// between releases. initialize's <c>_meta</c> carries the hash, the release notes list every definition's hash, and the MCP guide
/// keeps an entry for each.
/// </summary>
public sealed partial class ToolRegistry
{
    private static readonly Lazy<(JsonArray Definitions, string Hash)> Published = new(() =>
    {
        var definitions = new JsonArray(new ToolRegistry().tools.OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => (JsonNode)t.Describe()).ToArray());
        return (definitions, Hash(definitions));
    });

    /// <summary>
    /// Every tool this build defines, ordered by name, as tools/list describes it over standard input and output: the build's whole
    /// set, whichever of them a connection sees, without the network session's annotation of its writes. A copy.
    /// </summary>
    public static JsonArray Definitions() => (JsonArray)Published.Value.Definitions.DeepClone();

    /// <summary>
    /// The SHA-256 of <see cref="Definitions"/> in RFC 8785 canonical JSON (members in ordinal order, no insignificant whitespace),
    /// as sha256:&lt;hex&gt;: the value initialize's <c>_meta</c> carries as <c>vericairn/toolDefinitions</c>.
    /// </summary>
    public static string DefinitionsHash => Published.Value.Hash;

    // How many definitions the build publishes, without copying them.
    internal static int DefinitionsCount => Published.Value.Definitions.Count;

    /// <summary>
    /// The SHA-256 of one tool's definition, as tools/list describes it (<see cref="ToolDefinition.Describe"/>) and
    /// <see cref="Definitions"/> lists it, in the same canonical form, as sha256:&lt;hex&gt;.
    /// </summary>
    public static string DefinitionHash(JsonObject definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Hash(definition);
    }

    private static string Hash(JsonNode node)
    {
        using JsonDocument document = JsonDocument.Parse(node.ToJsonString());
        var text = new StringBuilder();
        Canonical(document.RootElement, text);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    // RFC 8785 for what a definition holds: objects, arrays, strings, booleans, null and integers.
    private static void Canonical(JsonElement element, StringBuilder text)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                text.Append('{');
                bool first = true;
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first) text.Append(',');
                    first = false;
                    Quoted(property.Name, text);
                    text.Append(':');
                    Canonical(property.Value, text);
                }

                text.Append('}');
                break;
            case JsonValueKind.Array:
                text.Append('[');
                bool head = true;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (!head) text.Append(',');
                    head = false;
                    Canonical(item, text);
                }

                text.Append(']');
                break;
            case JsonValueKind.String:
                Quoted(element.GetString()!, text);
                break;
            case JsonValueKind.Number when element.TryGetInt64(out long number):
                text.Append(number.ToString(CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.True:
                text.Append("true");
                break;
            case JsonValueKind.False:
                text.Append("false");
                break;
            case JsonValueKind.Null:
                text.Append("null");
                break;
            default:
                throw new InvalidOperationException("A tool definition holds a value that has no canonical form here.");
        }
    }

    private static void Quoted(string value, StringBuilder text)
    {
        text.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\b': text.Append("\\b"); break;
                case '\f': text.Append("\\f"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                case < ' ': text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); break;
                default: text.Append(c); break;
            }
        }

        text.Append('"');
    }
}
