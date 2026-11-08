// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// <c>POST /v1/history/checkpoints</c> through the daemon (OPS-07): the SDK sees the plan the embedded runtime gives, and a refused
/// adoption comes back as the runtime's problem, with nothing written.
/// </summary>
[Collection("daemon")]
public class HistoryCheckpointApiTests
{
    public HistoryCheckpointApiTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public async Task TheDaemonServesTheAdoptionCheckAndRefusesAnAgentsAdoption()
    {
        using var f = new HistoryFixture();
        await using DaemonHost host = await DaemonTestHome.StartAsync(f.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(DaemonHost.Discover(f.Root)!);

        (HistoryCheckpointResponse? embedded, _) = host.Service.HistoryCheckpoint(new HistoryCheckpointRequest(null, Adopt: true, Check: true));
        (HistoryCheckpointResponse? served, Problem? problem) = client.HistoryCheckpoint(new HistoryCheckpointRequest(null, Adopt: true, Check: true));
        Assert.Null(problem);
        Assert.False(served!.Applied);
        Assert.Equal(embedded!.Witnessed, served.Witnessed);
        Assert.Equal(embedded.Anchored.Select(a => a.Id), served.Anchored.Select(a => a.Id));
        Assert.NotEmpty(served.Anchored);

        (HistoryCheckpointResponse? agents, Problem? refused) = client.HistoryCheckpoint(new HistoryCheckpointRequest(f.Estate.BuilderId, Adopt: true));
        Assert.Null(agents);
        Assert.Equal("refused", refused!.Code);
        Assert.Contains("never an agent", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(host.Service.Session.Load().Records, r => r.Type == "history-checkpoint");
    }

    [Fact]
    public async Task AdoptionIsRefusedOverTheLocalNetworkAndItsCheckIsNot()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        WriteOutcome authority = service.DeviceCreate(new(estate.OperatorId, "Operator test device", "file"));
        Assert.True(authority.Ok, authority.Problem?.Message);
        Assert.True(service.DeviceAdmit(new(authority.Written!.Id, estate.OperatorId, "First authority.")).Ok);
        var keys = new FileKeyStore(VericairnHome.Path()); // DaemonTestHome created the test process's isolated home.
        string device = Ulid.NewUlid(estate.Clock.AddSeconds(1)).Value;
        byte[] publicKey = keys.Create(device);
        try
        {
            var body = DeviceRecords.Body(estate.OperatorId, "Operator's phone", "ios", DeviceKeyStores.IosSecureEnclaveKind, RecordSigning.EncodePublicKey(publicKey), ["embedded", "local-network"], 3, "none");
            estate.Add("device", estate.OperatorId, body.ToJsonString(), id: device, signer: new StoreSigner(keys, device));
            Assert.True(service.DeviceAdmit(new(device, estate.OperatorId, "The operator's phone.")).Ok);
            DeviceStanding standing = service.Standing(device)!;
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/v1/history/checkpoints";
            context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice(standing.Identity, estate.OperatorId, standing.PublicKey, LocalNetwork: true);
            var scope = new RequestScope(service, new LocalNetworkLedger(estate.Root));

            var adopt = new HistoryCheckpointRequest(estate.OperatorId, Adopt: true, ExpectPlan: "sha256:" + new string('0', 64));
            object? denied = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, adopt), _ =>
                throw new InvalidOperationException("An adoption over the local network must not reach its handler."));
            Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
            Assert.Contains("refused over the local network", Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult>(denied).ResponseContent, StringComparison.Ordinal);

            var check = new HistoryCheckpointRequest(null, Adopt: true, Check: true);
            bool reached = false;
            await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, check), _ => { reached = true; return ValueTask.FromResult<object?>(null); });
            Assert.True(reached);
            Assert.DoesNotContain(service.Session.Load().Records, r => r.Type == "history-checkpoint");
        }
        finally { keys.Remove(device); }
    }
}
