// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Release;

namespace Vericairn.Runtime.Tests;

public sealed class ReleasePlanningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingSeveralOrdersKeepsTheirAssignmentsAttemptsAndReviewsCurrent(bool newVersion)
    {
        using var f = new ReleasePlanningFixture();
        var retained = new List<RecordEnvelope>();
        foreach (RecordEnvelope order in new[] { f.First, f.Second })
        {
            RecordEnvelope attempt = f.Records.BeginAttempt(order: order);
            retained.Add(f.Model.ById[attempt.Subject!.Id]);
            retained.Add(attempt);
            RecordEnvelope result = f.Records.Add("result", PolicyFixture.ResultBody(), builder: true, subject: attempt.Reference);
            retained.Add(result);
            retained.Add(f.Records.Review(result));
            retained.Add(f.Records.Add("review", new() { ["independence"] = "self", ["outcome"] = "pass", ["summary"] = "Reviewed the planned work.", ["findings"] = new JsonArray() }, subject: order.Reference));
        }
        RecordEnvelope carrierReview = f.Records.Add("review", new() { ["independence"] = "self", ["outcome"] = "pass", ["summary"] = "Reviewed the release plan.", ["findings"] = new JsonArray() }, subject: f.Source.Reference);
        int before = f.Model.Records.Count;
        WorkMovePlanRequest request = newVersion ? f.Move with { To = null, ExpectedTarget = null, Version = "1.2.0" } : f.Move;
        WorkMovePlanResponse moved = f.Api.WorkMovePlan("PLAN-1", request);
        Assert.True(moved.Ok, JsonSerializer.Serialize(moved, ProtocolJson.Options));
        Assert.Equal(newVersion ? 3 : 2, moved.Written.Count);
        EstateModel model = f.Model;
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(before + moved.Written.Count, model.Records.Count);
        foreach (RecordEnvelope record in retained)
        {
            Assert.True(model.IsCurrent(record));
            Assert.Equal(record.ComputedHash, model.ById[record.Id].ComputedHash);
            Assert.Equal(record.Subject, model.ById[record.Id].Subject);
            Assert.DoesNotContain(model.Findings, finding => finding.Code == "record.subject-stale" && finding.Location == record.ExpectedPath);
        }
        foreach (RecordEnvelope order in new[] { f.First, f.Second })
        {
            RecordEnvelope current = model.Latest(order);
            Assert.NotEqual(order.Id, current.Id);
            Assert.Contains($"Pinned to work-order revision {order.ComputedHash} (current)", WorkView.RenderBrief(model, current), StringComparison.Ordinal);
            Assert.DoesNotContain("[pinned to an earlier work-order revision]", WorkView.RenderObserve(model, current), StringComparison.Ordinal);
        }
        // Moving to a new version also changes the carrier's releasePlan, which remains a content change.
        Assert.Equal(newVersion, model.Findings.Any(finding => finding.Code == "record.subject-stale" && finding.Location == carrierReview.ExpectedPath));
        Assert.Equal(newVersion ? 1 : 0, model.Findings.Count(finding => finding.Code == "record.subject-stale"));
    }

    [Fact]
    public void AVersionMoveCombinesASelfPlacedCarriersChangesIntoOneSuccessor()
    {
        using var f = new ReleasePlanningFixture();
        JsonObject body = PolicyFixture.Body(f.Source);
        body["placement"] = JsonNode.Parse(f.First.Body.GetProperty("placement").GetRawText());
        RecordEnvelope source = f.Records.Add("work-order", body, supersedes: f.Source.Id);
        var hashes = new Dictionary<string, string>(f.Move.ExpectedWork, StringComparer.Ordinal) { [source.Id] = source.ComputedHash };
        WorkMovePlanResponse result = f.Api.WorkMovePlan("PLAN-1", new(f.Operator, source.ComputedHash, hashes, Version: "1.2.0"));
        Assert.True(result.Ok, JsonSerializer.Serialize(result, ProtocolJson.Options));
        Assert.Equal(3, result.Written.Count);
        RecordEnvelope successor = f.Model.Latest(source);
        Assert.Equal(source.Id, successor.Supersedes);
        Assert.Equal("1.2.0", successor.Body.GetProperty("releasePlan").GetProperty("version").GetString());
        Assert.Equal("1.2.0", successor.Body.GetProperty("placement").GetProperty("version").GetString());
        Assert.Equal(f.Source.Id, successor.Body.GetProperty("placement").GetProperty("carrier").GetString());
        Assert.False(f.Model.Findings.HasErrors, string.Join("\n", f.Model.Findings));
    }

    [Fact]
    public void MovingAPlanRequiresTheSameHeldSignerAsIndividualPlacement()
    {
        using var f = new ReleasePlanningFixture();
        RecordEnvelope principal = f.Records.Add("principal", new() { ["kind"] = "human", ["name"] = "Planner without a held device", ["roles"] = new JsonArray() });
        int before = f.Model.Records.Count;
        WorkMovePlanResponse batch = f.Api.WorkMovePlan("PLAN-1", f.Move with { By = principal.Id });
        WriteOutcome single = f.Api.WorkPlace("WORK-1", new(principal.Id, f.First.ComputedHash, f.Target.ComputedHash, f.Target.Id));
        JsonObject body = PolicyFixture.Body(f.First); body["placement"]!["carrier"] = f.Target.Id; body["placement"]!["version"] = "1.1.0";
        WriteOutcome manual = f.Api.NewRecord(new("work-order", principal.Id, PolicyFixture.Element(body), Supersedes: f.First.Id, Expected: f.First.ComputedHash));
        Assert.Equal(single.Problem, batch.Problem);
        Assert.Equal(manual.Problem, batch.Problem);
        Assert.Empty(batch.Written);
        Assert.Equal(before, f.Model.Records.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStorageInterruptionReportsItsReceiptsAndCannotBeRetriedAsSuccess(bool withdrawal)
    {
        using var f = new ReleasePlanningFixture();
        int count = 0;
        f.Api.BeforeStoringTogether = _ => { if (++count == 2) throw new IOException("Fixture storage interrupted."); };
        int before = f.Model.Records.Count;
        if (withdrawal)
        {
            ReleaseResponse result = f.Api.ReleaseWithdraw(f.Withdraw);
            Assert.False(result.Ok);
            Assert.NotNull(result.Problem);
            Assert.NotNull(result.Release);
            Assert.Null(result.Operation);
            Assert.Equal("withdrawn", result.Status);
            Assert.Equal("conflict", f.Api.ReleaseWithdraw(f.Withdraw).Problem!.Code);
        }
        else
        {
            WorkMovePlanResponse result = f.Api.WorkMovePlan("PLAN-1", f.Move);
            Assert.False(result.Ok);
            Assert.Single(result.Written);
            Assert.Equal("conflict", f.Api.WorkMovePlan("PLAN-1", f.Move).Problem!.Code);
        }
        Assert.Equal(before + 1, f.Model.Records.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovesEveryPlannedMemberAndPreservesOtherWork(bool newVersion)
    {
        using var f = new ReleasePlanningFixture();
        WorkMovePlanRequest request = newVersion ? f.Move with { To = null, ExpectedTarget = null, Version = "1.2.0" } : f.Move;
        int before = f.Model.Records.Count;
        WorkMovePlanResponse moved = f.Api.WorkMovePlan("PLAN-1", request);
        Assert.True(moved.Ok, JsonSerializer.Serialize(moved, ProtocolJson.Options));
        Assert.Equal(newVersion ? 3 : 2, moved.Written.Count);
        Assert.Equal(before + moved.Written.Count, f.Model.Records.Count);
        foreach (RecordEnvelope original in new[] { f.First, f.Second })
        {
            RecordEnvelope current = f.Model.Latest(original);
            JsonElement placement = current.Body.GetProperty("placement");
            Assert.Equal(newVersion ? f.Source.Id : f.Target.Id, placement.GetProperty("carrier").GetString());
            Assert.Equal(newVersion ? "1.2.0" : "1.1.0", placement.GetProperty("version").GetString());
            Assert.Equal(f.Product.Id, placement.GetProperty("product").GetString());
            JsonObject unchanged = PolicyFixture.Body(current); unchanged["placement"] = JsonNode.Parse(original.Body.GetProperty("placement").GetRawText());
            Assert.True(JsonNode.DeepEquals(PolicyFixture.Body(original), unchanged));
            Assert.Equal(original.Id, current.Supersedes);
            Assert.True(SignatureChecks.IsAuthentic(f.Model, current));
        }
        Assert.Equal(f.Active.Id, f.Model.Latest(f.Active).Id);
        Assert.Equal(f.Target.Id, f.Model.Latest(f.Target).Id);
        Assert.Equal(newVersion ? "1.2.0" : "1.0.0", f.Model.Latest(f.Source).Body.GetProperty("releasePlan").GetProperty("version").GetString());
        Assert.False(f.Model.Findings.HasErrors, string.Join("\n", f.Model.Findings));
    }

    [Theory]
    [InlineData("moved")]
    [InlineData("revised")]
    [InlineData("added")]
    [InlineData("active")]
    [InlineData("accepted")]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("omitted")]
    [InlineData("id-case")]
    public void APartialConflictOrChangedMembershipWritesNothing(string change)
    {
        using var f = new ReleasePlanningFixture();
        WorkMovePlanRequest prepared = f.Move;
        if (change == "moved") Assert.True(f.Api.WorkPlace("WORK-2", new(f.Operator, f.Second.ComputedHash, f.Target.ComputedHash, f.Target.Id)).Ok);
        if (change is "revised" or "source" or "target")
        {
            RecordEnvelope original = change == "source" ? f.Source : change == "target" ? f.Target : f.Second;
            JsonObject body = PolicyFixture.Body(original); body["title"] = "A concurrently revised title";
            f.Records.Add("work-order", body, supersedes: original.Id);
        }
        if (change == "added") f.Member("WORK-4");
        if (change == "active") f.Records.Add("work-status", new() { ["status"] = "active", ["note"] = "Started after preparation." }, subject: f.Second.Reference);
        if (change == "accepted") f.Records.Add("acceptance", new() { ["statement"] = "Accept this work.", ["basis"] = new JsonArray(), ["self"] = true }, subject: f.Second.Reference);
        if (change == "omitted") prepared = prepared with { ExpectedWork = new Dictionary<string, string> { [f.First.Id] = f.First.ComputedHash } };
        if (change == "id-case") prepared = prepared with { ExpectedWork = prepared.ExpectedWork.ToDictionary(pair => pair.Key.ToLowerInvariant(), pair => pair.Value, StringComparer.OrdinalIgnoreCase) };
        f.Records.AssertValid();
        string[] before = f.Model.Records.Select(r => r.ComputedHash).Order(StringComparer.Ordinal).ToArray();
        WorkMovePlanResponse refused = f.Api.WorkMovePlan(f.Source.Id, prepared);
        Assert.Equal("conflict", refused.Problem!.Code);
        Assert.Empty(refused.Written);
        Assert.Equal(before, f.Model.Records.Select(r => r.ComputedHash).Order(StringComparer.Ordinal));
        Assert.Equal(f.First.Id, f.Model.Latest(f.First).Id);
    }

    [Theory]
    [InlineData("retired")]
    [InlineData("withdrawn")]
    [InlineData("published")]
    public void PlacementRefusalsMatchIndividualPlacementRecords(string ended)
    {
        using var f = new ReleasePlanningFixture();
        if (ended == "retired") f.Records.Add("work-status", new() { ["status"] = "retired", ["note"] = "Target retired." }, subject: f.Target.Reference);
        else
        {
            JsonObject release = PolicyFixture.Body(f.Candidate); release["version"] = "1.1.0";
            RecordEnvelope candidate = f.Records.Add("release", release, subject: f.Product.Reference);
            if (ended == "published")
            {
                f.Records.Add("acceptance", new() { ["statement"] = "Accept this candidate.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
                release["publishedAt"] = RecordParser.FormatTimestamp(f.Records.Estate.Clock); release["url"] = "https://example.invalid/releases/fixture";
            }
            release["status"] = ended;
            f.Records.Add("release", release, subject: candidate.Subject, supersedes: candidate.Id);
        }
        int before = f.Model.Records.Count;
        WorkMovePlanResponse batch = f.Api.WorkMovePlan(f.Source.Id, f.Move);
        WriteOutcome single = f.Api.WorkPlace("WORK-1", new(f.Operator, f.First.ComputedHash, f.Target.ComputedHash, f.Target.Id));
        Assert.Equal("rejected", batch.Problem!.Code);
        Assert.Equal(single.Problem!.Code, batch.Problem.Code);
        Assert.Equal(single.Problem.Findings.Select(x => (x.Code, x.Message)).Distinct(), batch.Problem.Findings.Select(x => (x.Code, x.Message)).Distinct());
        Assert.Empty(batch.Written);
        Assert.Equal(before, f.Model.Records.Count);
    }

    [Fact]
    public void WithdrawsTheExactCandidateAndRecordsIntentWithoutCallingAProvider()
    {
        using var f = new ReleasePlanningFixture();
        var provider = new ObservingProvider();
        var registry = new ProviderRegistry(); registry.Register(provider); f.Api.Providers = registry;
        int before = f.Model.Records.Count;
        ReleaseResponse result = f.Api.ReleaseWithdraw(f.Withdraw);
        Assert.True(result.Ok, JsonSerializer.Serialize(result, ProtocolJson.Options));
        Assert.Equal(before + 2, f.Model.Records.Count);
        Assert.Equal("withdrawn", result.Status);
        Assert.Equal(0, provider.Calls);
        RecordEnvelope withdrawn = f.Model.ById[result.Release!.Id];
        RecordEnvelope intent = f.Model.ById[result.Operation!.Id];
        Assert.Equal(f.Candidate.Id, withdrawn.Supersedes);
        Assert.Equal("withdrawn", withdrawn.Body.GetProperty("status").GetString());
        Assert.Equal(f.Candidate.Body.GetProperty("artifacts").GetRawText(), withdrawn.Body.GetProperty("artifacts").GetRawText());
        Assert.Equal(f.Candidate.ComputedHash, withdrawn.Body.GetProperty("evidence")[0].GetProperty("hash").GetString());
        Assert.Equal(withdrawn.Reference, intent.Subject);
        Assert.Equal(ReleaseFlow.DeleteDraftAction, intent.Body.GetProperty("action").GetString());
        Assert.Equal("fixture-releases:Example/widget:v1.0.0:delete-draft", intent.Body.GetProperty("identity").GetString());
        Assert.Equal("not-started", intent.Body.GetProperty("status").GetString());
        Assert.False(intent.Body.TryGetProperty("dispatchedAt", out _));
        Assert.All(new[] { withdrawn, intent }, r => Assert.True(SignatureChecks.IsAuthentic(f.Model, r)));
        Assert.Equal("conflict", f.Api.ReleaseWithdraw(f.Withdraw).Problem!.Code);
        Assert.Equal("refused", f.Api.ReleaseWithdraw(f.Withdraw with { Expected = withdrawn.ComputedHash }).Problem!.Code);
        Assert.Equal(before + 2, f.Model.Records.Count);
    }

    [Theory]
    [InlineData("artifacts")]
    [InlineData("author")]
    [InlineData("provider")]
    [InlineData("duplicate")]
    public void WithdrawalRefusalsMatchGenericAuthoringAndStoreNeitherRecord(string fault)
    {
        using var f = new ReleasePlanningFixture();
        RecordEnvelope candidate = f.Candidate;
        if (fault == "artifacts")
        {
            JsonObject body = PolicyFixture.Body(candidate); body["artifacts"] = new JsonArray();
            candidate = f.Records.Add("release", body, subject: candidate.Subject, supersedes: candidate.Id);
        }
        ReleaseWithdrawRequest request = f.Withdraw with { Expected = candidate.ComputedHash, By = fault == "author" ? f.Records.Estate.BuilderId : f.Operator, Provider = fault == "provider" ? "" : "fixture-releases" };
        if (fault == "duplicate")
            f.Records.Add("operation", new()
            {
                ["provider"] = request.Provider, ["action"] = ReleaseFlow.DeleteDraftAction, ["identity"] = "fixture-releases:Example/widget:v1.0.0:delete-draft",
                ["intent"] = "Delete the draft manually.", ["target"] = new JsonObject { ["repository"] = "Example/widget" }, ["status"] = "not-started", ["evidence"] = new JsonArray(),
            });
        int before = f.Model.Records.Count;
        ReleaseResponse refused = f.Api.ReleaseWithdraw(request);
        Assert.False(refused.Ok);
        Assert.Null(refused.Release);
        Assert.Null(refused.Operation);
        string code = fault switch { "artifacts" => "release.artifacts", "author" => "release.author", "provider" => "body.text", _ => "operation.duplicate-identity" };
        if (fault == "provider") Assert.Contains(refused.Problem!.Findings, x => x.Location.EndsWith(".provider", StringComparison.Ordinal) || x.Message.Contains("provider", StringComparison.Ordinal));
        else Assert.Contains(refused.Problem!.Findings, x => x.Code == code);
        if (fault is "artifacts" or "author")
        {
            JsonObject body = PolicyFixture.Body(candidate); body["status"] = "withdrawn";
            WriteOutcome manual = f.Api.NewRecord(new("release", request.By, PolicyFixture.Element(body), $"product:{f.Product.Id}", candidate.Id, Expected: candidate.ComputedHash));
            Assert.False(manual.Ok);
            Assert.Contains(manual.Problem!.Findings, x => x.Code == code);
        }
        Assert.Equal(before, f.Model.Records.Count);
    }

    [Fact]
    public void BuildObservationKeepsAWithdrawalEvenWhenTheProviderShowsPublication()
    {
        using var f = new ReleasePlanningFixture();
        var provider = new FakeReleaseProvider("fixture-releases");
        var registry = new ProviderRegistry(); registry.Register(provider); f.Api.Providers = registry;
        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["repository"] = "Example/widget" };
        JsonObject intentBody = ReleaseFlow.IntentBody(provider.Provider, "Example/widget", settings, "1.0.0", f.Product.Id);
        RecordEnvelope intent = f.Records.Add("operation", intentBody, subject: f.Product.Reference);
        DispatchReceipt receipt = provider.Dispatch(new("Example/widget", settings, new Dictionary<string, string>()));
        f.Records.Estate.SetClock(f.Model.Records.Max(r => r.AtTime).AddSeconds(1));
        receipt = receipt with { At = RecordParser.FormatTimestamp(f.Records.Estate.Clock) };
        RecordEnvelope dispatch = f.Records.Add("operation", ReleaseFlow.AfterDispatch(intent.Body, receipt), subject: intent.Subject, supersedes: intent.Id);
        provider.CompleteRun("Example/widget", receipt.RunId!, true, "1.0.0", ["widget.zip"]);
        JsonObject settled = PolicyFixture.Body(dispatch); settled["status"] = "observed-success";
        settled["observed"] = new JsonObject { ["summary"] = "The draft was built.", ["at"] = RecordParser.FormatTimestamp(f.Records.Estate.Clock) };
        f.Records.Add("operation", settled, subject: dispatch.Subject, supersedes: dispatch.Id);
        Assert.True(f.Api.ReleaseWithdraw(f.Withdraw).Ok);
        provider.Publish("Example/widget", "v1.0.0");
        int before = f.Model.Records.Count;
        ReleaseResponse observed = f.Api.ReleaseObserve(new("1.0.0", null, f.Operator, Product: f.Product.Id));
        Assert.True(observed.Ok, JsonSerializer.Serialize(observed, ProtocolJson.Options));
        Assert.Equal("withdrawn", observed.Status);
        Assert.Null(observed.Publish);
        Assert.Null(observed.Release);
        Assert.Equal(before, f.Model.Records.Count);
        Assert.Equal("withdrawn", EstateModel.BodyString(f.Model.Latest(f.Candidate), "status"));
    }

    [Fact]
    public void DeletionObservationRequiresOperatorDispatchAndRecordsOnlyObservedAbsence()
    {
        using var f = new ReleasePlanningFixture();
        var provider = new ObservingProvider();
        var registry = new ProviderRegistry(); registry.Register(provider); f.Api.Providers = registry;
        ReleaseResponse withdrawn = f.Api.ReleaseWithdraw(f.Withdraw);
        Assert.True(withdrawn.Ok);
        var request = new ReleaseObserveRequest("1.0.0", null, f.Operator) { Withdrawal = withdrawn.Operation!.Id };
        Assert.Equal("refused", f.Api.ReleaseObserve(request).Problem!.Code);
        Assert.Equal(0, provider.Calls);
        RecordEnvelope intent = f.Model.ById[withdrawn.Operation.Id];
        JsonObject dispatched = PolicyFixture.Body(intent); dispatched["status"] = "dispatched";
        f.Records.Estate.SetClock(f.Model.Records.Max(r => r.AtTime).AddSeconds(1));
        dispatched["dispatchedAt"] = RecordParser.FormatTimestamp(f.Records.Estate.Clock);
        f.Records.Add("operation", dispatched, subject: intent.Subject, supersedes: intent.Id);
        int before = f.Model.Records.Count;
        Assert.False(f.Api.ReleaseObserve(request).Ok);
        Assert.Equal(before, f.Model.Records.Count);
        provider.Absent = true;
        ReleaseResponse observed = f.Api.ReleaseObserve(request);
        Assert.True(observed.Ok, JsonSerializer.Serialize(observed, ProtocolJson.Options));
        Assert.Equal("observed-success", observed.Status);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(before + 1, f.Model.Records.Count);
        Assert.True(f.Api.ReleaseObserve(request).Ok);
        Assert.Equal(2, provider.Calls);
        Assert.Equal("withdrawn", EstateModel.BodyString(f.Model.Latest(f.Candidate), "status"));
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("provider-failure")]
    [InlineData("published")]
    [InlineData("changed")]
    public void DeletionObservationDoesNotSettleAnUnavailablePresentOrChangedIntent(string condition)
    {
        using var f = new ReleasePlanningFixture();
        var provider = new ObservingProvider { Absent = condition != "published", Published = condition == "published" };
        var registry = new ProviderRegistry();
        if (condition != "unavailable") registry.Register(provider);
        f.Api.Providers = registry;
        ReleaseResponse withdrawn = f.Api.ReleaseWithdraw(f.Withdraw);
        Assert.True(withdrawn.Ok);
        RecordEnvelope intent = f.Model.ById[withdrawn.Operation!.Id];
        JsonObject body = PolicyFixture.Body(intent); body["status"] = "dispatched";
        f.Records.Estate.SetClock(f.Model.Records.Max(r => r.AtTime).AddSeconds(1));
        body["dispatchedAt"] = RecordParser.FormatTimestamp(f.Records.Estate.Clock);
        RecordEnvelope dispatched = f.Records.Add("operation", body, subject: intent.Subject, supersedes: intent.Id);
        provider.BeforeObservation = () =>
        {
            if (condition == "provider-failure") throw new ProviderUncertainException("The fixture could not observe the destination.");
            if (condition == "changed")
            {
                JsonObject concurrent = PolicyFixture.Body(dispatched); concurrent["status"] = "uncertain";
                concurrent["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = "Concurrent operator reconciliation." });
                f.Records.Add("operation", concurrent, subject: intent.Subject, supersedes: dispatched.Id);
            }
        };
        int before = f.Model.Records.Count;
        ReleaseResponse observed = f.Api.ReleaseObserve(new("1.0.0", null, f.Operator) { Withdrawal = intent.Id });
        Assert.False(observed.Ok);
        Assert.Null(observed.Operation);
        Assert.Null(observed.Release);
        Assert.Equal(condition switch { "unavailable" => "refused", "provider-failure" => "provider", "changed" => "conflict", _ => null }, observed.Problem?.Code);
        Assert.Equal(before + (condition == "changed" ? 1 : 0), f.Model.Records.Count);
        Assert.Equal(condition == "changed" ? "uncertain" : "dispatched", EstateModel.BodyString(f.Model.Latest(intent), "status"));
        Assert.Equal(condition == "unavailable" ? 0 : 1, provider.Calls);
    }

    private sealed class ObservingProvider : IReleaseProvider
    {
        public int Calls { get; private set; }
        public bool Absent { get; set; }
        public bool Published { get; set; }
        public Action? BeforeObservation { get; set; }
        public string Provider => "fixture-releases";
        public ProviderDescriptor Descriptor => new FakeReleaseProvider(Provider).Descriptor;
        public DispatchReceipt Dispatch(DispatchRequest request) => throw new InvalidOperationException("Withdrawal never dispatches.");
        public RunObservation? ObserveRun(string destination, string runId) => throw new InvalidOperationException("Deletion observes no build.");
        public string ManualPublicationCommand(string destination, string tag) => throw new InvalidOperationException("Withdrawal never publishes.");
        public ReleaseObservation? ObserveRelease(string destination, string tag, IReadOnlyDictionary<string, string> settings)
        {
            Calls++;
            Assert.Equal("Example/widget", destination); Assert.Equal("v1.0.0", tag);
            BeforeObservation?.Invoke();
            return Absent ? null : new(tag, !Published, null, new string('a', 40), null, [], RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));
        }
    }
}
