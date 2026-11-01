// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class RevisionHistoryCommandTests
{
    [Fact]
    public void ConvertAndPinCommandsGenerateHistoryWithoutAnOrdinalArgument()
    {
        using var estate = new TestEstate(3);
        const string document = "design/example.md";
        string file = Path.Combine(estate.Root, document);
        const string firstLine = "Revision 1, October 13, 2026. Keep this complete history.";
        estate.WriteFile(document, "# Example\n\n" + firstLine + "\n\nOriginal body.\n");
        string old = CliHarness.Ok("--estate", estate.Root, "--json", "docs", "pin", document, "--by", estate.OperatorId, "--summary", "First pin.").Id;
        CliHarness.Outcome converted = CliHarness.Ok("--estate", estate.Root, "docs", "pin", document, "--by", estate.OperatorId,
            "--summary", "Convert history.", "--expected", old, "--convert-revisions");
        Assert.Contains("revision 2", converted.Out, StringComparison.Ordinal);
        Assert.Contains(firstLine, File.ReadAllText(file), StringComparison.Ordinal);
        File.WriteAllText(file, File.ReadAllText(file).Replace("Original body.", "Changed body.", StringComparison.Ordinal));
        CliHarness.Outcome next = CliHarness.Ok("--estate", estate.Root, "--json", "docs", "pin", document, "--by", estate.OperatorId, "--summary", "Change the body.");
        var pin = estate.Load().ById[next.Id];
        Assert.Equal(RevisionLines.HashMode, EstateModel.BodyString(pin, "hashMode"));
        Assert.Equal(ContentHash.Sha256(RevisionLines.AuthoredContent(File.ReadAllBytes(file))), EstateModel.BodyString(pin, "hash"));
        Assert.Contains("Revision 3, ", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.DoesNotContain("Change the body.", Encoding.UTF8.GetString(RevisionLines.AuthoredContent(File.ReadAllBytes(file))), StringComparison.Ordinal);
        CliHarness.Ok("--estate", estate.Root, "docs", "pin", document, "--by", estate.OperatorId, "--summary", "Refresh history.", "--render-template");
    }

    [Fact]
    public void ConversionReportsTheUnrecordedLineAndLeavesTheDocumentUntouched()
    {
        using var estate = new TestEstate(3);
        const string document = "design/example.md";
        const string text = "# Example\n\nRevision 1, October 13, 2026: A line without a record.\n";
        estate.WriteFile(document, text);
        CliHarness.Outcome result = CliHarness.Run(["--estate", estate.Root, "docs", "pin", document, "--by", estate.OperatorId,
            "--summary", "Convert history.", "--convert-revisions"]);
        Assert.Equal(2, result.Code);
        Assert.Contains("no record", result.Err, StringComparison.Ordinal);
        Assert.Empty(estate.Load().OfType("document-revision"));
        Assert.Equal(text, File.ReadAllText(Path.Combine(estate.Root, document)));
    }
}
