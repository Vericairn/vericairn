// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Records.Types;
using Vericairn.Runtime.Policies;
using static Vericairn.Runtime.Tests.RecurringDutyEvaluationTests;

namespace Vericairn.Runtime.Tests;

public class DutyNativeProofTests
{
    [Fact]
    public void AnAgentRunIsNeverADeclaredNativeActionProof()
    {
        using var f = new PolicyFixture();
        Policy(f, new JsonObject { ["key"] = "native", ["kind"] = "observed-operation", ["provider"] = "fixture-provider", ["action"] = OperationType.AgentRun, ["author"] = "operator" });
        PolicyEvaluation evaluation = Evaluate(f, FirstDue);
        Assert.Equal("unavailable", Assert.Single(evaluation.Findings).Verdict);
        Assert.Contains(evaluation.Diagnostics, x => x.Code == "policy.parameters" && x.Severity == Severity.Error);
    }

    private static RecordEnvelope Policy(PolicyFixture f, JsonObject clause, RecordEnvelope? subject = null, string review = "independent")
    {
        JsonObject body = f.PolicyBody(); JsonObject parameters = Parameters(body);
        if (subject is not null) parameters["subject"] = PolicyFixture.Ref(subject, false);
        parameters["discharge"]!["records"] = new JsonArray(clause); parameters["discharge"]!["review"] = review;
        return f.Policy(body);
    }

    private static RecordEnvelope Complete(PolicyFixture f, RecordEnvelope policy, RecordEnvelope subject, params RecordEnvelope[] proofs)
    {
        JsonObject link = f.DutyLink(policy); link["subject"] = PolicyFixture.Ref(subject);
        link["proofs"] = new JsonArray(new JsonObject { ["clause"] = "native", ["records"] = new JsonArray(proofs.Select(r => (JsonNode)PolicyFixture.Ref(r)).ToArray()) });
        return f.Review(f.Result(link));
    }

    internal static RecordEnvelope Operation(PolicyFixture f, RecordEnvelope subject, string provider = "fixture-provider", string action = "fixture-renewal", bool builder = false, bool uncertain = false, string? performed = null)
    {
        JsonObject body = new()
        {
            ["provider"] = provider, ["action"] = action, ["identity"] = Ulid.NewUlid().Value, ["intent"] = "Observe a synthetic fixture effect.",
            ["target"] = new JsonObject { ["fixture"] = "synthetic-only" }, ["status"] = "not-started", ["evidence"] = new JsonArray(),
        };
        RecordEnvelope intent = f.Add("operation", body, builder, subject.Reference);
        body["status"] = "dispatched"; body["dispatchedAt"] = intent.At;
        RecordEnvelope dispatched = f.Add("operation", body, builder, subject.Reference, intent.Id);
        body["status"] = uncertain ? "uncertain" : "observed-success";
        body["observed"] = new JsonObject { ["summary"] = "Synthetic observation only.", ["at"] = performed ?? dispatched.At };
        return f.Add("operation", body, builder, subject.Reference, dispatched.Id);
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("provider", false)]
    [InlineData("action", false)]
    [InlineData("subject", false)]
    [InlineData("uncertain", false)]
    [InlineData("author", false)]
    [InlineData("old-action", false)]
    [InlineData("editorial", false)]
    public void ObservedOperationsRequireTheExactNativeActionAndAuthority(string fault, bool completed)
    {
        using var f = new PolicyFixture();
        if (fault == "author") f.Add("authority", new JsonObject { ["grantee"] = f.Estate.BuilderId, ["scope"] = new JsonArray(Scopes.LocalEngineering), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = f.Decision.Id });
        RecordEnvelope policy = Policy(f, new JsonObject { ["key"] = "native", ["kind"] = "observed-operation", ["provider"] = "fixture-provider", ["action"] = "fixture-renewal", ["author"] = "operator" });
        if (fault != "editorial") f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        RecordEnvelope operation = Operation(f, fault == "subject" ? f.OperatorDevice : f.Estate.Estate,
            fault == "provider" ? "another-provider" : "fixture-provider", fault == "action" ? "another-action" : "fixture-renewal", builder: fault == "author", uncertain: fault == "uncertain",
            performed: fault == "old-action" ? "2026-10-13T12:00:00Z" : null);
        if (fault == "editorial")
        {
            f.Estate.SetClock(At("2026-10-13T14:00:00Z")); JsonObject edit = PolicyFixture.Body(operation); edit["observed"]!["summary"] = "Clarify an old effect.";
            operation = f.Add("operation", edit, subject: operation.Subject, supersedes: operation.Id);
        }
        RecordEnvelope review = Complete(f, policy, f.Estate.Estate, operation);
        if (fault == "editorial")
        {
            PolicyEvaluation invalid = PolicyExceptionEvaluationTests.Evaluate(f);
            Assert.Contains(invalid.Diagnostics, x => x.Code == "operation.transition" && x.Severity == Severity.Error);
            Assert.Equal("unavailable", Assert.Single(invalid.Findings).Verdict);
            return;
        }
        PolicyFinding finding = Number(Evaluate(f, "2026-10-20T14:00:00Z"), 0);
        Assert.Equal(completed, finding.Duty!.Completed);
        Assert.Equal(!completed, finding.ReleaseHold);
        if (completed) Assert.Equal(review.At, finding.Duty.CompletedAt);
        Assert.Equal(3 + (fault == "editorial" ? 1 : 0), f.Estate.Load().OfType("operation").Count());
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("rename", false)]
    [InlineData("another-device", false)]
    [InlineData("before-due", false)]
    public void DeviceRotationNeedsTheNamedIdentityAndBothNativeKeySignatures(string fault, bool completed)
    {
        using var f = new PolicyFixture();
        string fixtureHome = Path.Combine(Path.GetTempPath(), "vericairn-duty-device-" + Ulid.NewUlid().Value);
        try
        {
            var store = new FileKeyStore(fixtureHome); string id = Ulid.NewUlid().Value;
            JsonObject body = PolicyFixture.Body(f.OperatorDevice); body["publicKey"] = RecordSigning.EncodePublicKey(store.Create(id));
            RecordEnvelope device = f.Estate.Add("device", f.Estate.OperatorId, body.ToJsonString(), id: id, signer: new StoreSigner(store, id));
            f.Add("admission", new JsonObject { ["principal"] = f.Estate.OperatorId, ["status"] = "admitted", ["note"] = "Admit the fixture device." }, subject: device.Reference);
            RecordEnvelope policy = Policy(f, new JsonObject { ["key"] = "native", ["kind"] = "device-rotation" }, fault == "another-device" ? f.OperatorDevice : device);
            if (fault != "before-due") f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
            string revision = Ulid.NewUlid().Value; body["name"] = "Revised fixture device";
            if (fault != "rename") body["publicKey"] = RecordSigning.EncodePublicKey(store.Create(revision));
            RecordEnvelope rotated = f.Estate.Add("device", f.Estate.OperatorId, body.ToJsonString(), supersedes: device.Id, id: revision,
                signers: fault == "rename" ? [new StoreSigner(store, id)] : [new StoreSigner(store, id), new StoreSigner(store, revision, id)]);
            if (fault == "before-due") f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
            Complete(f, policy, fault == "another-device" ? f.OperatorDevice : rotated, device, rotated);
            Assert.Equal(completed, Number(Evaluate(f), 0).Duty!.Completed);
        }
        finally { if (Directory.Exists(fixtureHome)) Directory.Delete(fixtureHome, true); }
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("wrong-operation", false)]
    [InlineData("no-native-time", false)]
    [InlineData("same-identity", false)]
    public void CredentialRotationBindsThePriorSuccessorAndOperatorObservation(string fault, bool completed)
    {
        using var f = new PolicyFixture();
        RecordEnvelope prior = f.Add("credential-reference", DutyScheduleSourceTests.CredentialBody(f));
        RecordEnvelope policy = Policy(f, new JsonObject { ["key"] = "native", ["kind"] = "credential-rotation", ["provider"] = "fixture-provider", ["action"] = "fixture-renewal" }, prior, review: "operator");
        f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        JsonObject rotatedBody = PolicyFixture.Body(prior); rotatedBody["status"] = "rotated";
        if (fault != "no-native-time") rotatedBody["rotatedAt"] = RecordParser.FormatTimestamp(f.Estate.Clock);
        RecordEnvelope rotated = f.Add("credential-reference", rotatedBody, supersedes: prior.Id);
        JsonObject successorBody = DutyScheduleSourceTests.CredentialBody(f, fault == "same-identity" ? "fixture-reference" : "fixture-successor", "2026-11-14T00:00:00Z"); successorBody["rotatedAt"] = RecordParser.FormatTimestamp(f.Estate.Clock);
        RecordEnvelope successor = f.Add("credential-reference", successorBody, supersedes: fault == "same-identity" ? rotated.Id : null);
        RecordEnvelope operation = Operation(f, fault == "wrong-operation" ? successor : prior);
        Complete(f, policy, prior, prior, rotated, successor, operation);
        Assert.Equal(completed, Number(Evaluate(f), 0).Duty!.Completed);
    }

    [Fact]
    public void ACompletedCredentialRotationCanDeclareItsExactActiveSuccessorsNextDate()
    {
        using var f = new PolicyFixture();
        RecordEnvelope prior = f.Add("credential-reference", DutyScheduleSourceTests.CredentialBody(f));
        JsonObject body = DutyScheduleSourceTests.DateBody(f, prior);
        Parameters(body)["discharge"]!["review"] = "operator";
        Parameters(body)["discharge"]!["records"] = new JsonArray(new JsonObject { ["key"] = "native", ["kind"] = "credential-rotation", ["provider"] = "fixture-provider", ["action"] = "fixture-renewal" });
        RecordEnvelope policy = f.Policy(body); f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        JsonObject rotatedBody = PolicyFixture.Body(prior); rotatedBody["status"] = "rotated"; rotatedBody["rotatedAt"] = RecordParser.FormatTimestamp(f.Estate.Clock);
        RecordEnvelope rotated = f.Add("credential-reference", rotatedBody, supersedes: prior.Id);
        JsonObject successorBody = DutyScheduleSourceTests.CredentialBody(f, "fixture-successor", "2026-11-14T00:00:00Z"); successorBody["rotatedAt"] = RecordParser.FormatTimestamp(f.Estate.Clock);
        RecordEnvelope successor = f.Add("credential-reference", successorBody); RecordEnvelope operation = Operation(f, prior);
        JsonObject link = DutyScheduleSourceTests.DateLink(f, policy, prior);
        link["proofs"] = new JsonArray(new JsonObject { ["clause"] = "native", ["records"] = new JsonArray(PolicyFixture.Ref(prior), PolicyFixture.Ref(rotated), PolicyFixture.Ref(successor), PolicyFixture.Ref(operation)) });
        f.Review(f.Result(link)); Assert.False(Assert.Single(Evaluate(f).Findings).Duty!.Completed);
        JsonObject next = PolicyFixture.Body(policy); JsonObject parameters = Parameters(next);
        parameters["subject"] = PolicyFixture.Ref(successor, false); parameters["schedule"]!["at"] = "2026-11-14T00:00:00Z"; parameters["schedule"]!["dateSource"]!["record"] = PolicyFixture.Ref(successor);
        RecordEnvelope nextPolicy = f.Policy(next, policy);
        PolicyFinding completed = Assert.Single(Evaluate(f).Findings, x => x.Duty!.Origin == policy.Reference);
        Assert.True(completed.Duty!.Completed);
        Assert.Equal(prior.Id, completed.Duty.SubjectIdentity);
        Assert.Equal(nextPolicy.At, completed.Duty.CompletedAt);
    }

    [Theory]
    [InlineData("valid", true, "satisfied")]
    [InlineData("pending", false, "unmet")]
    [InlineData("present", false, "unmet")]
    [InlineData("missing-inventory", false, "unavailable")]
    public void RetirementRequiresBothTheNamedNativeEventAndItsCompleteRemovalInventory(string state, bool completed, string verdict)
    {
        using var f = new PolicyFixture();
        JsonObject body = new() { ["path"] = "temporary/fixture.txt", ["reason"] = "Synthetic temporary file.", ["condition"] = "When the fixture duty is due." };
        RecordEnvelope retirement = f.Add("retirement", body);
        RecordEnvelope policy = Policy(f, new JsonObject { ["key"] = "native", ["kind"] = "retirement", ["retirement"] = retirement.Id });
        f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        if (state != "pending") { body["retiredAt"] = RecordParser.FormatTimestamp(f.Estate.Clock); retirement = f.Add("retirement", body, supersedes: retirement.Id); }
        Complete(f, policy, f.Estate.Estate, retirement);
        PolicyRepository repository = new(f.Estate.Estate.Id, "fixture-snapshot", PolicyBasis.RepositoryContract, true,
            state == "present" ? [PolicyBasisTests.File("temporary/fixture.txt", "retained")] : []);
        PolicyFinding finding = Number(Evaluate(f, "2026-10-20T14:00:00Z", repositories: state == "missing-inventory" ? [] : [repository]), 0);
        Assert.Equal(completed, finding.Duty!.Completed); Assert.Equal(verdict, finding.Verdict);
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("another-key", false)]
    [InlineData("editorial", false)]
    public void ObligationProofRequiresTheExactNamedObligationAndANewNativeDischarge(string fault, bool completed)
    {
        using var f = new PolicyFixture();
        JsonObject decisionBody = f.DecisionBody();
        decisionBody["obligations"] = new JsonArray(
            new JsonObject { ["key"] = "cleanup", ["owner"] = f.Estate.OperatorId, ["gates"] = "When due.", ["summary"] = "Clean the fixture.", ["resolution"] = "action", ["discharge"] = "A recorded cleanup." },
            new JsonObject { ["key"] = "other", ["owner"] = f.Estate.OperatorId, ["gates"] = "When due.", ["summary"] = "Inspect another fixture.", ["resolution"] = "action", ["discharge"] = "A recorded inspection." });
        RecordEnvelope decision = f.Add("decision", decisionBody);
        RecordEnvelope policy = Policy(f, new JsonObject { ["key"] = "native", ["kind"] = "obligation", ["decision"] = decision.Id, ["obligation"] = "cleanup" });
        if (fault != "editorial") f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        JsonObject body = new() { ["obligation"] = fault == "another-key" ? "other" : "cleanup", ["summary"] = "Discharged the fixture obligation.", ["evidence"] = new JsonArray(new JsonObject { ["summary"] = "Observed fixture completion." }) };
        RecordEnvelope discharge = f.Add("discharge", body, subject: decision.Reference);
        if (fault == "editorial") { f.Estate.SetClock(At("2026-10-13T14:00:00Z")); body["summary"] = "Clarify the historical discharge."; discharge = f.Add("discharge", body, subject: decision.Reference, supersedes: discharge.Id); }
        Complete(f, policy, f.Estate.Estate, discharge);
        Assert.Equal(completed, Number(Evaluate(f), 0).Duty!.Completed);
    }
}
