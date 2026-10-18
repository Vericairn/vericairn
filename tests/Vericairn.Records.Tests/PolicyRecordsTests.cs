// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

public class PolicyRecordsTests
{
    [Fact]
    public void SignedPoliciesExceptionsAndWritingEntriesPreserveTheirHistory()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        RecordEnvelope writing = f.Add("writing-exception", PolicyFixture.WritingBody());
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero));
        RecordEnvelope exception = f.Add("policy-exception", f.ExceptionBody(policy), subject: policy.Reference);
        Dictionary<string, byte[]> before = f.Estate.Load().Records.ToDictionary(r => r.ExpectedPath, r => File.ReadAllBytes(Path.Combine(f.Estate.Root, r.ExpectedPath)));
        JsonObject removal = PolicyFixture.Body(writing);
        removal["state"] = "removed";
        removal["removalReason"] = "The spelling is no longer used.";
        f.Add("writing-exception", removal, supersedes: writing.Id);
        JsonObject revocation = PolicyFixture.Body(exception);
        revocation["state"] = "revoked";
        revocation["reason"] = "The hold applies again.";
        f.Add("policy-exception", revocation, subject: policy.Reference, supersedes: exception.Id);
        f.AssertValid();
        Assert.All(before, p => Assert.Equal(p.Value, File.ReadAllBytes(Path.Combine(f.Estate.Root, p.Key))));
        Assert.Empty(f.Estate.Load().OfType("authority"));
        Assert.Empty(f.Estate.Load().OfType("acceptance"));
    }

    [Theory]
    [InlineData("estate-policy")]
    [InlineData("policy-exception")]
    [InlineData("writing-exception")]
    public void EveryLifecycleRecordRequiresAnAdmittedSignatureEvenWhenTheCharterDoesNot(string type)
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero));
        RecordEnvelope unsigned = type switch
        {
            "estate-policy" => f.Policy(f.PolicyBody("second", duty: false), sign: false),
            "policy-exception" => f.Add(type, f.ExceptionBody(policy), subject: policy.Reference, sign: false),
            _ => f.Add(type, PolicyFixture.WritingBody(), sign: false),
        };
        Has(f, unsigned, "signature.required");
    }

    [Theory]
    [InlineData("estate-policy")]
    [InlineData("policy-exception")]
    [InlineData("writing-exception")]
    public void AForeignSignatureCannotAttributeAPolicyActionToTheOperator(string type)
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero));
        JsonObject body = type switch
        {
            "estate-policy" => PolicyFixture.Body(policy),
            "policy-exception" => f.ExceptionBody(policy),
            _ => PolicyFixture.WritingBody(),
        };
        RecordEnvelope forged = f.Estate.Add(type, f.Estate.OperatorId, body.ToJsonString(), type == "policy-exception" ? policy.Reference : null, signer: f.BuilderSigner);
        Has(f, forged, "signature.principal");
    }

    [Fact]
    public void AnOperatorCannotMisattributeItsActionToAnUnrelatedGrant()
    {
        using var f = new PolicyFixture();
        RecordEnvelope grant = f.Grant(Scopes.ManageWritingExceptions, PolicyFixture.WritingBounds());
        Has(f, f.Add("writing-exception", PolicyFixture.WritingBody(grant)), "policy.authority");
    }

    [Theory]
    [InlineData("unsigned-operator")]
    [InlineData("self-promotion")]
    [InlineData("operator-appointment")]
    public void AnAdmittedKeyCannotTurnAnUnattributableRoleClaimIntoPolicyAuthority(string appointment)
    {
        using var f = new PolicyFixture();
        JsonObject principal = PolicyFixture.Body(f.Estate.Builder);
        principal["kind"] = "human";
        principal["roles"] = new JsonArray(Roles.Operator);
        principal.Remove("agent");
        f.Add("principal", principal, builder: appointment == "self-promotion", supersedes: f.Estate.Builder.Id, sign: appointment != "unsigned-operator");
        RecordEnvelope policy = f.Policy(builder: true);
        RecordEnvelope writing = f.Add("writing-exception", PolicyFixture.WritingBody(), builder: true);
        if (appointment == "operator-appointment") f.AssertValid();
        else
        {
            Has(f, policy, "policy.authority");
            Has(f, writing, "policy.authority");
        }
    }

    [Fact]
    public void NeitherAnOperatorNameNorACopiedApprovalSuppliesASignatureOrAuthority()
    {
        using var f = new PolicyFixture();
        JsonObject body = f.PolicyBody();
        RecordEnvelope unsignedApproval = f.Approve(body, sign: false);
        body["basis"] = new JsonArray(new JsonObject { ["decision"] = PolicyFixture.Ref(unsignedApproval), ["requirement"] = "definition" });
        RecordEnvelope policy = f.Add("estate-policy", body);
        Has(f, policy, "policy.approval");
        RecordEnvelope agentApproval = f.Approve(body, builder: true);
        Has(f, agentApproval, "policy.approval");
    }

    [Theory]
    [InlineData(Scopes.LocalEngineering)]
    [InlineData(Scopes.Administer)]
    [InlineData(Scopes.RepositoryWrite)]
    public void GeneralAuthorityNeverGrantsPolicyOrWritingPower(string scope)
    {
        using var f = new PolicyFixture();
        f.Add("authority", new JsonObject { ["grantee"] = f.Estate.BuilderId, ["scope"] = new JsonArray(scope), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = f.Decision.Id });
        RecordEnvelope policy = f.Policy(builder: true);
        RecordEnvelope writing = f.Add("writing-exception", PolicyFixture.WritingBody(), builder: true);
        Has(f, policy, "policy.authority");
        Has(f, writing, "policy.authority");
    }

    [Fact]
    public void ADefinitionApprovalIsExactAndDoesNotGrantManagementAuthority()
    {
        using var f = new PolicyFixture();
        JsonObject definition = f.PolicyBody();
        RecordEnvelope approval = f.Approve(definition);
        definition["level"] = "warn";
        definition["basis"] = new JsonArray(new JsonObject { ["decision"] = PolicyFixture.Ref(approval), ["requirement"] = "definition" });
        RecordEnvelope changed = f.Add("estate-policy", definition, builder: true);
        Has(f, changed, "policy.approval");
        Has(f, changed, "policy.authority");
    }

    [Fact]
    public void AManagementGrantAndExactApprovalPermitOnlyTheirBoundedDefinition()
    {
        using var f = new PolicyFixture();
        RecordEnvelope grant = f.Grant(Scopes.ManageEstatePolicies, PolicyFixture.PolicyBounds());
        f.Policy(builder: true, grant: grant);
        f.AssertValid();
        RecordEnvelope outside = f.Policy(f.PolicyBody("outside"), builder: true, grant: grant);
        Has(f, outside, "policy.authority");
        RecordEnvelope spelling = f.Add("writing-exception", PolicyFixture.WritingBody(grant), builder: true);
        Has(f, spelling, "policy.authority");
    }

    [Fact]
    public void OwnershipIsResponsibilityAndNeverAGrant()
    {
        using var f = new PolicyFixture();
        JsonObject definition = f.PolicyBody();
        definition["owner"] = f.Estate.BuilderId;
        RecordEnvelope policy = f.Policy(definition);
        JsonObject edited = PolicyFixture.Body(policy);
        edited["title"] = "An editorial change";
        RecordEnvelope unauthorized = f.Add("estate-policy", edited, builder: true, supersedes: policy.Id);
        Has(f, unauthorized, "policy.authority");
        Assert.DoesNotContain(f.Estate.Load().Findings, p => p.Code == "policy.approval" && p.Location == unauthorized.ExpectedPath);
    }

    [Fact]
    public void WritingGrantsAreExplicitAndHistorySurvivesExpiryAndRevocation()
    {
        using var f = new PolicyFixture();
        RecordEnvelope grant = f.Grant(Scopes.ManageWritingExceptions, PolicyFixture.WritingBounds());
        RecordEnvelope entry = f.Add("writing-exception", PolicyFixture.WritingBody(grant), builder: true);
        JsonObject replacement = PolicyFixture.Body(grant);
        replacement["scope"] = new JsonArray(Scopes.LocalEngineering);
        replacement.Remove("bounds");
        f.Add("authority", replacement, supersedes: grant.Id);
        f.AssertValid();
        JsonObject removed = PolicyFixture.Body(entry);
        removed["state"] = "removed";
        removed["removalReason"] = "Attempt removal after the grant ended.";
        RecordEnvelope invalid = f.Add("writing-exception", removed, builder: true, supersedes: entry.Id);
        Has(f, invalid, "policy.authority");
        Assert.DoesNotContain(f.Estate.Load().Findings, p => p.Severity == Severity.Error && p.Location.StartsWith(entry.ExpectedPath, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("tokens")]
    [InlineData("scope")]
    [InlineData("actions")]
    [InlineData("expired")]
    [InlineData("unsigned")]
    [InlineData("future")]
    public void WritingGrantBoundsCannotBeBroadened(string failure)
    {
        using var f = new PolicyFixture();
        JsonObject bounds = PolicyFixture.WritingBounds();
        if (failure == "scope") bounds["coverage"] = new JsonObject { ["paths"] = new JsonArray(new JsonObject { ["repository"] = f.Estate.Estate.Id, ["selectors"] = new JsonArray("docs/*.md") }) };
        if (failure == "actions") bounds["actions"] = new JsonArray("remove");
        RecordEnvelope grant = f.Grant(Scopes.ManageWritingExceptions, bounds, sign: failure != "unsigned");
        JsonObject body = PolicyFixture.WritingBody(grant);
        if (failure == "tokens") body["token"] = "Other311";
        if (failure == "expired") f.Estate.SetClock(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        if (failure == "future") f.Estate.SetClock(grant.AtTime.AddSeconds(-2));
        RecordEnvelope entry = f.Add("writing-exception", body, builder: true);
        Assert.Contains(f.Estate.Load().Findings, p => p.Severity == Severity.Error && p.Location.StartsWith(entry.ExpectedPath, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void RemovingEveryPolicyScopeStillRequiresAnAttributableFormatThreeRevocation(long format)
    {
        using var f = new PolicyFixture();
        RecordEnvelope grant = f.Grant(Scopes.ManageWritingExceptions, PolicyFixture.WritingBounds());
        JsonObject replacement = PolicyFixture.Body(grant);
        replacement["scope"] = new JsonArray(Scopes.LocalEngineering);
        replacement.Remove("bounds");
        RecordEnvelope unsigned = f.Estate.Add("authority", f.Estate.OperatorId, replacement.ToJsonString(), supersedes: grant.Id, format: format);
        Has(f, unsigned, "signature.required");
        if (format == 2) Has(f, unsigned, "authority.format");
    }

    [Fact]
    public void ScopeContainmentCanBeProvedWithoutGuessingWildcardInclusion()
    {
        using var f = new PolicyFixture();
        JsonObject bounds = PolicyFixture.WritingBounds();
        bounds.Remove("tokens");
        bounds["allTokens"] = true;
        bounds["coverage"] = new JsonObject { ["paths"] = new JsonArray(new JsonObject { ["repository"] = f.Estate.Estate.Id, ["selectors"] = new JsonArray("docs/**") }), ["records"] = new JsonArray(new JsonObject { ["record"] = f.Decision.Id }) };
        RecordEnvelope grant = f.Grant(Scopes.ManageWritingExceptions, bounds);
        JsonObject entry = PolicyFixture.WritingBody(grant);
        entry["scope"] = new JsonObject { ["paths"] = new JsonArray(new JsonObject { ["repository"] = f.Estate.Estate.Id, ["selectors"] = new JsonArray("docs/reference/*.md") }), ["records"] = new JsonArray(new JsonObject { ["record"] = f.Decision.Id, ["field"] = "summary" }) };
        f.Add("writing-exception", entry, builder: true);
        f.AssertValid();
    }

    [Theory]
    [InlineData("Open*")]
    [InlineData("Open[0-9]")]
    [InlineData("Open311x")]
    [InlineData("OPEN311")]
    [InlineData("Open311 Open312")]
    public void WritingTokensAreLiteralWholeScannerTokens(string token)
    {
        using var f = new PolicyFixture();
        JsonObject body = PolicyFixture.WritingBody();
        body["token"] = token;
        Has(f, f.Add("writing-exception", body), "writing.token");
    }

    [Fact]
    public void WritingRemovalPreservesEveryEntryFieldAndCannotReviveIt()
    {
        using var f = new PolicyFixture();
        RecordEnvelope first = f.Add("writing-exception", PolicyFixture.WritingBody());
        JsonObject body = PolicyFixture.Body(first);
        body["state"] = "removed";
        body["removalReason"] = "No longer used.";
        RecordEnvelope removed = f.Add("writing-exception", body, supersedes: first.Id);
        f.AssertValid();
        JsonObject revive = PolicyFixture.Body(removed);
        revive["state"] = "active";
        revive.Remove("removalReason");
        Has(f, f.Add("writing-exception", revive, supersedes: removed.Id), "writing.lifecycle");
    }

    [Fact]
    public void DuplicateTokensAreIndependentIdentitiesAndUnknownWritingFieldsAreRefused()
    {
        using var f = new PolicyFixture();
        f.Add("writing-exception", PolicyFixture.WritingBody());
        f.Add("writing-exception", PolicyFixture.WritingBody());
        f.AssertValid();
        JsonObject invalid = PolicyFixture.WritingBody();
        invalid["expiresAt"] = "2027-01-01T00:00:00Z";
        Has(f, f.Add("writing-exception", invalid), "body.unknown-key");
    }

    [Fact]
    public void PolicyKeysAndRetirementAreTerminalAndOldBytesRemainUnchanged()
    {
        using var f = new PolicyFixture();
        RecordEnvelope first = f.Policy(f.PolicyBody(duty: false));
        JsonObject body = PolicyFixture.Body(first);
        body["state"] = "retired";
        body["retirement"] = new JsonObject { ["reason"] = "Requirement replaced.", ["findings"] = "No outstanding duties." };
        RecordEnvelope retired = f.Policy(body, first);
        f.AssertValid();
        JsonObject revived = PolicyFixture.Body(retired);
        revived["state"] = "active";
        revived.Remove("retirement");
        Has(f, f.Policy(revived, retired), "policy.lifecycle");
        Has(f, f.Policy(f.PolicyBody(duty: false)), "policy.duplicate-key");
    }

    [Fact]
    public void ExceptionsRequireIndependentBoundedAuthorityAndExactOccurrenceTargets()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero));
        JsonObject bounds = new() { ["scope"] = Scopes.GrantPolicyExceptions, ["actions"] = new JsonArray("grant", "revoke"), ["policies"] = new JsonArray(policy.Id), ["levels"] = new JsonArray("refuse"), ["rules"] = new JsonArray("monthly"), ["maxExceptionDays"] = 2, ["targets"] = new JsonArray(f.OccurrenceTarget(policy)) };
        RecordEnvelope grant = f.Grant(Scopes.GrantPolicyExceptions, bounds);
        f.Add("policy-exception", f.ExceptionBody(policy, grant), builder: true, subject: policy.Reference);
        f.AssertValid();
        JsonObject excessive = f.ExceptionBody(policy, grant);
        excessive["expiresAt"] = RecordParser.FormatTimestamp(f.Estate.Clock.AddDays(3));
        Has(f, f.Add("policy-exception", excessive, builder: true, subject: policy.Reference), "policy.authority");
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("duration")]
    [InlineData("future-occurrence")]
    [InlineData("wildcard")]
    [InlineData("hash")]
    public void PolicyExceptionsCannotInventAnOccurrenceOrBroadenItsFinitePins(string failure)
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero));
        JsonObject body = f.ExceptionBody(policy);
        switch (failure)
        {
            case "expired": body["expiresAt"] = RecordParser.FormatTimestamp(f.Estate.Clock); break;
            case "duration": body["expiresAt"] = RecordParser.FormatTimestamp(f.Estate.Clock.AddDays(31)); break;
            case "future-occurrence": body["targets"]![0]!["occurrence"]!["number"] = 1; break;
            case "wildcard": body["targets"] = new JsonArray(new JsonObject { ["kind"] = "all" }); break;
            case "hash": body["targets"]![0]!["subject"]!["hash"] = "sha256:" + new string('0', 64); break;
        }
        RecordEnvelope exception = f.Add("policy-exception", body, subject: policy.Reference);
        Assert.Contains(f.Estate.Load().Findings, x => x.Severity == Severity.Error && x.Location.StartsWith(exception.ExpectedPath, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../docs/*.md")]
    [InlineData("docs//*.md")]
    [InlineData("docs/./*.md")]
    [InlineData("docs/a**b.md")]
    public void WritingSelectorsCannotEscapeOrExtendTheFiniteGrammar(string selector)
    {
        using var f = new PolicyFixture();
        JsonObject body = PolicyFixture.WritingBody();
        body["scope"] = new JsonObject { ["paths"] = new JsonArray(new JsonObject { ["repository"] = f.Estate.Estate.Id, ["selectors"] = new JsonArray(selector) }) };
        Has(f, f.Add("writing-exception", body), "policy.selector");
    }

    [Fact]
    public void ADelegateCannotWaiveItsOwnDutyAndAnExceptionDoesNotEndDebt()
    {
        using var f = new PolicyFixture();
        JsonObject definition = f.PolicyBody();
        definition["owner"] = f.Estate.BuilderId;
        RecordEnvelope policy = f.Policy(definition);
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero));
        JsonObject bounds = new() { ["scope"] = Scopes.GrantPolicyExceptions, ["actions"] = new JsonArray("grant"), ["policies"] = new JsonArray(policy.Id), ["levels"] = new JsonArray("refuse"), ["rules"] = new JsonArray("monthly"), ["maxExceptionDays"] = 2, ["targets"] = new JsonArray(f.OccurrenceTarget(policy)) };
        RecordEnvelope grant = f.Grant(Scopes.GrantPolicyExceptions, bounds);
        Has(f, f.Add("policy-exception", f.ExceptionBody(policy, grant), builder: true, subject: policy.Reference), "exception.self");
        f.Add("policy-exception", f.ExceptionBody(policy), subject: policy.Reference);
        JsonObject retirement = PolicyFixture.Body(policy);
        retirement["state"] = "retired";
        retirement["retirement"] = new JsonObject { ["reason"] = "Stop the duty.", ["findings"] = "An exception remains evidence of deferred enforcement." };
        Has(f, f.Policy(retirement, policy), "duty.inventory");
    }

    [Fact]
    public void FormatTwoCannotCarryNewRecordsFieldsOrAuthorityScopes()
    {
        using var f = new PolicyFixture(format: 2);
        RecordEnvelope entry = f.Add("writing-exception", PolicyFixture.WritingBody());
        Has(f, entry, "record.type-format");
        RecordEnvelope grant = f.Grant(Scopes.ManageWritingExceptions, PolicyFixture.WritingBounds());
        Has(f, grant, "authority.format");
        JsonObject decision = f.DecisionBody();
        decision["policies"] = new JsonArray(new JsonObject { ["key"] = "proposal", ["action"] = "declare", ["policy"] = "test-policy", ["change"] = "Design the policy." });
        Has(f, f.Add("decision", decision), "body.format");
    }

    [Fact]
    public void APolicyConflictCannotBeReconciledWithOrdinaryEngineeringAuthority()
    {
        using var f = new PolicyFixture();
        RecordEnvelope original = f.Add("writing-exception", PolicyFixture.WritingBody());
        JsonObject removal = PolicyFixture.Body(original);
        removal["state"] = "removed";
        removal["removalReason"] = "Retain both branches in history.";
        RecordEnvelope first = f.Add("writing-exception", removal, supersedes: original.Id);
        RecordEnvelope second = f.Add("writing-exception", removal, supersedes: original.Id);
        RecordEnvelope reconcile = f.Add("reconciliation", new JsonObject { ["kept"] = first.Id, ["withdrawn"] = new JsonArray(second.Id), ["note"] = "Choose one signed removal." }, builder: true, subject: original.Reference);
        Has(f, reconcile, "policy.authority");

        RecordEnvelope disguised = f.Estate.Add("reconciliation", f.Estate.BuilderId, reconcile.Body.GetRawText(), original.Reference, signer: f.BuilderSigner, format: 2);
        Has(f, disguised, "policy.format");
        Has(f, disguised, "policy.authority");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("self")]
    [InlineData("independent")]
    public void DelegatedFileExceptionsRequireIndependentSignedOutputProvenance(string provenance)
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy(f.PolicyBody(duty: false));
        string hash = "sha256:" + new string('1', 64);
        if (provenance != "unknown")
        {
            bool builder = provenance == "self";
            RecordEnvelope result = f.Result(builder: builder);
            JsonObject body = PolicyFixture.Body(result);
            body["outputs"] = new JsonArray(new JsonObject { ["name"] = "fixture", ["hash"] = hash, ["location"] = "docs/fixture.txt" });
            f.Add("result", body, builder: builder, subject: result.Subject, supersedes: result.Id);
        }
        JsonObject target = new() { ["kind"] = "file", ["repository"] = f.Estate.Estate.Id, ["path"] = "docs/fixture.txt", ["hash"] = hash };
        JsonObject bounds = new() { ["scope"] = Scopes.GrantPolicyExceptions, ["actions"] = new JsonArray("grant"), ["policies"] = new JsonArray(policy.Id), ["levels"] = new JsonArray("refuse"), ["rules"] = new JsonArray("age"), ["maxExceptionDays"] = 2, ["targets"] = new JsonArray(target.DeepClone()) };
        RecordEnvelope grant = f.Grant(Scopes.GrantPolicyExceptions, bounds);
        JsonObject exception = f.ExceptionBody(policy, grant);
        exception["rule"] = "age";
        exception["targets"] = new JsonArray(target);
        RecordEnvelope entry = f.Add("policy-exception", exception, builder: true, subject: policy.Reference);
        if (provenance == "independent") f.AssertValid();
        else Has(f, entry, "exception.self");
    }

    [Fact]
    public void ChoosingBetweenPolicyGrantsIsAnOperatorActionEvenAfterTheirScopesWereRemoved()
    {
        using var f = new PolicyFixture();
        RecordEnvelope original = f.Grant(Scopes.ManageWritingExceptions, PolicyFixture.WritingBounds());
        JsonObject replacement = PolicyFixture.Body(original);
        replacement["scope"] = new JsonArray(Scopes.LocalEngineering);
        replacement.Remove("bounds");
        RecordEnvelope removed = f.Add("authority", replacement, supersedes: original.Id);
        RecordEnvelope first = f.Add("authority", replacement, supersedes: removed.Id);
        RecordEnvelope second = f.Add("authority", replacement, supersedes: removed.Id);
        JsonObject choice = new() { ["kept"] = first.Id, ["withdrawn"] = new JsonArray(second.Id), ["note"] = "Reconcile the grant's retained history." };
        RecordEnvelope delegated = f.Add("reconciliation", choice, builder: true, subject: removed.Reference);
        Has(f, delegated, "policy.authority");
        RecordEnvelope unsigned = f.Add("reconciliation", choice, subject: removed.Reference, sign: false);
        Has(f, unsigned, "signature.required");
        RecordEnvelope disguised = f.Estate.Add("reconciliation", f.Estate.OperatorId, choice.ToJsonString(), removed.Reference, signer: f.OperatorSigner, format: 2);
        Has(f, disguised, "policy.format");
    }

    internal static void Has(PolicyFixture f, RecordEnvelope record, string code) => Assert.Contains(f.Estate.Load().Findings, finding => finding.Code == code && finding.Location.StartsWith(record.ExpectedPath, StringComparison.Ordinal));
}
