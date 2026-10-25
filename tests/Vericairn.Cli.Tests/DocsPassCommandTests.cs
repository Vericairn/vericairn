// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Cli;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class DocsPassCommandTests
{
    [Fact]
    public void ListsReleaseChangesWritesOnlyPatchFilesAndAssignsOneSelectedDocument()
    {
        using var f = new DocumentPassFixture();
        using var output = new TestEstate();
        CliHarness.Outcome Run(params string[] args) => CliHarness.RunWithApi(f.Service, null, ["--estate", f.Estate.Root, .. args]);
        int before = f.Estate.Load().Records.Count;
        string directory = Path.Combine(output.Root, "patches");
        CliHarness.Outcome listing = Run("docs", "pass", "0.2.0", "--output", directory);
        Assert.Equal(0, listing.Code);
        Assert.Contains("4 documents still due", listing.Out, StringComparison.Ordinal);
        Assert.Contains("deleted: src/deleted.txt", listing.Out, StringComparison.Ordinal);
        Assert.Equal(3, Directory.GetFiles(directory, "*.patch").Length);
        Assert.Equal(before, f.Estate.Load().Records.Count);
        Assert.Empty(f.Git("status", "--porcelain"));
        Assert.Equal(f.SecondCommit, f.Git("rev-parse", "HEAD").Trim());
        string[] bytes = Directory.GetFiles(directory, "*.patch").Order(StringComparer.Ordinal).Select(File.ReadAllText).ToArray();
        Assert.NotEqual(0, Run("docs", "pass", "0.2.0", "--output", directory).Code);
        Assert.Equal(bytes, Directory.GetFiles(directory, "*.patch").Order(StringComparer.Ordinal).Select(File.ReadAllText));

        CliHarness.Outcome assigned = Run("--json", "docs", "pass", "0.2.0", "--document", f.Readme.Id,
            "--assign-to", f.Estate.BuilderId, "--by", f.Estate.OperatorId, "--key", "DOCS-REVIEW-1");
        Assert.Equal(0, assigned.Code);
        using JsonDocument json = JsonDocument.Parse(assigned.Out);
        Assert.True(json.RootElement.GetProperty("assignment").GetProperty("ok").GetBoolean());
        Assert.Equal(2, json.RootElement.GetProperty("assignment").GetProperty("written").GetArrayLength());
        Assert.Single(f.Service.DocumentPass("0.2.0").Documents.SelectMany(d => d.Assignments));
        Assert.Equal(4, f.Service.DocumentPass("0.2.0").Documents.Count);
        Assert.Equal(f.SecondCommit, f.Git("rev-parse", "HEAD").Trim());
    }

    [Fact]
    public void JsonReadPreservesTypedPatchesAndInvalidSelectionsOrIncompleteAssignmentOptionsWriteNothing()
    {
        using var f = new DocumentPassFixture();
        CliHarness.Outcome Run(params string[] args) => CliHarness.RunWithApi(f.Service, null, ["--estate", f.Estate.Root, .. args]);
        CliHarness.Outcome listing = Run("--json", "docs", "pass", "0.2.0", "--product", "widget");
        Assert.Equal(0, listing.Code);
        DocumentPassResponse pass = JsonSerializer.Deserialize<DocumentPassResponse>(listing.Out, ProtocolJson.Options)!;
        Assert.Equal(4, pass.Documents.Count);
        Assert.Equal(3, pass.Updates.Count);
        Assert.Contains("+++ b/README.md", Assert.Single(pass.Updates, u => u.Path == "README.md").Patch, StringComparison.Ordinal);
        foreach (string[] flags in new[]
        {
            new[] { "--assign-to", f.Estate.BuilderId }, new[] { "--by", f.Estate.OperatorId, "--key", "DOCS-1" },
            new[] { "--document", "unknown.md" }, new[] { "--document", f.Cleared.Id },
        })
            Assert.NotEqual(0, Run(["docs", "pass", "0.2.0", .. flags]).Code);
        Assert.Empty(f.Service.Records("assignment"));
        Assert.Empty(f.Service.Records("work-order"));
    }

    [Fact]
    public void HelpExplainsReviewAndPatchBoundaries()
    {
        CliHarness.Outcome help = CliHarness.Run("docs", "pass", "--help");
        Assert.Equal(0, help.Code);
        Assert.Contains("--assign-to", help.Out, StringComparison.Ordinal);
        Assert.Contains("--document", help.Out, StringComparison.Ordinal);
        Assert.Contains("--output", help.Out, StringComparison.Ordinal);
        Assert.Contains("Never applies or commits patches", string.Join(' ', help.Out.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), StringComparison.Ordinal);
    }
}
