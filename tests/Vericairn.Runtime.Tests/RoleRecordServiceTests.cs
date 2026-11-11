// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.History;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Records.Types;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>The runtime writes principal and authority records only when their authors may write them.</summary>
public class RoleRecordServiceTests
{
    private static void Require(PolicyFixture f) => f.Add("estate", new JsonObject
    {
        ["name"] = "Test Estate", ["slug"] = "test-estate", ["purpose"] = "Exercise the record format.", ["operator"] = f.Estate.OperatorId, ["recordFormat"] = 3,
        ["policies"] = new JsonObject { ["signatures"] = "required", ["releaseAcceptance"] = new JsonArray("operator") },
    }, supersedes: f.Estate.Estate.Id);

    private static void Checkpoint(PolicyFixture f, string trigger)
    {
        EstateModel model = f.Estate.Load();
        CheckpointDraft draft = SignedHistory.Prepare(model, trigger, trigger == "adoption" ? model.Records.Where(r => r.Signatures.Count == 0) : null);
        foreach (byte[] bytes in draft.Objects)
        {
            string path = Path.Combine(f.Estate.Root, EstateLayout.ObjectsDirectory, ContentHash.Sha256(bytes)["sha256:".Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        f.Estate.Add("history-checkpoint", f.Estate.OperatorId, draft.Body.GetRawText(), signer: f.OperatorSigner);
    }

    /// <summary>A principal with a device this runtime holds, admitted by the fixture's operator; returns the principal.</summary>
    internal static RecordEnvelope WithDevice(PolicyFixture f, EstateService api, JsonObject principal, DateTimeOffset? admittedAt = null)
    {
        RecordEnvelope person = f.Add("principal", principal);
        WriteOutcome device = api.DeviceCreate(new(person.Id, "Runtime-held device", "file"));
        Assert.True(device.Ok, device.Problem?.Message);
        RecordEnvelope held = api.Session.Load().ById[device.Written!.Id];
        f.Estate.SetClock(admittedAt ?? held.AtTime.AddSeconds(-2));
        f.Add("admission", new JsonObject { ["principal"] = person.Id, ["status"] = "admitted", ["note"] = "Runtime-held device." }, subject: held.Reference);
        return person;
    }

    [Fact]
    public void ThePrincipalARecordDescribesDoesNotWriteIt()
    {
        using var f = new PolicyFixture();
        Require(f);
        using EstateService api = EstateService.Open(f.Estate.Root);
        RecordEnvelope x = WithDevice(f, api, new JsonObject { ["kind"] = "human", ["name"] = "X", ["roles"] = new JsonArray("contributor") });
        WriteOutcome own = api.NewRecord(new NewRecordRequest("principal", x.Id, PolicyFixture.Element(new JsonObject { ["kind"] = "human", ["name"] = "X", ["roles"] = new JsonArray("operator") }), Supersedes: x.Id));
        Assert.False(own.Ok);
        Assert.Contains(own.Problem!.Findings, p => p.Code == "principal.self-authored");
        WriteOutcome grant = api.NewRecord(new NewRecordRequest("authority", x.Id, PolicyFixture.Element(new JsonObject
        {
            ["grantee"] = x.Id, ["scope"] = new JsonArray("administer"), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = f.Decision.Id,
        })));
        Assert.False(grant.Ok);
        Assert.Contains(grant.Problem!.Findings, p => p.Code == "authority.author");
        WriteOutcome admitted = api.NewRecord(new NewRecordRequest("principal", x.Id, PolicyFixture.Element(new JsonObject { ["kind"] = "human", ["name"] = "Z", ["roles"] = new JsonArray("contributor") })));
        Assert.False(admitted.Ok);
        EstateModel model = api.Session.Load();
        Assert.False(model.HasRole(x.Id, Roles.Operator));
        Assert.Equal(0, api.Check().Errors);
    }

    [Fact]
    public void ThePrincipalAndAuthorityRecordsTheRuntimeWritesStateNoEarlierTimeOrChosenId()
    {
        using var f = new PolicyFixture();
        Require(f);
        using EstateService api = EstateService.Open(f.Estate.Root);
        RecordEnvelope d = WithDevice(f, api, new JsonObject { ["kind"] = "human", ["name"] = "D", ["roles"] = new JsonArray("operator") }, f.Estate.Clock.AddMinutes(1));
        string tenure = RecordParser.FormatTimestamp(f.Estate.Clock.AddMinutes(10));
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        // In an estate that has adopted history checkpoints, records are judged by the roles at the time they state.
        Checkpoint(f, "adoption");
        f.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "D", ["roles"] = new JsonArray("contributor") }, supersedes: d.Id);
        Checkpoint(f, "manual");
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        JsonObject grant = new()
        {
            ["grantee"] = d.Id, ["scope"] = new JsonArray("administer", "repository-write", "accept-work"), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = f.Decision.Id,
        };
        JsonObject person = new() { ["kind"] = "human", ["name"] = "E", ["roles"] = new JsonArray("operator") };
        // The former operator dates a grant to itself, and a new operator, back into the time it held the role.
        WriteOutcome datedGrant = api.NewRecord(new NewRecordRequest("authority", d.Id, PolicyFixture.Element(grant), At: tenure));
        WriteOutcome datedPerson = api.NewRecord(new NewRecordRequest("principal", d.Id, PolicyFixture.Element(person), At: tenure));
        // Neither the root operator may state a time or choose an id for these records.
        WriteOutcome rootDated = api.NewRecord(new NewRecordRequest("principal", f.Estate.OperatorId, PolicyFixture.Element(person), At: tenure));
        WriteOutcome rootChosen = api.NewRecord(new NewRecordRequest("authority", f.Estate.OperatorId, PolicyFixture.Element(grant), Id: Vericairn.Records.Identity.Ulid.NewUlid().Value));
        foreach (WriteOutcome outcome in new[] { datedGrant, datedPerson, rootDated, rootChosen })
        {
            Assert.False(outcome.Ok);
            Assert.Null(outcome.Written);
        }

        EstateModel model = api.Session.Load();
        Assert.False(model.HasScope(d.Id, "administer"));
        Assert.DoesNotContain(model.OfType("principal"), p => EstateModel.BodyString(p, "name") == "E");
        Assert.Equal(0, api.Check().Errors);
    }

    [Fact]
    public void TheRuntimeReportsAsCurrentThePrincipalRevisionThatGivesItsRoles()
    {
        using var f = new PolicyFixture();
        Require(f);
        using EstateService api = EstateService.Open(f.Estate.Root);
        RecordEnvelope x = f.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "X", ["roles"] = new JsonArray("contributor") });
        StoreSigner xs = f.AdmitSigner(x.Id);
        RecordEnvelope y = f.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "Y", ["roles"] = new JsonArray("contributor") });
        // X's revision of Y's record counts as evidence, but X may not write it, so it gives nothing.
        RecordEnvelope ofY = f.Estate.Add("principal", x.Id, new JsonObject { ["kind"] = "human", ["name"] = "Y", ["roles"] = new JsonArray("operator") }.ToJsonString(), supersedes: y.Id, signer: xs);
        EstateModel model = api.Session.Load();
        Assert.True(model.Counts(model.ById[ofY.Id]));
        Assert.False(model.HasRole(y.Id, Roles.Operator));
        Assert.Contains(api.Records("principal"), r => r.Id == y.Id);
        Assert.DoesNotContain(api.Records("principal"), r => r.Id == ofY.Id);
        Assert.False(api.Record(ofY.Id)!.Current);
        Assert.True(api.Record(y.Id)!.Current);
    }
}
