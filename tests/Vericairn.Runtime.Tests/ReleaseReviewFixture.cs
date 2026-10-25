// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>Record-only review evidence with admitted writers in the test process's isolated home.</summary>
public sealed class ReleaseReviewFixture : IDisposable
{
    private readonly ReleasePlanningFixture planning = new();
    public PolicyFixture Records => planning.Records;
    public EstateService Api => planning.Api;
    public EstateModel Model => planning.Model;
    public string Operator => planning.Operator;
    public RecordEnvelope Product { get; }
    public RecordEnvelope Repository { get; }
    public RecordEnvelope Reviewer { get; }
    public RecordEnvelope Order { get; }
    public RecordEnvelope Previous { get; }
    public RecordEnvelope Candidate { get; }
    public RecordEnvelope Review { get; }
    public RecordEnvelope Disposition { get; }
    public RecordEnvelope Decision { get; }
    public RecordEnvelope Merge { get; }
    public RecordEnvelope OldMerge { get; }
    public RecordEnvelope FutureMerge { get; }
    public RecordEnvelope Pending { get; }
    public const string Version = "1.2.0";

    public ReleaseReviewFixture(string scope = "focused")
    {
        Repository = Records.Add("resource", new()
        {
            ["kind"] = "repository", ["name"] = "Widget", ["role"] = "Source",
            ["location"] = new JsonObject { ["github"] = "Example/widget" }, ["visibility"] = "private",
        });
        JsonObject product = PolicyFixture.Body(planning.Product);
        product["changes"] = new JsonObject { ["repository"] = Repository.Id, ["provider"] = "fixture", ["target"] = "main", ["requiredChecks"] = new JsonArray("tests") };
        Product = Records.Add("product", product, supersedes: planning.Product.Id);
        Reviewer = Records.Add("principal", new()
        {
            ["kind"] = "agent", ["name"] = "Release assessor", ["roles"] = new JsonArray("reviewer"),
            ["agent"] = new JsonObject { ["vendor"] = "Example", ["product"] = "Review harness" },
        });
        Order = Records.Add("work-order", new()
        {
            ["key"] = "REVIEW-1", ["kind"] = "ordinary", ["title"] = "Review the candidate", ["intent"] = "Inspect the recorded release evidence.",
            ["scope"] = new JsonArray("The approved release review."), ["exitQuestion"] = "Has the candidate been reviewed?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("Review result"),
        });
        OldMerge = AddMerge("10");
        Previous = AddRelease("1.1.0");
        Review = Records.Add("review", new()
        {
            ["independence"] = "independent-agent", ["outcome"] = "findings", ["summary"] = "Checked the prior candidate and its repeatable probes.",
            ["findings"] = new JsonArray(new JsonObject { ["severity"] = "warning", ["summary"] = "The command omits a recorded limitation.", ["location"] = "release output" }),
        }, builder: true, subject: Previous.Reference);
        JsonObject disposition = Records.DecisionBody();
        disposition["title"] = "Disposition of the prior review";
        disposition["effects"] = new JsonArray("Correct the omitted limitation and repeat the output probe.");
        Disposition = Records.Add("decision", disposition, subject: Review.Reference);
        Merge = AddMerge("11");
        Pending = AddMerge("12", complete: false);
        Candidate = AddRelease(Version);
        FutureMerge = AddMerge("13");
        Decision = Approve(scope);
        Records.AssertValid();
    }

    public RecordEnvelope Approve(string scope, RecordEnvelope? previous = null, RecordEnvelope? candidate = null)
    {
        JsonObject body = Records.DecisionBody();
        body["releaseReview"] = new JsonObject
        {
            ["candidate"] = PolicyFixture.Ref(candidate ?? Candidate), ["workOrder"] = PolicyFixture.Ref(Order), ["scope"] = scope,
            ["focus"] = scope == "focused" ? new JsonArray("The changed release output and the prior finding.") : new JsonArray(),
            ["probes"] = new JsonArray("Compare every candidate limitation with the release output.", "Repeat the recorded three-platform checks."),
        };
        return Records.Add("decision", body, supersedes: previous?.Id);
    }

    public RecordEnvelope AddRelease(string version, RecordEnvelope? previous = null)
    {
        JsonObject body = PolicyFixture.Body(planning.Candidate);
        body["version"] = version;
        body["commit"] = new string(version == Version ? 'c' : 'b', 40);
        body["evidence"] = new JsonArray(new JsonObject { ["summary"] = "Three-platform run passed at the candidate commit.", ["location"] = "https://example.invalid/runs/42", ["hash"] = "sha256:" + new string('e', 64) });
        body["qualified"] = new JsonArray("linux-x64", "osx-arm64", "win-x64");
        body["limitations"] = new JsonArray("The review harness requires an operator-configured launcher.");
        return Records.Add("release", body, subject: Product.Reference, supersedes: previous?.Id);
    }

    public RecordEnvelope AddMerge(string number, bool complete = true)
    {
        string head = new('d', 40);
        JsonObject body = new()
        {
            ["provider"] = "fixture", ["action"] = "change-merge", ["identity"] = "fixture:merge:" + number,
            ["intent"] = "Merge the reviewed output change.", ["status"] = "not-started",
            ["target"] = new JsonObject
            {
                ["head"] = head, ["product"] = Product.Id, ["productHash"] = Product.ComputedHash,
                ["repository"] = Repository.Id, ["repositoryHash"] = Repository.ComputedHash, ["destination"] = "Example/widget",
                ["target"] = "main", ["pullRequest"] = number,
            },
            ["requiredChecks"] = new JsonArray("tests"),
            ["checks"] = new JsonArray(new JsonObject { ["name"] = "tests", ["head"] = head, ["status"] = "completed", ["conclusion"] = "success" }),
            ["evidence"] = new JsonArray(new JsonObject { ["summary"] = "Checked pull request " + number }),
        };
        RecordEnvelope intent = Records.Add("operation", body, subject: Product.Reference);
        if (!complete) return intent;
        body["status"] = "dispatched";
        body["dispatchedAt"] = RecordParser.FormatTimestamp(Records.Estate.Clock.AddSeconds(1));
        RecordEnvelope dispatched = Records.Add("operation", body, subject: Product.Reference, supersedes: intent.Id);
        body["status"] = "observed-success";
        body["observed"] = new JsonObject { ["summary"] = "The merge was observed.", ["at"] = RecordParser.FormatTimestamp(Records.Estate.Clock.AddSeconds(1)) };
        body["target"]!["mergeCommit"] = new string('f', 40);
        return Records.Add("operation", body, subject: Product.Reference, supersedes: dispatched.Id);
    }

    public void Dispose() => planning.Dispose();
}
