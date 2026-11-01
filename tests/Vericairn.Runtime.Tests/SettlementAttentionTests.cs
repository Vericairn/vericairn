// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Store;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class SettlementAttentionTests
{
    [Theory]
    [InlineData("success", "observed-success")]
    [InlineData("stop", "known-failure")]
    [InlineData("launch-failure", "known-failure")]
    [InlineData("canceled", "canceled")]
    [InlineData("retained", "observed-success")]
    [InlineData("startup", "observed-success")]
    public void EveryTerminalPathWithdrawsOnlyProcessWarningsEvenWithoutRetainedAttentionIds(string path, string status)
    {
        using var run = new RunEstate();
        RecordEnvelope? intent = null;
        var warnings = new List<RecordEnvelope>();
        var preserved = new List<RecordEnvelope>();
        run.Service.Providers.Register(new HookedLauncher(new ReportedLauncher(new GenericLauncher()), command: () =>
        {
            intent = Assert.Single(AgentRuns.All(run.Estate.Load())).Intent;
            RecordEnvelope Ask(string question) => run.Estate.Add("ask", intent.By, JsonSerializer.Serialize(new
            {
                to = run.Estate.OperatorId, question, recommendation = "Inspect it.", consequence = "The observation remains.", state = "open",
            }), intent.Reference);
            warnings.Add(Ask($"Run {intent.Id} [loop/warning] needs attention at {intent.At}: Repeated action. Inspect the run?"));
            RecordEnvelope notified = Ask($"Run {intent.Id} [burn-rate/warning] needs attention at {intent.At}: Review usage. Inspect the run?");
            JsonObject notification = JsonNode.Parse(notified.Body.GetRawText())!.AsObject();
            notification["state"] = "notified";
            warnings.Add(run.Estate.Add("ask", intent.By, notification.ToJsonString(), intent.Reference, notified.Id));
            preserved.Add(Ask("Accept the result of this run?"));
            RecordEnvelope answered = Ask($"Run {intent.Id} [scope-read/warning] needs attention at {intent.At}: Review the read. Inspect the run?");
            preserved.Add(answered);
            run.Estate.Add("decision", run.Estate.OperatorId,
                $$"""{"title":"Reviewed","statement":"Continue.","summary":"The observation was reviewed.","synthesis":"Continue the admitted run.","effects":["Continue."],"affects":[],"decidedBy":"{{run.Estate.OperatorId}}","decidedAt":"{{answered.At}}","stake":"low","notAuthorized":[],"answers":["{{answered.Id}}"]}""");
            if (path == "launch-failure") throw new ProviderRefusedException("Synthetic launch failure.");
            if (path == "canceled") RunEstate.Ok(run.Service.DeviceCreate(new(run.Estate.OperatorId, "Arriving device", "file")));
        }));
        bool retained = path is "retained" or "startup";
        IReadOnlyList<AuthoredRecord>? batch = null;
        run.Service.BeforeValidatingRecords = records =>
        {
            if (!records.Any(r => r.Envelope.Type == "operation" && EstateModel.BodyString(r.Envelope, "status") == status)) return;
            if (retained) throw new IOException("Synthetic settlement failure.");
            batch = records;
        };
        RunResponse started = run.Start("generic", path == "stop" ? "ack-stop" : "generic", run.Report("terminal-attention"));
        Assert.Equal(path is not ("launch-failure" or "canceled"), started.Ok);
        if (path == "stop") Assert.True(run.Service.RunStop(started.Run, new(run.Estate.OperatorId, "End the fixture.", 1)).Ok);
        if (retained)
        {
            var directory = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
            run.WaitUntil(() => directory.Read() is { ExitedAt: not null, Problem: not null, Settled: null }, "the failed settlement");
            // A retained attention reference is not enough to make an ordinary decision ask a process warning.
            RunObservedState observation = directory.Read()!;
            RunSignal unrelated = new("scope-read", "warning", observation.ExitedAt!, "Inspect the decision.", Attention: preserved[0].Id);
            observation.Outcome = observation.Outcome! with { Signals = [.. observation.Outcome.Signals ?? [], unrelated] };
            observation.Signals = [.. observation.Outcome.Signals];
            directory.Write(observation);
            retained = false;
            if (path == "startup") run.Reopen();
            else
            {
                RecordEnvelope other = run.Estate.Add("principal", run.Estate.OperatorId, """{"kind":"human","name":"Another operator","roles":["operator"]}""");
                RunSettleResponse settled = run.Service.RunSettle(started.Run, new(other.Id));
                Assert.True(settled.Ok, settled.Message);
            }
        }
        Assert.Equal(status, run.Settled(started.Run).Status);
        EstateModel model = run.Estate.Load();
        RecordEnvelope settlement = AgentRuns.Find(model, started.Run)!.Latest;
        Assert.All(warnings, ask => RunAttentionReconciliationTests.AssertWithdrawal(model, ask, intent!, settlement));
        Assert.All(preserved, ask => Assert.Equal(ask.Id, model.Latest(ask).Id));
        Assert.Equal("open", Asks.State(model, preserved[0]).State);
        Assert.Equal("answered", Asks.State(model, preserved[1]).State);
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        if (path != "startup")
        {
            Assert.NotNull(batch);
            Assert.All(warnings, ask => Assert.Contains(batch, r => r.Envelope.Supersedes == ask.Id));
        }
        int count = model.Records.Count;
        run.Reopen();
        Assert.Equal(count, run.Estate.Load().Records.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettlementReviewAsksAreNewOpenRecordsInTheSettlementsJournal(bool outsideChanges)
    {
        using var run = new RunEstate();
        IReadOnlyList<AuthoredRecord>? batch = null;
        BatchJournal? journal = null;
        int? asksBeforeSettlement = null;
        run.Service.BeforeValidatingRecords = records =>
        {
            if (records.Any(r => r.Envelope.Type == "operation" && EstateModel.BodyString(r.Envelope, "status") == "observed-success")) batch = records;
        };
        run.Service.BeforeStoringTogether = record =>
        {
            if (record.Envelope.Type != "operation" || EstateModel.BodyString(record.Envelope, "status") != "observed-success") return;
            journal = BatchJournal.All(run.Service.Session).Journals.Single(j => j.Records.Any(r => r.Id == record.Envelope.Id));
            asksBeforeSettlement = run.Estate.Load().OfType("ask").Count();
        };

        var started = run.Start("generic", outsideChanges ? "scope-edits" : "generic", run.Report("settlement-asks"));
        Assert.True(started.Ok, started.Message);
        var settled = run.Settled(started.Run);
        Assert.NotNull(batch);
        Assert.NotNull(journal);
        Assert.Equal(0, asksBeforeSettlement);
        Assert.Equal(batch.Select(r => r.Envelope.Id), journal.Records.Select(r => r.Id));
        var operation = Assert.Single(batch, r => r.Envelope.Type == "operation").Envelope;
        var asks = batch.Where(r => r.Envelope.Type == "ask").Select(r => r.Envelope).ToArray();
        Assert.Equal(outsideChanges ? 2 : 1, asks.Length);
        Assert.Equal(asks.Select(a => a.Id).Order(), settled.Signals!.Select(s => s.Attention).Order());
        Assert.All(asks, ask =>
        {
            Assert.Equal("open", EstateModel.BodyString(ask, "state"));
            Assert.Null(ask.Supersedes);
            Assert.Equal(operation.Reference, ask.Subject);
        });

        run.Reopen();
        EstateModel model = run.Estate.Load();
        Assert.All(asks, ask => Assert.Equal("open", Asks.State(model, model.ById[ask.Id]).State));
        Assert.DoesNotContain(model.Findings, f => f.Code == "record.subject-stale" && f.Location.StartsWith("records/ask/", StringComparison.Ordinal));
        Assert.Empty(BatchJournal.All(run.Service.Session).Journals);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void InterruptedSettlementKeepsTheUnwrittenReviewAskInItsJournal()
    {
        using var run = new RunEstate();
        AuthoredRecord? pending = null;
        run.Service.BeforeStoringTogether = record =>
        {
            if (record.Envelope.Type != "ask") return;
            pending = record;
            throw new IOException("Synthetic interruption before the settlement ask.");
        };
        var started = run.Start("generic", "generic", run.Report("interrupted-settlement"));
        Assert.True(started.Ok, started.Message);
        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        run.WaitUntil(() => directory.Read()?.Problem is not null, "the interrupted settlement observation");
        Assert.NotNull(pending);
        var settled = run.Row(started.Run);
        var signal = Assert.Single(settled.Signals!);
        Assert.Equal(pending.Envelope.Id, signal.Attention);
        Assert.False(run.Estate.Load().ById.ContainsKey(signal.Attention!));
        BatchJournal journal = Assert.Single(BatchJournal.All(run.Service.Session).Journals);
        Assert.Contains(journal.Records, r => r.Id == pending.Envelope.Id);
        Assert.Contains(run.Service.Check().Findings, f => f.Code == "batch.incomplete");

        // Complete the known missing record as local reconciliation would; no published record is removed.
        run.Service.BeforeStoringTogether = null;
        RecordAuthor.Write(run.Estate.Root, pending);
        run.Reopen();
        EstateModel model = run.Estate.Load();
        Assert.Equal("open", Asks.State(model, model.ById[pending.Envelope.Id]).State);
        Assert.Equal(AgentRuns.Find(model, started.Run)!.Latest.Reference, pending.Envelope.Subject);
        Assert.Empty(BatchJournal.All(run.Service.Session).Journals);
        Assert.DoesNotContain(model.Findings, f => f.Code == "record.subject-stale" && f.Location.StartsWith("records/ask/", StringComparison.Ordinal));
        Assert.Equal(0, run.Service.Check().Errors);
    }
}
