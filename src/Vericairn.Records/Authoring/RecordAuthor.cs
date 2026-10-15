// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Authoring;

/// <summary>What a new record should contain.</summary>
/// <param name="Type">Record type.</param>
/// <param name="By">Authoring principal id.</param>
/// <param name="Body">Type-specific body (an object).</param>
/// <param name="Subject">Optional subject reference.</param>
/// <param name="Supersedes">Optional predecessor id.</param>
/// <param name="At">Authoring time; now when omitted.</param>
/// <param name="Id">Explicit id; generated when omitted (only the root operator principal needs one).</param>
/// <param name="Format">The record format to write; the writer of an estate passes the estate's own format, and format 1 is the default.</param>
public sealed record RecordDraft(string Type, string By, JsonElement Body, RecordReference? Subject = null, string? Supersedes = null, DateTimeOffset? At = null, string? Id = null, long? Format = null);

/// <summary>A record ready to be written.</summary>
/// <param name="RelativePath">Where it belongs in the estate repository.</param>
/// <param name="Bytes">Canonical pretty file bytes.</param>
/// <param name="Envelope">The parsed envelope.</param>
public sealed record AuthoredRecord(string RelativePath, byte[] Bytes, RecordEnvelope Envelope);

/// <summary>Creates records in canonical form with their hash.</summary>
public static class RecordAuthor
{
    /// <summary>Builds the canonical bytes of a new record, signed by the device when a signer is given.</summary>
    public static AuthoredRecord Create(RecordDraft draft, IRecordSigner? signer = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Body.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The body must be a JSON object.", nameof(draft));
        }

        long format = draft.Format ?? RecordFormats.First;
        if (format < RecordFormats.First || format > RecordFormats.Current)
        {
            throw new ArgumentException($"This build writes record formats {RecordFormats.First} to {RecordFormats.Current}, not {format}.", nameof(draft));
        }

        string at = RecordParser.FormatTimestamp(draft.At ?? DateTimeOffset.UtcNow);
        DateTimeOffset atTime = DateTimeOffset.ParseExact(at, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        string id = draft.Id ?? Ulid.NewUlid(atTime).Value;
        var envelope = new JsonObject
        {
            ["format"] = RecordFormats.Identifier(format),
            ["type"] = draft.Type,
            ["id"] = id,
            ["at"] = at,
            ["by"] = draft.By,
            ["body"] = JsonNode.Parse(draft.Body.GetRawText()),
        };
        if (draft.Subject is not null)
        {
            var subject = new JsonObject { ["type"] = draft.Subject.Type, ["id"] = draft.Subject.Id };
            if (draft.Subject.Hash is not null)
            {
                subject["hash"] = draft.Subject.Hash;
            }

            envelope["subject"] = subject;
        }

        if (draft.Supersedes is not null)
        {
            envelope["supersedes"] = draft.Supersedes;
        }

        string hash;
        using (JsonDocument unhashed = JsonDocument.Parse(envelope.ToJsonString()))
        {
            hash = ContentHash.Sha256(CanonicalJson.SerializeUtf8(unhashed.RootElement, RecordParser.HashExcludedKeys));
        }

        envelope["hash"] = hash;
        if (signer is not null)
        {
            RecordSignature signature = signer.Sign(RecordSigning.Digest(hash));
            envelope["signatures"] = new JsonArray(new JsonObject { ["algorithm"] = signature.Algorithm, ["device"] = signature.Device, ["signature"] = signature.Signature });
        }

        byte[] bytes;
        using (JsonDocument hashed = JsonDocument.Parse(envelope.ToJsonString()))
        {
            bytes = CanonicalJson.SerializePrettyUtf8(hashed.RootElement);
        }

        var findings = new FindingList();
        RecordEnvelope parsed = RecordParser.Parse(bytes, "(new record)", findings) ?? throw new InvalidOperationException("The new record is not valid: " + string.Join("; ", findings.Select(f => f.Message)));
        return new AuthoredRecord(parsed.ExpectedPath, bytes, parsed);
    }

    /// <summary>Reformats existing record bytes into canonical pretty form without changing content or hash.</summary>
    public static byte[] Format(ReadOnlyMemory<byte> bytes)
    {
        using JsonDocument document = CanonicalJson.Parse(bytes);
        return CanonicalJson.SerializePrettyUtf8(document.RootElement);
    }

    /// <summary>Writes a new record file; never overwrites.</summary>
    public static string Write(string root, AuthoredRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(record);
        string full = Path.Combine(root, record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full))
        {
            throw new IOException($"{record.RelativePath} already exists; records are never overwritten.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + ".tmp";
        File.WriteAllBytes(temporary, record.Bytes);
        File.Move(temporary, full, overwrite: false);
        return full;
    }
}
