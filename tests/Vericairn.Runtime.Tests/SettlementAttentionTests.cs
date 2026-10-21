// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Store;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class SettlementAttentionTests
{
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
