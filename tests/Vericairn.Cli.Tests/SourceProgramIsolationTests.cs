// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;

namespace Vericairn.Cli.Tests;

/// <summary>
/// sources inspect and sources adopt read a repository Vericairn did not create. Programs its own configuration names never run,
/// and the observation is the one taken before they were configured.
/// </summary>
[Collection("cli")]
public class SourceProgramIsolationTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Theory]
    [InlineData(RepositoryProgramProbe.Fsmonitor)]
    [InlineData(RepositoryProgramProbe.Filter)]
    [InlineData(RepositoryProgramProbe.Signature)]
    [InlineData(RepositoryProgramProbe.Hooks)]
    [InlineData(RepositoryProgramProbe.Pager)]
    public void InspectionAndAdoptionRunNoProgramTheRepositoryConfigurationNames(string kind)
    {
        string root = CliHarness.TempDirectory("isolation");
        using var probe = new RepositoryProgramProbe(kind);
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Isolation.",
                "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            JsonObject before = Observation(CliHarness.Ok("--json", "sources", "inspect", probe.Repository).Out);

            probe.Arm();
            JsonObject after = Observation(CliHarness.Ok("--json", "sources", "inspect", probe.Repository).Out);
            Assert.False(probe.Ran, $"sources inspect ran the program the repository's {kind} configuration names.");
            CliHarness.Outcome adopt = CliHarness.Ok("--estate", root, "--json", "sources", "adopt", probe.Repository, "--by", op, "--name", "Probe repository", "--role", "source of the probe");
            Assert.False(probe.Ran, $"sources adopt ran the program the repository's {kind} configuration names.");

            Assert.True(JsonNode.DeepEquals(before, after), $"{before.ToJsonString()}\n{after.ToJsonString()}");
            Assert.Equal(before["commit"]!.GetValue<string>(), adopt.Json.GetProperty("commit").GetString());
            Assert.False(adopt.Json.GetProperty("clean").GetBoolean());
            Assert.Equal((1, 1), (before["modified"]!.GetValue<int>(), before["untracked"]!.GetValue<int>()));

            if (probe.Control() is bool armed) Assert.True(armed, $"Plain git did not run the {kind} program; the probe proves nothing.");
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }

    [Fact]
    public void AWorkingTreeGitCannotReadIsRefusedAndNothingIsAdopted()
    {
        string root = CliHarness.TempDirectory("unreadable");
        using var probe = new RepositoryProgramProbe(RepositoryProgramProbe.Fsmonitor);
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Isolation.",
                "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            File.WriteAllBytes(Path.Combine(probe.Repository, ".git", "index"), "DIRC"u8.ToArray());

            Assert.NotEqual(0, CliHarness.Run("--json", "sources", "inspect", probe.Repository).Code);
            CliHarness.Outcome adopt = CliHarness.Run("--estate", root, "--json", "sources", "adopt", probe.Repository, "--by", op, "--name", "Probe repository", "--role", "source of the probe");
            Assert.NotEqual(0, adopt.Code);
            Assert.Contains("could not read the working tree", adopt.Out + adopt.Err, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(root, "records", "resource")) && Directory.EnumerateFiles(Path.Combine(root, "records", "resource"), "*.json", SearchOption.AllDirectories).Any());
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }

    // The observation without the time it was taken.
    private static JsonObject Observation(string json)
    {
        JsonObject observation = JsonNode.Parse(json)!.AsObject();
        Assert.True(observation.Remove("asOf"), json);
        return observation;
    }
}
