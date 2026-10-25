// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public sealed class ReleaseReviewRecordTests
{
    /// <summary>A recorded estate holding an approved release review, for the golden round trip of later format-3 fields.</summary>
    public static PolicyFixture RecordedFixture()
    {
        var f = new Fixture();
        f.Records.Add("decision", f.Approval());
        return f.Records;
    }

    [Theory]
    [InlineData("full")]
    [InlineData("focused")]
    public void ADecisionCanApproveAnExactCandidateAndWorkOrder(string scope)
    {
        using var f = new Fixture();
        f.Records.Add("decision", f.Approval(scope));
        f.Records.AssertValid();
    }

    [Theory]
    [InlineData("candidate-hash")]
    [InlineData("candidate-type")]
    [InlineData("candidate-missing-hash")]
    [InlineData("candidate-missing")]
    [InlineData("work-hash")]
    [InlineData("work-type")]
    [InlineData("scope")]
    [InlineData("focus-empty")]
    [InlineData("focus-full")]
    [InlineData("probes-missing")]
    [InlineData("unexpected")]
    public void InvalidScopeDeclarationsCannotBecomeReviewAuthority(string fault)
    {
        using var f = new Fixture();
        JsonObject approval = f.Approval();
        JsonObject review = approval["releaseReview"]!.AsObject();
        switch (fault)
        {
            case "candidate-hash": review["candidate"]!["hash"] = "sha256:" + new string('f', 64); break;
            case "candidate-type": review["candidate"]!["type"] = "product"; break;
            case "candidate-missing-hash": review["candidate"]!.AsObject().Remove("hash"); break;
            case "candidate-missing": review.Remove("candidate"); break;
            case "work-hash": review["workOrder"]!["hash"] = "sha256:" + new string('f', 64); break;
            case "work-type": review["workOrder"] = PolicyFixture.Ref(f.Product); break;
            case "scope": review["scope"] = "assumed"; break;
            case "focus-empty": review["focus"] = new JsonArray(); break;
            case "focus-full": review["scope"] = "full"; break;
            case "probes-missing": review.Remove("probes"); break;
            case "unexpected": review["permission"] = "publication"; break;
        }
        f.Records.Add("decision", approval);
        Assert.Contains(f.Records.Estate.Load().Findings, finding => finding.Severity == Severity.Error && finding.Location.Contains("releaseReview", StringComparison.Ordinal));
    }

    [Fact]
    public void AWithdrawnReleaseCannotBeNamedAsTheCandidate()
    {
        using var f = new Fixture();
        JsonObject release = PolicyFixture.Body(f.Candidate);
        release["status"] = "withdrawn";
        RecordEnvelope withdrawn = f.Records.Add("release", release, subject: f.Candidate.Subject, supersedes: f.Candidate.Id);
        JsonObject approval = f.Approval();
        approval["releaseReview"]!["candidate"] = PolicyFixture.Ref(withdrawn);
        f.Records.Add("decision", approval);
        Assert.Contains(f.Records.Estate.Load().Findings, finding => finding.Code == "decision.release-review");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EarlierRecordFormatsDoNotAdmitTheNewDeclaration(long format)
    {
        using var f = new Fixture(format);
        f.Records.Add("decision", f.Approval());
        Assert.Contains(f.Records.Estate.Load().Findings, finding => finding.Code == "body.format" && finding.Location.EndsWith("releaseReview", StringComparison.Ordinal));
    }

    [Fact]
    public void RetainedTextDoesNotRelaxRequiredAssignmentFieldsOrPinnedSources()
    {
        using var f = new Fixture();
        RecordEnvelope attempt = f.Records.BeginAttempt(order: f.Order);
        var model = f.Records.Estate.Load();
        RecordEnvelope assignment = model.ById[attempt.Subject!.Id];
        JsonObject body = PolicyFixture.Body(assignment);
        body["brief"]!["reviewBrief"] = "# A retained release review\n";
        body["brief"]!["sources"] = new JsonArray(new JsonObject { ["type"] = "release", ["id"] = f.Candidate.Id, ["hash"] = "sha256:" + new string('f', 64) });
        body["brief"]!.AsObject().Remove("permittedChanges");
        f.Records.Add("assignment", body, subject: f.Order.Reference, supersedes: assignment.Id);
        var findings = f.Records.Estate.Load().Findings;
        Assert.Contains(findings, finding => finding.Code == "assignment.source-hash");
        Assert.Contains(findings, finding => finding.Code == "body.missing" && finding.Location.EndsWith("permittedChanges", StringComparison.Ordinal));
    }

    private sealed class Fixture : IDisposable
    {
        internal PolicyFixture Records { get; }
        internal RecordEnvelope Product { get; }
        internal RecordEnvelope Order { get; }
        internal RecordEnvelope Candidate { get; }

        internal Fixture(long format = 3)
        {
            Records = new PolicyFixture(format);
            Product = Records.Add("product", new()
            {
                ["name"] = "Widget", ["slug"] = "widget", ["summary"] = "A review fixture.",
                ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Fixture." }),
                ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Fixture." }),
            });
            Order = Records.Add("work-order", new()
            {
                ["key"] = "REVIEW-1", ["kind"] = "ordinary", ["title"] = "Review the candidate", ["intent"] = "Compare the recorded evidence.",
                ["scope"] = new JsonArray("release review"), ["exitQuestion"] = "Is the candidate reviewed?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("review result"),
            });
            Candidate = Records.Add("release", new()
            {
                ["version"] = "1.0.0", ["repository"] = "Example/widget", ["commit"] = new string('a', 40), ["status"] = "candidate",
                ["artifacts"] = new JsonArray(new JsonObject { ["name"] = "widget.zip", ["hash"] = "sha256:" + new string('b', 64), ["bytes"] = 10, ["platform"] = "any" }),
                ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(format), ["writes"] = format }, ["evidence"] = new JsonArray(),
                ["qualified"] = new JsonArray(), ["signing"] = "unsigned fixture", ["limitations"] = new JsonArray(),
            }, subject: Product.Reference);
        }

        internal JsonObject Approval(string scope = "focused")
        {
            JsonObject body = Records.DecisionBody();
            body["releaseReview"] = new JsonObject
            {
                ["candidate"] = PolicyFixture.Ref(Candidate), ["workOrder"] = PolicyFixture.Ref(Order), ["scope"] = scope,
                ["focus"] = scope == "focused" ? new JsonArray("The recorded change.") : new JsonArray(), ["probes"] = new JsonArray(),
            };
            return body;
        }

        public void Dispose() => Records.Dispose();
    }
}
