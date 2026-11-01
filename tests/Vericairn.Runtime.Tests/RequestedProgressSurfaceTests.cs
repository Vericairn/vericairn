// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class RequestedProgressSurfaceTests
{
    [Fact]
    public void MissingAndStaleClaimsBecomeOverdueAtTheCadenceAndTheNextClaimClearsAttention()
    {
        using var fixture = new ProgressFixture();
        fixture.Set("brief", """{"statusText":true,"cadence":{"minutes":10}}""");
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        var clock = new WorkerClock(fixture.Attempt.AtTime);
        service.WorkerOptions = WorkerOptions.Default with { Clock = clock };
        int records = service.Session.Load().Records.Count;
        byte[] canonical = CanonicalJson.SerializePrettyUtf8(service.Status().Json);
        Assert.Equal("pending", Read().Freshness);
        clock.Advance(TimeSpan.FromSeconds(599));
        Assert.False(Read().Overdue);
        Assert.Empty(Overdue(service));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("overdue", Read().Freshness);
        Assert.True(Read().Overdue);
        CommunicationAttention attention = Assert.Single(Overdue(service));
        Assert.Equal(fixture.Attempt.Id, attention.Record);
        Assert.Equal(fixture.Attempt.ComputedHash, attention.Hash);
        Assert.Contains(fixture.Estate.BuilderId, attention.Text, StringComparison.Ordinal);
        Assert.Contains("600 seconds", attention.Text, StringComparison.Ordinal);
        Assert.Contains("never stops a run", attention.Text, StringComparison.Ordinal);
        Assert.Contains("progress-overdue", CommunicationPresentation.RenderAttention(Overdue(service)), StringComparison.Ordinal);

        WorkProgressRequest request = RequestedProgressTests.Request(fixture, """{"type":"accurate","done":7,"total":20,"unit":"files","estimatePercent":60}""");
        Assert.True(service.WorkProgress(request).Ok);
        Assert.Equal("fresh", Read().Freshness);
        Assert.Empty(Overdue(service));
        foreach (string frequency in CommunicationResolution.Frequencies)
        {
            CommunicationProfile profile = CommunicationResolution.Resolve(null, null, [], new CommunicationValues(Progress: frequency));
            string status = CommunicationPresentation.Status(service.Status(), profile);
            foreach (string expected in new[] { "Agent's progress claim", "accurate: 7 of 20 files", "best-effort estimate 60%", request.StatusText!, "fresh", "Worker liveness" })
            {
                Assert.Contains(expected, status, StringComparison.Ordinal);
                Assert.Contains(expected, service.Progress().Text, StringComparison.Ordinal);
                Assert.Contains(expected, service.Observe("W-1")!.Text, StringComparison.Ordinal);
            }
        }
        Assert.Equal(canonical, CanonicalJson.SerializePrettyUtf8(service.Status().Json));
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal("stale", Read().Freshness);
        Assert.Single(Overdue(service));
        Assert.True(service.WorkProgress(request with { StatusText = "Read another file." }).Ok);
        Assert.Equal("fresh", Read().Freshness);
        Assert.Empty(Overdue(service));
        Assert.Equal(records, service.Session.Load().Records.Count);
        Assert.Empty(service.Records("operation"));
        Assert.Empty(service.Records("ask"));

        RequestedProgress Read() => service.WorkProgressGet(fixture.Attempt.Id)!;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LiveButOverdueAndStalledButFreshRemainIndependentAndReadsNeverStopTheRun(bool overdue)
    {
        using var fixture = new ProgressFixture();
        fixture.Set("brief", """{"cadence":{"minutes":10}}""");
        fixture.Estate.Add("goal-status", fixture.Estate.OperatorId, """{"status":"active","note":"Exercise reporting."}""", fixture.Goal.Reference);
        RecordEnvelope intent = fixture.Estate.Add("operation", fixture.Estate.OperatorId, $$"""{"provider":"generic","action":"agent-run","identity":"progress-test","intent":"Observe reporting.","target":{"assignment":"{{fixture.Assignment.Id}}","principal":"{{fixture.Estate.BuilderId}}","workspace":"widget, branch work/progress","goal":"{{fixture.Goal.Id}}"},"status":"not-started","cap":{"wallMinutes":30,"attemptsPerStep":3},"evidence":[]}""");
        JsonObject dispatched = JsonNode.Parse(intent.Body.GetRawText())!.AsObject();
        dispatched["status"] = "dispatched";
        dispatched["dispatchedAt"] = intent.At;
        fixture.Estate.Add("operation", fixture.Estate.OperatorId, dispatched.ToJsonString(), supersedes: intent.Id);
        RecordEnvelope attempt = fixture.Estate.Add("attempt", fixture.Estate.BuilderId, fixture.Attempt.Body.GetRawText(), fixture.Assignment.Reference);
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        var clock = new WorkerClock(attempt.AtTime.AddMinutes(11));
        service.WorkerOptions = WorkerOptions.Default with { Clock = clock };
        string now = RecordParser.FormatTimestamp(clock.GetUtcNow());
        var directory = new RunDirectory(service.Session.Manifest.Estate, intent.Id);
        directory.Create();
        var liveness = new RunLiveness(60, overdue ? now : intent.At, AttentionAt: overdue ? null : now);
        directory.Write(new RunObservedState { Run = intent.Id, Heartbeat = now, Liveness = liveness });
        if (!overdue)
            Assert.True(service.WorkProgress(new WorkProgressRequest(attempt.Id, fixture.Estate.BuilderId, "review", "Working.")
                { Progress = RunEstate.Json("""{"type":"not-measurable"}"""), StatusText = "Reading the widget." }).Ok);
        int records = service.Session.Load().Records.Count;
        RequestedProgress progress = service.WorkProgressGet(intent.Id)!;
        Assert.Equal(attempt.Id, progress.Attempt);
        Assert.Equal(overdue, progress.Overdue);
        Assert.Equal(overdue ? "overdue" : "fresh", progress.Freshness);
        Assert.Equal(liveness, progress.Liveness);
        Assert.True(progress.WorkerObserved);
        RunRow run = Assert.Single(service.Runs().Runs);
        Assert.Equal(liveness, run.Liveness);
        Assert.Equal(JsonSerializer.Serialize(progress, ProtocolJson.Options), JsonSerializer.Serialize(run.RequestedProgress, ProtocolJson.Options));
        Assert.Equal(overdue, Overdue(service).Length == 1);
        Assert.Equal("dispatched", run.Status);
        Assert.Null(run.Stop);
        Assert.Null(directory.ReadStop());
        Assert.Equal(records, service.Session.Load().Records.Count);
        Assert.Equal(0, service.Validate().Errors);
    }

    [Fact]
    public void OverdueReportingLeavesASupervisedProcessRunningUntilAnExplicitStop()
    {
        JsonObject brief = JsonNode.Parse(RunEstate.Brief)!.AsObject();
        brief["progressRequest"] = JsonNode.Parse("""{"cadence":{"minutes":1},"statusText":true}""");
        var clock = new WorkerClock(DateTimeOffset.UtcNow);
        using var eventSeen = new ManualResetEventSlim();
        using var run = new RunEstate(recordFormat: 3, brief: brief.ToJsonString(), options: new WorkerOptions
        {
            Clock = clock, AfterEvent = eventSeen.Set, Heartbeat = TimeSpan.FromMilliseconds(50), Grace = TimeSpan.FromSeconds(1),
        });
        RunResponse started = run.Start("generic", "ack-stop", run.Report("progress-overdue"),
            settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["stall-seconds"] = "3600" });
        Assert.True(started.Ok, started.Message);
        Assert.True(eventSeen.Wait(TestTiming.HangTimeout, TestContext.Current.CancellationToken));
        clock.Advance(TimeSpan.FromMinutes(2));
        run.WaitUntil(() => run.Row(started.Run).Observed);
        RequestedProgress missing = run.Service.WorkProgressGet(started.Run)!;
        Assert.Null(missing.Attempt);
        Assert.True(missing.Overdue);
        Assert.Single(Overdue(run.Service));
        RunRow live = run.Row(started.Run);
        Assert.Equal("dispatched", live.Status);
        Assert.True(live.Observed);
        Assert.Null(live.Stop);
        Assert.Null(live.Liveness!.AttentionAt);
        RecordWritten attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(run.Assignment, run.Estate.BuilderId, "widget repository, work branch", "Report progress.")));
        Assert.True(run.Service.WorkProgress(new WorkProgressRequest(attempt.Id, run.Estate.BuilderId, "review", "Reviewing.")
            { Progress = RunEstate.Json("""{"type":"not-measurable"}"""), StatusText = "Reviewing the widget." }).Ok);
        Assert.Empty(Overdue(run.Service));
        Assert.Equal("fresh", run.Service.WorkProgressGet(started.Run)!.Freshness);
        Assert.Equal("dispatched", run.Row(started.Run).Status);
        Assert.Null(run.Row(started.Run).Stop);
        Assert.Empty(run.Service.Records("ask"));
        Assert.True(run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId, "The reporting test is finished.")).Ok);
        Assert.Equal(run.Estate.OperatorId, run.Settled(started.Run).Stop!.RequestedBy);
    }

    [Theory]
    [MemberData(nameof(RequestedProgressTests.Values), MemberType = typeof(RequestedProgressTests))]
    public async Task ReadOnlyMcpExposesEachClaimAndFreshnessWithoutAuthoring(string value)
    {
        using var fixture = new ProgressFixture();
        fixture.Set("estate", ProgressRequestTests.FullRequest);
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        Assert.True(service.WorkProgress(RequestedProgressTests.Request(fixture, value)).Ok);
        int records = service.Session.Load().Records.Count;
        var tools = new ToolRegistry(service, null);
        ToolDefinition tool = Assert.Single(tools.Visible(), t => t.Name == "work_progress_get");
        Assert.False(tool.Writes);
        ToolCallResult response = await tools.CallAsync("work_progress_get", new JsonObject { ["id"] = fixture.Attempt.Id }, TestContext.Current.CancellationToken);
        Assert.False(response.IsError, response.Text);
        RequestedProgress read = JsonSerializer.Deserialize<RequestedProgress>(response.Text, ProtocolJson.Options)!;
        Assert.Equal("fresh", read.Freshness);
        RequestedProgressTests.EqualValue(value, read.Claim!.Progress!.Value);
        Assert.Equal("Reading the widget.", read.Claim.StatusText);
        Assert.Equal(fixture.Estate.BuilderId, read.By);
        Assert.Null(read.Liveness);
        Assert.Equal(records, service.Session.Load().Records.Count);
    }

    [Fact]
    public void RunWindowsUseRecordOrderAtEqualTimesAndExcludeAttemptsAfterSettlement()
    {
        using var fixture = new ProgressFixture();
        fixture.Set("brief", ProgressRequestTests.FullRequest);
        fixture.Estate.Add("goal-status", fixture.Estate.OperatorId, """{"status":"active","note":"Exercise reporting."}""", fixture.Goal.Reference);
        DateTimeOffset at = fixture.Estate.Clock.AddMinutes(1);
        RecordEnvelope Add(string type, string by, string body, RecordReference? subject = null, string? supersedes = null)
        {
            fixture.Estate.SetClock(at.AddSeconds(-1));
            return fixture.Estate.Add(type, by, body, subject, supersedes);
        }
        string Intent(string identity) => $$"""{"provider":"generic","action":"agent-run","identity":"{{identity}}","intent":"Observe reporting.","target":{"assignment":"{{fixture.Assignment.Id}}","principal":"{{fixture.Estate.BuilderId}}","workspace":"widget, branch work/progress","goal":"{{fixture.Goal.Id}}"},"status":"not-started","cap":{"wallMinutes":5,"attemptsPerStep":3},"evidence":[]}""";
        void Cancel(RecordEnvelope intent)
        {
            JsonObject body = JsonNode.Parse(intent.Body.GetRawText())!.AsObject();
            body["status"] = "canceled";
            Add("operation", fixture.Estate.OperatorId, body.ToJsonString(), supersedes: intent.Id);
        }
        RecordEnvelope first = Add("operation", fixture.Estate.OperatorId, Intent("first"));
        RecordEnvelope firstAttempt = Add("attempt", fixture.Estate.BuilderId, fixture.Attempt.Body.GetRawText(), fixture.Assignment.Reference);
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        service.WorkerOptions = WorkerOptions.Default with { Clock = new WorkerClock(at) };
        Assert.True(service.WorkProgress(RequestedProgressTests.Request(fixture, """{"type":"not-measurable"}""") with { Attempt = firstAttempt.Id }).Ok);
        Cancel(first);
        RecordEnvelope second = Add("operation", fixture.Estate.OperatorId, Intent("second"));
        Assert.Null(service.WorkProgressGet(second.Id)!.Claim);
        Assert.Null(service.WorkProgressGet(second.Id)!.Attempt);
        Assert.Equal(firstAttempt.Id, service.WorkProgressGet(first.Id)!.Attempt);
        RecordEnvelope secondAttempt = Add("attempt", fixture.Estate.BuilderId, fixture.Attempt.Body.GetRawText(), fixture.Assignment.Reference);
        Assert.Equal(secondAttempt.Id, service.WorkProgressGet(second.Id)!.Attempt);
        Assert.Equal(first.Id, service.WorkProgressGet(firstAttempt.Id)!.Run);
        Assert.Equal(second.Id, service.WorkProgressGet(secondAttempt.Id)!.Run);
        Cancel(second);
        RecordEnvelope manual = Add("attempt", fixture.Estate.BuilderId, fixture.Attempt.Body.GetRawText(), fixture.Assignment.Reference);
        RequestedProgress manualProgress = service.WorkProgressGet(manual.Id)!;
        Assert.Null(manualProgress.Run);
        Assert.False(manualProgress.Closed);
        Assert.Equal(secondAttempt.Id, service.WorkProgressGet(second.Id)!.Attempt);
        Assert.Equal(0, service.Validate().Errors);
    }

    [Fact]
    public void PerStepHasNoInventedTimeLimitAndUnrequestedNotesNeverRaiseOverdue()
    {
        using var fixture = new ProgressFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        Assert.Equal("not-requested", service.WorkProgressGet(fixture.Attempt.Id)!.Freshness);
        Assert.Empty(Overdue(service));
        fixture.Set("estate", """{"cadence":{"perStep":true}}""");
        Assert.Single(Overdue(service));
        Assert.True(service.WorkProgress(RequestedProgressTests.Request(fixture, """{"type":"not-measurable"}""")).Ok);
        var clock = new WorkerClock(DateTimeOffset.UtcNow.AddDays(1));
        service.WorkerOptions = WorkerOptions.Default with { Clock = clock };
        RequestedProgress progress = service.WorkProgressGet(fixture.Attempt.Id)!;
        Assert.Null(progress.CadenceSeconds);
        Assert.Equal("fresh", progress.Freshness);
        Assert.Empty(Overdue(service));
    }

    [Fact]
    public void ResultRetainsTheLastInterimClaimAcrossLiveStateLossWithoutCompletingOrAcceptingWork()
    {
        using var fixture = new ProgressFixture();
        fixture.Set("estate", ProgressRequestTests.FullRequest);
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        WorkProgressResponse submitted = service.WorkProgress(RequestedProgressTests.Request(fixture, """{"type":"best-effort","percent":100}"""));
        Assert.True(submitted.Ok);
        RecordWritten result = RunEstate.Ok(service.Result(new ResultRequest(fixture.Attempt.Id, fixture.Estate.BuilderId,
            RunEstate.Json("""{"outcome":"partial","summary":"The estimate is not completion evidence.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review the remaining work."}"""))));
        JsonElement body = service.Record(result.Id)!.Envelope.GetProperty("body");
        JsonElement context = body.GetProperty("progressClaim");
        Assert.Equal(submitted.Claim!.At, context.GetProperty("at").GetString());
        Assert.Equal(submitted.Claim.StatusText, context.GetProperty("statusText").GetString());
        RequestedProgressTests.EqualValue("""{"type":"best-effort","percent":100}""", context.GetProperty("progress"));
        Assert.Empty(body.GetProperty("evidence").EnumerateArray());
        Assert.Equal("partial", body.GetProperty("outcome").GetString());
        Assert.Empty(service.Records("acceptance"));
        service.Session.Live.SetMeta("work-progress:" + fixture.Attempt.Id, "null");
        using EstateService reopened = EstateService.Open(fixture.Estate.Root);
        RequestedProgress retained = reopened.WorkProgressGet(fixture.Attempt.Id)!;
        Assert.True(retained.Closed);
        Assert.False(retained.Overdue);
        Assert.Equal(submitted.Claim.At, retained.Claim!.At);
        RequestedProgressTests.EqualValue(context.GetProperty("progress").GetRawText(), retained.Claim.Progress!.Value);
        Assert.Empty(Overdue(reopened));
        Assert.Contains("context only; never evidence of completion", reopened.Observe("W-1")!.Text, StringComparison.Ordinal);
        Assert.Equal(0, reopened.Validate().Errors);
    }

    private static CommunicationAttention[] Overdue(EstateService service) =>
        CommunicationPresentation.Attention(service).Where(a => a.Kind == "progress-overdue").ToArray();
}
