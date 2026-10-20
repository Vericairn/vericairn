// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Records.Types;
using Vericairn.Runtime.Policies;
using static Vericairn.Runtime.Tests.FoundationPolicyTests;

namespace Vericairn.Runtime.Tests;

public class ReleaseEntryTests
{
    private const string PublishedAt = "2026-01-01T01:00:00Z";
    private const string WrittenAt = "2026-01-01T02:00:00Z";
    private const string ReviewedAt = "2026-01-01T03:00:00Z";
    private const string Directory = "docs/knowledge/devlog";

    [Theory]
    [MemberData(nameof(ReleaseProductSubjects), MemberType = typeof(FoundationPolicyTests))]
    public void ReleaseEntryMatchesAnyProductRevision(string releaseProduct, bool matches)
    {
        RecordEnvelope publication = R("release", "publication", new { status = "published", version = "0.8.0" },
            PublishedAt, new("product", releaseProduct, null));
        RecordEnvelope[] records = [.. ProductRevisions(), publication];
        PolicyEvaluation missing = Run(records);
        Assert.True(missing.Complete);
        Assert.Equal(matches ? 1 : 0, Assert.Single(missing.Rules).Subjects);
        Assert.Equal(matches ? 1 : 0, missing.Holds);
        if (!matches)
        {
            Assert.Equal("unavailable", Assert.Single(Run(records, policy: Policy(Reference(publication))).Findings).Verdict);
            return;
        }

        Assert.Equal(publication.Reference, Assert.Single(missing.Findings).Target.Record);
        Assert.Equal("unmet", Assert.Single(Run(records, policy: Policy(Reference(publication))).Findings).Verdict);
        var entry = Entry(publication, text: Text(publication).Replace("product: product", "product: " + releaseProduct, StringComparison.Ordinal));
        PolicyFinding satisfied = Assert.Single(Run([.. records, entry.Pin, Review(entry.Pin)], [entry.File]).Findings);
        Assert.Equal("satisfied", satisfied.Verdict);
        Assert.Equal(publication.Reference, satisfied.Target.Record);
        Assert.False(satisfied.ReleaseHold);
        var next = new PolicySubmission(null, "product", "0.9.0", new string('a', 40));
        Assert.Equal(1, Run(records, submission: next).Holds);
        Assert.Equal(0, Run(records, submission: next with { Product = "other" }).Holds);
    }

    [Fact]
    public void CandidateAndPrepublicationWithdrawalOweNothingButPublishedHistorySurvivesWithdrawal()
    {
        RecordEnvelope candidate = Publication("candidate", status: "candidate");
        RecordEnvelope withdrawn = Publication("withdrawal", "withdrawn", supersedes: candidate.Id);
        Assert.Equal(0, Run([candidate, withdrawn]).Holds);
        RecordEnvelope publication = Publication("publication", supersedes: candidate.Id);
        withdrawn = Publication("withdrawal", "withdrawn", supersedes: publication.Id);
        PolicyFinding finding = Assert.Single(Run([candidate, publication, withdrawn]).Findings);
        Assert.Equal("unmet", finding.Verdict);
        Assert.Equal(publication.Reference, finding.Target.Record);
        Assert.True(finding.ReleaseHold);
    }

    [Fact]
    public void CohortUsesAtAndIdInclusivelyAndAnEditDoesNotRestartAfterDeclaration()
    {
        RecordEnvelope early = Publication("a", version: "9.0.0");
        RecordEnvelope boundary = Publication("b", version: "0.8.0");
        RecordEnvelope later = Publication("c", version: "0.1.0");
        RecordEnvelope policy = Policy(Reference(boundary));
        Assert.Equal(["b", "c"], Run([early, boundary, later], policy: policy).Findings.Select(f => f.Target.Identity));
        RecordEnvelope first = R("estate-policy", "b-policy", Policy().Body, PublishedAt);
        RecordEnvelope edit = R("estate-policy", "edit", first.Body, ReviewedAt, supersedes: first.Id);
        Assert.Equal(["c"], Run([early, later, first], policy: edit).Findings.Select(f => f.Target.Identity));
        Assert.Equal("unavailable", Assert.Single(Run([early], policy: Policy(Reference(boundary))).Findings).Verdict);
    }

    [Fact]
    public void PublishedEditsDoNotCreateNewEntryDebtOrChangeTheOriginalPublication()
    {
        RecordEnvelope publication = Publication();
        RecordEnvelope edit = Publication("edit", supersedes: publication.Id, at: ReviewedAt);
        var entry = Entry(publication);
        PolicyFinding result = Assert.Single(Run([publication, edit, entry.Pin, Review(entry.Pin)], [entry.File]).Findings);
        Assert.Equal("satisfied", result.Verdict);
        Assert.Equal(publication.Reference, result.Target.Record);
        Assert.Equal("unavailable", Assert.Single(Run([publication, edit], policy: Policy(Reference(edit))).Findings).Verdict);
    }

    [Fact]
    public void ANewInternalPinnedEntryAndAnExactPassingReviewSatisfyTheCondition()
    {
        RecordEnvelope publication = Publication();
        var entry = Entry(publication);
        RecordEnvelope review = Review(entry.Pin);
        PolicyFinding finding = Assert.Single(Run([publication, entry.Pin, review], [entry.File]).Findings);
        Assert.Equal("satisfied", finding.Verdict);
        Assert.Contains(entry.Pin.Reference, finding.Evidence);
        Assert.Contains(review.Reference, finding.Evidence);
        Assert.False(finding.ReleaseHold);
    }

    [Fact]
    public void ContentCorrectionRequiresReviewOfTheCurrentPinAndLatestQualifyingReviewMustPass()
    {
        RecordEnvelope publication = Publication();
        var first = Entry(publication);
        PolicyFile revised = PolicyBasisTests.File(first.File.Path, Text(publication) + "\nA correction.\n");
        RecordEnvelope pin = Pin(revised, "revision", ReviewedAt, first.Pin.Id, 2);
        RecordEnvelope oldReview = Review(first.Pin);
        PolicyObject[] objects = [new(first.File.Hash, first.File.Bytes)];
        RecordEnvelope[] records = [publication, first.Pin, pin, oldReview];
        Assert.Equal("unmet", Assert.Single(Run(records, [revised], objects).Findings).Verdict);
        RecordEnvelope pass = Review(pin, "a-pass");
        RecordEnvelope findings = Review(pin, "b-findings", "findings");
        Assert.Equal("satisfied", Assert.Single(Run([.. records, pass], [revised], objects).Findings).Verdict);
        Assert.Equal("unmet", Assert.Single(Run([.. records, pass, findings], [revised], objects).Findings).Verdict);
        RecordEnvelope recovered = Review(pin, "c-pass");
        Assert.Equal("satisfied", Assert.Single(Run([.. records, pass, findings, recovered], [revised], objects).Findings).Verdict);
    }

    [Theory]
    [InlineData("self", "author", "independent", "unmet")]
    [InlineData("independent-agent", "reviewer", "independent", "satisfied")]
    [InlineData("independent-agent", "reviewer", "operator", "unmet")]
    [InlineData("operator", "operator", "operator", "satisfied")]
    public void TheDeclaredReviewIndependenceIsRequired(string independence, string by, string required, string verdict)
    {
        RecordEnvelope publication = Publication();
        var entry = Entry(publication);
        RecordEnvelope review = Review(entry.Pin, independence: independence, by: by);
        Assert.Equal(verdict, Assert.Single(Run([publication, entry.Pin, review], [entry.File], policy: Policy(review: required)).Findings).Verdict);
        RecordEnvelope clearance = R("document-review", "clearance", new { outcome = "current", independence }, ReviewedAt, entry.Pin.Reference, by: by);
        Assert.Equal("unmet", Assert.Single(Run([publication, entry.Pin, clearance], [entry.File]).Findings).Verdict);
    }

    [Fact]
    public void OnlyCurrentReviewsCountAndUnqualifiedReviewsDoNotRemoveAnIndependentPass()
    {
        RecordEnvelope publication = Publication();
        var entry = Entry(publication);
        RecordEnvelope pass = Review(entry.Pin, "pass");
        RecordEnvelope self = Review(entry.Pin, "z-self", "findings", "self", "author");
        Assert.Equal("satisfied", Assert.Single(Run([publication, entry.Pin, pass, self], [entry.File]).Findings).Verdict);
        RecordEnvelope replacement = R("review", "replacement", new { outcome = "findings", independence = "independent-agent" }, ReviewedAt, entry.Pin.Reference, pass.Id, "reviewer");
        Assert.Equal("unmet", Assert.Single(Run([publication, entry.Pin, pass, replacement], [entry.File]).Findings).Verdict);
    }

    [Fact]
    public void TheFirstBytesProveNewAuthorshipAndAStableRelationInsteadOfTrustingTheLatestPin()
    {
        RecordEnvelope publication = Publication();
        var first = Entry(publication, at: "2026-01-01T00:30:00Z");
        PolicyFile revised = PolicyBasisTests.File(first.File.Path, Text(publication) + "\nCorrected.\n");
        RecordEnvelope pin = Pin(revised, "revision", WrittenAt, first.Pin.Id, 2);
        RecordEnvelope[] records = [publication, first.Pin, pin, Review(pin)];
        Assert.Equal("unavailable", Assert.Single(Run(records, [revised]).Findings).Verdict);
        Assert.Equal("unmet", Assert.Single(Run(records, [revised], [new(first.File.Hash, first.File.Bytes)]).Findings).Verdict);
        var changedIdentity = Entry(publication, text: Text(publication).Replace("id: entry", "id: old", StringComparison.Ordinal));
        pin = Pin(revised, "revision", WrittenAt, changedIdentity.Pin.Id, 2);
        Assert.Equal("unmet", Assert.Single(Run([publication, changedIdentity.Pin, pin, Review(pin)], [revised], [new(changedIdentity.File.Hash, changedIdentity.File.Bytes)]).Findings).Verdict);
    }

    [Fact]
    public void CopyingAnOldIdentityToANewPathCannotMakeItNewAndOneEntryCannotPayForTwoPublications()
    {
        RecordEnvelope publication = Publication();
        var old = Entry(publication, "old", at: "2026-01-01T00:30:00Z");
        var copy = Entry(publication, "copy");
        Assert.Equal("unmet", Assert.Single(Run([publication, old.Pin, copy.Pin, Review(copy.Pin)], [copy.File], [new(old.File.Hash, old.File.Bytes)]).Findings).Verdict);
        RecordEnvelope next = Publication("next", version: "0.9.0");
        PolicyEvaluation result = Run([publication, next, copy.Pin, Review(copy.Pin)], [copy.File]);
        Assert.Single(result.Findings, f => f.Verdict == "satisfied");
        Assert.Single(result.Findings, f => f.Verdict == "unmet");
    }

    [Theory]
    [InlineData("id: entry", "id: second")]
    [InlineData("id: entry", "id: entry")]
    public void DuplicateQualifyingEntriesNameEveryIdentityAndPath(string original, string replacement)
    {
        RecordEnvelope publication = Publication();
        var first = Entry(publication);
        var duplicate = Entry(publication, "second", text: Text(publication).Replace(original, replacement, StringComparison.Ordinal));
        PolicyFinding finding = Assert.Single(Run([publication, first.Pin, duplicate.Pin, Review(first.Pin), Review(duplicate.Pin, "review-2")], [first.File, duplicate.File]).Findings);
        Assert.Equal("unmet", finding.Verdict);
        Assert.Contains(first.File.Path, finding.Actual, StringComparison.Ordinal);
        Assert.Contains(duplicate.File.Path, finding.Actual, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("audience: internal", "audience: public")]
    [InlineData("versions: 0.8.0", "versions: '>=0.8.0'")]
    [InlineData("title: Release account", "title: ''")]
    [InlineData("summary: What changed", "summary: ''")]
    [InlineData("status: draft", "status: published")]
    [InlineData("The release account.", "")]
    public void EditorialFieldsAndExactVersionContextAreNecessary(string original, string replacement)
    {
        RecordEnvelope publication = Publication();
        var entry = Entry(publication, text: Text(publication).Replace(original, replacement, StringComparison.Ordinal));
        Assert.Equal("unmet", Assert.Single(Run([publication, entry.Pin, Review(entry.Pin)], [entry.File]).Findings).Verdict);
    }

    [Theory]
    [InlineData("id: entry", "id: entry\nid: duplicate")]
    [InlineData("title: Release account", "title: !execute text")]
    [InlineData("title: Release account", "title: &alias text")]
    [InlineData("title: Release account", "title: *alias")]
    [InlineData("title: Release account", "title: [wrong, type]")]
    [InlineData("title: Release account", "title: true")]
    [InlineData("release:\n", "release: {product: product, product: product}\n")]
    public void MalformedMetadataMakesTheWholeSourceUnavailable(string original, string replacement)
    {
        RecordEnvelope publication = Publication();
        var entry = Entry(publication, text: Text(publication).Replace(original, replacement, StringComparison.Ordinal));
        PolicyEvaluation result = Run([publication, entry.Pin, Review(entry.Pin)], [entry.File]);
        Assert.False(result.Complete);
        Assert.Equal("unavailable", Assert.Single(result.Findings).Verdict);
        Assert.Equal(0, Assert.Single(result.Rules).Subjects);
    }

    [Fact]
    public void BlockAndFlowMetadataAndQuotedProseHaveTheSameMeaning()
    {
        RecordEnvelope publication = Publication();
        string text = Text(publication).Replace("summary: What changed", "summary: |-\n  What changed", StringComparison.Ordinal)
            .Replace("title: Release account", "title: 'Release: account # one'", StringComparison.Ordinal)
            .Replace("  - {type: release, id: publication, hash: " + publication.ComputedHash + "}",
                "  - type: release\n    id: publication\n    hash: '" + publication.ComputedHash + "'", StringComparison.Ordinal);
        var entry = Entry(publication, text: text);
        Assert.Equal("satisfied", Assert.Single(Run([publication, entry.Pin, Review(entry.Pin)], [entry.File]).Findings).Verdict);
    }

    [Fact]
    public void MissingAndConflictingSourcesNeverBecomeAnEmptySuccessfulInventory()
    {
        RecordEnvelope publication = Publication();
        var entry = Entry(publication);
        Assert.Equal("unmet", Assert.Single(Run([publication], [entry.File]).Findings).Verdict);
        Assert.Equal("unavailable", Assert.Single(Run([publication], [entry.File with { Available = false }]).Findings).Verdict);
        Assert.Equal("unavailable", Assert.Single(Run([publication], [entry.File, entry.File with { Path = entry.File.Path.ToUpperInvariant() }]).Findings).Verdict);
        PolicyBasis basis = Basis([Policy(), publication]);
        Assert.Equal("unavailable", Assert.Single(PolicyEvaluator.Evaluate(basis, Kinds).Findings).Verdict);
    }

    [Fact]
    public void DebtHoldsOnlyLaterSubmissionsForTheSameProductAndNeverThePublicationItDescribes()
    {
        RecordEnvelope publication = Publication();
        var same = new PolicySubmission(publication.Reference, "product", "0.8.0", new string('a', 40));
        Assert.Equal(0, Run([publication], submission: same).Holds);
        var next = same with { Release = new("release", "candidate", PolicyBasis.Hash("candidate")), Version = "0.9.0" };
        Assert.Equal(1, Run([publication], submission: next).Holds);
        Assert.Equal(0, Run([publication], submission: next with { Product = "another" }).Holds);
        Assert.Equal(1, Run([publication], submission: next with { Version = "1.0.0" }).Holds);
        Assert.Equal(1, Run([publication]).Holds);
    }

    [Fact]
    public void StrictParametersAndSelectionsDoNotSilentlySelectNothing()
    {
        JsonObject body = JsonNode.Parse(Policy().Body.GetRawText())!.AsObject();
        body["rules"]![0]!["parameters"]!["extra"] = "unsupported";
        Assert.Equal(1, Run([], policy: R("estate-policy", "policy", body)).Errors);
        body["rules"]![0]!["parameters"]!.AsObject().Remove("extra");
        body["appliesTo"] = new JsonObject { ["kind"] = "product", ["product"] = "other" };
        Assert.Equal(1, Run([], policy: R("estate-policy", "policy", body)).Errors);
    }

    [Fact]
    public void CompleteOutputIsStableAcrossInputOrderAndCultures()
    {
        RecordEnvelope publication = Publication();
        var entry = Entry(publication);
        RecordEnvelope[] records = [publication, entry.Pin, Review(entry.Pin), Publication("another", version: "0.9.0")];
        string expected = Run(records, [entry.File]).Canonical();
        System.Globalization.CultureInfo saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "tr-TR", "ar-SA", "fr-FR" })
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
                Assert.Equal(expected, Run(records.Reverse().ToArray(), [entry.File]).Canonical());
            }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void ARealPublicationExceptionCoversOnlyItsExactNextSubmissionUntilExclusiveExpiry()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope product = DutyScheduleSourceTests.Product(fixture, "example");
        JsonObject body = fixture.PolicyBody(duty: false);
        body["rules"] = new JsonArray(new JsonObject
        {
            ["key"] = "entry", ["kind"] = ReleaseEntry.Kind, ["version"] = 1,
            ["parameters"] = new JsonObject { ["product"] = product.Id, ["entryKind"] = "devlog", ["source"] = "estate-draft/1", ["directory"] = Directory,
                ["fromPublication"] = "after-declaration", ["review"] = "independent" },
        });
        RecordEnvelope policy = fixture.Policy(body);
        fixture.Estate.SetClock(DateTimeOffset.Parse("2026-10-13T14:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        RecordEnvelope candidate = DutyScheduleSourceTests.Candidate(fixture, product, "0.8.0");
        fixture.Add("acceptance", new JsonObject { ["statement"] = "Accept fixture publication.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
        JsonObject published = PolicyFixture.Body(candidate); published["status"] = "published";
        published["publishedAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock); published["url"] = "https://example.invalid/releases/fixture";
        RecordEnvelope publication = fixture.Add("release", published, subject: candidate.Subject, supersedes: candidate.Id);
        RecordEnvelope next = DutyScheduleSourceTests.Candidate(fixture, product, "0.9.0");
        DateTimeOffset expiry = fixture.Estate.Clock.AddDays(1);
        fixture.Add("policy-exception", new JsonObject
        {
            ["rule"] = "entry", ["reason"] = "A bounded fixture delay.", ["expiresAt"] = RecordParser.FormatTimestamp(expiry), ["state"] = "granted",
            ["basis"] = new JsonObject { ["decision"] = PolicyFixture.Ref(fixture.Decision) },
            ["targets"] = new JsonArray(new JsonObject { ["kind"] = "publication", ["publication"] = PolicyFixture.Ref(publication),
                ["submission"] = new JsonObject { ["release"] = PolicyFixture.Ref(next), ["product"] = product.Id, ["version"] = "0.9.0" } }),
        }, subject: policy.Reference);
        fixture.AssertValid();
        var submission = new PolicySubmission(next.Reference, product.Id, "0.9.0", new string('1', 40));
        PolicyEvaluation Check(PolicySubmission proposed, DateTimeOffset at) => PolicyEvaluator.Evaluate(new(fixture.Estate.Load(), RecordParser.FormatTimestamp(at), PolicyTimeOrigin.Submission,
            [new(fixture.Estate.Estate.Id, "working-tree", PolicyBasis.RepositoryContract, true, [])], implementations: Kinds.Implementations, submission: proposed), Kinds);
        PolicyFinding excepted = Assert.Single(Check(submission, fixture.Estate.Clock).Findings);
        Assert.Equal("unmet", excepted.Verdict);
        Assert.Equal("excepted", excepted.Disposition);
        Assert.False(excepted.ReleaseHold);
        Assert.True(Assert.Single(Check(submission, expiry).Findings).ReleaseHold);
        Assert.True(Assert.Single(Check(submission with { Version = "1.0.0" }, fixture.Estate.Clock).Findings).ReleaseHold);
    }

    private static RecordEnvelope Policy(object? from = null, string review = "independent") => P(ReleaseEntry.Kind,
        new { product = "product", entryKind = "devlog", source = "estate-draft/1", directory = Directory, fromPublication = from ?? "after-declaration", review });

    private static RecordEnvelope Publication(string id = "publication", string status = "published", string version = "0.8.0", string? supersedes = null, string at = PublishedAt) =>
        R("release", id, new { status, version }, at, new("product", "product", null), supersedes);

    private static (PolicyFile File, RecordEnvelope Pin) Entry(RecordEnvelope publication, string path = "entry", string at = WrittenAt, string? text = null)
    {
        PolicyFile file = PolicyBasisTests.File(Directory + "/" + path + ".md", text ?? Text(publication));
        return (file, Pin(file, path + "-pin", at));
    }

    private static RecordEnvelope Pin(PolicyFile file, string id, string at, string? supersedes = null, int revision = 1) =>
        R("document-revision", id, new { path = file.Path, hash = file.Hash, bytes = file.Bytes.Length, revision }, at, supersedes: supersedes, by: "author");

    private static RecordEnvelope Review(RecordEnvelope pin, string id = "review", string outcome = "pass", string independence = "independent-agent", string by = "reviewer") =>
        R("review", id, new { outcome, independence }, ReviewedAt, pin.Reference, by: by);

    private static string Text(RecordEnvelope publication) => $$"""
        ---
        kind: devlog
        id: entry
        title: Release account
        summary: What changed
        audience: internal
        applicability: {product: Example, versions: {{publication.Body.GetProperty("version").GetString()}}}
        release:
          product: product
          version: {{publication.Body.GetProperty("version").GetString()}}
          publication: {id: {{publication.Id}}, hash: {{publication.ComputedHash}}}
        sources:
          - {type: release, id: {{publication.Id}}, hash: {{publication.ComputedHash}}}
        sites: []
        drafted-by: author
        status: draft
        ---
        The release account.
        """;

    private static PolicyEvaluation Run(RecordEnvelope[] records, PolicyFile[]? files = null, PolicyObject[]? objects = null, RecordEnvelope? policy = null, PolicySubmission? submission = null)
    {
        PolicyBasis basis = Basis([policy ?? Policy(), .. records]);
        return PolicyEvaluator.Evaluate(new(basis.Model, basis.AsOf, submission is null ? basis.Origin : PolicyTimeOrigin.Submission,
            [Repository(files ?? [])], objects, Kinds.Implementations, submission), Kinds);
    }
}
