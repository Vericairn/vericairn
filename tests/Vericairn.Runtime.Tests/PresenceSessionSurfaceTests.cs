// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// CODE-80 (0.9.4): a presence session on every surface (design/presence-sessions.md §§ 6, 7 and 14). The notices carry the session's
/// terms before its check, its nonce, each signature and its end; a client shows the list from the terms, counts and summarizes it itself,
/// labels the runtime's descriptions as the runtime's, and computes the fingerprint only for terms it read before the nonce; and the runtime
/// keeps an owner-only ledger of each session beside the estate's live store. The helper is the in-process stand-in, so nobody is asked.
/// </summary>
public sealed class PresenceSessionSurfaceTests
{
    private const string SessionId = "01J9Z3K4M5N6P7Q8R9S0T1V2W5";

    private static readonly string[] Steps = ["STEP-1", "STEP-2"];

    // ---- The client's own reading of the terms ----

    [Fact]
    public void TheClientsFingerprintIsTheDesignsAndTheHelpersOnTheSharedVectors()
    {
        foreach (System.Text.Json.JsonElement vector in Vectors.Root.GetProperty("fingerprints").EnumerateArray())
        {
            byte[] terms = Encoding.UTF8.GetBytes(vector.GetProperty("termsText").GetString()!);
            byte[] nonce = System.Buffers.Text.Base64Url.DecodeFromChars(vector.GetProperty("nonce").GetString());
            Assert.Equal(vector.GetProperty("fingerprint").GetString(), PresenceSessionView.Fingerprint(nonce, terms));
            Assert.Equal("E72R-85FF-KDKK", PresenceSessionView.Read(vector.GetProperty("termsText").GetString(), out _)!.FingerprintFor(vector.GetProperty("nonce").GetString()));
        }

        // The client's computation is its own; it agrees with the runtime's port of the helper's on any nonce and terms.
        for (int i = 0; i < 64; i++)
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(16), terms = RandomNumberGenerator.GetBytes(1 + i * 37);
            Assert.Equal(PresenceSessionRules.Fingerprint(nonce, terms), PresenceSessionView.Fingerprint(nonce, terms));
        }
    }

    [Fact]
    public void AListIsShownFromItsTermsWithTheRuntimesDescriptionsLabeledAndTheFingerprintOnlyForTermsReadBeforeTheNonce()
    {
        string terms = TermsText();
        PresenceNotice announced = Announced(terms);
        PresenceNotice check = Check("AAECAwQFBgcICQoLDA0ODw");

        var reader = new PresenceNoticeReader();
        string list = Assert.Single(reader.Read([announced])).Text;
        string asked = Assert.Single(reader.Read([check])).Text;

        Assert.StartsWith("operator record asks for your presence once, for one presence session of 4 signatures in at most 300 s, covering only this list, read here from its terms:", list, StringComparison.Ordinal);
        Assert.Contains("\n  1. acceptance by exact hash 0604e646bd69", list, StringComparison.Ordinal);
        // The runtime's descriptions, each line marked as the runtime's.
        Assert.Contains("\nruntime:   describes entry 1: acceptance STEP-1: accepts result 01J9Z3K4M5N6P7Q8R9S0T1V2W9 at its exact hash", list, StringComparison.Ordinal);
        Assert.Contains("\nruntime:   says entry 1 changes: adds an acceptance about STEP-1 on the basis of 1 review", list, StringComparison.Ordinal);
        Assert.Contains("\nruntime:   flags entry 1: a review there is by the same agent harness that authored the work it reviews", list, StringComparison.Ordinal);
        Assert.DoesNotContain("names the action", list, StringComparison.Ordinal);
        Assert.Contains("\n  2. 3 push records by kind, branch operator/01J9Z3K4M5N6P7Q8R9S0T1V2W8, repository 01J9Z3K4M5N6P7Q8R9S0T1V2W6, product 01J9Z3K4M5N6P7Q8R9S0T1V2W7 at 4a0ad014f4c0, through github-pull-requests, any of its six statuses", list, StringComparison.Ordinal);
        Assert.Contains("\n  In all, counted here from the terms: 4 signatures: 1 acceptance record by exact hash; 3 push records by kind.", list, StringComparison.Ordinal);
        Assert.EndsWith("\nAnything outside this list asks again.", list, StringComparison.Ordinal);
        Assert.StartsWith("Presence check for operator record: the system dialog should show fingerprint E72R-85FF-KDKK, computed here from the list above and the check's nonce.", asked, StringComparison.Ordinal);

        // The same two notices in one read: the terms arrived with the nonce, so no fingerprint is computed for them.
        string together = new PresenceNoticeReader().Read([announced, check])[1].Text;
        Assert.Equal("Presence check: warning, no fingerprint is shown, because this display received the list's terms only with the check's nonce, so the list it shows may not be the one the system dialog covers. Approve the system dialog only if you asked for exactly this action, or decline it.", together);
        Assert.DoesNotContain("E72R", together, StringComparison.Ordinal);
        // A nonce for terms never received, terms the helper would refuse, terms of another session and a nonce that is not 16 bytes: warnings.
        Assert.Contains("because this display has not received the terms of the list this check covers", Assert.Single(new PresenceNoticeReader().Read([check])).Text, StringComparison.Ordinal);
        string unreadable = Shown(Announced(terms.Replace("\"count\":3", "\"count\": 3", StringComparison.Ordinal)), check);
        Assert.StartsWith("Warning: this display cannot read a presence session's terms (", unreadable, StringComparison.Ordinal);
        Assert.Contains("\nruntime: the runtime's own text\n", unreadable, StringComparison.Ordinal);
        Assert.Contains("cannot read the list's terms", unreadable, StringComparison.Ordinal);
        Assert.Contains("cannot read a presence session's terms (the notice names another session than its terms)", Shown(announced with { Session = announced.Session! with { Id = "01J9Z3K4M5N6P7Q8R9S0T1V2WA" } }, check with { Session = check.Session! with { Id = "01J9Z3K4M5N6P7Q8R9S0T1V2WA" } }), StringComparison.Ordinal);
        Assert.Contains("the check's nonce is not 16 bytes", Shown(announced, Check("AAECAwQFBgcICQoLDA0O")), StringComparison.Ordinal);
    }

    [Fact]
    public void EachSignatureIsShownAsItIsMadeAndEveryOtherNoticeByItsText()
    {
        var reader = new PresenceNoticeReader();
        reader.Read([Announced(TermsText())]);
        var session = new PresenceSessionNotice(SessionId, 300, SecondsLeft: 290);
        PresenceNotice signed = new(3, "signed", "operator record", 4, 1, [new("acceptance", "STEP-1", "accepts result at its exact hash")],
            "Signature 1 of 4, under the session's check: acceptance STEP-1: accepts result at its exact hash (0604e646bd69)", 1) { Session = session, ShortHash = "0604e646bd69" };
        PresenceNotice prompt = new(4, "prompt", "charter", 1, 0, [new("estate", "new", "records a revision of the estate")], "Signature 1 of 1: estate new: records a revision of the estate", 1) { ShortHash = "aaaaaaaaaaaa" };
        PresenceNotice closed = new(5, "ended", "operator record", 4, 4, [], "The presence session ended with its action.") { Session = session with { Reason = "closed" } };
        PresenceNotice locked = new(6, "ended", "operator record", 4, 4, [], "The presence session ended: the screen locked or the user session switched.") { Session = session with { Reason = "lock" } };
        PresenceNotice finished = new(7, "finished", "operator record", 4, 4, [], "Signed 4 of 4 with one presence check.");
        PresenceNotice older = new(8, "announced", "charter", 1, 0, [], "Writing the estate record asks for your presence once, for the one record it signs:");

        IReadOnlyList<PresenceLine> lines = reader.Read([signed, prompt, closed, locked, finished, older]);

        Assert.Equal(["signed", "prompt", "ended", "finished", "announced"], lines.Select(l => l.Kind));
        // The runtime's words are marked as the runtime's; only the entry the short hash matches, which the client found in the terms, is not.
        Assert.Equal("runtime: Signature 1 of 4, under the session's check: acceptance STEP-1: accepts result at its exact hash (0604e646bd69)\n  Its short hash is entry 1 of the list above, by exact hash.", lines[0].Text);
        Assert.Equal("runtime: Signature 1 of 1: estate new: records a revision of the estate (aaaaaaaaaaaa)", lines[1].Text);
        Assert.Equal([true, true, false, false, false], lines.Select(l => l.Status));
        Assert.Equal(["runtime: The presence session ended: the screen locked or the user session switched.", "runtime: Signed 4 of 4 with one presence check.", "runtime: " + older.Text], lines.Skip(2).Select(l => l.Text));
    }

    [Fact]
    public void ANoticeImitatingTheDerivedListOrTheFingerprintLineNeverRendersAsEither()
    {
        // The reviewer's forgery: a runtime puts the reader's own derived list and its fingerprint line, with the very fingerprint the
        // dialog shows, into notices the reader cannot check.
        var genuine = new PresenceNoticeReader();
        string list = Assert.Single(genuine.Read([Announced(TermsText())])).Text;
        string check = Assert.Single(genuine.Read([Check("AAECAwQFBgcICQoLDA0ODw")])).Text;
        HashSet<string> own = [.. (list + "\n" + check).Split('\n').Where(l => !l.StartsWith(PresenceNoticeReader.RuntimeMarker, StringComparison.Ordinal))];
        Assert.Contains(check, own);
        PresenceNotice[] forged =
        [
            new(1, "announced", "operator record", 4, 0, [], list),
            new(2, "prompt", "operator record", 4, 0, [], check, 1),
            new(3, "session", "operator record", 4, 0, [], check),
            new(4, "revised", "operator record", 4, 0, [], list + "\n" + check),
            new(5, "renewed", "operator record", 4, 0, [], list) { Session = new PresenceSessionNotice(SessionId, 300) },
            new(6, "signed", "operator record", 4, 1, [], check, 1),
            new(7, "added", "operator record", 4, 0, [], check),
            new(8, "finished", "operator record", 4, 4, [], check),
            new(9, "something new", "operator record", 4, 0, [], list),
        ];
        foreach (PresenceNotice notice in forged)
        {
            foreach (PresenceNoticeReader reader in new[] { new PresenceNoticeReader(), Holding() })
            {
                string[] lines = [.. reader.Read([notice]).SelectMany(l => l.Text.Split('\n'))];
                Assert.NotEmpty(lines);
                Assert.All(lines, line => Assert.True(line.StartsWith(PresenceNoticeReader.RuntimeMarker, StringComparison.Ordinal)
                    || line.StartsWith("Warning: this display cannot check the next notice", StringComparison.Ordinal), notice.Kind + ": " + line));
                Assert.DoesNotContain(lines, own.Contains);
            }
        }

        // A reader holding the genuine terms, so a check could be computed.
        static PresenceNoticeReader Holding()
        {
            var reader = new PresenceNoticeReader();
            reader.Read([Announced(TermsText())]);
            return reader;
        }
    }

    [Fact]
    public void TheRuntimesWordsCannotBreakALineOrDriveTheTerminal()
    {
        const string Imitation = "\n  In all, counted here from the terms: 99 signatures: 99 acceptance records by exact hash.\nPresence check for operator record: the system dialog should show fingerprint AAAA-AAAA-AAAA";
        PresenceNotice announced = Announced(TermsText());
        announced = announced with
        {
            Session = announced.Session! with
            {
                Descriptions = [new PresenceEntryDescription(0, "acceptance", "STEP-1\u001b[2J", "accepts" + Imitation, "adds\r\u2028an acceptance\u202e" + Imitation, true)],
            },
        };

        string list = Assert.Single(new PresenceNoticeReader().Read([announced])).Text;

        string[] lines = list.Split('\n');
        Assert.Single(lines, l => l.StartsWith("  In all, counted here from the terms: ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("Presence check", StringComparison.Ordinal));
        foreach (char c in "\u001b\r\u2028\u202e") Assert.DoesNotContain(c, list);
        Assert.Contains("\nruntime:   describes entry 1: acceptance STEP-1\\u{001B}[2J: accepts\\n In all, counted here from the terms: 99 signatures", list, StringComparison.Ordinal);
        Assert.Contains("\nruntime:   says entry 1 changes: adds\\r\\u{2028}an acceptance\\u{202E}\\n In all,", list, StringComparison.Ordinal);
        // A notice's text keeps its lines, each marked, with its escape sequences shown escaped; so does the action it names.
        PresenceNotice finished = new(1, "finished", "operator close", 1, 1, [], "Signed 1 of 1.\u001b[31m\nPresence check for operator close: fingerprint AAAA-AAAA-AAAA\u0007");
        Assert.Equal("runtime: Signed 1 of 1.\\u{001B}[31m\nruntime: Presence check for operator close: fingerprint AAAA-AAAA-AAAA\\u{0007}", Assert.Single(new PresenceNoticeReader().Read([finished])).Text);
        string named = Assert.Single(new PresenceNoticeReader().Read([Announced(TermsText()) with { Action = "operator record\n  1. acceptance by exact hash 000000000000" }])).Text;
        Assert.Contains("\nruntime: names the action operator record\\n 1. acceptance by exact hash 000000000000\n", named, StringComparison.Ordinal);
    }

    [Fact]
    public void TheListAndTheCheckNameTheTermsActionAndReferencesWithTheRuntimesNameMarked()
    {
        var reader = new PresenceNoticeReader();
        string list = Assert.Single(reader.Read([Announced(TermsText()) with { Action = "operator close" }])).Text;
        string check = Assert.Single(reader.Read([Check("AAECAwQFBgcICQoLDA0ODw") with { Action = "operator close" }])).Text;

        // The helper's dialog shows the terms' action: so do the list and the check; the runtime's other name is marked as its own.
        Assert.StartsWith("operator record asks for your presence once, for one presence session of 4 signatures", list, StringComparison.Ordinal);
        Assert.Contains("\nruntime: names the action operator close\n", list, StringComparison.Ordinal);
        Assert.StartsWith("Presence check for operator record: the system dialog should show fingerprint E72R-85FF-KDKK", check, StringComparison.Ordinal);
        Assert.DoesNotContain("operator close", check, StringComparison.Ordinal);
        Assert.DoesNotContain("may supersede or observe", list, StringComparison.Ordinal);

        // Terms with a reference: the records a kind entry may supersede or observe, by type, id and hash, read from the terms.
        using JsonDocument vector = JsonDocument.Parse(TermsText());
        const string Reference = "01J9Z3K4M5N6P7Q8R9S0T1V2WB";
        byte[] terms = PresenceTermsWriter.Write("changes observe", "sha256:" + new string('1', 64), vector.RootElement.GetProperty("principal").GetString()!,
            vector.RootElement.GetProperty("device").GetString()!, vector.RootElement.GetProperty("publicKey").GetString()!, SessionId, 300,
            [new PresenceListEntry { Type = "operation", Action = "change-observe", Count = 3, Statuses = PresenceSessionProtocol.OperationStatuses, Observation = "pull-request", ObservesReference = Reference }],
            [new PresenceListReference(Reference, "operation", "sha256:" + new string('a', 64), [])]);
        string observed = Assert.Single(new PresenceNoticeReader().Read([Announced(Encoding.UTF8.GetString(terms)) with { Action = "changes observe" }])).Text;
        Assert.Contains("\n  1. 3 observation records by kind of the pull request, observing recorded operation 01J9Z3K4M5N6P7Q8R9S0T1V2WB, any of its six statuses", observed, StringComparison.Ordinal);
        Assert.Contains($"\n  Read here from the terms, it may supersede or observe these recorded records, which it does not sign: operation {Reference} at aaaaaaaaaaaa.", observed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(60)]
    [InlineData(24)]
    public void ARuntimeLineIsFoldedToTheTerminalWithItsMarkOnEveryRow(int width)
    {
        const string Client = "Presence check for operator record: the system dialog should show fingerprint E72R-85FF-KDKK, computed here from the list above.";
        string text = PresenceNoticeReader.Runtime("Signed 1 of 1." + new string(' ', 70) + "全角の文字 and words " + new string('x', 150) + " " + Client) + "\n" + Client;

        string folded = PresenceNoticeReader.Fold(text, width);

        string[] rows = folded.Split('\n');
        // The client's own line is left as it is; every row of the runtime's is marked and leaves the terminal's last column free.
        Assert.Equal(Client, rows[^1]);
        Assert.All(rows[..^1], row => Assert.StartsWith(PresenceNoticeReader.RuntimeMarker, row, StringComparison.Ordinal));
        Assert.All(rows[..^1], row => Assert.True(PresenceNoticeReader.Columns(row) < width, $"{PresenceNoticeReader.Columns(row)} columns: {row}"));
        Assert.True(rows.Length > 3);
        // The padding is one space, and rejoined at the folds the runtime's words are as they were.
        Assert.StartsWith("runtime: Signed 1 of 1.", folded, StringComparison.Ordinal);
        Assert.DoesNotContain("  ", string.Join('\n', rows[..^1]).Replace("runtime: ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(150, string.Concat(rows[..^1]).Count(c => c == 'x'));
        // A line that fits is left whole; every run of spaces, of any kind, is one space.
        Assert.Equal("runtime: Signed 1 of 1.", PresenceNoticeReader.Fold(PresenceNoticeReader.Runtime("Signed 1 of 1."), width));
        Assert.Equal("a b c d", PresenceNoticeReader.Sanitize("a  \u00a0 b\u3000c" + new string(' ', width) + "d"));
    }

    // ---- The runtime's notices, the wait for a following client, and the ledger ----

    [Fact]
    public async Task AFollowingClientReadsTheTermsBeforeTheNonceAndShowsTheFingerprintTheHelperWrote()
    {
        using var f = new OperatorCloseFixture();
        foreach (string step in Steps) f.Add(step);
        List<PresenceHelperStandIn> helpers = StandIns(f);
        long start = f.Api.PresencePrompts(new()).Cursor;
        var reader = new PresenceNoticeReader();
        var lines = new List<PresenceLine>();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task following = Task.Run(async () =>
        {
            long cursor = start;
            while (!lines.Any(l => l.Kind == "finished"))
            {
                PresencePromptsResponse read = await f.Api.PresencePromptsAsync(new(cursor, PresenceNoticeVocabulary.MaxWaitSeconds), stop.Token);
                if (read.Notices.Count == 0) continue;
                cursor = read.Cursor;
                lines.AddRange(reader.Read(read.Notices));
            }
        }, stop.Token);
        // A client follows: its read waits before the action starts.
        await WaitUntil(() => f.Api.PresenceNotices.Waiting > 0);

        OperatorCloseResponse closed = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);
        await following.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);

        Assert.True(closed.Ok, closed.Problem?.Message);
        PresenceHelperStandIn helper = Assert.Single(helpers);
        PresenceNotice check = Assert.Single(f.Api.PresencePrompts(new(start)).Notices, n => n.Kind == "session");
        string fingerprint = PresenceSessionRules.Fingerprint(System.Buffers.Text.Base64Url.DecodeFromChars(check.Session!.Nonce), helper.Terms[0]);
        Assert.Equal(["announced", "session", "signed", "signed", "finished"], lines.Select(l => l.Kind));
        Assert.StartsWith($"Presence check for operator close: the system dialog should show fingerprint {fingerprint}, computed here", lines[1].Text, StringComparison.Ordinal);
        Assert.Contains("\n  In all, counted here from the terms: 2 signatures: 2 acceptance records by exact hash.", lines[0].Text, StringComparison.Ordinal);
        Assert.EndsWith("\n  Its short hash is entry 2 of the list above, by exact hash.", lines[3].Text, StringComparison.Ordinal);
        Assert.Equal("runtime: Signed 2 of 2 with one presence check.", lines[^1].Text);
    }

    [Fact]
    public async Task WithNoClientFollowingNothingWaitsAndAReadOfTheTermsWithTheNonceGetsAWarning()
    {
        var notices = new PresenceNotices();
        PresenceNotice first = notices.Publish("announced", "operator close", 2, 0, [], "listed");
        // Nobody follows: the runtime does not wait, whatever the bound.
        Assert.False(notices.AwaitDelivery(first.Sequence, TimeSpan.FromHours(1)));
        Task<PresencePromptsResponse> waiting = notices.ReadAsync(new(first.Sequence, PresenceNoticeVocabulary.MaxWaitSeconds), TestContext.Current.CancellationToken);
        await WaitUntil(() => notices.Waiting > 0);
        PresenceNotice second = notices.Publish("announced", "operator close", 2, 0, [], "listed again");
        Assert.True(notices.AwaitDelivery(second.Sequence, TestTiming.HangTimeout));
        Assert.Equal(second.Sequence, Assert.Single((await waiting).Notices).Sequence);

        using var f = new OperatorCloseFixture();
        foreach (string step in Steps) f.Add(step);
        StandIns(f);
        long start = f.Api.PresencePrompts(new()).Cursor;
        Assert.True(f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken).Ok);

        IReadOnlyList<PresenceLine> lines = new PresenceNoticeReader().Read(f.Api.PresencePrompts(new(start)).Notices);
        Assert.Contains("warning, no fingerprint is shown", Assert.Single(lines, l => l.Kind == "session").Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRuntimeHoldsAListsTermsBackUntilAFollowingClientHasReadThem()
    {
        using var f = new OperatorCloseFixture();
        foreach (string step in Steps) f.Add(step);
        List<PresenceHelperStandIn> helpers = StandIns(f);
        // A controlled clock: a follower between its reads stays one, and the delivery's bound never passes while the test holds it.
        PresenceNotices notices = f.Api.PresenceNotices;
        notices.Clock = new SteppedClock(DateTimeOffset.UnixEpoch);
        long start = f.Api.PresencePrompts(new()).Cursor;
        var reader = new PresenceNoticeReader();
        var lines = new List<PresenceLine>();
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var gate = new SemaphoreSlim(0);
        Task following = Task.Run(async () =>
        {
            long cursor = start;
            bool first = true;
            while (!lines.Any(l => l.Kind == "finished"))
            {
                PresencePromptsResponse read = await notices.ReadAsync(new(cursor, PresenceNoticeVocabulary.MaxWaitSeconds), TestContext.Current.CancellationToken);
                if (read.Notices.Count == 0) continue;
                cursor = read.Cursor;
                lines.AddRange(reader.Read(read.Notices));
                if (!first) continue;
                // After its first batch the client stands between reads, as one does while it shows a batch, until the test lets it go on.
                first = false;
                holding.SetResult();
                await gate.WaitAsync(TestContext.Current.CancellationToken);
            }
        }, TestContext.Current.CancellationToken);
        await WaitUntil(() => notices.Waiting > 0);
        notices.Publish("added", "an earlier action", 1, 0, [], "an earlier action's notice");
        await holding.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);

        Task<OperatorCloseResponse> closing = Task.Run(() => f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        // The runtime announced the list and holds its terms back from the helper until the client has read them.
        await WaitUntil(() => notices.Awaiting > 0);
        Assert.Empty(helpers.SelectMany(h => h.Terms));
        gate.Release();
        OperatorCloseResponse closed = await closing.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        await following.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);

        Assert.True(closed.Ok, closed.Problem?.Message);
        PresenceNotice nonce = Assert.Single(f.Api.PresencePrompts(new(start)).Notices, n => n.Kind == "session");
        string fingerprint = PresenceSessionRules.Fingerprint(System.Buffers.Text.Base64Url.DecodeFromChars(nonce.Session!.Nonce), Assert.Single(helpers).Terms[0]);
        Assert.StartsWith($"Presence check for operator close: the system dialog should show fingerprint {fingerprint}", Assert.Single(lines, l => l.Kind == "session").Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryFollowingClientMustReadANoticeBeforeItCountsAsDelivered()
    {
        var clock = new SteppedClock(DateTimeOffset.UnixEpoch);
        var notices = new PresenceNotices { Clock = clock };
        PresenceNotice first = notices.Publish("added", "an action", 1, 0, [], "first");
        // Two clients follow; each reads the first notice and stands at it.
        Assert.Single((await notices.ReadAsync(new(0, PresenceNoticeVocabulary.MaxWaitSeconds), TestContext.Current.CancellationToken)).Notices);
        Assert.Single((await notices.ReadAsync(new(0, PresenceNoticeVocabulary.MaxWaitSeconds), TestContext.Current.CancellationToken)).Notices);
        Task<PresencePromptsResponse> one = notices.ReadAsync(new(first.Sequence, PresenceNoticeVocabulary.MaxWaitSeconds), TestContext.Current.CancellationToken);
        await WaitUntil(() => notices.Waiting == 1);

        PresenceNotice terms = notices.Publish("announced", "an action", 2, 0, [], "terms");
        Assert.Equal(terms.Sequence, Assert.Single((await one).Notices).Sequence);

        // One client has read it; the other, between its reads, has not: not delivered.
        Assert.False(notices.IsDelivered(terms.Sequence));
        Assert.Single((await notices.ReadAsync(new(first.Sequence, PresenceNoticeVocabulary.MaxWaitSeconds), TestContext.Current.CancellationToken)).Notices);
        Assert.True(notices.IsDelivered(terms.Sequence));
        // A client that stops reading for the gap no longer follows; with none following, nothing is delivered and nothing waits.
        clock.Advance(PresenceNotices.FollowGap);
        Assert.False(notices.IsDelivered(terms.Sequence));
        Assert.False(notices.AwaitDelivery(terms.Sequence, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void TheLedgerKeepsEachSessionsTermsNonceFingerprintSignaturesAndEndOwnerOnlyBesideTheLiveStore()
    {
        using var f = new OperatorCloseFixture();
        foreach (string step in Steps) f.Add(step);
        List<PresenceHelperStandIn> helpers = StandIns(f);

        OperatorCloseResponse closed = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);

        Assert.True(closed.Ok, closed.Problem?.Message);
        PresenceSessionLedger ledger = f.Api.PresenceLedger!;
        Assert.Equal(Path.GetDirectoryName(f.Api.Session.Live.Path), ledger.Folder);
        Assert.False(Path.GetFullPath(ledger.Folder).StartsWith(Path.GetFullPath(f.Estate.Root), StringComparison.Ordinal));
        string file = Path.Combine(ledger.Folder, PresenceSessionLedger.FileName);
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        PresenceSessionEntry session = Assert.Single(PresenceSessionLedger.Read(ledger.Folder));
        byte[] terms = Assert.Single(helpers).Terms[0];
        Assert.Equal(PresenceSessionRules.ReadTerms(terms).Session, session.Session);
        Assert.Equal(("operator close", "operator close", f.Device, f.Estate.OperatorId), (session.Action, session.Named, session.Device, session.Principal));
        Assert.Equal(Encoding.UTF8.GetString(terms), session.Terms);
        Assert.Equal(PresenceSessionRules.Fingerprint(System.Buffers.Text.Base64Url.DecodeFromChars(session.Nonce), terms), session.Fingerprint);
        Assert.Equal(300, session.LimitSeconds);
        Assert.NotNull(session.OpenedAt);
        Assert.Null(session.Why);
        Assert.Equal(closed.Written.Select(w => (w.Id, w.Hash)), session.Signed.Select(s => (s.Record, s.Hash!)));
        Assert.All(session.Signed, s => Assert.Equal("acceptance", s.Type));
        Assert.Equal([0, 1], session.Signed.Select(s => s.Entry!.Value));
        Assert.Empty(session.Refused);
        Assert.Equal("closed", session.Ended);
        // A diagnostic only: nothing in the estate's records names the ledger.
        Assert.DoesNotContain(Directory.EnumerateFiles(f.Estate.Root, "*", SearchOption.AllDirectories), p => File.ReadAllText(p).Contains(session.Session, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("declined", 0)]
    [InlineData("passed", 2)]
    public void ADeclinedCheckAndARefusedRecordAreLedgeredWithTheirRefusalAndEnd(string check, int refuseAt)
    {
        using var f = new OperatorCloseFixture();
        foreach (string step in Steps) f.Add(step);
        StandIns(f, check, refuseAt);
        long start = f.Api.PresencePrompts(new()).Cursor;

        Assert.False(f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken).Ok);

        PresenceSessionEntry session = Assert.Single(PresenceSessionLedger.Read(f.Api.PresenceLedger!.Folder));
        Assert.NotNull(session.Fingerprint);
        PresenceNotice ended = Assert.Single(f.Api.PresencePrompts(new(start)).Notices, n => n.Kind == "ended");
        if (check == "declined")
        {
            Assert.Null(session.OpenedAt);
            Assert.Empty(session.Signed);
            Assert.Equal("check.declined", Assert.Single(session.Refused).Code);
            Assert.Equal("declined", session.Ended);
            Assert.Equal("declined", ended.Session!.Reason);
            return;
        }

        PresenceSessionSigned signed = Assert.Single(session.Signed);
        PresenceSessionRefused refused = Assert.Single(session.Refused);
        Assert.Equal(("record.outside", "acceptance"), (refused.Code, refused.Type));
        Assert.NotEqual(signed.Hash, refused.Hash);
        Assert.Equal("refusal", session.Ended);
        Assert.Equal((session.Session, "refusal"), (ended.Session!.Id, ended.Session.Reason));
    }

    [Fact]
    public void TheLedgerNarrowsAWiderFileAndNeverWritesThroughALinkOrAFifo()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes, links made without privileges and FIFOs are not this platform's.");
            return;
        }

        string folder = Path.Combine(Path.GetTempPath(), "vericairn-ledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var ledger = new PresenceSessionLedger(folder);
            string file = Path.Combine(folder, PresenceSessionLedger.FileName);
            var opened = new PresenceSessionEvent("2026-11-16T10:00:00Z", SessionId, "announced") { Action = "operator close" };

            // A file left wider is narrowed to its owner on the next line.
            File.WriteAllText(file, string.Empty);
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            ledger.Append(opened);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            Assert.Single(PresenceSessionLedger.Read(folder));

            // A link at its name is never written through, and never read.
            File.Delete(file);
            string elsewhere = Path.Combine(folder, "elsewhere.txt");
            File.WriteAllText(elsewhere, "kept");
            File.CreateSymbolicLink(file, elsewhere);
            ledger.Append(opened);
            Assert.Equal("kept", File.ReadAllText(elsewhere));
            Assert.Throws<IOException>(() => PresenceSessionLedger.Read(folder));

            // Nor a FIFO, which would make a write fail and a read wait: the line is dropped and the read refused at once.
            File.Delete(file);
            if (!MakeFifo(file))
            {
                Assert.Skip("This machine makes no FIFO.");
                return;
            }

            ledger.Append(opened);
            Assert.Throws<IOException>(() => PresenceSessionLedger.Read(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AFifoAtTheLedgersNameStopsNoAction()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("FIFOs are not this platform's.");
            return;
        }

        using var f = new OperatorCloseFixture();
        foreach (string step in Steps) f.Add(step);
        StandIns(f);
        string file = Path.Combine(f.Api.PresenceLedger!.Folder, PresenceSessionLedger.FileName);
        if (!MakeFifo(file))
        {
            Assert.Skip("This machine makes no FIFO.");
            return;
        }

        OperatorCloseResponse closed = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);

        Assert.True(closed.Ok, closed.Problem?.Message);
        Assert.Equal(2, closed.Written.Count);
    }

    private static bool MakeFifo(string path)
    {
        try
        {
            using Process? made = Process.Start(new ProcessStartInfo("mkfifo") { ArgumentList = { path }, RedirectStandardError = true, UseShellExecute = false });
            if (made is null) return false;
            made.WaitForExit();
            return made.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    // The terms of the design's fingerprint vector: one acceptance by exact hash and one push's three records.
    private static string TermsText() => Vectors.Root.GetProperty("fingerprints")[0].GetProperty("termsText").GetString()!;

    private static PresenceNotice Announced(string terms) => new(1, "announced", "operator record", 4, 0, [new("acceptance", "STEP-1", "accepts result at its exact hash")], "the runtime's own text")
    {
        Session = new PresenceSessionNotice(SessionId, 300, terms,
            [new PresenceEntryDescription(0, "acceptance", "STEP-1", "accepts result 01J9Z3K4M5N6P7Q8R9S0T1V2W9 at its exact hash", "adds an acceptance about STEP-1 on the basis of 1 review", true)]),
    };

    private static PresenceNotice Check(string nonce) => new(2, "session", "operator record", 4, 0, [], "Presence check for operator record: the runtime's own words.")
    {
        Session = new PresenceSessionNotice(SessionId, 300, Nonce: nonce),
    };

    // A list and its check, each read on its own, as a client following the notices reads them: the check's line.
    private static string Shown(PresenceNotice announced, PresenceNotice check)
    {
        var reader = new PresenceNoticeReader();
        string list = Assert.Single(reader.Read([announced])).Text;
        return list + "\n" + Assert.Single(reader.Read([check])).Text;
    }

    private static List<PresenceHelperStandIn> StandIns(OperatorCloseFixture f, string check = "passed", int refuseAt = 0)
    {
        var helpers = new List<PresenceHelperStandIn>();
        f.Api.KeyStores = [new RecordingCustody(VericairnHome.Path())];
        f.Api.PresenceRequired = device => device == f.Device;
        f.Api.HostsEmbedded = true;
        PresenceSessionHost host = StandInSessions.Host(helpers, check);
        f.Api.PresenceHost = new PresenceSessionHost
        {
            Build = host.Build, MacOS = host.MacOS, MacOSMajor = host.MacOSMajor, Requirement = host.Requirement, HelperOf = host.HelperOf, Setting = host.Setting,
            SameUserAgentRun = host.SameUserAgentRun, Clock = host.Clock, CheckWait = host.CheckWait,
            Start = signer =>
            {
                var key = new FileKeyStore(VericairnHome.Path());
                var helper = new PresenceHelperStandIn(digest => key.Sign(signer.DeviceId, digest), key.PublicKey(signer.DeviceId)!) { Check = check, RefuseAt = refuseAt };
                helpers.Add(helper);
                return helper;
            },
        };
        return helpers;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var hang = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        hang.CancelAfter(TestTiming.HangTimeout);
        while (!condition()) await Task.Delay(10, hang.Token);
    }
}
