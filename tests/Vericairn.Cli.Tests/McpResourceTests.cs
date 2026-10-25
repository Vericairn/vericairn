// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class McpResourceTests
{
    [Fact]
    public void ResourceBytesAndHashesEqualCommandLineReads()
    {
        using var estate = new HandoffEstate();
        var order = estate.Add("work-order", estate.OperatorId, """{"key":"R-1","kind":"ordinary","title":"Resource parity","intent":"Read docs/optional.dat when allocated.","scope":["reads"],"exitQuestion":"Exact bytes?","inputs":[],"deliverables":["result"]}""");
        byte[] original = [0, 255, 254, 13, 10, 65, 195, 169];
        Directory.CreateDirectory(Path.Combine(estate.Root, "docs"));
        File.WriteAllBytes(Path.Combine(estate.Root, "docs/optional.dat"), original);
        estate.Add("document-revision", estate.OperatorId, $$"""{"path":"docs/optional.dat","title":"Original","hash":"{{ContentHash.Sha256(original)}}","bytes":{{original.Length}},"mediaType":"application/octet-stream","revision":1,"summary":"Pinned original."}""");
        using EstateService api = EstateService.Open(estate.Root);
        api.CommunicationClock = new ClientClock(DateTimeOffset.Parse("2026-10-13T12:00:00Z", CultureInfo.InvariantCulture));
        CliHarness.Outcome Read(params string[] args)
        {
            CliHarness.Outcome outcome = CliHarness.RunWithApi(api, null, args);
            Assert.Equal(0, outcome.Code);
            return outcome;
        }
        JsonArray resources = Exchange(api, "resources/list")["resources"]!.AsArray();
        string Resource(string kind) => resources.First(r => r!["uri"]!.GetValue<string>().Contains("/" + kind + "/", StringComparison.Ordinal))!["uri"]!.GetValue<string>();
        string prefix = "vericairn://" + estate.Load().Manifest!.Estate;
        Check(api, Resource("brief"), Encoding.UTF8.GetBytes(Read("--estate", estate.Root, "work", "brief", "R-1", "--agent-context").Out));
        Check(api, Resource("brief") + "?maxBytes=500000", Encoding.UTF8.GetBytes(Read("--estate", estate.Root, "work", "brief", "R-1", "--max-bytes", "500000").Out));
        Check(api, prefix + "/record/" + order.Id, Encoding.UTF8.GetBytes(CliHarness.Ok("--estate", estate.Root, "records", "show", order.Id).Out));

        string documentUri = Resource("document");
        string pin = documentUri[(documentUri.LastIndexOf('/') + 1)..];
        string exported = Path.Combine(estate.Root, "original-export.dat");
        CliHarness.Ok("--estate", estate.Root, "context", "read", pin, "--original", "--out", exported);
        Assert.Equal(original, File.ReadAllBytes(exported));
        Check(api, documentUri, File.ReadAllBytes(exported));
        string json = CliHarness.Ok("--estate", estate.Root, "--json", "context", "prepare", "R-1").Out;
        ContextPackage package = JsonSerializer.Deserialize<ContextPackage>(json, ProtocolJson.Options)!;
        Check(api, Resource("context"), CanonicalJson.SerializePrettyUtf8(package.Payload));
        Check(api, Resource("context") + "?form=json", Encoding.UTF8.GetBytes(json));
        Check(api, Resource("context") + "?form=text", Encoding.UTF8.GetBytes(CliHarness.Ok("--estate", estate.Root, "context", "prepare", "R-1").Out));
        JsonNode context = Exchange(api, "resources/read", new() { ["uri"] = Resource("context") })["contents"]![0]!;
        Assert.Equal(package.Hash, context["_meta"]!["vericairn/hash"]!.GetValue<string>());
        Assert.Equal(package.Bytes, context["_meta"]!["vericairn/bytes"]!.GetValue<long>());
    }

    private static void Check(IEstateApi api, string uri, byte[] bytes)
    {
        JsonNode content = Assert.Single(Exchange(api, "resources/read", new() { ["uri"] = uri })["contents"]!.AsArray())!;
        Assert.Equal(bytes, Convert.FromBase64String(content["blob"]!.GetValue<string>()));
        Assert.Equal(ContentHash.Sha256(bytes), content["_meta"]!["vericairn/hash"]!.GetValue<string>());
        Assert.Equal(bytes.LongLength, content["_meta"]!["vericairn/bytes"]!.GetValue<long>());
    }

    private static JsonNode Exchange(IEstateApi api, string method, JsonObject? parameters = null)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters };
        using var input = new StringReader(message.ToJsonString() + "\n");
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        new McpServer(new ToolRegistry(api, null), "test").Run(input, output);
        JsonNode reply = JsonNode.Parse(output.ToString())!;
        Assert.Null(reply["error"]);
        return reply["result"]!;
    }
}
