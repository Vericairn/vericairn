// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

public sealed class RevisionPolicyCompatibilityTests
{
    private static readonly string[] Selectors = ["design/*.md"];

    [Theory]
    [InlineData("Revision 1, January 2, 2026: First explanation.", "satisfied")]
    [InlineData("   Revision 1, January 2, 2026: Indented explanation.", "satisfied")]
    [InlineData("Revision 2, January 2, 2026: carries decision `example`: Revised explanation.", "satisfied")]
    [InlineData("Revision 1, January 3, 2026: Future explanation.", "unmet")]
    [InlineData("Revision 0, January 2, 2026: Invalid number.", "unmet")]
    [InlineData("Revision 2, January 2, 2026.", "unmet")]
    [InlineData("Revision 2, February 29, 2025: Invalid date.", "unmet")]
    public void TheSameLineHasTheSamePolicyMeaningWhenGenerated(string line, string verdict)
    {
        foreach (string text in new[] { "# Title\n\n" + line,
            "# Title\n\n<!-- vericairn:generated revisions -->\n" + line + "\n<!-- vericairn:end revisions -->\n\nBody.\n" })
        {
            var result = FoundationPolicyTests.Run(FoundationPolicyKinds.RevisionLine, new { selectors = Selectors, style = "estate-design/1" }, [],
                selection: new { kind = "repository", repository = "estate", selectors = Selectors },
                repositories: [FoundationPolicyTests.Repository(PolicyBasisTests.File("design/example.md", text))]);
            Assert.Equal(verdict, Assert.Single(result.Findings).Verdict);
        }
    }

    [Theory]
    [InlineData("<!-- other comment -->\nRevision 1, January 2, 2026: Hidden behind a comment.")]
    [InlineData("<!-- vericairn:generated revisions -->\nRevision 1, January 2, 2026: Missing closing marker.")]
    [InlineData("<!-- vericairn:generated revisions -->\n<!-- vericairn:end revisions -->\nRevision 1, January 2, 2026: Outside the region.")]
    public void MarkersCannotLoosenTheFirstProseRule(string body)
    {
        var result = FoundationPolicyTests.Run(FoundationPolicyKinds.RevisionLine, new { selectors = Selectors, style = "estate-design/1" }, [],
            selection: new { kind = "repository", repository = "estate", selectors = Selectors },
            repositories: [FoundationPolicyTests.Repository(PolicyBasisTests.File("design/example.md", "# Title\n\n" + body))]);
        Assert.Equal("unmet", Assert.Single(result.Findings).Verdict);
    }
}
