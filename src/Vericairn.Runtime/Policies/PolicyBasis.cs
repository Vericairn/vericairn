// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Policies;

internal sealed record PolicyFile(string Path, string Hash, ImmutableArray<byte> Bytes, bool Available = true, bool IsLink = false);
internal sealed record PolicyRepository(string Identity, string Revision, string Contract, bool Complete, ImmutableArray<PolicyFile> Files);
internal sealed record PolicyObject(string Hash, ImmutableArray<byte> Bytes, bool Available = true);
internal sealed record PolicyImplementation(string Kind, int Version, string Digest);
internal sealed record PolicySubmission(RecordReference Release, string Product, string Version, string Commit);

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
        Repositories = (repositories ?? []).Select(r => r with { Files = r.Files.OrderBy(f => f.Path, StringComparer.Ordinal).ThenBy(f => f.Hash, StringComparer.Ordinal)
                .ThenBy(f => f.Available).ThenBy(f => f.IsLink).ThenBy(f => ContentHash.Sha256(f.Bytes.AsSpan()), StringComparer.Ordinal).ToImmutableArray() })
            .OrderBy(r => r.Identity, StringComparer.Ordinal).ThenBy(r => r.Revision, StringComparer.Ordinal).ThenBy(r => r.Contract, StringComparer.Ordinal).ThenBy(r => r.Complete)
            .ThenBy(r => Hash(r.Files), StringComparer.Ordinal).ToImmutableArray();
        Objects = (objects ?? []).OrderBy(o => o.Hash, StringComparer.Ordinal).ThenBy(o => o.Available).ThenBy(o => ContentHash.Sha256(o.Bytes.AsSpan()), StringComparer.Ordinal).ToImmutableArray();
        Implementations = (implementations ?? []).OrderBy(i => i.Kind, StringComparer.Ordinal).ThenBy(i => i.Version).ThenBy(i => i.Digest, StringComparer.Ordinal).ToImmutableArray();
        AsOf = asOf;
        Origin = origin;
        Submission = submission;
        Complete = complete;
        RecordDigest = Hash(Records.Select(r => new { r.Type, r.Id, r.At, r.By, r.Subject, r.Supersedes, r.Body, r.DeclaredHash, r.ComputedHash, r.Signatures, r.Format }));
        Digest = Hash(new
        {
            contract = Contract, scanner = WritingScanner.Contract, RecordDigest, Findings, complete, asOf, origin, submission,
            successors = Model.SupersededBy.OrderBy(p => p.Key, StringComparer.Ordinal),
            repositories = Repositories.Select(r => new { r.Identity, r.Revision, r.Contract, r.Complete, files = r.Files.Select(f => new { f.Path, f.Hash, f.Available, f.IsLink, actualHash = ContentHash.Sha256(f.Bytes.AsSpan()) }) }),
            objects = Objects.Select(o => new { o.Hash, o.Available, actualHash = ContentHash.Sha256(o.Bytes.AsSpan()) }),
            implementations = Implementations,
        });
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
        if ((Origin == PolicyTimeOrigin.Submission) != (Submission is not null)) return "Submission evaluation requires the exact proposed release, product, version and commit.";
        if (Findings.Any(f => f.Severity == Severity.Error) || Records.GroupBy(r => r.Id, StringComparer.Ordinal).Any(g => g.Count() != 1)) return "Invalid or conflicting record prerequisites remain in the basis.";
        return null;
    }

    internal static string Canonical<T>(T value) => CanonicalJson.Serialize(JsonSerializer.SerializeToElement(value));
    internal static string Hash<T>(T value) => ContentHash.Sha256(Encoding.UTF8.GetBytes(Canonical(value)));
}
