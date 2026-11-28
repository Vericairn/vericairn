// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;

namespace Vericairn.Cli.Tests;

public sealed class StarterFlowGuideTests
{
    [Theory]
    [InlineData("runs locally in your own coding-agent harness, using your own harness account")]
    [InlineData("No hosted service takes part.")]
    [InlineData("`estate check` reports the earlier review as `record.subject-stale`")]
    [InlineData("A review with outcome `findings` sends the draft back to the scheduler for another attempt.")]
    [InlineData("The flow asks again only through `goal step`, never unattended")]
    [InlineData("`2 of 2 steps done; the run is complete`")]
    [InlineData("{\"roles\":[{\"role\":\"scheduler\",\"principals\":[\"${scheduler}\"]},{\"role\":\"approver\",\"principals\":[\"${operator}\"]}]}")]
    [InlineData("{\"independence\":\"operator\",\"outcome\":\"pass\",\"summary\":\"The visit matches the request.\",\"findings\":[]}")]
    public void StarterFlowExplainsHowToRunAndApproveAnAppointment(string expected)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Vericairn.slnx"))) root = root.Parent;
        Assert.NotNull(root);

        string guide = File.ReadAllText(Path.Combine(root.FullName, "docs", "templates.md")).ReplaceLineEndings("\n");
        string section = Assert.Single(Regex.Matches(guide, @"^### The starter flow[^\n]*\n(?<body>[\s\S]+?)(?=^#{1,3} )", RegexOptions.Multiline)).Groups["body"].Value;
        Assert.Contains(expected, Regex.Replace(section, @"\s+", " "), StringComparison.Ordinal);
    }
}
