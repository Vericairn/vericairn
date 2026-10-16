// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;

namespace Vericairn.Records.Types;

/// <summary>Something reported or measured about an exact subject, with an as-of time; not health unless it says so.</summary>
public sealed class ObservationType : IRecordType
{
    /// <summary>What kinds of subject are observed so far.</summary>
    public static readonly string[] Kinds = ["repository", "release", "workflow-run", "estate"];

    /// <inheritdoc />
    public string Name => "observation";

    /// <inheritdoc />
    public string Summary => "Something reported or measured about an exact subject with an as-of time: a repository, release or workflow run; from record format 2, a person's explicit observation of an estate. Facts, never permission or health.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("kind", string.Join('|', Kinds), true, "What was observed; estate requires record format 2, a human author and an exact estate subject."),
        new("asOf", "timestamp", true, "When the observation was made."),
        new("summary", "string", true, "The observation in a sentence."),
        new("facts", "object", true, "Flat facts: strings, numbers, booleans or arrays of strings; never machine paths. An estate observation requires records (count), recordSetHash, pinHash and statusHash."),
        new("limitations", "string[]", true, "What the observation does not establish; may be empty."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? kind = body.Enum("kind", true, context.Format >= 2 ? Kinds : ["repository", "release", "workflow-run"]);
        string? asOf = body.Timestamp("asOf", true);
        body.Text("summary", true, 4000);
        BodyReader? facts = body.Nested("facts", true);
        if (facts is not null)
        {
            if (kind == "estate")
            {
                facts.Number("records", true, min: 0);
                facts.Hash("recordSetHash", true);
                facts.Hash("pinHash", true);
                facts.Hash("statusHash", true);
            }

            foreach (JsonProperty property in facts.Element.EnumerateObject())
            {
                facts.Allow(property.Name);
                bool ok = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString()!.Length <= 2000,
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => true,
                    JsonValueKind.Array => property.Value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String && v.GetString()!.Length <= 2000),
                    _ => false,
                };
                if (!ok)
                {
                    facts.Error("body.type", property.Name, "Facts are strings, numbers, booleans or arrays of strings.");
                }
            }

            facts.NoOtherKeys();
        }

        body.StringArray("limitations", true, allowEmpty: true, maxItemLength: 2000);
        body.NoOtherKeys();
        if (context.Principal(context.Record.By) is null)
        {
            context.Error("observation.author", $"Observing principal {context.Record.By} has no principal record.");
        }

        if (kind == "estate")
        {
            if (context.Principal(context.Record.By) is not { } person || EstateModel.BodyString(person, "kind") != "human")
            {
                context.Error("observation.person", "An estate observation is authored by a person; automation cannot advance a person's last observation.");
            }

            if (context.Record.Subject?.Type != "estate")
            {
                context.Error("observation.estate", "An estate observation pins an estate charter as its subject.");
            }

            if (asOf is not null && string.CompareOrdinal(asOf, context.Record.At) > 0)
            {
                context.Error("observation.future", "An estate observation cannot report seeing a future state.");
            }
        }
    }
}
