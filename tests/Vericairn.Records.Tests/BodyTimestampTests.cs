// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>A body timestamp is checked for its form first, so any other form is refused with the field's name and the required form.</summary>
public sealed class BodyTimestampTests
{
    [Fact]
    public void AWholeSecondUtcTimestampIsAccepted()
    {
        (string? value, FindingList findings) = Read("""{"asOf":"2026-10-13T14:00:00Z"}""");
        Assert.Equal("2026-10-13T14:00:00Z", value);
        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("""{"asOf":"2026-10-13T14:00:00.123456Z"}""")]
    [InlineData("""{"asOf":"2026-10-13T14:00:00.1Z"}""")]
    [InlineData("""{"asOf":"2026-10-13T14:00:00+02:00"}""")]
    [InlineData("""{"asOf":"2026-10-13T14:00:00-0500"}""")]
    [InlineData("""{"asOf":"2026-10-13T14:00:00"}""")]
    [InlineData("""{"asOf":"2026-10-13 14:00:00Z"}""")]
    [InlineData("""{"asOf":"2026-02-30T14:00:00Z"}""")]
    [InlineData("""{"asOf":"soon"}""")]
    [InlineData("""{"asOf":""}""")]
    [InlineData("""{"asOf":1760364000}""")]
    [InlineData("""{"asOf":null}""")]
    public void AnyOtherFormIsRefusedWithTheFieldAndTheRequiredForm(string body)
    {
        (string? value, FindingList findings) = Read(body);
        Assert.Null(value);
        Finding finding = Assert.Single(findings);
        Assert.Equal("body.timestamp", finding.Code);
        Assert.Equal("body.asOf", finding.Location);
        Assert.Equal("'asOf' must be a UTC time in whole seconds, YYYY-MM-DDTHH:MM:SSZ, such as 2026-10-13T14:00:00Z, without fractional seconds or an offset.", finding.Message);
    }

    [Fact]
    public void AMissingRequiredTimestampIsRefusedWithTheRequiredForm()
    {
        (string? value, FindingList findings) = Read("{}");
        Assert.Null(value);
        Finding finding = Assert.Single(findings);
        Assert.Equal("body.missing", finding.Code);
        Assert.Equal("'asOf' is required: a UTC time in whole seconds, YYYY-MM-DDTHH:MM:SSZ, such as 2026-10-13T14:00:00Z, without fractional seconds or an offset.", finding.Message);
    }

    [Fact]
    public void AMissingOptionalTimestampIsNoFinding()
    {
        var findings = new FindingList();
        using JsonDocument document = JsonDocument.Parse("{}");
        Assert.Null(new BodyReader(document.RootElement, "body", findings).Timestamp("asOf", required: false));
        Assert.Empty(findings);
    }

    [Fact]
    public void EveryTimestampFieldIsDescribedWithItsForm()
    {
        FieldDescription[] timestamps = RecordTypeRegistry.All.SelectMany(t => t.Fields).Where(f => f.Kind.StartsWith("timestamp", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(timestamps);
        Assert.All(timestamps, f => Assert.Equal("timestamp (YYYY-MM-DDTHH:MM:SSZ)", f.Kind));
    }

    private static (string? Value, FindingList Findings) Read(string body)
    {
        var findings = new FindingList();
        using JsonDocument document = JsonDocument.Parse(body);
        string? value = new BodyReader(document.RootElement, "body", findings).Timestamp("asOf", required: true);
        return (value, findings);
    }
}
