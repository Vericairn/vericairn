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
    private bool Proofs(DutyOccurrence occurrence, RecordEnvelope result, JsonElement link, DateTimeOffset at, List<RecordEnvelope> evidence, out string reason)
    {
        reason = "The completion must bind every declared proof clause exactly once.";
        RecordEnvelope subject = Exact(Model, Get(link, "subject"));
        JsonElement[] bindings = Items(Get(link, "proofs")).ToArray();
        JsonElement[] clauses = Items(Get(Get(occurrence.Parameters, "discharge"), "records")).ToArray();
        if (bindings.Length != clauses.Length || bindings.GroupBy(b => Text(b, "clause"), StringComparer.Ordinal).Any(g => g.Count() != 1)) return false;
        foreach (JsonElement clause in clauses)
        {
            Step();
            reason = "Required proof clause does not qualify: " + Text(clause, "key") + ".";
            JsonElement binding = bindings.SingleOrDefault(b => Text(b, "clause") == Text(clause, "key"));
            if (binding.ValueKind == JsonValueKind.Undefined) return false;
            if (Text(binding, "kind") == "result-verification")
            {
                if (Text(clause, "kind") != "verification" || Text(clause, "subject") != "result") return false;
                RecordEnvelope? verification = Model.OfType("verification").Where(v => PolicyWork.CurrentAt(Model, v, at) && v.Subject == result.Reference && EstateModel.BodyString(v, "method") == Text(clause, "method"))
                    .OrderBy(v => v.At, StringComparer.Ordinal).ThenBy(v => v.Id, StringComparer.Ordinal).LastOrDefault();
                if (verification is not null) { evidence.Add(verification); EvidenceBytes(verification); }
                if (verification is null || !Verification(verification, clause, link, result.Reference, occurrence.Due, at)) return false;
                continue;
            }
            RecordEnvelope[] records = Items(Get(binding, "records")).Select(r => Exact(Model, r)).ToArray();
            evidence.AddRange(records);
            if (records.Any(r => r.AtTime > at || !Authentic(r))) return false;
            foreach (RecordEnvelope record in records) EvidenceBytes(record);
            bool valid = Text(clause, "kind") switch
            {
                "verification" => records.Length == 1 && Text(clause, "subject") == "duty-subject" && Verification(records[0], clause, link, subject.Reference, occurrence.Due, at),
                "device-rotation" => records.Length == 2 && records.All(r => r.Type == "device") && records[1].Supersedes == records[0].Id &&
                    PolicyWork.Identity(Model, records[0]) == occurrence.SubjectIdentity && EstateModel.BodyString(records[0], "publicKey") != EstateModel.BodyString(records[1], "publicKey") && records[1].AtTime >= occurrence.Due,
                "credential-rotation" => Credential(records, clause, occurrence, at),
                "observed-operation" => records.Length == 1 && Operation(records[0], clause, subject.Reference, occurrence.Due, at, forceOperator: false),
                "retirement" => records.Length == 1 && Retirement(records[0], clause, occurrence.Due, at),
                "obligation" => records.Length == 1 && Obligation(records[0], clause, occurrence.Due, at),
                _ => false,
            };
            if (!valid) return false;
        }
        reason = "";
        return true;
    }

    private bool Verification(RecordEnvelope verification, JsonElement clause, JsonElement link, RecordReference subject, DateTimeOffset due, DateTimeOffset at)
    {
        if (verification.Type != "verification" || verification.Subject != subject || verification.AtTime > at || !Authentic(verification) || EstateModel.BodyString(verification, "method") != Text(clause, "method") ||
            EstateModel.BodyString(verification, "outcome") != "passed" || EstateModel.BodyString(verification, "evidenceClass") is not ("observation" or "report" or "attestation")) return false;
        string[] fields = ["method", "commit", "outcome", "evidenceClass", "startedAt", "finishedAt", "expires"];
        RecordEnvelope latest = HeadAt(verification, at);
        if (latest.Subject != verification.Subject || !PolicyRecords.SameFields(latest, verification, fields)) return false;
        RecordEnvelope first = FirstEvent(verification, r => r.Subject == verification.Subject && PolicyRecords.SameFields(r, verification, fields));
        if (first.AtTime < due || !Authentic(first) || EstateModel.BodyString(first, "finishedAt") is not null && !ActionTime(first, "finishedAt", due)) return false;
        string? commit = Text(clause, "commitSource") switch
        {
            "result-scope" => Items(Get(Get(link, "scope"), "repositories")).Where(r => PolicyWork.Identity(Model, Exact(Model, Get(r, "record"))) == Text(clause, "repository")).Select(r => Text(r, "commit")).SingleOrDefault(),
            "publication" => EstateModel.BodyString(Exact(Model, Get(Get(link, "occurrence"), "publication")), "commit"),
            null => EstateModel.BodyString(verification, "commit"),
            _ => null,
        };
        return commit is not null && commit == EstateModel.BodyString(verification, "commit");
    }

    private bool Operation(RecordEnvelope operation, JsonElement clause, RecordReference subject, DateTimeOffset due, DateTimeOffset at, bool forceOperator)
    {
        if (operation.Type != "operation" || operation.Subject != subject || EstateModel.BodyString(operation, "provider") != Text(clause, "provider") ||
            EstateModel.BodyString(operation, "action") != Text(clause, "action") || EstateModel.BodyString(operation, "action") == OperationType.AgentRun || EstateModel.BodyString(operation, "status") != "observed-success") return false;
        bool Same(RecordEnvelope r) => r.Subject == operation.Subject && PolicyRecords.SameFields(r, operation, "provider", "action", "status") &&
            Text(Get(r.Body, "observed"), "at") == Text(Get(operation.Body, "observed"), "at");
        if (!Same(HeadAt(operation, at))) return false;
        RecordEnvelope first = FirstEvent(operation, Same);
        DateTimeOffset? performed = DutyRecords.Time(Text(Get(first.Body, "observed"), "at"));
        return first.AtTime >= due && performed >= due && performed <= first.AtTime && Authentic(first) &&
            (!(forceOperator || Text(clause, "author") == "operator") || PolicyAuthority.OperatorAt(Model, first.By, first.At));
    }

    private bool Credential(RecordEnvelope[] records, JsonElement clause, DutyOccurrence occurrence, DateTimeOffset at)
    {
        if (records.Length != 4 || records.Take(3).Any(r => r.Type != "credential-reference") || records[1].Supersedes != records[0].Id ||
            PolicyWork.Identity(Model, records[0]) != occurrence.SubjectIdentity || PolicyWork.Identity(Model, records[2]) == occurrence.SubjectIdentity ||
            EstateModel.BodyString(records[0], "status") != "active" || EstateModel.BodyString(records[1], "status") != "rotated" || EstateModel.BodyString(records[2], "status") != "active") return false;
        RecordEnvelope rotated = FirstEvent(records[1], r => PolicyRecords.SameFields(r, records[1], "status", "rotatedAt"));
        RecordEnvelope issued = FirstEvent(records[2], r => PolicyRecords.SameFields(r, records[2], "status", "rotatedAt"));
        if (EstateModel.BodyString(HeadAt(records[1], at), "status") == "active") return false;
        return ActionTime(rotated, "rotatedAt", occurrence.Due) && ActionTime(issued, "rotatedAt", occurrence.Due) && Authentic(rotated) && Authentic(issued) &&
            Operation(records[3], clause, records[0].Reference, occurrence.Due, at, forceOperator: true);
    }

    private bool Retirement(RecordEnvelope retirement, JsonElement clause, DateTimeOffset due, DateTimeOffset at)
    {
        if (retirement.Type != "retirement" || PolicyWork.Identity(Model, retirement) != Text(clause, "retirement")) return false;
        RecordEnvelope head = HeadAt(retirement, at);
        if (!PolicyRecords.SameFields(head, retirement, "path", "retiredAt")) return false;
        RecordEnvelope first = FirstEvent(retirement, r => PolicyRecords.SameFields(r, retirement, "path", "retiredAt"));
        return ActionTime(first, "retiredAt", due) && Authentic(first) && FoundationPolicyKinds.RetirementRemoved(context, retirement);
    }

    private bool Obligation(RecordEnvelope discharge, JsonElement clause, DateTimeOffset due, DateTimeOffset at)
    {
        bool Matches(RecordEnvelope r) => r.Subject is { Type: "decision" } reference && Model.ById.TryGetValue(reference.Id, out RecordEnvelope? decision) &&
            PolicyWork.Identity(Model, decision) == Text(clause, "decision") && EstateModel.BodyString(r, "obligation") == Text(clause, "obligation");
        if (discharge.Type != "discharge" || !Matches(discharge) || !Matches(HeadAt(discharge, at))) return false;
        RecordEnvelope first = FirstEvent(discharge, Matches);
        return first.AtTime >= due && Authentic(first);
    }

    private RecordEnvelope HeadAt(RecordEnvelope record, DateTimeOffset at)
    {
        string identity = PolicyWork.Identity(Model, record);
        RecordEnvelope[] heads = Model.OfType(record.Type).Where(r => PolicyWork.Identity(Model, r) == identity && PolicyWork.CurrentAt(Model, r, at)).ToArray();
        return heads.Length == 1 ? heads[0] : throw new PolicyInputException("The proof lineage is missing or conflicting.");
    }

    private RecordEnvelope FirstEvent(RecordEnvelope record, Func<RecordEnvelope, bool> same)
    {
        RecordEnvelope first = record;
        foreach (RecordEnvelope previous in Model.Chain(record).SkipLast(1).Reverse().Select(id => Model.ById[id]))
        {
            if (!same(previous)) break;
            first = previous;
        }
        return first;
    }

    private static bool ActionTime(RecordEnvelope record, string field, DateTimeOffset due) => record.AtTime >= due &&
        DutyRecords.Time(EstateModel.BodyString(record, field)) is { } performed && performed >= due && performed <= record.AtTime;
}
