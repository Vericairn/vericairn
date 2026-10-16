// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class HarnessQualificationTests
{
    private static readonly string[] Probes = ["version", "flags", "tools", "configuration", "login", "mcp"];
    [Fact]
    public void AHumanOperatorRecordsProbeDigestsInFormatTwo()
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.Add("harness-qualification", estate.OperatorId, Body());
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);
    }

    [Theory]
    [InlineData("human", false, false)]
    [InlineData("human", true, true)]
    [InlineData("agent", false, false)]
    [InlineData("agent", true, false)]
    [InlineData("service", true, false)]
    public void OnlyAHumanWithTheExplicitScopeCanQualify(string kind, bool grant, bool allowed)
    {
        using var estate = new TestEstate(recordFormat: 2);
        string author = kind == "agent" ? estate.BuilderId : estate.Add("principal", estate.OperatorId,
            $$"""{"kind":"{{kind}}","name":"Qualifier","roles":["contributor"]}""").Id;
        if (grant)
        {
            string decision = estate.Add("decision", estate.OperatorId, $$"""{"title":"Grant qualification","stake":"low","decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","statement":"Grant qualify.","summary":"Grant qualify.","synthesis":"Grant qualify."}""").Id;
            estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{author}}","scope":["qualify"],"reserved":[],"conditions":[],"basis":"{{decision}}"}""");
        }
        RecordEnvelope record = estate.Add("harness-qualification", author, Body());
        Finding[] errors = estate.Load().Findings.Where(f => f.Severity == Severity.Error && f.Location.StartsWith(record.ExpectedPath, StringComparison.Ordinal)).ToArray();
        if (allowed) Assert.Empty(errors);
        else Assert.Contains(errors, f => f.Code == "harness-qualification.author");
    }

    [Theory]
    [InlineData("missing-probe")]
    [InlineData("raw-output")]
    [InlineData("range")]
    [InlineData("path")]
    [InlineData("format-one")]
    public void OnlyExactPortableVersionsAndAllSixDigestsAreRecords(string defect)
    {
        using var estate = new TestEstate(recordFormat: 2);
        JsonObject body = JsonNode.Parse(Body())!.AsObject();
        switch (defect)
        {
            case "missing-probe": body["probes"]!.AsObject().Remove("login"); break;
            case "raw-output": body["probes"]!["flags"] = "raw help output"; break;
            case "range": body["version"] = ">=0.160.0"; break;
            case "path": body["version"] = "/machine/harness"; break;
        }
        RecordEnvelope record = estate.Add("harness-qualification", estate.OperatorId, body.ToJsonString(), format: defect == "format-one" ? 1 : 2);
        Assert.Contains(estate.Load().Findings, f => f.Severity == Severity.Error && f.Location.StartsWith(record.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void RevocationSupersedesAndPreservesTheQualificationItRevokes()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope first = estate.Add("harness-qualification", estate.OperatorId, Body());
        JsonObject body = JsonNode.Parse(Body())!.AsObject();
        body["status"] = "revoked";
        RecordEnvelope revoked = estate.Add("harness-qualification", estate.OperatorId, body.ToJsonString(), supersedes: first.Id);
        EstateModel model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
        Assert.False(model.IsCurrent(first));
        Assert.Equal(revoked.Id, Assert.Single(model.Current("harness-qualification")).Id);
        body["version"] = "0.161.0";
        RecordEnvelope changed = estate.Add("harness-qualification", estate.OperatorId, body.ToJsonString(), supersedes: revoked.Id);
        Assert.Contains(estate.Load().Findings, f => f.Code == "harness-qualification.revision" && f.Location == changed.ExpectedPath);
    }

    internal static string Body() => new JsonObject
    {
        ["launcher"] = "codex", ["version"] = "0.160.0", ["status"] = "qualified",
        ["probes"] = new JsonObject(Probes
            .Select(n => KeyValuePair.Create(n, (JsonNode?)("sha256:" + new string('a', 64))))),
    }.ToJsonString();
}
