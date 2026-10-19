// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using static Vericairn.Packs.Software.CompanionInput;

namespace Vericairn.Packs.Software;

internal sealed class FrozenIndexCompanion() : SoftwareCompanion("frozen-index")
{
    protected override string Render(CompanionInput input)
    {
        JsonElement root = input.Json();
        if (String(root, "format") != "vericairn.frozen-docs/1") throw new CompanionUnavailableException();
        var text = new StringBuilder("# Frozen documentation index\n\nThese are the sets and byte digests recorded in the selected index, including any withdrawn sets. The index does not record acceptance or current release status.\n\n");
        foreach (JsonProperty set in root.GetProperty("sets").EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (!DocumentPatterns.IsSafeSourcePath(set.Name) || set.Name.Contains('/', StringComparison.Ordinal)) throw new CompanionUnavailableException();
            text.Append("## ").Append(DocumentMarkdown.Text(set.Name)).Append("\n\n| File | Recorded SHA-256 |\n|---|---|\n");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty file in set.Value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (!DocumentPatterns.IsSafeSourcePath(file.Name) || !paths.Add(file.Name) || !ContentHash.IsValid(file.Value.GetString())) throw new CompanionUnavailableException();
                Row(text, set.Name + "/" + file.Name, file.Value.GetString()!);
            }
            text.Append('\n');
        }
        return text.ToString();
    }
}

internal sealed partial class ChecksumsCompanion() : SoftwareCompanion("checksums")
{
    protected override string Render(CompanionInput input)
    {
        var text = new StringBuilder("# Artifact checksums\n\nDigests below are claims in the retained checksum list. This report neither verifies artifact bytes nor establishes a signature, producer, rights or acceptance. Size and platform are not recorded in this format.\n\n| Artifact | Algorithm | Recorded digest |\n|---|---|---|\n");
        var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in input.Text(input.SourcePath).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length == 0) continue;
            Match match = ChecksumLine().Match(line);
            if (!match.Success || !DocumentPatterns.IsSafeSourcePath(match.Groups[2].Value) || !names.Add(match.Groups[2].Value)) throw new CompanionUnavailableException();
            rows.Add(match.Groups[2].Value, match.Groups[1].Value);
        }
        foreach ((string path, string hash) in rows) Row(text, path, "SHA-256", hash);
        if (rows.Count == 0) text.Append("\nThe retained list is empty.\n");
        return text.ToString();
    }

    [GeneratedRegex("^([0-9a-fA-F]{64}) [ *](.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChecksumLine();
}

internal sealed class ComponentsCompanion() : SoftwareCompanion("components")
{
    protected override string Render(CompanionInput input)
    {
        JsonElement root = input.Json();
        if (String(root, "bomFormat") != "CycloneDX" || String(root, "specVersion") is not ("1.2" or "1.3" or "1.4" or "1.5" or "1.6")) throw new CompanionUnavailableException();
        DocumentSourceSnapshot[] locks = input.Context.Sources.Where(s => s.Path!.EndsWith("packages.lock.json", StringComparison.Ordinal)).OrderBy(s => s.Path, StringComparer.Ordinal).ToArray();
        if (locks.Length == 0) throw new CompanionUnavailableException();
        var text = new StringBuilder("# Components and dependencies\n\nThis report restates the retained SBOM and dependency locks. Missing licenses, vulnerabilities and rights remain unrecorded. A listed component or digest is not integrity, signature, producer, rights or release acceptance. The sources may describe different scopes; no equivalence is inferred.\n\n## Components\n\n| Type | Name | Version | Reference | Licenses as recorded |\n|---|---|---|---|---|\n");
        if (root.TryGetProperty("components", out JsonElement components))
            foreach (JsonElement component in components.EnumerateArray().OrderBy(c => Recorded(c, "bom-ref"), StringComparer.Ordinal).ThenBy(c => Recorded(c, "name"), StringComparer.Ordinal))
                Row(text, Recorded(component, "type"), Recorded(component, "name"), Recorded(component, "version"), Recorded(component, "bom-ref"), Recorded(component, "licenses"));
        else text.Append("\nComponents are not recorded.\n");
        text.Append("\n## Dependency relations\n\n| Component reference | Depends on |\n|---|---|\n");
        if (root.TryGetProperty("dependencies", out JsonElement dependencies))
            foreach (JsonElement dependency in dependencies.EnumerateArray().OrderBy(d => String(d, "ref"), StringComparer.Ordinal))
                Row(text, String(dependency, "ref"), Recorded(dependency, "dependsOn"));
        else text.Append("\nDependency relations are not recorded.\n");
        text.Append("\n## Complete SBOM fields\n\nField paths are JSON Pointers; array positions preserve recorded order. Empty arrays, objects and null values are retained.\n\n").Append(Table(root));
        foreach (DocumentSourceSnapshot file in locks)
        {
            JsonElement data = input.Json(file.Path);
            if (data.GetProperty("version").GetInt32() is not (1 or 2) || data.GetProperty("dependencies").ValueKind != JsonValueKind.Object) throw new CompanionUnavailableException();
            text.Append("\n## Lock: ").Append(DocumentMarkdown.Text(file.Path!)).Append("\n\n").Append(Table(data));
        }
        return text.ToString();
    }
}

internal sealed class ExportCompanion() : SoftwareCompanion("export-contents")
{
    protected override string Render(CompanionInput input)
    {
        using var memory = new MemoryStream(input.Bytes(input.SourcePath), writable: false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        var members = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long size = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName.TrimEnd('/');
            if (!DocumentPatterns.IsSafeSourcePath(name) || !members.TryAdd(entry.FullName, entry)
                || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 || (size += entry.Length) > 64L * 1024 * 1024)
                throw new CompanionUnavailableException();
        }
        byte[] inventoryBytes = Read("export/inventory.json");
        using JsonDocument inventoryDocument = JsonDocument.Parse(inventoryBytes);
        using JsonDocument summaryDocument = JsonDocument.Parse(Read("export/export.json"));
        JsonElement summary = summaryDocument.RootElement;
        CheckUnique(summary);
        CheckUnique(inventoryDocument.RootElement);
        if (String(summary, "format") != "vericairn.export/1" || String(summary, "inventoryHash") != ContentHash.Sha256(inventoryBytes)) throw new CompanionUnavailableException();
        var text = new StringBuilder("# Export archive contents\n\nThis read-only report inspects the selected archive without extraction or restoration. Byte agreement is separate from record validity, compatible-reader availability, authority and acceptance. No machine configuration or credentials are needed.\n\n## Recorded export description\n\n");
        text.Append(Table(summary)).Append("\n## Payload inventory\n\n| Entry | Recorded bytes | Recorded SHA-256 | Observation |\n|---|---|---|---|\n");
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in inventoryDocument.RootElement.EnumerateArray().OrderBy(i => String(i, "path"), StringComparer.Ordinal))
        {
            string path = String(item, "path");
            string hash = String(item, "hash");
            long bytes = item.GetProperty("bytes").GetInt64();
            if (!DocumentPatterns.IsSafeSourcePath(path) || !listed.Add(path) || !ContentHash.IsValid(hash) || bytes < 0 || path is "export/export.json" or "export/inventory.json") throw new CompanionUnavailableException();
            string observation = !members.TryGetValue(path, out ZipArchiveEntry? entry) || entry.FullName != path ? "Missing" : entry.Length != bytes || ContentHash.Sha256(Read(path)) != hash ? "Bytes differ" : "Bytes match the inventory";
            Row(text, path, bytes.ToString(CultureInfo.InvariantCulture), hash, observation);
        }
        foreach (ZipArchiveEntry entry in members.Values.Where(e => !e.FullName.EndsWith('/') && e.FullName is not ("export/export.json" or "export/inventory.json") && !listed.Contains(e.FullName)).OrderBy(e => e.FullName, StringComparer.Ordinal))
            Row(text, entry.FullName, "Not recorded", "Not recorded", "Present but unlisted");
        return text.ToString();

        byte[] Read(string path)
        {
            if (!members.TryGetValue(path, out ZipArchiveEntry? entry) || entry.FullName != path) throw new CompanionUnavailableException();
            using Stream stream = entry.Open();
            using var output = new MemoryStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = stream.Read(buffer)) != 0)
            {
                if (output.Length + count > entry.Length) throw new CompanionUnavailableException();
                output.Write(buffer, 0, count);
            }
            if (output.Length != entry.Length) throw new CompanionUnavailableException();
            return output.ToArray();
        }
    }
}
