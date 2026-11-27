// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// ID-16 (0.9.4): presence sessions in the runtime (design/presence-sessions.md §§ 5, 7, 9, 10 and 11). The helper is an in-process
/// stand-in that applies the session's checks and signs with the device's file key, and the platform, build, release requirement and
/// macOS version are a test host's, so these run on every platform; no test asks anyone for presence. The release helper's own side and
/// its process are held by the record library's tests; what only a signed helper on a real Mac shows is the release candidate's
/// qualification.
/// </summary>
public sealed class PresenceSessionTests
{
    private static readonly string[] Unanswered = ["declined", "unanswered"];

    private static readonly string[] OperatorRole = ["operator"];

    // ---- Composed by an action at the shared signing boundary ----

    [Fact]
    public void OperatorCloseListsItsAcceptancesByExactHashAndSignsThemUnderOneCheck()
    {
        using var f = new OperatorCloseFixture();
        var first = f.Add("STEP-1");
        f.Add("STEP-2");
        f.Add("STEP-3");
        Sessions sessions = Sessions.For(f);
        long start = f.Api.PresencePrompts(new()).Cursor;

        OperatorCloseResponse closed = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);

        Assert.True(closed.Ok, closed.Problem?.Message);
        Assert.Equal(3, closed.Written.Count);
        PresenceHelperStandIn helper = Assert.Single(sessions.Helpers);
        Assert.True(helper.Closed);
        Assert.Empty(sessions.Custody.Reasons);
        Assert.Equal(3, helper.SignedHashes.Count);
        PresenceSessionRules terms = PresenceSessionRules.ReadTerms(Assert.Single(helper.Terms));
        Assert.Equal("operator close", terms.Action);
        Assert.Equal(3, terms.ExactCount);
        Assert.All(terms.Entries, e => Assert.Equal("acceptance", e.Type));
        Assert.Equal(300, terms.LimitSeconds);
        // Each stored acceptance is the one listed, signed by the device under the session's check, and verifies.
        EstateModel model = f.Estate.Load();
        foreach (RecordWritten written in closed.Written)
        {
            RecordEnvelope acceptance = model.ById[written.Id];
            Assert.Contains(acceptance.ComputedHash, terms.Entries.Select(e => e.Hash));
            Assert.Equal(f.Device, Assert.Single(acceptance.Signatures).Device);
        }

        IReadOnlyList<PresenceNotice> notices = Since(f, start);
        Assert.Equal(["announced", "session", "signed", "signed", "signed", "ended", "finished"], notices.Select(n => n.Kind));
        Assert.StartsWith("operator close asks for your presence once, for one presence session of 3 signatures in at most 300 s, covering only this list:", notices[0].Text, StringComparison.Ordinal);
        Assert.Contains($"1. acceptance STEP-1: accepts result {first.Result.Id} at its exact hash (exact ", notices[0].Text, StringComparison.Ordinal);
        Assert.Contains("changes: adds an acceptance about STEP-1 on the basis of 1 review", notices[0].Text, StringComparison.Ordinal);
        Assert.Contains("Presence check for operator close: the system dialog shows fingerprint ", notices[1].Text, StringComparison.Ordinal);
        Assert.StartsWith("Signature 1 of 3, under the session's check: acceptance STEP-1:", notices[2].Text, StringComparison.Ordinal);
        Assert.Equal("Signed 3 of 3 with one presence check.", notices[^1].Text);
        // CODE-80: the announcement carries the exact terms the helper was handed and the runtime's descriptions; the check's notice the nonce
        // alone, never the terms again; each signature the session, its seconds left and its short hash; the end its reason.
        PresenceSessionNotice announced = notices[0].Session!;
        Assert.Equal(System.Text.Encoding.UTF8.GetString(helper.Terms[0]), announced.Terms);
        Assert.Equal(terms.Session, announced.Id);
        Assert.Equal(300, announced.LimitSeconds);
        Assert.Equal([0, 1, 2], announced.Descriptions!.Select(d => d.Entry));
        Assert.Equal(("acceptance", "STEP-1", "adds an acceptance about STEP-1 on the basis of 1 review"), (announced.Descriptions![0].Type, announced.Descriptions[0].Key, announced.Descriptions[0].Changes));
        Assert.Null(announced.Nonce);
        PresenceSessionNotice check = notices[1].Session!;
        Assert.Equal((terms.Session, (string?)null), (check.Id, check.Terms));
        Assert.Equal(16, System.Buffers.Text.Base64Url.DecodeFromChars(check.Nonce).Length);
        Assert.Contains(PresenceSessionRules.Fingerprint(System.Buffers.Text.Base64Url.DecodeFromChars(check.Nonce), helper.Terms[0]), notices[1].Text, StringComparison.Ordinal);
        for (int i = 0; i < 3; i++)
        {
            PresenceNotice signed = notices[2 + i];
            Assert.Equal((i + 1, terms.Session), (signed.Index, signed.Session!.Id));
            Assert.Equal(terms.Entries[i].Hash![7..19], signed.ShortHash);
            Assert.NotNull(signed.Session.SecondsLeft);
        }

        Assert.Equal((terms.Session, "closed"), (notices[5].Session!.Id, notices[5].Session!.Reason));
    }

    [Fact]
    public void AClosingOfMoreThanSixteenStepsAnnouncesAListForEachSixteenEachBeforeItsOwnCheck()
    {
        using var f = new OperatorCloseFixture();
        for (int i = 1; i <= 18; i++) f.Add($"STEP-{i:00}");
        Sessions sessions = Sessions.For(f);

        OperatorCloseResponse closed = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);

        Assert.True(closed.Ok, closed.Problem?.Message);
        Assert.Equal(18, closed.Written.Count);
        Assert.Equal(2, sessions.Helpers.Count);
        Assert.Equal(16, PresenceSessionRules.ReadTerms(sessions.Helpers[0].Terms[0]).ExactCount);
        Assert.Equal(2, PresenceSessionRules.ReadTerms(sessions.Helpers[1].Terms[0]).ExactCount);
        Assert.Empty(sessions.Custody.Reasons);
        Assert.Equal("Signed 18 of 18 with 2 presence checks.", Since(f, 0)[^1].Text);
    }

    // An unsettled unattended run requires a check for every acceptance.
    [Trait("Journey", "J135")]
    [Fact]
    public void WhileAnAgentRunThisMachineStartedIsUnsettledEachJudgmentKeepsItsOwnCheck()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        f.Add("STEP-2");
        f.Add("STEP-3");
        Sessions sessions = Sessions.For(f, agentRun: true);

        OperatorCloseResponse closed = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);

        Assert.True(closed.Ok, closed.Problem?.Message);
        Assert.Empty(sessions.Helpers);
        Assert.Equal(3, sessions.Custody.Reasons.Count);
    }

    // Development and unqualified builds keep one prompt per signature.
    [Trait("Journey", "J135")]
    [Theory]
    [InlineData("charter")]
    [InlineData("owner")]
    [InlineData("unreadable setting")]
    [InlineData("development build")]
    [InlineData("macOS not qualified")]
    [InlineData("no macOS listed")]
    [InlineData("helper not signed")]
    [InlineData("another platform")]
    [InlineData("device request")]
    [InlineData("file custody")]
    public void EveryOtherSettingBuildPlatformAndProfileKeepsOneCheckPerSignature(string condition)
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        f.Add("STEP-2");
        if (condition == "charter")
            f.Estate.Add("estate", f.Estate.OperatorId, Charter(f.Estate.OperatorId, 3, "per-signature"),
                supersedes: f.Estate.Estate.Id);
        Sessions sessions = Sessions.For(f, host => condition switch
        {
            "owner" => host.With(setting: new PresenceSetting(true, null)),
            "unreadable setting" => host.With(setting: new PresenceSetting(true, null, "The presence settings cannot be used.")),
            "development build" => host.With(build: PresenceSessionBuild.Parse(null, null, "26")),
            "macOS not qualified" => host.With(build: PresenceSessionBuild.Parse("ABCDE12345", null, "15")),
            "no macOS listed" => host.With(build: PresenceSessionBuild.Parse("ABCDE12345", null, null)),
            "helper not signed" => host.With(requirement: "The helper is not signed with the release's Developer ID, team and identifier."),
            "another platform" => host.With(macOS: false),
            _ => host,
        }, kind: condition == "file custody" ? "file-owner-only" : SecureEnclaveCustody.Kind);
        string publicKey = EstateModelBody(f, f.Device!, "publicKey");
        long start = f.Api.PresencePrompts(new()).Cursor;

        OperatorCloseResponse closed;
        if (condition == "device request")
        {
            using EstateService.RequestDeviceScope request = f.Api.ForDeviceRequest(f.Device!, publicKey);
            closed = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);
        }
        else
        {
            closed = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);
        }

        Assert.True(closed.Ok, closed.Problem?.Message);
        Assert.Empty(sessions.Helpers);
        Assert.Equal(2, sessions.Custody.Reasons.Count);
        Assert.Equal("Signed 2 of 2.", Since(f, start)[^1].Text);
    }

    [Fact]
    public void TheLimitIsTheShorterOfTheOwnersAndThreeHundredSeconds()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        f.Add("STEP-2");
        Sessions sessions = Sessions.For(f, host => host.With(setting: new PresenceSetting(false, 90)));

        Assert.True(f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken).Ok);

        Assert.Equal(90, PresenceSessionRules.ReadTerms(sessions.Helpers[0].Terms[0]).LimitSeconds);
    }

    [Fact]
    public void ADeclinedOrUnansweredCheckSignsNothingAndNeverFallsBackToOneCheckPerSignature()
    {
        foreach (string answer in Unanswered)
        {
            using var f = new OperatorCloseFixture();
            f.Add("STEP-1");
            f.Add("STEP-2");
            Sessions sessions = Sessions.For(f, check: answer);
            long start = f.Api.PresencePrompts(new()).Cursor;

            OperatorCloseResponse refused = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);

            Assert.False(refused.Ok);
            Assert.Empty(refused.Written);
            Assert.Empty(f.Estate.Load().OfType("acceptance"));
            Assert.Empty(sessions.Custody.Reasons);
            Assert.Single(sessions.Helpers);
            Assert.StartsWith("Stopped after 0 signatures, made with one presence check.", Since(f, start)[^1].Text, StringComparison.Ordinal);
        }
    }

    // A helper refusal ends the session and raises attention.
    [Trait("Journey", "J133")]
    [Fact]
    public void AHelperThatRefusesWhatTheRuntimesChecksAdmittedEndsTheSessionAndRaisesAttention()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        f.Add("STEP-2");
        Sessions sessions = Sessions.For(f, refuseAt: 2);

        OperatorCloseResponse refused = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);

        Assert.False(refused.Ok);
        Assert.Empty(f.Estate.Load().OfType("acceptance"));
        Assert.Empty(sessions.Custody.Reasons);
        PresenceOutsideList outside = Assert.Single(f.Api.PresenceOutside);
        Assert.Equal("record.outside", outside.Code);
        Assert.Equal("acceptance", outside.Type);
        AttentionNotice notice = Assert.Single(f.Api.AttentionGet(f.Estate.OperatorId).Items, i => i.SubjectType == "presence.outside-list");
        Assert.Equal(outside.Hash, notice.SubjectId);
        Assert.Contains("refused to sign acceptance", notice.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void AReviewByTheHarnessThatAuthoredTheWorkIsFlaggedInTheAnnouncedList()
    {
        using var f = new OperatorCloseFixture();
        var step = f.Add("STEP-1");
        f.Review(step.Result, self: true);
        f.Add("STEP-2");
        Sessions.For(f);
        long start = f.Api.PresencePrompts(new()).Cursor;

        Assert.True(f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken).Ok);

        string announced = Since(f, start)[0].Text;
        string[] lines = announced.Split('\n');
        int first = Array.FindIndex(lines, l => l.Contains("acceptance STEP-1", StringComparison.Ordinal));
        int second = Array.FindIndex(lines, l => l.Contains("acceptance STEP-2", StringComparison.Ordinal));
        Assert.Contains(lines[first..second], l => l.Contains("flagged: a review here is by the same agent harness that authored the work it reviews", StringComparison.Ordinal));
        Assert.DoesNotContain(lines[second..], l => l.Contains("flagged", StringComparison.Ordinal));
    }

    [Fact]
    public void TheChartersPresencePolicyBelongsToRecordFormatThreeAndOlderFormatsRefuseIt()
    {
        using var three = new TestEstate(3);
        three.Add("estate", three.OperatorId, Charter(three.OperatorId, 3, "per-action"),
            supersedes: three.Estate.Id);
        EstateModel model = three.Load();
        Assert.False(model.Findings.HasErrors, string.Join("; ", model.Findings.Select(x => x.Message)));
        Assert.Equal("per-action", Records.Types.EstateType.PresencePolicy(model));

        using var two = new TestEstate(2);
        two.Add("estate", two.OperatorId, Charter(two.OperatorId, 2, "per-signature"),
            supersedes: two.Estate.Id, format: 2);
        Assert.Contains(two.Load().Findings, x => x.Code == "body.format" && x.Location.EndsWith("presence", StringComparison.Ordinal));

        using var bad = new TestEstate(3);
        bad.Add("estate", bad.OperatorId, Charter(bad.OperatorId, 3, "sometimes"),
            supersedes: bad.Estate.Id);
        Assert.True(bad.Load().Findings.HasErrors);
    }

    // Development and unlisted macOS builds give distinct reasons for disabling sessions.
    [Trait("Journey", "J135")]
    [Fact]
    public void DoctorSaysWhySessionsAreOffForThisMachineAndBuild()
    {
        PresenceSessionHost host = Sessions.Host(new SteppedClock(DateTimeOffset.UtcNow), () => false);
        Assert.Null(host.MachineOff("helper"));
        Assert.Equal("this is a development build, which carries no release identity; only a release build's signed helper opens a session",
            host.With(build: PresenceSessionBuild.Parse(null, null, "26")).MachineOff("helper"));
        PresenceSessionHost unlisted = host.With(build: PresenceSessionBuild.Parse("ABCDE12345", null, "15"));
        Assert.Equal("this release has not qualified macOS 26 for presence sessions", EstateService.PresenceSessionsOff(unlisted));
        Assert.Equal("this release lists no macOS version qualified for presence sessions", host.With(build: PresenceSessionBuild.Parse("ABCDE12345", null, null)).MachineOff("helper"));
        Assert.Equal("the release helper is not beside the command line", host.MachineOff(null));
        Assert.StartsWith("the release helper does not meet the release requirement: ", host.With(requirement: "unsigned").MachineOff("helper"), StringComparison.Ordinal);
        Assert.Equal("presence sessions open only for a Mac's Secure Enclave keys", host.With(macOS: false).MachineOff("helper"));
        // This build, from source, is a development build wherever it runs.
        Assert.NotNull(EstateService.PresenceSessionsOff());
    }

    [Fact]
    public void TheGoalStepsListIsComposedFromTheDraftsItsRouteAuthorsWithTheirIdsAndTimes()
    {
        // The goal step's composition waits for its route: nothing is listed until the route fills the drafts.
        var drafts = new List<Records.Authoring.RecordDraft>();
        PresenceSessionAction session = EstateService.GoalStepSession("01J9Z3K4M5N6P7Q8R9S0T1V2W9", drafts);
        DateTimeOffset at = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var assignment = new Records.Authoring.RecordDraft("assignment", "01J9Z3K4M5N6P7Q8R9S0T1V2W3", JsonSerializer.SerializeToElement(new { }), At: at, Id: "01J9Z3K4M5N6P7Q8R9S0T1V2WA");
        var ask = new Records.Authoring.RecordDraft("ask", "01J9Z3K4M5N6P7Q8R9S0T1V2W3", JsonSerializer.SerializeToElement(new { }), At: at, Id: "01J9Z3K4M5N6P7Q8R9S0T1V2WB");
        PresenceComposeContext Signing(Records.Authoring.RecordDraft draft) => new(draft, new HashSet<string>(StringComparer.Ordinal));
        Assert.Null(session.Compose(Signing(assignment)));
        drafts.AddRange([assignment, ask]);
        PresenceSessionPlan plan = session.Compose(Signing(assignment))!;
        Assert.Equal(("goal step", "goal step:01J9Z3K4M5N6P7Q8R9S0T1V2W9"), (plan.Action, plan.Identity));
        Assert.Equal(["assignment", "ask"], plan.Exact.Select(d => d.Type));
        Assert.Empty(plan.Kinds);
        // A later composition, such as a renewal's, lists the drafts from the one being signed on: those signed before it are not listed again.
        Assert.Equal(["ask"], session.Compose(Signing(ask))!.Exact.Select(d => d.Type));
        // A record the route did not author in advance, such as an agent run's intent, composes no list and keeps its own check.
        var intent = new Records.Authoring.RecordDraft("operation", "01J9Z3K4M5N6P7Q8R9S0T1V2W3", JsonSerializer.SerializeToElement(new { action = "agent-run", status = "not-started" }), At: at, Id: "01J9Z3K4M5N6P7Q8R9S0T1V2WC");
        Assert.Null(session.Compose(Signing(intent)));
        Assert.Null(session.Compose(Signing(intent with { Id = null })));
        // A draft without its id or time is not authored completely, so no list is composed from it.
        drafts.Add(new Records.Authoring.RecordDraft("ask", "01J9Z3K4M5N6P7Q8R9S0T1V2W3", JsonSerializer.SerializeToElement(new { })));
        Assert.Null(session.Compose(Signing(assignment)));
    }

    [Theory]
    [InlineData("none", 0)]
    [InlineData("embedded", 1)]
    [InlineData("local socket", 1)]
    [InlineData("local socket and a paired device", 0)]
    public void SessionsOpenOnlyOnTheEmbeddedAndLocalSocketProfilesEachNamedPositively(string profile, int opened)
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        f.Add("STEP-2");
        Sessions sessions = Sessions.For(f, embedded: profile == "embedded");
        string publicKey = EstateModelBody(f, f.Device!, "publicKey");

        OperatorCloseResponse closed;
        if (profile.StartsWith("local socket", StringComparison.Ordinal))
        {
            using IDisposable local = f.Api.ForLocalSocketRequest();
            using EstateService.RequestDeviceScope? device = profile.EndsWith("device", StringComparison.Ordinal) ? f.Api.ForDeviceRequest(f.Device!, publicKey) : null;
            closed = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);
        }
        else
        {
            // A service whose host names no profile, as a daemon's service reached other than over its local socket, opens none.
            closed = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);
        }

        Assert.True(closed.Ok, closed.Problem?.Message);
        Assert.Equal(opened, sessions.Helpers.Count);
        Assert.Equal(opened == 1 ? 0 : 2, sessions.Custody.Reasons.Count);
    }

    // The charter can require per-signature checks.
    [Trait("Journey", "J135")]
    [Theory]
    [InlineData("per-action", false)]
    [InlineData("per-signature", true)]
    [InlineData("sometimes", true)]
    [InlineData("3", true)]
    public void AChartersPresencePolicyThatIsNotPerActionKeepsOneCheckPerSignature(string policy, bool perSignature)
    {
        using var estate = new TestEstate(3);
        Assert.Null(EstateService.CharterPresenceOff(estate.Load()));
        JsonObject charter = JsonNode.Parse(Charter(estate.OperatorId, 3, "per-action"))!.AsObject();
        charter["policies"]!["presence"] = policy == "3" ? JsonValue.Create(3) : JsonValue.Create(policy);
        estate.Add("estate", estate.OperatorId, charter.ToJsonString(), supersedes: estate.Estate.Id);
        EstateModel model = estate.Load();
        // An invalid value is a validation error, and whether or not the charter validates it never opens a session.
        Assert.Equal(policy is not ("per-action" or "per-signature"), model.Findings.HasErrors);
        Assert.Equal(perSignature, EstateService.CharterPresenceOff(model) is not null);
    }

    [Fact]
    public void AnAgentRunCountsAsUnderWayWhereverItsStateCannotBeListedOrRead()
    {
        string home = Path.Combine(Path.GetTempPath(), "vericairn-runs-" + Guid.NewGuid().ToString("N"));
        string runs = Path.Combine(home, "estates", "01J9Z3K4M5N6P7Q8R9S0T1V2W2", "runs");
        string run = Path.Combine(runs, "01J9Z3K4M5N6P7Q8R9S0T1V2W1");
        try
        {
            Assert.False(Workers.CustodyGuard.UnsettledAgentRuns(home));
            Directory.CreateDirectory(run);
            // A run directory whose worker has not yet written its state ran nothing.
            Assert.False(Workers.CustodyGuard.UnsettledAgentRuns(home));
            File.WriteAllText(Path.Combine(run, "state.json"), $$"""{"run":"01J9Z3K4M5N6P7Q8R9S0T1V2W1","supervisor":{{Environment.ProcessId}},"settled":"completed"}""");
            Assert.False(Workers.CustodyGuard.UnsettledAgentRuns(home));
            // Unsettled while its worker lives.
            File.WriteAllText(Path.Combine(run, "state.json"), $$"""{"run":"01J9Z3K4M5N6P7Q8R9S0T1V2W1","supervisor":{{Environment.ProcessId}}}""");
            Assert.True(Workers.CustodyGuard.UnsettledAgentRuns(home));
            // A state that cannot be parsed counts as under way.
            File.WriteAllText(Path.Combine(run, "state.json"), "{ not json");
            Assert.True(Workers.CustodyGuard.UnsettledAgentRuns(home));
            File.WriteAllText(Path.Combine(run, "state.json"), $$"""{"run":"01J9Z3K4M5N6P7Q8R9S0T1V2W1","supervisor":{{Environment.ProcessId}},"settled":"completed"}""");
            if (!OperatingSystem.IsWindows() && Environment.UserName != "root")
            {
                // A runs or estates directory that cannot be listed counts as a run under way.
                File.SetUnixFileMode(runs, UnixFileMode.None);
                Assert.True(Workers.CustodyGuard.UnsettledAgentRuns(home));
                File.SetUnixFileMode(runs, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.SetUnixFileMode(Path.Combine(home, "estates"), UnixFileMode.None);
                Assert.True(Workers.CustodyGuard.UnsettledAgentRuns(home));
                File.SetUnixFileMode(Path.Combine(home, "estates"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.SetUnixFileMode(run, UnixFileMode.None);
                Assert.True(Workers.CustodyGuard.UnsettledAgentRuns(home));
                File.SetUnixFileMode(run, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Assert.False(Workers.CustodyGuard.UnsettledAgentRuns(home));
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
            {
                foreach (string directory in new[] { Path.Combine(home, "estates"), runs, run }.Where(Directory.Exists))
                    File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void ARenewalsEntryOfItsOwnReissuesOnlyWhatTheActionPlannedWithWhatRemainsOfItsStep()
    {
        using var estate = new TestEstate(3);
        EstateModel model = estate.Load();
        const string product = "01J9Z3K4M5N6P7Q8R9S0T1V2W7", repository = "01J9Z3K4M5N6P7Q8R9S0T1V2W6", hash = "sha256:4a0ad014f4c0131b74fc73f279421297704859445dd7aca1e530eb4642225d10";
        string head = new('a', 40), commit = new('c', 40);
        var push = new PresenceListEntry
        {
            Type = "operation", Action = "change-push", Count = 3, AtMost = true, Statuses = PresenceSessionProtocol.OperationStatuses,
            Fixed = new Dictionary<string, string>(StringComparer.Ordinal) { ["provider"] = "memory-pull-requests", ["repository"] = repository, ["product"] = product, ["productHash"] = hash, ["branch"] = "work/a" },
        };
        var observe = new PresenceListEntry { Type = "operation", Action = "change-observe", Count = 3, Statuses = PresenceSessionProtocol.OperationStatuses, Observation = "push-head", Observes = 0 };
        PresenceListEntry build = EstateService.ReleaseBuildEntry("github-releases-gh", "Example/widget", "1.1.0", product, commit, new Dictionary<string, string>(StringComparer.Ordinal) { ["workflow"] = "release.yml" }, 3)!.Entry;
        List<PresenceListEntry> planned = [push, observe, build];
        HashSet<string> actionRecords = new(["01J9Z3K4M5N6P7Q8R9S0T1V2WB"], StringComparer.Ordinal);
        var draft = new Records.Authoring.RecordDraft("operation", estate.OperatorId, JsonSerializer.SerializeToElement(new { }));

        PresenceRecord Change(string branch, string status, string? recordHead = null) => Record(estate.OperatorId, "change-push", status, new JsonObject
        {
            ["product"] = product, ["productHash"] = hash, ["repository"] = repository, ["head"] = recordHead ?? head, ["branch"] = branch,
        });

        // A push of the planned branch is re-issued at its head, with what remains of its step.
        (PresenceListEntry Entry, string Changes, IReadOnlyList<string> References) dispatched = EstateService.OwnEntry(Change("work/a", "dispatched"), draft, model, false, Now, planned, actionRecords)!.Value;
        Assert.Equal(2, dispatched.Entry.Count);
        Assert.False(dispatched.Entry.AtMost);
        Assert.Equal(head, dispatched.Entry.Fixed["head"]);
        Assert.Equal("work/a", dispatched.Entry.Fixed["branch"]);
        Assert.Equal(3, EstateService.OwnEntry(Change("work/a", "not-started"), draft, model, false, Now, planned, actionRecords)!.Value.Entry.Count);
        Assert.Equal(1, EstateService.OwnEntry(Change("work/a", "observed-success"), draft, model, false, Now, planned, actionRecords)!.Value.Entry.Count);
        // Another branch, or a head the plan fixes otherwise, is outside the action: no entry, so it keeps its own check.
        Assert.Null(EstateService.OwnEntry(Change("work/b", "dispatched"), draft, model, false, Now, planned, actionRecords));
        List<PresenceListEntry> pinned = [push with { Fixed = new Dictionary<string, string>(push.Fixed, StringComparer.Ordinal) { ["head"] = new string('b', 40) } }];
        Assert.Null(EstateService.OwnEntry(Change("work/a", "dispatched"), draft, model, false, Now, pinned, actionRecords));

        // An observation only of one of the action's own operations, and only of an observation the action planned.
        PresenceRecord Observation(string operation, string observation) => Record(estate.OperatorId, "change-observe", "not-started", new JsonObject
        {
            ["product"] = product, ["productHash"] = hash, ["repository"] = repository, ["head"] = head, ["operation"] = operation, ["observation"] = observation,
        });
        PresenceListEntry observed = EstateService.OwnEntry(Observation("01J9Z3K4M5N6P7Q8R9S0T1V2WB", "push-head"), draft, model, false, Now, planned, actionRecords)!.Value.Entry;
        Assert.Equal(("01J9Z3K4M5N6P7Q8R9S0T1V2WB", 3), (observed.ObservesReference, observed.Count));
        Assert.Null(EstateService.OwnEntry(Observation("01J9Z3K4M5N6P7Q8R9S0T1V2WC", "push-head"), draft, model, false, Now, planned, actionRecords));
        Assert.Null(EstateService.OwnEntry(Observation("01J9Z3K4M5N6P7Q8R9S0T1V2WB", "target-run"), draft, model, false, Now, planned, actionRecords));

        // A release build is re-issued only as the one the action planned, through ReleaseBuildEntry: a record naming another ref, or
        // another workflow than the planned one, or none, gets none (decision 01M7J7J6MRZPDMYDRGK0MDEZR7).
        PresenceRecord Build(string reference, string? workflow = "release.yml")
        {
            var target = new JsonObject { ["repository"] = "Example/widget", ["version"] = "1.1.0", ["product"] = product, ["ref"] = reference };
            if (workflow is not null) target["workflow"] = workflow;
            return Record(estate.OperatorId, "dispatch-release-workflow", "dispatched", target, "github-releases-gh");
        }

        PresenceListEntry rebuilt = EstateService.OwnEntry(Build(commit), draft, model, false, Now, planned, actionRecords)!.Value.Entry;
        Assert.Equal(build.Fixed, rebuilt.Fixed);
        Assert.Equal("release.yml", rebuilt.Fixed["workflow"]);
        Assert.Equal(2, rebuilt.Count);
        Assert.Null(EstateService.OwnEntry(Build("main"), draft, model, false, Now, planned, actionRecords));
        Assert.Null(EstateService.OwnEntry(Build(commit), draft, model, false, Now, [push], actionRecords));
        Assert.Null(EstateService.OwnEntry(Build(commit, "other.yml"), draft, model, false, Now, planned, actionRecords));
        Assert.Null(EstateService.OwnEntry(Build(commit, null), draft, model, false, Now, planned, actionRecords));
    }

    private static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static PresenceRecord Record(string by, string action, string status, JsonObject target, string provider = "memory-pull-requests")
    {
        var record = new JsonObject
        {
            ["format"] = "vericairn.record/3", ["type"] = "operation", ["id"] = Records.Identity.Ulid.NewUlid().Value,
            ["at"] = RecordParser.FormatTimestamp(DateTimeOffset.FromUnixTimeSeconds(Now)), ["by"] = by,
            ["body"] = new JsonObject { ["provider"] = provider, ["action"] = action, ["identity"] = "x", ["status"] = status, ["target"] = target },
            ["subject"] = new JsonObject { ["type"] = "product", ["id"] = target["product"]!.GetValue<string>() },
        };
        using JsonDocument document = JsonDocument.Parse(record.ToJsonString());
        return PresenceRecord.Read(CanonicalJson.SerializeUtf8(document.RootElement), "record.form");
    }

    [Fact]
    public void AReviewByTheSameHarnessUnderEitherSpellingRoutingAcceptsIsFlagged()
    {
        using var f = new OperatorCloseFixture();
        var step = f.Add("STEP-1");
        // The work's author names its harness "Example Agent"; this reviewer names the same harness by its launcher id's form.
        RecordEnvelope reviewer = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"agent","name":"Second Reviewer","roles":["reviewer"],"agent":{"vendor":"Example","product":"example-agent"}}""");
        f.Estate.Add("review", reviewer.Id, JsonSerializer.Serialize(new { independence = "independent-agent", outcome = "pass", summary = "Inspected the exact result.", findings = Array.Empty<string>() }), step.Result.Reference);
        f.Add("STEP-2");
        Sessions.For(f);
        long start = f.Api.PresencePrompts(new()).Cursor;

        Assert.True(f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken).Ok);

        string[] lines = Since(f, start)[0].Text.Split('\n');
        int first = Array.FindIndex(lines, l => l.Contains("acceptance STEP-1", StringComparison.Ordinal));
        int second = Array.FindIndex(lines, l => l.Contains("acceptance STEP-2", StringComparison.Ordinal));
        Assert.Contains(lines[first..second], l => l.StartsWith("     flagged:", StringComparison.Ordinal));
        Assert.DoesNotContain(lines[second..], l => l.Contains("flagged", StringComparison.Ordinal));

        // The harness a product names reads as the launcher's id, whichever of its two spellings it uses, or as the id's form of the name.
        IReadOnlyList<Workers.IAgentLauncher> launchers = [new NamedLauncher()];
        Assert.Equal("example-launcher", EstateService.HarnessIdentity("Example Harness", launchers));
        Assert.Equal("example-launcher", EstateService.HarnessIdentity("EXAMPLE-LAUNCHER", launchers));
        Assert.Equal("example-agent", EstateService.HarnessIdentity(" Example  Agent ", []));
        Assert.Equal(EstateService.HarnessIdentity("Example Agent", []), EstateService.HarnessIdentity("example_agent", []));
        Assert.NotEqual(EstateService.HarnessIdentity("Example Agent", []), EstateService.HarnessIdentity("Other Agent", []));
        Assert.Null(EstateService.HarnessIdentity("  ", []));
    }

    private sealed class NamedLauncher : Workers.IAgentLauncher
    {
        public string Launcher => "example-launcher";

        public string? Harness => "Example Harness";

        public Runtime.Providers.ProviderDescriptor Descriptor { get; } = new("example-launcher", Workers.IAgentLauncher.Contract, "candidate", [], []);

        public IReadOnlyList<string> Command(Workers.LaunchContext context) => ["example"];
    }

    // ---- The session itself: renewal, the margin rule, one per device ----

    [Fact]
    public void ARecordAfterTheSessionsLapseRenewsWithANewListAndItsReason()
    {
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(Vectors.Now));
        using var bench = new Bench(clock);
        using (bench.Counter.Action("release submit", null, bench.Action))
        {
            Assert.NotNull(bench.Sign("checkpoint"));
            clock.Advance(TimeSpan.FromSeconds(300));
            Assert.NotNull(bench.Sign("dispatchIntent"));
        }

        Assert.Equal(2, bench.Helpers.Count);
        Assert.All(bench.Helpers, h => Assert.True(h.Closed));
        PresenceNotice renewal = Assert.Single(bench.Notices(), n => n.Kind == "renewed");
        Assert.StartsWith("release submit renews its presence session because the session reached its limit, and asks once more", renewal.Text, StringComparison.Ordinal);
        Assert.Equal("Signed 2 of 2 with 2 presence checks.", bench.Notices()[^1].Text);
    }

    [Theory]
    [InlineData(300, 2)]
    [InlineData(120, 1)]
    public void BeforeAnEffectTheSessionMustOutlastItsBoundAndAMinuteOrBeRenewedWhenAFreshSessionWould(int limit, int checks)
    {
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(Vectors.Now));
        using var bench = new Bench(clock, limit);
        using (bench.Counter.Action("release submit", null, bench.Action))
        {
            Assert.NotNull(bench.Sign("checkpoint"));
            // 100 seconds left: fewer than the two-minute bound and a minute. A 300-second session renews first; a 120-second one cannot
            // cover the effect either way, so the effect goes under this session and its receipt under a renewal.
            clock.Advance(TimeSpan.FromSeconds(limit - 100));
            bench.Service.EffectAhead(PresenceSessionService.EffectDeadline);
            Assert.NotNull(bench.Sign("dispatchIntent"));
        }

        Assert.Equal(checks, bench.Helpers.Count);
        Assert.Equal(1, bench.Helpers[0].Statuses);
        if (checks == 2) Assert.Contains("so the session is renewed before the effect is dispatched", Assert.Single(bench.Notices(), n => n.Kind == "renewed").Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lock", "the session ended when the screen locked or the user session switched")]
    [InlineData("sleep", "the session ended when the Mac slept")]
    public void AScreenLockOrSleepBetweenSignaturesRenewsWithItsReason(string end, string reason)
    {
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(Vectors.Now));
        using var bench = new Bench(clock);
        using (bench.Counter.Action("release submit", null, bench.Action))
        {
            Assert.NotNull(bench.Sign("checkpoint"));
            // The helper ends its session by itself and exits; the runtime's next message finds its input closed.
            bench.Helpers[0].EndSession(end);
            Assert.NotNull(bench.Sign("dispatchIntent"));
        }

        Assert.Equal(2, bench.Helpers.Count);
        Assert.False(bench.Action.Failed);
        Assert.Contains("renews its presence session because " + reason, Assert.Single(bench.Notices(), n => n.Kind == "renewed").Text, StringComparison.Ordinal);
        Assert.Equal("Signed 2 of 2 with 2 presence checks.", bench.Notices()[^1].Text);
    }

    [Fact]
    public void ASleepBeforeAnEffectRenewsBeforeTheEffectIsDispatched()
    {
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(Vectors.Now));
        using var bench = new Bench(clock);
        using (bench.Counter.Action("release submit", null, bench.Action))
        {
            Assert.NotNull(bench.Sign("checkpoint"));
            bench.Helpers[0].EndSession("sleep");
            bench.Service.EffectAhead(PresenceSessionService.EffectDeadline);
            Assert.NotNull(bench.Sign("dispatchIntent"));
        }

        // The status the margin rule asks found the session ended; the renewal's own status cleared the margin before the dispatch record.
        Assert.Equal(2, bench.Helpers.Count);
        Assert.Equal(0, bench.Helpers[0].Statuses);
        Assert.Equal(1, bench.Helpers[1].Statuses);
        Assert.Single(bench.Helpers[1].SignedHashes);
    }

    [Fact]
    public void AnUnansweredRenewalAfterALockBeforeADispatchSignsNothing()
    {
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(Vectors.Now));
        using var bench = new Bench(clock) { Checks = ["passed", "unanswered"] };
        using (bench.Counter.Action("release submit", null, bench.Action))
        {
            Assert.NotNull(bench.Sign("checkpoint"));
            bench.Helpers[0].EndSession("lock");
            bench.Service.EffectAhead(PresenceSessionService.EffectDeadline);
            Assert.Throws<PresenceSessionEnded>(() => bench.Sign("dispatchIntent"));
            // The action asks for nothing more, never one check per signature.
            Assert.Throws<PresenceSessionEnded>(() => bench.Sign("dispatchDispatched"));
        }

        Assert.Equal(2, bench.Helpers.Count);
        Assert.Empty(bench.Helpers[1].SignedHashes);
        Assert.True(bench.Action.Failed);
    }

    [Fact]
    public void AnUnansweredRenewalBeforeADispatchSignsNothingSoTheEffectIsNotDispatched()
    {
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(Vectors.Now));
        using var bench = new Bench(clock) { Checks = ["passed", "unanswered"] };
        using (bench.Counter.Action("release submit", null, bench.Action))
        {
            Assert.NotNull(bench.Sign("checkpoint"));
            clock.Advance(TimeSpan.FromSeconds(200));
            bench.Service.EffectAhead(PresenceSessionService.EffectDeadline);
            Assert.Throws<PresenceSessionEnded>(() => bench.Sign("dispatchIntent"));
        }

        Assert.Equal(2, bench.Helpers.Count);
        Assert.Empty(bench.Helpers[1].SignedHashes);
        Assert.True(bench.Action.Failed);
    }

    [Fact]
    public async Task ARuntimeHoldsOneSessionPerDeviceAndAnotherActionsSignaturesAskTheirOwn()
    {
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(Vectors.Now));
        using var bench = new Bench(clock);
        using (bench.Counter.Action("release submit", null, bench.Action))
        {
            Assert.NotNull(bench.Sign("checkpoint"));
            var other = new PresenceSessionAction(_ => null);
            Task<RecordSignature?> running;
            using (ExecutionContext.SuppressFlow())
            {
                running = Task.Run(() =>
                {
                    using (bench.Counter.Action("release observe", null, other)) return bench.Sign("dispatchIntent");
                }, TestContext.Current.CancellationToken);
            }

            RecordSignature? elsewhere = await running;

            Assert.Null(elsewhere);
            Assert.NotNull(bench.Sign("dispatchIntent"));
        }

        Assert.Single(bench.Helpers);
    }

    [Fact]
    public void ASignatureOutsideAnyActionOfTheVocabularyAndADigestAloneNeverJoinASession()
    {
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(Vectors.Now));
        using var bench = new Bench(clock);
        // No action's tally: a single write keeps its own check.
        Assert.Null(bench.Sign("checkpoint"));
        using (bench.Counter.Action("Writing the operation record", null))
        {
            Assert.Null(bench.Sign("checkpoint"));
        }

        Assert.Empty(bench.Helpers);
    }

    private static string Charter(string root, int format, string presence) => JsonSerializer.Serialize(new
    {
        name = "Test Estate", slug = "test-estate", purpose = "Exercise the record format.", @operator = root, recordFormat = format,
        policies = new { signatures = "optional", releaseAcceptance = OperatorRole, presence },
    });

    private static IReadOnlyList<PresenceNotice> Since(OperatorCloseFixture f, long start) => f.Api.PresencePrompts(new(start)).Notices;

    private static string EstateModelBody(OperatorCloseFixture f, string device, string name) =>
        EstateModel.BodyString(f.Estate.Load().Current("device").Single(d => d.Id == device), name)!;

    /// <summary>The stand-in helpers an estate's sessions start, the recording custody of its device and the test host.</summary>
    private sealed class Sessions
    {
        internal List<PresenceHelperStandIn> Helpers { get; } = [];

        internal RecordingCustody Custody { get; private init; } = null!;

        internal static PresenceSessionHost Host(TimeProvider clock, Func<bool> agentRun) => new()
        {
            Build = PresenceSessionBuild.Parse("ABCDE12345", "vericairn-enclave", "26"),
            MacOS = () => true, MacOSMajor = () => 26, Requirement = new Requirement(null), HelperOf = _ => "helper", Setting = _ => PresenceSetting.Default,
            SameUserAgentRun = agentRun, Clock = clock, CheckWait = TimeSpan.Zero,
        };

        internal static Sessions For(OperatorCloseFixture f, Func<PresenceSessionHost, PresenceSessionHost>? adjust = null, bool agentRun = false, string check = "passed",
            int refuseAt = 0, string kind = SecureEnclaveCustody.Kind, bool embedded = true)
        {
            var custody = new RecordingCustody(VericairnHome.Path(), kind);
            var sessions = new Sessions { Custody = custody };
            f.Api.KeyStores = [custody];
            f.Api.PresenceRequired = device => device == f.Device;
            PresenceSessionHost host = Host(TimeProvider.System, () => agentRun);
            host = new PresenceSessionHost
            {
                Build = host.Build, MacOS = host.MacOS, MacOSMajor = host.MacOSMajor, Requirement = host.Requirement, HelperOf = host.HelperOf, Setting = host.Setting,
                SameUserAgentRun = host.SameUserAgentRun, Clock = host.Clock, CheckWait = host.CheckWait,
                Start = signer =>
                {
                    // The helper signs with the device's key itself, never through custody's own prompt.
                    var key = new FileKeyStore(VericairnHome.Path());
                    var helper = new PresenceHelperStandIn(digest => key.Sign(signer.DeviceId, digest), key.PublicKey(signer.DeviceId)!) { Check = check, RefuseAt = refuseAt };
                    sessions.Helpers.Add(helper);
                    return helper;
                },
            };
            // The command line hosts the runtime in its own process: the embedded profile.
            f.Api.HostsEmbedded = embedded;
            f.Api.PresenceHost = adjust is null ? host : adjust(host);
            return sessions;
        }
    }

    private sealed class Requirement(string? problem) : IPresenceHelperRequirement
    {
        public string? Problem(string helperPath, string requirement) => problem;
    }

    /// <summary>A presence session service over the shared vectors' records, its device's key a file key, its helpers stand-ins.</summary>
    private sealed class Bench : IDisposable
    {
        private readonly string home = Path.Combine(Path.GetTempPath(), "vericairn-bench-" + Guid.NewGuid().ToString("N"));
        private readonly FileKeyStore keys;
        private readonly SteppedClock clock;
        private readonly int limit;
        private int started;
        private const string Device = "01J9Z3K4M5N6P7Q8R9S0T1V2W4";

        internal Bench(SteppedClock clock, int limit = 300)
        {
            this.clock = clock;
            this.limit = limit;
            keys = new FileKeyStore(home);
            keys.Create(Device);
            Counter = new PresenceCounter();
            Service = new PresenceSessionService(Counter)
            {
                Host = new PresenceSessionHost
                {
                    Build = PresenceSessionBuild.Parse("ABCDE12345", null, "26"), MacOS = () => true, MacOSMajor = () => 26, Requirement = new Requirement(null),
                    HelperOf = _ => "helper", Setting = _ => PresenceSetting.Default, SameUserAgentRun = () => false, Clock = clock, CheckWait = TimeSpan.Zero,
                    Start = _ =>
                    {
                        var helper = new PresenceHelperStandIn(digest => keys.Sign(Device, digest), keys.PublicKey(Device)!, clock) { Check = Checks.ElementAtOrDefault(started++) ?? "passed" };
                        Helpers.Add(helper);
                        return helper;
                    },
                },
            };
            Action = new PresenceSessionAction(_ => null);
        }

        internal PresenceCounter Counter { get; }

        internal PresenceSessionService Service { get; }

        internal PresenceSessionAction Action { get; }

        internal List<PresenceHelperStandIn> Helpers { get; } = [];

        internal string[] Checks { get; init; } = [];

        internal IReadOnlyList<PresenceNotice> Notices() => Counter.Notices.ReadAsync(new PresencePromptsRequest(0), CancellationToken.None).GetAwaiter().GetResult().Notices;

        /// <summary>Signs a vector record, dated now, under the release submission's list: its checkpoint and its release build.</summary>
        internal RecordSignature? Sign(string name)
        {
            string publicKey = System.Buffers.Text.Base64Url.EncodeToString(keys.PublicKey(Device)!);
            byte[] content = Dated(name);
            var signing = new PresenceSessionSigning
            {
                Signer = new StoreSigner(keys, Device), Device = Device, Principal = "01J9Z3K4M5N6P7Q8R9S0T1V2W3", PublicKey = publicKey, Content = content,
                Describe = new PresenceSignature("operation", "release 0.9.4", "records the release build"), Limit = limit, WhyOff = () => null,
                ComposeList = (_, _) => List(publicKey),
            };
            return Service.Sign(signing);
        }

        private byte[] Dated(string name)
        {
            JsonObject record = JsonNode.Parse(Vectors.Root.GetProperty("records").GetProperty(name).GetRawText())!.AsObject();
            record["at"] = RecordParser.FormatTimestamp(clock.GetUtcNow());
            record["id"] = Records.Identity.Ulid.NewUlid(clock.GetUtcNow()).Value;
            record.Remove("supersedes");
            if (record["body"]!["status"] is not null && record["type"]!.GetValue<string>() == "operation") record["body"]!["status"] = "not-started";
            using JsonDocument document = JsonDocument.Parse(record.ToJsonString());
            return CanonicalJson.SerializeUtf8(document.RootElement);
        }

        private static PresenceSessionList List(string publicKey)
        {
            JsonElement terms = Vectors.Root.GetProperty("sessions").EnumerateArray().Single(v => v.GetProperty("name").GetString() == "release submit").GetProperty("terms");
            var entries = new List<PresenceListEntry>
            {
                new() { Type = "history-checkpoint", Count = 1, Trigger = "release" },
            };
            JsonElement build = terms.GetProperty("entries")[1];
            entries.Add(new PresenceListEntry
            {
                Type = "operation", Action = "dispatch-release-workflow", Count = 3, Statuses = PresenceSessionProtocol.OperationStatuses,
                Fixed = build.GetProperty("fixed").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal),
            });
            return new PresenceSessionList
            {
                Action = "release submit", ActionIdentity = "sha256:" + new string('1', 64), Entries = entries, References = [], ExactIds = [],
                Lines = [(new PresenceSignature("history-checkpoint", "trigger release", "witnesses"), "adds the checkpoint", false), (new PresenceSignature("operation", "release 0.9.4", "build"), "adds the build", false)],
            };
        }

        public void Dispose()
        {
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }
}

/// <summary>The shared vectors of tests/fixtures/presence-sessions/vectors.json, which the helper's tests and the runtime's checks read.</summary>
internal static class Vectors
{
    private static readonly Lazy<JsonElement> Shared = new(() =>
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx"))) root = Path.GetDirectoryName(root);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root!, "tests", "fixtures", "presence-sessions", "vectors.json")));
        return document.RootElement.Clone();
    });

    internal static JsonElement Root => Shared.Value;

    internal static long Now => DateTimeOffset.Parse(Root.GetProperty("clock").GetProperty("now").GetString()!, System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeSeconds();
}

/// <summary>Test helpers that vary one thing of a presence session host.</summary>
internal static class PresenceSessionHostVariation
{
    internal static PresenceSessionHost With(this PresenceSessionHost host, PresenceSessionBuild? build = null, bool? macOS = null, PresenceSetting? setting = null, string? requirement = null) => new()
    {
        Build = build ?? host.Build,
        MacOS = macOS is bool mac ? () => mac : host.MacOS,
        MacOSMajor = host.MacOSMajor,
        Requirement = requirement is null ? host.Requirement : new FixedRequirement(requirement),
        HelperOf = host.HelperOf,
        Setting = setting is null ? host.Setting : _ => setting,
        SameUserAgentRun = host.SameUserAgentRun,
        Clock = host.Clock,
        CheckWait = host.CheckWait,
        Start = host.Start,
    };

    private sealed class FixedRequirement(string problem) : IPresenceHelperRequirement
    {
        public string? Problem(string helperPath, string requirement) => problem;
    }
}

/// <summary>A test host whose sessions are stand-ins signing with the device's file key, for an estate whose operator's device needs presence.</summary>
internal static class StandInSessions
{
    internal static PresenceSessionHost Host(List<PresenceHelperStandIn> helpers, string check = "passed", TimeProvider? clock = null,
        Func<int, string>? checkForSession = null, Func<PresenceHelperStandIn, IPresenceHelperChannel>? channel = null,
        Func<bool>? agentRun = null) => new()
    {
        Clock = clock ?? TimeProvider.System,
        Build = PresenceSessionBuild.Parse("ABCDE12345", "vericairn-enclave", "26"),
        MacOS = () => true, MacOSMajor = () => 26, Requirement = new Accepting(), HelperOf = _ => "helper", Setting = _ => PresenceSetting.Default,
        SameUserAgentRun = agentRun ?? (() => false), CheckWait = TimeSpan.Zero,
        Start = signer =>
        {
            var key = new FileKeyStore(VericairnHome.Path());
            var helper = new PresenceHelperStandIn(digest => key.Sign(signer.DeviceId, digest), key.PublicKey(signer.DeviceId)!, clock)
            { Check = checkForSession?.Invoke(helpers.Count) ?? check };
            helpers.Add(helper);
            return channel?.Invoke(helper) ?? helper;
        },
    };

    private sealed class Accepting : IPresenceHelperRequirement
    {
        public string? Problem(string helperPath, string requirement) => null;
    }
}
