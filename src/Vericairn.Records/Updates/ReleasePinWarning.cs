// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Updates;

// Deliberately used only by check, never by canonical status or record validation: cached knowledge is machine-local.
internal static class ReleasePinWarning
{
    internal static void Add(EstateModel model, FindingList findings, PublishedRelease? cached, string? runningVersion = null)
    {
        if (model.Manifest is not { Repository: not null } manifest) return;
        string? baseline = manifest.Mode == "release" ? manifest.Version : runningVersion;
        if (baseline is null) return;
        PublishedRelease? newest = string.Equals(manifest.Repository, PublishedRelease.Repository, StringComparison.OrdinalIgnoreCase) ? cached : null;
        foreach (RecordEnvelope record in model.Current("release").Concat(model.Current("observation").Where(VericairnReleaseObservation.Is)))
        {
            bool observation = VericairnReleaseObservation.Is(record);
            System.Text.Json.JsonElement data = observation && record.Body.TryGetProperty("facts", out var facts) ? facts : record.Body;
            string? Field(string name) => data.ValueKind == System.Text.Json.JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
            if ((!observation && Field("status") != "published")
                || !string.Equals(Field("repository"), manifest.Repository, StringComparison.OrdinalIgnoreCase)) continue;
            PublishedRelease? release = PublishedRelease.FromTag(Field("version"));
            if (release is not null && (newest is null || release.IsNewerThan(newest.Version))) newest = release;
        }
        if (newest is not null && newest.IsNewerThan(baseline))
            findings.Warning("manifest.release-behind", EstateLayout.ManifestPath,
                (manifest.Mode == "release" ? $"The estate pins Vericairn {manifest.Version}" : $"The estate uses {manifest.Describe()}; Vericairn performing this check is {baseline}")
                + $"; newer published release {newest.Version} is known. Using that release's command line, review with vericairn estate upgrade --to {newest.Version} --check, then apply with --by <operator>. The command verifies the published dependency and archive before moving the pin; nothing upgrades automatically.");
    }
}
