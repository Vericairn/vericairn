// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>Derives generated verification and explicit reviewed adoption from existing declarations and evidence records.</summary>
public static class GeneratedDocumentContract
{
    /// <summary>The existing result output that binds a reviewed migration to its exact declaration.</summary>
    public const string MigrationOutput = "living-document-migration";

    /// <summary>The complete before/after generation preview reviewed alongside the declaration basis.</summary>
    public const string MigrationPreviewOutput = "living-document-migration-preview";

    /// <summary>New format-3 declarations are strict; an older lineage changes mode only through reviewed adoption.</summary>
    public static bool IsStrict(EstateModel model, RecordEnvelope declaration)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(declaration);
        bool strict = false;
        foreach (string id in model.Chain(declaration))
        {
            RecordEnvelope item = model.ById[id];
            if (EstateModel.BodyString(item, "kind") != "generated") { strict = false; continue; }
            if (item.Format < 3) continue;
            strict |= item.Supersedes is null || HasReviewedMigration(model, item);
        }
        return strict;
    }

    /// <summary>Whether a path lies in the declaration's ownership. Membership must still be proved separately.</summary>
    public static bool Owns(RecordEnvelope declaration, string path)
    {
        string? pattern = EstateModel.BodyString(declaration, "pathPattern");
        return pattern is not null && DocumentPatterns.IsValid(pattern)
            ? path.StartsWith(DocumentPatterns.Prefix(pattern), StringComparison.OrdinalIgnoreCase)
            : string.Equals(EstateModel.BodyString(declaration, "path"), path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Canonical migration proposal bytes, retained as an ordinary result output, not a new record or status ledger.</summary>
    public static byte[] MigrationBasis(RecordEnvelope? previous, JsonElement nextBody)
    {
        return CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(new
        {
            format = "vericairn.document-migration/1", previous = previous?.Reference,
            declaration = nextBody,
        }));
    }

    /// <summary>Finds an authentic passing operator review of a result carrying the exact migration basis.</summary>
    public static bool HasReviewedMigration(EstateModel model, RecordEnvelope declaration)
        => ReviewedResults(model, declaration).Any();

    /// <summary>The authenticated migration results whose declaration basis and preview were independently reviewed.</summary>
    public static IEnumerable<RecordEnvelope> ReviewedResults(EstateModel model, RecordEnvelope declaration)
    {
        RecordEnvelope? previous = declaration.Supersedes is null ? null : model.ById.GetValueOrDefault(declaration.Supersedes);
        string hash = ContentHash.Sha256(MigrationBasis(previous, declaration.Body));
        foreach (RecordEnvelope result in model.OfType("result").Where(r => string.CompareOrdinal(r.At, declaration.At) <= 0
            && r.Body.TryGetProperty("outputs", out JsonElement outputs) && outputs.ValueKind == JsonValueKind.Array
            && outputs.EnumerateArray().Any(o => Text(o, "name") == MigrationOutput && Text(o, "hash") == hash)
            && outputs.EnumerateArray().Any(o => Text(o, "name") == MigrationPreviewOutput && ContentHash.IsValid(Text(o, "hash")))))
        {
            if (!SignatureChecks.IsAuthentic(model, result)) continue;
            if (model.OfType("review").Any(r => r.Subject == result.Reference && r.By != result.By
                && string.CompareOrdinal(r.At, declaration.At) <= 0 && EstateModel.BodyString(r, "outcome") == "pass"
                && EstateModel.BodyString(r, "independence") == "operator" && model.HasRole(r.By, Roles.Operator)
                && SignatureChecks.IsAuthentic(model, r))) yield return result;
        }
    }

    /// <summary>A preexisting pin survives only when the exact migration evidence retained it; timestamps cannot mint new exemptions.</summary>
    public static bool IsHistoricalPin(EstateModel model, RecordEnvelope declaration, RecordEnvelope pin) =>
        model.Chain(declaration).Select(id => model.ById[id]).SelectMany(d => ReviewedResults(model, d))
            .Any(r => r.Body.TryGetProperty("evidence", out JsonElement evidence) && evidence.ValueKind == JsonValueKind.Array
                && evidence.EnumerateArray().Any(e => Text(e, "hash") == pin.ComputedHash && Text(e, "location") == pin.ExpectedPath));

    /// <summary>Validates adoption without changing old records or treating a format upgrade as conversion.</summary>
    public static void Validate(ValidationContext context, RecordEnvelope? previous)
    {
        RecordEnvelope declaration = context.Record;
        if (declaration.Format < 3)
        {
            if (previous is not null && IsStrict(context.Model, previous))
                context.Error("document.format", "A strict generated declaration cannot return to a legacy record format.");
            return;
        }
        bool generated = EstateModel.BodyString(declaration, "kind") == "generated";
        bool priorStrict = previous is not null && IsStrict(context.Model, previous);
        bool versioned = DocumentChecks.Generator(declaration) is (_, IReadOnlyList<string> inputs) && inputs.Any(i => i.StartsWith("format=", StringComparison.Ordinal));
        bool changed = previous is not null && ContractBody(previous) != ContractBody(declaration);
        bool existingPins = previous is null && context.Model.OfType("document-revision").Any(r => string.CompareOrdinal(r.At, declaration.At) <= 0
            && EstateModel.BodyString(r, "path") is { } path && Owns(declaration, path));
        bool migration = (priorStrict && changed) || (generated && ((previous is not null && !priorStrict && (versioned || changed || declaration.Body.TryGetProperty("pathPattern", out _))) || existingPins));
        if (migration && !HasReviewedMigration(context.Model, declaration))
            context.Error("document.migration", "Changing generated ownership or converting a legacy declaration requires an explicit migration result and an authentic independent operator review of its exact basis.");

        if (generated && IsStrict(context.Model, declaration))
        {
            if (!SignatureChecks.IsAuthentic(context.Model, declaration))
                context.Error("document.signature", "A strict generated declaration requires an admitted-device signature.");
            if (declaration.Body.TryGetProperty("clearedBy", out _) || declaration.Body.TryGetProperty("regions", out _))
                context.Error("document.clearance", "A wholly generated declaration has neither authored regions nor authored clearance.");
        }
        if (migration && !context.HasRole(declaration.By, Roles.Operator) && !context.HasScope(declaration.By, Scopes.LocalEngineering))
            context.Error("document.migration-author", "Generated ownership is migrated by the operator or under local-engineering authority.");
    }

    private static string ContractBody(RecordEnvelope declaration)
    {
        var body = new JsonObject();
        foreach (string field in new[] { "path", "pathPattern", "repository", "kind", "generator", "source", "regions" })
            if (declaration.Body.TryGetProperty(field, out JsonElement value)) body[field] = JsonNode.Parse(value.GetRawText());
        return CanonicalJson.Serialize(JsonSerializer.SerializeToElement(body));
    }

    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out JsonElement field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
}
