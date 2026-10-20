// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Objects;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public PolicyResponse Policies() => PolicyEvaluate();

    /// <inheritdoc />
    public PolicyResponse? Policy(string key)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            if (model.Current("estate-policy").Count(r => EstateModel.BodyString(r, "key") == key) > 1)
            {
                PolicyResponse conflict = PolicyRead(model, null, null) with { Complete = false,
                    Problem = new("conflict", "The policy key has conflicting declarations; inspect their exact identities and reconcile the records.", []) };
                return conflict with { Text = PolicyPresentation.Render(conflict) };
            }
            RecordEnvelope? policy = FindPolicy(model, key);
            return policy is null ? null : PolicyRead(model, null, null, policy);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PolicyKindDto> PolicyKinds() => PolicySources.Registry().Registrations.Select(r =>
        new PolicyKindDto(r.Kind, r.Version, r.ImplementationDigest, r.Selections, r.SourceContracts, r.MaximumRecords, r.MaximumFindings)).ToArray();

    /// <inheritdoc />
    public PolicyResponse PolicyEvaluate(string? asOf = null, string? release = null)
    {
        lock (gate) return PolicyRead(session.Load(), asOf, release);
    }

    /// <inheritdoc />
    public WritingExceptionsResponse WritingExceptions()
    {
        lock (gate) return WritingRead(session.Load());
    }

    /// <inheritdoc />
    public WritingExceptionsResponse? WritingException(string id)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? entry = EstateServiceValues.Latest(model, id, "writing-exception");
            return entry is null ? null : WritingRead(model, entry);
        }
    }

    private static RecordEnvelope? FindPolicy(EstateModel model, string key) =>
        EstateServiceValues.Latest(model, key, "estate-policy") ?? model.Current("estate-policy")
            .OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault(r => EstateModel.BodyString(r, "key") == key);

    private PolicyResponse PolicyRead(EstateModel model, string? asOf, string? release, RecordEnvelope? selected = null)
    {
        PolicyKindRegistry registry = PolicySources.Registry();
        RecordEnvelope? candidate = release is null ? null : EstateServiceValues.Latest(model, release, "release");
        Problem? problem = release is not null && candidate is null ? new("not-found", "The named release target is unavailable; use its release record id.", []) : null;
        HashSet<string> repositories = candidate is null ? [] : model.Current("resource").Where(r => EstateModel.BodyString(r, "kind") == "repository" &&
            string.Equals(PolicyInputs.Text(PolicyInputs.Get(r.Body, "location"), "github"), EstateModel.BodyString(candidate, "repository"), StringComparison.OrdinalIgnoreCase))
            .SelectMany(model.Chain).ToHashSet(StringComparer.Ordinal);
        PolicySubmission? submission = candidate is null ? null : SelectRelease(model, candidate.Subject?.Id ?? "",
            EstateModel.BodyString(candidate, "version") ?? "", EstateModel.BodyString(candidate, "commit"), repositories, candidate.Reference);
        PolicyBasis basis = PolicySnapshot(model, registry, asOf, submission, preview: asOf is not null || release is not null);
        PolicyEvaluation evaluated = PolicyEvaluator.Evaluate(basis, registry);
        return PolicyView(model, basis, evaluated, selected, problem);
    }

    private PolicyResponse PolicyView(EstateModel model, PolicyBasis basis, PolicyEvaluation evaluated, RecordEnvelope? selected = null, Problem? problem = null)
    {
        PolicyFinding[] findings = evaluated.Findings.Where(f => selected is null || f.PolicyIdentity == PolicyWork.Identity(model, selected)).ToArray();
        RecordEnvelope[] policies = model.Current("estate-policy").Where(p => selected is null || p.Id == selected.Id)
            .OrderBy(p => EstateModel.BodyString(p, "key"), StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal).ToArray();
        HashSet<string> identities = policies.Select(p => PolicyWork.Identity(model, p)).ToHashSet(StringComparer.Ordinal);
        RecordResponse[] exceptions = model.OfType("policy-exception").Where(e => e.Subject is { } s && model.ById.TryGetValue(s.Id, out RecordEnvelope? p) && identities.Contains(PolicyWork.Identity(model, p)))
            .OrderBy(e => e.At, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal).Select(e => PolicyRecord(model, e)).ToArray();
        var response = new PolicyResponse("", BasisView(basis), policies.Select(p => PolicyRecord(model, p)).ToArray(), exceptions,
            findings.Select(ConvertPolicy<PolicyFindingDto>).ToArray(), evaluated.Rules.Where(r => policies.Any(p => p.Id == r.Policy.Id)).Select(ConvertPolicy<PolicyRuleSummaryDto>).ToArray(),
            evaluated.Diagnostics.Select(PolicyDiagnostic).ToArray(), evaluated.Complete && problem is null, findings.Count(f => f.ReleaseHold), problem)
        { Requirements = evaluated.Requirements?.Where(r => selected is null || r.Policy == EstateModel.BodyString(selected, "key") || identities.Contains(r.Policy))
            .Select(ConvertPolicy<PolicyRequirementDto>).ToArray() };
        return response with { Text = PolicyPresentation.Render(response) };
    }

    private WritingExceptionsResponse WritingRead(EstateModel model, RecordEnvelope? selected = null)
    {
        PolicyBasis basis = PolicySnapshot(model, PolicySources.Registry());
        WritingEvaluation evaluated = WritingEvaluation.Evaluate(basis);
        return WritingView(model, basis, evaluated, selected);
    }

    private WritingExceptionsResponse WritingView(EstateModel model, PolicyBasis basis, WritingEvaluation evaluated, RecordEnvelope? selected = null)
    {
        HashSet<string>? chain = selected is null ? null : model.Chain(selected).ToHashSet(StringComparer.Ordinal);
        WritingOccurrenceDto Occurrence(WritingOccurrence item) => new(ConvertPolicy<WritingLocationDto>(item.Location),
            new(item.Occurrence.Token, item.Occurrence.Line, item.Occurrence.ByteOffset, item.Occurrence.ByteLength, PolicyDiagnostic(item.Occurrence.Diagnostic)),
            item.Basis, item.Entries.Select(ConvertPolicy<PolicyRecordReference>).ToArray());
        var response = new WritingExceptionsResponse("", BasisView(basis), evaluated.Scanner, evaluated.Complete,
            model.OfType("writing-exception").Where(e => chain is null || chain.Contains(e.Id)).OrderBy(e => e.At, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal).Select(e => PolicyRecord(model, e)).ToArray(),
            evaluated.Entries.Where(e => chain is null || chain.Contains(e.Entry.Id)).Select(ConvertPolicy<WritingEntryDto>).ToArray(),
            evaluated.Warnings.Where(w => selected is null || w.Occurrence.Token == EstateModel.BodyString(selected, "token")).Select(Occurrence).ToArray(),
            evaluated.ExceptedOccurrences.Where(w => chain is null || w.Entries.Any(e => chain.Contains(e.Id))).Select(Occurrence).ToArray(), evaluated.Diagnostics.Select(PolicyDiagnostic).ToArray());
        return response with { Text = PolicyPresentation.Render(response) };
    }

    private PolicyBasis PolicySnapshot(EstateModel model, PolicyKindRegistry registry, string? asOf = null, PolicySubmission? submission = null, bool preview = false, bool live = false)
    {
        List<PolicyRepository> repositories = [PolicySources.Estate(model)];
        // Only explicit repository relations select another repository. No network or provider lookup is involved.
        HashSet<string> selected = [];
        foreach (RecordEnvelope record in model.Current("estate-policy").Concat(model.Current("writing-exception")).Concat(model.OfType("result"))) SelectRepositories(record.Body);
        foreach (RecordEnvelope resource in model.Current("resource").Where(r => EstateModel.BodyString(r, "kind") == "repository"))
        {
            if (!model.Chain(resource).Any(selected.Contains)) continue;
            string identity = PolicyWork.Identity(model, resource);
            string? commit = RepositoryCommit(model, resource, submission);
            PolicyRepository inventory = PolicySources.RepositorySource?.Read(model, resource, commit)
                ?? new(identity, commit ?? "unavailable", PolicyBasis.RepositoryContract, false, []);
            repositories.Add(PolicySources.ObserveTrees(model, registry, submission, resource, inventory));
        }
        // Completion evidence keeps its own exact historical commits, independently of live release inputs.
        foreach (JsonElement source in model.OfType("result").SelectMany(r => PolicyInputs.Items(PolicyInputs.Get(r.Body, "duties")))
            .SelectMany(d => PolicyInputs.Items(PolicyInputs.Get(PolicyInputs.Get(d, "scope"), "repositories"))))
        {
            JsonElement reference = PolicyInputs.Get(source, "record");
            string? id = PolicyInputs.Text(reference, "id"), commit = PolicyInputs.Text(source, "commit");
            if (id is null || commit is null || !model.ById.TryGetValue(id, out RecordEnvelope? resource) || resource.Type != "resource" ||
                resource.ComputedHash != PolicyInputs.Text(reference, "hash")) continue;
            string identity = PolicyWork.Identity(model, resource);
            if (repositories.Any(r => r.Identity == identity && r.Revision == commit)) continue;
            PolicyRepository inventory = PolicySources.RepositorySource?.Read(model, resource, commit)
                ?? new(identity, commit, PolicyBasis.RepositoryContract, false, []);
            repositories.Add(inventory with { Evidence = true });
        }
        List<PolicyObject> objects = [];
        HashSet<string> hashes = [];
        HashSet<string> proofs = model.OfType("result").SelectMany(r => PolicyInputs.Items(PolicyInputs.Get(r.Body, "duties")))
            .SelectMany(d => PolicyInputs.Items(PolicyInputs.Get(d, "proofs"))).SelectMany(p => PolicyInputs.Items(PolicyInputs.Get(p, "records")))
            .Select(r => PolicyInputs.Text(r, "id")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (RecordEnvelope record in model.Records)
        {
            if (record.Type == "result")
            {
                FindObjects(PolicyInputs.Get(record.Body, "outputs"));
                foreach (JsonElement duty in PolicyInputs.Items(PolicyInputs.Get(record.Body, "duties")))
                foreach (JsonElement hash in PolicyInputs.Items(PolicyInputs.Get(PolicyInputs.Get(duty, "scope"), "objects")))
                    if (hash.ValueKind == JsonValueKind.String && ContentHash.IsValid(hash.GetString())) hashes.Add(hash.GetString()!);
            }
            if (record.Type is "result" or "verification" || proofs.Contains(record.Id)) FindObjects(PolicyInputs.Get(record.Body, "evidence"));
        }
        foreach (string hash in hashes.Order(StringComparer.Ordinal))
        {
            try
            {
                byte[]? bytes = session.Objects.Read(hash);
                objects.Add(new(hash, bytes is null ? [] : [.. bytes], bytes is not null));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectCorruptedException) { objects.Add(new(hash, [], false)); }
        }
        // Live time is sampled after source acquisition, so a deadline crossed while reading cannot use the earlier instant.
        string? time = live ? RecordParser.FormatTimestamp(PolicyClock.GetUtcNow()) : asOf ?? model.Records.Select(r => r.At).Where(RecordParser.IsTimestamp).Order(StringComparer.Ordinal).LastOrDefault();
        return new(model, time, live ? PolicyTimeOrigin.Submission : preview ? PolicyTimeOrigin.Preview : PolicyTimeOrigin.Recorded, repositories, objects, registry.Implementations, submission);

        void SelectRepositories(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (PolicyInputs.Text(value, "type") == "resource" && PolicyInputs.Text(value, "id") is { } resource) selected.Add(resource);
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (property.Name is "repository" or "registerRepository" && property.Value.ValueKind == JsonValueKind.String) selected.Add(property.Value.GetString()!);
                    SelectRepositories(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (JsonElement item in value.EnumerateArray()) SelectRepositories(item);
        }
        void FindObjects(JsonElement value)
        {
            foreach (JsonElement item in PolicyInputs.Items(value))
                if (PolicyInputs.Text(item, "hash") is { } hash && ContentHash.IsValid(hash)) hashes.Add(hash);
        }
    }

    private RecordResponse PolicyRecord(EstateModel model, RecordEnvelope record)
    {
        using JsonDocument document = JsonDocument.Parse(session.LoadedFile(record.ExpectedPath)!.Bytes);
        return new(record.ExpectedPath, record.Id, record.Type, record.ComputedHash, model.IsCurrent(record), document.RootElement.Clone());
    }

    private static string? RepositoryCommit(EstateModel model, RecordEnvelope resource, PolicySubmission? submission)
    {
        string? location = PolicyInputs.Text(PolicyInputs.Get(resource.Body, "location"), "github");
        if (submission?.Repositories?.Contains(PolicyWork.Identity(model, resource), StringComparer.Ordinal) == true) return submission.Commit;
        if (submission?.Release is { } selected && model.ById.TryGetValue(selected.Id, out RecordEnvelope? release) &&
            (location is not null && location == EstateModel.BodyString(release, "repository") || resource.Subject?.Id == submission.Product ||
             model.ById.TryGetValue(submission.Product, out RecordEnvelope? product) && model.Chain(resource).Contains(PolicyInputs.Text(PolicyInputs.Get(product.Body, "changes"), "repository") ?? "", StringComparer.Ordinal)))
            return submission.Commit;
        if (location is not null && model.Manifest is { Mode: "release", Version: { } version } pin && pin.Repository == location)
        {
            string[] commits = model.OfType("release").Where(r => EstateModel.BodyString(r, "status") == "published" && EstateModel.BodyString(r, "repository") == location && EstateModel.BodyString(r, "version") == version)
                .Select(r => EstateModel.BodyString(r, "commit") ?? "").Distinct(StringComparer.Ordinal).ToArray();
            return commits.Length == 1 ? commits[0] : "unavailable";
        }
        return null;
    }

    private static T ConvertPolicy<T>(object value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, ProtocolJson.Options), ProtocolJson.Options)!;
    private static FindingDto PolicyDiagnostic(Finding finding) => new(finding.Severity.ToString().ToLowerInvariant(), finding.Code, finding.Location, finding.Message);
    private static PolicyBasisDto BasisView(PolicyBasis basis) => new(basis.Digest, basis.RecordDigest, basis.AsOf, basis.Origin.ToString().ToLowerInvariant(),
        basis.Submission is null ? null : ConvertPolicy<PolicySubmissionDto>(basis.Submission), basis.Repositories.Select(r => new PolicyRepositoryDto(r.Identity, r.Revision, r.Contract, r.Complete,
            r.Files.Select(f => new PolicyFileDto(f.Path, f.Hash, f.Available, f.IsLink)).ToArray(), r.Evidence)
            { TreeComparisons = r.TreeComparisons.Select(ConvertPolicy<PolicyTreeComparisonDto>).ToArray() }).ToArray(), basis.Objects.Select(o => o.Hash).ToArray(), basis.Implementations.Select(ConvertPolicy<PolicyImplementationDto>).ToArray());
}
