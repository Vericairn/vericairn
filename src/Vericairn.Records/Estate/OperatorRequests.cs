// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// The single record-derived queue of operator requests; reads never close a request. A request for a reserved step of a run is
/// delivered only while it is due: while its step waits on the operator and it carries exactly the command the records give now
/// (the estate's design/release-procedure.md § 6). One whose step is done is settled, and one whose step no longer waits on it, or
/// whose command the records no longer give, is withdrawn; neither is delivered, and each stays open until the operator closes it.
/// </summary>
public static class OperatorRequests
{
    /// <summary>At most three requests are presented in one delivery.</summary>
    public const int PerDelivery = 3;

    /// <summary>All outcomes pinned to any revision of this request, including observed effects.</summary>
    public static IEnumerable<RecordEnvelope> Closures(EstateModel model, RecordEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<string> chain = model.Chain(request);
        return model.Records.Where(r => r.Subject is { Type: "operator-request" } subject && chain.Contains(subject.Id) &&
            (r.Type == "operator-request-close" || r.Type == "observation" && EstateModel.BodyString(r, "kind") == "operator-request"));
    }

    /// <summary>The current request that carries an ask, if any. The original ask remains readable and unanswered.</summary>
    public static RecordEnvelope? Carried(EstateModel model, RecordEnvelope ask)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(ask);
        IReadOnlyList<string> chain = model.Chain(ask);
        return model.Current("operator-request").FirstOrDefault(r => EstateModel.BodyString(r, "sourceAsk") is string id && chain.Contains(id));
    }

    /// <summary>An open request that is not delivered: a run's request whose step is done (settled) or no longer waits on it (withdrawn).</summary>
    /// <param name="Request">The request's current revision.</param>
    /// <param name="State">settled or withdrawn.</param>
    /// <param name="Reason">Why, in words.</param>
    public sealed record Held(RecordEnvelope Request, string State, string Reason);

    /// <summary>The open requests: those delivered in original request order, and those not delivered, each with why.</summary>
    /// <param name="Open">The open requests that wait on the operator, in original request order.</param>
    /// <param name="Withdrawn">The open requests of runs that no longer wait on the operator, in original request order.</param>
    public sealed record Waiting(IReadOnlyList<RecordEnvelope> Open, IReadOnlyList<Held> Withdrawn);

    /// <summary>Every open request that waits on the operator, in original request order, optionally addressed to one operator.</summary>
    public static IReadOnlyList<RecordEnvelope> Open(EstateModel model, string? to = null) => WaitingOf(model, to, DateTimeOffset.UtcNow).Open;

    /// <summary>
    /// The open requests at a time, optionally addressed to one operator: a run's request for a reserved step waits on the operator only
    /// while it is due, which the run's position at that time decides; every other open request waits.
    /// </summary>
    public static Waiting WaitingOf(EstateModel model, string? to, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        var positions = new Dictionary<string, FlowPosition.Position?>(StringComparer.Ordinal);
        var states = new Dictionary<string, FlowStops.StepRequest>(StringComparer.Ordinal);
        var open = new List<RecordEnvelope>();
        var held = new List<Held>();
        foreach (RecordEnvelope request in model.Current("operator-request").Where(r => (to is null || EstateModel.BodyString(r, "to") == to) && !Closures(model, r).Any())
            .OrderBy(r => model.ById[model.Chain(r)[0]].At, StringComparer.Ordinal).ThenBy(r => model.Chain(r)[0], StringComparer.Ordinal))
        {
            if (FlowStops.StepOf(request) is not FlowStops.FlowStepReference step)
            {
                open.Add(request);
                continue;
            }

            if (!positions.TryGetValue(step.Goal, out FlowPosition.Position? position))
            {
                position = GoalView.Find(model, step.Goal) is RecordEnvelope goal && FlowRuns.IsRun(goal) ? FlowPosition.Derive(model, goal, at) : null;
                positions[step.Goal] = position;
            }

            if (position is null)
            {
                held.Add(new Held(request, "withdrawn", $"goal {step.Goal} is not a run of a flow"));
                continue;
            }

            if (!states.ContainsKey(request.Id))
            {
                foreach (FlowStops.StepRequest classified in FlowStops.Classify(model, position, step.Step))
                {
                    states[classified.Request.Id] = classified;
                }
            }

            FlowStops.StepRequest state = states[request.Id];
            if (state.State == "due")
            {
                open.Add(request);
            }
            else
            {
                held.Add(new Held(request, state.State, state.Reason));
            }
        }

        return new Waiting(open, held);
    }

    /// <summary>Complete queue membership with three delivered bodies and the rest identified in order, and the open requests a run no longer waits on.</summary>
    public static JsonObject Derive(EstateModel model, string? to = null) => Derive(model, to, DateTimeOffset.UtcNow);

    /// <summary>Complete queue membership at a time.</summary>
    public static JsonObject Derive(EstateModel model, string? to, DateTimeOffset at)
    {
        Waiting queue = WaitingOf(model, to, at);
        IReadOnlyList<RecordEnvelope> open = queue.Open;
        var derived = new JsonObject
        {
            ["format"] = "vericairn.operator-requests/1", ["perDelivery"] = PerDelivery, ["to"] = to, ["waiting"] = open.Count,
            ["delivered"] = new JsonArray(open.Take(PerDelivery).Select(r => (JsonNode)Item(model, r)).ToArray()),
            ["queued"] = new JsonArray(open.Skip(PerDelivery).Select(r => (JsonNode)new JsonObject
            {
                ["id"] = model.Chain(r)[0], ["revision"] = r.Id, ["hash"] = r.ComputedHash,
            }).ToArray()),
        };

        // Only a run's open request that is no longer delivered adds a member, so a queue without one keeps its earlier bytes.
        if (queue.Withdrawn.Count > 0)
        {
            derived["withdrawn"] = new JsonArray(queue.Withdrawn.Select(h => (JsonNode)new JsonObject
            {
                ["id"] = model.Chain(h.Request)[0], ["revision"] = h.Request.Id, ["hash"] = h.Request.ComputedHash, ["state"] = h.State, ["reason"] = h.Reason,
            }).ToArray());
        }

        return derived;
    }

    /// <summary>The recommendation first, with every remaining identity visible behind the delivery.</summary>
    public static string RenderText(EstateModel model, string? to = null) => RenderText(model, to, DateTimeOffset.UtcNow);

    /// <summary>The queue as text at a time.</summary>
    public static string RenderText(EstateModel model, string? to, DateTimeOffset at)
    {
        Waiting queue = WaitingOf(model, to, at);
        IReadOnlyList<RecordEnvelope> open = queue.Open;
        var text = new StringBuilder("Operator requests: ").Append(open.Count.ToString(CultureInfo.InvariantCulture)).Append(" open\n");
        foreach (RecordEnvelope request in open.Take(PerDelivery))
        {
            string? command = FlowStops.StepOf(request) is null ? null : FlowStops.CommandOf(request);
            if (command is not null)
            {
                text.Append("  A run's reserved step; its command, checked against the records: ").Append(command).Append('\n')
                    .Append("  The requester's words, not checked:\n");
            }

            text.Append("  Recommendation: ").Append(EstateModel.BodyString(request, "recommendation")).Append('\n')
                .Append("  Action: ").Append(EstateModel.BodyString(request, "action")).Append(" [operator-request ").Append(request.Id).Append("]\n")
                .Append("  Why: ").Append(EstateModel.BodyString(request, "why")).Append('\n')
                .Append("  Destination: ").Append(EstateModel.BodyString(request, "destination")).Append('\n')
                .Append("  If unanswered: ").Append(EstateModel.BodyString(request, "consequence")).Append('\n')
                .Append("  Asked by: ").Append(EstateModel.BodyString(request, "askedBy")).Append("; to: ").Append(EstateModel.BodyString(request, "to")).Append('\n')
                .Append("  Inspect exact content: vericairn records show ").Append(request.Id).Append('\n')
                .Append("  Done: vericairn operator done ").Append(request.Id).Append(" --by ").Append(EstateModel.BodyString(request, "to"))
                .Append(" --expected ").Append(request.ComputedHash)
                .Append(EstateModel.BodyString(request, "kind") == "publication" ? " --link <observed-destination>\n" : "\n")
                .Append("  Dismiss: vericairn operator dismiss ").Append(request.Id).Append(" --by ").Append(EstateModel.BodyString(request, "to"))
                .Append(" --expected ").Append(request.ComputedHash).Append(" --reason <your-reason>\n");
        }
        if (open.Count > PerDelivery)
            text.Append("  Waiting in order: ").Append(string.Join(", ", open.Skip(PerDelivery).Select(r => r.Id))).Append("; each follows when an earlier request closes.\n");
        foreach (Held request in queue.Withdrawn)
            text.Append("  Not delivered, ").Append(request.State).Append(": operator-request ").Append(request.Request.Id).Append(", ").Append(request.Reason)
                .Append("; it stays open until its operator closes it with vericairn operator done or dismiss.\n");
        return text.ToString();
    }

    /// <summary>An exact request and its recorded body, without reinterpreting its content.</summary>
    public static JsonObject Item(EstateModel model, RecordEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        return new JsonObject
        {
            ["id"] = model.Chain(request)[0], ["revision"] = request.Id, ["hash"] = request.ComputedHash,
            ["by"] = request.By, ["at"] = request.At, ["body"] = JsonNode.Parse(request.Body.GetRawText()),
            ["subject"] = request.Subject is null ? null : new JsonObject { ["type"] = request.Subject.Type, ["id"] = request.Subject.Id, ["hash"] = request.Subject.Hash },
        };
    }
}
