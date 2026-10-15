// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Authoring;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class RecordParserTests
{
    private static AuthoredRecord Sample()
    {
        using JsonDocument body = JsonDocument.Parse("""{"status":"active","note":"Working."}""");
        string by = Ulid.NewUlid().Value;
        var subject = new RecordReference("work-order", Ulid.NewUlid().Value, null);
        return RecordAuthor.Create(new RecordDraft("work-status", by, body.RootElement, subject, null, new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void AuthoredRecordsParseCleanlyAndLiveAtTheirExpectedPath()
    {
        AuthoredRecord record = Sample();
        var findings = new FindingList();
        RecordEnvelope? parsed = RecordParser.Parse(record.Bytes, record.RelativePath, findings);
        Assert.NotNull(parsed);
        Assert.Empty(findings);
        Assert.Equal($"records/work-status/2026-10/{parsed.Id}.json", record.RelativePath);
        Assert.Equal(parsed.DeclaredHash, parsed.ComputedHash);
        Assert.Equal("2026-10-13T14:00:00Z", parsed.At);
    }

    [Fact]
    public void EditingContentIsDetectedByTheHash()
    {
        AuthoredRecord record = Sample();
        string text = Encoding.UTF8.GetString(record.Bytes).Replace("\"Working.\"", "\"Edited.\"", StringComparison.Ordinal);
        var findings = new FindingList();
        RecordEnvelope? parsed = RecordParser.Parse(Encoding.UTF8.GetBytes(text), "x", findings);
        Assert.NotNull(parsed);
        Assert.Contains(findings, f => f.Code == "record.hash-mismatch");
    }

    [Fact]
    public void FormattingDriftIsDetectedAndFormatRestoresIt()
    {
        AuthoredRecord record = Sample();
        string compact = Encoding.UTF8.GetString(record.Bytes).Replace("\n", string.Empty, StringComparison.Ordinal).Replace("  ", string.Empty, StringComparison.Ordinal);
        var findings = new FindingList();
        Assert.NotNull(RecordParser.Parse(Encoding.UTF8.GetBytes(compact), "x", findings));
        Assert.Contains(findings, f => f.Code == "record.formatting");
        Assert.Equal(record.Bytes, RecordAuthor.Format(Encoding.UTF8.GetBytes(compact)));
    }

    [Theory]
    [InlineData("""{"format":"vericairn.record/1","type":"work-status","id":"01ARZ3NDEKTSV4RRFFQ69G5FAV","at":"2026-10-13T14:00:00Z","by":"01ARZ3NDEKTSV4RRFFQ69G5FAV","body":{},"hash":"sha256:0000000000000000000000000000000000000000000000000000000000000000","extra":1}""", "record.envelope")]
    [InlineData("""{"format":"vericairn.record/3","type":"work-status","id":"01ARZ3NDEKTSV4RRFFQ69G5FAV","at":"2026-10-13T14:00:00Z","by":"01ARZ3NDEKTSV4RRFFQ69G5FAV","body":{},"hash":"sha256:0000000000000000000000000000000000000000000000000000000000000000"}""", "record.format")]
    [InlineData("""{"format":"vericairn.record/1","type":"Work Status","id":"01ARZ3NDEKTSV4RRFFQ69G5FAV","at":"2026-10-13T14:00:00Z","by":"01ARZ3NDEKTSV4RRFFQ69G5FAV","body":{},"hash":"sha256:0000000000000000000000000000000000000000000000000000000000000000"}""", "record.type")]
    [InlineData("""{"format":"vericairn.record/1","type":"work-status","id":"01ARZ3NDEKTSV4RRFFQ69G5FAV","at":"2026-10-13T14:00:00.000Z","by":"01ARZ3NDEKTSV4RRFFQ69G5FAV","body":{},"hash":"sha256:0000000000000000000000000000000000000000000000000000000000000000"}""", "record.at")]
    [InlineData("""{"format":"vericairn.record/1","type":"work-status","id":"01ARZ3NDEKTSV4RRFFQ69G5FAV","at":"2026-10-13T14:00:00Z","by":"01ARZ3NDEKTSV4RRFFQ69G5FAV","body":[],"hash":"sha256:0000000000000000000000000000000000000000000000000000000000000000"}""", "record.body")]
    public void EnvelopeProblemsAreReported(string json, string expectedCode)
    {
        var findings = new FindingList();
        RecordParser.Parse(Encoding.UTF8.GetBytes(json), "x", findings, checkFormatting: false);
        Assert.Contains(findings, f => f.Code == expectedCode);
    }
}
