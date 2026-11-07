// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>
/// The optional provenance a result or an operation records (the estate's design/flow.md § 12), additive in format 3: the inputs it
/// came from, each an exact record by hash, and the provider, tool and settings it used where its other fields do not show them, which
/// a flow item's provenance requirement reads. A claim of its author like the rest of the record, and never an acceptance.
/// </summary>
internal static class ProducedBy
{
    /// <summary>The field's description, shared by results and operations.</summary>
    internal const string Description = "What produced it, where a flow item's provenance requires it and the record's other fields do not show it: inputs, each {name?, type, id, hash} of an exact record; provider; tool; and settings, string values by name. A reader lacking the field shows the record and leaves an item requiring it unsatisfied.";

    /// <summary>Validates the optional provenance field of a result or an operation.</summary>
    internal static void Read(ValidationContext context, BodyReader body)
    {
        if (!body.Since(3, "provenance") || body.Nested("provenance", false) is not BodyReader provenance)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader input in provenance.NestedArray("inputs", false, allowEmpty: false) ?? [])
        {
            string? name = input.Text("name", false, 60, Patterns.Slug, "an input name");
            string? type = input.Text("type", true, 100, Patterns.Slug, "a record type");
            string? id = input.Ulid("id", true);
            string? hash = input.Hash("hash", true);
            input.NoOtherKeys();
            if (name is not null && !names.Add(name))
            {
                input.Error("provenance.inputs", "name", "Each named input is recorded once.");
            }

            if (type is not null && context.RequireReference("provenance.inputs", id, type) is RecordEnvelope pinned && pinned.ComputedHash != hash)
            {
                input.Error("provenance.inputs", "hash", "The input does not have the recorded content hash.");
            }
        }

        provenance.Text("provider", false, 200);
        provenance.Text("tool", false, 200);
        BodyReader? settings = provenance.Nested("settings", false);
        if (settings is not null)
        {
            foreach (JsonProperty setting in settings.Element.EnumerateObject())
            {
                settings.Text(setting.Name, true, 200);
            }
        }

        provenance.NoOtherKeys();
        if (!provenance.Element.EnumerateObject().Any())
        {
            provenance.Error("provenance.empty", "inputs", "A recorded provenance names its inputs, provider, tool or settings.");
        }
    }
}
