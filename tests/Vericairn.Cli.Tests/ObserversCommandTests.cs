// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

/// <summary>OPS-20 through the command line: declare, report and list observers, and human status shows each observer's last report.</summary>
[Collection("cli")]
public sealed class ObserversCommandTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheCommandsNameEveryRequiredInput()
    {
        Command observers = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken).Subcommands.Single(c => c.Name == "observers");
        Assert.Equal(["declare", "list", "report", "withdraw"], observers.Subcommands.Select(c => c.Name).Order());
        Command declare = observers.Subcommands.Single(c => c.Name == "declare");
        foreach (string name in new[] { "--kind", "--interval", "--by" }) Assert.True(declare.Options.Single(o => o.Name == name).Required);
        Assert.False(declare.Options.Single(o => o.Name == "--grace").Required);
        Command report = observers.Subcommands.Single(c => c.Name == "report");
        foreach (string name in new[] { "--outcome", "--by" }) Assert.True(report.Options.Single(o => o.Name == name).Required);
        foreach (string name in new[] { "--reason", "--at", "--detail" }) Assert.False(report.Options.Single(o => o.Name == name).Required);
        Assert.True(observers.Subcommands.Single(c => c.Name == "withdraw").Options.Single(o => o.Name == "--reason").Required);
    }

    [Fact]
    public void StatusShowsEachObserversLastReportAndASilentOneAsAttentionWhileStatusJsonStaysCanonical()
    {
        Assert.Equal(0, CliHarness.Run("observers", "--help").Code);
        using var estate = new TestEstate();
        using EstateService api = EstateService.Open(estate.Root);
        var clock = new HeldClock(Start);
        api.PolicyClock = clock;
        string json = CliHarness.RunWithApi(api, null, "--estate", estate.Root, "status", "--json").Out;

        CliHarness.Outcome declared = CliHarness.RunWithApi(api, null, "--estate", estate.Root, "observers", "declare", "ci-main", "--kind", "ci", "--interval", "PT1H", "--grace", "PT15M", "--by", estate.OperatorId);
        Assert.Equal(0, declared.Code);
        Assert.Contains("Observer ci-main is declared on this machine: it reports every PT1H, and its silence past a further PT15M is attention.", declared.Out, StringComparison.Ordinal);
        Assert.Equal(0, CliHarness.RunWithApi(api, null, "--estate", estate.Root, "observers", "declare", "nightly", "--kind", "scheduled-workflow", "--interval", "P1D", "--by", estate.OperatorId).Code);
        Assert.Equal(2, CliHarness.RunWithApi(api, null, "--estate", estate.Root, "observers", "declare", "weekly", "--kind", "other", "--interval", "a week", "--by", estate.OperatorId).Code);

        CliHarness.Outcome reported = CliHarness.RunWithApi(api, null, "--estate", estate.Root, "observers", "report", "ci-main", "--outcome", "passed", "--by", estate.BuilderId);
        Assert.Equal(0, reported.Code);
        CliHarness.Outcome quota = CliHarness.RunWithApi(api, null, "--estate", estate.Root, "observers", "report", "nightly", "--outcome", "not-observing", "--reason", "quota-exhausted", "--by", estate.BuilderId);
        Assert.Equal(0, quota.Code);
        Assert.Equal(1, CliHarness.RunWithApi(api, null, "--estate", estate.Root, "observers", "report", "nightly", "--outcome", "not-observing", "--by", estate.BuilderId).Code);

        clock.Now = Start.AddMinutes(76);
        CliHarness.Outcome status = CliHarness.RunWithApi(api, null, "--estate", estate.Root, "status");
        Assert.Equal(0, status.Code);
        Assert.Contains("Observers (this machine):", status.Out, StringComparison.Ordinal);
        Assert.Contains($"  ci-main (ci, every PT1H, grace PT15M): silent; last report passed at 2026-11-21T09:00:00Z by {estate.BuilderId}; attention: It has not reported since", status.Out, StringComparison.Ordinal);
        Assert.Contains($"  nightly (scheduled-workflow, every P1D, grace PT6H): not-observing; last report not-observing (quota-exhausted) at 2026-11-21T09:00:00Z by {estate.BuilderId}", status.Out, StringComparison.Ordinal);
        Assert.Contains("failed check: Observer ci-main (ci) is silent", status.Out, StringComparison.Ordinal);
        Assert.Contains("failed check: Observer nightly (scheduled-workflow) is not observing", status.Out, StringComparison.Ordinal);

        CliHarness.Outcome list = CliHarness.RunWithApi(api, null, "--estate", estate.Root, "--json", "observers", "list");
        Assert.Equal(0, list.Code);
        Assert.Equal(["silent", "not-observing"], list.Json.GetProperty("observers").EnumerateArray().Select(o => o.GetProperty("state").GetString()).ToArray());
        Assert.Equal(json, CliHarness.RunWithApi(api, null, "--estate", estate.Root, "status", "--json").Out);

        Assert.Equal(1, CliHarness.RunWithApi(api, null, "--estate", estate.Root, "observers", "withdraw", "nightly", "--reason", "Retired.", "--by", estate.BuilderId).Code);
        CliHarness.Outcome withdrawn = CliHarness.RunWithApi(api, null, "--estate", estate.Root, "observers", "withdraw", "nightly", "--reason", "The nightly workflow was retired.", "--by", estate.OperatorId);
        Assert.Equal(0, withdrawn.Code);
        Assert.DoesNotContain("nightly", CliHarness.RunWithApi(api, null, "--estate", estate.Root, "status").Out, StringComparison.Ordinal);
    }
}
