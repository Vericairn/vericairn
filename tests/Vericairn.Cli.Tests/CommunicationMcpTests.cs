// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class CommunicationMcpTests
{
    [Fact]
    public void CommunicationToolIsReadOnlyAndReturnsDefaultsWithoutInventingARecipient()
    {
        string root = CliHarness.TempDirectory("communication-mcp");
        try
        {
            CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Communication reads.", "--operator-name", "Person", "--organization", "Example", "--commit", "0123456789abcdef0123456789abcdef01234567");
            using EstateService api = EstateService.Open(root);
            var registry = new ToolRegistry(api, null);
            Assert.False(registry.Visible().Single(t => t.Name == "communication_get").Writes);
            Assert.DoesNotContain(registry.All, t => t.Name is "communication_set" or "communication_delegate");
            int count = Directory.EnumerateFiles(Path.Combine(root, "records"), "*.json", SearchOption.AllDirectories).Count();
            ToolCallResult result = registry.Call("communication_get", new JsonObject());
            Assert.False(result.IsError);
            using JsonDocument profile = JsonDocument.Parse(result.Text);
            Assert.Equal("short", profile.RootElement.GetProperty("fields").GetProperty("status").GetProperty("value").GetString());
            Assert.Equal(count, Directory.EnumerateFiles(Path.Combine(root, "records"), "*.json", SearchOption.AllDirectories).Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
