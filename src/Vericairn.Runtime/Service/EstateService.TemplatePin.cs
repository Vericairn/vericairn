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

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    private DocumentPinResponse PinTemplate(DocumentPinRequest request, EstateModel model, RecordDraft draft, long revision)
    {
        if (RequestDeviceRefusal(model) is { } denied) return new(null, null, denied);
        RecordEnvelope? declaration = TemplateDocument(model, request.Path);
        if (declaration is null || EstateModel.BodyString(declaration, "repository") is not null)
            return new(null, null, new("refused", "Template rendering requires an existing estate template instance.", []));
        try
        {
            TemplateDefinition effective = TemplateInstances.Effective(TemplateCatalog.Resolve(model, TemplateCatalog.Pin(declaration)!), declaration);
            var regions = DocumentChecks.Regions(declaration);
            foreach (JsonElement section in effective.Elements.Where(s => s.GetProperty("synthesis").GetProperty("mode").GetString() == "generated"))
            {
                JsonElement synthesis = section.GetProperty("synthesis");
                if (!regions.Any(r => r.Id == section.GetProperty("id").GetString() && r.Generator == synthesis.GetProperty("generator").GetString()
                    && r.Inputs.SequenceEqual(synthesis.GetProperty("inputs").EnumerateArray().Select(i => i.GetString()!), StringComparer.Ordinal)))
                    return new(null, null, new("review-required", "Declare the template's exact generated regions through the existing ownership migration review before rendering them.", []));
            }
            string file = GeneratedDocumentVerification.SafeFile(Root, request.Path);
            byte[] before = File.ReadAllBytes(file);
            string beforeHash = ContentHash.Sha256(before);
            if (beforeHash != draft.Body.GetProperty("hash").GetString())
                return new(null, null, new("conflict", "The document changed during pin preparation.", []));
            DateTimeOffset at = DateTimeOffset.UtcNow;
            if (draft.Supersedes is { } previous && model.ById[previous].AtTime > at) at = model.ById[previous].AtTime;
            draft = draft with { At = at, Id = Ulid.NewUlid(at).Value, Format = 3 };
            AuthoredRecord provisional = RecordAuthor.Create(draft);
            // This unsigned, in-memory projection supplies the new revision's metadata without a self-referential content hash.
            // No source bytes or prior records are changed to prepare it.
            EstateModel projection = ProjectRevision(model, provisional.Envelope);
            string next = TemplateDocuments.RefreshGenerated(projection, declaration, new UTF8Encoding(false, true).GetString(before));
            byte[] after = new UTF8Encoding(false, true).GetBytes(next);
            if (EstateChecks.Content(request.Path, after).HasErrors) return new(null, null, new("refused", "The prepared document fails repository content checks.", []));
            JsonObject body = JsonNode.Parse(draft.Body.GetRawText())!.AsObject();
            body["hash"] = ContentHash.Sha256(after); body["bytes"] = after.LongLength;
            (AuthoredRecord? pin, string? refusal) = AuthorLocked(draft with { Body = JsonSerializer.SerializeToElement(body) }, model);
            if (pin is null) return new(null, null, new("refused", refusal!, []));
            if (TemplateDocuments.RefreshGenerated(LoadWith([pin]), declaration, next) != next)
                return new(null, null, new("refused", "The generated content depends on its own output hash or another unstable source; no pin or output was stored.", []));
            var stored = StoreTogetherLocked([pin], "Refresh template regions and pin the resulting document revision.", null, current =>
                current.Current("document-revision").SingleOrDefault(r => EstateModel.BodyString(r, "path") == request.Path)?.Id != draft.Supersedes
                || current.Latest(declaration).ComputedHash != declaration.ComputedHash || ContentHash.Sha256(File.ReadAllBytes(file)) != beforeHash
                    ? new("conflict", "The document, current pin or template declaration changed during preparation.", []) : null,
                before.AsSpan().SequenceEqual(after) ? [] : [new(request.Path, beforeHash, [.. after])]);
            return new(stored.Written.SingleOrDefault(), stored.Written.Count == 0 ? null : revision, stored.Problem);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        {
            return new(null, null, new("refused", "The complete template output could not be prepared from its existing markers and sources; no pin was stored.", []));
        }
    }

    private static EstateModel ProjectRevision(EstateModel model, RecordEnvelope revision)
    {
        RecordEnvelope[] records = [.. model.Records, revision];
        var superseded = new Dictionary<string, string>(model.SupersededBy, StringComparer.Ordinal);
        if (revision.Supersedes is { } predecessor) superseded[predecessor] = revision.Id;
        return new(model.Root, records, records.ToDictionary(r => r.Id, StringComparer.Ordinal), superseded, model.Findings) { Manifest = model.Manifest };
    }
}
