// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Packs.Software;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

public class ReferenceInventoryTests
{
    internal static readonly string Commit = new('a', 40);
    internal static readonly ReferenceItem[] Items = [new("command", "vericairn status"), new("record-type", "release"), new("api-operation", "GET /v1/status")];

    internal static PolicyRepository Repository(string prefix = "", string? commit = null)
    {
        var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["cli-reference.md"] = Encoding.UTF8.GetBytes("# Commands\n\n## `vericairn status`\n"),
            ["record-reference.md"] = Encoding.UTF8.GetBytes("# Records\n\n## `release`\n"),
            ["api-reference.md"] = Encoding.UTF8.GetBytes("# API\n\n## `GET /v1/status`\n"),
        };
        string inventory = ReferenceInventory.Generate(commit ?? Commit, Items, bytes);
        return new("repository", commit ?? Commit, PolicyBasis.RepositoryContract, true,
            [PolicyBasisTests.File(prefix + ReferenceInventory.FileName, inventory), .. bytes.Select(b => PolicyBasisTests.File(prefix + b.Key, Encoding.UTF8.GetString(b.Value)))]);
    }

    [Fact]
    public void GenerationBindsAllExactIdentitiesAndReferenceBytesToTheBuildCommit()
    {
        PolicyRepository repository = Repository("docs/generated/");
        RetainedReferenceInventory inventory = ReferenceInventory.Read(repository, "docs/generated/" + ReferenceInventory.FileName, Commit);
        Assert.Equal(Commit, inventory.Commit);
        Assert.Equal(Items.OrderBy(i => i.Category, StringComparer.Ordinal).ThenBy(i => i.Name, StringComparer.Ordinal), inventory.Items);
        Assert.Equal(inventory.Items, ReferenceInventory.Read(repository with { Files = [.. repository.Files.Reverse()] }, "docs/generated/" + ReferenceInventory.FileName, Commit).Items);
    }

    [Theory]
    [InlineData("missing-inventory")]
    [InlineData("missing-reference")]
    [InlineData("wrong-commit")]
    [InlineData("wrong-source-commit")]
    [InlineData("wrong-hash")]
    [InlineData("wrong-bytes")]
    [InlineData("unavailable")]
    [InlineData("unsupported-version")]
    [InlineData("duplicate-item")]
    [InlineData("duplicate-category")]
    [InlineData("duplicate-field")]
    [InlineData("omitted-item")]
    [InlineData("extra-item")]
    [InlineData("traversal")]
    [InlineData("link")]
    [InlineData("case-collision")]
    [InlineData("incomplete")]
    [InlineData("invalid-json")]
    public void DefectiveInventoriesNeverReturnAPartialCount(string defect)
    {
        PolicyRepository repository = Repository();
        JsonObject manifest = JsonNode.Parse(PolicyInputs.Read(repository.Files[0]))!.AsObject();
        switch (defect)
        {
            case "wrong-commit": manifest["commit"] = new string('b', 40); break;
            case "unsupported-version": manifest["format"] = "vericairn.reference-inventory/2"; break;
            case "duplicate-item": manifest["items"]!.AsArray().Add(manifest["items"]![0]!.DeepClone()); break;
            case "duplicate-category": manifest["references"]![0] = manifest["references"]![1]!.DeepClone(); break;
            case "wrong-hash": manifest["references"]![0]!["hash"] = PolicyBasis.Hash("wrong"); break;
            case "omitted-item": manifest["items"]!.AsArray().RemoveAt(0); break;
            case "extra-item": manifest["items"]!.AsArray().Add(new JsonObject { ["category"] = "command", ["name"] = "vericairn invented" }); break;
            case "traversal": manifest["references"]![0]!["path"] = "../api-reference.md"; break;
        }
        string json = manifest.ToJsonString();
        if (defect == "duplicate-field") json = json.Replace("\"commit\":", "\"commit\":\"" + Commit + "\",\"commit\":", StringComparison.Ordinal);
        if (defect == "invalid-json") json = "{";
        repository = repository with { Files = [PolicyBasisTests.File(ReferenceInventory.FileName, json), .. repository.Files.Skip(1)] };
        repository = defect switch
        {
            "missing-inventory" => repository with { Files = [.. repository.Files.Skip(1)] },
            "missing-reference" => repository with { Files = [.. repository.Files.SkipLast(1)] },
            "wrong-source-commit" => repository with { Revision = new string('b', 40) },
            "wrong-bytes" => repository with { Files = [.. repository.Files.Select(f => f.Path == "api-reference.md" ? f with { Bytes = [1, 2] } : f)] },
            "unavailable" => repository with { Files = [.. repository.Files.Select(f => f with { Available = false })] },
            "link" => repository with { Files = [.. repository.Files.Select(f => f with { IsLink = true })] },
            "case-collision" => repository with { Files = [.. repository.Files, repository.Files[0] with { Path = ReferenceInventory.FileName.ToUpperInvariant() }] },
            "incomplete" => repository with { Complete = false },
            _ => repository,
        };
        Assert.Throws<PolicyInputException>(() => ReferenceInventory.Read(repository, ReferenceInventory.FileName, Commit));
    }

    [Theory]
    [InlineData("command", "vericairn status", true)]
    [InlineData("command", "vericairn release submit", true)]
    [InlineData("command", "Vericairn status", false)]
    [InlineData("command", "vericairn status --json", false)]
    [InlineData("command", "vericairn release *", false)]
    [InlineData("record-type", "estate-policy", true)]
    [InlineData("record-type", "Estate-policy", false)]
    [InlineData("api-operation", "POST /v1/work/{id}/attempt", true)]
    [InlineData("api-operation", "post /v1/work/{id}/attempt", false)]
    [InlineData("api-operation", "GET  /v1/status", false)]
    public void IdentitiesAreCanonicalAndExact(string category, string name, bool valid) => Assert.Equal(valid, ReferenceInventory.ValidItem(new(category, name)));

    [Fact]
    public void GenerationRefusesUnboundOrIncompleteReferences()
    {
        Assert.Throws<PolicyInputException>(() => ReferenceInventory.Generate("working-tree", [], new Dictionary<string, byte[]>()));
        Assert.Throws<PolicyInputException>(() => ReferenceInventory.Generate(Commit, Items, new Dictionary<string, byte[]>()));
        PolicyRepository repository = Repository();
        var references = repository.Files.Skip(1).ToDictionary(f => f.Path, f => f.Bytes.ToArray(), StringComparer.Ordinal);
        Assert.Throws<PolicyInputException>(() => ReferenceInventory.Generate(Commit, Items.Skip(1), references));
        references["api-reference.md"] = Encoding.UTF8.GetBytes("# API\n~~~\n## `GET /v1/status`\n~~~\n");
        Assert.Throws<PolicyInputException>(() => ReferenceInventory.Generate(Commit, Items, references));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("incomplete")]
    [InlineData("different")]
    [InlineData("missing")]
    [InlineData("source")]
    [InlineData("selected")]
    [InlineData("paths")]
    [InlineData("duplicate")]
    [InlineData("invalid-hash")]
    public void RetainedInventoryRequiresOneCompleteComparisonOfBothCommitsAndAllBuildPaths(string observation)
    {
        string selected = new('b', 40);
        string tree = PolicyBasis.Hash("identical build trees");
        var comparison = new PolicyTreeComparison(Commit, selected, BuildSourcePaths.All, true, tree, tree);
        comparison = observation switch
        {
            "incomplete" => comparison with { Complete = false },
            "different" => comparison with { SelectedTree = PolicyBasis.Hash("different build trees") },
            "source" => comparison with { SourceCommit = new string('c', 40) },
            "selected" => comparison with { SelectedCommit = new string('c', 40) },
            "paths" => comparison with { Paths = ["src"] },
            "invalid-hash" => comparison with { SourceTree = "unavailable", SelectedTree = "unavailable" },
            _ => comparison,
        };
        PolicyRepository source = Repository("docs/releases/0.8.0/") with { Revision = selected,
            TreeComparisons = observation == "missing" ? [] : observation == "duplicate" ? [comparison, comparison] : [comparison] };
        const string path = "docs/releases/0.8.0/reference-inventory.json";
        if (observation == "valid")
        {
            RetainedReferenceInventory inventory = ReferenceInventory.Read(source, path, selected);
            Assert.Equal(Commit, inventory.Commit);
            Assert.Equal(3, inventory.Items.Length);
        }
        else Assert.Throws<PolicyInputException>(() => ReferenceInventory.Read(source, path, selected));
    }
}
