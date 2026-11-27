// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class SignedInterventionTests
{
    [Fact]
    public async Task APausedAndResumedGoalInvalidatesAPhonePlanWhileItWaitsForTheTreeLock()
    {
        using var estate = new TestEstate(3);
        DirectoryInfo homes = Directory.CreateTempSubdirectory("vericairn-signed-intervention-");
        try
        {
            string person = estate.OperatorId;
            string device = Ulid.NewUlid(estate.Clock.AddSeconds(1)).Value;
            var keys = new FileKeyStore(homes.FullName);
            string publicKey = RecordSigning.EncodePublicKey(keys.Create(device));
            var signer = new StoreSigner(keys, device);
            var body = DeviceRecords.Body(person, "Phone presence test device", "ios", SecureEnclaveCustody.Kind,
                publicKey, ["local-network"], 3, "required");
            RecordEnvelope identity = estate.Add("device", person, body.ToJsonString(), id: device, signer: signer);
            RecordEnvelope admission = estate.Add("admission", person,
                $$"""{"principal":"{{person}}","status":"admitted","note":"Admit the test device."}""", identity.Reference, signer: signer);
            estate.NameFirstAuthority(admission.Id);
            RecordEnvelope goal = estate.Add("goal", person,
                $$"""{"title":"Inspect the run","objective":"Review the progress","completion":[{"evidence":"review","check":"Inspect"}],"constraints":[],"kind":"finite","sponsor":"{{person}}","budget":{"wallMinutes":60,"attemptsPerStep":2},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}""");
            RecordEnvelope active = estate.Add("goal-status", person, """{"status":"active","note":"Activated for review."}""", goal.Reference);

            using EstateService reader = EstateService.Open(estate.Root, null, Path.Combine(homes.FullName, "reader"));
            using EstateService writer = EstateService.Open(estate.Root, null, Path.Combine(homes.FullName, "writer"));
            reader.PreparedClock = new WorkerClock(estate.Clock.AddSeconds(1));
            RecordPrepareResponse preparation;
            using (reader.ForDeviceRequest(device, publicKey))
                preparation = reader.PrepareRecord(InterventionRecords.Pause(person, goal.Id, goal.ComputedHash, active.ComputedHash, "Pause from the phone."));
            Assert.True(preparation.Ok, preparation.Problem?.Message);
            RecordPrepared plan = Assert.IsType<RecordPrepared>(preparation.Prepared);
            var submission = new SignedRecordRequest(plan.Plan, signer.Sign(RecordSigning.Digest(plan.Hash)).Signature);

            using var waiting = new ManualResetEventSlim();
            reader.Session.WriteLock!.Waiting = waiting.Set;
            Task<WriteOutcome> signed;
            using (writer.Session.HoldWriteLock())
            {
                signed = Task.Run(() =>
                {
                    using var scope = reader.ForDeviceRequest(device, publicKey);
                    return reader.NewSignedRecord(submission);
                });
                Assert.True(waiting.Wait(TestTiming.HangTimeout, TestContext.Current.CancellationToken));
                estate.SetClock(estate.Clock.AddSeconds(3));
                estate.Add("goal-status", person, """{"status":"paused","note":"Paused on the other writer."}""", goal.Reference);
                estate.Add("goal-status", person, """{"status":"active","note":"Resumed on the other writer."}""", goal.Reference);
            }

            WriteOutcome result = await signed.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            Assert.Null(result.Written);
            Assert.Contains("reviewed active goal status changed", result.Problem?.Message, StringComparison.Ordinal);
            Assert.Equal("active", reader.Goal(goal.Id)!.Json.GetProperty("status").GetString());
            Assert.DoesNotContain(estate.Load().About(goal.Id, "goal-status"), status => EstateModel.BodyString(status, "note") == "Pause from the phone.");
        }
        finally { homes.Delete(recursive: true); }
    }
}
