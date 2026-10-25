// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json.Nodes;
using Vericairn.Cli;
using Vericairn.Protocol;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class OperatorCloseCommandTests
{
    public OperatorCloseCommandTests() => _ = CliHarness.BuiltExecutable;

    [Fact]
    public void CloseIsAnAdditionalCommandWithAReadOnlyDefaultAndExplicitSelectionOptions()
    {
        RootCommand root = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken);
        Command family = root.Subcommands.Single(c => c.Name == "operator");
        Command close = Assert.IsType<OperatorCloseCommand>(family.Subcommands.Single(c => c.Name == "close"));
        Assert.Contains(family.Subcommands, c => c is OperatorListCommand);
        Assert.Contains(family.Subcommands, c => c is OperatorRecordCommand);
        Assert.Empty(root.Parse(["operator", "close"]).Errors);
        Assert.Equal(["--select", "--all", "--by", "--from"], close.Options.Select(o => o.Name));
        Assert.Empty(root.Parse(["operator", "close", "--select", "STEP-1", "--select", "STEP-2", "--by", "operator"]).Errors);
    }

    [Fact]
    public void HumanAndJsonListingsShowTheSameExactEvidenceAndWriteNothing()
    {
        using var f = new OperatorCloseFixture(device: false);
        var step = f.Add("STEP-1");
        f.Add("UNREVIEWED", reviewOutcome: "fail");
        int before = f.Estate.Load().Records.Count;
        CliHarness.Outcome human = Run(f);
        Assert.Equal(0, human.Code);
        Assert.Contains("Is STEP-1 complete?", human.Out, StringComparison.Ordinal);
        Assert.Contains(step.Result.Id, human.Out, StringComparison.Ordinal);
        Assert.Contains(step.Result.ComputedHash, human.Out, StringComparison.Ordinal);
        Assert.Contains(step.Review.Id, human.Out, StringComparison.Ordinal);
        Assert.Contains(step.Review.ComputedHash, human.Out, StringComparison.Ordinal);
        Assert.Contains("completed", human.Out, StringComparison.Ordinal);
        Assert.Contains("operator", human.Out, StringComparison.Ordinal);
        Assert.Contains("Inspected the exact result.", human.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("UNREVIEWED", human.Out, StringComparison.Ordinal);
        CliHarness.Outcome json = Run(f, "--json");
        Assert.Equal(0, json.Code);
        Assert.Equal("STEP-1", Assert.Single(json.Json.GetProperty("items").EnumerateArray()).GetProperty("key").GetString());
        Assert.Equal(before, f.Estate.Load().Records.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatableSelectionAndAllCloseOnlyTheShownSteps(bool all)
    {
        using var f = new OperatorCloseFixture();
        var first = f.Add("STEP-1");
        f.Add("STEP-2");
        var third = f.Add("STEP-3");
        string[] selection = all ? ["--all"] : ["--select", "STEP-1", "--select", "STEP-3"];
        CliHarness.Outcome outcome = Run(f, ["--json", "--by", f.Estate.OperatorId, .. selection]);
        Assert.Equal(0, outcome.Code);
        Assert.True(outcome.Json.GetProperty("ok").GetBoolean());
        Assert.Equal(all ? 3 : 2, outcome.Json.GetProperty("written").GetArrayLength());
        Assert.Contains(first.Result.ComputedHash, outcome.Err, StringComparison.Ordinal);
        Assert.Contains(third.Review.ComputedHash, outcome.Err, StringComparison.Ordinal);
        Assert.Equal(all ? 0 : 1, f.Api.OperatorCloseCandidates().Items.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASavedListingRefusesAMovedResultOrReviewWithoutAnyAcceptance(bool review)
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        var step = f.Add("STEP-2");
        string scratch = CliHarness.TempDirectory("operator-close-listing");
        try
        {
            string saved = CliHarness.WriteJson(scratch, "listing.json", Run(f, "--json").Out);
            if (review) f.Review(step.Result, supersedes: step.Review.Id);
            else f.Result(step.Attempt, supersedes: step.Result.Id);
            int before = f.Estate.Load().Records.Count;
            CliHarness.Outcome refused = Run(f, "--json", "--all", "--by", f.Estate.OperatorId, "--from", saved);
            Assert.Equal(1, refused.Code);
            Assert.False(refused.Json.GetProperty("ok").GetBoolean());
            Assert.Empty(refused.Json.GetProperty("written").EnumerateArray());
            Assert.Equal(before, f.Estate.Load().Records.Count);
            Assert.Empty(f.Estate.Load().OfType("acceptance"));
        }
        finally { CliHarness.Delete(scratch); }
    }

    [Fact]
    public void MissingReviewsNonOperatorsAndInvalidCombinationsNeverWrite()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        f.Add("STEP-2", reviewOutcome: "fail");
        int before = f.Estate.Load().Records.Count;
        CliHarness.Outcome missing = Run(f, "--json", "--select", "STEP-1", "--select", "STEP-2", "--by", f.Estate.OperatorId);
        Assert.Equal(1, missing.Code);
        Assert.False(missing.Json.GetProperty("ok").GetBoolean());
        CliHarness.Outcome worker = Run(f, "--json", "--all", "--by", f.Estate.BuilderId);
        Assert.Equal(1, worker.Code);
        Assert.False(worker.Json.GetProperty("ok").GetBoolean());
        Assert.Equal(2, Run(f, "--all").Code);
        Assert.Equal(0, Run(f, "--by", f.Estate.OperatorId).Code);
        Assert.Equal(2, Run(f, "--all", "--select", "STEP-1", "--by", f.Estate.OperatorId).Code);
        Assert.Equal(2, Run(f, "--select", "STEP-1", "--select", "STEP-1", "--by", f.Estate.OperatorId).Code);
        Assert.Equal(before, f.Estate.Load().Records.Count);
        Assert.Empty(f.Estate.Load().OfType("acceptance"));
    }

    [Fact]
    public void ASavedPartialSelectionClosesThePinnedResultEvenWhenAnUnselectedReviewMoved()
    {
        using var f = new OperatorCloseFixture();
        var first = f.Add("STEP-1");
        var second = f.Add("STEP-2");
        string scratch = CliHarness.TempDirectory("operator-close-saved");
        try
        {
            string saved = CliHarness.WriteJson(scratch, "listing.json", Run(f, "--json").Out);
            f.Review(second.Result, "fail", second.Review.Id);
            CliHarness.Outcome response = Run(f, "--json", "--select", "STEP-1", "--by", f.Estate.OperatorId, "--from", saved);
            Assert.Equal(0, response.Code);
            Assert.True(response.Json.GetProperty("ok").GetBoolean());
            Assert.Single(response.Json.GetProperty("written").EnumerateArray());
            Assert.Equal(first.Result.Id, Assert.Single(f.Estate.Load().OfType("acceptance")).Subject!.Id);
            Assert.Contains(first.Review.ComputedHash, response.Err, StringComparison.Ordinal);
            Assert.DoesNotContain(second.Result.Id, response.Err, StringComparison.Ordinal);
        }
        finally { CliHarness.Delete(scratch); }
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("exitQuestion")]
    public void AlteredSavedDescriptionsCannotBeDisplayedAsThePinnedEvidence(string field)
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        string scratch = CliHarness.TempDirectory("operator-close-altered");
        try
        {
            JsonNode listing = JsonNode.Parse(Run(f, "--json").Out)!;
            listing["items"]![0]![field] = "A different description.";
            string saved = CliHarness.WriteJson(scratch, "listing.json", listing.ToJsonString());
            f.Api.BeforeSigningRecord = () => Assert.Fail("Altered descriptions must refuse before signing.");
            CliHarness.Outcome response = Run(f, "--json", "--all", "--by", f.Estate.OperatorId, "--from", saved);
            Assert.Equal(1, response.Code);
            Assert.False(response.Json.GetProperty("ok").GetBoolean());
            Assert.DoesNotContain("A different description.", response.Err, StringComparison.Ordinal);
            Assert.Empty(f.Estate.Load().OfType("acceptance"));
        }
        finally { CliHarness.Delete(scratch); }
    }

    private static CliHarness.Outcome Run(OperatorCloseFixture fixture, params string[] arguments) =>
        CliHarness.RunWithApi(fixture.Api, null, ["--estate", fixture.Estate.Root, "operator", "close", .. arguments]);
}
