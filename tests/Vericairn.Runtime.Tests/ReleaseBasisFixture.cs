// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>A retained reference-format basis over isolated records and objects, shared by the surface tests.</summary>
public sealed class ReleaseBasisFixture : IDisposable
{
    public PolicyFixture Records { get; } = new();
    public EstateService Api { get; }
    public JsonObject Stored { get; }
    public string SourceHash { get; }
    public string ProofHash { get; }
    public string Hash { get; }
    public string SourceLocation => $"source:fixture-repository@fixture-revision:docs/evidence.md@{SourceHash}";

    public ReleaseBasisFixture()
    {
        Api = EstateService.Open(Records.Estate.Root);
        SourceHash = Api.Session.Objects.Put("Retained admission source.\n"u8);
        ProofHash = Api.Session.Objects.Put("Retained proof.\n"u8);
        PolicyResponse policies = Api.Policies();
        Stored = JsonSerializer.SerializeToNode(new
        {
            format = "vericairn.release-basis/2",
            basis = policies.Basis,
            records = Api.Session.Load().Records.Select(r => new { r.Type, r.Id, hash = r.ComputedHash }).ToArray(),
            repositories = new[] { new
            {
                identity = "fixture-repository", revision = "fixture-revision", contract = "vericairn.policy-repository/1", complete = true,
                files = new[] { new { path = "docs/evidence.md", hash = SourceHash, available = true, isLink = false } },
            } },
            objects = new[] { new { hash = ProofHash, available = true } },
            policies, documents = Array.Empty<string>(), excludedOperationRevisions = Array.Empty<PolicyRecordReference>(),
        }, ProtocolJson.Options)!.AsObject();
        Hash = Store();
        ReleaseBasisResponse response = Assert.IsType<ReleaseBasisResponse>(Api.ReleaseBasis(Hash));
        Assert.NotNull(response.Evidence);
        Assert.Null(response.Problem);
    }

    public string Store() => Api.Session.Objects.Put(JsonSerializer.SerializeToUtf8Bytes(Stored, ProtocolJson.Options));

    public void LoseObject(string hash, bool corrupted)
    {
        if (corrupted)
            File.WriteAllText(Path.Combine(Records.Estate.Root, EstateLayout.ObjectsDirectory, hash[ContentHash.Prefix.Length..]), "Changed bytes.\n");
        else Assert.True(Api.Session.Objects.Remove(hash));
    }

    public void Dispose() { Api.Dispose(); Records.Dispose(); }
}
