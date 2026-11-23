// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;
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
            // A basis's record digest covers order as well as signatures. Use the snapshot's retained order.
            records = PolicyBasis.Recorded(Api.Session.Load()).Records.Select(r => new { r.Type, r.Id, hash = r.ComputedHash }).ToArray(),
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

    /// <summary>
    /// Stores the basis as one past the object limit is retained (FIX-163): its documents padded so that it passes the limit, its bytes
    /// cut into parts of exactly the limit, each an object, and a canonical root naming them in order with the whole basis's length and
    /// digest, written here independently of the writer. Returns the root's hash, the parts' hashes and the padded documents.
    /// </summary>
    public (string Root, IReadOnlyList<string> Parts, IReadOnlyList<string> Documents) StoreInParts()
    {
        string[] documents = ["Padding that takes the retained basis past the object limit: " + new string('x', (int)EstateLayout.MaxObjectBytes)];
        Stored["documents"] = new JsonArray([.. documents.Select(d => (JsonNode)d)]);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(Stored, ProtocolJson.Options);
        List<string> parts = [];
        for (long offset = 0; offset < bytes.LongLength; offset += EstateLayout.MaxObjectBytes)
            parts.Add(Api.Session.Objects.Put(bytes.AsSpan((int)offset, (int)Math.Min(EstateLayout.MaxObjectBytes, bytes.LongLength - offset))));
        return (StoreRoot(bytes.LongLength, ContentHash.Sha256(bytes), parts), parts, documents);
    }

    /// <summary>Stores the canonical root naming the given parts in order, with the given length and digest of their joined bytes.</summary>
    public string StoreRoot(long bytes, string digest, IEnumerable<string> parts) => Api.Session.Objects.Put(System.Text.Encoding.UTF8.GetBytes(
        $"{{\"bytes\":{bytes},\"digest\":\"{digest}\",\"format\":\"vericairn.split-object/1\",\"parts\":[{string.Join(",", parts.Select(p => "\"" + p + "\""))}]}}"));

    public void LoseObject(string hash, bool corrupted)
    {
        if (corrupted)
            File.WriteAllText(Path.Combine(Records.Estate.Root, EstateLayout.ObjectsDirectory, hash[ContentHash.Prefix.Length..]), "Changed bytes.\n");
        else Assert.True(Api.Session.Objects.Remove(hash));
    }

    public void Dispose() { Api.Dispose(); Records.Dispose(); }
}
