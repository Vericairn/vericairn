// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class WorkspaceRefusalTests
{
    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void APreflightRefusalRemovesTheWorkspaceItPrepared(string harness)
    {
        using var native = new NativeRun(harness);
        RunEstate run = native.Run;
        string workspace = DefaultWorkspace(run);
        AddAncestorConfiguration(workspace, harness);
        int records = run.Estate.Load().ById.Count;
        string refs = RunEstate.Git(run.Clone, "show-ref");

        RunResponse refused = native.Start();

        Assert.False(refused.Ok);
        Assert.Contains("Execution preflight refused", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Unqualified harness configuration", refused.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(workspace), refused.Message);
        Assert.Equal(records, run.Estate.Load().ById.Count);
        Assert.Empty(FakeHarness.Reports(native.Probe.Report));
        Assert.Equal(refs, RunEstate.Git(run.Clone, "show-ref"));
        Assert.Empty(RunEstate.Git(run.Clone, "status", "--porcelain"));
    }

    [Fact]
    public void RetryingAfterMovingTheRootStartsWithACleanWorkspace()
    {
        using var native = new NativeRun("claude");
        RunEstate run = native.Run;
        string oldWorkspace = DefaultWorkspace(run);
        AddAncestorConfiguration(oldWorkspace);
        Assert.False(native.Start().Ok);
        var store = new WorkerConfigurationStore();
        string? previous = store.ReadWorkspaceRoot();
        try
        {
            string root = Path.Combine(run.Scratch, "new-workspaces");
            store.WriteWorkspaceRoot(root);
            // A fresh provider must not recover a registration left by the refused preparation.
            run.Service.Providers.Register(new GitWorktreeWorkspaces(FakeHarness.ScopeCommand()));
            RunResponse retry = native.Start();

            Assert.True(retry.Ok, retry.Message);
            Assert.Equal("observed-success", run.Settled(retry.Run).Status);
            string workspace = Path.Combine(HarnessWorkspace.Canonical(root), run.Service.Session.Manifest.Estate, "work-w-1");
            Assert.False(Directory.Exists(oldWorkspace));
            Assert.True(Directory.Exists(workspace));
            Assert.Equal(RunEstate.Git(run.Clone, "rev-parse", "HEAD"), RunEstate.Git(workspace, "rev-parse", "HEAD"));
            Assert.Empty(RunEstate.Git(workspace, "status", "--porcelain"));
        }
        finally
        {
            if (previous is not null) store.WriteWorkspaceRoot(previous);
            else File.Delete(Path.Combine(VericairnHome.Path(), "worker", "workspaces.json"));
        }
    }

    [Fact]
    public void ALongBranchRefusalRemovesItsWorkspaceAndCanRetryUnderAnotherRoot()
    {
        using var run = new RunEstate();
        var store = new WorkerConfigurationStore();
        string? previous = store.ReadWorkspaceRoot();
        string branch = "work/" + new string('b', 80);
        string root = Path.Combine(run.Scratch, "long-root");
        while (root.Length < 120) root = Path.Combine(root, "nested-root");
        string? workspace = null;
        IAgentLauncher launcher = run.Service.Providers.Launcher("claude-code")!;
        run.Service.Providers.Register(new HookedLauncher(launcher, prepared: context =>
        {
            workspace = context.Workspace;
            string reference = Path.Combine(workspace, ".git", "logs", "refs", "heads", branch.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(reference.Length > 260);
            Assert.True(File.Exists(reference));
            throw new ProviderRefusedException("The preflight refuses this run.");
        }));
        int records = run.Estate.Load().ById.Count;
        string refs = RunEstate.Git(run.Clone, "show-ref");
        try
        {
            store.WriteWorkspaceRoot(root);
            RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"), branch: branch);

            Assert.False(refused.Ok);
            Assert.Contains("Execution preflight refused", refused.Message, StringComparison.Ordinal);
            Assert.NotNull(workspace);
            Assert.False(Directory.Exists(workspace), refused.Message);
            Assert.DoesNotContain("Workspace kept", refused.Message, StringComparison.Ordinal);
            Assert.Equal(records, run.Estate.Load().ById.Count);
            Assert.Equal(refs, RunEstate.Git(run.Clone, "show-ref"));
            Assert.False(File.Exists(run.Report("refused")));

            string newRoot = Path.Combine(run.Scratch, "new-root");
            store.WriteWorkspaceRoot(newRoot);
            run.Service.Providers.Register(launcher);
            run.Service.Providers.Register(new GitWorktreeWorkspaces(FakeHarness.ScopeCommand()));
            RunResponse retry = run.Start("claude-code", "claude", run.Report("retry"), branch: branch);

            Assert.True(retry.Ok, retry.Message);
            Assert.Equal("observed-success", run.Settled(retry.Run).Status);
            Assert.False(Directory.Exists(workspace));
            string retried = Path.Combine(HarnessWorkspace.Canonical(newRoot), run.Service.Session.Manifest.Estate, branch.Replace('/', '-'));
            Assert.True(Directory.Exists(retried));
            Assert.Equal(RunEstate.Git(run.Clone, "rev-parse", "HEAD"), WorkspaceGitCommand.Require(retried, "rev-parse", "HEAD"));
            Assert.Empty(WorkspaceGitCommand.Require(retried, "status", "--porcelain"));
        }
        finally
        {
            if (previous is not null) store.WriteWorkspaceRoot(previous);
            else File.Delete(Path.Combine(VericairnHome.Path(), "worker", "workspaces.json"));
        }
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("tracked")]
    [InlineData("untracked")]
    [InlineData("ignored")]
    [InlineData("staged")]
    [InlineData("metadata")]
    [InlineData("other-branch")]
    [InlineData("reset-commit")]
    public void WorkAddedDuringPreflightIsKeptAndNamed(string change)
    {
        using var run = new RunEstate();
        string? workspace = null;
        string? head = null;
        string? index = null;
        run.Service.Providers.Register(new HookedLauncher(run.Service.Providers.Launcher("claude-code")!, prepared: context =>
        {
            workspace = context.Workspace;
            switch (change)
            {
                case "commit":
                case "other-branch":
                case "reset-commit":
                    if (change == "other-branch") RunEstate.Git(workspace, "checkout", "-q", "-b", "saved-work");
                    RunEstate.Git(workspace, "commit", "--allow-empty", "-q", "-m", "Work to preserve");
                    if (change == "other-branch") RunEstate.Git(workspace, "checkout", "-q", "work/w-1");
                    if (change == "reset-commit") RunEstate.Git(workspace, "reset", "--hard", "HEAD~1");
                    break;
                case "tracked":
                case "staged":
                    File.WriteAllText(Path.Combine(workspace, "README.md"), "work to preserve\n");
                    if (change == "staged")
                    {
                        RunEstate.Git(workspace, "add", "README.md");
                        File.WriteAllText(Path.Combine(workspace, "README.md"), "widget\n");
                    }
                    break;
                case "untracked":
                    File.WriteAllText(Path.Combine(workspace, "work.txt"), "untracked work\n");
                    break;
                case "ignored":
                    File.WriteAllText(Path.Combine(workspace, ".vericairn", "work.txt"), "ignored work\n");
                    break;
                case "metadata":
                    WorkspaceMetadata.Write(workspace, File.ReadAllText(context.Brief) + "work added to the brief\n");
                    break;
            }
            head = RunEstate.Git(workspace, "rev-parse", "HEAD");
            index = RunEstate.Git(workspace, "diff", "--cached");
            throw new ProviderRefusedException("The preflight refuses this run.");
        }));
        int records = run.Estate.Load().ById.Count;

        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));

        Assert.False(refused.Ok);
        Assert.NotNull(workspace);
        Assert.True(Directory.Exists(workspace));
        Assert.Contains(HarnessWorkspace.Canonical(workspace), refused.Message, StringComparison.Ordinal);
        Assert.Contains("Preserve", refused.Message, StringComparison.Ordinal);
        Assert.Equal(head, RunEstate.Git(workspace, "rev-parse", "HEAD"));
        Assert.Equal(index, RunEstate.Git(workspace, "diff", "--cached"));
        Assert.Equal(records, run.Estate.Load().ById.Count);
        Assert.False(File.Exists(run.Report("refused")));
    }

    [Fact]
    public void AnEarlierRunsCleanWorkspaceIsKeptAndNamed()
    {
        using var run = new RunEstate();
        RunResponse first = run.Start("claude-code", "claude", run.Report("first"));
        Assert.True(first.Ok, first.Message);
        Assert.Equal("observed-success", run.Settled(first.Run).Status);
        string workspace = DefaultWorkspace(run);
        Assert.Empty(RunEstate.Git(workspace, "status", "--porcelain"));
        run.Service.Providers.Register(new HookedLauncher(run.Service.Providers.Launcher("claude-code")!, prepared: _ =>
            throw new ProviderRefusedException("The next preflight refuses.")));

        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));

        Assert.False(refused.Ok);
        Assert.True(Directory.Exists(workspace));
        Assert.Contains(workspace, refused.Message, StringComparison.Ordinal);
        Assert.Contains("Preserve", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CustodyRefusalAfterPreparationStillRefusesAndRemovesTheUnusedWorkspace()
    {
        using var run = new RunEstate();
        run.Service.Providers.Register(new HookedLauncher(run.Service.Providers.Launcher("claude-code")!, prepared: _ =>
            RunEstate.Ok(run.Service.DeviceCreate(new DeviceCreateRequest(run.Estate.OperatorId, "Arriving operator device", "file")))));

        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));

        Assert.False(refused.Ok);
        Assert.Contains("The worker launches nothing while a device", refused.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(DefaultWorkspace(run)), refused.Message);
        Assert.Empty(run.Service.Records("operation"));
        Assert.False(File.Exists(run.Report("refused")));
    }

    [Fact]
    public void ANewWorkspaceOnABranchWithEarlierCommitsIsKeptAndNamed()
    {
        using var run = new RunEstate();
        RunEstate.Git(run.Clone, "checkout", "-q", "-b", "work/w-1");
        RunEstate.Git(run.Clone, "commit", "--allow-empty", "-q", "-m", "Earlier work");
        string head = RunEstate.Git(run.Clone, "rev-parse", "HEAD");
        RunEstate.Git(run.Clone, "checkout", "-q", "main");
        run.Service.Providers.Register(new HookedLauncher(run.Service.Providers.Launcher("claude-code")!, prepared: _ =>
            throw new ProviderRefusedException("The preflight refuses this run.")));

        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));

        Assert.False(refused.Ok);
        string workspace = HarnessWorkspace.Canonical(DefaultWorkspace(run));
        Assert.Contains(workspace, refused.Message, StringComparison.Ordinal);
        Assert.Contains("Preserve", refused.Message, StringComparison.Ordinal);
        Assert.Equal(head, RunEstate.Git(workspace, "rev-parse", "HEAD"));
        Assert.Empty(RunEstate.Git(workspace, "status", "--porcelain"));
        Assert.Empty(run.Service.Records("operation"));
    }

    [Fact]
    public void ARootChangeDuringPreflightStillRefusesAndRemovesTheUnusedWorkspace()
    {
        using var run = new RunEstate();
        var store = new WorkerConfigurationStore();
        string? previous = store.ReadWorkspaceRoot();
        try
        {
            run.Service.Providers.Register(new HookedLauncher(run.Service.Providers.Launcher("claude-code")!, prepared: _ =>
                store.WriteWorkspaceRoot(Path.Combine(run.Scratch, "new-root"))));

            RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));

            Assert.False(refused.Ok);
            Assert.Contains("workspace root changed", refused.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(DefaultWorkspace(run)), refused.Message);
            Assert.Empty(run.Service.Records("operation"));
            Assert.False(File.Exists(run.Report("refused")));
        }
        finally
        {
            if (previous is not null) store.WriteWorkspaceRoot(previous);
            else File.Delete(Path.Combine(VericairnHome.Path(), "worker", "workspaces.json"));
        }
    }

    [Fact]
    public void AWorkspaceTakenUpByAnotherPreparationIsKeptAndNamed()
    {
        using var run = new RunEstate();
        run.Service.Providers.Register(new HookedLauncher(run.Service.Providers.Launcher("claude-code")!, prepared: context =>
        {
            var other = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
            Assert.True(other.Prepare(run.Clone, "work/w-1", null, context.Workspace).Reused);
            throw new ProviderRefusedException("The original preflight refuses.");
        }));

        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));

        Assert.False(refused.Ok);
        string workspace = HarnessWorkspace.Canonical(DefaultWorkspace(run));
        Assert.True(Directory.Exists(workspace));
        Assert.Contains(workspace, refused.Message, StringComparison.Ordinal);
        Assert.Contains("Preserve", refused.Message, StringComparison.Ordinal);
        Assert.Empty(run.Service.Records("operation"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARefusalAfterIntentStillSettlesWithoutDispatchAndRemovesTheUnusedWorkspace(bool custody)
    {
        using var run = new RunEstate();
        HookedLauncher.Install(run, command: () =>
        {
            if (custody)
                RunEstate.Ok(run.Service.DeviceCreate(new DeviceCreateRequest(run.Estate.OperatorId, "Arriving operator device", "file")));
            else throw new ProviderRefusedException("The final execution preflight refuses.");
        });

        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));

        Assert.False(refused.Ok);
        Assert.Equal(custody ? "canceled" : "known-failure", refused.Status);
        Assert.False(Directory.Exists(DefaultWorkspace(run)), refused.Message);
        Assert.False(File.Exists(run.Report("refused")));
        Assert.Empty(run.Row(refused.Run).Charged.EnumerateObject());
    }

    [Fact]
    public void AnUnavailablePreparationInventoryKeepsAndNamesTheWorkspace()
    {
        using var run = new RunEstate();
        run.Service.Providers.Register(new GitWorktreeWorkspaces([Path.Combine(run.Scratch, "missing-observer")]));
        run.Service.Providers.Register(new HookedLauncher(run.Service.Providers.Launcher("claude-code")!, prepared: _ =>
            throw new ProviderRefusedException("The preflight refuses this run.")));

        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));

        Assert.False(refused.Ok);
        string workspace = HarnessWorkspace.Canonical(DefaultWorkspace(run));
        Assert.True(Directory.Exists(workspace));
        Assert.Contains(workspace, refused.Message, StringComparison.Ordinal);
        Assert.Contains("Preserve", refused.Message, StringComparison.Ordinal);
        Assert.Empty(run.Service.Records("operation"));
    }

    [Fact]
    public void AReplacementDirectoryIsKeptAndNamed()
    {
        using var run = new RunEstate();
        string original = Path.Combine(run.Scratch, "original");
        run.Service.Providers.Register(new HookedLauncher(run.Service.Providers.Launcher("claude-code")!, prepared: context =>
        {
            Directory.Move(context.Workspace, original);
            Directory.CreateDirectory(context.Workspace);
            File.WriteAllText(Path.Combine(context.Workspace, "work.txt"), "Replacement work");
            throw new ProviderRefusedException("The preflight refuses this run.");
        }));

        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));

        Assert.False(refused.Ok);
        string workspace = HarnessWorkspace.Canonical(DefaultWorkspace(run));
        Assert.Contains(workspace, refused.Message, StringComparison.Ordinal);
        Assert.Contains("Preserve", refused.Message, StringComparison.Ordinal);
        Assert.Equal("Replacement work", File.ReadAllText(Path.Combine(workspace, "work.txt")));
        Assert.True(Directory.Exists(original));
    }

    private static string DefaultWorkspace(RunEstate run) =>
        Path.Combine(VericairnHome.Path(), "estates", run.Service.Session.Manifest.Estate, "workspaces", "work-w-1");

    private static void AddAncestorConfiguration(string workspace, string harness = "claude")
    {
        string ancestor = Path.GetDirectoryName(Path.GetDirectoryName(workspace))!;
        Directory.CreateDirectory(ancestor);
        if (harness == "codex")
        {
            Directory.CreateDirectory(Path.Combine(ancestor, ".codex"));
            File.WriteAllText(Path.Combine(ancestor, ".codex", "config.toml"), "");
        }
        else File.WriteAllText(Path.Combine(ancestor, ".mcp.json"), "{}");
    }
}
