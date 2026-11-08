// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;

namespace Vericairn.Records.History;

/// <summary>One record a checkpoint's inventory lists: its id, its path under records/ and its content hash.</summary>
/// <param name="Id">The record's id, the file name of its path.</param>
/// <param name="Path">The record's path under records/, such as admission/2026-10/01J00000000000000000000000.json.</param>
/// <param name="Hash">The record's content hash.</param>
public sealed record InventoryEntry(string Id, string Path, string Hash)
{
    /// <summary>The repository-relative path of the record file.</summary>
    public string RepositoryPath => EstateLayout.RecordsDirectory + "/" + Path;
}

/// <summary>The inventory of a parsed object: the entries it lists, in order, and what is wrong with it.</summary>
/// <param name="Entries">Every well-formed line, in the object's order.</param>
/// <param name="Problems">Each malformed line, ordering fault or size fault, in words.</param>
public sealed record InventoryObject(IReadOnlyList<InventoryEntry> Entries, IReadOnlyList<string> Problems);

/// <summary>
/// A history checkpoint's inventory (design/signed-history.md § 5): the records it first witnesses, one line each, the path under
/// records/, a space and the content hash, in UTF-8 with a line feed after every line, sorted by id across the checkpoint's objects
/// and split so that no object exceeds 8 MiB. Objects are content-addressed, so the checkpoint's signature over their hashes fixes
/// every line.
/// </summary>
public static class HistoryInventory
{
    /// <summary>The largest inventory object, the estate's object limit.</summary>
    public const long MaxObjectBytes = EstateLayout.MaxObjectBytes;

    // Objects never change, so parsed ones are kept by hash, up to a bound on the lines held across every estate this process reads.
    private const int MaxParsedEntries = 400_000;
    private static readonly ConcurrentDictionary<string, InventoryObject> Parsed = new(StringComparer.Ordinal);
    private static int parsedEntries;

    /// <summary>The inventory line of a record, without its line feed.</summary>
    public static string LineOf(RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.ExpectedPath[(EstateLayout.RecordsDirectory.Length + 1)..] + " " + record.ComputedHash;
    }

    /// <summary>The inventory objects listing the records, sorted by id, each at most <paramref name="maxObjectBytes"/> bytes.</summary>
    public static IReadOnlyList<byte[]> Write(IEnumerable<RecordEnvelope> records, long maxObjectBytes = MaxObjectBytes)
    {
        ArgumentNullException.ThrowIfNull(records);
        var objects = new List<byte[]>();
        var current = new MemoryStream();
        foreach (RecordEnvelope record in records.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            byte[] line = Encoding.UTF8.GetBytes(LineOf(record) + "\n");
            if (line.Length > maxObjectBytes) throw new InvalidOperationException("An inventory line exceeds the object limit.");
            if (current.Length + line.Length > maxObjectBytes)
            {
                objects.Add(current.ToArray());
                current = new MemoryStream();
            }

            current.Write(line);
        }

        if (current.Length > 0) objects.Add(current.ToArray());
        return objects;
    }

    /// <summary>Reads an inventory object's bytes; parsed objects are kept by hash, since their content never changes.</summary>
    public static InventoryObject Read(string hash, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(bytes);
        if (Parsed.TryGetValue(hash, out InventoryObject? known)) return known;
        InventoryObject parsed = Read(bytes);
        if (ContentHash.Sha256(bytes) == hash)
        {
            if (Interlocked.Add(ref parsedEntries, parsed.Entries.Count) > MaxParsedEntries)
            {
                Parsed.Clear();
                Interlocked.Exchange(ref parsedEntries, parsed.Entries.Count);
            }

            Parsed[hash] = parsed;
        }

        return parsed;
    }

    /// <summary>Reads an inventory object's bytes.</summary>
    public static InventoryObject Read(ReadOnlySpan<byte> bytes)
    {
        var entries = new List<InventoryEntry>();
        var problems = new List<string>();
        if (bytes.Length > MaxObjectBytes) problems.Add($"The object holds {bytes.Length:N0} bytes, more than the {MaxObjectBytes:N0}-byte limit.");
        if (!System.Text.Unicode.Utf8.IsValid(bytes))
        {
            problems.Add("The object is not UTF-8.");
            return new(entries, problems);
        }

        string text = Encoding.UTF8.GetString(bytes);
        if (text.Length > 0 && !text.EndsWith('\n')) problems.Add("The last line has no line feed.");
        string[] lines = text.Split('\n');
        string? previous = null;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (i == lines.Length - 1 && line.Length == 0) break;
            int number = i + 1;
            if (!TryParseLine(line, out InventoryEntry? entry))
            {
                problems.Add($"Line {number} is not '<type>/<yyyy-MM>/<id>.json sha256:<hex>'.");
                continue;
            }

            if (previous is not null && string.CompareOrdinal(entry!.Id, previous) <= 0)
            {
                problems.Add($"Line {number} lists {entry.Id} after {previous}; an inventory is sorted by id and lists each record once.");
                continue;
            }

            previous = entry!.Id;
            entries.Add(entry);
        }

        return new(entries, problems);
    }

    /// <summary>Parses one line: a path under records/, one space and a content hash.</summary>
    public static bool TryParseLine(string line, out InventoryEntry? entry)
    {
        entry = null;
        ArgumentNullException.ThrowIfNull(line);
        int space = line.IndexOf(' ', StringComparison.Ordinal);
        if (space <= 0 || line.IndexOf(' ', space + 1) >= 0) return false;
        string path = line[..space];
        string hash = line[(space + 1)..];
        if (!ContentHash.IsValid(hash)) return false;
        string[] parts = path.Split('/');
        if (parts.Length != 3 || !IsTypeName(parts[0]) || !IsMonth(parts[1]) || !parts[2].EndsWith(".json", StringComparison.Ordinal)) return false;
        string id = parts[2][..^".json".Length];
        if (!Ulid.IsValid(id) || id.Any(char.IsAsciiLetterLower)) return false;
        entry = new InventoryEntry(id, path, hash);
        return true;
    }

    private static bool IsTypeName(string text) =>
        text.Length is > 0 and <= 64 && text[0] is >= 'a' and <= 'z' && text.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    private static bool IsMonth(string text) =>
        text.Length == 7 && text[4] == '-' && text.Where((c, i) => i != 4).All(char.IsAsciiDigit) && (text[5] - '0') * 10 + (text[6] - '0') is >= 1 and <= 12;
}

/// <summary>The identity of a set of records, as an estate observation and a history checkpoint state it.</summary>
public static class RecordSets
{
    /// <summary>
    /// The SHA-256 of the canonical JSON array of <c>{"hash", "id"}</c> objects, one per record, sorted by id: the record set hash of
    /// estate observations and history checkpoints.
    /// </summary>
    public static string Hash(IEnumerable<(string Id, string Hash)> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var text = new StringBuilder("[");
        bool first = true;
        foreach ((string id, string hash) in records.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (!first) text.Append(',');
            first = false;
            // Ids and content hashes are ASCII letters, digits and a colon, which canonical JSON writes unescaped.
            text.Append("{\"hash\":\"").Append(hash).Append("\",\"id\":\"").Append(id).Append("\"}");
        }

        text.Append(']');
        return ContentHash.Sha256(Encoding.UTF8.GetBytes(text.ToString()));
    }
}
