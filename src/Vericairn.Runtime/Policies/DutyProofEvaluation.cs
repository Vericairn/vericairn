// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Policies.PolicyInputs;

namespace Vericairn.Runtime.Policies;

internal sealed partial class DutyProofEvaluation
{
    private readonly PolicyRuleContext context;
    private readonly Dictionary<string, bool> authenticity = new(StringComparer.Ordinal);
    private readonly ILookup<string, (RecordEnvelope Result, JsonElement Link)> links;
    private readonly DateTimeOffset[] instants;
    private int steps;
    private EstateModel Model => context.Model;
    private DateTimeOffset Now => context.Basis.Time!.Value;

    internal DutyProofEvaluation(PolicyRuleContext context)
    {
        this.context = context;
        instants = context.Basis.Records.Select(r => r.AtTime).Append(Now).Distinct().Order().ToArray();
        links = Model.OfType("result").SelectMany(r => Items(Get(r.Body, "duties")).Where(d => Text(d, "rule") == context.Key).Select(d => (Result: r, Link: d)))
            .ToLookup(x => LinkKey(x.Link), StringComparer.Ordinal);
    }

    internal PolicyFinding Finding(DutyOccurrence occurrence)
    {
        RecordEnvelope subject = Model.Latest(RecurringDuty.Subject(Model, occurrence.Parameters));
        string item = occurrence.Origin.At + ":" + occurrence.Origin.Id + ":" + ItemKey(occurrence.Item);
        var evidence = new List<RecordReference> { occurrence.Origin.Reference };
        List<DutyCompletionPeriod> periods = [];
        DateTimeOffset? completed = null;
        string? endedReason = null;
        RecordEnvelope? ended = null;
        string actual = "No exact, fully qualifying completion result.";
        string verdict, disposition;
        bool unavailable = false;
        try
        {
            foreach (RecordEnvelope revision in RecurringDuty.History(context))
            foreach (JsonElement debt in Items(Get(revision.Body, "dutyDispositions")))
                if (Matches(debt, occurrence, exactSubject: false)) { ended = revision; endedReason = Text(debt, "reason"); }
            if (ended is not null)
            {
                evidence.Add(ended.Reference);
                actual = "Debt explicitly ended without completion: " + endedReason;
            }
            else if (occurrence.Due <= Now)
            {
                evidence.AddRange(DateSource(occurrence.Parameters, Now));
                foreach ((RecordEnvelope result, JsonElement link) in links[occurrence.Key])
                {
                    Step();
                    if (result.AtTime < occurrence.Due || !Matches(link, occurrence, exactSubject: true)) continue;
                    List<DutyCompletionPeriod> resultPeriods = Replay(occurrence, result, link, evidence, out string reason);
                    periods.AddRange(resultPeriods);
                    if (reason != "") actual = reason;
                    DutyCompletionPeriod? current = resultPeriods.LastOrDefault(p => p.Until is null);
                    if (current is null) continue;
                    DateTimeOffset start = DutyRecords.Time(current.From)!.Value;
                    if (completed is null || start < completed)
                    {
                        completed = start;
                        subject = Exact(Model, Get(link, "subject"));
                        evidence.AddRange(current.Evidence);
                    }
                }
            }
            else evidence.AddRange(DateSource(occurrence.Parameters, Now));
            disposition = ended is not null ? "ended-unmet" : completed is not null ? completed > occurrence.Deadline ? "discharged-late" : "discharged" :
                Now < occurrence.Due ? "scheduled" : Now < occurrence.Deadline ? "due-within-grace" : "none";
            verdict = ended is not null || completed is null && Now >= occurrence.Deadline ? "unmet" : "satisfied";
            if (completed is not null) actual = Text(Get(occurrence.Parameters, "discharge"), "review") == "none"
                ? "The exact result, checks and proofs qualify as the contributor's unreviewed claim."
                : "The exact result, required checks, proofs and review qualify.";
        }
        catch (PolicyInputException error)
        {
            if (steps > RecurringDuty.MaximumProofSteps) throw;
            unavailable = true; verdict = "unavailable"; disposition = "none"; actual = error.Message; completed = null;
        }
        bool missed = completed is not null ? completed > occurrence.Deadline : (ended?.AtTime ?? Now) >= occurrence.Deadline;
        decimal late = completed is not null && completed > occurrence.Deadline ? (completed.Value - occurrence.Deadline).Ticks / (decimal)TimeSpan.TicksPerSecond : 0;
        var detail = new DutyFinding(occurrence.Origin.Reference, occurrence.SubjectIdentity, subject.Reference, occurrence.Item, Time(occurrence.Due), Time(occurrence.Deadline),
            completed is not null, completed is null ? null : Time(completed.Value), missed, late, occurrence.AlreadyDueAtAdoption, endedReason,
            periods.OrderBy(p => p.From, StringComparer.Ordinal).ThenBy(p => p.Result.Id, StringComparer.Ordinal).ToImmutableArray());
        PolicyFinding finding = context.Finding(new("record", occurrence.SubjectIdentity, subject.Reference), item, verdict, actual,
            PolicyBasis.Canonical(Get(occurrence.Parameters, "discharge")), unavailable ? "Restore the missing proof input and evaluate again." : actual.Contains("next date", StringComparison.Ordinal)
                ? "Renewal observed; next date needs declaration." : completed is not null || ended is not null ? "Retain the exact completion or disposition evidence." : "Complete and review this exact occurrence with its required evidence.",
            occurrence.Origin, evidence, disposition) with { Duty = detail };
        return finding;
    }

    private List<DutyCompletionPeriod> Replay(DutyOccurrence occurrence, RecordEnvelope result, JsonElement link, List<RecordReference> examined, out string reason)
    {
        List<DutyCompletionPeriod> periods = [];
        examined.Add(result.Reference);
        reason = "The result must be completed, have no unresolved effects and have an authentic admitted-device signature.";
        if (EstateModel.BodyString(result, "outcome") != "completed" || Items(Get(result.Body, "unresolvedEffects")).Any() || !Authentic(result)) return periods;
        JsonElement discharge = Get(occurrence.Parameters, "discharge");
        foreach (string name in Strings(discharge, "checks"))
        {
            JsonElement[] checks = Items(Get(result.Body, "checks")).Where(c => Text(c, "name") == name).ToArray();
            if (checks.Length != 1 || Text(checks[0], "outcome") != "passed") { reason = "Required check is absent, repeated or not passed: " + name + "."; return periods; }
        }
        IReadOnlyList<RecordReference> sources = Sources(result, link);
        examined.AddRange(sources);
        DateTimeOffset? start = null;
        ImmutableArray<RecordReference> periodEvidence = [];
        foreach (DateTimeOffset instant in instants.Where(t => t >= result.AtTime))
        {
            Step();
            bool qualifies = PolicyWork.CurrentAt(Model, result, instant);
            if (!qualifies) reason = "The exact completion result has been superseded.";
            List<RecordEnvelope> proofs = [];
            if (qualifies) qualifies = Proofs(occurrence, result, link, instant, proofs, out reason);
            examined.AddRange(proofs.Select(p => p.Reference));
            RecordEnvelope? review = null;
            string requirement = Text(discharge, "review")!;
            if (qualifies && requirement != "none")
            {
                review = Reviews(result, requirement, instant).LastOrDefault();
                if (review is not null) examined.Add(review.Reference);
                qualifies = review is not null && EstateModel.BodyString(review, "outcome") == "pass" && proofs.Where(p => p.Type == "verification" && p.Subject == result.Reference)
                    .All(p => review.AtTime >= p.AtTime && Items(Get(review.Body, "read")).Any(r => PolicyExceptions.ReferenceMatches(r, p.Reference)));
                if (!qualifies) reason = "The latest required review must pass for the exact result and read every selected result verification.";
            }
            RecordEnvelope? nextDate = null;
            List<RecordReference> nextEvidence = [];
            if (qualifies && Text(occurrence.Parameters, "renewal") == "next-date-required")
            {
                nextDate = NextDate(occurrence, link, instant, start ?? instant, nextEvidence);
                examined.AddRange(nextEvidence);
                qualifies = nextDate is not null;
                if (!qualifies) reason = "Renewal observed; next date needs declaration.";
            }
            // Expiry is tested when this continuous qualification starts. It does not expire a historical
            // successful performance merely because the explicit evaluation time advances.
            if (qualifies && start is null)
            {
                qualifies = proofs.Where(p => p.Type == "verification").All(p => DutyRecords.Time(EstateModel.BodyString(p, "expires")) is not { } expires || instant < expires);
                if (!qualifies) reason = "A required verification had expired when this completion could first qualify.";
            }
            if (!qualifies)
            {
                if (start is not null) periods.Add(new(result.Reference, Time(start.Value), Time(instant), periodEvidence));
                start = null; periodEvidence = [];
            }
            else
            {
                start ??= instant;
                periodEvidence = periodEvidence.Concat(sources).Concat(proofs.Select(p => p.Reference)).Append(result.Reference)
                    .Concat(review is null ? [] : [review.Reference]).Concat(nextEvidence).Concat(nextDate is null ? [] : [nextDate.Reference]).Distinct()
                    .OrderBy(r => r.Type, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ThenBy(r => r.Hash, StringComparer.Ordinal).ToImmutableArray();
            }
        }
        if (start is not null) periods.Add(new(result.Reference, Time(start.Value), null, periodEvidence));
        return periods;
    }

    private List<RecordReference> Sources(RecordEnvelope result, JsonElement link)
    {
        var references = new List<RecordReference>();
        foreach (JsonElement output in Items(Get(result.Body, "outputs"))) Object(String(output, "hash"));
        EvidenceBytes(result);
        JsonElement scope = Get(link, "scope");
        foreach (JsonElement hash in Items(Get(scope, "objects"))) Object(hash.GetString()!);
        foreach (JsonElement record in Items(Get(scope, "records"))) references.Add(Exact(Model, record).Reference);
        foreach (JsonElement source in Items(Get(scope, "repositories")))
        {
            RecordEnvelope repository = Exact(Model, Get(source, "record"));
            references.Add(repository.Reference);
            PolicyRepository inventory = Repository(context.Basis, PolicyWork.Identity(Model, repository));
            if (inventory.Revision != Text(source, "commit") || inventory.Files.Any(f => !f.Available || ContentHash.Sha256(f.Bytes.AsSpan()) != f.Hash))
                throw new PolicyInputException("The result's exact repository commit or retained source bytes are unavailable.");
        }
        return references;
    }

    private void EvidenceBytes(RecordEnvelope record)
    {
        foreach (JsonElement evidence in Items(Get(record.Body, "evidence")))
            if (Text(evidence, "hash") is { } hash) Object(hash);
    }

    private void Object(string hash)
    {
        PolicyObject[] found = context.Basis.Objects.Where(o => o.Hash == hash).ToArray();
        if (found.Length != 1 || !found[0].Available || ContentHash.Sha256(found[0].Bytes.AsSpan()) != hash) throw new PolicyInputException("Required retained proof bytes are unavailable or their hash differs: " + hash + ".");
    }

    private IEnumerable<RecordEnvelope> Reviews(RecordEnvelope result, string requirement, DateTimeOffset at) => Model.OfType("review")
        .Where(r => PolicyWork.CurrentAt(Model, r, at) && PolicyWork.ReviewQualifies(Model, r, result, requirement) && Authentic(r))
        .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal);

    private bool Authentic(RecordEnvelope record)
    {
        if (!authenticity.TryGetValue(record.Id, out bool value)) authenticity[record.Id] = value = SignatureChecks.IsAuthentic(Model, record);
        return value;
    }

    private void Step()
    {
        if (++steps > RecurringDuty.MaximumProofSteps) throw new PolicyInputException("The duty proof replay bound was exhausted; complete debt is unavailable and no truncated pass is returned.");
    }

    private string LinkKey(JsonElement link)
    {
        string? subject = Text(Get(link, "subject"), "id");
        string identity = subject is not null && Model.ById.TryGetValue(subject, out RecordEnvelope? record) ? PolicyWork.Identity(Model, record) : "missing";
        return Text(Get(link, "policy"), "id") + ":" + identity + ":" + PolicyBasis.Canonical(Get(link, "occurrence"));
    }

    private bool Matches(JsonElement link, DutyOccurrence occurrence, bool exactSubject)
    {
        if (Text(link, "rule") != context.Key || !PolicyExceptions.ReferenceMatches(Get(link, "policy"), occurrence.Origin.Reference) || !PolicyRecords.Equal(Get(link, "occurrence"), occurrence.Item)) return false;
        JsonElement reference = Get(link, "subject");
        RecordEnvelope? subject = exactSubject ? Exact(Model, reference) : Model.ById.GetValueOrDefault(Text(reference, "id") ?? "");
        return subject is not null && subject.Type == Text(Get(occurrence.Parameters, "subject"), "type") && PolicyWork.Identity(Model, subject) == occurrence.SubjectIdentity;
    }

    private static string ItemKey(JsonElement item) => Text(item, "kind") switch
    {
        "interval" => "interval:" + Integer(item, "number").ToString("D10", System.Globalization.CultureInfo.InvariantCulture),
        "date" => "date:" + Text(item, "at"),
        _ => "publication:" + Text(Get(item, "publication"), "id"),
    };
}
