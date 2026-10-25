// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Templates;

/// <summary>Additive format-3 instance, review and result fields. Historical pins remain evidence, never authority.</summary>
public static class TemplateRecords
{
    /// <summary>Validates the optional instance contract on a living-document declaration.</summary>
    public static void Declaration(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        BodyReader? template = body.Since(3, "template") ? body.Nested("template", false) : null;
        BodyReader? upgrade = body.Since(3, "templateUpgrade") ? body.Nested("templateUpgrade", false) : null;
        if (template is null)
        {
            if (upgrade is not null) context.Error("template.upgrade", "An upgrade belongs to a template instance.");
            return;
        }
        ReadPin(template);
        string? owner = template.Ulid("owner", true);
        context.RequireReference("template.owner", owner, "principal");
        ReadScope(template.Nested("scope", false), false);
        BodyReader? choices = template.Nested("options", true);
        if (choices is not null) foreach (JsonProperty property in choices.Element.EnumerateObject()) choices.Text(property.Name, true, 300);
        var changes = template.NestedArray("overrides", true, true) ?? [];
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader change in changes)
        {
            string? section = change.Text("section", true, 100, Patterns.Slug, "a section identifier");
            if (section is not null && !changed.Add(section)) change.Error("template.override", "section", "Each section has at most one structural override.");
            string? action = change.Enum("action", true, "omit", "rename", "add");
            change.Text("reason", true, 2000);
            if (action == "rename") change.Text("heading", true, 300);
            if (action == "add") change.Nested("definition", true);
            if (action == "omit") ReadReference(context, change, "decision", "decision", false);
            change.NoOtherKeys();
        }
        template.NoOtherKeys();
        if (EstateModel.BodyString(context.Record, "kind") != "authored" || body.Has("pathPattern"))
            context.Error("template.instance", "A template instance is one authored living document, possibly containing generated sections.");
        RecordEnvelope? previous = context.Find(context.Record.Supersedes);
        string? previousOwner = previous is null ? null : TJson.Text(TJson.Object(previous.Body, "template"), "owner");
        if (context.Record.By != (previousOwner ?? owner) && !context.HasRole(context.Record.By, Roles.Operator))
            context.Error("template.owner", "An instance is created or revised by its owner or an operator; a template grants no standing.");
        if (previousOwner is not null && owner != previousOwner && !context.HasRole(context.Record.By, Roles.Operator))
            context.Error("template.owner", "An owner change requires the operator's existing authority.");
        bool resolved = false;
        try
        {
            if (TemplateCatalog.Pin(context.Record) is not { } pin) return;
            TemplateDefinition definition = TemplateCatalog.Resolve(context.Model, pin);
            resolved = true;
            Dictionary<string, JsonElement> allowed = TemplateDefinition.OptionDefinitions(definition.Content);
            JsonElement values = TJson.Object(template.Element, "options");
            if (values.ValueKind == JsonValueKind.Object)
            {
                if (values.EnumerateObject().Select(p => p.Name).Except(allowed.Keys, StringComparer.Ordinal).Any()
                    || allowed.Keys.Except(values.EnumerateObject().Select(p => p.Name), StringComparer.Ordinal).Any())
                    context.Error("template.options", "An instance records every effective option, including defaults, and no undeclared option.");
                foreach (JsonProperty value in values.EnumerateObject())
                    if (value.Value.ValueKind != JsonValueKind.String || !allowed.TryGetValue(value.Name, out JsonElement option) || !TJson.Strings(option, "values").Contains(value.Value.GetString(), StringComparer.Ordinal))
                        context.Error("template.options", "An instance option is not one of the template's allowed values.");
            }
            foreach (BodyReader change in changes.Where(c => TJson.Text(c.Element, "action") == "omit"))
            {
                JsonElement section = definition.Elements.FirstOrDefault(e => TJson.Text(e, "id") == TJson.Text(change.Element, "section"));
                if (section.ValueKind != JsonValueKind.Undefined && TemplateInstances.Required(section, TemplateInstances.RecordedOptions(context.Record)))
                {
                    RecordEnvelope? decision = context.Find(TJson.Text(TJson.Object(change.Element, "decision"), "id"));
                    string path = EstateModel.BodyString(context.Record, "path") ?? "";
                    string effect = "omit-section " + path + "#" + TJson.Text(section, "id");
                    if (decision is null || !TJson.Strings(decision.Body, "documents").Contains(path, StringComparer.Ordinal)
                        || !TJson.Strings(decision.Body, "effects").Contains(effect, StringComparer.Ordinal)
                        || !context.HasRole(EstateModel.BodyString(decision, "decidedBy"), Roles.Operator))
                        context.Error("template.omission", "Omitting a required section needs an exact operator decision naming the document and the omit-section effect.");
                }
            }
            TemplateInstances.Effective(definition, context.Record);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            if (resolved) context.Error("template.override", "The instance overrides do not form a valid template contract.");
            else context.Warning("template.unavailable", "The pinned template or its declared primitives cannot be resolved; the instance cannot be cleared here.");
        }
        if (upgrade is not null)
        {
            BodyReader? offered = upgrade.Nested("offered", true);
            if (offered is not null) { ReadPin(offered); offered.NoOtherKeys(); }
            string? outcome = upgrade.Enum("outcome", true, "accepted", "declined");
            upgrade.Text("reason", true, 2000);
            upgrade.NoOtherKeys();
            TemplatePin? before = previous is null ? null : TemplateCatalog.Pin(previous);
            TemplatePin? after = TemplateCatalog.Pin(context.Record);
            if (before is null || after is null || offered is null || TJson.Text(offered.Element, "id") != before.Id
                || (outcome == "declined" && before != after)
                || (outcome == "accepted" && (after.Id != TJson.Text(offered.Element, "id") || after.Version != TJson.Text(offered.Element, "version") || after.Hash != TJson.Text(offered.Element, "hash"))))
                context.Error("template.upgrade", "An upgrade records the offered pin; acceptance adopts exactly it and decline preserves the old pin.");
        }
        else if (previous is not null && TemplateCatalog.Pin(previous) is { } before && before != TemplateCatalog.Pin(context.Record))
            context.Error("template.upgrade", "Changing the pinned template version requires an explicit accepted upgrade and reason.");
    }

    /// <summary>Validates section evidence on document reviews without rejudging historical content against today's files.</summary>
    public static void Review(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IReadOnlyList<BodyReader>? sections = context.Body.Since(3, "sections") ? context.Body.NestedArray("sections", false, false) : null;
        RecordEnvelope? declaration = context.Find(context.Record.Subject?.Id);
        if (sections is null)
        {
            if (declaration is not null && TemplateCatalog.Pin(declaration) is not null)
                context.Error("template.review", "A template instance requires explicit section review evidence.");
            return;
        }
        if (declaration is null || TemplateCatalog.Pin(declaration) is null) context.Error("template.review", "Section evidence belongs to a pinned template instance.");
        TemplateDefinition? definition = null;
        try
        {
            if (declaration is not null && TemplateCatalog.Pin(declaration) is { } pin)
                definition = TemplateInstances.Effective(TemplateCatalog.Resolve(context.Model, pin), declaration);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        { context.Warning("template.unavailable", "The retained section contract is unavailable; no current clearance can be established here."); }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader section in sections)
        {
            string? id = section.Text("id", true, 100, Patterns.Slug, "a section identifier");
            if (id is not null && !ids.Add(id)) section.Error("template.review", "id", "Each section is reviewed once in a review.");
            string? hash = section.Hash("hash", true);
            section.Hash("sourceDigest", true);
            string? outcome = section.Enum("outcome", true, DocumentReviewType.Outcomes);
            section.Enum("fidelity", true, "sketch", "draft", "final");
            bool clears = outcome is "current" or "revised";
            if (TJson.Text(context.Body.Element, "outcome") != "needs-change" && outcome == "needs-change")
                section.Error("template.review", "outcome", "A clearing review cannot contain a needs-change section.");
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            var sourceHashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyReader source in section.NestedArray("sources", true, true) ?? [])
            {
                string? sourceId = source.Text("id", true, 100, Patterns.Slug, "a source identifier");
                if (sourceId is not null && !sourceIds.Add(sourceId)) source.Error("template.source", "id", "Each source has one exact evidence selection.");
                source.Enum("kind", true, "record", "records", "section", "file", "external");
                source.Text("identity", true, 100000);
                string? sourceHash = source.Hash("hash", true);
                if (sourceHash is not null) sourceHashes.Add(sourceHash);
                source.Number("bytes", true, 0, 1L << 40);
                string? status = source.Enum("status", true, "available", "unavailable");
                if (clears && status != "available") source.Error("template.source", "status", "A clearance requires every source to be available.");
                foreach (BodyReader reference in source.NestedArray("records", true, true) ?? [])
                {
                    string? type = reference.Text("type", true, 100);
                    string? recordId = reference.Ulid("id", true);
                    string? recordHash = reference.Hash("hash", true);
                    if (recordHash is not null) sourceHashes.Add(recordHash);
                    RecordEnvelope? record = type is null ? null : context.RequireReference("sources.records", recordId, type);
                    if (record is not null && record.ComputedHash != recordHash) reference.Error("template.reference", "hash", "The source record hash must match its exact retained revision.");
                    reference.NoOtherKeys();
                }
                source.NoOtherKeys();
            }
            var checkNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyReader check in section.NestedArray("checks", true, true) ?? [])
            {
                string? name = check.Text("name", true, 200);
                if (name is not null && !checkNames.Add(name)) check.Error("template.check", "name", "Each declared check has one outcome.");
                string? verdict = check.Enum("outcome", true, DocumentReviewType.CheckOutcomes);
                check.Text("summary", true, 2000); check.NoOtherKeys();
                if (clears && verdict != "passed") check.Error("template.check", "outcome", "A section clearance requires every declared check to pass.");
            }
            var rules = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyReader rule in section.NestedArray("cohesion", true, true) ?? [])
            {
                string? key = rule.Text("id", true, 100, Patterns.Slug, "a rule identifier");
                if (key is not null && !rules.Add(key)) rule.Error("template.review", "id", "Each cohesion rule has one verdict.");
                string? kind = rule.Enum("kind", true, "covers", "references-exist", "matches-record", "agrees-on-values", "derived-from", "judgment");
                string? verdict = rule.Enum("outcome", true, "passed", "failed", "unknown", "not-judged", "not-performed");
                rule.Text("summary", true, 2000);
                IReadOnlyList<string>? evidence = kind == "judgment" ? rule.StringArray("evidence", verdict != "not-judged", false, 71) : null;
                if (evidence is not null && evidence.Any(h => !ContentHash.IsValid(h))) rule.Error("template.evidence", "evidence", "Evidence names exact SHA-256 hashes.");
                if (kind == "judgment" && verdict != "not-judged" && declaration is not null && id is not null)
                {
                    string? writer = TemplateDocuments.Writer(context.Model, declaration, id, hash, context.Record.At);
                    if (writer is null || writer == context.Record.By || outcome == "revised"
                        || !(TemplateInstances.HasRole(context.Model, context.Record.By, "evaluator") || context.HasRole(context.Record.By, Roles.Operator)))
                        context.Error("template.evaluator", "An evaluator judgment requires a qualified principal independent of the recorded writer.");
                    if (hash is not null && evidence is not null && !evidence.Contains(hash, StringComparer.Ordinal)) rule.Error("template.evidence", "evidence", "The evaluator must read the exact section bytes.");
                    if (evidence is not null && sourceHashes.Except(evidence, StringComparer.Ordinal).Any()) rule.Error("template.evidence", "evidence", "The evaluator must read every exact source selection and record revision.");
                }
                if (clears && verdict != "passed") rule.Error("template.cohesion", "outcome", "A section clearance cannot claim failed, unknown or unjudged cohesion.");
                rule.NoOtherKeys();
            }
            if (definition is not null && declaration is not null)
            {
                JsonElement contract = definition.Elements.FirstOrDefault(e => TJson.Text(e, "id") == id);
                if (contract.ValueKind == JsonValueKind.Undefined) context.Error("template.review", "A reviewed section must exist in the exact instance contract.");
                else
                {
                    if (!checkNames.SetEquals(TemplateDocuments.CheckNames(definition, contract))) context.Error("template.check", "A section review includes every declared check and no substitute check.");
                    if (!sourceIds.SetEquals(TJson.Array(contract, "sources").Select(s => TJson.Text(s, "id")!))) context.Error("template.source", "A section review includes every declared source selection.");
                    JsonElement[] declared = TJson.Array(contract, "cohesion").ToArray();
                    if (!rules.SetEquals(declared.Select(r => TJson.Text(r, "id")!))
                        || TJson.Array(section.Element, "cohesion").Any(r => !declared.Any(d => TJson.Text(d, "id") == TJson.Text(r, "id") && TJson.Text(d, "kind") == TJson.Text(r, "kind"))))
                        context.Error("template.cohesion", "A section review records every cohesion rule with its declared check or evaluator kind.");
                    if (!TemplateInstances.HasRole(context.Model, context.Record.By, TJson.Text(TJson.Object(contract, "role"), clears ? "clear" : "review")!))
                        context.Error("template.reviewer", "The reviewer needs the section's recorded role; the template assigns no role.");
                    string? depth = TJson.Text(TJson.Object(contract, "quality"), "review");
                    string? writer = TemplateDocuments.Writer(context.Model, declaration, id!, hash, context.Record.At);
                    if (clears && (depth == "operator" && !context.HasRole(context.Record.By, Roles.Operator)
                        || depth == "independent" && (writer is null || writer == context.Record.By || outcome == "revised")))
                        context.Error("template.independence", "A clearance must meet the section's recorded review depth.");
                }
            }
            section.NoOtherKeys();
        }
    }

    /// <summary>Validates an optional result output's exact section identity and authored-only restrictions.</summary>
    public static bool Output(ValidationContext context, BodyReader output, string? hash, long? bytes, string? location)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);
        BodyReader? section = output.Since(3, "section") ? output.Nested("section", false) : null;
        if (section is null) return false;
        string? id = section.Text("id", true, 100, Patterns.Slug, "a section identifier");
        RecordEnvelope? declaration = ReadReference(context, section, "document", "living-document", true);
        section.NoOtherKeys();
        if (declaration is null) return true;
        if (location != EstateModel.BodyString(declaration, "path")) context.Error("template.output", "A section output's location is the declared document path.");
        try
        {
            TemplateDefinition definition = TemplateInstances.Effective(TemplateCatalog.Resolve(context.Model, TemplateCatalog.Pin(declaration)
                ?? throw new InvalidOperationException("The document is not an instance.")), declaration);
            JsonElement element = definition.Elements.FirstOrDefault(e => TJson.Text(e, "id") == id);
            if (element.ValueKind == JsonValueKind.Undefined) { context.Error("template.output", "The output names no section in the instance."); return true; }
            if (!TemplateInstances.HasRole(context.Model, context.Record.By, TJson.Text(TJson.Object(element, "role"), "write")!))
                context.Error("template.writer", "The contributor does not hold this section's required recorded writing role.");
            JsonElement synthesis = TJson.Object(element, "synthesis");
            bool agent = context.Principal(context.Record.By) is { } principal && EstateModel.BodyString(principal, "kind") == "agent";
            if (agent && TJson.Text(synthesis, "mode") == "authored" && TJson.Flag(synthesis, "personOnly"))
                context.Error("template.authored-only", "An agent result cannot write an authored-only section.");
            if (EstateModel.BodyString(declaration, "repository") is null && TemplateEvidence.Document(context.Model, declaration, null) is { } document)
            {
                TemplateSectionContent? actual = TemplateSections.Read(new UTF8Encoding(false, true).GetString(document), definition.Elements).GetValueOrDefault(id ?? "");
                if (actual is null || actual.Hash != hash || bytes is not null && Encoding.UTF8.GetByteCount(actual.Content) != bytes)
                    context.Findings.Info("result.output-changed", output.Location, "The section bytes no longer match this result's exact output pin.");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or DecoderFallbackException)
        { context.Warning("template.unavailable", "The result's retained template or section is unavailable here."); }
        return true;
    }

    /// <summary>Checks current evidence before any authoring route stores template-bearing records.</summary>
    public static string? AuthoringRefusal(EstateModel model, string type, JsonElement body, RecordReference? subject, string by)
    {
        ArgumentNullException.ThrowIfNull(model);
        try
        {
            if (type == "living-document" && TJson.Object(body, "template").ValueKind == JsonValueKind.Object)
            {
                JsonElement template = TJson.Object(body, "template");
                TemplateCatalog.Resolve(model, new(TJson.Text(template, "id") ?? "", TJson.Text(template, "version") ?? "", TJson.Text(template, "hash") ?? ""));
            }
            if (type == "template-defaults")
            {
                JsonElement template = TJson.Object(body, "template");
                TemplateCatalog.Resolve(model, new(TJson.Text(template, "id") ?? "", TJson.Text(template, "version") ?? "", TJson.Text(template, "hash") ?? ""));
            }
            if (type == "document-review" && subject is not null && model.ById.TryGetValue(subject.Id, out RecordEnvelope? declaration) && TemplateCatalog.Pin(declaration) is not null)
            {
                JsonElement sections = TJson.Object(body, "sections");
                if (sections.ValueKind != JsonValueKind.Array) return "A template instance is reviewed section by section; name the sections being reviewed.";
                TemplateSectionReview[] requests = sections.EnumerateArray().Select(s => new TemplateSectionReview(TJson.Text(s, "id")!, TJson.Text(s, "outcome")!, TJson.Text(s, "fidelity")!,
                    TJson.Array(s, "cohesion").Where(c => TJson.Text(c, "kind") == "judgment" && TJson.Text(c, "outcome") != "not-judged")
                        .Select(c => new TemplateJudgment(TJson.Text(c, "id")!, TJson.Text(c, "outcome")!, TJson.Text(c, "summary")!, TJson.Strings(c, "evidence").ToArray())).ToArray())).ToArray();
                string? commit = TJson.Text(TJson.Object(body, "content"), "commit");
                byte[] bytes = TemplateEvidence.Document(model, declaration, commit) ?? throw new InvalidOperationException("The document is unavailable.");
                if (TJson.Text(TJson.Object(body, "content"), "hash") != ContentHash.Sha256(bytes)) return "The review must pin the document bytes actually read.";
                JsonArray expected = TemplateDocuments.ReviewSections(model, declaration, new UTF8Encoding(false, true).GetString(bytes), commit, by, requests);
                if (CanonicalJson.Serialize(JsonSerializer.SerializeToElement(expected)) != CanonicalJson.Serialize(sections))
                    return "The section review's hashes, checks or cohesion differ from the current exact evidence.";
            }
            if (type == "result") foreach (JsonElement output in TJson.Array(body, "outputs").Where(o => TJson.Object(o, "section").ValueKind == JsonValueKind.Object))
            {
                JsonElement section = TJson.Object(output, "section");
                JsonElement document = TJson.Object(section, "document");
                if (!model.ById.TryGetValue(TJson.Text(document, "id") ?? "", out RecordEnvelope? declared) || !model.IsCurrent(declared) || declared.ComputedHash != TJson.Text(document, "hash"))
                    return "A section output must pin its current exact instance declaration.";
                TemplateSectionState? state = TemplateDocuments.Sections(model, declared).SingleOrDefault(s => s.Id == TJson.Text(section, "id"));
                if (state?.ContentHash != TJson.Text(output, "hash") || state is null || state.State == "missing") return "A section output must name the actual available section bytes.";
                string? mode = TJson.Text(TJson.Object(state.Definition, "synthesis"), "mode");
                if (mode is "quoted" or "generated" && state.Checks.Any(c => c.Outcome != "passed")) return "Quoted and generated outputs must pass their byte comparisons.";
            }
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException or DecoderFallbackException)
        { return "The template record could not be bound to complete, current section evidence. " + (error is InvalidOperationException ? error.Message : "Inspect its schema and declared sources."); }
    }

    internal static void ReadPin(BodyReader pin)
    { pin.Text("id", true, 200, Patterns.Contribution, "namespace/name"); pin.Text("version", true, 100, Patterns.SemanticVersion, "a semantic version"); pin.Hash("hash", true); }

    internal static void ReadScope(BodyReader? scope, bool single)
    {
        if (scope is null) return;
        if (single)
        {
            string? kind = scope.Enum("kind", true, "estate", "domain", "product", "project");
            scope.Text("target", kind != "estate", 200);
            if (kind == "estate" && scope.Has("target")) scope.Error("template.scope", "target", "Estate defaults have no narrower target.");
        }
        else { scope.Text("domain", false, 200); scope.Ulid("product", false); scope.Text("project", false, 200); }
        scope.NoOtherKeys();
    }

    private static RecordEnvelope? ReadReference(ValidationContext context, BodyReader body, string field, string type, bool required)
    {
        BodyReader? reference = body.Nested(field, required);
        if (reference is null) return null;
        reference.Enum("type", true, type);
        string? id = reference.Ulid("id", true);
        string? hash = reference.Hash("hash", true);
        reference.NoOtherKeys();
        RecordEnvelope? target = context.RequireReference(field, id, type);
        if (target is not null && target.ComputedHash != hash) context.Error("template.reference", "A template evidence reference must pin the exact record hash.");
        return target;
    }
}
