// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;
using Vericairn.Records.Representation;

namespace Vericairn.Packs.Software;

/// <summary>
/// The Claude Code launcher (vericairn.launcher.agent/1): Claude Code's own /goal, with the work order's exit question, its checks and
/// its constraints as the condition, in print mode with stream-json output, in auto permission mode within the workspace, and with the
/// vericairn MCP server the worker configured as its only MCP server. Every flag is one claude --help lists.
/// </summary>
public sealed class ClaudeCodeLauncher : IAgentLauncher
{
    private static readonly string[] SetupTokenScopes = ["user:inference"];

    /// <inheritdoc />
    public RunCapabilities ExecutionCapabilities { get; } = new(RunSelection.Scopes, ["low", "medium", "high", "max"], ["anthropic"], ["anthropic-inference"], ["worker-user"], ProviderDefault: true, ClosedEnvironment: true) { Adapters = [RepresentationContract.LocalLossless] };

    /// <inheritdoc />
    public RunPreflight Preflight(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RefuseManagedSettings();
        return ClaudeCodeInventory.Establish(context);
    }

    /// <inheritdoc />
    public RunPreflight Probe(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RefuseManagedSettings();
        return ClaudeCodeInventory.Establish(context, qualifying: true);
    }

    /// <inheritdoc />
    public HarnessExecution? ReadExecution(string line) => HarnessSelection.Read(line, "system", "init");

    /// <inheritdoc />
    public IReadOnlyList<HarnessAction> ReadActions(string line) => HarnessActions.Claude(line);

    /// <inheritdoc />
    public IReadOnlyList<HarnessToolResult> ReadToolResults(string line) => HarnessActions.ClaudeResults(line);

    /// <inheritdoc />
    public RunWatchSettings WatchDefaults => new(NormalTokensPerMinute: 20000);

    /// <summary>The launcher id recorded as the provider of every run it launches.</summary>
    public const string LauncherId = "claude-code";

    /// <inheritdoc />
    public string Launcher => LauncherId;

    /// <inheritdoc />
    public string Limitations => "Claude Code receives --max-budget-usd for USD at the currency threshold below its reservation. This is an estimated-spend soft limit that can overshoot; no native total-token cap is available. Other currencies have no native cap.";

    /// <inheritdoc />
    public string? Harness => "Claude Code";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        "claude-code-cli",
        IAgentLauncher.Contract,
        "implemented",
        ["launch", "observe-usage"],
        [
            "Runs claude -p \"/goal <condition>\" --output-format stream-json --verbose --permission-mode auto --mcp-config <the worker's configuration> --strict-mcp-config in the workspace: Claude Code's own /goal, with the vericairn MCP server as its only MCP server.",
            "Stages an operator-provisioned pro or max sign-in in a fresh configuration for each run and qualification. Accepts the setup-token form with the operator's account type statement, or a provisioned OAuth credentials file.",
            "Watches provisional per-message tokens and settles tokens from the stream's complete final result; it reads no price, so a cap in currency is charged in full.",
            "Excludes user, project and local setting sources and repository customizations; preserves and inventories the built-in goal evaluator. Selects nine built-in tools explicitly and refuses detected managed settings.",
            "From 0.9.4 (RUN-04), a run whose brief permits retrieval adds WebFetch and WebSearch, allowed explicitly and admitted with the public-web route, and the worker retains each result as an untrusted original; no other run gets a web tool.",
            "Shipped baseline: Claude Code 2.1.296. Another exact version needs a current human qualification with matching local probe digests; no version range is inferred. Every required flag is confirmed by help, with strict explicit MCP and two inventory checks. Tested with stand-in executables; no live Claude Code or sign-in qualification.",
        ],
        [new ProviderSetting("stall-seconds", "Event-stream silence limit in seconds; roster and goal limits override it.", false, "600"), new ProviderSetting("command", "The Claude Code executable, or a JSON array with positional entry arguments; leading option flags are not qualified.", false, "claude")]);

    /// <inheritdoc />
    public IReadOnlyList<string> EnvironmentNames => [HarnessPlatformProfile.ClaudeCode.ConfigurationEnvironment];

    /// <inheritdoc />
    public string? Verify(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> environment) => HarnessProcess.Problem(settings, "claude");

    /// <inheritdoc />
    public LaunchContext Prepare(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RefuseManagedSettings();
        HarnessPlatformProfile profile = HarnessPlatformProfile.ClaudeCode;
        string home = RunDirectory.ClaudeConfigurationDirectory(context.McpConfiguration);
        HarnessInventory.FreshHome(home);
        WorkerConfigurationStore.CreateOwnerDirectory(home);
        HarnessSignIn? signIn = StageSignIn(context, profile, Path.Combine(home, profile.CredentialFile), out var setupToken);
        return context with
        {
            Environment = new Dictionary<string, string>(context.Environment, WorkerEnvironment.Names) { [profile.ConfigurationEnvironment] = home },
            SignIn = signIn,
            SetupToken = setupToken,
        };
    }

    private static HarnessSignIn? StageSignIn(LaunchContext context, HarnessPlatformProfile profile, string destination,
        out WorkerConfigurationStore.ClaudeSetupToken? setupToken)
    {
        var store = new WorkerConfigurationStore(context.Environment.GetValueOrDefault(VericairnHome.EnvironmentVariable));
        setupToken = store.ReadClaudeSetupToken(context.Environment);
        if (setupToken is { } provision)
        {
            WriteLogin(destination, new { claudeAiOauth = new
            {
                accessToken = provision.AccessToken, refreshToken = (string?)null, expiresAt = provision.ExpiresAt,
                scopes = SetupTokenScopes, subscriptionType = (string?)null,
            } });
            return new("setup-token", provision.AccountType, "operator-statement");
        }

        string? original = profile.ProvisionedConfiguration(context.Environment);

        // The existing file import copies only the harness login. Settings, plugins and MCP credentials stay behind.
        if (original is null || !File.Exists(Path.Combine(original, profile.CredentialFile))) return null;
        try
        {
            using FileStream source = File.OpenRead(Path.Combine(original, profile.CredentialFile));
            using JsonDocument credentials = JsonDocument.Parse(source);
            if (!credentials.RootElement.TryGetProperty("claudeAiOauth", out JsonElement oauth)) return null;
            string? accountType = oauth.TryGetProperty("subscriptionType", out JsonElement subscription) ? subscription.GetString() : null;
            if (accountType is "team" or "enterprise")
            {
                throw new ProviderRefusedException("Claude Code organization logins can load remote managed settings that this launcher cannot verify; use an unmanaged worker login.");
            }

            WriteLogin(destination, new { claudeAiOauth = oauth });
            return accountType is "pro" or "max" ? new("credentials-file", accountType, "credentials-file") : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new ProviderRefusedException("Claude Code's provisioned login could not be read.");
        }
    }

    private static void WriteLogin<T>(string path, T login)
    {
        using Stream stream = WorkerConfigurationStore.CreateOwnerFile(path);
        JsonSerializer.Serialize(stream, login);
    }

    /// <inheritdoc />
    public HarnessVersionObservation ProbeVersion(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(environment);
        RefuseManagedSettings();
        HarnessPlatformProfile profile = HarnessPlatformProfile.ClaudeCode;
        string scratch = Directory.CreateTempSubdirectory("vericairn-claude-version-").FullName;
        try
        {
            WorkerConfigurationStore.CreateOwnerDirectory(scratch);
            string home = Path.Combine(scratch, RunDirectory.ClaudeConfigurationDirectoryName);
            WorkerConfigurationStore.CreateOwnerDirectory(home);
            Dictionary<string, string> isolated = WorkerEnvironment.Build(environment);
            isolated[profile.ConfigurationEnvironment] = home;
            foreach (string name in RunDirectory.TemporaryVariables) isolated[name] = scratch;
            var context = new LaunchContext(scratch, "", "", "", [], new Dictionary<string, string>(StringComparer.Ordinal), settings, isolated);
            HarnessInventory.CommandPrefix(context, "claude");
            return new(ClaudeCodeInventory.InstalledVersion(context, qualifying: true), ClaudeCodeInventory.Version);
        }
        finally
        {
            WorkspaceCleanup.Delete(scratch);
        }
    }

    /// <inheritdoc />
    public Uri EfficiencyUpstream(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // The generated isolated configuration admits only this built-in provider; no run setting supplies a host.
        return new Uri("https://api.anthropic.com/");
    }

    /// <inheritdoc />
    public LaunchContext ConfigureEfficiencyHook(LaunchContext context, Uri? address)
    {
        ArgumentNullException.ThrowIfNull(context);
        HarnessEfficiency.CheckAddress(address);
        return context with { AdapterAddress = address };
    }

    private static void RefuseManagedSettings()
    {
        string directory = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClaudeCode")
            : OperatingSystem.IsMacOS() ? "/Library/Application Support/ClaudeCode" : "/etc/claude-code";
        RefuseManagedPaths([Path.Combine(directory, "managed-settings.json"), Path.Combine(directory, "managed-settings.d"), Path.Combine(directory, "managed-mcp.json")]);
        if (OperatingSystem.IsMacOS())
        {
            RefuseManagedPaths(["/Library/Managed Preferences/com.anthropic.claudecode.plist",
                Path.Combine("/Library/Managed Preferences", Environment.UserName, "com.anthropic.claudecode.plist")]);
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                {
                    using RegistryKey root = RegistryKey.OpenBaseKey(hive, view);
                    using RegistryKey? policy = root.OpenSubKey(@"SOFTWARE\Policies\ClaudeCode");
                    if (policy is not null)
                    {
                        throw new ProviderRefusedException("Claude Code managed settings cannot be isolated by this launcher; use an unmanaged worker account.");
                    }
                }
            }
        }
    }

    internal static void RefuseManagedPaths(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            try
            {
                _ = File.GetAttributes(path); // Unlike File.Exists, access denied must not mean absent.
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new ProviderRefusedException("Claude Code managed configuration could not be checked; launch was refused.", exception);
            }

            throw new ProviderRefusedException("Claude Code managed configuration cannot be isolated by this launcher; use an unmanaged worker account.");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Command(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        (string model, string effort) = HarnessSelection.Requested(context);
        var selection = new List<string> { "--model", model };
        if (effort != "provider-default") selection.AddRange(["--effort", effort]);
        var limits = new List<string>();
        if (context.Limits is { Currency: long amount, CurrencyUnit: "USD" })
        {
            limits.AddRange(["--max-budget-usd", (amount / 100m).ToString("0.00", CultureInfo.InvariantCulture)]);
        }

        return [.. HarnessProcess.Command(context.Settings, "claude"), "-p", "/goal " + context.Condition, "--output-format", "stream-json", "--verbose", "--permission-mode", "auto", "--mcp-config", context.McpConfiguration, "--strict-mcp-config", "--setting-sources", "", "--settings", ClaudeCodeInventory.SettingsFor(context), "--tools", ClaudeCodeInventory.ToolsFor(context), .. selection, .. limits];
    }

    /// <summary>The stream's final result reports the run's usage: its input, cache and output tokens, read as one total.</summary>
    public HarnessSignal? Read(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!line.TrimStart().StartsWith('{'))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (type.GetString() == "assistant" && root.TryGetProperty("message", out JsonElement message)
            && message.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String && message.TryGetProperty("usage", out _))
        {
            long? provisional = HarnessUsage.Message(message);
            return new HarnessSignal(UsageStarted: true, UsageIncomplete: provisional is null, UsageId: id.GetString(), UsageTokens: provisional);
        }

        if (type.GetString() != "result")
        {
            return null;
        }

        long? tokens = HarnessUsage.Total(root, "input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens", "output_tokens");
        return new HarnessSignal(Tokens: tokens, UsageIncomplete: tokens is null, UsageCompleted: true);
    }
}
