// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// The agent-run ledger (the estate's design/goals.md § 9, decision 01M4YBR5PGCPQD94BVNC547EBA): each run's cap recorded before it
/// starts and kept, its usage after, unknown usage charged at the cap and never zero, a run in flight holding its whole cap so that
/// concurrent runs never spend the same allowance twice, and a run that starts only while its goal is active and within its budget.
/// The record type holds every rule, so a run recorded by any route is judged the same.
/// </summary>
public class AgentRunLedgerTests
{
    [Theory]
    [InlineData("known-failure")]
    [InlineData("canceled")]
    [InlineData("uncertain")]
    public void AnAgentRunIdentityCannotBeDispatchedAgainEvenWithReconciliation(string status)
    {
        var ledger = new Ledger("""{"wallMinutes":60,"attemptsPerStep":10}""");
        RecordEnvelope prior = ledger.Run("one-launch", Small, status == "uncertain" ? null : Usage, status: status);
        var body = System.Text.Json.Nodes.JsonNode.Parse(prior.Body.GetRawText())!.AsObject();
        body["status"] = "dispatched";
        body["reconciliation"] = "The earlier process exited; try again.";
        body.Remove("observed");
        body.Remove("usage");
        RecordEnvelope retried = ledger.Estate.Add("operation", ledger.Estate.OperatorId, body.ToJsonString(), supersedes: prior.Id);
        Assert.Contains(ledger.Estate.Load().Findings, f => f.Location == retried.ExpectedPath && f.Code == "operation.agent-run-relaunch");
    }

    [Fact]
    public void ARunIsChargedWhatItsUsageReportsAndItsCapForWhatWasNotReported()
    {
        var ledger = new Ledger("""{"wallMinutes":60,"attemptsPerStep":4,"tokens":10000}""");
        RecordEnvelope silent = ledger.Run("run-silent", """{"wallMinutes":30,"attemptsPerStep":4,"tokens":4000}""", """{"wallMinutes":12,"unreported":["tokens"]}""");
        RecordEnvelope reported = ledger.Run("run-reported", """{"wallMinutes":10,"attemptsPerStep":4,"tokens":2000}""", """{"wallMinutes":5,"tokens":1200,"unreported":[]}""");
        RecordEnvelope unstarted = ledger.Run("run-unstarted", """{"wallMinutes":5,"attemptsPerStep":4,"tokens":500}""", """{"wallMinutes":0,"tokens":0,"unreported":[]}""", dispatched: false);
        RecordEnvelope flight = ledger.Run("run-flight", """{"wallMinutes":10,"attemptsPerStep":4,"tokens":1000}""", usage: null);
        EstateModel model = ledger.Estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);

        Assert.Equal(new Dictionary<string, long> { ["wallMinutes"] = 12, ["tokens"] = 4000 }, AgentRuns.ChargeOf(AgentRuns.Find(model, silent.Id)!));
        Assert.Equal(new Dictionary<string, long> { ["wallMinutes"] = 5, ["tokens"] = 1200 }, AgentRuns.ChargeOf(AgentRuns.Find(model, reported.Id)!));
        Assert.Equal(new Dictionary<string, long> { ["wallMinutes"] = 10, ["tokens"] = 1000 }, AgentRuns.ChargeOf(AgentRuns.Find(model, flight.Id)!));
        Assert.Empty(AgentRuns.ChargeOf(AgentRuns.Find(model, unstarted.Id)!));
        AgentRuns.Ledger spent = AgentRuns.Of(model, model.ById[ledger.Goal]);
        Assert.Equal(27, spent.Spent["wallMinutes"]);
        Assert.Equal(6200, spent.Spent["tokens"]);
        Assert.Equal(33, spent.Remaining("wallMinutes"));
        Assert.Equal(3800, spent.Remaining("tokens"));
        Assert.Null(spent.Remaining("currency"));
        Assert.Equal(1, spent.InFlight);
        Assert.Equal(3, spent.Attempts([ledger.Assignment]));
    }

    [Fact]
    public void AnIntentOutsideTheBudgetsUnitsOrBeyondWhatRemainsIsRefused()
    {
        var ledger = new Ledger("""{"wallMinutes":60,"attemptsPerStep":2,"tokens":1000}""");
        RecordEnvelope beyond = ledger.Intent("beyond", """{"wallMinutes":70,"attemptsPerStep":2,"tokens":1000}""");
        RecordEnvelope tokenless = ledger.Intent("tokenless", """{"wallMinutes":10,"attemptsPerStep":2}""");
        RecordEnvelope attempts = ledger.Intent("attempts", """{"wallMinutes":10,"tokens":10}""");
        RecordEnvelope concurrent = ledger.Intent("concurrent", """{"wallMinutes":10,"attemptsPerStep":2,"tokens":10,"concurrency":2}""");
        RecordEnvelope currency = ledger.Intent("currency", """{"wallMinutes":10,"attemptsPerStep":2,"tokens":10,"currency":{"amount":100,"unit":"USD"}}""");
        List<Finding> findings = ledger.Estate.Load().Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.Contains(findings, f => f.Location == beyond.ExpectedPath && f.Code == "operation.budget" && f.Message.Contains("The run's cap of 70 wallMinutes is more than the 60 that remain", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Location == tokenless.ExpectedPath && f.Code == "operation.cap" && f.Message.Contains("The goal's budget caps tokens, and the run's cap states none", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Location == attempts.ExpectedPath && f.Code == "operation.cap" && f.Message.Contains("attempts", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Location == concurrent.ExpectedPath && f.Code == "operation.cap" && f.Message.Contains("states no concurrency", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Location == currency.ExpectedPath && f.Code == "operation.cap" && f.Message.Contains("does not grant", StringComparison.Ordinal));
    }

    [Fact]
    public void RunsInFlightHoldTheirCapsSoConcurrentRunsNeverSpendTheSameAllowanceTwice()
    {
        const string Budget = """{"wallMinutes":60,"attemptsPerStep":3,"concurrency":2}""";
        var overlapping = new Ledger(Budget);
        overlapping.Intent("first", """{"wallMinutes":40,"attemptsPerStep":3}""");
        RecordEnvelope twice = overlapping.Intent("second", """{"wallMinutes":30,"attemptsPerStep":3}""");
        Assert.Contains(overlapping.Estate.Load().Findings, f => f.Location == twice.ExpectedPath && f.Code == "operation.budget" && f.Message.Contains("more than the 20 that remain", StringComparison.Ordinal));

        var sharing = new Ledger(Budget);
        sharing.Intent("first", """{"wallMinutes":40,"attemptsPerStep":3}""");
        RecordEnvelope rest = sharing.Intent("second", """{"wallMinutes":20,"attemptsPerStep":3}""");
        RecordEnvelope third = sharing.Intent("third", """{"wallMinutes":1,"attemptsPerStep":3}""");
        List<Finding> findings = sharing.Estate.Load().Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.DoesNotContain(findings, f => f.Location == rest.ExpectedPath);
        Assert.Contains(findings, f => f.Location == third.ExpectedPath && f.Message.Contains("The goal's budget is spent: wallMinutes 60 of 60", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Location == third.ExpectedPath && f.Message.Contains("2 of the goal's runs are in flight, and its budget allows 2 at a time", StringComparison.Ordinal));

        // A goal that states no concurrency runs one at a time: an absent cap is not an unlimited one.
        var single = new Ledger("""{"wallMinutes":60,"attemptsPerStep":3}""");
        single.Intent("first", """{"wallMinutes":10,"attemptsPerStep":3}""");
        RecordEnvelope waiting = single.Intent("second", """{"wallMinutes":10,"attemptsPerStep":3}""");
        Assert.Contains(single.Estate.Load().Findings, f => f.Location == waiting.ExpectedPath && f.Message.Contains("allows 1 at a time, one when it states no concurrency", StringComparison.Ordinal));
    }

    [Fact]
    public void ARunStartsOnlyWhileItsGoalIsActiveAndAStepHasNoMoreRunsThanItsAttempts()
    {
        var ledger = new Ledger("""{"wallMinutes":60,"attemptsPerStep":2}""");
        ledger.Run("one", """{"wallMinutes":5,"attemptsPerStep":2}""", """{"wallMinutes":5,"unreported":[]}""");
        ledger.Run("two", """{"wallMinutes":5,"attemptsPerStep":2}""", """{"wallMinutes":5,"unreported":[]}""");
        RecordEnvelope third = ledger.Intent("three", """{"wallMinutes":5,"attemptsPerStep":2}""");
        List<Finding> findings = ledger.Estate.Load().Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.Contains(findings, f => f.Location == third.ExpectedPath && f.Code == "operation.budget" && f.Message.Contains("The step has had 2 runs, the 2 attempts per step", StringComparison.Ordinal));
        Assert.Single(findings);

        var paused = new Ledger("""{"wallMinutes":60,"attemptsPerStep":2}""");
        paused.Status("paused");
        RecordEnvelope waiting = paused.Intent("paused", """{"wallMinutes":5,"attemptsPerStep":2}""");
        Assert.Contains(paused.Estate.Load().Findings, f => f.Location == waiting.ExpectedPath && f.Code == "operation.fenced" && f.Message.Contains("is paused: a run starts only while its goal is active", StringComparison.Ordinal));

        var proposed = new Ledger("""{"wallMinutes":60,"attemptsPerStep":2}""", activate: false);
        RecordEnvelope unfunded = proposed.Intent("unfunded", """{"wallMinutes":5,"attemptsPerStep":2}""");
        Assert.Contains(proposed.Estate.Load().Findings, f => f.Location == unfunded.ExpectedPath && f.Code == "operation.fenced" && f.Message.Contains("is proposed", StringComparison.Ordinal));
    }

    [Fact]
    public void ARunKeepsItsCapAndItsSettlementStatesEveryUnitOfIt()
    {
        var ledger = new Ledger("""{"wallMinutes":60,"attemptsPerStep":3,"tokens":1000,"currency":{"amount":500,"unit":"USD"}}""");
        const string Cap = """{"wallMinutes":10,"attemptsPerStep":3,"tokens":100,"currency":{"amount":50,"unit":"USD"}}""";
        RecordEnvelope raised = ledger.Run("raised", Cap, """{"wallMinutes":4,"unreported":["tokens","currency"]}""", settledCap: """{"wallMinutes":20,"attemptsPerStep":3,"tokens":100,"currency":{"amount":50,"unit":"USD"}}""");
        RecordEnvelope silent = ledger.Run("silent", Cap, """{"wallMinutes":4,"unreported":["currency"]}""");
        RecordEnvelope foreign = ledger.Run("foreign", Cap, """{"wallMinutes":4,"tokens":10,"currency":{"amount":5,"unit":"EUR"},"unreported":[]}""");
        RecordEnvelope whole = ledger.Run("whole", Cap, """{"wallMinutes":4,"tokens":10,"unreported":["currency"]}""");
        List<Finding> findings = ledger.Estate.Load().Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.Contains(findings, f => f.Location == raised.ExpectedPath && f.Code == "operation.cap" && f.Message.Contains("keeps the cap its intent recorded", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Location == silent.ExpectedPath && f.Code == "operation.usage" && f.Message.Contains("states the tokens spent or lists tokens as unreported", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Location == foreign.ExpectedPath && f.Code == "operation.usage" && f.Message.Contains("in EUR, and its cap in USD", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, f => f.Location == whole.ExpectedPath);
        Assert.Equal(new Dictionary<string, long> { ["wallMinutes"] = 4, ["tokens"] = 10, ["currency"] = 50 }, AgentRuns.ChargeOf(AgentRuns.Find(ledger.Estate.Load(), whole.Id)!));
    }

    [Fact]
    public void AStopRecordsEachFactOnlyOnceObservedAndContainmentOnlyWhenTheRunSettles()
    {
        var ledger = new Ledger("""{"wallMinutes":60,"attemptsPerStep":10}""");
        string op = ledger.Estate.OperatorId;
        RecordEnvelope contained = ledger.Run("contained", Small, Usage, stop: $$"""{"requestedAt":"2026-10-13T12:00:20Z","requestedBy":"{{op}}","reason":"Cancelled.","transmittedAt":"2026-10-13T12:00:21Z","transmission":"SIGTERM","escalatedAt":"2026-10-13T12:00:31Z","escalation":"The process tree was killed.","containedAt":"2026-10-13T12:00:32Z"}""");
        RecordEnvelope byWorker = ledger.Run("capped", Small, Usage, stop: """{"requestedAt":"2026-10-13T12:00:20Z","requestedBy":"worker","reason":"The run reached its cap.","transmittedAt":"2026-10-13T12:00:20Z","transmission":"SIGTERM","acknowledgedAt":"2026-10-13T12:00:21Z","acknowledgment":"It said so.","containedAt":"2026-10-13T12:00:22Z"}""");
        RecordEnvelope early = ledger.Run("early", Small, Usage, stop: $$"""{"requestedAt":"2026-10-13T12:00:20Z","requestedBy":"{{op}}","transmittedAt":"2026-10-13T12:00:19Z","transmission":"SIGTERM","containedAt":"2026-10-13T12:00:22Z"}""");
        RecordEnvelope unsent = ledger.Run("unsent", Small, Usage, stop: $$"""{"requestedAt":"2026-10-13T12:00:20Z","requestedBy":"{{op}}","acknowledgedAt":"2026-10-13T12:00:21Z","containedAt":"2026-10-13T12:00:22Z"}""");
        RecordEnvelope stranger = ledger.Run("stranger", Small, Usage, stop: """{"requestedAt":"2026-10-13T12:00:20Z","requestedBy":"01M4TRNE1GYEK4P8MXQAB3C8Q4"}""");
        RecordEnvelope uncertain = ledger.Run("uncertain", Small, usage: null, status: "uncertain", stop: $$"""{"requestedAt":"2026-10-13T12:00:20Z","requestedBy":"{{op}}","transmittedAt":"2026-10-13T12:00:21Z","transmission":"SIGTERM","containedAt":"2026-10-13T12:00:22Z"}""");
        RecordEnvelope release = ledger.Estate.Add("operation", op, """{"provider":"github-releases","action":"dispatch-release-workflow","identity":"github-releases:Example/widget:v1.0.0","intent":"Build 1.0.0.","target":{"repository":"Example/widget"},"status":"not-started","evidence":[],"stop":{"requestedAt":"2026-10-13T12:00:20Z","requestedBy":"worker"}}""");
        List<Finding> findings = ledger.Estate.Load().Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.DoesNotContain(findings, f => f.Location.StartsWith(contained.ExpectedPath, StringComparison.Ordinal) || f.Location.StartsWith(byWorker.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Location == early.ExpectedPath + "#body.stop.transmittedAt" && f.Code == "operation.stop");
        Assert.Contains(findings, f => f.Location == unsent.ExpectedPath && f.Message.Contains("acknowledged or escalated only once it was transmitted", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Location == stranger.ExpectedPath + "#body.stop.requestedBy" && f.Code == "operation.stop");
        Assert.Contains(findings, f => f.Location == uncertain.ExpectedPath && f.Message.Contains("Containment is observed when the run settles", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Location == release.ExpectedPath && f.Message.Contains("A stop's facts describe agent runs", StringComparison.Ordinal));
    }

    [Fact]
    public void ALaterRecordNeverUnmakesAnIntentJudgedBeforeIt()
    {
        var ledger = new Ledger("""{"wallMinutes":60,"attemptsPerStep":3,"concurrency":2}""");
        RecordEnvelope first = ledger.Intent("first", """{"wallMinutes":40,"attemptsPerStep":3}""");
        RecordEnvelope second = ledger.Intent("second", """{"wallMinutes":20,"attemptsPerStep":3}""");
        ledger.Settle(first, """{"wallMinutes":40,"attemptsPerStep":3}""", """{"wallMinutes":55,"unreported":[]}""");
        EstateModel model = ledger.Estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
        Assert.Equal(-15, AgentRuns.Of(model, model.ById[ledger.Goal]).Remaining("wallMinutes"));
    }

    private const string Small = """{"wallMinutes":5,"attemptsPerStep":10}""";

    private const string Usage = """{"wallMinutes":1,"unreported":[]}""";

    /// <summary>A goal the operator sponsors and activated, its work order and the builder's assignment, and runs recorded against it.</summary>
    private sealed class Ledger
    {
        public Ledger(string budget, bool activate = true)
        {
            Estate = new TestEstate(recordFormat: 2);
            RecordEnvelope goal = Estate.Add("goal", Estate.OperatorId, $$$"""{"title":"Build the widget","objective":"Ship it.","completion":[{"evidence":"It ships.","check":"Read it."}],"constraints":[],"kind":"finite","sponsor":"{{{Estate.OperatorId}}}","budget":{{{budget}}},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}""");
            Goal = goal.Id;
            Reference = goal.Reference;
            if (activate)
            {
                Status("active");
            }

            RecordEnvelope order = Estate.Add("work-order", Estate.OperatorId, $$"""{"key":"W-1","kind":"ordinary","title":"Build it","intent":"Build it.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[{"type":"goal","id":"{{Goal}}"}],"deliverables":["widget"]}""");
            Assignment = Estate.Add("assignment", Estate.OperatorId, $$$"""{"contributor":"{{{Estate.BuilderId}}}","brief":{"outcome":"A widget.","sources":[],"permittedChanges":["the widget"],"prohibitedEffects":[],"workspace":["widget"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["it builds"],"recovery":"Record a result."}}""", order.Reference).Id;
        }

        public TestEstate Estate { get; }

        public string Goal { get; }

        public RecordReference Reference { get; }

        public string Assignment { get; }

        public void Status(string status) => Estate.Add("goal-status", Estate.OperatorId, $$"""{"status":"{{status}}","note":"As the test has it."}""", Reference);

        public RecordEnvelope Intent(string identity, string cap) => Estate.Add("operation", Estate.OperatorId, Body(identity, "not-started", cap, string.Empty));

        public RecordEnvelope Settle(RecordEnvelope intent, string cap, string usage)
        {
            string identity = EstateModel.BodyString(intent, "identity")!;
            string started = $$""","dispatchedAt":"{{intent.At}}" """;
            RecordEnvelope dispatched = Estate.Add("operation", Estate.OperatorId, Body(identity, "dispatched", cap, started), supersedes: intent.Id);
            return Estate.Add("operation", Estate.OperatorId, Body(identity, "observed-success", cap, $$"""{{started}},"observed":{"summary":"It ran.","at":"{{dispatched.At}}"},"usage":{{usage}}"""), supersedes: dispatched.Id);
        }

        /// <summary>A run from its intent: dispatched (or not), then settled with its usage, or left in flight without one.</summary>
        public RecordEnvelope Run(string identity, string cap, string? usage, bool dispatched = true, string? settledCap = null, string status = "known-failure", string? stop = null)
        {
            RecordEnvelope intent = Intent(identity, cap);
            if (usage is null && status != "uncertain")
            {
                return intent;
            }

            RecordEnvelope latest = intent;
            string started = dispatched ? $$""","dispatchedAt":"{{intent.At}}" """ : string.Empty;
            if (dispatched)
            {
                latest = Estate.Add("operation", Estate.OperatorId, Body(identity, "dispatched", cap, started), supersedes: intent.Id);
            }

            string observed = status == "uncertain" ? string.Empty : ""","observed":{"summary":"It ran.","at":"2026-10-13T12:00:40Z"}""";
            string used = usage is null ? string.Empty : $$""","usage":{{usage}}""";
            string stopped = stop is null ? string.Empty : $$""","stop":{{stop}}""";
            return Estate.Add("operation", Estate.OperatorId, Body(identity, status, settledCap ?? cap, started + observed + used + stopped), supersedes: latest.Id);
        }

        private string Body(string identity, string status, string cap, string extra) =>
            $$"""{"provider":"claude-code","action":"agent-run","identity":"{{identity}}","intent":"Run the builder.","target":{"assignment":"{{Assignment}}","principal":"{{Estate.BuilderId}}","workspace":"widget, branch work/w-1","goal":"{{Goal}}"},"status":"{{status}}","evidence":[],"cap":{{cap}}{{extra}}}""";
    }
}
