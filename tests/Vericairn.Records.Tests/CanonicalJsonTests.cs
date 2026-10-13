// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Canonical;

namespace Vericairn.Records.Tests;

public class CanonicalJsonTests
{
    private static string Canonical(string json)
    {
        using JsonDocument document = CanonicalJson.Parse(Encoding.UTF8.GetBytes(json));
        return CanonicalJson.Serialize(document.RootElement);
    }

    private static string Pretty(string json)
    {
        using JsonDocument document = CanonicalJson.Parse(Encoding.UTF8.GetBytes(json));
        return CanonicalJson.SerializePretty(document.RootElement);
    }

    [Fact]
    public void SortsKeysByUtf16CodeUnits()
    {
        Assert.Equal("""{"a":2,"b":1}""", Canonical("""{"b":1,"a":2}"""));
        Assert.Equal("""{"A":1,"_":2,"a":3,"ä":4}""", Canonical("""{"ä":4,"a":3,"_":2,"A":1}"""));
        Assert.Equal("""{"x":{"a":[3,{"b":1,"a":2}]}}""".Replace("{\"b\":1,\"a\":2}", "{\"a\":2,\"b\":1}", StringComparison.Ordinal), Canonical("""{"x":{"a":[3,{"b":1,"a":2}]}}"""));
    }

    [Fact]
    public void EscapesOnlyWhatRfc8785Requires()
    {
        Assert.Equal("\"\\u0001\\b\\f\\n\\r\\t\\\"\\\\/é😀\"", Canonical("\"\\u0001\\b\\f\\n\\r\\t\\\"\\\\\\/\\u00e9\\ud83d\\ude00\""));
        Assert.Equal("\"\\u001f\"", Canonical("\"\\u001F\""));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1e2")]
    [InlineData("1E2")]
    [InlineData("9007199254740992")]
    [InlineData("-9007199254740992")]
    public void RejectsNonSafeIntegers(string number)
    {
        Assert.Throws<CanonicalJsonException>(() => Canonical(number));
    }

    [Fact]
    public void AcceptsSafeIntegersAndNormalizesNegativeZero()
    {
        Assert.Equal("9007199254740991", Canonical("9007199254740991"));
        Assert.Equal("0", Canonical("-0"));
        Assert.Equal("[true,false,null]", Canonical("[true, false, null]"));
    }

    [Fact]
    public void RejectsDuplicateKeysCommentsAndTrailingCommas()
    {
        Assert.Throws<CanonicalJsonException>(() => Canonical("""{"a":1,"a":2}"""));
        Assert.Throws<CanonicalJsonException>(() => Canonical("""{"a":1,}"""));
        Assert.Throws<CanonicalJsonException>(() => Canonical("""{"a":1} // c"""));
    }

    [Fact]
    public void PrettyFormIsStableAndReadable()
    {
        string pretty = Pretty("""{"b":[1,{"d":{},"c":[]}],"a":"x"}""");
        Assert.Equal("{\n  \"a\": \"x\",\n  \"b\": [\n    1,\n    {\n      \"c\": [],\n      \"d\": {}\n    }\n  ]\n}\n", pretty);
        Assert.Equal(pretty, Pretty(pretty));
        Assert.Equal(Canonical(pretty), Canonical("""{"b":[1,{"d":{},"c":[]}],"a":"x"}"""));
    }
}
