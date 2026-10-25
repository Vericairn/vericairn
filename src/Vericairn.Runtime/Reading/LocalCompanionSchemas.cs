// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Reading;

/// <summary>Only these metadata fields cross the local inspector boundary. Unknown and secret-bearing fields are never copied.</summary>
internal static class LocalCompanionSchemas
{
    internal sealed record Field(string Name, string Kind, ImmutableArray<Field> Children = default);

    internal static readonly ImmutableDictionary<string, ImmutableArray<Field>> All = Build();

    internal static readonly ImmutableDictionary<string, string> NoReader = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["private-keys"] = "Private device and certificate material supplies signing authority, not reader content; only custody, presence and fingerprints are inspected.",
        ["recovery-secrets"] = "Offline recovery secrets and passphrases are never reader content; the custody inspector shows only metadata and availability.",
        ["pairing-codes"] = "Pairing codes and challenge authentication payloads authorize a bounded exchange; inspectors expose only the resulting admission metadata.",
        ["bearer-values"] = "Bearer values and credentials are never copied into a rendition; connection metadata describes identity and trust without credential values.",
        ["replay-cache"] = "Anti-replay entries and request authentication payloads have no reader rendition; the write ledger reports outcomes under their original identities.",
        ["environment-values"] = "A copied environment can carry credentials and machine bindings; inspectors report declared execution metadata without environment values.",
        ["raw-logs"] = "Raw process logs can contain credentials, invocation inputs and machine bindings; only structured observations have a metadata reader.",
        ["context-bodies"] = "Retained originals and rendered briefs require the owning context access checks; this inspector shows exact identities and accounting without reproducing bodies.",
        ["tool-configuration"] = "Launcher commands and MCP configuration can contain bindings and authentication material; inspectors show declared tool identities and capabilities only.",
        ["machine-paths"] = "Machine bindings remain in local custody; all inspector renditions omit their paths, including renditions intended for sharing.",
        ["storage-internals"] = "Database pages, index blocks, lock bytes and staging payloads have no prose reader; recovery metadata preserves ownership and incomplete-batch conditions.",
    }.ToImmutableDictionary(StringComparer.Ordinal);

    private static ImmutableDictionary<string, ImmutableArray<Field>> Build()
    {
        var schemas = new Dictionary<string, ImmutableArray<Field>>(StringComparer.Ordinal);
        schemas["clones"] = [F("resource", "id"), F("repository", "repository"), F("identity", "verified|unverified|mismatch|unknown"), F("availability", "available|missing|unreadable|unknown")];
        schemas["devices"] = [F("id", "id"), F("principal", "id"), F("fingerprint", "hash"), F("platform", string.Join('|', DeviceType.Platforms)), F("custody", string.Join('|', DeviceKeyStores.Format2Kinds)), F("admission", "admitted|revoked|pending|unknown"), F("heldHere", "bool"), F("presence", string.Join('|', DeviceType.Presences)), F("purpose", string.Join('|', DeviceType.Purposes)), A("profiles", string.Join('|', DeviceType.Profiles))];
        schemas["worker-settings"] = [F("launcher", "selector"), F("origin", "operator-configuration|launcher-default|recorded-policy|unavailable"), F("compatible", "bool"), F("workspaceAvailable", "bool"), F("model", "selector"), F("effort", "selector"), A("allowedModels", "selector"), A("allowedEfforts", "selector")];
        schemas["launchers"] = [F("launcher", "selector"), F("contract", "vericairn.launcher.agent/1"), F("generic", "bool"), F("available", "bool"), A("scopes", "launcher|roster|goal|assignment|run"), A("efforts", "selector"), A("providers", "selector"), A("destinations", "selector"), A("dataBoundaries", "selector"), F("providerDefault", "bool"), F("closedEnvironment", "bool")];
        schemas["runtime"] = [F("estate", "id"), F("origin", "operator-configuration|format-default|unavailable"), F("binding", "disabled|loopback|private|public|wildcard|unknown"), F("allowPublic", "bool"), F("service", "running|stopped|unavailable|unknown"), F("identityFingerprint", "hash")];
        schemas["certificates"] = [F("fingerprint", "hash"), F("notBefore", "time"), F("notAfter", "time"), F("validity", "not-yet-valid|within-dates|expired|unknown"), F("algorithm", "ecdsa|rsa|other|unknown"), F("privateMaterialPresent", "bool")];
        schemas["connections"] = [F("id", "identity"), F("estate", "id"), F("device", "id"), F("principal", "id"), F("identityFingerprint", "hash"), F("pairedAt", "time"), A("profiles", "embedded|local-socket|loopback|local-network|relay"), F("state", "connecting|current|cached|partial|denied|unavailable"), O("observed", F("state", "connecting|current|cached|partial|denied|unavailable"), F("at", "time")), O("lastCurrent", F("at", "time"))];
        schemas["write-ledger"] = [F("id", "identity"), F("at", "time"), F("device", "id"), F("principal", "id"), F("operation", "operation"), F("state", "pending|written|failed|interrupted|unknown"), F("record", "id"), F("hash", "hash"), A("candidates", "id")];
        schemas["run-state"] = [F("run", "id"), F("heartbeatSeconds", "duration"), F("launchedAt", "time"), F("heartbeat", "time"), F("heartbeats", "number"), F("output", "time"), F("tokens", "number"), F("exitCode", "signed"), F("exitedAt", "time"), F("settled", "observed-success|known-failure|uncertain|canceled"),
            F("settlementPrepared", "bool"), F("minuteTicks", "number"),
            // Recovery retains full copies; the inspector selects accounting metadata, omitting raw text and paths.
            O("outcome", F("exitCode", "signed"), F("heartbeats", "number"), F("tokens", "number"), F("price", "number"), F("priceUnit", "selector")),
            O("scope", F("changed", "number"), F("outsideCount", "number")),
            O("liveness", F("stallSeconds", "number"), F("lastEventAt", "time"), F("attentionAt", "time"), F("attention", "id"), F("stoppedAt", "time")),
            O("stop", F("requestedAt", "time"), F("requestedBy", "actor"), F("transmittedAt", "time"), F("acknowledgedAt", "time"), F("escalatedAt", "time"), F("containedAt", "time")),
            L("signals", F("severity", "warning|critical"), F("at", "time"), F("count", "number"), F("attention", "id")),
            O("execution", L("reports", F("model", "selector"), F("modelRevision", "selector"), F("effort", "selector"), F("provider", "selector"), F("harnessVersion", "selector"), F("inventoryHash", "hash")), A("unknown", "model|modelRevision|effort|provider|destination|harnessVersion|tools|inventoryHash|complete-report"), A("discrepancies", "model|modelRevision|effort|provider|destination|harnessVersion|tools|inventoryHash"))];
        schemas["retained-context"] = [F("format", "vericairn.context/1"), O("manifest", F("hash", "hash")), O("accounting", F("bytes", "number"), F("maxBytes", "number"), F("remainingBytes", "number"), F("overageBytes", "number"), F("tokens", "number"), F("units", "bytes"), F("capSource", "limit not set|explicit context allocation")),
            L("originals", F("kind", "record|document|object|file"), F("id", "id"), F("type", "record-type"), F("hash", "hash"), F("byteHash", "hash"), F("bytes", "number"), F("revision", "id"), F("revisionHash", "hash"), F("mandatory", "bool")),
            L("withheld", F("kind", "record|document|object|file"), F("id", "id"), F("type", "record-type"), F("hash", "hash"), F("byteHash", "hash"), F("bytes", "number"), F("originalBytes", "number"), F("includedBytes", "number"), F("withheldBytes", "number"), F("reason", "outside-next-step|budget|excerpt|unavailable"))];
        schemas["recovery"] = [F("owner", "actor"), F("state", "available|unavailable|incomplete|recovering|settled|unknown"), F("authoritative", "bool"), F("incompleteBatches", "number"), F("pendingOutputs", "number"), A("records", "id")];
        return schemas.ToImmutableDictionary(StringComparer.Ordinal);
    }

    internal static JsonObject Project(JsonElement source, ImmutableArray<Field> fields)
    {
        var result = new JsonObject();
        foreach (Field field in fields)
        {
            JsonElement value = source.ValueKind == JsonValueKind.Object && source.TryGetProperty(field.Name, out JsonElement found) ? found : default;
            if (field.Kind == "object") result[field.Name] = value.ValueKind == JsonValueKind.Object ? Project(value, field.Children) : null;
            else if (field.Kind == "list") result[field.Name] = value.ValueKind == JsonValueKind.Array
                ? new JsonArray(value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Object ? (JsonNode)Project(v, field.Children) : null).ToArray()) : null;
            else if (field.Kind.EndsWith("[]", StringComparison.Ordinal)) result[field.Name] = value.ValueKind == JsonValueKind.Array
                ? new JsonArray(value.EnumerateArray().Select(v => Scalar(v, field.Kind[..^2])).ToArray()) : null;
            else result[field.Name] = Scalar(value, field.Kind);
        }
        return result;
    }

    private static JsonValue? Scalar(JsonElement value, string kind)
    {
        if (kind == "bool") return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? JsonValue.Create(value.GetBoolean()) : null;
        if (kind is "number" or "signed") return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) && number <= CanonicalJson.MaxSafeInteger
            && number >= (kind == "signed" ? int.MinValue : 0) ? JsonValue.Create(number) : null;
        if (kind == "duration")
        {
            string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
            return double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double duration) && double.IsFinite(duration) && duration is >= 0 and <= 86400
                ? JsonValue.Create(duration.ToString("G17", CultureInfo.InvariantCulture)) : null;
        }
        if (value.ValueKind != JsonValueKind.String) return null;
        string candidate = value.GetString()!;
        if (kind == "operation") return JsonValue.Create(Operation(candidate));
        bool safe = kind switch
        {
            "id" => Ulid.IsValid(candidate),
            "actor" => candidate == "worker" || Ulid.IsValid(candidate),
            "identity" => Ulid.IsValid(candidate) || Guid.TryParseExact(candidate, "N", out _) || Guid.TryParseExact(candidate, "D", out _),
            "hash" => ContentHash.IsValid(candidate),
            "time" => RecordParser.IsTimestamp(candidate),
            "selector" => candidate.Length is > 0 and <= 80 && char.IsAsciiLetter(candidate[0]) && candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') && !candidate.StartsWith("sk-", StringComparison.OrdinalIgnoreCase),
            "repository" => Patterns.GitHubRepository.IsMatch(candidate),
            "record-type" => RecordTypeRegistry.Find(candidate) is not null,
            _ => kind.Split('|').Contains(candidate, StringComparer.Ordinal),
        };
        return safe ? JsonValue.Create(candidate) : null;
    }

    // Keep the existing operation's route template, never an arbitrary request path or parameter.
    private static string? Operation(string candidate)
    {
        foreach ((string method, string route) in OpenApiDocument.Routes.Where(r => r.Method == "POST"))
        {
            if (candidate == method + " " + route) return candidate;
            if (!candidate.StartsWith(method + " ", StringComparison.Ordinal)) continue;
            string[] supplied = candidate[(method.Length + 1)..].Split('/'), expected = route.Split('/');
            if (supplied.Length == expected.Length && expected.Select((part, i) => part.StartsWith('{') && part.EndsWith('}')
                ? Ulid.IsValid(supplied[i]) : part == supplied[i]).All(v => v)) return method + " " + route;
        }
        return null;
    }

    internal static IEnumerable<string> Collections(ImmutableArray<Field> fields, string prefix = "")
    {
        foreach (Field field in fields)
        {
            string path = prefix + field.Name;
            bool list = field.Kind == "list" || field.Kind.EndsWith("[]", StringComparison.Ordinal);
            if (list) yield return path;
            if (!field.Children.IsDefaultOrEmpty)
                foreach (string nested in Collections(field.Children, path + (list ? "[]." : "."))) yield return nested;
        }
    }

    private static Field F(string name, string kind) => new(name, kind);
    private static Field A(string name, string kind) => F(name, kind + "[]");
    private static Field O(string name, params Field[] children) => new(name, "object", [.. children]);
    private static Field L(string name, params Field[] children) => new(name, "list", [.. children]);
}
