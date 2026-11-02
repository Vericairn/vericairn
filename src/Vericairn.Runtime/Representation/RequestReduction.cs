// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Representation;

namespace Vericairn.Runtime.Representation;

/// <summary>The native wire boundary. All bytes outside verified eligible string spans pass through untouched.</summary>
internal static class RequestReduction
{
    internal sealed record Result(byte[] Body, long BeforeBytes, long AfterBytes);

    internal static Result Reduce(byte[] body, Func<string, byte[]?> original, bool enabled = true)
    {
        try
        {
            Node root = Parse(body);
            List<Node>? strings = ToolResults(root);
            if (strings is null) return new(body, 0, 0);
            var edits = new List<(int Start, int End, byte[] Value)>();
            long before = 0, after = 0;
            foreach (Node token in strings)
            {
                byte[] decoded = Encoding.UTF8.GetBytes(token.Text!);
                Node envelope;
                IReadOnlyList<RepresentationBlock>? blocks;
                try
                {
                    using JsonDocument document = JsonDocument.Parse(decoded, Options);
                    blocks = RepresentationContract.Read(document.RootElement);
                    if (blocks is null) continue;
                    envelope = Parse(decoded);
                }
                catch (JsonException) { continue; }
                RepresentationReduction result = enabled ? LosslessAdapter.Reduce(blocks, original) : LosslessAdapter.Bypass(blocks, original);
                before += result.BeforeBytes;
                after += result.AfterBytes;
                int[] map = StringBoundaries(body.AsSpan(token.Start, token.End - token.Start), decoded.Length);
                for (int i = 0; i < blocks.Count; i++)
                {
                    if (result.Text[i] == blocks[i].Text) continue;
                    Node value = envelope.Fields["blocks"].Items[i].Fields["text"];
                    // Encode just this changed inner token into the outer string. Exact siblings retain even their escape spelling.
                    string replacement = JsonSerializer.Serialize(result.Text[i], WireStrings);
                    byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(replacement, WireStrings);
                    edits.Add((token.Start + map[value.Start], token.Start + map[value.End], encoded[1..^1]));
                }
            }
            if (edits.Count == 0) return new(body, before, after);
            using var output = new MemoryStream();
            int offset = 0;
            foreach (var edit in edits.OrderBy(e => e.Start))
            {
                output.Write(body.AsSpan(offset, edit.Start - offset));
                output.Write(edit.Value);
                offset = edit.End;
            }
            output.Write(body.AsSpan(offset));
            byte[] reduced = output.ToArray();
            // Escape expansion can outweigh a content saving. Never enlarge a model request.
            return reduced.Length < body.Length ? new(reduced, before, after) : new(body, before, before);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            return new(body, 0, 0);
        }
    }

    private static readonly JsonDocumentOptions Options = new() { AllowDuplicateProperties = false, MaxDepth = 64 };
    private static readonly JsonSerializerOptions WireStrings = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static List<Node>? ToolResults(Node root)
    {
        if (root.Type != JsonTokenType.StartObject || Protected(root) || root.Fields.ContainsKey("input") == root.Fields.ContainsKey("messages")) return null;
        var results = new List<Node>();
        if (root.Fields.TryGetValue("input", out Node? input))
        {
            if (input.Type != JsonTokenType.StartArray) return null;
            foreach (Node item in input.Items)
            {
                if (Protected(item)) continue;
                string? type = item.Fields.GetValueOrDefault("type")?.Text;
                if (type == "function_call_output")
                {
                    if (!item.Fields.TryGetValue("output", out Node? value) || value.Type != JsonTokenType.String) return null;
                    results.Add(value);
                }
                else if (type is not (null or "message" or "function_call" or "reasoning" or "compaction")) return null;
                else if (type is null or "message" && item.Fields.GetValueOrDefault("role")?.Text is not ("system" or "developer" or "user" or "assistant")) return null;
            }
        }
        else
        {
            Node messages = root.Fields["messages"];
            if (messages.Type != JsonTokenType.StartArray) return null;
            foreach (Node item in messages.Items)
            {
                if (Protected(item)) continue;
                string? role = item.Fields.GetValueOrDefault("role")?.Text;
                if (role is not ("user" or "assistant")) return null;
                if (!item.Fields.TryGetValue("content", out Node? content)) return null;
                if (content.Type == JsonTokenType.String) continue; // All human instructions are protected.
                if (content.Type != JsonTokenType.StartArray) return null;
                foreach (Node block in content.Items)
                {
                    if (Protected(block)) continue;
                    string? type = block.Fields.GetValueOrDefault("type")?.Text;
                    if (type == "tool_result" && role == "user")
                    {
                        if (!block.Fields.TryGetValue("content", out Node? value)) return null;
                        if (value.Type == JsonTokenType.String) results.Add(value);
                        else if (value.Type == JsonTokenType.StartArray)
                        {
                            foreach (Node part in value.Items)
                            {
                                if (Protected(part)) continue;
                                if (part.Fields.GetValueOrDefault("type")?.Text == "text" && part.Fields.GetValueOrDefault("text") is { Type: JsonTokenType.String } text) results.Add(text);
                                else return null;
                            }
                        }
                        else return null;
                    }
                    else if (type is not ("text" or "image" or "document" or "tool_use" or "thinking" or "redacted_thinking")) return null;
                }
            }
        }
        return results;
    }

    private static bool Protected(Node node) => node.Fields.ContainsKey("exact") || node.Fields.GetValueOrDefault("fidelity")?.Text == "exact"
        || node.Fields.GetValueOrDefault("mandatory")?.Type == JsonTokenType.True || node.Fields.GetValueOrDefault("is_error")?.Type == JsonTokenType.True;

    private sealed class Node
    {
        internal int Start { get; init; }
        internal int End { get; set; }
        internal JsonTokenType Type { get; init; }
        internal string? Text { get; init; }
        internal Dictionary<string, Node> Fields { get; } = new(StringComparer.Ordinal);
        internal List<Node> Items { get; } = [];
    }

    private static Node Parse(byte[] bytes)
    {
        using JsonDocument valid = JsonDocument.Parse(bytes, Options);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
        reader.Read();
        int count = 0;
        return Read(ref reader, ref count);
    }

    private static Node Read(ref Utf8JsonReader reader, ref int count)
    {
        if (++count > 100000) throw new JsonException();
        var node = new Node { Start = checked((int)reader.TokenStartIndex), Type = reader.TokenType,
            Text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null };
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string name = reader.GetString()!;
                reader.Read();
                node.Fields.Add(name, Read(ref reader, ref count));
            }
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) node.Items.Add(Read(ref reader, ref count));
        }
        node.End = checked((int)reader.BytesConsumed);
        return node;
    }

    // Map decoded UTF-8 boundaries back to the original JSON string. Token boundaries never split a Unicode scalar.
    private static int[] StringBoundaries(ReadOnlySpan<byte> raw, int length)
    {
        var map = new int[length + 1];
        int written = 0;
        for (int i = 1; i < raw.Length - 1;)
        {
            map[written] = i;
            int consumed, produced;
            if (raw[i] == '\\')
            {
                consumed = raw[i + 1] == 'u' ? 6 : 2;
                if (consumed == 6 && raw[i + 2] is (byte)'d' or (byte)'D' && raw[i + 3] is >= (byte)'8' and <= (byte)'9' or >= (byte)'a' and <= (byte)'b' or >= (byte)'A' and <= (byte)'B') consumed = 12;
                byte[] quoted = new byte[consumed + 2];
                quoted[0] = quoted[^1] = (byte)'"';
                raw.Slice(i, consumed).CopyTo(quoted.AsSpan(1));
                produced = Encoding.UTF8.GetByteCount(JsonSerializer.Deserialize<string>(quoted)!);
            }
            else
            {
                if (Rune.DecodeFromUtf8(raw[i..], out _, out consumed) != System.Buffers.OperationStatus.Done) throw new JsonException();
                produced = consumed;
            }
            written += produced;
            i += consumed;
            map[written] = i;
        }
        if (written != length) throw new JsonException();
        return map;
    }
}
