// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>Portable choices only; a local file cannot supply an environment, command, tool or route.</summary>
/// <param name="Model">Default model selector.</param>
/// <param name="Effort">Default native effort.</param>
/// <param name="AllowedModels">Permitted selectors; only the default model when omitted.</param>
/// <param name="AllowedEfforts">Permitted native efforts; the launcher's vocabulary when omitted.</param>
public sealed record WorkerConfiguration(string Model, string Effort, IReadOnlyList<string>? AllowedModels = null, IReadOnlyList<string>? AllowedEfforts = null)
{
    /// <summary>Default model selector.</summary>
    public string Model { get; init; } = Model;

    /// <summary>Default native effort.</summary>
    public string Effort { get; init; } = Effort;

    /// <summary>Permitted selectors; only the default model when omitted. Copied into owned read-only storage on initialization.</summary>
    public IReadOnlyList<string>? AllowedModels
    {
        get;
        init => field = OptionCollections.OptionalList(value);
    } = OptionCollections.OptionalList(AllowedModels);

    /// <summary>Permitted native efforts; the launcher's vocabulary when omitted. Copied into owned read-only storage on initialization.</summary>
    public IReadOnlyList<string>? AllowedEfforts
    {
        get;
        init => field = OptionCollections.OptionalList(value);
    } = OptionCollections.OptionalList(AllowedEfforts);
}

/// <summary>Owner-only launcher choices and workspace placement under VericairnHome, read afresh for each run by the CLI and daemon.</summary>
public sealed partial class WorkerConfigurationStore
{
    private static readonly JsonSerializerOptions Json = new(ProtocolJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        WriteIndented = true,
    };
    private readonly string directory;

    /// <summary>Uses this user's Vericairn home, or an isolated home supplied by a host.</summary>
    public WorkerConfigurationStore(string? home = null) => directory = Path.Combine(home ?? VericairnHome.Path(), "worker");

    /// <summary>Reads and validates one native launcher's settings; absence is not an ambient default.</summary>
    public WorkerConfiguration? Read(IAgentLauncher launcher)
    {
        try
        {
            string path = PathOf(launcher);
            RefuseLink(path);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 65536) throw new ProviderRefusedException("The worker configuration exceeds its size limit.");
            WorkerConfiguration configuration = JsonSerializer.Deserialize<WorkerConfiguration>(File.ReadAllBytes(path), Json)
                ?? throw new JsonException();
            Validate(launcher, configuration);
            return configuration;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new ProviderRefusedException("The local worker configuration could not be read; use vericairn worker configure to replace it. No ambient fallback is permitted.", exception);
        }
    }

    /// <summary>Replaces one launcher's settings atomically; validation happens before writing anything.</summary>
    public void Write(IAgentLauncher launcher, WorkerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string path = PathOf(launcher);
        Validate(launcher, configuration);
        WriteFile(path, configuration);
    }

    private static void WriteFile<T>(string path, T configuration)
    {
        RefuseLink(path);
        string parent = Path.GetDirectoryName(path)!;
        CreateOwnerDirectory(parent);
        string temporary = Path.Combine(parent, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options))
            {
                JsonSerializer.Serialize(stream, configuration, Json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void CreateOwnerDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier owner = identity.User ?? throw new ProviderRefusedException("The worker configuration owner could not be established.");
            var access = new DirectorySecurity();
            access.SetOwner(owner);
            access.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            access.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            var folder = new DirectoryInfo(path);
            folder.Create(access);
            folder.SetAccessControl(access);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Builds RUN-01's lowest-precedence defaults and cumulative local allowlists.</summary>
    internal RunConfiguration? Resolve(IAgentLauncher launcher, string? name)
    {
        if (launcher.Launcher is not ("codex" or "claude-code") || (name is not null && name != launcher.Launcher)) return null;
        WorkerConfiguration? configuration = Read(launcher);
        if (configuration is null) return null;
        RunCapabilities capabilities = launcher.ExecutionCapabilities;
        return new(launcher.Launcher, launcher.Launcher, JsonSerializer.SerializeToElement(new
        {
            choices = new { model = configuration.Model, effort = configuration.Effort, provider = capabilities.Providers.Single(),
                destination = capabilities.Destinations.Single(), dataBoundary = capabilities.DataBoundaries.Single(), adapter = "disabled" },
        }), configuration.AllowedModels ?? [configuration.Model], Efforts: configuration.AllowedEfforts);
    }

    private string PathOf(IAgentLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        if (launcher.Launcher is not ("codex" or "claude-code")) throw new ProviderRefusedException("Worker configuration supports only codex and claude-code.");
        return ConfigurationPath(launcher.Launcher + ".json");
    }

    private string ConfigurationPath(string name)
    {
        RefuseLink(directory);
        RefuseLink(Path.GetDirectoryName(directory)!);
        string canonical = CanonicalDirectory(directory);
        for (DirectoryInfo? parent = new(canonical); parent is not null; parent = parent.Parent)
        {
            if (RepositoryOrEstate(parent.FullName))
                throw new ProviderRefusedException("Worker configuration must be outside repositories and estates; choose a machine-local Vericairn home.");
        }
        return Path.Combine(canonical, name);
    }

    // Ancestors may be links (including the system's temporary directory); retain an absent tail without creating it.
    // Walk the resolved parents too, so a link cannot hide a repository, estate or harness configuration above its target.
    private static string CanonicalDirectory(string path)
    {
        string suffix = "";
        int links = 0;
        for (DirectoryInfo? parent = new(Path.GetFullPath(path)); parent is not null; parent = parent.Parent)
        {
            if (parent.LinkTarget is not null)
            {
                if (++links > 40) throw new ProviderRefusedException("The local directory has too many symbolic links or reparse points to resolve.");
                parent = new DirectoryInfo(parent.ResolveLinkTarget(returnFinalTarget: true)!.FullName);
            }
            if (parent.Parent is null) return Path.TrimEndingDirectorySeparator(Path.Combine(parent.FullName, suffix));
            suffix = Path.Combine(parent.Name, suffix);
        }
        throw new ProviderRefusedException("The local directory could not be resolved.");
    }

    private static bool RepositoryOrEstate(string path) => Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git"))
        || File.Exists(Path.Combine(path, "estate", "manifest.json"))
        || (File.Exists(Path.Combine(path, "HEAD")) && Directory.Exists(Path.Combine(path, "objects"))
            && (Directory.Exists(Path.Combine(path, "refs")) || Directory.Exists(Path.Combine(path, "reftable"))));

    private static void RefuseLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null)
            throw new ProviderRefusedException("Worker configuration cannot use symbolic links or reparse points.");
    }

    private static void Validate(IAgentLauncher launcher, WorkerConfiguration configuration)
    {
        RunCapabilities capabilities = launcher.ExecutionCapabilities;
        IReadOnlyList<string> efforts = capabilities.Efforts.Count == 0 ? ["not-supported"]
            : capabilities.ProviderDefault ? [.. capabilities.Efforts, "provider-default"] : capabilities.Efforts;
        if (!ExecutionPolicy.IsIdentity(configuration.Model) || configuration.Model == "unknown"
            || !efforts.Contains(configuration.Effort, StringComparer.Ordinal)
            || capabilities.Providers.Count != 1 || capabilities.Destinations.Count != 1 || capabilities.DataBoundaries.Count != 1
            || !Allowed(configuration.AllowedModels, configuration.Model) || !Allowed(configuration.AllowedEfforts, configuration.Effort)
            || configuration.AllowedEfforts?.Any(e => !efforts.Contains(e, StringComparer.Ordinal)) == true)
            throw new ProviderRefusedException("Worker configuration requires a portable model, a supported native effort, and nonempty allowlists containing their defaults. It cannot widen the launcher's capabilities.");

        static bool Allowed(IReadOnlyList<string>? values, string required) => values is null || (values.Count is > 0 and <= 128
            && values.All(v => ExecutionPolicy.IsIdentity(v) && v != "unknown")
            && values.Distinct(StringComparer.Ordinal).Count() == values.Count && values.Contains(required, StringComparer.Ordinal));
    }
}
