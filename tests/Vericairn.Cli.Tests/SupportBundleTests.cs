// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>
/// CODE-11: the support bundle holds what support needs, shows everything it holds, writes only the file named, and carries no
/// secret, machine path or record body.
/// </summary>
[Collection("cli")]
public sealed class SupportBundleTests
{
    // Assembled at run time so that no credential-shaped literal sits in the repository for the secret scan to find.
    private static readonly string Token = "ghp" + "_" + string.Concat(Enumerable.Repeat("abcdef", 6)) + "AB";

    [Fact]
    public void TheBundleShownIsTheBundleWrittenAndHoldsNoSecretPathOrRecordBody()
    {
        // First, so the harness has isolated the per-user state before the log path below is computed.
        string scratch = CliHarness.TempDirectory("support-bundle");
        using var estate = new TestEstate(2);
        string marker = "MARKER-" + Guid.NewGuid().ToString("N");
        estate.Add("work-order", estate.OperatorId, $$"""{"key":"SUPPORT-1","kind":"ordinary","title":"{{marker}}","intent":"Exercise the support bundle.","scope":["tests"],"exitQuestion":"Redacted?","inputs":[],"deliverables":["evidence"]}""");
        string log = DaemonLifecycle.DaemonLogPath(estate.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        File.WriteAllLines(log, [
            "2026-11-01T10:00:00Z served a status read",
            $"2026-11-01T10:00:01Z error: refused a request with {Token} for {Path.Combine(estate.Root, "records", "x.json")}",
            "2026-11-01T10:00:02Z failed to open /Users/someone/Library/estate and C:\\Users\\someone\\estate",
        ]);
        string output = Path.Combine(scratch, "bundle.json");
        try
        {
            CliHarness.Outcome bundle = CliHarness.Run("--estate", estate.Root, "support", "bundle", "--out", output);
            Assert.Equal(0, bundle.Code);
            Assert.Equal(bundle.Out, File.ReadAllText(output));
            Assert.Equal(["bundle.json"], Directory.GetFiles(scratch).Select(Path.GetFileName));
            foreach (string never in new[] { marker, Token, estate.Root, "/Users/someone", "C:\\Users\\someone", "Test Estate", "Test Operator" })
                Assert.DoesNotContain(never, bundle.Out, StringComparison.OrdinalIgnoreCase);

            JsonNode json = JsonNode.Parse(bundle.Out)!;
            Assert.Equal("vericairn.support/1", (string?)json["bundle"]);
            Assert.False(string.IsNullOrEmpty((string?)json["vericairn"]));
            Assert.Equal(1, (int?)json["estate"]!["workOrders"]!["planned"]);
            Assert.Equal("embedded", (string?)json["estate"]!["runtime"]);
            string errors = string.Join("\n", json["recentErrors"]!.AsArray().Select(line => (string?)line));
            Assert.Contains("<secret>", errors, StringComparison.Ordinal);
            Assert.Contains("<path>", errors, StringComparison.Ordinal);
            Assert.DoesNotContain("served a status read", errors, StringComparison.Ordinal);
            Assert.True((int?)json["redacted"]!["secrets"] >= 1);
            Assert.True((int?)json["redacted"]!["paths"] >= 3);
        }
        finally { CliHarness.Delete(scratch); }
    }

    [Fact]
    public void AnExistingFileIsNeverReplacedAndOutsideAnEstateOnlyTheVersionAndPlatformAreGathered()
    {
        string scratch = CliHarness.TempDirectory("support-bundle-existing");
        string existing = Path.Combine(scratch, "bundle.json");
        File.WriteAllText(existing, "keep");
        try
        {
            CliHarness.Outcome refused = CliHarness.Run("--estate", scratch, "support", "bundle", "--out", existing);
            Assert.NotEqual(0, refused.Code);
            Assert.Equal("keep", File.ReadAllText(existing));

            CliHarness.Outcome outside = CliHarness.Run("--estate", scratch, "support", "bundle");
            Assert.Equal(0, outside.Code);
            JsonNode json = JsonNode.Parse(outside.Out)!;
            Assert.NotNull(json["platform"]);
            Assert.Equal(JsonValueKind.String, json["estate"]!.GetValueKind());
            Assert.Null(json["configuration"]);
        }
        finally { CliHarness.Delete(scratch); }
    }

    [Fact]
    public void TheRedactorRemovesWhatTheEstateCheckCallsSecretAndNamesKnownDirectories()
    {
        string home = Path.Combine(Path.GetTempPath(), "redactor-home-" + Guid.NewGuid().ToString("N"));
        var redactor = new SupportBundle.Redactor([(home, "<home>")]);
        JsonNode redacted = redactor.Redact(new JsonObject
        {
            ["token"] = "anything at all",
            ["note"] = $"key {"AKIA" + "0123456789ABCDEF"} in {Path.Combine(home, "a", "b")} and /home/someone/x and /tmp/y and /var/folders/ab/c",
            ["nested"] = new JsonArray("-----BEGIN " + "RSA PRIVATE KEY-----", 3),
        })!;
        Assert.Equal("<secret>", (string?)redacted["token"]);
        string note = (string?)redacted["note"] ?? string.Empty;
        Assert.DoesNotContain("AKIA", note, StringComparison.Ordinal);
        Assert.Contains("<home>", note, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/someone", note, StringComparison.Ordinal);
        Assert.DoesNotContain("/tmp/y", note, StringComparison.Ordinal);
        Assert.DoesNotContain("/var/folders", note, StringComparison.Ordinal);
        Assert.Equal("<secret>", (string?)redacted["nested"]![0]);
        Assert.Equal(3, (int?)redacted["nested"]![1]);
        Assert.Equal(3, redactor.Secrets);
    }
}
