// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The cost of status at 10,000 generated records (FIX-136), measured as one <c>vericairn status</c> without a daemon measures it: the
/// runtime opens, judging the estate once for both of its startup passes, and status derives every view from that judgment, which the
/// one-shot runtime keeps (FIX-148; before it, status judged once more). The bound is the profile's, counted (<see cref="StatusPassCountTests"/>): the profile of a copy of the first-party estate
/// (9,778 records) found three judgments of the estate in one read and passes repeated for each record or view, and a read now judges it
/// twice, derives the living documents once and reads the whole work orders and operations as often as on an estate of 40 work orders.
/// The times are written to the test output, beside one judgment of the same records timed alone, but bound nothing: on an Apple silicon
/// Mac mini status took 1.8 to 2.2 times one judgment and the code before FIX-136 3.1 times (its judgment held the passes this change
/// removed), while on GitHub's Linux runner status took 2.3 times its own judgment, so no ratio both separates them and holds on every
/// platform. It generates and judges 10,000 records several times, so it runs only when <c>VERICAIRN_COST_TESTS</c> is 1, as
/// continuous integration's cost step sets it on each platform.
/// </summary>
[Collection("status-performance")]
public sealed class StatusCostTests(ITestOutputHelper output)
{
    private const int Records = 10_000;
    private const string EnvironmentVariable = "VERICAIRN_COST_TESTS";

    [Fact]
    public void StatusAtTenThousandRecordsJudgesTheEstateOnceWithoutRepeatedPassesAndWithUnchangedAnswers()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(EnvironmentVariable) == "1", $"The 10,000-record status cost test runs when {EnvironmentVariable} is 1.");
        var generation = Stopwatch.StartNew();
        // About 12 records for each work order, under 20 goals that its work names, with an operation each and living documents over its
        // documents: the shapes whose repeated passes the profile found.
        using var estate = new GeneratedStatusEstate(815, goals: 20, operations: true, livingDocuments: true);
        using var small = new GeneratedStatusEstate(40, goals: 20, operations: true, livingDocuments: true);
        output.WriteLine($"generated in {generation.Elapsed.TotalSeconds:F1} s");

        TimeSpan judgment = Judge(estate.Root);
        StatusPasses first = Read(estate.Root, "first");
        Assert.False(first.Status.HasErrors, "The generated estate's status must validate and evaluate without errors.");
        int records = first.Status.Json.GetProperty("records").GetInt32();
        output.WriteLine($"records: {records}");
        Assert.InRange(records, Records, Records + 200);

        // One judgment, the opening's, kept for status by the one-shot runtime (FIX-148), one derivation of the living documents, and no
        // more reads of the whole work orders or operations than on an estate of 40 work orders.
        StatusPasses fewer = StatusPasses.Read(small.Root);
        Assert.Equal((1, 1), (first.OpeningJudgments, first.Judgments));
        Assert.Equal(1, first.LivingDocumentDerivations);
        Assert.Equal((fewer.WorkOrderReads, fewer.OperationReads), (first.WorkOrderReads, first.OperationReads));

        // A second read, by a runtime opened afresh, answers the same.
        StatusPasses second = Read(estate.Root, "repeated");
        Assert.Equal(first.Status.Json.GetRawText(), second.Status.Json.GetRawText());
        Assert.Equal(first.Status.Text, second.Status.Text);

        // The record-derived views equal those of an independent judgment that shares nothing with the runtime: no parse cache, no
        // judgment kept from the opening, and no view derived for an earlier one.
        EstateModel independent = EstateLoader.Load(estate.Root);
        JsonObject expected = JsonNode.Parse(EstateStatus.RenderJson(independent, EstateChecks.Run(independent)))!.AsObject();
        JsonObject actual = JsonNode.Parse(first.Status.Json.GetRawText())!.AsObject();
        foreach (string member in new[] { "workOrders", "assignments", "operations", "documents", "releases", "operatorRequests", "history", "records", "estate" })
        {
            Assert.True(JsonNode.DeepEquals(expected[member], actual[member]), $"Status's {member} differ from an independent judgment's.");
        }

        TimeSpan fastest = first.Elapsed < second.Elapsed ? first.Elapsed : second.Elapsed;
        output.WriteLine($"status against one judgment of the same records: {fastest.TotalMilliseconds / judgment.TotalMilliseconds:F2} times");
        // goal and goal show, as one-shot commands (FIX-148): the opening's judgment serves the list and the goal, counted; the times are
        // written, never asserted.
        foreach ((string command, Func<EstateService, object?> read) in new (string, Func<EstateService, object?>)[]
        {
            ("goal", api => api.Goals()),
            ("goal show", api => api.Goal(api.Goals()[0].Id)),
        })
        {
            var clock = Stopwatch.StartNew();
            var judgments = new JudgmentContext(new Vericairn.Records.Signing.SignatureTables());
            using EstateService service = EstateService.Open(estate.Root, null, null, oneShot: true, judgments: judgments);
            Assert.NotNull(read(service));
            clock.Stop();
            output.WriteLine($"{command}: {clock.Elapsed.TotalMilliseconds:F0} ms; judgments {judgments.Judgments}, verifications {judgments.Verifications}, parses {judgments.Parses}");
            Assert.Equal(1, judgments.Judgments);
        }
    }

    private TimeSpan Judge(string root)
    {
        var clock = Stopwatch.StartNew();
        EstateModel model = EstateLoader.Load(root);
        clock.Stop();
        Assert.False(model.Findings.HasErrors);
        output.WriteLine($"one judgment of the same records: {clock.Elapsed.TotalMilliseconds:F0} ms");
        return clock.Elapsed;
    }

    private StatusPasses Read(string root, string run)
    {
        StatusPasses passes = StatusPasses.Read(root, (phase, elapsed) => output.WriteLine($"{run} {phase}: {elapsed.TotalMilliseconds:F0} ms"));
        output.WriteLine($"{run} opening: {passes.Opening.TotalMilliseconds:F0} ms; total: {passes.Elapsed.TotalMilliseconds:F0} ms; "
            + $"judgments {passes.Judgments}, living-document derivations {passes.LivingDocumentDerivations}, work-order reads {passes.WorkOrderReads}, operation reads {passes.OperationReads}");
        return passes;
    }
}
