// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Search;
using Vericairn.Records.Types;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Search;

/// <summary>
/// The searches a run's MCP server served its agent. The server appends each selection to the run's own directory, machine-local
/// and owner-only; settlement records them as <c>searchesObserved</c>, at most 100 in full and the digest of all. Searches made
/// any other way are ordinary reads and are not recorded.
/// </summary>
internal static class ObservedSearches
{
    // A settlement record stays well within the repository's file limit beside the run's other facts.
    private const long RecordedBytes = 448 * 1024;
    private const long MaximumLogBytes = 64L * 1024 * 1024;

    /// <summary>Appends one served selection to a run's log, when this machine holds the run's directory; never creates the directory.</summary>
    internal static bool Append(string estate, string run, JsonElement selection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(estate);
        if (!Records.Identity.Ulid.IsValid(run) || selection.ValueKind != JsonValueKind.Object) return false;
        var directory = new RunDirectory(estate, run);
        if (!directory.Exists || new DirectoryInfo(directory.Path).LinkTarget is not null) return false;
        var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.Read };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            if (new FileInfo(directory.SearchesFile) is { Exists: true } existing && (existing.LinkTarget is not null || existing.Length > MaximumLogBytes)) return false;
            byte[] line = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(selection) + "\n");
            using var stream = new FileStream(directory.SearchesFile, options);
            stream.Write(line);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CanonicalJsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// What settlement records, or null when the server served nothing: each valid selection as a record keeps it (omitting
    /// candidates every record reader cannot read), at most 100 and within a byte budget, whether that is every observed search,
    /// and the digest of all of them. A line that is not a valid selection makes the list incomplete.
    /// </summary>
    internal static JsonObject? Read(RunDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var info = new FileInfo(directory.SearchesFile);
        if (!info.Exists) return null;
        bool complete = info.LinkTarget is null && info.Length <= MaximumLogBytes;
        var hashes = new List<string>();
        var kept = new JsonArray();
        long bytes = 0;
        if (complete)
        {
            foreach (string line in File.ReadLines(directory.SearchesFile))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JsonObject? recorded = null;
                try
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    var findings = new FindingList();
                    SearchSelections.Validate(new BodyReader(document.RootElement, "selection", findings, RecordFormats.Current));
                    if (!findings.HasErrors) recorded = EstateService.ForRecord(document.RootElement);
                }
                catch (JsonException) { }

                if (recorded is null)
                {
                    complete = false;
                    continue;
                }

                hashes.Add(recorded["hash"]!.GetValue<string>());
                long size = CanonicalJson.SerializePrettyUtf8(JsonSerializer.SerializeToElement(recorded)).LongLength;
                if (kept.Count < SearchSelections.MaxObservedSelections && bytes + size <= RecordedBytes)
                {
                    kept.Add(recorded);
                    bytes += size;
                }
            }
        }

        if (hashes.Count == 0 && complete) return null;
        return new JsonObject
        {
            ["complete"] = complete && kept.Count == hashes.Count,
            ["digest"] = SearchSelections.ListDigest(hashes),
            ["selections"] = kept,
        };
    }
}
