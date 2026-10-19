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
            .SingleOrDefault(r => EstateModel.BodyString(r, "key") == key);

    private PolicyResponse PolicyRead(EstateModel model, string? asOf, string? release, RecordEnvelope? selected = null)
    {
        PolicyKindRegistry registry = PolicySources.Registry();
        RecordEnvelope? candidate = release is null ? null : EstateServiceValues.Latest(model, release, "release");
        Problem? problem = release is not null && candidate is null ? new("not-found", "The named release target is unavailable; use its release record id.", []) : null;
        PolicySubmission? submission = candidate is null ? null : new(candidate.Reference, candidate.Subject?.Id ?? "",
            EstateModel.BodyString(candidate, "version") ?? "", EstateModel.BodyString(candidate, "commit") ?? "");
        PolicyBasis basis = PolicySnapshot(model, registry, asOf, submission, preview: asOf is not null || release is not null);
        PolicyEvaluation evaluated = PolicyEvaluator.Evaluate(basis, registry);
        PolicyFinding[] findings = evaluated.Findings.Where(f => selected is null || f.PolicyIdentity == PolicyWork.Identity(model, selected)).ToArray();
        RecordEnvelope[] policies = model.Current("estate-policy").Where(p => selected is null || p.Id == selected.Id)
            .OrderBy(p => EstateModel.BodyString(p, "key"), StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal).ToArray();
        HashSet<string> identities = policies.Select(p => PolicyWork.Identity(model, p)).ToHashSet(StringComparer.Ordinal);
        RecordResponse[] exceptions = model.OfType("policy-exception").Where(e => e.Subject is { } s && model.ById.TryGetValue(s.Id, out RecordEnvelope? p) && identities.Contains(PolicyWork.Identity(model, p)))
            .OrderBy(e => e.At, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal).Select(e => PolicyRecord(model, e)).ToArray();
        var response = new PolicyResponse("", BasisView(basis), policies.Select(p => PolicyRecord(model, p)).ToArray(), exceptions,
            findings.Select(ConvertPolicy<PolicyFindingDto>).ToArray(), evaluated.Rules.Where(r => policies.Any(p => p.Id == r.Policy.Id)).Select(ConvertPolicy<PolicyRuleSummaryDto>).ToArray(),
            evaluated.Diagnostics.Select(PolicyDiagnostic).ToArray(), evaluated.Complete && problem is null, findings.Count(f => f.ReleaseHold), problem);
        return response with { Text = PolicyPresentation.Render(response) };
    }

    private WritingExceptionsResponse WritingRead(EstateModel model, RecordEnvelope? selected = null)
    {
        PolicyBasis basis = PolicySnapshot(model, PolicySources.Registry());
        WritingEvaluation evaluated = WritingEvaluation.Evaluate(basis);
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

    private PolicyBasis PolicySnapshot(EstateModel model, PolicyKindRegistry registry, string? asOf = null, PolicySubmission? submission = null, bool preview = false)
    {
        List<PolicyRepository> repositories = [PolicySources.Estate(model)];
        // Only explicit repository relations select another repository. No network or provider lookup is involved.
        HashSet<string> selected = [];
        foreach (RecordEnvelope record in model.Current("estate-policy").Concat(model.Current("writing-exception"))) SelectRepositories(record.Body);
        foreach (RecordEnvelope resource in model.Current("resource").Where(r => EstateModel.BodyString(r, "kind") == "repository"))
        {
            if (!model.Chain(resource).Any(selected.Contains)) continue;
            string identity = PolicyWork.Identity(model, resource);
            string? commit = RepositoryCommit(model, resource, submission);
            PolicyRepository inventory = PolicySources.RepositorySource?.Read(model, resource, commit)
                ?? new(identity, commit ?? "unavailable", PolicyBasis.RepositoryContract, false, []);
            repositories.Add(inventory);
        }
        List<PolicyObject> objects = [];
        HashSet<string> hashes = [];
        foreach (RecordEnvelope record in model.Records) FindObjects(record.Body);
        foreach (string hash in hashes.Order(StringComparer.Ordinal))
        {
            try
            {
                byte[]? bytes = session.Objects.Read(hash);
                objects.Add(new(hash, bytes is null ? [] : [.. bytes], bytes is not null));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectCorruptedException) { objects.Add(new(hash, [], false)); }
        }
        string? time = asOf ?? model.Records.Select(r => r.At).Where(RecordParser.IsTimestamp).Order(StringComparer.Ordinal).LastOrDefault();
        return new(model, time, preview ? PolicyTimeOrigin.Preview : PolicyTimeOrigin.Recorded, repositories, objects, registry.Implementations, submission);

        void SelectRepositories(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (property.Name is "repository" or "registerRepository" && property.Value.ValueKind == JsonValueKind.String) selected.Add(property.Value.GetString()!);
                    SelectRepositories(property.Value);
                }
            else if (value.ValueKind == JsonValueKind.Array) foreach (JsonElement item in value.EnumerateArray()) SelectRepositories(item);
        }
        void FindObjects(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String && ContentHash.IsValid(value.GetString())) hashes.Add(value.GetString()!);
            else if (value.ValueKind == JsonValueKind.Object) foreach (JsonProperty property in value.EnumerateObject()) FindObjects(property.Value);
            else if (value.ValueKind == JsonValueKind.Array) foreach (JsonElement item in value.EnumerateArray()) FindObjects(item);
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
        if (submission is not null && model.ById.TryGetValue(submission.Release.Id, out RecordEnvelope? release) &&
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
            r.Files.Select(f => new PolicyFileDto(f.Path, f.Hash, f.Available, f.IsLink)).ToArray())).ToArray(), basis.Objects.Select(o => o.Hash).ToArray(), basis.Implementations.Select(ConvertPolicy<PolicyImplementationDto>).ToArray());
}
