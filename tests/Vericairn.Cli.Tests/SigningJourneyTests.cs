// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Signing;

namespace Vericairn.Cli.Tests;

/// <summary>Signed records, devices and admission, headless through the CLI: a clone without an admitted key is read-only.</summary>
[Collection("cli")]
public class SigningJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void AdmittedDevicesSignAndACloneWithoutKeysIsReadOnly()
    {
        string a = CliHarness.TempDirectory("sign-a");
        string homeA = CliHarness.TempDirectory("sign-home-a");
        string homeB = CliHarness.TempDirectory("sign-home-b");
        string scratch = CliHarness.TempDirectory("sign-scratch");
        string b = string.Empty;
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeA);
            CliHarness.Outcome init = CliHarness.Ok("--estate", a, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Signing journey.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
            string op = init.Json.GetProperty("operator").GetString()!;
            string estateId = init.Json.GetProperty("estate").GetString()!;

            // The operator creates a device on this machine and admits it (root self-admission); then admits the builder's device.
            string operatorDevice = CliHarness.Ok("--estate", a, "--json", "devices", "create", "--principal", op, "--name", "Operator laptop", "--custody", "file").Id;
            CliHarness.Ok("--estate", a, "--json", "devices", "admit", operatorDevice, "--by", op, "--note", "The operator's own laptop.");
            string builderPath = CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""");
            string builder = CliHarness.Ok("--estate", a, "--json", "records", "new", "principal", "--by", op, "--body", builderPath).Id;
            string builderDevice = CliHarness.Ok("--estate", a, "--json", "devices", "create", "--principal", builder, "--name", "Build machine", "--custody", "file", "--profile", "embedded", "local-socket").Id;
            CliHarness.Ok("--estate", a, "--json", "devices", "admit", builderDevice, "--by", op, "--note", "The builder's machine.");
            string listing = CliHarness.Ok("--estate", a, "devices", "list").Out;
            Assert.Contains("admitted  key held on this machine", listing, StringComparison.Ordinal);

            // The operator switches the policy to required; the charter revision is signed by the operator's device.
            string charterPath = CliHarness.WriteJson(scratch, "charter.json", $$$"""{"name":"Example","slug":"example","purpose":"Signing journey.","operator":"{{{op}}}","recordFormat":1,"policies":{"signatures":"required","releaseAcceptance":["operator"]},"organization":{"name":"Example"}}""");
            CliHarness.Ok("--estate", a, "--json", "records", "new", "estate", "--by", op, "--body", charterPath, "--supersedes", estateId);

            // The builder authors a record: signed automatically by its admitted device.
            string orderPath = CliHarness.WriteJson(scratch, "order.json", """{"key":"W-1","kind":"ordinary","title":"Signed work","intent":"Prove signing.","scope":["x"],"exitQuestion":"Signed?","inputs":[],"deliverables":["y"]}""");
            CliHarness.Outcome order = CliHarness.Ok("--estate", a, "--json", "records", "new", "work-order", "--by", builder, "--body", orderPath);
            string orderFile = File.ReadAllText(Path.Combine(a, order.Json.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("\"signatures\"", orderFile, StringComparison.Ordinal);
            Assert.Contains(builderDevice, orderFile, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", a, "estate", "check").Code);

            // A fresh clone on a machine without the keys verifies everything but cannot author.
            b = CliHarness.Clone(a, "sign-b");
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeB);
            Assert.Equal(0, CliHarness.Run("--estate", b, "estate", "check").Code);
            CliHarness.Outcome refused = CliHarness.Run("--estate", b, "--json", "records", "new", "work-order", "--by", builder, "--body", CliHarness.WriteJson(scratch, "order2.json", """{"key":"W-2","kind":"ordinary","title":"Unsigned","intent":"Should be refused.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}"""));
            Assert.Equal(2, refused.Code);
            Assert.Contains("read-only", refused.Err, StringComparison.Ordinal);
            Assert.DoesNotContain(Directory.EnumerateFiles(Path.Combine(b, "records", "work-order"), "*.json", SearchOption.AllDirectories), f => !f.Contains(order.Id, StringComparison.Ordinal));

            // Revocation from the operator's machine: the builder's later records are refused and earlier ones stay valid.
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeA);
            Thread.Sleep(1100); // revocation applies from its own second; the earlier records must sit in an earlier one
            CliHarness.Ok("--estate", a, "--json", "devices", "revoke", builderDevice, "--by", op, "--reason", "Machine retired.");
            Assert.Contains("read-only", CliHarness.Run("--estate", a, "--json", "records", "new", "work-order", "--by", builder, "--body", orderPath).Err, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", a, "estate", "check").Code);
            JsonElement devices = CliHarness.Ok("--estate", a, "--json", "devices", "list").Json;
            Assert.Equal("revoked", devices.EnumerateArray().Single(d => d.GetProperty("id").GetString() == builderDevice).GetProperty("admission").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(a, b, homeA, homeB, scratch);
        }
    }
}
