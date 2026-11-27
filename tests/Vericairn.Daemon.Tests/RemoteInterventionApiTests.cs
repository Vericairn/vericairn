// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class RemoteInterventionApiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DesktopHandoffRoutesBindThePersonAndKeepThePreparationOutcome(bool overListener)
    {
        await using RemoteApiFixture f = await RemoteApiFixture.StartAsync(overListener);
        string owner = f.AddPerson("Handoff owner");
        string other = f.AddPerson("Other person");
        using var presence = f.RequireDesktopPresence(owner);
        using VericairnClient phone = f.Phone(owner, "Owner's phone");
        using VericairnClient foreign = f.Phone(other, "Other phone");
        using VericairnClient agent = f.Phone(f.Estate.BuilderId, "Agent device");
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordEnvelope subject = f.Estate.Operator;
        string id = Guid.NewGuid().ToString("N");
        var request = new DesktopHandoffRequest(owner, id, new(subject.Type, subject.Id, subject.ComputedHash),
            "Inspect the revision", "It needs the desktop", "Private draft", []);

        DesktopHandoffResponse prepared = await phone.PrepareDesktopHandoffAsync(request, token);
        Assert.Null(prepared.Problem);
        Assert.Equal(id, prepared.Handoff?.Id);
        Assert.Equal(id, (await phone.PrepareDesktopHandoffAsync(request, token)).Handoff?.Id);
        Assert.Equal("conflict", (await phone.PrepareDesktopHandoffAsync(request with { Task = "Different task" }, token)).Problem?.Code);
        var wrongPin = request.Subject with { Hash = "sha256:" + new string('0', 64) };
        Assert.Equal("conflict", (await phone.PrepareDesktopHandoffAsync(request with { RequestId = Guid.NewGuid().ToString("N"), Subject = wrongPin }, token)).Problem?.Code);
        Assert.Equal("Private draft", Assert.Single((await phone.DesktopHandoffsAsync(owner, token)).Handoffs).Draft);

        await Denied(() => foreign.DesktopHandoffsAsync(owner, token), "requesting device's principal");
        await Denied(() => foreign.PrepareDesktopHandoffAsync(request, token), "acts for");
        Assert.Empty((await foreign.DesktopHandoffsAsync(other, token)).Handoffs);
        await Denied(() => agent.DesktopHandoffsAsync(owner, token), "requesting device's principal");
        await Denied(() => agent.PrepareDesktopHandoffAsync(request, token), "acts for");
        Assert.Equal("denied", (await agent.DesktopHandoffsAsync(f.Estate.BuilderId, token)).Problem?.Code);
        Assert.Equal("denied", (await agent.PrepareDesktopHandoffAsync(request with { By = f.Estate.BuilderId }, token)).Problem?.Code);

        await Denied(() => phone.OpenDesktopHandoffAsync(new(owner, id), token), "Workbench");
        Assert.Null(Assert.Single((await phone.DesktopHandoffsAsync(owner, token)).Handoffs).OpenedAt);

        LocalNetworkWrite[] preparations = f.Writes().Where(w => w.Operation == "POST /v1/handoff/desktop" && w.Principal == owner).ToArray();
        Assert.Equal(2, preparations.Count(w => w.State == "written"));
        Assert.Equal(2, preparations.Count(w => w.State == "failed"));
        Assert.DoesNotContain(f.Writes(), w => w.Operation == "POST /v1/handoff/desktop/open");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewedPauseAndStopPinsReachTheWriteBoundary(bool overListener)
    {
        await using RemoteApiFixture f = await RemoteApiFixture.StartAsync(overListener);
        string owner = f.Estate.OperatorId;
        using VericairnClient phone = f.Phone(owner, "Owner's phone");
        using VericairnClient foreign = f.Phone(f.AddPerson("Other person"), "Other phone");
        using VericairnClient agent = f.Phone(f.Estate.BuilderId, "Agent device");
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordEnvelope goal = f.SeedGoal(owner);
        var run = f.SeedRun(goal);
        string firstGoalPin = (await phone.GoalAsync(goal.Id, token))!.ActiveStatusHash!;
        string firstRunPin = Assert.Single((await phone.RunsAsync(token)).Runs).OperationHash!;
        Assert.NotNull(firstGoalPin);
        Assert.Equal(run.Latest.ComputedHash, firstRunPin);

        var pause = new GoalActionRequest(owner, "Hold dispatch") { ExpectedActiveStatusHash = firstGoalPin };
        var stop = new RunStopRequest(owner, "Stop the selected run", 0) { ExpectedOperationHash = firstRunPin };
        foreach (VericairnClient wrong in new[] { foreign, agent })
        {
            await Denied(() => wrong.GoalPauseAsync(goal.Id, pause, token), "acts for");
            await Denied(() => wrong.RunStopAsync(run.Intent.Id, stop, token), "acts for");
        }

        f.Estate.Add("goal-status", owner, """{"status":"paused","note":"Changed since review."}""", goal.Reference);
        RecordEnvelope active = f.Estate.Add("goal-status", owner, """{"status":"active","note":"Active again."}""", goal.Reference);
        JsonObject newRunBody = JsonNode.Parse(run.Latest.Body.GetRawText())!.AsObject();
        newRunBody["status"] = "uncertain";
        RecordEnvelope changed = f.Estate.Add("operation", owner, newRunBody.ToJsonString(), supersedes: run.Latest.Id);

        WriteOutcome stalePause = await phone.GoalPauseAsync(goal.Id, pause, token);
        Assert.False(stalePause.Ok);
        Assert.Contains("reviewed active goal status changed", stalePause.Problem?.Message, StringComparison.Ordinal);
        RunStopResponse staleStop = await phone.RunStopAsync(run.Intent.Id, stop, token);
        Assert.False(staleStop.Ok);
        Assert.Contains("reviewed run operation changed", staleStop.Problem?.Message, StringComparison.Ordinal);
        Assert.Null(new RunDirectory(f.Service.Session.Manifest.Estate, run.Intent.Id).ReadStop());
        Assert.Equal(active.ComputedHash, (await phone.GoalAsync(goal.Id, token))!.ActiveStatusHash);
        Assert.Equal(changed.ComputedHash, Assert.Single((await phone.RunsAsync(token)).Runs).OperationHash);

        WriteOutcome correctPause = await phone.GoalPauseAsync(goal.Id, pause with { ExpectedActiveStatusHash = active.ComputedHash }, token);
        _ = RemoteApiFixture.Ok(correctPause);
        Assert.Null((await phone.GoalAsync(goal.Id, token))!.ActiveStatusHash);
        RunStopResponse correctStop = await phone.RunStopAsync(run.Intent.Id, stop with { ExpectedOperationHash = changed.ComputedHash }, token);
        Assert.True(correctStop.Ok, correctStop.Message);
        Assert.Equal(0, new RunDirectory(f.Service.Session.Manifest.Estate, run.Intent.Id).ReadStop()?.GraceSeconds);
    }

    private static async Task Denied(Func<Task> call, string why)
    {
        ClientRefusedException denied = await Assert.ThrowsAsync<ClientRefusedException>(call);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Contains(why, denied.Message, StringComparison.Ordinal);
    }
}

/// <summary>The same SDK serialization and endpoint filters over a real TLS listener or in memory for restricted builders.</summary>
internal sealed partial class RemoteApiFixture(TestEstate estate, EstateService service, LocalNetworkHarness? listener) : IAsyncDisposable
{
    private static readonly string[] TestScope = ["tests"];
    private static readonly string[] Deliverables = ["evidence"];

    internal TestEstate Estate { get; } = estate;
    internal EstateService Service { get; } = service;

    private string LedgerDirectory => listener is null ? Estate.Root : Path.GetDirectoryName(DaemonHost.EndpointFilePath(Estate.Root))!;

    internal static async Task<RemoteApiFixture> StartAsync(bool overListener)
    {
        DaemonTestHome.Ensure();
        if (overListener)
        {
            LocalNetworkHarness listener = await LocalNetworkHarness.StartAsync(recordFormat: 3);
            return new(listener.Estate, listener.Host.Service, listener);
        }

        var estate = new TestEstate(3);
        EstateService service = EstateService.Open(estate.Root);
        var f = new RemoteApiFixture(estate, service, null);
        f.Admit(estate.OperatorId, "Fixture authority");
        return f;
    }

    internal string AddPerson(string name, string role = "contributor") => Estate.Add("principal", Estate.OperatorId,
        JsonSerializer.Serialize(new { kind = "human", name, roles = (string[])[role] })).Id;

    internal VericairnClient Phone(string by, string name)
    {
        if (listener is not null) return listener.Connections.Connect(listener.Pair(by, name));
        var keys = new FileKeyStore(Path.Combine(Estate.Root, "phone-fixture"));
        string id = AdmitFixtureDevice(by, name, keys, ["local-network"]);
        return DeviceClient(id);
    }

    private VericairnClient DeviceClient(string deviceId)
    {
        DeviceStanding standing = Service.Standing(deviceId)!;
        var device = new AuthenticatedDevice(standing.Identity, standing.Principal, standing.PublicKey, LocalNetwork: true);
        return new VericairnClient(new HttpClient(new PolicyAdoptionApiTests.RouteHandler(Service, new RequestScope(Service, new LocalNetworkLedger(LedgerDirectory)), device))
            { BaseAddress = new Uri("http://fixture.invalid") });
    }

    private string AdmitFixtureDevice(string by, string name, IDeviceKeyStore keys, string[] profiles, string? presence = null)
    {
        string id = Ulid.NewUlid(Estate.Clock.AddSeconds(1)).Value;
        byte[] spki = keys.Create(id);
        JsonObject body = DeviceRecords.Body(by, name, "ios", keys.Kind, RecordSigning.EncodePublicKey(spki), profiles, 3, presence);
        Estate.Add("device", by, body.ToJsonString(), id: id, signer: new StoreSigner(keys, id));
        Ok(Service.DeviceAdmit(new(id, Estate.OperatorId, "Test phone admission.")));
        return id;
    }

    internal IDisposable RequireDesktopPresence(string by)
    {
        var keys = new FileKeyStore(VericairnHome.Path());
        string device = Ulid.NewUlid(Estate.Clock.AddSeconds(1)).Value;
        var cleanup = new FixtureKey(keys, device);
        try
        {
            byte[] spki = keys.Create(device);
            JsonObject body = DeviceRecords.Body(by, "Presence stand-in", "macos", SecureEnclaveCustody.Kind, RecordSigning.EncodePublicKey(spki), ["embedded"], 3, "required");
            Estate.Add("device", by, body.ToJsonString(), id: device, signer: new StoreSigner(keys, device));
            Ok(Service.DeviceAdmit(new(device, Estate.OperatorId, "Presence stand-in admission.")));
            Assert.NotNull(Service.PresenceRefusal(by));
            return cleanup;
        }
        catch
        {
            cleanup.Dispose();
            throw;
        }
    }

    internal IReadOnlyList<LocalNetworkWrite> Writes() => LocalNetworkLedger.Read(LedgerDirectory);

    internal RecordEnvelope SeedGoal(string owner)
    {
        RecordEnvelope goal = Estate.Add("goal", Estate.OperatorId, JsonSerializer.Serialize(new
        {
            title = "Inspect phone work", objective = "Try the reviewed intervention.", completion = new[] { new { evidence = "Tests", check = "Run them." } },
            constraints = Array.Empty<string>(), kind = "finite", sponsor = owner, budget = new { wallMinutes = 60, attemptsPerStep = 3 },
            plan = new { policy = "proposed-by-planner", approval = "sponsor-approves" }, inputs = Array.Empty<string>(),
        }));
        Estate.Add("goal-status", owner, """{"status":"active","note":"Ready for review."}""", goal.Reference);
        return goal;
    }

    internal (RecordEnvelope Intent, RecordEnvelope Latest) SeedRun(RecordEnvelope goal)
    {
        var assignment = SeedAssignment(goal);
        JsonObject body = JsonSerializer.SerializeToNode(new
        {
            provider = "generic", action = "agent-run", identity = "phone-observation", intent = "Inspect the phone view.",
            target = new { assignment = assignment.Id, principal = Estate.BuilderId, workspace = "widget branch work/phone", goal = goal.Id },
            status = "not-started", cap = new { wallMinutes = 5, attemptsPerStep = 3 }, evidence = Array.Empty<string>(),
        })!.AsObject();
        RecordEnvelope intent = Estate.Add("operation", Estate.OperatorId, body.ToJsonString());
        body["status"] = "dispatched";
        body["dispatchedAt"] = RecordParser.FormatTimestamp(Estate.Clock.AddSeconds(1));
        RecordEnvelope dispatched = Estate.Add("operation", Estate.OperatorId, body.ToJsonString(), supersedes: intent.Id);
        return (intent, dispatched);
    }

    internal RecordEnvelope SeedAssignment(RecordEnvelope goal)
    {
        RecordEnvelope order = Estate.Add("work-order", Estate.OperatorId, JsonSerializer.Serialize(new
        {
            key = "PHONE-RUN", kind = "ordinary", title = "Phone run", intent = "Inspect the run.", scope = TestScope,
            exitQuestion = "Was the expected effect observed?", inputs = new[] { new { type = "goal", id = goal.Id } }, deliverables = Deliverables,
        }));
        return Estate.Add("assignment", Estate.OperatorId, $$$"""
            {"contributor":"{{{Estate.BuilderId}}}","brief":{"outcome":"Inspect the phone intervention.","sources":[],"permittedChanges":["path:tests/**"],"prohibitedEffects":["no publication"],"workspace":["widget branch work/phone"],"limits":[],"expectedOutputs":["observations"],"reviewCriteria":["Check the effect."],"recovery":"Inspect the run."}}
            """, order.Reference);
    }

    internal static RecordWritten Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem?.Message + "\n" + string.Join('\n', outcome.Problem?.Findings.Select(f => f.Message) ?? []));
        return Assert.IsType<RecordWritten>(outcome.Written);
    }

    private string Admit(string by, string name)
    {
        string id = Ok(Service.DeviceCreate(new(by, name, "file", ["local-network", "embedded"]))).Id;
        Ok(Service.DeviceAdmit(new(id, Estate.OperatorId, "Fixture admission.")));
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        if (listener is not null) await listener.DisposeAsync();
        else
        {
            Service.Dispose();
            Estate.Dispose();
        }
    }

    private sealed class FixtureKey(FileKeyStore keys, string id) : IDisposable
    {
        public void Dispose() => keys.Remove(id);
    }
}
