// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

[CollectionDefinition("status-performance", DisableParallelization = true)]
public sealed class StatusPerformanceIsolation;

[Collection("status-performance")]
public sealed class StatusPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void LargeHistoryStatusStaysWithinBudgetAndRepeatedReadsAgree()
    {
        using var fixture = new GeneratedStatusEstate(2_000);
        using EstateService service = EstateService.Open(fixture.Root);
        StatusResponse first = Read("first");
        Assert.False(first.HasErrors, "Generated estate status must validate and evaluate successfully.");
        Assert.True(first.Json.GetProperty("records").GetInt32() >= 20_000);
        StatusResponse second = Read("repeated");
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(first.Json.GetRawText(), second.Json.GetRawText());
        Assert.Equal(first.HasErrors, second.HasErrors);

        StatusResponse Read(string run)
        {
            var clock = Stopwatch.StartNew();
            StatusResponse response = service.Status((phase, elapsed) =>
                output.WriteLine($"{run} {phase}: {elapsed.TotalMilliseconds:F1} ms"));
            clock.Stop();
            output.WriteLine($"{run} total: {clock.Elapsed.TotalMilliseconds:F1} ms");
            // A Release run takes about 4.6 seconds locally. Fifteen seconds gives the slower Windows CI runners
            // more than three times that allowance and leaves half the unchanged 30-second client deadline.
            // Include validation, checks and rendering; isolate this test from other collections' CPU and I/O load.
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"{run} status exceeded the 15-second budget: {clock.Elapsed}.");
            return response;
        }
    }
}

// History, not repeated copies of one status: parallel work, multiple attempts, exact reviews/acceptances,
// revised orders and documents, answered and waiting asks, and policies that inspect that history.
internal sealed class GeneratedStatusEstate : IDisposable
{
    private readonly PolicyFixture fixture = new();
    internal string Root => fixture.Estate.Root;

    internal GeneratedStatusEstate(int orders)
    {
        var documents = new RecordEnvelope?[32];
        for (int i = 0; i < orders; i++)
        {
            RecordEnvelope order = fixture.Add("work-order", new()
            {
                ["key"] = "TASK-" + i.ToString("D5", CultureInfo.InvariantCulture), ["kind"] = "ordinary", ["title"] = "Generated work",
                ["intent"] = "Exercise a growing history.", ["scope"] = new JsonArray("test fixtures"), ["exitQuestion"] = "Is the result ready?",
                ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("checked result"),
            });
            if (i % 5 == 0) order = fixture.Add("work-order", PolicyFixture.Body(order), supersedes: order.Id);
            fixture.Add("work-status", new() { ["status"] = i % 4 == 0 ? "planned" : "active", ["note"] = "Recorded work state." }, subject: order.Reference);
            RecordEnvelope attempt = fixture.BeginAttempt(order: order);
            fixture.Add("result", PolicyFixture.ResultBody(), builder: true, subject: attempt.Reference);
            RecordEnvelope next = fixture.Add("attempt", new()
            {
                ["startedAt"] = attempt.At, ["workspace"] = "generated repository", ["note"] = "Continue the work.",
            }, builder: true, subject: attempt.Subject);
            RecordEnvelope result = fixture.Add("result", PolicyFixture.ResultBody(), builder: true, subject: next.Reference);
            if (i % 3 != 0)
            {
                RecordEnvelope review = fixture.Review(result);
                if (i % 3 == 1) fixture.Add("acceptance", new()
                {
                    ["statement"] = "Accept the checked result.", ["basis"] = new JsonArray(review.Id), ["self"] = false,
                }, subject: result.Reference);
            }

            RecordEnvelope ask = fixture.Add("ask", new()
            {
                ["to"] = fixture.Estate.OperatorId, ["question"] = "Review the generated result?", ["recommendation"] = "Inspect the result.",
                ["consequence"] = "The work waits.", ["state"] = "open",
            }, builder: true, subject: order.Reference);
            JsonObject decision = fixture.DecisionBody();
            if (i % 20 != 0) decision["answers"] = new JsonArray(ask.Id);
            fixture.Add("decision", decision);

            int slot = i % documents.Length;
            string path = "docs/history-" + slot.ToString(CultureInfo.InvariantCulture) + ".md";
            string prose = "# Generated history\n\nRecorded change " + i.ToString(CultureInfo.InvariantCulture) + ".\n";
            fixture.Estate.WriteFile(path, prose);
            documents[slot] = fixture.Add("document-revision", new()
            {
                ["path"] = path, ["title"] = "Generated history", ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(prose)),
                ["bytes"] = Encoding.UTF8.GetByteCount(prose), ["mediaType"] = "text/markdown", ["revision"] = i / documents.Length + 1,
                ["summary"] = "Retain a document revision.",
            }, supersedes: documents[slot]?.Id);
        }

        foreach (string waiting in new[] { "planned-without-release", "assignment-without-attempt", "result-awaiting-review", "open-ask" })
        {
            JsonObject parameters = new() { ["class"] = waiting, ["maxAgeDays"] = 0 };
            if (waiting == "result-awaiting-review") parameters["reviewRequirement"] = "independent";
            JsonObject policy = fixture.PolicyBody(waiting);
            policy["level"] = "warn";
            policy["startsAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock.AddSeconds(2));
            policy["rules"] = new JsonArray(new JsonObject
            {
                ["key"] = "waiting", ["kind"] = FoundationPolicyKinds.Staleness, ["version"] = 1, ["parameters"] = parameters,
            });
            fixture.Policy(policy);
        }
    }

    public void Dispose() => fixture.Dispose();
}
