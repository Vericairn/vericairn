// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public TemplateRenderResponse TemplateRender(TemplateRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is { } denied) return new(null, null, null, null, [], denied);
            try
            {
                if (request.Template is null || request.Subject is null || model.Findings.HasErrors)
                    throw new InvalidOperationException("An exact template, exact subject and valid source records are required.");
                TemplateDefinition definition = PresentationTemplates.Definitions.SingleOrDefault(d => d.Id == request.Template.Id
                    && d.Version == request.Template.Version && d.Hash == request.Template.Hash)
                    ?? throw new InvalidOperationException("The exact presentation template is unavailable.");
                RecordEnvelope? adoption = request.Candidate is not null && request.Adoption is { } retained
                    ? ExactPresentationRecord(model, retained) : PresentationTemplates.Adoption(model, definition.Id);
                if (adoption is null || !PresentationTemplates.Matches(adoption, definition))
                    throw new InvalidOperationException("Explicit estate adoption of this exact template is required.");
                if (request.Adoption is { } expected && (expected.Type != adoption.Type || expected.Id != adoption.Id || expected.Hash != adoption.ComputedHash))
                    throw new InvalidOperationException("The requested adoption does not match the current recorded choice.");
                RecordEnvelope subject = ExactPresentationRecord(model, request.Subject);
                Dictionary<string, string> sections;
                IReadOnlyList<RecordFile> files = session.Files();
                if (definition.Id == PresentationTemplates.Context)
                {
                    if (subject.Type != "work-order" || request.Authored is not null)
                        throw new InvalidOperationException("A context brief selects an exact work order and has no authored replacements for its generated content.");
                    ContextResponse context = ContextLocked(model, files, subject);
                    if (context.Package is null) return new(null, null, null, null, [], context.Problem);
                    sections = ContextTemplateSections(context.Package, context.Text);
                }
                else
                {
                    var records = new Dictionary<string, RecordEnvelope>(StringComparer.Ordinal);
                    AddPresentationRecord(model, subject, records);
                    AddPresentationRecord(model, adoption, records);
                    sections = new(StringComparer.Ordinal);
                    JsonElement authored = request.Authored ?? JsonSerializer.SerializeToElement(new { });
                    if (authored.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Authored input is an object.");
                    if (definition.Id == PresentationTemplates.Communication)
                    {
                        OnlyFields(authored);
                        sections["facts"] = CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(new
                        {
                            type = subject.Type, id = subject.Id, hash = subject.ComputedHash, at = subject.At, by = subject.By,
                            subject = subject.Subject, body = subject.Body,
                        }, ProtocolJson.Options));
                    }
                    else if (definition.Id == PresentationTemplates.Commit)
                    {
                        if (subject.Type != "result") throw new InvalidOperationException("A commit's checks and work references come from an exact result.");
                        OnlyFields(authored, "outcome", "reason", "attribution");
                        sections["outcome"] = RequiredText(authored, "outcome");
                        if (sections["outcome"].Contains('\n') || sections["outcome"].Contains('\r')) throw new InvalidOperationException("A commit outcome is one complete subject line; length cannot force truncation.");
                        sections["reason"] = RequiredText(authored, "reason");
                        JsonElement trailers = RequiredArray(authored, "attribution");
                        if (trailers.EnumerateArray().Any(t => t.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(t.GetString()) || t.GetString()!.Contains('\n') || t.GetString()!.Contains('\r')))
                            throw new InvalidOperationException("Declare every required attribution trailer as a nonempty single line; an empty array explicitly declares none.");
                        sections["attribution"] = trailers.GetArrayLength() == 0 ? "No attribution trailers declared; policy review remains required." : string.Join('\n', trailers.EnumerateArray().Select(t => t.GetString()));
                        sections["checks"] = CanonicalJson.SerializePretty(subject.Body.GetProperty("checks"));
                        sections["limits"] = CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(new
                        {
                            limitations = subject.Body.GetProperty("limitations"), unresolvedEffects = subject.Body.GetProperty("unresolvedEffects"),
                            nextStep = subject.Body.GetProperty("nextStep"), acceptance = "Preparation is not acceptance. Attribution and authored meaning require policy review.",
                        }));
                        sections["references"] = CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(records.Values.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Reference), ProtocolJson.Options));
                    }
                    else ReviewTemplateSections(model, subject, authored, records, sections);
                    AddOriginals(files, records.Values, sections);
                }
                if (request.Explanation is { } explanation) sections["explanation"] = explanation;
                TemplatePresentationResult result = PresentationTemplates.Render(new(definition.Id, definition.Version, definition.Hash), request.Depth ?? PresentationUse(definition, adoption).Depth,
                    adoption.Reference, sections, request.MaxBytes, request.Candidate);
                bool passed = result.Checks.All(c => c.Outcome == "passed");
                return new(PresentationUse(definition, adoption), result.Text, result.Hash, result.Bytes, result.Checks.Select(CheckDto).ToArray(),
                    passed ? null : new("failed-check", "Required presentation content or its exact source reference has changed or is missing.", []));
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or JsonException or KeyNotFoundException or IOException)
            {
                return new(null, null, null, null, [], new("refused", error is InvalidOperationException or ArgumentException ? error.Message : "The complete exact presentation basis is unavailable.", []));
            }
        }
    }

    private static PresentationTemplateUse PresentationUse(TemplateDefinition definition, RecordEnvelope adoption) =>
        new(new(definition.Id, definition.Version, definition.Hash), new(adoption.Type, adoption.Id, adoption.ComputedHash),
            adoption.Body.GetProperty("options").TryGetProperty("depth", out JsonElement depth) ? depth.GetString()! : "standard");

    private static PresentationTemplateUse? PresentationUse(EstateModel model, string id) => PresentationTemplates.Adoption(model, id) is { } adoption
        ? PresentationUse(PresentationTemplates.Definitions.Single(d => d.Id == id), adoption) : null;

    private static RecordEnvelope ExactPresentationRecord(EstateModel model, TemplateRecordReference reference) =>
        model.ById.TryGetValue(reference.Id, out RecordEnvelope? record) && record.Type == reference.Type && record.ComputedHash == reference.Hash
            ? record : throw new InvalidOperationException("An exact original reference is unavailable; another revision cannot replace it.");

    private static void AddPresentationRecord(EstateModel model, RecordEnvelope record, Dictionary<string, RecordEnvelope> records)
    {
        if (!records.TryAdd(record.Id, record)) return;
        if (record.Subject is { Hash: { } hash } subject)
            AddPresentationRecord(model, ExactPresentationRecord(model, new(subject.Type, subject.Id, hash)), records);
        AddPresentationReferences(model, record.Body, records);
    }

    private static void AddPresentationReferences(EstateModel model, JsonElement value, Dictionary<string, RecordEnvelope> records)
    {
        if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in value.EnumerateArray()) AddPresentationReferences(model, item, records);
        if (value.ValueKind != JsonValueKind.Object) return;
        if (value.TryGetProperty("type", out JsonElement type) && value.TryGetProperty("id", out JsonElement id) && value.TryGetProperty("hash", out JsonElement hash)
            && type.ValueKind == JsonValueKind.String && id.ValueKind == JsonValueKind.String && hash.ValueKind == JsonValueKind.String)
            AddPresentationRecord(model, ExactPresentationRecord(model, new(type.GetString()!, id.GetString()!, hash.GetString()!)), records);
        else foreach (JsonProperty property in value.EnumerateObject()) AddPresentationReferences(model, property.Value, records);
    }

    private static void AddOriginals(IReadOnlyList<RecordFile> files, IEnumerable<RecordEnvelope> records, Dictionary<string, string> sections)
    {
        var identities = new List<object>();
        var originals = new List<object>();
        foreach (RecordEnvelope record in records.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            byte[] raw = files.SingleOrDefault(f => f.Path == record.ExpectedPath)?.Bytes
                ?? throw new InvalidOperationException("A required original is not retained.");
            using JsonDocument parsed = CanonicalJson.Parse(raw);
            if (ContentHash.Sha256(CanonicalJson.SerializeUtf8(parsed.RootElement, RecordParser.HashExcludedKeys)) != record.ComputedHash)
                throw new InvalidOperationException("A retained original no longer matches its exact record identity.");
            var identity = new { type = record.Type, id = record.Id, hash = record.ComputedHash, byteHash = ContentHash.Sha256(raw), bytes = raw.LongLength,
                retrieval = "vericairn records show " + record.Id, required = true };
            identities.Add(identity);
            originals.Add(new { identity, encoding = "base64", content = Convert.ToBase64String(raw) });
        }
        sections["source-identities"] = CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(identities));
        sections["originals"] = CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(originals));
        sections["withheld"] = "[]\nSelection: the exact subject, its pinned record references, ancestry, adoption and declared review evidence. Every selected original is included; unselected estate material is outside this scope.";
    }

    private static void ReviewTemplateSections(EstateModel model, RecordEnvelope subject, JsonElement authored,
        Dictionary<string, RecordEnvelope> records, Dictionary<string, string> sections)
    {
        if (subject.Type != "result") throw new InvalidOperationException("A review presentation selects an exact result and its complete assignment criteria.");
        OnlyFields(authored, "reviewer", "scope", "criteria", "findings", "limits", "next");
        string reviewerId = RequiredText(authored, "reviewer");
        RecordEnvelope reviewer = model.CurrentPrincipal(reviewerId) ?? throw new InvalidOperationException("The reviewer must be a recorded principal.");
        AddPresentationRecord(model, reviewer, records);
        if (subject.Subject is not { Type: "attempt" } attemptReference || !records.TryGetValue(attemptReference.Id, out RecordEnvelope? attempt)
            || attempt.Subject is not { Type: "assignment" } assignmentReference || !records.TryGetValue(assignmentReference.Id, out RecordEnvelope? assignment))
            throw new InvalidOperationException("The exact result assignment and criteria are unavailable.");
        string[] required = assignment.Body.GetProperty("brief").GetProperty("reviewCriteria").EnumerateArray().Select(c => c.GetString()!).ToArray();
        JsonElement criteria = RequiredArray(authored, "criteria");
        if (criteria.GetArrayLength() != required.Length) throw new InvalidOperationException("Every assignment criterion needs its own verdict, method and evidence.");
        int index = 0;
        foreach (JsonElement criterion in criteria.EnumerateArray())
        {
            OnlyFields(criterion, "criterion", "outcome", "method", "evidence");
            if (RequiredText(criterion, "criterion") != required[index++]) throw new InvalidOperationException("Review criteria must preserve the assignment's complete text and order.");
            string outcome = RequiredText(criterion, "outcome");
            if (outcome is not ("passed" or "failed" or "unknown" or "not-performed")) throw new InvalidOperationException("A criterion outcome is passed, failed, unknown or not-performed.");
            _ = RequiredText(criterion, "method");
            JsonElement evidence = RequiredArray(criterion, "evidence");
            if (outcome == "passed" && evidence.GetArrayLength() == 0) throw new InvalidOperationException("A passed criterion requires exact evidence references.");
            foreach (JsonElement reference in evidence.EnumerateArray()) AddReviewEvidence(model, reference, records);
        }
        JsonElement findings = RequiredArray(authored, "findings");
        foreach (JsonElement finding in findings.EnumerateArray())
        {
            OnlyFields(finding, "condition", "effect", "evidence", "disposition");
            foreach (string key in new[] { "condition", "effect", "disposition" }) _ = RequiredText(finding, key);
            JsonElement evidence = RequiredArray(finding, "evidence");
            if (evidence.GetArrayLength() == 0) throw new InvalidOperationException("A finding retains its exact evidence.");
            foreach (JsonElement reference in evidence.EnumerateArray()) AddReviewEvidence(model, reference, records);
        }
        JsonElement limits = RequiredArray(authored, "limits");
        if (limits.EnumerateArray().Any(l => l.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(l.GetString()))) throw new InvalidOperationException("Review limits are complete nonempty statements.");
        sections["subject"] = CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(new { type = subject.Type, id = subject.Id, hash = subject.ComputedHash, acceptance = "not conferred" }));
        string independence = reviewer.Id == model.CurrentPrincipal(subject.By)?.Id ? "self" : model.HasRole(reviewerId, "operator") ? "operator" : "independent";
        string verdict = criteria.EnumerateArray().Any(c => c.GetProperty("outcome").GetString() == "failed") ? "fail"
            : findings.GetArrayLength() == 0 && criteria.EnumerateArray().All(c => c.GetProperty("outcome").GetString() == "passed") ? "pass in the examined scope" : "findings or unverified criteria";
        sections["verdict"] = verdict + " — " + subject.Reference + " — " + independence + "; review proposal, not acceptance";
        sections["scope"] = RequiredText(authored, "scope");
        sections["criteria"] = CanonicalJson.SerializePretty(criteria);
        sections["findings"] = CanonicalJson.SerializePretty(findings);
        sections["independence"] = CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(new
        {
            reviewer = reviewer.Reference, subjectAuthor = subject.By,
            independence,
            status = "Authored review proposal; the template does not record a review or acceptance.",
        }));
        sections["checks"] = CanonicalJson.SerializePretty(subject.Body.GetProperty("checks"));
        sections["limits"] = CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(new
        {
            review = limits, result = subject.Body.GetProperty("limitations"), unresolvedEffects = subject.Body.GetProperty("unresolvedEffects"),
        }));
        sections["next"] = RequiredText(authored, "next");
    }

    private static void AddReviewEvidence(EstateModel model, JsonElement evidence, Dictionary<string, RecordEnvelope> records)
    {
        OnlyFields(evidence, "type", "id", "hash");
        AddPresentationRecord(model, ExactPresentationRecord(model, new(RequiredText(evidence, "type"), RequiredText(evidence, "id"), RequiredText(evidence, "hash"))), records);
    }

    private static string RequiredText(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out JsonElement field)
        && field.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(field.GetString()) ? field.GetString()!
        : throw new InvalidOperationException("A complete authored statement is required for " + key + ".");
    private static JsonElement RequiredArray(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out JsonElement field)
        && field.ValueKind == JsonValueKind.Array ? field : throw new InvalidOperationException("An explicit complete array is required for " + key + ".");
    private static void OnlyFields(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(p => !fields.Contains(p.Name, StringComparer.Ordinal))
            || value.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != value.EnumerateObject().Count())
            throw new InvalidOperationException("Authored input contains unknown or repeated fields; generated facts cannot be overridden.");
    }

    private static Dictionary<string, string> ContextTemplateSections(ContextPackage package, string original) => new(StringComparer.Ordinal)
    {
        ["basis"] = CanonicalJson.SerializePretty(package.Payload.GetProperty("manifest").GetProperty("content").GetProperty("basis")),
        ["required-sources"] = original,
        ["originals"] = CanonicalJson.SerializePretty(package.Payload.GetProperty("originals")),
        ["source-identities"] = CanonicalJson.SerializePretty(package.Payload.GetProperty("manifest")),
        ["withheld"] = CanonicalJson.SerializePretty(package.Payload.GetProperty("withheld")),
    };

    private static TemplatePresentationResult RenderContextTemplate(PresentationTemplateUse use, ContextPackage package, string original, string depth) =>
        PresentationTemplates.Render(new(use.Template.Id, use.Template.Version, use.Template.Hash), depth,
            new(use.Adoption.Type, use.Adoption.Id, use.Adoption.Hash), ContextTemplateSections(package, original));
}
