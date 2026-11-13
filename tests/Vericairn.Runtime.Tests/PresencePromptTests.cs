// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// FIX-124: every presence prompt an operator action asks for says which signature of how many it is and what it signs, announced
/// before the first. The keys are file keys in a recording stand-in for custody, so no test asks anyone for presence; the stand-in
/// keeps the reason each prompt would show.
/// </summary>
public sealed class PresencePromptTests
{
    [Fact]
    public void OperatorCloseAnnouncesEveryAcceptanceBeforeItsFirstPromptAndNamesEachPromptAndItsReason()
    {
        using var f = new OperatorCloseFixture();
        var first = f.Add("STEP-1");
        var second = f.Add("STEP-2");
        RecordingCustody custody = Presence(f);
        long start = f.Api.PresencePrompts(new()).Cursor;
        var seen = new List<string[]>();
        custody.BeforeSigning = _ => seen.Add([.. Since(f.Api, start).Select(n => n.Kind)]);

        OperatorCloseResponse closed = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);

        Assert.True(closed.Ok, closed.Problem?.Message);
        Assert.Equal(2, closed.Written.Count);
        // When custody asked for the first signature, every acceptance had been announced and the prompt named; at the second, its own.
        Assert.Equal(["announced", "prompt"], seen[0]);
        Assert.Equal(["announced", "prompt", "prompt"], seen[1]);
        IReadOnlyList<PresenceNotice> notices = Since(f.Api, start);
        Assert.Equal(["announced", "prompt", "prompt", "finished"], notices.Select(n => n.Kind));
        Assert.All(notices, n => Assert.Equal("operator close", n.Action));
        PresenceNotice announced = notices[0];
        Assert.Equal(2, announced.Count);
        Assert.Equal(["STEP-1", "STEP-2"], announced.Signatures.Select(s => s.Key));
        Assert.Equal($"""
            operator close asks for your presence twice, once for each record it signs:
              1. acceptance STEP-1: accepts result {first.Result.Id} at its exact hash
              2. acceptance STEP-2: accepts result {second.Result.Id} at its exact hash
            """.ReplaceLineEndings("\n"), announced.Text);
        Assert.Equal($"Signature 1 of 2: acceptance STEP-1: accepts result {first.Result.Id} at its exact hash", notices[1].Text);
        Assert.Equal($"Signature 2 of 2: acceptance STEP-2: accepts result {second.Result.Id} at its exact hash", notices[2].Text);
        Assert.Equal([1, 2], notices.Where(n => n.Kind == "prompt").Select(n => n.Index!.Value));
        Assert.Equal("Signed 2 of 2.", notices[3].Text);
        Assert.Equal(2, notices[3].Made);

        // The custody's own prompt carries the same count and the record's type, key and purpose, bounded and with no machine path.
        Assert.Equal(
        [
            $"sign Vericairn signature 1 of 2: acceptance STEP-1, accepts result {first.Result.Id} at its exact hash",
            $"sign Vericairn signature 2 of 2: acceptance STEP-2, accepts result {second.Result.Id} at its exact hash",
        ], custody.Reasons);
        Assert.All(custody.Reasons, r => Assert.True(r!.Length <= PresenceReason.MaxLength && !r.Contains('/', StringComparison.Ordinal), r));
    }

    [Fact]
    public void AnActionThatStopsPartWayKeepsTheCountItAnnouncedTrueForWhatItSigned()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        f.Add("STEP-2");
        f.Add("STEP-3");
        RecordingCustody custody = Presence(f);
        custody.DeclineAt = 2;
        long start = f.Api.PresencePrompts(new()).Cursor;

        OperatorCloseResponse refused = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);

        Assert.False(refused.Ok);
        Assert.Empty(refused.Written);
        Assert.Empty(f.Estate.Load().OfType("acceptance"));
        IReadOnlyList<PresenceNotice> notices = Since(f.Api, start);
        Assert.Equal(["announced", "prompt", "prompt", "finished"], notices.Select(n => n.Kind));
        Assert.Equal(3, notices[0].Count);
        Assert.StartsWith("Signature 1 of 3: acceptance STEP-1:", notices[1].Text, StringComparison.Ordinal);
        Assert.StartsWith("Signature 2 of 3: acceptance STEP-2:", notices[2].Text, StringComparison.Ordinal);
        // The third was never asked for; the second was asked and not made, and one signature was made.
        Assert.Equal("Stopped at signature 2 of 3, which was not made; 1 was signed.", notices[3].Text);
        Assert.Equal(1, notices[3].Made);
        Assert.Equal(2, custody.Reasons.Count);
    }

    [Fact]
    public void ACommandThatSignsNothingWithPresenceRaisesNoNotice()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        var custody = new RecordingCustody(VericairnHome.Path());
        f.Api.KeyStores = [custody];
        long start = f.Api.PresencePrompts(new()).Cursor;

        // The operator's file device states presence none, so its signature asks nobody and says nothing.
        OperatorCloseResponse closed = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);

        Assert.True(closed.Ok, closed.Problem?.Message);
        Assert.Empty(Since(f.Api, start));
        Assert.Equal([null], custody.Reasons);
    }

    [Fact]
    public void ASingleWriteAnnouncesItsOwnPromptBeforeAskingForIt()
    {
        using var f = new OperatorCloseFixture();
        var step = f.Add("STEP-1");
        RecordingCustody custody = Presence(f);
        long start = f.Api.PresencePrompts(new()).Cursor;

        WriteOutcome accepted = f.Api.NewRecord(new NewRecordRequest("acceptance", f.Estate.OperatorId,
            JsonSerializer.SerializeToElement(new { statement = "Accepted.", basis = new[] { step.Review.Id }, self = false }),
            $"result:{step.Result.Id}:{step.Result.ComputedHash}"));

        Assert.True(accepted.Ok, accepted.Problem?.Message);
        IReadOnlyList<PresenceNotice> notices = Since(f.Api, start);
        Assert.Equal(["announced", "prompt", "finished"], notices.Select(n => n.Kind));
        Assert.Equal($"Writing the acceptance record asks for your presence once, for the one record it signs:\n  1. acceptance STEP-1: accepts result {step.Result.Id} at its exact hash", notices[0].Text);
        Assert.Equal($"Signature 1 of 1: acceptance STEP-1: accepts result {step.Result.Id} at its exact hash", notices[1].Text);
        Assert.Equal("Signed 1 of 1.", notices[2].Text);
        Assert.Equal($"sign Vericairn signature 1 of 1: acceptance STEP-1, accepts result {step.Result.Id} at its exact hash", Assert.Single(custody.Reasons));
    }

    [Fact]
    public async Task ASignatureBeyondThePlanIsAnnouncedBeforeItsPromptAndARevisedPathIsAnnouncedBeforeTheNext()
    {
        var counter = new PresenceCounter();
        PresenceSignature a = new("operation", "branch b", "records the intent");
        PresenceSignature b = new("operation", "branch b", "records the outcome");
        PresenceSignature c = new("ask", "M-1", "records a question that waits on its principal");
        using (counter.Action("example action", [a]))
        {
            Assert.Contains("signature 1 of 1", counter.Prompt(a, out PresenceTally? own), StringComparison.Ordinal);
            Assert.Null(own);
            counter.Signed(null);
            counter.Expect([b], "the effect was observed");
            counter.Prompt(b, out _);
            counter.Signed(null);
            // A record the action did not plan is announced as an addition before it is asked for, so the count stays true.
            counter.Prompt(c, out _);
            counter.Signed(null);
            counter.Revise([], "nothing more is needed");
        }

        IReadOnlyList<PresenceNotice> notices = (await counter.Notices.ReadAsync(new(0), TestContext.Current.CancellationToken)).Notices;
        Assert.Equal(["announced", "prompt", "added", "prompt", "added", "prompt", "revised", "finished"], notices.Select(n => n.Kind));
        Assert.Equal("example action needs 1 more signature than it announced, 2 in all, because the effect was observed:\n  2. operation branch b: records the outcome", notices[2].Text);
        Assert.Equal("Signature 2 of 2: operation branch b: records the outcome", notices[3].Text);
        Assert.Equal("example action needs 1 more signature than it announced, 3 in all, because it has one more record to sign than it announced:\n  3. ask M-1: records a question that waits on its principal", notices[4].Text);
        Assert.Equal("Signature 3 of 3: ask M-1: records a question that waits on its principal", notices[5].Text);
        Assert.Equal("example action now needs 3 signatures in all, because nothing more is needed; it asks for no more.", notices[6].Text);
        Assert.Equal("Signed 3 of 3.", notices[7].Text);
    }

    [Fact]
    public async Task AnActionThatNeededFewerThanItAnnouncedSaysSoAndOneThatAskedNothingSaysNothing()
    {
        var counter = new PresenceCounter();
        PresenceSignature a = new("operation", "release 1.0.0", "records the intent");
        PresenceSignature b = new("release", "1.0.0", "records the release candidate for your acceptance");
        using (counter.Action("release observe 1.0.0", [a, b]))
        {
            counter.Prompt(a, out _);
            counter.Signed(null);
        }

        using (counter.Action("an action that signs nothing", [a]))
        {
        }

        IReadOnlyList<PresenceNotice> notices = (await counter.Notices.ReadAsync(new(0), TestContext.Current.CancellationToken)).Notices;
        Assert.Equal(["announced", "prompt", "finished"], notices.Select(n => n.Kind));
        Assert.Equal("Signed 1 of the 2 announced; the action needed no more this time.", notices[2].Text);
    }

    [Fact]
    public async Task AReadWaitsForTheNextNoticeAndAPositionBeyondTheRuntimesReadsEveryNoticeKept()
    {
        var notices = new PresenceNotices();
        PresencePromptsResponse head = await notices.ReadAsync(new(), TestContext.Current.CancellationToken);
        Assert.Empty(head.Notices);
        Task<PresencePromptsResponse> waiting = notices.ReadAsync(new(head.Cursor, PresenceNoticeVocabulary.MaxWaitSeconds), TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        notices.Publish("prompt", "example", 1, 0, [], "Signature 1 of 1: example");
        PresencePromptsResponse woken = await waiting.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        Assert.Equal("Signature 1 of 1: example", Assert.Single(woken.Notices).Text);
        Assert.Equal(woken.Notices[0].Sequence, woken.Cursor);

        // A runtime started again counts from the beginning: a reader holding a later position reads what it keeps.
        Assert.Single((await notices.ReadAsync(new(woken.Cursor + 50), TestContext.Current.CancellationToken)).Notices);
        Assert.Equal("invalid", (await notices.ReadAsync(new(null, PresenceNoticeVocabulary.MaxWaitSeconds + 1), TestContext.Current.CancellationToken)).Problem?.Code);
    }

    [Fact]
    public void AReleaseSubmissionAndItsObservationAnnounceEveryPromptAndCountThemAsTheySign()
    {
        using var f = new HistoryTriggerTests.Fixture();
        RecordEnvelope product = f.Records.Add("product", ReleaseLineFixture.ProductBody());
        (FakeReleaseProvider provider, RecordingCustody custody) = PresenceRelease(f);
        // The fixture stamps its records with its own clock, which can run a second ahead of the wall clock. The runtime reads a
        // controlled clock past every record instead (FIX-133), and the clock turns between the admission and the dispatch, as on a
        // loaded machine: the submission-time basis is complete at both and the same, so the path announced is the path signed.
        HeldClock clock = MinuteAhead();
        Hold(f, provider, clock);
        Assert.All(f.Api.Session.Load().Records, r => Assert.True(r.AtTime < clock.Now, $"{r.Type} {r.Id} is stamped {r.At}, past the runtime's clock."));
        f.Api.ReleaseBoundary = stage => { if (stage == "before-dispatch") clock.Now = clock.Now.AddSeconds(1); };
        List<PresenceNotice> asked = NewestAtEachSignature(custody, f.Api);
        long start = f.Api.PresencePrompts(new()).Cursor;

        ReleaseResponse submitted = f.Api.ReleaseSubmit(new(product.Id, "1.1.0", "Example/widget", f.Records.Estate.OperatorId,
            Provider: provider.Provider, Commit: provider.HeadSha, Expected: product.ComputedHash) { ClientBuild = EstateService.Version });

        Assert.True(submitted.Ok, submitted.Message);
        IReadOnlyList<PresenceNotice> notices = Since(f.Api, start);
        AssertAnnouncedBeforeAsked(notices, asked);
        Assert.Equal(["announced", "prompt", "prompt", "prompt", "prompt", "prompt", "finished"], notices.Select(n => n.Kind));
        Assert.All(notices, n => Assert.Equal("release submit 1.1.0", n.Action));
        Assert.Equal(
        [
            "history-checkpoint trigger release: witnesses the estate's history before the release check, as the release gate requires",
            "operation release 1.1.0: records the release intent before the build is dispatched",
            "operation release 1.1.0: a trial signature, never stored, that proves your device can still sign before the build is dispatched",
            "operation release 1.1.0: a trial signature, never stored, that proves your device can sign this release evidence before it is written",
            "operation release 1.1.0: records the release build's dispatch receipt",
        ], notices[0].Signatures.Select(s => s.Describe()));
        Assert.Equal(Enumerable.Range(1, 5).Select(k => $"Signature {k} of 5: " + notices[0].Signatures[k - 1].Describe()), notices.Where(n => n.Kind == "prompt").Select(n => n.Text));
        Assert.Equal("Signed 5 of 5.", notices[^1].Text);
        Assert.Equal(5, custody.Reasons.Count);
        Assert.All(custody.Reasons.Select((r, i) => (r, i)), x => Assert.StartsWith($"sign Vericairn signature {x.i + 1} of 5: ", x.r, StringComparison.Ordinal));

        // Observing the finished build signs the operation's revision and the candidate, each after its trial signature.
        provider.CompleteRun("Example/widget", submitted.Run!, success: true, "1.1.0");
        long observed = f.Api.PresencePrompts(new()).Cursor;
        ReleaseResponse candidate = f.Api.ReleaseObserve(new("1.1.0", "Example/widget", f.Records.Estate.OperatorId, [], [], "unsigned developer build", Product: product.Id));
        IReadOnlyList<PresenceNotice> observation = Since(f.Api, observed);
        Assert.True(candidate.Candidate is not null, candidate.Message);
        Assert.Equal(["announced", "prompt", "prompt", "prompt", "prompt", "finished"], observation.Select(n => n.Kind));
        Assert.Equal(4, observation[0].Count);
        Assert.Equal("release 1.1.0: records the release candidate for your acceptance", observation[0].Signatures[^1].Describe());
        Assert.Equal("Signed 4 of 4.", observation[^1].Text);
    }

    [Fact]
    public void ASubmissionWhoseBasisChangesBeforeDispatchAnnouncesItsRevisedPathBeforeItsNextPrompt()
    {
        using var f = new HistoryTriggerTests.Fixture();
        HeldClock clock = MinuteAhead();
        // The product is stamped a second past the runtime's controlled clock, so the submission-time basis is incomplete at admission.
        // The clock reaches the product's second before the dispatch: the basis is then complete, so it changed, and the reserved
        // intent is recorded again first (FIX-133). That revision is announced before the next prompt, and every prompt still counts.
        f.Records.Estate.SetClock(clock.Now);
        RecordEnvelope product = f.Records.Add("product", ReleaseLineFixture.ProductBody());
        Assert.Equal(clock.Now.AddSeconds(1), product.AtTime);
        (FakeReleaseProvider provider, RecordingCustody custody) = PresenceRelease(f);
        Hold(f, provider, clock);
        Assert.All(f.Api.Session.Load().Records.Where(r => r.Id != product.Id), r => Assert.True(r.AtTime < clock.Now, $"{r.Type} {r.Id} is stamped {r.At}, past the runtime's clock."));
        f.Api.ReleaseBoundary = stage => { if (stage == "before-dispatch") clock.Now = product.AtTime; };
        List<PresenceNotice> asked = NewestAtEachSignature(custody, f.Api);
        long start = f.Api.PresencePrompts(new()).Cursor;

        ReleaseResponse submitted = f.Api.ReleaseSubmit(new(product.Id, "1.1.0", "Example/widget", f.Records.Estate.OperatorId,
            Provider: provider.Provider, Commit: provider.HeadSha, Expected: product.ComputedHash) { ClientBuild = EstateService.Version });

        Assert.True(submitted.Ok, submitted.Message);
        Assert.Equal(2, submitted.BasisEvidence!.Count);
        IReadOnlyList<PresenceNotice> notices = Since(f.Api, start);
        AssertAnnouncedBeforeAsked(notices, asked);
        Assert.Equal(["announced", "prompt", "prompt", "prompt", "revised", "prompt", "prompt", "prompt", "finished"], notices.Select(n => n.Kind));
        Assert.Equal("""
            release submit 1.1.0 now needs 6 signatures in all, because the admission basis changed before dispatch, so the reserved intent is recorded again first. The rest are:
              4. operation release 1.1.0: records the release build as uncertain, to be observed before any retry
              5. operation release 1.1.0: a trial signature, never stored, that proves your device can sign this release evidence before it is written
              6. operation release 1.1.0: records the release build's dispatch receipt
            """.ReplaceLineEndings("\n"), notices[4].Text);
        Assert.Equal("Signed 6 of 6.", notices[^1].Text);
        Assert.Equal(["1 of 5", "2 of 5", "3 of 5", "4 of 6", "5 of 6", "6 of 6"], custody.Reasons.Select(r => r!["sign Vericairn signature ".Length..r!.IndexOf(':', StringComparison.Ordinal)]));
    }

    // A release source and provider for Example/widget, and the operator signing here only with a device recorded as needing presence,
    // held by the recording stand-in for its custody.
    private static (FakeReleaseProvider Provider, RecordingCustody Custody) PresenceRelease(HistoryTriggerTests.Fixture f)
    {
        var provider = new FakeReleaseProvider();
        f.Api.Providers = new ProviderRegistry();
        f.Api.Providers.Register(provider);
        ReleaseGateSource.Prepare(f.Api, "Example/widget", provider.HeadSha);
        f.PresenceDevice(f.Records.Estate.OperatorId);
        f.ForgetOperatorDevice();
        var custody = new RecordingCustody(VericairnHome.Path());
        f.Api.KeyStores = [custody];
        return (provider, custody);
    }

    // A controlled clock at a whole second a minute past the wall clock, and so past every record the fixture has written, whose own
    // clock runs at most a second ahead of the wall clock.
    private static HeldClock MinuteAhead() => new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds()));

    // The runtime reads its policy time, and the provider stamps its receipts and observations, from the one controlled clock.
    private static void Hold(HistoryTriggerTests.Fixture f, FakeReleaseProvider provider, HeldClock clock)
    {
        f.Api.PolicyClock = clock;
        provider.Now = () => clock.Now;
    }

    // The newest notice the runtime had published as custody was asked for each signature.
    private static List<PresenceNotice> NewestAtEachSignature(RecordingCustody custody, EstateService api)
    {
        var newest = new List<PresenceNotice>();
        custody.BeforeSigning = _ => newest.Add(Since(api, 0)[^1]);
        return newest;
    }

    // Read in order, the notices hold every prompt to what was announced before it: each names the signature announced at its place,
    // by the announcement or by a later addition or revision, and states the count announced by then. Custody was asked for each
    // signature only once its prompt was the newest notice.
    private static void AssertAnnouncedBeforeAsked(IReadOnlyList<PresenceNotice> notices, IReadOnlyList<PresenceNotice> newestAtSignature)
    {
        List<PresenceSignature> plan = [];
        int prompts = 0;
        foreach (PresenceNotice notice in notices)
        {
            switch (notice.Kind)
            {
                case "announced":
                    Assert.Equal(0, prompts);
                    plan = [.. notice.Signatures];
                    break;
                case "added":
                    plan.InsertRange(prompts, notice.Signatures);
                    break;
                case "revised":
                    plan.RemoveRange(prompts, plan.Count - prompts);
                    plan.AddRange(notice.Signatures);
                    break;
                case "prompt":
                    prompts++;
                    Assert.Equal(prompts, notice.Index);
                    Assert.True(prompts <= plan.Count, "Asked before it was announced: " + notice.Text);
                    Assert.Equal(plan[prompts - 1], Assert.Single(notice.Signatures));
                    break;
            }

            Assert.Equal(plan.Count, notice.Count);
        }

        Assert.Equal(notices.Where(n => n.Kind == "prompt").Select(n => n.Sequence), newestAtSignature.Select(n => n.Sequence));
    }

    // The operator's file device stated as needing presence, its key held by a recording stand-in for the custody that would ask.
    private static RecordingCustody Presence(OperatorCloseFixture f)
    {
        var custody = new RecordingCustody(VericairnHome.Path());
        f.Api.KeyStores = [custody];
        f.Api.PresenceRequired = device => device == f.Device;
        return custody;
    }

    private static IReadOnlyList<PresenceNotice> Since(EstateService api, long start) => api.PresencePrompts(new(start)).Notices;
}
