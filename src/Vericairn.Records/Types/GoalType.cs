// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>An objective the estate carries out with agents and people, from record format 2 (the estate's design/goals.md § 4).</summary>
public sealed class GoalType : IRecordType
{
    /// <summary>A finite goal ends when it is done; a stewardship goal holds a standing condition.</summary>
    public static readonly string[] Kinds = ["finite", "stewardship"];

    /// <summary>Who plans the work: a planner proposes work orders, or the sponsor gives them.</summary>
    public static readonly string[] PlanPolicies = ["proposed-by-planner", "given"];

    /// <summary>Who approves a proposed plan.</summary>
    public static readonly string[] Approvals = ["sponsor-approves", "self-approves-ordinary"];

    /// <inheritdoc />
    public string Name => "goal";

    /// <inheritdoc />
    public string Summary => "An objective the estate carries out with agents and people: what done looks like, its constraints, budget, roles and sponsor. Its sponsor activates it, and it is done only when the records and an independent evaluation show it and the sponsor accepts it.";

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
        new("objective", "string", true, "What the sponsor wants, in the sponsor's words.", 2),
        new("completion", "object[]", true, "The conditions that make it done, at least one: a record predicate {record: {type, subject?, product?, fields?, state?, min?, max?}}, checked from current records, counts the current records of the type about the subject (any revision of it), of the product (living documents), with each body field equal to its string and in the state status derives (ask, living-document, work-order), and holds when the count is within min and max (at least one when neither is given); an evidence statement {evidence, check} is judged by an evaluator who did none of the work.", 2),
        new("constraints", "string[]", true, "What must not change or happen; may be empty.", 2),
        new("kind", string.Join('|', Kinds), true, "finite ends when done; stewardship holds a standing condition.", 2),
        new("sponsor", "ulid", true, "The accountable principal, who activates, stops and accepts it.", 2),
        new("roster", "ulid", false, "The roster record whose roles it uses; the estate's current roster otherwise.", 2),
        new("roles", "object[]", false, "Role overrides for this goal: role, principals in order of preference, optional note.", 2),
        new("budget", "object", true, "Caps by unit, granted by the sponsor's activation; wallMinutes and attemptsPerStep are required. Units: " + Caps.Units + ".", 2),
        new("plan", "object", true, "policy (proposed-by-planner|given), approval (sponsor-approves|self-approves-ordinary), and workOrders (ulid[]) for a given plan.", 2),
        new("limits", "object", false, "Stop conditions: consecutiveFailures (a positive integer) and deadline (a timestamp).", 2),
        new("inputs", "reference[]", true, "Decisions, work orders and designs it builds on: {type, id, hash?}; may be empty.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Text("title", true, 200);
        body.Text("objective", true, 8000);
        IReadOnlyList<BodyReader>? completion = body.NestedArray("completion", true, allowEmpty: false);
        body.StringArray("constraints", true, allowEmpty: true, maxItemLength: 2000);
        body.Enum("kind", true, Kinds);
        string? sponsor = body.Ulid("sponsor", true);
        string? roster = body.Ulid("roster", false);
        IReadOnlyList<BodyReader>? roles = body.NestedArray("roles", false, allowEmpty: false);
        BodyReader? budget = body.Nested("budget", true);
        BodyReader? plan = body.Nested("plan", true);
        BodyReader? limits = body.Nested("limits", false);
        IReadOnlyList<BodyReader>? inputs = body.NestedArray("inputs", true, allowEmpty: true);
        body.NoOtherKeys();
        foreach (BodyReader condition in completion ?? [])
        {
            Condition(context, condition);
        }

        context.RequireReference("sponsor", sponsor, "principal");
        context.RequireReference("roster", roster, "roster");
        if (roles is not null)
        {
            RosterType.Roles(context, roles, "roles");
        }

        if (budget is not null)
        {
            Caps.Read(budget, "wallMinutes", "attemptsPerStep");
        }

        if (plan is not null)
        {
            string? policy = plan.Enum("policy", true, PlanPolicies);
            plan.Enum("approval", true, Approvals);
            IReadOnlyList<string>? orders = plan.StringArray("workOrders", policy == "given", allowEmpty: policy != "given", maxItemLength: Identity.Ulid.Length);
            plan.NoOtherKeys();
            foreach (string id in orders ?? [])
            {
                context.RequireReference("plan.workOrders", id, "work-order");
            }
        }

        if (limits is not null)
        {
            limits.Number("consecutiveFailures", false, 1, 1000);
            limits.Timestamp("deadline", false);
            limits.NoOtherKeys();
        }

        foreach (BodyReader input in inputs ?? [])
        {
            RecordReference? reference = RecordParser.ParseReference(input.Element, input.Location, context.Findings);
            if (reference is not null)
            {
                context.RequireReference("inputs", reference.Id, reference.Type);
            }
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("goal.author", $"Authoring principal {context.Record.By} has no principal record.");
        }
    }

    /// <summary>A completion condition: a record predicate the runtime checks, or an evidence statement an evaluator judges.</summary>
    private static void Condition(ValidationContext context, BodyReader condition)
    {
        bool record = condition.Has("record");
        bool evidence = condition.Has("evidence");
        if (record == evidence)
        {
            condition.Error("goal.condition", "record", "A condition is either a record predicate or an evidence statement with its check, never both or neither.");
        }

        BodyReader? predicate = condition.Nested("record", false);
        if (predicate is not null)
        {
            Predicate(context, predicate);
        }

        condition.Text("evidence", false, 2000);
        condition.Text("check", evidence, 2000);
        condition.NoOtherKeys();
    }

    /// <summary>A record predicate (the estate's design/goals.md § 5): type, subject, product, field equalities, derived state, and bounds.</summary>
    private static void Predicate(ValidationContext context, BodyReader predicate)
    {
        string? type = predicate.Text("type", true, 100, Patterns.Slug, "a record type");
        if (type is not null && RecordTypeRegistry.Find(type) is null)
        {
            predicate.Error("goal.predicate", "type", $"'{type}' is not a record type this build reads.");
        }

        // The subject is any record, followed across its revisions; the product names the product whose living documents count.
        string? subject = predicate.Ulid("subject", false);
        if (subject is not null && context.Find(subject) is null)
        {
            predicate.Error("body.reference", "subject", $"'subject' references {subject}, which does not exist in this estate.");
        }

        string? product = predicate.Ulid("product", false);
        if (product is not null && type is not null && type != "living-document")
        {
            predicate.Error("goal.predicate", "product", "'product' names the product whose living documents are counted; it goes with type living-document.");
        }
        else if (product is not null && context.Find(product) is not { Type: "product" })
        {
            predicate.Error("body.reference", "product", $"'product' references {product}, which is not a product record of this estate.");
        }

        BodyReader? fields = predicate.Nested("fields", false);
        if (fields is not null)
        {
            foreach (JsonProperty field in fields.Element.EnumerateObject())
            {
                fields.Allow(field.Name);
                if (field.Value.ValueKind != JsonValueKind.String)
                {
                    fields.Error("body.type", field.Name, "A predicate compares a body field with a string value.");
                }
            }
        }

        string? state = predicate.Text("state", false, 100);
        if (state is not null && type is not null && RecordTypeRegistry.Find(type) is not null)
        {
            if (!GoalPredicates.States.TryGetValue(type, out IReadOnlyList<string>? states))
            {
                predicate.Error("goal.predicate", "state", $"'state' is the state status derives, for {string.Join(", ", GoalPredicates.States.Keys)} records; a {type} record's own fields are compared with 'fields'.");
            }
            else if (!states.Contains(state, StringComparer.Ordinal))
            {
                predicate.Error("goal.predicate", "state", $"The state of a {type} record is one of: {string.Join(", ", states)}.");
            }
        }

        long? min = predicate.Number("min", false, 0, 1_000_000);
        long? max = predicate.Number("max", false, 0, 1_000_000);
        if (min is not null && max is not null && max < min)
        {
            predicate.Error("goal.predicate", "max", "'max' is at least 'min'.");
        }

        predicate.NoOtherKeys();
    }
}
