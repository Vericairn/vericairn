// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class McpClientsCommandTests
{
    [Fact]
    public void TheListingDistinguishesClientsAndReportsLocalAuthorizationExpiry()
    {
        using var estate = new TestEstate(3);
        string administrator = CliHarness.Ok("--estate", estate.Root, "--json", "devices", "create",
            "--principal", estate.OperatorId, "--name", "Administrator fixture", "--custody", "file").Id;
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", administrator,
            "--by", estate.OperatorId, "--note", "Fixture authority.");
        using var service = EstateService.Open(estate.Root);
        (PairingAuthority? authority, string? refusal) = service.McpTokenIssuer(estate.BuilderId, estate.OperatorId);
        Assert.True(authority is not null, refusal);
        var keys = new FileKeyStore(VericairnHome.Path());
        (McpTokenDevice? reader, Problem? readProblem) = service.CreateMcpTokenDevice(estate.BuilderId, authority, keys);
        (McpTokenDevice? editor, Problem? editProblem) = service.CreateMcpTokenDevice(estate.BuilderId, authority, keys);
        Assert.True(reader is not null, readProblem?.Message);
        Assert.True(editor is not null, editProblem?.Message);
        service.Dispose();

        string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(estate.Root))!;
        Directory.CreateDirectory(directory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var state = new
        {
            Format = "vericairn.mcp-token-families/1",
            Families = new Dictionary<string, object>
            {
                ["reader"] = Entry(reader, "Library client", "read", now.AddDays(1)),
                ["editor"] = Entry(editor, "Archive client", "contribute", now.AddDays(-1)),
            },
        };
        File.WriteAllBytes(Path.Combine(directory, "mcp-token-families.json"), JsonSerializer.SerializeToUtf8Bytes(state, ProtocolJson.Options));

        string output = CliHarness.Ok("--estate", estate.Root, "mcp", "clients").Out;
        string read = Assert.Single(output.Split('\n'), line => line.Contains(reader.Device, StringComparison.Ordinal));
        string edit = Assert.Single(output.Split('\n'), line => line.Contains(editor.Device, StringComparison.Ordinal));
        Assert.Contains("authorized  Library client  read  expires", read, StringComparison.Ordinal);
        Assert.Contains("expired  Archive client  contribute  expires", edit, StringComparison.Ordinal);

        JsonElement listing = CliHarness.Ok("--estate", estate.Root, "--json", "mcp", "clients").Json;
        JsonElement row = Assert.Single(listing.EnumerateArray(), item => item.GetProperty("id").GetString() == reader.Device);
        Assert.Equal("admitted", row.GetProperty("admission").GetString());
        Assert.Equal("Library client", row.GetProperty("authorization").GetProperty("clientName").GetString());
    }

    private static object Entry(McpTokenDevice device, string name, string scope, DateTimeOffset expiresAt) => new
    {
        Grant = new { device.Device, Client = "https://client.example/" + scope, ClientName = name, Scope = scope, ExpiresAt = expiresAt },
    };
}
