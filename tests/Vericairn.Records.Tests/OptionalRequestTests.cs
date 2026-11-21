// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// OPS-22 (0.9.4): an operator request may be marked optional, a possible todo. It is listed in its own section with its guide,
/// recommendation and commands, never holds a goal's evaluation, and a run's reserved step is never marked optional.
/// </summary>
public sealed class OptionalRequestTests
{
    [Fact]
    public void AnOptionalRequestIsAPossibleTodoInItsOwnSectionWithItsGuideAndCommands()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope work = OperatorRequestTests.Work(estate);
        RecordEnvelope waiting = estate.Add("operator-request", estate.BuilderId, OperatorRequestTests.Body(estate, 1).ToJsonString(), work.Reference);
        JsonObject body = OperatorRequestTests.Body(estate, 2);
        body["optional"] = true;
        body["content"]!.AsArray().Add(new JsonObject { ["kind"] = "file", ["name"] = "post.md", ["hash"] = "sha256:" + new string('a', 64) });
        RecordEnvelope todo = estate.Add("operator-request", estate.BuilderId, body.ToJsonString(), work.Reference);

        EstateModel model = estate.Load();

        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        OperatorRequests.Waiting queue = OperatorRequests.WaitingOf(model, null, DateTimeOffset.UtcNow);
        Assert.Equal([waiting.Id], queue.Open.Select(r => r.Id));
        Assert.Equal([todo.Id], queue.Optional.Select(r => r.Id));
        Assert.True(OperatorRequests.IsOptional(todo));
        JsonObject derived = OperatorRequests.Derive(model);
        Assert.Equal(1, derived["waiting"]!.GetValue<int>());
        JsonObject listed = Assert.IsType<JsonObject>(Assert.Single(derived["optional"]!.AsArray()));
        Assert.Equal(todo.Id, listed["revision"]!.GetValue<string>());
        string guide = listed["guide"]!.GetValue<string>();
        Assert.StartsWith("# Perform action 2\n", guide, StringComparison.Ordinal);
        Assert.Contains("Recommendation: Recommended action 2", guide, StringComparison.Ordinal);
        Assert.Contains("Exact draft.", guide, StringComparison.Ordinal);
        Assert.Contains("the file post.md, beside this guide", guide, StringComparison.Ordinal);
        Assert.Contains($"vericairn operator done {todo.Id} --by {estate.OperatorId} --expected {todo.ComputedHash}", guide, StringComparison.Ordinal);
        string text = OperatorRequests.RenderText(model);
        Assert.Contains("Possible todos: 1 optional request, which hold no release's closing and no goal's evaluation", text, StringComparison.Ordinal);
        Assert.Contains($"vericairn operator fetch {todo.Id} --to <folder>", text, StringComparison.Ordinal);
        // The operator queue keeps it apart from what waits, with its guide and the commands that fetch and close it.
        JsonObject actions = OperatorActions.Derive(model);
        Assert.DoesNotContain(actions["items"]!.AsArray().OfType<JsonObject>(), i => i["id"]!.GetValue<string>().Contains(todo.Id, StringComparison.Ordinal));
        JsonObject possible = Assert.IsType<JsonObject>(Assert.Single(actions["possibleTodos"]!.AsArray()));
        Assert.Equal(["operator fetch", "operator done", "operator dismiss"], possible["commands"]!.AsArray().Select(c => string.Join(' ', c!["arguments"]!.AsArray().Take(2).Select(a => a!.GetValue<string>()))));
        Assert.Contains("Possible todos: 1 optional request", OperatorActions.RenderText(actions), StringComparison.Ordinal);
        // Status shows it in the operator requests' section.
        Assert.Contains("Possible todo: Perform action 2", EstateStatus.Render(model, new FindingList()), StringComparison.Ordinal);
    }

    [Fact]
    public void AQueueWithoutAnOptionalRequestKeepsItsEarlierShape()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope work = OperatorRequestTests.Work(estate);
        estate.Add("operator-request", estate.BuilderId, OperatorRequestTests.Body(estate).ToJsonString(), work.Reference);
        EstateModel model = estate.Load();

        Assert.Null(OperatorRequests.Derive(model)["optional"]);
        Assert.Null(OperatorActions.Derive(model)["possibleTodos"]);
        Assert.DoesNotContain("Possible todos", OperatorRequests.RenderText(model), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOptionalRequestNeverHoldsAGoalsEvaluation()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope work = OperatorRequestTests.Work(estate);
        JsonObject body = OperatorRequestTests.Body(estate);
        body["optional"] = true;
        estate.Add("operator-request", estate.BuilderId, body.ToJsonString(), work.Reference);
        using JsonDocument none = JsonDocument.Parse("""{"type":"operator-request","max":0}""");

        Assert.Equal("met", GoalPredicates.EvaluatePredicate(estate.Load(), none.RootElement).Value);
        estate.Add("operator-request", estate.BuilderId, OperatorRequestTests.Body(estate, 2).ToJsonString(), work.Reference);
        Assert.Equal("not-met", GoalPredicates.EvaluatePredicate(estate.Load(), none.RootElement).Value);
    }

    [Fact]
    public void ARunsReservedStepIsNeverMarkedOptionalAndTheMarkIsABoolean()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope work = OperatorRequestTests.Work(estate);
        JsonObject step = OperatorRequestTests.Body(estate);
        step["optional"] = true;
        step["flowStep"] = new JsonObject { ["goal"] = Identity.Ulid.NewUlid().Value, ["key"] = "announce" };
        RecordEnvelope stepped = estate.Add("operator-request", estate.BuilderId, step.ToJsonString(), work.Reference);
        JsonObject text = OperatorRequestTests.Body(estate, 2);
        text["optional"] = "yes";
        RecordEnvelope worded = estate.Add("operator-request", estate.BuilderId, text.ToJsonString(), work.Reference);

        EstateModel model = estate.Load();

        Assert.Contains(model.Findings, f => f.Code == "operator-request.optional" && f.Location.StartsWith(stepped.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(model.Findings, f => f.Code == "body.type" && f.Location.StartsWith(worded.ExpectedPath, StringComparison.Ordinal));
        Assert.False(OperatorRequests.IsOptional(stepped));
    }
}
