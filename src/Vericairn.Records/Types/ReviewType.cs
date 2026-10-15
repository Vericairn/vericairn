// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A review of an exact record revision, with its independence stated honestly.</summary>
public sealed class ReviewType : IRecordType
{
    /// <inheritdoc />
    public string Name => "review";

    /// <inheritdoc />
    public string Summary => "A review of an exact subject revision. Independence is checked against who authored the subject: self, independent-agent or operator. A review of a goal is its evaluation: a verdict on each completion condition with the records it read, by a principal who authored none of the goal's steps.";

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
        new("conditions", "object[]", false, "For a review of a goal, its evaluation, and only then: one entry per completion condition in the goal's order, each a verdict (" + string.Join('|', GoalRules.EvaluationVerdicts) + "), the reason and the records it read ({type, id, hash}); the outcome is pass when every verdict is met, fail when one is impossible, and findings otherwise.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? independence = body.Enum("independence", true, "self", "independent-agent", "operator");
        string? outcome = body.Enum("outcome", true, "pass", "findings", "fail");
        body.Text("summary", true, 8000);
        IReadOnlyList<(BodyReader Reader, string? Verdict, int Read)>? conditions = body.Since(2, "conditions") ? Conditions(context, body.NestedArray("conditions", false, allowEmpty: false)) : null;
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
        if (subject is not null)
        {
            Evaluation(context, subject, conditions, outcome);
        }

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

    /// <summary>An evaluation's verdicts: each condition's verdict, reason and the records it read, at the hashes it pins.</summary>
    private static List<(BodyReader Reader, string? Verdict, int Read)>? Conditions(ValidationContext context, IReadOnlyList<BodyReader>? conditions)
    {
        if (conditions is null)
        {
            return null;
        }

        var verdicts = new List<(BodyReader Reader, string? Verdict, int Read)>();
        foreach (BodyReader condition in conditions)
        {
            string? verdict = condition.Enum("verdict", true, GoalRules.EvaluationVerdicts);
            condition.Text("reason", true, 4000);
            IReadOnlyList<BodyReader>? read = condition.NestedArray("read", true, allowEmpty: true);
            condition.NoOtherKeys();
            foreach (BodyReader reference in read ?? [])
            {
                RecordReference? parsed = RecordParser.ParseReference(reference.Element, reference.Location, context.Findings);
                if (parsed is not null && context.RequireReference("conditions.read", parsed.Id, parsed.Type) is RecordEnvelope target && parsed.Hash is not null && parsed.Hash != target.ComputedHash)
                {
                    reference.Error("review.read", "hash", $"Record {parsed.Id} does not have the pinned hash; an evaluation names the exact records it read.");
                }
            }

            verdicts.Add((condition, verdict, read?.Count ?? 0));
        }

        return verdicts;
    }

    /// <summary>
    /// A review of a goal is its evaluation (the estate's design/goals.md § 5): a verdict on each completion condition with the
    /// records it read, an outcome that follows from the verdicts, and an evaluator who authored none of the goal's steps.
    /// </summary>
    private static void Evaluation(ValidationContext context, RecordEnvelope subject, IReadOnlyList<(BodyReader Reader, string? Verdict, int Read)>? conditions, string? outcome)
    {
        if (subject.Type != "goal")
        {
            if (conditions is not null)
            {
                context.Error("review.conditions", "Only an evaluation of a goal gives a verdict per condition.");
            }

            return;
        }

        List<JsonElement> completion = subject.Body.TryGetProperty("completion", out JsonElement c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().ToList() : [];
        if (conditions is null)
        {
            context.Error("review.conditions", "A review of a goal is its evaluation and gives a verdict for each of its conditions.");
        }
        else if (conditions.Count != completion.Count)
        {
            context.Error("review.conditions", $"The goal has {completion.Count} completion conditions, and the evaluation gives {conditions.Count} verdicts; it gives one for each, in order.");
        }

        foreach (((BodyReader reader, string? verdict, int read), int index) in (conditions ?? []).Select((v, i) => (v, i)))
        {
            bool evidence = index < completion.Count && completion[index].ValueKind == JsonValueKind.Object && completion[index].TryGetProperty("evidence", out _);
            if (evidence && verdict == "met" && read == 0)
            {
                reader.Error("review.read", "read", "An evidence condition found met names the records the evaluator read.");
            }
        }

        if (outcome is not null && conditions is not null && conditions.All(v => v.Verdict is not null))
        {
            string expected = conditions.All(v => v.Verdict == "met") ? "pass" : conditions.Any(v => v.Verdict == "impossible") ? "fail" : "findings";
            if (outcome != expected)
            {
                context.Error("review.outcome", $"An evaluation's outcome follows its verdicts: {expected}, not {outcome}.");
            }
        }

        if (GoalRules.StepAuthors(context.Model, subject, context.Record).Contains(context.Record.By))
        {
            context.Error("review.evaluator", "A goal is evaluated by a principal other than any step's author; this principal authored one of its steps.");
        }
    }
}
