// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
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

    internal static TestEstate RecordedFixture(bool reserved = false)
    {
        var estate = new TestEstate(3);
        string version = reserved ? "0.0.1-reserved" : "1.0.0";
        JsonNode declaration = JsonNode.Parse(Product)!;
        if (reserved) declaration["packages"]!["reserved"] = new JsonArray(version);
        RecordEnvelope product = estate.Add("product", estate.OperatorId, declaration.ToJsonString());
        RecordEnvelope? release = reserved ? null : estate.Add("release", estate.OperatorId, """
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
            if (reserved) bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("1.0.0", version, StringComparison.Ordinal));
            evidence.Add(new JsonObject { ["url"] = url, ["hash"] = ContentHash.Sha256(bytes), ["status"] = 200, ["bytes"] = bytes.Length, ["encoding"] = "identity" });
        }
        var body = new JsonObject
        {
            ["kind"] = "package", ["asOf"] = RecordParser.FormatTimestamp(estate.Clock), ["summary"] = "Recorded registry fixture: published and listed.",
            ["facts"] = new JsonObject
            {
                ["provider"] = "nuget-org", ["product"] = product.Id, ["productHash"] = product.ComputedHash,
                ["package"] = "example.widget", ["version"] = version, ["publication"] = "published", ["listing"] = "listed", ["deprecation"] = "not-deprecated",
                ["packageContent"] = $"https://api.nuget.org/v3-flatcontainer/example.widget/{version}/example.widget.{version}.nupkg",
            },
            ["evidence"] = evidence, ["limitations"] = new JsonArray("Offline response fixture; no live registry claim."),
        };
        if (release is not null)
        {
            body["facts"]!["release"] = release.Id;
            body["facts"]!["releaseHash"] = release.ComputedHash;
        }
        else body["facts"]!["reserved"] = version;
        estate.Add("observation", estate.OperatorId, body.ToJsonString(), (release ?? product).Reference);
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

    [Theory]
    [InlineData("[\"0.0.1-reserved\"]", true)]
    [InlineData("[\"0.0.1-reserved\",\"0.0.2-reserved\",\"0.0.3-reserved\",\"0.0.4-reserved\",\"0.0.5-reserved\",\"0.0.6-reserved\",\"0.0.7-reserved\",\"0.0.8-reserved\"]", true)]
    [InlineData("[]", false)]
    [InlineData("[\"0.0.1-reserved\",\"0.0.1-reserved\"]", false)]
    [InlineData("[\"0.0.1-reserved\",\"0.0.1-RESERVED\"]", false)]
    [InlineData("[\"1.0\"]", false)]
    [InlineData("[\"01.0.0-reserved\"]", false)]
    [InlineData("[\"0.0.1-\"]", false)]
    [InlineData("[\"0.0.1-reserved\",null]", false)]
    [InlineData("\"0.0.1-reserved\"", false)]
    [InlineData("[\"0.0.1\",\"0.0.2\",\"0.0.3\",\"0.0.4\",\"0.0.5\",\"0.0.6\",\"0.0.7\",\"0.0.8\",\"0.0.9\"]", false)]
    public void ReservedDeclarationsAreBoundedDistinctNuGetVersions(string versions, bool valid)
    {
        using var estate = new TestEstate(3);
        JsonNode product = JsonNode.Parse(Product)!;
        product["packages"]!["reserved"] = JsonNode.Parse(versions);
        estate.Add("product", estate.OperatorId, product.ToJsonString());
        Assert.Equal(valid, !estate.Load().Findings.HasErrors);
        Assert.Empty(estate.Load().OfType("release"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReservedVersionsCannotOverlapRecordedReleasesAcrossProductRevisions(bool releaseFirst)
    {
        using TestEstate estate = RecordedFixture(reserved: !releaseFirst);
        RecordEnvelope product = Assert.Single(estate.Load().OfType("product"));
        JsonNode revised = JsonNode.Parse(product.Body.GetRawText())!;
        revised["summary"] = "A revised declaration.";
        if (releaseFirst) revised["packages"]!["reserved"] = new JsonArray("1.0.0");
        RecordEnvelope current = estate.Add("product", estate.OperatorId, revised.ToJsonString(), supersedes: product.Id);
        if (!releaseFirst) estate.Add("release", estate.OperatorId, """
            {"version":"0.0.1-RESERVED","repository":"Example/widget","commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","status":"candidate",
             "artifacts":[],"recordFormat":{"reads":[3],"writes":3},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}
            """, current.Reference);
        Assert.Contains(estate.Load().Findings, finding => finding.Code == "product.packages-reserved");
    }

    [Fact]
    public void AnotherProductsReleaseDoesNotConflictWithAReservation()
    {
        using TestEstate estate = RecordedFixture();
        JsonNode product = JsonNode.Parse(Product)!;
        product["slug"] = "another-widget";
        product["packages"]!["reserved"] = new JsonArray("1.0.0");
        estate.Add("product", estate.OperatorId, product.ToJsonString());
        Assert.False(estate.Load().Findings.HasErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReservedObservationsAndIntentsPinTheDeclarationWithoutARelease(bool intent)
    {
        using TestEstate estate = RecordedFixture(reserved: true);
        RecordEnvelope observed = Assert.Single(estate.Load().OfType("observation"));
        if (intent) estate.Add("operation", estate.OperatorId, IntentBody(observed).ToJsonString(), observed.Subject);
        Assert.False(estate.Load().Findings.HasErrors);
        Assert.Empty(estate.Load().OfType("release"));
        Assert.All(estate.Load().Records, record => Assert.Equal(3, record.Format));
    }

    [Theory]
    [InlineData(false, "undeclared", "package.reserved")]
    [InlineData(true, "undeclared", "package.reserved")]
    [InlineData(false, "mixed-release", "package.reserved")]
    [InlineData(true, "mixed-release", "package.reserved")]
    [InlineData(false, "mixed-hash", "package.reserved")]
    [InlineData(true, "mixed-hash", "package.reserved")]
    [InlineData(false, "missing-reservation", "package.release")]
    [InlineData(true, "missing-reservation", "package.release")]
    [InlineData(false, "invalid-reservation", "package.reserved")]
    [InlineData(true, "invalid-reservation", "package.reserved")]
    [InlineData(false, "version-mismatch", "package.reserved")]
    [InlineData(true, "version-mismatch", "package.reserved")]
    [InlineData(false, "product-hash", "package.product")]
    [InlineData(true, "product-hash", "package.product")]
    [InlineData(false, "wrong-subject", "observation.package-subject")]
    [InlineData(true, "wrong-subject", "operation.package-target")]
    public void ReservedPinsCannotBeForgedOrMixedWithReleasePins(bool intent, string defect, string code)
    {
        using TestEstate estate = RecordedFixture(reserved: true);
        RecordEnvelope observed = Assert.Single(estate.Load().OfType("observation"));
        JsonObject body = intent ? IntentBody(observed) : JsonNode.Parse(observed.Body.GetRawText())!.AsObject();
        JsonObject pins = body[intent ? "target" : "facts"]!.AsObject();
        RecordReference? subject = observed.Subject;
        switch (defect)
        {
            case "undeclared": pins["reserved"] = "0.0.2-reserved"; pins["version"] = "0.0.2-reserved"; break;
            case "mixed-release": pins["release"] = observed.Subject!.Id; break;
            case "mixed-hash": pins["releaseHash"] = null; break;
            case "missing-reservation": pins.Remove("reserved"); break;
            case "invalid-reservation": pins["reserved"] = true; break;
            case "version-mismatch": pins["version"] = "0.0.2-reserved"; break;
            case "product-hash": pins["productHash"] = estate.Operator.ComputedHash; break;
            case "wrong-subject": subject = estate.Operator.Reference; break;
        }
        estate.Add(intent ? "operation" : "observation", estate.OperatorId, body.ToJsonString(), subject);
        Assert.Contains(estate.Load().Findings, finding => finding.Code == code);
    }

    [Theory]
    [InlineData("evidence", "observation.evidence")]
    [InlineData("future", "observation.future")]
    [InlineData("before-intent", "observation.package-intent")]
    [InlineData("changed-pins", "observation.package-intent")]
    public void ReservedOutcomesKeepTheEvidenceAndTimeRules(string defect, string code)
    {
        using TestEstate estate = RecordedFixture(reserved: true);
        RecordEnvelope observed = Assert.Single(estate.Load().OfType("observation"));
        RecordEnvelope intent = estate.Add("operation", estate.OperatorId, IntentBody(observed).ToJsonString(), observed.Subject);
        JsonNode body = JsonNode.Parse(observed.Body.GetRawText())!;
        body["asOf"] = intent.At;
        switch (defect)
        {
            case "evidence": body["evidence"]!.AsArray().RemoveAt(0); break;
            case "future": body["asOf"] = RecordParser.FormatTimestamp(estate.Clock.AddDays(1)); break;
            case "before-intent": body["asOf"] = observed.At; break;
            case "changed-pins": body["facts"]!["package"] = "Example.Widget"; break;
        }
        estate.Add("observation", estate.OperatorId, body.ToJsonString(), intent.Reference);
        Assert.Contains(estate.Load().Findings, finding => finding.Code == code);
    }

    private static JsonObject IntentBody(RecordEnvelope observation)
    {
        var target = new JsonObject();
        foreach (string key in new[] { "provider", "product", "productHash", "reserved", "package", "version" })
            target[key] = observation.Body.GetProperty("facts").GetProperty(key).GetString();
        return new JsonObject
        {
            ["provider"] = "nuget-org", ["action"] = "package-unlist", ["identity"] = "reserved-unlisting", ["intent"] = "Unlist the placeholder.",
            ["target"] = target, ["status"] = "not-started", ["evidence"] = new JsonArray(),
        };
    }
}
