// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Signing;

namespace Vericairn.Cli.Tests;

/// <summary>
/// MCP-03 through the command line: the operator turns network MCP on and names the origins it allows, the runtime's status shows
/// /mcp, and on the agent's machine 'vericairn mcp serve --connection' bridges a stdio client to the estate over its paired connection,
/// signing each request and each record with that machine's device. The local-network profile listens on 127.0.0.1 only.
/// </summary>
[Collection("cli")]
public class NetworkMcpCommandTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task TheOperatorTurnsMcpOnAndAnAgentElsewhereBridgesAStdioClientToIt()
    {
        string root = CliHarness.TempDirectory("mcp-net-estate");
        string home = CliHarness.TempDirectory("mcp-net-home");
        string scratch = CliHarness.TempDirectory("mcp-net-scratch");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
            new Vericairn.Records.Updates.ReleaseUpdateStore().Configure(false);
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Network MCP.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string operatorDevice = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator laptop", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "--json", "devices", "admit", operatorDevice, "--by", op, "--note", "The operator's own laptop.");
            string agent = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "agent.json", """{"kind":"agent","name":"Remote Agent","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;

            // Off by default; on and its origins by the operator's configuration, each kept for the runtime's next start.
            Assert.Contains("network MCP is off", CliHarness.Ok("--estate", root, "runtime", "configure").Out, StringComparison.Ordinal);
            Assert.Contains("on or off", CliHarness.Run("--estate", root, "runtime", "configure", "--mcp", "maybe").Err, StringComparison.Ordinal);
            Assert.Contains("is not an origin", CliHarness.Run("--estate", root, "runtime", "configure", "--mcp-origin", "https://agent.example/path").Err, StringComparison.Ordinal);
            JsonElement configured = CliHarness.Ok("--estate", root, "--json", "runtime", "configure", "--local-network", "127.0.0.1:0", "--mcp", "on", "--mcp-origin", "https://Agent.Example:443", "--mcp-origin", "http://127.0.0.1:8080").Json;
            Assert.True(configured.GetProperty("mcp").GetBoolean());
            Assert.Equal(["https://agent.example", "http://127.0.0.1:8080"], configured.GetProperty("allowedOrigins").EnumerateArray().Select(o => o.GetString()));
            Assert.Equal("127.0.0.1:0", configured.GetProperty("localNetwork").GetString());
            JsonElement cleared = CliHarness.Ok("--estate", root, "--json", "runtime", "configure", "--mcp-origin", "none").Json;
            Assert.Equal(0, cleared.GetProperty("allowedOrigins").GetArrayLength());
            Assert.True(cleared.GetProperty("mcp").GetBoolean());
            Assert.Equal("127.0.0.1:0", cleared.GetProperty("localNetwork").GetString());
            Assert.Contains("network MCP at /mcp is on", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);

            CliHarness.RequireDaemonTransport();
            await using DaemonHost host = await DaemonHost.StartAsync(root, options: new DaemonOptions { IdentityStore = new FileKeyStore(home) }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Contains("mcp  /mcp on loopback and the local network", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);
            using (var socket = new Vericairn.Sdk.VericairnClient(host.Endpoint))
            {
                using JsonDocument order = JsonDocument.Parse("""{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Network MCP.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[],"deliverables":["widget"]}""");
                Assert.True(socket.NewRecord(new NewRecordRequest("work-order", op, order.RootElement)).Ok);
            }

            using JsonDocument brief = JsonDocument.Parse("""{"outcome":"Widget","sources":[],"permittedChanges":["src/"],"prohibitedEffects":[],"workspace":["widget repository"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["tests"],"recovery":"Record a result."}""");
            string assignment;
            using (var socket = new Vericairn.Sdk.VericairnClient(host.Endpoint))
            {
                assignment = socket.Assign(new AssignRequest("W-1", agent, op, brief.RootElement)).Written!.Id;
            }

            // The agent's machine pairs, and bridges its stdio client to the estate.
            JsonElement issued = CliHarness.Ok("--estate", root, "--json", "devices", "pair", "--principal", agent, "--by", op).Json;
            JsonElement added = CliHarness.Ok("--json", "connections", "add", issued.GetProperty("address").GetString()!, "--code", issued.GetProperty("code").GetString()!, "--name", "Agent laptop", "--custody", "file").Json;
            string connection = added.GetProperty("id").GetString()!;
            string device = added.GetProperty("device").GetString()!;
            Assert.Contains("one or the other", CliHarness.Run("mcp", "serve", "--connection", connection, "--as", agent).Err, StringComparison.Ordinal);
            Assert.Contains("No connection", CliHarness.Run("mcp", "serve", "--connection", "Nowhere").Err, StringComparison.Ordinal);

            string input = string.Join('\n',
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"harness","version":"1"}}}""",
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
                """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"estate_status","arguments":{}}}""",
                $$$$"""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"work_attempt","arguments":{"assignment":"{{{{assignment}}}}","workspace":"widget repository","note":"Build it."}}}""",
                """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"policy_declare","arguments":{}}}""",
                "not json") + "\n";
            (string output, string error, int exit) = await BridgeAsync(home, input, "mcp", "serve", "--connection", "Example");
            Assert.True(exit == 0, error);
            Assert.Contains("bridging to Example", error, StringComparison.Ordinal);
            Dictionary<string, JsonObject> answers = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonNode.Parse(l)!.AsObject())
                .Where(a => a["id"] is not null).ToDictionary(a => a["id"]!.ToJsonString());
            Assert.Equal("2025-06-18", answers["1"]["result"]!["protocolVersion"]!.GetValue<string>());
            Assert.DoesNotContain(answers["2"]["result"]!["tools"]!.AsArray(), t => t!["name"]!.GetValue<string>() == "policy_declare");
            Assert.False(answers["3"]["result"]!["isError"]!.GetValue<bool>());
            Assert.False(answers["4"]["result"]!["isError"]!.GetValue<bool>(), answers["4"].ToJsonString());
            string attempt = JsonNode.Parse(answers["4"]["result"]!["content"]![0]!["text"]!.GetValue<string>())!["written"]!["id"]!.GetValue<string>();
            Assert.Equal(-32602, answers["5"]["error"]!["code"]!.GetValue<int>());
            Assert.Contains(output.Split('\n'), l => l.Contains("-32700", StringComparison.Ordinal));

            // The attempt carries the agent's paired device's signature, and the runtime kept which device asked for it.
            JsonElement record = CliHarness.Ok("--estate", root, "records", "show", attempt).Json;
            Assert.Equal(device, Assert.Single(record.GetProperty("signatures").EnumerateArray()).GetProperty("device").GetString());
            JsonElement wrote = Assert.Single(CliHarness.Ok("--estate", root, "--json", "devices", "writes", device).Json.EnumerateArray());
            Assert.Equal("POST /v1/work/attempt", wrote.GetProperty("operation").GetString());
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(root, home, scratch);
        }
    }

    /// <summary>Runs the built command line as a stdio client would start it, with this machine's home, and returns what it wrote.</summary>
    private static async Task<(string Output, string Error, int Exit)> BridgeAsync(string home, string input, params string[] arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo(CliHarness.BuiltExecutable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        info.Environment[VericairnHome.EnvironmentVariable] = home;
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), TestContext.Current.CancellationToken);
            process.StandardInput.Close();
            await Task.WhenAll(process.WaitForExitAsync(TestContext.Current.CancellationToken), output, error)
                .WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        return (await output, await error, process.ExitCode);
    }
}
