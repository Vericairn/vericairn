// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using static Vericairn.Runtime.Tests.WorkingTreeLockTests;

namespace Vericairn.Runtime.Tests;

/// <summary>A reconciliation settles operations recorded independently under one identity (FIX-100).</summary>
public sealed class IdentityReconciliationTests
{
    private const string Identity = "synthetic-collision:vault/once-001";

    [Fact]
    public void TwoOperationsOfOneIdentityWrittenAtOnceAreSettledWithoutRewritingEitherAndWritesProceed()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        AdmitDevice(service, estate.OperatorId, estate.OperatorId);
        (RecordEnvelope kept, RecordEnvelope other) = Duplicates(estate, service);

        FindingsResponse before = service.Validate();
        Assert.Equal(2, before.Findings.Count(f => f.Code == "operation.duplicate-identity" && f.Severity == "error"));
        Assert.Equal(2, EstateStatus.ReconcileTasks(service.Session.Load().Findings).Count(f => f.Code == "operation.duplicate-identity"));
        WriteOutcome blocked = service.NewRecord(Operation(estate, "synthetic:unrelated"));
        Assert.False(blocked.Ok);
        Assert.Contains(blocked.Problem!.Findings, f => f.Code == "operation.duplicate-identity");

        WriteOutcome settled = service.NewRecord(Settlement(estate.OperatorId, kept, other));

        Assert.True(settled.Ok, settled.Problem?.Message);
        EstateModel model = service.Session.Load();
        Assert.Single(model.ById[settled.Written!.Id].Signatures);
        Assert.Equal(kept.ComputedHash, model.ById[kept.Id].ComputedHash);
        Assert.Equal(other.ComputedHash, model.ById[other.Id].ComputedHash);
        Assert.True(model.IsCurrent(model.ById[kept.Id]));
        Assert.False(model.IsCurrent(model.ById[other.Id]));
        Assert.Equal(kept.Id, model.Latest(model.ById[other.Id]).Id);
        foreach (FindingsResponse check in new[] { service.Validate(), service.Check() })
        {
            Assert.Equal(0, check.Errors);
            Assert.DoesNotContain(check.Findings, f => f.Code == "operation.duplicate-identity");
        }

        Assert.Empty(EstateStatus.ReconcileTasks(model.Findings));
        WriteOutcome proceeds = service.NewRecord(Operation(estate, "synthetic:unrelated"));
        Assert.True(proceeds.Ok, proceeds.Problem?.Message);
        // The settled identity stays settled: a new intent under it is a duplicate of the kept operation.
        WriteOutcome again = service.NewRecord(Operation(estate, Identity));
        Assert.False(again.Ok);
        Assert.Contains(again.Problem!.Findings, f => f.Code == "operation.duplicate-identity");
    }

    [Fact]
    public void OnlyTheAuthorityThatRecordsOperationsSettlesThemAndOnlySigned()
    {
        using var estate = new TestEstate(3);
        // The builder administers the estate, which lets it settle divergent revisions, but holds no authority over operations.
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, DecisionTimestampHistoryTests.Body(estate.OperatorId, "2026-10-13T12:00:00Z").GetRawText());
        estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{estate.BuilderId}}","scope":["administer"],"reserved":["publication"],"conditions":[],"basis":"{{decision.Id}}"}""");
        using EstateService service = EstateService.Open(estate.Root);
        AdmitDevice(service, estate.OperatorId, estate.OperatorId);
        AdmitDevice(service, estate.BuilderId, estate.OperatorId);
        (RecordEnvelope kept, RecordEnvelope other) = Duplicates(estate, service);

        WriteOutcome unauthorized = service.NewRecord(Settlement(estate.BuilderId, kept, other));
        Assert.False(unauthorized.Ok);
        Assert.Contains(unauthorized.Problem!.Findings, f => f.Code == "reconciliation.author" && f.Severity == "error");
        Assert.Empty(service.Session.Load().OfType("reconciliation"));

        // An unsigned settlement settles nothing, even by the operator under an optional signature policy.
        using EstateService unsignedService = EstateService.Open(estate.Root);
        new Vericairn.Records.Signing.FileKeyStore(Vericairn.Records.Signing.VericairnHome.Path()).Remove(Device(service, estate.OperatorId));
        WriteOutcome unsigned = unsignedService.NewRecord(Settlement(estate.OperatorId, kept, other));
        Assert.False(unsigned.Ok);
        Assert.Contains(unsigned.Problem!.Findings, f => f.Code == "signature.required");
        Assert.Empty(service.Session.Load().OfType("reconciliation"));
    }

    [Fact]
    public void ALocalEngineeringPrincipalSettlesThem()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, DecisionTimestampHistoryTests.Body(estate.OperatorId, "2026-10-13T12:00:00Z").GetRawText());
        estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{estate.BuilderId}}","scope":["local-engineering"],"reserved":["publication"],"conditions":[],"basis":"{{decision.Id}}"}""");
        using EstateService service = EstateService.Open(estate.Root);
        AdmitDevice(service, estate.OperatorId, estate.OperatorId);
        AdmitDevice(service, estate.BuilderId, estate.OperatorId);
        (RecordEnvelope kept, RecordEnvelope other) = Duplicates(estate, service);

        WriteOutcome settled = service.NewRecord(Settlement(estate.BuilderId, kept, other));

        Assert.True(settled.Ok, settled.Problem?.Message);
        Assert.Equal(0, service.Validate().Errors);
    }

    [Fact]
    public void RecordsThatDoNotShareTheIdentityOrAreEarlierRevisionsAreNotSettled()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        AdmitDevice(service, estate.OperatorId, estate.OperatorId);
        WriteOutcome first = service.NewRecord(Operation(estate, Identity));
        WriteOutcome second = service.NewRecord(Operation(estate, "synthetic:another"));
        Assert.True(first.Ok && second.Ok);
        EstateModel model = service.Session.Load();
        RecordEnvelope one = model.ById[first.Written!.Id];
        RecordEnvelope another = model.ById[second.Written!.Id];

        WriteOutcome mismatch = service.NewRecord(Settlement(estate.OperatorId, one, another));
        Assert.False(mismatch.Ok);
        FindingDto finding = Assert.Single(mismatch.Problem!.Findings, f => f.Code == "reconciliation.identity");
        Assert.Contains(another.Id, finding.Message, StringComparison.Ordinal);

        // A revision of the kept operation is not an independent operation.
        WriteOutcome revised = service.NewRecord(new("operation", estate.OperatorId, Json(Operation(estate, Identity).Body.GetRawText()
            .Replace("\"not-started\"", "\"canceled\"", StringComparison.Ordinal).Replace("\"evidence\":[]", "\"evidence\":[],\"reconciliation\":\"Never dispatched.\"", StringComparison.Ordinal)),
            Supersedes: one.Id));
        Assert.True(revised.Ok, revised.Problem?.Message);
        RecordEnvelope latest = service.Session.Load().ById[revised.Written!.Id];
        WriteOutcome sameOperation = service.NewRecord(new("reconciliation", estate.OperatorId,
            Json($$"""{"kept":"{{latest.Id}}","withdrawn":["{{one.Id}}"],"note":"Two revisions of one operation."}"""), $"operation:{another.Id}:{another.ComputedHash}"));
        Assert.False(sameOperation.Ok);
        Assert.Contains(sameOperation.Problem!.Findings, f => f.Code == "reconciliation.identity");
        Assert.Empty(service.Session.Load().OfType("reconciliation"));
        Assert.Equal(0, service.Validate().Errors);
    }

    /// <summary>
    /// Two current operations under one identity, as two writers made them before FIX-99: one runtime writes in the tree while another,
    /// in a copy that does not see it, writes the same intent, and the copy is then synced in without the tree's write lock.
    /// </summary>
    private static (RecordEnvelope Kept, RecordEnvelope Other) Duplicates(TestEstate estate, EstateService service)
    {
        WriteOutcome? inTree = null;
        WriteOutcome inCopy = UnlockedWriter.Sync(estate.Root, copy =>
        {
            using EstateService other = EstateService.Open(copy);
            WriteOutcome written = other.NewRecord(Operation(estate, Identity));
            inTree = service.NewRecord(Operation(estate, Identity));
            return written;
        });
        Assert.True(inCopy.Ok, inCopy.Problem?.Message);
        Assert.True(inTree!.Ok, inTree.Problem?.Message);
        EstateModel model = service.Session.Load();
        return (model.ById[inTree.Written!.Id], model.ById[inCopy.Written!.Id]);
    }

    private static NewRecordRequest Settlement(string by, RecordEnvelope kept, RecordEnvelope other) => new("reconciliation", by,
        Json($$"""{"kept":"{{kept.Id}}","withdrawn":["{{other.Id}}"],"note":"Both runtimes recorded the intent; the first stands, and the second was never dispatched."}"""),
        $"operation:{kept.Id}:{kept.ComputedHash}");

    private static void AdmitDevice(EstateService service, string principal, string admitter)
    {
        WriteOutcome created = service.DeviceCreate(new(principal, "Test device", "file"));
        Assert.True(created.Ok, created.Problem?.Message);
        WriteOutcome admitted = service.DeviceAdmit(new(created.Written!.Id, admitter, "Admit the test device."));
        Assert.True(admitted.Ok, admitted.Problem?.Message);
    }

    private static string Device(EstateService service, string principal) =>
        service.Session.Load().Current("device").Single(d => EstateModel.BodyString(d, "principal") == principal).Id;

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
