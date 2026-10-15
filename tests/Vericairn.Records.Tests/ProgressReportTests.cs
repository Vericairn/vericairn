// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Records.Tests.GoalEvaluationTests;

namespace Vericairn.Records.Tests;

/// <summary>
/// The progress report (decision 01M4ZSF918H5D7N99VNMK8YK81), section by section over small estates: where the estate stands, the work
/// orders by state, each published release and what it contained, what comes next by release, the next eligible work and what waits on
/// the operator; every line names the records it rests on, and the same records give the same report, byte for byte.
/// </summary>
public class ProgressReportTests
{
    private const string Repository = "Vericairn/vericairn";

    [Fact]
    public void WhereTheEstateStandsNamesThePinTheFormatAndTheCheckWithTheRecordsTheyRestOn()
    {
        using var estate = new TestEstate(recordFormat: 2);
        string text = ProgressReport.Render(estate.Load(), EstateChecks.Run(estate.Load()));
        Assert.Contains("\nWhere the estate stands\n  Pinned to source Vericairn/vericairn@0123456789ab [estate/manifest.json]\n  Record format 2 [estate/manifest.json]\n", text, StringComparison.Ordinal);
        Assert.Contains("  Check: 0 errors, 0 warnings [every record and the repository, as vericairn estate check runs them]\n", text, StringComparison.Ordinal);
        Assert.StartsWith($"Progress of Test Estate (test-estate), rendered from its records [estate {estate.Estate.Id}]\n", text, StringComparison.Ordinal);

        // A published release pinned by a migration that moved the format: the pin rests on the manifest, the release and the migration.
        RecordEnvelope product = Product(estate);
        RecordEnvelope published = Publish(estate, product, "0.1.0", Commit('a'));
        RecordEnvelope earlier = estate.Add("migration", estate.OperatorId, """{"fromPin":"source Vericairn/vericairn@0123456789ab","toPin":"release 0.0.9","fromFormat":1,"toFormat":1,"migrations":[],"changes":[]}""");
        RecordEnvelope migration = estate.Add("migration", estate.OperatorId, $$"""{"fromPin":"release 0.0.9","toPin":"release 0.1.0","fromFormat":1,"toFormat":2,"migrations":["record-format-2"],"changes":["pinned"],"release":"{{published.Id}}"}""");
        PinRelease(estate, "0.1.0");
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        text = ProgressReport.Render(model, EstateChecks.Run(model));
        Assert.Contains($"  Pinned to release 0.1.0 [estate/manifest.json, release {published.Id}, migration {migration.Id}]\n", text, StringComparison.Ordinal);
        Assert.Contains($"  Record format 2 [estate/manifest.json, migration {migration.Id}]\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain(earlier.Id, text, StringComparison.Ordinal);

        // The check is status's: a stray file is a repository finding, listed with its location; the records alone do not show it.
        estate.WriteFile("stray.txt", "scratch\n");
        model = estate.Load();
        text = ProgressReport.Render(model, EstateChecks.Run(model));
        Assert.Contains("  Check: 1 error, 0 warnings [every record and the repository, as vericairn estate check runs them]\n    ERROR layout.unexpected stray.txt: ", text, StringComparison.Ordinal);
        Assert.Contains("Findings: 1 errors, 0 warnings", EstateStatus.Render(model, EstateChecks.Run(model)), StringComparison.Ordinal);
        string recordsOnly = ProgressReport.Render(model, null);
        Assert.Contains("  Check: 0 errors, 0 warnings in the records [every record; the repository checks, which read this document, are vericairn estate check's]\n", recordsOnly, StringComparison.Ordinal);
        using JsonDocument json = JsonDocument.Parse(ProgressReport.RenderJson(model, EstateChecks.Run(model)));
        JsonElement standing = json.RootElement.GetProperty("standing");
        Assert.Equal("records and repository", standing.GetProperty("check").GetProperty("scope").GetString());
        Assert.Equal(1, standing.GetProperty("check").GetProperty("errors").GetInt32());
        Assert.Equal(published.Id, standing.GetProperty("pin").GetProperty("release").GetString());
        Assert.Equal(migration.Id, standing.GetProperty("recordFormat").GetProperty("migration").GetString());
        Assert.Equal("records", JsonDocument.Parse(ProgressReport.RenderJson(model, null)).RootElement.GetProperty("standing").GetProperty("check").GetProperty("scope").GetString());
    }

    [Fact]
    public void WorkOrdersAreGroupedByTheStateStatusDerivesEachWithTheRecordItRestsOn()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope planned = Order(estate, "W-1");
        RecordEnvelope active = Order(estate, "W-2");
        RecordEnvelope activeStatus = estate.Add("work-status", estate.OperatorId, """{"status":"active","note":"Started."}""", new RecordReference("work-order", active.Id, null));
        RecordEnvelope milestone = Order(estate, "M-1", kind: "milestone");
        RecordEnvelope submitted = estate.Add("work-status", estate.OperatorId, """{"status":"submitted","note":"For acceptance."}""", new RecordReference("work-order", milestone.Id, null));
        RecordEnvelope done = Order(estate, "W-3");
        estate.Add("work-status", estate.OperatorId, """{"status":"submitted","note":"Done."}""", new RecordReference("work-order", done.Id, null));
        RecordEnvelope accepted = Accept(estate, done);
        RecordEnvelope revisedFirst = Order(estate, "W-4");
        RecordEnvelope earlierAcceptance = Accept(estate, revisedFirst);
        RecordEnvelope revised = estate.Add("work-order", estate.OperatorId, revisedFirst.Body.GetRawText().Replace("\"Work.\"", "\"More work.\"", StringComparison.Ordinal), supersedes: revisedFirst.Id);
        RecordEnvelope retired = Order(estate, "W-5");
        RecordEnvelope retiredStatus = estate.Add("work-status", estate.OperatorId, """{"status":"retired","note":"Not needed."}""", new RecordReference("work-order", retired.Id, null));
        EstateModel model = estate.Load();

        string text = ProgressReport.Render(model, null);
        string section = Section(text, "Work orders by state");
        Assert.Equal(
            $"""
            Work orders by state: 6 current
              planned: 1
                W-1  ordinary   Order W-1 [work-order {planned.Id}]
              active: 1
                W-2  ordinary   Order W-2 [work-order {active.Id}, work-status {activeStatus.Id}]
              submitted: 1
                M-1  milestone  Order M-1 [work-order {milestone.Id}, work-status {submitted.Id}]
              retired: 1
                W-5  ordinary   Order W-5 [work-order {retired.Id}, work-status {retiredStatus.Id}]
              accepted: 1
                W-3  ordinary   Order W-3 [work-order {done.Id}, acceptance {accepted.Id}]
              accepted (earlier revision): 1
                W-4  ordinary   Order W-4 [work-order {revised.Id}, acceptance {earlierAcceptance.Id}]

            """,
            section);

        // Every state is the one status derives.
        using JsonDocument json = JsonDocument.Parse(ProgressReport.RenderJson(model, null));
        var reported = json.RootElement.GetProperty("workOrders").GetProperty("states").EnumerateArray()
            .SelectMany(s => s.GetProperty("workOrders").EnumerateArray().Select(o => (Key: o.GetProperty("key").GetString()!, Status: o.GetProperty("status").GetString()!, State: s.GetProperty("state").GetString()!)))
            .OrderBy(o => o.Key, StringComparer.Ordinal).ToList();
        Assert.Equal(EstateStatus.WorkItems(model).Select(i => (i.Key, i.Status, i.Status)), reported);
        Assert.Equal(6, json.RootElement.GetProperty("workOrders").GetProperty("total").GetInt32());
    }

    [Fact]
    public void EachPublishedReleaseListsWhatTheReleaseNotesDeriveFromItsWindow()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope product = Product(estate);
        (RecordEnvelope first, RecordEnvelope firstResult) = Worked(estate, "W-1");
        RecordEnvelope firstAcceptance = estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted.","basis":[],"self":false}""", firstResult.Reference);
        RecordEnvelope released = Publish(estate, product, "0.1.0", Commit('a'));

        // After 0.1.0: W-2 is accepted, W-3's result is not, and a withdrawn 0.2.0 falls into the window of the published 0.2.1.
        (RecordEnvelope second, RecordEnvelope secondResult) = Worked(estate, "W-2");
        RecordEnvelope secondAcceptance = estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted.","basis":[],"self":false}""", secondResult.Reference);
        (RecordEnvelope third, RecordEnvelope thirdResult) = Worked(estate, "W-3");
        RecordEnvelope withdrawn = estate.Add("release", estate.OperatorId, ReleaseBody("0.2.0", Commit('b'), "candidate"), new RecordReference("product", product.Id, null));
        estate.Add("release", estate.OperatorId, ReleaseBody("0.2.0", Commit('b'), "withdrawn"), new RecordReference("product", product.Id, null), supersedes: withdrawn.Id);
        RecordEnvelope next = Publish(estate, product, "0.2.1", Commit('c'));
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));

        string section = Section(ProgressReport.Render(model, null), "Published releases and what each contained");
        Assert.StartsWith("Published releases and what each contained: 2 published of 3 releases recorded\n", section, StringComparison.Ordinal);
        Assert.Contains($"  Example [product {product.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains($"    0.1.0  published 2026-10-13 from commit aaaaaaaaaaaa [release {released.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains($"      accepted: W-1 Order W-1 [result {firstResult.Id}, acceptance {firstAcceptance.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains($"    0.2.1  published 2026-10-13 from commit cccccccccccc [release {next.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains($" after the publication of 0.1.0 up to its own, 2 results [release {released.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains($"      accepted: W-2 Order W-2 [result {secondResult.Id}, acceptance {secondAcceptance.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains($"      recorded, not yet accepted: W-3 Order W-3 [result {thirdResult.Id}]\n", section, StringComparison.Ordinal);
        Assert.DoesNotContain("0.2.0  published", section, StringComparison.Ordinal);
        Assert.Equal(1, section.Split("accepted: W-1", StringSplitOptions.None).Length - 1);

        // The report reads the release notes' own derivation: the windows and results are ReleaseContents', release by release.
        IReadOnlyList<ReleaseContents.Published> windows = ReleaseContents.PublishedReleases(model);
        Assert.Equal([released.Id, next.Id], windows.Select(w => w.Release.Id));
        Assert.Null(windows[0].Previous);
        Assert.Equal(released.Id, windows[1].Previous!.Id);
        Assert.Equal(ReleaseContents.Window(model, released.At, next.At).Select(r => r.Id), windows[1].Window.Select(r => r.Id));
        (IReadOnlyList<ReleaseContents.Accepted> accepted, IReadOnlyList<RecordEnvelope> notAccepted) = ReleaseContents.Results(model, windows[1].Window);
        Assert.Equal([second.Id], accepted.Select(a => a.Order!.Id));
        Assert.Equal([thirdResult.Id], notAccepted.Select(r => r.Id));
        using JsonDocument json = JsonDocument.Parse(ProgressReport.RenderJson(model, null));
        JsonElement releases = json.RootElement.GetProperty("releases");
        Assert.Equal(3, releases.GetProperty("recorded").GetInt32());
        JsonElement latest = releases.GetProperty("published")[1];
        Assert.Equal(windows[1].Window.Count, latest.GetProperty("window").GetProperty("records").GetInt32());
        Assert.Equal("0.1.0", latest.GetProperty("window").GetProperty("after").GetProperty("version").GetString());
        Assert.Equal(third.Id, latest.GetProperty("notAccepted")[0].GetProperty("workOrder").GetString());
        Assert.Equal(first.Id, releases.GetProperty("published")[0].GetProperty("accepted")[0].GetProperty("workOrder").GetString());
    }

    [Fact]
    public void WhatComesNextIsEachReleaseWorkOrderNotYetAcceptedWithTheWorkOrdersAmongItsInputs()
    {
        using var estate = new TestEstate(recordFormat: 2);
        string note = "  Until goals plan releases (GOAL-06), the only source is each release work order not yet accepted, a work order whose key begins with REL-, and the work orders it carries, which are its input work orders.\n";
        Assert.Contains(note + "  No release work order waits for its acceptance.\n", ProgressReport.Render(estate.Load(), null), StringComparison.Ordinal);

        RecordEnvelope done = Order(estate, "W-1");
        RecordEnvelope acceptance = Accept(estate, done);
        RecordEnvelope first = Order(estate, "W-2");
        RecordEnvelope current = estate.Add("work-order", estate.OperatorId, first.Body.GetRawText().Replace("\"Work.\"", "\"Revised work.\"", StringComparison.Ordinal), supersedes: first.Id);
        RecordEnvelope decision = estate.Add("decision", estate.BuilderId, Decision(estate, string.Empty));
        string inputs = $$"""{"type":"decision","id":"{{decision.Id}}"},{"type":"work-order","id":"{{done.Id}}"},{"type":"work-order","id":"{{first.Id}}"},{"type":"work-order","id":"{{current.Id}}"}""";
        RecordEnvelope release = estate.Add("work-order", estate.OperatorId, OrderBody("REL-020", "ordinary", inputs));
        RecordEnvelope shipped = estate.Add("work-order", estate.OperatorId, OrderBody("REL-010", "ordinary", $$"""{"type":"work-order","id":"{{done.Id}}"}"""));
        Accept(estate, shipped);
        estate.Add("work-order", estate.OperatorId, OrderBody("W-9", "ordinary", $$"""{"type":"work-order","id":"{{done.Id}}"}"""));
        EstateModel model = estate.Load();

        Assert.Equal(
            $"""
            What comes next, by release
            {note}  REL-020  planned  Order REL-020 [work-order {release.Id}]
                2 work orders, 1 accepted
                W-1  accepted  Order W-1 [work-order {done.Id}, acceptance {acceptance.Id}]
                W-2  planned   Order W-2 [work-order {current.Id}]

            """,
            Section(ProgressReport.Render(model, null), "What comes next, by release"));
        using JsonDocument json = JsonDocument.Parse(ProgressReport.RenderJson(model, null));
        JsonElement next = Assert.Single(json.RootElement.GetProperty("next").GetProperty("releases").EnumerateArray());
        Assert.Equal("REL-020", next.GetProperty("workOrder").GetProperty("key").GetString());
        Assert.Equal(1, next.GetProperty("accepted").GetInt32());
        Assert.Equal(["W-1", "W-2"], next.GetProperty("carries").EnumerateArray().Select(c => c.GetProperty("key").GetString()));
    }

    [Fact]
    public void TheNextEligibleWorkIsThePlannedWorkThatNothingHolds()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope first = Order(estate, "A-1");
        RecordEnvelope ask = estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Which way?"));
        RecordEnvelope owed = estate.Add("decision", estate.BuilderId, Decision(estate, $$""","obligations":[{"key":"report","summary":"Write the report.","owner":"{{estate.BuilderId}}","resolution":"evidence","discharge":"The report is in the estate."}]"""));
        RecordEnvelope intent = estate.Add("operation", estate.OperatorId, Operation("not-started"));
        RecordEnvelope holder = Order(estate, "A-2");
        RecordEnvelope afterFirst = estate.Add("work-order", estate.OperatorId, OrderBody("B-1", "ordinary", string.Empty, after: first.Id));
        RecordEnvelope blocked = estate.Add("work-order", estate.OperatorId, OrderBody("B-2", "ordinary", string.Empty, blockedBy: $$"""{"type":"ask","id":"{{ask.Id}}"},{"type":"decision","id":"{{owed.Id}}"},{"type":"operation","id":"{{intent.Id}}"},{"type":"work-order","id":"{{holder.Id}}"}"""));
        RecordEnvelope active = Order(estate, "C-1");
        estate.Add("work-status", estate.OperatorId, """{"status":"active","note":"Started."}""", new RecordReference("work-order", active.Id, null));
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, Assignment(estate.BuilderId), first.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));

        IReadOnlyList<NextEligible.Item> view = NextEligible.Derive(model);
        Assert.Equal(["A-1", "A-2", "B-1", "B-2"], view.Select(i => EstateModel.BodyString(i.Order, "key")));
        Assert.Equal(["A-1", "A-2"], NextEligible.Eligible(model).Select(i => EstateModel.BodyString(i.Order, "key")));
        NextEligible.Item held = view.Single(i => i.Order.Id == blocked.Id);
        Assert.Equal(["open", "obligations open", "not-started", "planned"], held.Relations.Select(r => r.State));
        Assert.All(held.Relations, r => Assert.True(r.Holds));
        Assert.Equal(["report"], held.Relations[1].Open);
        string section = Section(ProgressReport.Render(model, null), "The next eligible work");
        Assert.StartsWith("The next eligible work: 2 of 4 planned work orders\n  Planned work orders whose after work orders are all accepted and whose blockedBy holds nothing open; routing by roster and budget is GOAL-04's.\n", section, StringComparison.Ordinal);
        Assert.Contains($"  A-1  ordinary   Order A-1 [work-order {first.Id}]\n    assigned to Test Builder: no attempt [assignment {assignment.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains($"  Held: 2\n    B-1  ordinary   Order B-1 [work-order {afterFirst.Id}]\n      after A-1: planned, holds it [work-order {first.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains($"      blockedBy ask {ask.Id}: open, holds it [ask {ask.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains($"      blockedBy decision {owed.Id}: obligations open (report), holds it [decision {owed.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains($"      blockedBy operation {intent.Id}: not-started, holds it [operation {intent.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains($"      blockedBy A-2: planned, holds it [work-order {holder.Id}]\n", section, StringComparison.Ordinal);
        Assert.DoesNotContain("C-1", section, StringComparison.Ordinal);

        // Accepting A-1 frees B-1; answering the ask, discharging the obligation, canceling the operation and retiring A-2 free B-2.
        RecordEnvelope accepted = Accept(estate, first);
        RecordEnvelope answer = estate.Add("decision", estate.BuilderId, Decision(estate, $$""","answers":["{{ask.Id}}"]"""));
        RecordEnvelope discharge = estate.Add("discharge", estate.BuilderId, """{"obligation":"report","summary":"Written.","evidence":[{"summary":"The report."}]}""", new RecordReference("decision", owed.Id, null));
        RecordEnvelope canceled = estate.Add("operation", estate.OperatorId, Operation("canceled"), supersedes: intent.Id);
        estate.Add("work-status", estate.OperatorId, """{"status":"retired","note":"Not needed."}""", new RecordReference("work-order", holder.Id, null));
        model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Equal(["B-1", "B-2"], NextEligible.Eligible(model).Select(i => EstateModel.BodyString(i.Order, "key")));
        section = Section(ProgressReport.Render(model, null), "The next eligible work");
        Assert.Contains($"    after A-1: accepted [work-order {first.Id}, acceptance {accepted.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains($"    blockedBy ask {ask.Id}: answered [ask {ask.Id}, decision {answer.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains($"    blockedBy decision {owed.Id}: obligations discharged [decision {owed.Id}, discharge {discharge.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains($"    blockedBy operation {intent.Id}: canceled [operation {canceled.Id}]\n", section, StringComparison.Ordinal);
        Assert.Contains("  Held: 0\n", section, StringComparison.Ordinal);
        using JsonDocument json = JsonDocument.Parse(ProgressReport.RenderJson(model, null));
        Assert.Equal(["B-1", "B-2"], json.RootElement.GetProperty("eligible").GetProperty("eligible").EnumerateArray().Select(e => e.GetProperty("key").GetString()));
        Assert.Equal(2, json.RootElement.GetProperty("eligible").GetProperty("planned").GetInt32());
    }

    [Fact]
    public void WhatWaitsOnTheOperatorIsTheOpenAsksAndTheAcceptancesOnlyTheOperatorGives()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope open = estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Open question?"));
        RecordEnvelope notifiedFirst = estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Notified question?"));
        RecordEnvelope notified = estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Notified question?", "notified"), supersedes: notifiedFirst.Id);
        RecordEnvelope answered = estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Answered question?"));
        estate.Add("decision", estate.BuilderId, Decision(estate, $$""","answers":["{{answered.Id}}"]"""));
        RecordEnvelope withdrawnFirst = estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Withdrawn question?"));
        estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Withdrawn question?", "withdrawn"), supersedes: withdrawnFirst.Id);
        estate.Add("ask", estate.OperatorId, Ask(estate.BuilderId, "A question to the builder?"));

        // A milestone submitted, and the results awaiting review toward milestones not yet accepted; ordinary work is not the operator's alone.
        RecordEnvelope milestone = Order(estate, "M-1", kind: "milestone");
        RecordEnvelope submitted = estate.Add("work-status", estate.OperatorId, """{"status":"submitted","note":"For acceptance."}""", new RecordReference("work-order", milestone.Id, null));
        RecordEnvelope ordinary = Order(estate, "O-1");
        estate.Add("work-status", estate.OperatorId, """{"status":"submitted","note":"For acceptance."}""", new RecordReference("work-order", ordinary.Id, null));
        (RecordEnvelope milestoneWork, RecordEnvelope milestoneResult) = Worked(estate, "M-2", "milestone");
        (RecordEnvelope acceptedMilestone, _) = Worked(estate, "M-3", "milestone");
        Accept(estate, acceptedMilestone);
        Worked(estate, "W-2");

        // A candidate nobody accepted, and one the operator accepted; a goal ready for its sponsor, the operator.
        RecordEnvelope product = Product(estate);
        RecordEnvelope candidate = estate.Add("release", estate.OperatorId, ReleaseBody("0.3.0", Commit('d'), "candidate"), new RecordReference("product", product.Id, null));
        RecordEnvelope acceptedCandidate = estate.Add("release", estate.OperatorId, ReleaseBody("0.4.0", Commit('e'), "candidate"), new RecordReference("product", product.Id, null));
        estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted.","basis":[],"self":true}""", acceptedCandidate.Reference);
        Work goal = Work.Write(estate);
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Go."}""", goal.Goal.Reference);
        RecordEnvelope evaluation = estate.Add("review", goal.Reviewer.Id, Evaluation("pass", Met(goal.Result), Met(goal.Result)), goal.Goal.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));

        string section = Section(ProgressReport.Render(model, null), "What waits on the operator");
        Assert.Equal(
            $"""
            What waits on the operator
              Asks awaiting an answer: 2
                to Test Operator, operator [principal {estate.OperatorId}]: 2
                open: Open question? [ask {open.Id}]
                  recommendation: Answer yes.
                notified: Notified question? [ask {notified.Id}]
                  recommendation: Answer yes.
              Acceptances only the operator gives: 4
                milestone work orders submitted: 1
                  M-1  Order M-1 [work-order {milestone.Id}, work-status {submitted.Id}]
                results submitted toward milestone work orders: 1
                  M-2  result completed by Test Builder, awaiting review [result {milestoneResult.Id}, assignment {Assignments(model, milestoneWork)}]
                release candidates recorded and not accepted: 1
                  0.3.0 from commit dddddddddddd [release {candidate.Id}]
                goals ready for acceptance by an operator: 1
                  Release the widget, accepted by its sponsor, Test Operator ({estate.OperatorId}) [goal {goal.Goal.Id}, review {evaluation.Id}]

            """,
            section);
        using JsonDocument json = JsonDocument.Parse(ProgressReport.RenderJson(model, null));
        JsonElement waiting = json.RootElement.GetProperty("operator");
        Assert.Equal(["open", "notified"], waiting.GetProperty("asks").EnumerateArray().Select(a => a.GetProperty("state").GetString()));
        Assert.Equal("Answer yes.", waiting.GetProperty("asks")[0].GetProperty("recommendation").GetString());
        Assert.Equal(candidate.ComputedHash, waiting.GetProperty("acceptances").GetProperty("releaseCandidates")[0].GetProperty("hash").GetString());
        Assert.Equal(evaluation.Id, waiting.GetProperty("acceptances").GetProperty("goals")[0].GetProperty("evaluation").GetString());
    }

    [Fact]
    public void TheSameRecordsGiveTheSameReportByteForByteInTextAndInJson()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope product = Product(estate);
        (_, RecordEnvelope result) = Worked(estate, "W-1");
        estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted.","basis":[],"self":false}""", result.Reference);
        Publish(estate, product, "0.1.0", Commit('a'));
        RecordEnvelope first = Order(estate, "W-2");
        estate.Add("work-order", estate.OperatorId, OrderBody("REL-020", "ordinary", $$"""{"type":"work-order","id":"{{first.Id}}"}""", after: first.Id));
        estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Which way?"));
        EstateModel model = estate.Load();
        string text = ProgressReport.Render(model, EstateChecks.Run(model));
        string json = ProgressReport.RenderJson(model, EstateChecks.Run(model));

        // Rendered again, and from a copy of the estate at another path loaded afresh, the bytes are the same; nothing reads the clock,
        // the machine or the path, and the machine form is canonical.
        Assert.Equal(text, ProgressReport.Render(estate.Load(), EstateChecks.Run(estate.Load())));
        string copy = Path.Combine(Path.GetTempPath(), "vericairn-progress-copy-" + Identity.Ulid.NewUlid().Value);
        try
        {
            Copy(estate.Root, copy);
            EstateModel copied = EstateLoader.Load(copy);
            Assert.Equal(text, ProgressReport.Render(copied, EstateChecks.Run(copied)));
            Assert.Equal(json, ProgressReport.RenderJson(copied, EstateChecks.Run(copied)));
            Assert.Equal(ProgressReport.Render(model, null), ProgressReport.Render(copied, null));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal(json, CanonicalJson.SerializePretty(parsed.RootElement));
        Assert.Equal(ProgressReport.Format, parsed.RootElement.GetProperty("format").GetString());
        Assert.DoesNotContain(estate.Root, text, StringComparison.Ordinal);
        Assert.DoesNotContain(estate.Root, json, StringComparison.Ordinal);
        Assert.Equal(["Progress of ", "Where the estate stands", "Work orders by state", "Published releases and what each contained", "What comes next, by release", "The next eligible work", "What waits on the operator"],
            text.Split("\n\n").Select(s => s.Split('\n')[0]).Select(h => h.StartsWith("Progress of ", StringComparison.Ordinal) ? "Progress of " : h.Split(':')[0]));
    }

    internal static RecordEnvelope Product(TestEstate estate) =>
        estate.Add("product", estate.OperatorId, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");

    internal static string Commit(char digit) => new(digit, 40);

    internal static string ReleaseBody(string version, string commit, string status) =>
        $$"""{"version":"{{version}}","repository":"{{Repository}}","commit":"{{commit}}","status":"{{status}}","artifacts":[{"name":"example-{{version}}-linux-x64.zip","hash":"sha256:{{new string('1', 64)}}","bytes":10,"platform":"linux-x64"}],"recordFormat":{"reads":[1,2],"writes":2},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}""";

    /// <summary>A candidate, the operator's acceptance of its exact hash, and its publication.</summary>
    internal static RecordEnvelope Publish(TestEstate estate, RecordEnvelope product, string version, string commit)
    {
        var subject = new RecordReference("product", product.Id, null);
        RecordEnvelope candidate = estate.Add("release", estate.OperatorId, ReleaseBody(version, commit, "candidate"), subject);
        estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted from the recorded evidence.","basis":[],"self":true}""", candidate.Reference);
        string published = ReleaseBody(version, commit, "candidate").Replace("\"status\":\"candidate\"", $"\"status\":\"published\",\"publishedAt\":\"{RecordParser.FormatTimestamp(estate.Clock)}\",\"url\":\"https://example.invalid/releases/{version}\"", StringComparison.Ordinal);
        return estate.Add("release", estate.OperatorId, published, subject, supersedes: candidate.Id);
    }

    internal static string OrderBody(string key, string kind, string inputs, string? after = null, string? blockedBy = null) =>
        $$"""{"key":"{{key}}","kind":"{{kind}}","title":"Order {{key}}","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[{{inputs}}],"deliverables":["work"]{{(after is null ? string.Empty : $",\"after\":[\"{after}\"]")}}{{(blockedBy is null ? string.Empty : $",\"blockedBy\":[{blockedBy}]")}}}""";

    internal static RecordEnvelope Order(TestEstate estate, string key, string kind = "ordinary") => estate.Add("work-order", estate.OperatorId, OrderBody(key, kind, string.Empty));

    /// <summary>The operator's acceptance of a work order's exact revision; the operator wrote it, so it is the operator's own.</summary>
    internal static RecordEnvelope Accept(TestEstate estate, RecordEnvelope order) =>
        estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted.","basis":[],"self":true}""", order.Reference);

    /// <summary>A work order, the builder's assignment and attempt, and its completed result.</summary>
    internal static (RecordEnvelope Order, RecordEnvelope Result) Worked(TestEstate estate, string key, string kind = "ordinary")
    {
        RecordEnvelope order = Order(estate, key, kind);
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, Assignment(estate.BuilderId), order.Reference);
        RecordEnvelope attempt = estate.Add("attempt", estate.BuilderId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"example repository, work branch","note":"Do it."}""", assignment.Reference);
        RecordEnvelope result = estate.Add("result", estate.BuilderId, """{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""", attempt.Reference);
        return (order, result);
    }

    internal static string Ask(string to, string question, string state = "open") =>
        $$"""{"to":"{{to}}","question":"{{question}}","recommendation":"Answer yes.","consequence":"Nothing moves.","state":"{{state}}"}""";

    internal static string Decision(TestEstate estate, string extra) =>
        $$"""{"title":"Decided","statement":"Do it.","summary":"Decided.","synthesis":"The operator decided.","effects":["done"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":[]{{extra}}}""";

    internal static string Operation(string status) =>
        $$"""{"provider":"example","action":"dispatch-build","identity":"example:build","intent":"Build it.","target":{"repository":"Example/example"},"status":"{{status}}","evidence":[]}""";

    /// <summary>The section of a report that begins with a heading, up to the next blank line, with that line.</summary>
    internal static string Section(string report, string heading)
    {
        int start = report.IndexOf("\n" + heading, StringComparison.Ordinal) + 1;
        Assert.True(start > 0, $"no section {heading} in:\n{report}");
        int end = report.IndexOf("\n\n", start, StringComparison.Ordinal);
        return end < 0 ? report[start..] : report[start..(end + 1)];
    }

    /// <summary>Pins the estate's manifest to a release, with the hash of a distributable for one platform, as an upgrade writes it.</summary>
    internal static void PinRelease(TestEstate estate, string version)
    {
        string manifest = $$$"""{"estate":"{{{estate.Estate.Id}}}","format":"vericairn.estate-manifest/1","recordFormat":{{{estate.RecordFormat}}},"vericairn":{"artifacts":{"linux-x64":"sha256:{{{new string('1', 64)}}}"},"mode":"release","repository":"{{{Repository}}}","version":"{{{version}}}"}}""";
        using JsonDocument document = JsonDocument.Parse(manifest);
        File.WriteAllBytes(Path.Combine(estate.Root, EstateLayout.ManifestPath), CanonicalJson.SerializePrettyUtf8(document.RootElement));
    }

    private static string Assignments(EstateModel model, RecordEnvelope order) => model.Current("assignment").Single(a => a.Subject?.Id == order.Id).Id;

    private static void Copy(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
