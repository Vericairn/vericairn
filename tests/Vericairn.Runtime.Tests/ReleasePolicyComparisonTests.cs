// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class ReleasePolicyComparisonTests
{
    [Theory]
    [InlineData("2026-01-01T00:00:00Z")]
    [InlineData("2026-01-01T00:00:01Z")]
    public void OnlyTheSubmissionsOwnSupersessionIsIgnoredWithoutChangingRetainedEvidence(string at)
    {
        EstateModel model = PriorHistory();
        RecordEnvelope retry = Retry();
        ReleasePolicyAssessment before = Assess(model);
        ReleasePolicyAssessment after = Assess(WithSuccessor(model, retry.Supersedes!, retry.Id), at);
        string beforeDigest = before.Basis.Digest, afterDigest = after.Basis.Digest;
        string beforeFindings = before.Evaluation.Canonical(), afterFindings = after.Evaluation.Canonical();

        Assert.Equal(before.Basis.RecordDigest, after.Basis.RecordDigest);
        Assert.False(before.HasSameInputsAndFindings(after));
        Assert.True(before.HasSameInputsAndFindings(after, [retry]));
        Assert.Equal(beforeDigest, before.Basis.Digest);
        Assert.Equal(afterDigest, after.Basis.Digest);
        Assert.NotEqual(beforeDigest, afterDigest);
        Assert.Equal(beforeFindings, before.Evaluation.Canonical());
        Assert.Equal(afterFindings, after.Evaluation.Canonical());
        Assert.Equal(at, after.Basis.AsOf);
        Assert.Equal(retry.Id, after.Basis.Model.SupersededBy[retry.Supersedes!]);
        Assert.False(before.Basis.Model.SupersededBy.ContainsKey(retry.Supersedes!));
    }

    [Theory]
    [InlineData("canceled", "another-retry")]
    [InlineData("unrelated", "retry")]
    [InlineData("original", "another-cancellation")]
    public void AnyOtherSuccessorChangeStillCounts(string predecessor, string successor)
    {
        EstateModel model = PriorHistory();
        RecordEnvelope retry = Retry();
        ReleasePolicyAssessment before = Assess(model);
        EstateModel afterOwnRevision = WithSuccessor(model, retry.Supersedes!, retry.Id);
        ReleasePolicyAssessment after = Assess(WithSuccessor(afterOwnRevision, predecessor, successor));

        Assert.Equal(before.Basis.RecordDigest, after.Basis.RecordDigest);
        Assert.False(before.HasSameInputsAndFindings(after, [retry]));
    }

    [Fact]
    public void ARecordChangeStillCountsAlongsideTheSubmissionsOwnSupersession()
    {
        EstateModel model = PriorHistory();
        RecordEnvelope retry = Retry();
        ReleasePolicyAssessment before = Assess(model);
        RecordEnvelope changed = FoundationPolicyTests.R("decision", "another-decision", new { summary = "Changed input." });
        EstateModel changedModel = PolicyBasisTests.Model([.. model.Records, changed]);
        ReleasePolicyAssessment after = Assess(WithSuccessor(changedModel, retry.Supersedes!, retry.Id));

        Assert.NotEqual(before.Basis.RecordDigest, after.Basis.RecordDigest);
        Assert.False(before.HasSameInputsAndFindings(after, [retry]));
    }

    [Fact]
    public void AnUnlistedOperationOrANonOperationCannotExcludeASupersession()
    {
        EstateModel model = PriorHistory();
        RecordEnvelope retry = Retry();
        ReleasePolicyAssessment before = Assess(model);
        ReleasePolicyAssessment after = Assess(WithSuccessor(model, retry.Supersedes!, retry.Id));
        RecordEnvelope unlisted = FoundationPolicyTests.R("operation", "unlisted-retry", new { status = "uncertain" }, supersedes: retry.Supersedes);
        RecordEnvelope otherType = FoundationPolicyTests.R("decision", retry.Id, new { summary = "Another record type." }, supersedes: retry.Supersedes);

        Assert.False(before.HasSameInputsAndFindings(after, [unlisted]));
        Assert.False(before.HasSameInputsAndFindings(after, [otherType]));
    }

    private static EstateModel PriorHistory() => PolicyBasisTests.Model([
        FoundationPolicyTests.R("operation", "original", new { status = "not-started" }),
        FoundationPolicyTests.R("operation", "canceled", new { status = "canceled" }, supersedes: "original"),
        FoundationPolicyTests.R("decision", "unrelated", new { summary = "Unrelated input." }),
    ]);

    private static RecordEnvelope Retry() => FoundationPolicyTests.R("operation", "retry", new { status = "uncertain" }, supersedes: "canceled");

    private static EstateModel WithSuccessor(EstateModel model, string predecessor, string successor)
    {
        var successors = model.SupersededBy.ToDictionary(StringComparer.Ordinal);
        successors[predecessor] = successor;
        return new(model.Root, model.Records, model.ById, successors, model.Findings) { Manifest = model.Manifest };
    }

    private static ReleasePolicyAssessment Assess(EstateModel model, string at = "2026-01-01T00:00:00Z")
    {
        var basis = new PolicyBasis(model, at, PolicyTimeOrigin.Submission, submission: new(null, "product", "1.0.0", new string('a', 40)));
        var view = new PolicyResponse("", new(basis.Digest, basis.RecordDigest, at, "submission", null, [], [], []), [], [], [], [], [], true, 0);
        return new(basis, PolicyEvaluator.Evaluate(basis), view, []);
    }
}
