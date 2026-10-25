// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Templates;

/// <summary>Effective instance choices and explicit deviations. Resolving them never changes their pinned template.</summary>
public static class TemplateInstances
{
    /// <summary>Template, estate, domain, product, project, then explicit choices, with narrower defaults taking precedence.</summary>
    public static IReadOnlyDictionary<string, string> Options(EstateModel model, TemplateDefinition definition, JsonElement scope, JsonElement choices)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(definition);
        Dictionary<string, JsonElement> allowed = TemplateDefinition.OptionDefinitions(definition.Content);
        var result = allowed.ToDictionary(p => p.Key, p => TJson.Text(p.Value, "default")!, StringComparer.Ordinal);
        foreach (string kind in new[] { "estate", "domain", "product", "project" })
        {
            string? target = kind == "estate" ? null : TJson.Text(scope, kind);
            if (kind != "estate" && target is null) continue;
            RecordEnvelope[] defaults = model.Current("template-defaults").Where(r =>
                TJson.Text(TJson.Object(r.Body, "template"), "id") == definition.Id && TJson.Text(TJson.Object(r.Body, "template"), "version") == definition.Version
                && TJson.Text(TJson.Object(r.Body, "template"), "hash") == definition.Hash
                && TJson.Text(TJson.Object(r.Body, "scope"), "kind") == kind && TJson.Text(TJson.Object(r.Body, "scope"), "target") == target).ToArray();
            if (defaults.Length > 1) throw new InvalidOperationException("Conflicting template defaults at one scope must be resolved explicitly.");
            if (defaults.Length == 1) Apply(TJson.Object(defaults[0].Body, "options"));
        }
        Apply(choices);
        return result;

        void Apply(JsonElement values)
        {
            if (values.ValueKind == JsonValueKind.Undefined) return;
            if (values.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Template options are an object of named string choices.");
            foreach (JsonProperty property in values.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String || !allowed.TryGetValue(property.Name, out JsonElement option)
                    || !TJson.Strings(option, "values").Contains(property.Value.GetString(), StringComparer.Ordinal))
                    throw new InvalidOperationException("An instance choice is not an allowed value of its template: " + property.Name);
                result[property.Name] = property.Value.GetString()!;
            }
        }
    }

    /// <summary>Recorded choices of an instance; defaults are materialized at creation, never silently reapplied.</summary>
    public static IReadOnlyDictionary<string, string> RecordedOptions(RecordEnvelope declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        JsonElement options = TJson.Object(TJson.Object(declaration.Body, "template"), "options");
        return options.ValueKind == JsonValueKind.Object
            ? options.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Resolves explicit structural overrides while leaving retained template bytes unchanged.</summary>
    public static TemplateDefinition Effective(TemplateDefinition definition, RecordEnvelope declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return Effective(definition, TJson.Object(declaration.Body, "template"));
    }

    /// <summary>Resolves a proposed instance's structural overrides before its declaration and bytes are prepared.</summary>
    public static TemplateDefinition Effective(TemplateDefinition definition, JsonElement instance)
    {
        ArgumentNullException.ThrowIfNull(definition);
        JsonNode root = JsonNode.Parse(definition.Content.GetRawText())!;
        foreach (JsonElement change in TJson.Array(instance, "overrides"))
        {
            string? id = TJson.Text(change, "section");
            string? action = TJson.Text(change, "action");
            (JsonArray? Parent, JsonObject? Element) Find(JsonNode node)
            {
                foreach (string key in new[] { "sections", "fields" })
                {
                    if (node[key] is not JsonArray children) continue;
                    foreach (JsonNode? child in children)
                    {
                        if (child is not JsonObject element) continue;
                        if (element["id"]?.GetValue<string>() == id) return (children, element);
                        var nested = Find(element);
                        if (nested.Element is not null) return nested;
                    }
                }
                return (null, null);
            }
            var found = Find(root);
            if (action == "add")
            {
                if (found.Element is not null) throw new InvalidOperationException("An added section must have a new identifier.");
                if (JsonNode.Parse(TJson.Object(change, "definition").GetRawText()) is not JsonObject added)
                    throw new InvalidOperationException("An added section needs a complete definition object.");
                if (added["id"]?.GetValue<string>() != id) throw new InvalidOperationException("The added section must match the override's identifier.");
                root["sections"]!.AsArray().Add(added);
            }
            else if (found.Element is null) throw new InvalidOperationException("An override names no section or field in the pinned template.");
            else if (action == "omit") found.Parent!.Remove(found.Element);
            else if (action == "rename")
            {
                found.Element.Remove("marker");
                found.Element["heading"] = TJson.Text(change, "heading");
            }
            else throw new InvalidOperationException("An override needs a recognized structural action.");
        }
        return TemplateDefinition.Read(JsonSerializer.SerializeToElement(root));
    }

    /// <summary>Whether the section is required under the recorded choices.</summary>
    public static bool Required(JsonElement element, IReadOnlyDictionary<string, string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return TJson.Text(element, "presence") == "required" || Matches(TJson.Object(element, "presence"), options);
    }

    /// <summary>The required fidelity, including every matching option condition; the strongest matching level wins.</summary>
    public static string Fidelity(JsonElement element, IReadOnlyDictionary<string, string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        JsonElement fidelity = TJson.Object(element, "fidelity");
        if (fidelity.ValueKind == JsonValueKind.String) return fidelity.GetString()!;
        string fallback = TJson.Text(fidelity, "default") ?? "final";
        return TJson.Array(fidelity, "when").Where(c => Matches(c, options)).Select(c => TJson.Text(c, "level")!)
            .OrderBy(TJson.Fidelity).LastOrDefault() ?? fallback;
    }

    internal static bool Matches(JsonElement condition, IReadOnlyDictionary<string, string> options) =>
        TJson.Text(condition, "option") is { } option && options.TryGetValue(option, out string? value) && value == TJson.Text(condition, "value");

    /// <summary>Recorded role standing only. A template's role label never assigns a principal to it.</summary>
    public static bool HasRole(EstateModel model, string principal, string role)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.HasRole(principal, role)) return true;
        return model.Current("roster").SelectMany(r => TJson.Array(r.Body, "roles"))
            .Any(r => TJson.Text(r, "role") == role && TJson.Strings(r, "principals").Contains(principal, StringComparer.Ordinal));
    }

    /// <summary>A read-only difference for a proposed version. Applying or declining it requires an owner-authored declaration revision.</summary>
    public static IReadOnlyList<string> Changes(TemplateDefinition previous, TemplateDefinition proposed)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(proposed);
        if (previous.Id != proposed.Id) throw new InvalidOperationException("An upgrade keeps the template identity.");
        var before = previous.Elements.ToDictionary(e => TJson.Text(e, "id")!, StringComparer.Ordinal);
        var after = proposed.Elements.ToDictionary(e => TJson.Text(e, "id")!, StringComparer.Ordinal);
        var changes = new List<string>();
        foreach (string id in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            if (!before.TryGetValue(id, out JsonElement oldElement)) changes.Add("Added section or field: " + id);
            else if (!after.TryGetValue(id, out JsonElement newElement)) changes.Add("Removed section or field: " + id);
            else if (Canonical.CanonicalJson.Serialize(oldElement) != Canonical.CanonicalJson.Serialize(newElement))
                changes.Add("Changed section, checks or cohesion: " + id);
        }
        if (Canonical.CanonicalJson.Serialize(previous.Content) != Canonical.CanonicalJson.Serialize(proposed.Content))
            changes.Add("Review the complete proposed contract and its options before applying it.");
        return changes;
    }
}
