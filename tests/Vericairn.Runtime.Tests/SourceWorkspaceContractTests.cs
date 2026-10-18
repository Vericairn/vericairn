// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class SourceWorkspaceContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothSourcesPrepareRecoverAuditRetainAndDiscardThroughTheContract(bool recorded)
    {
        using var run = new RunEstate();
        ISourceAdapter source = Source(run, recorded);
        var registry = new ProviderRegistry();
        registry.Register(source);
        IWorkspaceProvider workspaces = Assert.IsAssignableFrom<IWorkspaceProvider>(registry.Workspace("git"));
        Assert.Same(source.Workspaces, workspaces);
        Assert.Same(workspaces, Assert.Single(registry.Workspaces));
        string cloneHead = source.Inspect(run.Clone).Commit;
        PreparedWorkspace workspace = workspaces.Prepare(run.Clone, "work/contract", null, Path.Combine(run.Scratch, "workspace"), run.Scratch);
        Assert.False(workspace.Reused);
        Assert.NotEqual(WorkspacePaths.Canonical(run.Clone), workspace.Path);
        Assert.Equal(cloneHead, workspace.Commit);
        WorkspaceSnapshot baseline = workspaces.Capture(workspace);
        Assert.Null(baseline.Problem);
        Assert.NotNull(workspaces.RetainForReview(workspace));
        RunScope scope = workspaces.CheckScope(workspace, baseline, ["**"]);
        Assert.Null(scope.Problem);
        Assert.Equal(0, scope.Changed);
        Assert.Empty(scope.Outside);
        Assert.Null(workspaces.RetainForReview(workspace));
        Assert.Equal(cloneHead, source.Inspect(run.Clone).Commit);

        PreparedWorkspace recovered = workspaces.Prepare(run.Clone, workspace.Branch, null, Path.Combine(run.Scratch, "other"), run.Scratch);
        Assert.True(recovered.Reused);
        Assert.Equal(workspace.Path, recovered.Path);
        Assert.Equal(workspace.Commit, recovered.Commit);
        Assert.NotNull(workspaces.DiscardUnused(recovered, baseline, string.Empty));
        Assert.True(Directory.Exists(workspace.Path));

        PreparedWorkspace unused = workspaces.Prepare(run.Clone, "work/unused-contract", null, Path.Combine(run.Scratch, "unused"), run.Scratch);
        Assert.Null(workspaces.DiscardUnused(unused, null, string.Empty));
        Assert.False(Directory.Exists(unused.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdmissionAndSettlementUseTheWorkspaceOperationsOfTheSelectedSource(bool recorded)
    {
        using var run = new RunEstate();
        ISourceAdapter source = Source(run, recorded);
        var registry = new ProviderRegistry();
        foreach (IAgentLauncher launcher in run.Service.Providers.Launchers) registry.Register(launcher);
        registry.Register(source);
        run.Service.Providers = registry;

        RunResponse started = run.Start("generic", "claude", run.Report("source-contract"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        Assert.NotNull(settled.Scope);
        Assert.Null(settled.Scope.Problem);
        Assert.Empty(settled.Scope.Outside);
        if (source is DirectorySource replay)
        {
            Assert.Equal(1, replay.Workspace.Captures);
            Assert.Equal(1, replay.Workspace.Audits);
            Assert.Equal(1, replay.Workspace.Retentions);
        }
        else
        {
            Assert.Equal(source.Inspect(run.Clone).Commit, RunEstate.Git(run.Clone, "rev-parse", "refs/heads/work/w-1"));
        }
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void SourceReplacementNeverSilentlyFallsBackToGitAndExplicitWorkspaceOverridesRemainAvailable()
    {
        var registry = new ProviderRegistry();
        var git = new GitCommandAdapter();
        registry.Register(git);
        Assert.Same(git.Workspaces, registry.Workspace("git"));
        registry.Register(new RecordedSourceAdapter(new Dictionary<string, RepositoryObservation>(StringComparer.Ordinal)));
        Assert.Null(registry.Workspace("git"));
        Assert.Empty(registry.Workspaces);
        var explicitWorkspace = new DirectoryWorkspaces(new string('a', 40));
        registry.Register(explicitWorkspace);
        registry.Register(git);
        Assert.Same(explicitWorkspace, registry.Workspace("git"));
        Assert.Same(explicitWorkspace, Assert.Single(registry.Workspaces));
        registry.RemoveSource("git");
        Assert.Same(explicitWorkspace, registry.Workspace("git"));
    }

    private static ISourceAdapter Source(RunEstate run, bool recorded)
    {
        if (!recorded)
            return new GitCommandAdapter(info => BoundedProcess.Run(info, TimeSpan.FromSeconds(30)), new GitWorktreeWorkspaces(FakeHarness.ScopeCommand()));
        string head = RunEstate.Git(run.Clone, "rev-parse", "HEAD");
        return new DirectorySource(new RecordedSourceAdapter(new Dictionary<string, RepositoryObservation>(StringComparer.Ordinal)
        {
            [Path.GetFullPath(run.Clone)] = new(head, "main", null, head, true, 0, 0, [], [], "2026-10-18T12:00:00Z"),
        }), new DirectoryWorkspaces(head));
    }

    // A second source implementation: recorded observations and an empty directory workspace, with no git execution.
    // It exercises orchestration only and makes no claim to qualify a live repository's scope or isolation.
    private sealed class DirectorySource(RecordedSourceAdapter observations, DirectoryWorkspaces workspace) : ISourceAdapter
    {
        public ProviderDescriptor Descriptor { get; } = new("directory-recorded", ISourceAdapter.Contract, "implemented", ["inspect", "workspaces"], ["Replays empty workspace operations for tests only."]);
        public DirectoryWorkspaces Workspace => workspace;
        public IWorkspaceProvider Workspaces => workspace;
        public bool IsRepository(string path) => observations.IsRepository(path);
        public RepositoryObservation Inspect(string path) => observations.Inspect(path);
    }

    private sealed class DirectoryWorkspaces(string commit) : IWorkspaceProvider
    {
        private readonly Dictionary<string, PreparedWorkspace> branches = new(StringComparer.Ordinal);
        private readonly HashSet<string> audited = new(StringComparer.Ordinal);
        public ProviderDescriptor Descriptor { get; } = new("directory-recorded", IWorkspaceProvider.Contract, "implemented", ["prepare"], ["An empty workspace fixture, not a live scope auditor."]);
        public int Captures { get; private set; }
        public int Audits { get; private set; }
        public int Retentions { get; private set; }

        public PreparedWorkspace Prepare(string clone, string branch, string? baseReference, string directory)
        {
            if (branches.TryGetValue(branch, out PreparedWorkspace? prior)) return prior with { Reused = true };
            Directory.CreateDirectory(directory);
            var workspace = new PreparedWorkspace(WorkspacePaths.Canonical(directory), branch, commit, false);
            branches.Add(branch, workspace);
            return workspace;
        }

        public WorkspaceSnapshot Capture(PreparedWorkspace workspace)
        {
            Captures++;
            return new(new Dictionary<string, string>(StringComparer.Ordinal), Identity: workspace.Path);
        }

        public RunScope CheckScope(PreparedWorkspace workspace, WorkspaceSnapshot baseline, IReadOnlyList<string> permitted)
        {
            Audits++;
            audited.Add(workspace.Path);
            return new(0, 0, []);
        }

        public string? RetainForReview(PreparedWorkspace workspace)
        {
            if (!audited.Contains(workspace.Path)) return "No recorded scope audit is available.";
            Retentions++;
            return null;
        }

        public string? DiscardUnused(PreparedWorkspace workspace, WorkspaceSnapshot? exported, string brief)
        {
            if (workspace.Reused) return "The earlier workspace stays for review.";
            Directory.Delete(workspace.Path, recursive: true);
            branches.Remove(workspace.Branch);
            return null;
        }
    }
}
