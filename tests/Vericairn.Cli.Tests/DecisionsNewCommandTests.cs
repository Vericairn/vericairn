// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Cli;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class DecisionsNewCommandTests
{
    public DecisionsNewCommandTests() => CliHarness.Run("--help");

    [Fact]
    public void NewCommandRequiresTheAnswerAndPreservesExistingDecisionReads()
    {
        RootCommand root = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken);
        Command family = root.Subcommands.Single(c => c.Name == "decisions");
        Assert.Equal("id", Assert.Single(family.Arguments).Name);
        var command = Assert.IsType<DecisionsNewCommand>(family.Subcommands.Single(c => c.Name == "new"));
        foreach (string option in new[] { "--by", "--ask", "--statement", "--answered-at", "--base", "--title", "--summary", "--synthesis" })
            Assert.True(command.Options.Single(o => o.Name == option).Required);
        Assert.False(command.Options.Single(o => o.Name == "--decided-at").Required);
    }

    [Fact]
    public void FlagsRecordTheSuppliedAnswerAndPrintAllPinsInJson()
    {
        using var fixture = new DecisionFixture();
        DecisionNewRequest request = fixture.Request;
        CliHarness.Outcome output = CliHarness.RunWithApi(fixture.Service, null, ["--estate", fixture.Estate.Root, "--json", "decisions", "new", .. Flags(request)]);
        Assert.Equal(0, output.Code);
        DecisionNewResponse response = output.Json.Deserialize<DecisionNewResponse>(ProtocolJson.Options)!;
        Assert.True(response.Ok);
        Assert.Single(response.Pins);
        JsonElement body = fixture.Service.Record(response.Written!.Id)!.Envelope.GetProperty("body");
        Assert.Equal(request.Statement, body.GetProperty("statement").GetString());
        Assert.Equal(request.AnsweredAt, body.GetProperty("decidedAt").GetString());
        CliHarness.Outcome view = CliHarness.RunWithApi(fixture.Service, null, "--estate", fixture.Estate.Root, "--json", "decisions", response.Written.Id);
        Assert.Equal(0, view.Code);
        Assert.Equal(request.Statement, view.Json.GetProperty("statement").GetString());
        CliHarness.Outcome readable = CliHarness.RunWithApi(fixture.Service, null, "--estate", fixture.Estate.Root, "decisions", response.Written.Id);
        Assert.Equal(0, readable.Code);
        foreach (string fact in new[] { request.AnsweredAt, request.BaseCommit, fixture.Ask.ComputedHash, response.Pins[0].Id, response.Pins[0].Hash })
            Assert.Contains(fact, readable.Out, StringComparison.Ordinal);
        Assert.Equal(0, CliHarness.RunWithApi(fixture.Service, null, "--estate", fixture.Estate.Root, "decisions").Code);
    }

    [Theory]
    [InlineData("time")]
    [InlineData("documents")]
    public void RefusalsKeepEveryReceiptEmptyAndWriteNothing(string fault)
    {
        using var fixture = new DecisionFixture();
        DecisionNewRequest request = fixture.Request;
        request = fault == "time" ? request with { DecidedAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddHours(1)) }
            : request with { Documents = [] };
        var before = fixture.Records();
        CliHarness.Outcome output = CliHarness.RunWithApi(fixture.Service, null, ["--estate", fixture.Estate.Root, "--json", "decisions", "new", .. Flags(request)]);
        Assert.Equal(2, output.Code);
        DecisionNewResponse response = output.Json.Deserialize<DecisionNewResponse>(ProtocolJson.Options)!;
        Assert.False(response.Ok);
        Assert.Null(response.Written);
        Assert.Empty(response.Pins);
        Assert.Contains(fault == "time" ? "later than now" : "equal exactly", response.Problem!.Message, StringComparison.Ordinal);
        Assert.Equal(before, fixture.Records());
    }

    private static string[] Flags(DecisionNewRequest request) =>
    [
        "--by", request.By, "--ask", request.Ask, "--statement", request.Statement, "--answered-at", request.AnsweredAt,
        "--base", request.BaseCommit, "--title", request.Title, "--summary", request.Summary, "--synthesis", request.Synthesis,
        .. request.Effects.SelectMany(effect => new[] { "--effect", effect }),
        .. request.Documents.SelectMany(path => new[] { "--document", path }),
        .. request.DecidedAt is null ? Array.Empty<string>() : ["--decided-at", request.DecidedAt],
    ];
}
