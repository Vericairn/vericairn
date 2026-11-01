// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class RunAttentionReconciliationTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void OpeningWithdrawsOnlyTerminalProcessWarningsAndASecondOpenWritesNothing(long format)
    {
        using var fixture = new RunAttentionFixture(format);
        TestEstate estate = fixture.Estate;
        var withdrawals = new List<(RecordEnvelope Ask, RecordEnvelope Run, RecordEnvelope Settlement)>();
        var untouched = new List<(RecordEnvelope Ask, string State)>();
        foreach (string status in new[] { "observed-success", "known-failure", "canceled" })
        {
            RecordEnvelope run = fixture.Run();
            RecordEnvelope warning = fixture.Warning(run);
            RecordEnvelope notified = fixture.Warning(run, "loop", "critical");
            notified = fixture.ReviseAsk(notified, "notified");
            RecordEnvelope stall = fixture.Ask(run, $"Run {run.Id} needs attention: at {run.At} the worker observed no stdout event for 60 seconds. Inspect the run?");
            RecordEnvelope answered = fixture.Warning(run, "scope-read");
            fixture.Answer(answered);
            RecordEnvelope withdrawn = fixture.ReviseAsk(fixture.Warning(run, "audit-unverified"), "withdrawn");
            RecordEnvelope obsoleteAnswer = fixture.Warning(run, "execution");
            RecordEnvelope decision = fixture.Answer(obsoleteAnswer);
            JsonObject revised = JsonNode.Parse(decision.Body.GetRawText())!.AsObject();
            revised.Remove("answers");
            estate.Add("decision", estate.OperatorId, revised.ToJsonString(), supersedes: decision.Id);
            RecordEnvelope ordinary = fixture.Ask(run, "Decide whether to accept the run's result?");
            RecordEnvelope foreign = fixture.Warning(run, by: estate.BuilderId);
            RecordEnvelope misleading = fixture.Ask(run, $"Run {run.Id} [loop/warning] needs attention at some point: choose a policy?");
            RecordEnvelope settlement = fixture.End(run, status);
            RecordEnvelope review = fixture.Ask(settlement, $"Run {run.Id} [attempt-missing/warning] needs attention at {settlement.At}: Review its result. Inspect the run?");
            RecordEnvelope legacyReview = fixture.Warning(run, "scope-settlement");
            withdrawals.AddRange(new[] { warning, notified, stall }.Select(a => (a, run, settlement)));
            untouched.AddRange(new[] { (answered, "answered"), (obsoleteAnswer, "open"), (withdrawn, "withdrawn"), (ordinary, "open"), (foreign, "open"), (misleading, "open"), (review, "open"), (legacyReview, "open") });
        }
        foreach (string status in new[] { "not-started", "dispatched", "uncertain" })
        {
            RecordEnvelope run = fixture.Run();
            RecordEnvelope warning = fixture.Warning(run);
            if (status != "not-started") fixture.End(run, status);
            untouched.Add((warning, "open"));
        }
        RecordEnvelope unrelated = estate.Add("operation", estate.OperatorId,
            """{"provider":"fixture","action":"inspect","identity":"ordinary-effect","intent":"Inspect.","target":{},"status":"not-started","evidence":[]}""");
        untouched.Add((fixture.Warning(unrelated), "open"));
        fixture.End(unrelated, "observed-success");
        Dictionary<string, string> before = estate.Load().Records.ToDictionary(r => r.Id, r => r.ComputedHash);
        using (EstateService readOnly = EstateService.OpenReadOnly(estate.Root))
            Assert.Equal(before.Count, readOnly.Session.Load().Records.Count);

        using (EstateService opened = EstateService.Open(estate.Root))
        {
            EstateModel model = estate.Load();
            Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
            Assert.Equal(before.Count + withdrawals.Count, model.Records.Count);
            Assert.All(before, pair => Assert.Equal(pair.Value, model.ById[pair.Key].ComputedHash));
            foreach (var (ask, run, settlement) in withdrawals) AssertWithdrawal(model, ask, run, settlement);
            foreach (var (ask, state) in untouched)
            {
                Assert.Equal(ask.Id, model.Latest(ask).Id);
                Assert.Equal(state, Asks.State(model, ask).State);
            }
            Assert.Empty(BatchJournal.All(opened.Session).Journals);
        }
        Dictionary<string, string> after = estate.Load().Records.ToDictionary(r => r.Id, r => r.ComputedHash);
        using EstateService reopened = EstateService.Open(estate.Root);
        Assert.Equal(after.OrderBy(p => p.Key), estate.Load().Records.ToDictionary(r => r.Id, r => r.ComputedHash).OrderBy(p => p.Key));
    }

    [Fact]
    public void ReconciliationValidatesAndJournalsAllWithdrawalsInOneBatchAndCanRetryARefusal()
    {
        using var fixture = new RunAttentionFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        RecordEnvelope first = fixture.Run();
        RecordEnvelope firstAsk = fixture.Warning(first);
        RecordEnvelope firstSettlement = fixture.End(first, "observed-success");
        RecordEnvelope second = fixture.Run();
        RecordEnvelope secondAsk = fixture.Warning(second);
        RecordEnvelope secondSettlement = fixture.End(second, "known-failure");
        int count = fixture.Estate.Load().Records.Count;
        service.BeforeValidatingRecords = _ => throw new IOException("Synthetic reconciliation refusal.");
        service.ReconcileRunAttention();
        Assert.Equal(count, fixture.Estate.Load().Records.Count);
        Assert.All(service.Runs().Runs, r => Assert.Contains(r.Signals!, s => s.Code == "audit-unverified" && s.Summary.Contains("could not be withdrawn", StringComparison.Ordinal)));
        IReadOnlyList<AuthoredRecord>? batch = null;
        int validations = 0;
        var journals = new HashSet<string>(StringComparer.Ordinal);
        service.BeforeValidatingRecords = records => { validations++; batch = records; };
        service.BeforeStoringTogether = _ =>
        {
            BatchJournal journal = Assert.Single(BatchJournal.All(service.Session).Journals);
            Assert.Equal(batch!.Select(r => r.Envelope.Id), journal.Records.Select(r => r.Id));
            journals.Add(string.Join(',', journal.Records.Select(r => r.Id)));
        };
        service.ReconcileRunAttention();
        Assert.Equal(1, validations);
        Assert.Single(journals);
        Assert.Equal(2, batch!.Count);
        Assert.All(batch, r => Assert.Equal("ask", r.Envelope.Type));
        EstateModel model = fixture.Estate.Load();
        AssertWithdrawal(model, firstAsk, first, firstSettlement);
        AssertWithdrawal(model, secondAsk, second, secondSettlement);
        Assert.Empty(BatchJournal.All(service.Session).Journals);
        Assert.All(service.Runs().Runs, r => Assert.DoesNotContain(r.Signals ?? [], s => s.Summary.Contains("could not be withdrawn", StringComparison.Ordinal)));
        service.ReconcileRunAttention();
        Assert.Equal(1, validations);
        Assert.Equal(count + 2, fixture.Estate.Load().Records.Count);
    }

    internal static void AssertWithdrawal(EstateModel model, RecordEnvelope ask, RecordEnvelope run, RecordEnvelope settlement)
    {
        RecordEnvelope current = model.Latest(ask);
        Assert.Equal(ask.Id, current.Supersedes);
        Assert.Equal(ask.By, current.By);
        Assert.Equal(settlement.Reference, current.Subject);
        JsonObject expected = JsonNode.Parse(ask.Body.GetRawText())!.AsObject();
        expected["state"] = "withdrawn";
        expected["recommendation"] = $"No longer needed: run {run.Id} settled as {EstateModel.BodyString(settlement, "status")} in operation {settlement.Id}. Its worker observations remain available for review.";
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected), current.Body));
    }

    [Fact]
    public void InterruptedWithdrawalBatchKeepsItsIdentitiesAcrossReopening()
    {
        using var fixture = new RunAttentionFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        RecordEnvelope run = fixture.Run();
        fixture.Warning(run);
        fixture.Warning(run, "scope-read");
        fixture.End(run, "known-failure");
        IReadOnlyList<AuthoredRecord>? batch = null;
        int stores = 0;
        service.BeforeValidatingRecords = records => batch = records;
        service.BeforeStoringTogether = _ =>
        {
            if (++stores == 2) throw new IOException("Synthetic interrupted withdrawal batch.");
        };
        service.ReconcileRunAttention();
        Assert.Equal(2, batch!.Count);
        Assert.True(fixture.Estate.Load().ById.ContainsKey(batch[0].Envelope.Id));
        Assert.False(fixture.Estate.Load().ById.ContainsKey(batch[1].Envelope.Id));
        int count = fixture.Estate.Load().Records.Count;
        service.Dispose();
        using (EstateService reopened = EstateService.Open(fixture.Estate.Root))
        {
            Assert.Equal(count, fixture.Estate.Load().Records.Count);
            Assert.Single(BatchJournal.All(reopened.Session).Journals);
            Assert.Contains(reopened.Check().Findings, f => f.Code == "batch.incomplete");
        }
        // Complete the original batch's known missing fixture record; reopening must not have made a competing revision.
        RecordAuthor.Write(fixture.Estate.Root, batch[1]);
        using EstateService recovered = EstateService.Open(fixture.Estate.Root);
        Assert.Equal(count + 1, fixture.Estate.Load().Records.Count);
        Assert.Empty(BatchJournal.All(recovered.Session).Journals);
        Assert.Equal(0, recovered.Validate().Errors);
        Assert.All(fixture.Estate.Load().Current("ask"), ask => Assert.Equal("withdrawn", EstateModel.BodyString(ask, "state")));
    }
}

internal sealed class RunAttentionFixture : IDisposable
{
    internal TestEstate Estate { get; }
    private readonly RecordEnvelope assignment;

    internal RunAttentionFixture(long format = 2)
    {
        Estate = new TestEstate(format);
        RecordEnvelope work = Estate.Add("work-order", Estate.OperatorId, """{"key":"ATTENTION","kind":"ordinary","title":"Inspect attention","intent":"Inspect attention.","scope":["tests"],"exitQuestion":"Only pending asks?","inputs":[],"deliverables":["tests"]}""");
        assignment = Estate.Add("assignment", Estate.OperatorId, $$"""{"contributor":"{{Estate.BuilderId}}","brief":{{RunEstate.Brief}}}""", work.Reference);
    }

    internal RecordEnvelope Run() => Estate.Add("operation", Estate.OperatorId,
        $$"""{"provider":"generic","action":"agent-run","identity":"run-{{Estate.Clock.Ticks}}","intent":"Exercise process attention.","target":{"assignment":"{{assignment.Id}}","principal":"{{Estate.BuilderId}}","workspace":"fixture repository"},"status":"not-started","cap":{"wallMinutes":5},"evidence":[]}""");

    internal RecordEnvelope End(RecordEnvelope intent, string status)
    {
        RecordEnvelope previous = intent;
        JsonObject body = JsonNode.Parse(intent.Body.GetRawText())!.AsObject();
        if (status != "canceled")
        {
            body["status"] = "dispatched";
            body["dispatchedAt"] = intent.At;
            previous = Estate.Add("operation", Estate.OperatorId, body.ToJsonString(), supersedes: intent.Id);
            if (status == "dispatched") return previous;
        }
        body["status"] = status;
        if (status is "observed-success" or "known-failure")
        {
            body["observed"] = new JsonObject { ["summary"] = "The boundary exited.", ["at"] = previous.At };
            if (EstateModel.BodyString(intent, "action") == "agent-run")
                body["usage"] = new JsonObject { ["wallMinutes"] = 1, ["unreported"] = new JsonArray() };
        }
        return Estate.Add("operation", Estate.OperatorId, body.ToJsonString(), supersedes: previous.Id);
    }

    internal RecordEnvelope Warning(RecordEnvelope run, string code = "loop", string severity = "warning", string? by = null) =>
        Ask(run, $"Run {run.Id} [{code}/{severity}] needs attention at {run.At}: The worker observed a concern. Inspect the run?", by);

    internal RecordEnvelope Ask(RecordEnvelope run, string question, string? by = null) => Estate.Add("ask", by ?? Estate.OperatorId,
        JsonSerializer.Serialize(new { to = Estate.OperatorId, question, recommendation = "Inspect the run.", consequence = "The observation remains for review.", state = "open" }), run.Reference);

    internal RecordEnvelope ReviseAsk(RecordEnvelope ask, string state)
    {
        JsonObject body = JsonNode.Parse(ask.Body.GetRawText())!.AsObject();
        body["state"] = state;
        return Estate.Add("ask", ask.By, body.ToJsonString(), ask.Subject, ask.Id);
    }

    internal RecordEnvelope Answer(RecordEnvelope ask) => Estate.Add("decision", Estate.OperatorId,
        $$"""{"title":"Review the observation","statement":"Reviewed.","summary":"The observation was reviewed.","synthesis":"Continue the admitted run.","effects":["Continue."],"affects":[],"decidedBy":"{{Estate.OperatorId}}","decidedAt":"{{ask.At}}","stake":"low","notAuthorized":[],"answers":["{{ask.Id}}"]}""");

    public void Dispose() => Estate.Dispose();
}
