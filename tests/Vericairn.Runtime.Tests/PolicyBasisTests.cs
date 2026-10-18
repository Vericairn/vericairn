// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

public class PolicyBasisTests
{
    [Fact]
    public void IdenticalCompleteResultsIncludeDiagnosticsCountsAndStableOrdering()
    {
        RecordEnvelope[] records = [Policy("a", "zeta"), Policy("b", "alpha")];
        Finding[] diagnostics = [new(Severity.Error, "fixture.error", "z", "Preserved error."), new(Severity.Warning, "fixture.warning", "a", "Preserved warning.")];
        PolicyRepository[] repositories = [Repository("z"), Repository("a")];
        PolicyBasis basis = new(Model(records, diagnostics), "2026-01-02T00:00:00Z", PolicyTimeOrigin.Preview, repositories);
        PolicyEvaluation expected = PolicyEvaluator.Evaluate(basis);
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "en-US", "tr-TR", "ar-SA", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                PolicyBasis shuffled = new(Model(records.Reverse(), diagnostics.Reverse()), basis.AsOf, basis.Origin,
                    repositories.Reverse().Select(r => r with { Files = r.Files.Reverse().ToImmutableArray() }));
                Assert.Equal(basis.Digest, shuffled.Digest);
                Assert.Equal(expected.Canonical(), PolicyEvaluator.Evaluate(shuffled).Canonical());
                Assert.Equal(expected.Canonical(), PolicyEvaluator.Evaluate(basis).Canonical());
            }
        }
        finally { CultureInfo.CurrentCulture = saved; }
        Assert.Equal(1, expected.Errors);
        Assert.Equal(4, expected.Warnings);
        Assert.Equal(2, expected.Holds);
        Assert.Equal(["alpha", "zeta"], expected.Findings.Select(f => f.PolicyKey));
        Assert.All(expected.Rules, r => Assert.Equal(1, r.Unavailable));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2025-12-31T23:59:59Z")]
    [InlineData("2026-01-01T00:00:00-06:00")]
    [InlineData("2026-01-01T00:00:00.1Z")]
    public void MissingNoncanonicalAndHistoricalTimesAreUnavailable(string? at)
    {
        var basis = new PolicyBasis(Model([Policy("a", "policy")]), at, PolicyTimeOrigin.Preview);
        PolicyEvaluation result = PolicyEvaluator.Evaluate(basis);
        Assert.False(result.Complete);
        Assert.Equal("unavailable", Assert.Single(result.Findings).Verdict);
        Assert.Equal(1, result.Holds);
    }

    [Fact]
    public void RecordedTimeIsExplicitAndPreviewCannotBecomeSubmission()
    {
        EstateModel model = Model([Policy("a", "policy")]);
        PolicyBasis recorded = PolicyBasis.Recorded(model);
        Assert.Equal("2026-01-01T00:00:00Z", recorded.AsOf);
        Assert.Null(recorded.UnavailableReason());
        Assert.NotNull(new PolicyBasis(model, "2026-01-02T00:00:00Z", PolicyTimeOrigin.Recorded).UnavailableReason());
        Assert.NotNull(new PolicyBasis(model, recorded.AsOf, PolicyTimeOrigin.Submission).UnavailableReason());
        var submission = new PolicySubmission(new("release", "candidate", "hash"), "product", "1.0.0", new string('a', 40));
        Assert.NotNull(new PolicyBasis(model, recorded.AsOf, PolicyTimeOrigin.Preview, submission: submission).UnavailableReason());
        Assert.Null(new PolicyBasis(model, recorded.AsOf, PolicyTimeOrigin.Submission, submission: submission).UnavailableReason());
        Assert.NotEqual(recorded.Digest, new PolicyBasis(model, recorded.AsOf, PolicyTimeOrigin.Preview).Digest);
        Assert.NotNull(PolicyBasis.Recorded(Model([])).UnavailableReason());
    }

    [Fact]
    public void TheSnapshotCopiesMutableInputsAndHashesUncommittedBytesAndImplementations()
    {
        var records = new List<RecordEnvelope> { Policy("a", "policy") };
        EstateModel model = Model(records);
        var basis = new PolicyBasis(model, "2026-01-01T00:00:00Z", PolicyTimeOrigin.Preview, [Repository("a")]);
        string expected = PolicyEvaluator.Evaluate(basis).Canonical();
        records.Clear();
        model.Findings.Error("later", "fixture", "Later loader mutation.");
        Assert.Equal(expected, PolicyEvaluator.Evaluate(basis).Canonical());
        Assert.Empty(basis.Model.Root);
        Assert.NotEqual(basis.Digest, new PolicyBasis(basis.Model, basis.AsOf, basis.Origin, [Repository("a") with { Revision = "changed" }]).Digest);
        Assert.NotEqual(basis.Digest, new PolicyBasis(basis.Model, basis.AsOf, basis.Origin, basis.Repositories, implementations: [new("example/rule", 1, "changed")]).Digest);
        Assert.NotEqual(basis.Digest, new PolicyBasis(basis.Model, basis.AsOf, basis.Origin, [Repository("a") with { Files = [File("docs/a.md", "different bytes")] }]).Digest);
    }

    internal static EstateModel Model(IEnumerable<RecordEnvelope> records, IEnumerable<Finding>? diagnostics = null)
    {
        var items = records.ToArray();
        var findings = new FindingList();
        foreach (Finding finding in diagnostics ?? []) findings.Add(finding);
        return new("", items, items.ToDictionary(r => r.Id, StringComparer.Ordinal),
            items.Where(r => r.Supersedes is not null).ToDictionary(r => r.Supersedes!, r => r.Id, StringComparer.Ordinal), findings);
    }

    internal static RecordEnvelope Policy(string id, string key) => new("estate-policy", id, "2026-01-01T00:00:00Z", "operator", null, null,
        JsonSerializer.SerializeToElement(new { key, owner = "operator", state = "active", level = "refuse", startsAt = "2026-01-01T00:00:00Z", rules = new[] { new { key = "rule", kind = "example/rule", version = 1, parameters = new { } } } }), "hash-" + id, "hash-" + id, []);

    internal static PolicyFile File(string path, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return new(path, ContentHash.Sha256(bytes), [.. bytes]);
    }

    private static PolicyRepository Repository(string id) => new(id, "working-tree", PolicyBasis.RepositoryContract, true, [File("docs/b.md", "beta"), File("docs/a.md", "alpha")]);
}
