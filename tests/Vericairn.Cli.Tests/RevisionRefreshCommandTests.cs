// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class RevisionRefreshCommandTests
{
    [Fact]
    public void RefreshRestoresGeneratedTextWithoutChangingTheAuthoredContentHash()
    {
        using var estate = new TestEstate(3);
        const string path = "design/example.md";
        estate.WriteFile(path, "# Example\n\nAuthored body.\n");
        string pin = CliHarness.Ok("--estate", estate.Root, "--json", "docs", "pin", path, "--by", estate.OperatorId,
            "--summary", "First generated line.", "--convert-revisions").Id;
        string hash = EstateModel.BodyString(estate.Load().ById[pin], "hash")!;
        string file = Path.Combine(estate.Root, path);
        File.WriteAllText(file, File.ReadAllText(file).Replace("First generated line.", "Hand-edited payload.", StringComparison.Ordinal));
        Assert.Equal(hash, ContentHash.Sha256(RevisionLines.AuthoredContent(File.ReadAllBytes(file))));
        CliHarness.Outcome refreshed = CliHarness.Ok("--estate", estate.Root, "docs", "pin", path, "--by", estate.OperatorId,
            "--summary", "Refresh the history.", "--render-template");
        Assert.Contains("revision 2", refreshed.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("Hand-edited payload.", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Contains("First generated line.", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Equal(hash, ContentHash.Sha256(RevisionLines.AuthoredContent(File.ReadAllBytes(file))));
    }
}
