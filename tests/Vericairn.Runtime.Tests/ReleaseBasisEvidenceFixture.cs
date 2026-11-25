// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// FIX-168: a synthetic release basis in the shape the product retains (EstateService.ReleasePolicies, format 3): canonical JSON whose
/// basis, policy view and repositories record the candidate's commit and each repository's revision, as 0.9.4's candidate bases did, and
/// each of whose repositories lists a file named DocumentSourceGraph.cs near its end, the word that set gitleaks' Sourcegraph rule reading
/// those revisions as tokens. Every identity, hash and commit id is derived from a seed, so the fixture is the same on every run and holds
/// no real one.
/// </summary>
internal static class ReleaseBasisEvidenceFixture
{
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>The candidate's commit, which the basis's submission records in its basis and its policy view.</summary>
    internal static readonly string Submission = Commit("submission");

    /// <summary>The revisions of the three repositories read at exact commits.</summary>
    internal static readonly string[] Revisions = [Commit("product"), Commit("estate"), Commit("site")];

    /// <summary>The source commit of the one tree comparison, which the second repository records.</summary>
    internal static readonly string Compared = Commit("compared");

    /// <summary>Every git revision the basis records.</summary>
    internal static string[] Recorded => [Submission, .. Revisions, Compared];

    /// <summary>
    /// How often gitleaks' Sourcegraph rule reads one of those revisions in the basis's bytes: the submission's commit in the basis and in
    /// its policy view, the three revisions in each of those and in the repositories, and the comparison's two commits in each of the
    /// three. The candidate's commit in the policy view's text is followed by a full stop, which the rule does not read.
    /// </summary>
    internal const int Readings = 2 + 3 * 3 + 3 * 2;

    /// <summary>40 lower-case hexadecimal digits, as a commit id.</summary>
    internal static string Commit(string seed) => Hex("commit " + seed)[..40];

    /// <summary>A content hash.</summary>
    internal static string Hash(string seed) => "sha256:" + Hex("hash " + seed);

    /// <summary>A record id.</summary>
    internal static string Id(string seed) => "01M" + new string([.. SHA256.HashData(Encoding.UTF8.GetBytes("id " + seed)).Take(23).Select(b => Crockford[b % 32])]);

    /// <summary>The path an object of these bytes takes in an estate: its SHA-256 in the objects folder.</summary>
    internal static string ObjectPath(byte[] content) => "objects/" + Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>
    /// The basis's canonical bytes, listing <paramref name="files"/> files in each repository read at an exact commit; with
    /// <paramref name="beside"/>, the first of the repositories it retains also holds that value, once, in a member of its own between
    /// its identity and its revision.
    /// </summary>
    internal static byte[] Basis(int files, string? beside = null, string format = "vericairn.release-basis/3")
    {
        JsonArray Repositories(bool held = false)
        {
            var repositories = new JsonArray();
            string[] names = ["product", "estate", "working", "site"];
            for (int r = 0; r < names.Length; r++)
            {
                var listed = new JsonArray();
                int count = names[r] == "working" ? 4 : files;
                for (int f = 0; f < count; f++)
                {
                    string path = f == count - 2 ? "src/Vericairn.Records/Estate/DocumentSourceGraph.cs" : $"src/Example.{names[r]}/Area{f / 50:D4}/File{f:D6}.cs";
                    listed.Add(new JsonObject { ["available"] = true, ["hash"] = Hash(names[r] + " " + f), ["isLink"] = false, ["path"] = path });
                }

                var repository = new JsonObject
                {
                    ["complete"] = true,
                    ["contract"] = "vericairn.policy-repository/1",
                    ["evidence"] = names[r] != "working",
                    ["files"] = listed,
                    ["identity"] = Id("repository " + names[r]),
                };
                if (r == 0 && held && beside is not null) repository["note"] = beside;
                repository["revision"] = names[r] == "working" ? "working-tree:" + Hash("working tree") : Revisions[r < 2 ? r : 2];
                var comparisons = new JsonArray();
                if (r == 1)
                    comparisons.Add(new JsonObject
                    {
                        ["complete"] = true, ["paths"] = new JsonArray("src"), ["selectedCommit"] = Revisions[1], ["selectedTree"] = Hash("selected tree"),
                        ["sourceCommit"] = Compared, ["sourceTree"] = Hash("selected tree"),
                    });
                repository["treeComparisons"] = comparisons;
                repositories.Add(repository);
            }

            return repositories;
        }

        JsonObject Inputs() => new()
        {
            ["asOf"] = "2026-11-23T21:31:14Z",
            ["digest"] = Hash("digest"),
            ["implementations"] = new JsonArray(new JsonObject { ["digest"] = Hash("implementation"), ["kind"] = "vericairn.release/candidate", ["version"] = 1 }),
            ["objects"] = new JsonArray(Hash("object 1"), Hash("object 2")),
            ["origin"] = "submission",
            ["recordDigest"] = Hash("records"),
            ["repositories"] = Repositories(),
            ["submission"] = new JsonObject
            {
                ["commit"] = Submission, ["product"] = Id("product"), ["release"] = null, ["repositories"] = new JsonArray(Id("repository product")),
                ["version"] = "0.9.4", ["workOrders"] = new JsonArray(Id("work order 1"), Id("work order 2")),
            },
        };

        var basis = new JsonObject
        {
            ["basis"] = Inputs(),
            ["documents"] = new JsonArray("README.md at " + Submission[..12] + " is the content the release names."),
            ["excludedOperationRevisions"] = new JsonArray(),
            ["format"] = format,
            ["objects"] = new JsonArray(new JsonObject { ["available"] = true, ["hash"] = Hash("object 1") }),
            ["policies"] = new JsonObject
            {
                ["basis"] = Inputs(), ["complete"] = true, ["diagnostics"] = new JsonArray(), ["exceptions"] = new JsonArray(), ["findings"] = new JsonArray(),
                ["holds"] = 0, ["policies"] = new JsonArray(), ["problem"] = null, ["requirements"] = new JsonArray(), ["rules"] = new JsonArray(),
                ["text"] = "Release candidate proposed: " + Id("product") + " 0.9.4 at " + Submission + ".\n  No policy holds it.\n",
            },
            ["records"] = new JsonArray(new JsonObject { ["hash"] = Hash("record 1"), ["id"] = Id("record 1"), ["type"] = "decision" }),
            ["repositories"] = Repositories(held: true),
        };
        return CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(basis));
    }

    /// <summary>The objects that retain a basis: its one object, or past the estate's object limit its parts and then its root.</summary>
    internal static (string Path, byte[] Content)[] Objects(byte[] basis) =>
        [.. SplitObject.Plan(basis).Objects.Select(content => (ObjectPath(content), content))];

    private static string Hex(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));
}
