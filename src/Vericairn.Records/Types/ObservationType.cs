// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Records.Types;

/// <summary>Something reported or measured about an exact subject, with an as-of time; not health unless it says so.</summary>
public sealed class ObservationType : IRecordType
{
    /// <summary>What kinds of subject are observed so far.</summary>
    public static readonly string[] Kinds = ["repository", "release", "workflow-run"];

    /// <inheritdoc />
    public string Name => "observation";

    /// <inheritdoc />
    public string Summary => "Something reported or measured about an exact subject with an as-of time: a repository's commit, branch, remote and working-tree state; a release's assets; a workflow run. Facts, never permission or health.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("kind", string.Join('|', Kinds), true, "What was observed."),
        new("asOf", "timestamp", true, "When the observation was made."),
        new("summary", "string", true, "The observation in a sentence."),
        new("facts", "object", true, "Flat facts: strings, numbers, booleans or arrays of strings; never machine paths."),
        new("limitations", "string[]", true, "What the observation does not establish; may be empty."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Enum("kind", true, Kinds);
        body.Timestamp("asOf", true);
        body.Text("summary", true, 4000);
        BodyReader? facts = body.Nested("facts", true);
        if (facts is not null)
        {
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
    }
}
