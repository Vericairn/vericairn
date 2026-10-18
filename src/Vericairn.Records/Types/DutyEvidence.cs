// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

internal static partial class DutyRecords
{
    internal static string OccurrenceKey(JsonElement link) => PolicyRecords.Text(PolicyRecords.Property(link, "policy"), "id") + ":" + PolicyRecords.Text(link, "rule") + ":" +
        PolicyRecords.Text(PolicyRecords.Property(link, "subject"), "type") + ":" + PolicyRecords.Text(PolicyRecords.Property(link, "subject"), "id") + ":" +
        (PolicyRecords.Property(link, "occurrence").ValueKind == JsonValueKind.Object ? CanonicalJson.Serialize(PolicyRecords.Property(link, "occurrence")) : "?");

    internal static void Results(ValidationContext context)
    {
        if (!context.Body.Since(3, "duties")) return;
        IReadOnlyList<BodyReader> duties = context.Body.NestedArray("duties", false, false) ?? [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader duty in duties)
        {
            RecordEnvelope? policy = PolicyRecords.Reference(context, duty.Nested("policy", true), true, "estate-policy");
            string? rule = PolicyRecords.Key(duty, "rule");
            RecordEnvelope? subject = PolicyRecords.Reference(context, duty.Nested("subject", true), true, SubjectTypes);
            BodyReader? occurrence = duty.Nested("occurrence", true);
            if (policy is not null) Occurrence(context, occurrence, policy, rule, subject);
            string identity = policy?.Id + ":" + rule + ":" + (subject is null ? "?" : PolicyRecords.Identity(context.Model, subject)) + ":" + (occurrence is null ? "?" : CanonicalJson.Serialize(occurrence.Element));
            if (!seen.Add(identity)) duty.Error("duty.duplicate", "occurrence", "A result names an occurrence only once, even across subject revisions.");
            Scope(context, duty.Nested("scope", true));
            duty.Text("coverage", true, 8000);
            Proofs(context, duty, policy is null ? default : Rule(policy, rule));
            duty.NoOtherKeys();
        }
    }

    internal static void ReviewRead(ValidationContext context)
    {
        if (!context.Body.Since(3, "read")) return;
        PolicyRecords.References(context, context.Body, "read", false);
        if (context.Body.Has("read") && context.Record.Subject?.Type != "result") context.Error("review.read", "The ordinary read list belongs only to result reviews; goal reviews retain conditions.read.");
    }

    private static void Scope(ValidationContext context, BodyReader? scope)
    {
        if (scope is null) return;
        IReadOnlyList<BodyReader> repositories = scope.NestedArray("repositories", true) ?? [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader repository in repositories)
        {
            RecordEnvelope? record = PolicyRecords.Reference(context, repository.Nested("record", true), true, "resource");
            repository.Text("commit", true, 40, Patterns.Commit);
            repository.NoOtherKeys();
            if (record is not null)
            {
                if (EstateModel.BodyString(record, "kind") != "repository") repository.Error("duty.repository", "record", "Repository scope names a repository resource.");
                if (!seen.Add(PolicyRecords.Identity(context.Model, record))) repository.Error("duty.duplicate", "record", "Repository scope names each identity once.");
            }
        }
        IReadOnlyList<string> objects = scope.StringArray("objects", true, true, 71) ?? [];
        PolicyRecords.Unique(scope, objects, "objects");
        foreach (string hash in objects)
            if (!Hashing.ContentHash.IsValid(hash)) scope.Error("body.hash", "objects", "Retained object references are SHA-256 hashes.");
        PolicyRecords.References(context, scope, "records");
        scope.NoOtherKeys();
    }

    private static void Proofs(ValidationContext context, BodyReader duty, JsonElement rule)
    {
        IReadOnlyList<BodyReader> proofs = duty.NestedArray("proofs", true) ?? [];
        PolicyRecords.Unique(duty, proofs.Select(p => PolicyRecords.Text(p.Element, "clause")), "proofs");
        JsonElement[] clauses = PolicyRecords.Items(PolicyRecords.Property(PolicyRecords.Property(PolicyRecords.Property(rule, "parameters"), "discharge"), "records")).ToArray();
        foreach (BodyReader proof in proofs)
        {
            string? key = PolicyRecords.Key(proof, "clause");
            JsonElement clause = clauses.FirstOrDefault(c => PolicyRecords.Text(c, "key") == key);
            bool resultVerification = PolicyRecords.Text(clause, "kind") == "verification" && PolicyRecords.Text(clause, "subject") == "result";
            if (resultVerification)
                proof.Enum("kind", true, "result-verification");
            else
            {
                IReadOnlyList<RecordEnvelope> records = PolicyRecords.References(context, proof, "records", true, false);
                string[] types = PolicyRecords.Text(clause, "kind") switch
                {
                    "verification" => ["verification"],
                    "device-rotation" => ["device", "device"],
                    "credential-rotation" => ["credential-reference", "credential-reference", "credential-reference", "operation"],
                    "observed-operation" => ["operation"],
                    "retirement" => ["retirement"],
                    "obligation" => ["discharge"],
                    _ => [],
                };
                if (!records.Select(r => r.Type).SequenceEqual(types)) proof.Error("duty.proofs", "records", "Proof records must have the clause's exact types and order (prior states before completing actions).");
            }
            if (clause.ValueKind == JsonValueKind.Undefined) proof.Error("duty.proofs", "clause", "This duty has no such supporting clause.");
            proof.NoOtherKeys();
        }
        foreach (JsonElement clause in clauses)
            if (!proofs.Any(p => PolicyRecords.Text(p.Element, "clause") == PolicyRecords.Text(clause, "key"))) duty.Error("duty.proofs", "proofs", "Every declared clause has exactly one binding.");
    }

    internal static void Dispositions(ValidationContext context, RecordEnvelope? previous)
    {
        IReadOnlyList<BodyReader> dispositions = context.Body.NestedArray("dutyDispositions", false, false) ?? [];
        PolicyRecords.Unique(context.Body, dispositions.Select(d => OccurrenceKey(d.Element)), "dutyDispositions");
        foreach (BodyReader disposition in dispositions)
        {
            string? rule = PolicyRecords.Key(disposition, "rule");
            RecordEnvelope? policy = PolicyRecords.Reference(context, disposition.Nested("policy", true), true, "estate-policy");
            RecordEnvelope? subject = PolicyRecords.Reference(context, disposition.Nested("subject", true), false, SubjectTypes);
            BodyReader? occurrence = disposition.Nested("occurrence", true);
            DateTimeOffset? due = policy is null ? null : Occurrence(context, occurrence, policy, rule, subject);
            disposition.Enum("disposition", true, "ended-unmet");
            disposition.Text("reason", true, 4000);
            disposition.NoOtherKeys();
            if (previous is null || policy is null || !context.Model.Chain(previous).Contains(policy.Id, StringComparer.Ordinal)) disposition.Error("duty.disposition", "policy", "A revision disposes only of an occurrence from its own retained history.");
            else if (Ended(context.Model, previous, disposition.Element) || (due is not null && Completed(context.Model, policy, rule, PolicyRecords.Property(disposition.Element, "occurrence"), due.Value, context.Record.AtTime)))
                disposition.Error("duty.disposition", "occurrence", "A disposition cannot repeat ended debt or relabel completed work as unmet.");
        }
        if (previous is null) return;
        bool retired = EstateModel.BodyString(context.Record, "state") == "retired";
        foreach (JsonElement current in PolicyRecords.Items(PolicyRecords.Property(context.Record.Body, "rules")).Where(IsDuty))
        {
            string? key = PolicyRecords.Text(current, "key");
            if (Origin(context.Model, context.Record, key).Id != context.Record.Id) continue;
            JsonElement schedule = PolicyRecords.Property(PolicyRecords.Property(current, "parameters"), "schedule");
            DateTimeOffset? first = PolicyRecords.Text(schedule, "kind") switch { "interval" => IntervalDue(schedule, 0), "date" => DateDue(schedule), _ => null };
            if (first <= context.Record.AtTime) context.Error("duty.boundary", "A new or changed schedule starts strictly after its revision; prior debt keeps its original schedule.");
        }
        foreach (RecordEnvelope old in context.Model.Chain(previous).Select(id => context.Model.ById[id]))
        {
            foreach (JsonElement rule in PolicyRecords.Items(PolicyRecords.Property(old.Body, "rules")).Where(IsDuty))
            {
                string? key = PolicyRecords.Text(rule, "key");
                if (Origin(context.Model, old, key).Id != old.Id) continue;
                JsonElement next = Rule(context.Record, key);
                bool removed = retired || !IsDuty(next);
                foreach ((JsonElement occurrence, DateTimeOffset due) in DueOccurrences(context.Model, old, key, context.Record.AtTime))
                {
                    if (Time(EstateModel.BodyString(context.Record, "startsAt")) > Time(EstateModel.BodyString(previous, "startsAt"))) context.Error("duty.activation", "Activation cannot be postponed after an occurrence became due.");
                    if (!removed) break;
                    bool ended = context.Model.Chain(previous).Select(id => context.Model.ById[id]).SelectMany(p => PolicyRecords.Items(PolicyRecords.Property(p.Body, "dutyDispositions"))).Any(d => Matches(d, old.Id, key, occurrence));
                    if (!ended && !Completed(context.Model, old, key, occurrence, due, context.Record.AtTime) && !dispositions.Any(d => Matches(d.Element, old.Id, key, occurrence)))
                    {
                        context.Error("duty.inventory", "Removing a duty or retiring its policy must explicitly end every outstanding occurrence, including those within grace.");
                        break;
                    }
                }
            }
        }
    }

    private static bool Ended(EstateModel model, RecordEnvelope previous, JsonElement disposition) => model.Chain(previous).Select(id => model.ById[id]).SelectMany(p => PolicyRecords.Items(PolicyRecords.Property(p.Body, "dutyDispositions"))).Any(d => OccurrenceKey(d) == OccurrenceKey(disposition));

    private static bool Matches(JsonElement link, string policy, string? rule, JsonElement occurrence) => PolicyRecords.Text(PolicyRecords.Property(link, "policy"), "id") == policy && PolicyRecords.Text(link, "rule") == rule && PolicyRecords.Equal(PolicyRecords.Property(link, "occurrence"), occurrence);

    private static IEnumerable<(JsonElement Occurrence, DateTimeOffset Due)> DueOccurrences(EstateModel model, RecordEnvelope origin, string? key, DateTimeOffset asOf)
    {
        JsonElement schedule = PolicyRecords.Property(PolicyRecords.Property(Rule(origin, key), "parameters"), "schedule");
        DateTimeOffset activation = Time(EstateModel.BodyString(origin, "startsAt")) ?? origin.AtTime;
        if (origin.Supersedes is not null && origin.AtTime > activation) activation = origin.AtTime;
        if (activation > asOf) yield break;
        DateTimeOffset boundary = model.OfType("estate-policy").Where(p => p.Id != origin.Id && model.Chain(p).Contains(origin.Id, StringComparer.Ordinal) &&
            (EstateModel.BodyString(p, "state") == "retired" || !IsDuty(Rule(p, key)) || Origin(model, p, key).Id != origin.Id)).Select(p => p.AtTime).DefaultIfEmpty(asOf).Min();
        DateTimeOffset until = boundary < asOf ? boundary : asOf;
        switch (PolicyRecords.Text(schedule, "kind"))
        {
            case "interval":
                for (long number = 0; ; number++)
                {
                    DateTimeOffset? due = IntervalDue(schedule, number);
                    if (due is null || due > until) break;
                    yield return (JsonSerializer.SerializeToElement(new { kind = "interval", number }), due.Value);
                }
                break;
            case "date":
                if (DateDue(schedule) is { } date && date <= until) yield return (JsonSerializer.SerializeToElement(new { kind = "date", at = PolicyRecords.Text(schedule, "at") }), date);
                break;
            case "release":
                foreach (RecordEnvelope release in model.OfType("release").Where(r => r.AtTime >= activation && r.AtTime <= until && r.Subject?.Id == PolicyRecords.Text(schedule, "product") && EstateModel.BodyString(r, "status") == "published" && !model.Chain(r).SkipLast(1).Any(id => EstateModel.BodyString(model.ById[id], "status") == "published")))
                    yield return (JsonSerializer.SerializeToElement(new { kind = "release", publication = new { type = release.Type, id = release.Id, hash = release.ComputedHash } }), release.AtTime);
                break;
        }
    }

    private static bool CurrentAt(EstateModel model, RecordEnvelope record, DateTimeOffset at) => record.AtTime <= at && !model.OfType(record.Type).Any(r => r.Supersedes == record.Id && r.AtTime <= at);

    // This checks whether debt may be ended, not release readiness or acceptance. The policy evaluator owns finding projections.
    private static bool Completed(EstateModel model, RecordEnvelope policy, string? key, JsonElement occurrence, DateTimeOffset due, DateTimeOffset at)
    {
        JsonElement parameters = PolicyRecords.Property(Rule(policy, key), "parameters");
        JsonElement discharge = PolicyRecords.Property(parameters, "discharge");
        foreach (RecordEnvelope result in model.OfType("result").Where(r => CurrentAt(model, r, at) && r.AtTime >= due && EstateModel.BodyString(r, "outcome") == "completed" && SignatureChecks.IsAuthentic(model, r)))
        {
            JsonElement link = PolicyRecords.Items(PolicyRecords.Property(result.Body, "duties")).FirstOrDefault(d => Matches(d, policy.Id, key, occurrence));
            if (link.ValueKind == JsonValueKind.Undefined || PolicyRecords.Items(PolicyRecords.Property(result.Body, "unresolvedEffects")).Any()) continue;
            bool checks = PolicyRecords.Items(PolicyRecords.Property(discharge, "checks")).All(name =>
            {
                if (name.ValueKind != JsonValueKind.String) return false;
                JsonElement[] found = PolicyRecords.Items(PolicyRecords.Property(result.Body, "checks")).Where(c => PolicyRecords.Text(c, "name") == name.GetString()).ToArray();
                return found.Length == 1 && PolicyRecords.Text(found[0], "outcome") == "passed";
            });
            if (!checks) continue;
            DateTimeOffset? qualified = null;
            foreach (DateTimeOffset instant in model.Records.Where(r => r.AtTime >= result.AtTime && r.AtTime <= at).Select(r => r.AtTime).Append(at).Distinct().Order())
            {
                bool complete = ProofsComplete(model, result, link, discharge, due, instant, out List<RecordEnvelope> verifications);
                string? required = PolicyRecords.Text(discharge, "review");
                RecordEnvelope? review = model.OfType("review").Where(r => CurrentAt(model, r, instant) && r.Subject == result.Reference && r.By != result.By && SignatureChecks.IsAuthentic(model, r) &&
                    (required == "independent" || required == "operator" && PolicyAuthority.OperatorAt(model, r.By, r.At))).OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
                if (required != "none" && (review is null || EstateModel.BodyString(review, "outcome") != "pass" || verifications.Where(v => v.Subject == result.Reference).Any(v => review.AtTime < v.AtTime || !PolicyRecords.Items(PolicyRecords.Property(review.Body, "read")).Any(r => PolicyRecords.Text(r, "id") == v.Id && PolicyRecords.Text(r, "hash") == v.ComputedHash)))) complete = false;
                DateTimeOffset completion = qualified ?? instant;
                if (PolicyRecords.Text(parameters, "renewal") == "next-date-required" && !NextDate(model, policy, key, parameters, link, instant, completion)) complete = false;
                if (!complete) qualified = null;
                else if (qualified is null && verifications.All(v => Time(EstateModel.BodyString(v, "expires")) is not { } expires || expires > completion)) qualified = completion;
            }
            if (qualified is not null) return true;
        }
        return false;
    }

    private static bool NextDate(EstateModel model, RecordEnvelope policy, string? key, JsonElement parameters, JsonElement link, DateTimeOffset at, DateTimeOffset completed)
    {
        RecordEnvelope? next = model.OfType("estate-policy").Where(p => p.Id != policy.Id && model.Chain(p).Contains(policy.Id, StringComparer.Ordinal) && p.AtTime <= at &&
            (!IsDuty(Rule(p, key)) || Origin(model, p, key).Id == p.Id)).OrderBy(p => p.At, StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
        if (next is null || EstateModel.BodyString(next, "state") != "active") return false;
        JsonElement nextParameters = PolicyRecords.Property(Rule(next, key), "parameters");
        JsonElement schedule = PolicyRecords.Property(nextParameters, "schedule");
        if (PolicyRecords.Text(schedule, "kind") != "date" || PolicyRecords.Text(nextParameters, "renewal") != "next-date-required" || Time(PolicyRecords.Text(schedule, "at")) is not { } deadline || deadline <= completed ||
            PolicyRecords.Equal(PolicyRecords.Property(schedule, "dateSource"), PolicyRecords.Property(PolicyRecords.Property(parameters, "schedule"), "dateSource"))) return false;
        JsonElement nextSubject = PolicyRecords.Property(nextParameters, "subject");
        if (PolicyRecords.Equal(PolicyRecords.Property(parameters, "subject"), nextSubject)) return true;
        foreach (JsonElement clause in PolicyRecords.Items(PolicyRecords.Property(PolicyRecords.Property(parameters, "discharge"), "records")).Where(c => PolicyRecords.Text(c, "kind") == "credential-rotation"))
        {
            JsonElement binding = PolicyRecords.Items(PolicyRecords.Property(link, "proofs")).FirstOrDefault(p => PolicyRecords.Text(p, "clause") == PolicyRecords.Text(clause, "key"));
            JsonElement successor = PolicyRecords.Items(PolicyRecords.Property(binding, "records")).Skip(2).FirstOrDefault();
            if (PolicyRecords.Text(nextSubject, "type") == "credential-reference" && PolicyRecords.Text(nextSubject, "id") == PolicyRecords.Text(successor, "id")) return true;
        }
        return false;
    }
}
