// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Identity;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>
/// A machine-local command the worker runs in a run's workspace before dispatch, as the person and outside the harness's sandbox, for
/// one launcher or one repository. It is never recorded and never reaches a harness; only its outcome enters the run's intent.
/// </summary>
public sealed record WorkspaceSetupConfiguration
{
    /// <summary>The longest deadline, so that setup and the rest of a run's start fit within the fifteen minutes a client observes it.</summary>
    public const int MaximumTimeoutSeconds = 600;

    /// <summary>The deadline when none is given.</summary>
    public const int DefaultTimeoutSeconds = 300;

    /// <summary>The executable followed by its arguments, copied on initialization.</summary>
    public IReadOnlyList<string> Command { get; init => field = OptionCollections.List(value); } = [];

    /// <summary>The deadline for the command and every process it starts, in seconds.</summary>
    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;
}

/// <summary>A configured setup command and what it applies to: a launcher or a repository resource, never both.</summary>
/// <param name="Launcher">The launcher whose runs it prepares, or null.</param>
/// <param name="Repository">The repository resource whose runs it prepares, or null.</param>
/// <param name="Setup">The configuration, or null when its file cannot be read.</param>
/// <param name="Problem">Why its file cannot be read, when it cannot.</param>
public sealed record WorkspaceSetupEntry(string? Launcher, string? Repository, WorkspaceSetupConfiguration? Setup, string? Problem = null);

public sealed partial class WorkerConfigurationStore
{
    private const string SetupPrefix = "setup-";

    /// <summary>
    /// The setup a run of this launcher in this repository uses: the first of the repository's identities with one (a resource's
    /// revisions share its setup), else the launcher's; null when neither has one. An unreadable file refuses and never falls back.
    /// </summary>
    public WorkspaceSetupConfiguration? ReadSetup(string launcher, IEnumerable<string> repositories)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(repositories);
        foreach (string repository in repositories.Where(Ulid.IsValid))
        {
            if (ReadSetupFile(SetupPath(null, repository)) is WorkspaceSetupConfiguration configured) return configured;
        }

        // A launcher whose name no setup file can carry has none.
        return LauncherName().IsMatch(launcher) ? ReadSetupFile(SetupPath(launcher, null)) : null;
    }

    /// <summary>Every setup configured on this machine, launchers before repositories, each with its problem when unreadable.</summary>
    public IReadOnlyList<WorkspaceSetupEntry> ListSetup()
    {
        string folder = Path.GetDirectoryName(ConfigurationPath(SetupPrefix + "list"))!;
        if (!Directory.Exists(folder)) return [];
        var entries = new List<WorkspaceSetupEntry>();
        foreach (string file in Directory.EnumerateFiles(folder, SetupPrefix + "*.json").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file)[SetupPrefix.Length..];
            string? launcher = name.StartsWith("launcher-", StringComparison.Ordinal) ? name["launcher-".Length..] : null;
            string? repository = name.StartsWith("repository-", StringComparison.Ordinal) ? name["repository-".Length..] : null;
            if (launcher is null ? !Ulid.IsValid(repository) : !LauncherName().IsMatch(launcher)) continue;
            try { entries.Add(new(launcher, repository, ReadSetupFile(file))); }
            catch (ProviderRefusedException exception) { entries.Add(new(launcher, repository, null, exception.Message)); }
        }

        return entries.OrderBy(e => e.Launcher is null).ThenBy(e => e.Launcher ?? e.Repository, StringComparer.Ordinal).ToList();
    }

    /// <summary>Replaces the setup of exactly one launcher or repository resource, owner-only and outside repositories and estates.</summary>
    public void WriteSetup(string? launcher, string? repository, WorkspaceSetupConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string path = SetupPath(launcher, repository);
        ValidateSetup(configuration);
        WriteFile(path, configuration);
    }

    /// <summary>Removes the setup of exactly one launcher or repository resource; false when it had none.</summary>
    public bool ClearSetup(string? launcher, string? repository)
    {
        string path = SetupPath(launcher, repository);
        RefuseLink(path);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>A setup command as given: an executable, or a JSON array of the executable and its arguments.</summary>
    public static IReadOnlyList<string> SetupCommand(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string text = value.Trim();
        if (!text.StartsWith('[')) return text.Length > 0 ? [text] : throw SetupCommandRefusal();
        try
        {
            List<string>? items = JsonSerializer.Deserialize<List<string>>(text);
            if (items is { Count: > 0 } && items.All(i => !string.IsNullOrEmpty(i))) return items;
        }
        catch (JsonException)
        {
            // Refused below.
        }

        throw SetupCommandRefusal();
    }

    private static ProviderRefusedException SetupCommandRefusal() =>
        new("The setup command is an executable, or a JSON array of the executable and its arguments, each a non-empty string.");

    private string SetupPath(string? launcher, string? repository)
    {
        if ((launcher is null) == (repository is null) || (repository is not null && !Ulid.IsValid(repository)) || (launcher is not null && !LauncherName().IsMatch(launcher)))
            throw new ProviderRefusedException("A setup command belongs to exactly one launcher, named by its lowercase identity, or one repository resource, named by its id.");
        return ConfigurationPath(SetupPrefix + (repository is null ? "launcher-" + launcher : "repository-" + repository) + ".json");
    }

    private static WorkspaceSetupConfiguration? ReadSetupFile(string path)
    {
        try
        {
            RefuseLink(path);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 65536) throw new JsonException();
            WorkspaceSetupConfiguration configuration = JsonSerializer.Deserialize<WorkspaceSetupConfiguration>(File.ReadAllBytes(path), Json) ?? throw new JsonException();
            ValidateSetup(configuration);
            return configuration;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException or ProviderRefusedException)
        {
            throw new ProviderRefusedException($"The workspace setup configuration '{Path.GetFileName(path)}' could not be read; replace it with vericairn worker configure --setup-command, or remove it with --clear-setup. Nothing else was used in its place.", exception);
        }
    }

    private static void ValidateSetup(WorkspaceSetupConfiguration configuration)
    {
        if (configuration.Command.Count is < 1 or > 128 || configuration.Command.Any(a => a is null || a.Contains('\0', StringComparison.Ordinal))
            || string.IsNullOrWhiteSpace(configuration.Command[0]) || configuration.Command.Sum(a => (long)a.Length) > 32768
            || configuration.TimeoutSeconds is < 1 or > WorkspaceSetupConfiguration.MaximumTimeoutSeconds)
            throw new ProviderRefusedException($"A setup command is an executable and at most 127 arguments, 32768 characters in all and none holding a NUL character, with a deadline from 1 through {WorkspaceSetupConfiguration.MaximumTimeoutSeconds} seconds.");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex LauncherName();
}
