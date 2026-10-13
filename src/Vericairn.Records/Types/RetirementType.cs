// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;

namespace Vericairn.Records.Types;

/// <summary>Registers a bootstrap-only or temporary artifact and the condition for removing it.</summary>
public sealed partial class RetirementType : IRecordType
{
    /// <inheritdoc />
    public string Name => "retirement";

    /// <inheritdoc />
    public string Summary => "Registers a temporary artifact (path) with the condition for its removal; once retiredAt is set the path must be gone.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("path", "path", true, "Repository-relative path of the temporary artifact."),
        new("reason", "string", true, "Why it exists."),
        new("condition", "string", true, "When it must be removed."),
        new("retiredAt", "timestamp", false, "Set by the superseding record once the artifact is removed."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? path = body.Text("path", true, 500, RelativePathRegex(), "a normalized repository-relative path");
        body.Text("reason", true, 2000);
        body.Text("condition", true, 2000);
        body.Timestamp("retiredAt", false);
        body.NoOtherKeys();
        if (path is not null && (path.StartsWith('/') || path.Contains("..", StringComparison.Ordinal) || path.Contains('\\', StringComparison.Ordinal)))
        {
            body.Error("body.pattern", "path", "The path must be relative, forward-slashed and normalized.");
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("retirement.author", $"Authoring principal {context.Record.By} has no principal record.");
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)*$")]
    private static partial Regex RelativePathRegex();
}
