// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Store;

/// <summary>
/// This machine's account of records it stores together, such as routing's assignment of a step and the run intent or the ask that
/// carries it. Storing several records is not atomic, so the journal is written before the first record and removed after the last:
/// a failure or a crash between them leaves it behind, naming every record of the batch, and the runtime reports the batch as a
/// reconcile task until it no longer matters. The journal lives under the Vericairn home, never in the estate, and nothing it names
/// is ever removed from the estate.
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

    /// <summary>Where an estate's journals live on this machine.</summary>
    public static string Directory(string estate) => Path.Combine(VericairnHome.Path(), "estates", estate, "batches");

    /// <summary>Writes the journal of a batch before any of its records is stored.</summary>
    public static BatchJournal Begin(string estate, string purpose, string? workOrder, IReadOnlyList<AuthoredRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        string id = Ulid.NewUlid().Value;
        string directory = Directory(estate);
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

    /// <summary>Every journal of an estate left on this machine, oldest first; one that cannot be read is left alone.</summary>
    public static IReadOnlyList<BatchJournal> All(string estate)
    {
        string directory = Directory(estate);
        if (!System.IO.Directory.Exists(directory))
        {
            return [];
        }

        var journals = new List<BatchJournal>();
        foreach (string file in System.IO.Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));
                JsonElement root = document.RootElement;
                string? Text(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                List<Entry> entries = root.TryGetProperty("records", out JsonElement records) && records.ValueKind == JsonValueKind.Array
                    ? [.. records.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object).Select(r => new Entry(Text(r, "id") ?? "?", Text(r, "type") ?? "?", Text(r, "path") ?? "?"))]
                    : [];
                journals.Add(new BatchJournal(file, Text(root, "batch") ?? Path.GetFileNameWithoutExtension(file), Text(root, "purpose") ?? "a batch of records", Text(root, "workOrder"), Text(root, "startedAt") ?? "?", entries));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // A journal that cannot be read now is read again next time.
            }
        }

        return journals;
    }

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
