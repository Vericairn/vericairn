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
    /// <summary>The fields of the possibilities these tests write that format 3 adds.</summary>
    private static readonly string[] FormatThreeFields = ["raisedBy", "fingerprint", "sources", "evidence", "reviewTriggers"];

    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);

    /// <summary>What validating a record not yet written finds, against the estate as it stands, with the record among it.</summary>
    private static FindingList Validate(StandingFixture fixture, string type, JsonObject body, RecordReference? subject = null, string? supersedes = null, bool builder = false, string? by = null, long format = 3)
    {
        fixture.Estate.SetClock(fixture.Estate.Clock.AddSeconds(1));
        by ??= builder ? fixture.Estate.BuilderId : fixture.Estate.OperatorId;
        AuthoredRecord record = RecordAuthor.Create(new RecordDraft(type, by, Element(body), subject, supersedes, fixture.Estate.Clock, null, format));
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

    private static JsonObject Draft() => new()
    {
        ["title"] = "Cache the index", ["intent"] = "Stop reading the index on every call.", ["scope"] = new JsonArray("path:src/index/"), ["deliverables"] = new JsonArray("a cached index"),
        ["exitQuestion"] = "Is the index read once per change?",
    };

    /// <summary>A principal the operator records: an agent or a person, with one role.</summary>
    private static string Principal(StandingFixture fixture, string kind, string name, string role) =>
        fixture.Estate.Add("principal", fixture.Estate.OperatorId, kind == "agent"
            ? $$$"""{"kind":"agent","name":"{{{name}}}","roles":["{{{role}}}"],"agent":{"vendor":"Example","product":"Example Agent"}}"""
            : $$$"""{"kind":"human","name":"{{{name}}}","roles":["{{{role}}}"]}""").Id;

    /// <summary>A revision's body: the record's own, changed.</summary>
    private static JsonObject Revised(RecordEnvelope record, Action<JsonObject> change)
    {
        JsonObject body = PolicyFixture.Body(record);
        change(body);
        return body;
    }

    private static JsonObject Ask(StandingFixture fixture, JsonObject? raised, string question = "Should the index be cached?")
    {
        var body = new JsonObject
        {
            ["to"] = fixture.Estate.OperatorId, ["question"] = question, ["recommendation"] = "Cache it.", ["consequence"] = "Nothing waits on it.", ["goal"] = fixture.Goal.Id, ["state"] = "open",
        };
        if (raised is not null) body["raisedBy"] = raised.DeepClone();
        return body;
    }

    /// <summary>The operator's decision answering an ask, decided at the estate's clock, for whichever principal records it.</summary>
    private static JsonObject Answer(StandingFixture fixture, RecordEnvelope ask)
    {
        JsonObject body = fixture.Policies.DecisionBody();
        body["decidedAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock);
        body["answers"] = new JsonArray(ask.Id);
        return body;
    }

    /// <summary>The roster naming principals for the researcher role, and the operator for the decider's.</summary>
    private static JsonObject Roster(StandingFixture fixture, params string[] researchers) =>
        new() { ["roles"] = new JsonArray(FlowFixture.Role("researcher", researchers), FlowFixture.Role("decider", fixture.Estate.OperatorId)) };

    /// <summary>Three settled runs of an assignment by its contributor that fail; the last one's settlement.</summary>
    private static RecordEnvelope Failed(StandingFixture fixture, RecordEnvelope assignment, string contributor)
    {
        RecordEnvelope? last = null;
        for (int i = 0; i < 3; i++)
        {
            last = fixture.Settle(fixture.RunIntent(assignment, "run-failing-" + i, contributor), "known-failure");
        }

        return last!;
    }

    /// <summary>The failure pause's ask about a failed run, put to the operator as goal step writes it for its caller.</summary>
    private static JsonObject PauseAsk(StandingFixture fixture) => Ask(fixture, null, NextEligible.AttentionQuestion + "Research the widget: three of its runs failed. How does the run go on?");

    private static JsonObject Publication(StandingFixture fixture, JsonObject? raised, string to, string askedBy, string kind = "publication")
    {
        const string text = "The widget's index is now cached.";
        var body = new JsonObject
        {
            ["to"] = to, ["askedBy"] = askedBy, ["action"] = "Publish an agent's draft", ["why"] = "Users asked.", ["recommendation"] = "Post it.", ["consequence"] = "Nothing is posted.",
            ["kind"] = kind, ["destination"] = "the project's discussion board", ["content"] = new JsonArray(new JsonObject { ["kind"] = "text", ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(text)), ["text"] = text }),
        };
        if (raised is not null) body["raisedBy"] = raised.DeepClone();
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

    [Fact]
    public void ARevisionThatDisposesOfNothingIsWrittenAndLoadsWhoeverRevisesIt()
    {
        using var fixture = new StandingFixture();
        (RecordEnvelope assignment, RecordEnvelope run, string key) = Carried(fixture);
        JsonObject raised = RaisedBy(fixture, assignment, run, key);
        RecordEnvelope possibility = fixture.Add("possibility", Possibility(raised, "cache/index"), builder: true);

        // The raiser adds evidence and a draft, and the operator retitles it: neither disposes of it, and each is a valid revision.
        JsonObject evidence = Revised(possibility, b =>
        {
            b["evidence"]!.AsArray().Add(new JsonObject { ["stance"] = "for", ["statement"] = "A second profile shows the read." });
            b["draft"] = Draft();
        });
        Assert.Equal(string.Empty, Errors(Validate(fixture, "possibility", evidence, supersedes: possibility.Id, builder: true)));
        JsonObject retitled = Revised(possibility, b => b["title"] = "Read the widget's index once per change");
        Assert.Equal(string.Empty, Errors(Validate(fixture, "possibility", retitled, supersedes: possibility.Id)));

        // Written straight to records/, as a hand edit or a sync from another clone brings them, the estate loads with no error.
        RecordEnvelope added = fixture.Add("possibility", evidence, supersedes: possibility.Id, builder: true);
        fixture.Add("possibility", Revised(added, b => b["title"] = "Read the widget's index once per change"), supersedes: added.Id);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));
        Assert.Equal(1, fixture.Agent(fixture.Estate.Clock).OpenOutputs);

        // A revision of a plain ask or a plain request that adds raisedBy is a finding, never an exception.
        RecordEnvelope ask = fixture.Add("ask", Ask(fixture, null), builder: true);
        Assert.Contains(Validate(fixture, "ask", Revised(ask, b => { b["raisedBy"] = raised.DeepClone(); b["state"] = "notified"; }), supersedes: ask.Id, builder: true), f => f.Code == "standing.raised-by");
        var about = new RecordReference("work-order", fixture.Work.Id, fixture.Work.ComputedHash);
        RecordEnvelope request = fixture.Add("operator-request", Publication(fixture, null, fixture.Estate.OperatorId, fixture.Estate.BuilderId), builder: true, subject: about);
        Assert.Contains(Validate(fixture, "operator-request", Revised(request, b => b["raisedBy"] = raised.DeepClone()), about, request.Id, builder: true), f => f.Code == "standing.raised-by");
        fixture.Add("ask", Revised(ask, b => { b["raisedBy"] = raised.DeepClone(); b["state"] = "notified"; }), supersedes: ask.Id, builder: true);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "standing.raised-by");
    }

    [Fact]
    public void ARevisionKeepsWhatMakesTheStandingRulesApplyJudgedFromItsFirstRevision()
    {
        using var fixture = new StandingFixture();
        (RecordEnvelope assignment, RecordEnvelope run, string key) = Carried(fixture);
        JsonObject raised = RaisedBy(fixture, assignment, run, key);

        // Withdrawing its ask is a disposal the agent may not make, and leaving raisedBy out of the withdrawal changes nothing.
        RecordEnvelope ask = fixture.Add("ask", Ask(fixture, raised), builder: true);
        FindingList shed = Validate(fixture, "ask", Revised(ask, b => { b.Remove("raisedBy"); b["state"] = "withdrawn"; }), supersedes: ask.Id, builder: true);
        Assert.Contains(shed, f => f.Code == "standing.raised-by");
        Assert.Contains(shed, f => f.Code == "standing.own-output");
        Assert.Equal(string.Empty, Errors(Validate(fixture, "ask", Revised(ask, b => b["state"] = "notified"), supersedes: ask.Id, builder: true)));
        fixture.Add("ask", Revised(ask, b => { b.Remove("raisedBy"); b["state"] = "withdrawn"; }), supersedes: ask.Id, builder: true);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "standing.own-output" && f.Severity == Severity.Error);

        // A publication request's revision keeps its raisedBy and stays a publication request.
        var about = new RecordReference("work-order", fixture.Work.Id, fixture.Work.ComputedHash);
        RecordEnvelope request = fixture.Add("operator-request", Publication(fixture, raised, fixture.Estate.OperatorId, fixture.Estate.BuilderId), builder: true, subject: about);
        Assert.Contains(Validate(fixture, "operator-request", Revised(request, b => b.Remove("raisedBy")), about, request.Id, builder: true), f => f.Code == "standing.raised-by");
        Assert.Contains(Validate(fixture, "operator-request", Revised(request, b => b["kind"] = "action"), about, request.Id, builder: true), f => f.Code == "standing.publication");
        Assert.Equal(string.Empty, Errors(Validate(fixture, "operator-request", Revised(request, b => b["why"] = "Users asked twice."), about, request.Id, builder: true)));

        // A possibility's revision keeps raisedBy and its fingerprint as its first revision names them, whoever writes it.
        RecordEnvelope possibility = fixture.Add("possibility", Possibility(raised, "cache/index"), builder: true);
        FindingList dropped = Validate(fixture, "possibility", Revised(possibility, b => { b.Remove("raisedBy"); b["disposition"] = "declined"; b["reason"] = "Mine to drop."; }), supersedes: possibility.Id, builder: true);
        Assert.Contains(dropped, f => f.Code == "standing.raised-by");
        Assert.Contains(dropped, f => f.Code == "standing.own-output");
        Assert.Contains(Validate(fixture, "possibility", Revised(possibility, b => b.Remove("raisedBy")), supersedes: possibility.Id), f => f.Code == "standing.raised-by");

        // A revision stating the format before raisedBy leaves no rule behind either.
        JsonObject older = Revised(possibility, b =>
        {
            foreach (string field in FormatThreeFields) b.Remove(field);
            b["disposition"] = "declined";
            b["reason"] = "Mine to drop.";
        });
        FindingList formatTwo = Validate(fixture, "possibility", older, supersedes: possibility.Id, builder: true, format: 2);
        Assert.Contains(formatTwo, f => f.Code == "standing.raised-by");
        Assert.Contains(formatTwo, f => f.Code == "standing.own-output");
        FindingList askTwo = Validate(fixture, "ask", Revised(ask, b => { b.Remove("raisedBy"); b["state"] = "withdrawn"; }), supersedes: ask.Id, builder: true, format: 2);
        Assert.Contains(askTwo, f => f.Code == "standing.raised-by");
        Assert.Contains(askTwo, f => f.Code == "standing.own-output");
    }

    [Fact]
    public void AStandingPublicationRequestIsClosedOnlyByAnOperatorWhoIsAPerson()
    {
        using var fixture = new StandingFixture();
        (RecordEnvelope assignment, RecordEnvelope run, string key) = Carried(fixture);
        JsonObject raised = RaisedBy(fixture, assignment, run, key);
        string helper = Principal(fixture, "agent", "Helper", "contributor");
        var about = new RecordReference("work-order", fixture.Work.Id, fixture.Work.ComputedHash);
        RecordEnvelope request = fixture.Add("operator-request", Publication(fixture, raised, fixture.Estate.OperatorId, fixture.Estate.BuilderId), builder: true, subject: about);
        Assert.Equal(1, fixture.Agent(fixture.Estate.Clock).OpenOutputs);
        var closing = new RecordReference("operator-request", request.Id, request.ComputedHash);

        // The agent that raised it closes it neither as dismissed nor as done, whatever words of the operator it cites; nor does another agent.
        JsonObject Close(string outcome)
        {
            var body = new JsonObject { ["outcome"] = outcome, ["operatorWords"] = new JsonObject { ["by"] = fixture.Estate.OperatorId, ["statement"] = "Go ahead.", ["source"] = "the operator's message in the project chat", ["at"] = request.At } };
            if (outcome == "dismissed") body["reason"] = "Not now.";
            else body["link"] = "https://example.org/discussions/1";
            return body;
        }

        Assert.Contains(Validate(fixture, "operator-request-close", Close("dismissed"), closing, builder: true), f => f.Code == "standing.own-output");
        Assert.Contains(Validate(fixture, "operator-request-close", Close("done"), closing, builder: true), f => f.Code == "standing.own-output");
        Assert.Contains(Validate(fixture, "operator-request-close", Close("done"), closing, by: helper), f => f.Code == "standing.publication");
        JsonObject observed = new()
        {
            ["kind"] = "operator-request", ["asOf"] = RecordParser.FormatTimestamp(fixture.Estate.Clock), ["summary"] = "Posted.", ["limitations"] = new JsonArray(),
            ["facts"] = new JsonObject { ["outcome"] = "done", ["destination"] = "the project's discussion board", ["location"] = "https://example.org/discussions/1", ["evidenceHash"] = FlowFixture.Hash('e') },
        };
        Assert.Contains(Validate(fixture, "observation", observed, closing, builder: true), f => f.Code == "standing.own-output");
        Assert.Contains(Validate(fixture, "observation", observed, closing, by: helper), f => f.Code == "standing.publication");

        // Written straight to records/, the agent's close is an error the estate reports, and the operator's own close is not.
        fixture.Add("operator-request-close", Close("dismissed"), builder: true, subject: closing);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "standing.own-output" && f.Severity == Severity.Error);

        using var clean = new StandingFixture();
        (RecordEnvelope cleanAssignment, RecordEnvelope cleanRun, string cleanKey) = Carried(clean);
        RecordEnvelope posted = clean.Add("operator-request", Publication(clean, RaisedBy(clean, cleanAssignment, cleanRun, cleanKey), clean.Estate.OperatorId, clean.Estate.BuilderId), builder: true,
            subject: new RecordReference("work-order", clean.Work.Id, clean.Work.ComputedHash));
        clean.Add("operator-request-close", new JsonObject { ["outcome"] = "dismissed", ["reason"] = "Not now." }, subject: new RecordReference("operator-request", posted.Id, posted.ComputedHash));
        Assert.False(clean.Estate.Load().Findings.HasErrors, Errors(clean.Estate.Load().Findings));
        Assert.Equal(0, clean.Agent(clean.Estate.Clock).OpenOutputs);

        // A person carrying the occurrence puts its publication request to an operator other than itself, who then closes it.
        using var person = new StandingFixture();
        StandingAgents.Occurrence occurrence = Assert.Single(person.Agent(person.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope own = person.Assign(occurrence, person.Estate.OperatorId);
        var ownRaised = new JsonObject { ["goal"] = person.Goal.Id, ["occurrence"] = occurrence.Key, ["assignment"] = own.Id };
        Assert.Contains(Validate(person, "operator-request", Publication(person, ownRaised, person.Estate.OperatorId, person.Estate.OperatorId), new RecordReference("work-order", person.Work.Id, person.Work.ComputedHash)),
            f => f.Code == "standing.publication");
    }

    [Fact]
    public void AHeldFindingIsDisposedOfOnlyByAPersonAndItsHoldEndsOnlyByAnOperatorWhoIsOne()
    {
        using var fixture = new StandingFixture();
        (RecordEnvelope assignment, RecordEnvelope run, string key) = Carried(fixture);
        JsonObject raised = RaisedBy(fixture, assignment, run, key);
        string helper = Principal(fixture, "agent", "Helper", "contributor");
        string pat = Principal(fixture, "human", "Pat", "contributor");
        JsonObject held = Possibility(raised, "held/finding");
        held["disclosure"] = "held";
        RecordEnvelope finding = fixture.Add("possibility", held, builder: true);
        JsonObject Disposed(RecordEnvelope of, string disposition) => Revised(of, b =>
        {
            b["disposition"] = disposition;
            b["reason"] = "Read it.";
            if (disposition == "adopted") b["adoptedAs"] = new JsonObject { ["type"] = "work-order", ["id"] = fixture.Work.Id };
        });

        // No agent declines or parks it, though it neither raised it, carried an occurrence nor is named by the role.
        Assert.Contains(Validate(fixture, "possibility", Disposed(finding, "declined"), supersedes: finding.Id, by: helper), f => f.Code == "standing.held");
        Assert.Contains(Validate(fixture, "possibility", Disposed(finding, "parked"), supersedes: finding.Id, by: helper), f => f.Code == "standing.held");
        Assert.Contains(Validate(fixture, "possibility", Disposed(finding, "adopted"), supersedes: finding.Id, by: helper), f => f.Code == "standing.held");

        // A person declines it (design/standing-agents.md § 8: a person disposes of every held finding); only an operator who is one adopts it.
        Assert.Equal(string.Empty, Errors(Validate(fixture, "possibility", Disposed(finding, "declined"), supersedes: finding.Id, by: pat)));
        Assert.Contains(Validate(fixture, "possibility", Disposed(finding, "adopted"), supersedes: finding.Id, by: pat), f => f.Code == "standing.held");
        Assert.Equal(string.Empty, Errors(Validate(fixture, "possibility", Disposed(finding, "adopted"), supersedes: finding.Id)));

        // Its raiser adds evidence to it, but no one but an operator who is a person ends its hold.
        Assert.Equal(string.Empty, Errors(Validate(fixture, "possibility", Revised(finding, b => b["evidence"]!.AsArray().Add(new JsonObject { ["stance"] = "for", ["statement"] = "It reproduces." })), supersedes: finding.Id, builder: true)));
        Assert.Contains(Validate(fixture, "possibility", Revised(finding, b => b.Remove("disclosure")), supersedes: finding.Id, builder: true), f => f.Code == "standing.held");
        Assert.Contains(Validate(fixture, "possibility", Revised(finding, b => b.Remove("disclosure")), supersedes: finding.Id, by: pat), f => f.Code == "standing.held");
        Assert.Equal(string.Empty, Errors(Validate(fixture, "possibility", Revised(finding, b => b.Remove("disclosure")), supersedes: finding.Id)));

        // A held possibility no standing agent raised is held all the same: an agent declines none.
        RecordEnvelope plain = fixture.Add("possibility", new JsonObject
        {
            ["title"] = "A held note", ["idea"] = "The token is logged.", ["origin"] = "The builder's reading.", ["disposition"] = "open", ["disclosure"] = "held",
        }, builder: true);
        Assert.Contains(Validate(fixture, "possibility", Disposed(plain, "declined"), supersedes: plain.Id, by: helper), f => f.Code == "standing.held");
        Assert.Contains(Validate(fixture, "possibility", Disposed(plain, "declined"), supersedes: plain.Id, builder: true), f => f.Code == "standing.held");
        Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));
    }

    [Fact]
    public void EachOutputIsJudgedAgainstTheCapsInForceWhenItWasRaisedAndARevisionCountsToo()
    {
        using var fixture = new StandingFixture();
        (RecordEnvelope assignment, RecordEnvelope run, string key) = Carried(fixture);
        JsonObject raised = RaisedBy(fixture, assignment, run, key);
        foreach (string fingerprint in new[] { "one", "two", "three" })
        {
            fixture.Add("possibility", Possibility(raised, fingerprint, draft: true), builder: true);
        }

        RecordEnvelope four = fixture.Add("possibility", Possibility(raised, "four"), builder: true);
        RecordEnvelope five = fixture.Add("possibility", Possibility(raised, "five"), builder: true);

        // The sponsor lowers the caps of a busy agent: its proposal and its activation leave the earlier outputs valid.
        JsonObject body = fixture.GoalBody();
        body["plan"]!["standing"]!["outputs"] = new JsonObject { ["possibilities"] = 2, ["drafts"] = 1 };
        Assert.DoesNotContain(Validate(fixture, "goal", body, supersedes: fixture.Goal.Id), f => f.Severity == Severity.Error);
        RecordEnvelope revised = fixture.Add("goal", body, supersedes: fixture.Goal.Id);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));
        fixture.Activate(revised);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));

        // A revision counts as its occurrence's caps count: a fourth draft is refused, and so is a second summary.
        Assert.Contains(Validate(fixture, "possibility", Revised(four, b => b["draft"] = Draft()), supersedes: four.Id, builder: true), f => f.Code == "standing.output-cap");
        JsonObject summary = Revised(four, b => b["extras"] = new JsonArray("Index the widget's tags too."));
        Assert.Equal(string.Empty, Errors(Validate(fixture, "possibility", summary, supersedes: four.Id, builder: true)));
        fixture.Add("possibility", summary, supersedes: four.Id, builder: true);
        Assert.Contains(Validate(fixture, "possibility", Revised(five, b => b["extras"] = new JsonArray("Drop the legacy index.")), supersedes: five.Id, builder: true), f => f.Code == "standing.output-cap");
        Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));
    }

    [Fact]
    public void AnAgentsPlainAskInTheRuntimesWordsIsNotTakenForTheRuntimesOwn()
    {
        using var fixture = new StandingFixture(budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 6 }, period: new JsonObject { ["unit"] = "months", ["runs"] = 10, ["wallMinutes"] = 200 });
        DateTimeOffset since = fixture.Estate.Clock;

        // The builder's plain ask on the goal, in the period cap's words, holds back no period ask; the same ask by the operator, who takes
        // the goal's steps as goal step's caller does, is the runtime's.
        string question = StandingAgents.PeriodAsk + " Research the widget is reached for the period.";
        fixture.Add("ask", Ask(fixture, null, question), builder: true, subject: fixture.Goal.Reference);
        Assert.Null(StandingAgents.OpenAsk(fixture.Estate.Load(), fixture.Goal, StandingAgents.PeriodAsk, since));
        RecordEnvelope runtime = fixture.Add("ask", Ask(fixture, null, question), subject: fixture.Goal.Reference);
        Assert.Equal(runtime.Id, StandingAgents.OpenAsk(fixture.Estate.Load(), fixture.Goal, StandingAgents.PeriodAsk, since)?.Id);

        // Three failed runs pause the agent; the builder's ask about the last in the failure pause's words, answered, does not lift it.
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence);
        RecordEnvelope? last = null;
        foreach (string outcome in new[] { "blocked", "abandoned", "blocked" })
        {
            RecordEnvelope intent = fixture.RunIntent(assignment, "run-" + outcome + "-" + fixture.Estate.Clock.Ticks);
            last = fixture.Result(assignment, outcome);
            fixture.Settle(intent, "observed-success");
        }

        Assert.Equal(3, fixture.Agent(fixture.Estate.Clock).Failures);
        void Answered(bool builder)
        {
            RecordEnvelope ask = fixture.Add("ask", Ask(fixture, null, NextEligible.AttentionQuestion + "Research the widget: its runs failed. How does the run go on?"), builder: builder, subject: last!.Reference);
            JsonObject answer = fixture.Policies.DecisionBody();
            answer["answers"] = new JsonArray(ask.Id);
            fixture.Add("decision", answer);
        }

        Answered(builder: true);
        Assert.Equal(3, fixture.Agent(fixture.Estate.Clock).Failures);
        Answered(builder: false);
        Assert.Equal(0, fixture.Agent(fixture.Estate.Clock).Failures);
    }

    [Fact]
    public void AnAgentTheRoleNamesDisposesOfNoStandingOutputAtTheWriteBoundary()
    {
        using var fixture = new StandingFixture();
        (RecordEnvelope assignment, RecordEnvelope run, string key) = Carried(fixture);
        RecordEnvelope possibility = fixture.Add("possibility", Possibility(RaisedBy(fixture, assignment, run, key), "cache/index"), builder: true);

        // The roster now names a second agent for the researcher role, which carried none of the agent's occurrences.
        string helper = Principal(fixture, "agent", "Helper", "contributor");
        RecordEnvelope roster = Assert.Single(fixture.Estate.Load().Current("roster"));
        fixture.Add("roster", new JsonObject { ["roles"] = new JsonArray(FlowFixture.Role("researcher", fixture.Estate.BuilderId, helper), FlowFixture.Role("decider", fixture.Estate.OperatorId)) }, supersedes: roster.Id);

        // Its decline is refused: a roster revision written before it named the helper for the role, whatever the roster names later.
        JsonObject declined = Revised(possibility, b => { b["disposition"] = "declined"; b["reason"] = "Not worth it."; });
        FindingList findings = Validate(fixture, "possibility", declined, supersedes: possibility.Id, by: helper);
        Assert.Contains(findings, f => f.Code == "standing.own-output" && f.Severity == Severity.Error && f.Message.Contains(helper, StringComparison.Ordinal));
        Assert.DoesNotContain(Validate(fixture, "possibility", declined, supersedes: possibility.Id), f => f.Code is "standing.own-output" or StandingOutputs.BoundaryCode);

        // A clerk agent declines it while nothing ties the clerk to the agent; a roster revision naming the clerk afterwards makes that decline
        // a report (standing.output-now), never an error of the estate, so the revision is written.
        string clerk = Principal(fixture, "agent", "Clerk", "contributor");
        fixture.Estate.Add("possibility", clerk, declined.ToJsonString(), supersedes: possibility.Id);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));
        RecordEnvelope naming = Assert.Single(fixture.Estate.Load().Current("roster"));
        fixture.Add("roster", new JsonObject { ["roles"] = new JsonArray(FlowFixture.Role("researcher", fixture.Estate.BuilderId, helper, clerk), FlowFixture.Role("decider", fixture.Estate.OperatorId)) }, supersedes: naming.Id);
        EstateModel model = fixture.Estate.Load();
        Assert.Contains(model.Findings, f => f.Code == StandingOutputs.BoundaryCode && f.Severity == Severity.Info && f.Message.Contains(clerk, StringComparison.Ordinal));
        Assert.False(model.Findings.HasErrors, Errors(model.Findings));
    }

    [Fact]
    public void NoStandingAgentAnswersItsOwnAskOrTheRuntimesAboutItWhateverDecidedByNames()
    {
        using var fixture = new StandingFixture(budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 6 }, period: new JsonObject { ["unit"] = "months", ["runs"] = 10, ["wallMinutes"] = 200 }, builderHolds: false);
        string op = fixture.Estate.OperatorId;
        string builder = fixture.Estate.BuilderId;

        // A helper agent the roster names for the researcher role carries the occurrence in its run, and a spare agent is named too; the
        // builder neither raised the ask, carried an occurrence nor was ever named for the role.
        string helper = Principal(fixture, "agent", "Helper", "contributor");
        string spare = Principal(fixture, "agent", "Spare", "contributor");
        RecordEnvelope roster = Assert.Single(fixture.Estate.Load().Current("roster"));
        fixture.Add("roster", new JsonObject { ["roles"] = new JsonArray(FlowFixture.Role("researcher", helper, spare), FlowFixture.Role("decider", op)) }, supersedes: roster.Id);
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence, helper);
        RecordEnvelope run = fixture.RunIntent(assignment, "run-helper", helper);
        RecordEnvelope ask = fixture.Estate.Add("ask", helper, Ask(fixture, RaisedBy(fixture, assignment, run, occurrence.Key)).ToJsonString());
        Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));
        Assert.Equal(1, fixture.Agent(fixture.Estate.Clock).OpenOutputs);
        JsonObject Answer(RecordEnvelope answered)
        {
            JsonObject body = fixture.Policies.DecisionBody();
            body["decidedAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock);
            body["answers"] = new JsonArray(answered.Id);
            return body;
        }

        // The helper records the operator's decision on its own ask: refused, though decidedBy names the operator; so is the spare agent's,
        // which a roster revision written before it named for the role; the builder records the operator's decision as it always has.
        Assert.Contains(Validate(fixture, "decision", Answer(ask), by: helper), f => f.Code == "standing.own-output" && f.Severity == Severity.Error);
        Assert.Contains(Validate(fixture, "decision", Answer(ask), by: spare), f => f.Code == "standing.own-output" && f.Severity == Severity.Error && f.Message.Contains("role researcher", StringComparison.Ordinal));
        FindingList recorded = Validate(fixture, "decision", Answer(ask), builder: true);
        Assert.Equal(string.Empty, Errors(recorded));
        Assert.DoesNotContain(recorded, f => f.Code == StandingOutputs.BoundaryCode);

        // Written straight to records/, the helper's decision is an error of the estate and answers nothing: the ask stays open and counted.
        fixture.Estate.Add("decision", helper, Answer(ask).ToJsonString());
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "standing.own-output" && f.Severity == Severity.Error);
        Assert.Equal("open", Asks.State(fixture.Estate.Load(), ask).State);
        Assert.Equal(1, fixture.Agent(fixture.Estate.Clock).OpenOutputs);
        RecordEnvelope builders = fixture.Add("decision", Answer(ask), builder: true);
        (string state, IReadOnlyList<RecordEnvelope> answers) = Asks.State(fixture.Estate.Load(), ask);
        Assert.Equal(("answered", builders.Id), (state, Assert.Single(answers).Id));
        Assert.Equal(0, fixture.Agent(fixture.Estate.Clock).OpenOutputs);

        // Its run ends; three more of the helper's runs fail, and the failure pause's ask goes to the operator, as goal step records it.
        fixture.Settle(run, "observed-success");
        RecordEnvelope? last = null;
        for (int i = 0; i < 3; i++)
        {
            last = fixture.Settle(fixture.RunIntent(assignment, "run-failing-" + i, helper), "known-failure");
        }

        Assert.Equal(3, fixture.Agent(fixture.Estate.Clock).Failures);
        RecordEnvelope pause = fixture.Add("ask", Ask(fixture, null, NextEligible.AttentionQuestion + "Research the widget: three of its runs failed. How does the run go on?"), subject: last!.Reference);
        Assert.Equal(fixture.Goal.Id, StandingOutputs.GuardedBy(fixture.Estate.Load(), pause)?.Id);

        // The helper's decision answering it, naming the operator, is refused and, written by hand, does not lift the pause.
        Assert.Contains(Validate(fixture, "decision", Answer(pause), by: helper), f => f.Code == "standing.own-output" && f.Severity == Severity.Error);
        fixture.Estate.Add("decision", helper, Answer(pause).ToJsonString());
        Assert.Equal(3, fixture.Agent(fixture.Estate.Clock).Failures);
        Assert.Equal("open", Asks.State(fixture.Estate.Load(), pause).State);

        // The builder records the operator's answer, which lets the agent go on.
        Assert.Equal(string.Empty, Errors(Validate(fixture, "decision", Answer(pause), builder: true)));
        fixture.Add("decision", Answer(pause), builder: true);
        Assert.Equal(0, fixture.Agent(fixture.Estate.Clock).Failures);
    }

    [Fact]
    public void APossibilitysAuthorNeverAdoptsItWhoeverRaisedIt()
    {
        using var fixture = new StandingFixture();
        var order = new JsonObject { ["type"] = "work-order", ["id"] = fixture.Work.Id, ["hash"] = fixture.Work.ComputedHash };
        JsonObject Adopted(RecordEnvelope of) => Revised(of, b => { b["disposition"] = "adopted"; b["reason"] = "Worth doing."; b["adoptedAs"] = order.DeepClone(); });

        // A plain possibility the builder wrote with a draft, a field new in 0.9.4: its author's adoption is refused, the operator's is not.
        JsonObject idea = new() { ["title"] = "Cache the index", ["idea"] = "Read it once.", ["origin"] = "The builder's own note.", ["disposition"] = "open", ["draft"] = Draft() };
        RecordEnvelope drafted = fixture.Add("possibility", idea, builder: true);
        Assert.Contains(Validate(fixture, "possibility", Adopted(drafted), supersedes: drafted.Id, builder: true), f => f.Code == "standing.own-output" && f.Severity == Severity.Error);
        Assert.Equal(string.Empty, Errors(Validate(fixture, "possibility", Adopted(drafted), supersedes: drafted.Id)));
        Assert.Equal(string.Empty, Errors(Validate(fixture, "possibility", Revised(drafted, b => { b["disposition"] = "declined"; b["reason"] = "Mine to drop."; }), supersedes: drafted.Id, builder: true)));
        fixture.Add("possibility", Adopted(drafted), supersedes: drafted.Id, builder: true);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "standing.own-output" && f.Severity == Severity.Error);

        // One with only the fields earlier estates wrote: its author's adoption is reported as the records stand, which the write boundary
        // refuses (standing.output-now), at either format, so that an adoption an earlier estate recorded keeps its reading.
        using var older = new StandingFixture();
        JsonObject plain = new() { ["title"] = "A books pack", ["idea"] = "Ship a books pack.", ["origin"] = "The builder's own note.", ["disposition"] = "open" };
        RecordEnvelope noted = older.Add("possibility", plain, builder: true);
        var olderOrder = new JsonObject { ["type"] = "work-order", ["id"] = older.Work.Id, ["hash"] = older.Work.ComputedHash };
        JsonObject adopting = Revised(noted, b => { b["disposition"] = "adopted"; b["reason"] = "Worth doing."; b["adoptedAs"] = olderOrder.DeepClone(); });
        foreach (long format in new long[] { 3, 2 })
        {
            FindingList reported = Validate(older, "possibility", adopting, supersedes: noted.Id, builder: true, format: format);
            Assert.Contains(reported, f => f.Code == StandingOutputs.BoundaryCode && f.Severity == Severity.Info);
            Assert.Equal(string.Empty, Errors(reported));
        }

        Assert.DoesNotContain(Validate(older, "possibility", adopting, supersedes: noted.Id), f => f.Code == StandingOutputs.BoundaryCode);
        older.Add("possibility", adopting, supersedes: noted.Id, builder: true);
        Assert.False(older.Estate.Load().Findings.HasErrors, Errors(older.Estate.Load().Findings));
    }

    [Fact]
    public void ATiedAgentsBackdatedAnswerAnswersNothingWhateverTheRosterNowNames()
    {
        using var fixture = new StandingFixture(budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 6 }, period: new JsonObject { ["unit"] = "months", ["runs"] = 10, ["wallMinutes"] = 200 });
        string helper = Principal(fixture, "agent", "Helper", "contributor");
        string spare = Principal(fixture, "agent", "Spare", "contributor");
        fixture.Estate.SetClock(fixture.Estate.Clock.AddMinutes(2));

        // The roster names a helper agent and a spare for the researcher role; the helper carries the occurrence and asks the operator in its
        // run, three more of its runs fail, and the failure pause's ask goes to the operator as goal step records it.
        RecordEnvelope naming = fixture.Add("roster", Roster(fixture, helper, spare), supersedes: Assert.Single(fixture.Estate.Load().Current("roster")).Id);
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence, helper);
        RecordEnvelope run = fixture.RunIntent(assignment, "run-helper", helper);
        RecordEnvelope ask = fixture.Estate.Add("ask", helper, Ask(fixture, RaisedBy(fixture, assignment, run, occurrence.Key)).ToJsonString());
        fixture.Settle(run, "observed-success");
        RecordEnvelope pause = fixture.Add("ask", PauseAsk(fixture), subject: Failed(fixture, assignment, helper).Reference);
        Assert.Equal((3, 1), (fixture.Agent(fixture.Estate.Clock).Failures, fixture.Agent(fixture.Estate.Clock).OpenOutputs));

        // The operator takes both off the roster. The helper's decision answering the pause, and the spare's answering the helper's ask, each
        // naming the operator and dated thirty seconds before the helper's first assignment, are written straight to records/.
        fixture.Add("roster", Roster(fixture, fixture.Estate.BuilderId), supersedes: naming.Id);
        DateTimeOffset now = fixture.Estate.Clock;
        fixture.Estate.SetClock(assignment.AtTime.AddSeconds(-30));
        fixture.Estate.Add("decision", helper, Answer(fixture, pause).ToJsonString());
        fixture.Estate.Add("decision", spare, Answer(fixture, ask).ToJsonString());
        fixture.Estate.SetClock(now);

        // Each is an error of the estate, since what ties its author was written before the ask it answers, and neither answers anything,
        // whatever the roster names now and whatever date the decision claims: the pause holds, and the ask stays open and counted.
        EstateModel model = fixture.Estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "standing.own-output" && f.Severity == Severity.Error && f.Message.Contains(helper, StringComparison.Ordinal));
        Assert.Contains(model.Findings, f => f.Code == "standing.own-output" && f.Severity == Severity.Error && f.Message.Contains(spare, StringComparison.Ordinal));
        Assert.Equal(("open", "open"), (Asks.State(model, pause).State, Asks.State(model, ask).State));
        Assert.Equal((3, 1), (fixture.Agent(fixture.Estate.Clock).Failures, fixture.Agent(fixture.Estate.Clock).OpenOutputs));

        // The operator's own decisions answer both.
        fixture.Add("decision", Answer(fixture, pause));
        fixture.Add("decision", Answer(fixture, ask));
        Assert.Equal((0, 0), (fixture.Agent(fixture.Estate.Clock).Failures, fixture.Agent(fixture.Estate.Clock).OpenOutputs));
    }

    [Fact]
    public void ARosterOrAssignmentChangeReopensAnAnsweredAskForThePersonAndNeverClosesOne()
    {
        using var fixture = new StandingFixture(budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 6 }, period: new JsonObject { ["unit"] = "months", ["runs"] = 10, ["wallMinutes"] = 200 });
        string helper = Principal(fixture, "agent", "Helper", "contributor");
        string spare = Principal(fixture, "agent", "Spare", "contributor");
        string clerk = Principal(fixture, "agent", "Clerk", "contributor");
        string courier = Principal(fixture, "agent", "Courier", "contributor");
        RecordEnvelope roster = fixture.Add("roster", Roster(fixture, helper, spare), supersedes: Assert.Single(fixture.Estate.Load().Current("roster")).Id);
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence, helper);
        RecordEnvelope run = fixture.RunIntent(assignment, "run-helper", helper);
        RecordEnvelope ask = fixture.Estate.Add("ask", helper, Ask(fixture, RaisedBy(fixture, assignment, run, occurrence.Key)).ToJsonString());

        // The clerk, which neither raised the ask, carried an occurrence nor was ever named for the role, records the operator's decision: it
        // is valid, and the ask is answered.
        FindingList valid = Validate(fixture, "decision", Answer(fixture, ask), by: clerk);
        Assert.Equal(string.Empty, Errors(valid));
        Assert.DoesNotContain(valid, f => f.Code == StandingOutputs.BoundaryCode);
        fixture.Estate.Add("decision", clerk, Answer(fixture, ask).ToJsonString());
        Assert.Equal(("answered", 0), (Asks.State(fixture.Estate.Load(), ask).State, fixture.Agent(fixture.Estate.Clock).OpenOutputs));

        // The operator's roster revision names the clerk for the role: the ask reads waiting again and counts, and the clerk's decision is
        // reported, never an error of the estate, so the revision is written. A later revision dropping the clerk closes nothing.
        roster = fixture.Add("roster", Roster(fixture, helper, spare, clerk), supersedes: roster.Id);
        EstateModel model = fixture.Estate.Load();
        Assert.Equal(("open", 1), (Asks.State(model, ask).State, fixture.Agent(fixture.Estate.Clock).OpenOutputs));
        Assert.Contains(model.Findings, f => f.Code == StandingOutputs.BoundaryCode && f.Severity == Severity.Info && f.Message.Contains(clerk, StringComparison.Ordinal));
        Assert.False(model.Findings.HasErrors, Errors(model.Findings));
        roster = fixture.Add("roster", Roster(fixture, helper, spare), supersedes: roster.Id);
        Assert.Equal(("open", 1), (Asks.State(fixture.Estate.Load(), ask).State, fixture.Agent(fixture.Estate.Clock).OpenOutputs));

        // The same for the failure pause and an assignment: the courier records the operator's answer, which lifts the pause, until the
        // courier carries an occurrence of the agent; then the pause holds again.
        fixture.Settle(run, "observed-success");
        RecordEnvelope pause = fixture.Add("ask", PauseAsk(fixture), subject: Failed(fixture, assignment, helper).Reference);
        fixture.Estate.Add("decision", courier, Answer(fixture, pause).ToJsonString());
        Assert.Equal(0, fixture.Agent(fixture.Estate.Clock).Failures);

        // A run of the helper starts once the pause is lifted: its intent, judged as the records stood before it, stays valid whatever later
        // ties the courier, so the courier's assignment below is no write the boundary would refuse.
        fixture.RunIntent(assignment, "run-after-the-answer", helper);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));
        fixture.Estate.SetClock(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero));
        StandingAgents.Occurrence next = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences, o => o.Occurrence.Key != occurrence.Key).Occurrence;
        fixture.Assign(next, courier);
        model = fixture.Estate.Load();
        Assert.Equal(("open", 3), (Asks.State(model, pause).State, fixture.Agent(fixture.Estate.Clock).Failures));
        Assert.False(model.Findings.HasErrors, Errors(model.Findings));

        // The reverse never holds: the spare, which the roster names, writes the operator's decision by hand; it answers nothing while the
        // roster names the spare, and nothing once the roster drops it.
        fixture.Estate.Add("decision", spare, Answer(fixture, ask).ToJsonString());
        Assert.Equal(("open", 1), (Asks.State(fixture.Estate.Load(), ask).State, fixture.Agent(fixture.Estate.Clock).OpenOutputs));
        fixture.Add("roster", Roster(fixture, helper), supersedes: roster.Id);
        Assert.Equal(("open", 1), (Asks.State(fixture.Estate.Load(), ask).State, fixture.Agent(fixture.Estate.Clock).OpenOutputs));

        // The operator's own decision answers it.
        fixture.Add("decision", Answer(fixture, ask));
        Assert.Equal(("answered", 0), (Asks.State(fixture.Estate.Load(), ask).State, fixture.Agent(fixture.Estate.Clock).OpenOutputs));
    }

    [Fact]
    public void ADispositionByAnAgentTiedToTheGoalDisposesOfNothingWhateverItsDate()
    {
        using var fixture = new StandingFixture();
        (RecordEnvelope assignment, RecordEnvelope run, string key) = Carried(fixture);
        JsonObject raised = RaisedBy(fixture, assignment, run, key);
        RecordEnvelope declinable = fixture.Add("possibility", Possibility(raised, "cache/index"), builder: true);
        RecordEnvelope parkable = fixture.Add("possibility", Possibility(raised, "drop/legacy"), builder: true);
        RecordEnvelope adoptable = fixture.Add("possibility", Possibility(raised, "split/index", draft: true), builder: true);
        RecordEnvelope own = fixture.Add("possibility", Possibility(raised, "own/decline"), builder: true);
        string helper = Principal(fixture, "agent", "Helper", "contributor");
        string courier = Principal(fixture, "agent", "Courier", "contributor");
        string clerk = Principal(fixture, "agent", "Clerk", "contributor");
        DateTimeOffset raisedAt = fixture.Estate.Clock;
        Assert.Equal(4, fixture.Agent(fixture.Estate.Clock).OpenOutputs);
        var order = new JsonObject { ["type"] = "work-order", ["id"] = fixture.Work.Id, ["hash"] = fixture.Work.ComputedHash };
        JsonObject Disposed(RecordEnvelope of, string disposition) => Revised(of, b =>
        {
            b["disposition"] = disposition;
            b["reason"] = "Not worth it now.";
            if (disposition == "adopted") b["adoptedAs"] = order.DeepClone();
        });

        // Later, the roster names the helper for the researcher role, and an assignment names the courier as an occurrence's contributor.
        fixture.Estate.SetClock(raisedAt.AddMinutes(10));
        fixture.Add("roster", Roster(fixture, fixture.Estate.BuilderId, helper), supersedes: Assert.Single(fixture.Estate.Load().Current("roster")).Id);
        fixture.Estate.SetClock(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero));
        StandingAgents.Occurrence next = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences, o => o.Occurrence.Key != key).Occurrence;
        fixture.Assign(next, courier);
        DateTimeOffset now = fixture.Estate.Clock;

        // Written straight to records/, dated after the possibilities and before the records that tie their authors, the helper declines
        // one and adopts another and the courier parks a third, as the Reviewer's probe did. Each is reported (standing.output-now), none
        // is an error, and none disposes of anything: the four outputs stay open and counted, and each reads open.
        fixture.Estate.SetClock(raisedAt.AddMinutes(1));
        fixture.Estate.Add("possibility", helper, Disposed(declinable, "declined").ToJsonString(), supersedes: declinable.Id);
        fixture.Estate.Add("possibility", helper, Disposed(adoptable, "adopted").ToJsonString(), supersedes: adoptable.Id);
        fixture.Estate.Add("possibility", courier, Disposed(parkable, "parked").ToJsonString(), supersedes: parkable.Id);
        fixture.Estate.SetClock(now);
        EstateModel model = fixture.Estate.Load();
        Assert.False(model.Findings.HasErrors, Errors(model.Findings));
        Assert.Equal(3, model.Findings.Count(f => f.Code == StandingOutputs.BoundaryCode && f.Severity == Severity.Info && (f.Message.Contains(helper, StringComparison.Ordinal) || f.Message.Contains(courier, StringComparison.Ordinal))));
        Assert.Equal(4, fixture.Agent(fixture.Estate.Clock).OpenOutputs);
        Assert.Equal(4, StandingOutputs.OpenAndDeclined(model, fixture.Goal).Count);

        // A second open possibility with the declined one's fingerprint is refused as a duplicate, since that one was never declined.
        FindingList again = Validate(fixture, "possibility", Possibility(raised, "cache/index"), builder: true);
        Assert.Contains(again, f => f.Code == "standing.duplicate-output");
        Assert.DoesNotContain(again, f => f.Code == "standing.raises-again");

        // Written now, such a disposition is an error, the records tying its author having been written before it; and the agent's own
        // decline, by hand, is an error and disposes of nothing either.
        Assert.Contains(Validate(fixture, "possibility", Disposed(model.Latest(declinable), "parked"), supersedes: model.Latest(declinable).Id, by: helper),
            f => f.Code == "standing.own-output" && f.Severity == Severity.Error && f.Message.Contains(helper, StringComparison.Ordinal));
        fixture.Estate.Add("possibility", fixture.Estate.BuilderId, Disposed(own, "declined").ToJsonString(), supersedes: own.Id);
        model = fixture.Estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "standing.own-output" && f.Severity == Severity.Error && f.Message.Contains(own.Id, StringComparison.Ordinal));
        Assert.Equal(4, fixture.Agent(fixture.Estate.Clock).OpenOutputs);

        // A principal nothing ties to the goal disposes of them as before: the clerk, an agent, declines one by hand and the operator parks
        // another. Each reads as written, the open outputs fall, and the declined one is raised again by naming it.
        fixture.Estate.Add("possibility", clerk, Disposed(model.Latest(declinable), "declined").ToJsonString(), supersedes: model.Latest(declinable).Id);
        fixture.Add("possibility", Disposed(model.Latest(parkable), "parked"), supersedes: model.Latest(parkable).Id);
        model = fixture.Estate.Load();
        Assert.Equal(2, fixture.Agent(fixture.Estate.Clock).OpenOutputs);
        Assert.Equal([declinable.Id, adoptable.Id, own.Id], StandingOutputs.OpenAndDeclined(model, fixture.Goal).Select(p => model.Chain(p)[0]));
        JsonObject raisedAgain = Possibility(raised, "cache/index");
        raisedAgain["raisesAgain"] = declinable.Id;
        Assert.Equal(string.Empty, Errors(Validate(fixture, "possibility", raisedAgain, builder: true)));
    }

    [Fact]
    public void ARecordReusingADispositionsIdentifierLeavesTheOutputOpenAndTheAgentHeld()
    {
        // From 0.9.4 (FIX-156): the operator parks the agent's possibility and opens it again; the carrier writes by hand a record carrying the
        // identifier of the operator's reopening, parking it again, dated in an earlier month so that its path sorts first, or in a later one.
        // Neither copy is read, nor any revision of the possibility: it stays open and counted, and the agent is held until the copy is removed.
        foreach (DateTimeOffset month in new[] { StandingAgentTests.EarlierMonth, StandingAgentTests.LaterMonth })
        {
            using var fixture = new StandingFixture();
            (RecordEnvelope assignment, RecordEnvelope run, string key) = Carried(fixture);
            JsonObject raised = RaisedBy(fixture, assignment, run, key);
            RecordEnvelope possibility = fixture.Add("possibility", Possibility(raised, "cache/index"), builder: true);
            RecordEnvelope parked = fixture.Add("possibility", Revised(possibility, b =>
            {
                b["disposition"] = "parked";
                b["reason"] = "Not before the index format settles.";
            }), supersedes: possibility.Id);
            Assert.Equal(0, fixture.Agent(fixture.Estate.Clock).OpenOutputs);
            RecordEnvelope reopened = fixture.Add("possibility", Revised(parked, b =>
            {
                b["disposition"] = "open";
                b["reason"] = "The index format settled.";
            }), supersedes: parked.Id);
            Assert.False(fixture.Estate.Load().Findings.HasErrors, Errors(fixture.Estate.Load().Findings));
            Assert.Equal(1, fixture.Agent(fixture.Estate.Clock).OpenOutputs);

            RecordEnvelope copy = StandingAgentTests.Reusing(fixture, reopened, Revised(parked, b => b["reason"] = "Parked again by hand."), month, parked.Id);
            EstateModel model = fixture.Estate.Load();
            StandingAgents.Agent agent = fixture.Agent(fixture.Estate.Clock);
            Assert.Equal(("held", 1), (agent.State, agent.OpenOutputs));
            Assert.Contains("record.duplicate-id", agent.Reason, StringComparison.Ordinal);
            Assert.False(model.ById.ContainsKey(possibility.Id));
            Assert.Equal(new[] { copy.ExpectedPath, reopened.ExpectedPath }.Order(StringComparer.Ordinal), model.Findings.Where(f => f.Code == "record.duplicate-id").Select(f => f.Location).Order(StringComparer.Ordinal));

            StandingAgentTests.Remove(fixture, copy);
            agent = fixture.Agent(fixture.Estate.Clock);
            Assert.Equal(("active", 1), (agent.State, agent.OpenOutputs));
        }
    }

    [Fact]
    public void NoAgentWithdrawsAStandingAgentsAskOrClosesItsPublicationRequestByAnyRecord()
    {
        using var fixture = new StandingFixture();
        (RecordEnvelope assignment, RecordEnvelope run, string key) = Carried(fixture);
        JsonObject raised = RaisedBy(fixture, assignment, run, key);
        RecordEnvelope ask = fixture.Add("ask", Ask(fixture, raised), builder: true);
        var about = new RecordReference("work-order", fixture.Work.Id, fixture.Work.ComputedHash);
        RecordEnvelope request = fixture.Add("operator-request", Publication(fixture, raised, fixture.Estate.OperatorId, fixture.Estate.BuilderId), builder: true, subject: about);
        var closing = new RecordReference("operator-request", request.Id, request.ComputedHash);
        string helper = Principal(fixture, "agent", "Helper", "contributor");
        Assert.Equal(2, fixture.Agent(fixture.Estate.Clock).OpenOutputs);

        // Written straight to records/, the agent's withdrawal of its ask and its close of its publication request, and another agent's
        // observation of the request as posted, are errors of the estate, each at its own record and no other, and dispose of nothing: the
        // ask still waits on the operator, the request is not closed, and both stay counted.
        RecordEnvelope withdrawal = fixture.Add("ask", Revised(ask, b => b["state"] = "withdrawn"), supersedes: ask.Id, builder: true);
        RecordEnvelope closed = fixture.Add("operator-request-close", new JsonObject { ["outcome"] = "dismissed", ["reason"] = "Not now." }, builder: true, subject: closing);
        JsonObject observed = new()
        {
            ["kind"] = "operator-request", ["asOf"] = RecordParser.FormatTimestamp(fixture.Estate.Clock), ["summary"] = "Posted.", ["limitations"] = new JsonArray(),
            ["facts"] = new JsonObject { ["outcome"] = "done", ["destination"] = "the project's discussion board", ["location"] = "https://example.org/discussions/1", ["evidenceHash"] = FlowFixture.Hash('e') },
        };
        RecordEnvelope observation = fixture.Estate.Add("observation", helper, observed.ToJsonString(), closing);
        EstateModel model = fixture.Estate.Load();
        List<(string Code, string Location)> errors = [.. model.Findings.Where(f => f.Severity == Severity.Error).Select(f => (f.Code, f.Location)).Order()];
        string[] invalid = [withdrawal.Id, closed.Id, observation.Id];
        Assert.All(errors, e => Assert.Contains(invalid, id => e.Location.Contains(id, StringComparison.Ordinal)));
        Assert.Contains(errors, e => e.Code == "standing.own-output" && e.Location.Contains(withdrawal.Id, StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Code == "standing.own-output" && e.Location.Contains(closed.Id, StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Code == "standing.publication" && e.Location.Contains(observation.Id, StringComparison.Ordinal));
        Assert.Equal("open", Asks.State(model, ask).State);
        Assert.Contains(Asks.Deliveries(model, fixture.Estate.OperatorId).SelectMany(d => d.Asks), a => model.Chain(a)[0] == ask.Id);
        Assert.Empty(OperatorRequests.Closures(model, request));
        Assert.Equal(2, fixture.Agent(fixture.Estate.Clock).OpenOutputs);

        // The operator's decision and close are valid and dispose of both: no error lies at either, every error the estate reports still lies at
        // one of the agent's three records (which now also close a request the operator closed), and none of the earlier ones is gone.
        Assert.Equal(string.Empty, Errors(Validate(fixture, "decision", Answer(fixture, ask))));
        RecordEnvelope decided = fixture.Add("decision", Answer(fixture, ask));
        RecordEnvelope dismissed = fixture.Add("operator-request-close", new JsonObject { ["outcome"] = "dismissed", ["reason"] = "Not now." }, subject: closing);
        model = fixture.Estate.Load();
        List<(string Code, string Location)> after = [.. model.Findings.Where(f => f.Severity == Severity.Error).Select(f => (f.Code, f.Location))];
        Assert.All(after, e => Assert.Contains(invalid, id => e.Location.Contains(id, StringComparison.Ordinal)));
        Assert.DoesNotContain(after, e => e.Location.Contains(decided.Id, StringComparison.Ordinal) || e.Location.Contains(dismissed.Id, StringComparison.Ordinal));
        Assert.Subset(after.ToHashSet(), errors.ToHashSet());
        Assert.Equal("answered", Asks.State(model, ask).State);
        Assert.Single(OperatorRequests.Closures(model, request));
        Assert.Equal(0, fixture.Agent(fixture.Estate.Clock).OpenOutputs);
    }

    [Fact]
    public void ThePrincipalThatSteppedTheGoalRecordsTheOperatorsDecisionOnTheRuntimesAsksItWrote()
    {
        using var fixture = new StandingFixture(budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 6 }, period: new JsonObject { ["unit"] = "months", ["runs"] = 10, ["wallMinutes"] = 200 }, builderHolds: false);
        string builder = fixture.Estate.BuilderId;

        // The builder holds local-engineering, so it takes the goal's steps as goal step's caller; a helper agent the roster names carries the
        // occurrence, and the builder neither carries one nor was ever named for the role.
        fixture.Add("authority", new JsonObject { ["grantee"] = builder, ["scope"] = new JsonArray("local-engineering"), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = fixture.Policies.Decision.Id });
        string helper = Principal(fixture, "agent", "Helper", "contributor");
        fixture.Add("roster", Roster(fixture, helper), supersedes: Assert.Single(fixture.Estate.Load().Current("roster")).Id);
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence, helper);

        // Three of the helper's runs fail; the builder's goal step writes the failure pause's ask and the renewal ask, each put to the operator.
        RecordEnvelope pause = fixture.Add("ask", PauseAsk(fixture), builder: true, subject: Failed(fixture, assignment, helper).Reference);
        RecordEnvelope renewal = fixture.Add("ask", Ask(fixture, null, StandingAgents.RenewalAsk + " Research the widget before its until time 2027-06-01T00:00:00Z?"), builder: true, subject: fixture.Goal.Reference);
        EstateModel model = fixture.Estate.Load();
        Assert.Equal((fixture.Goal.Id, fixture.Goal.Id), (StandingOutputs.GuardedBy(model, pause)?.Id, StandingOutputs.GuardedBy(model, renewal)?.Id));
        Assert.Equal(3, fixture.Agent(fixture.Estate.Clock).Failures);

        // The helper, which carried the occurrence, answers neither, whatever decidedBy names; the builder records the operator's decision on
        // each ask it wrote, as it records the operator's answers everywhere.
        foreach (RecordEnvelope asked in new[] { pause, renewal })
        {
            Assert.Contains(Validate(fixture, "decision", Answer(fixture, asked), by: helper), f => f.Code == "standing.own-output" && f.Severity == Severity.Error);
            FindingList recorded = Validate(fixture, "decision", Answer(fixture, asked), builder: true);
            Assert.Equal(string.Empty, Errors(recorded));
            Assert.DoesNotContain(recorded, f => f.Code == StandingOutputs.BoundaryCode);
        }

        // Its decisions answer both, and the pause lifts.
        fixture.Add("decision", Answer(fixture, pause), builder: true);
        fixture.Add("decision", Answer(fixture, renewal), builder: true);
        model = fixture.Estate.Load();
        Assert.Equal(("answered", "answered"), (Asks.State(model, pause).State, Asks.State(model, renewal).State));
        Assert.Equal(0, fixture.Agent(fixture.Estate.Clock).Failures);
        Assert.False(model.Findings.HasErrors, Errors(model.Findings));
    }
}
