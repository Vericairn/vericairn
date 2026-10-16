// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public class CommunicationServiceTests
{
    [Fact]
    public void OwnValuesAreSharedByReadersAndEditsRequireTheExpectedRevision()
    {
        using var estate = new TestEstate(2);
        using EstateService service = EstateService.Open(estate.Root);
        WriteOutcome first = service.CommunicationSet(new(estate.OperatorId, "person", new(Status: "standard"), estate.OperatorId));
        Assert.True(first.Ok, first.Problem?.Message);
        Assert.Equal("standard", service.Communication(estate.OperatorId).Fields["status"].Value);
        Assert.Equal("conflict", service.CommunicationSet(new(estate.OperatorId, "person", new(Status: "short"), estate.OperatorId)).Problem!.Code);
        WriteOutcome reset = service.CommunicationSet(new(estate.OperatorId, "person", new(), estate.OperatorId, Expected: first.Written!.Hash));
        Assert.True(reset.Ok, reset.Problem?.Message);
        CommunicationProfile profile = service.Communication(estate.OperatorId);
        Assert.Equal("shipped", profile.Fields["status"].Source);
        Assert.Equal(2, profile.Revisions.Count);
        Assert.NotNull(profile.ObservedAt);
        Assert.NotNull(profile.RevisionHash);
        Assert.Equal("conflict", service.CommunicationSet(new(estate.OperatorId, "person", new(), estate.OperatorId, Expected: first.Written.Hash)).Problem!.Code);
    }

    [Fact]
    public void DefaultsAreDistinctFromPersonalChoicesAndReadsWriteNothing()
    {
        using var estate = new TestEstate(2);
        using EstateService service = EstateService.Open(estate.Root);
        Assert.True(service.CommunicationSet(new(estate.OperatorId, "estate", new(Status: "standard"))).Ok);
        Assert.True(service.CommunicationSet(new(estate.OperatorId, "person", new(Status: "short"), estate.OperatorId)).Ok);
        int count = estate.Load().Records.Count;
        Assert.Equal("standard", service.Communication().Fields["status"].Value);
        Assert.Equal("short", service.Communication(estate.OperatorId).Fields["status"].Value);
        Assert.Equal("comprehensive", service.Communication(estate.OperatorId, view: new(Status: "comprehensive")).Fields["status"].Value);
        Assert.Equal(count, estate.Load().Records.Count);
        Assert.NotNull(service.Communication(estate.BuilderId).Unavailable);
        Assert.NotNull(service.Communication("missing").Unavailable);
        Assert.NotNull(service.Communication(estate.OperatorId, "missing").Unavailable);
        Assert.NotNull(service.Communication(view: new(Status: "unsupported")).Unavailable);
    }

    [Fact]
    public void DelegationIsExplicitAttributedRevocableAndScoped()
    {
        using var estate = new TestEstate(2);
        using EstateService service = EstateService.Open(estate.Root);
        var setting = new CommunicationSetRequest(estate.BuilderId, "person", new(Status: "standard"), estate.OperatorId);
        Assert.False(service.CommunicationSet(setting).Ok);
        WriteOutcome grant = service.CommunicationDelegate(new(estate.OperatorId, estate.OperatorId, estate.BuilderId, "person"));
        Assert.True(grant.Ok, grant.Problem?.Message);
        WriteOutcome changed = service.CommunicationSet(setting with { Authority = grant.Written!.Id });
        Assert.True(changed.Ok, changed.Problem?.Message);
        Assert.Equal(estate.BuilderId, service.Communication(estate.OperatorId).Revisions.Single().By);
        Assert.True(service.CommunicationDelegate(new(estate.OperatorId, estate.OperatorId, estate.BuilderId, "person", Status: "revoked", Expected: grant.Written.Hash)).Ok);
        Assert.False(service.CommunicationSet(setting with { Authority = grant.Written.Id, Expected = changed.Written!.Hash }).Ok);
        Assert.False(service.CommunicationDelegate(new(estate.BuilderId, estate.OperatorId, estate.BuilderId, "person")).Ok);
        Assert.Equal(0, service.Validate().Errors);
    }

    [Fact]
    public void UnsupportedFormatCannotWriteAndDuplicateProfilesAreExposed()
    {
        using var old = new TestEstate();
        using EstateService service = EstateService.Open(old.Root);
        Assert.False(service.CommunicationSet(new(old.OperatorId, "person", new(), old.OperatorId)).Ok);
        Assert.Empty(old.Load().OfType("communication-setting"));
        using var estate = new TestEstate(2);
        using EstateService current = EstateService.Open(estate.Root);
        Assert.True(current.CommunicationSet(new(estate.OperatorId, "person", new(), estate.OperatorId)).Ok);
        estate.Add("communication-setting", estate.OperatorId, $$$"""{"scope":"person","recipient":"{{{estate.OperatorId}}}","values":{"status":"standard"}}""");
        Assert.NotEmpty(current.Communication(estate.OperatorId).Conflicts);
        Assert.Null(current.Communication(estate.OperatorId).Fields["status"].Value);
        Assert.Equal("conflict", current.CommunicationSet(new(estate.OperatorId, "person", new(), estate.OperatorId)).Problem!.Code);
    }
}
