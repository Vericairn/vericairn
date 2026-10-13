// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

namespace Vericairn.Records.Types;

/// <summary>A status change of a work order.</summary>
public sealed class WorkStatusType : IRecordType
{
    /// <summary>Allowed statuses; 'accepted' is never claimed here, it is derived from acceptance records.</summary>
    public static readonly string[] Statuses = ["planned", "active", "paused", "submitted", "retired"];

    /// <inheritdoc />
    public string Name => "work-status";

    /// <inheritdoc />
    public string Summary => "A status change of a work order: planned, active, paused, submitted or retired. Acceptance is a separate record.";

    /// <inheritdoc />
    public string? SubjectType => "work-order";

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("status", string.Join('|', Statuses), true, "The new status."),
        new("note", "string", true, "Why, and what remains."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Enum("status", true, Statuses);
        body.Text("note", true, 4000);
        body.NoOtherKeys();
        if (context.Principal(context.Record.By) is null)
        {
            context.Error("work.author", $"Authoring principal {context.Record.By} has no principal record.");
        }
    }
}
