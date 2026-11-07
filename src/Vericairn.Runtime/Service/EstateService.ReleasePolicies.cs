// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Runtime.Objects;
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
    // The shape of /2; files of a repository read at an exact commit are named by path and hash and retained nowhere in the estate.
    private const string CommittedBasisFormat = "vericairn.release-basis/3";

    /// <inheritdoc />
    public ReleaseBasisResponse? ReleaseBasis(string hash)
    {
        if (!ContentHash.IsValid(hash)) return null;
        byte[]? bytes;
        try { bytes = ObjectGet(hash); }
        catch (ObjectCorruptedException)
        {
            return UnresolvedBasis(hash, [new("error", "release-basis.hash-mismatch", "basis:" + hash,
                "The retained basis object's bytes do not match its hash.")]);
        }
        if (bytes is null) return null;
        string? format = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            format = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("format", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (format is ReferencedBasisFormat or CommittedBasisFormat) return ResolveReleaseBasis(hash, bytes);
            if (format != CopiedBasisFormat) return null;
            ReleaseBasisEvidence? evidence = JsonSerializer.Deserialize<ReleaseBasisEvidence>(bytes, ProtocolJson.Options);
            return evidence is { Format: CopiedBasisFormat, Basis: not null, Policies: not null,
                Records: not null, Repositories: not null, Objects: not null, Documents: not null, ExcludedOperationRevisions: not null }
                ? new(hash, evidence) : InvalidBasis(hash);
        }
        catch (JsonException) { return format is CommittedBasisFormat or ReferencedBasisFormat or CopiedBasisFormat ? InvalidBasis(hash) : null; }
    }

    // A referenced basis resolves to the same evidence a copied one carries: every record from the estate's own records, every
    // file of a repository read at an exact commit from that repository's git objects at the commit, through its registered clone
    // (FIX-95), and every other source file and object from the object store, each checked against the hash the basis names.
    // Bases retained before 0.9.2 also copied exact-commit files into the store; such a copy still reads when git cannot. A
    // reference is named by its content, so bytes of that hash read at any exact commit the basis names are the same bytes:
    // a store object those earlier copies supplied is read there too. Anything that no longer resolves exactly makes the basis
    // unavailable rather than partly answered. Collect every failed reference so the reader can distinguish loss from a wrong
    // identifier without opening the object store.
    private ReleaseBasisResponse? ResolveReleaseBasis(string hash, byte[] bytes)
    {
        StoredReleaseBasis? stored = JsonSerializer.Deserialize<StoredReleaseBasis>(bytes, ProtocolJson.Options);
        if (stored is not { Format: ReferencedBasisFormat or CommittedBasisFormat, Basis: not null, Policies: not null, Records: not null, Repositories: not null,
            Objects: not null, Documents: not null, ExcludedOperationRevisions: not null }) return InvalidBasis(hash);
        if (stored.Records.Any(r => r is null || string.IsNullOrEmpty(r.Id) || string.IsNullOrEmpty(r.Type) || r.Hash is null) ||
            stored.Repositories.Any(r => r is null || r.Files is null || r.Identity is null || r.Revision is null ||
                r.Files.Any(f => f is null || f.Path is null || f.Hash is null)) ||
            stored.Objects.Any(o => o is null || o.Hash is null)) return InvalidBasis(hash);
        EstateModel model;
        lock (gate) model = session.Load();
        List<RecordEnvelope> resolved = [];
        List<FindingDto> failures = [];
        foreach (StoredRecord reference in stored.Records)
        {
            string location = $"record:{reference.Type}:{reference.Id}@{reference.Hash}";
            if (!model.ById.TryGetValue(reference.Id, out RecordEnvelope? r))
            {
                failures.Add(new("error", "release-basis.missing", location, "The referenced record is missing or unreadable."));
                continue;
            }
            if (r.Type != reference.Type || r.ComputedHash != reference.Hash)
            {
                failures.Add(new("error", "release-basis.hash-mismatch", location, "The referenced record's type or content does not match its retained reference."));
                continue;
            }
            resolved.Add(new RecordEnvelope(r.Type, r.Id, r.At, r.By, r.Subject, r.Supersedes, r.Body.Clone(), r.DeclaredHash, r.ComputedHash,
                r.Signatures.ToImmutableArray(), r.Format));
        }
        // A record's hash leaves out its signatures, which the stored record digest covers: the records read back must be exactly
        // those the gate judged, signatures included.
        if (failures.Count == 0 && PolicyBasis.DigestOf(resolved) != stored.Basis.RecordDigest)
            failures.Add(new("error", "release-basis.record-digest-mismatch", "record-set:" + stored.Basis.RecordDigest,
                "The referenced record set, including signatures, does not match its retained digest."));
        List<JsonElement> records = resolved.Select(r => JsonSerializer.SerializeToElement(r, ProtocolJson.Options)).ToList();
        // Each exact commit is read once, as the gate read it; its files then answer by path, and by content for any reference.
        var committed = new Dictionary<StoredRepository, Dictionary<string, PolicyFile>?>(ReferenceEqualityComparer.Instance);
        var byContent = new Dictionary<string, PolicyFile>(StringComparer.Ordinal);
        foreach (StoredRepository repository in stored.Repositories.Where(r => ReleaseBasisRetention.AtExactCommit(r.Revision) && r.Files.Any(f => f.Available)))
        {
            Dictionary<string, PolicyFile>? read = Committed(model, repository.Identity, repository.Revision);
            committed[repository] = read;
            if (read is not null) foreach (PolicyFile file in read.Values) byContent.TryAdd(file.Hash, file);
        }
        List<ReleaseBasisRepository> repositories = [];
        foreach (StoredRepository repository in stored.Repositories)
        {
            Dictionary<string, PolicyFile>? read = committed.GetValueOrDefault(repository);
            bool exact = ReleaseBasisRetention.AtExactCommit(repository.Revision);
            List<ReleaseBasisFile> files = [];
            foreach (StoredFile file in repository.Files)
            {
                byte[]? content = null;
                if (file.Available)
                {
                    content = read?.GetValueOrDefault(file.Path) is { } held && held.Hash == file.Hash ? Bytes(held)
                        : Retained(file.Hash, $"source:{repository.Identity}@{repository.Revision}:{file.Path}@{file.Hash}", exact
                            ? "The file is not at its path in the repository's git objects at the recorded commit, read through a clone of the repository registered here, and the estate retains no copy."
                            : "The referenced object is missing.");
                }
                files.Add(new(file.Path, file.Hash, content ?? [], file.Available, file.IsLink));
            }
            repositories.Add(new(repository.Identity, repository.Revision, repository.Contract, repository.Complete, files, repository.Evidence)
            { TreeComparisons = repository.TreeComparisons ?? [] });
        }
        List<ReleaseBasisObject> objects = [];
        foreach (StoredObject retained in stored.Objects)
        {
            byte[]? content = null;
            if (retained.Available) content = Retained(retained.Hash, "object:" + retained.Hash, "The referenced object is missing.");
            objects.Add(new(retained.Hash, content ?? [], retained.Available));
        }
        return failures.Count > 0 ? UnresolvedBasis(hash, failures)
            : new(hash, new(stored.Format, stored.Basis, records, repositories, objects, stored.Policies, stored.Documents, stored.ExcludedOperationRevisions));

        byte[]? Retained(string objectHash, string location, string missing)
        {
            if (!ContentHash.IsValid(objectHash))
            {
                failures.Add(new("error", "release-basis.invalid-reference", location, "The retained reference is not a supported content hash."));
                return null;
            }
            try
            {
                byte[]? content = session.Objects.Read(objectHash);
                if (content is null && byContent.TryGetValue(objectHash, out PolicyFile? same)) return Bytes(same);
                if (content is null) failures.Add(new("error", "release-basis.missing", location, missing));
                return content;
            }
            catch (ObjectCorruptedException)
            {
                if (byContent.TryGetValue(objectHash, out PolicyFile? same)) return Bytes(same);
                failures.Add(new("error", "release-basis.hash-mismatch", location, "The referenced object's bytes do not match its hash."));
                return null;
            }
        }

        // The reader's bytes are immutable and verified against their hash, so the evidence shares them rather than copying.
        static byte[] Bytes(PolicyFile file) => System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(file.Bytes) ?? [];
    }

    // The files of a repository at an exact commit, from the installed repository source, which reads the commit's tree and blobs
    // from a clone registered here and confirms that the clone holds the commit; null when no complete read is possible here.
    private static Dictionary<string, PolicyFile>? Committed(EstateModel model, string identity, string commit)
    {
        if (PolicySources.RepositorySource is not { } source || !model.ById.TryGetValue(identity, out RecordEnvelope? resource) || resource.Type != "resource") return null;
        // The gate read the identity's current revision; registrations may name any revision of it.
        var seen = new HashSet<string>(StringComparer.Ordinal) { resource.Id };
        while (model.SupersededBy.TryGetValue(resource.Id, out string? next) && model.ById.TryGetValue(next, out RecordEnvelope? successor) &&
            successor.Type == "resource" && seen.Add(successor.Id)) resource = successor;
        try
        {
            PolicyRepository read = source.Read(model, resource, commit);
            if (!read.Complete || read.Revision != commit) return null;
            var files = new Dictionary<string, PolicyFile>(StringComparer.Ordinal);
            foreach (PolicyFile file in read.Files.Where(f => f.Available && !f.IsLink && ContentHash.Sha256(f.Bytes.AsSpan()) == f.Hash)) files.TryAdd(file.Path, file);
            return files;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }

    private static ReleaseBasisResponse UnresolvedBasis(string hash, IReadOnlyList<FindingDto> failures) => new(hash, null)
    {
        Problem = new("release-basis-unavailable", $"Retained release basis {hash} cannot resolve.", failures),
    };

    private static ReleaseBasisResponse InvalidBasis(string hash) => UnresolvedBasis(hash,
        [new("error", "release-basis.invalid", "basis:" + hash, "The retained basis has missing or invalid evidence fields.")]);

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

    private ReleasePolicyAssessment AssessReleasePolicies(EstateModel model, string product, string version, string? commit, IReadOnlySet<string> repositories, string? line)
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
        return new(basis, evaluation, PolicyView(model, basis, evaluation, sourceFiles: session.ReadFiles().ToDictionary(f => f.Path, StringComparer.Ordinal), releaseLine: line), objections);
    }

    private static byte[] SerializeReleaseBasis(ReleasePolicyAssessment assessment, IReadOnlyList<string> documents, IReadOnlyList<RecordReference> excluded)
    {
        // This exact serialization is measured by admission and retained by submission. It writes no source objects. Every file
        // is named by path and hash; which of them the estate retains follows from its repository's revision (ReleaseBasisRetention).
        var repositories = assessment.Basis.Repositories.Select(repository => new
        {
            repository.Identity, repository.Revision, repository.Contract, repository.Complete,
            files = repository.Files.Select(file => new { file.Path, file.Hash, file.Available, file.IsLink }).ToArray(),
            repository.Evidence, repository.TreeComparisons,
        }).ToArray();
        return System.Text.Encoding.UTF8.GetBytes(PolicyBasis.Canonical(JsonSerializer.SerializeToElement(new
        {
            format = CommittedBasisFormat, basis = assessment.View.Basis,
            records = assessment.Basis.Records.Select(r => new { r.Type, r.Id, hash = r.ComputedHash }).ToArray(),
            repositories, objects = assessment.Basis.Objects.Select(o => new { o.Hash, o.Available }).ToArray(), policies = assessment.View,
            documents, excludedOperationRevisions = excluded,
        }, ProtocolJson.Options)));
    }

    // Only files read from a working tree are retained as objects; a repository read at an exact commit keeps its bytes in its own
    // git objects, where a reader finds them again (FIX-95). Admission refuses a basis past the bound before anything is written.
    private string RetainReleaseBasis(ReleasePolicyAssessment assessment, IReadOnlyList<string> documents, IReadOnlyList<RecordReference> excluded)
    {
        byte[] bytes = SerializeReleaseBasis(assessment, documents, excluded);
        (PolicyRepository Repository, PolicyFile File)[] retained = [.. ReleaseBasisRetention.Retained(assessment.Basis)];
        if (bytes.Length > EstateLayout.MaxObjectBytes || retained.Any(r => r.File.Bytes.Length > EstateLayout.MaxObjectBytes))
            throw new InvalidOperationException("The release basis exceeds the estate's object limit; reduce the source selection and check again.");
        long retainedBytes = retained.Sum(r => (long)r.File.Bytes.Length);
        if (retained.Length > ReleaseBasisRetention.MaximumFiles || retainedBytes > ReleaseBasisRetention.MaximumBytes)
            throw new InvalidOperationException(ReleaseBasisRetention.Exceeded(retained.Length, retainedBytes));
        foreach ((_, PolicyFile file) in retained) session.Objects.Put(file.Bytes.AsSpan());
        return session.Objects.Put(bytes);
    }
}
