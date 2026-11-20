// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Estate;
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
        new("raisedBy", "object", false, "From 0.9.4, a standing agent's output: goal (the standing agent's goal, its first revision), occurrence (the occurrence key), assignment (the assignment carrying it, whose contributor records it) and, for an agent, run (the agent run it records it in, in flight). Raised open, with a fingerprint and a source; within the occurrence's caps; no second open one of the agent with the same fingerprint; disposed of only by another principal, never by an agent that carried the standing agent's occurrences.", 3),
        new("fingerprint", "string", false, "From 0.9.4, what makes two possibilities the same, by the flow's rule: lower-case letters, digits and . : / _ -, up to 200 characters.", 3),
        new("sources", "object[]", false, "From 0.9.4, where it was found or how it reproduces: each an address with optional retrievedAt, content hash and the extents relied on.", 3),
        new("evidence", "object[]", false, "From 0.9.4, evidence for and against, with no scores: each a stance (for|against), a statement and optionally the sources it rests on, by index.", 3),
        new("reviewTriggers", "object[]", false, "From 0.9.4, what calls for it to be read again: each a time (at) or a condition.", 3),
        new("links", "object[]", false, "From 0.9.4, other possibilities it relates to: each a relation (duplicate-of|supersedes), a possibility id and its exact hash.", 3),
        new("draft", "object", false, "From 0.9.4, proposed work as a draft work order (key?, title, intent, scope, deliverables, exitQuestion), adopted by work new --from-possibility by a principal other than its author.", 3),
        new("disclosure", "held", false, "From 0.9.4, a held finding: kept out of every outward package and adopted only by an operator who is a person.", 3),
        new("extras", "string[]", false, "From 0.9.4, a standing occurrence's summary possibility: the further ideas past its cap, one line each; one per occurrence.", 3),
        new("raisesAgain", "ulid", false, "From 0.9.4, the declined possibility of the same standing agent and fingerprint that this one raises again, with new evidence.", 3),
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
        StandingOutputs.RaisedBy? raised = null;
        if (body.Since(3, "raisedBy"))
        {
            raised = StandingOutputs.ValidateRaisedBy(context, body.Nested("raisedBy", false));
        }

        if (body.Since(3, "fingerprint")) StandingOutputs.Fingerprint(body);
        if (body.Since(3, "sources")) StandingOutputs.Sources(body);
        if (body.Since(3, "evidence")) StandingOutputs.Evidence(body);
        if (body.Since(3, "reviewTriggers")) StandingOutputs.ReviewTriggers(body);
        if (body.Since(3, "links")) StandingOutputs.Links(context, body);
        if (body.Since(3, "draft")) StandingOutputs.Draft(body);
        if (body.Since(3, "disclosure")) body.Enum("disclosure", false, "held");
        if (body.Since(3, "extras")) StandingOutputs.Extras(body);
        if (body.Since(3, "raisesAgain")) body.Ulid("raisesAgain", false);
        body.NoOtherKeys();
        if (context.Format >= 3)
        {
            StandingOutputs.ValidatePossibility(context, raised, disposition);
        }

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
