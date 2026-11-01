// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>A disposable estate with a reviewer step and an attempt, with no live storage or machine bindings.</summary>
public sealed class ProgressFixture : IDisposable
{
    public ProgressFixture(long format = 3)
    {
        Estate = new TestEstate(format);
        Order = Estate.Add("work-order", Estate.OperatorId, """{"key":"W-1","kind":"ordinary","role":"reviewer","title":"Review the widget","intent":"Read the widget.","scope":["widget"],"exitQuestion":"Reviewed?","inputs":[],"deliverables":["review"]}""");
        Roster = Estate.Add("roster", Estate.OperatorId, $$"""{"roles":[{"role":"reviewer","principals":["{{Estate.BuilderId}}"]},{"role":"builder","principals":["{{Estate.BuilderId}}"]}]}""");
        Goal = Estate.Add("goal", Estate.OperatorId, GoalEvaluationTests.Goal(Estate, $$"""{"policy":"given","approval":"sponsor-approves","workOrders":["{{Order.Id}}"]}"""));
        Assignment = Estate.Add("assignment", Estate.OperatorId, GoalEvaluationTests.Assignment(Estate.BuilderId), Order.Reference);
        Attempt = Estate.Add("attempt", Estate.BuilderId, """{"workspace":"widget repository, review branch","note":"Review the widget.","startedAt":"2026-10-13T12:00:00Z"}""", Assignment.Reference);
    }

    public TestEstate Estate { get; }
    public RecordEnvelope Order { get; }
    public RecordEnvelope Roster { get; private set; }
    public RecordEnvelope Goal { get; private set; }
    public RecordEnvelope Assignment { get; private set; }
    public RecordEnvelope Attempt { get; private set; }

    public RecordEnvelope Set(string scope, string? request)
    {
        RecordEnvelope current = scope switch
        {
            "estate" => Estate.Load().CurrentEstate!, "roster" => Roster, "goal" => Goal, "brief" => Assignment,
            _ => throw new ArgumentException("Unknown progress scope.", nameof(scope)),
        };
        JsonObject body = JsonNode.Parse(current.Body.GetRawText())!.AsObject();
        JsonObject target = scope == "roster" ? body["roles"]![0]!.AsObject() : scope == "brief" ? body["brief"]!.AsObject() : body;
        if (request is null) target.Remove("progressRequest");
        else target["progressRequest"] = JsonNode.Parse(request);
        RecordEnvelope changed = Estate.Add(current.Type, current.By, body.ToJsonString(), current.Subject, current.Id);
        if (scope == "roster") Roster = changed;
        if (scope == "goal") Goal = changed;
        if (scope == "brief")
        {
            Assignment = changed;
            Attempt = Estate.Add("attempt", Estate.BuilderId, Attempt.Body.GetRawText(), changed.Reference);
        }
        return changed;
    }

    public void Dispose() => Estate.Dispose();
}
