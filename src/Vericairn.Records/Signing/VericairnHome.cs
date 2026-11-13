// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Signing;

/// <summary>Where this user's Vericairn state lives on this machine: keys, caches, device identity. Never inside an estate repository.</summary>
public static class VericairnHome
{
    /// <summary>Environment variable that overrides the location (tests, continuous integration, unusual machines).</summary>
    public const string EnvironmentVariable = "VERICAIRN_HOME";

    /// <summary>
    /// The home of the scope the calling code runs in, for a process that runs several isolated scopes side by side: the runtime's
    /// tests give each test class its own home this way, since the environment variable is one per process. Null or empty outside a
    /// scope, and always so in the product, which never sets it.
    /// </summary>
    internal static Func<string?>? Scope { get; set; }

    /// <summary>The scope's own home when the calling code runs in one, which a process it starts is given; null otherwise.</summary>
    internal static string? Scoped() => Scope?.Invoke() is { Length: > 0 } scoped ? System.IO.Path.GetFullPath(scoped) : null;

    /// <summary>The per-user state directory for this machine.</summary>
    public static string Path()
    {
        if (Scoped() is string scoped)
        {
            return scoped;
        }

        string? overridden = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return System.IO.Path.GetFullPath(overridden);
        }

        if (OperatingSystem.IsWindows())
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vericairn");
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            return System.IO.Path.Combine(home, "Library", "Application Support", "Vericairn");
        }

        string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return System.IO.Path.Combine(string.IsNullOrWhiteSpace(xdg) ? System.IO.Path.Combine(home, ".config") : xdg, "vericairn");
    }

    /// <summary>The platform name recorded in device records.</summary>
    public static string Platform() =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsIOS() ? "ios" : OperatingSystem.IsAndroid() ? "android" : "other";
}
