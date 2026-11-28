// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;

namespace Vericairn.Adapters.Git;

/// <summary>Classifies addresses without asking Git to resolve its URL rewrites or select a transport.</summary>
internal static partial class RepositoryAddress
{
    internal static bool IsNetwork(string address)
    {
        if (!Plain(address)) return false;
        if (address.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || address.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && uri.Host.Length > 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
                && (uri.Scheme == "ssh" ? !uri.UserInfo.Contains(':', StringComparison.Ordinal) : uri.UserInfo.Length == 0);
        return !address.Contains("://", StringComparison.Ordinal) && !address.Contains('\\', StringComparison.Ordinal) && !(OperatingSystem.IsWindows() && Path.IsPathFullyQualified(address))
            && Scp().IsMatch(address);
    }

    internal static bool IsLocal(string address) => Plain(address) && !address.Contains("://", StringComparison.Ordinal)
        && !address.StartsWith("//", StringComparison.Ordinal) && !address.StartsWith(@"\\", StringComparison.Ordinal)
        && (!address.Contains(':', StringComparison.Ordinal) || OperatingSystem.IsWindows() && Path.IsPathFullyQualified(address));

    private static bool Plain(string address) => address.Length > 0 && !address.StartsWith('-')
        && !address.Contains("::", StringComparison.Ordinal) && !address.Any(char.IsControl) && address == address.Trim();

    [GeneratedRegex(@"\A(?:[A-Za-z0-9._-]+@)?[A-Za-z0-9][A-Za-z0-9.-]*:[^\s:]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex Scp();
}
