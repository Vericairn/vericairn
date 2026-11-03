// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Identity;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>A machine-local setup executable and arguments, never included in a run record or passed to a harness.</summary>
public sealed record WorkspaceSetupConfiguration
{
    /// <summary>The executable followed by its arguments. An empty list explicitly disables setup.</summary>
    public IReadOnlyList<string> Command { get; init => field = OptionCollections.List(value); } = [];

    /// <summary>The deadline for the complete setup process boundary, in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 600;
}

public sealed partial class WorkerConfigurationStore
{
    /// <summary>Reads repository setup in preference to launcher setup. Absence means no setup; malformed configuration refuses.</summary>
    public WorkspaceSetupConfiguration? ReadSetup(string launcher, string repository) => ReadSetupFile(SetupPath(null, repository)) ?? ReadSetupFile(SetupPath(launcher, null));

    /// <summary>Replaces setup for exactly one launcher or repository, outside repositories and estates.</summary>
    public void WriteSetup(string? launcher, string? repository, WorkspaceSetupConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateSetup(configuration);
        WriteFile(SetupPath(launcher, repository), configuration);
    }

    private string SetupPath(string? launcher, string? repository)
    {
        if ((launcher is null) == (repository is null) || repository is not null && !Ulid.IsValid(repository)
            || launcher is not null && (!ExecutionPolicy.IsIdentity(launcher) || launcher.Contains('.', StringComparison.Ordinal)))
            throw new ProviderRefusedException("Setup requires exactly one launcher identity or repository resource ULID.");
        return ConfigurationPath(repository is null ? "setup-launcher-" + launcher + ".json" : "setup-repository-" + repository + ".json");
    }

    private static WorkspaceSetupConfiguration? ReadSetupFile(string path)
    {
        try
        {
            RefuseLink(path);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 65536) throw new JsonException();
            var configuration = JsonSerializer.Deserialize<WorkspaceSetupConfiguration>(File.ReadAllBytes(path), Json) ?? throw new JsonException();
            ValidateSetup(configuration);
            return configuration;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new ProviderRefusedException("The machine-local workspace setup configuration is unreadable or invalid; replace it with vericairn worker configure --setup-command. No fallback was used.", exception);
        }
    }

    private static void ValidateSetup(WorkspaceSetupConfiguration configuration)
    {
        if (configuration.TimeoutSeconds is < 1 or > 3600 || configuration.Command.Count > 128
            || configuration.Command.Any(a => a is null || a.Contains('\0', StringComparison.Ordinal))
            || configuration.Command.Sum(a => (long)a.Length) > 32768
            || configuration.Command.Count > 0 && string.IsNullOrWhiteSpace(configuration.Command[0]))
            throw new ProviderRefusedException("Setup requires an executable and arguments without NUL characters, at most 128 arguments and 32768 characters, and a deadline from 1 through 3600 seconds. An empty command disables setup.");
    }
}
