// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class GeneratedRevisionCommandTests
{
    [Fact]
    public void ACarriedDecisionComesFromTheExactRecordNamedByTheCaller()
    {
        using var estate = new TestEstate(3);
        var decision = estate.Add("decision", estate.OperatorId, $$"""{"title":"Revise the design","statement":"Carry these decisions.","summary":"Carry these decisions.","synthesis":"The operator supplies the direction.","effects":["Revise the design."],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":[]}""");
        const string path = "design/example.md";
        estate.WriteFile(path, "# Example\n\nBody.\n");
        string pin = CliHarness.Ok("--estate", estate.Root, "--json", "docs", "pin", path, "--by", estate.OperatorId,
            "--summary", "Implement the direction.", "--decision", decision.Id, "--convert-revisions").Id;
        Assert.Equal(decision.Id, estate.Load().ById[pin].Body.GetProperty("decision").GetString());
        Assert.Contains("carries decision `" + decision.Id + "`: Implement the direction.", File.ReadAllText(Path.Combine(estate.Root, path)), StringComparison.Ordinal);
    }
}
