// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
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
        Assert.True(OperatorRequests.IsOptional(model, todo));
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
        Assert.False(OperatorRequests.IsOptional(model, stepped));
    }

    [Fact]
    public void ARequesterCannotTurnItsOwnRequiredRequestOptionalButTheOperatorItIsPutToCan()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope work = OperatorRequestTests.Work(estate);
        JsonObject body = OperatorRequestTests.Body(estate);
        RecordEnvelope required = estate.Add("operator-request", estate.BuilderId, body.ToJsonString(), work.Reference);
        body["optional"] = true;
        using JsonDocument none = JsonDocument.Parse("""{"type":"operator-request","max":0}""");

        // The requester's own revision is refused by the record rules, and counts for nothing: the request still holds.
        RecordEnvelope byRequester = estate.Add("operator-request", estate.BuilderId, body.ToJsonString(), work.Reference, required.Id);
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "operator-request.optional" && f.Location.StartsWith(byRequester.ExpectedPath, StringComparison.Ordinal));
        Assert.False(OperatorRequests.IsOptional(model, byRequester));
        Assert.Equal("not-met", GoalPredicates.EvaluatePredicate(model, none.RootElement).Value);
        Assert.Equal([byRequester.Id], OperatorRequests.WaitingOf(model, null, DateTimeOffset.UtcNow).Open.Select(r => r.Id));
    }

    [Fact]
    public void TheOperatorItIsPutToMarksARequiredRequestOptionalOnTheirWordAndNothingElse()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope work = OperatorRequestTests.Work(estate);
        JsonObject body = OperatorRequestTests.Body(estate);
        RecordEnvelope required = estate.Add("operator-request", estate.BuilderId, body.ToJsonString(), work.Reference);
        body["optional"] = true;
        using JsonDocument none = JsonDocument.Parse("""{"type":"operator-request","max":0}""");

        RecordEnvelope byOperator = estate.Add("operator-request", estate.OperatorId, body.ToJsonString(), work.Reference, required.Id);

        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.True(OperatorRequests.IsOptional(model, byOperator));
        Assert.Equal("met", GoalPredicates.EvaluatePredicate(model, none.RootElement).Value);
        Assert.Equal([byOperator.Id], OperatorRequests.WaitingOf(model, null, DateTimeOffset.UtcNow).Optional.Select(r => r.Id));
        // The requester's later revision keeps it optional; the operator may change only the mark.
        JsonObject reworded = JsonNode.Parse(body.ToJsonString())!.AsObject();
        reworded["why"] = "A clearer reason.";
        RecordEnvelope kept = estate.Add("operator-request", estate.BuilderId, reworded.ToJsonString(), work.Reference, byOperator.Id);
        Assert.False(estate.Load().Findings.HasErrors, string.Join('\n', estate.Load().Findings));
        Assert.True(OperatorRequests.IsOptional(estate.Load(), kept));
        reworded["why"] = "The operator's own reason.";
        RecordEnvelope widened = estate.Add("operator-request", estate.OperatorId, reworded.ToJsonString(), work.Reference, kept.Id);
        Assert.Contains(estate.Load().Findings, f => f.Code == "operator-request.revision" && f.Location.StartsWith(widened.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void TheProgressReportListsPossibleTodosInTheirOwnSectionAndAReportWithoutOneKeepsItsBytes()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope work = OperatorRequestTests.Work(estate);
        estate.Add("operator-request", estate.BuilderId, OperatorRequestTests.Body(estate, 1).ToJsonString(), work.Reference);
        EstateModel without = estate.Load();
        string text = ProgressReport.Render(without, null);
        string json = ProgressReport.RenderJson(without, null);
        Assert.DoesNotContain("Possible todo", text, StringComparison.Ordinal);
        Assert.Null(JsonNode.Parse(json)!["operator"]!["possibleTodos"]);

        JsonObject body = OperatorRequestTests.Body(estate, 2);
        body["optional"] = true;
        RecordEnvelope todo = estate.Add("operator-request", estate.BuilderId, body.ToJsonString(), work.Reference);
        EstateModel with = estate.Load();
        Assert.False(with.Findings.HasErrors, string.Join('\n', with.Findings));
        Assert.Equal(OperatorRequests.WaitingOf(with, null, DateTimeOffset.UtcNow).Optional, OperatorRequests.OpenOptional(with));
        Assert.Equal([todo.Id], OperatorRequests.OpenOptional(with).Select(r => r.Id));

        // The text gains the possible todos' section after the acceptances, as operator list and status show it, and nothing else.
        var section = new StringBuilder();
        OperatorRequests.AppendPossibleTodos(section, [todo], "  ");
        string listed = ProgressReport.Render(with, null);
        Assert.Equal(text + section, listed);
        Assert.Contains($"  Possible todo: Perform action 2 [operator-request {todo.Id}]\n", listed, StringComparison.Ordinal);
        Assert.Contains($"  Fetch its files and guide: vericairn operator fetch {todo.Id} --to <folder>\n", listed, StringComparison.Ordinal);

        // The JSON gains operator.possibleTodos, each as the request queue's optional member carries it, with its guide, and nothing else.
        JsonObject report = JsonNode.Parse(ProgressReport.RenderJson(with, null))!.AsObject();
        JsonObject item = Assert.IsType<JsonObject>(Assert.Single(report["operator"]!["possibleTodos"]!.AsArray()));
        Assert.Equal(todo.Id, item["revision"]!.GetValue<string>());
        Assert.Equal(todo.ComputedHash, item["hash"]!.GetValue<string>());
        Assert.Equal(OperatorRequests.Guide(todo, optional: true), item["guide"]!.GetValue<string>());
        report["operator"]!.AsObject().Remove("possibleTodos");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), report));
        Assert.Equal(ProgressReport.RenderJson(with, null), ProgressReport.RenderJson(estate.Load(), null));
    }

    [Fact]
    public void AProgressTargetListsPossibleTodosInItsPrivateFormAndOnlyCountsThemInItsPublicForm()
    {
        const string Marker = "vericairn progress-target status 0123456789abcdef";
        using var estate = new TestEstate(3);
        RecordEnvelope work = OperatorRequestTests.Work(estate);
        EstateModel without = estate.Load();
        Assert.DoesNotContain("Possible todos", ProgressReport.RenderTarget(without, ProgressReport.PrivateForm, null, Marker, estate.Clock).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Possible todos", ProgressReport.RenderTarget(without, ProgressReport.PublicForm, null, Marker, estate.Clock).Text, StringComparison.Ordinal);

        JsonObject body = OperatorRequestTests.Body(estate, 2);
        body["optional"] = true;
        RecordEnvelope todo = estate.Add("operator-request", estate.BuilderId, body.ToJsonString(), work.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));

        // The private form lists each with what it asks, whom and its recommendation, and carries the request and its operator.
        ProgressPayload inside = ProgressReport.RenderTarget(model, ProgressReport.PrivateForm, null, Marker, estate.Clock);
        Assert.Contains("- **Acceptances only the operator gives: 0**\n- **Possible todos: 1**, which hold no release's closing and no goal's evaluation\n"
            + "  - Perform action 2 (to Test Operator)\n    recommendation: Recommended action 2\n", inside.Text, StringComparison.Ordinal);
        Assert.Contains(new RecordReference(todo.Type, todo.Id, todo.ComputedHash), inside.Carried);
        Assert.Contains(inside.Carried, r => r.Id == estate.OperatorId);
        Assert.Null(ProgressReport.TargetRefusal(model, ProgressReport.PrivateForm, inside.Text, estate.Clock));

        // The public form counts them only: no text of a request, no principal, and it carries neither.
        ProgressPayload open = ProgressReport.RenderTarget(model, ProgressReport.PublicForm, null, Marker, estate.Clock);
        Assert.Contains("- Asks awaiting an answer: 0\n- Acceptances only the operator gives: 0\n- Possible todos: 1\n", open.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Perform action 2", open.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Recommended action 2", open.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Test Operator", open.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(open.Carried, r => r.Id == todo.Id || r.Type == "principal");
        Assert.Null(ProgressReport.TargetRefusal(model, ProgressReport.PublicForm, open.Text, estate.Clock));
    }
}
