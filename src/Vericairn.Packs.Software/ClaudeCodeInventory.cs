// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>Claude Code's explicit built-in tool set and source isolation for one qualified version.</summary>
internal static class ClaudeCodeInventory
{
    internal const string Version = "2.1.296";
    internal const string ToolList = "Bash,Read,Write,Edit,Glob,Grep,TaskOutput,TaskStop,EndConversation";
    // /goal is a built-in evaluator hook: disableAllHooks would also disable the requested run.
    internal const string Settings = "{\"hooks\":{},\"enabledPlugins\":{}}";

    // From 0.9.4 (RUN-04): the built-in web tools a retrieval run adds, and only such a run.
    internal const string RetrievalToolList = "WebFetch,WebSearch";

    internal static string ToolsFor(LaunchContext context) => HarnessInventory.Retrieval(context) ? ToolList + "," + RetrievalToolList : ToolList;

    internal static string SettingsFor(LaunchContext context)
    {
        bool retrieval = HarnessInventory.Retrieval(context);
        if (context.AdapterAddress is null && !retrieval) return Settings;
        var settings = new JsonObject { ["hooks"] = new JsonObject(), ["enabledPlugins"] = new JsonObject() };
        // A retrieval run's web tools are allowed explicitly, so a print-mode run neither prompts nor silently loses them.
        if (retrieval) settings["permissions"] = new JsonObject { ["allow"] = new JsonArray([.. RetrievalToolList.Split(',').Select(t => (JsonNode)t)]) };
        if (context.AdapterAddress is not null)
            settings["env"] = new JsonObject { ["ANTHROPIC_BASE_URL"] = context.AdapterAddress.AbsoluteUri.TrimEnd('/'), ["CLAUDE_CODE_MAX_RETRIES"] = "0" };
        return settings.ToJsonString();
    }

    internal static RunPreflight Establish(LaunchContext context, bool qualifying = false)
    {
        HarnessPlatformProfile profile = HarnessPlatformProfile.ClaudeCode;
        HarnessInventory.CommandPrefix(context, "claude");
        IReadOnlyList<RunComponent> instructions = HarnessConfiguration.Inspect(context, codex: false);
        HarnessSignIn login = Login(context, profile);
        HarnessInventory.McpFile(context);
        IReadOnlyList<string> command = new ClaudeCodeLauncher().Command(context);
        string binding = HarnessInventory.Binding(context, command);
        string version = InstalledVersion(context, qualifying);
        string help = SecretShapes.Secret().Replace(HarnessInventory.Probe(context, "claude", "--help"), "<secret>");
        HarnessInventory.Flags("claude", help, "-p", "--print", "--output-format", "--verbose", "--permission-mode", "--mcp-config",
            "--strict-mcp-config", "--setting-sources", "--settings", "--tools", "--model", "--effort", "--max-budget-usd");
        if (Login(context, profile) != login)
            throw new ProviderRefusedException("Claude Code's provisioned login changed during the probes.");
        string configuration = HarnessInventory.Digest(new
        {
            qualification = "run-02-claude-code-v1", version, tools = ToolList, settings = Settings, settingSources = "none",
            efficiency = context.AdapterAddress is null ? "disabled" : "local-lossless-loopback",
            strictMcp = true, home = "run-only-login", goal = "built-in-evaluator", help = HarnessInventory.Digest(help),
            arguments = HarnessInventory.Arguments(context, "claude"),
            loginPresent = true,
        });
        // Qualification is of the harness, not the run: the probes keep the base configuration, and a retrieval run's components carry
        // a configuration that names its web tools and their explicit permission.
        bool retrieval = HarnessInventory.Retrieval(context);
        string runConfiguration = retrieval ? HarnessInventory.Digest(new { configuration, retrieval = RetrievalAdmission.Destination, tools = ToolsFor(context), settings = SettingsFor(context) }) : configuration;
        List<RunComponent> components = HarnessInventory.Components(context, "claude", "claude-code", version, runConfiguration, ToolList.Split(','), profile.ConfigurationEnvironment, "anthropic-inference",
            retrieval ? RetrievalToolList.Split(',') : null);
        components.Add(new("claude-code-setting-sources-none", "configuration", "none", HarnessInventory.Digest("none"), runConfiguration, [], ["local"], ["claude-code-configuration"]));
        components.Add(new("claude-code-goal-evaluator", "hook", version, HarnessInventory.Digest(new { version, command = "goal", settings = Settings }), runConfiguration,
            [], ["anthropic-inference"], ["claude-code"]));
        HarnessInventory.Recheck(context, command, binding, instructions, codex: false);
        var probes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["version"] = HarnessInventory.Digest(profile.VersionPrefix + version + profile.VersionSuffix),
            ["flags"] = HarnessInventory.Digest(help),
            ["tools"] = HarnessInventory.Digest(new { tools = ToolList, goal = "built-in-evaluator", binary = HarnessInventory.Binary(context, HarnessProcess.Command(context.Settings, "claude")) }),
            ["configuration"] = login.Form == "setup-token"
                ? HarnessInventory.Digest(new { configuration, platform = profile.RuntimeIdentifier, profile = profile.Evidence })
                : HarnessInventory.Digest(new { configuration, platform = profile.RuntimeIdentifier }),
            ["login"] = login.Form == "setup-token"
                ? HarnessInventory.Digest(new { boundary = "provisioned-unmanaged-oauth-only", login.Form, login.AccountType, login.AccountTypeSource })
                : HarnessInventory.Digest(new { boundary = "provisioned-unmanaged-oauth-only", subscription = login.AccountType }),
            ["mcp"] = HarnessInventory.McpEvidence(context, "strict-explicit-config-help-confirmed"),
        };
        return new([.. components, .. instructions], true, true, version, LocalBindingHash: binding, Probes: new(Version, probes)) { SignIn = login };
    }

    internal static string InstalledVersion(LaunchContext context, bool qualifying)
    {
        HarnessPlatformProfile profile = HarnessPlatformProfile.ClaudeCode;
        return HarnessInventory.Version(context, "claude", profile.VersionPrefix + Version + profile.VersionSuffix, Version, qualifying);
    }

    private static HarnessSignIn Login(LaunchContext context, HarnessPlatformProfile profile)
    {
        string home = HarnessInventory.Home(context, profile.ConfigurationEnvironment, RunDirectory.ClaudeConfigurationDirectoryName, profile.CredentialFile);
        try
        {
            using FileStream source = File.OpenRead(Path.Combine(home, profile.CredentialFile));
            using JsonDocument login = JsonDocument.Parse(source);
            JsonElement oauth = login.RootElement.GetProperty("claudeAiOauth");
            if (login.RootElement.EnumerateObject().Count() == 1 && context.SignIn is { } signIn
                && WorkerConfigurationStore.IsClaudeAccountType(signIn.AccountType)
                && (signIn.Form == "setup-token" ? IsSetupLogin(oauth) : oauth.GetProperty("subscriptionType").GetString() == signIn.AccountType))
                return signIn;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new ProviderRefusedException("Claude Code needs a readable provisioned unmanaged login; run vericairn worker configure --launcher claude-code to import a setup token.");
        }
        throw new ProviderRefusedException("Claude Code's provisioned login does not establish an unmanaged account; managed accounts are not qualified.");
    }

    private static bool IsSetupLogin(JsonElement oauth) =>
        HarnessInventory.OnlyProperties(oauth, "accessToken", "refreshToken", "expiresAt", "scopes", "subscriptionType")
        && WorkerConfigurationStore.IsClaudeSetupToken(oauth.GetProperty("accessToken").GetString())
        && oauth.GetProperty("refreshToken").ValueKind == JsonValueKind.Null
        && oauth.GetProperty("expiresAt").TryGetInt64(out long expiry) && expiry > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        && oauth.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()).SequenceEqual(["user:inference"])
        && oauth.GetProperty("subscriptionType").ValueKind == JsonValueKind.Null;
}
