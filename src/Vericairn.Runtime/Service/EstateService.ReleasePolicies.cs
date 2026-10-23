// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Service;

internal sealed record ReleasePolicyAssessment(PolicyBasis Basis, PolicyEvaluation Evaluation, PolicyResponse View, IReadOnlyList<FindingDto> Objections)
{
    internal bool HasSameInputsAndFindings(ReleasePolicyAssessment other, IReadOnlyList<RecordEnvelope>? ownRevisions = null)
    {
        // Reuse the full basis digest contract at one instant for comparison only. Retained evidence
        // keeps its actual timestamp and digest; neither evaluation is rerun at an earlier time.
        EstateModel model = other.Basis.Model;
        if (ownRevisions is { Count: > 0 })
        {
            // Excluding a retry's new record also excludes its own link from the canceled intent.
            // Match both ends: another successor of that intent, or any other revision, still counts.
            var successors = model.SupersededBy.Where(pair => !ownRevisions.Any(revision =>
                revision.Type == "operation" && revision.Supersedes == pair.Key && revision.Id == pair.Value))
                .ToImmutableDictionary(StringComparer.Ordinal);
            model = new EstateModel(model.Root, model.Records, model.ById, successors, model.Findings) { Manifest = model.Manifest };
        }
        var comparable = new PolicyBasis(model, Basis.AsOf, other.Basis.Origin,
            other.Basis.Repositories, other.Basis.Objects, other.Basis.Implementations, other.Basis.Submission, other.Basis.Complete);
        return Basis.Digest == comparable.Digest && AdmissionState(Evaluation).Canonical() == AdmissionState(other.Evaluation).Canonical();
    }

    // Finding prose and elapsed counters describe an evaluation; they do not decide admission.
    // Keep identities, verdicts, enforcement, anchors, exact evidence and duty state, including
    // occurrence, due time, deadline, completion and missed state. Retained evaluations stay intact.
    private static PolicyEvaluation AdmissionState(PolicyEvaluation evaluation) => evaluation with
    {
        Basis = "",
        AsOf = null,
        Findings = evaluation.Findings.Select(finding => finding with
        {
            Basis = "",
            AsOf = null,
            Actual = "",
            Required = "",
            NextAction = "",
            Duty = finding.Duty is { } duty ? duty with { LateSeconds = 0 } : null,
        }).ToImmutableArray(),
    };
}

public sealed partial class EstateService
{
    private const string CopiedBasisFormat = "vericairn.release-basis/1";
    private const string ReferencedBasisFormat = "vericairn.release-basis/2";

    /// <inheritdoc />
    public ReleaseBasisResponse? ReleaseBasis(string hash)
    {
        if (!Vericairn.Records.Hashing.ContentHash.IsValid(hash)) return null;
        byte[]? bytes = ObjectGet(hash);
        if (bytes is null) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            string? format = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("format", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (format == ReferencedBasisFormat) return ResolveReleaseBasis(hash, bytes);
            ReleaseBasisEvidence? evidence = JsonSerializer.Deserialize<ReleaseBasisEvidence>(bytes, ProtocolJson.Options);
            return evidence is { Format: CopiedBasisFormat, Basis: not null, Policies: not null,
                Records: not null, Repositories: not null, Objects: not null, Documents: not null, ExcludedOperationRevisions: not null }
                ? new(hash, evidence) : null;
        }
        catch (JsonException) { return null; }
    }

    // A referenced basis resolves to the same evidence a copied one carries: every record from the estate's own records, and every
    // source file and object from the object store, each checked against the hash the basis names. Anything that no longer resolves
    // exactly makes the basis unavailable rather than partly answered.
    private ReleaseBasisResponse? ResolveReleaseBasis(string hash, byte[] bytes)
    {
        StoredReleaseBasis? stored = JsonSerializer.Deserialize<StoredReleaseBasis>(bytes, ProtocolJson.Options);
        if (stored is not { Format: ReferencedBasisFormat, Basis: not null, Policies: not null, Records: not null, Repositories: not null,
            Objects: not null, Documents: not null, ExcludedOperationRevisions: not null }) return null;
        EstateModel model;
        lock (gate) model = session.Load();
        List<RecordEnvelope> resolved = [];
        foreach (StoredRecord reference in stored.Records)
        {
            if (!model.ById.TryGetValue(reference.Id, out RecordEnvelope? r) || r.Type != reference.Type || r.ComputedHash != reference.Hash) return null;
            resolved.Add(new RecordEnvelope(r.Type, r.Id, r.At, r.By, r.Subject, r.Supersedes, r.Body.Clone(), r.DeclaredHash, r.ComputedHash,
                r.Signatures.ToImmutableArray(), r.Format));
        }
        // A record's hash leaves out its signatures, which the stored record digest covers: the records read back must be exactly
        // those the gate judged, signatures included.
        if (PolicyBasis.DigestOf(resolved) != stored.Basis.RecordDigest) return null;
        List<JsonElement> records = resolved.Select(r => JsonSerializer.SerializeToElement(r, ProtocolJson.Options)).ToList();
        List<ReleaseBasisRepository> repositories = [];
        foreach (StoredRepository repository in stored.Repositories)
        {
            List<ReleaseBasisFile> files = [];
            foreach (StoredFile file in repository.Files)
            {
                byte[]? content = null;
                if (file.Available && (content = Retained(file.Hash)) is null) return null;
                files.Add(new(file.Path, file.Hash, content ?? [], file.Available, file.IsLink));
            }
            repositories.Add(new(repository.Identity, repository.Revision, repository.Contract, repository.Complete, files, repository.Evidence)
            { TreeComparisons = repository.TreeComparisons ?? [] });
        }
        List<ReleaseBasisObject> objects = [];
        foreach (StoredObject retained in stored.Objects)
        {
            byte[]? content = null;
            if (retained.Available && (content = Retained(retained.Hash)) is null) return null;
            objects.Add(new(retained.Hash, content ?? [], retained.Available));
        }
        return new(hash, new(stored.Format, stored.Basis, records, repositories, objects, stored.Policies, stored.Documents, stored.ExcludedOperationRevisions));

        byte[]? Retained(string objectHash)
        {
            try { return Vericairn.Records.Hashing.ContentHash.IsValid(objectHash) ? session.Objects.Read(objectHash) : null; }
            catch (Vericairn.Runtime.Objects.ObjectCorruptedException) { return null; }
        }
    }

    private sealed record StoredReleaseBasis(string Format, PolicyBasisDto Basis, IReadOnlyList<StoredRecord> Records,
        IReadOnlyList<StoredRepository> Repositories, IReadOnlyList<StoredObject> Objects, PolicyResponse Policies,
        IReadOnlyList<string> Documents, IReadOnlyList<PolicyRecordReference> ExcludedOperationRevisions);

    private sealed record StoredRecord(string Type, string Id, string Hash);

    private sealed record StoredRepository(string Identity, string Revision, string Contract, bool Complete, IReadOnlyList<StoredFile> Files, bool Evidence,
        IReadOnlyList<PolicyTreeComparisonDto>? TreeComparisons);

    private sealed record StoredFile(string Path, string Hash, bool Available, bool IsLink);

    private sealed record StoredObject(string Hash, bool Available);

    // Tests can change the clock or sources at an actual boundary; no production caller supplies an evaluation verdict.
    internal Action<string>? ReleaseBoundary { get; set; }

    private static PolicySubmission SelectRelease(EstateModel model, string product, string version, string? commit, IReadOnlySet<string> repositories, RecordReference? release = null)
    {
        string Identity(string id) => model.ById.TryGetValue(id, out RecordEnvelope? record) ? PolicyWork.Identity(model, record) : id;
        string[] selectedRepositories = repositories.Select(Identity).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] work = model.Current("work-order").Where(order =>
        {
            JsonElement plan = PolicyInputs.Get(order.Body, "releasePlan"), placement = PolicyInputs.Get(order.Body, "placement");
            return PolicyInputs.Text(plan, "product") == Identity(product) && PolicyInputs.Text(plan, "version") == version ||
                PolicyInputs.Text(placement, "kind") == "release" && PolicyInputs.Text(placement, "product") == Identity(product) && PolicyInputs.Text(placement, "version") == version;
        }).Select(order => PolicyWork.Identity(model, order)).Order(StringComparer.Ordinal).ToArray();
        RecordEnvelope[] candidates = model.Current("release").Where(r => r.Subject is { } subject && Identity(subject.Id) == Identity(product) &&
            EstateModel.BodyString(r, "version") == version && EstateModel.BodyString(r, "commit") == commit && EstateModel.BodyString(r, "status") == "candidate").ToArray();
        return new(release ?? (candidates.Length == 1 ? candidates[0].Reference : null), Identity(product), version, commit ?? "unavailable", selectedRepositories, work);
    }

    private ReleasePolicyAssessment AssessReleasePolicies(EstateModel model, string product, string version, string? commit, IReadOnlySet<string> repositories)
    {
        PolicySubmission submission = SelectRelease(model, product, version, commit, repositories);
        PolicyKindRegistry registry = PolicySources.Registry();
        PolicyBasis basis = PolicySnapshot(model, registry, RecordParser.FormatTimestamp(PolicyClock.GetUtcNow()), submission, live: true);
        PolicyEvaluation evaluation = PolicyEvaluator.Evaluate(basis, registry);
        List<FindingDto> objections = model.Findings.Concat(RepositoryChecks(model)).Concat(ReleaseBatchFindings()).Where(f => f.Severity == Severity.Error).Distinct().Select(PolicyDiagnostic).ToList();
        foreach (Finding diagnostic in evaluation.Diagnostics.Where(d => d.Severity == Severity.Error).Except(model.Findings)) objections.Add(PolicyDiagnostic(diagnostic));
        foreach (PolicyFinding finding in evaluation.Findings.Where(f => f.ReleaseHold))
            objections.Add(new("warning", "policy." + finding.Verdict, "policy:" + finding.PolicyKey + "/" + finding.Rule,
                $"{finding.PolicyKey}/{finding.Rule} [{finding.Policy.Id} {finding.Policy.Hash}] {finding.Target.Identity}: {finding.Actual} Required: {finding.Required} Next: {finding.NextAction}" +
                (finding.Duty is { } duty ? $" Due {duty.DueAt}; deadline {duty.Deadline}; occurrence {duty.Occurrence.GetRawText()}." : "")));
        return new(basis, evaluation, PolicyView(model, basis, evaluation, sourceFiles: session.ReadFiles().ToDictionary(f => f.Path, StringComparer.Ordinal)), objections);
    }

    private static byte[] SerializeReleaseBasis(ReleasePolicyAssessment assessment, IReadOnlyList<string> documents, IReadOnlyList<RecordReference> excluded)
    {
        // This exact serialization is measured by admission and retained by submission. It writes no source objects.
        var repositories = assessment.Basis.Repositories.Select(repository => new
        {
            repository.Identity, repository.Revision, repository.Contract, repository.Complete,
            files = repository.Files.Select(file => new { file.Path, file.Hash, file.Available, file.IsLink }).ToArray(),
            repository.Evidence, repository.TreeComparisons,
        }).ToArray();
        return System.Text.Encoding.UTF8.GetBytes(PolicyBasis.Canonical(JsonSerializer.SerializeToElement(new
        {
            format = ReferencedBasisFormat, basis = assessment.View.Basis,
            records = assessment.Basis.Records.Select(r => new { r.Type, r.Id, hash = r.ComputedHash }).ToArray(),
            repositories, objects = assessment.Basis.Objects.Select(o => new { o.Hash, o.Available }).ToArray(), policies = assessment.View,
            documents, excludedOperationRevisions = excluded,
        }, ProtocolJson.Options)));
    }

    private string RetainReleaseBasis(ReleasePolicyAssessment assessment, IReadOnlyList<string> documents, IReadOnlyList<RecordReference> excluded)
    {
        byte[] bytes = SerializeReleaseBasis(assessment, documents, excluded);
        if (bytes.Length > EstateLayout.MaxObjectBytes || assessment.Basis.Repositories.SelectMany(r => r.Files).Any(f => f.Available && f.Bytes.Length > EstateLayout.MaxObjectBytes))
            throw new InvalidOperationException("The release basis exceeds the estate's object limit; reduce the source selection and check again.");
        foreach (var file in assessment.Basis.Repositories.SelectMany(r => r.Files).Where(f => f.Available)) session.Objects.Put(file.Bytes.AsSpan());
        return session.Objects.Put(bytes);
    }
}
