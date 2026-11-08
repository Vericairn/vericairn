// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;

namespace Vericairn.Records.Types;

/// <summary>
/// A signed history checkpoint (design/signed-history.md § 5): the records it first witnesses, by path and hash in inventory objects,
/// and the checkpoints it follows. What a counting checkpoint witnesses cannot be removed or altered unnoticed. The graph, the
/// inventory, anchors and acknowledged removals are judged together by <see cref="History.SignedHistory"/>.
/// </summary>
public sealed class HistoryCheckpointType : IRecordType
{
    /// <summary>The values of <c>trigger</c>.</summary>
    internal static readonly ImmutableArray<string> Triggers = ["adoption", "interval", "upgrade", "release", "charter", "closing", "manual"];

    /// <summary>The values of <c>closes.kind</c>.</summary>
    internal static readonly ImmutableArray<string> ClosingKinds = ["device", "key", "grant"];

    /// <inheritdoc />
    public string Name => "history-checkpoint";

    /// <inheritdoc />
    public string Summary => "A signed list of the records an estate holds, by path and hash, naming the checkpoints it follows. What a counting checkpoint witnesses cannot be removed or altered unnoticed; an anchoring one, by the root operator or a person holding administer, also lists the unsigned records that count under required signatures and the removals it acknowledges. Signed by an admitted device even under an optional charter; unsigned, it witnesses nothing.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public long SinceFormat => 3;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("previous", "object[]", true, "{id, hash} of every checkpoint head the signer's copy held, sorted by id; empty only for the adoption checkpoint, an estate's first. After a git merge the next checkpoint names every head, and what it witnesses is the union.", 3),
        new("inventory", "string[]", true, "Hashes of the objects listing the records this checkpoint first witnesses: one line per record, its path under records/, a space and its content hash, sorted by id across the objects, each object at most 8 MiB. A checkpoint never lists itself; the next one witnesses it.", 3),
        new("records", "integer", true, "How many records this checkpoint witnesses, through its own inventory and those of the checkpoints it follows, less any acknowledged removals.", 3),
        new("recordSetHash", "hash", true, "The record set hash of what it witnesses, defined as for estate observations: SHA-256 of the canonical JSON array of {hash, id} sorted by id.", 3),
        new("trigger", "adoption|interval|upgrade|release|charter|closing|manual", true, "Why it was written: adoption for an estate's first checkpoint, closing with closes, otherwise the runtime's trigger or manual.", 3),
        new("closes", "object", false, "For a closing checkpoint: {kind, id}, kind device, key (the device revision whose key is replaced) or grant (the authority revision being narrowed).", 3),
        new("anchored", "object[]", false, "{id, hash} of the unsigned records that count under required signatures only because this checkpoint lists them. Only the root operator or a person holding administer anchors, never an agent, with a device whose own admission rests on signatures.", 3),
        new("removed", "object[]", false, "{id, hash, reason} of witnessed records that had to go, such as a leaked secret: an acknowledgement settles their absence. Only the root operator or a person holding administer acknowledges a removal.", 3),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        IReadOnlyList<BodyReader>? previous = body.NestedArray("previous", true);
        IReadOnlyList<string>? inventory = body.StringArray("inventory", true, allowEmpty: false, maxItemLength: 71);
        body.Number("records", true, 0, Canonical.CanonicalJson.MaxSafeInteger);
        body.Hash("recordSetHash", true);
        string? trigger = body.Enum("trigger", true, [.. Triggers]);
        BodyReader? closes = body.Nested("closes", false);
        IReadOnlyList<BodyReader>? anchored = body.NestedArray("anchored", false, allowEmpty: false);
        IReadOnlyList<BodyReader>? removed = body.NestedArray("removed", false, allowEmpty: false);
        body.NoOtherKeys();
        string self = context.Record.Id;
        Pins(body, previous, "previous", self, false);
        Pins(body, anchored, "anchored", self, false);
        Pins(body, removed, "removed", self, true);
        if (inventory is not null)
        {
            foreach (string hash in inventory.Where(h => !Hashing.ContentHash.IsValid(h)).Distinct(StringComparer.Ordinal))
                body.Error("body.hash", "inventory", $"'{hash}' is not a content hash.");
            if (inventory.Distinct(StringComparer.Ordinal).Count() != inventory.Count)
                body.Error("history.checkpoint-invalid", "inventory", "An inventory names each object once.");
        }

        if (closes is not null)
        {
            closes.Enum("kind", true, [.. ClosingKinds]);
            closes.Ulid("id", true);
            closes.NoOtherKeys();
        }

        if (previous is not null && trigger is not null && (previous.Count == 0) != (trigger == "adoption"))
            body.Error("history.checkpoint-invalid", "trigger", "An estate's first checkpoint, the one that names no predecessor, is its adoption checkpoint; every later checkpoint names the heads it follows.");
        if (trigger is not null && (closes is not null) != (trigger == "closing"))
            body.Error("history.checkpoint-invalid", "closes", "A closing checkpoint names what it closes, and only a closing checkpoint does.");
        if (context.Record.Subject is not null)
            context.Error("record.subject", "A history checkpoint has no subject.");
        if (context.Record.Supersedes is not null)
            context.Error("history.checkpoint-invalid", "A history checkpoint supersedes nothing; it names the checkpoints it follows in previous.");
    }

    private static void Pins(BodyReader body, IReadOnlyList<BodyReader>? entries, string field, string self, bool reason)
    {
        if (entries is null) return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string? last = null;
        foreach (BodyReader entry in entries)
        {
            string? id = entry.Ulid("id", true);
            entry.Hash("hash", true);
            if (reason) entry.Text("reason", true, 2000);
            entry.NoOtherKeys();
            if (id is null) continue;
            if (id == self) body.Error("history.checkpoint-invalid", field, "A checkpoint cannot name itself; the next checkpoint witnesses it.");
            if (!ids.Add(id)) body.Error("history.checkpoint-invalid", field, $"{id} is listed twice.");
            else if (last is not null && string.CompareOrdinal(id, last) < 0) body.Error("history.checkpoint-invalid", field, "Entries are sorted by id.");
            last = id;
        }
    }
}
