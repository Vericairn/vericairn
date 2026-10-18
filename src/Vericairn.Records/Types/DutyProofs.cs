// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

internal static partial class DutyRecords
{
    private static bool ProofsComplete(EstateModel model, RecordEnvelope result, JsonElement link, JsonElement discharge, DateTimeOffset due, DateTimeOffset at, out List<RecordEnvelope> resultVerifications)
    {
        resultVerifications = [];
        foreach (JsonElement clause in PolicyRecords.Items(PolicyRecords.Property(discharge, "records")))
        {
            JsonElement binding = PolicyRecords.Items(PolicyRecords.Property(link, "proofs")).FirstOrDefault(p => PolicyRecords.Text(p, "clause") == PolicyRecords.Text(clause, "key"));
            if (binding.ValueKind == JsonValueKind.Undefined) return false;
            if (PolicyRecords.Text(binding, "kind") == "result-verification")
            {
                RecordEnvelope? verification = model.OfType("verification").Where(v => CurrentAt(model, v, at) && v.Subject == result.Reference && EstateModel.BodyString(v, "method") == PolicyRecords.Text(clause, "method")).OrderBy(v => v.At, StringComparer.Ordinal).ThenBy(v => v.Id, StringComparer.Ordinal).LastOrDefault();
                if (verification is null || !VerificationPasses(model, verification, clause, link, result.Reference, due, at)) return false;
                resultVerifications.Add(verification);
                continue;
            }
            List<RecordEnvelope> records = [];
            foreach (JsonElement reference in PolicyRecords.Items(PolicyRecords.Property(binding, "records")))
            {
                string? id = PolicyRecords.Text(reference, "id");
                if (id is null || !model.ById.TryGetValue(id, out RecordEnvelope? record) || record.Type != PolicyRecords.Text(reference, "type") || record.ComputedHash != PolicyRecords.Text(reference, "hash") || record.AtTime > at) return false;
                records.Add(record);
            }
            RecordReference? subject = LinkReference(PolicyRecords.Property(link, "subject"));
            switch (PolicyRecords.Text(clause, "kind"))
            {
                case "verification":
                    if (records.Count != 1 || !VerificationPasses(model, records[0], clause, link, subject, due, at)) return false;
                    resultVerifications.Add(records[0]);
                    break;
                case "device-rotation":
                    if (records.Count != 2 || records.Any(r => r.Type != "device") || records[1].Supersedes != records[0].Id ||
                        subject is null || PolicyRecords.Identity(model, records[0]) != IdentityOf(model, subject.Id) ||
                        EstateModel.BodyString(records[0], "publicKey") == EstateModel.BodyString(records[1], "publicKey") || records[1].AtTime < due || !SignatureChecks.IsAuthentic(model, records[1])) return false;
                    break;
                case "credential-rotation":
                    if (records.Count != 4 || records.Take(3).Any(r => r.Type != "credential-reference") || records[1].Supersedes != records[0].Id ||
                        EstateModel.BodyString(records[0], "status") != "active" || EstateModel.BodyString(records[1], "status") != "rotated" || EstateModel.BodyString(records[2], "status") != "active" ||
                        subject is null || PolicyRecords.Identity(model, records[0]) != IdentityOf(model, subject.Id) || !ActionTime(records[1], "rotatedAt", due) || !ActionTime(records[2], "rotatedAt", due) ||
                        !OperationPasses(model, records[3], clause, records[0].Reference, due, forceOperator: true)) return false;
                    break;
                case "observed-operation":
                    if (records.Count != 1 || !OperationPasses(model, records[0], clause, subject, due, forceOperator: false)) return false;
                    break;
                case "retirement":
                    if (records.Count != 1 || records[0].Type != "retirement" || PolicyRecords.Identity(model, records[0]) != PolicyRecords.Text(clause, "retirement") || !ActionTime(records[0], "retiredAt", due)) return false;
                    string? path = EstateModel.BodyString(records[0], "path");
                    if (path is null || !Patterns.RelativePath.IsMatch(path) || File.Exists(Path.Combine(model.Root, path.Replace('/', Path.DirectorySeparatorChar))) || Directory.Exists(Path.Combine(model.Root, path.Replace('/', Path.DirectorySeparatorChar)))) return false;
                    break;
                case "obligation":
                    if (records.Count != 1 || records[0].Type != "discharge" || records[0].AtTime < due || records[0].Subject?.Type != "decision" || IdentityOf(model, records[0].Subject!.Id) != PolicyRecords.Text(clause, "decision") || EstateModel.BodyString(records[0], "obligation") != PolicyRecords.Text(clause, "obligation")) return false;
                    break;
                default: return false;
            }
        }
        return true;
    }

    private static string? IdentityOf(EstateModel model, string id) => model.ById.TryGetValue(id, out RecordEnvelope? record) ? PolicyRecords.Identity(model, record) : null;

    private static RecordReference? LinkReference(JsonElement reference) => PolicyRecords.Text(reference, "type") is { } type && PolicyRecords.Text(reference, "id") is { } id && PolicyRecords.Text(reference, "hash") is { } hash ? new RecordReference(type, id, hash) : null;

    private static bool ActionTime(RecordEnvelope record, string field, DateTimeOffset due) => record.AtTime >= due && Time(EstateModel.BodyString(record, field)) is { } performed && performed >= due && performed <= record.AtTime;

    private static bool OperationPasses(EstateModel model, RecordEnvelope operation, JsonElement clause, RecordReference? subject, DateTimeOffset due, bool forceOperator) =>
        operation.Type == "operation" && operation.AtTime >= due && operation.Subject == subject && subject is not null && EstateModel.BodyString(operation, "status") == "observed-success" &&
        EstateModel.BodyString(operation, "provider") == PolicyRecords.Text(clause, "provider") && EstateModel.BodyString(operation, "action") == PolicyRecords.Text(clause, "action") &&
        EstateModel.BodyString(operation, "action") != OperationType.AgentRun && Time(PolicyRecords.Text(PolicyRecords.Property(operation.Body, "observed"), "at")) is { } observed && observed >= due && observed <= operation.AtTime &&
        (!(forceOperator || PolicyRecords.Text(clause, "author") == "operator") || PolicyAuthority.OperatorAt(model, operation.By, operation.At));

    private static bool VerificationPasses(EstateModel model, RecordEnvelope verification, JsonElement clause, JsonElement link, RecordReference? subject, DateTimeOffset due, DateTimeOffset at)
    {
        if (verification.Type != "verification" || subject is null || verification.Subject != subject || verification.AtTime < due || verification.AtTime > at || EstateModel.BodyString(verification, "method") != PolicyRecords.Text(clause, "method") ||
            EstateModel.BodyString(verification, "outcome") != "passed" || EstateModel.BodyString(verification, "evidenceClass") is not ("observation" or "report" or "attestation")) return false;
        string[] fields = ["method", "commit", "outcome", "evidenceClass", "startedAt", "finishedAt", "expires"];
        string identity = PolicyRecords.Identity(model, verification);
        List<RecordEnvelope> heads = model.OfType("verification").Where(v => PolicyRecords.Identity(model, v) == identity && CurrentAt(model, v, at)).ToList();
        if (heads.Count != 1 || heads[0].Subject != verification.Subject || !PolicyRecords.SameFields(heads[0], verification, fields)) return false;
        RecordEnvelope first = verification;
        foreach (RecordEnvelope previous in model.Chain(verification).SkipLast(1).Reverse().Select(id => model.ById[id]))
        {
            if (previous.Subject != verification.Subject || !PolicyRecords.SameFields(previous, verification, fields)) break;
            first = previous;
        }
        if (first.AtTime < due) return false;
        if (EstateModel.BodyString(verification, "finishedAt") is not null && !ActionTime(verification, "finishedAt", due)) return false;
        // The caller also checks expiry at the first continuously qualifying completion, never today's clock.
        if (Time(EstateModel.BodyString(verification, "expires")) <= verification.AtTime) return false;
        string? commit = null;
        switch (PolicyRecords.Text(clause, "commitSource"))
        {
            case "result-scope":
                commit = PolicyRecords.Items(PolicyRecords.Property(PolicyRecords.Property(link, "scope"), "repositories")).Where(r => IdentityOf(model, PolicyRecords.Text(PolicyRecords.Property(r, "record"), "id") ?? "") == PolicyRecords.Text(clause, "repository")).Select(r => PolicyRecords.Text(r, "commit")).FirstOrDefault();
                break;
            case "publication":
                string? id = PolicyRecords.Text(PolicyRecords.Property(PolicyRecords.Property(link, "occurrence"), "publication"), "id");
                if (id is not null && model.ById.TryGetValue(id, out RecordEnvelope? publication)) commit = EstateModel.BodyString(publication, "commit");
                break;
            case null: return true;
        }
        return commit is not null && commit == EstateModel.BodyString(verification, "commit");
    }
}
