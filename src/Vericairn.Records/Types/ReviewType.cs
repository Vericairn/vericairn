// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A review of an exact record revision, with its independence stated honestly.</summary>
public sealed class ReviewType : IRecordType
{
    /// <inheritdoc />
    public string Name => "review";

    /// <inheritdoc />
    public string Summary => "A review of an exact subject revision. Independence is checked against who authored the subject: self, independent-agent or operator.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("independence", "self|independent-agent|operator", true, "self when the reviewer authored the subject; independent-agent for a different agent principal; operator for the operator."),
        new("outcome", "pass|findings|fail", true, "The verdict."),
        new("summary", "string", true, "What was reviewed and how."),
        new("findings", "object[]", true, "severity (error|warning|info), summary, optional location; may be empty."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? independence = body.Enum("independence", true, "self", "independent-agent", "operator");
        body.Enum("outcome", true, "pass", "findings", "fail");
        body.Text("summary", true, 8000);
        foreach (BodyReader finding in body.NestedArray("findings", true, allowEmpty: true) ?? [])
        {
            finding.Enum("severity", true, "error", "warning", "info");
            finding.Text("summary", true, 2000);
            finding.Text("location", false, 500);
            finding.NoOtherKeys();
        }

        body.NoOtherKeys();
        RecordEnvelope? reviewer = context.Principal(context.Record.By);
        if (reviewer is null)
        {
            context.Error("review.author", $"Reviewing principal {context.Record.By} has no principal record.");
            return;
        }

        RecordEnvelope? subject = context.Record.Subject is null ? null : context.Find(context.Record.Subject.Id);
        if (subject is null || independence is null)
        {
            return;
        }

        bool sameAuthor = subject.By == context.Record.By;
        string reviewerKind = reviewer.Body.TryGetProperty("kind", out System.Text.Json.JsonElement kind) ? kind.GetString() ?? string.Empty : string.Empty;
        string expected = sameAuthor ? "self" : context.HasRole(context.Record.By, Roles.Operator) ? "operator" : reviewerKind == "agent" ? "independent-agent" : "independent-agent";
        if (sameAuthor && independence != "self")
        {
            context.Error("review.independence", "The reviewer authored the subject, so independence must be 'self'.");
        }
        else if (!sameAuthor && independence == "self")
        {
            context.Error("review.independence", "The reviewer did not author the subject, so independence cannot be 'self'.");
        }
        else if (independence == "operator" && !context.HasRole(context.Record.By, Roles.Operator))
        {
            context.Error("review.independence", "Only an operator-role principal can record an operator review.");
        }
        else if (independence == "independent-agent" && reviewerKind != "agent")
        {
            context.Error("review.independence", $"'independent-agent' requires an agent principal; this reviewer is a {reviewerKind}. Expected '{expected}'.");
        }
    }
}
