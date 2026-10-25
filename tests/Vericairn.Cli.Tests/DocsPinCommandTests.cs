// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text;
using Vericairn.Cli;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class DocsPinCommandTests
{
    [Fact]
    public void PinIsACommandClassWithRequiredAuthorAndSummary()
    {
        RootCommand root = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken);
        Command pin = root.Subcommands.Single(c => c.Name == "docs").Subcommands.Single(c => c.Name == "pin");
        Assert.IsType<DocsPinCommand>(pin);
        Assert.Equal("path", Assert.Single(pin.Arguments).Name);
        Assert.True(pin.Options.Single(o => o.Name == "--by").Required);
        Assert.True(pin.Options.Single(o => o.Name == "--summary").Required);
        Assert.False(pin.Options.Single(o => o.Name == "--title").Required);
        Assert.False(pin.Options.Single(o => o.Name == "--expected").Required);
        Assert.False(pin.Options.Single(o => o.Name == "--render-template").Required);
    }

    [Fact]
    public void OneCommandPinsExactBytesAndTheNextRevisionAndPrintsTheReceipt()
    {
        string root = CliHarness.TempDirectory("docs-pin");
        try
        {
            string by = Initialize(root);
            string path = Path.Combine(root, "docs", "guide.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[] firstBytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("# Café\r\n")];
            File.WriteAllBytes(path, firstBytes);
            CliHarness.Outcome first = CliHarness.Ok("--estate", root, "docs", "pin", "docs/guide.md", "--by", by,
                "--summary", "First revision.", "--title", "Guide", "--json");
            Assert.Equal(["hash", "id", "path"], first.Json.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            RecordEnvelope initial = EstateLoader.Load(root).ById[first.Id];
            Assert.Null(initial.Supersedes);
            Assert.Equal(1, initial.Body.GetProperty("revision").GetInt64());
            Assert.Equal(ContentHash.Sha256(firstBytes), initial.Body.GetProperty("hash").GetString());
            Assert.Equal(firstBytes.Length, initial.Body.GetProperty("bytes").GetInt64());
            Assert.Equal(firstBytes, File.ReadAllBytes(path));
            byte[] nextBytes = Encoding.UTF8.GetBytes("# Next\n");
            File.WriteAllBytes(path, nextBytes);
            CliHarness.Outcome next = CliHarness.Ok("--estate", root, "docs", "pin", "docs/guide.md", "--by", by,
                "--summary", "Next revision.", "--expected", first.Id);
            RecordEnvelope current = Assert.Single(EstateLoader.Load(root).Current("document-revision"));
            Assert.Equal(first.Id, current.Supersedes);
            Assert.Equal(2, current.Body.GetProperty("revision").GetInt64());
            Assert.Equal("Guide", current.Body.GetProperty("title").GetString());
            Assert.Equal(ContentHash.Sha256(nextBytes), current.Body.GetProperty("hash").GetString());
            Assert.Equal(nextBytes, File.ReadAllBytes(path));
            Assert.Contains("revision 2", next.Out, StringComparison.Ordinal);
            Assert.Contains(current.Id, next.Out, StringComparison.Ordinal);
            Assert.Contains(current.ComputedHash, next.Out, StringComparison.Ordinal);
            Refuses("unchanged bytes");
            File.WriteAllText(path, "# Third\n");
            Refuses("not current", "--expected", first.Id);
            Refuses("normalized", document: "../guide.md");

            void Refuses(string diagnostic, string? option = null, string? value = null, string document = "docs/guide.md")
            {
                var before = Directory.EnumerateFiles(Path.Combine(root, "records"), "*.json", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal).ToDictionary(p => p, ContentHash.Sha256File);
                string[] args = ["--estate", root, "docs", "pin", document, "--by", by, "--summary", "Must refuse."];
                CliHarness.Outcome refused = CliHarness.Run(option is null ? args : [.. args, option, value!]);
                Assert.Equal(2, refused.Code);
                Assert.Contains(diagnostic, refused.Err, StringComparison.Ordinal);
                Assert.Empty(refused.Out);
                Assert.Equal(before, Directory.EnumerateFiles(Path.Combine(root, "records"), "*.json", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal).ToDictionary(p => p, ContentHash.Sha256File));
            }
        }
        finally { CliHarness.Delete(root); }
    }

    private static string Initialize(string root) => CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example",
        "--slug", "example", "--purpose", "Pin documents.", "--operator-name", "Operator", "--organization", "Example",
        "--commit", "0123456789abcdef0123456789abcdef01234567").Json.GetProperty("operator").GetString()!;
}
