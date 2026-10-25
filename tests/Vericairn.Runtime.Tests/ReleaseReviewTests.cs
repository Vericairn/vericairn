// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class ReleaseReviewTests
{
    [Theory]
    [InlineData("focused")]
    [InlineData("full")]
    public void EveryBriefSectionMatchesItsExactRecordsAndAssignment(string scope)
    {
        using var f = new ReleaseReviewFixture(scope);
        int before = f.Model.Records.Count;
        using EstateService readOnly = EstateService.OpenReadOnly(f.Records.Estate.Root);
        ReleaseReviewBriefResponse brief = readOnly.ReleaseReviewBrief(ReleaseReviewFixture.Version);
        Assert.True(brief.Ok, brief.Problem?.Message);
        Assert.Null(brief.Assignment);
        Assert.Equal(before, f.Model.Records.Count);
        Assert.Equal(f.Reviewer.Id, brief.Reviewer);
        Assert.Equal("unavailable", brief.RequestedProgress);
        Assert.Equal(ContentHash.Sha256(Encoding.UTF8.GetBytes(brief.Text)), brief.Hash);
        Assert.DoesNotContain('\r', brief.Text);

        RecordEnvelope[] sources = [f.Candidate, f.Previous, f.Merge, f.Decision, f.Review, f.Disposition, f.Order, f.Reviewer];
        foreach (RecordEnvelope record in sources) AssertRecord(brief.Text, record);
        Assert.Equal(sources.Select(r => r.Id).Order(StringComparer.Ordinal), brief.Brief!.Value.GetProperty("sources").EnumerateArray()
            .Select(r => r.GetProperty("id").GetString()).Order(StringComparer.Ordinal));
        foreach (JsonElement source in brief.Brief.Value.GetProperty("sources").EnumerateArray())
        {
            RecordEnvelope record = f.Model.ById[source.GetProperty("id").GetString()!];
            Assert.Equal(record.Type, source.GetProperty("type").GetString());
            Assert.Equal(record.ComputedHash, source.GetProperty("hash").GetString());
        }
        foreach (RecordEnvelope excluded in new[] { f.OldMerge, f.Pending, f.FutureMerge }) Assert.DoesNotContain(excluded.Id, brief.Text, StringComparison.Ordinal);
        Assert.Contains("# Release review: " + ReleaseReviewFixture.Version + " (" + scope + ")\n", brief.Text, StringComparison.Ordinal);
        Assert.Equal(brief.Text, f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version).Text);

        ReleaseReviewBriefResponse assigned = f.Api.ReleaseReviewAssign(ReleaseReviewFixture.Version, new(f.Operator, brief.Hash!));
        Assert.True(assigned.Ok, JsonSerializer.Serialize(assigned.Problem, ProtocolJson.Options));
        Assert.Equal(before + 1, f.Model.Records.Count);
        RecordEnvelope assignment = f.Model.ById[assigned.Assignment!.Id];
        Assert.Equal("assignment", assignment.Type);
        Assert.Equal(f.Reviewer.Id, EstateModel.BodyString(assignment, "contributor"));
        Assert.Equal(f.Order.Reference, assignment.Subject);
        Assert.Equal(brief.Hash, assigned.Hash);
        Assert.Equal(brief.Text, assigned.Text);
        Assert.Equal(brief.Text, assignment.Body.GetProperty("brief").GetProperty("reviewBrief").GetString());
        Assert.True(JsonElement.DeepEquals(brief.Brief.Value, assignment.Body.GetProperty("brief")));
        Assert.Contains(brief.Text, f.Api.BriefContext(f.Order.Id)!.Markdown, StringComparison.Ordinal);
        Assert.Equal(before + 1, f.Model.Records.Count);
        f.Records.AssertValid();
    }

    [Fact]
    public void AChangedDispositionRefusesThePreparedHashWithoutAnAssignment()
    {
        using var f = new ReleaseReviewFixture();
        ReleaseReviewBriefResponse brief = f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version);
        JsonObject body = PolicyFixture.Body(f.Disposition);
        body["effects"] = new JsonArray("Repeat the output and artifact probes before accepting the finding's disposition.");
        f.Records.Add("decision", body, subject: f.Review.Reference, supersedes: f.Disposition.Id);
        int before = f.Model.Records.Count;
        ReleaseReviewBriefResponse changed = f.Api.ReleaseReviewAssign(ReleaseReviewFixture.Version, new(f.Operator, brief.Hash!));
        Assert.Equal("conflict", changed.Problem!.Code);
        Assert.Null(changed.Assignment);
        Assert.NotEqual(brief.Hash, changed.Hash);
        Assert.Equal(before, f.Model.Records.Count);
    }

    [Fact]
    public void DecisionRevisionsReplaceTheApprovedScopeWithoutReadingProseAsAuthority()
    {
        using var f = new ReleaseReviewFixture();
        RecordEnvelope full = f.Approve("full", f.Decision);
        ReleaseReviewBriefResponse response = f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version);
        Assert.True(response.Ok, response.Problem?.Message);
        AssertRecord(response.Text, full);
        Assert.DoesNotContain(f.Decision.Id, response.Text, StringComparison.Ordinal);
        Assert.Equal("not-found", f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version, decision: f.Decision.Id).Problem!.Code);
        JsonObject body = PolicyFixture.Body(full);
        body.Remove("releaseReview");
        body["effects"] = new JsonArray("A full review is mentioned in prose, without an exact scope declaration.");
        f.Records.Add("decision", body, supersedes: full.Id);
        Assert.Equal("not-found", f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version).Problem!.Code);
    }

    [Fact]
    public void TheScopeCannotFollowAReplacedCandidateOrWorkOrder()
    {
        using var f = new ReleaseReviewFixture();
        RecordEnvelope revised = f.AddRelease(ReleaseReviewFixture.Version, f.Candidate);
        Assert.Equal("not-found", f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version).Problem!.Code);
        f.Approve("full", f.Decision, revised);
        JsonObject order = PolicyFixture.Body(f.Order);
        order["scope"] = new JsonArray("A different review scope.");
        f.Records.Add("work-order", order, supersedes: f.Order.Id);
        Assert.Equal("conflict", f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version).Problem!.Code);
    }

    [Fact]
    public void AnAmbiguousScopeRequiresAnExplicitCurrentDecision()
    {
        using var f = new ReleaseReviewFixture();
        RecordEnvelope full = f.Approve("full");
        Assert.Equal("ambiguous", f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version).Problem!.Code);
        ReleaseReviewBriefResponse response = f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version, decision: full.Id);
        Assert.True(response.Ok, response.Problem?.Message);
        AssertRecord(response.Text, full);
    }

    [Fact]
    public void ReviewerRoleResolutionFollowsPrincipalRevisionsAndRejectsAmbiguity()
    {
        using var f = new ReleaseReviewFixture();
        JsonObject body = PolicyFixture.Body(f.Reviewer);
        body["name"] = "Another display name";
        RecordEnvelope revised = f.Records.Add("principal", body, supersedes: f.Reviewer.Id);
        ReleaseReviewBriefResponse response = f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version);
        Assert.True(response.Ok, response.Problem?.Message);
        Assert.Equal(f.Reviewer.Id, response.Reviewer);
        AssertRecord(response.Text, revised);
        f.Records.Add("principal", body);
        Assert.Equal("ambiguous", f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version).Problem!.Code);
    }

    [Fact]
    public void AnUnprivilegedPrincipalCannotAssignTheReview()
    {
        using var f = new ReleaseReviewFixture();
        ReleaseReviewBriefResponse brief = f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version);
        int before = f.Model.Records.Count;
        ReleaseReviewBriefResponse response = f.Api.ReleaseReviewAssign(ReleaseReviewFixture.Version, new(f.Reviewer.Id, brief.Hash!));
        Assert.False(response.Ok);
        Assert.Null(response.Assignment);
        Assert.Equal(before, f.Model.Records.Count);
    }

    [Fact]
    public void AReadOnlyServiceCannotStoreTheAssignment()
    {
        using var f = new ReleaseReviewFixture();
        using EstateService readOnly = EstateService.OpenReadOnly(f.Records.Estate.Root);
        ReleaseReviewBriefResponse brief = readOnly.ReleaseReviewBrief(ReleaseReviewFixture.Version);
        int before = f.Model.Records.Count;
        ReleaseReviewBriefResponse response = readOnly.ReleaseReviewAssign(ReleaseReviewFixture.Version, new(f.Operator, brief.Hash!));
        Assert.False(response.Ok);
        Assert.Null(response.Assignment);
        Assert.Equal(before, f.Model.Records.Count);
    }

    [Fact]
    public void LaterMergeRevisionsDoNotRewriteTheCandidatesChangeWindow()
    {
        using var f = new ReleaseReviewFixture();
        string before = f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version).Text;
        JsonObject body = PolicyFixture.Body(f.Pending);
        body["status"] = "dispatched";
        body["dispatchedAt"] = RecordParser.FormatTimestamp(f.Records.Estate.Clock.AddSeconds(1));
        RecordEnvelope dispatched = f.Records.Add("operation", body, subject: f.Pending.Subject, supersedes: f.Pending.Id);
        body["status"] = "observed-success";
        body["observed"] = new JsonObject { ["summary"] = "The merge was observed later.", ["at"] = RecordParser.FormatTimestamp(f.Records.Estate.Clock.AddSeconds(1)) };
        body["target"]!["mergeCommit"] = new string('e', 40);
        f.Records.Add("operation", body, subject: f.Pending.Subject, supersedes: dispatched.Id);
        f.Records.AssertValid();
        Assert.Equal(before, f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version).Text);
    }

    [Fact]
    public void AFirstCandidateReportsMissingHistoryWithoutInventingEvidence()
    {
        using var f = new ReleaseReviewFixture();
        RecordEnvelope first = f.Model.OfType("release").OrderBy(r => r.At, StringComparer.Ordinal).First();
        f.Approve("full", candidate: first);
        ReleaseReviewBriefResponse response = f.Api.ReleaseReviewBrief("1.0.0");
        Assert.True(response.Ok, response.Problem?.Message);
        Assert.Contains("## Previous candidate or release\n\nNone recorded.", response.Text, StringComparison.Ordinal);
        Assert.Contains("## Merged changes in the recorded window\n\nNone recorded.", response.Text, StringComparison.Ordinal);
        Assert.Contains("## Previous review findings\n\nNone recorded.", response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(f.Merge.Id, response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewerUnreviewedCandidateDoesNotHideTheLastReviewsFindings()
    {
        using var f = new ReleaseReviewFixture();
        RecordEnvelope next = f.AddRelease("1.3.0");
        f.Approve("full", candidate: next);
        ReleaseReviewBriefResponse response = f.Api.ReleaseReviewBrief("1.3.0");
        Assert.True(response.Ok, response.Problem?.Message);
        AssertRecord(response.Text, f.Candidate);
        AssertRecord(response.Text, f.Review);
        AssertRecord(response.Text, f.Disposition);
        AssertRecord(response.Text, f.FutureMerge);
        Assert.DoesNotContain(f.Merge.Id, response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductSelectionDisambiguatesVersionsAndFollowsProductRevisions()
    {
        using var f = new ReleaseReviewFixture();
        JsonObject product = PolicyFixture.Body(f.Product);
        product["slug"] = "another-widget";
        RecordEnvelope other = f.Records.Add("product", product);
        f.Records.Add("release", PolicyFixture.Body(f.Candidate), subject: other.Reference);
        Assert.Equal("ambiguous", f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version).Problem!.Code);
        string identity = f.Model.Chain(f.Product)[0];
        ReleaseReviewBriefResponse response = f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version, identity);
        Assert.True(response.Ok, response.Problem?.Message);
        AssertRecord(response.Text, f.Candidate);
        AssertRecord(response.Text, f.Merge);
        Assert.Equal("not-found", f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version, "absent").Problem!.Code);
    }

    [Fact]
    public void ADispositionMustPinTheReviewHash()
    {
        using var f = new ReleaseReviewFixture();
        JsonObject disposition = PolicyFixture.Body(f.Disposition);
        RecordEnvelope unpinned = f.Records.Add("decision", disposition, subject: f.Review.Reference with { Hash = null }, supersedes: f.Disposition.Id);
        ReleaseReviewBriefResponse response = f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version);
        Assert.True(response.Ok, response.Problem?.Message);
        AssertRecord(response.Text, f.Review);
        Assert.DoesNotContain(unpinned.Id, response.Text, StringComparison.Ordinal);
        Assert.Contains("## Disposition decisions pinned to those reviews\n\nNone recorded.", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AReviewerWhoAuthoredTheCandidateCannotBeAssigned()
    {
        using var f = new ReleaseReviewFixture();
        JsonObject reviewer = PolicyFixture.Body(f.Reviewer);
        reviewer["roles"] = new JsonArray("contributor");
        f.Records.Add("principal", reviewer, supersedes: f.Reviewer.Id);
        JsonObject author = PolicyFixture.Body(f.Records.Estate.Operator);
        author["roles"] = new JsonArray("operator", "reviewer");
        f.Records.Add("principal", author, supersedes: f.Operator);
        int before = f.Model.Records.Count;
        ReleaseReviewBriefResponse response = f.Api.ReleaseReviewAssign(ReleaseReviewFixture.Version, new(f.Operator, "prepared"));
        Assert.Equal("refused", response.Problem!.Code);
        Assert.Contains("authored", response.Problem.Message, StringComparison.Ordinal);
        Assert.Null(response.Assignment);
        Assert.Equal(before, f.Model.Records.Count);
    }

    [Fact]
    public void AWithdrawnCandidateIsNotPreparedOrAssigned()
    {
        using var f = new ReleaseReviewFixture();
        JsonObject body = PolicyFixture.Body(f.Candidate);
        body["status"] = "withdrawn";
        f.Records.Add("release", body, subject: f.Candidate.Subject, supersedes: f.Candidate.Id);
        int before = f.Model.Records.Count;
        Assert.Equal("refused", f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version).Problem!.Code);
        ReleaseReviewBriefResponse assigned = f.Api.ReleaseReviewAssign(ReleaseReviewFixture.Version, new(f.Operator, "prepared"));
        Assert.Equal("refused", assigned.Problem!.Code);
        Assert.Null(assigned.Assignment);
        Assert.Equal(before, f.Model.Records.Count);
    }

    private static void AssertRecord(string text, RecordEnvelope record)
    {
        string heading = "### " + record.Type + " " + record.Id + " at " + record.ComputedHash + "\n";
        int start = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, "The brief must name the exact source record and hash.");
        string section = text[(start + heading.Length)..];
        int end = section.IndexOf("\n#", StringComparison.Ordinal);
        if (end >= 0) section = section[..end];
        string json = string.Join('\n', section.Split('\n').Where(line => line.StartsWith("    ", StringComparison.Ordinal)).Select(line => line[4..]));
        using JsonDocument rendered = JsonDocument.Parse(json);
        Assert.True(JsonElement.DeepEquals(record.Body, rendered.RootElement), "The complete source body must survive rendering without rewritten facts.");
    }
}
