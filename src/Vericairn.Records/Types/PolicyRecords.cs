// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>Strict identity, evidence and selection shapes shared by format-3 policy records.</summary>
internal static class PolicyRecords
{
    internal static readonly string[] Levels = ["remind", "warn", "refuse"];
    internal static readonly string[] ProseFields = ["title", "summary", "synthesis", "note", "intent", "exitQuestion", "rationale", "nextStep"];

    internal static string? Key(BodyReader body, string name = "key", bool required = true) => body.Text(name, required, 100, Patterns.Slug);

    internal static JsonElement Property(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement found) ? found : default;

    internal static string? Text(JsonElement value, string name) => Property(value, name) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    internal static IEnumerable<JsonElement> Items(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    internal static bool Equal(JsonElement first, JsonElement second) => first.ValueKind == JsonValueKind.Undefined || second.ValueKind == JsonValueKind.Undefined
        ? first.ValueKind == second.ValueKind : CanonicalJson.Serialize(first) == CanonicalJson.Serialize(second);

    internal static bool SameFields(RecordEnvelope first, RecordEnvelope second, params string[] names) => names.All(n => Equal(Property(first.Body, n), Property(second.Body, n)));

    internal static bool Contains(JsonElement array, string? value) => value is not null && Items(array).Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == value);

    internal static string Identity(EstateModel model, RecordEnvelope record) => model.Chain(record)[0];

    internal static bool ReleaseMatchesProduct(EstateModel model, RecordEnvelope release, string? product) =>
        product is not null && release.Subject is { Type: "product" } subject &&
        LivingDocuments.ProductIdentity(model, subject.Id) == LivingDocuments.ProductIdentity(model, product);

    internal static RecordEnvelope? Reference(ValidationContext context, BodyReader? reader, bool exact = true, params string[] types)
    {
        if (reader is null) return null;
        RecordReference? reference = RecordParser.ParseReference(reader.Element, reader.Location, context.Findings);
        if (reference is null) return null;
        if (exact && reference.Hash is null) reader.Error("policy.reference", "hash", "Evidence pins the type, id and exact hash.");
        if (!exact && reference.Hash is not null) reader.Error("policy.identity", "hash", "A selection names a continuing identity, without an evidence hash.");
        if (types.Length > 0 && !types.Contains(reference.Type, StringComparer.Ordinal))
        {
            reader.Error("policy.reference", "type", $"Expected {string.Join(" or ", types)}.");
            return null;
        }
        RecordEnvelope? target = context.RequireReference(reader.Location, reference.Id, reference.Type);
        if (target is null) return null;
        if (reference.Hash is not null && reference.Hash != target.ComputedHash) reader.Error("policy.reference", "hash", "The pinned hash does not match the record.");
        if (!exact && Identity(context.Model, target) != target.Id) reader.Error("policy.identity", "id", "A selection names the first record of the identity.");
        if (target.Id == context.Record.Id || string.CompareOrdinal(target.At, context.Record.At) > 0) reader.Error("policy.reference-time", "id", "A reference cannot cite itself or a record authored in the future.");
        return target;
    }

    internal static RecordEnvelope? IdentityField(ValidationContext context, BodyReader reader, string field, params string[] types)
    {
        string? id = reader.Ulid(field, true);
        RecordEnvelope? record = context.Find(id);
        if (id is not null && (record is null || !types.Contains(record.Type, StringComparer.Ordinal) || Identity(context.Model, record) != id))
            reader.Error("policy.identity", field, $"Expected a {string.Join(" or ", types)} identity.");
        return record;
    }

    internal static void Unique(BodyReader reader, IEnumerable<string?> values, string field)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in values.OfType<string>())
            if (!seen.Add(value)) reader.Error("policy.duplicate", field, $"Duplicate '{value}'.");
    }

    internal static IReadOnlyList<RecordEnvelope> References(ValidationContext context, BodyReader body, string field, bool required = true, bool allowEmpty = true, params string[] types)
    {
        IReadOnlyList<BodyReader> readers = body.NestedArray(field, required, allowEmpty) ?? [];
        Unique(body, readers.Select(r => Text(r.Element, "id")), field);
        return readers.Select(r => Reference(context, r, true, types)).OfType<RecordEnvelope>().ToList();
    }

    internal static void Selection(ValidationContext context, BodyReader? selection)
    {
        if (selection is null) return;
        switch (selection.Enum("kind", true, "estate", "product", "work-orders", "repository"))
        {
            case "product": IdentityField(context, selection, "product", "product"); break;
            case "work-orders":
                IReadOnlyList<string> orders = selection.StringArray("workOrders", true, false, Records.Identity.Ulid.Length) ?? [];
                Unique(selection, orders, "workOrders");
                foreach (string id in orders)
                {
                    RecordEnvelope? order = context.RequireReference("appliesTo.workOrders", id, "work-order");
                    if (order is not null && Identity(context.Model, order) != id) selection.Error("policy.identity", "workOrders", "Work selections name identities.");
                }
                break;
            case "repository": Repository(context, selection); Selectors(selection); break;
        }
        selection.NoOtherKeys();
    }

    internal static void Repository(ValidationContext context, BodyReader reader)
    {
        RecordEnvelope? repository = IdentityField(context, reader, "repository", "estate", "resource");
        if (repository is { Type: "resource" } && EstateModel.BodyString(repository, "kind") != "repository") reader.Error("policy.repository", "repository", "The resource must be a repository.");
    }

    internal static void Selectors(BodyReader reader)
    {
        IReadOnlyList<string> paths = reader.StringArray("selectors", true, false, 500) ?? [];
        Unique(reader, paths, "selectors");
        foreach (string path in paths)
        {
            string[] parts = path.Split('/');
            if (path.Contains('\\', StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal) || parts.Any(p => p is "" or "." or ".." || (p != "**" && p.Contains("**", StringComparison.Ordinal))) ||
                path.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '.' or '_' or '-' or '*' or ' ')))
                reader.Error("policy.selector", "selectors", "Selectors are relative paths with literal components, * within a component or ** across components.");
        }
    }

    internal static void WritingCoverage(ValidationContext context, BodyReader? scope)
    {
        if (scope is null) return;
        IReadOnlyList<BodyReader> paths = scope.NestedArray("paths", false, false) ?? [];
        IReadOnlyList<BodyReader> records = scope.NestedArray("records", false, false) ?? [];
        if (paths.Count + records.Count == 0) scope.Error("writing.scope", "paths", "A scope contains a nonempty paths or records list.");
        foreach (BodyReader path in paths) { Repository(context, path); Selectors(path); path.NoOtherKeys(); }
        foreach (BodyReader record in records)
        {
            string? id = record.Ulid("record", true);
            if (id is not null && context.Find(id) is null) record.Error("body.reference", "record", "The selected exact record must exist.");
            record.Enum("field", false, ProseFields);
            record.NoOtherKeys();
        }
        Unique(scope, paths.Select(p => CanonicalJson.Serialize(p.Element)), "paths");
        Unique(scope, records.Select(p => CanonicalJson.Serialize(p.Element)), "records");
        scope.NoOtherKeys();
    }

    // Conservative containment is intentional: an unproved wildcard inclusion never enlarges a grant.
    internal static bool CoverageContains(JsonElement outer, JsonElement inner)
    {
        if (outer.ValueKind == JsonValueKind.Undefined) return true;
        if (inner.ValueKind == JsonValueKind.Undefined) return false;
        bool records = Items(Property(inner, "records")).All(r => Items(Property(outer, "records")).Any(o => Text(r, "record") == Text(o, "record") && (Text(o, "field") is null || Text(o, "field") == Text(r, "field"))));
        bool paths = Items(Property(inner, "paths")).All(r => Items(Property(outer, "paths")).Any(o => Text(o, "repository") == Text(r, "repository") &&
            Items(Property(r, "selectors")).All(p => p.ValueKind == JsonValueKind.String && Items(Property(o, "selectors")).Any(q => q.ValueKind == JsonValueKind.String && SelectorContains(q.GetString()!, p.GetString()!)))));
        return records && paths;
    }

    private static bool SelectorContains(string outer, string inner) => outer == "**" || outer == inner ||
        (outer.EndsWith("/**", StringComparison.Ordinal) && !outer[..^3].Contains('*', StringComparison.Ordinal) && inner.StartsWith(outer[..^2], StringComparison.Ordinal));
}
