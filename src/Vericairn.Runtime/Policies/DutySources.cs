// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Policies.PolicyInputs;

namespace Vericairn.Runtime.Policies;

internal sealed partial class DutyProofEvaluation
{
    private IReadOnlyList<RecordReference> DateSource(JsonElement parameters, DateTimeOffset at)
    {
        JsonElement schedule = Get(parameters, "schedule");
        if (Text(schedule, "kind") != "date") return [];
        JsonElement source = Get(schedule, "dateSource");
        if (Text(source, "kind") == "credential-rotate-by")
        {
            RecordEnvelope credential = Exact(Model, Get(source, "record"));
            if (credential.Type != "credential-reference" || credential.AtTime > at || EstateModel.BodyString(credential, "rotateBy") != Text(schedule, "at") ||
                PolicyWork.Identity(Model, credential) != Text(Get(parameters, "subject"), "id")) throw new PolicyInputException("The exact credential date source contradicts this declared deadline or subject.");
            return [credential.Reference];
        }
        if (Text(source, "kind") != "reviewed-report") throw new PolicyInputException("The date-source contract is unavailable.");
        RecordEnvelope result = Exact(Model, Get(source, "result"));
        RecordEnvelope declaredReview = Exact(Model, Get(source, "review"));
        if (result.Type != "result" || !PolicyWork.CurrentAt(Model, result, at) || !Authentic(result) || declaredReview.Type != "review" ||
            declaredReview.AtTime > at || declaredReview.Subject != result.Reference || !Authentic(declaredReview) || declaredReview.By == result.By || EstateModel.BodyString(declaredReview, "outcome") != "pass")
            throw new PolicyInputException("The exact reviewed date report is superseded, unavailable or lacks an authentic independent passing review.");
        string independence = EstateModel.BodyString(declaredReview, "independence") == "operator" ? "operator" : "independent";
        RecordEnvelope? latest = Reviews(result, independence, at).LastOrDefault();
        if (latest is null || EstateModel.BodyString(latest, "outcome") != "pass") throw new PolicyInputException("The latest required review of the date-source report does not pass.");
        JsonElement[] outputs = Items(Get(result.Body, "outputs")).ToArray();
        if (outputs.Length == 0) throw new PolicyInputException("The date-source report retains no source bytes.");
        foreach (JsonElement output in outputs) Object(String(output, "hash"));
        return [result.Reference, declaredReview.Reference, latest.Reference];
    }

    private RecordEnvelope? NextDate(DutyOccurrence occurrence, JsonElement link, DateTimeOffset at, DateTimeOffset completed, List<RecordReference> evidence)
    {
        RecordEnvelope? next = RecurringDuty.NextRevision(context, occurrence.Origin);
        if (next is null || next.AtTime > at || EstateModel.BodyString(next, "state") != "active") return null;
        JsonElement rule = DutyRecords.Rule(next, context.Key);
        if (!DutyRecords.IsDuty(rule)) return null;
        JsonElement parameters = Get(rule, "parameters");
        JsonElement schedule = Get(parameters, "schedule");
        if (Text(schedule, "kind") != "date" || Text(parameters, "renewal") != "next-date-required" || DutyRecords.Time(Text(schedule, "at")) is not { } deadline || deadline <= completed ||
            DutyRecords.DateDue(schedule) is not { } due || due <= next.AtTime ||
            PolicyRecords.Equal(Get(schedule, "dateSource"), Get(Get(occurrence.Parameters, "schedule"), "dateSource"))) return null;
        string? subject = Text(Get(parameters, "subject"), "id");
        bool same = subject == occurrence.SubjectIdentity && Text(Get(parameters, "subject"), "type") == Text(Get(occurrence.Parameters, "subject"), "type");
        if (!same)
        {
            foreach (JsonElement clause in Items(Get(Get(occurrence.Parameters, "discharge"), "records")).Where(c => Text(c, "kind") == "credential-rotation"))
            {
                JsonElement binding = Items(Get(link, "proofs")).Single(p => Text(p, "clause") == Text(clause, "key"));
                JsonElement successor = Items(Get(binding, "records")).Skip(2).First();
                RecordEnvelope record = Exact(Model, successor);
                same |= Text(Get(parameters, "subject"), "type") == "credential-reference" && subject == PolicyWork.Identity(Model, record);
            }
        }
        if (!same) return null;
        evidence.AddRange(DateSource(parameters, at));
        return next;
    }
}
