// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;

namespace Vericairn.Records.Tests;

public sealed class DocumentRevisionBytesTests
{
    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", true)]
    public void OnlyThePayloadIsExcludedIncludingWithBomAndNonAsciiText(string newline, bool bom)
    {
        string prefix = (bom ? "\uFEFF" : "") + "# Café" + newline + newline + GeneratedRegions.Start("revisions") + newline;
        string suffix = GeneratedRegions.End("revisions") + newline + newline + "Authored café. " + newline;
        byte[] expected = Encoding.UTF8.GetBytes(prefix + suffix);
        foreach (string payload in new[] { "", "Revision 1, October 13, 2026: First.\n", "Revision 99, October 13, 2026: Refresh.\r\n" })
            Assert.Equal(expected, RevisionLines.AuthoredContent(Encoding.UTF8.GetBytes(prefix + payload + suffix)));
        Assert.NotEqual(ContentHash.Sha256(expected), ContentHash.Sha256(RevisionLines.AuthoredContent(Encoding.UTF8.GetBytes(prefix + suffix.Replace("café", "body", StringComparison.Ordinal)))));
    }

    [Theory]
    [InlineData("# Title\n\nNo markers.\n")]
    [InlineData("# Title\n<!-- vericairn:generated revisions -->\nMissing end.\n")]
    [InlineData("# Title\nAuthored first.\n<!-- vericairn:generated revisions -->\n<!-- vericairn:end revisions -->\n")]
    [InlineData("# Title\n<!-- vericairn:generated revisions -->\n<!-- vericairn:generated other -->\n<!-- vericairn:end other -->\n<!-- vericairn:end revisions -->\n")]
    [InlineData("# Title\n<!-- vericairn:generated revisions -->\n<!-- vericairn:end revisions -->\n<!-- vericairn:generated revisions -->\n<!-- vericairn:end revisions -->\n")]
    public void AmbiguousOrMisplacedRegionsCannotHideAuthoredBytes(string text) =>
        Assert.Throws<InvalidOperationException>(() => RevisionLines.AuthoredContent(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void MarkerExamplesInFencesRemainAuthoredContent()
    {
        string region = "<!-- vericairn:generated revisions -->\n<!-- vericairn:end revisions -->\n";
        byte[] expected = Encoding.UTF8.GetBytes("# Title\n\n" + region + "\n```markdown\n" + region + "```\n");
        Assert.Equal(expected, RevisionLines.AuthoredContent(expected));
    }
}
