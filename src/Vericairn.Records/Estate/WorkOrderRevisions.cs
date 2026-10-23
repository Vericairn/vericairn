// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

internal static class WorkOrderRevisions
{
    private static readonly HashSet<string> PlacementKey = new(StringComparer.Ordinal) { "placement" };

    // Pins still identify the original bytes. Only placement may change along the entire path to the displayed revision.
    internal static bool IsCurrentSubject(EstateModel model, RecordReference? subject, RecordEnvelope order)
    {
        if (subject?.Type != "work-order" || order.Type != "work-order" ||
            !model.ById.TryGetValue(subject.Id, out RecordEnvelope? pinned) || pinned.Type != "work-order" ||
            subject.Hash != pinned.ComputedHash)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal) { order.Id };
        while (order.Id != pinned.Id)
        {
            if (order.Supersedes is null || !model.ById.TryGetValue(order.Supersedes, out RecordEnvelope? previous) ||
                previous.Type != "work-order" || !seen.Add(previous.Id) ||
                Placement(order) == Placement(previous) ||
                CanonicalJson.Serialize(order.Body, PlacementKey) != CanonicalJson.Serialize(previous.Body, PlacementKey))
            {
                return false;
            }

            // Compare every step: reverting a content edit does not revive a judgment made before that edit.
            order = previous;
        }

        return true;
    }

    private static string? Placement(RecordEnvelope record) => record.Body.TryGetProperty("placement", out JsonElement placement)
        ? CanonicalJson.Serialize(placement) : null;
}
