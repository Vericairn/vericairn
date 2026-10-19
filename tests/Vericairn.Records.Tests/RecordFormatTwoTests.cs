// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>Record format 2: a record states its format and follows that format's rules; nothing written before changes.</summary>
[Trait("Journey", "D19-I")]
public class RecordFormatTwoTests
{
    [Fact]
    public void ARecordStatesItsFormatAndIsReadUnderIt()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope charter = estate.Estate;
        Assert.Equal(2, charter.Format);
        string file = File.ReadAllText(Path.Combine(estate.Root, charter.ExpectedPath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("\"format\": \"vericairn.record/2\"", file, StringComparison.Ordinal);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);
    }

    [Fact]
    public void FormatOneRecordsKeepFormatOnesRules()
    {
        using var estate = new TestEstate();
        estate.Add("decision", estate.BuilderId, Decision(estate, stake: null));
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);

        // A field format 2 introduced is refused in a format-1 record, and named as such rather than as an unknown key.
        RecordEnvelope early = estate.Add("decision", estate.BuilderId, Decision(estate, stake: "low"));
        List<Finding> findings = estate.Load().Findings.Where(f => f.Location.StartsWith(early.ExpectedPath, StringComparison.Ordinal)).ToList();
        Assert.Contains(findings, f => f.Code == "body.format" && f.Location.EndsWith("#body.stake", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "body.format" && f.Location.EndsWith("#body.notAuthorized", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, f => f.Code == "body.unknown-key");
    }

    [Fact]
    public void ARecordAheadOfItsEstateIsRefused()
    {
        using var estate = new TestEstate();
        RecordEnvelope ahead = estate.Add("work-status", estate.BuilderId, """{"status":"planned","note":"Ahead of the estate."}""", format: 2);
        Assert.Contains(estate.Load().Findings, f => f.Code == "record.format-ahead" && f.Location == ahead.ExpectedPath);
    }

    [Fact]
    public void AFormatTwoDecisionStatesItsStakeAndWhatItDoesNotAuthorize()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope bare = estate.Add("decision", estate.BuilderId, Decision(estate, stake: null));
        List<Finding> findings = estate.Load().Findings.Where(f => f.Location.StartsWith(bare.ExpectedPath, StringComparison.Ordinal)).ToList();
        Assert.Contains(findings, f => f.Code == "body.missing" && f.Location.EndsWith("#body.stake", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "body.missing" && f.Location.EndsWith("#body.notAuthorized", StringComparison.Ordinal));

        using var other = new TestEstate(recordFormat: 2);
        RecordEnvelope wrong = other.Add("decision", other.BuilderId, Decision(other, stake: "enormous"));
        Assert.Contains(other.Load().Findings, f => f.Code == "body.enum" && f.Location == wrong.ExpectedPath + "#body.stake");

        using var good = new TestEstate(recordFormat: 2);
        good.Add("decision", good.BuilderId, Decision(good, stake: "high"));
        Assert.DoesNotContain(good.Load().Findings, f => f.Severity == Severity.Error);
    }

    [Fact]
    public void ARevisionKeepsTheOperatorsWords()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope first = estate.Add("decision", estate.BuilderId, Decision(estate, stake: "medium"));
        RecordEnvelope corrected = estate.Add("decision", estate.BuilderId, Decision(estate, stake: "medium", summary: "Corrected summary."), supersedes: first.Id);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);

        estate.Add("decision", estate.BuilderId, Decision(estate, stake: "medium", statement: "Words the operator did not say."), supersedes: corrected.Id);
        Assert.Contains(estate.Load().Findings, f => f.Code == "decision.statement");
    }

    [Fact]
    public void AnEnvelopeOfAnUnknownFormatIsRefused()
    {
        using var estate = new TestEstate(recordFormat: 2);
        string path = Path.Combine(estate.Root, estate.Estate.ExpectedPath.Replace('/', Path.DirectorySeparatorChar));
        string text = File.ReadAllText(path).Replace("vericairn.record/2", RecordFormats.Identifier(RecordFormats.Current + 1), StringComparison.Ordinal);
        var findings = new FindingList();
        RecordParser.Parse(System.Text.Encoding.UTF8.GetBytes(text), "x", findings);
        Assert.Contains(findings, f => f.Code == "record.format");
    }

    [Fact]
    public void AnEstateBeyondThisBuildIsRefusedWithItsReason()
    {
        using var estate = new TestEstate();
        string manifest = Path.Combine(estate.Root, EstateLayout.ManifestPath);
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest).Replace("\"recordFormat\": 1", "\"recordFormat\": 4", StringComparison.Ordinal)))
        {
            File.WriteAllBytes(manifest, Canonical.CanonicalJson.SerializePrettyUtf8(document.RootElement));
        }

        var findings = new FindingList();
        Assert.Null(EstateManifest.Load(estate.Root, findings));
        Finding refusal = Assert.Single(findings, f => f.Code == "manifest.record-format");
        Assert.Contains("record format 4", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABuildNeverWritesAFormatBeyondItsOwn()
    {
        using JsonDocument body = JsonDocument.Parse("""{"status":"planned","note":"x"}""");
        Assert.Throws<ArgumentException>(() => RecordAuthor.Create(new RecordDraft("work-status", "01ARZ3NDEKTSV4RRFFQ69G5FAV", body.RootElement, Format: RecordFormats.Current + 1)));
    }

    private static string Decision(TestEstate estate, string? stake, string statement = "Accept ordinary work yourself.", string summary = "Delegation.")
    {
        string fields = stake is null ? string.Empty : $$""","stake":"{{stake}}","notAuthorized":["publication"]""";
        return $$"""{"title":"Delegate","statement":"{{statement}}","summary":"{{summary}}","synthesis":"Standing delegation.","effects":["builder self-accepts ordinary work"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"{{fields}}}""";
    }
}
