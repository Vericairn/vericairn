// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>The closed recurring-duty schema; declarations and evidence never perform or authorize work.</summary>
internal static partial class DutyRecords
{
    internal const string Kind = "vericairn.estate/recurring-duty";
    private static readonly string[] SubjectTypes = ["estate", "product", "resource", "device", "credential-reference"];

    internal static DateTimeOffset? Time(string? value) => RecordParser.IsTimestamp(value) ? DateTimeOffset.ParseExact(value!, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal) : null;

    internal static void Parameters(ValidationContext context, BodyReader parameters, BodyReader? selection)
    {
        RecordEnvelope? subject = PolicyRecords.Reference(context, parameters.Nested("subject", true), false, SubjectTypes);
        if (selection is not null)
        {
            string? kind = PolicyRecords.Text(selection.Element, "kind");
            if (kind is not ("estate" or "product")) parameters.Error("duty.selection", "subject", "Duties support only estate and product selections.");
            if (kind == "product" && subject is not null)
            {
                string? product = PolicyRecords.Text(selection.Element, "product");
                bool related = subject.Type == "product" && subject.Id == product || subject.Type == "resource" && subject.Subject is { Type: "product" } relation && relation.Id == product;
                if (!related) parameters.Error("duty.selection", "subject", "A product duty names that product or a resource explicitly related to it.");
            }
        }
        long? grace = parameters.Number("graceDays", true, 0, 36500);
        string? renewal = parameters.Enum("renewal", true, "none", "next-date-required");
        BodyReader? schedule = parameters.Nested("schedule", true);
        string? scheduleKind = schedule?.Enum("kind", true, "interval", "release", "date");
        if (schedule is not null)
        {
            switch (scheduleKind)
            {
                case "interval":
                    schedule.Enum("unit", true, "days", "months");
                    schedule.Number("every", true, 1, 36500);
                    string? first = schedule.Timestamp("firstDueAt", true);
                    if (Time(first) is { } anchor && Time(PolicyRecords.Text(context.Record.Body, "startsAt")) is { } starts && anchor < starts) schedule.Error("duty.anchor", "firstDueAt", "The initial interval anchor cannot precede activation.");
                    break;
                case "release":
                    RecordEnvelope? product = PolicyRecords.IdentityField(context, schedule, "product", "product");
                    if (selection is not null && PolicyRecords.Text(selection.Element, "kind") == "product" && product?.Id != PolicyRecords.Text(selection.Element, "product")) schedule.Error("duty.selection", "product", "The release schedule belongs to the selected product.");
                    break;
                case "date":
                    string? at = schedule.Timestamp("at", true);
                    long? lead = schedule.Number("leadDays", true, 0, 36500);
                    if (grace > lead) schedule.Error("duty.grace", "leadDays", "Grace cannot extend beyond the dated deadline.");
                    if (Time(at) is { } deadline && lead is { } days && deadline.UtcTicks < TimeSpan.FromDays(days).Ticks) schedule.Error("duty.date", "at", "The lead period must fit in the supported calendar.");
                    DateSource(context, schedule.Nested("dateSource", true), at, subject);
                    break;
            }
            schedule.NoOtherKeys();
        }
        if (renewal == "next-date-required" && scheduleKind != "date") parameters.Error("duty.renewal", "renewal", "Only a dated duty may require the next date.");
        Discharge(context, parameters.Nested("discharge", true), subject);
        parameters.NoOtherKeys();
    }

    private static void DateSource(ValidationContext context, BodyReader? source, string? at, RecordEnvelope? subject)
    {
        if (source is null) return;
        switch (source.Enum("kind", true, "credential-rotate-by", "reviewed-report"))
        {
            case "credential-rotate-by":
                RecordEnvelope? credential = PolicyRecords.Reference(context, source.Nested("record", true), true, "credential-reference");
                if (credential is not null && EstateModel.BodyString(credential, "rotateBy") != at) source.Error("duty.date-source", "record", "The declared date must equal the credential's typed rotateBy.");
                if (credential is not null && subject is not null && PolicyRecords.Identity(context.Model, credential) != subject.Id) source.Error("duty.date-source", "record", "A credential date source belongs to the selected credential identity.");
                break;
            case "reviewed-report":
                RecordEnvelope? result = PolicyRecords.Reference(context, source.Nested("result", true), true, "result");
                RecordEnvelope? review = PolicyRecords.Reference(context, source.Nested("review", true), true, "review");
                if (result is not null && review is not null && (review.Subject != result.Reference || review.By == result.By || EstateModel.BodyString(review, "outcome") != "pass" || !SignatureChecks.IsAuthentic(context.Model, result) || !SignatureChecks.IsAuthentic(context.Model, review)))
                    source.Error("duty.date-source", "review", "A report date cites an admitted-device signed result and an exact, signed, independent passing review.");
                if (result is not null && !PolicyRecords.Items(PolicyRecords.Property(result.Body, "outputs")).Any()) source.Error("duty.date-source", "result", "A source report retains its bytes in hashed outputs.");
                break;
        }
        source.NoOtherKeys();
    }

    private static void Discharge(ValidationContext context, BodyReader? discharge, RecordEnvelope? subject)
    {
        if (discharge is null) return;
        IReadOnlyList<string> checks = discharge.StringArray("checks", true, false, 300) ?? [];
        PolicyRecords.Unique(discharge, checks, "checks");
        string? review = discharge.Enum("review", true, "none", "independent", "operator");
        IReadOnlyList<BodyReader> clauses = discharge.NestedArray("records", true) ?? [];
        PolicyRecords.Unique(discharge, clauses.Select(c => PolicyRecords.Text(c.Element, "key")), "records");
        foreach (BodyReader clause in clauses)
        {
            PolicyRecords.Key(clause);
            string? kind = clause.Enum("kind", true, "verification", "device-rotation", "credential-rotation", "observed-operation", "retirement", "obligation");
            switch (kind)
            {
                case "verification":
                    clause.Text("method", true, 200);
                    string? mode = clause.Enum("subject", true, "duty-subject", "result");
                    string? commit = clause.Enum("commitSource", false, "result-scope", "publication");
                    if (commit == "result-scope") PolicyRecords.IdentityField(context, clause, "repository", "resource");
                    if (mode == "result" && review == "none") clause.Error("duty.review", "subject", "A result-subject verification requires independent or operator review.");
                    if (subject is { Type: "resource" } && EstateModel.BodyString(subject, "kind") == "repository" && commit is null) clause.Error("duty.commit", "commitSource", "Repository verification names its commit source.");
                    break;
                case "device-rotation":
                    if (subject is not null && subject.Type != "device") clause.Error("duty.subject", "kind", "Device rotation needs a device subject.");
                    break;
                case "credential-rotation":
                    if (subject is not null && subject.Type != "credential-reference") clause.Error("duty.subject", "kind", "Credential rotation needs a credential-reference subject.");
                    clause.Text("provider", true, 100);
                    clause.Text("action", true, 200);
                    if (review != "operator") clause.Error("duty.review", "kind", "Credential rotation requires operator review.");
                    break;
                case "observed-operation":
                    clause.Text("provider", true, 100);
                    clause.Text("action", true, 200);
                    clause.Enum("author", true, "eligible", "operator");
                    break;
                case "retirement": PolicyRecords.IdentityField(context, clause, "retirement", "retirement"); break;
                case "obligation":
                    RecordEnvelope? decision = PolicyRecords.IdentityField(context, clause, "decision", "decision");
                    string? obligation = PolicyRecords.Key(clause, "obligation");
                    if (decision is not null && !PolicyRecords.Items(PolicyRecords.Property(decision.Body, "obligations")).Any(o => PolicyRecords.Text(o, "key") == obligation)) clause.Error("duty.obligation", "obligation", "The named decision has no such obligation.");
                    break;
            }
            clause.NoOtherKeys();
        }
        discharge.NoOtherKeys();
    }

    internal static JsonElement Rule(RecordEnvelope policy, string? key) => PolicyRecords.Items(PolicyRecords.Property(policy.Body, "rules")).FirstOrDefault(r => PolicyRecords.Text(r, "key") == key);

    internal static bool IsDuty(JsonElement rule) => PolicyRecords.Text(rule, "kind") == Kind && PolicyRecords.Property(rule, "version").TryGetInt64Safe(out long version) && version == 1;

    internal static RecordEnvelope Origin(EstateModel model, RecordEnvelope policy, string? key)
    {
        JsonElement rule = Rule(policy, key);
        var seen = new HashSet<string>(StringComparer.Ordinal) { policy.Id };
        while (policy.Supersedes is { } id && model.ById.TryGetValue(id, out RecordEnvelope? previous) && seen.Add(id))
        {
            JsonElement old = Rule(previous, key);
            if (!IsDuty(old) || !PolicyRecords.Equal(PolicyRecords.Property(old, "parameters"), PolicyRecords.Property(rule, "parameters"))) break;
            policy = previous;
        }
        return policy;
    }

    internal static DateTimeOffset? Occurrence(ValidationContext context, BodyReader? occurrence, RecordEnvelope policy, string? key, RecordEnvelope? subject)
    {
        if (occurrence is null) return null;
        JsonElement rule = Rule(policy, key);
        if (!IsDuty(rule)) { occurrence.Error("duty.rule", "kind", "The exact policy revision must contain this recurring-duty rule."); return null; }
        if (Origin(context.Model, policy, key).Id != policy.Id) occurrence.Error("duty.origin", "kind", "An occurrence pins its originating schedule revision, not an editorial revision.");
        JsonElement parameters = PolicyRecords.Property(rule, "parameters");
        JsonElement declaredSubject = PolicyRecords.Property(parameters, "subject");
        if (subject is not null && (subject.Type != PolicyRecords.Text(declaredSubject, "type") || PolicyRecords.Identity(context.Model, subject) != PolicyRecords.Text(declaredSubject, "id"))) occurrence.Error("duty.subject", "kind", "The evidence belongs to this duty's selected subject identity.");
        JsonElement schedule = PolicyRecords.Property(parameters, "schedule");
        string? kind = occurrence.Enum("kind", true, "interval", "release", "date");
        if (kind != PolicyRecords.Text(schedule, "kind")) occurrence.Error("duty.occurrence", "kind", "The occurrence tag must match the declared schedule.");
        DateTimeOffset? due = null;
        switch (kind)
        {
            case "interval":
                long? number = occurrence.Number("number", true, 0, int.MaxValue);
                if (number is not null) due = IntervalDue(schedule, number.Value);
                if (due is null && number is not null) occurrence.Error("duty.occurrence", "number", "The occurrence must fit the declared calendar.");
                break;
            case "date":
                string? at = occurrence.Timestamp("at", true);
                if (at != PolicyRecords.Text(schedule, "at")) occurrence.Error("duty.occurrence", "at", "The occurrence pins the declared deadline.");
                due = DateDue(schedule);
                break;
            case "release":
                RecordEnvelope? release = PolicyRecords.Reference(context, occurrence.Nested("publication", true), true, "release");
                if (release is not null)
                {
                    if (EstateModel.BodyString(release, "status") != "published" || release.Subject?.Id != PolicyRecords.Text(schedule, "product") || context.Model.Chain(release).SkipLast(1).Select(id => context.Model.ById[id]).Any(r => EstateModel.BodyString(r, "status") == "published")) occurrence.Error("duty.publication", "publication", "Use the first observed publication of the declared product.");
                    due = release.AtTime;
                }
                break;
        }
        occurrence.NoOtherKeys();
        DateTimeOffset activation = Time(EstateModel.BodyString(policy, "startsAt")) ?? policy.AtTime;
        if (due is { } instant)
        {
            bool changed = policy.Supersedes is not null;
            if ((changed && instant <= policy.AtTime) || (!changed && kind != "date" && instant < activation)) occurrence.Error("duty.occurrence", "kind", "This occurrence predates its schedule's activation boundary.");
            RecordEnvelope? boundary = context.Model.OfType("estate-policy").Where(p => context.Model.Chain(p).Contains(policy.Id, StringComparer.Ordinal) && p.Id != policy.Id &&
                (EstateModel.BodyString(p, "state") == "retired" || !IsDuty(Rule(p, key)) || Origin(context.Model, p, key).Id != policy.Id)).OrderBy(p => p.At, StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
            if (boundary is not null && instant > boundary.AtTime) occurrence.Error("duty.occurrence", "kind", "An unopened occurrence was replaced at the next schedule boundary.");
            if (instant > context.Record.AtTime || activation > context.Record.AtTime) occurrence.Error("duty.future", "kind", "Only already-due occurrences can be named.");
        }
        return due;
    }

    internal static DateTimeOffset? IntervalDue(JsonElement schedule, long number)
    {
        if (number < 0 || Time(PolicyRecords.Text(schedule, "firstDueAt")) is not { } anchor || !PolicyRecords.Property(schedule, "every").TryGetInt64Safe(out long every) || every <= 0) return null;
        try
        {
            long count = checked(number * every);
            return PolicyRecords.Text(schedule, "unit") switch
            {
                "months" when count <= int.MaxValue => anchor.AddMonths((int)count),
                "days" when count <= 3652059 => anchor.AddDays(count),
                _ => null,
            };
        }
        catch (ArgumentOutOfRangeException) { return null; }
        catch (OverflowException) { return null; }
    }

    internal static DateTimeOffset? DateDue(JsonElement schedule)
    {
        if (Time(PolicyRecords.Text(schedule, "at")) is not { } at || !PolicyRecords.Property(schedule, "leadDays").TryGetInt64Safe(out long lead) || lead < 0 || lead > 36500) return null;
        try { return at.AddDays(-lead); } catch (ArgumentOutOfRangeException) { return null; }
    }
}
