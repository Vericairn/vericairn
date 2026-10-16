// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public class StatusObservationTests
{
    [Fact]
    public void AFormatOneEstateNeedsAnExplicitUpgradeToRecordAnEstateObservation()
    {
        using var estate = new TestEstate();
        using EstateService service = EstateService.Open(estate.Root);
        WriteOutcome refused = service.ObserveEstate(new EstateObserveRequest(estate.OperatorId));
        Assert.False(refused.Ok);
        Assert.Contains("record format 2", refused.Problem!.Message, StringComparison.Ordinal);
        Assert.Empty(estate.Load().OfType("observation"));
    }

    [Fact]
    public void APersonExplicitlyObservesStatusAndOrdinaryReadsNeverAdvanceIt()
    {
        using var estate = new TestEstate(recordFormat: 2);
        using EstateService service = EstateService.Open(estate.Root);
        int count = estate.Load().Records.Count;
        Assert.Equal(JsonValueKind.Null, service.Status().Json.GetProperty("lastObservedByPerson").ValueKind);
        Assert.Equal(count, estate.Load().Records.Count);
        HandoffPackage package = service.Handoff().Package!;
        WriteOutcome observation = service.ObserveEstate(new EstateObserveRequest(estate.OperatorId));
        Assert.True(observation.Ok, observation.Problem?.Message);
        JsonElement seen = service.Status().Json.GetProperty("lastObservedByPerson");
        Assert.Equal(observation.Written!.Id, seen.GetProperty("record").GetString());
        Assert.Equal(estate.OperatorId, seen.GetProperty("by").GetString());
        JsonElement body = service.Record(observation.Written.Id)!.Envelope.GetProperty("body");
        Assert.Equal(count, body.GetProperty("facts").GetProperty("records").GetInt32());
        Assert.Equal(count + 1, estate.Load().Records.Count);
        Assert.Equal(seen.GetRawText(), service.Status().Json.GetProperty("lastObservedByPerson").GetRawText());
        Assert.True(service.ResumeHandoff(package).Json!.Value.GetProperty("changed").GetBoolean());
        Assert.False(service.ObserveEstate(new EstateObserveRequest(estate.BuilderId)).Ok);
        Assert.Equal(count + 1, estate.Load().Records.Count);
    }
}
