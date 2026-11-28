// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.InteropServices;

namespace Vericairn.Packs.Software;

/// <summary>The native harness paths and version response that qualification and the platform stand-ins exercise.</summary>
internal sealed record HarnessPlatformProfile(
    string RuntimeIdentifier, string DefaultConfigurationDirectory, string ConfigurationEnvironment, string CredentialFile,
    string OrdinarySignIn, string VersionPrefix, string VersionSuffix, string Evidence)
{
    internal static HarnessPlatformProfile ClaudeCode { get; } = new(
        RuntimeInformation.RuntimeIdentifier, ".claude", "CLAUDE_CONFIG_DIR", ".credentials.json",
        OperatingSystem.IsMacOS() ? "macos-keychain" : "configuration-file", "", " (Claude Code)",
        OperatingSystem.IsMacOS() ? "FIX-167-operator-probe-2.1.322" : "RUN-02-configuration-file");

    internal static HarnessPlatformProfile Codex { get; } = new(
        RuntimeInformation.RuntimeIdentifier, ".codex", "CODEX_HOME", "auth.json",
        "configuration-file", "codex-cli ", "", "RUN-02-configuration-file");

    internal string? ProvisionedConfiguration(IReadOnlyDictionary<string, string> environment)
    {
        if (environment.TryGetValue(ConfigurationEnvironment, out string? directory)) return directory;
        string? user = environment.GetValueOrDefault("HOME") ?? environment.GetValueOrDefault("USERPROFILE");
        return user is null ? null : Path.Combine(user, DefaultConfigurationDirectory);
    }
}
