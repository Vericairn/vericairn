// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>
/// Which principal and authority records give roles and scopes: only those their authors may write, judged in time order from the root
/// operator, so a record's own validation never reads a role from that record.
/// </summary>
public class RoleRecordTests
{
    private const string Person = """{"kind":"human","name":"Person","roles":["contributor"]}""";

    private static string Body(string name, params string[] roles) =>
        new JsonObject { ["kind"] = "human", ["name"] = name, ["roles"] = new JsonArray([.. roles.Select(r => (JsonNode)r)]) }.ToJsonString();

    private static string GrantBody(string grantee, RecordEnvelope basis, string scope) =>
        new JsonObject { ["grantee"] = grantee, ["scope"] = new JsonArray(scope), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = basis.Id }.ToJsonString();

    [Fact]
    public void ARevisionOfAPrincipalsOwnRecordGivesNoRole()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        f.Require();
        RecordEnvelope x = f.Estate.Add("principal", f.Estate.OperatorId, Person, signer: f.OperatorSigner);
        (RecordEnvelope device, _) = f.Admitted(x.Id, "X's device");
        RecordEnvelope own = f.Estate.Add("principal", x.Id, Body("Person", "operator"), supersedes: x.Id, signer: f.Signer(device));
        RecordEnvelope admitted = f.Estate.Add("principal", x.Id, Body("Admitted", "contributor"), signer: f.Signer(device));
        RecordEnvelope grant = f.Estate.Add("authority", x.Id, GrantBody(x.Id, decision, Scopes.Administer), signer: f.Signer(device));
        EstateModel model = f.Load();
        Assert.False(model.Counts(model.ById[own.Id]));
        Assert.Contains(model.Findings, e => e.Code == "principal.self-authored" && e.Location == own.ExpectedPath);
        Assert.False(model.HasRole(x.Id, Roles.Operator));
        Assert.Equal(x.Id, model.CurrentPrincipal(x.Id)!.Id);
        Assert.Contains(model.Findings, e => e.Code == "principal.author" && e.Location == admitted.ExpectedPath);
        Assert.Null(model.CurrentPrincipal(admitted.Id));
        Assert.Contains(model.Findings, e => e.Code == "authority.author" && e.Location == grant.ExpectedPath);
        Assert.False(model.HasScope(x.Id, Scopes.Administer));
    }

    [Fact]
    public void AnAgentsRecordGivesNoOperatorRole()
    {
        using var f = new HistoryFixture();
        f.Require();
        // The agent's own revision to the operator role, and the operator's record of another agent with it.
        JsonObject body = HistoryFixture.Body(f.Estate.Builder);
        body["roles"] = new JsonArray("builder", "operator");
        RecordEnvelope own = f.Estate.Add("principal", f.Estate.BuilderId, body.ToJsonString(), supersedes: f.Estate.Builder.Id, signer: f.BuilderSigner);
        RecordEnvelope agent = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"agent","name":"Another agent","roles":["operator"],"agent":{"vendor":"Example","product":"Example Agent"}}""", signer: f.OperatorSigner);
        EstateModel model = f.Load();
        Assert.False(model.Counts(model.ById[own.Id]));
        Assert.False(model.HasRole(f.Estate.BuilderId, Roles.Operator));
        Assert.Contains(model.Findings, e => e.Code == "principal.operator" && e.Location.StartsWith(agent.ExpectedPath, StringComparison.Ordinal));
        Assert.False(model.HasRole(agent.Id, Roles.Operator));
    }

    [Fact]
    public void AGrantByAPrincipalWhoIsNotAnOperatorGivesNoScope()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        f.Require();
        RecordEnvelope x = f.Estate.Add("principal", f.Estate.OperatorId, Person, signer: f.OperatorSigner);
        (RecordEnvelope device, _) = f.Admitted(x.Id, "X's device");
        RecordEnvelope grant = f.Estate.Add("authority", x.Id, GrantBody(x.Id, decision, Scopes.Administer), signer: f.Signer(device));
        RecordEnvelope admitted = f.Estate.Add("principal", x.Id, Body("Admitted", "operator"), signer: f.Signer(device));
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, e => e.Code == "authority.author" && e.Location == grant.ExpectedPath);
        Assert.False(model.HasScope(x.Id, Scopes.Administer));
        Assert.Contains(model.Findings, e => e.Code == "principal.author" && e.Location == admitted.ExpectedPath);
        Assert.Null(model.CurrentPrincipal(admitted.Id));
        Assert.False(model.HasRole(admitted.Id, Roles.Operator));
    }

    [Fact]
    public void TwoPrincipalsRevisingEachOtherInOneSecondGainNoRole()
    {
        using var f = new HistoryFixture();
        RecordEnvelope x = f.Estate.Add("principal", f.Estate.OperatorId, Body("X", "contributor"), signer: f.OperatorSigner);
        RecordEnvelope y = f.Estate.Add("principal", f.Estate.OperatorId, Body("Y", "contributor"), signer: f.OperatorSigner);
        (RecordEnvelope xDevice, _) = f.Admitted(x.Id, "X's device");
        (RecordEnvelope yDevice, _) = f.Admitted(y.Id, "Y's device");
        f.Require();
        f.Adopt();
        DateTimeOffset moment = f.Estate.Clock.AddSeconds(5);
        f.Estate.SetClock(moment.AddSeconds(-1));
        RecordEnvelope ofY = f.Estate.Add("principal", x.Id, Body("Y", "operator"), supersedes: y.Id, signer: f.Signer(xDevice));
        f.Estate.SetClock(moment.AddSeconds(-1));
        RecordEnvelope ofX = f.Estate.Add("principal", y.Id, Body("X", "operator"), supersedes: x.Id, signer: f.Signer(yDevice));
        Assert.Equal(ofY.At, ofX.At);
        RecordEnvelope closing = f.Close("device", f.OperatorDevice.Id, x.Id, f.Signer(xDevice));
        EstateModel model = f.Load();
        Assert.False(model.HasRole(x.Id, Roles.Operator));
        Assert.False(model.HasRole(y.Id, Roles.Operator));
        Assert.DoesNotContain(model.Trust.Closings, c => c.Record.Id == closing.Id);
        Assert.Contains(model.Findings, e => e.Code == "principal.amendment" && e.Location == ofY.ExpectedPath);
    }

    [Fact]
    public void RecordsByTheRootOperatorAndThoseItAuthorizesGiveTheirRoles()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        f.Require();
        RecordEnvelope o = f.Estate.Add("principal", f.Estate.OperatorId, Body("Second operator", "operator"), signer: f.OperatorSigner);
        (RecordEnvelope oDevice, _) = f.Admitted(o.Id, "The second operator's device");
        RecordEnvelope p = f.Estate.Add("principal", o.Id, Body("Administrator", "reviewer"), signer: f.Signer(oDevice));
        (RecordEnvelope pDevice, _) = f.Admitted(p.Id, "The administrator's device");
        f.Estate.Add("authority", o.Id, GrantBody(p.Id, decision, Scopes.Administer), signer: f.Signer(oDevice));
        RecordEnvelope q = f.Estate.Add("principal", p.Id, Body("Admitted", "contributor"), signer: f.Signer(pDevice));
        JsonObject renamed = HistoryFixture.Body(f.Estate.Operator);
        renamed["name"] = "Renamed Operator";
        RecordEnvelope own = f.Estate.Add("principal", f.Estate.OperatorId, renamed.ToJsonString(), supersedes: f.Estate.Operator.Id, signer: f.OperatorSigner);
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(e => e.Severity == Severity.Error)));
        Assert.True(model.HasRole(o.Id, Roles.Operator));
        Assert.True(model.HasScope(p.Id, Scopes.Administer));
        Assert.NotNull(model.CurrentPrincipal(q.Id));
        Assert.Equal(own.Id, model.CurrentPrincipal(f.Estate.OperatorId)!.Id);
    }

    [Fact]
    public void AContributorsOwnGrantAuthorizesNeitherAHarnessQualificationNorARepositoryChange()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        RecordEnvelope repository = f.Estate.Add("resource", f.Estate.OperatorId, """{"kind":"repository","name":"Widget","role":"Source","location":{"github":"Example/widget"},"visibility":"private"}""", signer: f.OperatorSigner);
        RecordEnvelope product = f.Estate.Add("product", f.Estate.OperatorId, $$"""
            {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],
             "changes":{"provider":"example","repository":"{{repository.Id}}","target":"main","requiredChecks":[] } }
            """, signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope x = f.Estate.Add("principal", f.Estate.OperatorId, Body("X", "contributor"), signer: f.OperatorSigner);
        (RecordEnvelope device, _) = f.Admitted(x.Id, "X's device");
        RecordEnvelope grant = f.Estate.Add("authority", x.Id, new JsonObject
        {
            ["grantee"] = x.Id, ["scope"] = new JsonArray(Scopes.Qualify, Scopes.RepositoryWrite), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = decision.Id,
        }.ToJsonString(), signer: f.Signer(device));
        RecordEnvelope qualification = f.Estate.Add("harness-qualification", x.Id, HarnessQualificationTests.Body(), signer: f.Signer(device));
        RecordEnvelope change = f.Estate.Add("operation", x.Id, new JsonObject
        {
            ["provider"] = "example", ["action"] = "change-merge", ["identity"] = "example-change-merge", ["intent"] = "A checked repository change.", ["status"] = "not-started",
            ["target"] = new JsonObject { ["head"] = new string('a', 40), ["product"] = product.Id, ["productHash"] = product.ComputedHash,
                ["repository"] = repository.Id, ["repositoryHash"] = repository.ComputedHash, ["destination"] = "Example/widget", ["target"] = "main" },
            ["requiredChecks"] = new JsonArray(), ["evidence"] = new JsonArray(),
        }.ToJsonString(), signer: f.Signer(device));
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, e => e.Code == "authority.author" && e.Location == grant.ExpectedPath);
        Assert.Contains(model.Findings, e => e.Code == "harness-qualification.author" && e.Location.StartsWith(qualification.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(model.Findings, e => e.Code == "operation.repository-write" && e.Location.StartsWith(change.ExpectedPath, StringComparison.Ordinal));
        Assert.False(model.HasScope(x.Id, Scopes.Qualify));
        Assert.False(model.HasScope(x.Id, Scopes.RepositoryWrite));
    }

    [Fact]
    public void APrincipalHoldingAdministerGivesNoOperatorRole()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        f.Require();
        RecordEnvelope p = f.Estate.Add("principal", f.Estate.OperatorId, Body("Administrator", "reviewer"), signer: f.OperatorSigner);
        (RecordEnvelope device, _) = f.Admitted(p.Id, "The administrator's device");
        f.Grant(p.Id, decision, [Scopes.Administer]);
        RecordEnvelope contributor = f.Estate.Add("principal", p.Id, Body("Contributor", "contributor"), signer: f.Signer(device));
        RecordEnvelope made = f.Estate.Add("principal", p.Id, Body("Made an operator", "operator"), signer: f.Signer(device));
        EstateModel model = f.Load();
        Assert.DoesNotContain(model.Findings, e => e.Severity == Severity.Error && e.Location.StartsWith(contributor.ExpectedPath, StringComparison.Ordinal));
        Assert.True(model.HasRole(contributor.Id, Roles.Contributor));
        Assert.Contains(model.Findings, e => e.Code == "principal.author" && e.Location == made.ExpectedPath);
        Assert.Null(model.CurrentPrincipal(made.Id));
        Assert.False(model.HasRole(made.Id, Roles.Operator));
    }

    [Fact]
    public void APrincipalHoldingAdministerAmendsOnlyALineWhoseRevisionItWrote()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        f.Require();
        RecordEnvelope p = f.Estate.Add("principal", f.Estate.OperatorId, Body("Administrator", "reviewer"), signer: f.OperatorSigner);
        (RecordEnvelope device, _) = f.Admitted(p.Id, "The administrator's device");
        f.Grant(p.Id, decision, [Scopes.Administer]);
        RecordEnvelope x = f.Estate.Add("principal", f.Estate.OperatorId, Body("X", "contributor"), signer: f.OperatorSigner);
        // The administrator revises the operator's record of X, then revises its own refused revision.
        RecordEnvelope first = f.Estate.Add("principal", p.Id, Body("X", "contributor", "reviewer"), supersedes: x.Id, signer: f.Signer(device));
        RecordEnvelope second = f.Estate.Add("principal", p.Id, Body("X", "contributor", "reviewer", "builder"), supersedes: first.Id, signer: f.Signer(device));
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, e => e.Code == "principal.amendment" && e.Location == first.ExpectedPath);
        Assert.Contains(model.Findings, e => e.Code == "principal.amendment" && e.Location == second.ExpectedPath);
        Assert.Equal(x.Id, model.CurrentPrincipal(x.Id)!.Id);
        Assert.False(model.HasRole(x.Id, Roles.Reviewer));
        Assert.False(model.HasRole(x.Id, Roles.Builder));
    }

    [Fact]
    public void AnOperatorsOwnRevisionThatGivesNothingIsAWarningAndTheLineStaysWhole()
    {
        using var f = new HistoryFixture();
        f.Require();
        RecordEnvelope o = f.Estate.Add("principal", f.Estate.OperatorId, Body("Second operator", "operator"), signer: f.OperatorSigner);
        (RecordEnvelope device, _) = f.Admitted(o.Id, "The second operator's device");
        RecordEnvelope rename = f.Estate.Add("principal", o.Id, Body("Second operator renamed", "operator"), supersedes: o.Id, signer: f.Signer(device));
        EstateModel model = f.Load();
        // An estate holding a second operator's own rename stays valid: the rename is reported and gives nothing.
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(e => e.Severity == Severity.Error)));
        Assert.Contains(model.Findings, e => e.Code == "principal.self-authored" && e.Severity == Severity.Warning && e.Location == rename.ExpectedPath);
        Assert.False(model.Counts(model.ById[rename.Id]));
        Assert.Equal(o.Id, model.CurrentPrincipal(o.Id)!.Id);
        Assert.True(model.HasRole(o.Id, Roles.Operator));

        // The operator's later revision on top of it follows the line: one current record describes the principal.
        RecordEnvelope revised = f.Estate.Add("principal", f.Estate.OperatorId, Body("Second operator renamed", "operator"), supersedes: rename.Id, signer: f.OperatorSigner);
        // A revision of its own record that would give a role is an error, and gives nothing either.
        RecordEnvelope widened = f.Estate.Add("principal", o.Id, Body("Second operator renamed", "operator", "reviewer"), supersedes: revised.Id, signer: f.Signer(device));
        model = f.Load();
        Assert.Equal(revised.Id, model.CurrentPrincipal(o.Id)!.Id);
        Assert.Single(model.Current("principal"), p => model.Chain(p)[0] == o.Id);
        Assert.Contains(model.Findings, e => e.Code == "principal.self-authored" && e.Severity == Severity.Error && e.Location == widened.ExpectedPath);
        Assert.False(model.HasRole(o.Id, Roles.Reviewer));
    }

    [Fact]
    public void AnOperatorsOwnRevisionIsNotReadForTheStrongestActsEither()
    {
        using var f = new HistoryFixture();
        RecordEnvelope o = f.Estate.Add("principal", f.Estate.OperatorId, Body("Second operator", "operator"), signer: f.OperatorSigner);
        (RecordEnvelope device, _) = f.Admitted(o.Id, "The second operator's device");
        f.Require();
        f.Adopt();
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(10));
        RecordEnvelope rename = f.Estate.Add("principal", o.Id, Body("Second operator renamed", "operator"), supersedes: o.Id, signer: f.Signer(device));
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(10));
        RecordEnvelope closing = f.Close("device", f.BuilderDevice.Id, o.Id, f.Signer(device));
        EstateModel model = f.Load();

        // The operator's own rename counts for nothing in every read, the strongest acts included: they read the line below it, which
        // still gives the operator role, so the operator's closing acts.
        Assert.False(model.Counts(model.ById[rename.Id]));
        Assert.Contains(model.Findings, e => e.Code == "principal.self-authored" && e.Severity == Severity.Warning && e.Location == rename.ExpectedPath);
        Assert.True(model.HasRole(o.Id, Roles.Operator));
        Assert.Contains(model.Trust.Closings, c => c.Record.Id == closing.Id);
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(e => e.Severity == Severity.Error)));
    }
}
