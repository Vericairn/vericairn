// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

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
        if (context.Model.Current("living-document").Any(d => GeneratedDocumentContract.IsStrict(context.Model, d)
            && EstateModel.BodyString(d, "repository") is null && GeneratedDocumentContract.RetirementPath(d) == path))
        {
            if (context.Format < 3 || !SignatureChecks.IsAuthentic(context.Model, context.Record)
                || (!context.HasRole(context.Record.By, Roles.Operator) && !context.HasScope(context.Record.By, Scopes.LocalEngineering)))
                context.Error("retirement.generated-author", "Retiring generated ownership requires a signed format-3 record by the operator or under local-engineering authority.");
            if (context.Record.Supersedes is { } previous && context.Find(previous) is { } prior
                && EstateModel.BodyString(prior, "retiredAt") is not null && EstateModel.BodyString(context.Record, "retiredAt") is null)
                context.Error("retirement.generated-reopen", "Retired generated ownership cannot be silently reactivated.");
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)*$")]
    private static partial Regex RelativePathRegex();
}
