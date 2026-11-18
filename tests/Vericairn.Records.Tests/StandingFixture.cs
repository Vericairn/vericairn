// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Tests;

/// <summary>
/// A standing agent in a disposable format-3 estate (from 0.9.4, GOAL-07): a researcher flow with every trigger kind and one work step, the
/// standing work order, a roster naming the builder for the researcher role, and a stewardship goal whose plan carries standing, which the
/// operator sponsors and activates. The estate's clock moves one second with every record, and can be set.
/// </summary>
internal sealed class StandingFixture : IDisposable
{
    public StandingFixture(JsonArray? triggers = null, JsonObject? period = null, JsonObject? budget = null, string until = "2027-06-01T00:00:00Z", bool activate = true, string subject = "the widget", JsonObject? limits = null)
    {
        Policies = new PolicyFixture();
        Flow = FlowFixture.Install(Estate, Researcher());
        Work = Add("work-order", new JsonObject
        {
            ["key"] = "RES-1", ["kind"] = "ordinary", ["title"] = "Research the widget", ["intent"] = "Keep researching the widget between releases.", ["scope"] = new JsonArray("path:research/"),
            ["exitQuestion"] = "Is each occurrence's research recorded?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("cited findings"),
        });
        Add("roster", new JsonObject { ["roles"] = new JsonArray(FlowFixture.Role("researcher", Estate.BuilderId), FlowFixture.Role("decider", Estate.OperatorId)) });
        Goal = Add("goal", GoalBody(triggers, period, budget, until, subject, limits));
        if (activate)
        {
            Activate(Goal);
        }
    }

    public PolicyFixture Policies { get; }

    public TestEstate Estate => Policies.Estate;

    public TemplateDefinition Flow { get; }

    public RecordEnvelope Work { get; }

    public RecordEnvelope Goal { get; private set; }

    /// <summary>The researcher flow: every trigger kind, the researcher role, one work step and a never line.</summary>
    public static JsonObject Researcher() => StandingFlows.Researcher();

    /// <summary>A monthly schedule from the first day of October 2026.</summary>
    public static JsonObject Monthly(string key = "monthly", string firstAt = "2026-10-01T00:00:00Z") => StandingFlows.Monthly(key, firstAt);

    /// <summary>The standing goal's body: its budget each run's caps, its period's caps, and its plan carrying standing.</summary>
    public JsonObject GoalBody(JsonArray? triggers = null, JsonObject? period = null, JsonObject? budget = null, string until = "2027-06-01T00:00:00Z", string subject = "the widget", JsonObject? limits = null)
    {
        var body = new JsonObject
        {
            ["title"] = "Research the widget", ["objective"] = "Keep researching the widget between releases.",
            ["completion"] = new JsonArray(new JsonObject { ["record"] = FlowFixture.Marker("RESEARCHED") }), ["constraints"] = new JsonArray("nothing outside research/"),
            ["kind"] = "stewardship", ["sponsor"] = Estate.OperatorId, ["budget"] = budget ?? new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 2 },
            ["plan"] = new JsonObject
            {
                ["policy"] = "given", ["approval"] = "sponsor-approves", ["workOrders"] = new JsonArray(Work.Id),
                ["standing"] = new JsonObject
                {
                    ["pin"] = FlowFixture.Pin(Flow), ["subject"] = new JsonObject { ["topic"] = subject },
                    ["triggers"] = triggers ?? new JsonArray(Monthly()),
                    ["period"] = period ?? new JsonObject { ["unit"] = "months", ["runs"] = 4, ["wallMinutes"] = 120 },
                    ["until"] = until,
                },
            },
            ["inputs"] = new JsonArray(),
        };
        if (limits is not null) body["limits"] = limits;
        return body;
    }

    public RecordEnvelope Add(string type, JsonObject body, bool builder = false, RecordReference? subject = null, string? supersedes = null) =>
        Policies.Add(type, body, builder: builder, subject: subject, supersedes: supersedes);

    public RecordEnvelope Activate(RecordEnvelope goal) =>
        Add("goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated for the test." }, subject: goal.Reference);

    /// <summary>The agent as of a time.</summary>
    public StandingAgents.Agent Agent(DateTimeOffset asOf, IReadOnlyList<StandingAgents.DutyDue>? duties = null) =>
        StandingAgents.Derive(Estate.Load(), Goal, asOf, duties);

    /// <summary>The operator's assignment of an occurrence to the builder, as goal step would write it.</summary>
    public RecordEnvelope Assign(StandingAgents.Occurrence occurrence, string? contributor = null, string? key = null) =>
        Add("assignment", AssignmentBody(occurrence, contributor ?? Estate.BuilderId, key), subject: Work.Reference);

    public static JsonObject AssignmentBody(StandingAgents.Occurrence occurrence, string contributor, string? key = null) => new()
    {
        ["contributor"] = contributor,
        ["brief"] = new JsonObject
        {
            ["outcome"] = "Carry occurrence " + occurrence.Key + ".", ["sources"] = new JsonArray(), ["permittedChanges"] = new JsonArray("path:research/"), ["prohibitedEffects"] = new JsonArray("publication"),
            ["workspace"] = new JsonArray("research repository, a work branch"), ["limits"] = new JsonArray(), ["expectedOutputs"] = new JsonArray("findings"),
            ["reviewCriteria"] = new JsonArray("the findings cite their sources"), ["recovery"] = "Carry it again.",
        },
        ["occurrence"] = new JsonObject { ["key"] = key ?? occurrence.Key, ["trigger"] = occurrence.Trigger, ["revision"] = occurrence.Revision, ["item"] = JsonNode.Parse(occurrence.Item.GetRawText()) },
    };

    /// <summary>The contributor's attempt and result on an assignment, with an outcome.</summary>
    public RecordEnvelope Result(RecordEnvelope assignment, string outcome = "completed")
    {
        bool builder = EstateModel.BodyString(assignment, "contributor") == Estate.BuilderId;
        RecordEnvelope attempt = Add("attempt", new JsonObject { ["startedAt"] = RecordParser.FormatTimestamp(Estate.Clock), ["workspace"] = "research repository, a work branch", ["note"] = "Research." }, builder: builder, subject: assignment.Reference);
        return Add("result", new JsonObject
        {
            ["outcome"] = outcome, ["summary"] = "Researched.", ["outputs"] = new JsonArray(new JsonObject { ["name"] = "findings", ["hash"] = FlowFixture.Hash('f') }), ["evidence"] = new JsonArray(), ["checks"] = new JsonArray(),
            ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = "Review the findings.",
        }, builder: builder, subject: attempt.Reference);
    }

    /// <summary>The intent of an agent run carrying an assignment of the goal, with a cap of ten minutes.</summary>
    public RecordEnvelope RunIntent(RecordEnvelope assignment, string identity) => Add("operation", RunBody(assignment, identity, "not-started"));

    /// <summary>A run's settlement: its intent's revision with the status, observed and charged five minutes.</summary>
    public RecordEnvelope Settle(RecordEnvelope intent, string status)
    {
        RecordEnvelope assignment = Estate.Load().ById[intent.Body.GetProperty("target").GetProperty("assignment").GetString()!];
        JsonObject dispatched = RunBody(assignment, intent.Body.GetProperty("identity").GetString()!, "dispatched");
        dispatched["dispatchedAt"] = intent.At;
        RecordEnvelope running = Add("operation", dispatched, supersedes: intent.Id);
        JsonObject body = RunBody(assignment, intent.Body.GetProperty("identity").GetString()!, status);
        body["dispatchedAt"] = intent.At;
        body["observed"] = new JsonObject { ["summary"] = "The run ended.", ["at"] = RecordParser.FormatTimestamp(Estate.Clock) };
        body["usage"] = new JsonObject { ["wallMinutes"] = 5, ["unreported"] = new JsonArray() };
        return Add("operation", body, supersedes: running.Id);
    }

    public JsonObject RunBody(RecordEnvelope assignment, string identity, string status) => new()
    {
        ["provider"] = "stand-in", ["action"] = "agent-run", ["identity"] = identity, ["intent"] = "Carry the occurrence.",
        ["target"] = new JsonObject { ["assignment"] = assignment.Id, ["principal"] = Estate.BuilderId, ["workspace"] = "research, branch work/res-1", ["goal"] = Goal.Id },
        ["status"] = status, ["evidence"] = new JsonArray(), ["cap"] = new JsonObject { ["wallMinutes"] = 10, ["attemptsPerStep"] = Goal.Body.GetProperty("budget").GetProperty("attemptsPerStep").GetInt32() },
    };

    /// <summary>The operator's review of a result.</summary>
    public RecordEnvelope Review(RecordEnvelope result, string outcome) => Add("review", new JsonObject
    {
        ["independence"] = "operator", ["outcome"] = outcome, ["summary"] = "Read the exact result.", ["findings"] = outcome == "pass" ? new JsonArray() : new JsonArray(new JsonObject { ["severity"] = "error", ["summary"] = "Cite the second source." }),
    }, subject: new RecordReference("result", result.Id, result.ComputedHash));

    public void Dispose() => Policies.Dispose();
}
