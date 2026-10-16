// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

/// <summary>Compares identities, never timestamps: imported and backdated records count as additions too.</summary>
internal static class HandoffComparison
{
    private const string Caution = "The world may have moved. This compares this copy's records, pin and recorded observations; it does not establish live external state. Recheck current authority and required originals before acting. Nothing is restarted or accepted.";

    internal static ResumeResponse Compare(HandoffPackage package, EstateModel current, IReadOnlyList<RecordFile> files, string estate)
    {
        Dictionary<string, JsonElement> before;
        try
        {
            // The opened session supplies identity even if the current manifest has become unreadable.
            before = Validate(package, estate);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or KeyNotFoundException or FormatException or CanonicalJsonException or OverflowException)
        {
            const string message = "The retained hand-over is invalid, incomplete, from another estate or uses an unsupported contract. Supply the unchanged package produced by handoff; do not repair its contents by hand.";
            return new ResumeResponse(message + "\n", null, true, new Problem("invalid-handoff", message, []));
        }

        // Match the loader's first occurrence of each id, including a record at an invalid location.
        // A resume reports damaged current state; it must not assume the source's expected path exists.
        var byId = new Dictionary<string, RecordFile>(StringComparer.Ordinal);
        foreach (RecordFile file in files.Where(f => f.Path.EndsWith(".json", StringComparison.Ordinal)).OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            if (RecordParser.Parse(file.Bytes, file.Path, new FindingList()) is { } record)
            {
                byId.TryAdd(record.Id, file);
            }
        }

        var added = new JsonArray();
        var replaced = new JsonArray();
        var missing = new JsonArray();
        var superseded = new JsonArray();
        foreach (RecordEnvelope record in current.Records.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            RecordFile file = byId[record.Id];
            var identity = new JsonObject
            {
                ["id"] = record.Id,
                ["type"] = record.Type,
                ["hash"] = record.ComputedHash,
                ["byteHash"] = ContentHash.Sha256(file.Bytes),
                ["path"] = file.Path,
                ["original"] = JsonNode.Parse(file.Bytes),
            };
            if (!before.TryGetValue(record.Id, out JsonElement previous))
            {
                added.Add(identity);
                if (record.Supersedes is string predecessor)
                {
                    superseded.Add(new JsonObject { ["previous"] = predecessor, ["successor"] = record.Id, ["hash"] = record.ComputedHash });
                }
            }
            else if (previous.GetProperty("hash").GetString() != record.ComputedHash || previous.GetProperty("byteHash").GetString() != ContentHash.Sha256(file.Bytes) || previous.GetProperty("path").GetString() != file.Path)
            {
                identity["previousHash"] = previous.GetProperty("hash").GetString();
                identity["previousByteHash"] = previous.GetProperty("byteHash").GetString();
                identity["previousPath"] = previous.GetProperty("path").GetString();
                replaced.Add(identity);
            }
        }

        foreach ((string id, JsonElement source) in before.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!current.ById.ContainsKey(id))
            {
                missing.Add(new JsonObject { ["id"] = id, ["type"] = source.GetProperty("type").GetString(), ["hash"] = source.GetProperty("hash").GetString(), ["retrieve"] = source.GetProperty("retrieve").GetString(), ["state"] = "missing from this copy; original retained in the hand-over" });
            }
        }

        JsonNode? oldPin = JsonNode.Parse(package.Payload.GetProperty("pin").GetRawText());
        JsonNode? newPin = JsonSerializer.SerializeToNode(current.Manifest, ProtocolJson.Options);
        bool pinChanged = !JsonNode.DeepEquals(oldPin, newPin);
        JsonNode? oldState = JsonNode.Parse(package.Payload.GetProperty("observedState").GetRawText());
        JsonNode newState = current.Findings.HasErrors
            ? new JsonObject { ["unavailable"] = "Current records are invalid; review every finding and source change before continuing." }
            : HandoffAssembly.State(current);
        var observedChanges = new JsonArray();
        Differences(oldState, newState, string.Empty, observedChanges);
        bool hasErrors = current.Findings.HasErrors || missing.Count > 0 || replaced.Count > 0;
        var view = new JsonObject
        {
            ["format"] = "vericairn.resume/1",
            ["basis"] = package.Hash,
            ["estate"] = estate,
            ["caution"] = Caution,
            ["changed"] = added.Count > 0 || replaced.Count > 0 || missing.Count > 0 || pinChanged || observedChanges.Count > 0,
            ["hasErrors"] = hasErrors,
            ["added"] = added,
            ["superseded"] = superseded,
            ["missing"] = missing,
            ["replaced"] = replaced,
            ["pin"] = new JsonObject { ["changed"] = pinChanged, ["before"] = oldPin, ["after"] = newPin },
            ["observedChanges"] = observedChanges,
            ["observedState"] = newState,
            ["findings"] = JsonSerializer.SerializeToNode(current.Findings.Sorted().Select(f => new FindingDto(f.Severity.ToString().ToLowerInvariant(), f.Code, f.Location, f.Message)), ProtocolJson.Options),
        };
        JsonElement json = JsonSerializer.SerializeToElement(view, ProtocolJson.Options);
        return new ResumeResponse("Resume from " + package.Hash + "\n" + Caution + "\n" +
            (view["changed"]!.GetValue<bool>() ? "Changes follow in full.\n" : "No changes observed in this copy.\n") + CanonicalJson.SerializePretty(json), json, hasErrors, null);
    }

    private static Dictionary<string, JsonElement> Validate(HandoffPackage package, string? estate)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Payload.ValueKind != JsonValueKind.Object || package.Payload.GetProperty("format").GetString() != HandoffAssembly.Format ||
            package.Payload.GetProperty("estate").GetString() != estate)
        {
            throw new InvalidOperationException();
        }

        byte[] bytes = CanonicalJson.SerializePrettyUtf8(package.Payload);
        if (package.Bytes != bytes.LongLength || package.Hash != ContentHash.Sha256(bytes))
        {
            throw new InvalidOperationException();
        }

        var originals = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var findings = new FindingList();
        foreach (JsonElement source in package.Payload.GetProperty("originals").EnumerateArray())
        {
            string id = source.GetProperty("id").GetString()!;
            byte[] original = Convert.FromBase64String(source.GetProperty("content").GetString()!);
            string path = source.GetProperty("path").GetString()!;
            RecordEnvelope? record = RecordParser.Parse(original, path, findings);
            if (record is null || findings.HasErrors || record.Id != id || record.ComputedHash != source.GetProperty("hash").GetString() ||
                record.ExpectedPath != path || record.Type != source.GetProperty("type").GetString() || source.GetProperty("encoding").GetString() != "base64" ||
                original.LongLength != source.GetProperty("bytes").GetInt64() || ContentHash.Sha256(original) != source.GetProperty("byteHash").GetString() ||
                source.GetProperty("retrieve").GetString() != "vericairn records show " + id || !originals.TryAdd(id, source))
            {
                throw new InvalidOperationException();
            }
        }

        JsonElement basis = package.Payload.GetProperty("presentation").GetProperty("basis");
        if (basis.GetArrayLength() != originals.Count || package.Payload.GetProperty("observedState").GetProperty("status").GetProperty("records").GetInt32() != originals.Count ||
            package.Payload.GetProperty("withheld").GetArrayLength() != 0 ||
            basis.EnumerateArray().Any(r => !originals.TryGetValue(r.GetProperty("id").GetString()!, out JsonElement original) || r.GetProperty("hash").GetString() != original.GetProperty("hash").GetString()) ||
            package.Payload.GetProperty("pin").GetProperty("estate").GetString() != estate)
        {
            throw new InvalidOperationException();
        }

        return originals;
    }

    private static void Differences(JsonNode? before, JsonNode? after, string path, JsonArray changes)
    {
        if (JsonNode.DeepEquals(before, after))
        {
            return;
        }

        if (before is JsonObject left && after is JsonObject right)
        {
            foreach (string key in left.Select(p => p.Key).Union(right.Select(p => p.Key), StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                string next = path + "/" + key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
                if (left.ContainsKey(key) != right.ContainsKey(key))
                {
                    changes.Add(new JsonObject { ["path"] = next, ["beforePresent"] = left.ContainsKey(key), ["afterPresent"] = right.ContainsKey(key), ["before"] = left[key]?.DeepClone(), ["after"] = right[key]?.DeepClone() });
                }
                else
                {
                    Differences(left[key], right[key], next, changes);
                }
            }

            return;
        }

        // Arrays are retained whole on both sides, including ordering and every changed entry.
        changes.Add(new JsonObject { ["path"] = path, ["before"] = before?.DeepClone(), ["after"] = after?.DeepClone() });
    }
}
