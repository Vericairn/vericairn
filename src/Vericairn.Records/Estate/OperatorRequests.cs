// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Types;

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

    /// <summary>
    /// All outcomes pinned to any revision of this request, including observed effects. From 0.9.4 (FIX-155), of a standing agent's
    /// publication request only those an operator who is a person recorded, neither its author nor its askedBy, close it, as the estate
    /// check requires of each (standing.own-output, standing.publication): an agent's closure, written by hand with any date, closes nothing.
    /// </summary>
    public static IEnumerable<RecordEnvelope> Closures(EstateModel model, RecordEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<string> chain = model.Chain(request);
        RecordEnvelope first = StandingOutputs.FirstOf(model, request);
        bool standing = StandingOutputs.Of(first) is not null;
        return model.CountingRecords.Where(r => r.Subject is { Type: "operator-request" } subject && chain.Contains(subject.Id) &&
            (r.Type == "operator-request-close" || r.Type == "observation" && EstateModel.BodyString(r, "kind") == "operator-request")
            && (!standing || (r.By != first.By && r.By != EstateModel.BodyString(first, "askedBy") && GoalRules.IsPerson(model, r.By) && model.HasRoleAt(r.By, Roles.Operator, r))));
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
    public sealed record Waiting(IReadOnlyList<RecordEnvelope> Open, IReadOnlyList<Held> Withdrawn)
    {
        /// <summary>From 0.9.4 (OPS-22): the open optional requests, possible todos in original request order, which wait on nobody and hold nothing.</summary>
        public IReadOnlyList<RecordEnvelope> Optional { get; init; } = [];
    }

    /// <summary>From 0.9.4 (OPS-22): whether a request is marked optional, a possible todo that never holds a release's closing or a goal's evaluation.</summary>
    public static bool IsOptional(RecordEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Type == "operator-request" && request.Body.ValueKind == System.Text.Json.JsonValueKind.Object
            && request.Body.TryGetProperty("optional", out System.Text.Json.JsonElement optional) && optional.ValueKind == System.Text.Json.JsonValueKind.True
            && FlowStops.StepOf(request) is null;
    }

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
        var optional = new List<RecordEnvelope>();
        var held = new List<Held>();
        foreach (RecordEnvelope request in model.Current("operator-request").Where(r => (to is null || EstateModel.BodyString(r, "to") == to) && !Closures(model, r).Any())
            .OrderBy(r => model.ById[model.Chain(r)[0]].At, StringComparer.Ordinal).ThenBy(r => model.Chain(r)[0], StringComparer.Ordinal))
        {
            if (FlowStops.StepOf(request) is not FlowStops.FlowStepReference step)
            {
                (IsOptional(request) ? optional : open).Add(request);
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

        return new Waiting(open, held) { Optional = optional };
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

        // From 0.9.4 (OPS-22) the optional requests are possible todos in their own member, each with its guide; a queue without one keeps its
        // earlier bytes.
        if (queue.Optional.Count > 0)
        {
            derived["optional"] = new JsonArray(queue.Optional.Select(r =>
            {
                JsonObject item = Item(model, r);
                item["guide"] = Guide(r);
                return (JsonNode)item;
            }).ToArray());
        }

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
        AppendPossibleTodos(text, queue.Optional, "  ");
        return text.ToString();
    }

    /// <summary>
    /// From 0.9.4 (OPS-22): the optional requests as possible todos, in their own section: each with its recommendation first, its guide's
    /// action and destination, its pinned files and the commands that fetch its kit and close it, done or dismissed, whenever the operator
    /// chooses. They hold nothing.
    /// </summary>
    public static void AppendPossibleTodos(StringBuilder text, IReadOnlyList<RecordEnvelope> optional, string indent)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(optional);
        if (optional.Count == 0) return;
        text.Append(indent).Append("Possible todos: ").Append(optional.Count.ToString(CultureInfo.InvariantCulture))
            .Append(optional.Count == 1 ? " optional request" : " optional requests").Append(", which hold no release's closing and no goal's evaluation\n");
        foreach (RecordEnvelope request in optional)
        {
            string to = EstateModel.BodyString(request, "to") ?? "<operator-id>";
            text.Append(indent).Append("  Recommendation: ").Append(EstateModel.BodyString(request, "recommendation")).Append('\n')
                .Append(indent).Append("  Possible todo: ").Append(EstateModel.BodyString(request, "action")).Append(" [operator-request ").Append(request.Id).Append("]\n")
                .Append(indent).Append("  Destination: ").Append(EstateModel.BodyString(request, "destination")).Append('\n');
            foreach (System.Text.Json.JsonElement item in TJson.Array(request.Body, "content").Where(c => TJson.Text(c, "kind") == "file"))
                text.Append(indent).Append("  File: ").Append(TJson.Text(item, "name")).Append(" at ").Append(TJson.Text(item, "hash")).Append('\n');
            text.Append(indent).Append("  Fetch its files and guide: vericairn operator fetch ").Append(request.Id).Append(" --to <folder>\n")
                .Append(indent).Append("  Done: vericairn operator done ").Append(request.Id).Append(" --by ").Append(to).Append(" --expected ").Append(request.ComputedHash)
                .Append(EstateModel.BodyString(request, "kind") == "publication" ? " --link <observed-destination>\n" : "\n")
                .Append(indent).Append("  Dismiss: vericairn operator dismiss ").Append(request.Id).Append(" --by ").Append(to).Append(" --expected ").Append(request.ComputedHash).Append(" --reason <your-reason>\n");
        }
    }

    /// <summary>
    /// From 0.9.4 (OPS-22): a request's guide, as vericairn operator fetch writes it beside the request's files: what it asks and why, the
    /// recommendation, where it lands, what waits while it stays open, each pinned item by its hash with its text or command verbatim and each
    /// file by its name, and the commands that close it. It reinterprets nothing.
    /// </summary>
    public static string Guide(RecordEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string to = EstateModel.BodyString(request, "to") ?? "<operator-id>";
        var text = new StringBuilder("# ").Append(EstateModel.BodyString(request, "action")).Append("\n\n")
            .Append("Operator request ").Append(request.Id).Append(" at ").Append(request.ComputedHash).Append(", put to ").Append(to)
            .Append(" by ").Append(EstateModel.BodyString(request, "askedBy")).Append(IsOptional(request) ? "; optional, a possible todo that holds nothing" : string.Empty).Append(".\n\n")
            .Append("Recommendation: ").Append(EstateModel.BodyString(request, "recommendation")).Append("\n\n")
            .Append("Why: ").Append(EstateModel.BodyString(request, "why")).Append("\n\n")
            .Append("Destination: ").Append(EstateModel.BodyString(request, "destination")).Append("\n\n")
            .Append("While it stays open: ").Append(EstateModel.BodyString(request, "consequence")).Append("\n\n")
            .Append("## Pinned content\n\n");
        foreach (System.Text.Json.JsonElement item in TJson.Array(request.Body, "content"))
        {
            string? kind = TJson.Text(item, "kind");
            text.Append("- ").Append(kind).Append(' ').Append(TJson.Text(item, "hash"));
            if (kind == "file") text.Append(": the file ").Append(TJson.Text(item, "name")).Append(", beside this guide\n");
            else text.Append(":\n\n```text\n").Append(TJson.Text(item, "text")).Append("\n```\n");
        }

        return text.Append("\n## Finishing it\n\n")
            .Append("Done: vericairn operator done ").Append(request.Id).Append(" --by ").Append(to).Append(" --expected ").Append(request.ComputedHash)
            .Append(EstateModel.BodyString(request, "kind") == "publication" ? " --link <observed-destination>\n" : "\n")
            .Append("Dismiss: vericairn operator dismiss ").Append(request.Id).Append(" --by ").Append(to).Append(" --expected ").Append(request.ComputedHash).Append(" --reason <your-reason>\n")
            .ToString();
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
