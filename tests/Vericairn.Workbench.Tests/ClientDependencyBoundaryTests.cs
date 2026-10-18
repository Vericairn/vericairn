// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using System.Text.Json;
using Vericairn.Providers.GitHub.Releases;
using Vericairn.Records.Updates;

namespace Vericairn.Workbench.Tests;

public sealed class ClientDependencyBoundaryTests
{
    [Fact]
    public void WorkbenchCompiledDependencyClosureContainsNeitherRuntimeNorSqlite()
    {
        // Read Workbench's own build output. This test host also references the CLI and runtime for integration tests.
        string manifest = Path.ChangeExtension(typeof(ShellViewModel).Assembly.Location, ".deps.json");
        using JsonDocument dependencies = JsonDocument.Parse(File.ReadAllBytes(manifest));
        string[] libraries = dependencies.RootElement.GetProperty("libraries").EnumerateObject()
            .Select(library => library.Name.Split('/')[0]).ToArray();

        Assert.Contains("vericairn-workbench", libraries);
        Assert.Contains(typeof(GitHubReleaseSource).Assembly.GetName().Name, libraries);
        Assert.DoesNotContain("Vericairn.Providers.GitHub", libraries);
        Assert.DoesNotContain("Vericairn.Runtime", libraries);
        Assert.DoesNotContain(libraries, name => name.Contains("sqlite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReleaseSourceCompiledAssemblyDependsOnlyOnRecordsAndThePlatform()
    {
        Assembly source = typeof(GitHubReleaseSource).Assembly;
        Assert.Equal("Vericairn.Providers.GitHub.Releases", source.GetName().Name);
        AssemblyName dependency = Assert.Single(source.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("System.", StringComparison.Ordinal) != true);
        Assert.Equal(typeof(IReleaseSource).Assembly.GetName().Name, dependency.Name);
    }

    [Fact]
    public void BothClientHostsReferenceTheIndependentReleaseSource()
    {
        string? source = typeof(GitHubReleaseSource).Assembly.GetName().Name;
        foreach (Assembly host in new[] { typeof(ShellViewModel).Assembly, typeof(Vericairn.Cli.CliApp).Assembly })
            Assert.Contains(host.GetReferencedAssemblies(), reference => reference.Name == source);
        Assert.DoesNotContain(typeof(ShellViewModel).Assembly.GetReferencedAssemblies(),
            reference => reference.Name is "Vericairn.Runtime" or "Vericairn.Providers.GitHub");
    }
}
