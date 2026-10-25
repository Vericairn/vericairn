// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

internal static class DocumentationPass
{
    internal static DocumentPassResponse Build(EstateModel model, string version, string? product, bool prepareUpdates = true)
    {
        if (model.Findings.HasErrors)
            return new(version, null, null, null, null, [], [], [], [], new("rejected", "Resolve the estate's record errors before preparing a publication pass.", []));
        var publications = ReleaseContents.PublishedReleases(model).Where(p => EstateModel.BodyString(p.Release, "version") == version
            && (product is null || MatchesProduct(model, p.Release.Subject!.Id, product))).ToArray();
        if (publications.Length != 1)
            return new(version, null, null, null, null, [], [], [], [], new("refused", publications.Length == 0
                ? "No current published release matches this version and product."
                : "More than one product published this version; select a product by id or slug.", []));
        ReleaseContents.Published publication = publications[0];
        RecordEnvelope release = publication.Release;
        string commit = EstateModel.BodyString(release, "commit")!;
        string? previousCommit = publication.Previous is null ? null : EstateModel.BodyString(publication.Previous, "commit");
        RecordEnvelope[] records = [.. ReleaseContents.Results(model, publication.Window).Accepted.Select(a => a.Result)
            .Concat(ReleaseContents.CurrentRevisions(model, publication.Window, release.At).Where(r => r.Type is "decision" or "document-revision"))
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)];
        var documents = new List<DocumentPassItem>();
        var updates = new List<DocumentPassUpdate>();
        var comparisons = new Dictionary<(string Repository, string Source), DocumentSourceChanges>();
        // This is the same derivation used by docs living and the ordinary release gate. A different release's reason,
        // a content-only reason and an unavailable contribution alone do not belong to this publication's pass.
        foreach (LivingDocuments.State state in LivingDocuments.Derive(model).Where(s => s.Reasons.Any(r => r.On == "release" && r.Record == release.Id)))
        {
            RecordEnvelope declaration = state.Declaration;
            IReadOnlyList<string> paths = LivingDocuments.CoveredPaths(declaration);
            var notes = state.Notes.Concat(state.Unavailable.Select(u => "Unavailable contribution: " + u)).ToList();
            IReadOnlyList<DocumentPassChange> changes = [];
            if (state.Repository is { } repository)
            {
                string sourceName = EstateModel.BodyString(declaration, "source")!;
                if (!IsReleaseRepository(model, repository, release))
                    notes.Add("This document belongs to another repository; the publication commits do not describe its file changes.");
                else
                {
                    var key = (repository, sourceName);
                    if (!comparisons.TryGetValue(key, out DocumentSourceChanges? comparison))
                    {
                        comparison = publication.Previous is not null
                            && EstateModel.BodyString(publication.Previous, "repository") != EstateModel.BodyString(release, "repository")
                            ? new([], "The preceding publication used another repository; no file comparison was made.")
                            : DocumentSources.Find(sourceName)?.Changes(model, declaration, previousCommit, commit)
                                ?? new([], "The document source is unavailable; no publication comparison was made.");
                        comparisons.Add(key, comparison);
                    }
                    if (comparison.Problem is not null) notes.Add(comparison.Problem);
                    else changes = comparison.Files.Where(c => Covers(paths, c.Path)).Select(c => new DocumentPassChange(c.Path, c.Kind)).ToArray();
                }
            }
            else notes.Add("Estate document changes are identified from the publication's record window; product commits do not describe the estate's working tree.");

            DocumentPassRecord[] relevant = [.. records.Where(r => Touches(model, declaration, paths, r)).Select(Describe)];
            if (prepareUpdates && EstateModel.BodyString(declaration, "kind") == "authored"
                && (state.Repository is null || IsReleaseRepository(model, state.Repository, release)))
            {
                var prepared = DocumentFrontUpdates.Prepare(model, state, release);
                if (prepared.Update is not null) updates.Add(prepared.Update);
                if (prepared.Note is not null) notes.Add(prepared.Note);
            }
            documents.Add(new(Pin(declaration), state.Path, state.Repository, EstateModel.BodyString(declaration, "title")!, state.Level!, paths,
                state.Reasons.Select(r => new DocumentReason(r.On, r.Level, r.Summary, r.Record)).ToArray(), changes, relevant, notes,
                Assignments(model, declaration, release)));
        }
        return new(version, Pin(release), publication.Previous is null ? null : Pin(publication.Previous),
            LivingDocuments.ProductIdentity(model, release.Subject!.Id), commit, documents, records.Select(Describe).ToArray(), updates, []);
    }

    internal static DocumentPassPin Pin(RecordEnvelope record) => new(record.Id, record.ComputedHash);

    private static DocumentPassRecord Describe(RecordEnvelope record) => new(record.Type, Pin(record),
        EstateModel.BodyString(record, "summary") ?? EstateModel.BodyString(record, "title") ?? record.Id);

    private static bool MatchesProduct(EstateModel model, string id, string selection) =>
        LivingDocuments.ProductIdentity(model, id) == LivingDocuments.ProductIdentity(model, selection)
        || (model.ById.TryGetValue(id, out RecordEnvelope? product) && EstateModel.BodyString(model.Latest(product), "slug") == selection);

    private static bool IsReleaseRepository(EstateModel model, string repository, RecordEnvelope release) =>
        model.ById.TryGetValue(repository, out RecordEnvelope? resource) && model.Latest(resource).Body.TryGetProperty("location", out JsonElement location)
        && location.TryGetProperty("github", out JsonElement github)
        && string.Equals(github.GetString(), EstateModel.BodyString(release, "repository"), StringComparison.OrdinalIgnoreCase);

    private static bool Covers(IReadOnlyList<string> paths, string file) =>
        paths.Any(p => p == file || file.StartsWith(p.TrimEnd('/') + "/", StringComparison.Ordinal));

    private static bool Touches(EstateModel model, RecordEnvelope declaration, IReadOnlyList<string> paths, RecordEnvelope record)
    {
        if (record.Type == "document-revision") return EstateModel.BodyString(declaration, "repository") is null
            && Covers(paths, EstateModel.BodyString(record, "path") ?? "");
        if (record.Type == "decision" && record.Body.TryGetProperty("documents", out JsonElement documents))
            return documents.EnumerateArray().Any(d => d.ValueKind == JsonValueKind.String &&
                (Covers(paths, d.GetString()!) || model.Chain(declaration).Contains(d.GetString()!)));
        if (record.Type == "result" && EstateModel.BodyString(declaration, "repository") is null && record.Body.TryGetProperty("outputs", out JsonElement outputs))
            return outputs.EnumerateArray().Any(o => o.TryGetProperty("location", out JsonElement path) && path.ValueKind == JsonValueKind.String && Covers(paths, path.GetString()!));
        return false;
    }

    private static DocumentPassAssignment[] Assignments(EstateModel model, RecordEnvelope declaration, RecordEnvelope release) =>
        [.. model.Current("assignment").Where(a => a.Body.TryGetProperty("brief", out JsonElement brief)
                && brief.TryGetProperty("sources", out JsonElement sources) && Includes(sources, release) && Includes(sources, declaration))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal)
            .Select(a => new DocumentPassAssignment(a.Subject is not null && model.ById.TryGetValue(a.Subject.Id, out RecordEnvelope? order)
                ? EstateModel.BodyString(order, "key")! : "?", a.Id, EstateModel.BodyString(a, "contributor")!))];

    private static bool Includes(JsonElement sources, RecordEnvelope record) => sources.EnumerateArray().Any(s =>
        s.TryGetProperty("id", out JsonElement id) && id.GetString() == record.Id
        && s.TryGetProperty("hash", out JsonElement hash) && hash.GetString() == record.ComputedHash);
}
