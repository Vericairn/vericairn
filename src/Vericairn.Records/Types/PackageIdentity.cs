// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;

namespace Vericairn.Records.Types;

/// <summary>The package ids and release versions supported by the NuGet observation contract.</summary>
public static partial class PackageIdentity
{
    /// <summary>A NuGet id of at most 100 ASCII characters, with nonempty name components separated by dots or hyphens.</summary>
    public static bool ValidId(string? value) => value is { Length: > 0 and <= 100 } && IdPattern().IsMatch(value);

    /// <summary>A three-component release version, with optional prerelease identifiers; normalized without changing its identity.</summary>
    public static bool ValidVersion(string? value) => value is { Length: > 0 and <= 100 } && VersionPattern().IsMatch(value);

    /// <summary>A credential-free URL on nuget.org's public API, without a query or fragment.</summary>
    public static bool PublicUrl(string? value) => value is { Length: <= 500 }
        && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host == "api.nuget.org" && uri.Port == 443 && uri.UserInfo.Length == 0
        && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsoluteUri == value && !value.Contains('%', StringComparison.Ordinal)
        && (uri.AbsolutePath.StartsWith("/v3/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/v3-flatcontainer/", StringComparison.Ordinal));

    [GeneratedRegex(@"\A[A-Za-z0-9_]+([.-][A-Za-z0-9_]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
