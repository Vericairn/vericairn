// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

[CollectionDefinition("release format reader", DisableParallelization = true)]
public sealed class ReleaseFormatReaderGroup;

[Collection("release format reader")]
public class ReleaseLineReaderTests
{
    [Theory]
    [InlineData("release", "line", "\"one\"")]
    [InlineData("release", "channel", "\"nightly\"")]
    [InlineData("release", "endOfSupport", "\"2028-01-01T00:00:00Z\"")]
    [InlineData("release", "backportOf", "{}")]
    [InlineData("product", "lines", "[]")]
    [InlineData("product", "cadence", "{}")]
    public void AReaderLimitedToFormatTwoRefusesNewRecords(string type, string field, string value)
    {
        JsonObject body = type == "release" ? ReleaseLineFixture.Body() : ReleaseLineFixture.ProductBody();
        body[field] = JsonNode.Parse(value);
        AuthoredRecord record = RecordAuthor.Create(new(type, "01M57TPYNPYJYZKTGCETXTHRT8", PolicyFixture.Element(body), Format: 3));
        using (RecordFormats.ReadThrough(2))
        {
            var findings = new FindingList();
            Assert.Null(RecordParser.Parse(record.Bytes, record.RelativePath, findings));
            Assert.Contains(findings, f => f.Code == "record.format");
        }
    }
}
