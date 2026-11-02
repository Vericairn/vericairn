// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class AttentionAcknowledgmentTests
{
    [Fact]
    public void ChangedWorkerSignalsRenewTheNoticeEvenWhenTheirCountStaysTheSame()
    {
        using var fixture = new ProgressFixture();
        RecordEnvelope intent = fixture.Estate.Add("operation", fixture.Estate.OperatorId, $$"""{"provider":"generic","action":"agent-run","identity":"attention-test","intent":"Observe attention.","target":{"assignment":"{{fixture.Assignment.Id}}","principal":"{{fixture.Estate.BuilderId}}","workspace":"widget branch work/phone","goal":"{{fixture.Goal.Id}}"},"status":"not-started","cap":{"wallMinutes":5,"attemptsPerStep":3},"evidence":[]}""");
        RecordEnvelope dispatched = fixture.Estate.Add("operation", fixture.Estate.OperatorId, intent.Body.GetRawText().Replace("not-started", "dispatched", StringComparison.Ordinal), supersedes: intent.Id);
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        var clock = new WorkerClock(dispatched.AtTime.AddSeconds(1));
        service.WorkerOptions = WorkerOptions.Default with { Clock = clock };
        string at = RecordParser.FormatTimestamp(clock.GetUtcNow());
        var directory = new RunDirectory(service.Session.Manifest.Estate, intent.Id);
        directory.Create();
        var observation = new RunObservedState { Run = intent.Id, Heartbeat = at, Signals = [new("repetition", "warning", at, "Repeated work needs inspection.")] };
        directory.Write(observation);
        AttentionNotice notice = Assert.Single(service.AttentionGet(fixture.Estate.OperatorId).Items, n => n.Kind == "incident" && n.SubjectId == dispatched.Id);
        Assert.True(service.AttentionAcknowledge(Request(fixture.Estate.OperatorId, notice)).Ok);
        Assert.Equal("acknowledged", Assert.Single(service.AttentionGet(fixture.Estate.OperatorId).Items, n => n.Key == notice.Key).AttentionState);
        int records = fixture.Estate.Load().Records.Count;
        observation.Signals = [new("scope", "critical", at, "A changed scope observation needs inspection.")];
        directory.Write(observation);
        AttentionNotice changed = Assert.Single(service.AttentionGet(fixture.Estate.OperatorId).Items, n => n.Key == notice.Key);
        Assert.Equal("unacknowledged", changed.AttentionState);
        Assert.NotEqual(notice.Revision, changed.Revision);
        Assert.Equal("conflict", service.AttentionAcknowledge(Request(fixture.Estate.OperatorId, notice)).Problem!.Code);
        Assert.Equal(records, fixture.Estate.Load().Records.Count);
    }

    [Fact]
    public void AdvancingTheAgeDoesNotUndoAcknowledgmentButANewClaimDoes()
    {
        using var fixture = new ProgressFixture();
        fixture.Set("brief", """{"cadence":{"minutes":10},"types":["not-measurable"]}""");
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        var clock = new WorkerClock(fixture.Attempt.AtTime.AddMinutes(11));
        service.WorkerOptions = WorkerOptions.Default with { Clock = clock };
        AttentionNotice notice = Assert.Single(service.AttentionGet(fixture.Estate.OperatorId).Items, i => i.Kind == "progress-overdue");
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.True(service.AttentionAcknowledge(Request(fixture.Estate.OperatorId, notice)).Ok);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("acknowledged", Assert.Single(service.AttentionGet(fixture.Estate.OperatorId).Items, i => i.Kind == "progress-overdue").AttentionState);
        Assert.True(service.WorkProgress(new(fixture.Attempt.Id, fixture.Estate.BuilderId, "review", "Reading the work.")
            { Progress = System.Text.Json.JsonSerializer.SerializeToElement(new { type = "not-measurable" }) }).Ok);
        Assert.DoesNotContain(service.AttentionGet(fixture.Estate.OperatorId).Items, i => i.Kind == "progress-overdue");
        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal("unacknowledged", Assert.Single(service.AttentionGet(fixture.Estate.OperatorId).Items, i => i.Kind == "progress-overdue").AttentionState);
    }

    [Fact]
    public void AcknowledgmentPersistsOnlyForItsPrincipalAndNeitherHidesNorChangesWork()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope failure = Failure(estate);
        string[] before = estate.Load().Records.Select(r => r.ComputedHash).Order(StringComparer.Ordinal).ToArray();
        string status;
        AttentionNotice notice;
        using (EstateService service = EstateService.Open(estate.Root))
        {
            status = service.Status().Json.GetRawText();
            notice = Notice(service, estate.OperatorId, failure.Id);
            AttentionAcknowledgeResponse acknowledged = service.AttentionAcknowledge(Request(estate.OperatorId, notice));
            Assert.True(acknowledged.Ok, acknowledged.Problem?.Message);
            Assert.Equal("acknowledged", acknowledged.Item!.AttentionState);
            Assert.Equal(notice with { AcknowledgedAt = acknowledged.Item.AcknowledgedAt }, acknowledged.Item);
            Assert.Equal(acknowledged, service.AttentionAcknowledge(Request(estate.OperatorId, notice)));
            Assert.Equal("unacknowledged", Notice(service, estate.BuilderId, failure.Id).AttentionState);
            Assert.Equal(status, service.Status().Json.GetRawText());
            Assert.Equal("failed", service.Record(failure.Id)!.Envelope.GetProperty("body").GetProperty("outcome").GetString());
        }
        using EstateService reopened = EstateService.Open(estate.Root);
        Assert.Equal("acknowledged", Notice(reopened, estate.OperatorId, failure.Id).AttentionState);
        Assert.Equal(notice.Revision, Notice(reopened, estate.OperatorId, failure.Id).Revision);
        Assert.Equal("unacknowledged", Notice(reopened, estate.BuilderId, failure.Id).AttentionState);
        Assert.Equal(before, estate.Load().Records.Select(r => r.ComputedHash).Order(StringComparer.Ordinal));
        Assert.Empty(estate.Load().OfType("acceptance"));
        Assert.Equal(status, reopened.Status().Json.GetRawText());
    }

    [Fact]
    public void ChangedAndGoneItemsClearAcknowledgmentAndStaleRequestsAreRefused()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope failure = Failure(estate);
        using EstateService service = EstateService.Open(estate.Root);
        AttentionNotice first = Notice(service, estate.OperatorId, failure.Id);
        Assert.True(service.AttentionAcknowledge(Request(estate.OperatorId, first)).Ok);
        RecordEnvelope changed = estate.Add("verification", estate.BuilderId, failure.Body.GetRawText().Replace("failed", "unknown", StringComparison.Ordinal), failure.Subject, failure.Id);
        AttentionNotice next = Notice(service, estate.OperatorId, changed.Id);
        Assert.Equal("unacknowledged", next.AttentionState);
        Assert.NotEqual(first.Revision, next.Revision);
        Assert.Equal("conflict", service.AttentionAcknowledge(Request(estate.OperatorId, first)).Problem!.Code);
        Assert.Equal("conflict", service.AttentionAcknowledge(Request(estate.OperatorId, next) with { Expected = first.Revision }).Problem!.Code);
        Assert.True(service.AttentionAcknowledge(Request(estate.OperatorId, next)).Ok);
        estate.Add("verification", estate.BuilderId, changed.Body.GetRawText().Replace("unknown", "passed", StringComparison.Ordinal), changed.Subject, changed.Id);
        Assert.DoesNotContain(service.AttentionGet(estate.OperatorId).Items, i => i.SubjectId == changed.Id);
        Assert.Equal("conflict", service.AttentionAcknowledge(Request(estate.OperatorId, next)).Problem!.Code);
    }

    [Fact]
    public void ARequestingDeviceCannotAcknowledgeForAnotherPrincipalOrAfterRevocation()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope failure = Failure(estate);
        using EstateService service = EstateService.Open(estate.Root);
        WriteOutcome authority = service.DeviceCreate(new(estate.OperatorId, "Operator test device", "file"));
        Assert.True(authority.Ok, authority.Problem?.Message);
        WriteOutcome first = service.DeviceAdmit(new(authority.Written!.Id, estate.OperatorId, "First authority."));
        Assert.True(first.Ok, first.Problem?.Message);
        WriteOutcome device = service.DeviceCreate(new(estate.BuilderId, "Test device", "file"));
        Assert.True(device.Ok, device.Problem?.Message);
        Assert.True(service.DeviceAdmit(new(device.Written!.Id, estate.OperatorId, "Test admission.")).Ok);
        DeviceStanding standing = service.Standing(device.Written.Id)!;
        AttentionNotice notice = Notice(service, estate.BuilderId, failure.Id);
        using (service.ForDeviceRequest(standing.Identity, standing.PublicKey))
        {
            Assert.Equal("denied", service.AttentionAcknowledge(Request(estate.OperatorId, notice)).Problem!.Code);
            Assert.Throws<UnauthorizedAccessException>(() => service.AttentionGet(estate.OperatorId));
            Assert.True(service.AttentionAcknowledge(Request(estate.BuilderId, notice)).Ok);
        }
        Assert.True(service.DeviceRevoke(new(standing.Identity, estate.OperatorId, "End test access.")).Ok);
        using (service.ForDeviceRequest(standing.Identity, standing.PublicKey))
            Assert.Equal("denied", service.AttentionAcknowledge(Request(estate.BuilderId, notice)).Problem!.Code);
        Assert.Equal("unacknowledged", Notice(service, estate.OperatorId, failure.Id).AttentionState);
    }

    internal static AttentionNotice Notice(EstateService service, string principal, string subject) => Assert.Single(service.AttentionGet(principal).Items, i => i.SubjectId == subject);
    internal static AttentionAcknowledgeRequest Request(string principal, AttentionNotice item) => new(principal, item.Kind, item.SubjectType, item.SubjectId, item.Revision);
    internal static RecordEnvelope Failure(TestEstate estate)
    {
        RecordEnvelope repository = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Widget","role":"source","location":{"github":"Example/widget"},"visibility":"private"}""");
        return estate.Add("verification", estate.BuilderId, """{"method":"test","commit":"0123456789abcdef0123456789abcdef01234567","environment":{"runner":"fixture"},"role":"automation","outcome":"failed","summary":"A check failed.","evidence":[],"limitations":[]}""", repository.Reference);
    }
}
