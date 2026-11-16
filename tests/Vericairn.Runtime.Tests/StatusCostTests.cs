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
/// runtime opens, judging the estate once for both of its startup passes, and status judges it once more and derives every view from that
/// judgment; before this change the opening judged it twice, and the test counts the judgments. The bound is a ratio to a baseline
/// measured in the same test, as the history cost tests bound theirs: reading and parsing the same records, which no judgment, check or
/// view is part of. The profile of a copy of the first-party estate (9,778 records) put status at 11.5 seconds before this change and
/// 7.0 after; on this generated estate status took about 28 times as long as reading and parsing its records before this change and
/// about 11 times after, on an Apple silicon Mac mini, so it may take 20 times as long. It generates and judges 10,000 records several
/// times, so it runs only when <c>VERICAIRN_COST_TESTS</c> is 1, as continuous integration's cost step sets it on each platform; the
/// measured times are written to the test output.
/// </summary>
[Collection("status-performance")]
public sealed class StatusCostTests(ITestOutputHelper output)
{
    private const int Records = 10_000;
    private const string EnvironmentVariable = "VERICAIRN_COST_TESTS";
    private const double Ratio = 20;

    [Fact]
    public void StatusAtTenThousandRecordsTakesAtMostTwentyTimesReadingItsRecordsWithUnchangedAnswers()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(EnvironmentVariable) == "1", $"The 10,000-record status cost test runs when {EnvironmentVariable} is 1.");
        var generation = Stopwatch.StartNew();
        // About 12 records for each work order, under 20 goals that its work names, with an operation each and living documents over its
        // documents: the shapes whose repeated passes the profile found.
        using var estate = new GeneratedStatusEstate(815, goals: 20, operations: true, livingDocuments: true);
        output.WriteLine($"generated in {generation.Elapsed.TotalSeconds:F1} s");

        // The baseline: the record files read and parsed afresh, without any judgment, measured before and after the reads under the
        // same conditions, the faster counting.
        TimeSpan baseline = Parse(estate.Root, "baseline before");
        (StatusResponse first, TimeSpan firstElapsed) = Read(estate.Root, "first");
        Assert.False(first.HasErrors, "The generated estate's status must validate and evaluate without errors.");
        int records = first.Json.GetProperty("records").GetInt32();
        output.WriteLine($"records: {records}");
        Assert.InRange(records, Records, Records + 200);

        // A second read, by a runtime opened afresh, answers the same.
        (StatusResponse second, TimeSpan secondElapsed) = Read(estate.Root, "repeated");
        TimeSpan after = Parse(estate.Root, "baseline after");
        if (after < baseline) baseline = after;
        Assert.Equal(first.Json.GetRawText(), second.Json.GetRawText());
        Assert.Equal(first.Text, second.Text);

        // The record-derived views equal those of an independent judgment that shares nothing with the runtime: no parse cache, no
        // judgment kept from the opening, and no view derived for an earlier one.
        EstateModel independent = EstateLoader.Load(estate.Root);
        JsonObject expected = JsonNode.Parse(EstateStatus.RenderJson(independent, EstateChecks.Run(independent)))!.AsObject();
        JsonObject actual = JsonNode.Parse(first.Json.GetRawText())!.AsObject();
        foreach (string member in new[] { "workOrders", "assignments", "operations", "documents", "releases", "operatorRequests", "history", "records", "estate" })
        {
            Assert.True(JsonNode.DeepEquals(expected[member], actual[member]), $"Status's {member} differ from an independent judgment's.");
        }

        TimeSpan fastest = firstElapsed < secondElapsed ? firstElapsed : secondElapsed;
        output.WriteLine($"status against reading and parsing the records: {fastest.TotalMilliseconds / baseline.TotalMilliseconds:F2} times");
        Assert.True(fastest <= Ratio * baseline, $"Status at {records} records took {fastest.TotalSeconds:F1} s, beyond {Ratio:F0} times the {baseline.TotalSeconds:F2} s of reading and parsing its records.");
    }

    private TimeSpan Parse(string root, string run)
    {
        var clock = Stopwatch.StartNew();
        EstateModel model = EstateLoader.LoadIdentities(root, EstateLoader.EnumerateRecordFiles(root), null);
        clock.Stop();
        Assert.True(model.Records.Count >= Records);
        output.WriteLine($"{run}: records read and parsed, {clock.Elapsed.TotalMilliseconds:F0} ms");
        return clock.Elapsed;
    }

    private (StatusResponse Status, TimeSpan Elapsed) Read(string root, string run)
    {
        var clock = Stopwatch.StartNew();
        using EstateService service = EstateService.Open(root);
        TimeSpan opened = clock.Elapsed;
        // The opening's settlement recovery and its attention reconciliation share one judgment when neither stores a record.
        Assert.Equal(1, service.Session.Loads);
        StatusResponse status = service.Status((phase, elapsed) => output.WriteLine($"{run} {phase}: {elapsed.TotalMilliseconds:F0} ms"));
        clock.Stop();
        Assert.Equal(2, service.Session.Loads);
        output.WriteLine($"{run} opening: {opened.TotalMilliseconds:F0} ms; total: {clock.Elapsed.TotalMilliseconds:F0} ms");
        return (status, clock.Elapsed);
    }
}
