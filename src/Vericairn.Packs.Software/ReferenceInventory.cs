// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Hashing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Policies;
using static Vericairn.Runtime.Policies.PolicyInputs;

namespace Vericairn.Packs.Software;

internal sealed record ReferenceItem(string Category, string Name);
internal sealed record RetainedReferenceInventory(string Commit, ImmutableArray<ReferenceItem> Items);

// Pure data contract shared by generation, document checks and policy evaluation.
// All bytes and the exact commit are supplied; this reader performs no I/O.
internal static class ReferenceInventory
{
    internal const string Contract = "vericairn.reference-inventory/1";
    internal const string FileName = "reference-inventory.json";
    internal const int MaximumItems = 100000;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    internal static readonly ImmutableSortedDictionary<string, string> ReferencePaths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["command"] = "cli-reference.md", ["record-type"] = "record-reference.md", ["api-operation"] = "api-reference.md",
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    internal static string Generate(string commit, IEnumerable<ReferenceItem> items, IReadOnlyDictionary<string, byte[]> references)
    {
        if (!Patterns.Commit.IsMatch(commit)) throw new PolicyInputException("The reference inventory requires the exact build commit.");
        ReferenceItem[] ordered = items.OrderBy(i => i.Category, StringComparer.Ordinal).ThenBy(i => i.Name, StringComparer.Ordinal).ToArray();
        ValidateItems(ordered);
        if (references.Count != ReferencePaths.Count || ReferencePaths.Values.Any(p => !references.ContainsKey(p))) throw new PolicyInputException("All three generated reference files are required.");
        string json = JsonSerializer.Serialize(new
        {
            format = Contract, commit,
            references = ReferencePaths.Select(r => new { category = r.Key, path = r.Value, hash = ContentHash.Sha256(references[r.Value]) }),
            items = ordered.Select(i => new { category = i.Category, name = i.Name }),
        }, Indented) + "\n";
        PolicyFile manifest = File(FileName, Encoding.UTF8.GetBytes(json));
        var repository = new PolicyRepository("generated", commit, PolicyBasis.RepositoryContract, true,
            [manifest, .. references.Select(r => File(r.Key, r.Value))]);
        _ = Read(repository, FileName, commit);
        return json;
    }

    internal static RetainedReferenceInventory Read(PolicyRepository repository, string manifestPath, string expectedCommit)
    {
        if (!repository.Complete || repository.Contract != PolicyBasis.RepositoryContract || !Patterns.Commit.IsMatch(expectedCommit) || repository.Revision != expectedCommit)
            throw new PolicyInputException("The retained reference set does not identify the exact selected commit.");
        if (!Path(manifestPath) || repository.Files.Any(f => !Path(f.Path) || f.IsLink) || repository.Files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() != 1))
            throw new PolicyInputException("The retained reference paths are unsafe, duplicated or case-colliding.");
        PolicyFile manifest = repository.Files.SingleOrDefault(f => f.Path == manifestPath) ?? throw new PolicyInputException("The reference inventory is missing.");
        if (manifest.Bytes.Length > 32 * 1024 * 1024) throw new PolicyInputException("The reference inventory byte bound was exhausted.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(PolicyInputs.Read(manifest), new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            Shape(root, ["format", "commit", "references", "items"]);
            if (String(root, "format") != Contract || String(root, "commit") != expectedCommit)
                throw new PolicyInputException("The reference inventory version or build commit differs from the selected source.");
            JsonElement[] references = Array(root, "references");
            if (references.Length != ReferencePaths.Count) throw new PolicyInputException("The inventory must bind all three reference categories exactly once.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            List<ReferenceItem> headings = [];
            string parent = manifestPath.Contains('/', StringComparison.Ordinal) ? manifestPath[..(manifestPath.LastIndexOf('/') + 1)] : "";
            long totalBytes = 0;
            foreach (JsonElement reference in references)
            {
                Shape(reference, ["category", "path", "hash"]);
                string category = String(reference, "category");
                if (!ReferencePaths.TryGetValue(category, out string? path) || String(reference, "path") != path || !seen.Add(category))
                    throw new PolicyInputException("An inventory reference category or path is unsupported or duplicated.");
                PolicyFile file = repository.Files.SingleOrDefault(f => f.Path == parent + path) ?? throw new PolicyInputException("A generated reference file is missing: " + path + ".");
                if (String(reference, "hash") != file.Hash) throw new PolicyInputException("A generated reference hash differs from the inventory: " + path + ".");
                totalBytes += file.Bytes.Length;
                if (totalBytes > 64L * 1024 * 1024) throw new PolicyInputException("The generated reference byte bound was exhausted.");
                headings.AddRange(Headings(category, PolicyInputs.Read(file)));
            }
            JsonElement[] data = Array(root, "items", empty: true);
            if (data.Length > MaximumItems) throw new PolicyInputException("The reference item bound was exhausted.");
            ReferenceItem[] items = data.Select(i =>
            {
                Shape(i, ["category", "name"]);
                return new ReferenceItem(String(i, "category"), String(i, "name"));
            }).ToArray();
            ValidateItems(items);
            ValidateItems(headings);
            if (!items.ToHashSet().SetEquals(headings)) throw new PolicyInputException("The inventory does not exactly cover its generated reference headings.");
            return new(expectedCommit, items.OrderBy(i => i.Category, StringComparer.Ordinal).ThenBy(i => i.Name, StringComparer.Ordinal).ToImmutableArray());
        }
        catch (JsonException) { throw new PolicyInputException("The reference inventory is malformed JSON."); }
        catch (PolicyParameterException error) { throw new PolicyInputException("The reference inventory is malformed: " + error.Message); }
    }

    private static IEnumerable<ReferenceItem> Headings(string category, string text)
    {
        foreach (string line in ReferenceMarkdown.Lines(text))
        {
            int level = line.TakeWhile(c => c == '#').Count();
            if (level < 2 || (category == "command" ? level > 4 : level != 2)) continue;
            string heading = line[level..];
            if (!heading.StartsWith(" `", StringComparison.Ordinal)) continue;
            if (!heading.EndsWith('`') || heading.Length < 4) throw new PolicyInputException("Malformed generated reference heading.");
            yield return new(category, heading[2..^1]);
        }
    }

    private static void ValidateItems(IEnumerable<ReferenceItem> items)
    {
        var seen = new HashSet<ReferenceItem>();
        foreach (ReferenceItem item in items)
        {
            if (seen.Count >= MaximumItems || !ValidItem(item) || !seen.Add(item)) throw new PolicyInputException("An inventory identity is invalid, duplicated or exceeds the item bound.");
        }
    }

    internal static bool ValidItem(ReferenceItem item) => item.Category switch
    {
        "command" => item.Name.StartsWith("vericairn ", StringComparison.Ordinal) && item.Name[10..].Split(' ').All(Name),
        "record-type" => Name(item.Name),
        "api-operation" => item.Name.IndexOf(' ', StringComparison.Ordinal) is > 0 and var split &&
            new[] { "GET", "HEAD", "POST", "PUT", "DELETE", "CONNECT", "OPTIONS", "TRACE", "PATCH" }.Contains(item.Name[..split], StringComparer.Ordinal) &&
            item.Name.Length > split + 1 && item.Name[split + 1] == '/' && item.Name[(split + 1)..].All(c => c > ' ' && c < 127 && c is not ('`' or '|' or '#')),
        _ => false,
    };

    private static bool Name(string text) => text.Length > 0 && text[0] is >= 'a' and <= 'z' && text.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    private static PolicyFile File(string path, byte[] bytes) => new(path, ContentHash.Sha256(bytes), [.. bytes]);
}
