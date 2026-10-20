// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class OperatorRecordTests
{
    private static readonly string Head = new('a', 40);
    private static readonly string Merged = new('b', 40);
    private static readonly string Basis = new('c', 40);
    private static readonly string[] Required = ["tests (linux)", "tests (macos)", "tests (windows)"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheOperatorsPinnedAcceptanceTraversesRecordedOperationsAndIdenticalInputNeverRepeatsIt(bool release)
    {
        using var f = new Fixture(release: release);
        OperatorRecordResponse result = f.Run();
        Assert.True(result.Ok, result.Message + " " + result.NextStep);
        Assert.Equal("completed", result.Status);
        RecordEnvelope record = Assert.Single(f.Service.Session.Load().OfType("acceptance"));
        Assert.Equal(f.Subject.Reference, record.Subject);
        Assert.Equal(f.Estate.OperatorId, record.By);
        Assert.Equal(f.Device, Assert.Single(record.Signatures).Device);
        Assert.Equal(record.ComputedHash, result.Record!.Hash);
        Assert.Equal(Head, result.Head);
        Assert.Equal(record.ExpectedPath, f.Local.CommittedPath);
        Assert.Equal(["branch", "commit", "scan", "push", "open", "pull", "pull", "merge", "target"], f.Events);
        Assert.All(f.Service.Session.Load().OfType("operation"), o => Assert.Equal(f.Estate.OperatorId, o.By));
        Assert.Equal(0, f.Service.Validate().Errors);
        f.Reopen();
        OperatorRecordResponse repeated = f.Run();
        Assert.True(repeated.Ok, repeated.Message);
        Assert.Equal(result.Record, repeated.Record);
        Assert.Equal(1, f.Local.Commits);
        Assert.Equal(1, f.Remote.Pushes);
        Assert.Equal(2, f.Remote.Memory.Effects);
    }

    [Theory]
    [InlineData("dirty")]
    [InlineData("checks")]
    [InlineData("device")]
    [InlineData("principal")]
    [InlineData("body")]
    [InlineData("clone")]
    [InlineData("wait")]
    public void PreflightRefusalsWriteNothingAndReachNoRepositoryEffect(string refusal)
    {
        using var f = new Fixture(required: refusal == "checks" ? [] : null, device: refusal != "device");
        OperatorRecordRequest request = f.Request;
        if (refusal == "dirty") f.Local.Dirty = true;
        if (refusal == "principal") request = request with { By = f.Estate.BuilderId };
        if (refusal == "body") request = request with { Body = JsonSerializer.SerializeToElement(new { statement = (string?)null, basis = Array.Empty<string>(), self = true }) };
        if (refusal == "clone") RepositoryClones.Register(f.Estate.Estate.Id, f.Repository, Path.Combine(f.Estate.Root, "different-clone"));
        if (refusal == "wait") request = request with { WaitSeconds = -1 };
        int before = f.Service.Session.Load().Records.Count;
        OperatorRecordResponse result = f.Service.OperatorRecord(request, TestContext.Current.CancellationToken);
        Assert.False(result.Ok);
        Assert.Equal(before, f.Service.Session.Load().Records.Count);
        Assert.Empty(f.Events);
        Assert.Null(result.Record);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("commit")]
    [InlineData("scan")]
    [InlineData("push")]
    [InlineData("open")]
    [InlineData("pull")]
    [InlineData("merge")]
    [InlineData("target")]
    public void EachFailedStepStopsAndResumesWithoutRepeatingEarlierEffects(string step)
    {
        using var f = new Fixture();
        f.Fail = step;
        OperatorRecordResponse first = f.Run();
        Assert.False(first.Ok);
        Assert.NotNull(first.Branch);
        Assert.Contains("vericairn", first.NextStep, StringComparison.Ordinal);
        int effects = f.Remote.Memory.Effects;
        int pushes = f.Remote.Pushes;
        if (step == "scan") Assert.Equal(0, pushes);
        if (step == "push") Assert.Equal(0, effects);
        if (step == "open") Assert.Equal(0, effects);
        if (step == "pull") Assert.Equal(1, effects);
        if (step == "merge") Assert.Equal(1, effects);
        if (step == "target") Assert.Equal(2, effects);
        f.Fail = null;
        f.Reopen();
        OperatorRecordResponse resumed = f.Run("The failed prerequisite is repaired and no unconfirmed effect is retried.");
        Assert.True(resumed.Ok, resumed.Message + " " + resumed.NextStep);
        Assert.Single(f.Service.Session.Load().OfType("acceptance"));
        Assert.Equal(1, f.Local.Commits);
        Assert.Equal(1, f.Remote.Pushes);
        Assert.Equal(2, f.Remote.Memory.Effects);
        Assert.Equal(0, f.Service.Validate().Errors);
    }

    [Fact]
    public void ACommitWhoseReceiptWasLostIsVerifiedAfterRestartWithoutSigningOrCommittingAgain()
    {
        using var f = new Fixture();
        f.Local.LoseReceipt = true;
        OperatorRecordResponse first = f.Run();
        Assert.False(first.Ok);
        RecordEnvelope acceptance = Assert.Single(f.Service.Session.Load().OfType("acceptance"));
        string original = File.ReadAllText(Path.Combine(f.Estate.Root, acceptance.ExpectedPath));
        f.Local.LoseReceipt = false;
        f.Reopen();
        OperatorRecordResponse resumed = f.Run();
        Assert.True(resumed.Ok, resumed.Message);
        Assert.Equal(1, f.Local.Commits);
        Assert.Equal(original, File.ReadAllText(Path.Combine(f.Estate.Root, acceptance.ExpectedPath)));
    }

    [Theory]
    [InlineData("push")]
    [InlineData("open")]
    [InlineData("merge")]
    public void UncertainRemoteReceiptsAreObservedUnderTheirOriginalIdentity(string step)
    {
        using var f = new Fixture();
        f.Remote.LoseReceipt = step;
        OperatorRecordResponse first = f.Run();
        Assert.False(first.Ok);
        Assert.Equal("uncertain", first.Change!.Status);
        Assert.Contains("changes observe --operation " + first.Change.Operation!.Id, first.NextStep, StringComparison.Ordinal);
        f.Remote.LoseReceipt = null;
        f.Reopen();
        OperatorRecordResponse resumed = f.Run();
        Assert.True(resumed.Ok, resumed.Message + " " + resumed.NextStep);
        Assert.Single(f.Service.Session.Load().OfType("acceptance"));
        Assert.Equal(1, f.Remote.Pushes);
        Assert.Equal(2, f.Remote.Memory.Effects);
        Assert.Equal(0, f.Service.Validate().Errors);
    }

    [Fact]
    public void AnUnconfirmedPushCannotBeRetriedEvenWithReconciliationText()
    {
        using var f = new Fixture();
        f.Remote.LoseReceipt = "push";
        OperatorRecordResponse first = f.Run();
        f.Remote.RemoteHead = null;
        f.Remote.LoseReceipt = null;
        OperatorRecordResponse again = f.Run("A retry was requested without observing the original effect.");
        Assert.False(again.Ok);
        Assert.Equal(first.Change!.Identity, again.Change!.Identity);
        Assert.Equal(1, f.Remote.Pushes);
        Assert.Equal(0, f.Remote.Memory.Effects);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("pending")]
    [InlineData("failure")]
    [InlineData("skipped")]
    [InlineData("unknown")]
    [InlineData("stale")]
    [InlineData("incomplete")]
    [InlineData("moved")]
    public void NoUnmetOrUncertainCheckAllowsAMerge(string state)
    {
        using var f = new Fixture();
        f.Remote.Read = p => state switch
        {
            "missing" => p with { Checks = p.Checks.Take(2).ToArray() },
            "pending" => p with { Checks = [new(Required[0], Head, "in_progress", null)] },
            "stale" => p with { Checks = Checks(Basis) },
            "incomplete" => p with { Complete = false },
            "moved" => p with { Head = Basis },
            "unknown" => p with { Checks = [new(Required[0], Head, "unknown", "unknown")] },
            _ => p with { Checks = [new(Required[0], Head, "completed", state)] },
        };
        OperatorRecordResponse result = f.Run();
        Assert.False(result.Ok);
        Assert.Equal(1, f.Remote.Memory.Effects);
        Assert.DoesNotContain("merge", f.Events);
        Assert.NotNull(result.Change!.Observation);
        f.Remote.Read = null;
        Assert.True(f.Run().Ok);
        Assert.Equal(2, f.Remote.Memory.Effects);
    }

    [Fact]
    public void WaitingPollsRecordedObservationsAndTheMergeStillRechecksTheHead()
    {
        using var f = new Fixture();
        int reads = 0;
        f.Remote.Read = p => ++reads == 1 ? p with { Checks = [new(Required[0], Head, "queued", null)] } : p;
        OperatorRecordResponse result = f.Service.OperatorRecord(f.Request with { WaitSeconds = 1 }, TestContext.Current.CancellationToken);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(3, reads);
        Assert.Equal(3, f.Service.Session.Load().Current("operation").Count(o => EstateModel.BodyString(o, "action") == "change-observe"
            && o.Body.GetProperty("target").GetProperty("observation").GetString() == "pull-request"));
    }

    [Fact]
    public void AChangedHeadBetweenWaitingAndMergeStopsAtMergeAdmission()
    {
        using var f = new Fixture();
        int reads = 0;
        f.Remote.Read = p => ++reads == 1 ? p : p with { Head = Basis };
        OperatorRecordResponse result = f.Run();
        Assert.False(result.Ok);
        Assert.Equal("known-failure", result.Change!.Status);
        Assert.Equal(1, f.Remote.Memory.Effects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingOrFailedTargetChecksNeverRepeatTheMerge(bool failed)
    {
        using var f = new Fixture();
        f.Remote.Memory.SetTarget("Example/widget", new("main", Merged, [new(Required[0], Merged, failed ? "completed" : "queued", failed ? "failure" : null)]));
        OperatorRecordResponse first = f.Run();
        Assert.False(first.Ok);
        Assert.Equal("observed-success", first.Change!.Status);
        Assert.Equal(failed ? "failed" : "pending", first.Change.CheckStatus);
        Assert.Equal(failed ? 1 : 0, f.Service.Session.Load().Current("ask").Count());
        f.Remote.Memory.SetTarget("Example/widget", new("main", Merged, Checks(Merged)));
        f.Reopen();
        Assert.True(f.Run().Ok);
        Assert.Equal(2, f.Remote.Memory.Effects);
        Assert.Equal(1, f.Remote.Pushes);
    }

    [Fact]
    public void DirtyOrMovedLocalStateAndAnotherRequestCannotBypassAnUnfinishedAction()
    {
        using var f = new Fixture();
        f.Fail = "scan";
        OperatorRecordResponse first = f.Run();
        f.Fail = null;
        f.Local.Dirty = true;
        Assert.False(f.Run("The scan was repaired.").Ok);
        f.Local.Dirty = false;
        f.Local.Head = Merged;
        Assert.False(f.Run("The scan was repaired.").Ok);
        f.Local.Head = Head;
        OperatorRecordRequest other = f.Request with { Body = JsonSerializer.SerializeToElement(new { statement = "A different judgment.", basis = Array.Empty<string>(), self = true }) };
        Assert.False(f.Service.OperatorRecord(other, TestContext.Current.CancellationToken).Ok);
        Assert.Equal(0, f.Remote.Pushes);
        Assert.Single(f.Service.Session.Load().OfType("acceptance"));
        Assert.True(f.Run("The scan was repaired.").Ok);
        Assert.Equal(first.Record, f.Run().Record);
    }

    [Fact]
    public void ADeviceRequestCannotNameAnotherPrincipalEvenInsideTheRuntime()
    {
        using var f = new Fixture();
        string device = f.Service.DeviceCreate(new(f.Estate.BuilderId, "Contributor device", "file")).Written!.Id;
        Assert.True(f.Service.DeviceAdmit(new(device, f.Estate.OperatorId, "Admit the contributor.")).Ok);
        string publicKey = EstateModel.BodyString(f.Service.Session.Load().ById[device], "publicKey")!;
        using EstateService.RequestDeviceScope scope = f.Service.ForDeviceRequest(device, publicKey);
        Assert.False(f.Run().Ok);
        Assert.Empty(f.Service.Session.Load().OfType("acceptance"));
        Assert.Empty(f.Events);
    }

    [Fact]
    public void MalformedApiInputNeverBecomesAnExecutableRecoveryCommand()
    {
        using var f = new Fixture();
        Assert.False(f.Service.OperatorRecord(f.Request with { Type = null! }, TestContext.Current.CancellationToken).Ok);
        Assert.False(f.Service.OperatorRecord(f.Request with { By = null! }, TestContext.Current.CancellationToken).Ok);
        OperatorRecordResponse result = f.Service.OperatorRecord(f.Request with { Subject = "release;unexpected:" + f.Subject.Id }, TestContext.Current.CancellationToken);
        Assert.False(result.Ok);
        Assert.DoesNotContain("unexpected", result.NextStep, StringComparison.Ordinal);
        Assert.Empty(f.Events);
        Assert.Empty(f.Service.Session.Load().OfType("acceptance"));
    }

    [Fact]
    public void ALocalCheckpointCannotTurnPendingRecordedTargetChecksIntoSuccess()
    {
        using var f = new Fixture();
        f.Remote.Memory.SetTarget("Example/widget", new("main", Merged, []));
        OperatorRecordResponse waiting = f.Run();
        Assert.False(waiting.Ok);
        JsonObject saved = JsonNode.Parse(f.Service.Session.Live.Meta(waiting.Identity)!)!.AsObject();
        saved["completed"] = JsonSerializer.SerializeToNode(waiting.Change! with { Ok = true, CheckStatus = "passed" }, ProtocolJson.Options);
        f.Service.Session.Live.SetMeta(waiting.Identity, saved.ToJsonString());
        OperatorRecordResponse repeated = f.Run();
        Assert.False(repeated.Ok);
        Assert.Contains("recorded evidence", repeated.Message, StringComparison.Ordinal);
        Assert.Equal(2, f.Remote.Memory.Effects);
    }

    [Fact]
    public void ACompletedCheckpointStillRequiresTheOriginalRecordCommit()
    {
        using var f = new Fixture();
        Assert.True(f.Run().Ok);
        f.Local.CommitAvailable = false;
        OperatorRecordResponse repeated = f.Run();
        Assert.False(repeated.Ok);
        Assert.Contains("original commit", repeated.Message, StringComparison.Ordinal);
        Assert.Equal(1, f.Local.Commits);
        Assert.Equal(2, f.Remote.Memory.Effects);
    }

    [Fact]
    public void CancellationDuringCheckObservationStopsBeforeMergeAndTheSameInputCanResume()
    {
        using var f = new Fixture();
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        f.Remote.Read = pull => { canceled.Cancel(); return pull; };
        OperatorRecordResponse stopped = f.Service.OperatorRecord(f.Request, canceled.Token);
        Assert.False(stopped.Ok);
        Assert.Contains("interrupted", stopped.Message, StringComparison.Ordinal);
        Assert.Equal(1, f.Remote.Memory.Effects);
        Assert.DoesNotContain("merge", f.Events);
        f.Remote.Read = null;
        Assert.True(f.Run().Ok);
        Assert.Equal(2, f.Remote.Memory.Effects);
    }

    private static ChangeCheck[] Checks(string head) => Required.Select(n => new ChangeCheck(n, head, "completed", "success")).ToArray();

    private sealed class Fixture : IDisposable
    {
        internal TestEstate Estate { get; } = new(3);
        internal EstateService Service { get; private set; }
        internal LocalAdapter Local { get; }
        internal RemoteAdapter Remote { get; }
        internal List<string> Events { get; } = [];
        internal string? Fail { get; set; }
        internal string Repository { get; }
        internal string Product { get; }
        internal string? Device { get; }
        internal RecordEnvelope Subject { get; }
        internal OperatorRecordRequest Request => new("acceptance", Estate.OperatorId, JsonSerializer.SerializeToElement(new
        { statement = "I accept the pinned evidence.", basis = Array.Empty<string>(), self = true }), $"{Subject.Type}:{Subject.Id}:{Subject.ComputedHash}", WaitSeconds: 0);

        internal Fixture(IReadOnlyList<string>? required = null, bool device = true, bool release = false)
        {
            Repository = Estate.Add("resource", Estate.OperatorId, """{"kind":"repository","name":"Widget","role":"Estate","location":{"github":"Example/widget"},"visibility":"private"}""").Id;
            RecordEnvelope product = Estate.Add("product", Estate.OperatorId, $$"""
                {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],
                 "changes":{"provider":"memory-pull-requests","repository":"{{Repository}}","target":"main","requiredChecks":{{JsonSerializer.Serialize(required ?? Required)}} } }
                """);
            Product = product.Id;
            Subject = release
                ? Estate.Add("release", Estate.OperatorId, $$"""{"version":"1.0.0","status":"candidate","repository":"Example/widget","commit":"{{Basis}}","artifacts":[{"name":"widget.zip","hash":"sha256:{{new string('1', 64)}}","bytes":10,"platform":"linux-x64"}],"recordFormat":{"reads":[1,2,3],"writes":3},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]} """, product.Reference)
                : Estate.Add("work-order", Estate.OperatorId, """{"key":"M-1","kind":"milestone","title":"The milestone","intent":"Finish.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["result"]}""");
            if (!release) Estate.Add("work-status", Estate.OperatorId, """{"status":"submitted","note":"Ready."}""", Subject.Reference);
            RepositoryClones.Register(Estate.Estate.Id, Repository, Estate.Root);
            Local = new(this);
            Remote = new(this);
            Service = Open();
            if (device)
            {
                WriteOutcome created = Service.DeviceCreate(new(Estate.OperatorId, "Operator device", "file"));
                Assert.True(created.Ok, created.Problem?.Message);
                Device = created.Written!.Id;
                WriteOutcome admitted = Service.DeviceAdmit(new(Device, Estate.OperatorId, "Admit the operator's own device."));
                Assert.True(admitted.Ok, admitted.Problem?.Message);
            }
        }

        internal OperatorRecordResponse Run(string? reconciliation = null) => Service.OperatorRecord(Request with { Reconciliation = reconciliation }, TestContext.Current.CancellationToken);
        internal void Reopen() { Service.Dispose(); Service = Open(); }
        private EstateService Open()
        {
            EstateService service = EstateService.Open(Estate.Root);
            service.Providers = new() { RecordCommitAdapter = Local, PushAdapter = Remote, SecretScanner = Remote };
            service.Providers.Register(Remote);
            return service;
        }
        internal void Step(string step)
        {
            Events.Add(step);
            if (Fail == step) throw new ProviderRefusedException("The controlled " + step + " step refused.");
        }
        public void Dispose() { Service.Dispose(); Estate.Dispose(); }
    }

    private sealed class LocalAdapter(Fixture fixture) : IRecordCommitAdapter
    {
        internal bool Dirty { get; set; }
        internal bool LoseReceipt { get; set; }
        internal bool CommitAvailable { get; set; } = true;
        internal int Commits { get; private set; }
        internal string Head { get; set; } = Basis;
        internal string? CommittedPath { get; private set; }
        private string branch = "main";
        public RecordCommitBasis Prepare(string directory) { Clean(); return new(branch, Head); }
        public void Branch(string directory, RecordCommitBasis basis, string next, IReadOnlyDictionary<string, string> files)
        { if (branch != next) { fixture.Step("branch"); branch = next; } }
        public string Commit(string directory, string name, string basis, IReadOnlyDictionary<string, string> files, IReadOnlySet<string> receipts)
        {
            Clean();
            Assert.Equal(branch, name);
            KeyValuePair<string, string> file = Assert.Single(files);
            string path = file.Key;
            Assert.Equal(file.Value, ContentHash.Sha256(File.ReadAllBytes(Path.Combine(directory, path))));
            if (CommittedPath is not null)
            {
                if (Head != OperatorRecordTests.Head) throw new ProviderRefusedException("The commit moved.");
                Assert.Equal(CommittedPath, path);
                return Head;
            }
            fixture.Step("commit");
            Head = OperatorRecordTests.Head;
            CommittedPath = path;
            Commits++;
            if (LoseReceipt) throw new IOException("The local receipt was lost.");
            return Head;
        }
        public void Check(string directory, string name, string head, IReadOnlySet<string> receipts)
        {
            Clean();
            if (branch != name || Head != head) throw new ProviderRefusedException("The branch moved.");
            // Every generated receipt is accounted for on recovery, including failed merge intents and target asks.
            Assert.All(fixture.Service.Session.Load().Records.Where(r => r.Type is "operation" or "ask"), r => Assert.Contains(r.ExpectedPath, receipts));
        }
        public void Verify(string directory, string head, string basis, IReadOnlyDictionary<string, string> files)
        {
            if (!CommitAvailable) throw new ProviderRefusedException("The original commit is unavailable.");
            Assert.Equal(OperatorRecordTests.Head, head);
            Assert.Equal(Basis, basis);
            KeyValuePair<string, string> file = Assert.Single(files);
            Assert.Equal(CommittedPath, file.Key);
            string path = file.Key;
            Assert.Equal(file.Value, ContentHash.Sha256(File.ReadAllBytes(Path.Combine(directory, path))));
        }
        private void Clean() { if (Dirty) throw new ProviderRefusedException("Other changes remain."); }
    }

    private sealed class RemoteAdapter : IPushAdapter, ISecretScanner, IPullRequestProvider
    {
        private readonly Fixture fixture;
        internal MemoryPullRequestProvider Memory { get; } = new() { Checks = Checks(Head) };
        internal string? LoseReceipt { get; set; }
        internal string? RemoteHead { get; set; }
        internal int Pushes { get; private set; }
        internal Func<PullRequestObservation, PullRequestObservation>? Read { get; set; }
        internal RemoteAdapter(Fixture fixture)
        { this.fixture = fixture; Memory.SetTarget("Example/widget", new("main", Merged, Checks(Merged))); }
        public string Provider => Memory.Provider;
        public ProviderDescriptor Descriptor => Memory.Descriptor;
        public string PushRemote(string destination) => Memory.PushRemote(destination);
        public PushPlan Prepare(string directory, string remote, string branch, string target, string head) => new(branch, target, head, Basis, null, Basis);
        public SecretScan Scan(string directory, PushPlan plan)
        {
            fixture.Step("scan");
            Assert.Contains(fixture.Service.Session.Load().OfType("operation"), o => EstateModel.BodyString(o, "action") == "change-push" && EstateModel.BodyString(o, "status") == "not-started");
            return new("scanner", "1.0.0", plan.Range, "passed", 0);
        }
        public void Push(string directory, string remote, PushPlan plan, SecretScan scan)
        { fixture.Step("push"); Assert.True(scan.Passed(plan)); Pushes++; RemoteHead = plan.Head; Receipt("push"); }
        public string? Observe(string directory, string remote, string branch) => RemoteHead;
        public ChangeReceipt Open(string destination, string branch, string target, string head, string title, string body)
        { fixture.Step("open"); ChangeReceipt receipt = Memory.Open(destination, branch, target, head, title, body); Receipt("open"); return receipt; }
        public PullRequestObservation? Observe(string destination, string numberOrBranch)
        { fixture.Step("pull"); PullRequestObservation? pull = Memory.Observe(destination, numberOrBranch); return pull is null ? null : Read?.Invoke(pull) ?? pull; }
        public ChangeReceipt Merge(string destination, string number, string head)
        { fixture.Step("merge"); Assert.Equal(Head, head); ChangeReceipt receipt = Memory.Merge(destination, number, head); Receipt("merge"); return receipt; }
        public TargetObservation ObserveTarget(string destination, string branch, string commit)
        { fixture.Step("target"); Assert.Equal(Merged, commit); return Memory.ObserveTarget(destination, branch, commit); }
        private void Receipt(string step) { if (LoseReceipt == step) throw new ProviderUncertainException("The receipt was lost."); }
    }
}
