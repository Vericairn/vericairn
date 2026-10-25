// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.IO.Compression;
using System.Text.Json;
using Vericairn.Records.Signing;

namespace Vericairn.Cli.Tests;

/// <summary>Portability: an export restores to the same status on another machine, carries no authority, and the local-only profile migrates by export.</summary>
[Collection("cli")]
public class PortabilityJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Brief = """{"outcome":"A widget.","sources":[],"permittedChanges":["widget"],"prohibitedEffects":[],"workspace":["example"],"limits":[],"expectedOutputs":["widget.txt"],"reviewCriteria":["hash verified"],"recovery":"Read the last result."}""";

    [Fact]
    public void J17_RestoreAnEstateOnAnotherMachineAndContinueSafely()
    {
        string a = CliHarness.TempDirectory("j17-a");
        string b = CliHarness.TempDirectory("j17-b");
        string homeA = CliHarness.TempDirectory("j17-home-a");
        string homeB = CliHarness.TempDirectory("j17-home-b");
        string scratch = CliHarness.TempDirectory("j17-scratch");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeA);
            string op = Init(a);
            string device = CliHarness.Ok("--estate", a, "--json", "devices", "create", "--principal", op, "--name", "Operator laptop", "--custody", "file").Id;
            CliHarness.Ok("--estate", a, "--json", "devices", "admit", device, "--by", op, "--note", "Root device.");
            string builder = Admit(a, op, scratch);
            CliHarness.Ok("--estate", a, "--json", "records", "new", "work-order", "--by", builder, "--body", CliHarness.WriteJson(scratch, "order.json", """{"key":"W-1","kind":"ordinary","title":"Widget","intent":"Portability.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["widget.txt"]}"""));
            string assignment = CliHarness.Ok("--estate", a, "--json", "work", "assign", "W-1", "--contributor", builder, "--by", op, "--brief", CliHarness.WriteJson(scratch, "brief.json", Brief)).Id;
            string attempt = CliHarness.Ok("--estate", a, "--json", "work", "attempt", assignment, "--by", builder, "--workspace", "example", "--note", "Build.").Id;
            string widget = CliHarness.WriteJson(scratch, "widget.txt", "widget bytes\n");
            CliHarness.Outcome put = CliHarness.Ok("--estate", a, "--json", "objects", "put", widget);
            string hash = put.Json.GetProperty("hash").GetString()!;
            CliHarness.Ok("--estate", a, "--json", "work", "result", attempt, "--by", builder, "--body", CliHarness.WriteJson(scratch, "result.json", $$"""{"outcome":"partial","summary":"Half done before the machine was lost.","outputs":[{"name":"widget.txt","hash":"{{hash}}","location":"objects/{{hash["sha256:".Length..]}}"}],"evidence":[],"checks":[],"limitations":["Tests not run."],"unresolvedEffects":["A publication may have been dispatched; reconcile before retry."],"nextStep":"Reconcile, then finish."}"""));

            // Git's metadata travels with the estate's documents: a .mailmap beside .gitattributes.
            File.WriteAllText(Path.Combine(a, ".mailmap"), "Operator <operator@example.invalid> <old@example.invalid>\n");
            string statusA = CliHarness.Ok("--estate", a, "--json", "status").Out;

            // Export, then restore on "another machine" (a different per-user state directory).
            string zip = Path.Combine(scratch, "estate-export.zip");
            CliHarness.Outcome export = CliHarness.Ok("--estate", a, "--json", "estate", "export", "--out", zip, "--by", op);
            Assert.Equal(1, export.Json.GetProperty("Objects").GetInt32());
            Assert.True(export.Json.GetProperty("Records").GetInt32() >= 8);
            Assert.NotEqual(0, CliHarness.Run("--estate", a, "--json", "estate", "export", "--out", zip).Code);

            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeB);
            CliHarness.Outcome restore = CliHarness.Ok("--json", "estate", "restore", "--from", zip, "--to", b);
            Assert.Equal(0, restore.Json.GetProperty("errors").GetInt32());
            Assert.Equal(0, CliHarness.Run("--estate", b, "estate", "check").Code);
            Assert.Equal(File.ReadAllText(Path.Combine(a, ".mailmap")), File.ReadAllText(Path.Combine(b, ".mailmap")));
            Assert.Equal(statusA, CliHarness.Ok("--estate", b, "--json", "status").Out);
            Assert.Equal(0, CliHarness.Run("--estate", b, "objects", "verify").Code);
            string inspect = CliHarness.Ok("--estate", b, "recover", "inspect").Out;
            Assert.Contains("key not held here", inspect, StringComparison.Ordinal);
            Assert.Contains("A publication may have been dispatched", inspect, StringComparison.Ordinal);
            Assert.Contains("partial, awaiting review", inspect, StringComparison.Ordinal);
            string compare = CliHarness.Ok("--estate", b, "recover", "compare", "--with", a).Out;
            Assert.Contains("Only in this copy: 0", compare, StringComparison.Ordinal);
            Assert.Contains("Only in the other copy: 0", compare, StringComparison.Ordinal);
            Assert.DoesNotContain(Directory.EnumerateFiles(b, "*", SearchOption.AllDirectories), f => f.EndsWith(".p8", StringComparison.Ordinal) || f.EndsWith(".db", StringComparison.Ordinal));

            // The restored copy verifies the operator's signature but cannot sign as the operator: the key stayed on machine A. An
            // admission authored here is refused whatever the policy, because record format 2 signs every admission, and the refusal
            // names the ways on; an ordinary record authored here under the optional policy is unsigned, and under a required policy
            // it would be refused.
            CliHarness.Outcome readmit = CliHarness.Run("--estate", b, "--json", "devices", "admit", device, "--by", op, "--note", "Re-admitted from the restored copy.");
            Assert.NotEqual(0, readmit.Code);
            Assert.Contains("--recovery", readmit.Out + readmit.Err, StringComparison.Ordinal);
            CliHarness.Outcome orderOnB = CliHarness.Ok("--estate", b, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "order-b.json", """{"key":"W-2","kind":"ordinary","title":"After the restore","intent":"Portability.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["x"]}"""));
            Assert.DoesNotContain("\"signatures\"", File.ReadAllText(Path.Combine(b, orderOnB.Json.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))), StringComparison.Ordinal);
            Assert.Contains("unsigned only", CliHarness.Ok("--estate", b, "recover", "inspect").Out, StringComparison.Ordinal);

            // Corrupting an object is detected by verification, never silently served.
            string objectPath = Path.Combine(b, "objects", hash["sha256:".Length..]);
            File.WriteAllText(objectPath, "tampered\n");
            Assert.NotEqual(0, CliHarness.Run("--estate", b, "objects", "verify").Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(a, b, homeA, homeB, scratch);
        }
    }

    [Fact]
    public void LocalOnlyEstateWorksHeadlessAndMigratesByExport()
    {
        string c = CliHarness.TempDirectory("local-c");
        string d = CliHarness.TempDirectory("local-d");
        string scratch = CliHarness.TempDirectory("local-scratch");
        try
        {
            CliHarness.Outcome init = CliHarness.Ok("--estate", c, "--json", "estate", "init", "--profile", "local-only", "--name", "Local", "--slug", "local", "--purpose", "Local-only.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
            string op = init.Json.GetProperty("operator").GetString()!;
            string builder = Admit(c, op, scratch);
            CliHarness.Ok("--estate", c, "--json", "records", "new", "work-order", "--by", builder, "--body", CliHarness.WriteJson(scratch, "order.json", """{"key":"L-1","kind":"ordinary","title":"Local work","intent":"Local-only.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}"""));
            Assert.False(Directory.Exists(Path.Combine(c, "records")));
            Assert.Equal([".gitattributes", "bootstrap", "bootstrap.ps1", "estate"], Directory.EnumerateFileSystemEntries(c).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
            EstateBootstrapTests.AssertInstalled(c);
            string statusC = CliHarness.Ok("--estate", c, "--json", "status").Out;
            Assert.Contains("\"L-1\"", statusC, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", c, "estate", "check").Code);

            string zip = Path.Combine(scratch, "local-export.zip");
            CliHarness.Ok("--estate", c, "--json", "estate", "export", "--out", zip);
            CliHarness.Ok("--json", "estate", "restore", "--from", zip, "--to", d);
            Assert.True(Directory.Exists(Path.Combine(d, "records")));
            Assert.Equal(0, CliHarness.Run("--estate", d, "estate", "check").Code);
            string statusD = CliHarness.Ok("--estate", d, "--json", "status").Out;
            // Restore deliberately changes the manifest's storage profile. The policy basis pins
            // that manifest, so only its digest changes; records, inventories and findings stay exact.
            using JsonDocument source = JsonDocument.Parse(statusC), restored = JsonDocument.Parse(statusD);
            JsonElement sourceBasis = source.RootElement.GetProperty("policies").GetProperty("basis");
            JsonElement restoredBasis = restored.RootElement.GetProperty("policies").GetProperty("basis");
            Assert.Equal(sourceBasis.GetProperty("recordDigest").GetString(), restoredBasis.GetProperty("recordDigest").GetString());
            string sourceDigest = sourceBasis.GetProperty("digest").GetString()!, restoredDigest = restoredBasis.GetProperty("digest").GetString()!;
            Assert.NotEqual(sourceDigest, restoredDigest);
            Assert.Equal(statusC.Replace(sourceDigest, restoredDigest, StringComparison.Ordinal), statusD);
        }
        finally
        {
            CliHarness.Delete(c, d, scratch);
        }
    }

    [Fact]
    public void DuplicateMemberIsReportedAsARejectedExportWithoutRestoringAnything()
    {
        string root = CliHarness.TempDirectory("duplicate-export");
        string scratch = CliHarness.TempDirectory("duplicate-export-scratch");
        try
        {
            Init(root);
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "docs", "probe.md"), "verified bytes");
            string archive = Path.Combine(scratch, "estate.zip");
            CliHarness.Ok("--estate", root, "--json", "estate", "export", "--out", archive);
            using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Update))
            using (var writer = new StreamWriter(zip.CreateEntry("docs/probe.md").Open()))
            {
                writer.Write("unverified replacement");
            }

            string destination = Path.Combine(scratch, "restored");
            CliHarness.Outcome result = CliHarness.Run("--json", "estate", "restore", "--from", archive, "--to", destination);
            Assert.NotEqual(0, result.Code);
            Assert.Contains("export.entry-duplicate", result.Out, StringComparison.Ordinal);
            Assert.Contains("docs/probe.md", result.Out, StringComparison.Ordinal);
            Assert.Contains("Nothing restored", result.Err, StringComparison.Ordinal);
            Assert.False(Directory.Exists(destination));
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    private static string Init(string root) =>
        CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Portability.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;

    private static string Admit(string root, string op, string scratch)
    {
        string path = CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""");
        return CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", path).Id;
    }
}
