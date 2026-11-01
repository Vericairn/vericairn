// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Updates;

// Deliberately used only by check, never by canonical status or record validation: cached knowledge is machine-local.
internal static class ReleasePinWarning
{
    internal static void Add(EstateModel model, FindingList findings, PublishedRelease? cached, string? runningVersion = null, DateTimeOffset? now = null)
    {
        if (model.Manifest is not { Repository: not null } manifest) return;
        ReleaseFollowing.WarnUnsupported(model, findings, now ?? DateTimeOffset.UtcNow);
        string? baseline = manifest.Mode == "release" ? manifest.Version : runningVersion;
        if (baseline is null) return;
        string? newest = ReleaseFollowing.Newest(model, cached);
        if (newest is not null && ReleaseFollowing.IsNewer(newest, baseline))
            findings.Warning("manifest.release-behind", EstateLayout.ManifestPath,
                (manifest.Mode == "release" ? $"The estate pins Vericairn {manifest.Version}" : $"The estate uses {manifest.Describe()}; Vericairn performing this check is {baseline}")
                + $"; newer published release {newest} is known in line {manifest.Line} at channel {manifest.Channel} or more stable. Using that release's command line, review with vericairn estate upgrade --to {newest} --check, then apply with --by <operator>. The command verifies the published dependency and archive before moving the pin; nothing upgrades automatically.");
    }
}
