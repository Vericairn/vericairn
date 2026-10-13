// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Portability;

/// <summary>What an export contains, as written into its manifest entry.</summary>
/// <param name="Id">Export id.</param>
/// <param name="Estate">Estate id (the charter's first record).</param>
/// <param name="CreatedAt">Creation time.</param>
/// <param name="Profile">Profile of the source.</param>
/// <param name="Records">Record files included.</param>
/// <param name="Objects">Objects included.</param>
/// <param name="Documents">Documents and other repository files included.</param>
/// <param name="InventoryHash">Hash of the inventory entry.</param>
public sealed record ExportSummary(string Id, string Estate, string CreatedAt, string Profile, int Records, int Objects, int Documents, string InventoryHash);

/// <summary>
/// Portability: an export is a zip holding the estate's records, objects, manifest and documents with a verified
/// inventory; a restore verifies every byte against that inventory before anything is used. An export carries
/// records and bytes, never keys, sessions or authority.
/// </summary>
public static class EstateExport
{
    /// <summary>Entry name of the inventory.</summary>
    public const string InventoryEntry = "export/inventory.json";

    /// <summary>Entry name of the export description.</summary>
    public const string SummaryEntry = "export/export.json";

    private static readonly string[] CopiedTopLevel = ["estate", "objects", "design", "docs", "README.md", "LICENSE", "bootstrap", "bootstrap.ps1", ".gitattributes", ".gitignore", ".github"];

    /// <summary>Writes an export of an opened estate to a zip file.</summary>
    public static ExportSummary Write(EstateSession session, string zipPath, string? by = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        if (File.Exists(zipPath))
        {
            throw new IOException($"{zipPath} already exists; exports are never overwritten.");
        }

        IReadOnlyList<RecordFile> records = session.Files();
        var inventory = new JsonArray();
        string createdAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
        string id = Ulid.NewUlid().Value;
        int objects = 0, documents = 0;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (RecordFile record in records)
            {
                Add(zip, inventory, record.Path, record.Bytes);
            }

            foreach (string hash in session.Objects.List())
            {
                byte[]? bytes = session.Objects.Read(hash);
                if (bytes is not null)
                {
                    Add(zip, inventory, EstateLayout.ObjectsDirectory + "/" + hash[ContentHash.Prefix.Length..], bytes);
                    objects++;
                }
            }

            foreach (string entry in CopiedTopLevel)
            {
                string full = Path.Combine(session.Root, entry);
                if (File.Exists(full))
                {
                    Add(zip, inventory, entry, File.ReadAllBytes(full));
                    documents++;
                }
                else if (Directory.Exists(full) && entry != EstateLayout.ObjectsDirectory)
                {
                    foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                    {
                        Add(zip, inventory, EstateLayout.Relative(session.Root, file), File.ReadAllBytes(file));
                        documents++;
                    }
                }
            }

            byte[] inventoryBytes = Canonical(inventory);
            string inventoryHash = ContentHash.Sha256(inventoryBytes);
            WriteEntry(zip, InventoryEntry, inventoryBytes);
            var summary = new JsonObject
            {
                ["format"] = "vericairn.export/1",
                ["id"] = id,
                ["estate"] = session.Manifest.Estate,
                ["createdAt"] = createdAt,
                ["by"] = by,
                ["profile"] = session.Profile,
                ["vericairn"] = session.Manifest.Describe(),
                ["records"] = records.Count,
                ["objects"] = objects,
                ["documents"] = documents,
                ["inventoryHash"] = inventoryHash,
                ["carries"] = "records and bytes; no keys, sessions, credentials or authority",
            };
            WriteEntry(zip, SummaryEntry, Canonical(summary));
            return new ExportSummary(id, session.Manifest.Estate, createdAt, session.Profile, records.Count, objects, documents, inventoryHash);
        }
    }

    /// <summary>Verifies an export against its inventory without extracting anything. Returns the summary and the problems found.</summary>
    public static (ExportSummary? Summary, FindingList Findings) Verify(string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        var findings = new FindingList();
        if (!File.Exists(zipPath))
        {
            findings.Error("export.missing", zipPath, "The export file does not exist.");
            return (null, findings);
        }

        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry? inventoryEntry = zip.GetEntry(InventoryEntry);
        ZipArchiveEntry? summaryEntry = zip.GetEntry(SummaryEntry);
        if (inventoryEntry is null || summaryEntry is null)
        {
            findings.Error("export.format", zipPath, "Not a Vericairn export: the inventory or the summary entry is missing.");
            return (null, findings);
        }

        byte[] inventoryBytes = ReadEntry(inventoryEntry);
        using JsonDocument summaryDocument = JsonDocument.Parse(ReadEntry(summaryEntry));
        JsonElement summary = summaryDocument.RootElement;
        string inventoryHash = summary.GetProperty("inventoryHash").GetString() ?? string.Empty;
        if (ContentHash.Sha256(inventoryBytes) != inventoryHash)
        {
            findings.Error("export.inventory", InventoryEntry, "The inventory does not match the hash in the export summary.");
            return (null, findings);
        }

        using JsonDocument inventoryDocument = JsonDocument.Parse(inventoryBytes);
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in inventoryDocument.RootElement.EnumerateArray())
        {
            string path = item.GetProperty("path").GetString() ?? string.Empty;
            string hash = item.GetProperty("hash").GetString() ?? string.Empty;
            long size = item.GetProperty("bytes").GetInt64();
            listed.Add(path);
            ZipArchiveEntry? entry = zip.GetEntry(path);
            if (entry is null)
            {
                findings.Error("export.entry-missing", path, "Listed in the inventory but absent from the export.");
                continue;
            }

            byte[] bytes = ReadEntry(entry);
            if (bytes.LongLength != size || ContentHash.Sha256(bytes) != hash)
            {
                findings.Error("export.entry-hash", path, "The entry's bytes do not match the inventory.");
            }
        }

        foreach (ZipArchiveEntry entry in zip.Entries.Where(e => e.FullName != InventoryEntry && e.FullName != SummaryEntry && !e.FullName.EndsWith('/')))
        {
            if (!listed.Contains(entry.FullName))
            {
                findings.Error("export.entry-unlisted", entry.FullName, "Present in the export but not in the inventory.");
            }
        }

        var result = new ExportSummary(
            summary.GetProperty("id").GetString() ?? string.Empty,
            summary.GetProperty("estate").GetString() ?? string.Empty,
            summary.GetProperty("createdAt").GetString() ?? string.Empty,
            summary.GetProperty("profile").GetString() ?? string.Empty,
            summary.GetProperty("records").GetInt32(),
            summary.GetProperty("objects").GetInt32(),
            summary.GetProperty("documents").GetInt32(),
            inventoryHash);
        return (result, findings);
    }

    /// <summary>
    /// Restores a verified export into an empty directory as a repository-synced estate (its records as files),
    /// whatever the source profile was. Nothing is restored unless every byte verifies. Returns the summary.
    /// </summary>
    public static ExportSummary Restore(string zipPath, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        (ExportSummary? summary, FindingList findings) = Verify(zipPath);
        if (summary is null || findings.HasErrors)
        {
            throw new InvalidOperationException("The export does not verify: " + string.Join("; ", findings.Select(f => f.Message)));
        }

        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw new InvalidOperationException("The destination must be empty; a restore never merges into existing material.");
        }

        Directory.CreateDirectory(destination);
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in zip.Entries.Where(e => e.FullName != InventoryEntry && e.FullName != SummaryEntry && !e.FullName.EndsWith('/')))
        {
            string relative = entry.FullName;
            if (relative.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                throw new InvalidOperationException($"Refusing entry '{relative}'.");
            }

            string target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            byte[] bytes = ReadEntry(entry);
            if (relative == EstateLayout.ManifestPath)
            {
                bytes = AsRepositorySynced(bytes);
            }

            File.WriteAllBytes(target, bytes);
        }

        return summary;
    }

    private static byte[] AsRepositorySynced(byte[] manifestBytes)
    {
        JsonNode? node = JsonNode.Parse(manifestBytes);
        if (node is JsonObject manifest)
        {
            manifest["profile"] = "repository-synced";
            return Canonical(manifest);
        }

        return manifestBytes;
    }

    private static void Add(ZipArchive zip, JsonArray inventory, string path, byte[] bytes)
    {
        WriteEntry(zip, path, bytes);
        inventory.Add(new JsonObject { ["path"] = path, ["bytes"] = bytes.LongLength, ["hash"] = ContentHash.Sha256(bytes) });
    }

    private static void WriteEntry(ZipArchive zip, string path, byte[] bytes)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using Stream stream = entry.Open();
        stream.Write(bytes);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static byte[] Canonical(JsonNode node)
    {
        using JsonDocument document = JsonDocument.Parse(node.ToJsonString());
        return Encoding.UTF8.GetBytes(CanonicalJson.SerializePretty(document.RootElement));
    }
}
