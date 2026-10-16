// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class CommunicationRecordTests
{
    [Fact]
    public void OwnSettingsAndResetAreAppendOnlyFormatTwoRecords()
    {
        using var estate = new TestEstate(2);
        RecordEnvelope first = estate.Add("communication-setting", estate.OperatorId, Setting(estate.OperatorId, "{\"progress\":\"none\",\"status\":\"standard\"}"));
        estate.Add("communication-setting", estate.OperatorId, Setting(estate.OperatorId, "{}"), supersedes: first.Id);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(2, model.OfType("communication-setting").Count());
        Assert.Single(model.Current("communication-setting"));
    }

    [Fact]
    public void FormatOneAndUnknownValuesAreRefused()
    {
        using var old = new TestEstate();
        old.Add("communication-setting", old.OperatorId, Setting(old.OperatorId, "{}"));
        Assert.Contains(old.Load().Findings, f => f.Code == "record.type-format");
        using var estate = new TestEstate(2);
        estate.Add("communication-setting", estate.OperatorId, Setting(estate.OperatorId, "{\"status\":\"terse\",\"extra\":true}"));
        Assert.Contains(estate.Load().Findings, f => f.Code == "body.enum");
        Assert.Contains(estate.Load().Findings, f => f.Code == "body.unknown-key");
    }

    [Fact]
    public void AnAgentAndEvenAnOperatorCannotSilentlyChangeAnotherPersonsValues()
    {
        using var estate = new TestEstate(2);
        estate.Add("communication-setting", estate.BuilderId, Setting(estate.OperatorId, "{}"));
        Assert.Contains(estate.Load().Findings, f => f.Code == "communication.authority");
        using var other = new TestEstate(2);
        RecordEnvelope person = other.Add("principal", other.OperatorId, """{"kind":"human","name":"Another person","roles":["contributor"]}""");
        other.Add("communication-setting", other.OperatorId, Setting(person.Id, "{}"));
        Assert.Contains(other.Load().Findings, f => f.Code == "communication.authority");
    }

    [Fact]
    public void ExplicitScopedDelegationKeepsTheActualAuthorAndRevocationDoesNotInvalidateHistory()
    {
        using var estate = new TestEstate(2);
        RecordEnvelope grant = estate.Add("communication-delegation", estate.OperatorId, Grant(estate.OperatorId, estate.BuilderId));
        RecordEnvelope setting = estate.Add("communication-setting", estate.BuilderId, Setting(estate.OperatorId, "{}", grant));
        estate.Add("communication-delegation", estate.OperatorId, Grant(estate.OperatorId, estate.BuilderId, "revoked"), supersedes: grant.Id);
        Assert.False(estate.Load().Findings.HasErrors, string.Join('\n', estate.Load().Findings));
        Assert.Equal(estate.BuilderId, setting.By);
        estate.Add("communication-setting", estate.BuilderId, Setting(estate.OperatorId, "{\"status\":\"short\"}", grant), supersedes: setting.Id);
        Assert.Contains(estate.Load().Findings, f => f.Code == "communication.authority");
    }

    [Fact]
    public void ParallelProfilesAndScopeChangesAreNotAcceptedAsRevisions()
    {
        using var estate = new TestEstate(2);
        RecordEnvelope first = estate.Add("communication-setting", estate.OperatorId, Setting(estate.OperatorId, "{}"));
        estate.Add("communication-setting", estate.OperatorId, Setting(estate.OperatorId, "{\"status\":\"short\"}"));
        Assert.Contains(estate.Load().Findings, f => f.Code == "communication.conflict");
        estate.Add("communication-setting", estate.OperatorId, """{"scope":"estate","values":{}}""", supersedes: first.Id);
        Assert.Contains(estate.Load().Findings, f => f.Code == "communication.scope");
    }

    [Fact]
    public void EstateDefaultsRequireAdministrationAndAssignmentScopesNameAnAssignment()
    {
        using var estate = new TestEstate(2);
        estate.Add("communication-setting", estate.BuilderId, """{"scope":"estate","values":{}}""");
        Assert.Contains(estate.Load().Findings, f => f.Code == "communication.authority");
        using var valid = new TestEstate(2);
        valid.Add("communication-setting", valid.OperatorId, """{"scope":"estate","values":{"handoff":"standard"}}""");
        Assert.False(valid.Load().Findings.HasErrors, string.Join('\n', valid.Load().Findings));
        valid.Add("communication-setting", valid.OperatorId, $$$"""{"scope":"assignment","recipient":"{{{valid.OperatorId}}}","values":{}}""");
        Assert.Contains(valid.Load().Findings, f => f.Code == "body.missing");
    }

    private static string Setting(string recipient, string values, RecordEnvelope? grant = null) =>
        $$"""{"scope":"person","recipient":"{{recipient}}","values":{{values}}{{(grant is null ? string.Empty : $",\"authority\":\"{grant.Id}\",\"authorityHash\":\"{grant.ComputedHash}\"")}}}""";

    private static string Grant(string recipient, string grantee, string status = "active") =>
        $$"""{"recipient":"{{recipient}}","grantee":"{{grantee}}","scope":"person","status":"{{status}}"}""";
}
