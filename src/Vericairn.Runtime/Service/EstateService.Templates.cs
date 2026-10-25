// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Types;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public TemplatesResponse Templates()
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is { } denied) return new([], denied);
            try { return new(TemplateCatalog.All(model).Select(d => new TemplateDto(new(d.Id, d.Version, d.Hash), d.Content)).ToArray()); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
            { return new([], new Problem("unavailable", "The template catalogue contains unavailable or invalid content or primitives.", [])); }
        }
    }

    /// <inheritdoc />
    public DocumentSectionsResponse DocumentSections(string declaration, string? commit = null)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is { } denied) return new(null, null, [], denied);
            RecordEnvelope? document = TemplateDocument(model, declaration);
            return document is null ? new(null, null, [], new Problem("not-found", "No template instance matches this declaration or path.", []))
                : new(document.Id, EstateModel.BodyString(document, "path"), TemplateDocuments.Sections(model, document, commit).Select(SectionDto).ToArray());
        }
    }

    /// <inheritdoc />
    public SectionBriefResponse SectionBrief(string declaration, string section, long? maxBytes = null, string? commit = null)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is { } denied) return new(null, denied);
            RecordEnvelope? document = TemplateDocument(model, declaration);
            if (document is null) return new(null, new Problem("not-found", "No template instance matches this declaration or path.", []));
            try
            {
                TemplateSectionBrief brief = TemplateDocuments.Brief(model, document, section, maxBytes, commit, session.Files());
                return new(new(brief.Declaration, brief.DeclarationHash, new(brief.Template.Id, brief.Template.Version, brief.Template.Hash), brief.Section,
                    brief.Purpose, brief.Role, brief.RoleBrief, brief.Fidelity, brief.Quality, brief.Options, brief.Sources.Select(SourceDto).ToArray(),
                    brief.Cohesion.Select(CheckDto).ToArray(), brief.Bytes, brief.Complete, brief.Problem),
                    brief.Complete ? null : new Problem("incomplete", brief.Problem!, []));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
            { return new(null, new Problem("unavailable", "The requested section and its complete evidence could not be read.", [])); }
        }
    }

    /// <inheritdoc />
    public DocumentNewResponse DocumentNew(DocumentNewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Ulid.IsValid(request.By) || request.Template is null || !DocumentPatterns.IsSafePath(request.Path)
            || !Patterns.DocumentPath.IsMatch(request.Path) || string.IsNullOrWhiteSpace(request.Title) || !LivingDocumentType.Levels.Contains(request.Level))
            return NewRefused("Name an author, exact template, title, document path under design/ or docs/, and a recognized due level.");
        lock (gate)
        {
            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is { } denied) return new(null, null, null, denied);
            BatchJournal? journal = null;
            AuthoredRecord? record = null;
            bool stored = false;
            string? hash = null;
            try
            {
                string file = GeneratedDocumentVerification.SafeFile(session.Root, request.Path);
                if (File.Exists(file) || Directory.Exists(file)) return NewRefused("The destination already exists; instantiation never overwrites existing work.");
                TemplateDefinition definition = TemplateCatalog.Resolve(model, new(request.Template.Id, request.Template.Version, request.Template.Hash));
                if (!TemplateCatalog.All(model).Any(t => t.Id == definition.Id && t.Version == definition.Version)) return NewRefused("The template version is retired for new instances.");
                IReadOnlyDictionary<string, string> options = TemplateInstances.Options(model, definition, request.Scope ?? default, request.Options ?? default);
                var template = new JsonObject
                {
                    ["id"] = definition.Id, ["version"] = definition.Version, ["hash"] = definition.Hash, ["owner"] = request.By,
                    ["options"] = JsonSerializer.SerializeToNode(options),
                    ["overrides"] = request.Overrides is { } overrides ? JsonNode.Parse(overrides.GetRawText()) : new JsonArray(),
                };
                if (request.Scope is { } scope) template["scope"] = JsonNode.Parse(scope.GetRawText());
                var body = new JsonObject
                {
                    ["path"] = request.Path, ["title"] = request.Title, ["kind"] = "authored", ["source"] = "estate-file",
                    ["triggers"] = new JsonArray(new JsonObject { ["on"] = "decision", ["level"] = request.Level }),
                    ["clearedBy"] = "any", ["summary"] = "An instance of " + definition.Id + " at " + definition.Version + ".",
                    ["template"] = template,
                };
                var regions = new JsonArray();
                foreach (JsonElement section in TemplateInstances.Effective(definition, JsonSerializer.SerializeToElement(template)).Elements)
                {
                    JsonElement synthesis = section.GetProperty("synthesis");
                    if (synthesis.GetProperty("mode").GetString() != "generated") continue;
                    var region = new JsonObject { ["id"] = section.GetProperty("id").GetString(), ["generator"] = synthesis.GetProperty("generator").GetString() };
                    if (synthesis.GetProperty("inputs").GetArrayLength() > 0) region["inputs"] = JsonNode.Parse(synthesis.GetProperty("inputs").GetRawText());
                    regions.Add(region);
                }
                if (regions.Count > 0) body["regions"] = regions;
                var draft = new RecordDraft("living-document", request.By, JsonSerializer.SerializeToElement(body));
                (record, string? refusal) = AuthorLocked(draft, model);
                if (record is null) return NewRefused(refusal!);
                if (JudgeProposedLocked([record]) is { } rejected) return new(null, null, null, rejected);
                byte[] bytes = new UTF8Encoding(false, true).GetBytes(TemplateDocuments.NewContent(model, record.Envelope));
                if (EstateChecks.Content(request.Path, bytes).HasErrors) return NewRefused("The prepared document fails ordinary repository content checks.");
                hash = ContentHash.Sha256(bytes);
                DocumentChangePlan companions = PrepareDocuments(model, [record]);
                if (companions.Status != DocumentGenerationStatus.Success) return NewRefused("The declaration's generated companions could not be prepared.");
                DocumentOutputChange[] outputs = [.. companions.Changes, new(request.Path, null, [.. bytes])];
                GeneratedOutputStore.Validate(session.Root, outputs);
                journal = BatchJournal.Begin(session, "Create a template instance and its declaration.", null, [record], outputs);
                session.Write(record);
                stored = true;
                GeneratedOutputStore.Apply(session.Root, outputs, [], BeforePublishingDocuments);
                journal.Close();
                return new(new(record.RelativePath, record.Envelope.Id, record.Envelope.ComputedHash), request.Path, hash);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
            {
                if (stored && record is not null) return new(new(record.RelativePath, record.Envelope.Id, record.Envelope.ComputedHash), request.Path, hash,
                    new Problem("reconcile-required", "The instance declaration was stored; its original batch retains the document output. Reopen the estate to reconcile before retrying.", []));
                journal?.Close();
                return NewRefused("The instance could not be prepared from its contract, overrides and complete sources; no declaration was stored.");
            }
        }
    }

    private static DocumentNewResponse NewRefused(string message) => new(null, null, null, new Problem("refused", message, []));

    private static TemplateSourceDto SourceDto(TemplateSource source) => new(source.Id, source.Kind, source.Identity, source.Hash, source.Bytes,
        source.Content, source.Status, source.Records.Select(r => new TemplateRecordReference(r.Type, r.Id, r.Hash!)).ToArray());
    private static TemplateCheckDto CheckDto(TemplateCheckResult check) => new(check.Id, check.Outcome, check.Summary);
    private static TemplateSectionDto SectionDto(TemplateSectionState section) => new(section.Id, section.State, section.Required, section.RequiredFidelity,
        section.ContentHash, section.SourceDigest, section.Writer, section.Review, section.Definition, section.Sources.Select(SourceDto).ToArray(),
        section.Checks.Select(CheckDto).ToArray(), section.Cohesion.Select(CheckDto).ToArray(), section.Reasons);

    private static TemplateSectionReview SectionRequest(DocumentSectionReview request) => new(request.Id, request.Outcome, request.Fidelity,
        request.Judgments?.Select(j => new TemplateJudgment(j.Rule, j.Verdict, j.Summary, j.Evidence)).ToArray());

    private static RecordEnvelope? TemplateDocument(EstateModel model, string selection)
    {
        RecordEnvelope? document = model.ById.TryGetValue(selection, out RecordEnvelope? selected) && selected.Type == "living-document" ? model.Latest(selected) : null;
        document ??= model.Current("living-document").SingleOrDefault(d => EstateModel.BodyString(d, "path") == selection && EstateModel.BodyString(d, "repository") is null);
        return document is not null && TemplateCatalog.Pin(document) is not null ? document : null;
    }
}
