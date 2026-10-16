// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class RunSelectionTests
{
    internal static RunCapabilities Capabilities { get; } = new(RunSelection.Scopes, ["low", "high"], ["provider"], ["inference"], ["worker-user"]);

    internal static RunConfiguration Configuration { get; } = new("test", "stand-in", RunEstate.Json("""{"choices":{"model":"model-a","effort":"low","provider":"provider","destination":"inference","dataBoundary":"worker-user"}}"""), ["model-a", "model-b"]);

    [Fact]
    public void ChoicesHaveOrderedProvenanceAndEachHigherScopeOverridesDefaults()
    {
        RunPolicySource[] sources =
        [
            new("assignment", "assignment", RunEstate.Json("""{"choices":{"model":"model-b","effort":"low"}}""")),
            new("goal", "goal", RunEstate.Json("""{"choices":{"effort":"high"}}""")),
            new("roster", "roster", RunEstate.Json("""{"choices":{"model":"model-a"}}""")),
        ];
        JsonObject selection = RunSelection.Resolve(Configuration, Capabilities, sources, RunEstate.Json("""{"effort":"high"}"""), RunEstate.Json("{}"));
        Assert.Equal("model-b", selection["requested"]!["model"]!.GetValue<string>());
        Assert.Equal("high", selection["requested"]!["effort"]!.GetValue<string>());
        Assert.Equal(["launcher", "roster", "goal", "assignment", "run"], selection["provenance"]!.AsArray().Select(s => s!["scope"]!.GetValue<string>()));
        Assert.Equal("low", selection["provenance"]![3]!["policy"]!["choices"]!["effort"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("requiredModel", "\"model-a\"", "{\"model\":\"model-b\"}")]
    [InlineData("requiredEffort", "\"high\"", "{\"effort\":\"low\"}")]
    [InlineData("permittedProviders", "[\"other\"]", "{\"provider\":\"provider\"}")]
    [InlineData("permittedDestinations", "[\"elsewhere\"]", "{\"destination\":\"inference\"}")]
    [InlineData("dataBoundary", "\"local-only\"", "{\"dataBoundary\":\"worker-user\"}")]
    [InlineData("maxCurrency", "{\"amount\":10,\"unit\":\"USD\"}", "{}")]
    public void OverridesCannotRemoveAnyInheritedRestriction(string field, string value, string choice)
    {
        var source = new RunPolicySource("roster", "builder", RunEstate.Json("{\"restrictions\":{\"" + field + "\":" + value + "}}"));
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(Configuration, Capabilities, [source], RunEstate.Json(choice), RunEstate.Json("""{"currency":{"amount":11,"unit":"USD"}}""")));
    }

    [Fact]
    public void ALauncherRefusesAnUndeclaredSelectionScope()
    {
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(Configuration, Capabilities with { Scopes = ["launcher"] }, [], RunEstate.Json("""{"model":"model-b"}"""), RunEstate.Json("{}")));
    }

}
