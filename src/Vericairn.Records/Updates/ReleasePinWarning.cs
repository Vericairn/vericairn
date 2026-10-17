// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Updates;

// Deliberately used only by check, never by canonical status or record validation: cached knowledge is machine-local.
internal static class ReleasePinWarning
{
    internal static void Add(EstateModel model, FindingList findings, PublishedRelease? cached)
    {
        if (model.Manifest is not { Mode: "release", Version: not null, Repository: not null } manifest) return;
        PublishedRelease? newest = string.Equals(manifest.Repository, PublishedRelease.Repository, StringComparison.OrdinalIgnoreCase) ? cached : null;
        foreach (RecordEnvelope record in model.Current("release"))
        {
            if (EstateModel.BodyString(record, "status") != "published"
                || !string.Equals(EstateModel.BodyString(record, "repository"), manifest.Repository, StringComparison.OrdinalIgnoreCase)) continue;
            PublishedRelease? release = PublishedRelease.FromTag(EstateModel.BodyString(record, "version"));
            if (release is not null && (newest is null || release.IsNewerThan(newest.Version))) newest = release;
        }
        if (newest is not null && newest.IsNewerThan(manifest.Version))
            findings.Warning("manifest.release-behind", EstateLayout.ManifestPath,
                $"The estate pins Vericairn {manifest.Version}; newer published release {newest.Version} is known. Review with vericairn estate upgrade --to {newest.Version} --check, then apply with --by <operator>. The target's published release record must be in the estate first; nothing upgrades automatically.");
    }
}
