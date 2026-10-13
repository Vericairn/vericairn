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
    ];

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
        body.NoOtherKeys();
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
