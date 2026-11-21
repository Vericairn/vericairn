// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class EstateLoaderTests
{
    private const string WorkOrderBody = """{"key":"M0","kind":"milestone","title":"Estate online","intent":"Prove the format.","scope":["records"],"exitQuestion":"Does a fresh clone validate?","inputs":[],"deliverables":["records"]}""";
    private const string OrdinaryBody = """{"key":"W-1","kind":"ordinary","title":"Small task","intent":"Exercise policy.","scope":["x"],"exitQuestion":"Done?","inputs":[],"deliverables":["y"]}""";

    [Fact]
    public void SeededEstateIsValidAndRendersStatus()
    {
        using var estate = new TestEstate();
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.NotNull(model.CurrentEstate);
        Assert.True(model.HasRole(estate.OperatorId, "operator"));
        Assert.True(model.HasRole(estate.BuilderId, "builder"));
        string status = EstateStatus.Render(model, EstateChecks.Run(model));
        Assert.Contains("Estate: Test Estate (test-estate)", status, StringComparison.Ordinal);
        Assert.Contains("source Vericairn/vericairn@0123456789ab", status, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkOrderLifecycleDerivesStatus()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, WorkOrderBody);
        estate.Add("work-status", estate.BuilderId, """{"status":"active","note":"Started."}""", new RecordReference("work-order", order.Id, null));
        RecordEnvelope review = estate.Add("review", estate.BuilderId, """{"independence":"self","outcome":"pass","summary":"Looked at it.","findings":[]}""", order.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Equal("active", EstateStatus.WorkItems(model).Single().Status);

        estate.Add("acceptance", estate.OperatorId, $$"""{"statement":"Accepted.","basis":["{{review.Id}}"],"self":false}""", order.Reference);
        model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Equal("accepted", EstateStatus.WorkItems(model).Single().Status);
    }

    [Fact]
    public void MilestonesCannotBeAcceptedByTheBuilder()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, WorkOrderBody);
        estate.Add("acceptance", estate.BuilderId, """{"statement":"I accept my own milestone.","basis":[],"self":true}""", order.Reference);
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "acceptance.policy");
    }

    [Fact]
    public void SelfAcceptanceOfOrdinaryWorkNeedsAuthority()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, OrdinaryBody);
        estate.Add("acceptance", estate.BuilderId, """{"statement":"Done.","basis":[],"self":true}""", order.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "acceptance.policy");

        RecordEnvelope decision = estate.Add("decision", estate.BuilderId, $$"""{"title":"Delegate","statement":"Accept ordinary work yourself.","summary":"Delegation.","effects":["builder self-accepts ordinary work"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}""");
        estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{estate.BuilderId}}","scope":["local-engineering","self-accept-ordinary-work"],"reserved":["publication"],"conditions":["label self-review"],"basis":"{{decision.Id}}"}""");
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Equal("accepted", EstateStatus.WorkItems(model).Single().Status);
    }

    [Fact]
    public void LyingAboutIndependenceIsRejected()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, WorkOrderBody);
        estate.Add("review", estate.BuilderId, """{"independence":"independent-agent","outcome":"pass","summary":"Totally independent.","findings":[]}""", order.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "review.independence");
    }

    [Fact]
    public void StaleSubjectsAreUnresolvedNotInvalid()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, WorkOrderBody);
        estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted.","basis":[],"self":false}""", order.Reference);
        estate.Add("work-order", estate.BuilderId, WorkOrderBody.Replace("Estate online", "Estate online, revised", StringComparison.Ordinal), supersedes: order.Id);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Contains(model.Findings, f => f.Code == "record.subject-stale");
        Assert.Contains(model.Findings, f => f.Code == "acceptance.unresolved");
        Assert.Equal("accepted (earlier revision)", EstateStatus.WorkItems(model).Single().Status);
    }

    [Fact]
    public void DuplicateKeysAndTwoSuccessorsAreErrors()
    {
        using var estate = new TestEstate();
        estate.Add("work-order", estate.BuilderId, WorkOrderBody);
        estate.Add("work-order", estate.BuilderId, WorkOrderBody);
        RecordEnvelope ordinary = estate.Add("work-order", estate.BuilderId, OrdinaryBody);
        estate.Add("work-order", estate.BuilderId, OrdinaryBody.Replace("Small task", "Revised once", StringComparison.Ordinal), supersedes: ordinary.Id);
        estate.Add("work-order", estate.BuilderId, OrdinaryBody.Replace("Small task", "Revised twice", StringComparison.Ordinal), supersedes: ordinary.Id);
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "work.duplicate-key");
        Assert.Contains(model.Findings, f => f.Code == "reconcile.successors");
    }

    [Fact]
    public void DecisionsWantASynthesisAndRenderCleanly()
    {
        using var estate = new TestEstate();
        RecordEnvelope bare = estate.Add("decision", estate.BuilderId, $$"""{"title":"Bare","statement":"Do it.","summary":"Do it.","effects":["x"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}""");
        Assert.Contains(estate.Load().Findings, f => f.Code == "decision.synthesis" && f.Severity == Severity.Warning);
        estate.Add("decision", estate.BuilderId, $$"""{"title":"Bare","statement":"Do it.","summary":"Do it.","synthesis":"Context, decision, rationale, consequences, limits.","effects":["x"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}""", supersedes: bare.Id);
        EstateModel model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Code == "decision.synthesis");
        RecordEnvelope? current = DecisionView.Find(model, bare.Id);
        Assert.NotNull(current);
        string markdown = DecisionView.Render(model, current);
        Assert.Contains("## Synthesis", markdown, StringComparison.Ordinal);
        Assert.Contains("> Do it.", markdown, StringComparison.Ordinal);
        Assert.Contains("(revision 2)", markdown, StringComparison.Ordinal);
        Assert.Single(DecisionView.Current(model));
    }

    [Fact]
    public void OnlyTheRootOperatorMayAuthorItself()
    {
        using var estate = new TestEstate();
        string impostor = Records.Identity.Ulid.NewUlid().Value;
        estate.Add("principal", impostor, """{"kind":"human","name":"Impostor","roles":["operator"]}""", id: impostor);
        Assert.Contains(estate.Load().Findings, f => f.Code == "principal.self-authored");
    }

    /// <summary>The path of a record written at a time: what the loader expects for it.</summary>
    private static string PathOf(TestEstate estate, RecordEnvelope record) => Path.Combine(estate.Root, record.ExpectedPath.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void AnIdentifierTwoRecordsCarryIsReadInNeitherPlaceAndNorIsAnyRecordOfItsLine()
    {
        // From 0.9.4 (FIX-156): a second record carrying the identifier of a revision in the middle of a work order's line, dated in an earlier
        // month so that its path sorts first, or in a later one. Neither copy is read, and neither is any record of the line: the first
        // revision does not read as current in its place, and the revision after it does not read as a line of its own.
        foreach (DateTimeOffset month in new[] { new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 15, 12, 0, 0, TimeSpan.Zero) })
        {
            using var estate = new TestEstate(3);
            RecordEnvelope first = estate.Add("work-order", estate.OperatorId, OrdinaryBody);
            RecordEnvelope second = estate.Add("work-order", estate.OperatorId, OrdinaryBody.Replace("Small task", "Small task, revised", StringComparison.Ordinal), supersedes: first.Id);
            RecordEnvelope third = estate.Add("work-order", estate.OperatorId, OrdinaryBody.Replace("Small task", "Small task, revised again", StringComparison.Ordinal), supersedes: second.Id);
            RecordEnvelope unrelated = estate.Add("work-order", estate.OperatorId, OrdinaryBody.Replace("W-1", "W-2", StringComparison.Ordinal));
            Assert.False(estate.Load().Findings.HasErrors, string.Join('\n', estate.Load().Findings));

            DateTimeOffset now = estate.Clock;
            estate.SetClock(month);
            RecordEnvelope copy = estate.Add("work-order", estate.BuilderId, OrdinaryBody.Replace("Small task", "Small task, the copy", StringComparison.Ordinal), supersedes: first.Id, id: second.Id);
            estate.SetClock(now);
            Assert.NotEqual(second.ExpectedPath, copy.ExpectedPath);

            EstateModel model = estate.Load();
            foreach (RecordEnvelope record in new[] { first, second, third })
            {
                Assert.False(model.ById.ContainsKey(record.Id), record.Id);
                Assert.DoesNotContain(model.Records, r => r.Id == record.Id);
            }

            Assert.DoesNotContain(model.Current("work-order"), r => r.Id != unrelated.Id);
            Assert.Same(model.ById[unrelated.Id], Assert.Single(model.Current("work-order")));
            Assert.Equal(new[] { copy.ExpectedPath, second.ExpectedPath }.Order(StringComparer.Ordinal), model.Findings.Where(f => f.Code == "record.duplicate-id").Select(f => f.Location).Order(StringComparer.Ordinal));
            Assert.All(model.Findings.Where(f => f.Code == "record.duplicate-id"), f => Assert.Equal(Severity.Error, f.Severity));
            Assert.Equal([first.ExpectedPath, third.ExpectedPath], model.Findings.Where(f => f.Code == "record.duplicate-line").Select(f => f.Location).Order(StringComparer.Ordinal));
            IReadOnlyList<RecordEnvelope> line = Assert.Single(model.UnreadableLines);
            Assert.Equal(4, line.Count);
            Assert.True(model.IsUnreadable(third.Id));
            Assert.Contains(EstateStatus.ReconcileTasks(model.Findings), f => f.Code == "record.duplicate-id" && f.Location == copy.ExpectedPath);

            // Removing the copy makes the line read again, its latest revision current.
            File.Delete(PathOf(estate, copy));
            model = estate.Load();
            Assert.Empty(model.UnreadableLines);
            Assert.Equal(third.Id, model.Latest(model.ById[first.Id]).Id);
            Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        }
    }

    [Fact]
    public void AnEstateWithNoDuplicateIdentifierReadsEveryRecordAsBefore()
    {
        // With no identifier carried twice, the loader reads every record file, sets none apart and reports nothing new: divergent revisions,
        // a record superseding one that does not exist and a record at the wrong path read exactly as they did before 0.9.4.
        using var estate = new TestEstate(3);
        RecordEnvelope first = estate.Add("work-order", estate.OperatorId, OrdinaryBody);
        estate.Add("work-order", estate.OperatorId, OrdinaryBody.Replace("Small task", "Small task, one way", StringComparison.Ordinal), supersedes: first.Id);
        estate.Add("work-order", estate.OperatorId, OrdinaryBody.Replace("Small task", "Small task, another way", StringComparison.Ordinal), supersedes: first.Id);
        RecordEnvelope orphan = estate.Add("work-order", estate.OperatorId, OrdinaryBody.Replace("W-1", "W-3", StringComparison.Ordinal), supersedes: Records.Identity.Ulid.NewUlid().Value);
        RecordEnvelope moved = estate.Add("work-order", estate.OperatorId, OrdinaryBody.Replace("W-1", "W-4", StringComparison.Ordinal));
        string original = PathOf(estate, moved);
        string elsewhere = Path.Combine(Path.GetDirectoryName(original)!, "..", "2026-09", Path.GetFileName(original));
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        File.Move(original, elsewhere);

        EstateModel model = estate.Load();
        int files = Directory.EnumerateFiles(Path.Combine(estate.Root, EstateLayout.RecordsDirectory), "*.json", SearchOption.AllDirectories).Count();
        Assert.Equal(files, model.Records.Count);
        Assert.Equal(files, model.ById.Count);
        Assert.Empty(model.UnreadableLines);
        Assert.DoesNotContain(model.Findings, f => f.Code is "record.duplicate-id" or "record.duplicate-line");
        Assert.Contains(model.Findings, f => f.Code == "reconcile.successors");
        Assert.Contains(model.Findings, f => f.Code == "record.supersedes" && f.Location == orphan.ExpectedPath);
        Assert.Contains(model.Findings, f => f.Code == "record.path");
        Assert.True(model.IsCurrent(model.ById[orphan.Id]));
    }

    /// <summary>
    /// Leaves a record of a history fixture's estate unread, as FIX-156's review did: a copy of its identifier dated in the month before or after,
    /// a decision carrying it, or two new files sharing a fresh identifier that both supersede it. Returns the files written, for removal.
    /// </summary>
    private static List<RecordEnvelope> LeaveUnread(HistoryFixture f, RecordEnvelope original, string shape)
    {
        DateTimeOffset now = f.Estate.Clock;
        string body = original.Body.GetRawText();
        try
        {
            switch (shape)
            {
                case "earlier":
                case "later":
                    f.Estate.SetClock(shape == "earlier" ? new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero) : new DateTimeOffset(2026, 12, 15, 12, 0, 0, TimeSpan.Zero));
                    return [f.Estate.Add(original.Type, f.Estate.BuilderId, body, original.Subject, original.Supersedes, id: original.Id)];
                case "other-type":
                    return [f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("COPY-1"), id: original.Id)];
                default:
                    string fresh = Records.Identity.Ulid.NewUlid(now).Value;
                    RecordEnvelope first = f.Estate.Add(original.Type, f.Estate.BuilderId, body, original.Subject, original.Id, id: fresh);
                    f.Estate.SetClock(new DateTimeOffset(2026, 12, 15, 12, 0, 0, TimeSpan.Zero));
                    return [first, f.Estate.Add(original.Type, f.Estate.BuilderId, body, original.Subject, original.Id, id: fresh)];
            }
        }
        finally
        {
            f.Estate.SetClock(now.AddSeconds(1));
        }
    }

    private static readonly string[] Shapes = ["earlier", "later", "other-type", "fresh-pair"];

    [Fact]
    public void LeavingTheAdoptionUnreadCountsNoUnanchoredUnsignedRecord()
    {
        // From 0.9.4 (FIX-159): an adoption refuses unsigned records no anchoring checkpoint lists, whatever their date, while signatures are required.
        // Left unread, by any of four shapes, it adopted nothing, so an unsigned work order in the operator's name dated before the required revision
        // counted under the rule before adoption. The reading with the adoption refuses it, so it counts for nothing while the copy stands; removing
        // the copy reads the estate as before.
        foreach (string shape in Shapes)
        {
            using var f = new HistoryFixture();
            DateTimeOffset early = f.Estate.Clock;
            f.Require();
            RecordEnvelope adoption = f.Adopt();
            DateTimeOffset now = f.Estate.Clock;
            f.Estate.SetClock(early);
            RecordEnvelope forged = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FORGED-1"));
            f.Estate.SetClock(now);
            EstateModel before = f.Load();
            Assert.False(before.Counts(before.ById[forged.Id]));

            List<RecordEnvelope> written = LeaveUnread(f, adoption, shape);
            EstateModel model = f.Load();
            Assert.True(model.IsUnreadable(adoption.Id), shape);
            Assert.False(model.Counts(model.ById[forged.Id]), $"{shape}: the unanchored unsigned record counts");
            Assert.DoesNotContain(model.Current("work-order"), r => r.Id == forged.Id);
            Assert.Contains(model.Findings, x => x.Code == "record.duplicate-refused" && x.Message.Contains(forged.Id, StringComparison.Ordinal));

            foreach (RecordEnvelope file in written) File.Delete(PathOf(f.Estate, file));
            EstateModel restored = f.Load();
            Assert.Empty(restored.UnreadableLines);
            Assert.False(restored.Counts(restored.ById[forged.Id]));
            Assert.DoesNotContain(restored.Findings, x => x.Code is "record.duplicate-refused" or "record.duplicate-id");
        }
    }

    [Fact]
    public void LeavingACheckpointUnreadCountsNoRecordItWitnessedWithOtherBytes()
    {
        // From 0.9.4 (FIX-159): a counting checkpoint refuses a record it witnessed when the bytes present differ (history.altered). Left unread, by
        // any of four shapes, it witnessed nothing, so the builder's work order rewritten with other bytes and signed again counted. The reading with
        // the checkpoint refuses it, so it counts for nothing while the copy stands.
        foreach (string shape in Shapes)
        {
            using var f = new HistoryFixture();
            DateTimeOffset at = f.Estate.Clock;
            RecordEnvelope order = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("WIT-1"), signer: f.BuilderSigner);
            RecordEnvelope adoption = f.Adopt();
            DateTimeOffset now = f.Estate.Clock;
            f.Estate.SetClock(at);
            File.Delete(PathOf(f.Estate, order));
            RecordEnvelope altered = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("WIT-1", "Another title"), id: order.Id, signer: f.BuilderSigner);
            f.Estate.SetClock(now);
            Assert.Equal(order.ExpectedPath, altered.ExpectedPath);
            EstateModel before = f.Load();
            Assert.False(before.Counts(before.ById[order.Id]));
            Assert.Contains(before.Findings, x => x.Code == "history.altered");

            List<RecordEnvelope> written = LeaveUnread(f, adoption, shape);
            EstateModel model = f.Load();
            Assert.True(model.IsUnreadable(adoption.Id), shape);
            Assert.False(model.Counts(model.ById[order.Id]), $"{shape}: the record present with other bytes counts");

            foreach (RecordEnvelope file in written) File.Delete(PathOf(f.Estate, file));
            Assert.False(f.Load().Counts(f.Load().ById[order.Id]));
        }
    }

    [Fact]
    public void MoreReadingsOfTheUnreadRecordsThanTheLoaderJudgesCountNothing()
    {
        // From 0.9.4 (FIX-159): three identifiers each carried by two records can be read 27 ways (each copy or none), more than the 16 the loader
        // judges. Which records a reading would refuse is then not established, so no record counts (record.duplicate-unsettled, an error) until the
        // copies are removed, and the estate then reads as before.
        using var estate = new TestEstate(3);
        var orders = new List<RecordEnvelope>();
        for (int i = 1; i <= 4; i++) orders.Add(estate.Add("work-order", estate.OperatorId, OrdinaryBody.Replace("W-1", "W-" + i, StringComparison.Ordinal)));
        DateTimeOffset now = estate.Clock;
        estate.SetClock(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
        List<RecordEnvelope> copies = [.. orders.Take(3).Select(o => estate.Add("work-order", estate.BuilderId, OrdinaryBody.Replace("W-1", "COPY", StringComparison.Ordinal), id: o.Id))];
        estate.SetClock(now);

        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "record.duplicate-unsettled" && f.Severity == Severity.Error);
        Assert.Empty(model.CountingRecords);
        Assert.False(model.Counts(model.ById[orders[3].Id]));
        Assert.Empty(model.Current("work-order"));

        // Two of them can be read nine ways, which the loader judges: the fourth order counts, and nothing is reported as unsettled.
        File.Delete(PathOf(estate, copies[2]));
        model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Code == "record.duplicate-unsettled");
        Assert.True(model.Counts(model.ById[orders[3].Id]));
        Assert.True(model.Counts(model.ById[orders[2].Id]));

        foreach (RecordEnvelope copy in copies.Take(2)) File.Delete(PathOf(estate, copy));
        model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(4, model.Current("work-order").Count());
    }
}
