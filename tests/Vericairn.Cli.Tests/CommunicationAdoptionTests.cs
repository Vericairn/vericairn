// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class CommunicationAdoptionTests
{
    [Fact]
    public void ExplicitAdoptionPinsTheDecisionAndKeepsExactlyItsFiveValues()
    {
        using var estate = new HandoffEstate();
        RecordEnvelope decision = estate.Add("decision", estate.BuilderId, $$"""
            {"title":"Recorded communication setting","statement":"Use your recommendations for all of these and continue.",
            "summary":"Progress on completion and need; short status; comprehensive decisions, results and handoffs.",
            "synthesis":"An explicit personal setting, not a builder habit.","effects":["The builder follows the recorded setting."],"affects":[],
            "decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}
            """, format: 1);
        RecordEnvelope other = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Another person","roles":["contributor"]}""");
        CliHarness.Ok("--estate", estate.Root, "communication", "set", "--as", estate.OperatorId, "--basis", decision.Id, "--expected", "absent",
            "--progress", "completion-and-need", "--status", "short", "--decisions", "comprehensive", "--results", "comprehensive", "--handoffs", "comprehensive");
        RecordEnvelope setting = Assert.Single(estate.Load().Current("communication-setting"));
        Assert.Equal(estate.OperatorId, setting.By);
        Assert.Equal(decision.Id, EstateModel.BodyString(setting, "basis"));
        Assert.Equal(decision.ComputedHash, EstateModel.BodyString(setting, "basisHash"));
        Assert.Equal("{\"decision\":\"comprehensive\",\"handoff\":\"comprehensive\",\"progress\":\"completion-and-need\",\"result\":\"comprehensive\",\"status\":\"short\"}",
            Vericairn.Records.Canonical.CanonicalJson.Serialize(setting.Body.GetProperty("values")));
        Assert.Equal(decision.ComputedHash, estate.Load().ById[decision.Id].ComputedHash);
        using EstateService service = EstateService.Open(estate.Root);
        Assert.All(service.Communication(estate.OperatorId).Fields.Values, field => Assert.Equal("person", field.Source));
        Assert.All(service.Communication(other.Id).Fields.Values, field => Assert.Equal("shipped", field.Source));
        Assert.Single(estate.Load().Current("communication-setting"));
        Assert.NotEqual(0, CliHarness.Run("--estate", estate.Root, "communication", "set", "--as", estate.OperatorId,
            "--basis", decision.Id, "--expected", "absent", "--status", "standard").Code);
        Assert.Equal(setting.Id, Assert.Single(estate.Load().Current("communication-setting")).Id);
    }

    [Fact]
    public void NamingADecisionDoesNotConferDelegatedAuthority()
    {
        using var estate = new HandoffEstate();
        RecordEnvelope decision = estate.Add("decision", estate.BuilderId, $$"""
            {"title":"Communication","statement":"Short status.","summary":"Short status.","synthesis":"Personal choice.","effects":["Short status."],"affects":[],
            "decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}
            """, format: 1);
        Assert.NotEqual(0, CliHarness.Run("--estate", estate.Root, "communication", "set", "--as", estate.BuilderId,
            "--recipient", estate.OperatorId, "--basis", decision.Id, "--status", "short").Code);
        Assert.Empty(estate.Load().OfType("communication-setting"));
    }
}
