// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>
/// Standing outputs at the record (from 0.9.4, GOAL-08; the estate's design/standing-agents.md §§ 8 to 10 and 12): every output of an
/// occurrence names what raised it and is recorded by the assignment's contributor in its run; a possibility carries a fingerprint and its
/// sources, a second open one with the fingerprint is refused, a declined one is raised again only by naming it with new evidence, an
/// occurrence stays within its caps with extras in one summary possibility, open outputs at the plan's cap defer dispatch, and no agent
/// disposes of its own output.
/// </summary>
public class StandingOutputTests
{
    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);

    /// <summary>What validating a record not yet written finds, against the estate as it stands, with the record among it.</summary>
    private static FindingList Validate(StandingFixture fixture, string type, JsonObject body, RecordReference? subject = null, string? supersedes = null, bool builder = false)
    {
        fixture.Estate.SetClock(fixture.Estate.Clock.AddSeconds(1));
        string by = builder ? fixture.Estate.BuilderId : fixture.Estate.OperatorId;
        AuthoredRecord record = RecordAuthor.Create(new RecordDraft(type, by, Element(body), subject, supersedes, fixture.Estate.Clock, null, 3));
        var findings = new FindingList();
        RecordTypeRegistry.Find(type)!.Validate(new ValidationContext(fixture.Estate.Load(), record.Envelope, findings));
        return findings;
    }

    private static string Errors(FindingList findings) => string.Join('\n', findings.Where(f => f.Severity == Severity.Error).Select(f => $"{f.Code}: {f.Message}"));

    /// <summary>An occurrence carried by the builder, in a run in flight.</summary>
    private static (RecordEnvelope Assignment, RecordEnvelope Run, string Key) Carried(StandingFixture fixture)
    {
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence);
        RecordEnvelope run = fixture.RunIntent(assignment, "run-" + assignment.Id);
        return (assignment, run, occurrence.Key);
    }

    private static JsonObject RaisedBy(StandingFixture fixture, RecordEnvelope assignment, RecordEnvelope? run, string key)
    {
        var raised = new JsonObject { ["goal"] = fixture.Goal.Id, ["occurrence"] = key, ["assignment"] = assignment.Id };
        if (run is not null) raised["run"] = run.Id;
        return raised;
    }

    private static JsonObject Possibility(JsonObject raisedBy, string fingerprint, bool draft = false, JsonArray? extras = null)
    {
        var body = new JsonObject
        {
            ["title"] = "Cache the widget's index " + fingerprint, ["idea"] = "Reading the index on every call costs a second.", ["origin"] = "The researcher's monthly occurrence.",
            ["disposition"] = "open", ["raisedBy"] = raisedBy.DeepClone(), ["fingerprint"] = fingerprint,
            ["sources"] = new JsonArray(new JsonObject { ["address"] = "https://example.org/widget/index", ["retrievedAt"] = "2026-10-01T00:00:00Z", ["hash"] = FlowFixture.Hash('c'), ["extents"] = "section 2" }),
            ["evidence"] = new JsonArray(new JsonObject { ["stance"] = "for", ["statement"] = "The profile shows the index read.", ["sources"] = new JsonArray(0) }, new JsonObject { ["stance"] = "against", ["statement"] = "A cache can go stale." }),
            ["reviewTriggers"] = new JsonArray(new JsonObject { ["at"] = "2027-01-01T00:00:00Z" }, new JsonObject { ["condition"] = "when the index format changes" }),
        };
        if (draft)
        {
            body["draft"] = new JsonObject
            {
                ["title"] = "Cache the index", ["intent"] = "Stop reading the index on every call.", ["scope"] = new JsonArray("path:src/index/"), ["deliverables"] = new JsonArray("a cached index"),
                ["exitQuestion"] = "Is the index read once per change?",
            };
        }

        if (extras is not null) body["extras"] = extras;
        return body;
    }

    [Fact]
    public void AnOccurrencesContributorRecordsItsPossibilitiesInItsRunWithinItsCaps()
    {
        using var fixture = new StandingFixture();
        (RecordEnvelope assignment, RecordEnvelope run, string key) = Carried(fixture);
        JsonObject raised = RaisedBy(fixture, assignment, run, key);

        // The contributor, in its run, records a cited possibility with a fingerprint; status counts it open.
        RecordEnvelope first = fixture.Add("possibility", Possibility(raised, "cache/index", draft: true), builder: true);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));
        Assert.Equal(1, fixture.Agent(fixture.Estate.Clock).OpenOutputs);

        // Only the contributor records it, in a run of the assignment; it is raised open, with a fingerprint and a source.
        Assert.Contains(Validate(fixture, "possibility", Possibility(raised, "other")), f => f.Code == "standing.raised-by");
        Assert.Contains(Validate(fixture, "possibility", Possibility(RaisedBy(fixture, assignment, null, key), "other"), builder: true), f => f.Code == "standing.raised-by");
        Assert.Contains(Validate(fixture, "possibility", Possibility(RaisedBy(fixture, assignment, run, "another/key"), "other"), builder: true), f => f.Code == "standing.raised-by");
        JsonObject bare = Possibility(raised, "bare");
        bare.Remove("fingerprint");
        bare.Remove("sources");
        bare.Remove("evidence");
        Assert.Equal(2, Validate(fixture, "possibility", bare, builder: true).Count(f => f.Code == "standing.possibility"));
        JsonObject parked = Possibility(raised, "parked");
        parked["disposition"] = "parked";
        parked["reason"] = "Later.";
        Assert.Contains(Validate(fixture, "possibility", parked, builder: true), f => f.Code == "standing.possibility");

        // A second open possibility of the agent with the same fingerprint is refused.
        Assert.Contains(Validate(fixture, "possibility", Possibility(raised, "cache/index"), builder: true), f => f.Code == "standing.duplicate-output" && f.Message.Contains(first.Id, StringComparison.Ordinal));

        // Five possibilities, three of them drafts, then one summary possibility for the rest.
        fixture.Add("possibility", Possibility(raised, "two", draft: true), builder: true);
        fixture.Add("possibility", Possibility(raised, "three", draft: true), builder: true);
        Assert.Contains(Validate(fixture, "possibility", Possibility(raised, "four", draft: true), builder: true), f => f.Code == "standing.output-cap");
        fixture.Add("possibility", Possibility(raised, "four"), builder: true);
        fixture.Add("possibility", Possibility(raised, "five"), builder: true);
        Assert.Contains(Validate(fixture, "possibility", Possibility(raised, "six"), builder: true), f => f.Code == "standing.output-cap");
        fixture.Add("possibility", Possibility(raised, "summary", extras: new JsonArray("Index the widget's tags too.", "Drop the legacy index.")), builder: true);
        Assert.Contains(Validate(fixture, "possibility", Possibility(raised, "summary-2", extras: new JsonArray("More.")), builder: true), f => f.Code == "standing.output-cap");
        Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));
        Assert.Equal(6, fixture.Agent(fixture.Estate.Clock).OpenOutputs);

        // Once its run has ended, the agent records no further output in it; once a result settles the occurrence, none at all.
        fixture.Settle(run, "observed-success");
        Assert.Contains(Validate(fixture, "possibility", Possibility(raised, "late"), builder: true), f => f.Code == "standing.raised-by" && f.Message.Contains("had ended", StringComparison.Ordinal));
        fixture.Result(assignment);
        Assert.Contains(Validate(fixture, "possibility", Possibility(raised, "late"), builder: true), f => f.Code == "standing.raised-by" && f.Message.Contains("settled", StringComparison.Ordinal));
    }

    [Fact]
    public void AnotherPrincipalDisposesOfAPossibilityAndADeclinedOneIsRaisedAgainOnlyByNamingIt()
    {
        using var fixture = new StandingFixture();
        (RecordEnvelope assignment, RecordEnvelope run, string key) = Carried(fixture);
        JsonObject raised = RaisedBy(fixture, assignment, run, key);
        RecordEnvelope possibility = fixture.Add("possibility", Possibility(raised, "cache/index", draft: true), builder: true);

        // The agent neither declines nor adopts its own output; a revision keeps its raisedBy and fingerprint.
        JsonObject declined = JsonNode.Parse(possibility.Body.GetRawText())!.AsObject();
        declined["disposition"] = "declined";
        declined["reason"] = "Not worth it.";
        Assert.Contains(Validate(fixture, "possibility", declined, supersedes: possibility.Id, builder: true), f => f.Code == "standing.own-output");
        JsonObject refingered = JsonNode.Parse(declined.ToJsonString())!.AsObject();
        refingered["fingerprint"] = "changed";
        Assert.Contains(Validate(fixture, "possibility", refingered, supersedes: possibility.Id), f => f.Code == "standing.raised-by");

        // The operator declines it; the agent may not raise it again without naming it and new evidence.
        RecordEnvelope decline = fixture.Add("possibility", declined, supersedes: possibility.Id);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));
        Assert.Equal(0, fixture.Agent(fixture.Estate.Clock).OpenOutputs);
        Assert.Contains(Validate(fixture, "possibility", Possibility(raised, "cache/index"), builder: true), f => f.Code == "standing.raises-again" && f.Message.Contains(possibility.Id, StringComparison.Ordinal));
        JsonObject again = Possibility(raised, "cache/index");
        again["raisesAgain"] = decline.Id;
        again.Remove("evidence");
        Assert.Contains(Validate(fixture, "possibility", again, builder: true), f => f.Code == "standing.raises-again" && f.Message.Contains("new evidence", StringComparison.Ordinal));
        again["raisesAgain"] = fixture.Goal.Id;
        Assert.Contains(Validate(fixture, "possibility", again, builder: true), f => f.Code == "standing.raises-again");
        again = Possibility(raised, "cache/index");
        again["raisesAgain"] = possibility.Id;
        again["links"] = new JsonArray(new JsonObject { ["relation"] = "supersedes", ["possibility"] = decline.Id, ["hash"] = decline.ComputedHash });
        Assert.Equal(string.Empty, Errors(Validate(fixture, "possibility", again, builder: true)));

        // The brief of the agent's next occurrence names its declined possibility.
        Assert.Contains(StandingOutputs.OpenAndDeclined(fixture.Estate.Load(), fixture.Goal), p => p.Id == decline.Id);

        // A link names a possibility at its exact hash.
        JsonObject linked = Possibility(raised, "linked");
        linked["links"] = new JsonArray(new JsonObject { ["relation"] = "duplicate-of", ["possibility"] = decline.Id, ["hash"] = FlowFixture.Hash('9') });
        Assert.Contains(Validate(fixture, "possibility", linked, builder: true), f => f.Code == "possibility.link");
        JsonObject scored = Possibility(raised, "scored");
        scored["score"] = 7;
        Assert.Contains(Validate(fixture, "possibility", scored, builder: true), f => f.Code == "body.unknown-key");

        // A held finding is adopted only by an operator who is a person; no agent that carried the agent's occurrences adopts it either.
        JsonObject held = Possibility(raised, "held/finding");
        held["disclosure"] = "held";
        RecordEnvelope finding = fixture.Add("possibility", held, builder: true);
        JsonObject adopted = JsonNode.Parse(finding.Body.GetRawText())!.AsObject();
        adopted["disposition"] = "adopted";
        adopted["reason"] = "Reviewed by the Reviewer.";
        adopted["adoptedAs"] = new JsonObject { ["type"] = "work-order", ["id"] = fixture.Work.Id };
        Assert.Contains(Validate(fixture, "possibility", adopted, supersedes: finding.Id, builder: true), f => f.Code == "standing.own-output");
        Assert.Equal(string.Empty, Errors(Validate(fixture, "possibility", adopted, supersedes: finding.Id)));
    }

    [Fact]
    public void AStandingAgentsAsksAndPublicationsGoToPeopleAndOpenOutputsDeferItsDispatch()
    {
        using var fixture = new StandingFixture();
        (RecordEnvelope assignment, RecordEnvelope run, string key) = Carried(fixture);
        JsonObject raised = RaisedBy(fixture, assignment, run, key);

        // An ask goes to a person other than its author, belongs to the goal and is never withdrawn by its author.
        JsonObject Ask(string to) => new()
        {
            ["to"] = to, ["question"] = "Should the index be cached?", ["recommendation"] = "Cache it.", ["consequence"] = "Nothing waits on it.", ["goal"] = fixture.Goal.Id, ["state"] = "open", ["raisedBy"] = raised.DeepClone(),
        };
        Assert.Contains(Validate(fixture, "ask", Ask(fixture.Estate.BuilderId), builder: true), f => f.Code == "standing.ask");
        JsonObject attention = Ask(fixture.Estate.OperatorId);
        attention["question"] = StandingAgents.RenewalAsk + " now?";
        Assert.Contains(Validate(fixture, "ask", attention, builder: true), f => f.Code == "standing.ask");
        RecordEnvelope ask = fixture.Add("ask", Ask(fixture.Estate.OperatorId), builder: true);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));
        JsonObject withdrawn = Ask(fixture.Estate.OperatorId);
        withdrawn["state"] = "withdrawn";
        Assert.Contains(Validate(fixture, "ask", withdrawn, supersedes: ask.Id, builder: true), f => f.Code == "standing.own-output");

        // Words outside the estate are a publication request pinning the agent's work order.
        const string text = "The widget's index is now cached.";
        JsonObject Request(string kind) => new()
        {
            ["to"] = fixture.Estate.OperatorId, ["askedBy"] = fixture.Estate.BuilderId, ["action"] = "Publish an agent's draft", ["why"] = "Users asked.", ["recommendation"] = "Post it.", ["consequence"] = "Nothing is posted.",
            ["kind"] = kind, ["destination"] = "the project's discussion board", ["content"] = new JsonArray(new JsonObject { ["kind"] = "text", ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(text)), ["text"] = text }), ["raisedBy"] = raised.DeepClone(),
        };
        Assert.Contains(Validate(fixture, "operator-request", Request("action"), new RecordReference("work-order", fixture.Work.Id, fixture.Work.ComputedHash), builder: true), f => f.Code == "standing.publication");
        fixture.Add("operator-request", Request("publication"), builder: true, subject: new RecordReference("work-order", fixture.Work.Id, fixture.Work.ComputedHash));
        Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));
        Assert.Equal(2, fixture.Agent(fixture.Estate.Clock).OpenOutputs);

        // A revision capping open outputs at two defers what falls due, and the run caps refuse a new run.
        JsonObject body = fixture.GoalBody();
        body["plan"]!["standing"]!["outputs"] = new JsonObject { ["possibilities"] = 2, ["drafts"] = 1, ["open"] = 2 };
        RecordEnvelope revised = fixture.Add("goal", body, supersedes: fixture.Goal.Id);
        fixture.Activate(revised);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));
        StandingAgents.Agent agent = StandingAgents.Derive(fixture.Estate.Load(), revised, fixture.Estate.Clock.AddMonths(1));
        Assert.Equal((2, 2), (agent.OpenOutputs, agent.OpenCap));
        Assert.Contains(agent.Occurrences, o => o.State == "deferred" && o.Reason.Contains("2 of its outputs are open, its cap of 2", StringComparison.Ordinal));
        Assert.Contains(StandingAgents.RunRefusals(fixture.Estate.Load(), revised, assignment.Id, null), r => r.Message.Contains("outputs are open", StringComparison.Ordinal));
        Assert.Contains("Open outputs: 2 of 2", StandingView.RenderText(fixture.Estate.Load(), agent), StringComparison.Ordinal);
        Assert.Equal(2, StandingView.Summary(fixture.Estate.Load(), agent)["outputs"]!["open"]!.GetValue<int>());

        // The plan's output caps are validated: drafts are among the possibilities.
        body["plan"]!["standing"]!["outputs"] = new JsonObject { ["possibilities"] = 2, ["drafts"] = 3 };
        Assert.Contains(Validate(fixture, "goal", body, supersedes: revised.Id), f => f.Code == "standing.outputs");
    }
}
