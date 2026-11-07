// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Reflection;
using Vericairn.Cli;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class OperatorUnfinishedCommandTests
{
    public OperatorUnfinishedCommandTests() => _ = CliHarness.BuiltExecutable;

    [Fact]
    public void UnfinishedIsAReadByDefaultAndFinishingNamesItsOperator()
    {
        RootCommand root = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken);
        Command unfinished = Assert.IsType<OperatorUnfinishedCommand>(root.Subcommands.Single(c => c.Name == "operator").Subcommands.Single(c => c.Name == "unfinished"));
        Assert.Equal(["--finish", "--by", "--wait-seconds"], unfinished.Options.Select(o => o.Name));
        Assert.Empty(root.Parse(["operator", "unfinished"]).Errors);
        Assert.Empty(root.Parse(["operator", "unfinished", "--finish", "--by", "operator", "--wait-seconds", "0"]).Errors);
    }

    [Fact]
    public void AnEstateWithoutAnUnfinishedActionSaysSoAndFinishingNeedsAnOperator()
    {
        using var estate = new TestEstate(3);
        int before = estate.Load().Records.Count;
        CliHarness.Outcome human = CliHarness.Run("--estate", estate.Root, "operator", "unfinished");
        Assert.Equal(0, human.Code);
        Assert.Contains("no unfinished operator action", human.Out, StringComparison.Ordinal);
        CliHarness.Outcome json = CliHarness.Run("--estate", estate.Root, "--json", "operator", "unfinished");
        Assert.Equal(0, json.Code);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.Json.GetProperty("action").ValueKind);
        Assert.NotEqual(0, CliHarness.Run("--estate", estate.Root, "operator", "unfinished", "--finish").Code);
        Assert.NotEqual(0, CliHarness.Run("--estate", estate.Root, "operator", "unfinished", "--by", estate.OperatorId).Code);
        Assert.Equal(before, estate.Load().Records.Count);
    }

    [Fact]
    public void AnUnfinishedActionShowsItsRecordPullRequestStageAndNextStep()
    {
        using var estate = new TestEstate(3);
        var action = new OperatorUnfinishedAction("operator-record:abc", "01M6D638WDQ105MT8421CKRK9G", "acceptance", "release:01M6D63QHMCEVJEKPQJ9YRYB24:sha256:" + new string('e', 64),
            estate.OperatorId, new("records/acceptance/2026-11/01M6D638WDQ105MT8421CKRK9G.json", "01M6D638WDQ105MT8421CKRK9G", "sha256:" + new string('f', 64)),
            "operator/01M6D638WDQ105MT8421CKRK9G", new string('a', 40), "merged", "508", new("records/operation/2026-11/merge.json", "01M6D68SA0392CSA439DGJ2XGH", "sha256:" + new string('1', 64)),
            new string('b', 40), "failed", null, null, true, "acceptance 01M6D638WDQ105MT8421CKRK9G, merged in pull request 508", "Rerun the failed target run, then finish it.");
        using EstateService service = EstateService.Open(estate.Root);
        IEstateApi api = DispatchProxy.Create<IEstateApi, FixedUnfinishedApi>();
        ((FixedUnfinishedApi)api).Target = service;
        ((FixedUnfinishedApi)api).Response = new(action);
        CliHarness.Outcome human = CliHarness.RunWithApi(api, null, ["--estate", estate.Root, "operator", "unfinished"]);
        Assert.Equal(0, human.Code);
        foreach (string expected in new[] { "Unfinished: acceptance 01M6D638WDQ105MT8421CKRK9G", "Pull request: 508", "target checks: failed", "Stage: merged", "Rerun the failed target run" })
            Assert.Contains(expected, human.Out, StringComparison.Ordinal);
    }

    public class FixedUnfinishedApi : DispatchProxy
    {
        public IEstateApi Target { get; set; } = null!;
        public OperatorUnfinishedResponse Response { get; set; } = new(null);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
        {
            nameof(IEstateApi.OperatorUnfinished) => Response,
            nameof(IEstateApi.OperatorUnfinishedAsync) => Task.FromResult(Response),
            _ => targetMethod.Invoke(Target, args),
        };
    }
}
