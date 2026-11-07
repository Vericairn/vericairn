// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>Delegates a work order to a contributor under a brief, pinned to the exact work-order revision.</summary>
public sealed class AssignmentType : IRecordType
{
    /// <inheritdoc />
    public string Name => "assignment";

    /// <inheritdoc />
    public string Summary => "Delegates a work order to a contributor under a brief (outcome, sources, permitted changes, prohibited effects, workspace, limits, expected outputs, review criteria, recovery), pinned to the exact work-order revision.";

    /// <inheritdoc />
    public string? SubjectType => "work-order";

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        SavedPrompts.UseField,
        new("execution", "object", false, "Explicit execution choices and cumulative restrictions; launcher, roster, goal, assignment, then run precedence. Role restrictions remain binding.", 2),
        new("contributor", "ulid", true, "The principal that does the work: a human, a deterministic tool, a native agent or a qualified external agent. Admitted before the work is assigned."),
        new("brief", "object", true, "outcome, sources (references {type, id, hash?}), permittedChanges, prohibitedEffects, workspace (allowed trees, named without machine paths), limits, expectedOutputs, reviewCriteria, recovery; from format 3, optional reviewBrief retains the complete release review text and optional progressRequest (types, statusText, cadence) replaces broader progress requests."),
        new("note", "string", false, "Free text."),
        new("step", "object", false, "For an assignment of a run's work order, the flow step it carries: key, instance for a step that runs over a list under each, and optional inputs, each input of the step its brief pins by name and the exact record {type, id, hash} that carries it. Every assignment of a run's work order names its step, and only those do. A review before hand-off adds review: the items it reviews (none for a step that declares no items) and the exact subject {type, id, hash} that carries them; its contributor is never an author of the step's work unless every item takes a self review.", 3),
        new("routedWith", string.Join('|', RoutedWith), false, "Set by routing on an assignment it stores together with the record that carries it: run, the intent of the agent run on it (an agent-run operation whose subject is this assignment), or ask, the ask that names it. Until that record exists the assignment holds nothing of its step, so an interrupted store never holds a step.", 2),
    ];

    /// <summary>The records routing stores an assignment together with.</summary>
    public static readonly ImmutableArray<string> RoutedWith = ["run", "ask"];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? contributor = body.Ulid("contributor", true);
        BodyReader? brief = body.Nested("brief", true);
        if (brief is not null)
        {
            brief.Text("outcome", true, 4000);
            foreach (BodyReader source in brief.NestedArray("sources", true, allowEmpty: true) ?? [])
            {
                RecordReference? reference = RecordParser.ParseReference(source.Element, source.Location, context.Findings);
                if (reference is null)
                {
                    continue;
                }

                RecordEnvelope? target = context.RequireReference("brief.sources", reference.Id, reference.Type);
                if (target is not null && reference.Hash is not null && reference.Hash != target.ComputedHash)
                {
                    context.Findings.Error("assignment.source-hash", source.Location, $"Source {reference.Id} does not have the pinned content hash; the brief relied on a different revision.");
                }
            }

            brief.StringArray("permittedChanges", true, allowEmpty: false, maxItemLength: 2000);
            brief.StringArray("prohibitedEffects", true, allowEmpty: true, maxItemLength: 2000);
            brief.StringArray("workspace", true, allowEmpty: false, maxItemLength: 500);
            brief.StringArray("limits", true, allowEmpty: true, maxItemLength: 2000);
            brief.StringArray("expectedOutputs", true, allowEmpty: false, maxItemLength: 2000);
            brief.StringArray("reviewCriteria", true, allowEmpty: false, maxItemLength: 2000);
            brief.Text("recovery", true, 4000);
            if (context.Format >= 3)
                brief.Text("reviewBrief", false, 1_000_000);
            ProgressReporting.Request(brief);
            brief.NoOtherKeys();
        }

        body.Text("note", false, 4000);
        BodyReader? step = body.Since(3, "step") ? body.Nested("step", false) : null;
        if (step is not null)
        {
            step.Text("key", true, 100, Patterns.Slug, "a step key");
            step.Text("instance", false, 100, Patterns.Slug, "an instance name");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyReader input in step.NestedArray("inputs", false, allowEmpty: false) ?? [])
            {
                string? name = input.Text("name", true, 60, Patterns.Slug, "an input name");
                string? type = input.Text("type", true, 100, Patterns.Slug, "a record type");
                string? id = input.Ulid("id", true);
                string? hash = input.Hash("hash", true);
                input.NoOtherKeys();
                if (name is not null && !names.Add(name))
                {
                    input.Error("assignment.step", "name", "Each input is pinned once.");
                }

                if (type is not null && context.RequireReference("step.inputs", id, type) is RecordEnvelope pinned && pinned.ComputedHash != hash)
                {
                    input.Error("assignment.step", "hash", $"Input {name} does not have the pinned content hash; the brief relied on a different revision.");
                }
            }

            // A review before hand-off (design/flow.md § 6): the items it reviews and the exact subject that carries them.
            BodyReader? review = step.Nested("review", false);
            if (review is not null)
            {
                IReadOnlyList<string>? items = review.StringArray("items", false, allowEmpty: false, maxItemLength: 60, itemPattern: Patterns.Slug, patternHint: "an item name");
                if (items is not null && items.Distinct(StringComparer.Ordinal).Count() != items.Count)
                {
                    review.Error("assignment.step", "items", "Each item is reviewed once.");
                }

                BodyReader? subject = review.Nested("subject", true);
                if (subject is not null)
                {
                    string? type = subject.Text("type", true, 100, Patterns.Slug, "a record type");
                    string? id = subject.Ulid("id", true);
                    string? hash = subject.Hash("hash", true);
                    subject.NoOtherKeys();
                    if (type is not null && context.RequireReference("step.review.subject", id, type) is RecordEnvelope reviewed && reviewed.ComputedHash != hash)
                    {
                        subject.Error("assignment.step", "hash", "The reviewed subject does not have the pinned content hash; a review names the exact revision it reviews.");
                    }
                }

                review.NoOtherKeys();
            }

            step.NoOtherKeys();
        }

        if (body.Since(2, "routedWith"))
        {
            body.Enum("routedWith", false, RoutedWith);
        }

        SavedPrompts.ValidateUse(context);
        ExecutionPolicy.Read(body);
        body.NoOtherKeys();
        string by = context.Record.By;
        RecordEnvelope? order = context.Find(context.Record.Subject?.Id) is { Type: "work-order" } named ? named : null;
        RecordEnvelope? handedOver = context.Format >= 2 && order is not null ? Routing.TakesOverFrom(context.Model, context.Record) : null;
        if (context.Principal(by) is null)
        {
            context.Error("assignment.author", $"Assigning principal {by} has no principal record.");
        }
        else if (handedOver is not null)
        {
            // A hand-over, from record format 2: a person takes over a step an agent holds (design/goals.md § 6, step 4). It is recorded
            // by a person, the one taking the step over, who decides for its goal or an operator, and never by an agent.
            if (!Routing.MayHandOver(context.Model, by, contributor, order!, context.Record))
            {
                context.Error("assignment.handover", $"This assignment takes work order {EstateModel.BodyString(order!, "key") ?? order!.Id} over from the agent's assignment {handedOver.Id}: a hand-over is recorded by a person, the one taking the step over, the sponsor of its goal or an operator, and never by an agent.");
            }
        }
        else if (!context.HasRole(by, Roles.Operator) && !context.HasScope(by, Scopes.LocalEngineering)
            && !(context.Format >= 2 && order is not null && Routing.DecidesForStep(context.Model, by, order, context.Record)))
        {
            context.Error("assignment.author", "Work is assigned by the operator, by a principal granted the 'local-engineering' scope, or, for a step of a goal, by the goal's sponsor when the sponsor is a person.");
        }

        if (context.Format >= 3 && order is not null)
        {
            FlowRuns.ValidateStep(context, order, step);
        }

        if (contributor is not null && context.Principal(contributor) is null)
        {
            context.Error("assignment.contributor", $"Contributor {contributor} has no principal record; contributors are admitted before work is assigned to them.");
        }
    }
}
