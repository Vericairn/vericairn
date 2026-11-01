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
    private DocumentPinResponse PinRevisionLines(DocumentPinRequest request, EstateModel model, RecordDraft draft)
    {
        if (RequestDeviceRefusal(model) is { } denied) return new(null, null, denied);
        try
        {
            string file = GeneratedDocumentVerification.SafeFile(Root, request.Path);
            byte[] before = File.ReadAllBytes(file);
            string beforeHash = ContentHash.Sha256(before);
            if (request.ConvertRevisions && draft.Supersedes is { } previous && EstateModel.BodyString(model.ById[previous], "hash") != beforeHash)
                return new(null, null, new("conflict", "The whole-file pin no longer matches the document being converted.", []));
            byte[] prepared = request.ConvertRevisions ? RevisionLines.Convert(model, request.Path, before).Content : before;
            if (ContentHash.Sha256(RevisionLines.AuthoredContent(prepared)) != draft.Body.GetProperty("hash").GetString())
                return new(null, null, new("conflict", "The document changed during pin preparation.", []));
            IReadOnlyList<RecordEnvelope> history = RevisionLines.History(model, request.Path);
            draft = StampRevision(draft, history);
            RecordEnvelope? declaration = request.RenderTemplate ? TemplateDocument(model, request.Path) : null;
            if (declaration is not null && TemplateRenderingRefusal(model, declaration) is { } ownership)
                return new(null, null, ownership);
            byte[] Render(EstateModel basis, byte[] input)
            {
                byte[] next = RevisionLines.Refresh(basis, request.Path, input);
                if (declaration is not null)
                    next = new UTF8Encoding(false, true).GetBytes(TemplateDocuments.RefreshGenerated(basis, declaration, new UTF8Encoding(false, true).GetString(next)));
                return next;
            }
            byte[] after = Render(ProjectRevision(model, RecordAuthor.Create(draft).Envelope), prepared);
            if (BlockedTerms.Introduced(BlockedTerms.In(model, draft.At!.Value), Encoding.UTF8.GetString(before), Encoding.UTF8.GetString(after)) is string blocked)
                return new(null, null, new("refused", BlockedTerms.Refusal("The refreshed revision history of " + request.Path, blocked), []));
            if (EstateChecks.Content(request.Path, after).HasErrors)
                return new(null, null, new("refused", "The prepared document fails repository content checks.", []));
            JsonObject body = JsonNode.Parse(draft.Body.GetRawText())!.AsObject();
            byte[] authored = RevisionLines.AuthoredContent(after);
            body["hash"] = ContentHash.Sha256(authored); body["bytes"] = authored.LongLength;
            (AuthoredRecord? pin, string? refusal) = AuthorLocked(draft with { Body = JsonSerializer.SerializeToElement(body) }, model);
            if (pin is null) return new(null, null, new("refused", refusal!, []));
            EstateModel projected = LoadWith([pin]);
            if (!after.AsSpan().SequenceEqual(Render(projected, after)))
                return new(null, null, new("refused", "The generated content depends on its own output hash or another unstable source; no pin or output was stored.", []));
            var stored = StoreTogetherLocked([pin], "Regenerate the document revision history and pin its authored content.", null, current =>
                !RevisionLines.History(current, request.Path).Select(r => r.ComputedHash).SequenceEqual(history.Select(r => r.ComputedHash), StringComparer.Ordinal)
                || declaration is not null && current.Latest(declaration).ComputedHash != declaration.ComputedHash
                || ContentHash.Sha256(File.ReadAllBytes(file)) != beforeHash
                    ? new("conflict", "The document, revision history or template declaration changed during preparation.", []) : null,
                before.AsSpan().SequenceEqual(after) ? [] : [new(request.Path, beforeHash, [.. after])]);
            return new(stored.Written.SingleOrDefault(), stored.Written.Count == 0 ? null : RevisionLines.Ordinal(projected, pin.Envelope), stored.Problem);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        {
            return new(null, null, new("refused", "The complete revision history could not be prepared from its markers and records; no pin was stored.", []));
        }
    }

    private static RecordDraft StampRevision(RecordDraft draft, IReadOnlyList<RecordEnvelope> history)
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        if (history.Count > 0 && history[^1].AtTime > at) at = history[^1].AtTime;
        DateTimeOffset identityAt = at;
        if (history.Count > 0 && Ulid.Parse(history[^1].Id).Timestamp >= identityAt)
            identityAt = Ulid.Parse(history[^1].Id).Timestamp.AddMilliseconds(1);
        // Ties are ordered by identity. Advancing identity need not invent a future recorded date.
        return draft with { At = at, Id = Ulid.NewUlid(identityAt).Value, Format = 3 };
    }
}
