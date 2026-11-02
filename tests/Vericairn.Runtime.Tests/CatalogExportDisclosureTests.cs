// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Runtime.Guides;

namespace Vericairn.Runtime.Tests;

public sealed class CatalogExportDisclosureTests
{
    [Theory]
    [InlineData("README.md")]
    [InlineData("LICENSE.md")]
    [InlineData("NOTICE.md")]
    [InlineData("CONTRIBUTING.md")]
    [InlineData("templates/README.md")]
    public void BuiltInPublicTextPassesTheSameDisclosureScan(string path)
    {
        string text = path switch
        {
            "README.md" => CatalogText.Readme,
            "LICENSE.md" => CatalogText.License,
            "NOTICE.md" => CatalogText.Notice,
            "CONTRIBUTING.md" => CatalogText.Contributing,
            "templates/README.md" => CatalogText.Template,
            _ => throw new InvalidOperationException(),
        };
        Assert.Empty(Scan(text, path));
    }

    [Fact]
    public void EveryBundleFileAndNameIsScannedWithoutEchoingRestrictedValues()
    {
        var fixture = new CatalogFixture();
        CatalogBundle bundle = fixture.Generate();
        Assert.True(bundle.Success, string.Join("; ", bundle.Findings));
        Assert.Empty(CatalogDisclosure.Check(bundle.Files, ["synthetic-restricted-label"]));
        foreach (string path in bundle.Files.Keys)
        {
            var altered = bundle.Files.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
            altered[path] = Encoding.UTF8.GetBytes(CatalogFixture.Text(bundle, path) + "\nsynthetic-restricted-label\n");
            IReadOnlyList<GuideFinding> findings = CatalogDisclosure.Check(altered, ["synthetic-restricted-label"]);
            Assert.NotEmpty(findings);
            Assert.DoesNotContain("synthetic-restricted-label", string.Join("; ", findings), StringComparison.Ordinal);
        }
        Assert.NotEmpty(Scan("Safe content", "synthetic-restricted-label.md", ["synthetic-restricted-label"]));
    }

    [Theory]
    [InlineData("record")]
    [InlineData("record-envelope")]
    [InlineData("digest")]
    [InlineData("commit")]
    [InlineData("credential")]
    [InlineData("location")]
    [InlineData("restricted-name")]
    public void PrivateShapesAndRestrictedLabelsCannotEnterRepinnedClearedContent(string fault)
    {
        var fixture = new CatalogFixture();
        string payload = fault switch
        {
            "record" => "0" + new string('1', 25),
            "record-envelope" => "vericairn.record/3",
            "digest" => GuideGenerator.Hash(Encoding.UTF8.GetBytes("Synthetic private input")),
            "commit" => new string('a', 40),
            "credential" => "password" + "=" + new string('x', 12),
            "location" => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "synthetic-catalog-private", "sample.txt")),
            "restricted-name" => "synthetic-restricted-label",
            _ => throw new InvalidOperationException(),
        };
        fixture.Guides.Replace("family00", "the designated person approves every delivery", payload);
        fixture.Plan = CatalogFixture.Selection(fixture.Guides.Index) with { RestrictedText = ["synthetic-restricted-label"] };
        CatalogBundle result = fixture.Generate();
        Assert.False(result.Success);
        Assert.Empty(result.Files);
        Assert.Empty(result.Inventory.Entries);
        Assert.DoesNotContain(payload, string.Join("; ", result.Findings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("html")]
    [InlineData("percent")]
    [InlineData("json")]
    [InlineData("nested")]
    public void EncodingsDoNotConcealRestrictedText(string encoding)
    {
        const string payload = "synthetic-restricted-label";
        string encoded = encoding switch
        {
            "html" => string.Concat(payload.Select(c => "&#" + ((int)c).ToString(System.Globalization.CultureInfo.InvariantCulture) + ";")),
            "percent" => string.Concat(Encoding.UTF8.GetBytes(payload).Select(b => "%" + b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))),
            "json" => string.Concat(payload.Select(c => "\\u" + ((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture))),
            "nested" => WebUtility.HtmlEncode("&#115;ynthetic-restricted-label"),
            _ => throw new InvalidOperationException(),
        };
        Assert.NotEmpty(Scan(encoded, restricted: [payload]));
    }

    [Theory]
    [InlineData("missing-field")]
    [InlineData("extra-field")]
    [InlineData("duplicate-field")]
    [InlineData("unknown-output")]
    [InlineData("omitted-output")]
    [InlineData("hash-outside-identity")]
    [InlineData("wrong-hash")]
    public void OnlyVerifiedManifestIdentityFieldsCanCarryPublicDigests(string fault)
    {
        CatalogBundle bundle = new CatalogFixture().Generate();
        Assert.True(bundle.Success, string.Join("; ", bundle.Findings));
        var files = bundle.Files.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        const string manifest = "manifests/outputs.json";
        JsonObject node = JsonNode.Parse(files[manifest])!.AsObject();
        JsonArray identities = node["files"]!.AsArray();
        switch (fault)
        {
            case "missing-field": node.Remove("files"); break;
            case "extra-field": identities[0]!["note"] = "Not a declared identity field"; break;
            case "duplicate-field":
                files[manifest] = Encoding.UTF8.GetBytes(CatalogFixture.Text(bundle, manifest).Replace("\"format\":", "\"format\": \"duplicate\", \"format\":", StringComparison.Ordinal)); break;
            case "unknown-output": identities[0]!["path"] = "unlisted.md"; break;
            case "omitted-output": identities.RemoveAt(0); break;
            case "hash-outside-identity": node["note"] = new JsonObject { ["sha256"] = identities[0]!["sha256"]!.GetValue<string>() }; break;
            case "wrong-hash": identities[0]!["sha256"] = GuideGenerator.Hash(Encoding.UTF8.GetBytes("Different")); break;
        }
        if (fault != "duplicate-field") files[manifest] = Encoding.UTF8.GetBytes(node.ToJsonString(GuideJson.Options) + "\n");
        Assert.NotEmpty(CatalogDisclosure.Check(files));
    }

    [Fact]
    public void BadUtf8AndPrivateHashesOutsideManifestsAreRefused()
    {
        Assert.NotEmpty(CatalogDisclosure.Check(new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["sample.md"] = [0xff] }));
        string digest = GuideGenerator.Hash(Encoding.UTF8.GetBytes("Public-looking private value"));
        Assert.NotEmpty(Scan(digest));
        Assert.NotEmpty(Scan(digest[7..]));
        Assert.NotEmpty(Scan("private_" + digest[7..] + "_value"));
        Assert.NotEmpty(Scan("Safe", "private_" + new string('1', 26) + "_value.md"));
    }

    [Fact]
    public void JsonEscapingIsDecodedBeforeLocationsAreChecked()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["sources/catalog/example.json"] = CatalogFixture.Json(new { text = "Keep the escaped \\| symbol." }),
        };
        Assert.Empty(CatalogDisclosure.Check(files));
        string location = new string('\\', 2) + string.Join('\\', "synthetic-server", "fixture-share", "file");
        files["sources/catalog/example.json"] = CatalogFixture.Json(new { text = location });
        Assert.Contains(CatalogDisclosure.Check(files), f => f.Code == "disclosure-location");
    }

    [Theory]
    [InlineData("password")]
    [InlineData("access_token")]
    [InlineData("apiKey")]
    public void CredentialPropertiesCannotHideBehindJsonSyntax(string field)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["sources/catalog/example.json"] = CatalogFixture.Json(new Dictionary<string, string>(StringComparer.Ordinal) { [field] = new string('x', 12) }),
        };
        Assert.Contains(CatalogDisclosure.Check(files), f => f.Code == "disclosure-credential");
    }

    private static IReadOnlyList<GuideFinding> Scan(string value, string path = "sample.md", string[]? restricted = null) =>
        CatalogDisclosure.Check(new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = Encoding.UTF8.GetBytes(value) }, restricted);
}
