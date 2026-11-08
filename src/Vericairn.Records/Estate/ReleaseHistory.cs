// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;
using Vericairn.Records.Updates;

namespace Vericairn.Records.Estate;

/// <summary>
/// The earlier releases a release's version-tracking guides must not name as the current one (the estate's design/release-procedure.md
/// § 7, step docs-before-freeze): the two newest published releases of the product before the version, newest first, then any candidate
/// withdrawn after the older of them, such as 0.8.1 when 0.8.2 follows 0.8.0. docs pass --before reads these versions and records them,
/// and the release flow's step reads the same list from the records, so a check of other versions never counts.
/// </summary>
public static class ReleaseHistory
{
    /// <summary>The earlier versions, from each release's version and status.</summary>
    public static IReadOnlyList<string> Previous(IEnumerable<(string Version, string Status)> releases, string version)
    {
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(version);
        List<(string Version, string Status)> earlier = releases.Where(r => ReleaseFollowing.IsNewer(version, r.Version)).ToList();
        List<string> published = earlier.Where(r => r.Status == "published").Select(r => r.Version).Distinct(StringComparer.Ordinal).ToList();
        published.Sort((a, b) => ReleaseFollowing.IsNewer(a, b) ? -1 : ReleaseFollowing.IsNewer(b, a) ? 1 : string.CompareOrdinal(a, b));
        List<string> taken = published.Take(2).ToList();
        string? oldest = taken.Count == 2 ? taken[1] : null;
        List<string> withdrawn = earlier.Where(r => r.Status == "withdrawn" && (oldest is null || ReleaseFollowing.IsNewer(r.Version, oldest))).Select(r => r.Version).Distinct(StringComparer.Ordinal).ToList();
        withdrawn.Sort((a, b) => ReleaseFollowing.IsNewer(a, b) ? -1 : ReleaseFollowing.IsNewer(b, a) ? 1 : string.CompareOrdinal(a, b));
        return taken.Concat(withdrawn.Where(w => !taken.Contains(w, StringComparer.Ordinal))).ToList();
    }

    /// <summary>The earlier versions of a product in an estate's current release records, at any revision of the product.</summary>
    public static IReadOnlyList<string> Previous(EstateModel model, string product, string version)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(product);
        string identity = LivingDocuments.ProductIdentity(model, product);
        return Previous(model.Current("release").Where(r => r.Subject is not null && LivingDocuments.ProductIdentity(model, r.Subject.Id) == identity)
            .Select(r => (EstateModel.BodyString(r, "version") ?? string.Empty, EstateModel.BodyString(r, "status") ?? string.Empty)), version);
    }

    /// <summary>The earlier versions as a check records them: separated by single spaces, empty when there are none.</summary>
    public static string Joined(IReadOnlyList<string> previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        return string.Join(' ', previous);
    }
}
