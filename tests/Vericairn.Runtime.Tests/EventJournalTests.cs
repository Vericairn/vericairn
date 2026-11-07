// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Events;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>The journal's bound, its sealed cursors and its epochs, and the derivation's first observation and registrations.</summary>
public sealed class EventJournalTests : IDisposable
{
    private static readonly string[] SixKinds = ["decision needed", "blocker", "uncertain effect", "failed check", "incident", "information"];

    private readonly string directory = Path.Combine(Path.GetTempPath(), "vericairn-events-" + Guid.NewGuid().ToString("N"));

    public EventJournalTests() => Directory.CreateDirectory(directory);

    private string Path_ => Path.Combine(directory, EventJournal.FileName);

    [Fact]
    public void ACursorOpensOnlyForItsPrincipalInItsEpochAndRevealsNoPosition()
    {
        using EventJournal journal = EventJournal.Open(Path_);
        Append(journal, 3);
        string sealedCursor = journal.Seal(2, "reader");
        Assert.True(journal.TryOpen(sealedCursor, "reader", out long position));
        Assert.Equal(2, position);
        Assert.False(journal.TryOpen(sealedCursor, "another", out _));

        // Each sealing draws a fresh nonce: two cursors for one position differ and both open.
        string again = journal.Seal(2, "reader");
        Assert.NotEqual(sealedCursor, again);
        Assert.True(journal.TryOpen(again, "reader", out position));
        Assert.Equal(2, position);

        // Any changed byte, a truncation, garbage or a position ahead of the head answers false, which a reader turns into reset.
        byte[] bytes = Base64Url.DecodeFromChars(sealedCursor);
        for (int i = 0; i < bytes.Length; i++)
        {
            byte[] altered = (byte[])bytes.Clone();
            altered[i] ^= 0x01;
            Assert.False(journal.TryOpen(Base64Url.EncodeToString(altered), "reader", out _), $"byte {i}");
        }

        Assert.False(journal.TryOpen(sealedCursor[..^2], "reader", out _));
        Assert.False(journal.TryOpen("garbage", "reader", out _));
        Assert.False(journal.TryOpen(new string('A', 500), "reader", out _));
        Assert.False(journal.TryOpen(journal.Seal(4, "reader"), "reader", out _));
    }

    [Fact]
    public void TheJournalKeepsItsBoundAndOnlyAPrefixIsDropped()
    {
        using EventJournal journal = EventJournal.Open(Path_, capacity: 3);
        string start = journal.Seal(0, "reader");
        Append(journal, 5);
        Assert.Equal(5, journal.Head);
        Assert.Equal(3, journal.Floor);
        Assert.Equal([3L, 4L, 5L], journal.After(0, 10).Select(e => e.Position));

        // Position 2 still has every later event; position 1 and the start do not.
        Assert.True(journal.TryOpen(journal.Seal(2, "reader"), "reader", out _));
        Assert.False(journal.TryOpen(journal.Seal(1, "reader"), "reader", out _));
        Assert.False(journal.TryOpen(start, "reader", out _));
        Assert.False(journal.Holds(1));
        Assert.True(journal.Holds(5));
    }

    [Fact]
    public void ACleanCloseContinuesTheEpochAndAnUncleanOneStartsAnother()
    {
        string cursor;
        string epoch;
        using (EventJournal journal = EventJournal.Open(Path_))
        {
            Assert.True(journal.Fresh);
            Append(journal, 2, observed: ["01RECORD"]);
            cursor = journal.Seal(2, "reader");
            epoch = journal.Epoch;
        }

        using (EventJournal reopened = EventJournal.Open(Path_))
        {
            Assert.False(reopened.Fresh);
            Assert.Equal(epoch, reopened.Epoch);
            Assert.True(reopened.TryOpen(cursor, "reader", out long position));
            Assert.Equal(2, position);
            Assert.True(reopened.Observed("01RECORD"));
            Assert.NotNull(reopened.Baseline("attention:reader"));
        }

        // A journal left open (a crash) or copied from a running machine is never trusted again.
        using (var connection = new SqliteConnection("Data Source=" + Path_ + ";Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE meta SET value = '0' WHERE key = 'clean';";
            command.ExecuteNonQuery();
        }

        using (EventJournal recovered = EventJournal.Open(Path_))
        {
            Assert.True(recovered.Fresh);
            Assert.NotEqual(epoch, recovered.Epoch);
            Assert.False(recovered.TryOpen(cursor, "reader", out _));
            Assert.False(recovered.Observed("01RECORD"));
        }

        File.WriteAllText(Path_, "not a database");
        foreach (string file in Directory.GetFiles(directory, EventJournal.FileName + "-*")) File.Delete(file);
        using EventJournal replaced = EventJournal.Open(Path_);
        Assert.True(replaced.Fresh);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path_));
    }

    [Fact]
    public async Task AWaitingReaderWakesOnAppendAndOnClose()
    {
        using EventJournal journal = EventJournal.Open(Path_);
        Task waiting = journal.WaitAsync(0, TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        Append(journal, 1);
        await waiting.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        Assert.True(journal.WaitAsync(0, TestContext.Current.CancellationToken).IsCompleted);
        Task closing = journal.WaitAsync(1, TestContext.Current.CancellationToken);
        journal.Dispose();
        await closing.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void TheFirstPassObservesSilentlyAndARegistrationRaisesItsPrincipalsItemsOnce()
    {
        using var estate = new TestEstate(3);
        string source = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Widget","role":"source","location":{"github":"Example/widget"},"visibility":"private"}""").Id;
        estate.Add("verification", estate.BuilderId, """{"method":"test","commit":"0123456789abcdef0123456789abcdef01234567","environment":{"runner":"fixture"},"role":"automation","outcome":"failed","summary":"A check failed.","evidence":[],"limitations":[]}""",
            new Vericairn.Records.Model.RecordReference("resource", source, estate.Load().ById[source].ComputedHash));
        using EstateService service = EstateService.Open(estate.Root);
        service.EnableEvents(directory);
        string start = service.Status().EventsCursor!;

        // Every record present when the epoch began was observed without an event.
        EventsResponse quiet = service.Events(after: start, filter: new EventFilter(Kinds: ["record"]));
        Assert.Empty(quiet.Events);

        // The operator's attention registers on first read: its items are raised once, before the cursor that read returns.
        (EventSubscription? subscription, Problem? problem) = service.OpenEvents(start, new EventFilter(Kinds: ["attention"]), estate.OperatorId);
        Assert.Null(problem);
        EventBatch raised = subscription!.Read();
        Assert.NotEmpty(raised.Events);
        Assert.All(raised.Events, e => Assert.Equal(("raised", estate.OperatorId), (e.Change, e.To)));
        string after = service.AttentionGet(estate.OperatorId).EventsCursor!;
        Assert.Empty(service.Events(after: after, filter: new EventFilter(Kinds: ["attention"])).Events);

        // No envelope names a path or a command: a finding's subject is an opaque digest.
        string json = JsonSerializer.Serialize(raised.Events, ProtocolJson.Options);
        Assert.DoesNotContain("records/", json, StringComparison.Ordinal);
        Assert.DoesNotContain("vericairn ", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ItsFilesAreOwnerOnlyAndItsNamesAreKeyedAndOutliveAnEpoch()
    {
        const string Location = "records/work-order/2026-11/finding.json:record.code";
        string name;
        using (EventJournal journal = EventJournal.Open(Path_))
        {
            Append(journal, 1);
            name = journal.Name("finding", Location);
            Assert.StartsWith("hmac-sha256:", name, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("finding\n" + Location))), name, StringComparison.Ordinal);
            Assert.NotEqual(name, journal.Name("finding", Location + "x"));
            Assert.NotEqual(name, journal.Name("reconciliation", Location));
            if (!OperatingSystem.IsWindows())
            {
                // The -wal and -shm files exist while the journal is open, and SQLite gave them the database's own mode.
                foreach (string file in new[] { Path_, Path_ + "-wal", Path_ + "-shm", Path.Combine(directory, EventJournal.KeyFileName) })
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            }
        }

        // A lost journal starts a new epoch; the names stay the same on this machine.
        foreach (string file in Directory.GetFiles(directory, EventJournal.FileName + "*")) File.Delete(file);
        using EventJournal next = EventJournal.Open(Path_);
        Assert.True(next.Fresh);
        Assert.Equal(name, next.Name("finding", Location));
    }

    [Fact]
    public void AWiderKeyFileIsMadeOwnerOnlyAndAKeyThatCannotBeKeptStillLetsTheJournalOpen()
    {
        string key = Path.Combine(directory, EventJournal.KeyFileName);
        string name;
        using (EventJournal journal = EventJournal.Open(Path_))
        {
            name = journal.Name("finding", "a");
        }

        if (!OperatingSystem.IsWindows())
        {
            // An earlier build or a copy left the key readable by others: the next opening keeps the key and narrows its mode.
            File.SetUnixFileMode(key, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            using (EventJournal reopened = EventJournal.Open(Path_))
            {
                Assert.Equal(name, reopened.Name("finding", "a"));
            }

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(key));
        }

        // A directory where the key file belongs can be neither read nor replaced: the journal still opens, naming subjects under a
        // key of its own for this opening, and events still flow.
        File.Delete(key);
        Directory.CreateDirectory(key);
        using EventJournal keyless = EventJournal.Open(Path_);
        Append(keyless, 1);
        Assert.Equal(1, keyless.Head);
        string here = keyless.Name("finding", "a");
        Assert.StartsWith("hmac-sha256:", here, StringComparison.Ordinal);
        Assert.Equal(here, keyless.Name("finding", "a"));
        Assert.NotEqual(name, here);
        Assert.True(Directory.Exists(key));
    }

    [Fact]
    public void TwoAttentionItemsOnOneSubjectThatChangeInOnePassStayTwoEvents()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        service.EnableEvents(directory);
        string op = estate.OperatorId;
        string cursor = service.AttentionGet(op).EventsCursor!;
        Assert.True(service.NewRecord(new NewRecordRequest("work-order", op, Json("""{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Two notices on one result.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[],"deliverables":["widget"]}"""))).Ok);
        string assignment = Written(service.Assign(new AssignRequest("W-1", estate.BuilderId, op, Json("""{"outcome":"A widget.","sources":[],"permittedChanges":["the widget"],"prohibitedEffects":["no publication"],"workspace":["example repository"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["tests pass"],"recovery":"Record a result."}"""))));
        string attempt = Written(service.Attempt(new AttemptRequest(assignment, estate.BuilderId, "example repository, work branch", "Build it.")));
        string result = Written(service.Result(new ResultRequest(attempt, estate.BuilderId, Json("""{"outcome":"completed","summary":"Built it; one check failed.","outputs":[],"evidence":[],"checks":[{"name":"tests","outcome":"failed","summary":"One test failed."}],"limitations":[],"unresolvedEffects":[],"nextStep":"Fix the test."}"""))));

        // One pass derives both of the operator's notices on the result: the decision it awaits and its failed check.
        AttentionResponse attention = service.AttentionGet(op);
        string[] kinds = [.. attention.Items.Where(i => i.SubjectId == result).Select(i => EstateService.Category(i.Kind)).Order(StringComparer.Ordinal)];
        Assert.Equal(["decision-needed", "failed-check"], kinds);
        EventEnvelope[] raised = [.. service.Events(after: cursor, filter: new EventFilter(Kinds: ["attention"]), principal: op).Events.Where(e => e.Subject.Id == result)];
        Assert.Equal(kinds, raised.Select(e => e.Category!).Order(StringComparer.Ordinal));
        Assert.All(raised, e => Assert.Equal("raised", e.Change));
        Assert.NotEqual(raised[0].Key, raised[1].Key);
        Assert.Single(service.Events(after: cursor, filter: new EventFilter(Kinds: ["attention"], Categories: ["failed-check"]), principal: op).Events, e => e.Subject.Id == result);
    }

    [Fact]
    public void AFaultingPassNeverFailsAStateReadAndAClosedJournalEndsItsReaders()
    {
        using var estate = new TestEstate(3);
        var service = EstateService.Open(estate.Root);
        try
        {
            service.EnableEvents(directory);
            // A fault no narrower catch would name, in the fresh epoch's first pass, the one a state read runs.
            service.EventPassStarting = () => throw new ArithmeticException("Injected fault.");
            StatusResponse status = service.Status();
            Assert.Null(status.EventsCursor);
            Assert.False(string.IsNullOrEmpty(status.Text));
            Assert.Null(service.Progress().EventsCursor);
            Assert.Null(service.AttentionGet(estate.OperatorId).EventsCursor);
            Assert.Null(service.Asks().EventsCursor);
            Assert.Equal("unavailable", service.Events().Problem?.Code);

            service.EventPassStarting = null;
            Assert.NotNull(service.Status().EventsCursor);
            (EventSubscription? subscription, Problem? problem) = service.OpenEvents(null, null, estate.OperatorId);
            Assert.Null(problem);
            service.Dispose();
            EventBatch closed = subscription!.Read();
            Assert.True(closed.Closed);
            Assert.True(subscription.WaitAsync(TestContext.Current.CancellationToken).IsCompleted);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void AttentionKindsMapToTheSixCategories()
    {
        Assert.Equal("decision-needed", EstateService.Category("decision needed"));
        Assert.Equal("blocked", EstateService.Category("progress-overdue"));
        Assert.Equal("uncertain-effect", EstateService.Category("uncertain-effect"));
        Assert.Equal("failed-check", EstateService.Category("unknown-check"));
        Assert.Equal("incident", EstateService.Category("incident"));
        Assert.Equal("information", EstateService.Category("something new"));
        Assert.All(SixKinds, kind => Assert.Contains(EstateService.Category(kind), EventVocabulary.Categories));
        Assert.Equal(EventVocabulary.Categories.Order(StringComparer.Ordinal), SixKinds.Select(EstateService.Category).Order(StringComparer.Ordinal));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static string Written(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem?.Message);
        return outcome.Written!.Id;
    }

    private static void Append(EventJournal journal, int count, IReadOnlyCollection<string>? observed = null)
    {
        var events = Enumerable.Range(0, count).Select(i => new PendingEvent("sha256:" + i.ToString("x64", System.Globalization.CultureInfo.InvariantCulture), "record", "written",
            new EventReference("resource", "01ID" + i), [], null, null, false, [])).ToList();
        journal.Commit("2026-11-07T12:00:00Z", events, observed ?? [], [], ["attention:reader"], []);
    }
}
