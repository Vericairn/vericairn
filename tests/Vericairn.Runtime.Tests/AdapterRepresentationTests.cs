// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Hashing;
using Vericairn.Records.Representation;
using Vericairn.Runtime.Representation;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;
using System.Text.Json.Nodes;

namespace Vericairn.Runtime.Tests;

public class AdapterRepresentationTests(ITestOutputHelper output)
{
    internal static readonly string Eligible = "{\n  \"items\": [\n" + string.Join(",\n", Enumerable.Repeat("    { \"value\": 12.00, \"label\": \"a  b\\n\\u0063\" }", 45)) + "\n  ]\n}";

    internal static string Pin(string text) => ContextRetrieval.Pin(new JsonObject
    {
        ["kind"] = "document", ["path"] = "context/data.json", ["byteHash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(text)),
        ["bytes"] = Encoding.UTF8.GetByteCount(text),
    }, "test-estate");

    internal static string Envelope() => RepresentationContract.Write([
        new("  exact instructions and quoted code: \\u0061\n  ", false, "exact"),
        new(Eligible, true, Pin(Eligible)), new(Eligible, true, Pin(Eligible)),
    ]);

    internal static byte[]? Original(string pin) => pin == Pin(Eligible) ? Encoding.UTF8.GetBytes(Eligible) : null;

    [Fact]
    public void DeterministicReductionsRestoreEveryOriginalThroughItsPin()
    {
        using JsonDocument doc = JsonDocument.Parse(Envelope());
        var blocks = RepresentationContract.Read(doc.RootElement)!;
        RepresentationReduction first = LosslessAdapter.Reduce(blocks, Original);
        RepresentationReduction second = LosslessAdapter.Reduce(blocks, Original);
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(blocks[0].Text, first.Text[0]);
        Assert.Contains("repeat of block 1", first.Text[2], StringComparison.Ordinal);
        Assert.True(first.AfterBytes < first.BeforeBytes);
        Assert.Equal(Encoding.UTF8.GetByteCount(Eligible) * 2, first.BeforeBytes);
        var received = blocks.Select((b, i) => b with { Text = first.Text[i] }).ToArray();
        Assert.Equal(blocks.Select(b => b.Text), LosslessAdapter.Restore(received, Original));
        Assert.Equal(JsonDocument.Parse(Eligible).RootElement.GetProperty("items")[0].GetProperty("label").GetString(),
            JsonDocument.Parse(first.Text[1]).RootElement.GetProperty("items")[0].GetProperty("label").GetString());
        Assert.Contains("12.00", first.Text[1], StringComparison.Ordinal);
        output.WriteLine("Eligible UTF-8 bytes: {0} -> {1}; tokens unknown; every original restored.", first.BeforeBytes, first.AfterBytes);
    }

    [Theory]
    [InlineData("responses")]
    [InlineData("messages")]
    [InlineData("messages-array")]
    public void NativeShapesPreserveProtectedWireBytesWhileReducingEligibleBlocks(string shape)
    {
        byte[] body = Request(shape);
        RequestReduction.Result result = RequestReduction.Reduce(body, Original);
        Assert.True(result.Body.Length < body.Length);
        Assert.Equal(result.Body, RequestReduction.Reduce(body, Original).Body);
        string sent = Encoding.UTF8.GetString(result.Body);
        // The raw protected subtrees, including whitespace and escape spellings, never pass through a serializer.
        Assert.Contains("\"system\" : [ { \"type\" : \"text\", \"text\":\"keep  \\u0061\\n\" } ]", sent, StringComparison.Ordinal);
        Assert.Contains("\"instructions\" : \"brief  \\u0062\\n\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"tools\" : [ { \"name\": \"exact\", \"description\":\"  code  \" } ]", sent, StringComparison.Ordinal);
        Assert.Contains(JsonSerializer.Serialize("  exact instructions and quoted code: \\u0061\n  ")[1..^1].Replace("\\", "\\\\", StringComparison.Ordinal), sent, StringComparison.Ordinal);
        Assert.Contains("repeat of block 1", sent, StringComparison.Ordinal);
        Assert.Equal(2 * Encoding.UTF8.GetByteCount(Eligible), result.BeforeBytes);
        using JsonDocument request = JsonDocument.Parse(result.Body);
        JsonElement value = shape == "responses" ? request.RootElement.GetProperty("input")[1].GetProperty("output")
            : request.RootElement.GetProperty("messages")[0].GetProperty("content")[1].GetProperty("content");
        if (value.ValueKind == JsonValueKind.Array) value = value[0].GetProperty("text");
        using JsonDocument received = JsonDocument.Parse(value.GetString()!);
        using JsonDocument original = JsonDocument.Parse(Envelope());
        Assert.Equal(RepresentationContract.Read(original.RootElement)!.Select(b => b.Text),
            LosslessAdapter.Restore(RepresentationContract.Read(received.RootElement)!, Original));
        output.WriteLine("{0}: eligible bytes {1} -> {2}; wire bytes {3} -> {4}; protected bytes unchanged.", shape, result.BeforeBytes, result.AfterBytes, body.Length, result.Body.Length);
    }

    internal static byte[] Request(string shape)
    {
        string envelope = JsonSerializer.Serialize(Envelope());
        string toolContent = shape == "messages-array" ? "[{\"type\":\"text\",\"text\":" + envelope + "}]" : envelope;
        string content = shape == "responses" ? "\"input\":[{\"role\":\"user\",\"content\":\"assignment exact  \\u0061\"},{\"type\":\"function_call_output\",\"call_id\":\"tool-one\",\"output\":" + envelope + "}]"
            : "\"messages\":[{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"assignment exact  \\u0061\"},{\"type\":\"tool_result\",\"tool_use_id\":\"tool-one\",\"content\":" + toolContent + "}]}]";
        return Encoding.UTF8.GetBytes("{\n\"system\" : [ { \"type\" : \"text\", \"text\":\"keep  \\u0061\\n\" } ],\n\"instructions\" : \"brief  \\u0062\\n\",\n\"tools\" : [ { \"name\": \"exact\", \"description\":\"  code  \" } ]," + content + "}");
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("{\"input\":[{\"type\":\"new-protocol\",\"text\":\"  exact  \"}]}")]
    [InlineData("{\"messages\":[{\"role\":\"unknown\",\"content\":\"  exact  \"}]}")]
    [InlineData("{\"input\":[],\"messages\":[]}")]
    [InlineData("{\"input\":[],\"input\":[]}")]
    [InlineData("{\"unclassified\": \"  exact  \"}")]
    public void UnclassifiedOrMalformedRequestsPassByteForByte(string request)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(request);
        Assert.Same(bytes, RequestReduction.Reduce(bytes, _ => throw new InvalidOperationException()).Body);
    }

    [Fact]
    public void LabelsNeverOverrideRequiredOriginalsUnknownPinsOrHumanInstructions()
    {
        string pin = Pin(Eligible);
        JsonObject source = new() { ["mandatory"] = true, ["kind"] = "document", ["encoding"] = "base64", ["pin"] = pin,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(Eligible)), ["byteHash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(Eligible)) };
        JsonObject brief = new() { ["context"] = new JsonObject { ["payload"] = new JsonObject { ["originals"] = new JsonArray(source) } } };
        Assert.Empty(EfficiencyRun.Originals(JsonSerializer.SerializeToElement(brief)));
        byte[] bytes = Request("responses");
        Assert.Equal(bytes, RequestReduction.Reduce(bytes, _ => null).Body);
        byte[] instruction = JsonSerializer.SerializeToUtf8Bytes(new { input = new[] { new { role = "user", content = Envelope() } } });
        Assert.Equal(instruction, RequestReduction.Reduce(instruction, Original).Body);
        source["mandatory"] = false;
        Assert.Equal(Encoding.UTF8.GetBytes(Eligible), EfficiencyRun.Originals(JsonSerializer.SerializeToElement(brief))[pin]);
    }

    [Fact]
    public void ExactBlocksAreNotReadForReductionAndUnknownEnvelopeFieldsRefuse()
    {
        RepresentationBlock[] blocks = [new(Eligible, false, "exact")];
        Assert.Equal(Eligible, LosslessAdapter.Reduce(blocks, _ => throw new InvalidOperationException()).Text[0]);
        using JsonDocument doc = JsonDocument.Parse("{\"format\":\"vericairn.representation/1\",\"blocks\":[],\"eligible\":true}");
        Assert.Null(RepresentationContract.Read(doc.RootElement));
    }

    [Theory]
    [InlineData("{ \"nested\": { \"fidelity\": \"exact\", \"value\": 1 } }")]
    [InlineData("{ \"instructions\": \"read  exactly\" }")]
    [InlineData("{ \"hash\": \"preserve  spelling\" }")]
    [InlineData("{ \"mandatory\": true, \"value\": 1 }")]
    [InlineData("{ \"authorization\": \"fixture only\" }")]
    public void ProtectedStructuredMaterialCannotBeRelabelledEligible(string text)
    {
        RepresentationBlock[] blocks = [new(text, true, Pin(text)), new(text, true, Pin(text))];
        RepresentationReduction result = LosslessAdapter.Reduce(blocks, _ => Encoding.UTF8.GetBytes(text));
        Assert.Equal(blocks.Select(b => b.Text), result.Text);
        Assert.Equal(0, result.BeforeBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnicodeAndSurrogateEscapesBeforeEligibleSpansKeepTheirExactWireSpelling(bool literal)
    {
        const string protectedText = "exact 🚀 é 文 \\u0061";
        var options = new JsonSerializerOptions { Encoder = literal ? System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping : null };
        string exactBlock = JsonSerializer.Serialize(new { fidelity = "exact", text = protectedText, original = "exact" }, options);
        string eligibleBlock = JsonSerializer.Serialize(new { fidelity = "eligible", text = Eligible, original = Pin(Eligible) }, options);
        string envelope = "{\"format\":\"vericairn.representation/1\",\"blocks\":[" + exactBlock + "," + eligibleBlock + "]}";
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { input = new[] { new { type = "function_call_output", output = envelope } } }, options);
        RequestReduction.Result result = RequestReduction.Reduce(body, Original);
        Assert.True(result.AfterBytes < result.BeforeBytes);
        Assert.Contains(JsonSerializer.Serialize(exactBlock, options)[1..^1], Encoding.UTF8.GetString(result.Body), StringComparison.Ordinal);
        using JsonDocument request = JsonDocument.Parse(result.Body);
        using JsonDocument reduced = JsonDocument.Parse(request.RootElement.GetProperty("input")[0].GetProperty("output").GetString()!);
        Assert.Equal(protectedText, RepresentationContract.Read(reduced.RootElement)![0].Text);
    }
}
