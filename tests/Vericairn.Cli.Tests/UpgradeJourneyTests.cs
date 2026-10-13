// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Cli.Tests;

/// <summary>The self-hosting loop as records: a candidate release, the operator's acceptance, the observed publication, and the estate pinned to it with a migration record.</summary>
[Collection("cli")]
public class UpgradeJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Product = """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""";

    [Fact]
    public void CandidateAcceptancePublicationAndUpgradeArePinnedRecords()
    {
        string root = CliHarness.TempDirectory("upgrade");
        string scratch = CliHarness.TempDirectory("upgrade-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Upgrade.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            string decision = CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", builder, "--body", CliHarness.WriteJson(scratch, "decision.json", $$"""{"title":"Delegate","statement":"Engineer.","summary":"Delegation.","synthesis":"Standing delegation.","effects":["builder engineers"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z"}""")).Id;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "authority", "--by", op, "--body", CliHarness.WriteJson(scratch, "authority.json", $$"""{"grantee":"{{builder}}","scope":["local-engineering"],"reserved":["publication"],"conditions":["label self-review"],"basis":"{{decision}}"}"""));
            string product = CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "product.json", Product)).Id;

            string candidateBody = $$"""{"version":"0.1.0","repository":"Example/example","commit":"{{Commit}}","status":"candidate","artifacts":[{"name":"vericairn-0.1.0-linux-x64.zip","hash":"sha256:{{new string('1', 64)}}","bytes":10,"platform":"linux-x64"},{"name":"vericairn-0.1.0-osx-arm64.zip","hash":"sha256:{{new string('2', 64)}}","bytes":10,"platform":"osx-arm64"},{"name":"vericairn-0.1.0-win-x64.zip","hash":"sha256:{{new string('3', 64)}}","bytes":10,"platform":"win-x64"},{"name":"SHA256SUMS","hash":"sha256:{{new string('4', 64)}}","bytes":3,"platform":"any"}],"recordFormat":{"reads":[1],"writes":1},"evidence":[{"summary":"Tests passed on three operating systems."}],"qualified":["linux-x64","osx-arm64","win-x64"],"signing":"unsigned developer build","limitations":["No signing material yet."]}""";
            CliHarness.Outcome candidate = CliHarness.Ok("--estate", root, "--json", "records", "new", "release", "--by", builder, "--subject", $"product:{product}", "--body", CliHarness.WriteJson(scratch, "candidate.json", candidateBody));
            Assert.Contains("0.1.0  candidate", CliHarness.Ok("--estate", root, "release", "list").Out, StringComparison.Ordinal);

            // Not yet published: the upgrade plan refuses and explains.
            CliHarness.Outcome refused = CliHarness.Run("--estate", root, "estate", "upgrade", "--to", "0.1.0", "--check");
            Assert.Equal(2, refused.Code);
            Assert.Contains("not published", refused.Err, StringComparison.Ordinal);

            // The builder cannot publish without the operator's acceptance of the exact candidate.
            string publishedBody = candidateBody.Replace("\"status\":\"candidate\"", "\"status\":\"published\",\"publishedAt\":\"2026-10-13T21:00:00Z\",\"url\":\"https://example.invalid/releases/0.1.0\"", StringComparison.Ordinal);
            string publishedPath = CliHarness.WriteJson(scratch, "published.json", publishedBody);
            Assert.NotEqual(0, CliHarness.Run("--estate", root, "--json", "records", "new", "release", "--by", builder, "--subject", $"product:{product}", "--supersedes", candidate.Id, "--body", publishedPath).Code);
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"release:{candidate.Id}:{candidate.Hash}", "--body", CliHarness.WriteJson(scratch, "accept.json", """{"statement":"Accepted release 0.1.0 from the recorded evidence.","basis":[],"self":false}"""));
            CliHarness.Ok("--estate", root, "--json", "records", "new", "release", "--by", builder, "--subject", $"product:{product}", "--supersedes", candidate.Id, "--body", publishedPath);

            // The upgrade: one manifest change plus a migration record.
            JsonElement plan = CliHarness.Ok("--estate", root, "--json", "estate", "upgrade", "--to", "0.1.0", "--check").Json;
            Assert.Equal("release 0.1.0", plan.GetProperty("toPin").GetString());
            Assert.Equal(3, plan.GetProperty("artifacts").EnumerateObject().Count());
            Assert.Equal(0, CliHarness.Run("--estate", root, "--json", "estate", "upgrade", "--to", "0.1.0", "--by", builder).Code);
            string manifest = File.ReadAllText(Path.Combine(root, "estate", "manifest.json"));
            Assert.Contains("\"mode\": \"release\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"version\": \"0.1.0\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"osx-arm64\": \"sha256:2222", manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("\"commit\"", manifest, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
            JsonElement status = CliHarness.Ok("--estate", root, "--json", "status").Json;
            Assert.Equal("release", status.GetProperty("vericairn").GetProperty("mode").GetString());
            Assert.Equal("published", status.GetProperty("releases").EnumerateArray().Single().GetProperty("status").GetString());
            Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "records", "migration"), "*.json", SearchOption.AllDirectories));
            Assert.Contains("release 0.1.0", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }
}
