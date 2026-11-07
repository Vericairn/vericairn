// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Cli;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;

namespace Vericairn.DocsTool;

/// <summary>
/// A release's documentation set prepared by the build tooling alone (DOCS-11): assembled from the documents the product declares,
/// its approved section inserted byte for byte, frozen into the checksum index and described in the release index. Each step writes
/// only the set's directory or one of the two shared files beside it, the checksum index and the release index; each is idempotent;
/// and each refuses rather than overwrite what a person wrote or what a frozen set holds.
/// </summary>
internal static partial class ReferenceSet
{
    /// <summary>The set's overview, written by a person for its release; never the repository's README and never overwritten.</summary>
    public const string Overview = "README.md";

    /// <summary>The set's release notes, rendered from the estate's records by <c>vericairn docs notes</c>.</summary>
    public const string Notes = "release-notes.md";

    /// <summary>The checksum index of every frozen set, beside the sets.</summary>
    public const string ChecksumIndex = "frozen-sha256.json";

    /// <summary>The format the checksum index declares.</summary>
    public const string FrozenFormat = "vericairn.frozen-docs/1";

    /// <summary>The heading of the notes an approved section is inserted before.</summary>
    public const string NotesAnchor = "## What the release contains";

    /// <summary>The release index's heading under which each set's paragraph is added, newest first.</summary>
    public const string SetsHeading = "## Documentation sets";

    // The API document is declared under both names from 0.8.0: api.md is a byte copy of the maintained api-v1.md.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal) { ["api.md"] = "api-v1.md" };

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private sealed record SetPaths(string Set, string Version, string Releases, string Index, string ReleaseIndex);

    // What the build tooling writes into a set; a maintained guide of the same name is never copied over it.
    private static HashSet<string> GeneratedMembers =>
        [.. Readers.Select(r => r.Name + ".md"), "reference-index.md", ReferenceInventoryFiles.Name, "manifest.json"];

    /// <summary>
    /// Assembles a set from the declared documents: the references and manifest generated from this build (unless the set keeps
    /// the ones it holds), every maintained guide copied byte for byte from the guides directory, <c>api.md</c> from
    /// <c>api-v1.md</c>, and the overview kept, or taken once from a file. The notes are left to <c>vericairn docs notes</c>.
    /// </summary>
    public static int Assemble(string output, string declaration, bool json, string? overview = null, string? guides = null, bool keepReferences = false, string? provenanceAt = null, string? register = null)
    {
        try
        {
            SetPaths set = Locate(output);
            RefuseIndexed(set);
            List<string> documents = Declared(declaration);
            string sources = Path.GetFullPath(guides ?? Path.GetDirectoryName(set.Releases) ?? throw new InvalidOperationException("Name the maintained guides' directory with --guides."));
            var copies = new List<(string Name, string Source)>();
            var missing = new List<string>();
            foreach (string name in documents.Where(d => !GeneratedMembers.Contains(d) && d is not (Overview or Notes)))
            {
                string source = Aliases.GetValueOrDefault(name, name);
                string path = Path.Combine(sources, source);
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) copies.Add((name, path));
                else missing.Add(source == name ? name : $"{source} (copied as {name})");
            }

            if (missing.Count > 0)
                throw new InvalidOperationException($"The set copies maintained guides that {sources} lacks: {string.Join(", ", missing)}. Write them, or remove them from the declaration.");

            byte[]? taken = overview is null ? null : File.ReadAllBytes(overview);
            string readme = Path.Combine(set.Set, Overview);
            bool hadOverview = File.Exists(readme);
            if (taken is not null && hadOverview && !ReadBytes(set.Set, Overview).AsSpan().SequenceEqual(taken))
                throw new InvalidOperationException("The set's overview, README.md, differs from --overview. An overview is never overwritten: revise README.md in the set, or remove it to take the file.");

            if (keepReferences) KeptReferences(set, documents);
            else
            {
                if (set.Version != ReleaseVersion)
                    throw new InvalidOperationException($"The set is {set.Version} and this build is {ReleaseVersion}: set VersionPrefix to {set.Version}, commit and rebuild before assembling its set.");
                int generated = Generate(set.Set, declaration, json, provenanceAt, register);
                if (generated != Commands.Clean) return generated;
            }

            int changed = 0;
            foreach ((string name, string source) in copies)
                if (WriteIfChanged(set.Set, name, File.ReadAllBytes(source))) changed++;
            string state = hadOverview ? "kept" : taken is null ? "pending" : "taken from --overview";
            if (!hadOverview && taken is not null) WriteBytes(set.Set, Overview, taken);
            var pending = new List<string>();
            if (!File.Exists(readme)) pending.Add(Overview);
            if (documents.Contains(Notes) && !File.Exists(Path.Combine(set.Set, Notes))) pending.Add(Notes);
            string[] aliases = [.. copies.Where(c => Aliases.ContainsKey(c.Name)).Select(c => $"{c.Name} from {Aliases[c.Name]}")];
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { step = "assemble", output, version = set.Version, references = keepReferences ? "kept" : "generated", copied = copies.Count, changed, aliases, overview = state, pending }));
            }
            else
            {
                string waiting = pending.Count == 0 ? string.Empty
                    : "; pending: " + string.Join(", ", pending.Select(p => p == Overview ? "README.md, the set's overview (write it, or pass --overview)" : "release-notes.md (vericairn docs notes renders it)"));
                Console.Out.WriteLine($"{output}: references {(keepReferences ? "kept" : "generated")}, {copies.Count} maintained guides copied byte for byte ({changed} written){(aliases.Length > 0 ? ", " + string.Join(", ", aliases) : string.Empty)}, README.md {state}{waiting}");
            }

            return Commands.Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return Commands.Fail(exception.Message);
        }
    }

    /// <summary>
    /// Inserts an approved section into one of the set's documents byte for byte, immediately before its anchor heading; refuses
    /// when the anchor is missing or repeated, when another section with the same heading is there, or when the bytes are not the
    /// approved ones. Inserting the same bytes again changes nothing.
    /// </summary>
    public static int Insert(string directory, string section, bool json, string document = Notes, string before = NotesAnchor, string? expectedHash = null)
    {
        try
        {
            SetPaths set = Locate(directory);
            RefuseIndexed(set);
            if (!DocumentPatterns.IsSafeSourcePath(document)) throw new InvalidOperationException($"{document} is not a document path inside the set.");
            byte[] block = File.ReadAllBytes(section);
            string hash = ContentHash.Sha256(block);
            if (expectedHash is not null && !string.Equals(expectedHash, hash, StringComparison.Ordinal))
                throw new InvalidOperationException($"{section} is {hash}, not the approved {expectedHash}; only the approved bytes are inserted.");
            string text = Text(block, section);
            if (text.Trim().Length == 0) throw new InvalidOperationException($"{section} is empty.");
            string heading = text.Split('\n')[0];
            if (!HeadingLine().IsMatch(heading)) throw new InvalidOperationException($"{section} must begin with its heading line, such as \"## Security\".");
            if (text.Split('\n').Contains(before, StringComparer.Ordinal)) throw new InvalidOperationException($"{section} holds the anchor line \"{before}\" itself; the section goes before it.");

            byte[] original = ReadBytes(set.Set, document);
            string current = Text(original, document);
            int anchor = OnlyLine(current, before, document);
            byte[] inserted = [.. block, .. Separator(text)];
            int offset = StrictUtf8.GetByteCount(current.AsSpan(0, anchor));
            bool present = offset >= inserted.Length && original.AsSpan(offset - inserted.Length, inserted.Length).SequenceEqual(inserted);
            if (!present)
            {
                if (current.Split('\n').Contains(heading, StringComparer.Ordinal))
                    throw new InvalidOperationException($"{document} already holds a section headed \"{heading}\" whose bytes differ from {section}; an inserted section is never replaced.");
                WriteBytes(set.Set, document, [.. original.AsSpan(0, offset), .. inserted, .. original.AsSpan(offset)]);
            }

            if (json) Console.Out.WriteLine(JsonSerializer.Serialize(new { step = "insert", document, heading, hash, before, inserted = !present }));
            else Console.Out.WriteLine(present
                ? $"{document}: already holds \"{heading}\" ({hash}) before \"{before}\"; unchanged"
                : $"{document}: inserted \"{heading}\" ({hash}) byte for byte before \"{before}\"");
            return Commands.Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Commands.Fail(exception.Message);
        }
    }

    /// <summary>
    /// Freezes a complete set: every file's SHA-256 added to the checksum index under its version. Refuses a set the shipped
    /// command line's check finds incomplete, a file the set should not hold, and a frozen set whose files changed; freezing an
    /// unchanged frozen set changes nothing.
    /// </summary>
    public static int Freeze(string directory, bool json, string? declaration = null)
    {
        try
        {
            SetPaths set = Locate(directory);
            if (!Directory.Exists(set.Set)) throw new InvalidOperationException($"{directory}: no such documentation set.");
            JsonObject index = ReadIndex(set);
            RefuseLinks(set.Set);
            SortedDictionary<string, string> hashes = Hashes(set.Set);
            JsonObject sets = index["sets"]!.AsObject();
            bool added = false;
            if (sets[set.Version] is JsonObject frozen)
            {
                List<string> differences = Differences(frozen, hashes);
                if (differences.Count > 0)
                    throw new InvalidOperationException($"{set.Version} is frozen and differs from the checksum index: {string.Join("; ", differences)}. A frozen set is never changed; a correction goes into the next set.");
            }
            else
            {
                string? declared = declaration ?? (File.Exists(Path.Combine(set.Releases, "documents.json")) ? Path.Combine(set.Releases, "documents.json") : null);
                var report = new StringWriter { NewLine = "\n" };
                int check = Docs.Check(new CommandContext(report, report), set.Set, declared, json);
                if (check != Commands.Clean)
                {
                    Console.Out.Write(report.ToString());
                    Console.Error.WriteLine($"error: {set.Version} is incomplete, so it is not frozen.");
                    return check;
                }

                using (JsonDocument manifest = JsonDocument.Parse(ReadBytes(set.Set, "manifest.json")))
                    if (Str(manifest.RootElement, "version") != set.Version)
                        throw new InvalidOperationException($"The set's manifest documents {Str(manifest.RootElement, "version")}, not {set.Version}, the version its directory names.");
                List<string> unexpected = Unexpected(set, hashes.Keys);
                if (unexpected.Count > 0)
                    throw new InvalidOperationException($"{set.Version} holds files that are neither declared nor generated: {string.Join(", ", unexpected)}. Remove them, or declare them, before freezing.");
                sets[set.Version] = new JsonObject(hashes.Select(h => KeyValuePair.Create(h.Key, (JsonNode?)JsonValue.Create(h.Value))));
                WriteShared(set.Index, IndexBytes(index));
                added = true;
            }

            if (json) Console.Out.WriteLine(JsonSerializer.Serialize(new { step = "freeze", version = set.Version, files = hashes.Count, index = ChecksumIndex, added }));
            else Console.Out.WriteLine(added
                ? $"{set.Version}: frozen, {hashes.Count} files indexed by SHA-256 in {ChecksumIndex}"
                : $"{set.Version}: already frozen; its {hashes.Count} files match {ChecksumIndex}");
            return Commands.Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return Commands.Fail(exception.Message);
        }
    }

    /// <summary>
    /// Adds a frozen set's paragraph to the release index, newest first under its sets heading, byte for byte from a file. Refuses a
    /// set that is not frozen, a paragraph that does not link the set's overview, and a set the index already describes in other
    /// words, which the index's living-document pass revises; adding the same paragraph again changes nothing.
    /// </summary>
    public static int IndexParagraph(string directory, string paragraph, bool json)
    {
        try
        {
            SetPaths set = Locate(directory);
            JsonObject index = ReadIndex(set);
            if (index["sets"]![set.Version] is null) throw new InvalidOperationException($"{set.Version} is not frozen; freeze the set before the release index describes it.");
            byte[] block = File.ReadAllBytes(paragraph);
            string text = Text(block, paragraph);
            string body = text.TrimEnd('\n');
            string link = SetLink(set.Version);
            if (body.Trim().Length == 0) throw new InvalidOperationException($"{paragraph} is empty.");
            if (!body.Contains(link, StringComparison.Ordinal)) throw new InvalidOperationException($"{paragraph} must link the set's overview as {link}.");
            if (body.Contains("\n\n", StringComparison.Ordinal) || body.Split('\n').Any(l => l.TrimStart().StartsWith('#')))
                throw new InvalidOperationException($"{paragraph} must be one paragraph, with no blank line or heading.");

            if (!File.Exists(set.ReleaseIndex)) throw new InvalidOperationException($"The release index {Overview} beside the sets is missing.");
            RefuseLink(set.ReleaseIndex);
            byte[] original = File.ReadAllBytes(set.ReleaseIndex);
            string current = Text(original, Overview);
            bool present = current.Contains(link, StringComparison.Ordinal);
            if (present && !current.Contains("\n\n" + body + "\n", StringComparison.Ordinal))
                throw new InvalidOperationException($"The release index already describes the {set.Version} set in other words; its living-document pass revises that paragraph, never this command.");
            if (!present)
            {
                int heading = OnlyLine(current, SetsHeading, Overview);
                int after = current.IndexOf('\n', heading);
                if (after < 0) throw new InvalidOperationException($"The release index ends at \"{SetsHeading}\"; it needs a line break after the heading.");
                after++;
                bool blank = after < current.Length && current[after] == '\n';
                int offset = StrictUtf8.GetByteCount(current.AsSpan(0, blank ? after + 1 : after));
                byte[] lead = blank ? [] : "\n"u8.ToArray();
                byte[] inserted = [.. lead, .. block, .. Separator(text)];
                WriteShared(set.ReleaseIndex, [.. original.AsSpan(0, offset), .. inserted, .. original.AsSpan(offset)]);
            }

            if (json) Console.Out.WriteLine(JsonSerializer.Serialize(new { step = "index", version = set.Version, index = Overview, added = !present }));
            else Console.Out.WriteLine(present
                ? $"{set.Version}: the release index already holds its paragraph; unchanged"
                : $"{set.Version}: paragraph added to the release index under \"{SetsHeading}\", newest first");
            return Commands.Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return Commands.Fail(exception.Message);
        }
    }

    /// <summary>
    /// Prepares a set for a version in one command: assemble, the notes from the estate's records when the set has none, the
    /// approved section, the comparison with this build, the freeze and the release index's paragraph. A frozen set is only
    /// confirmed against its index and described; a step that refuses stops the rest.
    /// </summary>
    public static int Prepare(string output, string declaration, bool json, string? estate = null, string? from = null, string? since = null, string? overview = null,
        string? security = null, string? securityHash = null, string? paragraph = null, string? guides = null, bool keepReferences = false)
    {
        SetPaths set;
        bool frozen;
        try
        {
            set = Locate(output);
            frozen = ReadIndex(set)["sets"]![set.Version] is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return Commands.Fail(exception.Message);
        }

        if (frozen)
        {
            int confirmed = Freeze(set.Set, json, declaration);
            if (confirmed != Commands.Clean) return confirmed;
        }
        else
        {
            int code = Assemble(set.Set, declaration, json, overview, guides, keepReferences);
            if (code != Commands.Clean) return code;
            if (HasNotes(set))
            {
                if (json) Console.Out.WriteLine(JsonSerializer.Serialize(new { step = "notes", rendered = false }));
                else Console.Out.WriteLine($"{Notes}: kept with the window its manifest records; vericairn docs notes renders them again, and the approved section is then inserted again");
            }
            else
            {
                if (estate is null || from is null) return Commands.Fail("The set has no notes yet; name the estate with --estate and the previous release with --from to render them.");
                code = Docs.Notes(estate, from, set.Version, set.Set, since, json);
                if (code != Commands.Clean) return code;
            }

            if (security is not null)
            {
                code = Insert(set.Set, security, json, expectedHash: securityHash);
                if (code != Commands.Clean) return code;
            }

            code = CompareWithBuild(set.Set, json);
            if (code != Commands.Clean) return code;
            code = Freeze(set.Set, json, declaration);
            if (code != Commands.Clean) return code;
        }

        return paragraph is null ? Commands.Clean : IndexParagraph(set.Set, paragraph, json);
    }

    private static SetPaths Locate(string directory)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string version = Path.GetFileName(full);
        if (!SetVersion().IsMatch(version))
            throw new InvalidOperationException($"A documentation set's directory is named for its version, such as docs/releases/0.9.2; {version} is not a version.");
        string releases = Path.GetDirectoryName(full) ?? throw new InvalidOperationException("A documentation set lives in a directory of sets.");
        return new(full, version, releases, Path.Combine(releases, ChecksumIndex), Path.Combine(releases, Overview));
    }

    private static void RefuseIndexed(SetPaths set)
    {
        if (File.Exists(set.Index) && ReadIndex(set)["sets"]![set.Version] is not null)
            throw new InvalidOperationException($"The documentation set {set.Version} is frozen; a frozen set is never changed, and a correction goes into the next set.");
        RefuseFrozen(set.Set);
    }

    private static List<string> Declared(string declaration)
    {
        using JsonDocument declared = JsonDocument.Parse(File.ReadAllBytes(declaration));
        if (declared.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException($"{declaration} must be a JSON array of document names.");
        var documents = new List<string>();
        foreach (JsonElement name in declared.RootElement.EnumerateArray())
        {
            if (name.ValueKind != JsonValueKind.String || !DocumentPatterns.IsSafeSourcePath(name.GetString()!) || documents.Contains(name.GetString()!))
                throw new InvalidOperationException($"{declaration} must name each document once, as a relative path inside the set.");
            documents.Add(name.GetString()!);
        }

        return documents;
    }

    // A set assembled against references generated earlier: its manifest must already document this version and these documents.
    private static void KeptReferences(SetPaths set, List<string> documents)
    {
        string path = Path.Combine(set.Set, "manifest.json");
        if (!File.Exists(path)) throw new InvalidOperationException($"The set {set.Version} holds no generated references to keep; assemble it without --keep-references.");
        using JsonDocument manifest = JsonDocument.Parse(ReadBytes(set.Set, "manifest.json"));
        if (Str(manifest.RootElement, "version") != set.Version)
            throw new InvalidOperationException($"The kept manifest documents {Str(manifest.RootElement, "version")}, not {set.Version}.");
        if (!Strings(manifest.RootElement, "documents").ToHashSet(StringComparer.Ordinal).SetEquals(documents))
            throw new InvalidOperationException("The kept manifest lists other documents than the declaration; generate the references again.");
    }

    private static bool HasNotes(SetPaths set)
    {
        string manifest = Path.Combine(set.Set, "manifest.json");
        if (!File.Exists(Path.Combine(set.Set, Notes)) || !File.Exists(manifest)) return false;
        using JsonDocument document = JsonDocument.Parse(ReadBytes(set.Set, "manifest.json"));
        return document.RootElement.TryGetProperty("notes", out JsonElement window) && window.ValueKind == JsonValueKind.Object;
    }

    private static bool WriteIfChanged(string directory, string name, byte[] bytes)
    {
        string path = Path.Combine(directory, name);
        if (File.Exists(path) && ReadBytes(directory, name).AsSpan().SequenceEqual(bytes)) return false;
        WriteBytes(directory, name, bytes);
        return true;
    }

    private static string Text(byte[] bytes, string name)
    {
        string text;
        try { text = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { throw new InvalidOperationException($"{name} is not UTF-8 text."); }
        if (text.Contains('\r', StringComparison.Ordinal))
            throw new InvalidOperationException($"{name} has carriage returns; a set's documents end their lines with LF alone, and inserted bytes are never converted.");
        return text;
    }

    // The position of the one line equal to the given text; a missing or repeated anchor is refused.
    private static int OnlyLine(string text, string line, string name)
    {
        var starts = new List<int>();
        for (int start = 0; start <= text.Length;)
        {
            int end = text.IndexOf('\n', start);
            if (text.AsSpan(start, (end < 0 ? text.Length : end) - start).SequenceEqual(line)) starts.Add(start);
            if (end < 0) break;
            start = end + 1;
        }

        return starts.Count switch
        {
            1 => starts[0],
            0 => throw new InvalidOperationException($"{name} has no line \"{line}\"."),
            _ => throw new InvalidOperationException($"{name} has {starts.Count} lines \"{line}\"; the anchor must be one line."),
        };
    }

    // One blank line separates inserted text from what follows it, whatever its own last line ends with.
    private static byte[] Separator(string text) => text.EndsWith("\n\n", StringComparison.Ordinal) ? [] : text.EndsWith('\n') ? "\n"u8.ToArray() : "\n\n"u8.ToArray();

    private static string SetLink(string version) => $"[{version} set]({version}/README.md)";

    private static JsonObject ReadIndex(SetPaths set)
    {
        if (!File.Exists(set.Index)) throw new InvalidOperationException($"The checksum index {ChecksumIndex} beside the sets is missing.");
        RefuseLink(set.Index);
        JsonObject index = JsonNode.Parse(File.ReadAllBytes(set.Index)) as JsonObject ?? throw new InvalidOperationException($"{ChecksumIndex} is not a JSON object.");
        if (index["format"]?.GetValueKind() != JsonValueKind.String || index["format"]!.GetValue<string>() != FrozenFormat || index["sets"] is not JsonObject)
            throw new InvalidOperationException($"{ChecksumIndex} must declare {FrozenFormat} and its sets.");
        return index;
    }

    // Two-space indentation, LF and one final line break, as the index has always been written; ASCII paths stay unescaped.
    private static byte[] IndexBytes(JsonObject index)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            index.WriteTo(writer);
        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    private static SortedDictionary<string, string> Hashes(string directory)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            hashes.Add(Path.GetRelativePath(directory, path).Replace('\\', '/'), ContentHash.Sha256File(path));
        return hashes;
    }

    private static List<string> Differences(JsonObject frozen, SortedDictionary<string, string> hashes)
    {
        var differences = new List<string>();
        foreach ((string path, JsonNode? hash) in frozen)
        {
            if (!hashes.TryGetValue(path, out string? actual)) differences.Add(path + " missing");
            else if (hash?.GetValueKind() != JsonValueKind.String || hash.GetValue<string>() != actual) differences.Add(path + " changed");
        }

        differences.AddRange(hashes.Keys.Where(p => !frozen.ContainsKey(p)).Select(p => p + " added"));
        return differences;
    }

    // A set holds its declared documents and what the build tooling generated, as its manifest names them, and nothing else.
    private static List<string> Unexpected(SetPaths set, IEnumerable<string> files)
    {
        using JsonDocument document = JsonDocument.Parse(ReadBytes(set.Set, "manifest.json"));
        JsonElement manifest = document.RootElement;
        var members = new HashSet<string>(Strings(manifest, "documents"), StringComparer.Ordinal) { "manifest.json" };
        if (Str(manifest, "referenceInventory") is { } inventory) members.Add(inventory);
        if (manifest.TryGetProperty("companions", out JsonElement companions) && companions.ValueKind == JsonValueKind.Object)
        {
            foreach (string list in new[] { "inputs", "referenceInputs", "outputs" })
                if (companions.TryGetProperty(list, out JsonElement entries) && entries.ValueKind == JsonValueKind.Array)
                    members.UnionWith(entries.EnumerateArray().Select(e => Str(e, "path")).OfType<string>());
        }

        return [.. files.Where(f => !members.Contains(f))];
    }

    private static void RefuseLinks(string directory)
    {
        foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"{Path.GetRelativePath(directory, entry.FullName).Replace('\\', '/')} is a symbolic link; a set holds its own files.");
    }

    private static void RefuseLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"{Path.GetFileName(path)} is a symbolic link; the shared files beside the sets are written in place.");
    }

    // The index and the release index are shared by every set: a new version replaces the old whole, never a partial write.
    private static void WriteShared(string path, byte[] bytes)
    {
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$")]
    private static partial Regex SetVersion();

    [GeneratedRegex(@"^#{1,6} \S")]
    private static partial Regex HeadingLine();
}
