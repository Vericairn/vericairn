// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public class ReleasePinWarningTests
{
    [Fact]
    public void RuntimeCheckWarnsFromItsOwnPublishedRecordsWithoutChangingStatusOrPin()
    {
        using var estate = new TestEstate();
        Grant(estate);
        Pin(estate, "1.0.0");
        Release(estate, "1.9.0");
        Release(estate, "1.10.0");
        Release(estate, "9.0.0", published: false);
        Release(estate, "10.0.0-preview");
        Release(estate, "99.0.0", repository: "Example/another-product");
        using EstateService service = EstateService.Open(estate.Root);
        var findings = service.Check();
        Assert.Equal(0, findings.Errors);
        var warning = Assert.Single(findings.Findings, f => f.Code == "manifest.release-behind");
        Assert.Equal("warning", warning.Severity);
        Assert.Contains("newer published release 1.10.0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("vericairn estate upgrade --to 1.10.0 --check", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("manifest.release-behind", service.Status().Text, StringComparison.Ordinal);
        Assert.Equal("1.0.0", estate.Load().Manifest?.Version);
    }

    [Theory]
    [InlineData("1.9.0", "v1.10.0", "1.10.0")]
    [InlineData("1.10.0", "v1.9.0", "1.10.0")]
    [InlineData("1.10.0", null, "1.10.0")]
    public void ChoosesTheNewestOfRecordsAndCachedKnowledge(string recorded, string? cached, string expected)
    {
        using var estate = new TestEstate();
        Grant(estate);
        Pin(estate, "1.0.0");
        Release(estate, recorded);
        var findings = new FindingList();
        ReleasePinWarning.Add(estate.Load(), findings, PublishedRelease.FromTag(cached));
        Finding warning = Assert.Single(findings);
        Assert.Equal(Severity.Warning, warning.Severity);
        Assert.Contains("newer published release " + expected, warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1.10.0", "v1.10.0", "Vericairn/vericairn")]
    [InlineData("2.0.0", "v1.10.0", "Vericairn/vericairn")]
    [InlineData("1.0.0", null, "Vericairn/vericairn")]
    [InlineData("1.0.0", "v1.10.0", "Example/fork")]
    public void UnknownEqualOlderAndOtherRepositoryReleasesDoNotWarn(string pin, string? tag, string repository)
    {
        using var estate = new TestEstate();
        Pin(estate, pin, repository);
        var findings = new FindingList();
        ReleasePinWarning.Add(estate.Load(), findings, PublishedRelease.FromTag(tag));
        Assert.Empty(findings);
    }

    [Fact]
    public void ASourcePinIsNotMistakenForAnOlderRelease()
    {
        using var estate = new TestEstate();
        var findings = new FindingList();
        ReleasePinWarning.Add(estate.Load(), findings, PublishedRelease.FromTag("v99.0.0"));
        Assert.Empty(findings);
    }

    private static void Pin(TestEstate estate, string version, string repository = "Vericairn/vericairn") =>
        estate.WriteFile(EstateLayout.ManifestPath, CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(new
        {
            estate = estate.Estate.Id, format = EstateManifest.Format1, recordFormat = 1,
            vericairn = new { mode = "release", repository, version, artifacts = new Dictionary<string, string> { ["linux-x64"] = "sha256:" + new string('1', 64) } },
        })));

    private static void Grant(TestEstate estate)
    {
        RecordEnvelope decision = estate.Add("decision", estate.BuilderId, $$"""{"title":"Delegate","statement":"Engineer.","summary":"Delegation.","synthesis":"Standing delegation.","effects":["builder engineers"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}""");
        estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{estate.BuilderId}}","scope":["local-engineering"],"reserved":["publication"],"conditions":["label self-review"],"basis":"{{decision.Id}}"}""");
    }

    private static void Release(TestEstate estate, string version, bool published = true, string repository = "Vericairn/vericairn")
    {
        RecordEnvelope product = estate.Add("product", estate.OperatorId, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
        string candidate = $$"""{"version":"{{version}}","repository":"{{repository}}","commit":"0123456789abcdef0123456789abcdef01234567","status":"candidate","artifacts":[{"name":"vericairn-{{version}}-linux-x64.zip","hash":"sha256:{{new string('1', 64)}}","bytes":10,"platform":"linux-x64"}],"recordFormat":{"reads":[1],"writes":1},"evidence":[{"summary":"Tests passed."}],"qualified":["linux-x64"],"signing":"unsigned developer build","limitations":[]}""";
        var subject = new RecordReference("product", product.Id, null);
        RecordEnvelope record = estate.Add("release", estate.BuilderId, candidate, subject);
        if (!published) return;
        estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted from the recorded evidence.","basis":[],"self":false}""", record.Reference);
        estate.Add("release", estate.BuilderId, candidate.Replace("\"status\":\"candidate\"", "\"status\":\"published\",\"publishedAt\":\"2026-10-13T20:00:00Z\",\"url\":\"https://example.invalid/releases/current\"", StringComparison.Ordinal), subject, supersedes: record.Id);
    }
}
