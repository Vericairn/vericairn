// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;

namespace Vericairn.Runtime.Tests;

[Collection("policy-sources")]
public sealed class ReleaseCadenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void DueCadenceIsSharedContextWithNoHoldRecordOrDispatch(int graceDays)
    {
        using var f = new ReleaseCadenceFixture(graceDays: graceDays);
        f.At(f.DueAt);
        string[] before = f.Snapshot();
        string[] objects = f.Api.Session.Objects.List().ToArray();
        StatusResponse status = f.Api.Status();
        ProgressResponse progress = f.Api.Progress();
        ReleaseResponse gate = f.Api.ReleaseCheck(f.Request);

        Assert.True(gate.Ok, gate.Message);
        Assert.Empty(gate.Check!.Objections);
        Assert.Equal(0, gate.Policies!.Holds);
        ReleaseCadenceDto row = Assert.Single(gate.Policies.ReleaseCadences!);
        Assert.Equal("current", row.Line);
        Assert.Equal("1.0.0", row.Version);
        Assert.Equal(f.Product.Id, row.Product.Id);
        Assert.Equal(f.Policy.Id, row.Policy.Id);
        Assert.Equal(RecordParser.FormatTimestamp(f.DueAt), row.DueAt);
        Assert.Equal(0, row.Occurrence.GetProperty("number").GetInt64());
        foreach (JsonElement view in new[] { status.Json, progress.Json })
        {
            JsonElement due = Assert.Single(view.GetProperty("operator").GetProperty("releaseCadences").EnumerateArray());
            Assert.Equal(row.DueAt, due.GetProperty("dueAt").GetString());
            Assert.Equal(row.Line, due.GetProperty("line").GetString());
            Assert.Equal(row.Version, due.GetProperty("version").GetString());
            Assert.Single(due.GetProperty("targets").EnumerateArray());
            Assert.Empty(view.GetProperty("operator").GetProperty("duties").EnumerateArray());
        }
        foreach (string text in new[] { status.Text, progress.Text, gate.Policies.Text })
        {
            Assert.Contains("release due on the cadence; line current; target version 1.0.0", text, StringComparison.Ordinal);
            Assert.Contains("A person must submit the release and accept its candidate", text, StringComparison.Ordinal);
        }
        Assert.Equal(0, f.Dispatches);
        Assert.Equal(0, f.ProviderCalls);
        Assert.Empty(f.Api.Records("operation"));
        Assert.Empty(f.Api.Records("release"));
        Assert.Empty(f.Api.Records("acceptance"));
        Assert.Equal(objects, f.Api.Session.Objects.List());
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public void FutureCadenceAddsNoContextAndReadingWritesNothing()
    {
        using var f = new ReleaseCadenceFixture();
        f.At(f.DueAt.AddSeconds(-1));
        string[] before = f.Snapshot();
        AssertNoContext(f);
        ReleaseResponse gate = f.Api.ReleaseCheck(f.Request);
        Assert.True(gate.Ok, gate.Message);
        Assert.Null(gate.Policies!.ReleaseCadences);
        Assert.DoesNotContain("release due on the cadence", gate.Policies.Text, StringComparison.Ordinal);
        Assert.Empty(gate.Check!.Objections);
        Assert.Equal(0, f.Dispatches);
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public void OrdinaryDutyStillHoldsAndRemovingTheDeclarationRestoresItsHold()
    {
        using var f = new ReleaseCadenceFixture();
        f.At(f.DueAt.AddDays(4));
        JsonObject unrelated = f.Records.PolicyBody("other-duty");
        string due = RecordParser.FormatTimestamp(f.Records.Estate.Clock.AddSeconds(2));
        unrelated["startsAt"] = due;
        unrelated["rules"]![0]!["parameters"]!["schedule"]!["firstDueAt"] = due;
        unrelated["rules"]![0]!["parameters"]!["graceDays"] = 0;
        f.Records.Policy(unrelated);
        f.At(f.Records.Estate.Clock.AddSeconds(1));
        ReleaseResponse gate = f.Api.ReleaseCheck(f.Request);
        Assert.False(gate.Ok);
        Assert.Single(gate.Policies!.ReleaseCadences!);
        Assert.Contains(gate.Check!.Objections, o => o.Location == "policy:other-duty/monthly");
        Assert.DoesNotContain(gate.Check.Objections, o => o.Location == "policy:release-cadence/monthly");
        f.RemoveCadence();
        ReleaseResponse removed = f.Api.ReleaseCheck(f.Request);
        Assert.Null(removed.Policies!.ReleaseCadences);
        Assert.Contains(removed.Check!.Objections, o => o.Location == "policy:release-cadence/monthly");
        Assert.Equal(0, f.Dispatches);
    }

    [Fact]
    public void GateUsesTheRequestedLineAndVersionWhileViewsUseCandidateMetadata()
    {
        using var f = new ReleaseCadenceFixture();
        RecordEnvelope candidate = f.Candidate(line: "1.x", channel: "lts");
        f.At(f.DueAt);
        JsonElement due = Assert.Single(f.Api.Progress().Json.GetProperty("operator").GetProperty("releaseCadences").EnumerateArray());
        Assert.Equal("1.x", due.GetProperty("line").GetString());
        Assert.Equal(2, due.GetProperty("targets").GetArrayLength());
        Assert.Contains(due.GetProperty("targets").EnumerateArray(), x => x.GetProperty("id").GetString() == candidate.Id);
        ReleaseResponse gate = f.Api.ReleaseCheck(f.Request with { Version = "1.0.1", Line = "1.x", Channel = "lts", EndOfSupport = "2028-01-01T00:00:00Z" });
        Assert.True(gate.Ok, gate.Message);
        ReleaseCadenceDto context = Assert.Single(gate.Policies!.ReleaseCadences!);
        Assert.Equal("1.x", context.Line);
        Assert.Equal("1.0.1", context.Version);
        Assert.Contains("line 1.x; target version 1.0.1", gate.Policies.Text, StringComparison.Ordinal);
        Assert.Equal(0, f.Dispatches);
    }

    [Fact]
    public void PublicationScheduleOpensContextOnlyAfterAnObservedPublication()
    {
        using var f = new ReleaseCadenceFixture(publicationSchedule: true);
        AssertNoContext(f);
        RecordEnvelope publication = f.Publish("0.9.0");
        f.At(f.DueAt);
        ReleaseCadenceDto context = Assert.Single(f.Api.ReleaseCheck(f.Request).Policies!.ReleaseCadences!);
        Assert.Equal("release", context.Occurrence.GetProperty("kind").GetString());
        Assert.Equal(publication.Id, context.Occurrence.GetProperty("publication").GetProperty("id").GetString());
        Assert.Equal("1.0.0", context.Version);
        Assert.Equal(publication.At, context.DueAt);
        Assert.Single(f.Api.Progress().Json.GetProperty("operator").GetProperty("releaseCadences").EnumerateArray());
        Assert.Equal(0, f.Dispatches);
    }

    [Fact]
    public void MissingTargetIsExplicitAndPublishedPlansAreNotProposedAgain()
    {
        using var f = new ReleaseCadenceFixture();
        f.Publish("1.0.0");
        f.At(f.DueAt);
        Assert.Contains("target version not declared", f.Api.Status().Text, StringComparison.Ordinal);
        f.Plan("1.1.0");
        JsonElement row = Assert.Single(f.Api.Progress().Json.GetProperty("operator").GetProperty("releaseCadences").EnumerateArray());
        Assert.Equal("1.1.0", row.GetProperty("version").GetString());
    }

    [Fact]
    public void ANewScheduleDoesNotInheritAnOlderCadencePin()
    {
        using var f = new ReleaseCadenceFixture();
        f.At(f.DueAt);
        JsonObject revision = PolicyFixture.Body(f.Policy);
        revision["rules"]![0]!["parameters"]!["schedule"]!["firstDueAt"] = RecordParser.FormatTimestamp(f.DueAt.AddDays(1));
        f.Records.Policy(revision, f.Policy);
        f.At(f.DueAt.AddDays(1));
        ReleaseResponse gate = f.Api.ReleaseCheck(f.Request);
        ReleaseCadenceDto context = Assert.Single(gate.Policies!.ReleaseCadences!);
        Assert.Equal(f.Policy.Id, context.Origin.Id);
        Assert.Contains(gate.Policies.Findings, p => p.Duty is not null && p.Duty.Origin.Id != f.Policy.Id && p.ReleaseHold && !p.IsReleaseCadence);
        Assert.NotEmpty(gate.Check!.Objections);
    }

    [Fact]
    public void CompletionAndExplicitlyEndedDebtLeaveNoCadenceWait()
    {
        using var completed = new ReleaseCadenceFixture();
        completed.At(completed.DueAt);
        completed.Records.Review(completed.Records.Result(completed.Records.DutyLink(completed.Policy)));
        AssertNoContext(completed);
        using var ended = new ReleaseCadenceFixture();
        ended.At(ended.DueAt);
        JsonObject revision = PolicyFixture.Body(ended.Policy);
        revision["dutyDispositions"] = new JsonArray(ended.Records.Disposition(ended.Policy));
        ended.Records.Policy(revision, ended.Policy);
        AssertNoContext(ended);
    }

    [Fact]
    public void TheSharedSubmissionGateRetainsContextAndOnlyAnExplicitSubmissionDispatches()
    {
        using var f = new ReleaseCadenceFixture();
        f.At(f.DueAt);
        ReleaseResponse refused = f.Api.ReleaseSubmit(f.Request with { Expected = "stale" });
        Assert.False(refused.Ok);
        Assert.Single(refused.Policies!.ReleaseCadences!);
        Assert.DoesNotContain(refused.Problem!.Findings, x => x.Code == "policy.unmet");
        Assert.Equal(0, f.Dispatches);
        Assert.Empty(f.Api.Records("operation"));
        ReleaseResponse submitted = f.Api.ReleaseSubmit(f.Request);
        Assert.True(submitted.Ok, submitted.Message);
        Assert.Single(submitted.Policies!.ReleaseCadences!);
        Assert.Equal(1, f.Dispatches);
        Assert.Empty(f.Api.Records("release"));
        Assert.Empty(f.Api.Records("acceptance"));
    }

    private static void AssertNoContext(ReleaseCadenceFixture f)
    {
        StatusResponse status = f.Api.Status();
        ProgressResponse progress = f.Api.Progress();
        Assert.False(status.Json.GetProperty("operator").TryGetProperty("releaseCadences", out _));
        Assert.False(progress.Json.GetProperty("operator").TryGetProperty("releaseCadences", out _));
        Assert.DoesNotContain("release due on the cadence", status.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("release due on the cadence", progress.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadOnlyMcpCheckCarriesExactCadenceContextWithoutSigningOrProviderCalls()
    {
        using var f = new ReleaseCadenceFixture();
        f.At(f.DueAt);
        f.Api.BeforeSigningRecord = () => Assert.Fail("A cadence check cannot sign.");
        string[] before = f.Snapshot();
        ReleaseResponse expected = f.Api.ReleaseCheck(f.Request);
        var registry = new ToolRegistry(f.Api, null);
        ToolDefinition tool = Assert.Single(registry.Visible(), t => t.Name == "release_submit_check");
        Assert.False(tool.Writes);
        ToolCallResult result = await registry.CallAsync(tool.Name, JsonSerializer.SerializeToNode(f.Request, ProtocolJson.Options)!.AsObject(), TestContext.Current.CancellationToken);
        Assert.False(result.IsError);
        Assert.Equal(JsonSerializer.Serialize(expected, ProtocolJson.Options), result.Text);
        Assert.Single(expected.Policies!.ReleaseCadences!);
        Assert.Equal(0, f.ProviderCalls);
        Assert.Equal(before, f.Snapshot());
    }
}
