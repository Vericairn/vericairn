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
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// Policy authority read at the runtime's time: a principal acts as the operator for policy records only while its role still stands, or
/// through a record signed history places before the role was taken away.
/// </summary>
public class PolicyAuthorityTimeTests
{
    private static void Require(PolicyFixture f) => f.Add("estate", new JsonObject
    {
        ["name"] = "Test Estate", ["slug"] = "test-estate", ["purpose"] = "Exercise the record format.", ["operator"] = f.Estate.OperatorId, ["recordFormat"] = 3,
        ["policies"] = new JsonObject { ["signatures"] = "required", ["releaseAcceptance"] = new JsonArray("operator") },
    }, supersedes: f.Estate.Estate.Id);

    private static JsonObject PolicyGrant(string grantee, PolicyFixture f, string scope, JsonObject bounds) => new()
    {
        ["grantee"] = grantee, ["scope"] = new JsonArray(scope), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = f.Decision.Id,
        ["expires"] = "2027-06-01T00:00:00Z", ["bounds"] = new JsonArray(bounds),
    };

    /// <summary>A policy declared by a principal: its own approving decision and the policy itself, both signed by it.</summary>
    private static RecordEnvelope Declare(PolicyFixture f, string by, IRecordSigner signer, string key)
    {
        JsonObject policy = f.PolicyBody(key, duty: false);
        policy["owner"] = by;
        JsonObject approval = f.DecisionBody();
        approval["decidedBy"] = by;
        approval["decidedAt"] = RecordParser.FormatTimestamp(f.Estate.Clock.AddSeconds(1));
        approval["policies"] = new JsonArray(new JsonObject { ["key"] = "definition", ["action"] = "declare", ["policy"] = key, ["change"] = "Approve this exact body.", ["definitionHash"] = PolicyRequirements.DefinitionHash(PolicyFixture.Element(policy)) });
        RecordEnvelope decision = f.Estate.Add("decision", by, approval.ToJsonString(), signer: signer);
        policy["basis"] = new JsonArray(new JsonObject { ["decision"] = PolicyFixture.Ref(decision), ["requirement"] = "definition" });
        return f.Estate.Add("estate-policy", by, policy.ToJsonString(), signer: signer);
    }

    private static RecordEnvelope Checkpoint(PolicyFixture f, string trigger, string? by = null, IRecordSigner? signer = null)
    {
        EstateModel model = f.Estate.Load();
        CheckpointDraft draft = SignedHistory.Prepare(model, trigger, trigger == "adoption" ? model.Records.Where(r => r.Signatures.Count == 0) : null);
        foreach (byte[] bytes in draft.Objects)
        {
            string path = Path.Combine(f.Estate.Root, EstateLayout.ObjectsDirectory, ContentHash.Sha256(bytes)["sha256:".Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        return f.Estate.Add("history-checkpoint", by ?? f.Estate.OperatorId, draft.Body.GetRawText(), signer: signer ?? f.OperatorSigner);
    }

    private static JsonObject Person(string name, params string[] roles) => new() { ["kind"] = "human", ["name"] = name, ["roles"] = new JsonArray([.. roles.Select(r => (JsonNode)r)]) };

    private static string Now(PolicyFixture f) => RecordParser.FormatTimestamp(f.Estate.Clock);

    private static bool Warned(EstateModel model, RecordEnvelope record) =>
        model.Findings.Any(e => e.Code == "policy.operator-removed" && e.Severity == Severity.Warning && e.Location == record.ExpectedPath);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecordsDatedBackIntoAFormerOperatorsTimeAuthorizeNoPolicy(bool adopted)
    {
        using var f = new PolicyFixture();
        Require(f);
        RecordEnvelope y = f.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "Y", ["roles"] = new JsonArray("operator") });
        StoreSigner ys = f.AdmitSigner(y.Id);
        if (adopted) Checkpoint(f, "adoption");
        DateTimeOffset appointed = f.Estate.Clock;
        f.Estate.SetClock(appointed.AddHours(1));
        f.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "Y", ["roles"] = new JsonArray("contributor") }, supersedes: y.Id);
        DateTimeOffset resume = f.Estate.Clock;

        // Dated into the time Y held the role: bounded grants to itself and a declared policy.
        f.Estate.SetClock(appointed.AddMinutes(10));
        RecordEnvelope grant = f.Estate.Add("authority", y.Id, PolicyGrant(y.Id, f, Scopes.ManageEstatePolicies, PolicyFixture.PolicyBounds("y-policy")).ToJsonString(), signer: ys);
        RecordEnvelope writingGrant = f.Estate.Add("authority", y.Id, PolicyGrant(y.Id, f, Scopes.ManageWritingExceptions, PolicyFixture.WritingBounds()).ToJsonString(), signer: ys);
        RecordEnvelope policy = Declare(f, y.Id, ys, "y-policy");
        f.Estate.SetClock(resume);
        RecordEnvelope writing = f.Estate.Add("writing-exception", y.Id, PolicyFixture.WritingBody(writingGrant).ToJsonString(), signer: ys);
        EstateModel model = f.Estate.Load();
        Assert.False(model.Counts(model.ById[policy.Id]));
        Assert.DoesNotContain(model.Current("estate-policy"), p => EstateModel.BodyString(p, "key") == "y-policy");
        Assert.False(model.Counts(model.ById[writing.Id]));
        Assert.False(PolicyAuthority.OperatorAt(model, y.Id, RecordParser.FormatTimestamp(appointed.AddMinutes(10))));
        Assert.False(PolicyAuthority.OperatorAt(model, model.ById[grant.Id]));
        if (adopted)
        {
            // Written after the estate adopted checkpoints, and listed by none before the removal: errors.
            Assert.Contains(model.Findings, e => e.Code == "authority.author" && e.Location == grant.ExpectedPath);
            Assert.Contains(model.Findings, e => e.Code == "authority.author" && e.Location == writingGrant.ExpectedPath);
            Assert.Contains(model.Findings, e => e.Code == "policy.approval" && e.Location == policy.ExpectedPath);
            Assert.Contains(model.Findings, e => e.Code == "policy.authority" && e.Location.StartsWith(writing.ExpectedPath, StringComparison.Ordinal));
        }
        else
        {
            // Without checkpoints nothing tells them from records Y wrote while it held the role: each counts for nothing, reported once.
            Assert.True(Warned(model, grant));
            Assert.True(Warned(model, writingGrant));
            Assert.True(Warned(model, policy));
            Assert.True(Warned(model, writing));
            foreach (RecordEnvelope ended in new[] { grant, writingGrant, policy, writing })
                Assert.DoesNotContain(model.Findings, e => e.Severity == Severity.Error && e.Location.StartsWith(ended.ExpectedPath, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheRuntimeWritesAPolicyGrantAtItsOwnTimeAndOnlyForAnOperatorNow()
    {
        using var f = new PolicyFixture();
        Require(f);
        using EstateService api = EstateService.Open(f.Estate.Root);
        RecordEnvelope y = f.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "Y", ["roles"] = new JsonArray("operator") });
        WriteOutcome device = api.DeviceCreate(new(y.Id, "Y's device", "file"));
        RecordEnvelope held = api.Session.Load().ById[device.Written!.Id];
        f.Estate.SetClock(held.AtTime.AddHours(-2));
        f.Add("admission", new JsonObject { ["principal"] = y.Id, ["status"] = "admitted", ["note"] = "Y's device." }, subject: held.Reference);
        f.Estate.SetClock(held.AtTime.AddHours(-1));
        f.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "Y", ["roles"] = new JsonArray("contributor") }, supersedes: y.Id);
        string earlier = RecordParser.FormatTimestamp(held.AtTime.AddMinutes(-90));

        WriteOutcome dated = api.NewRecord(new NewRecordRequest("authority", y.Id, PolicyFixture.Element(PolicyGrant(y.Id, f, Scopes.ManageEstatePolicies, PolicyFixture.PolicyBounds("y-policy"))), At: earlier));
        Assert.False(dated.Ok);
        Assert.Contains("runtime's time", dated.Problem!.Message, StringComparison.Ordinal);
        WriteOutcome writingDated = api.NewRecord(new NewRecordRequest("authority", y.Id, PolicyFixture.Element(PolicyGrant(y.Id, f, Scopes.ManageWritingExceptions, PolicyFixture.WritingBounds())), At: earlier));
        Assert.False(writingDated.Ok);
        WriteOutcome now = api.NewRecord(new NewRecordRequest("authority", y.Id, PolicyFixture.Element(PolicyGrant(y.Id, f, Scopes.ManageWritingExceptions, PolicyFixture.WritingBounds()))));
        Assert.False(now.Ok);
        Assert.Contains(now.Problem!.Findings, p => p.Code == "authority.author");
        Assert.Empty(api.PolicyAccess(y.Id));
        Assert.Equal(0, api.Check().Errors);
    }

    [Fact]
    public void AFormerOperatorsPolicyStandsWhenSignedHistoryPlacesItBeforeTheRoleWasTakenAway()
    {
        using var f = new PolicyFixture();
        Require(f);
        RecordEnvelope o = f.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "O", ["roles"] = new JsonArray("operator") });
        StoreSigner os = f.AdmitSigner(o.Id);
        Checkpoint(f, "adoption");
        RecordEnvelope kept = Declare(f, o.Id, os, "o-policy");
        Checkpoint(f, "manual");
        DateTimeOffset declared = kept.AtTime;
        f.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "O", ["roles"] = new JsonArray("contributor") }, supersedes: o.Id);
        DateTimeOffset resume = f.Estate.Clock;
        f.Estate.SetClock(declared.AddSeconds(5));
        RecordEnvelope dated = Declare(f, o.Id, os, "o-dated");
        f.Estate.SetClock(resume);
        EstateModel model = f.Estate.Load();
        Assert.True(SignedHistory.Adopted(model));
        Assert.True(model.Counts(model.ById[kept.Id]));
        Assert.Contains(model.Current("estate-policy"), p => EstateModel.BodyString(p, "key") == "o-policy");
        Assert.DoesNotContain(model.Findings, e => e.Location == kept.ExpectedPath && e.Severity == Severity.Error);
        Assert.False(model.Counts(model.ById[dated.Id]));
        Assert.DoesNotContain(model.Current("estate-policy"), p => EstateModel.BodyString(p, "key") == "o-dated");
    }

    [Fact]
    public void WithoutSignedHistoryAFormerOperatorsPoliciesNoLongerStand()
    {
        using var f = new PolicyFixture();
        Require(f);
        RecordEnvelope o = f.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "O", ["roles"] = new JsonArray("operator") });
        StoreSigner os = f.AdmitSigner(o.Id);
        RecordEnvelope policy = Declare(f, o.Id, os, "o-policy");
        Assert.True(f.Estate.Load().Counts(f.Estate.Load().ById[policy.Id]));
        Assert.Contains(f.Estate.Load().Current("estate-policy"), p => EstateModel.BodyString(p, "key") == "o-policy");

        f.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "O", ["roles"] = new JsonArray("contributor") }, supersedes: o.Id);
        EstateModel model = f.Estate.Load();
        Assert.False(model.Counts(model.ById[policy.Id]));
        Assert.True(Warned(model, policy));
        Assert.DoesNotContain(model.Findings, e => e.Severity == Severity.Error && e.Location == policy.ExpectedPath);
        // The root operator's own policy is unaffected.
        RecordEnvelope root = f.Policy(f.PolicyBody("root-policy", duty: false));
        Assert.True(f.Estate.Load().Counts(f.Estate.Load().ById[root.Id]));
    }

    [Fact]
    public void AnAppointmentDatedBackIntoAFormerOperatorsTimeGivesNoPolicyAuthority()
    {
        using var f = new PolicyFixture();
        Require(f);
        RecordEnvelope y = f.Add("principal", Person("Y", "operator"));
        StoreSigner ys = f.AdmitSigner(y.Id);
        Checkpoint(f, "adoption");
        DateTimeOffset appointed = f.Estate.Clock;
        f.Estate.SetClock(appointed.AddHours(1));
        f.Add("principal", Person("Y", "contributor"), supersedes: y.Id);
        DateTimeOffset resume = f.Estate.Clock;
        f.Estate.SetClock(appointed.AddMinutes(10));
        RecordEnvelope z = f.Estate.Add("principal", y.Id, """{"kind":"human","name":"Z","roles":["operator"]}""", signer: ys);
        f.Estate.SetClock(resume);
        StoreSigner zs = f.AdmitSigner(z.Id);
        RecordEnvelope writingGrant = f.Estate.Add("authority", z.Id, PolicyGrant(z.Id, f, Scopes.ManageWritingExceptions, PolicyFixture.WritingBounds()).ToJsonString(), signer: zs);
        RecordEnvelope writing = f.Estate.Add("writing-exception", z.Id, PolicyFixture.WritingBody(writingGrant).ToJsonString(), signer: zs);
        EstateModel model = f.Estate.Load();
        Assert.False(PolicyAuthority.OperatorAt(model, z.Id, Now(f)));
        Assert.False(model.Counts(model.ById[writing.Id]));
        Assert.Contains(model.Findings, e => e.Code == "authority.author" && e.Location == writingGrant.ExpectedPath);
    }

    [Fact]
    public void AnAppointmentSignedHistoryPlacesBeforeTheAppointersRemovalKeepsPolicyAuthority()
    {
        using var f = new PolicyFixture();
        Require(f);
        RecordEnvelope o = f.Add("principal", Person("O", "operator"));
        StoreSigner os = f.AdmitSigner(o.Id);
        Checkpoint(f, "adoption");
        RecordEnvelope p = f.Estate.Add("principal", o.Id, """{"kind":"human","name":"P","roles":["operator"]}""", signer: os);
        Checkpoint(f, "manual");
        f.Add("principal", Person("O", "contributor"), supersedes: o.Id);
        StoreSigner ps = f.AdmitSigner(p.Id);
        RecordEnvelope policy = Declare(f, p.Id, ps, "p-policy");
        EstateModel model = f.Estate.Load();
        Assert.True(PolicyAuthority.OperatorAt(model, p.Id, Now(f)));
        Assert.True(model.Counts(model.ById[policy.Id]));
    }

    /// <summary>
    /// Records a removed operator writes in a copy without its removal, dated back into its time: a checkpoint that is not by the root
    /// operator or a standing operator, or that stands beside the removal rather than before it, places none of them before the removal.
    /// </summary>
    [Theory]
    [InlineData("the removed operator")]
    [InlineData("an agent")]
    [InlineData("the root operator, after the removal")]
    [InlineData("the root operator, beside the removal")]
    public void OnlyACheckpointBeforeTheRemovalByTheRootOrAStandingOperatorPlacesARecordBeforeIt(string witness)
    {
        using var f = new PolicyFixture();
        Require(f);
        RecordEnvelope y = f.Add("principal", Person("Y", "operator"));
        StoreSigner ys = f.AdmitSigner(y.Id);
        Checkpoint(f, "adoption");
        DateTimeOffset appointed = f.Estate.Clock;
        f.Estate.SetClock(appointed.AddHours(1));
        RecordEnvelope removal = f.Add("principal", Person("Y", "contributor"), supersedes: y.Id);
        bool beside = witness.EndsWith("beside the removal", StringComparison.Ordinal);
        RecordEnvelope? carrying = beside ? Checkpoint(f, "manual") : null;
        DateTimeOffset resume = f.Estate.Clock;

        // A copy that does not hold the removal, nor the checkpoint that lists it.
        var withheld = new List<(string Path, byte[] Bytes)>();
        foreach (RecordEnvelope record in carrying is null ? [removal] : new[] { removal, carrying })
        {
            string path = Path.Combine(f.Estate.Root, record.ExpectedPath);
            withheld.Add((path, File.ReadAllBytes(path)));
            File.Delete(path);
        }

        f.Estate.SetClock(appointed.AddMinutes(10));
        RecordEnvelope grant = f.Estate.Add("authority", y.Id, PolicyGrant(y.Id, f, Scopes.ManageWritingExceptions, PolicyFixture.WritingBounds()).ToJsonString(), signer: ys);
        RecordEnvelope policy = Declare(f, y.Id, ys, "y-policy");
        RecordEnvelope z = f.Estate.Add("principal", y.Id, Person("Z", "operator").ToJsonString(), signer: ys);
        f.Estate.SetClock(beside ? appointed.AddMinutes(20) : resume.AddMinutes(1));
        RecordEnvelope checkpoint = witness switch
        {
            "the removed operator" => Checkpoint(f, "manual", y.Id, ys),
            "an agent" => Checkpoint(f, "manual", f.Estate.BuilderId, f.BuilderSigner),
            _ => Checkpoint(f, "manual"),
        };
        foreach ((string path, byte[] bytes) in withheld) File.WriteAllBytes(path, bytes);
        f.Estate.SetClock(resume.AddMinutes(2));
        EstateModel model = f.Estate.Load();
        Assert.True(model.Counts(model.ById[checkpoint.Id]));
        Assert.Contains(model.Trust.History.Listings[grant.Id], id => id == checkpoint.Id);
        Assert.False(model.Trust.WitnessedBefore(model.ById[grant.Id], model.ById[removal.Id]));
        Assert.False(model.Trust.WitnessedBefore(model.ById[policy.Id], model.ById[removal.Id]));
        Assert.False(PolicyAuthority.OperatorAt(model, model.ById[grant.Id]));
        Assert.Contains(model.Findings, e => e.Code == "authority.author" && e.Severity == Severity.Error && e.Location == grant.ExpectedPath);
        Assert.False(model.Counts(model.ById[policy.Id]));
        Assert.DoesNotContain(model.Current("estate-policy"), p => EstateModel.BodyString(p, "key") == "y-policy");
        // The appointment Y dated into its time gives Z no policy authority, reserved acts among them.
        Assert.False(PolicyAuthority.OperatorAt(model, z.Id, Now(f)));
        Assert.DoesNotContain(model.Trust.PolicyOperators.StandingNow, id => id == z.Id);

        // Y uses the grant at the present time; the root operator then writes its next checkpoint, which follows every head.
        RecordEnvelope writing = f.Estate.Add("writing-exception", y.Id, PolicyFixture.WritingBody(grant).ToJsonString(), signer: ys);
        f.Estate.SetClock(resume.AddMinutes(5));
        Checkpoint(f, "manual");
        model = f.Estate.Load();
        Assert.False(model.Counts(model.ById[writing.Id]));
        Assert.False(model.Counts(model.ById[policy.Id]));
        Assert.False(PolicyAuthority.OperatorAt(model, z.Id, Now(f)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARemovalByAnOperatorWhoLaterLosesTheRoleKeepsCounting(bool adopted)
    {
        using var f = new PolicyFixture();
        Require(f);
        using EstateService api = EstateService.Open(f.Estate.Root);
        RecordEnvelope o2 = f.Add("principal", Person("O2", "operator"));
        StoreSigner o2s = f.AdmitSigner(o2.Id);
        RecordEnvelope y = RoleRecordServiceTests.WithDevice(f, api, Person("Y", "operator"), f.Estate.Clock.AddMinutes(1));
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        if (adopted) Checkpoint(f, "adoption");
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        f.Estate.Add("principal", o2.Id, Person("Y", "contributor").ToJsonString(), supersedes: y.Id, signer: o2s);
        Assert.False(PolicyAuthority.OperatorAt(api.Session.Load(), y.Id, Now(f)));
        // Later the root operator removes O2's role.
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        f.Add("principal", Person("O2", "contributor"), supersedes: o2.Id);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        EstateModel model = api.Session.Load();
        Assert.False(PolicyAuthority.OperatorAt(model, y.Id, Now(f)));
        Assert.False(PolicyAuthority.OperatorAt(model, o2.Id, Now(f)));
        Assert.False(model.HasRole(y.Id, Roles.Operator));
        WriteOutcome grant = api.NewRecord(new NewRecordRequest("authority", y.Id, PolicyFixture.Element(PolicyGrant(y.Id, f, Scopes.ManageWritingExceptions, PolicyFixture.WritingBounds()))));
        Assert.False(grant.Ok);
        Assert.Contains(grant.Problem!.Findings, x => x.Code == "authority.author");
        Assert.Empty(api.PolicyAccess(y.Id));
    }

    [Fact]
    public void AnUnsignedRemovalTakesPolicyAuthorityAway()
    {
        // The fixture's charter leaves signatures optional: the root operator's removal of Y's role is unsigned.
        using var f = new PolicyFixture();
        RecordEnvelope y = f.Add("principal", Person("Y", "operator"));
        StoreSigner ys = f.AdmitSigner(y.Id);
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        RecordEnvelope removal = f.Add("principal", Person("Y", "contributor"), supersedes: y.Id, sign: false);
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        EstateModel model = f.Estate.Load();
        Assert.True(model.Counts(model.ById[removal.Id]));
        Assert.False(PolicyAuthority.OperatorAt(model, y.Id, Now(f)));
        RecordEnvelope policy = Declare(f, y.Id, ys, "y-policy");
        model = f.Estate.Load();
        Assert.False(model.Counts(model.ById[policy.Id]));
        Assert.DoesNotContain(model.Current("estate-policy"), p => EstateModel.BodyString(p, "key") == "y-policy");
        Assert.Contains(model.Findings, e => e.Code is "policy.authority" or "policy.approval" && e.Severity == Severity.Error && e.Location.StartsWith(policy.ExpectedPath, StringComparison.Ordinal));
        // An unsigned revision takes the role away but never gives it.
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        f.Add("principal", Person("Y", "operator"), supersedes: removal.Id, sign: false);
        Assert.False(PolicyAuthority.OperatorAt(f.Estate.Load(), y.Id, Now(f)));
    }

    /// <summary>
    /// The root operator removes a second operator's role where no history checkpoint could place that operator's policy records before the
    /// removal: the estate stays valid, and each record that rests on the role is reported once and counts for nothing.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARemovalBeforeHistoryLeavesTheEstateValidAndReportsEachPolicyRecordItEnds(bool adoptedAfter)
    {
        using var f = new PolicyFixture();
        Require(f);
        RecordEnvelope o = f.Add("principal", Person("O", "operator"));
        StoreSigner os = f.AdmitSigner(o.Id);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        RecordEnvelope grant = f.Estate.Add("authority", o.Id, PolicyGrant(f.Estate.BuilderId, f, Scopes.ManageWritingExceptions, PolicyFixture.WritingBounds()).ToJsonString(), signer: os);
        RecordEnvelope builderWriting = f.Add("writing-exception", PolicyFixture.WritingBody(grant), builder: true);
        RecordEnvelope writing = f.Estate.Add("writing-exception", o.Id, new JsonObject { ["token"] = "Open511", ["code"] = "writing.spacing", ["reason"] = "An intentional service name.", ["state"] = "active", ["basis"] = new JsonObject { ["kind"] = "operator" } }.ToJsonString(), signer: os);
        RecordEnvelope preference = f.Estate.Add("prompt-preference", o.Id, new JsonObject { ["prompt"] = "estate:review", ["scope"] = "estate", ["disabled"] = true }.ToJsonString(), signer: os);
        EstateModel before = f.Estate.Load();
        Assert.False(before.Findings.HasErrors, string.Join('\n', before.Findings.Where(e => e.Severity == Severity.Error)));
        Assert.True(before.Counts(before.ById[builderWriting.Id]));
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        f.Add("principal", Person("O", "contributor"), supersedes: o.Id);
        if (adoptedAfter) Checkpoint(f, "adoption");
        EstateModel model = f.Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(e => e.Severity == Severity.Error)));
        foreach (RecordEnvelope ended in new[] { grant, builderWriting, writing, preference })
        {
            Assert.True(Warned(model, ended), ended.Type);
            Assert.False(model.Counts(model.ById[ended.Id]), ended.Type);
        }

        Assert.Contains(model.Findings, e => e.Code == "policy.operator-removed" && e.Message.Contains(o.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void AnEstatePromptChoiceWhoseAuthorityFailsCountsForNothing()
    {
        using var f = new PolicyFixture();
        Require(f);
        RecordEnvelope y = f.Add("principal", Person("Y", "operator"));
        StoreSigner ys = f.AdmitSigner(y.Id);
        Checkpoint(f, "adoption");
        DateTimeOffset appointed = f.Estate.Clock;
        f.Estate.SetClock(appointed.AddHours(1));
        f.Add("principal", Person("Y", "contributor"), supersedes: y.Id);
        Checkpoint(f, "manual");
        DateTimeOffset resume = f.Estate.Clock;
        f.Estate.SetClock(appointed.AddMinutes(10));
        RecordEnvelope preference = f.Estate.Add("prompt-preference", y.Id, new JsonObject { ["prompt"] = "estate:review", ["scope"] = "estate", ["disabled"] = true }.ToJsonString(), signer: ys);
        f.Estate.SetClock(resume);
        EstateModel model = f.Estate.Load();
        Assert.Contains(model.Findings, e => e.Code == "prompt.authority" && e.Severity == Severity.Error && e.Location == preference.ExpectedPath);
        Assert.False(model.Counts(model.ById[preference.Id]));
        Assert.DoesNotContain(model.Current("prompt-preference"), p => p.Id == preference.Id);
    }
}
