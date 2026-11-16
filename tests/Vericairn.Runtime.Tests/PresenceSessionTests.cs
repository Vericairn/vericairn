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
        Assert.Equal(["announced", "prompt", "prompt", "prompt", "prompt", "finished"], notices.Select(n => n.Kind));
        Assert.StartsWith("operator close asks for your presence once, for one presence session of 3 signatures in at most 300 s, covering only this list:", notices[0].Text, StringComparison.Ordinal);
        Assert.Contains($"1. acceptance STEP-1: accepts result {first.Result.Id} at its exact hash (exact ", notices[0].Text, StringComparison.Ordinal);
        Assert.Contains("changes: adds an acceptance about STEP-1 on the basis of 1 review", notices[0].Text, StringComparison.Ordinal);
        Assert.Contains("Presence check for operator close: the system dialog shows fingerprint ", notices[1].Text, StringComparison.Ordinal);
        Assert.StartsWith("Signature 1 of 3, under the session's check: acceptance STEP-1:", notices[2].Text, StringComparison.Ordinal);
        Assert.Equal("Signed 3 of 3 with one presence check.", notices[^1].Text);
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

    [Fact]
    public void DoctorSaysWhySessionsAreOffForThisMachineAndBuild()
    {
        PresenceSessionHost host = Sessions.Host(new SteppedClock(DateTimeOffset.UtcNow), () => false);
        Assert.Null(host.MachineOff("helper"));
        Assert.Equal("this is a development build, which carries no release identity; only a release build's signed helper opens a session",
            host.With(build: PresenceSessionBuild.Parse(null, null, "26")).MachineOff("helper"));
        Assert.Equal("this release has not qualified macOS 26 for presence sessions", host.With(build: PresenceSessionBuild.Parse("ABCDE12345", null, "15")).MachineOff("helper"));
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
        // The goal step's composition waits here for its action behind routing: nothing is listed until its route fills the drafts.
        var drafts = new List<Records.Authoring.RecordDraft>();
        PresenceSessionAction session = EstateService.GoalStepSession("01J9Z3K4M5N6P7Q8R9S0T1V2W9", () => drafts);
        var context = new PresenceComposeContext(new Records.Authoring.RecordDraft("assignment", "01J9Z3K4M5N6P7Q8R9S0T1V2W3", JsonSerializer.SerializeToElement(new { })),
            new HashSet<string>(StringComparer.Ordinal));
        Assert.Null(session.Compose(context));
        DateTimeOffset at = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        drafts.Add(new Records.Authoring.RecordDraft("assignment", "01J9Z3K4M5N6P7Q8R9S0T1V2W3", JsonSerializer.SerializeToElement(new { }), At: at, Id: "01J9Z3K4M5N6P7Q8R9S0T1V2WA"));
        drafts.Add(new Records.Authoring.RecordDraft("ask", "01J9Z3K4M5N6P7Q8R9S0T1V2W3", JsonSerializer.SerializeToElement(new { }), At: at, Id: "01J9Z3K4M5N6P7Q8R9S0T1V2WB"));
        PresenceSessionPlan plan = session.Compose(context)!;
        Assert.Equal("goal step", plan.Action);
        Assert.Equal(["assignment", "ask"], plan.Exact.Select(d => d.Type));
        Assert.Empty(plan.Kinds);
        // A draft without its id or time is not authored completely, so no list is composed from it.
        drafts.Add(new Records.Authoring.RecordDraft("ask", "01J9Z3K4M5N6P7Q8R9S0T1V2W3", JsonSerializer.SerializeToElement(new { })));
        Assert.Null(session.Compose(context));
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
        PresenceNotice renewal = Assert.Single(bench.Notices(), n => n.Kind == "revised");
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
        if (checks == 2) Assert.Contains("so the session is renewed before the effect is dispatched", Assert.Single(bench.Notices(), n => n.Kind == "revised").Text, StringComparison.Ordinal);
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
            int refuseAt = 0, string kind = SecureEnclaveCustody.Kind)
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
    internal static PresenceSessionHost Host(List<PresenceHelperStandIn> helpers, string check = "passed", TimeProvider? clock = null) => new()
    {
        Clock = clock ?? TimeProvider.System,
        Build = PresenceSessionBuild.Parse("ABCDE12345", "vericairn-enclave", "26"),
        MacOS = () => true, MacOSMajor = () => 26, Requirement = new Accepting(), HelperOf = _ => "helper", Setting = _ => PresenceSetting.Default,
        SameUserAgentRun = () => false, CheckWait = TimeSpan.Zero,
        Start = signer =>
        {
            var key = new FileKeyStore(VericairnHome.Path());
            var helper = new PresenceHelperStandIn(digest => key.Sign(signer.DeviceId, digest), key.PublicKey(signer.DeviceId)!, clock) { Check = check };
            helpers.Add(helper);
            return helper;
        },
    };

    private sealed class Accepting : IPresenceHelperRequirement
    {
        public string? Problem(string helperPath, string requirement) => null;
    }
}
