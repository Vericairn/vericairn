// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>Acceptance of an exact subject revision by a principal allowed to accept it.</summary>
public sealed class AcceptanceType : IRecordType
{
    /// <inheritdoc />
    public string Name => "acceptance";

    /// <inheritdoc />
    public string Summary => "Acceptance of an exact subject revision: a work order, or a result toward one; a goal; or another record, such as a release candidate, which only the operator accepts. Milestones and their results need the operator; ordinary work may be self-accepted only under an explicit authority scope. A goal is accepted by its sponsor when the sponsor is a person and by an operator when an agent sponsors it, after its sponsor activated it, relying on its current evaluation, which finds every condition met, and never by that evaluation's author or by the author of one of its steps.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("statement", "string", true, "What is accepted and on what basis."),
        new("basis", "ulid[]", true, "Review records of the same subject that the acceptance relies on; may be empty for operator acceptance. A goal's acceptance names the goal's current evaluation."),
        new("self", "boolean", true, "True when the accepting principal authored the subject; must match the facts."),
    ];

    /// <summary>The subject types whose acceptance follows rules of their own: a work order, a result toward one, and a goal.</summary>
    public static readonly string[] SubjectTypes = ["work-order", "result", "goal"];

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
        if (subject.Type == "goal")
        {
            // Who accepts a goal is one rule (GoalRules.AccepterOf); the rest is the same for every goal (design/goals.md § 5).
            foreach ((string code, string message) in GoalRules.AcceptanceRefusals(context.Model, subject, context.Record.By, basis ?? [], context.Record))
            {
                context.Error(code, message);
            }
        }
        else if (order is not null)
        {
            string kind = order.Body.TryGetProperty("kind", out System.Text.Json.JsonElement k) ? k.GetString() ?? "milestone" : "milestone";
            if (kind == "milestone" && !isOperator)
            {
                context.Error("acceptance.policy", subject.Type == "result" ? "A result toward a milestone work order is accepted by the operator." : "A milestone work order is accepted by the operator.");
            }
            else if (kind == "ordinary" && !isOperator)
            {
                bool allowed = sameAuthor ? context.HasScope(context.Record.By, Scopes.SelfAcceptOrdinaryWork) : context.HasScope(context.Record.By, Scopes.AcceptWork);
                if (!allowed)
                {
                    context.Error("acceptance.policy", sameAuthor ? "Self-acceptance of ordinary work needs the 'self-accept-ordinary-work' authority scope." : "Accepting another principal's work needs the 'accept-work' authority scope.");
                }
            }
        }
        else if (!isOperator)
        {
            context.Error("acceptance.policy", $"Acceptance of a {subject.Type} record is reserved to the operator.");
        }

        RecordEnvelope latest = context.Model.Latest(subject);
        if (latest.Id != subject.Id && !ReleaseType.IsPublicationOf(subject, latest))
        {
            context.Warning("acceptance.unresolved", "The subject was superseded after this acceptance; it accepts an earlier revision and needs reconsideration.");
        }
    }
}
