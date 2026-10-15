// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Records.Signing;

namespace Vericairn.Cli.Tests;

/// <summary>
/// ID-02's exit question through the command line: a second device pairs over the local network with mutual authentication, acts
/// within its admitted scope, keeps working across a certificate rotation, and is refused at once when revoked. The local-network
/// profile listens on 127.0.0.1 only.
/// </summary>
[Collection("cli")]
public class PairingJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task ASecondDevicePairsActsWithinItsScopeAndIsRefusedOnceRevoked()
    {
        string root = CliHarness.TempDirectory("pair-estate");
        string home = CliHarness.TempDirectory("pair-home");
        string scratch = CliHarness.TempDirectory("pair-scratch");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Pairing journey.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string operatorDevice = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator laptop", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "--json", "devices", "admit", operatorDevice, "--by", op, "--note", "The operator's own laptop.");
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;

            // Nothing listens beyond the local socket and loopback until the operator configures an address, and nothing pairs without a runtime.
            Assert.Contains("the local-network profile is off", CliHarness.Ok("--estate", root, "runtime", "configure").Out, StringComparison.Ordinal);
            Assert.Contains("needs one", CliHarness.Run("--estate", root, "devices", "pair", "--principal", builder, "--by", op).Err, StringComparison.Ordinal);
            Assert.Contains("no port", CliHarness.Run("--estate", root, "runtime", "configure", "--local-network", "127.0.0.1").Err, StringComparison.Ordinal);
            Assert.Equal("127.0.0.1:0", CliHarness.Ok("--estate", root, "--json", "runtime", "configure", "--local-network", "127.0.0.1:0").Json.GetProperty("localNetwork").GetString());
            Assert.Contains("configured at 127.0.0.1:0", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);

            await using DaemonHost host = await DaemonHost.StartAsync(root, options: new DaemonOptions { IdentityStore = new FileKeyStore(home) }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Contains("local network  https://127.0.0.1:", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);

            // The operator issues a code; on the second device, a mistyped code is refused and the right one pairs it.
            CliHarness.Outcome shown = CliHarness.Ok("--estate", root, "devices", "pair", "--principal", builder, "--by", op);
            Assert.Contains("pairing code  ", shown.Out, StringComparison.Ordinal);
            Assert.Contains("on the new device: vericairn connections add 127.0.0.1:", shown.Out, StringComparison.Ordinal);
            JsonElement issued = CliHarness.Ok("--estate", root, "--json", "devices", "pair", "--principal", builder, "--by", op).Json;
            string code = issued.GetProperty("code").GetString()!;
            string address = issued.GetProperty("address").GetString()!;
            string mistyped = code[..^1] + (code[^1] == '0' ? '1' : '0');
            Assert.Contains("does not know this pairing code", CliHarness.Run("connections", "add", address, "--code", mistyped, "--name", "Build laptop", "--custody", "file").Err, StringComparison.Ordinal);
            JsonElement added = CliHarness.Ok("--json", "connections", "add", address, "--code", code, "--name", "Build laptop", "--custody", "file").Json;
            Assert.Equal("current", added.GetProperty("state").GetString());
            Assert.Equal(issued.GetProperty("identity").GetString(), added.GetProperty("identity").GetString());
            string connection = added.GetProperty("id").GetString()!;
            string device = added.GetProperty("device").GetString()!;
            Assert.Contains("used already", CliHarness.Run("connections", "add", address, "--code", code, "--name", "Another laptop", "--custody", "file").Err, StringComparison.Ordinal);

            // The runtime recorded the device with its permitted profile and admitted it; the second device lists its connection as current.
            JsonElement row = CliHarness.Ok("--estate", root, "--json", "devices", "list").Json.EnumerateArray().Single(d => d.GetProperty("id").GetString() == device);
            Assert.Equal("admitted", row.GetProperty("admission").GetString());
            Assert.Equal(["local-network"], row.GetProperty("profiles").EnumerateArray().Select(p => p.GetString()));
            Assert.Contains("profiles local-network", CliHarness.Ok("--estate", root, "devices", "list").Out, StringComparison.Ordinal);
            Assert.Equal("current", Listed(connection).GetProperty("state").GetString());
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);

            // A rotation keeps the device working without pairing again.
            Assert.Contains("paired devices keep working", CliHarness.Ok("--estate", root, "runtime", "rotate").Out, StringComparison.Ordinal);
            Assert.Equal("current", Listed(connection).GetProperty("state").GetString());

            // Revoked on the runtime's machine: refused at once.
            CliHarness.Ok("--estate", root, "--json", "devices", "revoke", device, "--by", op, "--reason", "The laptop was lost.");
            JsonElement denied = Listed(connection);
            Assert.Equal("denied", denied.GetProperty("state").GetString());
            Assert.Contains("revoked", denied.GetProperty("message").GetString(), StringComparison.Ordinal);

            // With the runtime stopped, what was last current is cached; the certificate can still be replaced, for the next start.
            await host.StopAsync();
            JsonElement cached = Listed(connection);
            Assert.Equal("cached", cached.GetProperty("state").GetString());
            Assert.Contains("served from the next start", CliHarness.Ok("--estate", root, "runtime", "rotate").Out, StringComparison.Ordinal);

            // Removing the connection forgets the runtime here and revokes nothing on its own.
            Assert.Contains("disconnection is not revocation", CliHarness.Ok("connections", "remove", connection).Out, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Ok("--json", "connections", "list").Json.GetArrayLength());
            Assert.Equal(2, CliHarness.Run("connections", "remove", connection).Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(root, home, scratch);
        }
    }

    private static JsonElement Listed(string connection) =>
        CliHarness.Ok("--json", "connections", "list").Json.EnumerateArray().Single(c => c.GetProperty("id").GetString() == connection);
}
