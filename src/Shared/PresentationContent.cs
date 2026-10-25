// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vericairn.Presentation;

// Compiled into the record and wire libraries so every client uses the identical
// installed contracts and byte-preserving presentation, without an estate dependency.
internal static class PresentationContent
{
    internal const string Communication = "vericairn.estate/communication";
    internal const string Context = "vericairn.estate/context-brief";
    internal const string Commit = "vericairn.software/commit";
    internal const string Review = "vericairn.estate/review";
    internal static IReadOnlyList<string> Ids { get; } = [Communication, Context, Commit, Review];
    private static readonly Dictionary<string, (JsonElement Definition, string Hash)> Contracts = Load();
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    internal static JsonElement Definition(string id) => Contracts[id].Definition;
    internal static string Hash(string id) => Contracts[id].Hash;
    internal static string Digest(string value) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value, Pretty);

    private static Dictionary<string, (JsonElement, string)> Load()
    {
        var result = new Dictionary<string, (JsonElement, string)>(StringComparer.Ordinal);
        foreach (string id in Ids)
        {
            using Stream stream = typeof(PresentationContent).Assembly.GetManifestResourceStream("PresentationTemplates." + id[(id.IndexOf('/') + 1)..] + ".json")
                ?? throw new InvalidOperationException("An installed presentation contract is unavailable.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string content = reader.ReadToEnd();
            using JsonDocument document = JsonDocument.Parse(content);
            result.Add(id, (document.RootElement.Clone(), Digest(content)));
        }
        return result;
    }

    // The added one-space prefix is presentation framing, outside the original.
    // It keeps hostile generated markers inert even for the legacy region parser.
    internal static string Block(string content)
    {
        string fence = "```";
        while (content.Contains(fence, StringComparison.Ordinal)) fence += "`";
        return fence + "text\n" + string.Join('\n', content.Split('\n').Select(line => " " + line)) + "\n" + fence + "\n";
    }

    internal static (string Text, long Bytes, IReadOnlyDictionary<string, string> Sections) Render(string id, string depth,
        string adoption, IReadOnlyDictionary<string, string> supplied, long? maxBytes = null)
    {
        if (depth is not ("short" or "standard" or "comprehensive")) throw new ArgumentException("Choose short, standard or comprehensive.", nameof(depth));
        if (maxBytes is < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes), "A byte allocation must be positive.");
        JsonElement definition = Definition(id);
        var sections = new Dictionary<string, string>(supplied, StringComparer.Ordinal);
        long bytes = 0;
        while (true)
        {
            sections["budget"] = Json(new { bytes, maxBytes, units = "UTF-8 bytes", tokens = "unknown", scope = "complete presentation including this accounting", fit = "not claimed" });
            var text = new StringBuilder().Append(id).Append(" 1.0.0 at ").Append(Hash(id)).Append('\n')
                .Append("Adoption: ").Append(adoption).Append("\nDepth: ").Append(depth).Append('\n');
            if (depth != "short") text.Append("Required content and original references remain complete. This presentation grants no acceptance or authority.\n");
            if (depth == "comprehensive") text.Append("The source identities, withheld list and measured budget follow with the complete selected originals.\n");
            foreach (JsonElement section in definition.GetProperty("sections").EnumerateArray())
            {
                string key = section.GetProperty("id").GetString()!;
                if (!sections.TryGetValue(key, out string? content) || string.IsNullOrWhiteSpace(content))
                {
                    if (section.GetProperty("presence").GetString() == "optional") continue;
                    throw new InvalidOperationException("A required presentation section is missing: " + key);
                }
                bool generated = section.GetProperty("synthesis").GetProperty("mode").GetString() == "generated";
                text.Append("\n## ").Append(section.TryGetProperty("heading", out JsonElement heading) ? heading.GetString() : key.Replace('-', ' ')).Append('\n');
                text.Append("<!-- vericairn:").Append(generated ? "generated " : "section ").Append(key).Append(" -->\n")
                    .Append(Block(content)).Append("<!-- vericairn:").Append(generated ? "end " : "section-end ").Append(key).Append(" -->\n");
            }
            string rendered = text.ToString();
            long measured = Encoding.UTF8.GetByteCount(rendered);
            if (bytes == measured)
            {
                if (maxBytes is { } cap && measured > cap) throw new InvalidOperationException("The complete presentation exceeds its byte allocation; narrow the work or increase the allocation. Required content was not removed.");
                return (rendered, measured, sections);
            }
            bytes = measured;
        }
    }
}
