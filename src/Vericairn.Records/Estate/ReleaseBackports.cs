// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>Record-derived backport notes, visible from either end of a cross-line release relationship.</summary>
public static class ReleaseBackports
{
    /// <summary>
    /// Published backports recorded after <paramref name="after"/> and through <paramref name="through"/>, with both versions
    /// and lines. An optional line selects incoming and outgoing backports; a product selects its continuing identity.
    /// Later backports appear in later notes; earlier frozen notes are never rewritten.
    /// </summary>
    public static IReadOnlyList<string> Notes(EstateModel model, string after, string through, string? line = null, string? product = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var notes = new List<string>();
        foreach (RecordEnvelope release in model.OfType("release").Where(r => string.CompareOrdinal(r.At, after) > 0
            && string.CompareOrdinal(r.At, through) <= 0 && EstateModel.BodyString(r, "status") == "published")
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            var pin = PolicyRecords.Property(release.Body, "backportOf");
            string? id = PolicyRecords.Text(pin, "id");
            if (id is null || !model.ById.TryGetValue(id, out RecordEnvelope? source) || source.Type != "release"
                || source.ComputedHash != PolicyRecords.Text(pin, "hash") || EstateModel.BodyString(source, "status") != "published"
                || !PolicyRecords.ReleaseMatchesProduct(model, source, release.Subject?.Id)
                || product is not null && !PolicyRecords.ReleaseMatchesProduct(model, release, product)) continue;
            string destinationLine = ReleaseLines.Line(release.Body);
            string sourceLine = ReleaseLines.Line(source.Body);
            if (line is not null && line != destinationLine && line != sourceLine) continue;
            notes.Add($"{EstateModel.BodyString(release, "version")} ({destinationLine}) is a backport of {EstateModel.BodyString(source, "version")} ({sourceLine}); source release {source.Id} at {source.ComputedHash}.");
        }
        return notes;
    }
}
