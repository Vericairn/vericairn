// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Collections;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Workers;

/// <summary>
/// The environment a launched harness receives, built from an allowlist so that no ambient credential reaches the agent (the estate's
/// design/goals.md §§ 7 and 10). A brief names no credential, so none passes: no GitHub, cloud, package-registry or model-provider
/// token, whatever the worker's own environment holds. What passes is what a process needs to run as its user (the path, the home
/// and temporary directories, the locale and the platform's own variables), what Vericairn's MCP server needs to find the agent's key
/// and runtime, and the harness's own configuration variables its launcher names, never one whose name marks a credential. The
/// harness signs in with its own login, kept under the user's home; files of the operating-system user stay readable to it, which
/// only running the worker as another user changes.
/// </summary>
public static class WorkerEnvironment
{
    /// <summary>The variables that pass when the worker's environment holds them.</summary>
    public static readonly ImmutableArray<string> Allowed =
    [
        "PATH", "HOME", "USER", "USERNAME", "LOGNAME", "SHELL", "LANG", "LANGUAGE", "TZ", "TERM", "TMPDIR", "TEMP", "TMP",
        "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME", "XDG_STATE_HOME", "XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS",
        "SystemRoot", "SystemDrive", "windir", "ComSpec", "PATHEXT", "APPDATA", "LOCALAPPDATA", "USERPROFILE", "HOMEDRIVE", "HOMEPATH",
        "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "CommonProgramFiles", "CommonProgramFiles(x86)", "CommonProgramW6432",
        "PUBLIC", "ALLUSERSPROFILE", "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "OS",
        "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64", "DOTNET_ROOT_X86",
        VericairnHome.EnvironmentVariable, SecureEnclaveCustody.HelperVariable,
    ];

    /// <summary>The variables Vericairn's MCP server needs, which the worker also passes it through the MCP configuration.</summary>
    public static readonly ImmutableArray<string> McpServer = [VericairnHome.EnvironmentVariable, SecureEnclaveCustody.HelperVariable, "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64", "DOTNET_ROOT_X86"];

    private static readonly string[] CredentialMarks = ["TOKEN", "SECRET", "PASSWORD", "PASSWD", "CREDENTIAL", "API_KEY", "APIKEY", "PRIVATE_KEY", "ACCESS_KEY", "AUTH"];

    private static readonly string[] CredentialPrefixes = ["AWS_", "AZURE_", "GOOGLE_", "GCP_", "GCLOUD_", "GH_", "GITHUB_", "GITLAB_", "NPM_", "NUGET_", "PYPI_", "CARGO_REGISTRY", "DOCKER_", "ANTHROPIC_", "OPENAI_", "CODEX_API", "SSH_AUTH"];

    private static readonly string[] LoaderNames = ["BASH_ENV", "ENV", "ZDOTDIR", "NODE_OPTIONS", "NODE_PATH", "PYTHONPATH", "PYTHONHOME", "RUBYOPT", "RUBYLIB", "PERL5OPT", "PERL5LIB", "JAVA_TOOL_OPTIONS", "JDK_JAVA_OPTIONS", "CLASSPATH", "DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE", "COMPLUS_ENABLEPROFILING", "CORECLR_ENABLE_PROFILING", "CORECLR_PROFILER", "CORECLR_PROFILER_PATH"];

    /// <summary>Ambient load directives are not configuration grants: they can execute undeclared helpers before preflight.</summary>
    public static bool IsLoadDirective(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string upper = name.ToUpperInvariant();
        return upper.StartsWith("LD_", StringComparison.Ordinal) || upper.StartsWith("DYLD_", StringComparison.Ordinal)
            || upper.StartsWith("GIT_CONFIG", StringComparison.Ordinal) || upper.StartsWith("COMPLUS_", StringComparison.Ordinal)
            || upper.StartsWith("CORECLR_", StringComparison.Ordinal) || LoaderNames.Contains(upper, StringComparer.Ordinal);
    }

    /// <summary>A portable variable name; values and machine bindings are deliberately not identities.</summary>
    public static bool IsVariableName(string name) => !string.IsNullOrEmpty(name) && name.Length <= 160
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '(' or ')');

    /// <summary>How environment variable names compare on this platform: without case on Windows, exactly elsewhere.</summary>
    public static StringComparer Names => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>True when a variable's name marks a credential or a credential's service; such a variable never passes, even when a launcher names it.</summary>
    public static bool IsCredential(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string upper = name.ToUpperInvariant();
        return CredentialMarks.Any(m => upper.Contains(m, StringComparison.Ordinal)) || CredentialPrefixes.Any(p => upper.StartsWith(p, StringComparison.Ordinal));
    }

    /// <summary>This process's environment, as a source to build from.</summary>
    public static IReadOnlyDictionary<string, string> Current()
    {
        var current = new Dictionary<string, string>(Names);
        foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                current[name] = value;
            }
        }

        return current;
    }

    /// <summary>
    /// The environment a harness starts with: the variables of the source the allowlist names, the locale's LC_ variables, and the
    /// launcher's own configuration variables, minus any whose name marks a credential.
    /// </summary>
    public static Dictionary<string, string> Build(IReadOnlyDictionary<string, string> source, IEnumerable<string>? launcherNames = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var names = new HashSet<string>(Allowed, Names);
        foreach (string name in launcherNames ?? [])
        {
            names.Add(name);
        }

        var environment = new Dictionary<string, string>(Names);
        foreach ((string name, string value) in source)
        {
            if (IsVariableName(name) && (names.Contains(name) || name.StartsWith("LC_", StringComparison.Ordinal)) && !IsCredential(name) && !IsLoadDirective(name))
            {
                environment[name] = value;
            }
        }

        return environment;
    }
}
