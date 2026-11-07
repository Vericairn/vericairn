// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Policies;

internal sealed record PolicyFile(string Path, string Hash, ImmutableArray<byte> Bytes, bool Available = true, bool IsLink = false);
internal sealed record PolicyRepository(string Identity, string Revision, string Contract, bool Complete, ImmutableArray<PolicyFile> Files, bool Evidence = false)
{
    public ImmutableArray<PolicyTreeComparison> TreeComparisons { get; init; } = [];
}
internal sealed record PolicyTreeRequest(string SourceCommit, ImmutableArray<string> Paths);
internal sealed record PolicyTreeComparison(string SourceCommit, string SelectedCommit, ImmutableArray<string> Paths, bool Complete, string? SourceTree = null, string? SelectedTree = null);
internal sealed record PolicyObject(string Hash, ImmutableArray<byte> Bytes, bool Available = true);
internal sealed record PolicyImplementation(string Kind, int Version, string Digest);
internal sealed record PolicySubmission(RecordReference? Release, string Product, string Version, string Commit,
    IReadOnlyList<string>? Repositories = null, IReadOnlyList<string>? WorkOrders = null);

internal enum PolicyTimeOrigin { Recorded, Preview, Submission }

// This is an in-memory evaluation input, not a record or a public transport contract. Adapters do all I/O
// and validation before constructing it. Copy collections and discard the loader's machine-local root.
internal sealed class PolicyBasis
{
    private readonly string? unavailable;
    internal const string Contract = "vericairn.estate-policy/1";
    internal const string RepositoryContract = "vericairn.policy-repository/1";

    internal PolicyBasis(EstateModel validated, string? asOf, PolicyTimeOrigin origin,
        IEnumerable<PolicyRepository>? repositories = null, IEnumerable<PolicyObject>? objects = null,
        IEnumerable<PolicyImplementation>? implementations = null, PolicySubmission? submission = null,
        bool complete = true)
    {
        Records = validated.Records.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
            .ThenBy(r => r.ComputedHash, StringComparer.Ordinal).Select(r => new RecordEnvelope(r.Type, r.Id, r.At, r.By,
                r.Subject, r.Supersedes, r.Body.Clone(), r.DeclaredHash, r.ComputedHash, r.Signatures.ToImmutableArray(), r.Format)).ToImmutableArray();
        Findings = validated.Findings.OrderBy(f => f.Location, StringComparer.Ordinal).ThenBy(f => f.Code, StringComparer.Ordinal)
            .ThenBy(f => f.Message, StringComparer.Ordinal).ThenBy(f => f.Severity).ToImmutableArray();
        var findings = new FindingList();
        foreach (Finding finding in Findings) findings.Add(finding);
        Model = new EstateModel(string.Empty, Records, Records.GroupBy(r => r.Id, StringComparer.Ordinal).ToImmutableDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
            validated.SupersededBy.ToImmutableDictionary(StringComparer.Ordinal), findings);
        if (validated.Manifest is { } manifest) Model.Manifest = new()
        {
            Estate = manifest.Estate, RecordFormat = manifest.RecordFormat, Profile = manifest.Profile, Mode = manifest.Mode,
            Repository = manifest.Repository, Commit = manifest.Commit, Version = manifest.Version,
            Artifacts = manifest.Artifacts?.ToImmutableSortedDictionary(StringComparer.Ordinal),
        };
        var order = new SourceOrder();
        Repositories = (repositories ?? []).Select(r => r with { TreeComparisons = r.TreeComparisons.OrderBy(Canonical, StringComparer.Ordinal).ToImmutableArray(), Files = r.Files.OrderBy(f => f, order).ToImmutableArray() })
            .OrderBy(r => r, order).ToImmutableArray();
        Objects = (objects ?? []).OrderBy(o => o.Hash, StringComparer.Ordinal).ThenBy(o => o.Available).ThenBy(o => ContentHash.Sha256(o.Bytes.AsSpan()), StringComparer.Ordinal).ToImmutableArray();
        Implementations = (implementations ?? []).OrderBy(i => i.Kind, StringComparer.Ordinal).ThenBy(i => i.Version).ThenBy(i => i.Digest, StringComparer.Ordinal).ToImmutableArray();
        AsOf = asOf;
        Origin = origin;
        Submission = submission is null ? null : submission with { Repositories = submission.Repositories?.ToImmutableArray(), WorkOrders = submission.WorkOrders?.ToImmutableArray() };
        Complete = complete;
        RecordDigest = DigestOf(Records);
        Digest = Hash(new
        {
            contract = Contract, scanner = WritingScanner.Contract, RecordDigest, Findings, complete, asOf, origin, submission,
            successors = Model.SupersededBy.OrderBy(p => p.Key, StringComparer.Ordinal),
            repositories = Repositories.Select(r => new { r.Identity, r.Revision, r.Contract, r.Complete, r.Evidence, files = r.Files.Select(f => new { f.Path, f.Hash, f.Available, f.IsLink, actualHash = ContentHash.Sha256(f.Bytes.AsSpan()) }) }),
            objects = Objects.Select(o => new { o.Hash, o.Available, actualHash = ContentHash.Sha256(o.Bytes.AsSpan()) }),
            implementations = Implementations,
        });
        // Preserve the existing basis for sources without tree observations; retained build bindings extend only their own snapshots.
        if (Repositories.Any(r => !r.TreeComparisons.IsEmpty)) Digest = Hash(new
        {
            snapshot = Digest,
            repositoryTrees = Repositories.Where(r => !r.TreeComparisons.IsEmpty).Select(r => new { r.Identity, r.Revision, r.TreeComparisons }),
        });
        if (Model.Manifest is not null) Digest = Hash(new { snapshot = Digest, manifest = Model.Manifest });
        unavailable = CheckUnavailable();
    }

    internal ImmutableArray<RecordEnvelope> Records { get; }
    internal ImmutableArray<Finding> Findings { get; }
    internal ImmutableArray<PolicyRepository> Repositories { get; }
    internal ImmutableArray<PolicyObject> Objects { get; }
    internal ImmutableArray<PolicyImplementation> Implementations { get; }
    internal EstateModel Model { get; }
    internal string? AsOf { get; }
    internal PolicyTimeOrigin Origin { get; }
    internal PolicySubmission? Submission { get; }
    internal bool Complete { get; }
    internal string RecordDigest { get; }
    internal string Digest { get; }
    internal DateTimeOffset? Time => DutyRecords.Time(AsOf);

    internal static PolicyBasis Recorded(EstateModel validated, IEnumerable<PolicyRepository>? repositories = null,
        IEnumerable<PolicyImplementation>? implementations = null) => new(validated,
            validated.Records.Select(r => r.At).Where(RecordParser.IsTimestamp).Order(StringComparer.Ordinal).LastOrDefault(),
            PolicyTimeOrigin.Recorded, repositories, implementations: implementations);

    internal string? UnavailableReason() => unavailable;

    private string? CheckUnavailable()
    {
        if (!Complete) return "The record view is incomplete.";
        if (Time is null || Records.Length == 0) return "The evaluation needs a valid explicit UTC time and recorded basis.";
        if (Records.Any(r => !RecordParser.IsTimestamp(r.At) || string.CompareOrdinal(r.At, AsOf) > 0)) return "The evaluation time precedes an included record or its time is invalid; supply a historical snapshot for replay.";
        if (Origin == PolicyTimeOrigin.Recorded && AsOf != Records[^1].At) return "Recorded time must be the greatest record time.";
        if (Origin == PolicyTimeOrigin.Submission && Submission is null || Origin == PolicyTimeOrigin.Recorded && Submission is not null)
            return "Submission evaluation requires the exact proposed release, product, version and commit; a recorded-time view cannot select a candidate.";
        if (Findings.Any(f => f.Severity == Severity.Error) || Records.GroupBy(r => r.Id, StringComparer.Ordinal).Any(g => g.Count() != 1)) return "Invalid or conflicting record prerequisites remain in the basis.";
        return null;
    }

    // Sources are ordered by path, hash, availability and link for a file, and by identity, revision, contract, completeness and
    // evidence for a repository; content hashes, and a repository's canonical file and tree hashes, break only the ties that
    // remain. Each is computed only when every earlier key ties, so a large inventory is not serialized just to be ordered.
    private sealed class SourceOrder : IComparer<PolicyFile>, IComparer<PolicyRepository>
    {
        private readonly Dictionary<object, string> computed = new(ReferenceEqualityComparer.Instance);

        public int Compare(PolicyFile? x, PolicyFile? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null || y is null) return x is null ? -1 : 1;
            int order = string.CompareOrdinal(x.Path, y.Path);
            if (order == 0) order = string.CompareOrdinal(x.Hash, y.Hash);
            if (order == 0) order = x.Available.CompareTo(y.Available);
            if (order == 0) order = x.IsLink.CompareTo(y.IsLink);
            return order != 0 ? order : string.CompareOrdinal(Content(x), Content(y));
        }

        public int Compare(PolicyRepository? x, PolicyRepository? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null || y is null) return x is null ? -1 : 1;
            int order = string.CompareOrdinal(x.Identity, y.Identity);
            if (order == 0) order = string.CompareOrdinal(x.Revision, y.Revision);
            if (order == 0) order = string.CompareOrdinal(x.Contract, y.Contract);
            if (order == 0) order = x.Complete.CompareTo(y.Complete);
            if (order == 0) order = x.Evidence.CompareTo(y.Evidence);
            if (order == 0) order = string.CompareOrdinal(Files(x), Files(y));
            return order != 0 ? order : string.CompareOrdinal(Trees(x), Trees(y));
        }

        private string Content(PolicyFile file) => Once(file, () => ContentHash.Sha256(file.Bytes.AsSpan()));

        // Keyed by the immutable array itself, so equal inventories held by different records are hashed once.
        private string Files(PolicyRepository repository) => Once(ImmutableCollectionsMarshal.AsArray(repository.Files)!, () => Hash(repository.Files));

        private string Trees(PolicyRepository repository) => Once(ImmutableCollectionsMarshal.AsArray(repository.TreeComparisons)!, () => Hash(repository.TreeComparisons));

        private string Once(object source, Func<string> compute)
        {
            if (!computed.TryGetValue(source, out string? key)) computed[source] = key = compute();
            return key;
        }
    }

    internal static string Canonical<T>(T value) => CanonicalJson.Serialize(JsonSerializer.SerializeToElement(value));
    internal static string Hash<T>(T value) => ContentHash.Sha256(Encoding.UTF8.GetBytes(Canonical(value)));

    /// <summary>The digest of exactly these records in this order, signatures included, which a record's own hash leaves out.</summary>
    internal static string DigestOf(IEnumerable<RecordEnvelope> records) =>
        Hash(records.Select(r => new { r.Type, r.Id, r.At, r.By, r.Subject, r.Supersedes, r.Body, r.DeclaredHash, r.ComputedHash, r.Signatures, r.Format }));
}
