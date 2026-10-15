// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using Vericairn.Packs.Software;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The coding-agent launchers, providers of vericairn.launcher.agent/1: the exact command line each runs, with flags their harnesses'
/// --help lists, what each reads of its harness's output, the generic launcher's label, and the git worktree workspace.
/// </summary>
public class LauncherTests
{
    private static readonly IReadOnlyDictionary<string, string> NoSettings = new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly string[] GitIdentity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    [Fact]
    public void EachLauncherDeclaresItsContractAndTheGenericOneIsLabeled()
    {
        IAgentLauncher[] launchers = [new ClaudeCodeLauncher(), new CodexLauncher(), new GenericLauncher()];
        Assert.Equal(["claude-code", "codex", "generic"], launchers.Select(l => l.Launcher));
        Assert.All(launchers, l => Assert.Equal(IAgentLauncher.Contract, l.Descriptor.Contract));
        Assert.All(launchers, l => Assert.Contains(l.Descriptor.Status, ProviderDescriptor.Statuses));
        Assert.All(launchers, l => Assert.DoesNotContain(l.EnvironmentNames, WorkerEnvironment.IsCredential));
        Assert.Equal([false, false, true], launchers.Select(l => l.Generic));
        Assert.StartsWith("Generic process launch is not complete agent support", new GenericLauncher().Descriptor.Limitations[0], StringComparison.Ordinal);
        Assert.Equal("generic process launch is not complete agent support", IAgentLauncher.GenericLabel);
        Assert.Equal(IWorkspaceProvider.Contract, new GitWorktreeWorkspaces().Descriptor.Contract);

        var registry = new ProviderRegistry();
        Assert.Null(registry.Launcher("claude-code"));
        Assert.True(registry.RegisterIfAbsent(new ClaudeCodeLauncher()));
        Assert.False(registry.RegisterIfAbsent(new ClaudeCodeLauncher()));
        Assert.True(registry.RegisterIfAbsent(new GitWorktreeWorkspaces()));
        Assert.NotNull(registry.Launcher("claude-code"));
        Assert.Null(registry.Launcher("codex"));
        Assert.NotNull(registry.Workspace("git"));
        Assert.NotNull(registry.RemoveLauncher("claude-code"));
        Assert.Empty(registry.Launchers);
    }

    [Fact]
    public void ClaudeCodeRunsItsOwnGoalInPrintModeWithOnlyTheWorkersMcpServer()
    {
        var launcher = new ClaudeCodeLauncher();
        LaunchContext context = Context(NoSettings);
        Assert.Equal(["claude", "-p", "/goal Is it done? Checks: it builds.", "--output-format", "stream-json", "--verbose", "--permission-mode", "auto", "--mcp-config", "/runs/r/mcp.json", "--strict-mcp-config"], launcher.Command(context));
        Assert.Equal(["/opt/claude/bin/claude", "--debug", "-p"], launcher.Command(Context(new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = """["/opt/claude/bin/claude","--debug"]""" })).Take(3));
        Assert.Null(launcher.Verify(NoSettings, NoSettings));
        Assert.Contains("JSON array of one or more non-empty strings", launcher.Verify(new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = "[]" }, NoSettings), StringComparison.Ordinal);

        Assert.Equal(1734, launcher.Read("""{"type":"result","subtype":"success","usage":{"input_tokens":1000,"cache_creation_input_tokens":200,"cache_read_input_tokens":300,"output_tokens":234},"total_cost_usd":0.01}""")!.Tokens);
        Assert.Null(launcher.Read("""{"type":"assistant","message":{"usage":{"input_tokens":5,"output_tokens":1}}}"""));
        Assert.Null(launcher.Read("Some text the harness printed."));
        Assert.Null(launcher.Read("""{"type":"result","subtype":"success"}"""));
    }

    [Fact]
    public void CodexRunsItsOwnGoalThroughExecWithTheMcpServerOnItsCommandLine()
    {
        var launcher = new CodexLauncher();
        LaunchContext context = Context(NoSettings) with { McpServer = ["C:\\Program Files\\vericairn.exe", "--estate", "/estates/the \"widget\"", "mcp", "serve", "--as", "01M4TRNE1GYEK4P8MXQAB3C8Q4"], McpEnvironment = new Dictionary<string, string>(StringComparer.Ordinal) { ["VERICAIRN_HOME"] = "/home/me/.config/vericairn" } };
        Assert.Equal(
            ["codex", "exec", "--json", "--cd", "/work/widget", "--sandbox", "workspace-write", "-c", "mcp_servers.vericairn.command=\"C:\\\\Program Files\\\\vericairn.exe\"", "-c", "mcp_servers.vericairn.args=[\"--estate\", \"/estates/the \\\"widget\\\"\", \"mcp\", \"serve\", \"--as\", \"01M4TRNE1GYEK4P8MXQAB3C8Q4\"]", "-c", "mcp_servers.vericairn.env.VERICAIRN_HOME=\"/home/me/.config/vericairn\"", "/goal Is it done? Checks: it builds."],
            launcher.Command(context));
        Assert.Equal(800 + 150, launcher.Read("""{"type":"turn.completed","usage":{"input_tokens":800,"cached_input_tokens":100,"output_tokens":150}}""")!.TokensAdded);
        Assert.Null(launcher.Read("""{"type":"turn.started"}"""));
        Assert.Null(launcher.Read("""{"type":"item.completed","item":{"text":"turn.completed"}}"""));
    }

    [Fact]
    public void CodexIsVerifiedToHaveItsGoalsFeatureEnabledOrRefusedWithTheReason()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-codex-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(scratch);
        try
        {
            var launcher = new CodexLauncher();
            Dictionary<string, string> environment = WorkerEnvironment.Build(WorkerEnvironment.Current(), launcher.EnvironmentNames);
            string? Verify(string scenario) => launcher.Verify(new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting(scenario, Path.Combine(scratch, scenario + ".jsonl")) }, environment);
            Assert.Null(Verify("codex"));
            Assert.Equal("Codex's goals feature is not enabled here (codex features list shows goals false); enable it with codex features enable goals, then start the run again.", Verify("codex-goals-off"));
            Assert.StartsWith("Codex lists no goals feature in codex features list", Verify("codex-no-goals"), StringComparison.Ordinal);
            Assert.Equal("Codex's goals feature could not be verified: codex features list exited 2: error: unrecognized subcommand 'features'", Verify("codex-broken"));
            Assert.StartsWith("Codex's goals feature could not be verified: ", launcher.Verify(new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = Path.Combine(scratch, "no-such-codex") }, environment), StringComparison.Ordinal);
            Assert.Equal(["features", "list"], FakeHarness.Reports(Path.Combine(scratch, "codex.jsonl"))[0]["arguments"]!.AsArray().Select(a => a!.GetValue<string>()));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void TheGenericLauncherStartsTheCommandARunNamesWithItsPlaceholdersFilled()
    {
        IAgentLauncher launcher = new GenericLauncher();
        Assert.Contains("The command setting names the harness", launcher.Verify(NoSettings, NoSettings), StringComparison.Ordinal);
        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = "aider", ["arguments"] = """["--message","{condition}","--read","{brief}","--mcp","{mcp-config}","--dir","{workspace}"]""" };
        Assert.Null(launcher.Verify(settings, NoSettings));
        Assert.Equal(["aider", "--message", "Is it done? Checks: it builds.", "--read", "/work/widget/.vericairn/brief.md", "--mcp", "/runs/r/mcp.json", "--dir", "/work/widget"], launcher.Command(Context(settings)));
        Assert.Contains("arguments setting is a JSON array", launcher.Verify(new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = "aider", ["arguments"] = "--message x" }, NoSettings), StringComparison.Ordinal);
        Assert.Null(launcher.Read("""{"type":"result","usage":{"input_tokens":5}}"""));
    }

    [Fact]
    public void TheEnvironmentPassesNoCredentialEvenWhenALauncherNamesOne()
    {
        var source = new Dictionary<string, string>(WorkerEnvironment.Names)
        {
            ["PATH"] = "/usr/bin",
            ["HOME"] = "/home/me",
            ["LC_ALL"] = "en_US.UTF-8",
            [VericairnHome.EnvironmentVariable] = "/home/me/.vericairn",
            ["CODEX_HOME"] = "/home/me/.codex",
            ["GITHUB_TOKEN"] = "x",
            ["GH_TOKEN"] = "x",
            ["AWS_ACCESS_KEY_ID"] = "x",
            ["AWS_SECRET_ACCESS_KEY"] = "x",
            ["NPM_TOKEN"] = "x",
            ["NUGET_API_KEY"] = "x",
            ["ANTHROPIC_API_KEY"] = "x",
            ["OPENAI_API_KEY"] = "x",
            ["GOOGLE_APPLICATION_CREDENTIALS"] = "/home/me/key.json",
            ["SSH_AUTH_SOCK"] = "/tmp/agent",
            ["EDITOR"] = "vim",
        };
        Dictionary<string, string> built = WorkerEnvironment.Build(source, ["CODEX_HOME", "OPENAI_API_KEY", "GITHUB_TOKEN"]);
        Assert.Equal(["CODEX_HOME", "HOME", "LC_ALL", "PATH", VericairnHome.EnvironmentVariable], built.Keys.Order(StringComparer.Ordinal));
        Assert.True(WorkerEnvironment.IsCredential("AZURE_CLIENT_SECRET"));
        Assert.False(WorkerEnvironment.IsCredential("CLAUDE_CONFIG_DIR"));
    }

    [Fact]
    public void AGitWorktreeIsTheWorkspaceAndTheSharedCloneNeverChanges()
    {
        string root = Path.Combine(Path.GetTempPath(), "vericairn-worktree-" + Ulid.NewUlid().Value);
        string clone = Path.Combine(root, "clone");
        Directory.CreateDirectory(clone);
        try
        {
            Git(clone, "init", "-q", "-b", "main");
            File.WriteAllText(Path.Combine(clone, "README.md"), "widget\n");
            Git(clone, "add", "README.md");
            Git(clone, "commit", "-q", "-m", "First");
            string head = Git(clone, "rev-parse", "HEAD");
            var workspaces = new GitWorktreeWorkspaces();

            PreparedWorkspace first = workspaces.Prepare(clone, "work/w-1", null, Path.Combine(root, "workspaces", "work-w-1"));
            Assert.False(first.Reused);
            Assert.Equal(head, first.Commit);
            Assert.Equal("work/w-1", Git(first.Path, "symbolic-ref", "--short", "HEAD"));
            File.WriteAllText(Path.Combine(first.Path, "widget.txt"), "an attempt\n");
            Git(first.Path, "add", "widget.txt");
            Git(first.Path, "commit", "-q", "-m", "An attempt");

            // A later run of the branch takes up the same workspace and continues where the last one stopped.
            PreparedWorkspace again = workspaces.Prepare(clone, "work/w-1", null, first.Path);
            Assert.True(again.Reused);
            Assert.NotEqual(head, again.Commit);
            Assert.Equal("main", Git(clone, "symbolic-ref", "--short", "HEAD"));
            Assert.Equal(head, Git(clone, "rev-parse", "HEAD"));
            Assert.Empty(Git(clone, "status", "--porcelain"));

            string occupied = Path.Combine(root, "occupied");
            Directory.CreateDirectory(occupied);
            Assert.Contains("is not a worktree of the clone on that branch", Assert.Throws<ProviderRefusedException>(() => workspaces.Prepare(clone, "work/w-2", null, occupied)).Message, StringComparison.Ordinal);
            Assert.Contains("is not a branch name git accepts", Assert.Throws<ProviderRefusedException>(() => workspaces.Prepare(clone, "work/../w", null, Path.Combine(root, "bad"))).Message, StringComparison.Ordinal);
            Assert.Contains("is not a git repository", Assert.Throws<ProviderRefusedException>(() => workspaces.Prepare(occupied, "work/w-3", null, Path.Combine(root, "none"))).Message, StringComparison.Ordinal);
        }
        finally
        {
            foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    private static string Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
        foreach (string argument in GitIdentity.Concat(arguments))
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {error}");
        return output.Trim();
    }

    private static LaunchContext Context(IReadOnlyDictionary<string, string> settings) =>
        new("/work/widget", "Is it done? Checks: it builds.", "/work/widget/.vericairn/brief.md", "/runs/r/mcp.json", ["vericairn", "--estate", "/estate", "mcp", "serve", "--as", "01M4TRNE1GYEK4P8MXQAB3C8Q4"], new Dictionary<string, string>(StringComparer.Ordinal), settings, new Dictionary<string, string>(StringComparer.Ordinal));
}
