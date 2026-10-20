// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

public sealed class PackageRecordsTests
{
    private const string Product = """
        {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],
         "surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],"packages":{"provider":"nuget-org","ids":["Example.Widget"]}}
        """;

    internal static TestEstate RecordedFixture()
    {
        var estate = new TestEstate(3);
        RecordEnvelope product = estate.Add("product", estate.OperatorId, Product);
        RecordEnvelope release = estate.Add("release", estate.OperatorId, """
            {"version":"1.0.0","repository":"Example/widget","commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","status":"candidate",
             "artifacts":[],"recordFormat":{"reads":[3],"writes":3},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}
            """, product.Reference);
        var evidence = new JsonArray();
        foreach ((string fixture, string url) in new[]
        {
            ("service-index", "https://api.nuget.org/v3/index.json"),
            ("versions", "https://api.nuget.org/v3-flatcontainer/example.widget/index.json"),
            ("listed", "https://api.nuget.org/v3/registration5-gz-semver2/example.widget/index.json"),
        })
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(RecordCompanionTests.FindRepository(), "tests", "TestSupport", "NuGetFixtures", fixture + ".json"));
            evidence.Add(new JsonObject { ["url"] = url, ["hash"] = ContentHash.Sha256(bytes), ["status"] = 200, ["bytes"] = bytes.Length, ["encoding"] = "identity" });
        }
        var body = new JsonObject
        {
            ["kind"] = "package", ["asOf"] = RecordParser.FormatTimestamp(estate.Clock), ["summary"] = "Recorded registry fixture: published and listed.",
            ["facts"] = new JsonObject
            {
                ["provider"] = "nuget-org", ["product"] = product.Id, ["productHash"] = product.ComputedHash, ["release"] = release.Id, ["releaseHash"] = release.ComputedHash,
                ["package"] = "example.widget", ["version"] = "1.0.0", ["publication"] = "published", ["listing"] = "listed", ["deprecation"] = "not-deprecated",
                ["packageContent"] = "https://api.nuget.org/v3-flatcontainer/example.widget/1.0.0/example.widget.1.0.0.nupkg",
            },
            ["evidence"] = evidence, ["limitations"] = new JsonArray("Offline response fixture; no live registry claim."),
        };
        estate.Add("observation", estate.OperatorId, body.ToJsonString(), release.Reference);
        Assert.False(estate.Load().Findings.HasErrors);
        return estate;
    }

    [Fact]
    public void APackageDeclarationRequiresRecordFormatThree()
    {
        using var old = new TestEstate(2);
        old.Add("product", old.OperatorId, Product);
        Assert.True(old.Load().Findings.HasErrors);
        using var current = new TestEstate(3);
        current.Add("product", current.OperatorId, Product);
        Assert.False(current.Load().Findings.HasErrors);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("too-many")]
    [InlineData("other-registry")]
    [InlineData("invalid-id")]
    public void InvalidInventoriesAreRefused(string defect)
    {
        using var estate = new TestEstate(3);
        JsonNode body = JsonNode.Parse(Product)!;
        JsonArray ids = body["packages"]!["ids"]!.AsArray();
        switch (defect)
        {
            case "empty": ids.Clear(); break;
            case "duplicate": ids.Add("example.widget"); break;
            case "too-many": for (int i = 0; i < 16; i++) ids.Add("Example.Widget" + i); break;
            case "other-registry": body["packages"]!["provider"] = "other-registry"; break;
            case "invalid-id": ids[0] = ".."; break;
        }
        estate.Add("product", estate.OperatorId, body.ToJsonString());
        Assert.True(estate.Load().Findings.HasErrors);
    }

    [Theory]
    [InlineData("https://api.nuget.org/v3/index.json", true)]
    [InlineData("https://api.nuget.org/v3-flatcontainer/example.widget/index.json", true)]
    [InlineData("https://api.nuget.org/v3/../index.json", false)]
    [InlineData("https://api.nuget.org/v3/%2e%2e/index.json", false)]
    [InlineData("https://api.nuget.org/v3/index.json?key=value", false)]
    [InlineData("https://api.nuget.org/v3/index.json#fragment", false)]
    public void EvidenceUrlsAreExactPublicApiAddresses(string url, bool valid) => Assert.Equal(valid, PackageIdentity.PublicUrl(url));
}
