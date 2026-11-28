// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Remote;
using Vericairn.Runtime.Tests;
using Vericairn.Runtime.Workers;
using Vericairn.Sdk;
using Vericairn.TestSupport;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class SignedInterventionApiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThePhonesPresenceSignaturePausesDispatchWithTheDesktopPresenceRuleStillInForce(bool overListener)
    {
        await using RemoteApiFixture f = await RemoteApiFixture.StartAsync(overListener);
        string person = f.AddPerson("Phone operator", "operator");
        using var desktop = f.RequireDesktopPresence(person);
        var (client, key, device) = f.SignedPhone(person);
        using var phone = client;
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordEnvelope goal = f.SeedGoal(person);
        GoalResponse active = (await phone.GoalAsync(goal.Id, token))!;
        string pin = Assert.IsType<string>(active.ActiveStatusHash);

        ClientRefusedException refused = await Assert.ThrowsAsync<ClientRefusedException>(() =>
            phone.GoalPauseAsync(goal.Id, new(person) { ExpectedActiveStatusHash = pin }, token));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Contains("presence on the runtime's machine", refused.Message, StringComparison.Ordinal);
        Assert.Equal("active", (await phone.GoalAsync(goal.Id, token))!.Json.GetProperty("status").GetString());

        WriteOutcome outcome = await DeviceSignedRecords.WriteWithPresenceAsync(phone,
            InterventionRecords.Pause(person, goal.Id, goal.ComputedHash, pin, "Pause reviewed on the phone."),
            key, device, cancellationToken: token);
        AssertSigned(f, outcome, device);
        GoalResponse paused = (await phone.GoalAsync(goal.Id, token))!;
        Assert.Equal("paused", paused.Json.GetProperty("status").GetString());
        Assert.Null(paused.ActiveStatusHash);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ThePhonesPresenceSignatureReachesTheSupervisingWorkerAndItsEffectIsObserved(bool overListener, bool contain)
    {
        await using RemoteApiFixture f = await RemoteApiFixture.StartAsync(overListener);
        string person = f.AddPerson("Phone operator", "operator");
        using var desktop = f.RequireDesktopPresence(person);
        var (client, key, device) = f.SignedPhone(person);
        using var phone = client;
        CancellationToken token = TestContext.Current.CancellationToken;
        var run = f.SeedRun(f.SeedGoal(person));
        string runId = run.Intent.Id;
        string reason = contain ? "Contain reviewed run" : "Cancel reviewed run";
        string requestId = Guid.NewGuid().ToString("N");
        RunRow row = Assert.Single((await phone.RunsAsync(token)).Runs);
        Assert.Equal((run.Latest.Id, run.Latest.ComputedHash), (row.OperationRevision, row.OperationHash));
        var directory = new RunDirectory(f.Service.Session.Manifest.Estate, runId);
        var options = new WorkerOptions { Heartbeat = TimeSpan.FromMilliseconds(100), Grace = TimeSpan.FromSeconds(5) };
        f.Service.WorkerOptions = options;

        ClientRefusedException refused = await Assert.ThrowsAsync<ClientRefusedException>(() =>
            phone.RunStopAsync(runId, new(person, reason, contain ? 0 : null) { ExpectedOperationHash = row.OperationHash }, token));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Contains("presence on the runtime's machine", refused.Message, StringComparison.Ordinal);
        Assert.Null(directory.ReadStop());

        directory.Create();
        string report = Path.Combine(f.Estate.Root, "worker-ready.jsonl");
        using var changed = new FileSignal(f.Estate.Root);
        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(token);
        var environment = WorkerEnvironment.Build(WorkerEnvironment.Current());
        environment["VERICAIRN_STOP_FILE"] = directory.StopFile;
        RunSupervisor worker = RunSupervisor.Start(runId, FakeHarness.Command(contain ? "ignore-stop" : "ack-stop", report),
            f.Estate.Root, environment, new RunApiTests.StandInLauncher(), directory, options, 5, null);
        Task<RunOutcome?> observed = Task.Run(() => worker.Supervise(abandon.Token), CancellationToken.None);
        try
        {
            await changed.UntilAsync(() => File.Exists(report), "the stand-in waiting for a stop", token);
            WriteOutcome outcome = await DeviceSignedRecords.WriteWithPresenceAsync(phone,
                InterventionRecords.Stop(person, runId, row.OperationRevision!, row.OperationHash!, requestId, contain, reason),
                key, device, cancellationToken: token);
            AssertSigned(f, outcome, device);
            StopRequest retained = Assert.IsType<StopRequest>(directory.ReadStop());
            Assert.Equal(contain ? 0 : (int?)null, retained.GraceSeconds);

            RunOutcome ended = Assert.IsType<RunOutcome>(await observed.WaitAsync(TestTiming.HangTimeout, token));
            RunStop actual = Assert.IsType<RunStop>(ended.Stop);
            Assert.Equal(reason, actual.Reason);
            Assert.NotNull(actual.TransmittedAt);
            Assert.NotNull(actual.ContainedAt);
            if (contain) Assert.NotNull(actual.EscalatedAt);
            else Assert.NotNull(actual.AcknowledgedAt);
            RunStop read = Assert.IsType<RunStop>(Assert.Single((await phone.RunsAsync(token)).Runs).Stop);
            Assert.Equal(actual, read);
        }
        finally
        {
            if (!observed.IsCompleted)
            {
                directory.RequestStop(new(RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), person, "Fixture cleanup", 0));
                worker.EscalateNow();
                await observed.WaitAsync(TestTiming.HangTimeout, CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task AnUnattendedRequestKeyCannotSignAReviewedStop()
    {
        await using RemoteApiFixture f = await RemoteApiFixture.StartAsync(overListener: false);
        string person = f.Estate.OperatorId;
        var (client, key, device) = f.UnattendedPhone(person);
        using var phone = client;
        CancellationToken token = TestContext.Current.CancellationToken;
        var (request, directory) = ReviewedCancel(f, person);

        RecordPrepared? prepared = null;
        WriteOutcome refused = await DeviceSignedRecords.WriteAsync(phone, request, key, device,
            plan => { prepared = plan; return true; }, token);

        Assert.NotNull(prepared);
        Assert.Null(refused.Written);
        Assert.Equal("refused", refused.Problem?.Code);
        Assert.Contains("admitted presence device", refused.Problem?.Message, StringComparison.Ordinal);
        Assert.Empty(f.Estate.Load().OfType(request.Type));
        Assert.Null(directory.ReadStop());
    }

    [Fact]
    public async Task TheUnsignedRecordRouteRefusesAReviewedStop()
    {
        await using RemoteApiFixture f = await RemoteApiFixture.StartAsync(overListener: false);
        using var local = new VericairnClient(new HttpClient(new PolicyAdoptionApiTests.RouteHandler(f.Service))
            { BaseAddress = new Uri("http://fixture.invalid") });
        CancellationToken token = TestContext.Current.CancellationToken;
        var (request, directory) = ReviewedCancel(f, f.Estate.OperatorId);

        WriteOutcome refused = await local.NewRecordAsync(request, token);

        Assert.Null(refused.Written);
        Assert.Equal("refused", refused.Problem?.Code);
        Assert.Contains("must be prepared and signed", refused.Problem?.Message, StringComparison.Ordinal);
        Assert.Empty(f.Estate.Load().OfType(request.Type));
        Assert.Null(directory.ReadStop());
    }

    [Fact]
    public async Task TwoSignedPlansForTheSameRequestIdentityCanWriteOnlyOneStopRecord()
    {
        await using RemoteApiFixture f = await RemoteApiFixture.StartAsync(overListener: false);
        string person = f.Estate.OperatorId;
        var (client, key, device) = f.SignedPhone(person);
        using var phone = client;
        CancellationToken token = TestContext.Current.CancellationToken;
        var (request, directory) = ReviewedCancel(f, person);

        RecordPrepared first = Assert.IsType<RecordPrepared>((await phone.PrepareRecordAsync(request, token)).Prepared);
        RecordPrepared second = Assert.IsType<RecordPrepared>((await phone.PrepareRecordAsync(request, token)).Prepared);
        Assert.NotEqual(first.Id, second.Id);

        Task<WriteOutcome> Submit(RecordPrepared plan) => phone.NewSignedRecordAsync(
            new SignedRecordRequest(plan.Plan, new StoreSigner(key, device).Sign(RecordSigning.Digest(plan.Hash)).Signature)
            { Device = device }, token);

        RecordWritten written = RemoteApiFixture.Ok(await Submit(first));
        Assert.Equal(first.Id, written.Id);
        StopRequest stop = Assert.IsType<StopRequest>(directory.ReadStop());

        WriteOutcome repeated = await Submit(second);
        Assert.Null(repeated.Written);
        Assert.Equal("conflict", repeated.Problem?.Code);
        Assert.Contains("already has a signed record", repeated.Problem?.Message, StringComparison.Ordinal);
        Assert.Equal(first.Id, Assert.Single(f.Estate.Load().OfType(request.Type)).Id);
        Assert.Equal(stop, directory.ReadStop());

        RecordPrepareResponse fresh = await phone.PrepareRecordAsync(request, token);
        Assert.Null(fresh.Prepared);
        Assert.Equal("conflict", fresh.Problem?.Code);
    }

    [Fact]
    public async Task APhoneWithoutAPresenceKeyExplainsTheRefusalAndOffersADesktopHandoff()
    {
        await using RemoteApiFixture f = await RemoteApiFixture.StartAsync(overListener: true);
        string person = f.AddPerson("Phone operator", "operator");
        using var desktop = f.RequireDesktopPresence(person);
        RecordEnvelope goal = f.SeedGoal(person);
        f.SeedRun(goal);
        RemoteViewModel remote = await f.PairRemoteWithoutPresence(person);
        remote.SelectedAssignment = Assert.Single(remote.Assignments);
        await remote.OpenAssignmentCommand.ExecuteAsync();
        remote.InterventionTarget = Assert.Single(remote.InterventionTargets);
        await remote.ReviewPauseCommand.ExecuteAsync();
        if (remote.SubmitInterventionCommand.CanExecute(null)) await remote.SubmitInterventionCommand.ExecuteAsync();

        Assert.Contains("presence", remote.InterventionMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inspect", remote.InterventionMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("handoff", remote.InterventionMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("401", remote.InterventionMessage, StringComparison.Ordinal);
        Assert.Equal("active", f.Service.Goal(goal.Id)!.Json.GetProperty("status").GetString());
        Assert.DoesNotContain(f.Writes(), w => w.Operation is "POST /v1/records/signed" or "POST /v1/goals/{id}/pause");
    }

    private static (NewRecordRequest Request, RunDirectory Directory) ReviewedCancel(RemoteApiFixture f, string person)
    {
        var run = f.SeedRun(f.SeedGoal(person));
        var request = InterventionRecords.Stop(person, run.Intent.Id, run.Latest.Id, run.Latest.ComputedHash,
            Guid.NewGuid().ToString("N"), contain: false, "Cancel reviewed run");
        return (request, new RunDirectory(f.Service.Session.Manifest.Estate, run.Intent.Id));
    }

    private static void AssertSigned(RemoteApiFixture f, WriteOutcome outcome, string device)
    {
        RecordWritten written = RemoteApiFixture.Ok(outcome);
        Assert.Equal(device, Assert.Single(f.Estate.Load().ById[written.Id].Signatures).Device);
        LocalNetworkWrite ledger = Assert.Single(f.Writes(), w => w.Operation == "POST /v1/records/signed");
        Assert.Equal((written.Id, written.Hash, "written"), (ledger.Record, ledger.Hash, ledger.State));
    }
}

internal sealed partial class RemoteApiFixture
{
    internal (VericairnClient Client, FileKeyStore Key, string Device) UnattendedPhone(string person)
    {
        var key = new FileKeyStore(Path.Combine(Estate.Root, "phone-fixture"));
        string device = AdmitFixtureDevice(person, "Phone request device", key, ["local-network"]);
        return (DeviceClient(device), key, device);
    }

    internal (VericairnClient Client, IDeviceKeyStore Key, string Device) SignedPhone(string person)
    {
        if (listener is not null)
        {
            var key = listener.AndroidPresence();
            RuntimeConnection connection = listener.Connections.Add(listener.Address, listener.Issue(person).Code,
                new PairingOptions("Presence phone") { PresenceKeyStore = key });
            return (listener.Connections.Connect(connection), key, Assert.IsType<string>(connection.PresenceDevice));
        }

        string home = Path.Combine(Estate.Root, "phone-fixture");
        var requestKey = new FileKeyStore(home);
        string requestDevice = AdmitFixtureDevice(person, "Phone request device", requestKey, ["local-network"]);
        // A separately enrolled desktop presence stand-in lets the mapped route exercise signing without native attestation.
        var presenceKey = new PresenceKeyStandIn(home, SecureEnclaveCustody.Kind);
        string presenceDevice = AdmitFixtureDevice(person, "Presence stand-in", presenceKey, ["embedded"], "required", "macos");
        return (DeviceClient(requestDevice), presenceKey, presenceDevice);
    }

    internal async Task<RemoteViewModel> PairRemoteWithoutPresence(string person)
    {
        LocalNetworkHarness harness = Assert.IsType<LocalNetworkHarness>(listener);
        var remote = new RemoteViewModel(new LocalNetworkConnections(harness.DeviceHome, harness.DeviceKeys))
        {
            Address = harness.Address,
            Code = harness.Issue(person).Code,
        };
        await remote.PairCommand.ExecuteAsync();
        Assert.Equal(ConnectionStates.Current, remote.ConnectionState);
        return remote;
    }
}
