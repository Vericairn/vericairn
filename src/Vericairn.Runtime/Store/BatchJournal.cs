// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Store;

/// <summary>
/// This machine's account of records it stores together, such as routing's assignment of a step and the run intent or the ask that
/// carries it. Storing several records is not atomic, so the journal is written before the first record and removed after the last:
/// a failure or a crash between them leaves it behind, naming every record of the batch, and the runtime reports the batch as a
/// reconcile task until it no longer matters. The journal lives beside the owning session's live store, under the Vericairn home,
/// never in the estate. Only that storage identity judges it, and nothing it names is ever removed from the estate.
/// </summary>
internal sealed class BatchJournal
{
    private readonly string file;

    private BatchJournal(string file, string id, string purpose, string? workOrder, string startedAt, IReadOnlyList<Entry> records)
    {
        this.file = file;
        Id = id;
        Purpose = purpose;
        WorkOrder = workOrder;
        StartedAt = startedAt;
        Records = records;
    }

    /// <summary>The batch's identifier.</summary>
    public string Id { get; }

    /// <summary>What the batch stores, in words.</summary>
    public string Purpose { get; }

    /// <summary>The work order whose step the batch was for, when it was for one.</summary>
    public string? WorkOrder { get; }

    /// <summary>When the batch began.</summary>
    public string StartedAt { get; }

    /// <summary>The batch's records, in the order they are stored.</summary>
    public IReadOnlyList<Entry> Records { get; }

    /// <summary>Where this session's journals live: per clone for repository-synced estates, shared for local-only estates.</summary>
    public static string Directory(EstateSession session) => Path.Combine(Path.GetDirectoryName(session.Live.Path)!, "batches");

    /// <summary>Writes the journal of a batch before any of its records is stored.</summary>
    public static BatchJournal Begin(EstateSession session, string purpose, string? workOrder, IReadOnlyList<AuthoredRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        string id = Ulid.NewUlid().Value;
        string directory = Directory(session);
        System.IO.Directory.CreateDirectory(directory);
        string startedAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
        List<Entry> entries = [.. records.Select(r => new Entry(r.Envelope.Id, r.Envelope.Type, r.RelativePath))];
        var json = new JsonObject
        {
            ["batch"] = id,
            ["purpose"] = purpose,
            ["workOrder"] = workOrder,
            ["startedAt"] = startedAt,
            ["records"] = new JsonArray([.. entries.Select(e => (JsonNode)new JsonObject { ["id"] = e.Id, ["type"] = e.Type, ["path"] = e.Path })]),
        };
        string file = Path.Combine(directory, id + ".json");
        string pending = file + ".tmp";
        File.WriteAllText(pending, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        File.Move(pending, file, overwrite: true);
        return new BatchJournal(file, id, purpose, workOrder, startedAt, entries);
    }

    /// <summary>
    /// Reads journals in filename order, separating usable journals from diagnostics. A missing directory is empty; an unreadable
    /// directory or file is uncertainty, never absence. Nothing is removed here, and diagnostics contain no raw filename or exception.
    /// </summary>
    public static ReadResult All(EstateSession session)
    {
        string directory = Directory(session);
        string[] files;
        try
        {
            files = System.IO.Directory.GetFiles(directory, "*.json");
        }
        catch (DirectoryNotFoundException) when (!File.Exists(directory))
        {
            return new ReadResult([], []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ReadResult([], [new UnreadableJournal("batch-journal:directory", "The journal directory could not be read.")]);
        }

        var journals = new List<BatchJournal>();
        var unreadable = new List<UnreadableJournal>();
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));
                BatchJournal? journal = Read(file, document.RootElement);
                if (journal is null)
                {
                    unreadable.Add(new UnreadableJournal(LocalId(file), "The journal is incomplete or has an invalid structure."));
                }
                else
                {
                    journals.Add(journal);
                }
            }
            catch (JsonException)
            {
                unreadable.Add(new UnreadableJournal(LocalId(file), "The journal contains malformed or truncated JSON."));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                unreadable.Add(new UnreadableJournal(LocalId(file), "The journal could not be read."));
            }
        }

        return new ReadResult(journals, unreadable);
    }

    // A filename is untrusted local data. Preserve a generated ULID for locating a normal journal, otherwise identify the basename
    // by its full digest. Neither a machine path, arbitrary filename text nor a parser/OS exception enters a finding.
    private static string LocalId(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        return "batch-journal:" + (Ulid.IsValid(name) ? name : "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFileName(file)))));
    }

    private static BatchJournal? Read(string file, JsonElement root)
    {
        if (!Object(root)) return null;
        string? id = Text(root, "batch");
        string? purpose = Text(root, "purpose");
        string? startedAt = Text(root, "startedAt");
        if (!Ulid.IsValid(id) || id != Path.GetFileNameWithoutExtension(file) || string.IsNullOrWhiteSpace(purpose)
            || !RecordParser.IsTimestamp(startedAt) || !root.TryGetProperty("workOrder", out JsonElement workOrder)
            || (workOrder.ValueKind != JsonValueKind.Null && (workOrder.ValueKind != JsonValueKind.String || !Ulid.IsValid(workOrder.GetString())))
            || !root.TryGetProperty("records", out JsonElement records) || records.ValueKind != JsonValueKind.Array || records.GetArrayLength() == 0)
        {
            return null;
        }

        var entries = new List<Entry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement record in records.EnumerateArray())
        {
            if (!Object(record)) return null;
            string? recordId = Text(record, "id");
            string? type = Text(record, "type");
            string? path = Text(record, "path");
            if (recordId is null || !Ulid.IsValid(recordId) || !ids.Add(recordId) || type is null || RecordTypeRegistry.Find(type) is null || path is null)
            {
                return null;
            }

            string[] parts = path.Split('/');
            if (parts.Length != 4 || parts[0] != "records" || parts[1] != type || parts[3] != recordId + ".json"
                || !DateTime.TryParseExact(parts[2], "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                return null;
            }

            entries.Add(new Entry(recordId, type, path));
        }

        return new BatchJournal(file, id!, purpose, workOrder.ValueKind == JsonValueKind.Null ? null : workOrder.GetString(), startedAt!, entries);

        static bool Object(JsonElement value) => value.ValueKind == JsonValueKind.Object
            && value.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == value.EnumerateObject().Count();
        static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>Usable journals and journals that must be recovered before any further record writes.</summary>
    public sealed record ReadResult(IReadOnlyList<BatchJournal> Journals, IReadOnlyList<UnreadableJournal> Unreadable);

    /// <summary>A stable local identifier and a safe reason, never a machine path or untrusted journal content.</summary>
    public sealed record UnreadableJournal(string Id, string Reason);

    /// <summary>Removes the journal: its batch was stored whole, stored nothing, or no longer matters. It removes no record.</summary>
    public void Close()
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A journal that cannot be removed now is judged again next time.
        }
    }

    /// <summary>One record of a batch.</summary>
    /// <param name="Id">The record's identifier.</param>
    /// <param name="Type">Its type.</param>
    /// <param name="Path">Its path in the estate.</param>
    public sealed record Entry(string Id, string Type, string Path);
}
