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
    public void RealCodexProbesRejectMergedServersAndAddressCanonicalProjectTrustWithoutStartingServers()
    {
        string? executable = Environment.GetEnvironmentVariable("VERICAIRN_TEST_CODEX");
        Assert.SkipWhen(executable is null, "Set VERICAIRN_TEST_CODEX to run read-only mcp list probes with synthetic configuration.");
        string root = Path.Combine(Path.GetTempPath(), "vericairn-config-" + Ulid.NewUlid().Value);
        string workspace = Path.Combine(root, "workspace.with.dots");
        string home = Path.Combine(root, "home");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(home);
        try
        {
            Git(workspace, "init", "-q");
            workspace = HarnessWorkspace.Canonical(workspace);
            Directory.CreateDirectory(Path.Combine(workspace, ".codex"));
            File.WriteAllText(Path.Combine(workspace, ".codex", "config.toml"), "[mcp_servers.project_ambient]\ncommand=\"nonexistent-project-sentinel\"\n");
            var environment = WorkerEnvironment.Build(WorkerEnvironment.Current(), []);
            environment["CODEX_HOME"] = home;
            var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = executable };
            LaunchContext context = Context(settings) with
            {
                Workspace = workspace, Environment = environment, McpServer = ["nonexistent-approved-sentinel"],
                McpEnvironment = new Dictionary<string, string>(StringComparer.Ordinal),
            };
            string trust = "projects=" + CodexLauncher.Projects(context).Replace("untrusted", "trusted", StringComparison.Ordinal) + "\n";
            File.WriteAllText(Path.Combine(home, "config.toml"), trust + "[mcp_servers.ambient]\ncommand=\"nonexistent-ambient-sentinel\"\n");
            (int code, string baseline, _) = HarnessProcess.Run([executable, "mcp", "list", "--json"], environment, TimeSpan.FromSeconds(30), workspace);
            Assert.Equal(0, code);
            Assert.Contains("project_ambient", baseline, StringComparison.Ordinal);
            Assert.Contains("ambient", baseline, StringComparison.Ordinal);
            Assert.Throws<ProviderRefusedException>(() => CodexLauncher.VerifyMcp(context)); // Extra lower-layer server is refused.
            File.WriteAllText(Path.Combine(home, "config.toml"), trust);
            CodexLauncher.VerifyMcp(context); // Whole projects value suppresses the project layer, even with dots in its canonical key.
            File.WriteAllText(Path.Combine(home, "config.toml"), trust + "[mcp_servers.vericairn.env]\nAMBIENT=\"synthetic\"\n");
            Assert.Throws<ProviderRefusedException>(() => CodexLauncher.VerifyMcp(context)); // Merged fields of the approved name are refused too.

            string runDirectory = Path.Combine(root, "run");
            Directory.CreateDirectory(runDirectory);
            string alias = Path.Combine(root, "workspace-alias");
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(alias, workspace);
            }

            context = context with { Workspace = OperatingSystem.IsWindows() ? workspace : alias, McpConfiguration = Path.Combine(runDirectory, "mcp.json") };
            LaunchContext prepared = new CodexLauncher().Prepare(context);
            Assert.Equal(workspace, prepared.Workspace);
            string configuration = File.ReadAllText(Path.Combine(prepared.Environment["CODEX_HOME"], "config.toml"));
            Assert.Contains("projects=" + CodexLauncher.Projects(prepared), configuration, StringComparison.Ordinal);
            Assert.DoesNotContain("workspace-alias", configuration, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"input_tokens\":1}")]
    [InlineData("{\"input_tokens\":-1,\"output_tokens\":2}")]
    [InlineData("{\"input_tokens\":1.5,\"output_tokens\":2}")]
    [InlineData("{\"input_tokens\":\"1\",\"output_tokens\":2}")]
    [InlineData("{\"input_tokens\":null,\"output_tokens\":2}")]
    [InlineData("{\"input_tokens\":9223372036854775807,\"output_tokens\":1}")]
    [InlineData("{\"input_tokens\":9223372036854775808,\"output_tokens\":1}")]
    public void IncompleteOrInvalidUsageIsNeverATokenSignal(string usage)
    {
        HarnessSignal codex = new CodexLauncher().Read("{\"type\":\"turn.completed\",\"usage\":" + usage + "}")!;
        HarnessSignal claude = new ClaudeCodeLauncher().Read("{\"type\":\"result\",\"usage\":" + usage + "}")!;
        Assert.Null(codex.TokensAdded);
        Assert.True(codex.UsageIncomplete);
        Assert.Null(claude.Tokens);
        Assert.True(claude.UsageIncomplete);
    }

    [Fact]
    public void NativeControlsReceiveTheOperationalAllowanceAndUnsupportedUnitsStaySoft()
    {
        using var cap = System.Text.Json.JsonDocument.Parse("""{"wallMinutes":1,"tokens":1001,"currency":{"amount":1001,"unit":"USD"}}""");
        RunLimits limits = RunLimits.For(cap.RootElement, WorkerOptions.Default);
        Assert.Equal(TimeSpan.FromSeconds(48), limits.WallTime);
        Assert.Equal(900, limits.Tokens);
        Assert.Equal(900, limits.Currency);
        LaunchContext context = Context(NoSettings) with { Limits = limits };
        Assert.Equal(["--max-budget-usd", "9.00"], new ClaudeCodeLauncher().Command(context).TakeLast(2));
        Assert.DoesNotContain("--max-budget-usd", new ClaudeCodeLauncher().Command(context with { Limits = limits with { CurrencyUnit = "EUR" } }));
        Assert.Contains("features.rollout_budget.limit_tokens=900", new CodexLauncher().Command(context));
        Assert.Contains("No native token or currency limit", ((IAgentLauncher)new GenericLauncher()).Limitations, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteZeroUsageIsKnownAndClaudeRequiresItsCacheCounts()
    {
        var launcher = new ClaudeCodeLauncher();
        Assert.True(launcher.Read("""{"type":"result","usage":{"input_tokens":1,"output_tokens":1}}""")!.UsageIncomplete);
        Assert.Equal(0, launcher.Read("""{"type":"result","usage":{"input_tokens":0,"output_tokens":0,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}""")!.Tokens);
        Assert.Null(launcher.Read("""{"type":"result","usage":{"input_tokens":9223372036854775807,"output_tokens":0,"cache_creation_input_tokens":1,"cache_read_input_tokens":0}}""")!.Tokens);
    }

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
        Assert.Equal(["claude", "-p", "/goal Is it done? Checks: it builds.", "--output-format", "stream-json", "--verbose", "--permission-mode", "auto", "--mcp-config", "/runs/r/mcp.json", "--strict-mcp-config", "--setting-sources", "", "--settings", "{\"disableAllHooks\":true,\"enabledPlugins\":{}}"], launcher.Command(context));
        Assert.Equal(["/opt/claude/bin/claude", "--debug", "-p"], launcher.Command(Context(new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = """["/opt/claude/bin/claude","--debug"]""" })).Take(3));
        Assert.Null(launcher.Verify(NoSettings, NoSettings));
        Assert.Contains("JSON array of one or more non-empty strings", launcher.Verify(new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = "[]" }, NoSettings), StringComparison.Ordinal);

        Assert.Equal(1734, launcher.Read("""{"type":"result","subtype":"success","usage":{"input_tokens":1000,"cache_creation_input_tokens":200,"cache_read_input_tokens":300,"output_tokens":234},"total_cost_usd":0.01}""")!.Tokens);
        Assert.Null(launcher.Read("""{"type":"assistant","message":{"usage":{"input_tokens":5,"output_tokens":1}}}"""));
        Assert.Null(launcher.Read("Some text the harness printed."));
        Assert.True(launcher.Read("""{"type":"result","subtype":"success"}""")!.UsageIncomplete);
    }

    [Fact]
    public void CodexRunsItsOwnGoalThroughExecWithTheMcpServerOnItsCommandLine()
    {
        var launcher = new CodexLauncher();
        LaunchContext context = Context(NoSettings) with { McpServer = ["C:\\Program Files\\vericairn.exe", "--estate", "/estates/the \"widget\"", "mcp", "serve", "--as", "01M4TRNE1GYEK4P8MXQAB3C8Q4"], McpEnvironment = new Dictionary<string, string>(StringComparer.Ordinal) { ["VERICAIRN_HOME"] = "/home/me/.config/vericairn" } };
        IReadOnlyList<string> command = launcher.Command(context);
        Assert.Equal(["codex", "exec", "--json", "--cd", "/work/widget", "--sandbox", "workspace-write", "-c"], command.Take(8));
        Assert.StartsWith("mcp_servers={vericairn={command=", command[8], StringComparison.Ordinal);
        Assert.Contains("env={\"VERICAIRN_HOME\"=\"/home/me/.config/vericairn\"}", command[8], StringComparison.Ordinal);
        Assert.Equal("projects={\"/work/widget\"={trust_level=\"untrusted\"}}", command[10]);
        Assert.Equal("/goal Is it done? Checks: it builds.", command[^1]);
        Assert.Equal(800 + 150, launcher.Read("""{"type":"turn.completed","usage":{"input_tokens":800,"cached_input_tokens":100,"output_tokens":150}}""")!.TokensAdded);
        Assert.True(launcher.Read("""{"type":"turn.started"}""")!.UsageStarted);
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
