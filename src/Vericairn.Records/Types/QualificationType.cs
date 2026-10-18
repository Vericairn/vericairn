// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A platform profile's qualification for a release, from record format 2 (DESIGN-02 decision 15; the estate's design/platforms.md).</summary>
public sealed class QualificationType : IRecordType
{
    /// <summary>What the observations establish for a profile; a non-claim is a result, not a gap.</summary>
    public static readonly ImmutableArray<string> Outcomes = ["qualified", "measured-in-part", "not-qualified", "non-claim"];

    /// <summary>What a qualification rests on.</summary>
    private static readonly string[] EvidenceTypes = ["verification", "review", "observation"];

    /// <inheritdoc />
    public string Name => "qualification";

    /// <inheritdoc />
    public string Summary => "A platform profile's qualification for a release, derived from its observations and never asserted beside them: qualified, measured in part, not qualified, or a non-claim, with the verifications, reviews and observations it rests on and what they do not establish.";

    /// <inheritdoc />
    public string? SubjectType => "release";

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public long SinceFormat => 2;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("profile", "string", true, "The platform profile, as design/platforms.md names it, for example linux-x64 or Workbench on macOS.", 2),
        new("outcome", string.Join('|', Outcomes), true, "What the evidence establishes; qualified needs evidence, a non-claim needs none.", 2),
        new("evidence", "reference[]", true, "The verifications, reviews and observations it rests on: {type, id, hash?}.", 2),
        new("summary", "string", true, "What was observed, in a sentence or two.", 2),
        new("limitations", "string[]", true, "What the evidence does not establish; may be empty.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Text("profile", true, 200);
        string? outcome = body.Enum("outcome", true, Outcomes);
        IReadOnlyList<BodyReader>? evidence = body.NestedArray("evidence", true, allowEmpty: true);
        body.Text("summary", true, 4000);
        body.StringArray("limitations", true, allowEmpty: true, maxItemLength: 2000);
        body.NoOtherKeys();
        foreach (BodyReader item in evidence ?? [])
        {
            RecordReference? reference = RecordParser.ParseReference(item.Element, item.Location, context.Findings);
            if (reference is null)
            {
                continue;
            }

            if (Array.IndexOf(EvidenceTypes, reference.Type) < 0)
            {
                item.Error("qualification.evidence", "type", "A qualification rests on verifications, reviews and observations.");
                continue;
            }

            context.RequireReference("evidence", reference.Id, reference.Type);
        }

        if (outcome is "qualified" or "measured-in-part" && evidence is { Count: 0 })
        {
            context.Error("qualification.evidence", $"A profile is {outcome} only on evidence; without any it is a non-claim.");
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("qualification.author", $"Authoring principal {context.Record.By} has no principal record.");
        }
    }
}
