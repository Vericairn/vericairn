// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Policies;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    internal TimeProvider PolicyClock { get; set; } = TimeProvider.System;
    /// <inheritdoc />
    public WriteOutcome PolicyDeclare(PolicyChangeRequest request) => PolicyChange(null, request, "declare");
    /// <inheritdoc />
    public WriteOutcome PolicyRevise(string key, PolicyChangeRequest request) => PolicyChange(key, request, "revise");
    /// <inheritdoc />
    public WriteOutcome PolicyRetire(string key, PolicyChangeRequest request) => PolicyChange(key, request, "retire");
    /// <inheritdoc />
    public WriteOutcome PolicyExcept(string key, PolicyChangeRequest request) => PolicyChange(key, request, "except");

    private WriteOutcome PolicyChange(string? key, PolicyChangeRequest request, string action)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? policy = key is null ? null : FindPolicy(model, key);
            if (key is not null && policy is null) return NotFound("No policy has that key or id.");
            if (request.Expected != (policy?.ComputedHash ?? PolicyBasis.Hash(request.Body))) return PolicyConflict();
            string state = action == "retire" ? "retired" : action == "except" ? "granted" : "active";
            if (PolicyInputs.Text(request.Body, "state") != state) return Refuse("The body state must be " + state + " for this operation.");
            return SignedPolicyWrite(new(action == "except" ? "policy-exception" : "estate-policy", request.By, request.Body,
                action == "except" ? policy!.Reference : null, action is "revise" or "retire" ? policy!.Id : null), model);
        }
    }

    /// <inheritdoc />
    public WriteOutcome PolicyRevokeException(string key, string id, PolicyChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? policy = FindPolicy(model, key);
            RecordEnvelope? exception = Latest(model, id, "policy-exception");
            if (policy is null || exception?.Subject is not { } subject || !model.Chain(policy).Contains(subject.Id, StringComparer.Ordinal))
                return NotFound("No exception with that id belongs to the named policy.");
            if (request.Expected != exception.ComputedHash) return PolicyConflict();
            if (PolicyInputs.Text(request.Body, "state") != "revoked") return Refuse("A revocation body has state revoked.");
            return SignedPolicyWrite(new("policy-exception", request.By, request.Body, exception.Subject, exception.Id), model);
        }
    }

    /// <inheritdoc />
    public WriteOutcome WritingExceptionAdd(PolicyChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            if (request.Expected != PolicyBasis.Hash(request.Body)) return PolicyConflict();
            if (PolicyInputs.Text(request.Body, "state") != "active") return Refuse("A writing entry starts active.");
            return SignedPolicyWrite(new("writing-exception", request.By, request.Body), session.Load());
        }
    }

    /// <inheritdoc />
    public WriteOutcome WritingExceptionRemove(string id, WritingExceptionRemoveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? entry = Latest(model, id, "writing-exception");
            if (entry is null) return NotFound("No writing exception has that id.");
            if (request.Expected != entry.ComputedHash) return PolicyConflict();
            JsonObject body = JsonNode.Parse(entry.Body.GetRawText())!.AsObject();
            body["state"] = "removed";
            body["removalReason"] = request.Reason;
            if (request.Basis is { } basis) body["basis"] = JsonNode.Parse(basis.GetRawText());
            else if (PolicyAuthority.OperatorAt(model, request.By, RecordParser.FormatTimestamp(PolicyClock.GetUtcNow()))) body["basis"] = new JsonObject { ["kind"] = "operator" };
            return SignedPolicyWrite(new("writing-exception", request.By, Element(body), Supersedes: entry.Id), model);
        }
    }

    /// <inheritdoc />
    public WriteOutcome WorkPlace(string key, WorkPlaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? order = WorkView.FindOrder(model, key);
            if (order is null) return NotFound("No work order has that key or id.");
            if (request.Expected != order.ComputedHash) return PolicyConflict();
            if ((request.ReleasePlan is null) == (request.Backlog is null)) return Refuse("Select exactly one release plan or backlog.");
            JsonObject placement;
            if (request.ReleasePlan is { } plan)
            {
                RecordEnvelope? carrier = WorkView.FindOrder(model, plan);
                if (carrier is null || PolicyInputs.Get(carrier.Body, "releasePlan").ValueKind != JsonValueKind.Object) return Refuse("The named carrier has no typed release plan.");
                if (request.ExpectedSelection != carrier.ComputedHash) return PolicyConflict();
                JsonElement target = PolicyInputs.Get(carrier.Body, "releasePlan");
                placement = new() { ["kind"] = "release", ["carrier"] = PolicyWork.Identity(model, carrier), ["product"] = PolicyInputs.Text(target, "product"), ["version"] = PolicyInputs.Text(target, "version") };
            }
            else
            {
                RecordEnvelope[] policies = model.Current("estate-policy").Where(p => EstateModel.BodyString(p, "state") == "active" &&
                    PolicyInputs.Items(PolicyInputs.Get(p.Body, "rules")).Any(r => PolicyInputs.Text(r, "kind") == FoundationPolicyKinds.Placement)).ToArray();
                if (policies.Length != 1) return Refuse("A backlog needs one unambiguous active placement registry.");
                if (request.ExpectedSelection != policies[0].ComputedHash) return PolicyConflict();
                if (!PolicyInputs.Items(PolicyInputs.Get(policies[0].Body, "rules")).Where(r => PolicyInputs.Text(r, "kind") == FoundationPolicyKinds.Placement)
                    .SelectMany(r => PolicyInputs.Items(PolicyInputs.Get(PolicyInputs.Get(r, "parameters"), "backlogs"))).Any(b => PolicyInputs.Text(b, "key") == request.Backlog))
                    return Refuse("The named backlog is absent from the current placement registry.");
                placement = new() { ["kind"] = "backlog", ["key"] = request.Backlog };
            }
            JsonObject body = JsonNode.Parse(order.Body.GetRawText())!.AsObject();
            body["placement"] = placement;
            return SignedPolicyWrite(new("work-order", request.By, Element(body), order.Subject, order.Id), model);
        }
    }

    private WriteOutcome SignedPolicyWrite(RecordDraft draft, EstateModel model)
    {
        draft = draft with { At = PolicyClock.GetUtcNow() };
        string? refusal = ResolveSigner(model, draft, out IRecordSigner? signer);
        if (refusal is not null || signer is null) return Refuse(refusal ?? "The acting principal needs an admitted device whose key this runtime holds.");
        return WriteLocked(draft, model, [signer]);
    }

    private static WriteOutcome PolicyConflict() => WriteOutcome.Fail(new("conflict", "The expected hash differs from the current target or prepared body. Read the current evidence and prepare again.", []));

    private static bool IsPolicyWrite(RecordDraft draft) => draft.Type is "estate-policy" or "policy-exception" or "writing-exception" ||
        draft.Type == "work-order" && (PolicyInputs.Get(draft.Body, "placement").ValueKind != JsonValueKind.Undefined || PolicyInputs.Get(draft.Body, "releasePlan").ValueKind != JsonValueKind.Undefined);

    // Called from the common authoring boundary, so generic record authoring receives the same evaluation.
    private Problem? JudgePolicySources(IReadOnlyList<AuthoredRecord> records, EstateModel proposed)
    {
        if (!records.Any(r => r.Envelope.Type is "estate-policy" or "policy-exception" or "writing-exception" or "review" || r.Envelope.Type == "result" && HasDuties(r.Envelope.Body) ||
            r.Envelope.Type == "work-order" && PolicyInputs.Get(r.Envelope.Body, "placement").ValueKind != JsonValueKind.Undefined)) return null;
        PolicyKindRegistry registry = PolicySources.Registry();
        PolicyBasis basis = PolicySnapshot(proposed, registry);
        PolicyEvaluation evaluation = PolicyEvaluator.Evaluate(basis, registry);
        _ = WritingEvaluation.Evaluate(basis);
        List<FindingDto> errors = evaluation.Diagnostics.Where(f => f.Severity == Severity.Error && f.Code == "policy.parameters" &&
            records.Any(r => f.Location.StartsWith(r.RelativePath + "#", StringComparison.Ordinal))).Select(PolicyDiagnostic).ToList();
        foreach (RecordEnvelope exception in records.Select(r => r.Envelope).Where(r => r.Type == "policy-exception" && EstateModel.BodyString(r, "state") == "granted"))
        foreach (JsonElement target in PolicyInputs.Items(PolicyInputs.Get(exception.Body, "targets")))
        {
            JsonElement selected = PolicyInputs.Get(target, "submission");
            PolicySubmission? submission = null;
            if (selected.ValueKind == JsonValueKind.Object)
            {
                RecordEnvelope release = PolicyInputs.Exact(proposed, PolicyInputs.Get(selected, "release"));
                submission = new(release.Reference, PolicyInputs.Text(selected, "product")!, PolicyInputs.Text(selected, "version")!, EstateModel.BodyString(release, "commit")!);
            }
            PolicyBasis targetBasis = PolicySnapshot(proposed, registry, exception.At, submission, preview: true);
            PolicyEvaluation targetEvaluation = PolicyEvaluator.Evaluate(targetBasis, registry);
            if (!targetEvaluation.Findings.Any(f => f.Policy.Id == exception.Subject!.Id && f.Rule == EstateModel.BodyString(exception, "rule") &&
                f.Verdict == "unmet" && PolicyExceptions.TargetMatches(targetBasis, target, f)))
                errors.Add(new("error", "policy.exception-target", exception.ExpectedPath, "The target is not a current unmet finding in the server's exact source snapshot; repair unavailable inputs or prepare a fresh target."));
        }
        foreach (RecordEnvelope order in records.Select(r => r.Envelope).Where(r => r.Type == "work-order" && PolicyInputs.Text(PolicyInputs.Get(r.Body, "placement"), "kind") == "release"))
        {
            var placement = PolicyWork.ReleasePlacement(proposed, order, order.AtTime);
            if (placement.Verdict != "satisfied") errors.Add(new("error", "policy.placement", order.ExpectedPath, placement.Reason));
        }
        return errors.Count == 0 ? null : new("rejected", "The server rejected the proposed policy change after re-evaluating its sources.", errors);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> PolicyAccess(string principal)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            if (model.Findings.HasErrors || RequestDeviceRefusal(model) is not null || model.Manifest?.RecordFormat < 3) return [];
            DateTimeOffset instant = PolicyClock.GetUtcNow();
            string now = RecordParser.FormatTimestamp(instant);
            if (PolicyAuthority.OperatorAt(model, principal, now)) return ["declare", "revise", "retire", "except", "revoke-exception", "writing-add", "writing-remove"];
            HashSet<string> allowed = [];
            foreach (RecordEnvelope grant in model.Current("authority").Where(g => EstateModel.BodyString(g, "grantee") == principal && g.AtTime <= instant &&
                !EstateModel.Expired(g, instant) && SignatureChecks.IsAuthentic(model, g) && !PolicyInputs.Items(PolicyInputs.Get(g.Body, "conditions")).Any()))
            foreach (JsonElement bound in PolicyInputs.Items(PolicyInputs.Get(grant.Body, "bounds")))
            foreach (JsonElement action in PolicyInputs.Items(PolicyInputs.Get(bound, "actions")))
            {
                string? scope = PolicyInputs.Text(bound, "scope");
                string? name = action.GetString();
                if (!PolicyRecords.Contains(PolicyInputs.Get(grant.Body, "scope"), scope) || PolicyRecords.Contains(PolicyInputs.Get(grant.Body, "reserved"), scope) || PolicyRecords.Contains(PolicyInputs.Get(grant.Body, "reserved"), name)) continue;
                string? operation = (scope, name) switch
                {
                    (Scopes.ManageEstatePolicies, "declare" or "revise" or "retire") => name,
                    (Scopes.GrantPolicyExceptions, "grant") => "except",
                    (Scopes.GrantPolicyExceptions, "revoke") => "revoke-exception",
                    (Scopes.ManageWritingExceptions, "add") => "writing-add",
                    (Scopes.ManageWritingExceptions, "remove") => "writing-remove",
                    _ => null,
                };
                if (operation is not null) allowed.Add(operation);
            }
            return allowed.Order(StringComparer.Ordinal).ToArray();
        }
    }
}
