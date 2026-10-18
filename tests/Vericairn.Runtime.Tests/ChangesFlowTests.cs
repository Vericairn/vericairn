// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class ChangesFlowTests
{
    internal static readonly string Head = new('a', 40);
    internal static readonly string Basis = new('c', 40);
    internal static readonly string Merged = new('b', 40);
    private static readonly string[] ScanFields = ["findingCount", "range", "tool", "verdict", "version"];
    internal static readonly string[] Required = ["tests (linux)", "tests (macos)", "tests (windows)"];

    [Fact]
    public void PushOpeningMergeAndEveryObservationHaveIntentBeforeEffectAndOutcomeAfter()
    {
        using var f = new Fixture();
        f.Adapter.BeforePrepare = () => f.IntentExists("change-push");
        f.Scanner.BeforeScan = () => f.IntentExists("change-push");
        f.Adapter.BeforePush = () =>
        {
            RecordEnvelope operation = f.Current("change-push");
            Assert.Equal("dispatched", EstateModel.BodyString(operation, "status"));
            Assert.Equal(Basis + ".." + Head, operation.Body.GetProperty("scan").GetProperty("range").GetString());
        };
        f.Provider.BeforeEffect = action => Assert.Equal("dispatched", EstateModel.BodyString(f.Current(action), "status"));
        f.Provider.BeforeRead = () => Assert.Equal("dispatched", EstateModel.BodyString(f.Current("change-observe"), "status"));

        ChangeResponse pushed = f.Service.ChangesPush(f.Push);
        Assert.True(pushed.Ok, pushed.Message);
        ChangeResponse opened = f.Service.ChangesOpen(f.Open);
        Assert.True(opened.Ok, opened.Message);
        ChangeResponse merged = f.Service.ChangesMerge(f.Merge with { PullRequest = opened.PullRequest! });
        Assert.True(merged.Ok, merged.Message);
        Assert.Equal(Merged, merged.MergeCommit);
        Assert.Equal("passed", merged.CheckStatus);
        Assert.Equal(Head, f.Provider.MergedHead);
        Assert.Equal(1, f.Adapter.Pushes);
        Assert.Equal(Merged, f.Provider.ObservedTarget);
        Assert.DoesNotContain(f.Service.Validate().Findings, finding => finding.Severity == "error");
        foreach (IGrouping<string?, RecordEnvelope> chain in f.Service.Session.Load().OfType("operation").GroupBy(o => EstateModel.BodyString(o, "identity")))
        {
            RecordEnvelope first = chain.Single(o => o.Supersedes is null);
            Assert.Equal("not-started", EstateModel.BodyString(first, "status"));
            RecordEnvelope last = f.Service.Session.Load().Latest(first);
            Assert.Equal("observed-success", EstateModel.BodyString(last, "status"));
            Assert.True(last.Body.TryGetProperty("observed", out _));
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("pending")]
    [InlineData("failed")]
    [InlineData("other-head")]
    [InlineData("other-failed")]
    [InlineData("head-moved")]
    [InlineData("none-declared")]
    [InlineData("incomplete")]
    [InlineData("wrong-target")]
    [InlineData("skipped")]
    public void MergeRefusesEveryUnqualifiedHead(string failure)
    {
        using var f = new Fixture(failure == "none-declared" ? [] : Required);
        IReadOnlyList<ChangeCheck> checks = Checks(Head);
        checks = failure switch
        {
            "missing" => checks.Take(2).ToArray(),
            "pending" => [checks[0] with { Status = "in_progress", Conclusion = null }, .. checks.Skip(1)],
            "failed" => [checks[0] with { Conclusion = "failure" }, .. checks.Skip(1)],
            "other-head" => [checks[0] with { Head = Basis }, .. checks.Skip(1)],
            "other-failed" => [.. checks, new("extra", Head, "completed", "failure")],
            "skipped" => [checks[0] with { Conclusion = "skipped" }, .. checks.Skip(1)],
            _ => checks,
        };
        f.Provider.Memory.Set("Example/widget", new("1", "work/change", failure == "wrong-target" ? "other" : "main",
            failure == "head-moved" ? Basis : Head, "open", null, checks, failure != "incomplete"));
        ChangeResponse result = f.Service.ChangesMerge(f.Merge);
        Assert.False(result.Ok);
        Assert.Equal("known-failure", result.Status);
        Assert.Equal(0, f.Provider.Memory.Effects);
        Assert.Equal("not-started", EstateModel.BodyString(f.Service.Session.Load().OfType("operation").First(), "status"));
        Assert.DoesNotContain(f.Service.Validate().Findings, finding => finding.Severity == "error");
    }

    [Fact]
    public void HeadMovingAfterObservationCannotBeMerged()
    {
        using var f = new Fixture();
        f.SeedPull();
        f.Provider.BeforeEffect = _ => f.Provider.Memory.Set("Example/widget", new("1", "work/change", "main", Basis, "open", null, Checks(Basis)));
        ChangeResponse result = f.Service.ChangesMerge(f.Merge);
        Assert.False(result.Ok);
        Assert.Equal("known-failure", result.Status);
        Assert.Equal(0, f.Provider.Memory.Effects);
    }

    [Theory]
    [InlineData("findings", 1, false)]
    [InlineData("unavailable", null, false)]
    [InlineData("incomplete", null, false)]
    [InlineData("passed", 0, true)]
    [InlineData("passed", 1, false)]
    public void PushRequiresASuccessfulScanOfExactlyItsCommits(string verdict, int? count, bool otherRange)
    {
        using var f = new Fixture();
        f.Scanner.Result = new("scanner", "1.2.3", otherRange ? Basis + ".." + Merged : Basis + ".." + Head, verdict, count);
        ChangeResponse response = f.Service.ChangesPush(f.Push);
        Assert.False(response.Ok);
        Assert.Equal("known-failure", response.Status);
        Assert.Equal(0, f.Adapter.Pushes);
        JsonElement scan = f.Current("change-push").Body.GetProperty("scan");
        Assert.Equal(ScanFields.Where(n => count is not null || n != "findingCount").Order(), scan.EnumerateObject().Select(p => p.Name).Order());
        Assert.DoesNotContain(f.Estate.Root, scan.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingScannerRefusesBeforeAnyRemoteObservation()
    {
        using var f = new Fixture();
        f.Service.Providers.SecretScanner = null;
        ChangeResponse response = f.Service.ChangesPush(f.Push);
        Assert.False(response.Ok);
        Assert.Equal(0, f.Adapter.Preparations);
        Assert.Equal(0, f.Adapter.Pushes);
    }

    [Theory]
    [InlineData("push")]
    [InlineData("open")]
    [InlineData("merge")]
    public void UnknownEffectsStayUnknownAcrossRestartAndCannotBeRetried(string action)
    {
        using var f = new Fixture();
        if (action == "open") Assert.True(f.Service.ChangesPush(f.Push).Ok);
        f.SeedPull();
        if (action == "push") f.Adapter.Uncertain = true;
        else f.Provider.Uncertain = true;
        ChangeResponse Run() => action switch { "push" => f.Service.ChangesPush(f.Push), "open" => f.Service.ChangesOpen(f.Open), _ => f.Service.ChangesMerge(f.Merge) };
        ChangeResponse first = Run();
        Assert.Equal("uncertain", first.Status);
        f.Reopen();
        int effects = f.Provider.Calls + f.Adapter.Pushes;
        ChangeResponse retry = Run();
        Assert.False(retry.Ok);
        Assert.Equal("uncertain", retry.Status);
        Assert.Equal(first.Identity, retry.Identity);
        Assert.Equal(effects, f.Provider.Calls + f.Adapter.Pushes);
        f.Provider.ReadUnavailable = true;
        f.Adapter.RemoteHead = null;
        ChangeResponse observation = f.Service.ChangesObserve(new(first.Operation!.Id, f.Estate.OperatorId));
        Assert.False(observation.Ok);
        Assert.Equal("uncertain", EstateModel.BodyString(f.Current("change-" + action), "status"));
    }

    [Fact]
    public void AConfirmedMergeCanBeRecoveredWithoutRepeatingItsEffect()
    {
        using var f = new Fixture();
        f.SeedPull();
        f.Provider.Uncertain = true;
        ChangeResponse first = f.Service.ChangesMerge(f.Merge);
        f.Provider.Uncertain = false;
        f.Provider.Memory.Set("Example/widget", new("1", "work/change", "main", Head, "merged", Merged, Checks(Head)));
        ChangeResponse observed = f.Service.ChangesObserve(new(first.Operation!.Id, f.Estate.OperatorId));
        Assert.True(observed.Ok, observed.Message);
        Assert.Equal(1, f.Provider.Calls);
    }

    [Fact]
    public void FailureAtTheMergeCommitRaisesOneOperatorAskAndKeepsTheSuccessfulMerge()
    {
        using var f = new Fixture();
        f.SeedPull();
        f.Provider.Memory.SetTarget("Example/widget", new("main", Merged, [Checks(Merged)[0] with { Conclusion = "failure" }, .. Checks(Merged).Skip(1)]));
        ChangeResponse result = f.Service.ChangesMerge(f.Merge);
        Assert.Equal("observed-success", result.Status);
        Assert.Equal("failed", result.CheckStatus);
        Assert.NotNull(result.Ask);
        Assert.False(result.Ok);
        ChangeResponse again = f.Service.ChangesObserve(new(result.Operation!.Id, f.Estate.OperatorId));
        Assert.Equal(result.Ask.Id, again.Ask!.Id);
        Assert.Single(f.Service.Session.Load().OfType("ask"));
        Assert.Equal(Merged, f.Provider.ObservedTarget);
    }

    [Fact]
    public void PendingTargetChecksRemainPendingAndDoNotRepeatTheMerge()
    {
        using var f = new Fixture();
        f.SeedPull();
        f.Provider.Memory.SetTarget("Example/widget", new("main", Merged, []));
        ChangeResponse result = f.Service.ChangesMerge(f.Merge);
        Assert.False(result.Ok);
        Assert.Equal("pending", result.CheckStatus);
        Assert.Empty(f.Service.Session.Load().OfType("ask"));
        f.Provider.Memory.SetTarget("Example/widget", new("main", Merged, Checks(Merged)));
        Assert.True(f.Service.ChangesObserve(new(result.Operation!.Id, f.Estate.OperatorId)).Ok);
        Assert.Equal(1, f.Provider.Memory.Effects);
    }

    [Fact]
    public void RepositoryWriteAuthorityIsRequiredBeforeAllProviderCalls()
    {
        using var f = new Fixture();
        f.Provider.BeforeRead = () => Assert.Fail("No provider observation was authorized.");
        Assert.False(f.Service.ChangesPush(f.Push with { By = f.Estate.BuilderId }).Ok);
        Assert.False(f.Service.ChangesOpen(f.Open with { By = f.Estate.BuilderId }).Ok);
        Assert.False(f.Service.ChangesMerge(f.Merge with { By = f.Estate.BuilderId }).Ok);
        Assert.False(f.Service.ChangesObserve(new(f.Estate.Estate.Id, f.Estate.BuilderId)).Ok);
        Assert.Empty(f.Service.Session.Load().OfType("operation"));
    }

    [Fact]
    public void MissingRequestPinsRefuseWithoutInvokingAProvider()
    {
        using var f = new Fixture();
        Assert.False(f.Service.ChangesPush(f.Push with { Head = null! }).Ok);
        Assert.False(f.Service.ChangesOpen(f.Open with { Product = null! }).Ok);
        Assert.False(f.Service.ChangesMerge(f.Merge with { PullRequest = null! }).Ok);
        Assert.False(f.Service.ChangesObserve(new(null!, f.Estate.OperatorId)).Ok);
        Assert.Equal(0, f.Adapter.Preparations);
        Assert.Equal(0, f.Provider.Calls);
        Assert.Empty(f.Service.Session.Load().OfType("operation"));
    }

    [Fact]
    public void ARepositoryWriteGrantAllowsTheBuilderAndIsRecheckedAfterTheScan()
    {
        using var f = new Fixture();
        RecordEnvelope authority = f.Grant();
        f.Scanner.BeforeScan = () =>
        {
            f.Estate.SetClock(DateTimeOffset.UtcNow);
            f.Estate.Add("authority", f.Estate.OperatorId,
                authority.Body.GetRawText().Replace("repository-write", "local-engineering", StringComparison.Ordinal), supersedes: authority.Id);
        };
        ChangeResponse result = f.Service.ChangesPush(f.Push with { By = f.Estate.BuilderId });
        Assert.False(result.Ok);
        Assert.Equal(0, f.Adapter.Pushes);
    }

    [Fact]
    public void AGrantInForceAdmitsTheBuilderWithoutGrantingPublication()
    {
        using var f = new Fixture();
        f.Grant();
        ChangeResponse result = f.Service.ChangesPush(f.Push with { By = f.Estate.BuilderId });
        Assert.True(result.Ok, result.Message);
    }

    [Fact]
    public void ADeclarationChangedAfterObservationRefusesBeforeTheMerge()
    {
        using var f = new Fixture();
        f.SeedPull();
        f.Provider.BeforeRead = () =>
        {
            f.Provider.BeforeRead = null;
            JsonObject product = JsonNode.Parse(f.Estate.Load().ById[f.Product].Body.GetRawText())!.AsObject();
            product["changes"]!["requiredChecks"] = new JsonArray("another check");
            f.Estate.SetClock(DateTimeOffset.UtcNow);
            f.Estate.Add("product", f.Estate.OperatorId, product.ToJsonString(), supersedes: f.Product);
        };
        ChangeResponse result = f.Service.ChangesMerge(f.Merge);
        Assert.Equal("known-failure", result.Status);
        Assert.Equal(0, f.Provider.Memory.Effects);
        Assert.Equal(0, f.Service.Validate().Errors);
    }

    [Fact]
    public void RecoveryKeepsTheOriginalDeclarationAfterTheProductChanges()
    {
        using var f = new Fixture();
        f.SeedPull();
        f.Provider.AfterEffect = () => throw new IOException("The receipt was interrupted.");
        ChangeResponse first = f.Service.ChangesMerge(f.Merge);
        Assert.Equal("uncertain", first.Status);
        JsonObject product = JsonNode.Parse(f.Estate.Load().ById[f.Product].Body.GetRawText())!.AsObject();
        product["changes"]!["provider"] = "unavailable-provider";
        product["changes"]!["requiredChecks"] = new JsonArray("another check");
        f.Estate.SetClock(DateTimeOffset.UtcNow);
        f.Estate.Add("product", f.Estate.OperatorId, product.ToJsonString(), supersedes: f.Product);
        f.Reopen();
        ChangeResponse observed = f.Service.ChangesObserve(new(first.Operation!.Id, f.Estate.OperatorId));
        Assert.True(observed.Ok, observed.Message);
        Assert.Equal(first.Identity, observed.Identity);
        Assert.Equal(1, f.Provider.Memory.Effects);
        Assert.Equal(0, f.Service.Validate().Errors);
    }

    [Fact]
    public void OpeningWithoutARecordedScannedPushIsRefused()
    {
        using var f = new Fixture();
        Assert.False(f.Service.ChangesOpen(f.Open).Ok);
        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public void AFailedGateCanBeRetriedOnlyWithReconciliationAndANewObservation()
    {
        using var f = new Fixture();
        f.Provider.Memory.Set("Example/widget", new("1", "work/change", "main", Head, "open", null, []));
        Assert.Equal("known-failure", f.Service.ChangesMerge(f.Merge).Status);
        f.SeedPull();
        Assert.False(f.Service.ChangesMerge(f.Merge).Ok);
        ChangeResponse retried = f.Service.ChangesMerge(f.Merge with { Reconciliation = "The earlier gate refused before any merge; the checks have now completed." });
        Assert.True(retried.Ok, retried.Message);
        Assert.Equal(1, f.Provider.Memory.Effects);
        Assert.Equal(0, f.Service.Validate().Errors);
    }

    [Fact]
    public void AnExpiredGrantAdmitsNoProviderCall()
    {
        using var f = new Fixture();
        RecordEnvelope grant = f.Grant();
        JsonObject body = JsonNode.Parse(grant.Body.GetRawText())!.AsObject();
        body["expires"] = "2026-10-13T13:00:00Z";
        f.Estate.Add("authority", f.Estate.OperatorId, body.ToJsonString(), supersedes: grant.Id);
        Assert.False(f.Service.ChangesPush(f.Push with { By = f.Estate.BuilderId }).Ok);
        Assert.Equal(0, f.Adapter.Preparations);
    }

    [Fact]
    public void AFutureDatedGrantIsNotYetInForce()
    {
        using var f = new Fixture();
        f.Estate.SetClock(DateTimeOffset.UtcNow.AddHours(1));
        f.Grant();
        Assert.False(f.Service.ChangesPush(f.Push with { By = f.Estate.BuilderId }).Ok);
        Assert.Equal(0, f.Adapter.Preparations);
    }

    [Fact]
    public void AnUnavailablePreMergeObservationRecordsItsUncertaintyWithoutClaimingAMergeWasSent()
    {
        using var f = new Fixture();
        f.SeedPull();
        f.Provider.ReadUnavailable = true;
        ChangeResponse response = f.Service.ChangesMerge(f.Merge);
        Assert.Equal("known-failure", response.Status);
        Assert.Equal("uncertain", EstateModel.BodyString(f.Current("change-observe"), "status"));
        Assert.Equal(0, f.Provider.Calls);
        f.Provider.ReadUnavailable = false;
        Assert.True(f.Service.ChangesMerge(f.Merge with { Reconciliation = "The earlier attempt stopped at observation before any merge request." }).Ok);
    }

    [Fact]
    public void AnUnknownTargetConclusionStaysUncertainAndIsNotCalledAFailure()
    {
        using var f = new Fixture();
        f.SeedPull();
        f.Provider.Memory.SetTarget("Example/widget", new("main", Merged, [Checks(Merged)[0] with { Conclusion = "unknown" }, .. Checks(Merged).Skip(1)]));
        ChangeResponse response = f.Service.ChangesMerge(f.Merge);
        Assert.Equal("uncertain", response.CheckStatus);
        Assert.Equal("observed-success", response.Status);
        Assert.Null(response.Ask);
        Assert.False(response.Ok);
    }

    [Fact]
    public void ANewHeadCannotBypassAnUnresolvedPush()
    {
        using var f = new Fixture();
        f.Adapter.Uncertain = true;
        Assert.Equal("uncertain", f.Service.ChangesPush(f.Push).Status);
        Assert.False(f.Service.ChangesPush(f.Push with { Head = Merged, Reconciliation = "A new head is ready." }).Ok);
        Assert.Equal(1, f.Adapter.Pushes);
    }

    [Fact]
    public void ANewResourceForTheSameDestinationCannotBypassAnUnresolvedPush()
    {
        using var f = new Fixture();
        f.Adapter.Uncertain = true;
        ChangeResponse first = f.Service.ChangesPush(f.Push);
        JsonObject product = JsonNode.Parse(f.Estate.Load().ById[f.Product].Body.GetRawText())!.AsObject();
        RecordEnvelope original = f.Estate.Load().ById[product["changes"]!["repository"]!.GetValue<string>()];
        f.Estate.SetClock(DateTimeOffset.UtcNow);
        RecordEnvelope alias = f.Estate.Add("resource", f.Estate.OperatorId, original.Body.GetRawText(), supersedes: original.Id);
        product["changes"]!["repository"] = alias.Id;
        RecordEnvelope revised = f.Estate.Add("product", f.Estate.OperatorId, product.ToJsonString(), supersedes: f.Product);
        RepositoryClones.Register(f.Estate.Estate.Id, alias.Id, f.Estate.Root);
        ChangeResponse second = f.Service.ChangesPush(f.Push with { Product = revised.Id, Head = Merged });
        Assert.Equal("uncertain", second.Status);
        Assert.Equal(first.Identity, second.Identity);
        Assert.Equal(1, f.Adapter.Pushes);
    }

    [Fact]
    public async Task AnInFlightMergeCannotBeRepeatedOrReconciledAndDoesNotHoldReaders()
    {
        using var f = new Fixture();
        f.SeedPull();
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        f.Provider.BeforeEffect = _ => { entered.Set(); Assert.True(finish.Wait(TimeSpan.FromSeconds(10))); };
        Task<ChangeResponse> first = Task.Run(() => f.Service.ChangesMerge(f.Merge));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            await Task.Run(() => f.Service.Health()).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            ChangeResponse overlapping = await Task.Run(() => f.Service.ChangesMerge(f.Merge)).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.False(overlapping.Ok);
            Assert.False(f.Service.ChangesObserve(new(f.Current("change-merge").Id, f.Estate.OperatorId)).Ok);
        }
        finally
        {
            finish.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        Assert.True((await first).Ok);
        Assert.Equal(1, f.Provider.Memory.Effects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInterruptedMergeReceiptIsRecordedAsUncertainAndRecoveredWithoutRetry(bool accessDenied)
    {
        using var f = new Fixture();
        f.SeedPull();
        f.Provider.AfterEffect = () => throw (accessDenied ? new UnauthorizedAccessException("Simulated receipt interruption.")
            : new IOException("Simulated receipt interruption."));
        ChangeResponse first = f.Service.ChangesMerge(f.Merge);
        Assert.Equal("uncertain", first.Status);
        Assert.Equal(1, f.Provider.Memory.Effects);
        f.Reopen();
        Assert.False(f.Service.ChangesMerge(f.Merge).Ok);
        f.Provider.AfterEffect = null;
        Assert.True(f.Service.ChangesObserve(new(first.Operation!.Id, f.Estate.OperatorId)).Ok);
        Assert.Equal(1, f.Provider.Memory.Effects);
    }

    [Fact]
    public void OnlyAnExplicitOperatorReconciliationAllowsRetryOfAnUnconfirmedEffect()
    {
        using var f = new Fixture();
        f.Grant();
        f.SeedPull();
        f.Provider.Uncertain = true;
        ChangeResponse first = f.Service.ChangesMerge(f.Merge);
        Assert.Equal("uncertain", first.Status);
        const string reason = "The operator established that the request had no effect at the host.";
        Assert.False(f.Service.ChangesObserve(new(first.Operation!.Id, f.Estate.BuilderId, reason)).Ok);
        Assert.Equal("uncertain", EstateModel.BodyString(f.Current("change-merge"), "status"));
        f.Provider.BeforeRead = () => Assert.Fail("An operator reconciliation calls no provider.");
        ChangeResponse canceled = f.Service.ChangesObserve(new(first.Operation.Id, f.Estate.OperatorId, reason));
        Assert.True(canceled.Ok, canceled.Message);
        Assert.Equal("canceled", canceled.Status);
        Assert.Equal(first.Identity, canceled.Identity);
        Assert.Equal(reason, EstateModel.BodyString(f.Current("change-merge"), "reconciliation"));
        Assert.False(f.Service.ChangesMerge(f.Merge).Ok);
        f.Provider.BeforeRead = null;
        f.Provider.Uncertain = false;
        f.Provider.Memory.Set("Example/widget", new("1", "work/change", "main", Head, "open", null, []));
        Assert.Equal("known-failure", f.Service.ChangesMerge(f.Merge with { Reconciliation = reason }).Status);
        Assert.Equal(0, f.Provider.Memory.Effects);
        f.SeedPull();
        Assert.True(f.Service.ChangesMerge(f.Merge with { Reconciliation = reason }).Ok);
        Assert.Equal(1, f.Provider.Memory.Effects);
        Assert.False(f.Service.ChangesObserve(new(first.Operation.Id, f.Estate.OperatorId, reason)).Ok);
        Assert.Equal("observed-success", EstateModel.BodyString(f.Current("change-merge"), "status"));
        Assert.Equal(0, f.Service.Validate().Errors);
    }

    internal static IReadOnlyList<ChangeCheck> Checks(string head) => Required.Select(n => new ChangeCheck(n, head, "completed", "success")).ToArray();

    internal sealed class Fixture : IDisposable
    {
        internal TestEstate Estate { get; } = new(recordFormat: 3);
        internal ControlledProvider Provider { get; } = new();
        internal TestPushAdapter Adapter { get; } = new();
        internal TestScanner Scanner { get; } = new();
        internal EstateService Service { get; private set; }
        internal string Product { get; }
        internal ChangesPushRequest Push => new(Product, "work/change", Head, Estate.OperatorId);
        internal ChangesOpenRequest Open => new(Product, "work/change", Head, "A checked change", "Review this change.", Estate.OperatorId);
        internal ChangesMergeRequest Merge => new(Product, "1", Head, Estate.OperatorId);

        internal Fixture(IReadOnlyList<string>? required = null)
        {
            RecordEnvelope repository = Estate.Add("resource", Estate.OperatorId, """{"kind":"repository","name":"Widget","role":"Source","location":{"github":"Example/widget"},"visibility":"private"}""");
            Product = Estate.Add("product", Estate.OperatorId, $$"""
                {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],
                 "changes":{"provider":"memory-pull-requests","repository":"{{repository.Id}}","target":"main","requiredChecks":{{JsonSerializer.Serialize(required ?? Required)}} } }
                """).Id;
            RepositoryClones.Register(Estate.Estate.Id, repository.Id, Estate.Root);
            Service = OpenService();
            Provider.Memory.Checks = Checks(Head);
            Provider.Memory.SetTarget("Example/widget", new("main", Merged, Checks(Merged)));
        }

        internal RecordEnvelope Grant()
        {
            RecordEnvelope decision = Estate.Add("decision", Estate.OperatorId, $$"""{"title":"Repository access","statement":"Grant repository writes.","summary":"Test grant.","synthesis":"A bounded grant.","effects":["Grant checked repository writes."],"affects":[],"decidedBy":"{{Estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":["publication"]}""");
            return Estate.Add("authority", Estate.OperatorId, $$"""{"grantee":"{{Estate.BuilderId}}","scope":["repository-write"],"reserved":["publication"],"conditions":[],"basis":"{{decision.Id}}"}""");
        }
        internal void SeedPull() => Provider.Memory.Set("Example/widget", new("1", "work/change", "main", Head, "open", null, Checks(Head)));
        internal RecordEnvelope Current(string action) => Service.Session.Load().Current("operation").Last(o => EstateModel.BodyString(o, "action") == action);
        internal void IntentExists(string action) => Assert.Contains(Service.Session.Load().OfType("operation"), o => o.Supersedes is null && EstateModel.BodyString(o, "action") == action && EstateModel.BodyString(o, "status") == "not-started");
        internal void Reopen() { Service.Dispose(); Service = OpenService(); }
        private EstateService OpenService()
        {
            var service = EstateService.Open(Estate.Root);
            service.Providers = new() { PushAdapter = Adapter, SecretScanner = Scanner };
            service.Providers.Register(Provider);
            return service;
        }
        public void Dispose() { Service.Dispose(); Estate.Dispose(); }
    }

    internal sealed class TestScanner : ISecretScanner
    {
        internal Action? BeforeScan { get; set; }
        internal SecretScan? Result { get; set; }
        public SecretScan Scan(string directory, PushPlan plan) { BeforeScan?.Invoke(); return Result ?? new("scanner", "1.2.3", plan.Range, "passed", 0); }
    }

    internal sealed class TestPushAdapter : IPushAdapter
    {
        internal Action? BeforePrepare { get; set; }
        internal Action? BeforePush { get; set; }
        internal bool Uncertain { get; set; }
        internal int Preparations { get; private set; }
        internal int Pushes { get; private set; }
        internal string? RemoteHead { get; set; }
        public PushPlan Prepare(string directory, string remote, string branch, string target, string head) { BeforePrepare?.Invoke(); Preparations++; return new(branch, target, head, Basis, null, Basis); }
        public void Push(string directory, string remote, PushPlan plan, SecretScan scan)
        {
            BeforePush?.Invoke(); Pushes++;
            Assert.True(scan.Passed(plan));
            if (Uncertain) throw new ProviderUncertainException("Unconfirmed request.");
            RemoteHead = plan.Head;
        }
        public string? Observe(string directory, string remote, string branch) => RemoteHead;
    }

    internal sealed class ControlledProvider : IPullRequestProvider
    {
        internal MemoryPullRequestProvider Memory { get; } = new();
        internal Action<string>? BeforeEffect { get; set; }
        internal Action? AfterEffect { get; set; }
        internal Action? BeforeRead { get; set; }
        internal bool Uncertain { get; set; }
        internal bool ReadUnavailable { get; set; }
        internal int Calls { get; private set; }
        internal string? MergedHead { get; private set; }
        internal string? ObservedTarget { get; private set; }
        public string Provider => Memory.Provider;
        public ProviderDescriptor Descriptor => Memory.Descriptor;
        public string PushRemote(string destination) => Memory.PushRemote(destination);
        public ChangeReceipt Open(string destination, string branch, string target, string head, string title, string body)
        { Effect("change-open"); return Memory.Open(destination, branch, target, head, title, body); }
        public PullRequestObservation? Observe(string destination, string numberOrBranch)
        { Read(); return Memory.Observe(destination, numberOrBranch); }
        public ChangeReceipt Merge(string destination, string number, string head)
        { Effect("change-merge"); MergedHead = head; ChangeReceipt result = Memory.Merge(destination, number, head); AfterEffect?.Invoke(); return result; }
        public TargetObservation ObserveTarget(string destination, string branch, string commit)
        { Read(); ObservedTarget = commit; return Memory.ObserveTarget(destination, branch, commit); }
        private void Effect(string action) { BeforeEffect?.Invoke(action); Calls++; if (Uncertain) throw new ProviderUncertainException("Unconfirmed request."); }
        private void Read() { BeforeRead?.Invoke(); if (ReadUnavailable) throw new ProviderRefusedException("Observation unavailable."); }
    }
}
