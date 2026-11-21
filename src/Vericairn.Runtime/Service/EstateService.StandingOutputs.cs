// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

/// <summary>
/// Standing outputs (from 0.9.4, GOAL-08; the estate's design/standing-agents.md §§ 8 to 10): the contributor of an occurrence's
/// assignment records each of its outputs as a possibility, an ask or a publication request, the runtime naming what raised it; and
/// work new --from-possibility adopts a possibility's draft work order, by a principal other than the agent, as one batch of the work
/// order and the possibility's adopting revision. The records' own rules hold whoever writes them, so these operations add convenience,
/// never authority.
/// </summary>
public sealed partial class EstateService
{
    /// <summary>The fields a standing agent's possibility takes from its author; the runtime names what raised it and that it is open.</summary>
    private static readonly string[] PossibilityFields = ["title", "idea", "origin", "fingerprint", "sources", "evidence", "reviewTriggers", "links", "draft", "disclosure", "extras", "raisesAgain"];

    /// <summary>The fields a standing agent's ask takes from its author.</summary>
    private static readonly string[] AskFields = ["to", "question", "recommendation", "consequence", "options"];

    /// <summary>The fields a standing agent's publication request takes from its author.</summary>
    private static readonly string[] PublicationFields = ["destination", "text", "why", "recommendation", "consequence"];

    /// <inheritdoc />
    public WriteOutcome StandingOutput(StandingOutputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Ulid.IsValid(request.By) || !Ulid.IsValid(request.Assignment))
        {
            return Refuse("The contributor and the assignment are named by their ULIDs.");
        }

        string[] fields = request.Kind switch
        {
            "possibility" => PossibilityFields,
            "ask" => AskFields,
            "publication" => PublicationFields,
            _ => [],
        };
        if (fields.Length == 0)
        {
            return Refuse($"A standing output is a possibility, an ask or a publication, not '{request.Kind}'.");
        }

        if (request.Body.ValueKind != JsonValueKind.Object)
        {
            return Refuse("A standing output's body is an object.");
        }

        if (request.Body.EnumerateObject().Select(p => p.Name).FirstOrDefault(n => Array.IndexOf(fields, n) < 0) is string unknown)
        {
            return Refuse($"A standing {request.Kind} takes {string.Join(", ", fields)}; '{unknown}' is not one of them: what raised it, its disposition and its state are the runtime's to name and another principal's to change.");
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            if (!model.ById.TryGetValue(request.Assignment, out RecordEnvelope? named) || named.Type != "assignment")
            {
                return NotFound($"No assignment has id {request.Assignment}.");
            }

            RecordEnvelope assignment = model.Latest(named);
            string? key = StandingAgents.KeyOf(assignment);
            RecordEnvelope? order = assignment.Subject is null || !model.ById.TryGetValue(assignment.Subject.Id, out RecordEnvelope? subject) ? null : model.Latest(subject);
            RecordEnvelope? goal = order is null ? null : StandingAgents.StandingOf(model, order);
            if (key is null || order is null || goal is null)
            {
                return Refuse($"Assignment {assignment.Id} carries no standing agent's occurrence: standing outputs are recorded on an occurrence's assignment.");
            }

            if (EstateModel.BodyString(assignment, "contributor") != request.By)
            {
                return Refuse($"Only the contributor of assignment {assignment.Id}, which carries occurrence {key}, records its outputs.");
            }

            IReadOnlyList<string> chain = model.Chain(assignment);
            AgentRuns.Run? run = AgentRuns.All(model).LastOrDefault(r => r.InFlight && r.Assignment is not null && chain.Contains(r.Assignment) && r.Principal == request.By);
            var raised = new JsonObject { ["goal"] = model.Chain(goal)[0], ["occurrence"] = key, ["assignment"] = assignment.Id };
            if (run is not null)
            {
                raised["run"] = run.Id;
            }

            JsonObject given = JsonNode.Parse(request.Body.GetRawText())!.AsObject();
            string title = EstateModel.BodyString(goal, "title") ?? goal.Id;
            JsonObject body;
            RecordReference? about = null;
            string type;
            switch (request.Kind)
            {
                case "possibility":
                    type = "possibility";
                    body = given;
                    body["origin"] ??= $"Standing agent {title}, occurrence {key}.";
                    body["disposition"] = "open";
                    body["raisedBy"] = raised;
                    break;
                case "ask":
                    type = "ask";
                    body = given;
                    body["to"] ??= Routing.Asked(model, goal, null, FlowClock.GetUtcNow());
                    body["goal"] = goal.Id;
                    body["state"] = "open";
                    body["raisedBy"] = raised;
                    break;
                default:
                    type = "operator-request";
                    string? text = given["text"]?.GetValue<string>();
                    string? to = Routing.Asked(model, goal, null, FlowClock.GetUtcNow()) is string decider && model.HasRole(decider, Vericairn.Records.Types.Roles.Operator) && GoalRules.IsPerson(model, decider)
                        ? decider : model.RootOperator;
                    if (text is null || to is null)
                    {
                        return Refuse(text is null ? "A standing publication carries its exact text." : "The estate names no operator to put a publication request to.");
                    }

                    body = new JsonObject
                    {
                        ["to"] = to,
                        ["askedBy"] = request.By,
                        ["action"] = $"Publish an agent's draft, from standing agent {title}'s occurrence {key}",
                        ["why"] = given["why"]?.DeepClone(),
                        ["recommendation"] = given["recommendation"]?.DeepClone(),
                        ["consequence"] = given["consequence"]?.DeepClone(),
                        ["kind"] = "publication",
                        ["destination"] = given["destination"]?.DeepClone(),
                        ["content"] = new JsonArray(new JsonObject { ["kind"] = "text", ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(text)), ["text"] = text }),
                        ["raisedBy"] = raised,
                    };
                    about = order.Reference;
                    break;
            }

            return WriteLocked(new RecordDraft(type, request.By, Element(body), about), model);
        }
    }

    /// <summary>
    /// work new --from-possibility: the work order from the possibility's draft, the request's own fields winning, with the possibility
    /// among its inputs, and the possibility's revision adopting it as that work order, judged together and stored as one batch. Its author
    /// is refused, whoever raised it; the possibility's rules refuse an adoption by an agent that carried its standing agent's occurrences,
    /// and of a held finding by anyone but an operator who is a person; and a held finding's work order is held from disclosure.
    /// </summary>
    private WriteOutcome WorkNewFromPossibility(WorkNewRequest request, JsonObject body)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            if (!model.ById.TryGetValue(request.FromPossibility!, out RecordEnvelope? named) || named.Type != "possibility")
            {
                return NotFound($"No possibility has id {request.FromPossibility}.");
            }

            RecordEnvelope possibility = model.Latest(named);
            if (EstateModel.BodyString(possibility, "disposition") != "open")
            {
                return Refuse($"Possibility {possibility.Id} is {EstateModel.BodyString(possibility, "disposition")}: only an open possibility is adopted.");
            }

            // Whoever raised it, a standing agent or not, a principal other than its author adopts it (design/standing-agents.md § 8).
            RecordEnvelope raiser = StandingOutputs.FirstOf(model, possibility);
            if (raiser.By == request.By)
            {
                string own = $"Possibility {raiser.Id} ({EstateModel.BodyString(raiser, "title")}) was raised by {request.By}: a principal other than its author adopts it by work new --from-possibility.";
                return WriteOutcome.Fail(new Problem("rejected", "Not written: " + own, [new FindingDto("error", "standing.own-output", raiser.Id, own)]));
            }

            JsonElement draft = possibility.Body.TryGetProperty("draft", out JsonElement d) && d.ValueKind == JsonValueKind.Object ? d : default;
            foreach (string field in new[] { "key", "title", "intent", "exitQuestion" })
            {
                if (body[field] is null && draft.ValueKind == JsonValueKind.Object && draft.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                {
                    body[field] = value.GetString();
                }
            }

            foreach (string field in new[] { "scope", "deliverables" })
            {
                if (body[field] is JsonArray { Count: 0 } && draft.ValueKind == JsonValueKind.Object && draft.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.Array)
                {
                    body[field] = JsonNode.Parse(value.GetRawText());
                }
            }

            // A held finding's work order is held from disclosure too, so the checked push keeps its key and text out of public repositories
            // until the release that carries it publishes (design/release-lines.md § 10).
            if (EstateModel.BodyString(possibility, "disclosure") == "held")
            {
                body["disclosure"] = "held";
            }

            JsonArray inputs = body["inputs"]!.AsArray();
            string first = model.Chain(possibility)[0];
            if (!inputs.Any(i => i?["type"]?.GetValue<string>() == "possibility"))
            {
                inputs.Add(new JsonObject { ["type"] = "possibility", ["id"] = first });
            }

            (AuthoredRecord? order, string? refusal) = AuthorLocked(new RecordDraft("work-order", request.By, Element(body)), model);
            if (order is null)
            {
                return Refuse(refusal!);
            }

            JsonObject adopting = JsonNode.Parse(possibility.Body.GetRawText())!.AsObject();
            adopting["disposition"] = "adopted";
            adopting["reason"] = $"Adopted as work order {EstateModel.BodyString(order.Envelope, "key") ?? order.Envelope.Id} by work new --from-possibility.";
            adopting["adoptedAs"] = new JsonObject { ["type"] = "work-order", ["id"] = order.Envelope.Id, ["hash"] = order.Envelope.ComputedHash };
            (AuthoredRecord? adopted, refusal) = AuthorLocked(new RecordDraft("possibility", request.By, Element(adopting), Supersedes: possibility.Id), model);
            if (adopted is null)
            {
                return Refuse(refusal!);
            }

            (IReadOnlyList<RecordWritten> written, Problem? problem) = StoreTogetherLocked([order, adopted], "Create a work order from a possibility and record its adoption.", null);
            RecordWritten? created = written.FirstOrDefault(w => w.Id == order.Envelope.Id);
            return problem is null ? WriteOutcome.Success(created!) : new WriteOutcome(written.Count == 0 ? null : written[^1], problem);
        }
    }
}
