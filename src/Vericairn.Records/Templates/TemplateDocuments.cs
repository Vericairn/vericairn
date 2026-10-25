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

/// <summary>An evaluator's verdict and the exact content and source hashes read. A verdict supplies no acceptance.</summary>
public sealed record TemplateJudgment(string Rule, string Verdict, string Summary, IReadOnlyList<string> Evidence);

/// <summary>A request to review one section at a stated fidelity; the runtime reads its bytes and executes its checks.</summary>
public sealed record TemplateSectionReview(string Id, string Outcome, string Fidelity, IReadOnlyList<TemplateJudgment>? Judgments = null);

/// <summary>A complete section brief, including exact sources, required criteria, neighboring content and measured UTF-8 bytes.</summary>
public sealed record TemplateSectionBrief(string Declaration, string DeclarationHash, TemplatePin Template, string Section,
    string Purpose, string Role, string RoleBrief, string Fidelity, JsonElement Quality, IReadOnlyDictionary<string, string> Options,
    IReadOnlyList<TemplateSource> Sources, IReadOnlyList<TemplateCheckResult> Cohesion, long Bytes, bool Complete, string? Problem);

/// <summary>The template section lifecycle, derived from exact content and records and shared by every surface.</summary>
public static class TemplateDocuments
{
    private static readonly JsonSerializerOptions BriefJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    /// <summary>Refreshes only existing generated payloads of a pinned instance, preserving every other character.</summary>
    public static string RefreshGenerated(EstateModel model, RecordEnvelope declaration, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        TemplateDefinition definition = TemplateInstances.Effective(TemplateCatalog.Resolve(model, TemplateCatalog.Pin(declaration)
            ?? throw new InvalidOperationException("The document is not a template instance.")), declaration);
        foreach (JsonElement section in definition.Elements)
        {
            JsonElement synthesis = TJson.Object(section, "synthesis");
            if (TJson.Text(synthesis, "mode") != "generated") continue;
            string id = TJson.Text(section, "id")!;
            DocumentRenderResult rendered = DocumentRepair.Region(model, declaration, TJson.Text(synthesis, "generator")!, id, TJson.Strings(synthesis, "inputs").ToArray());
            if (rendered.Status != DocumentGenerationStatus.Success || rendered.Content is null)
                throw new InvalidOperationException("A required template generator is unavailable.");
            text = GeneratedRegions.Replace(text, id, rendered.Content)
                ?? throw new InvalidOperationException("Locate each generated section with unique balanced markers before refreshing it.");
        }
        _ = TemplateSections.Read(text, definition.Elements);
        return text;
    }
    /// <summary>Prepares an instance's initial bytes, filling quotes and generated sections from exact available sources.</summary>
    public static string NewContent(EstateModel model, RecordEnvelope declaration)
    {
        TemplateDefinition definition = TemplateInstances.Effective(TemplateCatalog.Resolve(model, TemplateCatalog.Pin(declaration)
            ?? throw new InvalidOperationException("The document is not an instance.")), declaration);
        string text = TemplateSections.Skeleton(definition);
        IReadOnlyDictionary<string, TemplateSectionContent> parts = TemplateSections.Read(text, definition.Elements);
        foreach (JsonElement section in definition.Elements.OrderByDescending(e => parts.GetValueOrDefault(TJson.Text(e, "id")!)?.Start ?? -1))
        {
            JsonElement synthesis = TJson.Object(section, "synthesis");
            string? mode = TJson.Text(synthesis, "mode");
            if (mode is not ("quoted" or "generated")) continue;
            string id = TJson.Text(section, "id")!;
            if (!parts.TryGetValue(id, out TemplateSectionContent? part)) throw new InvalidOperationException("A generated or quoted section could not be located.");
            string? content = mode == "quoted"
                ? TemplateEvidence.Sources(model, declaration, section, parts, null).SingleOrDefault(s => s.Id == TJson.Text(synthesis, "source"))?.Content
                : DocumentRepair.Region(model, declaration, TJson.Text(synthesis, "generator")!, id, TJson.Strings(synthesis, "inputs").ToArray()).Content;
            if (content is null) throw new InvalidOperationException("A required quoted or generated source is unavailable.");
            text = string.Concat(text.AsSpan(0, part.Start), content, text.AsSpan(part.Start + part.Length));
        }
        IReadOnlyDictionary<string, TemplateSectionContent> filled = TemplateSections.Read(text, definition.Elements);
        if (filled.Count != parts.Count) throw new InvalidOperationException("Filled content obscures another section's locator; use explicit section markers for quoted text.");
        foreach (JsonElement section in definition.Elements.Where(e => TJson.Text(TJson.Object(e, "synthesis"), "mode") == "quoted"))
        {
            string id = TJson.Text(section, "id")!;
            string? expected = TemplateEvidence.Sources(model, declaration, section, filled, null)
                .SingleOrDefault(s => s.Id == TJson.Text(TJson.Object(section, "synthesis"), "source"))?.Content;
            if (filled[id].Content != expected) throw new InvalidOperationException("A quoted section's locator does not preserve its source bytes; use explicit section markers.");
        }
        return text;
    }

    /// <summary>Derives each section without authoring a review, changing a file or granting authority.</summary>
    public static IReadOnlyList<TemplateSectionState> Sections(EstateModel model, RecordEnvelope declaration, string? commit = null, string? text = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(declaration);
        if (TemplateCatalog.Pin(declaration) is not { } pin) return [];
        try
        {
            TemplateDefinition original = TemplateCatalog.Resolve(model, pin);
            TemplateDefinition effective = TemplateInstances.Effective(original, declaration);
            IReadOnlyDictionary<string, string> options = TemplateInstances.RecordedOptions(declaration);
            if (text is null)
            {
                byte[]? bytes = TemplateEvidence.Document(model, declaration, commit);
                text = bytes is null ? "" : new UTF8Encoding(false, true).GetString(bytes);
            }
            IReadOnlyDictionary<string, TemplateSectionContent> parts = TemplateSections.Read(text, effective.Elements);
            var states = new List<TemplateSectionState>();
            foreach (JsonElement element in effective.Elements)
            {
                string id = TJson.Text(element, "id")!;
                TemplateSectionContent? content = parts.GetValueOrDefault(id);
                IReadOnlyList<TemplateSource> sources = TemplateEvidence.Sources(model, declaration, element, parts, commit);
                string digest = TemplateEvidence.Digest(original, options, element, sources);
                bool required = TemplateInstances.Required(element, options);
                string fidelity = TemplateInstances.Fidelity(element, options);
                RecordEnvelope? review = Reviews(model, declaration, id).LastOrDefault();
                JsonElement reviewed = review is null ? default : TJson.Array(review.Body, "sections").Single(s => TJson.Text(s, "id") == id);
                string? writer = Writer(model, declaration, id, content?.Hash);
                IReadOnlyList<TemplateCheckResult> checks = content is null || content.Empty ? [] : Checks(model, declaration, effective, element, content.Content, text, sources, commit);
                IReadOnlyList<TemplateCheckResult> cohesion = content is null || content.Empty ? [] : Cohesion(model, declaration, element, content.Content, sources, commit, reviewed, digest, content.Hash);
                var reasons = new List<string>();
                string state;
                if (content is null || content.Empty) { state = "missing"; reasons.Add("The section is absent or empty."); }
                else if (checks.Concat(cohesion).Any(c => c.Outcome == "failed"))
                { state = "failing"; reasons.AddRange(checks.Concat(cohesion).Where(c => c.Outcome == "failed").Select(c => c.Summary)); }
                else if (sources.Any(s => s.Status != "available") || checks.Any(c => c.Outcome != "passed"))
                { state = "due"; reasons.Add("Required source or check evidence is unavailable."); }
                else if (review is not null && TJson.Text(reviewed, "outcome") == "needs-change")
                { state = "due"; reasons.Add("The latest review requires a change."); }
                else if (review is not null && (TJson.Text(reviewed, "sourceDigest") != digest || TJson.Text(reviewed, "hash") != content.Hash))
                { state = "due"; reasons.Add("The section, a source or a cohesion partner changed after its review."); }
                else if (review is not null && TJson.Fidelity(TJson.Text(reviewed, "fidelity")) >= TJson.Fidelity(fidelity) && cohesion.All(c => c.Outcome == "passed"))
                    state = "reviewed";
                else
                {
                    state = Synthesized(model, declaration, id, content.Hash) ? "synthesized" : "drafted";
                    reasons.Add("No review clears these exact bytes at the required fidelity and source basis.");
                    reasons.AddRange(cohesion.Where(c => c.Outcome != "passed").Select(c => c.Summary));
                }
                states.Add(new(id, state, required, fidelity, content?.Hash, digest, writer, review?.Id, element, sources, checks, cohesion, reasons));
            }
            return states;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or DecoderFallbackException)
        {
            return [new("*", "failing", true, "final", null, "", null, null, JsonSerializer.SerializeToElement(new { }), [], [], [],
                ["The pinned template, its primitives, document structure or exact input bytes are unavailable or invalid."] )];
        }
    }

    /// <summary>Builds a complete brief. A byte limit refuses the complete package; it never truncates a required original.</summary>
    public static TemplateSectionBrief Brief(EstateModel model, RecordEnvelope declaration, string section, long? maxBytes = null, string? commit = null,
        IReadOnlyList<RecordFile>? recordFiles = null)
    {
        TemplatePin pin = TemplateCatalog.Pin(declaration) ?? throw new InvalidOperationException("The document is not a template instance.");
        TemplateSectionState state = Sections(model, declaration, commit).SingleOrDefault(s => s.Id == section)
            ?? throw new InvalidOperationException("The section is unavailable in this instance.");
        string role = TJson.Text(TJson.Object(state.Definition, "role"), "write")!;
        TemplateDefinition definition = TemplateCatalog.Resolve(model, pin);
        var sources = state.Sources.ToList();
        string contract = CanonicalJson.Serialize(definition.Content);
        sources.Add(new("template/contract", "template", pin.Id + "@" + pin.Version, pin.Hash, Encoding.UTF8.GetByteCount(contract), contract, "available", []));
        byte[]? document = TemplateEvidence.Document(model, declaration, commit);
        string? current = document is null ? null : TemplateSections.Read(new UTF8Encoding(false, true).GetString(document),
            TemplateInstances.Effective(definition, declaration).Elements).GetValueOrDefault(section)?.Content;
        sources.Add(new("section/current", "section", section, ContentHash.Sha256(Encoding.UTF8.GetBytes(current ?? "")),
            Encoding.UTF8.GetByteCount(current ?? ""), current ?? "", "available", []));
        RecordReference[] originals = new[] { declaration.Reference }.Concat(state.Sources.SelectMany(s => s.Records))
            .Concat(model.Current("roster").Select(r => r.Reference)).DistinctBy(r => r.Id).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
        recordFiles ??= EstateLoader.EnumerateRecordFiles(model.Root);
        foreach (RecordReference reference in originals)
        {
            RecordEnvelope record = model.ById[reference.Id];
            byte[]? bytes = recordFiles.SingleOrDefault(f => f.Path == record.ExpectedPath)?.Bytes;
            bool available = bytes is not null;
            if (bytes is not null)
            {
                try
                {
                    using JsonDocument json = CanonicalJson.Parse(bytes);
                    available = ContentHash.Sha256(CanonicalJson.SerializeUtf8(json.RootElement, RecordParser.HashExcludedKeys)) == reference.Hash;
                }
                catch (CanonicalJsonException) { available = false; }
            }
            sources.Add(new("record/" + reference.Id, "original-record", reference.ToString(), ContentHash.Sha256(bytes ?? []), bytes?.LongLength ?? 0,
                available ? new UTF8Encoding(false, true).GetString(bytes!) : null, available ? "available" : "unavailable", [reference]));
        }
        var brief = new TemplateSectionBrief(declaration.Id, declaration.ComputedHash, pin, section, TJson.Text(state.Definition, "purpose")!, role,
            TemplatePrimitives.RoleBrief(role), state.RequiredFidelity, TJson.Object(state.Definition, "quality"), TemplateInstances.RecordedOptions(declaration), sources, state.Cohesion, 0,
            sources.All(s => s.Status == "available"), null);
        if (!brief.Complete) brief = brief with { Problem = "Required sources are unavailable; no completeness claim is made." };
        brief = Measure(brief);
        if (maxBytes is < 1 || maxBytes is { } limit && brief.Bytes > limit)
            brief = Measure(brief with { Complete = false, Problem = "The complete required section context exceeds the allocation; narrow the work or increase the allocation. No source was truncated." });
        return brief;
    }

    private static TemplateSectionBrief Measure(TemplateSectionBrief brief)
    {
        // The count includes itself. Its decimal width stabilizes without estimating tokens or dropping an original.
        while (true)
        {
            long bytes = CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(brief, BriefJson)).LongLength;
            if (bytes == brief.Bytes) return brief;
            brief = brief with { Bytes = bytes };
        }
    }

    /// <summary>Runs checks and binds requested section reviews to the runtime's exact content and sources.</summary>
    public static JsonArray ReviewSections(EstateModel model, RecordEnvelope declaration, string text, string? commit,
        string reviewer, IReadOnlyList<TemplateSectionReview> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0 || requests.Any(r => r is null) || requests.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != requests.Count)
            throw new InvalidOperationException("A section review names distinct sections and at least one section.");
        IReadOnlyList<TemplateSectionState> states = Sections(model, declaration, commit, text);
        var sections = new JsonArray();
        foreach (TemplateSectionReview request in requests)
        {
            TemplateSectionState state = states.SingleOrDefault(s => s.Id == request.Id) ?? throw new InvalidOperationException("The selected section is unavailable.");
            if (TJson.Fidelity(request.Fidelity) < 0 || !DocumentReviewType.Outcomes.Contains(request.Outcome)) throw new InvalidOperationException("A section review needs a recognized outcome and fidelity.");
            if (state.ContentHash is null || state.State == "missing") throw new InvalidOperationException("An absent or empty section cannot be reviewed.");
            bool clears = request.Outcome != "needs-change";
            if (clears && state.Checks.Any(c => c.Outcome != "passed")) throw new InvalidOperationException("Every section check must pass before clearance.");
            if (clears && state.Sources.Any(s => s.Status != "available")) throw new InvalidOperationException("Every required source must be available before clearance.");
            if (!TemplateInstances.HasRole(model, reviewer, TJson.Text(TJson.Object(state.Definition, "role"), clears ? "clear" : "review")!))
                throw new InvalidOperationException("The reviewer does not hold the section's required recorded role.");
            string depth = TJson.Text(TJson.Object(state.Definition, "quality"), "review")!;
            if (clears && depth == "operator" && !model.HasRole(reviewer, Roles.Operator)) throw new InvalidOperationException("This section requires an operator review.");
            if (clears && depth == "independent" && (state.Writer is null || state.Writer == reviewer || request.Outcome == "revised"))
                throw new InvalidOperationException("This section needs a reviewer independent of its recorded writer.");
            var judgments = new JsonArray();
            var supplied = (request.Judgments ?? []).ToArray();
            var declared = TJson.Array(state.Definition, "cohesion").ToArray();
            if (supplied.Any(j => j is null || j.Evidence is null) || supplied.Select(j => j.Rule).Distinct(StringComparer.Ordinal).Count() != supplied.Length
                || supplied.Any(j => !declared.Any(r => TJson.Text(r, "id") == j.Rule && TJson.Text(r, "kind") == "judgment")))
                throw new InvalidOperationException("Judgments name distinct declared evaluator rules; deterministic rules cannot be replaced by judgments.");
            foreach (JsonElement rule in declared)
            {
                string id = TJson.Text(rule, "id")!;
                if (TJson.Text(rule, "kind") != "judgment")
                {
                    TemplateCheckResult check = state.Cohesion.Single(c => c.Id == id);
                    if (clears && check.Outcome != "passed") throw new InvalidOperationException("Every deterministic cohesion rule must pass before clearance.");
                    judgments.Add(new JsonObject { ["id"] = id, ["kind"] = TJson.Text(rule, "kind"), ["outcome"] = check.Outcome, ["summary"] = check.Summary });
                    continue;
                }
                TemplateJudgment? verdict = supplied.SingleOrDefault(j => j.Rule == id);
                if (verdict is null)
                {
                    if (clears) throw new InvalidOperationException("Cohesion is not judged until an independent evaluator records each required verdict.");
                    judgments.Add(new JsonObject { ["id"] = id, ["kind"] = "judgment", ["outcome"] = "not-judged", ["summary"] = "No evaluator verdict was supplied." });
                    continue;
                }
                if (state.Writer is null || state.Writer == reviewer || request.Outcome == "revised"
                    || !(TemplateInstances.HasRole(model, reviewer, "evaluator") || model.HasRole(reviewer, Roles.Operator)))
                    throw new InvalidOperationException("A cohesion evaluator must be qualified and independent of the recorded writer.");
                string[] required = new[] { state.ContentHash }.Concat(state.Sources.Select(s => s.Hash)).Concat(state.Sources.SelectMany(s => s.Records).Select(r => r.Hash!)).Distinct(StringComparer.Ordinal).ToArray();
                if (verdict.Verdict is not ("passed" or "failed" or "unknown") || string.IsNullOrWhiteSpace(verdict.Summary)
                    || required.Except(verdict.Evidence, StringComparer.Ordinal).Any() || verdict.Evidence.Any(h => !ContentHash.IsValid(h)))
                    throw new InvalidOperationException("An evaluator verdict must name all exact section and source evidence it read.");
                if (clears && verdict.Verdict != "passed") throw new InvalidOperationException("Failed or unknown evaluator judgments cannot clear a section.");
                judgments.Add(new JsonObject { ["id"] = id, ["kind"] = "judgment", ["outcome"] = verdict.Verdict, ["summary"] = verdict.Summary,
                    ["evidence"] = JsonSerializer.SerializeToNode(verdict.Evidence) });
            }
            sections.Add(new JsonObject
            {
                ["id"] = request.Id, ["hash"] = state.ContentHash, ["sourceDigest"] = state.SourceDigest,
                ["outcome"] = request.Outcome, ["fidelity"] = request.Fidelity,
                ["sources"] = JsonSerializer.SerializeToNode(state.Sources.Select(s => new
                {
                    id = s.Id, kind = s.Kind, identity = s.Identity, hash = s.Hash, bytes = s.Bytes, status = s.Status,
                    records = s.Records.Select(r => new { type = r.Type, id = r.Id, hash = r.Hash }),
                })),
                ["checks"] = JsonSerializer.SerializeToNode(state.Checks.Select(c => new { name = c.Id, outcome = c.Outcome, summary = c.Summary })),
                ["cohesion"] = judgments,
            });
        }
        return sections;
    }

    /// <summary>The most recent recorded author of these section bytes, falling back to the document's last recorded writer.</summary>
    public static string? Writer(EstateModel model, RecordEnvelope declaration, string section, string? hash, string? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(declaration);
        var candidates = model.OfType("result").Where(r => (before is null || string.CompareOrdinal(r.At, before) <= 0)
            && TJson.Array(r.Body, "outputs").Any(o => OutputMatches(model, declaration, o, section, hash))).ToList();
        if (candidates.Count > 0) return candidates.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).Last().By;
        return model.OfType("document-revision").Where(r => EstateModel.BodyString(r, "path") == EstateModel.BodyString(declaration, "path")
                && (before is null || string.CompareOrdinal(r.At, before) <= 0)).OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault()?.By;
    }

    private static IEnumerable<RecordEnvelope> Reviews(EstateModel model, RecordEnvelope declaration, string id) => model.Current("document-review")
        .Where(r => r.Subject?.Id == declaration.Id && r.Subject.Hash == declaration.ComputedHash && TJson.Array(r.Body, "sections").Any(s => TJson.Text(s, "id") == id))
        .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal);

    private static bool Synthesized(EstateModel model, RecordEnvelope declaration, string id, string hash) =>
        model.OfType("result").Any(r => model.CurrentPrincipal(r.By) is { } principal && EstateModel.BodyString(principal, "kind") == "agent"
            && TJson.Array(r.Body, "outputs").Any(o => OutputMatches(model, declaration, o, id, hash)));

    internal static bool OutputMatches(EstateModel model, RecordEnvelope declaration, JsonElement output, string id, string? hash)
    {
        JsonElement section = TJson.Object(output, "section");
        JsonElement document = TJson.Object(section, "document");
        return hash is not null && TJson.Text(output, "hash") == hash && TJson.Text(section, "id") == id
            && model.Chain(declaration).Contains(TJson.Text(document, "id"), StringComparer.Ordinal)
            && model.ById.TryGetValue(TJson.Text(document, "id") ?? "", out RecordEnvelope? record) && record.ComputedHash == TJson.Text(document, "hash");
    }

    internal static IEnumerable<string> CheckNames(TemplateDefinition definition, JsonElement section)
    {
        yield return "field-type";
        string? mode = TJson.Text(TJson.Object(section, "synthesis"), "mode");
        if (mode == "quoted") yield return "quoted";
        if (mode == "generated" && !TJson.Array(TJson.Object(section, "quality"), "checks").Any(c => TJson.Text(c, "name") == "matches-generator")) yield return "matches-generator";
        foreach (JsonElement check in TJson.Array(definition.Content, "checks")) yield return "document/" + TJson.Text(check, "name");
        foreach (JsonElement check in TJson.Array(TJson.Object(section, "quality"), "checks")) yield return TJson.Text(check, "name")!;
        if (TJson.Text(section, "type") == "reference") yield return "field-reference";
    }

    private static List<TemplateCheckResult> Checks(EstateModel model, RecordEnvelope declaration, TemplateDefinition definition,
        JsonElement section, string content, string documentText, IReadOnlyList<TemplateSource> sources, string? commit)
    {
        var checks = new List<TemplateCheckResult> { TemplateEvidence.FieldType(section, content) };
        JsonElement synthesis = TJson.Object(section, "synthesis");
        if (TJson.Text(synthesis, "mode") == "quoted")
        {
            TemplateSource? source = sources.SingleOrDefault(s => s.Id == TJson.Text(synthesis, "source"));
            checks.Add(new("quoted", source?.Content is null ? "not-performed" : source.Content == content ? "passed" : "failed", "Quoted section bytes must equal the selected record field byte for byte."));
        }
        if (TJson.Text(synthesis, "mode") == "generated" && !TJson.Array(TJson.Object(section, "quality"), "checks").Any(c => TJson.Text(c, "name") == "matches-generator"))
            checks.Add(Generated(model, declaration, section, content, commit, "matches-generator"));
        foreach (var selection in TJson.Array(definition.Content, "checks").Select(c => (Check: c, Whole: true))
            .Concat(TJson.Array(TJson.Object(section, "quality"), "checks").Select(c => (Check: c, Whole: false))))
        {
            JsonElement check = selection.Check;
            string name = TJson.Text(check, "name")!;
            string id = selection.Whole ? "document/" + name : name;
            string checkedText = selection.Whole ? documentText : content;
            bool passed;
            switch (name)
            {
                case "length":
                    long count = checkedText.EnumerateRunes().LongCount();
                    passed = (!check.TryGetProperty("min", out JsonElement min) || count >= min.GetInt64()) && (!check.TryGetProperty("max", out JsonElement max) || count <= max.GetInt64());
                    checks.Add(new(id, passed ? "passed" : "failed", "The complete content must meet its length bounds without dropping required information."));
                    break;
                case "required-terms":
                    passed = TJson.Strings(check, "terms").All(term => checkedText.Contains(term, StringComparison.Ordinal));
                    checks.Add(new(id, passed ? "passed" : "failed", "Every declared required term must occur."));
                    break;
                case "references-exist": checks.Add(References(model, declaration, checkedText, commit, id)); break;
                case "matches-generator" when !selection.Whole:
                    checks.Add(Generated(model, declaration, section, checkedText, commit, id)); break;
                case "links-resolve":
                case "matches-generator":
                case "names-version":
                    var body = JsonNode.Parse(declaration.Body.GetRawText())!.AsObject();
                    body["checks"] = new JsonArray(name);
                    var selected = new RecordEnvelope(declaration.Type, declaration.Id, declaration.At, declaration.By, declaration.Subject, declaration.Supersedes,
                        JsonSerializer.SerializeToElement(body), declaration.DeclaredHash, declaration.ComputedHash, declaration.Signatures, declaration.Format);
                    Func<string, bool> exists = path => TemplateEvidence.Document(model, declaration, commit, path) is not null;
                    IEnumerable<TemplateSource> checkSources = selection.Whole ? definition.Elements.SelectMany(e => TemplateEvidence.Sources(model, declaration, e, TemplateSections.Read(documentText, definition.Elements), commit)) : sources;
                    RecordEnvelope[] versions = checkSources.SelectMany(s => s.Records).Where(r => r.Type == "release" && model.ById.ContainsKey(r.Id)).DistinctBy(r => r.Id).Select(r => model.ById[r.Id]).ToArray();
                    DocumentChecks.Outcome result = DocumentChecks.Run(model, selected, checkedText, EstateModel.BodyString(declaration, "path")!, null, versions, exists, commit).Single();
                    checks.Add(new(id, result.Result, result.Summary)); break;
                default:
                    TemplateCheckResult custom = TemplatePrimitives.Check(name)?.Check(checkedText, sources) ?? new(name, "not-performed", "The declared check is unavailable.");
                    checks.Add(custom with { Id = id }); break;
            }
        }
        if (TJson.Text(section, "type") == "reference") checks.Add(References(model, declaration, content, commit, "field-reference"));
        return checks;
    }

    private static List<TemplateCheckResult> Cohesion(EstateModel model, RecordEnvelope declaration, JsonElement section, string content,
        IReadOnlyList<TemplateSource> sources, string? commit, JsonElement reviewed, string digest, string contentHash)
    {
        var checks = new List<TemplateCheckResult>();
        foreach (JsonElement rule in TJson.Array(section, "cohesion"))
        {
            string id = TJson.Text(rule, "id")!;
            string kind = TJson.Text(rule, "kind")!;
            TemplateSource? source = sources.SingleOrDefault(s => s.Id == TJson.Text(rule, "source"));
            string outcome;
            if (kind == "judgment")
            {
                JsonElement prior = TJson.Array(reviewed, "cohesion").FirstOrDefault(c => TJson.Text(c, "id") == id && TJson.Text(c, "kind") == kind);
                outcome = TJson.Text(reviewed, "hash") == contentHash && TJson.Text(reviewed, "sourceDigest") == digest ? TJson.Text(prior, "outcome") ?? "not-judged" : "not-judged";
            }
            else if (kind == "references-exist") { checks.Add(References(model, declaration, content, commit, id, source)); continue; }
            else if (kind == "derived-from") { checks.Add(Generated(model, declaration, section, content, commit, id)); continue; }
            else if (source?.Content is null) outcome = "not-performed";
            else outcome = kind switch
            {
                "covers" => TemplateEvidence.Items(source.Content).Except(TemplateEvidence.Items(content), StringComparer.Ordinal).Any() ? "failed" : "passed",
                "matches-record" => source.Kind != "record" ? "failed" : content == source.Content ? "passed" : "failed",
                "agrees-on-values" => TemplateEvidence.Items(source.Content).Order(StringComparer.Ordinal).SequenceEqual(TemplateEvidence.Items(content).Order(StringComparer.Ordinal), StringComparer.Ordinal) ? "passed" : "failed",
                _ => "not-performed",
            };
            checks.Add(new(id, outcome, (outcome == "not-judged" ? "Not judged: " : "") + TJson.Text(rule, "summary")));
        }
        return checks;
    }

    private static TemplateCheckResult Generated(EstateModel model, RecordEnvelope declaration, JsonElement section, string content, string? commit, string id)
    {
        JsonElement synthesis = TJson.Object(section, "synthesis");
        string? generator = TJson.Text(synthesis, "generator");
        if (generator is null) return new(id, "not-performed", "No generator is declared.");
        DocumentRenderResult fresh = DocumentRepair.Region(model, declaration, generator, TJson.Text(section, "id")!, TJson.Strings(synthesis, "inputs").ToArray(), commit);
        return new(id, fresh.Status != DocumentGenerationStatus.Success ? "not-performed" : fresh.Content == content ? "passed" : "failed", "Generated content must equal a fresh generation from the declared inputs.");
    }

    private static TemplateCheckResult References(EstateModel model, RecordEnvelope declaration, string content, string? commit, string id, TemplateSource? source = null)
    {
        bool Exists(string item) => model.ById.ContainsKey(item) || model.Current("work-order").Any(r => EstateModel.BodyString(r, "key") == item)
            || RecordTypeRegistry.Find(item) is not null || source?.Content is { } catalog && TemplateEvidence.Items(catalog).Contains(item, StringComparer.Ordinal)
            || model.Current("document-revision").Any(r => EstateModel.BodyString(r, "path") == item)
            || DocumentPatterns.IsSafeSourcePath(item) && TemplateEvidence.Document(model, declaration, commit, item) is not null;
        string[] missing = TemplateEvidence.Items(content).Where(item => !Exists(item)).ToArray();
        return new(id, missing.Length == 0 ? "passed" : "failed", missing.Length == 0 ? "Every named reference exists." : "References do not exist: " + string.Join(", ", missing));
    }
}
