// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>One completed phase claimed by an attempt's contributor, never an observation or acceptance.</summary>
public sealed class WorkProgressType : IRecordType
{
    /// <inheritdoc />
    public string Name => "work-progress";

    /// <inheritdoc />
    public string Summary => "The contributor's claim about one completed phase of an attempt. At most one record per phase; interim notes stay in live state. Neither liveness nor acceptance.";

    /// <inheritdoc />
    public string? SubjectType => "attempt";

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public long SinceFormat => 2;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("phase", "string", true, "A stable lower-case phase name, reused by its interim notes.", 2),
        new("note", "string", true, "The contributor's claim about the phase, never proof of liveness or scope.", 2),
        new("completedAt", "timestamp", true, "When the contributor claimed the phase completed.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string? phase = context.Body.Text("phase", true, 100, Patterns.Slug, "a stable lower-case phase name");
        context.Body.Text("note", true, 2000);
        context.Body.Timestamp("completedAt", true);
        context.Body.NoOtherKeys();
        if (context.Find(context.Record.Subject?.Id) is { Type: "attempt" } attempt && attempt.By != context.Record.By)
        {
            context.Error("work-progress.contributor", "Only the principal that made the attempt claims its progress.");
        }

        if (context.Record.Supersedes is not null || context.Model.Records.Any(r => r.Type == Name && r.Id != context.Record.Id
            && r.Subject?.Id == context.Record.Subject?.Id && EstateModel.BodyString(r, "phase") == phase))
        {
            context.Error("work-progress.duplicate", "An attempt records a completed phase once; interim notes stay in live state.");
        }
    }
}
