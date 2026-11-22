// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

/// <summary>
/// From 0.9.4 (FIX-148; design/performance.md §§ 3.1, 3.2, 6): each one-shot command judges the estate once, counted rather than timed.
/// A command runs as the command line runs it without a daemon: a one-shot runtime opens, judging the estate for its startup passes, and
/// the command's every later load reads that judgment while nothing changed. Each runtime counts in a context of its own with fresh
/// signature tables, so the counts are exact whatever else runs in the process. A second runtime reading the same tables verifies nothing
/// a first verified, a judgment larger than the tables' starting bound grows the bound without evicting a verdict, and every command's
/// answer equals that of a runtime judging at every load from empty tables and an empty parse cache, which shares nothing with the kept
/// judgment.
/// </summary>
[Collection("cli")]
public sealed class OneJudgmentCommandTests : IDisposable
{
    private readonly PolicyFixture fixture = new();
    private readonly string goal;
    private readonly string key;
    private readonly string record;

    public OneJudgmentCommandTests()
    {
        RecordEnvelope planned = fixture.Add("goal", new()
        {
            ["title"] = "Counted goal", ["objective"] = "Finish the counted work.",
            ["completion"] = new JsonArray(new JsonObject { ["evidence"] = "The work is accepted.", ["check"] = "Read the acceptances." }),
            ["constraints"] = new JsonArray(), ["kind"] = "finite", ["sponsor"] = fixture.Estate.OperatorId,
            ["budget"] = new JsonObject { ["wallMinutes"] = 600, ["attemptsPerStep"] = 5, ["concurrency"] = 5 },
            ["plan"] = new JsonObject { ["policy"] = "proposed-by-planner", ["approval"] = "sponsor-approves" }, ["inputs"] = new JsonArray(),
        });
        goal = planned.Id;
        RecordEnvelope? first = null;
        for (int i = 0; i < 4; i++)
        {
            RecordEnvelope order = fixture.Add("work-order", new()
            {
                ["key"] = "TASK-" + i.ToString("D3", CultureInfo.InvariantCulture), ["kind"] = "ordinary", ["title"] = "Counted work",
                ["intent"] = "Exercise the count.", ["scope"] = new JsonArray("test fixtures"), ["exitQuestion"] = "Is the result ready?",
                ["inputs"] = new JsonArray(new JsonObject { ["type"] = "goal", ["id"] = goal }), ["deliverables"] = new JsonArray("checked result"),
            });
            first ??= order;
            RecordEnvelope attempt = fixture.BeginAttempt(order: order);
            RecordEnvelope result = fixture.Add("result", PolicyFixture.ResultBody(), builder: true, subject: attempt.Reference);
            if (i % 2 == 0)
            {
                RecordEnvelope review = fixture.Review(result);
                fixture.Add("acceptance", new() { ["statement"] = "Accept the checked result.", ["basis"] = new JsonArray(review.Id), ["self"] = false }, subject: result.Reference);
            }
        }

        key = EstateModel.BodyString(first!, "key")!;
        record = first!.Id;
    }

    private string Root => fixture.Estate.Root;

    private string[][] Commands =>
    [
        ["goal"], ["goal", "show", goal], ["goal", "next", goal], ["goal", "step", goal, "--by", fixture.Estate.OperatorId],
        ["status"], ["status", "--json"], ["progress"], ["operator", "list"], ["work", "observe", key], ["work", "brief", key], ["records", "show", record],
    ];

    [Fact]
    public void EveryOneShotCommandJudgesTheEstateOnceAndAnswersAsAFreshJudgmentDoes()
    {
        foreach (string[] command in Commands)
        {
            var judgments = new JudgmentContext(new SignatureTables());
            (CliHarness.Outcome outcome, EstateService service) = Run(judgments, command);
            using (service)
            {
                string name = string.Join(' ', command);
                // Each read answers; the goal is proposed, so its step is refused before anything is recorded.
                Assert.True(command is ["goal", "step", ..] ? outcome.Code == 2 && outcome.Err.Length > 0 : outcome.Code == 0, $"vericairn {name} exited {outcome.Code}\n{outcome.Err}");
                // The opening's judgment serves the command; a load under the write lock, of which a read takes none, would be fresh.
                Assert.True(judgments.Judgments == 1, $"vericairn {name} judged the estate {judgments.Judgments} times.");
                Assert.Equal(0, service.Session.LockedJudgments);

                // The same command against a runtime that judges at every load, from empty signature tables and an empty parse cache:
                // its first judgment shares nothing with the kept one, so a wrongly kept answer cannot agree with itself.
                CliHarness.Outcome fresh = Fresh(command);
                Assert.Equal(fresh.Code, outcome.Code);
                Assert.Equal(Normalized(fresh.Out), Normalized(outcome.Out));
            }
        }
    }

    [Fact]
    public void ASecondRuntimeReadingTheSameTablesVerifiesNothingTheFirstVerified()
    {
        var tables = new SignatureTables();
        var first = new JudgmentContext(tables);
        (_, EstateService opened) = Run(first, ["status", "--json"]);
        opened.Dispose();
        Assert.True(first.Verifications > 0);
        Assert.True(first.KeyImports > 0);

        var second = new JudgmentContext(tables);
        (CliHarness.Outcome outcome, EstateService again) = Run(second, ["status", "--json"]);
        using (again)
        {
            Assert.Equal(0, outcome.Code);
            Assert.Equal((1L, 0L, 0L), (second.Judgments, second.Verifications, second.KeyImports));
        }
    }

    [Fact]
    public void AJudgmentLargerThanTheStartingBoundGrowsTheTablesWithoutEvictingAVerdict()
    {
        var tables = new SignatureTables(startingVerdicts: 4, startingKeys: 1);
        var judgments = new JudgmentContext(tables);
        (CliHarness.Outcome outcome, EstateService service) = Run(judgments, ["status", "--json"]);
        using (service)
        {
            Assert.Equal(0, outcome.Code);
            EstateModel model = service.Session.Load();
            int signatures = model.Records.Sum(r => r.Signatures.Count);
            Assert.True(signatures > 4);
            Assert.True(tables.VerdictBound >= 2 * signatures);
            Assert.True(tables.KeyBound >= 4);
            Assert.Equal(judgments.Verifications, tables.Verdicts);
            Assert.Equal((0L, 0L, 0L), (tables.Evictions, tables.EvictedInUse, judgments.EvictionsInUse));
        }
    }

    // A one-shot runtime as the command line opens one without a daemon, counting in the given context, and the command run against it.
    private (CliHarness.Outcome Outcome, EstateService Service) Run(JudgmentContext judgments, string[] command)
    {
        EstateService service = EstateService.Open(Root, null, null, oneShot: true, judgments: judgments);
        service.HostsEmbedded = true;
        try
        {
            return (CliHarness.RunWithApi(service, null, ["--estate", Root, .. command]), service);
        }
        catch
        {
            service.Dispose();
            throw;
        }
    }

    private CliHarness.Outcome Fresh(string[] command)
    {
        using EstateService service = EstateService.Open(Root, null, null, oneShot: false, judgments: new JudgmentContext(new SignatureTables()));
        service.HostsEmbedded = true;
        // Handed over behind another interface, so that, as for each request a daemon serves, the command's calls share no derivation (FIX-152).
        return CliHarness.RunWithApi(new Vericairn.Protocol.CancellationBoundApi(service, CancellationToken.None), null, ["--estate", Root, .. command]);
    }

    // The clock's readings (a brief's freshness, status's time) are the only difference two runs of one command may show.
    private static string Normalized(string text) => Regex.Replace(text, @"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(\.\d+)?Z", "<time>");

    public void Dispose() => fixture.Dispose();
}
