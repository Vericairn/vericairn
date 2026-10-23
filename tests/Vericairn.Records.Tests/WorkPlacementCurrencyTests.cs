// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public sealed class WorkPlacementCurrencyTests
{
    [Fact]
    public void PlacementChangesKeepExistingRecordsCurrentWithoutRewritingThem()
    {
        using var f = new PolicyFixture();
        RecordEnvelope original = Order(f);
        IReadOnlyList<RecordEnvelope> records = RecordsOn(f, original);
        IReadOnlyList<RecordFile> before = EstateLoader.EnumerateRecordFiles(f.Estate.Root);
        RecordEnvelope current = original;
        JsonNode?[] placements =
        [
            Backlog("next"),
            new JsonObject { ["kind"] = "release", ["carrier"] = original.Id, ["product"] = original.Body.GetProperty("releasePlan").GetProperty("product").GetString(), ["version"] = "1.0.0" },
            null,
            Backlog("later"),
        ];
        int count = before.Count;
        foreach (JsonNode? placement in placements)
        {
            JsonObject body = PolicyFixture.Body(current);
            if (placement is null) body.Remove("placement");
            else body["placement"] = placement;
            current = f.Add("work-order", body, supersedes: current.Id);
            EstateModel model = f.Estate.Load();
            Assert.Equal(++count, model.Records.Count);
            AssertCurrency(model, current, records, current: true);
            Assert.DoesNotContain(model.Findings, IsStale);
            Assert.Equal("accepted", EstateStatus.WorkItems(model).Single(w => w.Id == current.Id).Status);
            Assert.False(model.IsCurrent(original));
            Assert.Equal(current.Id, model.Latest(original).Id);
        }

        // Re-reading the already recorded history clears the warnings without any compensating records or changed pins.
        EstateModel reloaded = f.Estate.Load();
        Assert.Equal(count, reloaded.Records.Count);
        AssertCurrency(reloaded, current, records, current: true);
        IReadOnlyList<RecordFile> after = EstateLoader.EnumerateRecordFiles(f.Estate.Root);
        Assert.Equal(count, after.Count);
        Assert.All(before, file => Assert.Equal(file.Bytes, after.Single(a => a.Path == file.Path).Bytes));
        Assert.All(records, record => Assert.Equal(record.ComputedHash, reloaded.ById[record.Id].ComputedHash));
        Assert.All(records, record => Assert.Equal(record.Subject, reloaded.ById[record.Id].Subject));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnIdenticalBodyReissueIsStillStaleEvenWhenFollowedByAMove(bool placed)
    {
        using var f = new PolicyFixture();
        RecordEnvelope original = Order(f);
        if (!placed)
        {
            JsonObject body = PolicyFixture.Body(original);
            body.Remove("placement");
            original = f.Add("work-order", body, supersedes: original.Id);
        }
        IReadOnlyList<RecordEnvelope> records = RecordsOn(f, original);
        RecordEnvelope reissued = f.Add("work-order", PolicyFixture.Body(original), supersedes: original.Id);
        AssertCurrency(f.Estate.Load(), reissued, records, current: false);
        RecordEnvelope moved = Move(f, reissued, "next");
        AssertCurrency(f.Estate.Load(), moved, records, current: false);
    }

    [Theory]
    [InlineData("key")]
    [InlineData("kind")]
    [InlineData("title")]
    [InlineData("intent")]
    [InlineData("scope")]
    [InlineData("exitQuestion")]
    [InlineData("inputs")]
    [InlineData("deliverables")]
    [InlineData("after")]
    [InlineData("blockedBy")]
    [InlineData("role")]
    [InlineData("releasePlan")]
    public void EveryOtherBodyFieldStillMakesThePinnedRecordsStale(string field)
    {
        using var f = new PolicyFixture();
        RecordEnvelope original = Order(f);
        IReadOnlyList<RecordEnvelope> records = RecordsOn(f, original);
        JsonObject body = PolicyFixture.Body(original);
        body["placement"] = Backlog("next");
        if (field == "releasePlan") body[field]!["version"] = "1.1.0";
        else body[field] = field switch
        {
            "key" => JsonValue.Create("TASK-REVISED"),
            "kind" => JsonValue.Create("milestone"),
            "role" => JsonValue.Create("reviewer"),
            "scope" or "inputs" or "deliverables" => new JsonArray(body[field]!.AsArray().Reverse().Select(n => n!.DeepClone()).ToArray()),
            "after" or "blockedBy" => new JsonArray(),
            _ => JsonValue.Create("Changed work content."),
        };
        RecordEnvelope current = f.Add("work-order", body, supersedes: original.Id);
        EstateModel model = f.Estate.Load();
        AssertCurrency(model, current, records, current: false);
        Assert.Equal("accepted (earlier revision)", EstateStatus.WorkItems(model).Single(w => w.Id == current.Id).Status);
    }

    [Theory]
    [InlineData("after", false)]
    [InlineData("after", true)]
    [InlineData("blockedBy", false)]
    [InlineData("blockedBy", true)]
    [InlineData("role", false)]
    [InlineData("role", true)]
    [InlineData("releasePlan", false)]
    [InlineData("releasePlan", true)]
    public void AddingOrRemovingOtherOptionalFieldsIsNotAPlacementChange(string field, bool adding)
    {
        using var f = new PolicyFixture();
        RecordEnvelope original = Order(f);
        JsonObject without = PolicyFixture.Body(original);
        without.Remove(field);
        if (adding) original = f.Add("work-order", without, supersedes: original.Id);
        IReadOnlyList<RecordEnvelope> records = RecordsOn(f, original);
        JsonObject body = adding ? PolicyFixture.Body(f.Estate.Load().ById[original.Supersedes!]) : without;
        body["placement"] = Backlog("next");
        RecordEnvelope current = f.Add("work-order", body, supersedes: original.Id);
        AssertCurrency(f.Estate.Load(), current, records, current: false);
    }

    [Fact]
    public void MixedChainsAreJudgedFromEachPinAndAContentRevertDoesNotReviveOldJudgments()
    {
        using var f = new PolicyFixture();
        RecordEnvelope original = Order(f);
        IReadOnlyList<RecordEnvelope> beforeEdit = RecordsOn(f, original);
        RecordEnvelope moved = Move(f, original, "next");
        AssertCurrency(f.Estate.Load(), moved, beforeEdit, current: true);

        JsonObject edited = PolicyFixture.Body(moved);
        edited["title"] = "Revised inventory";
        RecordEnvelope content = f.Add("work-order", edited, supersedes: moved.Id);
        IReadOnlyList<RecordEnvelope> afterEdit = RecordsOn(f, content);
        moved = Move(f, content, "later");
        EstateModel model = f.Estate.Load();
        AssertCurrency(model, moved, beforeEdit, current: false);
        AssertCurrency(model, moved, afterEdit, current: true);

        RecordEnvelope reverted = f.Add("work-order", PolicyFixture.Body(original), supersedes: moved.Id);
        IReadOnlyList<RecordEnvelope> afterRevert = RecordsOn(f, reverted);
        moved = Move(f, reverted, "last");
        model = f.Estate.Load();
        AssertCurrency(model, moved, beforeEdit, current: false);
        AssertCurrency(model, moved, afterEdit, current: false);
        AssertCurrency(model, moved, afterRevert, current: true);
    }

    [Theory]
    [InlineData("assignment")]
    [InlineData("review")]
    [InlineData("acceptance")]
    public void PlacementChangesDoNotHideAnInvalidOriginalHash(string type)
    {
        using var f = new PolicyFixture();
        RecordEnvelope original = Order(f);
        RecordEnvelope valid = RecordsOn(f, original).Single(r => r.Type == type && r.Subject!.Id == original.Id);
        RecordEnvelope invalid = f.Add(type, PolicyFixture.Body(valid),
            subject: new RecordReference("work-order", original.Id, "sha256:" + new string('f', 64)), supersedes: valid.Id);
        Move(f, original, "next");
        EstateModel model = f.Estate.Load();
        Assert.Contains(model.Findings, finding => finding.Location == invalid.ExpectedPath && finding.Code == "record.subject-hash" && finding.Severity == Severity.Error);
        Assert.Contains(model.Findings, finding => finding.Location == invalid.ExpectedPath && finding.Code == "record.subject-stale");
    }

    [Theory]
    [InlineData("assignment", "attempt")]
    [InlineData("attempt", "result")]
    [InlineData("result", "review")]
    public void ThePlacementExceptionDoesNotMakeOtherRevisedSubjectsCurrent(string type, string dependentType)
    {
        using var f = new PolicyFixture();
        RecordEnvelope original = Order(f);
        IReadOnlyList<RecordEnvelope> records = RecordsOn(f, original);
        Move(f, original, "next");
        RecordEnvelope subject = records.Single(r => r.Type == type);
        RecordEnvelope dependent = records.Single(r => r.Type == dependentType && r.Subject!.Id == subject.Id);
        JsonObject body = PolicyFixture.Body(subject);
        if (type == "assignment") body["brief"]!["outcome"] = "A revised inventory.";
        else body[type == "attempt" ? "note" : "summary"] = "Reconsider the inventory.";
        f.Add(type, body, builder: type != "assignment", subject: subject.Subject, supersedes: subject.Id);
        EstateModel model = f.Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Contains(model.Findings, finding => finding.Location == dependent.ExpectedPath && finding.Code == "record.subject-stale");
    }

    private static RecordEnvelope Order(PolicyFixture f)
    {
        RecordEnvelope product = f.Add("product", new()
        {
            ["name"] = "Widget", ["slug"] = "widget", ["summary"] = "An inventory fixture.",
            ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Fixture." }),
            ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Fixture." }),
        });
        JsonObject body = new()
        {
            ["key"] = "PRIOR-1", ["kind"] = "ordinary", ["title"] = "Inventory", ["intent"] = "Check the inventory.",
            ["scope"] = new JsonArray("inventory", "report"), ["exitQuestion"] = "Was the inventory checked?",
            ["inputs"] = new JsonArray(PolicyFixture.Ref(f.Decision), PolicyFixture.Ref(product)), ["deliverables"] = new JsonArray("inventory", "report"),
        };
        RecordEnvelope prior = f.Add("work-order", body);
        body["key"] = "TASK-1";
        body["after"] = new JsonArray(prior.Id);
        body["blockedBy"] = new JsonArray(PolicyFixture.Ref(f.Decision));
        body["role"] = "builder";
        body["releasePlan"] = new JsonObject { ["product"] = product.Id, ["version"] = "1.0.0" };
        body["placement"] = Backlog("future");
        return f.Add("work-order", body);
    }

    private static IReadOnlyList<RecordEnvelope> RecordsOn(PolicyFixture f, RecordEnvelope order)
    {
        RecordEnvelope attempt = f.BeginAttempt(order: order);
        RecordEnvelope assignment = f.Estate.Load().ById[attempt.Subject!.Id];
        RecordEnvelope result = f.Add("result", PolicyFixture.ResultBody(), builder: true, subject: attempt.Reference);
        RecordEnvelope review = f.Add("review", new() { ["independence"] = "self", ["outcome"] = "pass", ["summary"] = "Reviewed the inventory work.", ["findings"] = new JsonArray() }, subject: order.Reference);
        RecordEnvelope resultReview = f.Review(result);
        RecordEnvelope acceptance = f.Add("acceptance", new() { ["statement"] = "Accepted the inventory.", ["basis"] = new JsonArray(), ["self"] = true }, subject: order.Reference);
        return [assignment, attempt, result, review, resultReview, acceptance];
    }

    private static JsonObject Backlog(string key) => new() { ["kind"] = "backlog", ["key"] = key };

    private static RecordEnvelope Move(PolicyFixture f, RecordEnvelope previous, string backlog)
    {
        JsonObject body = PolicyFixture.Body(previous);
        body["placement"] = Backlog(backlog);
        return f.Add("work-order", body, supersedes: previous.Id);
    }

    private static bool IsStale(Finding finding) => finding.Code is "record.subject-stale" or "acceptance.unresolved";

    private static void AssertCurrency(EstateModel model, RecordEnvelope order, IReadOnlyList<RecordEnvelope> records, bool current)
    {
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        foreach (RecordEnvelope record in records)
        {
            Assert.True(model.IsCurrent(record));
            bool stale = !current && record.Subject!.Type == "work-order";
            Assert.Equal(stale, model.Findings.Any(f => f.Location == record.ExpectedPath && f.Code == "record.subject-stale"));
            if (record.Type == "acceptance")
                Assert.Equal(stale, model.Findings.Any(f => f.Location == record.ExpectedPath && f.Code == "acceptance.unresolved"));
        }
        RecordEnvelope assignment = records.Single(r => r.Type == "assignment");
        string brief = WorkView.RenderBrief(model, order);
        Assert.Contains($"Pinned to work-order revision {assignment.Subject!.Hash}" +
            (current ? " (current)" : " (an earlier revision; review before continuing)"), brief, StringComparison.Ordinal);
        string assignmentLine = WorkView.RenderObserve(model, order).Split('\n').Single(line => line.StartsWith("  assignment " + assignment.Id, StringComparison.Ordinal));
        Assert.Equal(!current, assignmentLine.Contains("[pinned to an earlier work-order revision]", StringComparison.Ordinal));
    }
}
