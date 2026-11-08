// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Records.Updates;

namespace Vericairn.Records.Estate;

/// <summary>A complete, read-only queue of recorded operator actions. Commands describe record authoring; reading never signs or dispatches.</summary>
public static class OperatorActions
{
    /// <summary>The versioned machine representation.</summary>
    public const string Format = "vericairn.operator-actions/1";

    /// <summary>Derive every waiting item from one record snapshot, without inspecting a device or a provider, a run's requests delivered now.</summary>
    public static JsonObject Derive(EstateModel model) => Derive(model, DateTimeOffset.UtcNow);

    /// <summary>Every waiting item, a run's operator requests delivered as of a time, the reading runtime's clock.</summary>
    public static JsonObject Derive(EstateModel model, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        var items = new JsonArray();
        List<string> operators = model.Current("principal").Where(p => model.HasRole(p.Id, Roles.Operator))
            .Select(p => model.Chain(p)[0]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        Dictionary<string, string> states = EstateStatus.WorkItems(model).ToDictionary(w => w.Id, w => w.Status, StringComparer.Ordinal);

        AddRequests(model, items, at);
        AddAsks(model, items);
        AddAcceptances(model, items, operators, states);
        AddPublications(model, items, operators);
        AddObligations(model, items);
        AddReservedWork(model, items, states);
        AddAdoption(model, items);

        // An invalid estate cannot provide an actionable authority claim. Keep its queue visible, but offer inspection only.
        if (model.Findings.HasErrors)
        {
            foreach (JsonObject item in items.OfType<JsonObject>())
            {
                item["commands"] = new JsonArray();
                item["waitsOn"]!.AsArray().Add("Resolve the estate's record errors before acting.");
            }
        }

        return new JsonObject
        {
            ["format"] = Format,
            ["estate"] = model.CurrentEstate?.Id,
            ["items"] = items,
            ["hasErrors"] = model.Findings.HasErrors,
            ["findings"] = new JsonArray(model.Findings.Sorted().Where(f => f.Severity != Severity.Info).Select(f => (JsonNode)new JsonObject
            {
                ["severity"] = f.Severity.ToString().ToLowerInvariant(), ["code"] = f.Code, ["location"] = f.Location, ["message"] = f.Message,
            }).ToArray()),
        };
    }

    /// <summary>The queue as canonical JSON; presentation preferences never remove an item or a command.</summary>
    public static string RenderJson(JsonObject report)
    {
        ArgumentNullException.ThrowIfNull(report);
        using JsonDocument document = JsonDocument.Parse(report.ToJsonString());
        return CanonicalJson.SerializePretty(document.RootElement);
    }

    /// <summary>Every item, with its reason, dependencies, exact command and required standard input, even in a short presentation.</summary>
    public static string RenderText(JsonObject report)
    {
        ArgumentNullException.ThrowIfNull(report);
        JsonArray items = report["items"]!.AsArray();
        var text = new StringBuilder("What waits on the operator: ").Append(items.Count.ToString(CultureInfo.InvariantCulture)).Append(" items\n");
        int requests = 0;
        foreach (JsonObject item in items.OfType<JsonObject>())
        {
            if (item["kind"]!.GetValue<string>() == "operator-request")
            {
                if (++requests > OperatorRequests.PerDelivery)
                {
                    text.Append("  Request waiting in order: ").Append(item["id"]!.GetValue<string>()).Append('\n');
                    continue;
                }
                if (item["stepCommand"] is JsonNode stepCommand)
                {
                    text.Append("\nA run's reserved step; its command, checked against the records: ").Append(stepCommand.GetValue<string>()).Append('\n')
                        .Append("  The requester's words, not checked:\n");
                }

                text.Append(item["stepCommand"] is null ? "\nRecommendation: " : "  Recommendation: ").Append(item["recommendation"]!.GetValue<string>()).Append('\n')
                    .Append("  Action: ").Append(item["title"]!.GetValue<string>()).Append('\n')
                    .Append("  Why: ").Append(item["why"]!.GetValue<string>()).Append('\n')
                    .Append("  Destination: ").Append(item["destination"]!.GetValue<string>()).Append('\n')
                    .Append("  Asked by: ").Append(item["askedBy"]!.GetValue<string>()).Append("; to: ").Append(item["to"]!.GetValue<string>()).Append('\n')
                    .Append("  If unanswered: ").Append(item["consequence"]!.GetValue<string>()).Append('\n')
                    .Append("  Inspect: ").Append(item["inspect"]!.GetValue<string>()).Append('\n');
                foreach (JsonObject command in item["commands"]!.AsArray().OfType<JsonObject>())
                    text.Append("  Command: ").Append(command["command"]!.GetValue<string>()).Append('\n');
                foreach (JsonNode? wait in item["waitsOn"]!.AsArray()) text.Append("  Waits on: ").Append(wait!.GetValue<string>()).Append('\n');
                continue;
            }
            text.Append('\n').Append(item["kind"]!.GetValue<string>()).Append(": ").Append(item["title"]!.GetValue<string>()).Append('\n');
            if (item["recommendation"] is JsonNode recommendation) text.Append("  Recommendation: ").Append(recommendation.GetValue<string>()).Append('\n');
            text.Append("  Why: ").Append(item["why"]!.GetValue<string>()).Append(" State: ").Append(item["state"]!.GetValue<string>()).Append(".\n");
            if (item["consequence"] is JsonNode consequence) text.Append("  If unanswered: ").Append(consequence.GetValue<string>()).Append('\n');
            foreach (JsonNode? wait in item["waitsOn"]!.AsArray()) text.Append("  Waits on: ").Append(wait!.GetValue<string>()).Append('\n');
            foreach (JsonObject reference in item["references"]!.AsArray().OfType<JsonObject>())
                text.Append("  Source: ").Append(reference["type"]!.GetValue<string>()).Append(' ').Append(reference["id"]!.GetValue<string>()).Append(" at ").Append(reference["hash"]!.GetValue<string>()).Append('\n');
            text.Append("  Inspect: ").Append(item["inspect"]!.GetValue<string>()).Append('\n');
            foreach (JsonObject command in item["commands"]!.AsArray().OfType<JsonObject>())
            {
                text.Append("  Command: ").Append(command["command"]!.GetValue<string>()).Append('\n');
                if (command["oneStep"]?["command"] is JsonNode oneStep)
                    text.Append("  Record and merge: ").Append(oneStep.GetValue<string>()).Append('\n');
                text.Append("    ").Append(command["purpose"]!.GetValue<string>()).Append('\n');
                text.Append("    Supply JSON on standard input; fill every null with your own words or evidence:\n    ").Append(command["body"]!.ToJsonString()).Append('\n');
            }
        }

        foreach (JsonObject finding in report["findings"]!.AsArray().OfType<JsonObject>())
            text.Append('\n').Append(finding["severity"]!.GetValue<string>()).Append(' ').Append(finding["code"]!.GetValue<string>()).Append(": ").Append(finding["message"]!.GetValue<string>()).Append('\n');
        return text.Append("\nUse these commands in this estate. Each --by names the principal who must act on their own device. Reading records nothing; required input is not an answer or an acceptance.\n").ToString();
    }

    private static void AddAsks(EstateModel model, JsonArray items)
    {
        foreach (Asks.Delivery delivery in Asks.Deliveries(model).Where(d => model.HasRole(d.Principal, Roles.Operator)))
        {
            // Include the whole queue, including asks beyond the three-item notification delivery.
            foreach (RecordEnvelope ask in delivery.Asks)
            {
                JsonObject item = Item("ask", ask, EstateModel.BodyString(ask, "question")!, Asks.State(model, ask).State,
                    "The operator it is addressed to has not answered this ask.", [$"A decision by {delivery.Principal} naming this ask in answers."], [ask]);
                item["recommendation"] = EstateModel.BodyString(ask, "recommendation");
                item["consequence"] = EstateModel.BodyString(ask, "consequence");
                AddCommand(item, "decision", delivery.Principal, null, new JsonObject
                {
                    ["title"] = null, ["statement"] = null, ["summary"] = null, ["synthesis"] = null,
                    ["effects"] = new JsonArray((JsonNode?)null), ["affects"] = new JsonArray(),
                    ["decidedBy"] = delivery.Principal, ["decidedAt"] = null, ["stake"] = null,
                    ["notAuthorized"] = new JsonArray(), ["answers"] = new JsonArray(ask.Id),
                }, "Record your decision, including its actual time and stake; the recommendation is not your answer.");
                items.Add(item);
            }
        }
    }

    private static void AddRequests(EstateModel model, JsonArray items, DateTimeOffset at)
    {
        foreach (RecordEnvelope request in OperatorRequests.WaitingOf(model, null, at).Open)
        {
            string to = EstateModel.BodyString(request, "to") ?? "?";
            JsonObject item = Item("operator-request", request, EstateModel.BodyString(request, "action") ?? "Request", "open",
                EstateModel.BodyString(request, "why") ?? "Unknown", [], [request]);
            foreach (string field in new[] { "recommendation", "consequence", "destination", "askedBy", "to" }) item[field] = EstateModel.BodyString(request, field) ?? "unknown";
            // A run's step request is delivered only while its command is the records' own; its prose is the requester's, unchecked.
            if (FlowStops.StepOf(request) is not null)
            {
                item["stepCommand"] = FlowStops.CommandOf(request);
            }

            item["request"] = OperatorRequests.Item(model, request);
            foreach (string action in new[] { "done", "dismiss" })
            {
                List<string> arguments = ["operator", action, request.Id, "--by", to, "--expected", request.ComputedHash];
                if (action == "dismiss") arguments.AddRange(["--reason", "<your-reason>"]);
                else if (EstateModel.BodyString(request, "kind") == "publication") arguments.AddRange(["--link", "<observed-destination>"]);
                item["commands"]!.AsArray().Add(new JsonObject
                {
                    ["by"] = to, ["command"] = "vericairn " + string.Join(' ', arguments), ["arguments"] = Strings(arguments),
                    ["purpose"] = "Record one signed outcome; replace any placeholder with your words or observed destination. No external action is dispatched.",
                });
            }
            items.Add(item);
        }
    }

    private static void AddAcceptances(EstateModel model, JsonArray items, List<string> operators, Dictionary<string, string> states)
    {
        foreach (RecordEnvelope release in Ordered(model.Current("release")).Where(r => EstateModel.BodyString(r, "status") == "candidate" && !Accepted(model, r)))
            items.Add(Acceptance("release-acceptance", release, "Release " + EstateModel.BodyString(release, "version"), "candidate",
                "Publication requires the operator's acceptance of this exact candidate.", operators, [], [release]));

        foreach (RecordEnvelope order in Ordered(model.Current("work-order")).Where(o => EstateModel.BodyString(o, "kind") == "milestone"
            && states.GetValueOrDefault(o.Id) is "submitted" or "accepted (earlier revision)"))
        {
            RecordEnvelope? basis = EstateStatus.StatusRecord(model, order);
            items.Add(Acceptance("milestone-acceptance", order, WorkTitle(order), states[order.Id],
                "A submitted milestone, or a changed revision of an accepted milestone, needs the operator's acceptance at its exact hash.", operators, [], basis is null ? [order] : [order, basis]));
        }

        foreach (WorkView.AssignmentItem assignment in WorkView.AssignmentItems(model).Where(a => a.LatestResultId is not null && a.State is "awaiting review" or "accepted (earlier revision)"))
        {
            RecordEnvelope assigned = model.ById[assignment.AssignmentId];
            RecordEnvelope? order = WorkView.OrderOfAssignment(model, assigned);
            if (order is null || EstateModel.BodyString(order, "kind") != "milestone" || states.GetValueOrDefault(model.Latest(order).Id) is "accepted" or "retired") continue;
            RecordEnvelope result = model.ById[assignment.LatestResultId!];
            items.Add(Acceptance("milestone-result-acceptance", result, WorkTitle(model.Latest(order)) + " result", assignment.State,
                "Only the operator accepts a result toward a milestone; this exact result has no acceptance.", operators, [], [result, assigned, order, model.Latest(order)]));
        }

        foreach (RecordEnvelope goal in Ordered(model.Current("goal")))
        {
            string state = GoalRules.Status(model, goal);
            GoalRules.Accepter accepter = GoalRules.AccepterOf(model, goal);
            // A delegation to a non-operator removes the reservation only while that delegate actually holds the scope.
            if (state is "proposed" or "achieved" or "impossible" or "abandoned"
                || (accepter.Principal is not null && !model.HasRole(accepter.Principal, Roles.Operator))
                || (accepter.Delegate is string delegated && !model.HasRole(delegated, Roles.Operator) && model.HasScope(delegated, Scopes.AcceptWork))) continue;
            RecordEnvelope? evaluation = GoalRules.CurrentEvaluation(model, goal);
            List<string> basis = evaluation is null ? [] : [evaluation.Id];
            List<string> wait = GoalView.NotReady(model, goal).ToList();
            bool ready = wait.Count == 0;
            List<string> eligible = [];
            foreach (string principal in operators.Where(p => accepter.Allows(model, p, DateTimeOffset.UtcNow)))
            {
                IReadOnlyList<(string Code, string Message)> refusals = GoalRules.AcceptanceRefusals(model, goal, principal, basis);
                if (refusals.Count == 0 && ready) eligible.Add(principal);
                else wait.AddRange(refusals.Select(r => principal + ": " + r.Message));
            }
            JsonObject item = Acceptance("goal-acceptance", goal, EstateModel.BodyString(goal, "title")!, state,
                "This goal is accepted by " + accepter.Description + ".", eligible, basis, evaluation is null ? [goal] : [goal, evaluation]);
            foreach (string reason in wait.Distinct(StringComparer.Ordinal)) item["waitsOn"]!.AsArray().Add(reason);
            items.Add(item);
        }
    }

    private static void AddObligations(EstateModel model, JsonArray items)
    {
        foreach (RecordEnvelope decision in Ordered(model.Current("decision")))
        {
            IReadOnlyList<string> chain = model.Chain(decision);
            foreach (JsonElement obligation in Array(decision.Body, "obligations"))
            {
                string? owner = Field(obligation, "owner");
                string? key = Field(obligation, "key");
                if (owner is null || key is null || !model.HasRole(owner, Roles.Operator)
                    || model.Current("discharge").Any(d => d.Subject is not null && chain.Contains(d.Subject.Id) && EstateModel.BodyString(d, "obligation") == key)) continue;
                string discharge = Field(obligation, "discharge") ?? "The recorded discharge evidence.";
                JsonObject item = Item("reserved-action", decision, Field(obligation, "summary") ?? key, "obligation open",
                    $"Decision obligation {key} belongs to operator {owner} and has no discharge.", [discharge], [decision], key);
                item["resolution"] = Field(obligation, "resolution");
                item["gates"] = Field(obligation, "gates");
                AddCommand(item, "discharge", owner, decision, new JsonObject
                {
                    ["obligation"] = key, ["summary"] = null, ["evidence"] = new JsonArray(new JsonObject { ["summary"] = null }),
                }, "After carrying out the obligation, record the evidence that meets its discharge requirement.");
                items.Add(item);
            }
        }
    }

    private static void AddPublications(EstateModel model, JsonArray items, List<string> operators)
    {
        foreach (RecordEnvelope release in Ordered(model.Current("release")).Where(r => EstateModel.BodyString(r, "status") == "candidate" && Accepted(model, r)))
        {
            List<RecordEnvelope> references = [release, .. model.OfType("acceptance").Where(a => a.Subject == release.Reference && model.HasRole(a.By, Roles.Operator))];
            JsonObject item = Item("reserved-action", release, "Publication of release " + EstateModel.BodyString(release, "version"), "publication not recorded",
                "The operator accepted this candidate, but no publication is recorded. External publication is not inferred from acceptance.",
                ["The operator's publication and evidence of its observed time and location; reconcile an uncertain effect before any retry."], references);
            if (release.Subject is not null && model.ById.TryGetValue(release.Subject.Id, out RecordEnvelope? product))
            {
                foreach (string by in operators)
                {
                    JsonObject body = JsonNode.Parse(release.Body.GetRawText())!.AsObject();
                    body["status"] = "published";
                    body["publishedAt"] = null;
                    body["url"] = null;
                    AddCommand(item, "release", by, product, body,
                        "After observing the operator's publication, record only its time and location, preserving the accepted candidate's contents. This command does not publish artifacts.", release.Id);
                }
            }
            items.Add(item);
        }
    }

    /// <summary>
    /// Adopting history checkpoints, the root operator's step once after the estate's upgrade to a release that writes them
    /// (design/signed-history.md § 10; design/release-procedure.md § 7, the upgrade's adopt step): listed while no adoption checkpoint
    /// counts in an estate pinned to such a release, with the check to read first and the signing command. Only the root operator or a
    /// person holding administer adopts; an agent's adoption counts for nothing, so it never removes the item.
    /// </summary>
    private static void AddAdoption(EstateModel model, JsonArray items)
    {
        if (model.Manifest is not { RecordFormat: >= 3, Mode: "release", Version: { } version } manifest || !ReleaseFollowing.IsNewer(version, LastReleaseWithoutCheckpoints)) return;
        if (History.SignedHistory.Adopted(model)) return;
        RecordEnvelope? source = manifest.Release is { } pin && model.ById.TryGetValue(pin.Id, out RecordEnvelope? pinned) ? pinned : model.CurrentEstate;
        if (source is null) return;
        string by = model.RootOperator ?? "<root operator>";
        JsonObject item = Item("history-adoption", source, "Adopt history checkpoints", "not adopted",
            $"The estate is pinned to {version}, which writes history checkpoints, and no adoption checkpoint counts. Adoption witnesses every record present and anchors the unsigned records that count today, so it changes no reading; from then on the runtime writes checkpoints at upgrades, release submissions, charter revisions and intervals.",
            [$"The root operator reads what adoption would witness and anchor with vericairn estate checkpoint --adopt --check, then signs it once with vericairn estate checkpoint --adopt --expect-plan <the plan hash the check prints> --by {by}. A person holding administer may sign instead; an agent's adoption counts for nothing."],
            [source]);
        item["inspect"] = "vericairn estate checkpoint --adopt --check";
        items.Add(item);
    }

    /// <summary>The newest release that does not write history checkpoints: an estate pinned to a later one lists adoption until it adopts.</summary>
    internal const string LastReleaseWithoutCheckpoints = "0.9.1";

    private static void AddReservedWork(EstateModel model, JsonArray items, Dictionary<string, string> states)
    {
        foreach (RecordEnvelope order in Ordered(model.Current("work-order")))
        {
            string? reserved = Routing.ReservedAction(Routing.RoleOf(order));
            string state = states.GetValueOrDefault(order.Id) ?? "planned";
            if (reserved is null || state is "accepted" or "retired") continue;
            IReadOnlyList<RecordEnvelope> assignments = Routing.Assignments(model, order);
            RecordEnvelope? assignment = assignments.Count == 0 ? null : assignments[^1];
            string? by = assignment is null ? null : EstateModel.BodyString(assignment, "contributor");
            if (assignment is null || by is null || !model.HasRole(by, Roles.Operator)) continue;
            IReadOnlyList<RecordEnvelope> attempts = WorkView.AttemptsOf(model, assignment);
            RecordEnvelope? attempt = attempts.Count == 0 ? null : attempts[^1];
            RecordEnvelope? result = attempt is null ? null : WorkView.ResultOf(model, attempt);
            if (result is not null && EstateModel.BodyString(result, "outcome") == "completed") continue;
            var wait = new List<string>();
            var references = new List<RecordEnvelope> { order, assignment };
            if (result is not null)
            {
                references.Add(result);
                wait.Add("The previous attempt ended " + EstateModel.BodyString(result, "outcome") + ": " + EstateModel.BodyString(result, "nextStep"));
                if (result.Body.TryGetProperty("unresolvedEffects", out JsonElement effects) && effects.ValueKind == JsonValueKind.Array)
                    wait.AddRange(effects.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => "Reconcile before retry: " + e.GetString()));
            }
            foreach (NextEligible.Relation relation in NextEligible.Relations(model, order).Where(r => r.Holds))
            {
                wait.Add($"{relation.Kind} {relation.Type} {relation.Id}: {relation.State}");
                if (relation.Holder is not null) references.Add(relation.Holder);
                references.AddRange(relation.Basis);
            }
            JsonObject item = Item("reserved-action", order, WorkTitle(order), result is not null ? "another attempt needed" : attempt is null ? "assigned" : "in progress",
                $"The recorded {Routing.RoleOf(order)} assignment puts {Routing.Describe(reserved)} with operator {by}.", wait, references);
            if (attempt is null || result is not null)
                AddCommand(item, "attempt", by, assignment, new JsonObject { ["startedAt"] = null, ["workspace"] = null, ["note"] = null }, "Read the assignment, satisfy its holds and record your own start before performing the reserved action.");
            else
            {
                item["references"]!.AsArray().Add(Reference(attempt));
                AddCommand(item, "result", by, attempt, new JsonObject
                {
                    ["outcome"] = null, ["summary"] = null, ["outputs"] = new JsonArray(), ["evidence"] = new JsonArray(),
                    ["checks"] = new JsonArray(), ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = null,
                }, "After your attempt, record its actual outcome, checks, evidence and unresolved effects; a result does not accept the work.");
            }
            items.Add(item);
        }
    }

    private static JsonObject Acceptance(string kind, RecordEnvelope subject, string title, string state, string why, List<string> operators, List<string> basis, IReadOnlyList<RecordEnvelope> references)
    {
        JsonObject item = Item(kind, subject, title, state, why, ["The authorized operator's judgment and statement of acceptance."], references);
        foreach (string by in operators)
            AddCommand(item, "acceptance", by, subject, new JsonObject
            {
                ["statement"] = null, ["basis"] = Strings(basis), ["self"] = subject.By == by,
            }, "Inspect the pinned subject and evidence, then supply your own acceptance statement.");
        if (operators.Count == 0) item["waitsOn"]!.AsArray().Add("No operator can currently record this acceptance; inspect its authority and prerequisites.");
        return item;
    }

    private static JsonObject Item(string kind, RecordEnvelope source, string? title, string state, string why, IReadOnlyList<string> wait, IReadOnlyList<RecordEnvelope> references, string? key = null) => new()
    {
        ["id"] = kind + ":" + source.Id + (key is null ? string.Empty : ":" + key), ["kind"] = kind,
        ["source"] = Reference(source), ["title"] = title ?? source.Type + " " + source.Id, ["state"] = state, ["why"] = why, ["waitsOn"] = Strings(wait),
        ["references"] = new JsonArray(references.DistinctBy(r => r.Id).Select(r => (JsonNode)Reference(r)).ToArray()),
        ["inspect"] = "vericairn records show " + source.Id, ["commands"] = new JsonArray(),
    };

    private static void AddCommand(JsonObject item, string type, string by, RecordEnvelope? subject, JsonObject body, string purpose, string? supersedes = null)
    {
        List<string> args = ["records", "new", type, "--by", by];
        if (subject is not null) args.AddRange(["--subject", $"{subject.Type}:{subject.Id}:{subject.ComputedHash}"]);
        if (supersedes is not null) args.AddRange(["--supersedes", supersedes]);
        args.AddRange(["--body", "-"]);
        List<string> oneStep = ["operator", "record", .. args.Skip(2)];
        // Only fixed command words, validated identifiers and hashes enter the command line. All authored prose stays in JSON stdin.
        item["commands"]!.AsArray().Add(new JsonObject
        {
            ["by"] = by, ["command"] = "vericairn " + string.Join(' ', args), ["arguments"] = Strings(args),
            ["body"] = body, ["purpose"] = purpose,
            ["oneStep"] = new JsonObject { ["command"] = "vericairn " + string.Join(' ', oneStep), ["arguments"] = Strings(oneStep) },
        });
    }

    private static bool Accepted(EstateModel model, RecordEnvelope subject) => model.OfType("acceptance")
        .Any(a => a.Subject?.Id == subject.Id && a.Subject.Hash == subject.ComputedHash && model.HasRole(a.By, Roles.Operator));
    private static JsonObject Reference(RecordEnvelope record) => new() { ["type"] = record.Type, ["id"] = record.Id, ["hash"] = record.ComputedHash };
    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
    private static IEnumerable<RecordEnvelope> Ordered(IEnumerable<RecordEnvelope> records) => records.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal);
    private static string WorkTitle(RecordEnvelope order) => EstateModel.BodyString(order, "key") + " " + EstateModel.BodyString(order, "title");
    private static IEnumerable<JsonElement> Array(JsonElement body, string name) => body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Object) : [];
    private static string? Field(JsonElement body, string name) => body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
