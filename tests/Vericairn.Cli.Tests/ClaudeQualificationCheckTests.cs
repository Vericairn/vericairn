// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class ClaudeQualificationCheckTests : IDisposable
{
    private const string Launcher = "claude-code";
    private const string Shipped = "2.1.296";
    private const string Newer = "2.1.322";
    private readonly PolicyFixture estate = new();
    private readonly string scratch = CliHarness.TempDirectory("claude-qualification-check");
    private readonly string? previousHome;
    private readonly string? previousConfig;
    private readonly string report;
    private readonly string home;

    public ClaudeQualificationCheckTests()
    {
        _ = CliHarness.Run("--help");
        home = Path.Combine(scratch, "home");
        previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        previousConfig = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Path.Combine(scratch, "ambient"));
        new ReleaseUpdateStore().Configure(false);
        report = Path.Combine(scratch, "claude.json");
    }

    [Fact]
    public void CheckEstablishesTheProvisionedInventoryWithoutAnActingPrincipalOrARecord()
    {
        Provision();
        var before = Snapshot();
        CliHarness.Outcome result = Check("--json");
        Assert.True(result.Code == 0, result.Out + result.Err);
        JsonElement json = result.Json;
        Assert.True(json.GetProperty("inventoryEstablished").GetBoolean());
        Assert.True(json.GetProperty("qualified").GetBoolean());
        JsonElement signIn = json.GetProperty("signIn");
        Assert.Equal("setup-token", signIn.GetProperty("form").GetString());
        Assert.Equal("max", signIn.GetProperty("accountType").GetString());
        Assert.Equal("operator-statement", signIn.GetProperty("accountTypeSource").GetString());
        string probeHome = File.ReadAllText(report + ".probed-sign-in");
        Assert.NotEqual(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"), probeHome);
        Assert.False(Directory.Exists(probeHome), "The probe's credential directory survived.");
        Assert.False(File.Exists(report));
        Assert.False(Directory.Exists(Path.Combine(home, "keys")));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void CheckReportsInstalledAgainstQualifiedVersionAndTheCommandToQualify()
    {
        Provision();
        Version(Newer);
        var before = Snapshot();

        CliHarness.Outcome result = Check("--json");

        Assert.Equal(1, result.Code);
        JsonElement json = result.Json;
        Assert.True(json.GetProperty("inventoryEstablished").GetBoolean());
        Assert.False(json.GetProperty("qualified").GetBoolean());
        JsonElement version = json.GetProperty("version");
        Assert.Equal(Newer, version.GetProperty("installed").GetString());
        Assert.Equal(Shipped, version.GetProperty("qualified").GetString());
        Assert.False(version.GetProperty("matches").GetBoolean());
        Assert.Equal("vericairn worker qualify --launcher claude-code --by <human-principal>", json.GetProperty("qualification").GetString());
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void DoctorReportsTheVersionDifferenceUsingTheSameOverrideWithoutReadingASignIn()
    {
        Version(Newer);
        var before = Snapshot();

        CliHarness.Outcome result = CliHarness.Ok("--estate", estate.Estate.Root, "--json", "doctor", "--setting", Command());

        JsonElement probe = result.Json.GetProperty("claudeCode");
        Assert.Equal("unavailable", probe.GetProperty("state").GetString());
        Assert.Equal(Newer, probe.GetProperty("value").GetString());
        string detail = probe.GetProperty("detail").GetString()!;
        Assert.Contains($"installed version {Newer}, qualified version {Shipped}", detail, StringComparison.Ordinal);
        Assert.Contains("vericairn worker qualify --launcher claude-code --by <human-principal>", detail, StringComparison.Ordinal);
        Assert.True(File.Exists(report + ".probed"));
        Assert.False(File.Exists(report));
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("Fixture-Selector", "blocked", 1)]
    [InlineData("Fixture-SelectorExtra", "clear", 0)]
    public void CheckUsesTheEstatesWholeTermMatchingOnTheConfiguredModel(string model, string expected, int status)
    {
        JsonObject policy = estate.PolicyBody("blocked-terms", duty: false);
        policy["rules"] = new JsonArray(new JsonObject
        {
            ["key"] = "terms", ["kind"] = BlockedTerms.Kind, ["version"] = 1,
            ["parameters"] = new JsonObject { ["terms"] = new JsonArray("fixture-selector") },
        });
        estate.Policy(policy);
        Provision(model);
        var before = Snapshot();

        CliHarness.Outcome result = Check("--json");

        Assert.True(result.Code == status, result.Out + result.Err);
        Assert.True(result.Json.GetProperty("inventoryEstablished").GetBoolean());
        Assert.Equal(model, result.Json.GetProperty("model").GetString());
        Assert.Equal(expected, result.Json.GetProperty("modelStatus").GetString());
        Assert.Equal(before, Snapshot());
    }

    private void Provision(string model = "fixture-selector")
    {
        string token = string.Concat("sk-", "ant-", "oat", "01-", new string('a', 202));
        CliHarness.Ok("worker", "configure", "--launcher", Launcher, "--model", model, "--effort", "high");
        CliHarness.OkWithInput(token + "\n", "worker", "configure", "--launcher", Launcher,
            "--import-setup-token", "--account-type", "max", "--token-stdin");
        Version(Shipped);
    }

    private void Version(string version) =>
        File.WriteAllText(report + ".behavior", JsonSerializer.Serialize(new { version = version + " (Claude Code)", setupToken = "required" }));

    private string Command() => "command=" + FakeHarness.Setting("native-claude", report);

    private CliHarness.Outcome Check(params string[] options) =>
        CliHarness.Run(["--estate", estate.Estate.Root, "worker", "qualify", "--check", "--launcher", Launcher, "--setting", Command(), .. options]);

    private (string Path, string Hash)[] Snapshot() =>
        new[] { Path.Combine(estate.Estate.Root, "records"), home }
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal)
            .Select(path => (path, ContentHash.Sha256File(path))).ToArray();

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfig);
        estate.Dispose();
        CliHarness.Delete(scratch);
    }
}
