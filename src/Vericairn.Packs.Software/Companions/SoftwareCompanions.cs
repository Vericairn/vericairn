// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;

namespace Vericairn.Packs.Software;

/// <summary>Software readers over retained, authorized bytes. Registration performs no source discovery.</summary>
public static class SoftwareCompanions
{
    private static readonly object Gate = new();
    private static bool registered;

    /// <summary>The installed output contracts; source acquisition and publication are separate operations.</summary>
    public static ImmutableArray<IDocumentGenerator> Generators { get; } =
    [
        new BuildReferenceCompanion("cli-reference"), new BuildReferenceCompanion("cli-client-outcomes"), new BuildReferenceCompanion("record-reference"),
        new BuildReferenceCompanion("api-reference"), new FrozenIndexCompanion(), new WorkflowCompanion(),
        new ComponentsCompanion(), new ChecksumsCompanion(), new ExportCompanion(), new ReferenceIndexCompanion(),
    ];

    /// <summary>Registers these implementations and their exact managed-kind coverage once per process.</summary>
    public static void Register()
    {
        lock (Gate)
        {
            if (registered) return;
            foreach (IDocumentGenerator generator in Generators) DocumentGenerators.Register(generator);
            DocumentCoverage.Register(Generators.Select(g => new DocumentManagedKind(g.Id, g.Id + "/1")),
                Generators.Select(g => new DocumentCoverageDescriptor(1, g.Id, "Read the exact retained software inputs and their provenance.",
                    new(g.Id, 1), new(g.Id, 1), g.Id + "/1", "Internal", "artifact")));
            registered = true;
        }
    }
}

internal abstract class SoftwareCompanion : IDocumentGenerator
{
    private static readonly string ImplementationHash = SourceHash();
    protected SoftwareCompanion(string name)
    {
        Id = "vericairn.software/" + name;
        Descriptor = new(Id, 1, "vericairn.software.companions/1", ImplementationHash,
            new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
            {
                ["audience"] = ["Internal"], ["selection"] = ["item", "list"],
                ["build-commit"] = ["@commit"], ["source"] = ["@text"],
            }.ToImmutableDictionary(StringComparer.Ordinal), [], ["file"], ["item", "list"], "text/markdown",
            "Ordinal source path and identity; source array order where meaningful.", "Internal", "single");
    }

    public string Id { get; }
    public DocumentGeneratorDescriptor Descriptor { get; }

    public DocumentRenderResult Generate(DocumentGenerationContext context)
    {
        try
        {
            var input = new CompanionInput(context);
            string content = Render(input);
            return DocumentRenderResult.Success(content.TrimEnd() + "\n\n" + Provenance(input));
        }
        catch (Exception error) when (error is CompanionUnavailableException or JsonException or DecoderFallbackException or InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return DocumentRenderResult.Unavailable("A complete retained input is unavailable or unsupported.");
        }
    }

    protected abstract string Render(CompanionInput input);

    private static string Provenance(CompanionInput input)
    {
        var text = new StringBuilder("## Generation basis\n\n");
        text.Append("Build commit: `").Append(input.Commit).Append("`.\n\nGenerator: `").Append(input.Context.Generator.Id)
            .Append("/1`; implementation: `").Append(input.Context.Generator.Implementation).Append("`; source digest: `")
            .Append(input.Context.Generator.ImplementationHash).Append("`.\n\nInput digest: `").Append(input.Context.InputDigest).Append("`.\n\n")
            .Append("| Input | SHA-256 | Bytes |\n|---|---|---|\n");
        foreach (DocumentSourceSnapshot source in input.Context.Sources.OrderBy(s => s.Path, StringComparer.Ordinal))
            text.Append("| ").Append(DocumentMarkdown.Text(source.Path!)).Append(" | `").Append(source.Hash).Append("` | ")
                .Append(source.Bytes.Length.ToString(CultureInfo.InvariantCulture)).Append(" |\n");
        return text.ToString();
    }

    private static string SourceHash()
    {
        // Embedded source bytes pin this implementation independently of the enclosing output commit and platform binary.
        Assembly assembly = typeof(SoftwareCompanion).Assembly;
        var sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("companion-source/", StringComparison.Ordinal)))
        {
            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            sources.Add(name, ContentHash.Sha256(bytes.ToArray()));
        }
        if (sources.Count == 0) throw new InvalidOperationException("The companion implementation sources are not retained in this build.");
        return ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(sources)));
    }
}

internal sealed class CompanionUnavailableException : Exception;

internal sealed class CompanionInput
{
    private readonly Dictionary<string, DocumentSourceSnapshot> sources;
    internal CompanionInput(DocumentGenerationContext context)
    {
        Context = context;
        Commit = context.Parameters["build-commit"];
        SourcePath = context.Parameters["source"];
        if (!DocumentPatterns.IsSafeSourcePath(SourcePath) || context.Sources.IsEmpty
            || context.Sources.Select(s => s.Repository).Distinct(StringComparer.Ordinal).Count() != 1
            || context.Sources.Select(s => s.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != context.Sources.Length
            || context.Sources.Sum(s => (long)s.Bytes.Length) > 64L * 1024 * 1024) throw new CompanionUnavailableException();
        sources = context.Sources.ToDictionary(s => s.Path!, StringComparer.Ordinal);
    }
    internal DocumentGenerationContext Context { get; }
    internal string Commit { get; }
    internal string SourcePath { get; }
    internal DocumentSourceSnapshot File(string path) => sources.GetValueOrDefault(path) ?? throw new CompanionUnavailableException();
    internal byte[] Bytes(string path) => File(path).Bytes.ToArray();
    internal string Text(string path) => new UTF8Encoding(false, true).GetString(File(path).Bytes.AsSpan());
    internal JsonElement Json(string? path = null)
    {
        using JsonDocument document = JsonDocument.Parse(Bytes(path ?? SourcePath), new JsonDocumentOptions { MaxDepth = 64 });
        CheckUnique(document.RootElement);
        return document.RootElement.Clone();
    }
    internal string Relative(string path)
    {
        if (!DocumentPatterns.IsSafeSourcePath(path)) throw new CompanionUnavailableException();
        int slash = SourcePath.LastIndexOf('/');
        return (slash < 0 ? "" : SourcePath[..(slash + 1)]) + path;
    }
    internal void Build(JsonElement root, string format)
    {
        if (String(root, "format") != format || String(root, "commit") != Commit) throw new CompanionUnavailableException();
    }
    internal static string String(JsonElement item, string field) => item.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw new CompanionUnavailableException();
    internal static string Recorded(JsonElement item, string field) => item.TryGetProperty(field, out JsonElement value)
        ? Value(value) : "Not recorded";
    internal static string Value(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : CanonicalJson.Serialize(value);
    internal static JsonElement[] Array(JsonElement item, string field) => item.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().ToArray() : throw new CompanionUnavailableException();
    internal static void Row(StringBuilder text, params string[] cells) => text.Append("| ").AppendJoin(" | ", cells.Select(DocumentMarkdown.Text)).Append(" |\n");
    internal static string Table(JsonElement item)
    {
        var text = new StringBuilder("| Recorded field | Value |\n|---|---|\n");
        Add(item, "");
        return text.ToString();

        void Add(JsonElement value, string path)
        {
            if (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Any())
                foreach (JsonProperty field in value.EnumerateObject().OrderBy(f => f.Name, StringComparer.Ordinal)) Add(field.Value, path + "/" + field.Name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));
            else if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0)
            {
                int index = 0;
                foreach (JsonElement child in value.EnumerateArray()) Add(child, path + "/" + (index++).ToString(CultureInfo.InvariantCulture));
            }
            else Row(text, path.Length == 0 ? "(root)" : path, Value(value));
        }
    }
    internal static void CheckUnique(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty field in item.EnumerateObject())
            {
                if (!names.Add(field.Name)) throw new CompanionUnavailableException();
                CheckUnique(field.Value);
            }
        }
        else if (item.ValueKind == JsonValueKind.Array) foreach (JsonElement child in item.EnumerateArray()) CheckUnique(child);
    }
}
