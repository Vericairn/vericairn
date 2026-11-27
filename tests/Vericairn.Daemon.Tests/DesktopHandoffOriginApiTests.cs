// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class DesktopHandoffOriginApiTests
{
    [Fact]
    public async Task TheSocketRefusesPlantingAndOnlyTheAuthenticatedOpenerCanMarkAHandoffOpened()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
        string owner = harness.Estate.OperatorId;
        string other = harness.Estate.Add("principal", owner, """{"kind":"human","name":"Other person","roles":["contributor"]}""").Id;
        RuntimeConnection paired = harness.Pair(owner);
        RuntimeConnection foreign = harness.Pair(other, "Other person's phone");
        using VericairnClient phone = harness.Connections.Connect(paired);
        using VericairnClient otherPhone = harness.Connections.Connect(foreign);
        using VericairnClient otherSocket = new(harness.Host.Endpoint, harness.Signer(foreign));
        using VericairnClient desktop = new(harness.Host.Endpoint,
            new DeviceRequestSigner(new FileKeyStore(VericairnHome.Path()), harness.OperatorDevice));
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordEnvelope subject = harness.Estate.Operator;
        string id = Guid.NewGuid().ToString("N");
        var request = new DesktopHandoffRequest(owner, id, new(subject.Type, subject.Id, subject.ComputedHash),
            "Inspect the review", "Review on the desktop", "Private draft");

        DesktopHandoffResponse planted = await harness.Socket.PrepareDesktopHandoffAsync(request with { By = other, Task = "Planted locally" }, token);
        Assert.Equal("denied", planted.Problem?.Code);
        Assert.Empty((await otherPhone.DesktopHandoffsAsync(other, token)).Handoffs);

        DesktopHandoffResponse prepared = await phone.PrepareDesktopHandoffAsync(request, token);
        Assert.Equal(paired.Device, prepared.Handoff?.PreparedByDevice);
        ClientRefusedException unsigned = await Assert.ThrowsAsync<ClientRefusedException>(
            () => harness.Socket.OpenDesktopHandoffAsync(new(owner, id), token));
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Contains("admitted Workbench device", unsigned.Message, StringComparison.Ordinal);
        await Refused(() => otherSocket.OpenDesktopHandoffAsync(new(owner, id), token));
        await Refused(() => phone.OpenDesktopHandoffAsync(new(owner, id), token));
        Assert.Null(Assert.Single((await phone.DesktopHandoffsAsync(owner, token)).Handoffs).OpenedAt);

        DesktopHandoffResponse opened = await desktop.OpenDesktopHandoffAsync(new(owner, id), token);
        Assert.Equal(harness.OperatorDevice, opened.Handoff?.OpenedByDevice);
        DesktopHandoff kept = Assert.Single((await phone.DesktopHandoffsAsync(owner, token)).Handoffs);
        Assert.NotNull(kept.OpenedAt);
        Assert.Equal(harness.OperatorDevice, kept.OpenedByDevice);
        Assert.Equal(paired.Device, kept.PreparedByDevice);

        await Refused(() => harness.Socket.RemoveDesktopHandoffAsync(new(owner, id), token));
        await Refused(() => otherSocket.RemoveDesktopHandoffAsync(new(owner, id), token));
        Assert.True((await desktop.RemoveDesktopHandoffAsync(new(owner, id), token)).Removed);
        Assert.Empty((await phone.DesktopHandoffsAsync(owner, token)).Handoffs);
    }

    private static async Task Refused(Func<Task> act) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Assert.ThrowsAsync<ClientRefusedException>(act)).StatusCode);
}
