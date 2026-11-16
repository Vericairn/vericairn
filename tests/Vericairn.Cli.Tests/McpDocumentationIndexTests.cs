// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

/// <summary>
/// DOCS-38: a client's agent finds the guides of the release it talks to from one machine-readable entry point. The MCP server offers
/// the release's documentation index as a resource in llms.txt's form, the same for every estate, reading nothing from it.
/// </summary>
[Collection("cli")]
public class McpDocumentationIndexTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void TheServerListsAndReadsItsReleasesDocumentationIndex()
    {
        string root = CliHarness.TempDirectory("mcp-docs-index");
        try
        {
            CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "MCP documentation.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
            using EstateService api = EstateService.Open(root);
            JsonObject[] replies = Drive(api, "0.9.4+" + Commit,
                """{"jsonrpc":"2.0","id":1,"method":"resources/list"}""",
                """{"jsonrpc":"2.0","id":2,"method":"resources/read","params":{"uri":"vericairn://docs/llms.txt"}}""",
                """{"jsonrpc":"2.0","id":3,"method":"resources/read","params":{"uri":"vericairn://docs/llms-full.txt"}}""");
            JsonNode listed = Assert.Single(replies[0]["result"]!["resources"]!.AsArray(), r => r!["uri"]!.GetValue<string>() == DocumentationIndex.Uri)!;
            Assert.Equal("text/markdown", listed["mimeType"]!.GetValue<string>());
            Assert.Contains("Untrusted content", listed["description"]!.GetValue<string>(), StringComparison.Ordinal);

            JsonNode content = replies[1]["result"]!["contents"]![0]!;
            byte[] bytes = Convert.FromBase64String(content["blob"]!.GetValue<string>());
            string index = Encoding.UTF8.GetString(bytes);
            Assert.Equal(DocumentationIndex.Render("0.9.4+" + Commit), index);
            Assert.StartsWith("# Vericairn 0.9.4\n\n> ", index, StringComparison.Ordinal);
            Assert.Contains("(https://raw.githubusercontent.com/Vericairn/vericairn/v0.9.4/docs/releases/0.9.4/llms.txt)", index, StringComparison.Ordinal);
            Assert.Contains("(https://raw.githubusercontent.com/Vericairn/vericairn/v0.9.4/docs/releases/0.9.4/llms-full.txt)", index, StringComparison.Ordinal);
            Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)), content["_meta"]!["vericairn/hash"]!.GetValue<string>());
            Assert.Equal(bytes.LongLength, content["_meta"]!["vericairn/bytes"]!.GetValue<long>());
            // Only the index is offered; any other address under it is refused as an invalid resource.
            Assert.Equal(-32602, replies[2]["error"]!["code"]!.GetValue<int>());
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }

    [Theory]
    [InlineData("0.9.5-dev+0123456789abcdef0123456789abcdef01234567")]
    [InlineData("test")]
    [InlineData("unknown")]
    public void ADevelopmentBuildPointsAtTheRepositorysRootIndex(string version)
    {
        string index = DocumentationIndex.Render(version);
        Assert.Contains("(https://raw.githubusercontent.com/Vericairn/vericairn/main/llms.txt)", index, StringComparison.Ordinal);
        Assert.DoesNotContain("/docs/releases/0.9.5/", index, StringComparison.Ordinal);
        Assert.Contains("(https://github.com/Vericairn/vericairn/blob/main/docs/releases/README.md)", index, StringComparison.Ordinal);
    }

    private static JsonObject[] Drive(EstateService api, string version, params string[] messages)
    {
        var input = new StringReader(string.Join("\n", messages) + "\n");
        var output = new StringWriter { NewLine = "\n" };
        new McpServer(new ToolRegistry(api, null), version).Run(input, output);
        return output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => (JsonObject)JsonNode.Parse(l)!).ToArray();
    }
}
