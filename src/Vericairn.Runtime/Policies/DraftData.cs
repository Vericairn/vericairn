// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vericairn.Runtime.Policies;

// The estate-draft/1 data subset: mappings, sequences and string scalars, including
// flow collections and quoted or block strings. No YAML constructors or implicit types.
internal sealed class DraftData
{
    private readonly string[] lines;
    private int position;
    private int nodes;

    private DraftData(string[] lines) => this.lines = lines;

    internal static (JsonElement Metadata, string Body) Read(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > 1024 * 1024) throw Bad("The draft exceeds the source byte bound.");
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length < 3 || lines[0] != "---") throw Bad("A draft needs a delimited front-matter mapping.");
        int end = System.Array.IndexOf(lines, "---", 1);
        if (end < 0) throw Bad("The draft front matter is not closed.");
        var reader = new DraftData(lines[1..end]);
        reader.Skip();
        JsonNode metadata = reader.position < reader.lines.Length && reader.lines[reader.position].StartsWith('{')
            ? reader.Value(reader.lines[reader.position++], 0, 0) : reader.Block(0, 0);
        reader.Skip();
        if (reader.position != reader.lines.Length || metadata is not JsonObject) throw Bad("The draft front matter must be one mapping.");
        return (JsonSerializer.SerializeToElement(metadata), string.Join('\n', lines[(end + 1)..]));
    }

    private JsonNode Block(int indent, int depth)
    {
        Bound(depth);
        if (position >= lines.Length || Indent(lines[position]) != indent) throw Bad("Invalid data indentation.");
        bool sequence = Sequence(lines[position][indent..]);
        JsonNode result = sequence ? new JsonArray() : new JsonObject();
        while (position < lines.Length && Indent(lines[position]) == indent)
        {
            string line = lines[position++][indent..];
            if (Sequence(line) != sequence) throw Bad("Mixed mapping and sequence entries.");
            if (sequence)
            {
                string item = line.Length == 1 ? "" : line[2..].Trim();
                if (Pair(item) is { } pair)
                {
                    var map = new JsonObject();
                    Add(map, pair.Key, Value(pair.Value, indent + 2, depth + 1));
                    Skip();
                    if (position < lines.Length && Indent(lines[position]) > indent)
                    {
                        if (Block(indent + 2, depth + 1) is not JsonObject rest) throw Bad("Expected a sequence item's mapping.");
                        foreach (var field in rest) Add(map, field.Key, field.Value!.DeepClone());
                    }
                    ((JsonArray)result).Add(map);
                }
                else ((JsonArray)result).Add(Value(item, indent, depth + 1));
            }
            else
            {
                var pair = Pair(line) ?? throw Bad("Expected a mapping key and value.");
                Add((JsonObject)result, pair.Key, Value(pair.Value, indent, depth + 1));
            }
            Skip();
        }
        return result;
    }

    private JsonNode Value(string value, int indent, int depth)
    {
        Bound(depth);
        if (value is "|" or "|-" or "|+" or ">" or ">-" or ">+")
        {
            var body = new List<string>();
            int start = position;
            while (position < lines.Length && (string.IsNullOrWhiteSpace(lines[position]) || Indent(lines[position]) > indent)) position++;
            int margin = lines[start..position].Where(l => !string.IsNullOrWhiteSpace(l)).Select(Indent).DefaultIfEmpty(indent + 1).Min();
            foreach (string line in lines[start..position]) body.Add(line.Length >= margin ? line[margin..] : "");
            string text = string.Join(value[0] == '|' ? "\n" : " ", body);
            return JsonValue.Create(value.EndsWith('-') ? text.TrimEnd('\n') : text.TrimEnd('\n') + "\n")!;
        }
        if (value.Length == 0 || value.StartsWith('#'))
        {
            Skip();
            if (position == lines.Length || Indent(lines[position]) <= indent) throw Bad("Null data fields are unsupported.");
            return Block(Indent(lines[position]), depth + 1);
        }
        int at = 0;
        JsonNode result = Flow(value, ref at, depth + 1, false);
        Space(value, ref at);
        if (at != value.Length && value[at] != '#') throw Bad("Trailing data after a value.");
        return result;
    }

    private JsonNode Flow(string value, ref int at, int depth, bool nested)
    {
        Bound(depth);
        Space(value, ref at);
        if (at == value.Length) throw Bad("Missing data value.");
        char first = value[at];
        if (first is '{' or '[')
        {
            at++;
            JsonNode result = first == '{' ? new JsonObject() : new JsonArray();
            char closing = first == '{' ? '}' : ']';
            Space(value, ref at);
            while (at < value.Length && value[at] != closing)
            {
                if (result is JsonObject map)
                {
                    string key = Key(value, ref at);
                    Space(value, ref at);
                    if (at == value.Length || value[at++] != ':') throw Bad("Missing flow mapping separator.");
                    Add(map, key, Flow(value, ref at, depth + 1, true));
                }
                else ((JsonArray)result).Add(Flow(value, ref at, depth + 1, true));
                Space(value, ref at);
                if (at == value.Length || value[at] == closing) break;
                if (value[at++] != ',') throw Bad("Missing flow collection separator.");
                Space(value, ref at);
                if (at == value.Length || value[at] == closing) throw Bad("Empty flow item.");
            }
            if (at == value.Length || value[at++] != closing) throw Bad("Unclosed flow collection.");
            return result;
        }
        if (first is '\'' or '"') return JsonValue.Create(Quoted(value, ref at))!;
        int start = at;
        while (at < value.Length && (!nested || value[at] is not (',' or ']' or '}')) && !(value[at] == '#' && (at == start || value[at - 1] == ' '))) at++;
        string scalar = value[start..at].Trim();
        if (scalar.Length == 0 || scalar[0] is '!' or '&' or '*' or '%' or '@' or '`' or '|' or '>' || scalar.Contains(": ", StringComparison.Ordinal) ||
            scalar is "null" or "Null" or "NULL" or "~" or "true" or "True" or "TRUE" or "false" or "False" or "FALSE" or "yes" or "no" or "on" or "off" ||
            decimal.TryParse(scalar, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            throw Bad("Only string scalars are supported; quote ambiguous values. Tags, anchors and aliases are forbidden.");
        return JsonValue.Create(scalar)!;
    }

    private static string Quoted(string value, ref int at)
    {
        int start = at;
        char quote = value[at++];
        var result = new StringBuilder();
        while (at < value.Length)
        {
            char c = value[at++];
            if (c == quote)
            {
                if (quote == '\'' && at < value.Length && value[at] == '\'') { result.Append('\''); at++; continue; }
                if (quote == '\'') return result.ToString();
                try { return JsonSerializer.Deserialize<string>(value[start..at])!; }
                catch (JsonException) { throw Bad("Unsupported quoted string escape."); }
            }
            if (quote == '"' && c == '\\')
            {
                if (at == value.Length) break;
                at++;
            }
            else result.Append(c);
        }
        throw Bad("Unclosed quoted string.");
    }

    private static string Key(string value, ref int at)
    {
        Space(value, ref at);
        if (at < value.Length && value[at] is '\'' or '"') return Quoted(value, ref at);
        int start = at;
        while (at < value.Length && (char.IsAsciiLetterOrDigit(value[at]) || value[at] is '-' or '_')) at++;
        if (at == start) throw Bad("Unsupported mapping key.");
        return value[start..at];
    }

    private static (string Key, string Value)? Pair(string line)
    {
        if (line.Length == 0 || line[0] is '{' or '[') return null;
        int at = 0;
        string key = Key(line, ref at);
        Space(line, ref at);
        if (at == line.Length || line[at] != ':' || at + 1 < line.Length && line[at + 1] != ' ') return null;
        return (key, line[(at + 1)..].Trim());
    }

    private static void Add(JsonObject map, string key, JsonNode value)
    {
        if (key == "<<" || map.ContainsKey(key)) throw Bad("Duplicate or merged front-matter keys are forbidden.");
        map.Add(key, value);
    }

    private static int Indent(string line)
    {
        int count = line.TakeWhile(c => c == ' ').Count();
        if (count < line.Length && line[count] == '\t') throw Bad("Tabs cannot indent front matter.");
        return count;
    }

    private void Skip() { while (position < lines.Length && (string.IsNullOrWhiteSpace(lines[position]) || lines[position].TrimStart().StartsWith('#'))) position++; }
    private static bool Sequence(string line) => line == "-" || line.StartsWith("- ", StringComparison.Ordinal);
    private static void Space(string value, ref int at) { while (at < value.Length && value[at] == ' ') at++; }
    private void Bound(int depth) { if (depth > 16 || ++nodes > 10000) throw Bad("The draft data bound was exhausted."); }
    private static PolicyInputException Bad(string message) => new("estate-draft/1: " + message);
}
