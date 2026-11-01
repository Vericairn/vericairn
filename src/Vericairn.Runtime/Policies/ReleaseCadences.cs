// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Policies.PolicyInputs;

namespace Vericairn.Runtime.Policies;

/// <summary>Projects declared cadences from the existing duty evaluation; never authors or dispatches work.</summary>
internal static class ReleaseCadences
{
    internal sealed record Declaration(RecordEnvelope Product, RecordEnvelope Policy, string Rule, RecordEnvelope Origin)
    {
        internal bool Matches(EstateModel model, PolicyFinding finding) => finding.Kind == DutyRecords.Kind && finding.Version == 1 && finding.Rule == Rule &&
            finding.PolicyIdentity == PolicyWork.Identity(model, Policy) &&
            (finding.Duty is { } duty ? duty.Origin == Origin.Reference :
                model.ById.TryGetValue(finding.Policy.Id, out RecordEnvelope? current) && DutyRecords.IsDuty(DutyRecords.Rule(current, Rule)) &&
                DutyRecords.Origin(model, current, Rule).Reference == Origin.Reference);
    }

    internal static Declaration[] Declarations(EstateModel model) => model.Current("product").Select(product =>
    {
        JsonElement cadence = Get(product.Body, "cadence"), pin = Get(cadence, "policy");
        string? rule = Text(cadence, "rule");
        if (Text(pin, "type") != "estate-policy" || !model.ById.TryGetValue(Text(pin, "id") ?? "", out RecordEnvelope? policy) ||
            policy.Type != "estate-policy" || policy.ComputedHash != Text(pin, "hash") || rule is null || !DutyRecords.IsDuty(DutyRecords.Rule(policy, rule))) return null;
        return new Declaration(product, policy, rule, DutyRecords.Origin(model, policy, rule));
    }).OfType<Declaration>().OrderBy(d => d.Product.Id, StringComparer.Ordinal).ToArray();

    internal static ReleaseCadenceDto[] Due(EstateModel model, IEnumerable<PolicyFinding> findings, PolicySubmission? submission, string? line)
    {
        List<ReleaseCadenceDto> rows = [];
        foreach (Declaration declaration in Declarations(model))
        {
            string product = PolicyWork.Identity(model, declaration.Product);
            if (submission is not null && submission.Product != product) continue;
            (string Line, string? Version, PolicyRecordReference[] Evidence)[] targets = submission is null ? Targets(model, product) :
                [(line ?? CandidateLine(model, product, submission.Version), submission.Version, [])];
            foreach (PolicyFinding finding in findings.Where(f => f.IsReleaseCadence && f.Enforced && declaration.Matches(model, f) &&
                f.Duty is { Completed: false, EndedReason: null } duty && DutyRecords.Time(f.AsOf) is { } at && DutyRecords.Time(duty.DueAt) <= at))
            {
                foreach (var target in targets)
                    rows.Add(new(Reference(declaration.Product), EstateModel.BodyString(declaration.Product, "name") ?? product,
                        Reference(declaration.Policy), declaration.Rule, Reference(declaration.Origin), finding.Duty!.Occurrence,
                        finding.Duty.DueAt, target.Line, target.Version, target.Evidence));
            }
        }
        return rows.OrderBy(r => r.Product.Id, StringComparer.Ordinal).ThenBy(r => r.DueAt, StringComparer.Ordinal)
            .ThenBy(r => r.Line, StringComparer.Ordinal).ThenBy(r => r.Version, StringComparer.Ordinal).ToArray();
    }

    private static (string Line, string? Version, PolicyRecordReference[] Evidence)[] Targets(EstateModel model, string product)
    {
        RecordEnvelope[] releases = model.Current("release").Where(r => r.Subject is { Type: "product" } subject && Identity(model, subject.Id) == product).ToArray();
        HashSet<string> published = releases.Where(r => EstateModel.BodyString(r, "status") == "published")
            .Select(r => EstateModel.BodyString(r, "version")!).ToHashSet(StringComparer.Ordinal);
        var targets = releases.Where(r => EstateModel.BodyString(r, "status") == "candidate" && !published.Contains(EstateModel.BodyString(r, "version")!))
            .Select(r => (Line: ReleaseLines.Line(r.Body), Version: EstateModel.BodyString(r, "version"), Record: r)).ToList();
        Dictionary<string, string> states = EstateStatus.WorkItems(model).ToDictionary(w => w.Id, w => w.Status, StringComparer.Ordinal);
        foreach (RecordEnvelope order in model.Current("work-order"))
        {
            JsonElement plan = Get(order.Body, "releasePlan");
            string? version = Text(plan, "version");
            if (Identity(model, Text(plan, "product") ?? "") != product || version is null || published.Contains(version)) continue;
            if (states.GetValueOrDefault(order.Id) is "accepted" or "retired") continue;
            targets.Add((CandidateLine(model, product, version), version, order));
        }
        return targets.Count == 0 ? [("current", null, [])] : targets.GroupBy(t => (t.Line, t.Version))
            .Select(g => (g.Key.Line, g.Key.Version, g.Select(t => Reference(t.Record)).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray())).ToArray();
    }

    private static string CandidateLine(EstateModel model, string product, string version) => model.Current("release")
        .Where(r => r.Subject is { Type: "product" } subject && Identity(model, subject.Id) == product && EstateModel.BodyString(r, "version") == version && EstateModel.BodyString(r, "status") == "candidate")
        .OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => ReleaseLines.Line(r.Body)).FirstOrDefault() ?? "current";

    private static string Identity(EstateModel model, string id) => model.ById.TryGetValue(id, out RecordEnvelope? record) ? PolicyWork.Identity(model, record) : id;
    private static PolicyRecordReference Reference(RecordEnvelope record) => new(record.Type, record.Id, record.ComputedHash);
}
