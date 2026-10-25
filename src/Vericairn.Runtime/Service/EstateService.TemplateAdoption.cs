// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public DocumentAdoptResponse DocumentAdopt(DocumentAdoptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Ulid.IsValid(request.By) || request.Template is null || !DocumentPatterns.IsSafePath(request.Path)
            || !(Patterns.DocumentPath.IsMatch(request.Path) || request.Path == "README.md") || !ContentHash.IsValid(request.ExpectedContentHash)
            || !LivingDocumentType.Levels.Contains(request.Level))
            return AdoptionRefused("Name an author, exact template, expected content hash, due level and a path under design/ or docs/, or README.md.");
        lock (gate)
        {
            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is { } denied) return new(null, null, null, false, null, null, [], denied);
            try
            {
                string file = GeneratedDocumentVerification.SafeFile(Root, request.Path);
                byte[] original = File.ReadAllBytes(file);
                string hash = ContentHash.Sha256(original);
                if (hash != request.ExpectedContentHash) return AdoptionRefused("The document changed after inspection; read its bytes and hash again.");
                RecordEnvelope[] declarations = model.Current("living-document").Where(d => EstateModel.BodyString(d, "repository") is null
                    && GeneratedDocumentContract.Owns(d, request.Path)).ToArray();
                if (declarations.Length > 1) return AdoptionRefused("Resolve overlapping document declarations before adoption.");
                RecordEnvelope? previous = declarations.SingleOrDefault();
                if (previous?.ComputedHash != request.ExpectedDeclaration)
                    return AdoptionRefused("Supply the exact current declaration hash when adopting an already declared document.");
                if (previous is not null && (EstateModel.BodyString(previous, "kind") != "authored" || EstateModel.BodyString(previous, "source") != "estate-file"
                    || previous.Body.TryGetProperty("pathPattern", out _) || TemplateCatalog.Pin(previous) is not null))
                    return AdoptionRefused("Adoption attaches an untemplated authored estate document; existing instances use explicit template upgrades.");
                TemplateDefinition definition = TemplateCatalog.Resolve(model, new(request.Template.Id, request.Template.Version, request.Template.Hash));
                if (!TemplateCatalog.All(model).Any(d => d.Hash == definition.Hash)) return AdoptionRefused("The template version is retired for new instances.");
                var instance = new JsonObject
                {
                    ["id"] = definition.Id, ["version"] = definition.Version, ["hash"] = definition.Hash, ["owner"] = request.By,
                    ["options"] = JsonSerializer.SerializeToNode(TemplateInstances.Options(model, definition, request.Scope ?? default, request.Options ?? default)),
                    ["overrides"] = request.Overrides is { } overrides ? JsonNode.Parse(overrides.GetRawText()) : new JsonArray(),
                };
                if (request.Scope is { } scope) instance["scope"] = JsonNode.Parse(scope.GetRawText());
                TemplateDefinition effective = TemplateInstances.Effective(definition, JsonSerializer.SerializeToElement(instance));
                JsonObject body = previous is null ? new JsonObject
                {
                    ["path"] = request.Path, ["title"] = request.Title ?? Path.GetFileNameWithoutExtension(request.Path),
                    ["kind"] = "authored", ["source"] = "estate-file", ["clearedBy"] = "any",
                    ["triggers"] = new JsonArray(new JsonObject { ["on"] = "decision", ["level"] = request.Level }),
                    ["summary"] = "A retained document adopted as an instance of " + definition.Id + ".",
                } : JsonNode.Parse(previous.Body.GetRawText())!.AsObject();
                if (request.Title is not null) body["title"] = request.Title;
                body["template"] = instance;
                if (request.IncludeRegions)
                {
                    JsonArray regions = body["regions"] as JsonArray ?? new JsonArray();
                    foreach (JsonElement section in effective.Elements.Where(s => s.GetProperty("synthesis").GetProperty("mode").GetString() == "generated"))
                    {
                        string id = section.GetProperty("id").GetString()!;
                        JsonElement synthesis = section.GetProperty("synthesis");
                        var region = new JsonObject { ["id"] = id, ["generator"] = synthesis.GetProperty("generator").GetString() };
                        if (synthesis.GetProperty("inputs").GetArrayLength() > 0) region["inputs"] = JsonNode.Parse(synthesis.GetProperty("inputs").GetRawText());
                        JsonNode? existing = regions.SingleOrDefault(r => r?["id"]?.GetValue<string>() == id);
                        if (existing is not null && CanonicalJson.Serialize(JsonSerializer.SerializeToElement(existing)) != CanonicalJson.Serialize(JsonSerializer.SerializeToElement(region)))
                            return AdoptionRefused("A template region conflicts with an existing generator contract; review that migration separately.");
                        if (existing is null) regions.Add(region);
                    }
                    if (regions.Count > 0 && body["regions"] is null) body["regions"] = regions;
                }
                JsonElement proposedBody = JsonSerializer.SerializeToElement(body);
                // A preview is unsigned, unrecorded content. It is never handed to the record store.
                AuthoredRecord preview = RecordAuthor.Create(new("living-document", request.By, proposedBody, Supersedes: previous?.Id, Format: 3));
                bool migration = GeneratedDocumentContract.RequiresMigration(model, preview.Envelope);
                string? scaffold = request.Preview ? TemplateDocuments.NewContent(model, preview.Envelope) : null;
                (RecordDraft? pinDraft, string? prepared, Problem? preparation) = request.IncludeRegions
                    ? PrepareAdoptionContent(model, preview.Envelope, original, request.By) : (null, null, null);
                AuthoredRecord? provisionalPin = pinDraft is null ? null : RecordAuthor.Create(pinDraft);
                EstateModel projected = provisionalPin is null ? model : ProjectRevision(model, provisionalPin.Envelope);
                DocumentMigrationPreview? evidence = migration ? DocumentMigration.Prepare(projected, preview.Envelope,
                    introducedPins: provisionalPin is null ? null : new HashSet<string>(StringComparer.Ordinal) { provisionalPin.Envelope.Id }) : null;
                var response = new DocumentAdoptResponse(null, hash, proposedBody, migration,
                    evidence is null ? null : Convert.ToBase64String(evidence.Basis.AsSpan()),
                    evidence is null || evidence.Status != DocumentGenerationStatus.Success ? null : Convert.ToBase64String(evidence.Preview.AsSpan()),
                    evidence?.Pins.Select(p => new TemplateRecordReference(p.Type, p.Id, p.Hash!)).ToArray() ?? [])
                    { TemplateContent = scaffold, PreparedContent = prepared };
                if (request.Preview) return response with { Problem = preparation ?? (evidence is { Status: not DocumentGenerationStatus.Success }
                    ? new("incomplete", string.Join(" ", evidence.Problems), []) : null) };
                if (migration && (evidence is null || !DocumentMigration.IsReviewed(model, preview.Envelope, evidence)))
                    return response with { Problem = new("review-required", "Generated-region changes retain the existing requirement for an exact migration result and independent operator review. Inspect the adoption preview before applying it.", []) };
                if (preparation is not null) return response with { Problem = preparation };
                (AuthoredRecord? record, string? refusal) = AuthorLocked(new("living-document", request.By, proposedBody, Supersedes: previous?.Id), model);
                if (record is null) return response with { Problem = new("refused", refusal!, []) };
                AuthoredRecord? pin = null;
                if (pinDraft is not null)
                {
                    (pin, refusal) = AuthorLocked(pinDraft, model);
                    if (pin is null) return response with { Problem = new("refused", refusal!, []) };
                }
                AuthoredRecord[] batch = pin is null ? [record] : [pin, record];
                var stored = StoreTogetherLocked(batch, "Attach an exact template while retaining authored document bytes and history.", null, current =>
                {
                    RecordEnvelope? actual = current.Current("living-document").SingleOrDefault(d => EstateModel.BodyString(d, "repository") is null && GeneratedDocumentContract.Owns(d, request.Path));
                    if (actual?.ComputedHash != request.ExpectedDeclaration || ContentHash.Sha256(File.ReadAllBytes(file)) != hash
                        || (pinDraft is not null && current.Current("document-revision").SingleOrDefault(r => EstateModel.BodyString(r, "path") == request.Path)?.Id != pinDraft.Supersedes))
                        return new Problem("conflict", "The document, pin or declaration changed during preparation; no adoption was stored.", []);
                    if (pin is not null) session.Objects.Put(original);
                    return null;
                }, !migration && prepared is not null && prepared != new UTF8Encoding(false, true).GetString(original)
                    ? [new(request.Path, hash, [.. new UTF8Encoding(false, true).GetBytes(prepared)])] : null);
                return response with
                {
                    Declaration = stored.Written.SingleOrDefault(r => r.Id == record.Envelope.Id),
                    Pin = pin is null ? null : stored.Written.SingleOrDefault(r => r.Id == pin.Envelope.Id),
                    Problem = stored.Problem is not null && stored.Written.Count > 0
                        ? new("reconcile-required", "Part of the original adoption batch was stored. Inspect its receipts and reconcile before retrying.", stored.Problem.Findings) : stored.Problem,
                };
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
            {
                return AdoptionRefused("The existing file, template choices or declaration could not be safely prepared. Inspect the current bytes and exact contract before retrying.");
            }
        }
    }

    private static DocumentAdoptResponse AdoptionRefused(string message) => new(null, null, null, false, null, null, [], new("refused", message, []));

    private static (RecordDraft? Pin, string? Content, Problem? Problem) PrepareAdoptionContent(EstateModel model, RecordEnvelope declaration, byte[] original, string by)
    {
        try
        {
            string text = new UTF8Encoding(false, true).GetString(original);
            string content = TemplateDocuments.RefreshGenerated(model, declaration, text);
            if (content == text) return (null, content, null);
            string path = EstateModel.BodyString(declaration, "path")!;
            RecordEnvelope? previous = model.Current("document-revision").SingleOrDefault(r => EstateModel.BodyString(r, "path") == path);
            DateTimeOffset at = DateTimeOffset.UtcNow;
            if (previous is not null && previous.AtTime > at) at = previous.AtTime;
            var body = new JsonObject
            {
                ["path"] = path, ["title"] = EstateModel.BodyString(declaration, "title"), ["hash"] = ContentHash.Sha256(original),
                ["bytes"] = original.LongLength, ["mediaType"] = "text/markdown", ["revision"] = previous is null ? 1 : previous.Body.GetProperty("revision").GetInt64() + 1,
                ["summary"] = "Adopt the document's exact template and recorded generated regions.",
            };
            var draft = new RecordDraft("document-revision", by, JsonSerializer.SerializeToElement(body), Supersedes: previous?.Id, At: at, Id: Ulid.NewUlid(at).Value, Format: 3);
            content = TemplateDocuments.RefreshGenerated(ProjectRevision(model, RecordAuthor.Create(draft).Envelope), declaration, text);
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(content);
            if (EstateChecks.Content(path, bytes).HasErrors) return (null, null, new("refused", "The prepared document fails repository content checks.", []));
            body["hash"] = ContentHash.Sha256(bytes); body["bytes"] = bytes.LongLength;
            draft = draft with { Body = JsonSerializer.SerializeToElement(body) };
            if (TemplateDocuments.RefreshGenerated(ProjectRevision(model, RecordAuthor.Create(draft).Envelope), declaration, content) != content)
                return (null, null, new("refused", "The generated adoption contains an output-hash cycle.", []));
            return (draft, content, null);
        }
        catch (InvalidOperationException)
        {
            return (null, null, new("incomplete", "Place the template's generated markers explicitly; retain authored text and inspect the complete scaffold before adopting generated ownership.", []));
        }
    }
}
