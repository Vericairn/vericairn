// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class OptionOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkerOptionsOwnTheirInputsIncludingWithAssignments(bool assignWith)
    {
        RunConfiguration configuration = Configuration();
        var configurations = new Dictionary<string, RunConfiguration>(StringComparer.OrdinalIgnoreCase) { ["sample"] = configuration };
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["FIRST"] = "one", ["SECOND"] = "two" };
        string[] command = ["sample-command", "serve"];
        WorkerOptions original = new();
        WorkerOptions options = assignWith
            ? original with { Configurations = configurations, Environment = environment, Vericairn = command }
            : new WorkerOptions { Configurations = configurations, Environment = environment, Vericairn = command };

        configurations.Clear();
        environment["FIRST"] = "changed";
        environment.Remove("SECOND");
        command[0] = "changed";

        Assert.Same(configuration, options.Configurations["SAMPLE"]);
        Assert.Equal(["FIRST", "SECOND"], options.Environment!.Keys);
        Assert.Equal("one", options.Environment["first"]);
        Assert.Equal("two", options.Environment["second"]);
        Assert.Equal(["sample-command", "serve"], options.VericairnCommand());
        AssertReadOnlyDictionary(options.Configurations);
        AssertReadOnlyDictionary(options.Environment);
        AssertReadOnlyList(options.Vericairn!);
        Assert.Empty(original.Configurations);
        Assert.Null(original.Environment);
        Assert.Null(original.Vericairn);

        WorkerOptions copy = options with { Configurations = options.Configurations, Environment = options.Environment };
        Assert.Same(configuration, copy.Configurations["SAMPLE"]);
        Assert.Equal("one", copy.Environment!["first"]);
    }

    [Fact]
    public void WorkerDefaultsAndUnspecifiedCollectionsRemainEmptyOrNull()
    {
        Assert.Empty(WorkerOptions.Default.Configurations);
        AssertReadOnlyDictionary(WorkerOptions.Default.Configurations);
        Assert.Null(WorkerOptions.Default.Environment);
        Assert.Null(WorkerOptions.Default.Vericairn);
        RunConfiguration configuration = Configuration();
        Assert.Null(configuration.Settings);
        Assert.Null(configuration.Efforts);
        Assert.Null(configuration.EnvironmentPolicy);
        WorkerConfiguration worker = new("choice-a", "test-effort");
        Assert.Null(worker.AllowedModels);
        Assert.Null(worker.AllowedEfforts);
        Assert.Equal(worker, worker with { });
        Assert.Equal(configuration, configuration with { });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunConfigurationSnapshotsTheFullInventoryAndKeepsItsSerializedShape(bool assignWith)
    {
        string[] models = ["choice-a", "choice-b"];
        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["second"] = "two", ["first"] = "one" };
        var efforts = new List<string> { "test-effort", "another-effort" };
        string[] environment = ["SAMPLE_FIRST", "SAMPLE_SECOND"];
        var destinations = new List<string> { "sample-destination" };
        string[] dependencies = ["sample-dependency"];
        RunComponent originalComponent = new("component", "tool", "1", "digest", "configuration-digest", [], [], []);
        RunComponent component = assignWith
            ? originalComponent with { Environment = environment, Destinations = destinations, Dependencies = dependencies }
            : new("component", "tool", "1", "digest", "configuration-digest", environment, destinations, dependencies);
        var components = new List<RunComponent> { component };
        RunEnvironmentPolicy policy = assignWith
            ? new RunEnvironmentPolicy("policy", []) with { Components = components }
            : new("policy", components);
        RunConfiguration configuration = assignWith
            ? Configuration() with { Models = models, Settings = settings, EnvironmentPolicy = policy, Efforts = efforts }
            : new("configuration", "launcher", JsonSerializer.SerializeToElement(new { }), models, settings, policy, efforts);
        string before = JsonSerializer.Serialize(configuration, ProtocolJson.Options);

        models[0] = "changed";
        settings.Clear();
        efforts.Clear();
        environment[0] = "CHANGED";
        destinations.Clear();
        dependencies[0] = "changed";
        components.Clear();

        const string expected = """
            {"name":"configuration","launcher":"launcher","policy":{},"models":["choice-a","choice-b"],"settings":{"second":"two","first":"one"},"environmentPolicy":{"id":"policy","components":[{"id":"component","kind":"tool","version":"1","hash":"digest","configurationHash":"configuration-digest","environment":["SAMPLE_FIRST","SAMPLE_SECOND"],"destinations":["sample-destination"],"dependencies":["sample-dependency"]}]},"efforts":["test-effort","another-effort"]}
            """;
        Assert.Equal(expected, before);
        Assert.Equal(before, JsonSerializer.Serialize(configuration, ProtocolJson.Options));
        RunConfiguration roundTrip = JsonSerializer.Deserialize<RunConfiguration>(before, ProtocolJson.Options)!;
        Assert.Equal(before, JsonSerializer.Serialize(roundTrip, ProtocolJson.Options));
        AssertReadOnlyList(configuration.Models);
        AssertReadOnlyList(configuration.Efforts!);
        AssertReadOnlyDictionary(configuration.Settings!);
        AssertReadOnlyList(policy.Components);
        AssertReadOnlyList(component.Environment);
        AssertReadOnlyList(component.Destinations);
        AssertReadOnlyList(component.Dependencies);
        Assert.Empty(originalComponent.Environment);

        var (_, _, _, deconstructedModels, deconstructedSettings, deconstructedPolicy, deconstructedEfforts) = configuration;
        Assert.Same(configuration.Models, deconstructedModels);
        Assert.Same(configuration.Settings, deconstructedSettings);
        Assert.Same(policy, deconstructedPolicy);
        Assert.Same(configuration.Efforts, deconstructedEfforts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableWorkerConfigurationCopiesItsAllowlists(bool assignWith)
    {
        string[] models = ["choice-a", "choice-b"];
        var efforts = new List<string> { "test-effort", "another-effort" };
        WorkerConfiguration original = new("choice-a", "test-effort");
        WorkerConfiguration configuration = assignWith
            ? original with { AllowedModels = models, AllowedEfforts = efforts }
            : new("choice-a", "test-effort", models, efforts);
        models[0] = "changed";
        efforts.Clear();
        Assert.Equal(["choice-a", "choice-b"], configuration.AllowedModels);
        Assert.Equal(["test-effort", "another-effort"], configuration.AllowedEfforts);
        AssertReadOnlyList(configuration.AllowedModels!);
        AssertReadOnlyList(configuration.AllowedEfforts!);
        Assert.Null(original.AllowedModels);
        Assert.Null(original.AllowedEfforts);
        Assert.Equal("{\"model\":\"choice-a\",\"effort\":\"test-effort\",\"allowedModels\":[\"choice-a\",\"choice-b\"],\"allowedEfforts\":[\"test-effort\",\"another-effort\"]}",
            JsonSerializer.Serialize(configuration, ProtocolJson.Options));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapabilitiesCopyEveryVocabulary(bool assignWith)
    {
        var source = new List<string> { "first", "second" };
        RunCapabilities capabilities = assignWith
            ? RunCapabilities.Unsupported with { Scopes = source, Efforts = source, Providers = source, Destinations = source, DataBoundaries = source, ProviderDefault = true, ClosedEnvironment = true }
            : new(source, source, source, source, source, ProviderDefault: true, ClosedEnvironment: true);
        source.Clear();
        IReadOnlyList<string>[] vocabularies = [capabilities.Scopes, capabilities.Efforts, capabilities.Providers, capabilities.Destinations, capabilities.DataBoundaries];
        foreach (IReadOnlyList<string> values in vocabularies)
        {
            Assert.Equal(["first", "second"], values);
            AssertReadOnlyList(values);
        }
        Assert.Empty(RunCapabilities.Unsupported.Scopes);
        Assert.False(RunCapabilities.Unsupported.ProviderDefault);
        Assert.False(RunCapabilities.Unsupported.ClosedEnvironment);
        Assert.Equal("{\"scopes\":[\"first\",\"second\"],\"efforts\":[\"first\",\"second\"],\"providers\":[\"first\",\"second\"],\"destinations\":[\"first\",\"second\"],\"dataBoundaries\":[\"first\",\"second\"],\"providerDefault\":true,\"closedEnvironment\":true}",
            JsonSerializer.Serialize(capabilities, ProtocolJson.Options));
    }

    [Fact]
    public void SortedAndImmutableDictionaryComparersAndOrderAreRetained()
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["second"] = "two", ["FIRST"] = "one" };
        var sortedList = new SortedList<string, string>(StringComparer.OrdinalIgnoreCase) { ["second"] = "two", ["FIRST"] = "one" };
        ImmutableDictionary<string, string> immutable = sorted.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, string>[] inputs = [sorted, sortedList, immutable];
        foreach (IReadOnlyDictionary<string, string> input in inputs)
        {
            RunConfiguration configuration = Configuration() with { Settings = input };
            Assert.Equal(input.Keys, configuration.Settings!.Keys);
            Assert.Equal("one", configuration.Settings["first"]);
            Assert.Equal("two", configuration.Settings["SECOND"]);
            AssertReadOnlyDictionary(configuration.Settings);
        }
        RunConfiguration copy = Configuration() with { Settings = sorted };
        sorted.Clear();
        Assert.Equal("one", copy.Settings!["first"]);
    }

    [Fact]
    public void AReadOnlyViewOfCallerOwnedStorageIsStillCopied()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["second"] = "two", ["first"] = "one" };
        var view = new ReadOnlyDictionary<string, string>(values);
        RunConfiguration configuration = Configuration() with { Settings = view };
        values.Clear();
        Assert.Equal(["second", "first"], configuration.Settings!.Keys);
        Assert.Equal("one", configuration.Settings["first"]);
        AssertReadOnlyDictionary(configuration.Settings);
    }

    private static RunConfiguration Configuration() => new("configuration", "launcher", JsonSerializer.SerializeToElement(new { }), ["choice-a"]);

    private static void AssertReadOnlyList<T>(IReadOnlyList<T> values)
    {
        var list = Assert.IsAssignableFrom<IList<T>>(values);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(default!));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        if (list.Count > 0) Assert.Throws<NotSupportedException>(() => list[0] = default!);
    }

    private static void AssertReadOnlyDictionary<T>(IReadOnlyDictionary<string, T> values)
    {
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, T>>(values);
        Assert.True(dictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => dictionary.Add("changed", default!));
        Assert.Throws<NotSupportedException>(() => dictionary.Clear());
        if (dictionary.Count > 0) Assert.Throws<NotSupportedException>(() => dictionary[dictionary.Keys.First()] = default!);
    }
}
