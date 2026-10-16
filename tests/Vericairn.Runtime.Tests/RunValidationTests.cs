// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class RunValidationTests
{
    private static RunConfiguration Configuration => RunSelectionTests.Configuration;
    private static RunCapabilities Capabilities => RunSelectionTests.Capabilities;

    [Theory]
    [InlineData("local")]
    [InlineData("efficiency")]
    [InlineData("enabled")]
    public void AnEfficiencyAdapterRequestIsUnsupportedEvenWhenALaterScopeDisablesIt(string adapter)
    {
        RunPolicySource source = new("goal", "goal", RunEstate.Json("{\"choices\":{\"adapter\":\"" + adapter + "\"}}"));
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(Configuration, Capabilities, [source], RunEstate.Json("""{"adapter":"disabled"}"""), RunEstate.Json("{}")));
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(Configuration, Capabilities, [], RunEstate.Json("{\"adapter\":\"" + adapter + "\"}"), RunEstate.Json("{}")));
    }

    [Fact]
    public void ExplicitlyDisabledAdapterNeedsNoImplementation()
    {
        JsonObject selected = RunSelection.Resolve(Configuration, Capabilities, [], RunEstate.Json("""{"adapter":"disabled"}"""), RunEstate.Json("{}"));
        Assert.Equal("disabled", selected["requested"]!["adapter"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("not-supported")]
    [InlineData("provider-default")]
    public void ReservedEffortChoicesCannotBeAdvertisedAsNativeEffortValues(string effort)
    {
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(Configuration, Capabilities with { Efforts = ["high", effort] }, [], RunEstate.Json("{\"effort\":\"" + effort + "\"}"), RunEstate.Json("{}")));
    }

    [Theory]
    [InlineData("model", "missing")]
    [InlineData("effort", "ultra")]
    [InlineData("effort", "not-supported")]
    [InlineData("effort", "provider-default")]
    [InlineData("provider", "other")]
    [InlineData("destination", "other")]
    [InlineData("dataBoundary", "local-only")]
    public void UnsupportedValuesNeverFallBack(string name, string value)
    {
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(Configuration, Capabilities, [], RunEstate.Json("{\"" + name + "\":\"" + value + "\"}"), RunEstate.Json("{}")));
    }

    [Theory]
    [InlineData("model")]
    [InlineData("effort")]
    public void UnresolvedRequiredChoicesAreRefused(string field)
    {
        JsonObject policy = JsonNode.Parse(Configuration.Policy.GetRawText())!.AsObject();
        policy["choices"]!.AsObject().Remove(field);
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(Configuration with { Policy = RunEstate.Json(policy.ToJsonString()) }, Capabilities, [], null, RunEstate.Json("{}")));
    }

    [Fact]
    public void ProviderDefaultIsAnExplicitUnknownEffortAndNotSupportedNeedsNoControl()
    {
        JsonObject selected = RunSelection.Resolve(Configuration, Capabilities with { ProviderDefault = true }, [], RunEstate.Json("""{"effort":"provider-default"}"""), RunEstate.Json("{}"));
        Assert.Equal("provider-default", selected["requested"]!["effort"]!.GetValue<string>());
        JsonObject unsupported = RunSelection.Resolve(Configuration, Capabilities with { Efforts = [] }, [], RunEstate.Json("""{"effort":"not-supported"}"""), RunEstate.Json("{}"));
        Assert.Equal("not-supported", unsupported["requested"]!["effort"]!.GetValue<string>());
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(Configuration, Capabilities with { Efforts = [] }, [], null, RunEstate.Json("{}")));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("unknown", false)]
    [InlineData("revision-2", false)]
    [InlineData("revision-1", true)]
    public void AnAliasKeepsItsSelectionAndOnlyAnObservableRevisionSatisfiesAnExactPin(string? revision, bool admitted)
    {
        RunPolicySource pin = new("roster", "builder", RunEstate.Json("""{"restrictions":{"exactRevision":"revision-1"}}"""));
        JsonObject selected = RunSelection.Resolve(Configuration, Capabilities, [pin], null, RunEstate.Json("{}"));
        Assert.Equal("unknown", selected["resolvedRevision"]!.GetValue<string>());
        if (!admitted)
        {
            Assert.Throws<ProviderRefusedException>(() => RunSelection.ResolveRevision(selected, revision));
            return;
        }

        RunSelection.ResolveRevision(selected, revision);
        Assert.Equal("model-a", selected["requested"]!["model"]!.GetValue<string>());
        Assert.Equal("revision-1", selected["resolvedRevision"]!.GetValue<string>());
    }

    [Fact]
    public void ProviderDefaultCannotSatisfyARequiredEffort()
    {
        RunPolicySource fixedEffort = new("goal", "reviewer", RunEstate.Json("""{"restrictions":{"requiredEffort":"high"}}"""));
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(Configuration, Capabilities with { ProviderDefault = true }, [fixedEffort], RunEstate.Json("""{"effort":"provider-default"}"""), RunEstate.Json("{}")));
    }
}
