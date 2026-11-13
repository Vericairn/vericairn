// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using static Vericairn.Records.Tests.OperationEffectKeptTests;

namespace Vericairn.Runtime.Tests;

/// <summary>FIX-109 at the write boundary: a new operation revision that changes its predecessor's intent or target is refused.</summary>
public sealed class OperationEffectKeptWriteTests
{
    [Theory]
    [InlineData(1, "intent")]
    [InlineData(3, "intent")]
    [InlineData(1, "target")]
    [InlineData(3, "target")]
    public void ANewRevisionThatChangesItsIntentOrTargetIsRefusedAndWritesNothing(long format, string changed)
    {
        using var estate = new TestEstate(format);
        (RecordEnvelope intent, RecordEnvelope dispatched) = Dispatch(estate);
        using EstateService service = EstateService.Open(estate.Root);
        int before = service.Session.Load().Records.Count;

        WriteOutcome refused = service.NewRecord(new("operation", estate.OperatorId, Json(Changed(changed, intent.At)), Supersedes: dispatched.Id));

        Assert.False(refused.Ok);
        Assert.Null(refused.Written);
        FindingDto finding = Assert.Single(refused.Problem!.Findings, f => f.Code == (changed == "intent" ? IntentCode : TargetCode));
        Assert.Equal("error", finding.Severity);
        Assert.EndsWith("#body." + changed, finding.Location, StringComparison.Ordinal);
        Assert.Equal(before, service.Session.Load().Records.Count);
        Assert.Equal(dispatched.Id, Assert.Single(service.Session.Load().Current("operation")).Id);

        WriteOutcome kept = service.NewRecord(new("operation", estate.OperatorId, Json(Body("uncertain", intent.At)), Supersedes: dispatched.Id));
        Assert.True(kept.Ok, kept.Problem?.Message);
        Assert.Equal(0, service.Validate().Errors);
    }

    [Fact]
    public void TheRetryReconciliationControlIsWrittenAsBefore()
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope intent, RecordEnvelope dispatched) = Dispatch(estate);
        using EstateService service = EstateService.Open(estate.Root);
        WriteOutcome uncertain = service.NewRecord(new("operation", estate.OperatorId, Json(Body("uncertain", intent.At)), Supersedes: dispatched.Id));
        Assert.True(uncertain.Ok, uncertain.Problem?.Message);

        WriteOutcome unreconciled = service.NewRecord(new("operation", estate.OperatorId, Json(Body("dispatched", intent.At)), Supersedes: uncertain.Written!.Id));
        Assert.False(unreconciled.Ok);
        Assert.Contains(unreconciled.Problem!.Findings, f => f.Code == "operation.reconciliation");

        JsonObject retry = Body("dispatched", intent.At);
        retry["reconciliation"] = "Nothing reached the example destination; retried under the same identity.";
        WriteOutcome retried = service.NewRecord(new("operation", estate.OperatorId, Json(retry), Supersedes: uncertain.Written!.Id));
        Assert.True(retried.Ok, retried.Problem?.Message);
        FindingsResponse check = service.Check();
        Assert.Equal(0, check.Errors);
        Assert.DoesNotContain(check.Findings, f => f.Code is IntentCode or TargetCode);
    }

    [Fact]
    public void AnExistingChangedRevisionIsInformationAndTheNextRevisionKeepsWhatItSupersedes()
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope intent, RecordEnvelope dispatched) = Dispatch(estate);
        RecordEnvelope earlier = estate.Add("operation", estate.OperatorId, Changed("target", intent.At), supersedes: dispatched.Id);
        using EstateService service = EstateService.Open(estate.Root);

        foreach (FindingsResponse check in new[] { service.Validate(), service.Check() })
        {
            Assert.Equal(0, check.Errors);
            FindingDto information = Assert.Single(check.Findings, f => f.Code == TargetCode);
            Assert.Equal("info", information.Severity);
            Assert.Equal(earlier.ExpectedPath + "#body.target", information.Location);
        }

        // An unrelated record is written as before.
        WriteOutcome unrelated = service.NewRecord(new("resource", estate.OperatorId, Json("""{"kind":"service","name":"Site","role":"the public site","location":{"url":"https://example.invalid/site"},"visibility":"public"}""")));
        Assert.True(unrelated.Ok, unrelated.Problem?.Message);

        // The next revision is judged against the revision it supersedes: returning to the original destination is another change.
        WriteOutcome restored = service.NewRecord(new("operation", estate.OperatorId, Json(Body("uncertain", intent.At)), Supersedes: earlier.Id));
        Assert.False(restored.Ok);
        Assert.Equal("error", Assert.Single(restored.Problem!.Findings, f => f.Code == TargetCode).Severity);
        WriteOutcome kept = service.NewRecord(new("operation", estate.OperatorId, Json(Changed("target", intent.At)), Supersedes: earlier.Id));
        Assert.True(kept.Ok, kept.Problem?.Message);
        Assert.Equal(earlier.ComputedHash, service.Session.Load().ById[earlier.Id].ComputedHash);
        Assert.Equal("info", Assert.Single(service.Validate().Findings, f => f.Code == TargetCode).Severity);
    }

    [Fact]
    public void ADevicePreparedRevisionThatChangesItsIntentIsRefusedBeforeSigning()
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope intent, RecordEnvelope dispatched) = Dispatch(estate);
        using EstateService service = EstateService.Open(estate.Root);
        WriteOutcome created = service.DeviceCreate(new(estate.OperatorId, "Operator device", "file"));
        Assert.True(created.Ok, created.Problem?.Message);
        string device = created.Written!.Id;
        Assert.True(service.DeviceAdmit(new(device, estate.OperatorId, "Admit the operator's own device.")).Ok);
        string publicKey = EstateModel.BodyString(service.Session.Load().ById[device], "publicKey")!;
        using EstateService.RequestDeviceScope scope = service.ForDeviceRequest(device, publicKey);

        RecordPrepareResponse refused = service.PrepareRecord(new("operation", estate.OperatorId, Json(Changed("intent", intent.At)), Supersedes: dispatched.Id));
        Assert.False(refused.Ok);
        Assert.Null(refused.Prepared);
        Assert.Equal("error", Assert.Single(refused.Problem!.Findings, f => f.Code == IntentCode).Severity);

        RecordPrepareResponse prepared = service.PrepareRecord(new("operation", estate.OperatorId, Json(Body("uncertain", intent.At)), Supersedes: dispatched.Id));
        Assert.True(prepared.Ok, prepared.Problem?.Message);
    }

    /// <summary>The issue's two cases: the uncertain revision with only its intent changed, or with its destination and idempotency key changed.</summary>
    private static string Changed(string changed, string dispatchedAt)
    {
        JsonObject body = Body("uncertain", dispatchedAt);
        if (changed == "intent")
        {
            body["intent"] = "Publish synthetic media item 0001 and its companion to the example destination.";
        }
        else
        {
            body["target"]!["destination"] = "https://example.invalid/media/0002";
            body["target"]!["idempotencyKey"] = "synthetic-key-0002";
        }

        return body.ToJsonString();
    }

    private static JsonElement Json(JsonObject body) => Json(body.ToJsonString());

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
