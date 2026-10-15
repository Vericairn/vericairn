// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Model;

/// <summary>Parses and validates the common envelope of a record file.</summary>
public static partial class RecordParser
{
    /// <summary>Envelope keys every record must have.</summary>
    public static readonly IReadOnlySet<string> RequiredKeys = new HashSet<string>(StringComparer.Ordinal) { "format", "type", "id", "at", "by", "body", "hash" };

    /// <summary>Envelope keys a record may have.</summary>
    public static readonly IReadOnlySet<string> OptionalKeys = new HashSet<string>(StringComparer.Ordinal) { "subject", "supersedes", "signatures" };

    /// <summary>Envelope keys excluded from the hash.</summary>
    public static readonly IReadOnlySet<string> HashExcludedKeys = new HashSet<string>(StringComparer.Ordinal) { "hash", "signatures" };

    /// <summary>Record type names: kebab-case.</summary>
    public static Regex TypePattern => TypeRegex();

    /// <summary>Timestamps: RFC 3339, UTC, second precision.</summary>
    public static Regex TimestampPattern => TimestampRegex();

    /// <summary>True when the text is a valid record timestamp.</summary>
    public static bool IsTimestamp(string? text) =>
        text is not null && TimestampPattern.IsMatch(text) && DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _);

    /// <summary>Formats a time as a record timestamp (truncated to whole seconds, UTC).</summary>
    public static string FormatTimestamp(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses one record file. Envelope problems become findings at <paramref name="location"/>;
    /// the result is null when the envelope is unusable.
    /// </summary>
    public static RecordEnvelope? Parse(ReadOnlyMemory<byte> bytes, string location, FindingList findings, bool checkFormatting = true)
    {
        ArgumentNullException.ThrowIfNull(findings);
        JsonDocument document;
        try
        {
            document = CanonicalJson.Parse(bytes);
        }
        catch (CanonicalJsonException exception)
        {
            findings.Error("record.json", location, exception.Message);
            return null;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                findings.Error("record.envelope", location, "A record is a JSON object.");
                return null;
            }

            int before = findings.Errors;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!keys.Add(property.Name))
                {
                    findings.Error("record.envelope", location, $"Duplicate envelope key '{property.Name}'.");
                }
                else if (!RequiredKeys.Contains(property.Name) && !OptionalKeys.Contains(property.Name))
                {
                    findings.Error("record.envelope", location, $"Unknown envelope key '{property.Name}'. Type-specific content belongs under 'body'.");
                }
            }

            foreach (string key in RequiredKeys)
            {
                if (!keys.Contains(key))
                {
                    findings.Error("record.envelope", location, $"Missing envelope key '{key}'.");
                }
            }

            if (findings.Errors > before)
            {
                return null;
            }

            string? format = StringOrNull(root, "format");
            long? formatNumber = RecordFormats.Parse(format);
            if (formatNumber is null)
            {
                findings.Error("record.format", location, $"Unsupported record format '{format}'; this build reads {RecordFormats.Identifier(RecordFormats.First)} to {RecordFormats.Identifier(RecordFormats.Latest)}.");
            }

            string? type = StringOrNull(root, "type");
            if (type is null || !TypePattern.IsMatch(type))
            {
                findings.Error("record.type", location, "The record type must be a kebab-case name.");
            }

            string? id = StringOrNull(root, "id");
            if (!Ulid.IsValid(id))
            {
                findings.Error("record.id", location, "The record id must be an upper-case 26-character ULID.");
            }

            string? at = StringOrNull(root, "at");
            if (!IsTimestamp(at))
            {
                findings.Error("record.at", location, "The authoring time must look like 2026-10-13T14:00:00Z (UTC, whole seconds).");
            }

            string? by = StringOrNull(root, "by");
            if (!Ulid.IsValid(by))
            {
                findings.Error("record.by", location, "The authoring principal must be a ULID.");
            }

            RecordReference? subject = null;
            if (root.TryGetProperty("subject", out JsonElement subjectElement))
            {
                subject = ParseReference(subjectElement, location + "#subject", findings);
            }

            string? supersedes = null;
            if (root.TryGetProperty("supersedes", out JsonElement supersedesElement))
            {
                supersedes = supersedesElement.ValueKind == JsonValueKind.String ? supersedesElement.GetString() : null;
                if (!Ulid.IsValid(supersedes))
                {
                    findings.Error("record.supersedes", location, "'supersedes' must be the ULID of the record this one replaces.");
                }
                else if (supersedes == id)
                {
                    findings.Error("record.supersedes", location, "A record cannot supersede itself.");
                }
            }

            if (root.GetProperty("body").ValueKind != JsonValueKind.Object)
            {
                findings.Error("record.body", location, "'body' must be an object.");
            }

            string? declaredHash = StringOrNull(root, "hash");
            if (!ContentHash.IsValid(declaredHash))
            {
                findings.Error("record.hash", location, "'hash' must look like sha256:<64 lower-case hex digits>.");
            }

            var parsedSignatures = new List<RecordSignature>();
            if (root.TryGetProperty("signatures", out JsonElement signatures))
            {
                if (signatures.ValueKind != JsonValueKind.Array)
                {
                    findings.Error("record.signatures", location, "'signatures' must be an array.");
                }
                else
                {
                    int index = 0;
                    foreach (JsonElement item in signatures.EnumerateArray())
                    {
                        RecordSignature? parsedSignature = ParseSignature(item, $"{location}#signatures[{index++}]", findings);
                        if (parsedSignature is not null)
                        {
                            parsedSignatures.Add(parsedSignature);
                        }
                    }
                }
            }

            if (findings.Errors > before)
            {
                return null;
            }

            string computedHash;
            byte[] pretty;
            try
            {
                computedHash = ContentHash.Sha256(CanonicalJson.SerializeUtf8(root, HashExcludedKeys));
                pretty = CanonicalJson.SerializePrettyUtf8(root);
            }
            catch (CanonicalJsonException exception)
            {
                findings.Error("record.canonical", location, exception.Message);
                return null;
            }

            if (!string.Equals(computedHash, declaredHash, StringComparison.Ordinal))
            {
                findings.Error("record.hash-mismatch", location, $"The declared hash does not match the content ({computedHash}). The record was edited after it was hashed.");
            }

            if (checkFormatting && !pretty.AsSpan().SequenceEqual(bytes.Span))
            {
                findings.Error("record.formatting", location, "The file is not in canonical pretty form (sorted keys, two-space indent, LF, trailing newline). Run 'vericairn records format'.");
            }

            return new RecordEnvelope(type!, id!, at!, by!, subject, supersedes, root.GetProperty("body").Clone(), declaredHash!, computedHash, parsedSignatures, formatNumber ?? RecordFormats.First);
        }
    }

    /// <summary>Parses a {type,id,hash?} reference object.</summary>
    public static RecordReference? ParseReference(JsonElement element, string location, FindingList findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (element.ValueKind != JsonValueKind.Object)
        {
            findings.Error("record.reference", location, "A reference is an object with 'type', 'id' and optionally 'hash'.");
            return null;
        }

        string? type = null, id = null, hash = null;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "type":
                    type = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    break;
                case "id":
                    id = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    break;
                case "hash":
                    hash = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    break;
                default:
                    findings.Error("record.reference", location, $"Unknown reference key '{property.Name}'.");
                    break;
            }
        }

        bool ok = true;
        if (type is null || !TypePattern.IsMatch(type))
        {
            findings.Error("record.reference", location, "Reference 'type' must be a kebab-case record type.");
            ok = false;
        }

        if (!Ulid.IsValid(id))
        {
            findings.Error("record.reference", location, "Reference 'id' must be a ULID.");
            ok = false;
        }

        if (element.TryGetProperty("hash", out _) && !ContentHash.IsValid(hash))
        {
            findings.Error("record.reference", location, "Reference 'hash' must look like sha256:<64 hex digits>.");
            ok = false;
        }

        return ok ? new RecordReference(type!, id!, hash) : null;
    }

    /// <summary>Parses one {algorithm, device, signature} entry.</summary>
    public static RecordSignature? ParseSignature(JsonElement element, string location, FindingList findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (element.ValueKind != JsonValueKind.Object)
        {
            findings.Error("record.signatures", location, "A signature is an object with 'algorithm', 'device' and 'signature'.");
            return null;
        }

        string? algorithm = null, device = null, signature = null;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "algorithm":
                    algorithm = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    break;
                case "device":
                    device = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    break;
                case "signature":
                    signature = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    break;
                default:
                    findings.Error("record.signatures", location, $"Unknown signature key '{property.Name}'.");
                    break;
            }
        }

        bool ok = true;
        if (string.IsNullOrEmpty(algorithm) || algorithm.Length > 100)
        {
            findings.Error("record.signatures", location, "Signature 'algorithm' must be a short name.");
            ok = false;
        }

        if (!Ulid.IsValid(device))
        {
            findings.Error("record.signatures", location, "Signature 'device' must be the ULID of a device record.");
            ok = false;
        }

        if (!RecordSigning.IsSignature(signature))
        {
            findings.Error("record.signatures", location, "Signature 'signature' must be a base64url 64-byte value.");
            ok = false;
        }

        return ok ? new RecordSignature(algorithm!, device!, signature!) : null;
    }

    private static string? StringOrNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
    private static partial Regex TypeRegex();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$")]
    private static partial Regex TimestampRegex();
}
