// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>Journeys J01 and J03 driven through the SDK against a running daemon: the same records, the same refusals, the same state language as the CLI.</summary>
[Collection("daemon")]
public class ApiJourneyTests
{
    private const string Brief = """{"outcome":"A widget that builds and passes its tests.","sources":[],"permittedChanges":["the widget sources and tests"],"prohibitedEffects":["no publication","no spending"],"workspace":["example repository, main branch"],"limits":["one working session"],"expectedOutputs":["widget.txt"],"reviewCriteria":["build passes","tests pass","output hash verified"],"recovery":"Record a result before stopping; whoever continues reads the brief and the last result."}""";

    public ApiJourneyTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public async Task J01_CreateAndDeliverALocalSoftwareProductThroughTheApi()
    {
        using var estate = new TestEstate();
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var api = new VericairnClient(DaemonHost.Discover(estate.Root)!);
        string op = estate.OperatorId;
        string builder = estate.BuilderId;

        string decision = Ok(api.NewRecord(new NewRecordRequest("decision", builder, Json($$"""{"title":"Standing delegation","statement":"Build and accept ordinary work yourself.","summary":"The builder engineers and self-accepts ordinary work.","synthesis":"Standing delegation for ordinary work; milestones and releases stay with the operator.","effects":["builder self-accepts ordinary work"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z"}""")))).Id;
        Ok(api.NewRecord(new NewRecordRequest("authority", op, Json($$"""{"grantee":"{{builder}}","scope":["local-engineering","self-accept-ordinary-work"],"reserved":["publication","spending"],"conditions":["self-review is labeled"],"basis":"{{decision}}"}"""))));
        Ok(api.NewRecord(new NewRecordRequest("work-order", builder, Json("""{"key":"W-2","kind":"ordinary","title":"Deliver the widget","intent":"Produce an identified, checked, accepted widget without any vendor account.","scope":["widget"],"exitQuestion":"Is the exact widget built, checked and accepted?","inputs":[],"deliverables":["widget.txt"]}"""))));
        string assignment = Ok(api.Assign(new AssignRequest("W-2", builder, builder, Json(Brief)))).Id;
        string attempt = Ok(api.Attempt(new AttemptRequest(assignment, builder, "example repository, main branch", "Build, test, record."))).Id;

        string widget = Path.Combine(estate.Root, "docs", "widget.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(widget)!);
        File.WriteAllText(widget, "widget\n");
        string hash = ContentHash.Sha256File(widget);

        // A wrong hash is refused with the finding; the output named must be the file present.
        WriteOutcome wrong = api.Result(new ResultRequest(attempt, builder, Json($$"""{"outcome":"completed","summary":"Built.","outputs":[{"name":"widget.txt","hash":"sha256:{{new string('0', 64)}}","location":"docs/widget.txt"}],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""")));
        Assert.False(wrong.Ok);
        Assert.Equal("rejected", wrong.Problem!.Code);
        Assert.Contains(wrong.Problem.Findings, f => f.Code == "result.output-hash");

        RecordWritten result = Ok(api.Result(new ResultRequest(attempt, builder, Json($$"""{"outcome":"completed","summary":"Built and tested the widget.","outputs":[{"name":"widget.txt","hash":"{{hash}}","bytes":7,"location":"docs/widget.txt"}],"evidence":[],"checks":[{"name":"build","outcome":"passed","summary":"Built cleanly."},{"name":"tests","outcome":"passed","summary":"All tests pass."}],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}"""))));

        // Only the builder may attempt the builder's assignment; the refusal carries the finding, not an exception.
        WriteOutcome notMine = api.Attempt(new AttemptRequest(assignment, op, "elsewhere", "Not mine."));
        Assert.Equal("rejected", notMine.Problem!.Code);
        Assert.Contains(notMine.Problem.Findings, f => f.Code == "attempt.contributor");

        Ok(api.NewRecord(new NewRecordRequest("acceptance", builder, Json("""{"statement":"Self-accepted the exact result under standing delegation.","basis":[],"self":true}"""), $"result:{result.Id}:{result.Hash}")));
        JsonElement order = api.Brief("W-2")!.Json.GetProperty("workOrder");
        Ok(api.NewRecord(new NewRecordRequest("acceptance", builder, Json("""{"statement":"Self-accepted: the exit question is answered yes by the accepted result.","basis":[],"self":true}"""), $"work-order:{order.GetProperty("id").GetString()}:{order.GetProperty("hash").GetString()}")));

        JsonElement status = api.Status().Json;
        Assert.Equal("accepted", status.GetProperty("workOrders").EnumerateArray().Single().GetProperty("status").GetString());
        Assert.Equal("accepted", status.GetProperty("assignments").EnumerateArray().Single().GetProperty("state").GetString());
        Assert.Contains("completed, accepted", api.Observe("W-2")!.Text, StringComparison.Ordinal);
        Assert.Equal(0, api.Check().Errors);

        // The embedded runtime, opened after the daemon stops, renders the identical status: nothing lived only in the daemon.
        await host.StopAsync();
        using var embedded = Runtime.Service.EstateService.Open(estate.Root);
        Assert.Equal(Records.Canonical.CanonicalJson.SerializePretty(status), Records.Canonical.CanonicalJson.SerializePretty(embedded.Status().Json));
    }

    [Fact]
    public async Task J03_ContinueWorkWithAnotherAgentThroughTheApi()
    {
        using var estate = new TestEstate();
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var api = new VericairnClient(DaemonHost.Discover(estate.Root)!);
        string op = estate.OperatorId;
        string builder = estate.BuilderId;
        string agentOne = Ok(api.NewRecord(new NewRecordRequest("principal", op, Json("""{"kind":"agent","name":"Agent One","roles":["contributor"],"agent":{"vendor":"Example","product":"Example Agent"}}""")))).Id;
        string agentTwo = Ok(api.NewRecord(new NewRecordRequest("principal", op, Json("""{"kind":"agent","name":"Agent Two","roles":["contributor"],"agent":{"vendor":"Example","product":"Example Agent"}}""")))).Id;
        Ok(api.NewRecord(new NewRecordRequest("work-order", builder, Json("""{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Prove that work transfers between contributors through records alone.","scope":["widget sources and tests"],"exitQuestion":"Does the widget build and pass its tests?","inputs":[],"deliverables":["widget.txt"]}"""))));

        string assignmentOne = Ok(api.Assign(new AssignRequest("W-1", agentOne, op, Json(Brief)))).Id;
        string attemptOne = Ok(api.Attempt(new AttemptRequest(assignmentOne, agentOne, "a clone of example at main", "Implement the widget."))).Id;
        Ok(api.Result(new ResultRequest(attemptOne, agentOne, Json("""{"outcome":"partial","summary":"Implemented half of the widget before the usage limit was reached.","outputs":[],"evidence":[],"checks":[{"name":"tests","outcome":"not-performed","summary":"Not run before the limit."}],"limitations":["Tests were not run."],"unresolvedEffects":["A publication to the example feed may have been dispatched; reconcile it under its original identity before any retry."],"nextStep":"Reconcile the possible dispatch, then run the tests and finish the widget."}"""))));

        // The brief carries everything the next contributor needs, and never a machine path.
        string brief = api.Brief("W-1")!.Markdown;
        Assert.Contains("partial, awaiting review", brief, StringComparison.Ordinal);
        Assert.Contains("unresolved effect: A publication to the example feed may have been dispatched", brief, StringComparison.Ordinal);
        Assert.Contains("next step: Reconcile the possible dispatch", brief, StringComparison.Ordinal);
        Assert.DoesNotContain(estate.Root, brief, StringComparison.Ordinal);

        string assignmentTwo = Ok(api.Assign(new AssignRequest("W-1", agentTwo, op, Json(Brief)))).Id;
        string attemptTwo = Ok(api.Attempt(new AttemptRequest(assignmentTwo, agentTwo, "a fresh clone of example at main", "Reconcile the possible dispatch, then finish the widget."))).Id;
        string widget = Path.Combine(estate.Root, "docs", "widget.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(widget)!);
        File.WriteAllText(widget, "widget\n");
        string hash = ContentHash.Sha256File(widget);
        RecordWritten resultTwo = Ok(api.Result(new ResultRequest(attemptTwo, agentTwo, Json($$"""{"outcome":"completed","summary":"Reconciled the dispatch (nothing had been published) and finished the widget.","outputs":[{"name":"widget.txt","hash":"{{hash}}","bytes":7,"location":"docs/widget.txt"}],"evidence":[{"summary":"Feed lookup showed no publication under the original operation identity."}],"checks":[{"name":"build","outcome":"passed","summary":"Built cleanly."},{"name":"tests","outcome":"passed","summary":"All tests pass."}],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}"""))));
        Ok(api.NewRecord(new NewRecordRequest("acceptance", op, Json("""{"statement":"Accepted the exact result after inspecting the output hash and the reconciliation evidence.","basis":[],"self":false}"""), $"result:{resultTwo.Id}:{resultTwo.Hash}")));

        Dictionary<string, (string Outcome, string State)> assignments = api.Status().Json.GetProperty("assignments").EnumerateArray()
            .ToDictionary(x => x.GetProperty("contributor").GetString()!, x => (x.GetProperty("outcome").GetString()!, x.GetProperty("state").GetString()!));
        Assert.Equal(("completed", "accepted"), assignments[agentTwo]);
        Assert.Equal(("partial", "awaiting review"), assignments[agentOne]);
        Assert.Contains("partial, awaiting review", api.Observe("W-1")!.Text, StringComparison.Ordinal);
        Assert.Equal(0, api.Check().Errors);
    }

    private static RecordWritten Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem is null ? "no problem reported" : outcome.Problem.Message + "\n" + string.Join('\n', outcome.Problem.Findings.Select(f => $"{f.Severity} {f.Code} {f.Location}: {f.Message}")));
        return outcome.Written!;
    }

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
