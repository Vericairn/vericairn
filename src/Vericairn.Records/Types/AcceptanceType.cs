// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>Acceptance of an exact subject revision by a principal allowed to accept it.</summary>
public sealed class AcceptanceType : IRecordType
{
    /// <inheritdoc />
    public string Name => "acceptance";

    /// <inheritdoc />
    public string Summary => "Acceptance of an exact subject revision: a work order, or a result toward one. Milestones and their results need the operator; ordinary work may be self-accepted only under an explicit authority scope.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("statement", "string", true, "What is accepted and on what basis."),
        new("basis", "ulid[]", true, "Review records of the same subject that the acceptance relies on; may be empty for operator acceptance."),
        new("self", "boolean", true, "True when the accepting principal authored the subject; must match the facts."),
    ];

    /// <summary>The subject types an acceptance may have: a work order, or a result toward one.</summary>
    public static readonly string[] SubjectTypes = ["work-order", "result"];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Text("statement", true, 8000);
        IReadOnlyList<string>? basis = body.StringArray("basis", true, allowEmpty: true, maxItemLength: Identity.Ulid.Length);
        bool? self = body.Flag("self", true);
        body.NoOtherKeys();
        if (context.Principal(context.Record.By) is null)
        {
            context.Error("acceptance.author", $"Accepting principal {context.Record.By} has no principal record.");
            return;
        }

        RecordEnvelope? subject = context.Record.Subject is null ? null : context.Find(context.Record.Subject.Id);
        if (subject is null)
        {
            return;
        }

        foreach (string id in basis ?? [])
        {
            RecordEnvelope? review = context.RequireReference("basis", id, "review");
            if (review is not null && review.Subject?.Id != subject.Id)
            {
                context.Error("acceptance.basis", $"Review {id} is not a review of this subject.");
            }
        }

        bool sameAuthor = subject.By == context.Record.By;
        if (self is not null && self.Value != sameAuthor)
        {
            context.Error("acceptance.self", sameAuthor ? "The accepting principal authored the subject; 'self' must be true." : "The accepting principal did not author the subject; 'self' must be false.");
        }

        bool isOperator = context.HasRole(context.Record.By, Roles.Operator);
        RecordEnvelope? order = subject.Type == "work-order" ? subject : subject.Type == "result" ? WorkView.OrderOfResult(context.Model, subject) : null;
        if (order is not null)
        {
            string kind = order.Body.TryGetProperty("kind", out System.Text.Json.JsonElement k) ? k.GetString() ?? "milestone" : "milestone";
            if (kind == "milestone" && !isOperator)
            {
                context.Error("acceptance.policy", subject.Type == "result" ? "A result toward a milestone work order is accepted by the operator." : "A milestone work order is accepted by the operator.");
            }
            else if (kind == "ordinary" && !isOperator)
            {
                bool allowed = sameAuthor ? context.Model.HasScope(context.Record.By, Scopes.SelfAcceptOrdinaryWork) : context.Model.HasScope(context.Record.By, Scopes.AcceptWork);
                if (!allowed)
                {
                    context.Error("acceptance.policy", sameAuthor ? "Self-acceptance of ordinary work needs the 'self-accept-ordinary-work' authority scope." : "Accepting another principal's work needs the 'accept-work' authority scope.");
                }
            }
        }
        else if (!isOperator)
        {
            context.Error("acceptance.policy", $"Acceptance of a {subject.Type} record is reserved to the operator in record format 1.");
        }

        if (context.Model.Latest(subject).Id != subject.Id)
        {
            context.Warning("acceptance.unresolved", "The subject was superseded after this acceptance; it accepts an earlier revision and needs reconsideration.");
        }
    }
}
