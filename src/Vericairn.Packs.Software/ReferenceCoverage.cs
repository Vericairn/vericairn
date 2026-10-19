// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;
using static Vericairn.Runtime.Policies.PolicyInputs;

namespace Vericairn.Packs.Software;

internal static class ReferenceCoverage
{
    internal const string Kind = "vericairn.software/reference-coverage";

    internal static PolicyKindRegistration Registration(string implementationDigest) => new(Kind, 1, implementationDigest,
        ["estate", "product", "repository"], [PolicyBasis.RepositoryContract], 100000, ReferenceInventory.MaximumItems + 1, Validate, Evaluate);

    private static void Validate(PolicyRuleContext context)
    {
        JsonElement p = context.Parameters;
        Shape(p, ["product", "repository", "referenceManifest", "registerRepository", "registerPath", "categories"]);
        String(p, "product"); String(p, "repository"); String(p, "registerRepository");
        if (!Path(String(p, "referenceManifest")) || !Path(String(p, "registerPath"))) throw new PolicyParameterException("Reference coverage requires normalized relative source paths.");
        string[] categories = Strings(p, "categories");
        if (categories.Any(c => !ReferenceInventory.ReferencePaths.ContainsKey(c))) throw new PolicyParameterException("Unknown reference coverage category.");
        JsonElement selection = Get(context.Policy.Body, "appliesTo");
        if (Text(selection, "kind") == "product" && Text(selection, "product") != Text(p, "product")) throw new PolicyParameterException("The coverage rule names another product.");
        if (Text(selection, "kind") == "repository")
        {
            string[] selectors = Strings(selection, "selectors"); Selectors(selectors);
            if (Text(selection, "repository") != Text(p, "registerRepository") || !selectors.Any(s => Matches(s, String(p, "registerPath"))))
                throw new PolicyParameterException("The repository selection must cover the declared register.");
        }
    }

    private static List<PolicyFinding> Evaluate(PolicyRuleContext context)
    {
        string product = String(context.Parameters, "product");
        string repositoryId = String(context.Parameters, "repository");
        if (!context.Model.ById.TryGetValue(product, out RecordEnvelope? productRecord) || productRecord.Type != "product") throw new PolicyInputException("The coverage product is unavailable.");
        if (!context.Model.ById.TryGetValue(repositoryId, out RecordEnvelope? resource) || resource.Type != "resource") throw new PolicyInputException("The reference repository resource is unavailable.");
        resource = context.Model.Latest(resource);
        productRecord = context.Model.Latest(productRecord);
        if (resource.Subject is { Type: "product" } other && other.Id != product) throw new PolicyInputException("The repository is explicitly related to a different product.");
        JsonElement destination = Get(Get(productRecord.Body, "release"), "destination");
        bool related = resource.Subject is { Type: "product" } subject && subject.Id == product ||
            Text(Get(productRecord.Body, "changes"), "repository") == repositoryId ||
            Text(destination, "provider") == "github-releases" && Text(Get(destination, "settings"), "repository") is { } named && named == Text(Get(resource.Body, "location"), "github");
        if (EstateModel.BodyString(resource, "kind") != "repository" || !related) throw new PolicyInputException("The reference repository lacks the declared typed product relation.");
        PolicyRepository repository = Repository(context.Basis, repositoryId);
        (string commit, RecordEnvelope? release) = SelectedCommit(context, repository, resource, product);
        RetainedReferenceInventory inventory = ReferenceInventory.Read(repository, String(context.Parameters, "referenceManifest"), commit);
        string registerId = String(context.Parameters, "registerRepository");
        if (!context.Model.ById.TryGetValue(registerId, out RecordEnvelope? registerSource) || registerSource.Type != "estate" &&
            (registerSource.Type != "resource" || EstateModel.BodyString(registerSource, "kind") != "repository")) throw new PolicyInputException("The register repository identity is unavailable.");
        PolicyRepository register = Repository(context.Basis, registerId);
        string path = String(context.Parameters, "registerPath");
        PolicyFile file = register.Files.SingleOrDefault(f => f.Path == path) ?? throw new PolicyInputException("The declared register file is missing.");
        ReferenceCoverageTables tables = ReferenceCoverageTables.Read(Read(file));
        string[] categories = Strings(context.Parameters, "categories");
        ReferenceItem[] selected = inventory.Items.Where(i => categories.Contains(i.Category, StringComparer.Ordinal)).ToArray();
        var mappings = tables.Mappings.ToLookup(m => m.Item);
        var terms = tables.Terms.ToLookup(t => t.Name, StringComparer.Ordinal);
        var target = new PolicyTarget("file", registerId + ":" + path, Repository: registerId, Path: path, Hash: file.Hash);
        List<PolicyFinding> findings = [];
        foreach (ReferenceItem item in selected)
        {
            ReferenceMapping[] matches = mappings[item].ToArray();
            RegisteredTerm[] current = matches.Length == 1 ? terms[matches[0].Term].Where(t => t.Status == "current").ToArray() : [];
            string? problem = matches.Length == 0 ? "No exact mapping." : matches.Length != 1 ? "Ambiguous mapping: more than one row names this identity." :
                current.Length == 0 ? "The mapping has no current term; historical and avoid rows do not count." : current.Length > 1 ? "The mapped current term name is duplicated." :
                string.IsNullOrWhiteSpace(current[0].Meaning) ? "The mapped term has an empty definition." : !ReferenceCoverageTables.HasOwner(current[0]) ? "The mapped term lacks an owning document and section." : null;
            if (problem is not null) findings.Add(context.Finding(target, Key(item), "unmet", item.Name + ": " + problem + " Build commit " + commit + ".",
                "One exact mapping to a unique current term with a definition and an owning section.", "Revise the register's coverage mapping or term definition.", release,
                release is null ? [] : [release.Reference]));
        }
        var built = inventory.Items.ToHashSet();
        foreach (ReferenceItem historical in tables.Mappings.Select(m => m.Item).Where(i => categories.Contains(i.Category, StringComparer.Ordinal) && !built.Contains(i)).Distinct()
                     .OrderBy(i => i.Category, StringComparer.Ordinal).ThenBy(i => i.Name, StringComparer.Ordinal))
            findings.Add(context.Finding(target, Key(historical), "satisfied", historical.Name + " is absent from build commit " + commit + "; this is historical coverage only.",
                "Historical mappings never prove a feature exists in the selected build.", "Keep or remove the historical mapping attributably.", subjects: 0));
        if (!findings.Any(f => f.Verdict == "unmet")) findings.Add(context.Finding(target, "", "satisfied", Number(selected.Length) + " exact reference items covered at build commit " + commit + ".",
            "Every selected inventory item has a valid mapping.", "No structural coverage action; editorial review remains separate.", release));
        return findings;
    }

    private static (string Commit, RecordEnvelope? Release) SelectedCommit(PolicyRuleContext context, PolicyRepository repository, RecordEnvelope resource, string product)
    {
        if (context.Basis.Submission is { } submission)
        {
            if (submission.Product != product) throw new PolicyInputException("The supplied submission targets a different product.");
            return (submission.Commit, null);
        }
        if (context.Basis.Origin == PolicyTimeOrigin.Preview) return (repository.Revision, null);
        EstateManifest? pin = context.Model.Manifest;
        string? location = Text(Get(resource.Body, "location"), "github");
        if (pin is null || pin.Mode != "release" || pin.Repository != location || pin.Version is null)
            throw new PolicyInputException("Recorded-time reference coverage requires the estate's pinned product release.");
        RecordEnvelope[] publications = context.Model.OfType("release").Where(r => r.Subject is { Type: "product" } subject && subject.Id == product &&
            EstateModel.BodyString(r, "status") == "published" && EstateModel.BodyString(r, "version") == pin.Version && EstateModel.BodyString(r, "repository") == pin.Repository).ToArray();
        string[] commits = publications.Select(r => EstateModel.BodyString(r, "commit") ?? "").Distinct(StringComparer.Ordinal).ToArray();
        if (commits.Length != 1) throw new PolicyInputException("The pinned release commit is missing or ambiguous.");
        return (commits[0], publications.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).First());
    }

    private static string Key(ReferenceItem item) => item.Category + ":" + item.Name;
}
