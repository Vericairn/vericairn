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

    /// <summary>
    /// The policy records of someone a removed operator appointed, directly or through another appointee, rest on that removal in an
    /// estate that has not adopted history checkpoints, as the removed operator's own do: each counts for nothing and is the warning
    /// naming the removal, not errors. Records that read roles as they are now still fail, the appointment among them.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ThePolicyRecordsOfARemovedOperatorsAppointeeRestOnTheRemovalBeforeHistory(int depth)
    {
        using var f = new PolicyFixture();
        Require(f);
        RecordEnvelope o1 = f.Add("principal", Person("O1", "operator"));
        StoreSigner appointer = f.AdmitSigner(o1.Id);
        RecordEnvelope appointee = o1;
        StoreSigner signer = appointer;
        for (int level = 1; level <= depth; level++)
        {
            f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
            appointee = f.Estate.Add("principal", appointee.Id, Person("O" + (level + 1), "operator").ToJsonString(), signer: signer);
            signer = f.AdmitSigner(appointee.Id);
        }

        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        RecordEnvelope policy = Declare(f, appointee.Id, signer, "appointee-policy");
        RecordEnvelope writing = OperatorWriting(f, appointee.Id, signer);
        EstateModel before = f.Estate.Load();
        Assert.False(before.Findings.HasErrors, string.Join('\n', before.Findings.Where(e => e.Severity == Severity.Error)));
        Assert.True(before.Counts(before.ById[policy.Id]));

        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        RecordEnvelope removal = f.Add("principal", Person("O1", "contributor"), supersedes: o1.Id);
        EstateModel model = f.Estate.Load();
        foreach (RecordEnvelope ended in new[] { policy, writing })
        {
            Assert.True(Warned(model, ended), ended.Type);
            Assert.Contains(model.Findings, e => e.Code == "policy.operator-removed" && e.Location == ended.ExpectedPath &&
                e.Message.Contains($"{o1.Id} lost it by revision {removal.Id}", StringComparison.Ordinal));
            Assert.DoesNotContain(model.Findings, e => e.Severity == Severity.Error && e.Location.StartsWith(ended.ExpectedPath, StringComparison.Ordinal));
            Assert.False(model.Counts(model.ById[ended.Id]), ended.Type);
        }

        Assert.Contains(model.Findings, e => e.Severity == Severity.Error && e.Code == "principal.author");
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

    /// <summary>A device for the root operator whose key the runtime holds, admitted by the root operator, so the runtime signs for it.</summary>
    private static void RuntimeHeldRootDevice(PolicyFixture f, EstateService api)
    {
        WriteOutcome device = api.DeviceCreate(new(f.Estate.OperatorId, "The root operator's runtime-held device", "file"));
        Assert.True(device.Ok, device.Problem?.Message);
        RecordEnvelope held = api.Session.Load().ById[device.Written!.Id];
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        f.Add("admission", new JsonObject { ["principal"] = f.Estate.OperatorId, ["status"] = "admitted", ["note"] = "Runtime-held device." }, subject: held.Reference);
    }

    private static string Outcome(WriteOutcome outcome) => outcome.Problem?.Message + " " + string.Join('\n', outcome.Problem?.Findings ?? []);

    /// <summary>
    /// The root operator removes a second operator's role through the runtime after that operator recorded policy records and, in some
    /// cases, a decision approving a policy and the policy. In an estate that has adopted history checkpoints the runtime writes a
    /// checkpoint first, so all of them keep counting. In one that has not, nothing places them before the removal: the runtime refuses
    /// it, naming each record that would fail and saying that adopting checkpoints first keeps them, and writes nothing, whether the
    /// records would fail with errors, as the decision does, or count for nothing, as policy records do; once the root operator adopts,
    /// the same removal is written after a checkpoint and every record keeps counting. A removal that would make no record fail is
    /// written either way.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void TheRuntimeRemovesAnOperatorsRoleOnlyWhereWhatTheyWroteKeepsCounting(bool adopted, bool decided)
    {
        using var f = new PolicyFixture();
        Require(f);
        using EstateService api = EstateService.Open(f.Estate.Root);
        RuntimeHeldRootDevice(f, api);
        RecordEnvelope o = f.Add("principal", Person("O", "operator"));
        StoreSigner os = f.AdmitSigner(o.Id);
        RecordEnvelope idle = f.Add("principal", Person("Idle", "operator"));
        if (adopted) Checkpoint(f, "adoption");
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        RecordEnvelope grant = f.Estate.Add("authority", o.Id, PolicyGrant(f.Estate.BuilderId, f, Scopes.ManageWritingExceptions, PolicyFixture.WritingBounds()).ToJsonString(), signer: os);
        RecordEnvelope builderWriting = f.Add("writing-exception", PolicyFixture.WritingBody(grant), builder: true);
        RecordEnvelope writing = f.Estate.Add("writing-exception", o.Id, new JsonObject { ["token"] = "Open511", ["code"] = "writing.spacing", ["reason"] = "An intentional service name.", ["state"] = "active", ["basis"] = new JsonObject { ["kind"] = "operator" } }.ToJsonString(), signer: os);
        RecordEnvelope preference = f.Estate.Add("prompt-preference", o.Id, new JsonObject { ["prompt"] = "estate:review", ["scope"] = "estate", ["disabled"] = true }.ToJsonString(), signer: os);
        RecordEnvelope? policy = decided ? Declare(f, o.Id, os, "o-policy") : null;
        RecordEnvelope? decision = decided ? f.Estate.Load().OfType("decision").Single(d => d.By == o.Id) : null;
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        Assert.Equal(0, api.Check().Errors);
        RecordEnvelope[] written = [grant, builderWriting, writing, preference, .. decided ? new[] { policy!, decision! } : []];
        var request = new NewRecordRequest("principal", f.Estate.OperatorId, PolicyFixture.Element(Person("O", "contributor")), Supersedes: o.Id);

        WriteOutcome removal = api.NewRecord(request);
        if (!adopted)
        {
            // Refused, naming every record that would fail, the decision and the policy it approves among them; nothing is written.
            Assert.False(removal.Ok);
            Assert.Equal("refused", removal.Problem!.Code);
            Assert.Contains("has not adopted history checkpoints", removal.Problem.Message, StringComparison.Ordinal);
            Assert.Contains("Adopt history checkpoints first", removal.Problem.Message, StringComparison.Ordinal);
            foreach (RecordEnvelope ended in written) Assert.Contains(ended.Id, removal.Problem.Message, StringComparison.Ordinal);
            Assert.Contains(removal.Problem.Findings, x => x.Code == "policy.operator-removed" && x.Location == writing.ExpectedPath);
            if (decided)
            {
                Assert.Contains(removal.Problem.Findings, x => x.Code == "decision.operator" && x.Severity == "error" && x.Location.StartsWith(decision!.ExpectedPath, StringComparison.Ordinal));
                Assert.Contains(removal.Problem.Findings, x => x.Code == "policy.operator-removed" && x.Location == policy!.ExpectedPath);
            }

            EstateModel unchanged = api.Session.Load();
            Assert.True(unchanged.HasRole(o.Id, Roles.Operator));
            Assert.DoesNotContain(unchanged.OfType("principal"), p => p.Supersedes == o.Id);
            foreach (RecordEnvelope kept in written) Assert.True(unchanged.Counts(unchanged.ById[kept.Id]), kept.Type);
            Assert.Equal(0, api.Check().Errors);
        }

        // A removal that would make no record fail is written in either estate.
        WriteOutcome idleRemoval = api.NewRecord(new NewRecordRequest("principal", f.Estate.OperatorId, PolicyFixture.Element(Person("Idle", "contributor")), Supersedes: idle.Id));
        Assert.True(idleRemoval.Ok, Outcome(idleRemoval));
        Assert.False(api.Session.Load().HasRole(idle.Id, Roles.Operator));

        if (!adopted)
        {
            // Adopting checkpoints first keeps them: the root operator adopts, and the same removal is written after a checkpoint.
            Checkpoint(f, "adoption");
            f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
            removal = api.NewRecord(request);
        }

        Assert.True(removal.Ok, Outcome(removal));
        EstateModel model = api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(e => e.Severity == Severity.Error)));
        Assert.Equal(0, api.Check().Errors);
        Assert.False(model.HasRole(o.Id, Roles.Operator));
        Assert.False(PolicyAuthority.OperatorAt(model, o.Id, Now(f)));

        // The checkpoint the runtime wrote first precedes the removal, and with the adoption lists what O wrote, so all of it keeps counting.
        Assert.NotNull(removal.Checkpoint?.Written);
        RecordEnvelope checkpoint = model.ById[removal.Checkpoint!.Written!.Id];
        RecordEnvelope revision = model.ById[removal.Written!.Id];
        Assert.Equal("history-checkpoint", checkpoint.Type);
        Assert.True(string.CompareOrdinal(checkpoint.At, revision.At) <= 0);
        foreach (RecordEnvelope kept in written)
        {
            Assert.True(model.Counts(model.ById[kept.Id]), kept.Type);
            Assert.True(model.Trust.WitnessedBefore(model.ById[kept.Id], revision), kept.Type);
            Assert.False(Warned(model, kept), kept.Type);
        }

        if (decided) Assert.Contains(model.Current("estate-policy"), p => EstateModel.BodyString(p, "key") == "o-policy");

        // The estate stays writable: the root operator admits a new contributor through the runtime.
        Assert.True(api.NewRecord(new NewRecordRequest("principal", f.Estate.OperatorId, PolicyFixture.Element(Person("New person", "contributor")))).Ok);
    }

    /// <summary>
    /// Taking administer away is judged the same way: in an estate that has not adopted history checkpoints the runtime refuses a grant
    /// revision that would leave a principal record the administrator wrote failing, naming it, and writes nothing; once the root operator
    /// adopts, it writes the revision after a checkpoint and the record stays valid.
    /// </summary>
    [Fact]
    public void TheRuntimeTakesAdministerAwayOnlyWhereWhatTheAdministratorWroteKeepsCounting()
    {
        using var f = new PolicyFixture();
        Require(f);
        using EstateService api = EstateService.Open(f.Estate.Root);
        RuntimeHeldRootDevice(f, api);
        RecordEnvelope a = f.Add("principal", Person("A", "reviewer"));
        StoreSigner signer = f.AdmitSigner(a.Id);
        JsonObject body = new() { ["grantee"] = a.Id, ["scope"] = new JsonArray(Scopes.Administer), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = f.Decision.Id };
        RecordEnvelope grant = f.Add("authority", body);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        RecordEnvelope admitted = f.Estate.Add("principal", a.Id, Person("X", "contributor").ToJsonString(), signer: signer);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        Assert.Equal(0, api.Check().Errors);
        body["scope"] = new JsonArray(Scopes.LocalEngineering);
        var request = new NewRecordRequest("authority", f.Estate.OperatorId, PolicyFixture.Element(body), Supersedes: grant.Id);

        WriteOutcome removal = api.NewRecord(request);
        Assert.False(removal.Ok);
        Assert.Equal("refused", removal.Problem!.Code);
        Assert.Contains(admitted.Id, removal.Problem.Message, StringComparison.Ordinal);
        Assert.Contains(removal.Problem.Findings, x => x.Code == "principal.author" && x.Location.StartsWith(admitted.ExpectedPath, StringComparison.Ordinal));
        Assert.True(api.Session.Load().HasScope(a.Id, Scopes.Administer));

        Checkpoint(f, "adoption");
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        removal = api.NewRecord(request);
        Assert.True(removal.Ok, Outcome(removal));
        Assert.NotNull(removal.Checkpoint?.Written);
        EstateModel model = api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(e => e.Severity == Severity.Error)));
        Assert.False(model.HasScope(a.Id, Scopes.Administer));
        Assert.True(model.Counts(model.ById[admitted.Id]));
        Assert.Equal(admitted.Id, model.CurrentPrincipal(admitted.Id)!.Id);
    }

    /// <summary>A device for a principal whose key the runtime holds, admitted by the root operator, so the runtime signs that principal's writes.</summary>
    private static void RuntimeHeldDevice(PolicyFixture f, EstateService api, string principal)
    {
        WriteOutcome device = api.DeviceCreate(new(principal, "A runtime-held device", "file"));
        Assert.True(device.Ok, device.Problem?.Message);
        RecordEnvelope held = api.Session.Load().ById[device.Written!.Id];
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        f.Add("admission", new JsonObject { ["principal"] = principal, ["status"] = "admitted", ["note"] = "Runtime-held device." }, subject: held.Reference);
    }

    private static RecordEnvelope OperatorWriting(PolicyFixture f, string by, IRecordSigner signer) =>
        f.Estate.Add("writing-exception", by, new JsonObject { ["token"] = "Open511", ["code"] = "writing.spacing", ["reason"] = "An intentional service name.", ["state"] = "active", ["basis"] = new JsonObject { ["kind"] = "operator" } }.ToJsonString(), signer: signer);

    /// <summary>
    /// What an operator recorded is listed only by the interval checkpoint the runtime wrote with the builder's record, signed by the
    /// builder's device. The checkpoint the runtime writes before the root operator's removal follows it, so the records keep counting.
    /// </summary>
    [Fact]
    public void ARecordListedByAnotherAuthorsCheckpointIsKeptThroughTheCheckpointBeforeTheRemoval()
    {
        using var f = new PolicyFixture();
        Require(f);
        using EstateService api = EstateService.Open(f.Estate.Root);
        RuntimeHeldRootDevice(f, api);
        RuntimeHeldDevice(f, api, f.Estate.BuilderId);
        RecordEnvelope o = f.Add("principal", Person("O", "operator"));
        StoreSigner os = f.AdmitSigner(o.Id);
        Checkpoint(f, "adoption");
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        RecordEnvelope writing = OperatorWriting(f, o.Id, os);
        RecordEnvelope policy = Declare(f, o.Id, os, "o-policy");
        RecordEnvelope decision = f.Estate.Load().OfType("decision").Single(d => d.By == o.Id);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));

        // The builder's next write carries the interval checkpoint, which lists what O wrote.
        WriteOutcome order = api.NewRecord(new NewRecordRequest("work-order", f.Estate.BuilderId, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("INT-1"))!)));
        Assert.True(order.Ok, Outcome(order));
        Assert.Equal("interval", order.Checkpoint?.Trigger);
        string interval = order.Checkpoint!.Written!.Id;
        Assert.Equal(f.Estate.BuilderId, api.Session.Load().ById[interval].By);

        WriteOutcome removal = api.NewRecord(new NewRecordRequest("principal", f.Estate.OperatorId, PolicyFixture.Element(Person("O", "contributor")), Supersedes: o.Id));
        Assert.True(removal.Ok, Outcome(removal));
        EstateModel model = api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(e => e.Severity == Severity.Error)));
        RecordEnvelope revision = model.ById[removal.Written!.Id];
        string before = removal.Checkpoint!.Written!.Id;
        Assert.Contains(interval, model.Trust.History.Predecessors[before]);
        foreach (RecordEnvelope kept in new[] { writing, policy, decision })
        {
            // Listed by the builder's checkpoint alone; the root operator's checkpoint before the removal follows it.
            Assert.Equal([interval], model.Trust.History.Listings[kept.Id]);
            Assert.True(model.Trust.WitnessedBefore(model.ById[kept.Id], revision), kept.Type);
            Assert.True(model.Counts(model.ById[kept.Id]), kept.Type);
            Assert.False(Warned(model, kept), kept.Type);
        }

        Assert.Contains(model.Current("estate-policy"), p => EstateModel.BodyString(p, "key") == "o-policy");
        Assert.False(PolicyAuthority.OperatorAt(model, o.Id, Now(f)));
    }

    /// <summary>
    /// The same record, listed only by the builder's checkpoint, on a fork beside the removal and the root operator's checkpoint listing
    /// it: no checkpoint by the root operator follows the builder's and precedes the removal, so the record is not kept, even once the
    /// root operator's next checkpoint joins the fork. Without the fork, the root operator's checkpoint between them keeps it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARecordListedOnAForkBesideTheRemovalIsNotKept(bool fork)
    {
        using var f = new PolicyFixture();
        Require(f);
        RecordEnvelope o = f.Add("principal", Person("O", "operator"));
        StoreSigner os = f.AdmitSigner(o.Id);
        Checkpoint(f, "adoption");
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        RecordEnvelope writing = OperatorWriting(f, o.Id, os);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        RecordEnvelope listing = Checkpoint(f, "interval", f.Estate.BuilderId, f.BuilderSigner);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));

        // On the fork, a copy that holds neither the builder's checkpoint nor what it lists takes the removal.
        var withheld = new List<(string Path, byte[] Bytes)>();
        if (fork)
        {
            foreach (RecordEnvelope record in new[] { writing, listing })
            {
                string path = Path.Combine(f.Estate.Root, record.ExpectedPath);
                withheld.Add((path, File.ReadAllBytes(path)));
                File.Delete(path);
            }
        }
        else
        {
            Checkpoint(f, "manual");
        }

        RecordEnvelope removal = f.Add("principal", Person("O", "contributor"), supersedes: o.Id);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        RecordEnvelope carrying = Checkpoint(f, "manual");
        foreach ((string path, byte[] bytes) in withheld) File.WriteAllBytes(path, bytes);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        RecordEnvelope joined = Checkpoint(f, "manual");

        EstateModel model = f.Estate.Load();
        Assert.Equal([listing.Id], model.Trust.History.Listings[writing.Id]);
        Assert.Equal([carrying.Id], model.Trust.History.Listings[removal.Id]);
        // On the fork the root operator's next checkpoint joins the builder's directly, after the removal.
        if (fork) Assert.Contains(listing.Id, model.Trust.History.Predecessors[joined.Id]);
        Assert.Equal(!fork, model.Trust.WitnessedBefore(model.ById[writing.Id], model.ById[removal.Id]));
        Assert.Equal(!fork, model.Counts(model.ById[writing.Id]));
        Assert.Equal(!fork, PolicyAuthority.OperatorAt(model, model.ById[writing.Id]));
        if (fork) Assert.Contains(model.Findings, e => e.Code == "policy.authority" && e.Severity == Severity.Error && e.Location.StartsWith(writing.ExpectedPath, StringComparison.Ordinal));
        else Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(e => e.Severity == Severity.Error)));
    }

    /// <summary>
    /// A record the removed operator dates back into its time and lists in a checkpoint it signs itself, after the removal or in a copy
    /// without it: a later checkpoint by the root operator follows that checkpoint, but after the removal, so the record is not kept.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARecordListedOnlyByTheRemovedOperatorsOwnCheckpointIsNotKept(bool withoutTheRemoval)
    {
        using var f = new PolicyFixture();
        Require(f);
        RecordEnvelope o = f.Add("principal", Person("O", "operator"));
        StoreSigner os = f.AdmitSigner(o.Id);
        Checkpoint(f, "adoption");
        DateTimeOffset appointed = f.Estate.Clock;
        f.Estate.SetClock(appointed.AddHours(1));
        RecordEnvelope removal = f.Add("principal", Person("O", "contributor"), supersedes: o.Id);
        DateTimeOffset resume = f.Estate.Clock;
        string removalPath = Path.Combine(f.Estate.Root, removal.ExpectedPath);
        byte[] removalBytes = File.ReadAllBytes(removalPath);
        if (withoutTheRemoval) File.Delete(removalPath);

        f.Estate.SetClock(appointed.AddMinutes(10));
        RecordEnvelope writing = OperatorWriting(f, o.Id, os);
        f.Estate.SetClock(withoutTheRemoval ? appointed.AddMinutes(20) : resume.AddMinutes(1));
        RecordEnvelope own = Checkpoint(f, "manual", o.Id, os);
        if (withoutTheRemoval) File.WriteAllBytes(removalPath, removalBytes);
        f.Estate.SetClock(resume.AddMinutes(2));
        RecordEnvelope root = Checkpoint(f, "manual");

        EstateModel model = f.Estate.Load();
        Assert.Equal([own.Id], model.Trust.History.Listings[writing.Id]);
        Assert.Contains(own.Id, model.Trust.History.Predecessors[root.Id]);
        Assert.False(model.Trust.WitnessedBefore(model.ById[writing.Id], model.ById[removal.Id]));
        Assert.False(model.Counts(model.ById[writing.Id]));
        Assert.False(PolicyAuthority.OperatorAt(model, model.ById[writing.Id]));
        Assert.Contains(model.Findings, e => e.Code == "policy.authority" && e.Severity == Severity.Error && e.Location.StartsWith(writing.ExpectedPath, StringComparison.Ordinal));
    }

    /// <summary>
    /// An estate whose adoption a person holding administer signed: what the operator recorded before it is listed by that adoption alone.
    /// A removal the runtime writes is preceded by its checkpoint, which the root operator signs and which follows the adoption, so those
    /// records keep counting. A removal written with no checkpoint by the root operator or a standing operator after the adoption leaves
    /// each of them counting for nothing, the warning policy.operator-removed.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AfterAnAdministratorsAdoptionTheCheckpointBeforeTheRemovalKeepsWhatTheAdoptionListed(bool throughTheRuntime)
    {
        using var f = new PolicyFixture();
        Require(f);
        using EstateService api = EstateService.Open(f.Estate.Root);
        RuntimeHeldRootDevice(f, api);
        RecordEnvelope a = f.Add("principal", Person("A", "reviewer"));
        StoreSigner signer = f.AdmitSigner(a.Id);
        f.Add("authority", new JsonObject { ["grantee"] = a.Id, ["scope"] = new JsonArray(Scopes.Administer), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = f.Decision.Id });
        RecordEnvelope o = f.Add("principal", Person("O", "operator"));
        StoreSigner os = f.AdmitSigner(o.Id);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        RecordEnvelope writing = OperatorWriting(f, o.Id, os);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        RecordEnvelope adoption = Checkpoint(f, "adoption", a.Id, signer);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(1));
        Assert.Equal(0, api.Check().Errors);

        RecordEnvelope revision;
        if (throughTheRuntime)
        {
            WriteOutcome removal = api.NewRecord(new NewRecordRequest("principal", f.Estate.OperatorId, PolicyFixture.Element(Person("O", "contributor")), Supersedes: o.Id));
            Assert.True(removal.Ok, Outcome(removal));
            Assert.NotNull(removal.Checkpoint?.Written);
            revision = api.Session.Load().ById[removal.Written!.Id];
        }
        else
        {
            revision = f.Add("principal", Person("O", "contributor"), supersedes: o.Id);
        }

        EstateModel model = api.Session.Load();
        Assert.Equal([adoption.Id], model.Trust.History.Listings[writing.Id]);
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(e => e.Severity == Severity.Error)));
        Assert.Equal(throughTheRuntime, model.Trust.WitnessedBefore(model.ById[writing.Id], model.ById[revision.Id]));
        Assert.Equal(throughTheRuntime, model.Counts(model.ById[writing.Id]));
        Assert.Equal(!throughTheRuntime, Warned(model, writing));
    }
}
