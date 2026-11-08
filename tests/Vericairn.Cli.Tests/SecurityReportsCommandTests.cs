// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Cli;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class SecurityReportsCommandTests
{
    [Fact]
    public void TheCommandsNameEveryRequiredInputAndOfferNoCredentialOrPostingOption()
    {
        Command reports = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken).Subcommands.Single(c => c.Name == "security-reports");
        Assert.Equal(["poll", "triage"], reports.Subcommands.Select(c => c.Name).Order());
        Command triage = reports.Subcommands.Single(c => c.Name == "triage");
        foreach (string name in new[] { "--repository", "--expected", "--disposition", "--by" })
            Assert.True(triage.Options.Single(o => o.Name == name).Required);
        Assert.False(triage.Options.Single(o => o.Name == "--reason").Required);
        Assert.Single(triage.Arguments, a => a.Name == "advisory");
        Assert.True(reports.Subcommands.Single(c => c.Name == "poll").Options.Single(o => o.Name == "--by").Required);
        Assert.DoesNotContain(reports.Subcommands.SelectMany(c => c.Options), o => o.Name.Contains("token", StringComparison.OrdinalIgnoreCase) || o.Name.Contains("key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PollShowsIdentityStateAndSeverityStatusShowsTheIncidentAndTriageRecordsTheRevision()
    {
        Assert.Equal(0, CliHarness.Run("security-reports", "--help").Code);
        using var f = new SecurityReportFixture();
        string root = f.Api.Session.Root;

        CliHarness.Outcome poll = CliHarness.RunWithApi(f.Api, null, "--estate", root, "security-reports", "poll", "--by", f.Operator);
        Assert.Equal(0, poll.Code);
        Assert.Contains("Example/widget through github-security-advisories: read completely at ", poll.Out, StringComparison.Ordinal);
        Assert.Contains(FakeGh.Triaged + " on Example/widget: state triage, severity high, revision sha256:", poll.Out, StringComparison.Ordinal);
        Assert.Contains("Releases are held: a report awaits triage.", poll.Out, StringComparison.Ordinal);

        CliHarness.Outcome status = CliHarness.RunWithApi(f.Api, null, "--estate", root, "status");
        Assert.Contains("incident: A security report is new or changed and awaits the operator's triage: " + FakeGh.Triaged + " on Example/widget, state triage, severity high.", status.Out, StringComparison.Ordinal);

        CliHarness.Outcome json = CliHarness.RunWithApi(f.Api, null, "--estate", root, "--json", "security-reports", "poll", "--by", f.Operator);
        string hash = json.Json.GetProperty("reports").EnumerateArray().Single(r => r.GetProperty("advisory").GetString() == FakeGh.Triaged).GetProperty("contentHash").GetString()!;
        Assert.True(json.Json.GetProperty("held").GetBoolean());

        CliHarness.Outcome triage = CliHarness.RunWithApi(f.Api, null, "--estate", root, "security-reports", "triage", FakeGh.Triaged,
            "--repository", FakeGh.Repository, "--expected", hash, "--disposition", "security", "--by", f.Operator);
        Assert.Equal(0, triage.Code);
        Assert.Contains("records/observation/", triage.Out, StringComparison.Ordinal);
        Assert.Contains(FakeGh.Triaged + " on Example/widget: state triage, severity high, revision " + hash + ", triaged by ", triage.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeGh.Triaged, CliHarness.RunWithApi(f.Api, null, "--estate", root, "status").Out, StringComparison.Ordinal);

        string all = poll.Out + status.Out + json.Out + triage.Out + poll.Err + triage.Err;
        Assert.All(FakeGh.OutsideText, text => Assert.DoesNotContain(text, all, StringComparison.Ordinal));
    }

    [Fact]
    public void AnyoneButTheOperatorIsRefusedAlikeWithExitOne()
    {
        Assert.Equal(0, CliHarness.Run("security-reports", "--help").Code);
        using var f = new SecurityReportFixture();
        CliHarness.Outcome refused = CliHarness.RunWithApi(f.Api, null, "--estate", f.Api.Session.Root, "security-reports", "poll", "--by", f.Builder);
        Assert.Equal(1, refused.Code);
        Assert.Contains("operator only", refused.Out, StringComparison.Ordinal);
        Assert.Empty(f.Gh.Calls);
    }
}
