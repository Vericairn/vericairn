// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.History;

/// <summary>
/// The acts a closing precedes (design/signed-history.md § 8; OPS-07): revoking a device, rotating a device's key, and revising a grant
/// in a way that narrows it. In an estate that has adopted history checkpoints each is stored after its closing, in the same batch.
/// </summary>
internal static class ClosingActs
{
    /// <summary>The record types such an act can be: a revision of an admission, a device or an authority record.</summary>
    internal static readonly IReadOnlyList<string> Types = ["admission", "device", "authority"];

    /// <summary>
    /// The closing a retried act is written after, alone (OPS-07): a closing that counts and acts, names exactly what the act closes,
    /// was written by the act's author, and that no act follows yet, as a batch stopped between a closing and its act, or a process
    /// that stopped between them, leaves it. The newest such closing, or null when there is none and the act carries its own. Nothing
    /// else changes: the act is judged by its own rules, and what the closed device or key signed after the closing still counts only
    /// when the closing witnesses it.
    /// </summary>
    internal static Closing? Stored(EstateModel model, (string Kind, string Id) closes, string by)
    {
        ArgumentNullException.ThrowIfNull(model);
        EstateTrust trust = model.Trust;
        return trust.Closings.LastOrDefault(c => c.Kind == closes.Kind && c.Target == closes.Id && c.Record.By == by && model.Counts(c.Record) && !trust.Followed(c));
    }

    /// <summary>
    /// What the closing before a record names, when the record is such an act: <c>device</c> and the device record a revocation names;
    /// <c>key</c> and the device revision whose key a revision replaces; <c>grant</c> and the authority revision a revision replaces and
    /// narrows. A grant revision replaces the nearest record below it in its line that counts (<see cref="EstateTrust.AttachedTo"/>):
    /// the one it supersedes when that counts, otherwise the one that record attaches to, and with none below it that counts it narrows
    /// nothing. A device revision needs no such reading, since it counts only on top of one that counts: a rotation that counts replaces
    /// a key its device held. Null for any other record.
    /// </summary>
    internal static (string Kind, string Id)? For(EstateModel model, RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return For(model, record.Type, record.Body, record.Subject, record.Supersedes);
    }

    /// <inheritdoc cref="For(EstateModel, RecordEnvelope)"/>
    internal static (string Kind, string Id)? For(EstateModel model, string type, JsonElement body, RecordReference? subject, string? supersedes) =>
        For(model, null, type, body, subject, supersedes);

    /// <summary>
    /// <inheritdoc cref="For(EstateModel, RecordEnvelope)"/> What counts is read from <paramref name="trust"/>, the model's judgment
    /// when null: the judgment passes itself while it is still being made.
    /// </summary>
    internal static (string Kind, string Id)? For(EstateModel model, EstateTrust? trust, string type, JsonElement body, RecordReference? subject, string? supersedes)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (supersedes is null || body.ValueKind != JsonValueKind.Object || !model.ById.TryGetValue(supersedes, out RecordEnvelope? previous) || previous.Type != type) return null;
        switch (type)
        {
            case "admission":
                if (Text(body, "status") != "revoked") return null;
                string? device = subject?.Id ?? previous.Subject?.Id;
                return device is not null && model.ById.TryGetValue(device, out RecordEnvelope? named) && named.Type == "device" ? ("device", device) : null;
            case "device":
                return Text(body, "publicKey") is { } key && key != EstateModel.BodyString(previous, "publicKey") ? ("key", previous.Id) : null;
            case "authority":
                trust ??= model.Trust;
                RecordEnvelope? replaced = trust.Counts(previous) ? previous : trust.AttachedTo(previous);
                return replaced is not null && Narrows(replaced.Body, body) ? ("grant", replaced.Id) : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Whether a grant revision narrows the revision it replaces, read conservatively: it names another grantee, drops a scope, reserves
    /// another action, adds a condition, adds or shortens an expiry, or changes the bounds of a policy scope in any way. Only a revision
    /// that keeps or widens every one of them writes no closing.
    /// </summary>
    internal static bool Narrows(JsonElement previous, JsonElement next)
    {
        if (previous.ValueKind != JsonValueKind.Object || next.ValueKind != JsonValueKind.Object) return true;
        if (Text(previous, "grantee") != Text(next, "grantee")) return true;
        if (!Strings(previous, "scope").IsSubsetOf(Strings(next, "scope"))) return true;
        if (!Strings(next, "reserved").IsSubsetOf(Strings(previous, "reserved"))) return true;
        if (!Strings(next, "conditions").IsSubsetOf(Strings(previous, "conditions"))) return true;
        string? was = Text(previous, "expires");
        string? now = Text(next, "expires");
        if (now is not null && (was is null || string.CompareOrdinal(now, was) < 0)) return true;
        bool hadBounds = previous.TryGetProperty("bounds", out JsonElement before);
        bool hasBounds = next.TryGetProperty("bounds", out JsonElement after);
        return hadBounds != hasBounds || hadBounds && !JsonElement.DeepEquals(before, after);
    }

    private static string? Text(JsonElement body, string name) =>
        body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static HashSet<string> Strings(JsonElement body, string name) =>
        body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!)]
            : new HashSet<string>(StringComparer.Ordinal);
}
