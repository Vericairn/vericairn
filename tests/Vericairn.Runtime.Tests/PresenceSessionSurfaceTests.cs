// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text;
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
        Assert.Contains("\n     the runtime's description: acceptance STEP-1: accepts result 01J9Z3K4M5N6P7Q8R9S0T1V2W9 at its exact hash", list, StringComparison.Ordinal);
        Assert.Contains("\n     what it changes, in the runtime's words: adds an acceptance about STEP-1 on the basis of 1 review", list, StringComparison.Ordinal);
        Assert.Contains("\n     flagged by the runtime: a review here is by the same agent harness that authored the work it reviews", list, StringComparison.Ordinal);
        Assert.Contains("\n  2. 3 push records by kind, branch operator/01J9Z3K4M5N6P7Q8R9S0T1V2W8, repository 01J9Z3K4M5N6P7Q8R9S0T1V2W6, product 01J9Z3K4M5N6P7Q8R9S0T1V2W7 at 4a0ad014f4c0, through github-pull-requests, any of its six statuses", list, StringComparison.Ordinal);
        Assert.Contains("\n  In all, counted here from the terms: 4 signatures: 1 acceptance record by exact hash; 3 push records by kind.", list, StringComparison.Ordinal);
        Assert.EndsWith("\nAnything outside this list asks again.", list, StringComparison.Ordinal);
        Assert.StartsWith("Presence check for operator record: the system dialog should show fingerprint E72R-85FF-KDKK, computed here from the list above and the check's nonce.", asked, StringComparison.Ordinal);

        // The same two notices in one read: the terms arrived with the nonce, so no fingerprint is computed for them.
        string together = new PresenceNoticeReader().Read([announced, check])[1].Text;
        Assert.Contains("warning, no fingerprint is shown, because this display received the list's terms only with the check's nonce or after it", together, StringComparison.Ordinal);
        Assert.DoesNotContain("E72R", together, StringComparison.Ordinal);
        // A nonce for terms never received, terms the helper would refuse, terms of another session and a nonce that is not 16 bytes: warnings.
        Assert.Contains("warning, no fingerprint is shown", Assert.Single(new PresenceNoticeReader().Read([check])).Text, StringComparison.Ordinal);
        Assert.Contains("cannot read the list's terms", Shown(Announced(terms.Replace("\"count\":3", "\"count\": 3", StringComparison.Ordinal)), check), StringComparison.Ordinal);
        Assert.Contains("cannot read the list's terms", Shown(announced with { Session = announced.Session! with { Id = "01J9Z3K4M5N6P7Q8R9S0T1V2WA" } }, check with { Session = check.Session! with { Id = "01J9Z3K4M5N6P7Q8R9S0T1V2WA" } }), StringComparison.Ordinal);
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
        Assert.Equal("Signature 1 of 4, under the session's check: acceptance STEP-1: accepts result at its exact hash (0604e646bd69); entry 1 of its list, by exact hash", lines[0].Text);
        Assert.Equal("Signature 1 of 1: estate new: records a revision of the estate (aaaaaaaaaaaa)", lines[1].Text);
        Assert.Equal([true, true, false, false, false], lines.Select(l => l.Status));
        Assert.Equal(["The presence session ended: the screen locked or the user session switched.", "Signed 4 of 4 with one presence check.", older.Text], lines.Skip(2).Select(l => l.Text));
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
        Assert.EndsWith("; entry 2 of its list, by exact hash", lines[3].Text, StringComparison.Ordinal);
        Assert.Equal("Signed 2 of 2 with one presence check.", lines[^1].Text);
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
