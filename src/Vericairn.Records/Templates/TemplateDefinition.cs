// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Templates;

/// <summary>Validated, declarative template content. Its canonical hash pins the entire contract; it grants no authority.</summary>
public sealed class TemplateDefinition
{
    private TemplateDefinition(JsonElement content)
    {
        Content = content.Clone();
        Id = TJson.Text(content, "id")!;
        Version = TJson.Text(content, "version")!;
        Hash = ContentHash.Sha256(CanonicalJson.SerializeUtf8(content));
    }

    /// <summary>The namespaced template identity.</summary>
    public string Id { get; }
    /// <summary>The immutable version label.</summary>
    public string Version { get; }
    /// <summary>The canonical content hash, independent of JSON formatting.</summary>
    public string Hash { get; }
    /// <summary>The complete contract, including sections and fields in source order.</summary>
    public JsonElement Content { get; }
    /// <summary>Every section and field in depth-first order.</summary>
    public IReadOnlyList<JsonElement> Elements => Flatten(Content).ToArray();

    /// <summary>Reads a contract, refusing unknown properties, ambiguous structure and unavailable primitives.</summary>
    public static TemplateDefinition Read(JsonElement content)
    {
        IReadOnlyList<Finding> findings = Validate(content);
        if (findings.Any(f => f.Severity == Severity.Error))
            throw new InvalidOperationException(string.Join(" ", findings.Select(f => f.Message)));
        try { return new TemplateDefinition(content); }
        catch (CanonicalJsonException error) { throw new InvalidOperationException("Template content must have an unambiguous canonical representation.", error); }
    }

    /// <summary>Checks the fixed vocabulary and every referenced primitive without installing anything.</summary>
    public static IReadOnlyList<Finding> Validate(JsonElement content)
    {
        var findings = new FindingList();
        var body = new BodyReader(content, "template", findings, 3);
        if (content.ValueKind != JsonValueKind.Object)
        {
            findings.Error("template.shape", "template", "A template is an object.");
            return findings.ToArray();
        }
        body.Enum("format", true, "vericairn.template/1");
        body.Text("id", true, 200, Patterns.Contribution, "namespace/name");
        body.Text("version", true, 100, Patterns.SemanticVersion, "a semantic version");
        body.Text("purpose", true, 4000);
        body.Text("artifact", true, 100);
        body.Text("domain", false, 100);
        ReadOptions(body);
        ReadChecks(body);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var headings = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader section in body.NestedArray("sections", true, allowEmpty: false) ?? [])
            ReadElement(section, ids, headings, false, 0);
        body.NoOtherKeys();
        if (!findings.HasErrors)
        {
            Dictionary<string, JsonElement> options = OptionDefinitions(content);
            foreach (JsonElement element in Flatten(content))
            {
                foreach (JsonElement source in TJson.Array(element, "sources"))
                    if (TJson.Text(source, "kind") == "section" && !ids.Contains(TJson.Text(source, "section") ?? ""))
                        findings.Error("template.source", "template", "A section source must name a declared section or field.");
                foreach (JsonElement condition in Conditions(element))
                {
                    string key = TJson.Text(condition, "option") ?? "";
                    if (!options.TryGetValue(key, out JsonElement option) || !TJson.Strings(option, "values").Contains(TJson.Text(condition, "value"), StringComparer.Ordinal))
                        findings.Error("template.option", "template", "A condition must name an option and one of its allowed values.");
                }
            }
        }
        return findings.ToArray();
    }

    internal static IEnumerable<JsonElement> Flatten(JsonElement container)
    {
        foreach (JsonElement element in TJson.Array(container, "sections").Concat(TJson.Array(container, "fields")))
        {
            yield return element;
            foreach (JsonElement child in Flatten(element)) yield return child;
        }
    }

    internal static Dictionary<string, JsonElement> OptionDefinitions(JsonElement content)
    {
        var options = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement owner in new[] { content }.Concat(Flatten(content)))
        {
            string prefix = owner.Equals(content) ? "" : TJson.Text(owner, "id") + ".";
            foreach (JsonElement option in TJson.Array(owner, "options"))
                options.Add(prefix + TJson.Text(option, "id"), option);
        }
        return options;
    }

    private static IEnumerable<JsonElement> Conditions(JsonElement element)
    {
        if (element.TryGetProperty("presence", out JsonElement presence) && presence.ValueKind == JsonValueKind.Object) yield return presence;
        foreach (JsonElement condition in TJson.Array(TJson.Object(element, "fidelity"), "when")) yield return condition;
    }

    private static void ReadElement(BodyReader body, HashSet<string> ids, HashSet<string> headings, bool field, int depth)
    {
        if (depth > 12) { body.Error("template.depth", "sections", "Templates nest at most twelve levels."); return; }
        string? id = body.Text("id", true, 100, Patterns.Slug, "a section slug");
        if (id is not null && !ids.Add(id)) body.Error("template.duplicate", "id", "Section and field identifiers are unique throughout a template.");
        string? heading = body.Text("heading", false, 300);
        bool marker = body.Flag("marker", false) == true;
        if ((heading is null) == !marker) body.Error("template.locator", "heading", "Choose exactly one heading or marker:true.");
        if (heading is not null && (heading.Contains('\n', StringComparison.Ordinal) || heading.Contains('\r', StringComparison.Ordinal) || !headings.Add(heading)))
            body.Error("template.heading", "heading", "Headings are distinct, single-line text.");
        if (field) body.Enum("type", true, "text", "list", "table", "reference", "date", "number");
        body.Text("purpose", true, 4000);
        if (TJson.Object(body.Element, "presence").ValueKind == JsonValueKind.Object)
        {
            BodyReader condition = body.Nested("presence", true)!;
            ReadCondition(condition); condition.NoOtherKeys();
        }
        else body.Enum("presence", true, "required", "optional");
        BodyReader? role = body.Nested("role", true);
        if (role is not null)
        {
            foreach (string action in new[] { "write", "review", "clear" })
            {
                string? name = role.Text(action, true, 100);
                if (name is not null && !TemplatePrimitives.Roles.Contains(name)) role.Error("template.primitive", action, "The role is not installed.");
            }
            role.NoOtherKeys();
        }
        BodyReader? synthesis = body.Nested("synthesis", true);
        string? mode = synthesis?.Enum("mode", true, "authored", "synthesized", "generated", "quoted");
        if (synthesis is not null)
        {
            synthesis.Flag("personOnly", false);
            if (mode == "generated")
            {
                string? generator = synthesis.Text("generator", true, 200, Patterns.Contribution, "namespace/name");
                if (generator is not null && DocumentGenerators.Find(generator) is null) synthesis.Error("template.primitive", "generator", "The generator is not installed.");
                synthesis.StringArray("inputs", true, true, 500);
            }
            if (mode == "quoted") synthesis.Text("source", true, 100, Patterns.Slug, "a source id");
            if (mode != "authored" && synthesis.Has("personOnly")) synthesis.Error("template.synthesis", "personOnly", "personOnly applies only to authored sections.");
            synthesis.NoOtherKeys();
        }
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader source in body.NestedArray("sources", true, true) ?? [])
        {
            string? sourceId = source.Text("id", true, 100, Patterns.Slug, "a source id");
            if (sourceId is not null && !sources.Add(sourceId)) source.Error("template.duplicate", "id", "Source identifiers are unique within a section.");
            string? kind = source.Enum("kind", true, "record", "records", "section", "file", "external");
            if (kind == "record") source.Ulid("record", true);
            if (kind == "records")
            {
                string? type = source.Text("type", true, 100);
                if (type is not null && RecordTypeRegistry.Find(type) is null) source.Error("template.primitive", "type", "The record type is not installed.");
                BodyReader? where = source.Nested("where", false);
                if (where is not null) { where.Text("field", true, 200); where.Text("equals", true, 4000); where.NoOtherKeys(); }
            }
            if (kind is "record" or "records") source.Text("field", false, 200);
            if (kind == "section") source.Text("section", true, 100, Patterns.Slug, "a section id");
            if (kind is "file" or "external") source.Text("path", true, 500, Patterns.RelativePath, "a repository-relative retained input");
            if (kind == "external") { source.Text("reference", true, 1000); source.Hash("hash", true); }
            source.NoOtherKeys();
        }
        if (mode == "quoted" && !sources.Contains(TJson.Text(TJson.Object(body.Element, "synthesis"), "source") ?? ""))
            body.Error("template.source", "synthesis", "A quote names one of this section's record sources.");
        if (mode == "quoted" && TJson.Array(body.Element, "sources").Any(s => TJson.Text(s, "id") == TJson.Text(TJson.Object(body.Element, "synthesis"), "source") && TJson.Text(s, "kind") != "record"))
            body.Error("template.source", "synthesis", "Quoted content comes from a record.");
        var rules = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader rule in body.NestedArray("cohesion", true, true) ?? [])
        {
            string? ruleId = rule.Text("id", true, 100, Patterns.Slug, "a rule id");
            if (ruleId is not null && !rules.Add(ruleId)) rule.Error("template.duplicate", "id", "Rule identifiers are unique within a section.");
            string? kind = rule.Enum("kind", true, "covers", "references-exist", "matches-record", "agrees-on-values", "derived-from", "judgment");
            rule.Text("summary", true, 2000);
            string? source = rule.Text("source", kind is "covers" or "matches-record" or "agrees-on-values", 100);
            if (source is not null && !sources.Contains(source)) rule.Error("template.source", "source", "A rule names one of this section's sources.");
            if (kind == "derived-from" && mode != "generated") rule.Error("template.synthesis", "kind", "derived-from needs a generated section.");
            rule.NoOtherKeys();
        }
        if (TJson.Object(body.Element, "fidelity").ValueKind == JsonValueKind.Object)
        {
            BodyReader fidelity = body.Nested("fidelity", true)!;
            fidelity.Enum("default", true, "sketch", "draft", "final");
            foreach (BodyReader when in fidelity.NestedArray("when", true, false) ?? [])
            { ReadCondition(when); when.Enum("level", true, "sketch", "draft", "final"); when.NoOtherKeys(); }
            fidelity.NoOtherKeys();
        }
        else body.Enum("fidelity", true, "sketch", "draft", "final");
        BodyReader? quality = body.Nested("quality", true);
        if (quality is not null)
        {
            quality.Enum("review", true, "none", "self", "independent", "operator");
            ReadChecks(quality); quality.NoOtherKeys();
        }
        ReadOptions(body);
        foreach (BodyReader child in body.NestedArray("fields", false, false) ?? []) ReadElement(child, ids, headings, true, depth + 1);
        if (!field) foreach (BodyReader child in body.NestedArray("sections", false, false) ?? []) ReadElement(child, ids, headings, false, depth + 1);
        body.NoOtherKeys();
    }

    private static void ReadCondition(BodyReader body)
    { body.Text("option", true, 300); body.Text("value", true, 300); }

    private static void ReadOptions(BodyReader body)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader option in body.NestedArray("options", false, false) ?? [])
        {
            string? id = option.Text("id", true, 100, Patterns.Slug, "an option id");
            if (id is not null && !ids.Add(id)) option.Error("template.duplicate", "id", "Option identifiers are unique within their owner.");
            IReadOnlyList<string>? values = option.StringArray("values", true, false, 300);
            string? value = option.Text("default", true, 300);
            if (value is not null && values is not null && (!values.Contains(value, StringComparer.Ordinal) || values.Distinct(StringComparer.Ordinal).Count() != values.Count))
                option.Error("template.option", "default", "Allowed values are distinct and include the default.");
            option.NoOtherKeys();
        }
    }

    private static void ReadChecks(BodyReader body)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader check in body.NestedArray("checks", false, true) ?? [])
        {
            string? name = check.Text("name", true, 200);
            if (name is not null && !names.Add(name)) check.Error("template.duplicate", "name", "A check is declared once within its owner.");
            if (name == "length")
            {
                long? min = check.Number("min", false, 0, 10000000);
                long? max = check.Number("max", false, 0, 10000000);
                if (min is null && max is null || min > max) check.Error("template.check", "min", "Length needs a bound, and minimum cannot exceed maximum.");
            }
            else if (name == "required-terms") check.StringArray("terms", true, false, 1000);
            else if (name is not null && name is not ("links-resolve" or "references-exist" or "names-version" or "matches-generator") && TemplatePrimitives.Check(name) is null)
                check.Error("template.primitive", "name", "The section check is not installed.");
            check.NoOtherKeys();
        }
    }
}

/// <summary>A pack's section check. Installing a check selects no template and grants no authority.</summary>
public interface ITemplateCheck
{
    /// <summary>The namespaced check identity.</summary>
    string Id { get; }
    /// <summary>Checks exact content with its already resolved sources; unknown evidence must not pass.</summary>
    TemplateCheckResult Check(string content, IReadOnlyList<TemplateSource> sources);
}

/// <summary>Installed template vocabulary extensions. Packs add role and check values, never contract properties.</summary>
public static class TemplatePrimitives
{
    private static readonly Dictionary<string, ITemplateCheck> Checks = new(StringComparer.Ordinal);
    static TemplatePrimitives()
    {
        foreach (ITemplateCheck check in FirstPartyTemplateChecks.All) RegisterCheck(check);
    }
    private static readonly HashSet<string> RoleNames = new(Types.Roles.All.Concat(RosterType.KnownRoles), StringComparer.Ordinal);
    private static readonly Dictionary<string, string> RoleBriefs = RoleNames.ToDictionary(r => r, r => r switch
    {
        "operator" => "The accountable human owns the estate and makes its reserved decisions. Record the person's words and decisions faithfully; a template creates no decision or acceptance.",
        "builder" => "Produce the assigned code, documents or records within the recorded brief. Preserve source evidence and report the exact result. Completion is a claim for review, not acceptance.",
        "reviewer" => "Inspect the exact work, its sources and criteria. Record findings and independence, and leave failed or unknown criteria unresolved.",
        "evaluator" => "Judge each declared semantic rule against all exact section and source evidence. Be independent of the writer; record passed, failed or unknown with reasons. A judgment grants no acceptance.",
        "planner" => "Plan work against recorded intent, inputs and constraints. Identify dependencies and questions without inventing authority or acceptance.",
        "decider" => "Make only decisions within the operator's existing standing. Preserve the alternatives, consequences and exact words of the decision.",
        "release-manager" => "Prepare and inspect the recorded release evidence and gates under existing authority. Preparation grants no release acceptance or publication permission.",
        "publisher" => "Carry out only explicitly authorized publication and record its observed result. A template or prepared artifact cannot authorize publication.",
        "service" => "Perform the bounded service operation under its recorded authority and preserve the exact evidence of its effects.",
        _ => "Contribute only the bounded work permitted by the recorded assignment and authority. Preserve required evidence and leave acceptance to its authorized reviewer.",
    }, StringComparer.Ordinal);
    /// <summary>The installed role names; these identify requirements, not permission grants.</summary>
    public static IReadOnlySet<string> Roles { get { lock (RoleNames) return RoleNames.ToImmutableHashSet(StringComparer.Ordinal); } }
    /// <summary>Registers a role vocabulary value; principals still need recorded standing.</summary>
    public static void RegisterRole(string role, string brief)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(brief);
        if (!Patterns.Slug.IsMatch(role)) throw new ArgumentException("A role is a slug.", nameof(role));
        lock (RoleNames)
        {
            if (!RoleNames.Add(role)) throw new InvalidOperationException("A role with this identity is already installed.");
            RoleBriefs.Add(role, brief);
        }
    }
    /// <summary>The complete installed writing brief for a role, without assigning it to anyone.</summary>
    public static string RoleBrief(string role)
    {
        ArgumentNullException.ThrowIfNull(role);
        lock (RoleNames) return RoleBriefs.TryGetValue(role, out string? brief) ? brief : throw new InvalidOperationException("The writing role's brief is unavailable.");
    }
    /// <summary>Registers an implementation once under its namespaced identity.</summary>
    public static void RegisterCheck(ITemplateCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (!Patterns.Contribution.IsMatch(check.Id)) throw new ArgumentException("A check is named namespace/name.", nameof(check));
        lock (Checks) if (!Checks.TryAdd(check.Id, check)) throw new InvalidOperationException("A template check with this identity is already installed.");
    }
    internal static ITemplateCheck? Check(string name) { lock (Checks) return Checks.GetValueOrDefault(name); }
}

internal static class TJson
{
    internal static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out JsonElement item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    internal static JsonElement Object(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out JsonElement item) ? item : default;
    internal static IEnumerable<JsonElement> Array(JsonElement value, string key) => Object(value, key) is { ValueKind: JsonValueKind.Array } items ? items.EnumerateArray() : [];
    internal static IEnumerable<string> Strings(JsonElement value, string key) => Array(value, key).Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!);
    internal static bool Flag(JsonElement value, string key) => Object(value, key).ValueKind == JsonValueKind.True;
    internal static int Fidelity(string? value) => value switch { "sketch" => 0, "draft" => 1, "final" => 2, _ => -1 };
}
