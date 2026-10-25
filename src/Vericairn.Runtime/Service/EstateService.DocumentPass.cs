// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    private static readonly string[] PassDeliverables = ["A document review for each selected declaration and a result identifying any remaining changes."];
    private static readonly string[] PassPermissions = ["the estate: document-review records for the selected declarations, and the reviewer's own attempt and result"];
    private static readonly string[] PassProhibitions = ["No publication, push, merge or automatic commit.", "No change to frozen documentation sets or their checksum index."];
    private static readonly string[] PassWorkspace = ["The estate and the registered repositories named by the selected document declarations."];
    private static readonly string[] PassLimits = ["The declaration's checks and clearedBy requirement still apply.", "Proposed front-document patches require separate inspection and application."];
    private static readonly string[] PassOutputs = ["Document reviews with exact content pins, findings and declared checks.", "A result naming every remaining due document and why."];

    /// <inheritdoc />
    public DocumentPassResponse DocumentPass(string version, string? product = null)
    {
        lock (gate) return DocumentationPass.Build(session.Read(), version, product);
    }

    /// <inheritdoc />
    public DocumentPassAssignResponse DocumentPassAssign(string version, DocumentPassAssignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Release is null || !Ulid.IsValid(request.Release.Id) || !ContentHash.IsValid(request.Release.Hash)
            || request.Documents is null || request.Documents.Count == 0
            || request.Documents.Any(d => d is null || !Ulid.IsValid(d.Id) || !ContentHash.IsValid(d.Hash)) || request.Documents.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != request.Documents.Count
            || string.IsNullOrWhiteSpace(request.Key) || !Patterns.WorkKey.IsMatch(request.Key) || request.Key.Length > 40)
            return new([], new("refused", "Supply a new work-order key and distinct exact document pins from the publication pass.", []));
        DocumentPassPin[] selection = [.. request.Documents];
        lock (gate)
        {
            EstateModel model = session.Load();
            if (Check(model) is Problem refused) return new([], refused);
            RecordEnvelope release = model.ById[request.Release.Id];
            DocumentPassResponse pass = DocumentationPass.Build(model, version, release.Subject!.Id, prepareUpdates: false);
            DocumentPassItem[] selected = [.. pass.Documents.Where(d => selection.Contains(d.Declaration))];
            RecordReference[] sources = [.. new[] { release.Reference }
                .Concat(pass.Previous is null ? [] : new[] { model.ById[pass.Previous.Id].Reference })
                .Concat(selected.Select(d => model.ById[d.Declaration.Id].Reference))
                .Concat(pass.Records.Select(r => model.ById[r.Record.Id].Reference)).Distinct()];
            string title = "Review documentation after " + version;
            string[] scope = [.. selected.Select(d => $"Review {d.Path} (declaration {d.Declaration.Id}, repository {d.Repository ?? "estate"}).")];
            JsonElement orderBody = JsonSerializer.SerializeToElement(new
            {
                key = request.Key, kind = "ordinary", title, role = "reviewer",
                intent = $"Review the documents still due for publication {release.Id} at {pass.Commit}.", scope,
                exitQuestion = "Has each selected document been reviewed against the publication, with findings and required changes recorded?",
                inputs = sources, deliverables = PassDeliverables,
            }, ProtocolJson.Options);
            (AuthoredRecord? order, string? refusal) = AuthorLocked(new("work-order", request.By, orderBody), model);
            if (order is null) return new([], new("refused", refusal!, []));
            string[] context = [.. selected.SelectMany(d =>
                d.CoveredPaths.Select(p => $"{d.Path}: covers {p}.")
                    .Concat(d.Changes.Select(c => $"{d.Path}: {c.Kind} {c.Path} between the publication commits."))
                    .Concat(d.Records.Select(r => $"{d.Path}: {r.Type} {r.Record.Id}; read the pinned source for its complete change summary."))
                    .Concat(d.Notes.Select(n => $"{d.Path}: {n}")))];
            JsonElement assignmentBody = JsonSerializer.SerializeToElement(new
            {
                contributor = request.Reviewer,
                brief = new
                {
                    outcome = title + "; report current, revised or needs-change for every selected declaration. Assignment does not clear a document.",
                    sources, permittedChanges = PassPermissions,
                    prohibitedEffects = PassProhibitions,
                    workspace = PassWorkspace,
                    limits = PassLimits,
                    expectedOutputs = PassOutputs,
                    reviewCriteria = context.Prepend("Every selected document has a review; failed checks and unmet independence requirements remain due.").ToArray(),
                    recovery = "Read this work order and its assignment, then run docs pass for the same publication to inspect the remaining due documents before continuing.",
                },
                note = "Documentation pass for publication " + release.Id,
            }, ProtocolJson.Options);
            (AuthoredRecord? assignment, refusal) = AuthorLocked(new("assignment", request.By, assignmentBody, order.Envelope.Reference), model);
            if (assignment is null) return new([], new("refused", refusal!, []));
            var stored = StoreTogetherLocked([order, assignment], "Assign publication documentation for review.", order.Envelope.Id, Check);
            return new(stored.Written, stored.Written.Count > 0 && stored.Problem is not null
                ? stored.Problem with { Code = "reconcile-required", Message = "The documentation assignment batch did not finish. Inspect the returned receipts and original batch before retrying." }
                : stored.Problem);

            Problem? Check(EstateModel current)
            {
                if (RequestDeviceRefusal(current) is Problem denied) return denied;
                if (!current.HasRole(request.By, Roles.Operator) && !current.HasScope(request.By, Scopes.LocalEngineering, DateTimeOffset.UtcNow))
                    return new("denied", "Documentation is assigned by the operator or a principal with local-engineering authority.", []);
                if (current.CurrentPrincipal(request.Reviewer) is null)
                    return new("refused", "The reviewer must be an admitted principal.", []);
                if (current.Current("work-order").Any(w => EstateModel.BodyString(w, "key") == request.Key))
                    return new("conflict", "This work-order key already exists; inspect its original assignment before retrying.", []);
                if (!current.ById.TryGetValue(request.Release.Id, out RecordEnvelope? publication) || publication.Type != "release"
                    || publication.ComputedHash != request.Release.Hash || !current.IsCurrent(publication) || EstateModel.BodyString(publication, "status") != "published")
                    return new("conflict", "The exact displayed publication is no longer current.", []);
                DocumentPassResponse actual = DocumentationPass.Build(current, version, publication.Subject!.Id, prepareUpdates: false);
                if (!actual.Ok || actual.Release != request.Release || selection.Any(pin => !actual.Documents.Any(d => d.Declaration == pin)))
                    return new("conflict", "A selected document changed or is no longer due for this publication. Read the pass again; nothing was assigned.", []);
                return null;
            }
        }
    }
}
