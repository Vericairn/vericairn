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
        string device;
        using (EstateService service = EstateService.Open(fixture.Estate.Root))
        {
            device = Admit(service, person);
            using EstateService.RequestDeviceScope bound = AsDevice(service, device);
            Assert.Equal("conflict", service.PrepareDesktopHandoff(request with { Subject = request.Subject with { Hash = null } }).Problem?.Code);
            prepared = Assert.IsType<DesktopHandoff>(service.PrepareDesktopHandoff(request).Handoff);
            Assert.Null(prepared.OpenedAt);
            Assert.Equal("current", prepared.SubjectState);
            Assert.Equal(request.Draft, prepared.Draft);
            Assert.Equal(device, prepared.PreparedByDevice);
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
            using EstateService.RequestDeviceScope bound = AsDevice(service, device);
            using IDisposable socket = service.ForLocalSocketRequest();
            Assert.Equal(prepared.Id, service.PrepareDesktopHandoff(request).Handoff?.Id);
            Assert.Null(retained.OpenedAt);
            DesktopHandoff opened = Assert.IsType<DesktopHandoff>(service.OpenDesktopHandoff(new(person, retained.Id)).Handoff);
            Assert.NotNull(opened.OpenedAt);
            Assert.Equal(device, opened.OpenedByDevice);
            Assert.Equal(opened.OpenedAt, service.OpenDesktopHandoff(new(person, retained.Id)).Handoff?.OpenedAt);
        }

        using EstateService restarted = EstateService.Open(fixture.Estate.Root);
        Assert.NotNull(Assert.Single(restarted.DesktopHandoffs(person).Handoffs).OpenedAt);
        Assert.Equal(records, fixture.Estate.Load().Records.Count);
    }

    [Fact]
    public void ExpiredHandoffsAndOpeningsAreRemoved()
    {
        using var fixture = new ProgressFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        using EstateService.RequestDeviceScope bound = AsDevice(service, Admit(service, fixture.Estate.OperatorId));
        using IDisposable socket = service.ForLocalSocketRequest();
        var clock = new WorkerClock(fixture.Estate.Clock);
        service.WorkerOptions = service.WorkerOptions with { Clock = clock };
        var request = Request(fixture);
        DesktopHandoff prepared = Assert.IsType<DesktopHandoff>(service.PrepareDesktopHandoff(request).Handoff);
        Assert.NotNull(service.OpenDesktopHandoff(new(request.By, prepared.Id)).Handoff?.OpenedAt);
        string directory = Path.Combine(Path.GetDirectoryName(service.Session.Live.Path)!, "desktop-handoffs", request.By);
        clock.Advance(TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1));
        Assert.Equal(RecordParser.FormatTimestamp(fixture.Estate.Clock.AddDays(30)), Assert.Single(service.DesktopHandoffs(request.By).Handoffs).ExpiresAt);
        Assert.Single(Directory.EnumerateFiles(directory, "*.opened"));

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Empty(service.DesktopHandoffs(request.By).Handoffs);
        Assert.Equal("unknown", service.OpenDesktopHandoff(new(request.By, prepared.Id)).Problem?.Code);
        Assert.Empty(Directory.EnumerateFiles(directory, "*.prepared"));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.opened"));
    }

    [Fact]
    public void KeepsOriginalAtTheCapAndRemovalMakesRoom()
    {
        using var fixture = new ProgressFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        using EstateService.RequestDeviceScope bound = AsDevice(service, Admit(service, fixture.Estate.OperatorId));
        using IDisposable socket = service.ForLocalSocketRequest();
        DesktopHandoffRequest first = Request(fixture);
        Assert.NotNull(service.PrepareDesktopHandoff(first).Handoff);
        for (int index = 1; index < 32; index++)
            Assert.NotNull(service.PrepareDesktopHandoff(Request(fixture)).Handoff);

        DesktopHandoffRequest next = Request(fixture);
        Assert.Equal("limit", service.PrepareDesktopHandoff(next).Problem?.Code);
        Assert.Equal(first.RequestId, service.PrepareDesktopHandoff(first).Handoff?.Id);
        Assert.NotNull(service.OpenDesktopHandoff(new(first.By, first.RequestId)).Handoff?.OpenedAt);
        Assert.True(service.RemoveDesktopHandoff(new(first.By, first.RequestId)).Removed);
        Assert.True(service.RemoveDesktopHandoff(new(first.By, first.RequestId)).Removed);
        Assert.Equal("unknown", service.OpenDesktopHandoff(new(first.By, first.RequestId)).Problem?.Code);
        Assert.NotNull(service.PrepareDesktopHandoff(next).Handoff);
        Assert.Equal(32, service.DesktopHandoffs(first.By).Handoffs.Count);
        string directory = Path.Combine(Path.GetDirectoryName(service.Session.Live.Path)!, "desktop-handoffs", first.By);
        Assert.False(File.Exists(Path.Combine(directory, first.RequestId + ".prepared")));
        Assert.False(File.Exists(Path.Combine(directory, first.RequestId + ".opened")));
    }

    [Fact]
    public void UnsignedLocalCallsCannotPlantOrOpenSomeoneElsesHandoff()
    {
        using var fixture = new ProgressFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        string owner = fixture.Estate.OperatorId;
        string other = fixture.Estate.Add("principal", owner, """{"kind":"human","name":"Other person","roles":["contributor"]}""").Id;
        string phone = Admit(service, owner);
        string desktop = Admit(service, owner);
        string secondDesktop = Admit(service, owner);
        string otherDevice = Admit(service, other);
        DesktopHandoffRequest request = Request(fixture);

        using IDisposable socket = service.ForLocalSocketRequest();
        Assert.Equal("denied", service.PrepareDesktopHandoff(request with { By = other, Task = "Planted locally" }).Problem?.Code);
        Assert.Empty(service.DesktopHandoffs(other).Handoffs);
        using (AsDevice(service, otherDevice))
            Assert.Equal("denied", service.PrepareDesktopHandoff(request).Problem?.Code);

        using (AsDevice(service, phone))
            Assert.Equal(phone, service.PrepareDesktopHandoff(request).Handoff?.PreparedByDevice);
        Assert.Equal("denied", service.OpenDesktopHandoff(new(owner, request.RequestId)).Problem?.Code);
        using (AsDevice(service, otherDevice))
            Assert.Equal("denied", service.OpenDesktopHandoff(new(owner, request.RequestId)).Problem?.Code);
        Assert.Null(Assert.Single(service.DesktopHandoffs(owner).Handoffs).OpenedAt);

        using (AsDevice(service, desktop))
            Assert.Equal(desktop, service.OpenDesktopHandoff(new(owner, request.RequestId)).Handoff?.OpenedByDevice);
        using (AsDevice(service, secondDesktop))
            Assert.Equal(desktop, service.OpenDesktopHandoff(new(owner, request.RequestId)).Handoff?.OpenedByDevice);
        DesktopHandoff observed = Assert.Single(service.DesktopHandoffs(owner).Handoffs);
        Assert.Equal(phone, observed.PreparedByDevice);
        Assert.Equal(desktop, observed.OpenedByDevice);
    }

    private static string Admit(EstateService service, string principal)
    {
        WriteOutcome created = service.DeviceCreate(new(principal, "Handoff fixture device", "file"));
        Assert.True(created.Ok, created.Problem?.Message);
        string id = created.Written!.Id;
        string administrator = service.Session.Load().CurrentEstate!.Body.GetProperty("operator").GetString()!;
        WriteOutcome admitted = service.DeviceAdmit(new(id, administrator, "Handoff fixture admission."));
        Assert.True(admitted.Ok, admitted.Problem?.Message);
        return id;
    }

    private static EstateService.RequestDeviceScope AsDevice(EstateService service, string id) =>
        service.ForDeviceRequest(id, service.Standing(id)!.PublicKey);

    private static DesktopHandoffRequest Request(ProgressFixture fixture) =>
        new(fixture.Estate.OperatorId, Guid.NewGuid().ToString("N"), Pin(fixture.Goal), "Review on the desktop", "Full review needed", "A draft.");

    private static AttentionReference Pin(RecordEnvelope record) => new(record.Type, record.Id, record.ComputedHash);
}
