// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Reading;

/// <summary>Selects only the authorized Internal record/source basis before invoking a registered pure renderer.</summary>
internal static class CompanionReader
{
    private static readonly CompanionCoverageRow[] ExistingReaders =
    [
        new("view/work-brief", "Read the measured brief with its exact originals, derivatives and withheld list.", null, null, "BriefResponse", "Internal", "on-demand", null, "BriefContext"),
        new("view/context", "Read the measured context and retrieve pinned originals without inferring token fit.", null, null, "ContextResponse", "Internal", "on-demand", null, "Context"),
        new("view/handoff", "Read the retained handoff basis, its exclusions and measurement limits.", null, null, "HandoffResponse", "Internal", "on-demand", null, "Handoff"),
        new("view/communication", "Read effective settings with their contributing revisions, conflicts and freshness.", null, null, "CommunicationProfile", "Internal", "on-demand", null, "Communication"),
    ];

    internal static CompanionsResponse Coverage(EstateModel model, byte[] manifest) => new(1, "success", DocumentOnDemand.RecordSnapshot(model),
        DocumentCoverage.All.Select(d => new CompanionCoverageRow(d.Kind, d.ReaderTask, d.Item?.Generator, d.List?.Generator,
            d.InputSchema, d.Audience, d.Destination, d.NoReaderReason)).Concat(ExistingReaders).OrderBy(d => d.Kind, StringComparer.Ordinal).ToArray(),
        DocumentGenerators.All.Where(g => g.Descriptor is not null).Select(g => Describe(g.Descriptor!)).ToArray(),
        model.Current("living-document").Where(d => DocumentChecks.Generator(d) is not null)
            .OrderBy(d => d.Id, StringComparer.Ordinal).Select(d => new CompanionDestination(d.Id, d.ComputedHash,
                DocumentChecks.Generator(d)!.Value.Id, DocumentPatterns.Destination(d)!, EstateModel.BodyString(d, "repository"))).ToArray(), null,
        DocumentOnDemand.ManifestSnapshot(model, manifest));

    internal static CompanionGeneratorRow Describe(DocumentGeneratorDescriptor descriptor) => new(descriptor.Id, descriptor.ContractVersion,
        descriptor.Implementation, descriptor.ImplementationHash, descriptor.Selections,
        descriptor.Inputs.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value, StringComparer.Ordinal),
        descriptor.Audience, descriptor.RequiredPattern, descriptor.MaxMemberBytes, descriptor.SourceKinds.Contains("local-inspection"));

    internal static CompanionResponse Read(EstateModel model, CompanionRequest request, bool render, Func<byte[]> readManifest)
    {
        if (request.Audience != "Internal") return CompanionResponse.Unavailable("The requested reader scope is unavailable.");
        if (request.Page < 1 || string.IsNullOrWhiteSpace(request.Generator)) return Failure("error", "The reader selection is invalid.");
        string snapshot = DocumentOnDemand.RecordSnapshot(model);
        if (request.Generator != "vericairn.estate/manifest" && request.Snapshot is not null && request.Snapshot != snapshot)
            return CompanionResponse.Unavailable("The requested snapshot is unavailable; refresh coverage and select again.");
        IDocumentGenerator? generator = DocumentGenerators.Find(request.Generator);
        if (generator?.Descriptor is not { } descriptor || descriptor.ContractVersion != request.Contract)
            return CompanionResponse.Unavailable("The requested generator contract is unavailable; inspect the authoritative source.");
        if (descriptor.Audience != "Internal" || descriptor.SourceKinds.Contains("local-inspection"))
            return CompanionResponse.Unavailable("The requested reader scope is unavailable.");
        try
        {
            DocumentGenerationContext context;
            string currentness;
            string? notice = null;
            if (request.Declaration is not null)
            {
                if (request.Selection != "item" || request.Revision is not null || request.Field != "body" || request.Index != "all" || request.Page != 1
                    || !model.ById.TryGetValue(request.Declaration, out RecordEnvelope? declaration) || declaration.Type != "living-document"
                    || !model.IsCurrent(declaration) || GeneratedDocumentContract.IsRetired(model, declaration)
                    || DocumentChecks.Generator(declaration) is not { } declared || declared.Id != descriptor.Id
                    || EstateModel.BodyString(declaration, "pathPattern") is not null)
                    return CompanionResponse.Unavailable("The exact declared item selection is unavailable.");
                string? sourceId = EstateModel.BodyString(declaration, "source");
                IDocumentSource? adapter = DocumentSources.Find(sourceId);
                if (sourceId != LivingDocumentType.EstateFile && (adapter?.SnapshotVersion != DocumentGeneration.InterfaceVersion || !ExactCommit(request.Commit)))
                    return CompanionResponse.Unavailable("The exact source commit or adapter is unavailable.");
                DocumentSourceSelection sources = sourceId == LivingDocumentType.EstateFile
                    ? DocumentSourceSelection.Records(model, descriptor)
                    : adapter!.ReadSelection(model, declaration, request.Commit!, descriptor);
                if (request.Commit is not null && sources.Commit != request.Commit)
                    return CompanionResponse.Unavailable("The source did not supply the requested exact commit.");
                DocumentMemberSelection members = DocumentGeneration.Members(declaration, sources);
                if (members.Status != DocumentGenerationStatus.Success || members.Members.Length != 1)
                    return CompanionResponse.Unavailable("The complete declared source selection is unavailable.");
                var graph = DocumentSourceGraph.Check(model, declaration, sources.Sources, request.Commit);
                if (graph.Status != DocumentGenerationStatus.Success) return CompanionResponse.Unavailable("The declared source dependency basis is unavailable.");
                context = members.Members[0].Context;
                currentness = "historical";
            }
            else if (descriptor.Id == "vericairn.estate/manifest")
            {
                if (request.Selection is not ("item" or "list") || request.Revision is not null || request.Snapshot is null
                    || request.Field != "body" || request.Index != "all" || request.Commit is not null)
                    return Failure("error", "The manifest requires its explicit snapshot and an item or list selection.");
                var selected = DocumentOnDemand.ManifestContext(model, readManifest(), request.Snapshot, request.Page);
                if (selected.Context is null) return CompanionResponse.Unavailable("The exact manifest basis is unavailable.");
                context = selected.Context;
                snapshot = request.Snapshot;
                currentness = "current";
            }
            else
            {
                if (request.Commit is not null || descriptor.SourceKinds.Any(k => k != "record"))
                    return CompanionResponse.Unavailable("This reader requires a declared, retained source selection.");
                DocumentSourceSelection sources = DocumentSourceSelection.Records(model, descriptor);
                if (sources.Status != DocumentGenerationStatus.Success) return CompanionResponse.Unavailable("The complete record basis is unavailable.");
                var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["format"] = request.Contract.ToString(CultureInfo.InvariantCulture), ["audience"] = "Internal",
                };
                string mode;
                if (request.Revision is not null)
                {
                    if (request.Selection is not ("item" or "list") || !model.ById.TryGetValue(request.Revision, out RecordEnvelope? record)
                        || !descriptor.RecordTypes.Contains(record.Type)) return CompanionResponse.Unavailable("The exact revision selection is unavailable.");
                    sources = sources with { Sources = [DocumentSourceSnapshot.Record(record)] };
                    currentness = model.IsCurrent(record) ? "current" : "historical";
                    var subjectFinding = model.Findings.FirstOrDefault(f => f.Location == record.ExpectedPath
                        && f.Code is "record.subject-stale" or "record.subject-withdrawn");
                    if (subjectFinding is not null)
                    {
                        currentness = "historical";
                        notice = subjectFinding.Message;
                    }
                    mode = request.Selection;
                    if (descriptor.RequiredPattern is not null)
                    {
                        if (request.Selection != "item" || request.Field != "body" || request.Index != "all" || request.Page != 1)
                            return Failure("error", "Register items select one complete revision; use the record item reader for pages and nested fields.");
                        parameters["selection"] = "all-revisions";
                        if (record.Supersedes is { } predecessor)
                        {
                            if (!model.ById.TryGetValue(predecessor, out RecordEnvelope? prior) || prior.Type != record.Type)
                                return CompanionResponse.Unavailable("The exact predecessor basis is unavailable.");
                            sources = sources with { Sources = [.. sources.Sources, DocumentSourceSnapshot.Record(prior)] };
                        }
                    }
                }
                else
                {
                    if (request.Snapshot is null || request.Selection is not ("current" or "all-revisions") || request.Field != "body" || request.Index != "all")
                        return Failure("error", "A collection requires an explicit snapshot and current or all-revisions selection.");
                    if (request.Selection == "current") sources = sources with { Sources = [.. sources.Sources.Where(s => model.IsCurrent(model.ById[s.Identity]))] };
                    currentness = request.Selection == "current" ? "current" : "historical";
                    if (descriptor.Id == "vericairn.records/items" && request.Selection == "current")
                        notice = "The runtime selected current lineage revisions at this exact snapshot. The generator restates their immutable fields; it makes no live status judgment.";
                    mode = "list";
                    if (descriptor.RequiredPattern is not null) parameters["selection"] = request.Selection;
                }
                if (descriptor.RequiredPattern is null)
                {
                    if (descriptor.Inputs.ContainsKey("selection")) parameters["selection"] = mode;
                    if (descriptor.Inputs.ContainsKey("field")) parameters["field"] = request.Field;
                    else if (request.Field != "body") return Failure("error", "This reader does not select nested fields.");
                    if (descriptor.Inputs.ContainsKey("index")) parameters["index"] = request.Index;
                    else if (request.Index != "all") return Failure("error", "This reader does not select nested members.");
                    if (descriptor.Inputs.ContainsKey("page")) parameters["page"] = request.Page.ToString(CultureInfo.InvariantCulture);
                }
                context = new(DocumentGeneration.InterfaceVersion, descriptor, parameters.ToImmutableDictionary(StringComparer.Ordinal), mode, null, sources.Sources)
                { SnapshotIdentity = request.Revision is null ? snapshot : null, Page = request.Page };
            }
            if (DocumentGeneration.ValidateSources(descriptor, context.Sources) is not null)
                return CompanionResponse.Unavailable("The selected source basis could not be verified.");
            if (context.Parameters.Any(p => p.Key != "format" && (!descriptor.Inputs.TryGetValue(p.Key, out var allowed) || !DocumentInputs.Accepts(allowed, p.Value)))
                || descriptor.Inputs.Keys.Any(k => !context.Parameters.ContainsKey(k)) || !descriptor.Selections.Contains(context.Selection))
                return Failure("error", "The selection does not satisfy the registered input contract.");
            CompanionSource[] basis = context.Sources.Select(s => new CompanionSource(s.Kind, s.Identity, s.Hash, s.Path, s.Repository,
                s.RecordType, s.At, s.Kind == "record" ? model.IsCurrent(model.ById[s.Identity]) : null, s.Kind == "record" ? null : s.Bytes.Length)).ToArray();
            DocumentRendition? rendition = render ? DocumentRendering.Render(generator, context) : null;
            if (rendition is not null && rendition.Status != DocumentGenerationStatus.Success)
                return new(1, Status(rendition.Status), "unavailable", "unavailable", "Internal", snapshot, request.Commit, Describe(descriptor), basis, null, null, null, rendition.Reason);
            string completeness = descriptor.RequiredPattern is not null && request.Revision is not null ? "bounded-overview" : "selected-page";
            return new(1, "success", currentness, render ? completeness : "source-basis", "Internal", snapshot, request.Commit, Describe(descriptor), basis,
                context.InputDigest, rendition?.ContentHash, rendition is null ? null : Encoding.UTF8.GetString(rendition.Bytes.AsSpan()), notice);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return CompanionResponse.Unavailable("The complete permitted source basis is unavailable.");
        }
    }

    internal static CompanionComparison Compare(EstateModel model, CompanionCompareRequest request)
    {
        if (request.Audience != "Internal") return UnavailableComparison();
        RecordEnvelope[] declarations = model.Current("living-document").Where(d => d.Id == request.Selection
            || DocumentPatterns.Destination(d) == request.Selection || GeneratedDocumentContract.RetirementPath(d) == request.Selection
            || GeneratedDocumentContract.Owns(d, request.Selection)).ToArray();
        if (declarations.Length != 1) return new(1, "error", "unavailable", null, null, [], ["Select one declared document or register."]);
        RecordEnvelope declaration = declarations[0];
        string snapshot = DocumentOnDemand.RecordSnapshot(model);
        if (EstateModel.BodyString(declaration, "repository") is not null && !ExactCommit(request.Commit)) return UnavailableComparison();
        if (GeneratedDocumentContract.IsStrict(model, declaration))
        {
            GeneratedDocumentPlan plan = GeneratedDocumentVerification.Plan(model, declaration, request.Commit);
            return new(1, Status(plan.Status), plan.Status != DocumentGenerationStatus.Success ? "unavailable" : plan.Current ? "current" : "different",
                snapshot, plan.Commit, plan.Status != DocumentGenerationStatus.Success ? [] : DocumentRepair.Changes(plan.Actual, plan.Expected)
                    .Select(c => new CompanionDifference(c.Path, c.BeforeHash is null ? "missing" : c.After is null ? "unexpected" : "changed", c.BeforeHash, c.AfterHash)).ToArray(), plan.Problems);
        }
        if (request.Commit is not null || EstateModel.BodyString(declaration, "repository") is not null) return UnavailableComparison();
        DocumentChangePlan repair = DocumentRepair.Prepare(model, declaration);
        return new(1, Status(repair.Status), repair.Status != DocumentGenerationStatus.Success ? "unavailable" : repair.Changes.IsEmpty ? "current" : "different",
            snapshot, null, repair.Changes.Select(c => new CompanionDifference(c.Path, c.BeforeHash is null ? "missing" : "changed", c.BeforeHash, c.AfterHash)).ToArray(), repair.Problems);
    }

    internal static CompanionComparison UnavailableComparison() => new(1, "unavailable", "unavailable", null, null, [], ["The complete permitted comparison basis is unavailable."]);
    private static bool ExactCommit(string? commit) => commit is { Length: 40 or 64 } && commit.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Status(DocumentGenerationStatus status) => status.ToString().ToLowerInvariant();
    private static CompanionResponse Failure(string status, string reason) => CompanionResponse.Unavailable(reason) with { Status = status };
}
