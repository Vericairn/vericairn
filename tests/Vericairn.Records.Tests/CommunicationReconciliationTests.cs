// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class CommunicationReconciliationTests
{
    [Fact]
    public void IndependentFirstProfilesAreReconcileTasksAndTheirOwnerCanKeepOne()
    {
        using var estate = new TestEstate(2);
        RecordEnvelope person = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Person","roles":["contributor"]}""");
        RecordEnvelope a = estate.Add("communication-setting", person.Id, Setting(person.Id, "short"));
        RecordEnvelope b = estate.Add("communication-setting", person.Id, Setting(person.Id, "standard"));
        Assert.Contains(EstateStatus.ReconcileTasks(estate.Load().Findings), f => f.Code == "communication.conflict");
        estate.Add("reconciliation", person.Id, $$"""{"kept":"{{b.Id}}","withdrawn":["{{a.Id}}"],"note":"Keep the choice made on the second device."}""", new("communication-setting", a.Id, a.ComputedHash));
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(b.Id, Assert.Single(model.Current("communication-setting")).Id);
        Assert.Equal(2, model.OfType("communication-setting").Count());
    }

    [Fact]
    public void AnAdministratorCannotChooseAnotherPersonsConflictingPreference()
    {
        using var estate = new TestEstate(2);
        RecordEnvelope person = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Person","roles":["contributor"]}""");
        RecordEnvelope original = estate.Add("communication-setting", person.Id, Setting(person.Id, "short"));
        RecordEnvelope a = estate.Add("communication-setting", person.Id, Setting(person.Id, "standard"), supersedes: original.Id);
        RecordEnvelope b = estate.Add("communication-setting", person.Id, Setting(person.Id, "comprehensive"), supersedes: original.Id);
        estate.Add("reconciliation", estate.OperatorId, $$"""{"kept":"{{a.Id}}","withdrawn":["{{b.Id}}"],"note":"An administrator is not the owner."}""", new("communication-setting", original.Id, original.ComputedHash));
        Assert.Contains(estate.Load().Findings, f => f.Code == "communication.authority");
    }

    [Fact]
    public void ReconciliationCannotCrossPersonalScopes()
    {
        using var estate = new TestEstate(2);
        RecordEnvelope person = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Person","roles":["contributor"]}""");
        RecordEnvelope a = estate.Add("communication-setting", estate.OperatorId, Setting(estate.OperatorId, "short"));
        RecordEnvelope b = estate.Add("communication-setting", person.Id, Setting(person.Id, "short"));
        estate.Add("reconciliation", estate.OperatorId, $$"""{"kept":"{{a.Id}}","withdrawn":["{{b.Id}}"],"note":"Different people are not a conflict."}""", new("communication-setting", a.Id, a.ComputedHash));
        Assert.Contains(estate.Load().Findings, f => f.Code == "reconciliation.successor");
    }

    private static string Setting(string person, string status) => $$$"""{"scope":"person","recipient":"{{{person}}}","values":{"status":"{{{status}}}"}}""";
}
