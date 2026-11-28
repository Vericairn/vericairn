// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Vericairn.Records.Signing;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

public sealed partial class WorkerConfigurationStore
{
    internal const string ClaudeSetupTokenFile = "claude-code-setup-token.json";
    private const string ClaudeSetupTokenPrefix = "sk-ant-" + "oat01-";
    // The issuer does not promise a fixed length. Bound the input while accepting the observed prefix and alphabet.
    private const int ClaudeSetupTokenMinimumLength = 100;
    internal const int ClaudeSetupTokenMaximumLength = 1024;
    private const int ClaudeSetupTokenLifetimeDays = 365;
    private const string ExpiredSetupToken = "The provisioned Claude Code setup token has expired. Generate and import a new one with worker configure.";
    private static readonly SearchValues<char> ClaudeSetupTokenCharacters = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-");
    private static readonly string[] HarnessDirectories = [".claude", ".codex", ".gemini", ".agents"];
    private static readonly string[] HarnessDirectoryOverrides = ["CLAUDE_CONFIG_DIR", "CODEX_HOME", "GEMINI_CLI_HOME"];

    // This value stays inside the worker. A class avoids a synthesized ToString that would print its token.
    internal sealed class ClaudeSetupToken(string accessToken, string accountType, long expiresAt)
    {
        public string AccessToken { get; } = accessToken;
        public string AccountType { get; } = accountType;
        public long ExpiresAt { get; } = expiresAt;
        internal bool ContainsFragment(string text) => SecretShapes.ContainsProvisionFragment(text, AccessToken);
    }

    internal sealed record ClaudeSetupTokenStatus(bool Provisioned, DateTimeOffset? ImportedAt = null, string? AccountType = null,
        string? AccountTypeSource = null, DateTimeOffset? ExpiresAt = null, string? Problem = null);

    internal static bool IsClaudeSetupToken(string? token) => token is { Length: >= ClaudeSetupTokenMinimumLength and <= ClaudeSetupTokenMaximumLength }
        && token.StartsWith(ClaudeSetupTokenPrefix, StringComparison.Ordinal)
        && token.AsSpan(ClaudeSetupTokenPrefix.Length).IndexOfAnyExcept(ClaudeSetupTokenCharacters) < 0;

    internal static bool IsClaudeAccountType(string? type) => type is "pro" or "max";

    internal bool HasClaudeSetupToken() => File.Exists(ConfigurationPath(ClaudeSetupTokenFile));

    internal void ImportClaudeSetupToken(string? token, string accountType)
    {
        if (!IsClaudeAccountType(accountType))
            throw new ProviderRefusedException("State an unmanaged Claude Code account type with --account-type pro or --account-type max.");
        if (!IsClaudeSetupToken(token))
            throw new ProviderRefusedException("Enter one bare Claude Code setup-token value.");

        string path = ConfigurationPath(ClaudeSetupTokenFile);
        RefuseHarnessConfiguration(path, WorkerEnvironment.Current());
        RequireHomeNotWritableByOthers(path);
        // The headless form has no expiry field. Keep the probe's one-year timestamp fixed across every copy.
        WriteFile(path, new ClaudeSetupToken(token!, accountType,
            DateTimeOffset.UtcNow.AddDays(ClaudeSetupTokenLifetimeDays).ToUnixTimeMilliseconds()), requireOwnerOnly: true);
    }

    internal ClaudeSetupTokenStatus DescribeClaudeSetupToken()
    {
        try
        {
            if (ReadClaudeSetupToken(allowExpired: true) is not { } setup) return new(false);
            var expiry = DateTimeOffset.FromUnixTimeMilliseconds(setup.ExpiresAt);
            // The provision's expiry has always been stored as the import time plus this fixed lifetime.
            return new(true, expiry.AddDays(-ClaudeSetupTokenLifetimeDays), setup.AccountType, "operator-statement", expiry,
                SetupTokenExpired(setup) ? ExpiredSetupToken : null);
        }
        catch (ProviderRefusedException exception)
        {
            return new(true, Problem: exception.Message);
        }
    }

    internal (bool Removed, IReadOnlyList<string> Warnings) RemoveClaudeSetupToken(Action? beforeUnixUnlink = null)
    {
        string path = ConfigurationPath(ClaudeSetupTokenFile);
        RefuseLink(path);
        if (!File.Exists(path)) return (false, []);
        RefuseHarnessConfiguration(path, WorkerEnvironment.Current());

        string worker = Path.GetDirectoryName(path)!;
        string home = Path.GetDirectoryName(worker)!;
        using SafeFileHandle homeHandle = OperatingSystem.IsWindows()
            ? DirectoryHandles.OpenWindows(home)
            : DirectoryHandles.OpenUnix(home, searchOnly: true);
        using SafeFileHandle workerHandle = OperatingSystem.IsWindows()
            ? DirectoryHandles.OpenWindows(Path.GetFileName(worker), homeHandle)
            : DirectoryHandles.OpenUnix(Path.GetFileName(worker), homeHandle, searchOnly: true);

        if (OperatingSystem.IsWindows())
        {
            if (!DirectoryHandles.UnlinkWindows(workerHandle, ClaudeSetupTokenFile)) return (false, []);
        }
        else
        {
            RefuseLink(path);
            beforeUnixUnlink?.Invoke();
            // Unlink removes the entry in this directory even if a concurrent rename substitutes a link at the leaf.
            if (DirectoryHandles.UnlinkAt(workerHandle, ClaudeSetupTokenFile, 0) != 0)
            {
                if (Marshal.GetLastPInvokeError() == 2) return (false, []); // ENOENT
                throw new IOException("The provisioned Claude Code setup token could not be removed.");
            }
        }

        List<string> warnings = [];
        try
        {
            bool writableHome = OperatingSystem.IsWindows() ? HomeWritableByOthers(path) : IsWritableByOthers(File.GetUnixFileMode(homeHandle));
            bool ownerOnlyWorker = OperatingSystem.IsWindows()
                ? HasOwnerOnlyPermissions(worker, isDirectory: true)
                : File.GetUnixFileMode(workerHandle) == OwnerOnlyState.DirectoryMode;
            if (writableHome)
                warnings.Add("The Vericairn home is writable by other users. Restrict its write access to its owner.");
            if (!ownerOnlyWorker)
                warnings.Add("The worker directory must have owner-only permissions. Restrict access to its owner.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add("Could not check the Vericairn home and worker directory permissions. Check their ownership and restrict access.");
        }
        return (true, warnings);
    }

    internal ClaudeSetupToken? ReadClaudeSetupToken(IReadOnlyDictionary<string, string>? environment = null, bool allowExpired = false)
    {
        try
        {
            string path = ConfigurationPath(ClaudeSetupTokenFile);
            RefuseLink(path);
            if (Directory.Exists(path)) throw new JsonException();
            if (!File.Exists(path)) return null;
            RefuseHarnessConfiguration(path, environment ?? WorkerEnvironment.Current());
            RequireHomeNotWritableByOthers(path);
            RequireOwnerOnly(Path.GetDirectoryName(path)!, isDirectory: true);
            RequireOwnerOnly(path, isDirectory: false);
            if (OwnerOnlyState.IsOwnRegularFile(path) == false) throw new JsonException();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > 4096) throw new JsonException();
            ClaudeSetupToken? setup = JsonSerializer.Deserialize<ClaudeSetupToken>(stream, Json);
            if (setup is null || !IsClaudeSetupToken(setup.AccessToken) || !IsClaudeAccountType(setup.AccountType)
                || setup.ExpiresAt <= 0 || setup.ExpiresAt > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
                throw new JsonException();
            if (!allowExpired && SetupTokenExpired(setup)) throw SetupTokenProblem(ExpiredSetupToken);
            return setup;
        }
        catch (JsonException)
        {
            throw SetupTokenProblem("The provisioned Claude Code setup token file is corrupt. Import it again with worker configure.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw SetupTokenProblem("The provisioned Claude Code setup token file could not be read. Check its owner and permissions or import it again with worker configure.");
        }
    }

    private static bool SetupTokenExpired(ClaudeSetupToken setup) => setup.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static ProviderRefusedException SetupTokenProblem(string message) => new(message) { SafeForQualification = true };

    private static void RequireHomeNotWritableByOthers(string provision)
    {
        if (HomeWritableByOthers(provision))
            throw SetupTokenProblem("The Vericairn home is writable by other users. Restrict its write access before using a Claude Code setup token.");
    }

    private static bool HomeWritableByOthers(string provision)
    {
        string home = Path.GetDirectoryName(Path.GetDirectoryName(provision)!)!;
        if (!Directory.Exists(home)) return false;
        if (OperatingSystem.IsWindows())
        {
            const FileSystemRights change = FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories | FileSystemRights.Delete
                | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
            DirectorySecurity security = new DirectoryInfo(home).GetAccessControl();
            foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & change) != 0
                    && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0
                    && rule.IdentityReference is SecurityIdentifier identity && (identity.IsWellKnown(WellKnownSidType.WorldSid)
                        || identity.IsWellKnown(WellKnownSidType.AuthenticatedUserSid) || identity.IsWellKnown(WellKnownSidType.BuiltinUsersSid)))
                    return true;
            }
            return false;
        }
        return IsWritableByOthers(File.GetUnixFileMode(home));
    }

    private static bool IsWritableByOthers(UnixFileMode mode) => (mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0;

    private void RefuseHarnessConfiguration(string path, IReadOnlyDictionary<string, string> environment)
    {
        const string message = "Store the provisioned Claude Code setup token outside harness configuration directories.";
        StringComparer comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var configured = new HashSet<string>(comparer);
        foreach (string name in HarnessDirectoryOverrides)
        {
            string? location = environment.GetValueOrDefault(name) ?? Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(location)) configured.Add(CanonicalDirectory(location));
        }
        foreach (string? user in new[] { environment.GetValueOrDefault("HOME"), environment.GetValueOrDefault("USERPROFILE"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) })
        {
            if (string.IsNullOrWhiteSpace(user)) continue;
            foreach (string name in HarnessDirectories) configured.Add(CanonicalDirectory(Path.Combine(user, name)));
        }
        if (OperatingSystem.IsWindows())
            configured.Add(CanonicalDirectory(Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClaudeCode")));
        else if (OperatingSystem.IsMacOS())
        {
            configured.Add(CanonicalDirectory("/Library/Application Support/ClaudeCode"));
            configured.Add(CanonicalDirectory("/Library/Managed Preferences"));
        }
        else configured.Add(CanonicalDirectory("/etc/claude-code"));

        for (DirectoryInfo? parent = new(Path.GetFullPath(directory)); parent is not null; parent = parent.Parent)
        {
            if (HarnessDirectories.Contains(parent.Name, comparer))
                throw new ProviderRefusedException(message);
        }
        for (DirectoryInfo? parent = new(Path.GetDirectoryName(path)!); parent is not null; parent = parent.Parent)
        {
            if (HarnessDirectories.Contains(parent.Name, comparer) || configured.Contains(parent.FullName))
                throw new ProviderRefusedException(message);
        }
    }

    internal static FileStream CreateOwnerFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier owner = identity.User ?? throw new ProviderRefusedException("The worker file owner could not be established.");
            var security = new FileSecurity();
            security.SetOwner(owner);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security);
        }

        var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, UnixCreateMode = OwnerOnlyState.FileMode,
        });
        try
        {
            if (File.GetUnixFileMode(stream.SafeFileHandle) != OwnerOnlyState.FileMode)
                throw new ProviderRefusedException("The filesystem did not create an owner-only worker file.");
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void RequireOwnerOnly(string path, bool isDirectory)
    {
        const string message = "The provisioned Claude Code setup token file and worker directory must have owner-only permissions. Restrict access or import the token again with worker configure.";
        RefuseLink(path);
        if (!HasOwnerOnlyPermissions(path, isDirectory)) throw SetupTokenProblem(message);
    }

    private static bool HasOwnerOnlyPermissions(string path, bool isDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            FileSystemSecurity security = isDirectory ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
            SecurityIdentifier? owner = identity.User;
            if (owner is null || !owner.Equals(security.GetOwner(typeof(SecurityIdentifier))))
                return false;
            foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow && !owner.Equals(rule.IdentityReference))
                    return false;
            }
            return true;
        }
        return File.GetUnixFileMode(path) == (isDirectory ? OwnerOnlyState.DirectoryMode : OwnerOnlyState.FileMode)
            && OwnerOnlyState.Restriction(path) is null;
    }
}
