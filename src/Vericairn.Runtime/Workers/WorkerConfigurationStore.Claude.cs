// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Vericairn.Records.Signing;
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

    internal bool RemoveClaudeSetupToken()
    {
        string path = ConfigurationPath(ClaudeSetupTokenFile);
        RefuseLink(path);
        if (!File.Exists(path)) return false;
        RefuseHarnessConfiguration(path, WorkerEnvironment.Current());
        RequireOwnerOnly(Path.GetDirectoryName(path)!, isDirectory: true);
        File.Delete(path);
        return true;
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
        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            FileSystemSecurity security = isDirectory ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
            SecurityIdentifier? owner = identity.User;
            if (owner is null || !owner.Equals(security.GetOwner(typeof(SecurityIdentifier))))
                throw SetupTokenProblem(message);
            foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow && !owner.Equals(rule.IdentityReference))
                    throw SetupTokenProblem(message);
            }
        }
        else if (File.GetUnixFileMode(path) != (isDirectory ? OwnerOnlyState.DirectoryMode : OwnerOnlyState.FileMode)
            || OwnerOnlyState.Restriction(path) is not null)
            throw SetupTokenProblem(message);
    }
}
