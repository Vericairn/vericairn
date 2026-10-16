// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Authoring;

/// <summary>
/// A device's first record, built one way wherever a device is recorded: on the machine that makes its key, and by a runtime that
/// pairs a device whose key it never holds, where the device signs the very record the runtime writes.
/// </summary>
public static class DeviceRecords
{
    /// <summary>The body of a device's first record; record format 2 adds presence (none unless given) and purpose.</summary>
    public static JsonObject Body(string principal, string name, string platform, string custody, string publicKey, IReadOnlyList<string> profiles, long format, string? presence = null, string purpose = "signing")
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var body = new JsonObject
        {
            ["principal"] = principal,
            ["name"] = name,
            ["platform"] = platform,
            ["custody"] = custody,
            ["algorithm"] = RecordSigning.Algorithm,
            ["publicKey"] = publicKey,
            ["profiles"] = new JsonArray(profiles.Select(p => (JsonNode)p).ToArray()),
        };
        if (format >= 2)
        {
            body["presence"] = presence ?? "none";
            body["purpose"] = purpose;
        }

        return body;
    }

    /// <summary>The draft of a device's first record: authored by its principal, with the id that becomes its identity, at a time, in a format.</summary>
    public static RecordDraft Draft(string principal, string id, DateTimeOffset at, long format, JsonObject body)
    {
        ArgumentNullException.ThrowIfNull(body);
        using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
        return new RecordDraft("device", principal, document.RootElement.Clone(), At: at, Id: id, Format: format);
    }

    /// <summary>The content hash of a draft: what its device signs to prove it holds the key the record names.</summary>
    public static string Hash(RecordDraft draft) => RecordAuthor.Create(draft).Envelope.ComputedHash;
}
