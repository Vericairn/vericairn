// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Estate;

/// <summary>
/// A run's stops (the estate's design/flow.md § 8; design/release-procedure.md §§ 6 and 8; design/cohesion-contract.md § 5): each step,
/// review or run-wide matter that waits on a person, in the order the flow reaches it, with who it waits on, why, and how it reaches
/// them. A run stops only where a person holds the step's role or an item's review, where a step is a reserved act with no grant in force
/// within its bounds, where required attention arises, and where the goal is ready to accept. A reserved act waits on the operator as an
/// operator request whose exact command is computed from the records only when its step is next; the request grants nothing, and the
/// step is done only once the records of the operator's own act hold. At most three stops of a run reach one person at a time; the rest
/// wait in order. Everything here is derived from the records alone, so status, goal show, goal next, the API, Workbench and Remote show
/// the same stops; reading them records nothing.
/// </summary>
public static class FlowStops
{
    /// <summary>
    /// Why a stop waits on a person: a person holds the step's role, its assignment or an item's review (person); the step is a reserved
    /// act with no grant in force within its bounds (reserved); required attention arises (attention); or the run is complete and the goal
    /// is ready for its acceptance (accept).
    /// </summary>
    public static readonly ImmutableArray<string> Kinds = ["person", "reserved", "attention", "accept"];

    /// <summary>
    /// How a stop reaches its person: due (the next goal step records its assignment and ask, its ask or its operator request);
    /// delivered (its assignment, ask or operator request waits on the person); closed (its ask was answered or its request closed, and
    /// it is not asked again while that answer holds); queued (three stops of the run already reach that person, and it follows in order
    /// as one of them settles); and outside (carried outside goal step, so nothing is recorded to reach the person in this build).
    /// </summary>
    public static readonly ImmutableArray<string> Deliveries = ["due", "delivered", "closed", "queued", "outside"];

    /// <summary>
    /// The states of an open operator request for a reserved step: due (it carries the step's command exactly as the records give it now),
    /// settled (its step is done, from the records of the operator's act) and withdrawn (its step no longer waits on it, or its command
    /// is no longer the one the records give). Only a due request is delivered; each stays open until the operator closes it.
    /// </summary>
    public static readonly ImmutableArray<string> RequestStates = ["due", "settled", "withdrawn"];

    /// <summary>At most this many stops of a run reach one person at a time (design/cohesion-contract.md § 5).</summary>
    public const int PerDelivery = 3;

    /// <summary>One stop of a run.</summary>
    /// <param name="Kind">Why it waits: one of <see cref="Kinds"/>.</param>
    /// <param name="Name">The step instance in words (key or key[instance], with "review" for an item's review), or "the run" and "the goal".</param>
    /// <param name="Title">What waits, in words.</param>
    /// <param name="Step">The step instance, marked for its review; null for the run's own attention and the goal's acceptance.</param>
    /// <param name="Principal">Who it waits on; null when the records name no one.</param>
    /// <param name="Reason">Why, in words.</param>
    /// <param name="Delivery">How it reaches the person: one of <see cref="Deliveries"/>.</param>
    /// <param name="Record">The record that delivers it, or that closed it: an assignment, an ask or an operator request.</param>
    /// <param name="Command">For a reserved act, its exact command as the records give it now; null otherwise.</param>
    /// <param name="Act">For a reserved act, the act; null otherwise.</param>
    public sealed record RunStop(string Kind, string Name, string Title, FlowRuns.StepRef? Step, string? Principal, string Reason, string Delivery, RecordEnvelope? Record, string? Command = null, string? Act = null);

    /// <summary>The step an operator request carries: the run's goal by its first revision, the step's key and its instance.</summary>
    /// <param name="Goal">The goal's first revision.</param>
    /// <param name="Key">The step's key.</param>
    /// <param name="Instance">Its instance, for a step that runs over a list.</param>
    public sealed record FlowStepReference(string Goal, string Key, string? Instance)
    {
        /// <summary>The step instance.</summary>
        public FlowRuns.StepRef Step => new(Key, Instance);
    }

    /// <summary>An open request for a reserved step with its state: one of <see cref="RequestStates"/>, and why.</summary>
    /// <param name="Request">The request's current revision.</param>
    /// <param name="State">due, settled or withdrawn.</param>
    /// <param name="Reason">Why, in words.</param>
    public sealed record StepRequest(RecordEnvelope Request, string State, string Reason);

    /// <summary>The step an operator request carries, or null for a request of no step.</summary>
    public static FlowStepReference? StepOf(RecordEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(request);
        JsonElement step = TJson.Object(request.Body, "flowStep");
        return TJson.Text(step, "goal") is string goal && TJson.Text(step, "key") is string key ? new FlowStepReference(goal, key, TJson.Text(step, "instance")) : null;
    }

    /// <summary>The command an operator request carries: its one content item, of kind command; null when it carries anything else.</summary>
    public static string? CommandOf(RecordEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<JsonElement> content = TJson.Array(request.Body, "content").ToList();
        return content.Count == 1 && TJson.Text(content[0], "kind") == "command" ? TJson.Text(content[0], "text") : null;
    }

    /// <summary>Every current operator request for a step of a run, open or closed, by the time of its first revision.</summary>
    public static IReadOnlyList<RecordEnvelope> Requests(EstateModel model, RecordEnvelope goal, FlowRuns.StepRef step)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(step);
        string root = model.Chain(goal)[0];
        return model.Current("operator-request").Where(r => StepOf(r) is FlowStepReference named && named.Goal == root && named.Key == step.Key && named.Instance == step.Instance)
            .OrderBy(r => model.ById[model.Chain(r)[0]].At, StringComparer.Ordinal).ThenBy(r => model.Chain(r)[0], StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The open requests for a step of a run, each with its state: due when the step is a reserved act stopped for the operator now, the
    /// request carries exactly the command the records give now, it is put to a person who holds the act and was recorded by a principal
    /// who takes the run's steps, and no earlier request is due; settled once the step is done; withdrawn otherwise. A request with a
    /// stale or altered command is never delivered, and its prose is shown as the requester's, unchecked; it grants nothing.
    /// </summary>
    public static IReadOnlyList<StepRequest> Classify(EstateModel model, FlowPosition.Position position, FlowRuns.StepRef step)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(step);
        string status = GoalRules.Status(model, position.Goal);
        FlowPosition.StepInstance? instance = position.Flow is null ? null : position.Find(step);
        var states = new List<StepRequest>();
        bool carried = false;
        foreach (RecordEnvelope request in Requests(model, position.Goal, step).Where(r => !OperatorRequests.Closures(model, r).Any()))
        {
            (string state, string reason) = status == "achieved" || GoalRules.Ended.IndexOf(status) >= 0 ? ("withdrawn", $"its goal is {status}")
                : position.Flow is null ? ("withdrawn", $"the run's {position.Unavailable}")
                : instance is null ? ("withdrawn", $"step {step} is not part of the run")
                : instance.State == "done" ? ("settled", $"step {step} is done: {instance.Reasons[0]}")
                : instance.State != "stopped" || instance.Stop != "reserved" || instance.Command is null ? ("withdrawn", $"step {step} is {instance.State} now: {instance.Reasons[0]}")
                : CommandOf(request) != instance.Command ? ("withdrawn", $"its command is not the one the records give now for step {step}")
                : Unproven(model, position.Goal, request) is string why ? ("withdrawn", why)
                : carried ? ("withdrawn", $"an earlier request carries step {step}")
                : ("due", $"it carries step {step}'s command as the records give it now");
            carried |= state == "due";
            states.Add(new StepRequest(request, state, reason));
        }

        return states;
    }

    /// <summary>
    /// Why a request for a reserved step is not one to deliver, or null when it is: its author takes the run's steps, and it is put to a
    /// person who holds the act. Its command is checked apart, against the records.
    /// </summary>
    private static string? Unproven(EstateModel model, RecordEnvelope goal, RecordEnvelope request) =>
        !Routing.MayTakeStep(model, goal, request.By, request.AtTime, request) ? $"{Routing.NameOf(model, request.By)} recorded it, and does not take the run's steps"
        : !FlowPosition.HoldsReservedAct(model, EstateModel.BodyString(request, "to")) ? "it is put to no person who holds the act"
        : null;

    /// <summary>
    /// The request for a reserved step that was closed and is not made again while its command holds: one that would have been due,
    /// carrying the command the records give now, recorded by a principal who takes the run's steps and put to a person who holds the act,
    /// whose close is that addressed operator's own authentic record. Any other close, an agent's citing the operator's words among them,
    /// leaves the step's request due again.
    /// </summary>
    private static RecordEnvelope? Closed(EstateModel model, FlowPosition.Position position, FlowPosition.StepInstance instance) =>
        Requests(model, position.Goal, instance.Ref).LastOrDefault(r => CommandOf(r) == instance.Command && Unproven(model, position.Goal, r) is null
            && OperatorRequests.Closures(model, r).Any(c => c.By == EstateModel.BodyString(r, "to") && SignatureChecks.IsAuthentic(model, c)));

    /// <summary>
    /// Who a reserved step's operator request is put to: the first person with the operator role its role names, through the goal's
    /// roles or the roster, else the goal's sponsor when the sponsor is one, else the estate's root operator; null when none is a person
    /// with the operator role.
    /// </summary>
    public static string? RequestTo(EstateModel model, RecordEnvelope goal, FlowStep step)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(step);
        string? named = Routing.Principals(model, goal, step.Role).Principals.FirstOrDefault(p => FlowPosition.HoldsReservedAct(model, p));
        string? sponsor = EstateModel.BodyString(goal, "sponsor");
        string? root = Routing.RootOperator(model);
        return named ?? (FlowPosition.HoldsReservedAct(model, sponsor) ? sponsor : FlowPosition.HoldsReservedAct(model, root) ? root : null);
    }

    /// <summary>
    /// Why a reserved step waits on the operator now, as its operator request states it: the step is next and a reserved act, each of its
    /// preconditions as it holds now, and each of its inputs exactly as handed on.
    /// </summary>
    public static string Why(EstateModel model, FlowPosition.Position position, FlowPosition.StepInstance instance)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(instance);
        FlowDefinition flow = position.Flow ?? throw new InvalidOperationException("The run's flow does not resolve here.");
        IReadOnlyDictionary<string, string> bindings = FlowRuns.Bindings(model, position.Goal, flow, instance.Instance);
        var parts = new List<string> { $"Step {instance.Name}, {instance.Step.Title}, of the run of flow {flow.Pin.Id} {flow.Pin.Version} is next: every step it waits on is done, and it is a reserved act, {instance.Step.Reserved}, which no agent carries without a grant in force within its bounds, none of which this build issues" };
        List<string> holding = instance.Step.Requires.Select(p => GoalPredicates.EvaluatePredicate(model, FlowRuns.Bind(p, bindings))).Select(v => $"{v.Value}: {v.Reason}").ToList();
        if (holding.Count > 0)
        {
            parts.Add("Its preconditions as they hold now: " + string.Join("; ", holding));
        }

        List<string> handed = instance.Inputs.Where(i => i.Carrier is not null).Select(i => $"{i.Name}, {i.Source}, at {i.Hash} in {i.Carrier!.Type} {i.Carrier.Id}").ToList();
        if (handed.Count > 0)
        {
            parts.Add("Its inputs exactly as handed on: " + string.Join("; ", handed));
        }

        return string.Join(". ", parts) + ".";
    }

    /// <summary>
    /// A run's stops from its position and the records, in the order the flow reaches them: the run's own required attention first, then
    /// each stopped step and each review of a step in review that a person holds, then the goal's acceptance once the run is complete and
    /// the goal is ready for it; each as it reaches its person before the limit across runs (<see cref="Deliver"/>) queues any. A run that
    /// is not active, paused, budget-limited or blocked has none.
    /// </summary>
    public static IReadOnlyList<RunStop> Of(EstateModel model, FlowPosition.Position position, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(position);
        RecordEnvelope goal = position.Goal;
        string status = GoalRules.Status(model, goal);
        if (position.Flow is null || position.Work is null || status is not ("active" or "paused" or "budget-limited" or "blocked"))
        {
            return [];
        }

        string? sponsor = Routing.Asked(model, goal, null, at);
        var stops = new List<RunStop>();
        foreach (FlowPosition.Attention attention in position.RunAttention)
        {
            stops.Add(Attending(model, goal, "the run", null, "the run's required attention", attention, sponsor));
        }

        foreach (FlowPosition.StepInstance instance in position.Steps)
        {
            if (instance.State == "stopped" && instance.Stop is string kind)
            {
                stops.Add(kind switch
                {
                    "reserved" => Reserved(model, position, instance),
                    "attention" => instance.Attention is FlowPosition.Attention attention && instance.Step.IsRouted
                        ? Attending(model, goal, instance.Name, instance.Ref, instance.Step.Title, attention, sponsor)
                        : new RunStop("attention", instance.Name, instance.Step.Title, instance.Ref, sponsor, Last(instance), "outside", null),
                    _ => Person(model, instance),
                });
            }
            else if (instance.State == "in-review")
            {
                foreach (FlowPosition.PendingReview review in instance.Reviews)
                {
                    if (Reviewing(model, position, instance, review) is RunStop stop)
                    {
                        stops.Add(stop);
                    }
                }
            }
        }

        if (position.Complete && GoalView.NotReady(model, goal).Count == 0)
        {
            GoalRules.Accepter accepter = GoalRules.AccepterOf(model, goal);
            stops.Add(new RunStop("accept", "the goal", "the goal's acceptance", null, accepter.Principal,
                $"the run is complete and the goal is ready for its acceptance by {accepter.Description}: vericairn goal accept {goal.Id} --by <id> --statement <text>", "outside", null));
        }

        return stops;
    }

    /// <summary>
    /// A run's stops delivered across every run (design/cohesion-contract.md § 5): at most <see cref="PerDelivery"/> reach one person at a
    /// time, whichever runs they belong to. Those already delivered count first; then each due one in the flows' order, the earliest
    /// position in its flow first and, at the same position, the older run first; the rest are queued. Returns the run's stops and how
    /// many reach each person.
    /// </summary>
    public static (IReadOnlyList<RunStop> Stops, IReadOnlyDictionary<string, int> Reaching) Deliver(EstateModel model, FlowPosition.Position position, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(position);
        string own = model.Chain(position.Goal)[0];
        var runs = new List<(string Root, IReadOnlyList<RunStop> Stops)>();
        foreach (RecordEnvelope goal in Active(model))
        {
            string root = model.Chain(goal)[0];
            runs.Add((root, root == own ? position.Stops : FlowPosition.Derive(model, goal, at, deliver: false).Stops));
        }

        if (runs.All(r => r.Root != own))
        {
            runs.Add((own, position.Stops));
        }

        var reaching = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (RunStop delivered in runs.SelectMany(r => r.Stops).Where(s => s.Delivery == "delivered" && s.Principal is not null))
        {
            reaching[delivered.Principal!] = reaching.GetValueOrDefault(delivered.Principal!) + 1;
        }

        List<RunStop> result = [.. position.Stops];
        var due = runs.SelectMany((run, order) => run.Stops.Select((stop, index) => (Stop: stop, Index: index, Order: order, Own: run.Root == own)))
            .Where(s => s.Stop is { Delivery: "due", Principal: not null }).OrderBy(s => s.Index).ThenBy(s => s.Order).ToList();
        foreach ((RunStop stop, int index, int _, bool mine) in due)
        {
            int count = reaching.GetValueOrDefault(stop.Principal!);
            if (count >= PerDelivery)
            {
                if (mine)
                {
                    result[index] = stop with { Delivery = "queued" };
                }
            }
            else
            {
                reaching[stop.Principal!] = count + 1;
            }
        }

        return (result, reaching);
    }

    /// <summary>
    /// The records' own time: the newest record's, from which canonical status derives a run's position, its stops and the delivery of a
    /// run's operator requests, so that the same records give the same document whenever and wherever it is read, as a policy evaluated
    /// at its recorded time is. A due time, backoff or watch limit that passes after it shows in canonical status once a later record is
    /// written; a live read shows it at once.
    /// </summary>
    public static DateTimeOffset RecordedTime(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        // In an estate that has adopted history checkpoints, only records that count give the recorded time (design/signed-history.md § 8).
        IReadOnlyList<RecordEnvelope> records = History.SignedHistory.Adopted(model) ? model.CountingRecords : model.Records;
        return records.Count == 0 ? DateTimeOffset.UnixEpoch : records.Max(r => r.AtTime);
    }

    /// <summary>The runs status shows: every current goal that is a run and is active, paused, budget-limited or blocked, oldest first, with its position.</summary>
    public static IReadOnlyList<FlowPosition.Position> Runs(EstateModel model, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Active(model).Select(g => FlowPosition.Derive(model, g, at)).ToList();
    }

    /// <summary>Every current goal that is a run and is active, paused, budget-limited or blocked, the oldest run first by its first revision.</summary>
    private static List<RecordEnvelope> Active(EstateModel model) =>
        model.Current("goal").Where(FlowRuns.IsRun).Where(g => GoalRules.Status(model, g) is "active" or "paused" or "budget-limited" or "blocked")
            .OrderBy(g => model.ById[model.Chain(g)[0]].At, StringComparer.Ordinal).ThenBy(g => model.Chain(g)[0], StringComparer.Ordinal).ToList();

    /// <summary>
    /// A run's stops as JSON, the same in status, goal show, goal next and every client. A record-derived document, canonical status or a
    /// hand-over package, passes <paramref name="commands"/> false: it trusts each record's claimed time, so a reserved step's command,
    /// which a live read derives at the real time, is left out of it.
    /// </summary>
    public static JsonArray RenderJson(EstateModel model, IReadOnlyList<RunStop> stops, bool commands = true)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(stops);
        return new JsonArray(stops.Select(s => (JsonNode)new JsonObject
        {
            ["kind"] = s.Kind,
            ["name"] = s.Name,
            ["title"] = s.Title,
            ["step"] = s.Step is null ? null : new JsonObject { ["key"] = s.Step.Key, ["instance"] = s.Step.Instance, ["review"] = s.Step.Review },
            ["principal"] = s.Principal,
            ["principalName"] = s.Principal is null ? null : Routing.NameOf(model, s.Principal),
            ["reason"] = s.Reason,
            ["delivery"] = s.Delivery,
            ["record"] = s.Record is null ? null : new JsonObject { ["type"] = s.Record.Type, ["id"] = s.Record.Id, ["hash"] = s.Record.ComputedHash },
            ["act"] = s.Act,
            ["command"] = commands ? s.Command : null,
        }).ToArray());
    }

    /// <summary>
    /// A run's stops as text: each with why it waits, on whom, how it reaches them and, for a reserved act that is next and a live read,
    /// its exact command.
    /// </summary>
    public static void AppendText(StringBuilder text, EstateModel model, IReadOnlyList<RunStop> stops, string indent, bool commands = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(stops);
        if (stops.Count == 0)
        {
            text.Append(indent).Append("Stops: none\n");
            return;
        }

        int queued = stops.Count(s => s.Delivery == "queued");
        text.Append(indent).Append("Stops: ").Append(stops.Count.ToString(CultureInfo.InvariantCulture)).Append(", in the order the flow reaches them; at most ")
            .Append(PerDelivery.ToString(CultureInfo.InvariantCulture)).Append(" reach one person at a time")
            .Append(queued == 0 ? string.Empty : $", and {queued.ToString(CultureInfo.InvariantCulture)} more {(queued == 1 ? "waits" : "wait")} queued").Append('\n');
        foreach (RunStop stop in stops)
        {
            text.Append(indent).Append("  ").Append(stop.Name).Append("  ").Append(stop.Kind).Append(", waits on ")
                .Append(stop.Principal is null ? "a person the records do not name" : $"{Routing.NameOf(model, stop.Principal)} [principal {stop.Principal}]")
                .Append(": ").Append(stop.Delivery).Append(stop.Record is null ? string.Empty : $" ({stop.Record.Type} {stop.Record.Id})").Append('\n');
            text.Append(indent).Append("    ").Append(stop.Reason).Append('\n');
            if (stop.Command is not null && commands)
            {
                text.Append(indent).Append("    command: ").Append(stop.Command).Append('\n');
            }
            else if (stop.Command is not null)
            {
                text.Append(indent).Append("    command: shown by a live read, vericairn goal next or goal show\n");
            }
        }
    }

    /// <summary>The runs status shows, as canonical JSON's runs array: each run's goal, flow, position and stops.</summary>
    public static JsonArray RunsJson(EstateModel model, IReadOnlyList<FlowPosition.Position> runs)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(runs);
        return new JsonArray(runs.Select(run =>
        {
            List<FlowPosition.StepInstance> running = run.Running.ToList();
            TemplatePin? pin = run.Flow?.Pin ?? FlowRuns.Pin(run.Goal);
            return (JsonNode)new JsonObject
            {
                ["goal"] = new JsonObject { ["id"] = run.Goal.Id, ["hash"] = run.Goal.ComputedHash, ["title"] = EstateModel.BodyString(run.Goal, "title"), ["status"] = GoalRules.Status(model, run.Goal) },
                ["asOf"] = RecordParser.FormatTimestamp(run.At),
                ["pin"] = pin is null ? null : new JsonObject { ["id"] = pin.Id, ["version"] = pin.Version, ["hash"] = pin.Hash },
                ["available"] = run.Flow is not null,
                ["unavailable"] = run.Unavailable,
                ["done"] = running.Count(s => s.State == "done"),
                ["steps"] = running.Count,
                ["complete"] = run.Complete,
                ["next"] = new JsonArray(run.Steps.Where(s => s.State == "next").Select(s => (JsonNode)s.Name).ToArray()),
                ["stops"] = RenderJson(model, run.Stops, commands: false),
            };
        }).ToArray());
    }

    /// <summary>The runs status shows, as text: nothing when there is none.</summary>
    public static string RunsText(EstateModel model, IReadOnlyList<FlowPosition.Position> runs)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(runs);
        if (runs.Count == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder("Runs:\n");
        foreach (FlowPosition.Position run in runs)
        {
            List<FlowPosition.StepInstance> running = run.Running.ToList();
            List<string> next = run.Steps.Where(s => s.State == "next").Select(s => s.Name).ToList();
            text.Append("  ").Append(EstateModel.BodyString(run.Goal, "title")).Append(" [goal ").Append(run.Goal.Id).Append("], ").Append(GoalRules.Status(model, run.Goal)).Append(": ");
            if (run.Flow is null)
            {
                text.Append("not run here: ").Append(run.Unavailable).Append('\n');
                continue;
            }

            text.Append("flow ").Append(run.Flow.Pin.Id).Append(' ').Append(run.Flow.Pin.Version).Append(", ").Append(running.Count(s => s.State == "done").ToString(CultureInfo.InvariantCulture))
                .Append(" of ").Append(running.Count.ToString(CultureInfo.InvariantCulture)).Append(running.Count == 1 ? " step" : " steps").Append(" done; next: ")
                .Append(next.Count == 0 ? "none" : string.Join(", ", next)).Append('\n');
            AppendText(text, model, run.Stops, "    ");
        }

        return text.ToString();
    }

    /// <summary>The last reason of a step instance: for required attention, why it is required.</summary>
    private static string Last(FlowPosition.StepInstance instance) => instance.Reasons.Count == 0 ? instance.State : instance.Reasons[^1];

    /// <summary>
    /// A stop for required attention (design/flow.md §§ 7 and 8): asked of who decides for the goal, its sponsor, about the record that
    /// raised it, delivered while an ask about that record waits and closed once one is answered, so it is asked once.
    /// </summary>
    private static RunStop Attending(EstateModel model, RecordEnvelope goal, string name, FlowRuns.StepRef? step, string title, FlowPosition.Attention attention, string? sponsor)
    {
        IReadOnlyList<string> trigger = model.Chain(attention.Trigger);
        List<RecordEnvelope> asks = Routing.AsksOf(model, goal).Where(a => model.Latest(a).Subject is RecordReference subject && trigger.Contains(subject.Id)).ToList();
        string reason = $"required attention ({attention.Kind}): {attention.Summary}";
        if (asks.LastOrDefault(a => Asks.IsWaiting(Asks.State(model, a).State)) is RecordEnvelope waiting)
        {
            return new RunStop("attention", name, title, step, EstateModel.BodyString(model.Latest(waiting), "to") ?? sponsor, reason, "delivered", waiting);
        }

        return asks.LastOrDefault(a => Asks.State(model, a).State == "answered") is RecordEnvelope answered
            ? new RunStop("attention", name, title, step, EstateModel.BodyString(model.Latest(answered), "to") ?? sponsor, reason, "closed", answered)
            : new RunStop("attention", name, title, step, sponsor, reason, "due", null);
    }

    /// <summary>
    /// A stop where a person holds a step: delivered while the person holds its assignment; due when its role resolves to a person and
    /// goal step is to assign and ask them; outside for a step carried outside goal step, such as an effect a person carries.
    /// </summary>
    private static RunStop Person(EstateModel model, FlowPosition.StepInstance instance)
    {
        string? holder = instance.Assignment is null ? null : EstateModel.BodyString(instance.Assignment, "contributor");
        if (instance.Step.IsRouted && !instance.Rerun && holder is not null && GoalRules.IsPerson(model, holder))
        {
            return new RunStop("person", instance.Name, instance.Step.Title, instance.Ref, holder, instance.Reasons[0], "delivered", instance.Assignment);
        }

        string? person = instance.Principal is string named && GoalRules.IsPerson(model, named) ? named : null;
        return new RunStop("person", instance.Name, instance.Step.Title, instance.Ref, person, instance.Reasons[0], instance.Step.IsRouted && person is not null ? "due" : "outside", null);
    }

    /// <summary>
    /// A stop for a reserved act (design/release-procedure.md § 6): an operator request put to the operator, carrying the step's exact
    /// command as the records give it now. Delivered while a due request carries it; closed once a request carrying that same command was
    /// closed, which is not asked again while the command holds; due otherwise, for goal step to record.
    /// </summary>
    private static RunStop Reserved(EstateModel model, FlowPosition.Position position, FlowPosition.StepInstance instance)
    {
        string act = instance.Step.Reserved!;
        string reason = instance.Reasons[0];
        if (Classify(model, position, instance.Ref).FirstOrDefault(r => r.State == "due") is StepRequest due)
        {
            return new RunStop("reserved", instance.Name, instance.Step.Title, instance.Ref, EstateModel.BodyString(due.Request, "to"), reason, "delivered", due.Request, instance.Command, act);
        }

        if (Closed(model, position, instance) is RecordEnvelope closed)
        {
            return new RunStop("reserved", instance.Name, instance.Step.Title, instance.Ref, EstateModel.BodyString(closed, "to"),
                $"{reason}; operator request {closed.Id} carrying this command was closed, and it is not made again while the command holds: the step is done once the operator's act is recorded", "closed", closed, instance.Command, act);
        }

        string? to = RequestTo(model, position.Goal, instance.Step);
        return to is null
            ? new RunStop("reserved", instance.Name, instance.Step.Title, instance.Ref, null, reason + "; the estate names no person with the operator role to put its request to", "outside", null, instance.Command, act)
            : new RunStop("reserved", instance.Name, instance.Step.Title, instance.Ref, to, reason, "due", null, instance.Command, act);
    }

    /// <summary>
    /// A stop for a review before hand-off that a person holds (design/flow.md § 8, item 1): delivered while a person holds its review
    /// assignment; due when the first principal of its reviewer role whom its independence admits is a person. A review an agent carries
    /// is no stop.
    /// </summary>
    private static RunStop? Reviewing(EstateModel model, FlowPosition.Position position, FlowPosition.StepInstance instance, FlowPosition.PendingReview review)
    {
        string name = instance.Name + " review";
        string title = $"the review of {review.What} at {review.Hash}";
        FlowRuns.StepRef step = instance.Ref with { Review = true };
        if (review.Assignments.Count > 0)
        {
            RecordEnvelope assignment = review.Assignments[^1];
            string? contributor = EstateModel.BodyString(assignment, "contributor");
            return contributor is not null && GoalRules.IsPerson(model, contributor)
                ? new RunStop("person", name, title, step, contributor, $"a person holds the review: assigned to {Routing.NameOf(model, contributor)} by assignment {assignment.Id}", "delivered", assignment)
                : null;
        }

        IEnumerable<string> named = review.Requirement.Independence == "self" ? review.Authors.Order(StringComparer.Ordinal) : Routing.Principals(model, position.Goal, review.Role).Principals;
        string? first = named.FirstOrDefault(p => model.CurrentPrincipal(p) is not null && FlowRuns.ReviewerRefusals(model, position.Flow!, review.Requirement, review.Role, p, review.Authors).Count == 0);
        return first is not null && GoalRules.IsPerson(model, first)
            ? new RunStop("person", name, title, step, first, $"a person holds the {review.Role} role for its {review.Requirement.Independence} review: {Routing.NameOf(model, first)}", "due", null)
            : null;
    }
}
