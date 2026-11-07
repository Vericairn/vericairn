// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// A workspace setup command configured on the machine (FIX-86): run in the workspace before dispatch, as the person and outside the
/// harness, with a harness's filtered environment and the variables it names (FIX-96), its outcome alone recorded when it succeeds; a
/// failure, a timeout, a new link or a change outside the permitted paths refuses before the intent, so nothing is recorded.
/// </summary>
[Collection("home")]
public sealed class SetupBeforeDispatchTests
{
    [Fact]
    public void ASetupCommandRunsInTheWorkspaceAsThePersonAndOnlyItsOutcomeIsRecorded()
    {
        using RunEstate run = PrepareOnlyWorkspaceTests.Ignoring(new RunEstate());
        string mark = "person-" + Ulid.NewUlid().Value;
        // Credential-shaped names are assembled here, so no literal credential name or value sits in the source.
        string named = "NPM_" + "TOKEN", ambient = "GH_" + "TOKEN";
        run.Service.WorkerOptions = run.Service.WorkerOptions with
        {
            Environment = new Dictionary<string, string>(run.Service.WorkerOptions.Environment!, WorkerEnvironment.Names)
            {
                ["PERSON_SETTING"] = mark, ["UNNAMED_SETTING"] = mark, [named] = mark, [ambient] = mark, ["NODE_OPTIONS"] = "--require " + mark,
                ["VERICAIRN_RUNTIME_BINDING"] = mark,
            },
        };
        string setup = run.Report("setup");
        using Configured configured = new(run, new() { Command = FakeHarness.Command("setup-install", setup), Variables = ["PERSON_SETTING", named] });
        string report = run.Report("run");

        RunResponse started = run.Start("claude-code", "claude", report);

        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        JsonObject given = Assert.Single(FakeHarness.Reports(setup));
        JsonObject environment = given["environment"]!.AsObject();
        // A harness's filtered environment, plus only what the configuration names: a named credential passes, an ambient one,
        // a loader directive and an unnamed setting do not (FIX-96).
        Assert.Equal(mark, environment["PERSON_SETTING"]!.GetValue<string>());
        Assert.Equal(mark, environment[named]!.GetValue<string>());
        // Windows names it Path; variable names compare without case there.
        Assert.Contains(environment, variable => string.Equals(variable.Key, "PATH", StringComparison.OrdinalIgnoreCase));
        foreach (string absent in new[] { ambient, "NODE_OPTIONS", "UNNAMED_SETTING" }) Assert.False(environment.ContainsKey(absent), absent);
        Assert.DoesNotContain(environment, variable => variable.Key.StartsWith("VERICAIRN_", StringComparison.OrdinalIgnoreCase));
        string workspace = given["directory"]!.GetValue<string>();
        Assert.Equal(workspace, FakeHarness.Reports(report)[^1]["directory"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(workspace, "node_modules", "left-pad", "index.js")));

        // The intent holds the outcome; never the command, its environment or its output.
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        Assert.Contains(recorded.Intent.Body.GetProperty("evidence").EnumerateArray(), e => e.GetProperty("summary").GetString()!
            .StartsWith("A workspace setup command configured on the machine that ran the worker ran before dispatch: status exited, exit code 0, ", StringComparison.Ordinal));
        string text = recorded.Intent.Body.GetRawText();
        foreach (string absent in new[] { "setup-install", setup, mark, "installed 1 package", "left-pad" })
            Assert.DoesNotContain(absent, text, StringComparison.Ordinal);

        // Settlement judges the agent from what setup left: the installed files are no change of its.
        Assert.Empty(settled.Scope!.Outside);
    }

    [Theory]
    [InlineData("setup-fails", "status exited, exit code 3, ")]
    [InlineData("setup-hangs", "status timed-out, exit code none, ")]
    public void AFailedOrTimedOutSetupRefusesBeforeTheIntentAndRecordsNothing(string scenario, string facts)
    {
        using RunEstate run = PrepareOnlyWorkspaceTests.Ignoring(new RunEstate());
        string setup = run.Report("setup");
        using Configured configured = new(run, new()
        {
            Command = FakeHarness.Command(scenario, setup),
            TimeoutSeconds = scenario == "setup-hangs" ? 3 : WorkspaceSetupConfiguration.DefaultTimeoutSeconds,
        });
        int records = run.Estate.Load().ById.Count;
        string report = run.Report("run");

        RunResponse refused = run.Start("claude-code", "claude", report);

        Assert.False(refused.Ok);
        Assert.Equal("refused", refused.Problem!.Code);
        Assert.StartsWith($"The workspace setup command did not succeed ({facts}", refused.Message, StringComparison.Ordinal);
        Assert.Contains(" ms); run it in the workspace yourself to see its output. Nothing was recorded.", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Its setup command ran there, so it stays with what that left.", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("no registry here", refused.Message, StringComparison.Ordinal);
        Assert.Equal(records, run.Estate.Load().ById.Count);
        Assert.Empty(run.Service.Runs().Runs);

        // Nothing the setup command started outlives it.
        foreach (JsonObject started in FakeHarness.Reports(setup))
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(started["process"]!.GetValue<int>()));
    }

    [Theory]
    [InlineData("setup-link", "node_modules/.bin/left-pad", "holds a symbolic link or reparse point at")]
    [InlineData("setup-outside", "README.md", "changed outside the assignment's permitted changes after its setup command")]
    public void ASetupThatLeavesALinkOrAChangeOutsideThePermittedPathsIsRefusedWithThePathNamed(string scenario, string path, string why)
    {
        using RunEstate run = PrepareOnlyWorkspaceTests.Ignoring(new RunEstate());
        using Configured configured = new(run, new() { Command = FakeHarness.Command(scenario, run.Report("setup")) });
        int records = run.Estate.Load().ById.Count;

        RunResponse refused = run.Start("claude-code", "claude", run.Report("run"));

        Assert.False(refused.Ok);
        Assert.Contains(why, refused.Message, StringComparison.Ordinal);
        Assert.Contains(path, refused.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was recorded.", refused.Message, StringComparison.Ordinal);
        Assert.Equal(records, run.Estate.Load().ById.Count);
        Assert.Empty(run.Service.Runs().Runs);
    }

    [Fact]
    public void AnUnreadableSetupConfigurationRefusesAndNothingRunsInItsPlace()
    {
        using RunEstate run = PrepareOnlyWorkspaceTests.Ignoring(new RunEstate());
        string setup = run.Report("setup");
        using Configured configured = new(run, new() { Command = FakeHarness.Command("setup-install", setup) });
        var store = new WorkerConfigurationStore();
        Assert.Equal(FakeHarness.Command("setup-install", setup), store.ReadSetup("claude-code", [Ulid.NewUlid().Value, run.Resource])!.Command);

        string file = Path.Combine(Records.Signing.VericairnHome.Path(), "worker", "setup-repository-" + run.Resource + ".json");
        File.WriteAllText(file, "{\"command\":[\"tool\"],\"shell\":true}");
        int records = run.Estate.Load().ById.Count;
        RunResponse refused = run.Start("claude-code", "claude", run.Report("run"));
        Assert.False(refused.Ok);
        Assert.Contains($"The workspace setup configuration 'setup-repository-{run.Resource}.json' could not be read", refused.Message, StringComparison.Ordinal);
        Assert.Equal(records, run.Estate.Load().ById.Count);
        Assert.Empty(FakeHarness.Reports(setup));
    }

    /// <summary>A repository's setup command for the length of a test, under the shared test home and keyed by its unique resource.</summary>
    private sealed class Configured : IDisposable
    {
        private readonly string resource;

        public Configured(RunEstate run, WorkspaceSetupConfiguration setup)
        {
            resource = run.Resource;
            new WorkerConfigurationStore().WriteSetup(null, resource, setup);
        }

        public void Dispose() => new WorkerConfigurationStore().ClearSetup(null, resource);
    }
}
