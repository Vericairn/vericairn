// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed partial class WorkOperations(CommandContext context)
{
    public int WorkBrief(string root, string key, bool json, CommunicationSelection? presentation = null, bool agentContext = false, long? maxBytes = null, IReadOnlyList<string>? searches = null) => context.WithApi(root, api =>
    {
        BriefResponse? brief = searches is { Count: > 0 }
            ? api.BriefContext(new BriefContextRequest(key, maxBytes, presentation?.Recipient, presentation?.Assignment, presentation?.Values("handoff"), searches))
            : agentContext || maxBytes is not null
            ? api.BriefContext(key, maxBytes, presentation?.Recipient, presentation?.Assignment, presentation?.Values("handoff"))
            : json ? api.Brief(key) : api.BriefFor(key, presentation?.Recipient, presentation?.Assignment, presentation?.Values("handoff"));
        if (brief is null)
        {
            return context.Fail($"No current work order has key or id {key}.");
        }

        context.Output.Write(json ? CanonicalJson.SerializePretty(brief.Json) : brief.Markdown);
        return brief.Problem is null ? Clean : Failed;
    });

    public int WorkAssign(string root, string key, string contributor, string by, string briefPath, string? note, bool json, string? repository = null) =>
        context.WithBody(briefPath, brief => context.WithApi(root, api => context.ReportOutcome(api.Assign(new AssignRequest(key, contributor, by, brief, note) { Repository = repository }), json)));

    public int WorkAttempt(string root, string assignmentId, string by, string workspace, string note, string? at, bool json) =>
        context.WithApi(root, api => context.ReportOutcome(api.Attempt(new AttemptRequest(assignmentId, by, workspace, note, at)), json));

    public int WorkResult(string root, string attemptId, string by, string bodyPath, string? expected, bool json) =>
        context.WithBody(bodyPath, body => context.WithApi(root, api => context.ReportOutcome(api.Result(new ResultRequest(attemptId, by, body, expected)), json)));

    public int WorkReview(string root, string resultId, string by, string bodyPath, string expected, bool json) =>
        context.WithBody(bodyPath, body => context.WithApi(root, api => context.ReportOutcome(api.ReviewResult(new ResultReviewRequest(resultId, by, body, expected)), json)));

    public int WorkAmend(string root, string assignmentId, string by, string bodyPath, bool json) =>
        context.WithBody(bodyPath, body => context.WithApi(root, api => context.ReportOutcome(api.Amend(new AmendRequest(assignmentId, by, body)), json)));

    /// <summary>Hands a step an agent holds over to a person, or from 0.9.4 to a principal of its role citing the lapse ask, and prints the stops asked of the agent's runs in flight.</summary>
    public int WorkTakeover(string root, string assignment, string by, string? contributor, string? note, bool json, string? basis = null) => context.WithApi(root, api =>
    {
        TakeoverResponse taken = api.WorkTakeover(new TakeoverRequest(assignment, by, contributor, note) { Basis = basis });
        if (!taken.Ok)
        {
            return taken.Problem is { Code: "rejected", Findings.Count: > 0 } rejected ? context.ReportOutcome(WriteOutcome.Fail(rejected), json) : context.Fail(taken.Message);
        }

        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(taken, ProtocolJson.Options));
            return Clean;
        }

        context.Output.WriteLine($"{taken.Assignment!.Path}  {taken.Assignment.Id}  {taken.Assignment.Hash}");
        context.Output.WriteLine("  " + taken.Message);
        return Clean;
    });

    /// <summary>From 0.9.4 (CODE-73): the overlap view, or a draft brief's preview; a named repository that is none fails.</summary>
    public int WorkOverlap(string root, string? briefPath, string? repository, bool json) => briefPath is null
        ? context.WithApi(root, api => ReportOverlap(api.WorkOverlap(repository), json), readOnly: true)
        : context.WithBody(briefPath, brief => context.WithApi(root, api => ReportOverlap(api.WorkOverlapPreview(new OverlapPreviewRequest(brief, repository)), json), readOnly: true));

    private int ReportOverlap(OverlapResponse response, bool json)
    {
        if (response.Json.ValueKind == JsonValueKind.Object && response.Json.TryGetProperty("problem", out JsonElement problem) && problem.ValueKind == JsonValueKind.String)
        {
            return context.Fail(problem.GetString()!);
        }

        context.Output.Write(json ? CanonicalJson.SerializePretty(response.Json) : response.Text);
        return Clean;
    }

    public int WorkObserve(string root, string key, bool json, CommunicationSelection? presentation = null) => context.WithApi(root, api =>
    {
        ObserveResponse? observe = api.Observe(key);
        if (observe is null)
        {
            return context.Fail($"No current work order has key or id {key}.");
        }

        context.Output.Write(json ? CanonicalJson.SerializePretty(observe.Json) : CommandPresentation.Protected(api, "result", key, observe.Text, presentation));
        return Clean;
    });
}
