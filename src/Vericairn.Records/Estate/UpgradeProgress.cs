// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// An upgrade writes its migration record before it replaces the manifest. Between the two, the newest migration record
/// starts from the manifest's pin and names another; only the upgrade to that release finishes it.
/// </summary>
public static class UpgradeProgress
{
    /// <summary>The newest migration record when it starts from the manifest's current pin and moves to another, else null.</summary>
    public static RecordEnvelope? Pending(EstateManifest manifest, IEnumerable<RecordEnvelope> records)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(records);
        RecordEnvelope? newest = records.Where(r => r.Type == "migration")
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
        if (newest is null) return null;
        string current = manifest.Describe();
        return EstateModel.BodyString(newest, "fromPin") == current && EstateModel.BodyString(newest, "toPin") is { } to && to != current
            ? newest
            : null;
    }

    /// <summary>Whether a record ahead of the manifest's format is the pending migration that moves the estate to exactly that format.</summary>
    public static bool MovesToItsOwnFormat(EstateManifest manifest, RecordEnvelope pending) =>
        pending.Format > manifest.RecordFormat
        && Number(pending, "fromFormat") == manifest.RecordFormat
        && Number(pending, "toFormat") == pending.Format;

    private static long? Number(RecordEnvelope record, string field) =>
        record.Body.TryGetProperty(field, out System.Text.Json.JsonElement value) && value.TryGetInt64(out long number) ? number : null;
}
