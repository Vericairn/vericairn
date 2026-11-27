// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class DesktopHandoffTests
{
    [Fact]
    public void RetainsExactContextAcrossRestartAndObservesDesktopOpeningSeparately()
    {
        using var fixture = new ProgressFixture();
        string person = fixture.Estate.OperatorId;
        var request = new DesktopHandoffRequest(person, Guid.NewGuid().ToString("N"), Pin(fixture.Goal),
            "Review the proposed change", "The phone cannot present the full review", "Keep exact draft",
            [Pin(fixture.Assignment)]);
        DesktopHandoff prepared;
        using (EstateService service = EstateService.Open(fixture.Estate.Root))
        {
            Assert.Equal("conflict", service.PrepareDesktopHandoff(request with { Subject = request.Subject with { Hash = null } }).Problem?.Code);
            prepared = Assert.IsType<DesktopHandoff>(service.PrepareDesktopHandoff(request).Handoff);
            Assert.Null(prepared.OpenedAt);
            Assert.Equal("current", prepared.SubjectState);
            Assert.Equal(request.Draft, prepared.Draft);
            Assert.Equal("conflict", service.PrepareDesktopHandoff(request with { Draft = "Different" }).Problem?.Code);
            Assert.Empty(service.DesktopHandoffs(fixture.Estate.BuilderId).Handoffs);
        }

        fixture.Set("goal", """{"cadence":{"minutes":10},"types":["not-measurable"]}""");
        int records = fixture.Estate.Load().Records.Count;

        using (EstateService service = EstateService.Open(fixture.Estate.Root))
        {
            DesktopHandoff retained = Assert.Single(service.DesktopHandoffs(person).Handoffs);
            Assert.Equal(prepared.Id, retained.Id);
            Assert.Equal(prepared.PreparedAt, retained.PreparedAt);
            Assert.Equal(request.Subject, retained.Subject);
            Assert.Equal("superseded", retained.SubjectState);
            Assert.Equal(request.Evidence, retained.Evidence);
            Assert.Equal(prepared.Id, service.PrepareDesktopHandoff(request).Handoff?.Id);
            Assert.Null(retained.OpenedAt);
            DesktopHandoff opened = Assert.IsType<DesktopHandoff>(service.OpenDesktopHandoff(new(person, retained.Id)).Handoff);
            Assert.NotNull(opened.OpenedAt);
            Assert.Equal(opened.OpenedAt, service.OpenDesktopHandoff(new(person, retained.Id)).Handoff?.OpenedAt);
        }

        using EstateService restarted = EstateService.Open(fixture.Estate.Root);
        Assert.NotNull(Assert.Single(restarted.DesktopHandoffs(person).Handoffs).OpenedAt);
        Assert.Equal(records, fixture.Estate.Load().Records.Count);
    }

    private static AttentionReference Pin(RecordEnvelope record) => new(record.Type, record.Id, record.ComputedHash);
}
