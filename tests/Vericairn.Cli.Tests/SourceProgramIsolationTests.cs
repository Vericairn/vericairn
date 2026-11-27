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
            string resource = adopt.Json.GetProperty("resource").GetProperty("id").GetString()!;
            CliHarness.Ok("--estate", root, "--json", "sources", "observe", probe.Repository, "--resource", resource, "--by", op);
            Assert.False(probe.Ran, $"sources observe ran the program the repository's {kind} configuration names.");
            CliHarness.Ok("--estate", root, "--json", "sources", "clone", probe.Repository, "--resource", resource);
            Assert.False(probe.Ran, $"sources clone ran the program the repository's {kind} configuration names.");
            if (kind is RepositoryProgramProbe.Fsmonitor or RepositoryProgramProbe.Filter or RepositoryProgramProbe.Signature)
                Assert.True(probe.Control(), $"Plain git did not run the configured {kind} fixture program.");
            Assert.NotEmpty(after["ignoredConfiguration"]!.AsArray());
            Assert.NotEqual(before["configurationHash"]!.GetValue<string>(), after["configurationHash"]!.GetValue<string>());
            before.Remove("ignoredConfiguration");
            before.Remove("configurationHash");
            after.Remove("ignoredConfiguration");
            after.Remove("configurationHash");
            Assert.True(JsonNode.DeepEquals(before, after), $"{before.ToJsonString()}\n{after.ToJsonString()}");
            Assert.Equal(before["commit"]!.GetValue<string>(), adopt.Json.GetProperty("commit").GetString());
            Assert.False(adopt.Json.GetProperty("clean").GetBoolean());
            Assert.Equal((1, 1), (before["modified"]!.GetValue<int>(), before["untracked"]!.GetValue<int>()));

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
            Assert.Contains("could not read the working tree", adopt.Out + adopt.Err, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(root, "records", "resource")) && Directory.EnumerateFiles(Path.Combine(root, "records", "resource"), "*.json", SearchOption.AllDirectories).Any());
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }

    [Theory]
    [InlineData("inspect")]
    [InlineData("adopt")]
    [InlineData("observe")]
    [InlineData("clone")]
    public void SourcesRefuseAnOutsideGitDirectoryBeforeAnyEstateWrite(string command)
    {
        string root = CliHarness.TempDirectory("sources-boundary");
        string repository = CliHarness.TempDirectory("sources-repository");
        try
        {
            const string id = "01M00000000000000000000001";
            File.WriteAllText(Path.Combine(repository, ".git"), $"gitdir: {root}\n");
            string[] options = command switch
            {
                "adopt" => ["--by", id, "--name", "Example", "--role", "source"],
                "observe" => ["--resource", id, "--by", id],
                "clone" => ["--resource", id],
                _ => [],
            };

            CliHarness.Outcome result = CliHarness.Run(["--estate", root, "--json", "sources", command, repository, .. options]);
            Assert.NotEqual(0, result.Code);
            Assert.Contains("gitdir:", result.Err + result.Out, StringComparison.Ordinal);
            Assert.Contains("outside the selected operator boundary", result.Err + result.Out, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(root, "records")));
        }
        finally
        {
            CliHarness.Delete(root, repository);
        }
    }

    [Fact]
    public void CloneRejectsARegistrationThatLaterReadsCouldNotOpen()
    {
        string root = CliHarness.TempDirectory("sources-linked-estate");
        string boundary = CliHarness.TempDirectory("sources-linked-boundary");
        string repository = Directory.CreateDirectory(Path.Combine(boundary, "repository")).FullName;
        string linked = Path.Combine(boundary, "linked");
        try
        {
            ContextJourneyTests.Git(repository, "init", "-q", "--initial-branch=main");
            ContextJourneyTests.Git(repository, "config", "core.autocrlf", "false");
            ContextJourneyTests.Git(repository, "commit", "-qm", "Initial fixture", "--allow-empty");
            ContextJourneyTests.Git(repository, "worktree", "add", "--quiet", "--detach", linked);
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Isolation.",
                "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string resource = CliHarness.Ok("--estate", root, "--json", "sources", "adopt", repository, "--by", op,
                "--name", "Example source", "--role", "Source fixture").Json.GetProperty("resource").GetProperty("id").GetString()!;
            CliHarness.Ok("--json", "sources", "inspect", linked, "--boundary", boundary);

            CliHarness.Outcome result = CliHarness.Run("--estate", root, "--json", "sources", "clone", linked, "--resource", resource, "--boundary", boundary);

            Assert.NotEqual(0, result.Code);
            Assert.Contains("later reads use the repository root as their boundary", result.Err + result.Out, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root, boundary);
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
