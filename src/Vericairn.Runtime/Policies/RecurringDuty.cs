// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Policies.PolicyInputs;

namespace Vericairn.Runtime.Policies;

internal sealed record DutyCompletionPeriod(RecordReference Result, string From, string? Until, ImmutableArray<RecordReference> Evidence);
internal sealed record DutyFinding(RecordReference Origin, string SubjectIdentity, RecordReference Subject, JsonElement Occurrence,
    string DueAt, string Deadline, bool Completed, string? CompletedAt, bool Missed, decimal LateSeconds,
    bool AlreadyDueAtAdoption, string? EndedReason, ImmutableArray<DutyCompletionPeriod> Completions);

internal sealed record DutyOccurrence(RecordEnvelope Origin, JsonElement Parameters, string SubjectIdentity,
    JsonElement Item, DateTimeOffset Due, DateTimeOffset Deadline, bool AlreadyDueAtAdoption)
{
    internal string Key => Origin.Id + ":" + SubjectIdentity + ":" + PolicyBasis.Canonical(Item);
}

internal static class RecurringDuty
{
    internal const int MaximumOccurrences = 10000;
    internal const int MaximumProofSteps = 1000000;

    internal static void Validate(PolicyRuleContext context)
    {
        var findings = new FindingList();
        var validation = new ValidationContext(context.Model, context.Policy, findings);
        DutyRecords.Parameters(validation, new(context.Parameters, "parameters", findings, 3), new(Get(context.Policy.Body, "appliesTo"), "appliesTo", findings, 3));
        if (findings.HasErrors) throw new PolicyParameterException("Invalid recurring-duty parameters: " + string.Join(", ", findings.Where(f => f.Severity == Severity.Error).Select(f => f.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) + ".");
        if (Items(Get(Get(context.Parameters, "discharge"), "records")).Any(c => Text(c, "kind") is "observed-operation" or "credential-rotation" && Text(c, "action") == OperationType.AgentRun))
            throw new PolicyParameterException("An agent-run cannot be a duty's native action proof.");
    }

    internal static IReadOnlyList<PolicyFinding> Evaluate(PolicyRuleContext context)
    {
        var evaluation = new DutyProofEvaluation(context);
        DutyOccurrence[] occurrences = Occurrences(context).ToArray();
        List<PolicyFinding> findings = [];
        foreach (DutyOccurrence occurrence in occurrences) findings.Add(evaluation.Finding(occurrence));
        return findings.Count == 0 ? [context.Finding(new("policy", context.Identity, context.Policy.Reference), "schedule", "satisfied", "No publication occurrence has opened.",
            "Each first observed publication opens a distinct occurrence.", "Wait for the next observed publication.", disposition: "scheduled", subjects: 0)] : findings;
    }

    internal static IReadOnlyList<RecordEnvelope> History(PolicyRuleContext context) => context.Model.Chain(context.Policy).Select(id => context.Model.ById[id]).ToArray();

    private static IEnumerable<DutyOccurrence> Occurrences(PolicyRuleContext context)
    {
        IReadOnlyList<RecordEnvelope> history = History(context);
        DateTimeOffset now = context.Basis.Time!.Value;
        int count = 0;
        foreach (RecordEnvelope origin in history.Where(p => EstateModel.BodyString(p, "state") == "active" && DutyRecords.IsDuty(DutyRecords.Rule(p, context.Key)) && DutyRecords.Origin(context.Model, p, context.Key).Id == p.Id))
        {
            JsonElement parameters = Get(DutyRecords.Rule(origin, context.Key), "parameters");
            // Each old revision keeps its own schema, scope and proof requirements.
            Validate(new(context.Basis, origin, DutyRecords.Rule(origin, context.Key)));
            RecordEnvelope subject = Subject(context.Model, parameters);
            DateTimeOffset activation = Timestamp(origin.Body, "startsAt");
            RecordEnvelope? boundary = NextRevision(context, origin);
            DateTimeOffset until = boundary is not null && boundary.AtTime < now ? boundary.AtTime : now;
            if (activation > until) continue;
            JsonElement schedule = Get(parameters, "schedule");
            string kind = Text(schedule, "kind")!;
            IEnumerable<(JsonElement Item, DateTimeOffset Due)> items = kind switch
            {
                "interval" => Intervals(schedule, until, boundary is null),
                "date" => [(JsonSerializer.SerializeToElement(new { kind = "date", at = Text(schedule, "at") }), DutyRecords.DateDue(schedule) ?? throw new PolicyInputException("The dated occurrence overflows the supported calendar."))],
                "release" => Publications(context, schedule).Where(r => r.AtTime >= activation && r.AtTime <= until && (origin.Supersedes is null || r.AtTime > origin.AtTime))
                    .Select(r => (JsonSerializer.SerializeToElement(new { kind = "release", publication = new { type = r.Type, id = r.Id, hash = r.ComputedHash } }), r.AtTime)),
                _ => throw new PolicyParameterException("Unsupported duty schedule."),
            };
            foreach ((JsonElement item, DateTimeOffset due) in items)
            {
                if (boundary is not null && due > until) continue;
                if (origin.Supersedes is not null && due <= origin.AtTime) throw new PolicyInputException("A changed schedule opens strictly after its revision boundary.");
                if (++count > MaximumOccurrences) throw new PolicyInputException("The duty occurrence bound was exhausted; complete debt is unavailable and no truncated pass is returned.");
                DateTimeOffset deadline;
                try { deadline = due.AddDays(Integer(parameters, "graceDays", 0, 36500)); }
                catch (ArgumentOutOfRangeException) { throw new PolicyInputException("The duty deadline overflows the supported calendar."); }
                yield return new(origin, parameters, PolicyWork.Identity(context.Model, subject), item, due, deadline, due < activation);
            }
        }
    }

    private static IEnumerable<(JsonElement Item, DateTimeOffset Due)> Intervals(JsonElement schedule, DateTimeOffset until, bool includeNext)
    {
        for (long number = 0; ; number++)
        {
            DateTimeOffset due = DutyRecords.IntervalDue(schedule, number) ?? throw new PolicyInputException("The next interval occurrence overflows the supported calendar.");
            if (due > until && !includeNext) yield break;
            yield return (JsonSerializer.SerializeToElement(new { kind = "interval", number }), due);
            if (due > until) yield break;
        }
    }

    internal static RecordEnvelope Subject(EstateModel model, JsonElement parameters)
    {
        JsonElement selected = Get(parameters, "subject");
        if (!model.ById.TryGetValue(Text(selected, "id") ?? "", out RecordEnvelope? subject) || subject.Type != Text(selected, "type")) throw new PolicyInputException("The named duty subject is unavailable.");
        return subject;
    }

    internal static RecordEnvelope? NextRevision(PolicyRuleContext context, RecordEnvelope origin) => History(context).SkipWhile(p => p.Id != origin.Id).Skip(1)
        .FirstOrDefault(p => EstateModel.BodyString(p, "state") == "retired" || !DutyRecords.IsDuty(DutyRecords.Rule(p, context.Key)) || DutyRecords.Origin(context.Model, p, context.Key).Id != origin.Id);

    private static RecordEnvelope[] Publications(PolicyRuleContext context, JsonElement schedule)
    {
        string product = String(schedule, "product");
        if (!context.Model.ById.TryGetValue(product, out RecordEnvelope? record) || record.Type != "product") throw new PolicyInputException("The publication schedule's product is unavailable.");
        RecordEnvelope[] publications = context.Model.OfType("release").Where(r => r.Subject?.Id == product && EstateModel.BodyString(r, "status") == "published" &&
            !context.Model.Chain(r).SkipLast(1).Any(id => EstateModel.BodyString(context.Model.ById[id], "status") == "published")).ToArray();
        if (publications.GroupBy(r => EstateModel.BodyString(r, "version"), StringComparer.Ordinal).Any(g => g.Count() != 1)) throw new PolicyInputException("The publication identity is ambiguous.");
        return publications;
    }
}
