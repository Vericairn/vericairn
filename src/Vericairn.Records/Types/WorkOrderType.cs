// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A bounded unit of work with an exit question.</summary>
public sealed class WorkOrderType : IRecordType
{
    /// <inheritdoc />
    public string Name => "work-order";

    /// <inheritdoc />
    public string Summary => "A bounded unit of work: intent, scope, inputs, deliverables and the one question whose answer closes it.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("key", "KEY", true, "Short upper-case key such as M0 or DESIGN-01; unique among current work orders."),
        new("kind", "milestone|ordinary", true, "Milestones need operator acceptance; ordinary work may be self-accepted under authority."),
        new("title", "string", true, "Short title."),
        new("intent", "string", true, "Why this work exists and what outcome it serves."),
        new("scope", "string[]", true, "What is in scope."),
        new("exitQuestion", "string", true, "The question whose honest 'yes' closes the work."),
        new("inputs", "reference[]", true, "Records this work builds on: {type, id}; may be empty."),
        new("deliverables", "string[]", true, "What will exist when the work is done."),
        new("after", "ulid[]", false, "Work orders this one starts after, so the next eligible work can be derived; never itself, never a cycle.", 2),
        new("blockedBy", "reference[]", false, "What holds it: asks, decisions, operations or work orders, as {type, id, hash?}.", 2),
    ];

    /// <summary>The records that can hold a work order.</summary>
    private static readonly string[] Blockers = ["ask", "decision", "operation", "work-order"];

    /// <summary>Checks the work orders this one starts after: each exists, none is this one, and following them never returns here.</summary>
    private static void After(ValidationContext context, IReadOnlyList<string>? after)
    {
        if (after is null || after.Count == 0)
        {
            return;
        }

        string root = context.Model.Chain(context.Record)[0];
        var pending = new Stack<RecordEnvelope>();
        foreach (string id in after)
        {
            RecordEnvelope? order = context.RequireReference("after", id, "work-order");
            if (order is null)
            {
                continue;
            }

            if (context.Model.Chain(order)[0] == root)
            {
                context.Error("work.after", "A work order does not start after itself.");
                return;
            }

            pending.Push(context.Model.Latest(order));
        }

        if (!context.Model.IsCurrent(context.Record))
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            RecordEnvelope order = pending.Pop();
            string orderRoot = context.Model.Chain(order)[0];
            if (orderRoot == root)
            {
                context.Error("work.after-cycle", "Following the work orders this one starts after leads back to it; the order of work has a cycle.");
                return;
            }

            if (!seen.Add(orderRoot) || !order.Body.TryGetProperty("after", out System.Text.Json.JsonElement next) || next.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                continue;
            }

            foreach (System.Text.Json.JsonElement id in next.EnumerateArray())
            {
                if (context.Find(id.GetString()) is { Type: "work-order" } successor)
                {
                    pending.Push(context.Model.Latest(successor));
                }
            }
        }
    }

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? key = body.Text("key", true, 40, Patterns.WorkKey, "an upper-case key such as M0 or DESIGN-01");
        body.Enum("kind", true, "milestone", "ordinary");
        body.Text("title", true, 200);
        body.Text("intent", true, 4000);
        body.StringArray("scope", true, allowEmpty: false, maxItemLength: 2000);
        body.Text("exitQuestion", true, 2000);
        IReadOnlyList<BodyReader>? inputs = body.NestedArray("inputs", true, allowEmpty: true);
        body.StringArray("deliverables", true, allowEmpty: false, maxItemLength: 2000);
        IReadOnlyList<string>? after = body.Since(2, "after") ? body.StringArray("after", false, allowEmpty: true, maxItemLength: Identity.Ulid.Length) : null;
        IReadOnlyList<BodyReader>? blockedBy = body.Since(2, "blockedBy") ? body.NestedArray("blockedBy", false, allowEmpty: true) : null;
        body.NoOtherKeys();
        After(context, after);
        foreach (BodyReader blocker in blockedBy ?? [])
        {
            RecordReference? reference = RecordParser.ParseReference(blocker.Element, blocker.Location, context.Findings);
            if (reference is null)
            {
                continue;
            }

            if (Array.IndexOf(Blockers, reference.Type) < 0)
            {
                blocker.Error("work.blocked-by", "type", $"A work order is held by an ask, a decision, an operation or another work order, not a {reference.Type}.");
                continue;
            }

            context.RequireReference("blockedBy", reference.Id, reference.Type);
        }
        if (inputs is not null)
        {
            foreach (BodyReader input in inputs)
            {
                RecordReference? reference = RecordParser.ParseReference(input.Element, input.Location, context.Findings);
                if (reference is not null)
                {
                    context.RequireReference("inputs", reference.Id, reference.Type);
                }
            }
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("work.author", $"Authoring principal {context.Record.By} has no principal record.");
        }

        if (key is not null && context.Model.IsCurrent(context.Record))
        {
            bool duplicate = context.Model.Current(Name).Any(other => other.Id != context.Record.Id && other.Body.TryGetProperty("key", out System.Text.Json.JsonElement k) && k.GetString() == key);
            if (duplicate)
            {
                context.Error("work.duplicate-key", $"Another current work order already uses key {key}.");
            }
        }
    }
}
