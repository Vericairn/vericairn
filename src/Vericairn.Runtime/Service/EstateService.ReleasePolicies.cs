// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Service;

internal sealed record ReleasePolicyAssessment(PolicyBasis Basis, PolicyEvaluation Evaluation, PolicyResponse View, IReadOnlyList<FindingDto> Objections);

public sealed partial class EstateService
{
    /// <inheritdoc />
    public ReleaseBasisResponse? ReleaseBasis(string hash)
    {
        if (!Vericairn.Records.Hashing.ContentHash.IsValid(hash)) return null;
        byte[]? bytes = ObjectGet(hash);
        if (bytes is null) return null;
        try
        {
            ReleaseBasisEvidence? evidence = JsonSerializer.Deserialize<ReleaseBasisEvidence>(bytes, ProtocolJson.Options);
            return evidence is { Format: "vericairn.release-basis/1", Basis: not null, Policies: not null,
                Records: not null, Repositories: not null, Objects: not null, Documents: not null, ExcludedOperationRevisions: not null }
                ? new(hash, evidence) : null;
        }
        catch (JsonException) { return null; }
    }

    // Tests can change the clock or sources at an actual boundary; no production caller supplies an evaluation verdict.
    internal Action<string>? ReleaseBoundary { get; set; }

    private static PolicySubmission SelectRelease(EstateModel model, string product, string version, string? commit, IReadOnlySet<string> repositories, RecordReference? release = null)
    {
        string Identity(string id) => model.ById.TryGetValue(id, out RecordEnvelope? record) ? PolicyWork.Identity(model, record) : id;
        string[] selectedRepositories = repositories.Select(Identity).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] work = model.Current("work-order").Where(order =>
        {
            JsonElement plan = PolicyInputs.Get(order.Body, "releasePlan"), placement = PolicyInputs.Get(order.Body, "placement");
            return PolicyInputs.Text(plan, "product") == Identity(product) && PolicyInputs.Text(plan, "version") == version ||
                PolicyInputs.Text(placement, "kind") == "release" && PolicyInputs.Text(placement, "product") == Identity(product) && PolicyInputs.Text(placement, "version") == version;
        }).Select(order => PolicyWork.Identity(model, order)).Order(StringComparer.Ordinal).ToArray();
        RecordEnvelope[] candidates = model.Current("release").Where(r => r.Subject is { } subject && Identity(subject.Id) == Identity(product) &&
            EstateModel.BodyString(r, "version") == version && EstateModel.BodyString(r, "commit") == commit && EstateModel.BodyString(r, "status") == "candidate").ToArray();
        return new(release ?? (candidates.Length == 1 ? candidates[0].Reference : null), Identity(product), version, commit ?? "unavailable", selectedRepositories, work);
    }

    private ReleasePolicyAssessment AssessReleasePolicies(EstateModel model, string product, string version, string? commit, IReadOnlySet<string> repositories)
    {
        PolicySubmission submission = SelectRelease(model, product, version, commit, repositories);
        PolicyKindRegistry registry = PolicySources.Registry();
        PolicyBasis basis = PolicySnapshot(model, registry, RecordParser.FormatTimestamp(PolicyClock.GetUtcNow()), submission, live: true);
        PolicyEvaluation evaluation = PolicyEvaluator.Evaluate(basis, registry);
        List<FindingDto> objections = model.Findings.Concat(ChecksWithBatches(model)).Where(f => f.Severity == Severity.Error).Distinct().Select(PolicyDiagnostic).ToList();
        foreach (Finding diagnostic in evaluation.Diagnostics.Where(d => d.Severity == Severity.Error).Except(model.Findings)) objections.Add(PolicyDiagnostic(diagnostic));
        foreach (PolicyFinding finding in evaluation.Findings.Where(f => f.ReleaseHold))
            objections.Add(new("warning", "policy." + finding.Verdict, "policy:" + finding.PolicyKey + "/" + finding.Rule,
                $"{finding.PolicyKey}/{finding.Rule} [{finding.Policy.Id} {finding.Policy.Hash}] {finding.Target.Identity}: {finding.Actual} Required: {finding.Required} Next: {finding.NextAction}" +
                (finding.Duty is { } duty ? $" Due {duty.DueAt}; deadline {duty.Deadline}; occurrence {duty.Occurrence.GetRawText()}." : "")));
        return new(basis, evaluation, PolicyView(model, basis, evaluation), objections);
    }

    private string RetainReleaseBasis(ReleasePolicyAssessment assessment, IReadOnlyList<string> documents, IReadOnlyList<RecordReference> excluded)
    {
        // Ordinary operation evidence points to a retained object; no record field or record type is added.
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(PolicyBasis.Canonical(JsonSerializer.SerializeToElement(new
        {
            format = "vericairn.release-basis/1", basis = assessment.View.Basis, records = assessment.Basis.Records,
            repositories = assessment.Basis.Repositories, objects = assessment.Basis.Objects, policies = assessment.View,
            documents, excludedOperationRevisions = excluded,
        }, ProtocolJson.Options)));
        return session.Objects.Put(bytes);
    }
}
