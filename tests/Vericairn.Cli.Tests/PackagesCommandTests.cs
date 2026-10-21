// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Cli;
using Vericairn.Records.Estate;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class PackagesCommandTests
{
    [Fact]
    public void CommandTreeNamesEveryInputAndHasNoRegistryCredentialOption()
    {
        Command packages = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken).Subcommands.Single(c => c.Name == "packages");
        Assert.Equal(["deprecate", "observe", "unlist"], packages.Subcommands.Select(c => c.Name).Order());
        foreach (string action in new[] { "deprecate", "unlist" })
        {
            Command command = packages.Subcommands.Single(c => c.Name == action);
            foreach (string name in new[] { "--product", "--package", "--version", "--by" })
                Assert.True(command.Options.Single(o => o.Name == name).Required);
            Assert.DoesNotContain(command.Options, o => o.Name.Contains("key", StringComparison.OrdinalIgnoreCase));
        }
        Command deprecate = packages.Subcommands.Single(c => c.Name == "deprecate");
        Assert.True(deprecate.Options.Single(o => o.Name == "--reason").Required);
        foreach (string name in new[] { "--message", "--alternate-package", "--alternate-range" })
            Assert.Contains(deprecate.Options, o => o.Name == name && !o.Required);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextAndJsonKeepTheOriginalIntentThenRecordTheObservedOutcome(bool deprecate)
    {
        // The harness initializes its own home before this fixture opens runtime storage.
        Assert.Equal(0, CliHarness.Run("packages", "--help").Code);
        using var f = new PackageTestEstate();
        string[] action = ["--estate", f.Service.Session.Root, "--json", "packages", deprecate ? "deprecate" : "unlist", "--product", f.Product.Id, "--version", "1.0.0", "--package", "Example.Widget", "--by", f.Estate.OperatorId];
        if (deprecate) action = [.. action, "--reason", "Legacy", "Other", "--message", "Use the replacement package.", "--alternate-package", "Example.Replacement", "--alternate-range", "[2.0.0,)"];
        CliHarness.Outcome intent = CliHarness.RunWithApi(f.Service, null, action);
        Assert.Equal(0, intent.Code);
        string id = intent.Json.GetProperty("intent").GetProperty("id").GetString()!;
        Assert.Equal("not-started", intent.Json.GetProperty("status").GetString());
        Assert.Contains("https://www.nuget.org/packages/example.widget/1.0.0/", intent.Json.GetProperty("instruction").GetString(), StringComparison.Ordinal);
        CliHarness.Outcome rerun = CliHarness.RunWithApi(f.Service, null, action);
        Assert.Equal(id, rerun.Json.GetProperty("intent").GetProperty("id").GetString());
        Assert.Single(f.Service.Session.Load().OfType("operation"));
        f.Handler.Registration = deprecate ? "deprecated" : "unlisted";
        CliHarness.Outcome observed = CliHarness.RunWithApi(f.Service, null, "--estate", f.Service.Session.Root, "packages", "observe", "--operation", id, "--by", f.Estate.OperatorId);
        Assert.Equal(0, observed.Code);
        Assert.Contains(deprecate ? "published, listed, deprecated" : "published, unlisted, not-deprecated", observed.Out, StringComparison.Ordinal);
        Assert.Contains("records/observation/", observed.Out, StringComparison.Ordinal);
        Assert.Equal("observed-success", EstateModel.BodyString(f.Service.Session.Load().Current("operation").Single(), "status"));
    }

    [Fact]
    public void ObserveAndRefusalHaveCanonicalJsonAndTheExpectedExitCodes()
    {
        Assert.Equal(0, CliHarness.Run("packages", "--help").Code);
        using var f = new PackageTestEstate();
        f.Handler.Registration = "not-found"; f.Handler.Versions = "not-found";
        CliHarness.Outcome observed = CliHarness.RunWithApi(f.Service, null, "--estate", f.Service.Session.Root, "--json", "packages", "observe", "--product", f.Product.Id, "--version", "1.0.0", "--by", f.Estate.OperatorId);
        Assert.Equal(0, observed.Code);
        Assert.Equal("not-found", observed.Json.GetProperty("packages")[0].GetProperty("state").GetProperty("publication").GetString());
        int before = f.Service.Session.Load().Records.Count;
        CliHarness.Outcome refused = CliHarness.RunWithApi(f.Service, null, "--estate", f.Service.Session.Root, "--json", "packages", "observe", "--by", f.Estate.OperatorId);
        // A refusal exits 1, as the changes family's operations do.
        Assert.Equal(1, refused.Code);
        Assert.Equal("refused", refused.Json.GetProperty("status").GetString());
        Assert.Equal(before, f.Service.Session.Load().Records.Count);
    }
}
