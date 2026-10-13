// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>One attributable contribution toward an assignment, authored by its contributor.</summary>
public sealed class AttemptType : IRecordType
{
    /// <inheritdoc />
    public string Name => "attempt";

    /// <inheritdoc />
    public string Summary => "One attributable contribution toward an assignment, authored by the assignment's contributor and pinned to the exact brief. A result record closes it; several attempts can exist without erasing one another.";

    /// <inheritdoc />
    public string? SubjectType => "assignment";

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("startedAt", "timestamp", true, "When the attempt began."),
        new("workspace", "string", true, "Where the work happens, named without machine-specific paths: a repository and branch, a clone, a tool."),
        new("note", "string", true, "What the contributor intends to do."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Timestamp("startedAt", true);
        body.Text("workspace", true, 500);
        body.Text("note", true, 4000);
        body.NoOtherKeys();
        if (context.Principal(context.Record.By) is null)
        {
            context.Error("attempt.author", $"Attempting principal {context.Record.By} has no principal record.");
            return;
        }

        RecordEnvelope? assignment = context.Record.Subject is null ? null : context.Find(context.Record.Subject.Id);
        if (assignment is not null && assignment.Type == "assignment")
        {
            string? contributor = EstateModel.BodyString(assignment, "contributor");
            if (contributor != context.Record.By)
            {
                context.Error("attempt.contributor", $"An attempt is authored by the assignment's contributor ({contributor}), not by {context.Record.By}.");
            }
        }
    }
}
