// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using System.Net.Http.Json;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class PromptMcpTests
{
    [Fact]
    public async Task StandardPromptsAreRoleFilteredAndGetRecordsBeforeReturningText()
    {
        using var writer = new PromptWriter();
        PromptRow row = writer.Add();
        JsonObject restricted = PromptRecordsTests.Definition("operator-only"); restricted["roles"] = new JsonArray("operator");
        PromptLibraryTests.Ok(writer.Api.PromptSave(new(writer.Operator, PolicyFixture.Element(restricted))));
        var registry = new ToolRegistry(writer.Api, writer.Builder);
        int before = writer.Model.Records.Count;
        JsonObject listing = await registry.PromptsListAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(listing["prompts"]!.AsArray(), p => p!["name"]!.GetValue<string>() == "estate:operator-only");
        JsonNode offered = Assert.Single(listing["prompts"]!.AsArray(), p => p!["name"]!.GetValue<string>() == row.Identity)!;
        Assert.Equal("Work/Review", offered["_meta"]!["vericairn/category"]!.GetValue<string>());
        Assert.Equal(row.Hash, offered["_meta"]!["vericairn/hash"]!.GetValue<string>());
        Assert.Contains(offered["arguments"]!.AsArray(), a => a!["name"]!.GetValue<string>() == "topic" && a["required"]!.GetValue<bool>());
        Assert.Equal(before, writer.Model.Records.Count);

        AssignRequest assignment = writer.Assignment();
        var op = new ToolRegistry(writer.Api, writer.Operator);
        var arguments = new JsonObject { ["topic"] = "the assigned evidence", ["_expected"] = row.Hash, ["_favor"] = "true",
            ["_target"] = new JsonObject { ["assignment"] = JsonSerializer.SerializeToNode(assignment, ProtocolJson.Options) }.ToJsonString() };
        JsonObject result = await op.PromptGetAsync(row.Identity, arguments, TestContext.Current.CancellationToken);
        Assert.Equal("user", result["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Contains("the assigned evidence", result["messages"]![0]!["content"]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.True(writer.Model.ById.ContainsKey(result["_meta"]!["vericairn/record"]!.GetValue<string>()));
        Assert.Equal(row.Identity, Assert.Single(writer.Api.Prompts(new(writer.Operator, Favorites: true)).Prompts).Identity);
        PromptLibraryTests.Ok(writer.Api.PromptPreference(new(writer.Operator, row.Identity, Disabled: true)));
        int count = writer.Model.Records.Count;
        await Assert.ThrowsAsync<ArgumentException>(() => op.PromptGetAsync(row.Identity, arguments, TestContext.Current.CancellationToken));
        Assert.Equal(count, writer.Model.Records.Count);
    }

    [Fact]
    public async Task ReadOnlyGetAndForeignAuthorAreRefusedWithoutReturningMessages()
    {
        using var writer = new PromptWriter();
        PromptRow row = writer.Add();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ToolRegistry(writer.Api, null).PromptGetAsync(row.Identity, null, TestContext.Current.CancellationToken));
        var registry = new ToolRegistry(writer.Api, writer.Builder);
        var body = new JsonObject { ["by"] = writer.Operator, ["prompt"] = row.Identity, ["favorite"] = true };
        int before = writer.Model.Records.Count;
        ToolCallResult result = await registry.CallAsync("prompt_preference", new JsonObject { ["body"] = body }, TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Equal(before, writer.Model.Records.Count);
    }

    [Fact]
    public void ServerAdvertisesAndServesStandardPromptsOverItsExistingTransport()
    {
        using var writer = new PromptWriter();
        writer.Add();
        using var input = new StringReader("""
            {"jsonrpc":"2.0","id":1,"method":"initialize"}
            {"jsonrpc":"2.0","id":2,"method":"prompts/list"}
            """);
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        new McpServer(new ToolRegistry(writer.Api, writer.Operator), "test").Run(input, output);
        JsonObject[] replies = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => JsonNode.Parse(s)!.AsObject()).ToArray();
        Assert.NotNull(replies[0]["result"]!["capabilities"]!["prompts"]);
        Assert.Contains(replies[1]["result"]!["prompts"]!.AsArray(), p => p!["name"]!.GetValue<string>() == "estate:review");
    }

    [Fact]
    public async Task AnIncompleteResponseAfterStorageKeepsTheReceiptInTheMcpError()
    {
        using var writer = new PromptWriter();
        PromptRow row = writer.Add();
        AssignRequest assignment = writer.Assignment();
        using var handler = new IncompleteUseHandler(writer);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid") };
        using var client = new VericairnClient(http);
        var registry = new ToolRegistry(client, writer.Operator);
        var arguments = new JsonObject { ["topic"] = "the assigned evidence", ["_expected"] = row.Hash,
            ["_target"] = new JsonObject { ["assignment"] = JsonSerializer.SerializeToNode(assignment, ProtocolJson.Options) }.ToJsonString() };
        int before = writer.Model.Records.Count;
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => registry.PromptGetAsync(row.Identity, arguments, TestContext.Current.CancellationToken));
        Assert.NotNull(handler.Stored);
        Assert.Contains(handler.Stored.Id, error.Message, StringComparison.Ordinal);
        Assert.Contains(handler.Stored.Hash, error.Message, StringComparison.Ordinal);
        Assert.Equal(before + 1, writer.Model.Records.Count);
        Assert.Equal(handler.Stored.Hash, writer.Model.ById[handler.Stored.Id].ComputedHash);
    }

    // A real signed use followed by an incomplete transport response; no host or sockets.
    private sealed class IncompleteUseHandler(PromptWriter writer) : HttpMessageHandler
    {
        internal RecordWritten? Stored { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object response;
            if (request.RequestUri!.AbsolutePath == "/v1/devices") response = writer.Api.Devices();
            else if (request.RequestUri.AbsolutePath == "/v1/prompts") response = writer.Api.Prompts(new(writer.Operator));
            else
            {
                Assert.Equal("/v1/prompts/use", request.RequestUri.AbsolutePath);
                PromptUseRequest use = (await request.Content!.ReadFromJsonAsync<PromptUseRequest>(ProtocolJson.Options, cancellationToken))!;
                PromptUseResponse used = writer.Api.PromptUse(use);
                Assert.True(used.Ok, used.Problem?.Message);
                Stored = used.Written;
                response = used with { Ok = false, Text = null, Problem = new("reconcile-required", "Companion publication needs recovery.", []) };
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(response, response.GetType(), options: ProtocolJson.Options) };
        }
    }
}
