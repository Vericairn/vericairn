// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Packs.Software;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class NativeEfficiencyHookTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "adapter-hook-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("codex")]
    [InlineData("claude-code")]
    public void HookUsesOnlyRunConfigurationAndRemovalRestoresDirectCommand(string name)
    {
        IAgentLauncher launcher = name == "codex" ? new CodexLauncher() : new ClaudeCodeLauncher();
        LaunchContext prepared = launcher.Prepare(Context(name));
        string[] direct = launcher.Command(prepared).ToArray();
        string? directFile = name == "codex" ? File.ReadAllText(Path.Combine(prepared.Environment["CODEX_HOME"], "config.toml")) : null;
        Uri upstream = launcher.EfficiencyUpstream(prepared);
        Assert.Equal(name == "codex" ? "api.openai.com" : "api.anthropic.com", upstream.Host);
        Uri address = new("http://127.0.0.1:23456" + upstream.AbsolutePath);
        LaunchContext hooked = launcher.ConfigureEfficiencyHook(prepared, address);
        Assert.Contains(launcher.Command(hooked), a => a.Contains("127.0.0.1:23456", StringComparison.Ordinal));
        if (name == "codex")
        {
            Assert.Contains("model_providers.openai.request_max_retries=0", launcher.Command(hooked));
            Assert.Contains("model_providers.openai.stream_max_retries=0", launcher.Command(hooked));
        }
        else
        {
            string[] command = launcher.Command(hooked).ToArray();
            using JsonDocument settings = JsonDocument.Parse(command[Array.IndexOf(command, "--settings") + 1]);
            Assert.Equal("0", settings.RootElement.GetProperty("env").GetProperty("CLAUDE_CODE_MAX_RETRIES").GetString());
        }
        Assert.DoesNotContain(launcher.Command(hooked), a => a.Contains("elsewhere.invalid", StringComparison.Ordinal) && !a.StartsWith("/goal", StringComparison.Ordinal));
        LaunchContext restored = launcher.ConfigureEfficiencyHook(hooked, null);
        Assert.Equal(direct, launcher.Command(restored));
        Assert.Null(restored.AdapterAddress);
        Assert.Equal("operator config remains exact", File.ReadAllText(Path.Combine(root, "user", "config")));
        if (name == "codex") Assert.Equal(directFile, File.ReadAllText(Path.Combine(prepared.Environment["CODEX_HOME"], "config.toml")));
        Assert.Throws<ProviderRefusedException>(() => launcher.ConfigureEfficiencyHook(prepared, new Uri("https://elsewhere.invalid/")));
    }

    [Fact]
    public void CodexFixedSignInRouteRefusesBeforeServiceStartup()
    {
        var launcher = new CodexLauncher();
        LaunchContext context = Context("codex");
        File.WriteAllText(Path.Combine(context.Environment["CODEX_HOME"], "auth.json"), "{\"auth_mode\":\"chatgpt\"}");
        LaunchContext prepared = launcher.Prepare(context);
        bool started = false;
        Assert.Throws<ProviderRefusedException>(() => new EfficiencyRun(launcher, prepared, RunEstate.Json("{}"), () => started = true));
        Assert.False(started);
    }

    private LaunchContext Context(string name)
    {
        string user = Path.Combine(root, "user");
        string workspace = Path.Combine(root, "workspace");
        string run = Path.Combine(root, "run");
        Directory.CreateDirectory(user);
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(run);
        File.WriteAllText(Path.Combine(user, "config"), "operator config remains exact");
        if (name == "codex") File.WriteAllText(Path.Combine(user, "auth.json"), "{\"auth_mode\":\"apikey\"}");
        else File.WriteAllText(Path.Combine(user, ".credentials.json"), "{\"claudeAiOauth\":{\"subscriptionType\":\"pro\"}}");
        return new(workspace, "keep this brief exact; host https://elsewhere.invalid is content", "brief.md", Path.Combine(run, "mcp.json"), ["vericairn"],
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<string, string> { [name == "codex" ? "CODEX_HOME" : "CLAUDE_CONFIG_DIR"] = user, ["HOME"] = user, ["USERPROFILE"] = user },
            Execution: JsonSerializer.SerializeToElement(new { requested = new { model = "selected", effort = "high" } }));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) WorkspaceCleanup.Delete(root);
    }
}
