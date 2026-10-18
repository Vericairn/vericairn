// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>An idea captured with its disposition, explicitly not an approval, from record format 2 (DESIGN-02 decision 10).</summary>
public sealed class PossibilityType : IRecordType
{
    /// <summary>What has become of an idea.</summary>
    public static readonly ImmutableArray<string> Dispositions = ["open", "parked", "adopted", "declined"];

    /// <summary>What an adopted idea can become.</summary>
    private static readonly string[] AdoptedAs = ["work-order", "decision", "goal"];

    /// <inheritdoc />
    public string Name => "possibility";

    /// <inheritdoc />
    public string Summary => "An idea captured with its disposition, explicitly not an approval: what it is, where it came from, and whether it is open, parked, adopted (with the work order, decision or goal it became) or declined, and why.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public long SinceFormat => 2;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("title", "string", true, "Short title.", 2),
        new("idea", "string", true, "The idea, as it was raised.", 2),
        new("origin", "string", true, "Who raised it and where, without machine paths.", 2),
        new("disposition", string.Join('|', Dispositions), true, "What has become of it; a revision records a new disposition.", 2),
        new("reason", "string", false, "Why it was parked, adopted or declined; required unless it is open.", 2),
        new("adoptedAs", "reference", false, "The work order, decision or goal it became: {type, id, hash?}; required when adopted.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Text("title", true, 200);
        body.Text("idea", true, 8000);
        body.Text("origin", true, 2000);
        string? disposition = body.Enum("disposition", true, Dispositions);
        body.Text("reason", disposition is not null and not "open", 4000);
        BodyReader? adopted = body.Nested("adoptedAs", disposition == "adopted");
        body.NoOtherKeys();
        if (adopted is not null)
        {
            RecordReference? reference = RecordParser.ParseReference(adopted.Element, adopted.Location, context.Findings);
            if (reference is not null && Array.IndexOf(AdoptedAs, reference.Type) < 0)
            {
                adopted.Error("possibility.adopted-as", "type", "An adopted idea becomes a work order, a decision or a goal.");
            }
            else if (reference is not null)
            {
                context.RequireReference("adoptedAs", reference.Id, reference.Type);
            }
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("possibility.author", $"Authoring principal {context.Record.By} has no principal record.");
        }
    }
}
