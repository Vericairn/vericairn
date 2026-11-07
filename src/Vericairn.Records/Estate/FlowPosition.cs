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
/// A run's position (the estate's design/flow.md §§ 5 to 8; design/release-procedure.md § 5): each step of its flow, once or once
/// per item, with its state derived from the records whenever it is asked and never stored, so a clone, a restore or a second machine
/// derives the same position. A step is done when its done predicates hold and each declared output exists at an exact hash, reviewed
/// where declared; stale when an input it used changed after use; blocked, with the failing predicate, while what it waits on is not
/// done or its preconditions fail; in review while its output awaits review; stopped while a person holds its role, it is a reserved
/// act, or required attention arises; next otherwise; and off when the run's configuration leaves it out. Reading grants nothing.
/// </summary>
public static class FlowPosition
{
    /// <summary>The states of a step of a run, in the state language's flow-step vocabulary.</summary>
    public static readonly ImmutableArray<string> States = ["done", "next", "blocked", "stale", "in-review", "stopped", "off"];

    /// <summary>
    /// The states of an item a step produces or collects (design/flow.md § 5): satisfied; missing; unproven (its provenance does not
    /// hold); unverified (a declared verification does not pass at its exact hash); in review (it awaits its review, or its review found
    /// what to change); stale (it changed after its review); and off (configured off, an optional item only).
    /// </summary>
    public static readonly ImmutableArray<string> ItemStates = ["satisfied", "missing", "unproven", "unverified", "in-review", "stale", "off"];

    /// <summary>One item of a step instance with its state, derived from the records.</summary>
    /// <param name="Declared">The item as the flow declares it.</param>
    /// <param name="State">One of <see cref="ItemStates"/>.</param>
    /// <param name="Reasons">Why, every unmet requirement listed; empty when satisfied.</param>
    /// <param name="Value">The item as it exists now; null while missing or off.</param>
    /// <param name="Producer">Who produced it: a result's author, a record's author, a decision's decider.</param>
    /// <param name="Review">The review that satisfies its review requirement, when one does.</param>
    /// <param name="Evidence">The verifications that satisfy its evidence.</param>
    public sealed record ItemState(FlowOutput Declared, string State, IReadOnlyList<string> Reasons, Output? Value, string? Producer, RecordEnvelope? Review, IReadOnlyList<RecordEnvelope> Evidence)
    {
        /// <summary>True when the item holds every requirement it declares.</summary>
        public bool Satisfied => State == "satisfied";
    }

    /// <summary>A step's output as it exists now: the record that carries it and its exact hash.</summary>
    /// <param name="Name">The output's name.</param>
    /// <param name="Kind">The item's kind.</param>
    /// <param name="Carrier">The record that carries it: a file's result, or the record itself.</param>
    /// <param name="Hash">A file's content hash, or the record's hash.</param>
    /// <param name="MediaType">A file's media type, when its result names one.</param>
    public sealed record Output(string Name, string Kind, RecordEnvelope Carrier, string Hash, string? MediaType);

    /// <summary>An input of a step: what carries it now, what the step's brief pinned, and how it changed after use.</summary>
    /// <param name="Name">The input's name.</param>
    /// <param name="From">subject or step.</param>
    /// <param name="Source">The subject field, or the producing step instance and output, in words.</param>
    /// <param name="Carrier">The record that carries it now; null while it does not exist.</param>
    /// <param name="Hash">Its exact hash now: a file's or a record's.</param>
    /// <param name="Pinned">The record the step's brief pinned for it; null when none did.</param>
    /// <param name="Change">How it changed after the step used it; null when it did not.</param>
    public sealed record Input(string Name, string From, string Source, RecordEnvelope? Carrier, string? Hash, RecordReference? Pinned, string? Change);

    /// <summary>A record predicate of a step, bound to the run, with its verdict.</summary>
    /// <param name="Predicate">The bound predicate.</param>
    /// <param name="Verdict">Its verdict now.</param>
    public sealed record Check(JsonElement Predicate, GoalPredicates.Verdict Verdict);

    /// <summary>One instance of a step: the step, once or once per name its each field lists, with its derived state.</summary>
    /// <param name="Step">The flow's step.</param>
    /// <param name="Instance">The instance, for a step that runs over a list under each.</param>
    /// <param name="State">One of <see cref="States"/>.</param>
    /// <param name="Reasons">Why it is in that state, the first reason first; every failing predicate of a blocked step.</param>
    /// <param name="Principal">Who holds it or would carry it by its role, from the records; null when no one is named.</param>
    /// <param name="Assignment">The assignment that holds it, the latest of its step.</param>
    /// <param name="Result">The holding assignment's latest result.</param>
    /// <param name="Review">The review that clears it: a work step's review before hand-off, or a review step's own review.</param>
    /// <param name="Outputs">Its outputs as they exist now.</param>
    /// <param name="Items">Each item it declares, with its state.</param>
    /// <param name="Inputs">Its inputs, now and as pinned.</param>
    /// <param name="Done">Its done predicates and their verdicts.</param>
    /// <param name="DoneAt">When it was done: its newest carrying record; null when it is not.</param>
    /// <param name="Waiting">For a stale step, whether something it waits on still holds it, so it reruns only once that is done.</param>
    public sealed record StepInstance(FlowStep Step, string? Instance, string State, IReadOnlyList<string> Reasons, string? Principal, RecordEnvelope? Assignment, RecordEnvelope? Result,
        RecordEnvelope? Review, IReadOnlyList<Output> Outputs, IReadOnlyList<ItemState> Items, IReadOnlyList<Input> Inputs, IReadOnlyList<Check> Done, DateTimeOffset? DoneAt, bool Waiting = false)
    {
        /// <summary>The step instance an assignment names.</summary>
        public FlowRuns.StepRef Ref => new(Step.Key, Instance);

        /// <summary>The instance in words: key, or key[item].</summary>
        public string Name => Ref.ToString();
    }

    /// <summary>A run's position.</summary>
    /// <param name="Goal">The goal's current revision.</param>
    /// <param name="Flow">The resolved flow; null when it does not resolve here.</param>
    /// <param name="Unavailable">Why the flow does not resolve here.</param>
    /// <param name="Configuration">The run's configuration.</param>
    /// <param name="Work">The run's work order.</param>
    /// <param name="Steps">Every step instance, in the flow's order.</param>
    public sealed record Position(RecordEnvelope Goal, FlowDefinition? Flow, string? Unavailable, FlowRuns.Configuration? Configuration, RecordEnvelope? Work, IReadOnlyList<StepInstance> Steps)
    {
        /// <summary>True when every step of the run is done or off: the run is complete, and the goal's evaluation and acceptance remain.</summary>
        public bool Complete => Flow is not null && Steps.Count > 0 && Steps.All(s => s.State is "done" or "off");

        /// <summary>The instances that are part of the run.</summary>
        public IEnumerable<StepInstance> Running => Steps.Where(s => s.State != "off");

        /// <summary>The instance a step reference names, or null.</summary>
        public StepInstance? Find(FlowRuns.StepRef step) => Steps.FirstOrDefault(s => s.Ref == step);
    }

    /// <summary>Derives a run's position from the records at a time (due times and watches read it); nothing is stored.</summary>
    public static Position Derive(EstateModel model, RecordEnvelope goal, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        RecordEnvelope current = model.Latest(goal);
        (FlowDefinition? flow, string? unavailable) = FlowRuns.Resolve(model, current);
        RecordEnvelope? work = FlowRuns.Work(model, current);
        if (flow is null)
        {
            return new Position(current, null, unavailable, null, work, []);
        }

        var deriving = new Deriving(model, current, flow, FlowRuns.Configure(model, current, flow), work, at);
        foreach (FlowStep step in flow.Steps)
        {
            IReadOnlyList<string> items = step.Each is null ? [] : FlowRuns.Items(current, step.Each);
            if (step.Each is null)
            {
                deriving.Add(deriving.Derive(step, null));
            }
            else if (items.Count == 0)
            {
                deriving.Add(Deriving.Off(step, null, $"the run's {step.Each} names no items"));
            }
            else
            {
                foreach (string item in items)
                {
                    deriving.Add(deriving.Derive(step, item));
                }
            }
        }

        return new Position(current, flow, null, deriving.Configuration, work, deriving.Instances);
    }

    private sealed class Deriving(EstateModel model, RecordEnvelope goal, FlowDefinition flow, FlowRuns.Configuration configuration, RecordEnvelope? work, DateTimeOffset at)
    {
        private readonly Dictionary<FlowRuns.StepRef, StepInstance> derived = [];
        private readonly Dictionary<FlowRuns.StepRef, List<RecordEnvelope>> held = work is null ? []
            : Routing.Assignments(model, work).Where(a => FlowRuns.StepOf(a) is not null).GroupBy(a => FlowRuns.StepOf(a)!).ToDictionary(g => g.Key, g => g.ToList());

        public FlowRuns.Configuration Configuration => configuration;

        public List<StepInstance> Instances { get; } = [];

        public void Add(StepInstance instance)
        {
            Instances.Add(instance);
            derived[instance.Ref] = instance;
        }

        public static StepInstance Off(FlowStep step, string? item, string reason) => new(step, item, "off", [reason], null, null, null, null, [], [], [], [], null);

        public StepInstance Derive(FlowStep step, string? item)
        {
            if (configuration.Off.TryGetValue(step.Key, out string? off))
            {
                return Off(step, item, off);
            }

            IReadOnlyDictionary<string, string> bindings = FlowRuns.Bindings(model, goal, flow, item);
            if (flow.BranchOf(step.Key) is FlowBranch branch && Failing(branch.When, bindings) is { Count: > 0 } entering)
            {
                return Off(step, item, $"its branch {branch.Name} is not entered: {entering[0]}");
            }

            if (!step.Required && step.Inputs.Select(i => (Input: i, Producer: Producer(i, item))).FirstOrDefault(p => p.Producer is { State: "off" }) is { Producer: not null } fed)
            {
                return Off(step, item, $"its input {fed.Input.Name} comes from {fed.Producer.Name}, which is off");
            }

            List<RecordEnvelope> assignments = held.GetValueOrDefault(new FlowRuns.StepRef(step.Key, item)) ?? [];
            RecordEnvelope? holder = assignments.Count == 0 ? null : assignments[^1];
            RecordEnvelope? result = holder is null ? null : LatestResult(holder);
            List<Check> done = step.Done.Select(p => FlowRuns.Bind(p, bindings)).Select(p => new Check(p, GoalPredicates.EvaluatePredicate(model, p))).ToList();
            string? outcome = result is null ? null : EstateModel.BodyString(result, "outcome");
            (bool complete, List<Output> outputs, List<ItemState> items, RecordEnvelope? review, RecordEnvelope? findings, string? pending) = Completion(step, item, assignments, result, done);
            bool doneMet = done.All(d => d.Verdict.Value == "met");
            DateTimeOffset? doneAt = complete ? new[] { result, review }.Concat(outputs.Select(o => o.Carrier)).Concat(done.SelectMany(d => GoalPredicates.Matching(model, d.Predicate)))
                .OfType<RecordEnvelope>().Select(r => (DateTimeOffset?)r.AtTime).Max() : null;
            List<Input> inputs = Inputs(step, item, holder, complete && holder is null ? doneAt : null);
            // A review step's own review names the exact result it judged, so a review of the current result is never stale.
            bool stale = inputs.Any(i => i.Change is not null) && (complete || holder is not null) && !(step.Kind == "review" && complete);
            if (complete && !stale)
            {
                return new StepInstance(step, item, "done", [Completed(step, result, review, done)], Who(holder), holder, result, review, outputs, items, inputs, done, doneAt);
            }

            // Blocked: what it waits on is not done, or a precondition, a trap or a due time does not hold; every reason is listed. A stale
            // step shows stale, and reruns once nothing it waits on holds it.
            var blocked = new List<string>();
            List<StepInstance> waits = Dependencies(step, item).ToList();
            foreach (StepInstance wait in waits.Where(w => w.State != "done"))
            {
                blocked.Add(wait.State == "off" ? $"it takes an input from {wait.Name}, which is off" : $"it waits on {wait.Name}, which is {wait.State}");
            }

            blocked.AddRange(Failing(step.Requires, bindings).Select(f => "a precondition fails: " + f));
            foreach (FlowTrap trap in step.Traps)
            {
                if (Failing(trap.Holds, bindings) is { Count: > 0 } sprung)
                {
                    blocked.Add($"trap {trap.Name}: {trap.Summary} ({sprung[0]})");
                }
            }

            if (TJson.Object(step.When, "due") is { ValueKind: JsonValueKind.Object } due && FlowContract.ParseDuration(TJson.Text(due, "after")) is TimeSpan delay)
            {
                StepInstance? from = InstanceOf(TJson.Text(due, "from")!, item);
                if (from?.DoneAt is DateTimeOffset start && start + delay > at)
                {
                    blocked.Add($"it is due at {Time(start + delay)}, {TJson.Text(due, "after")} after {from.Name} was done");
                }
            }

            if (stale)
            {
                List<string> changed = inputs.Where(i => i.Change is not null).Select(i => i.Change!).ToList();
                changed.AddRange(blocked);
                changed.Add($"it reruns under its recovery: {step.Recovery}");
                return new StepInstance(step, item, "stale", changed, Who(holder), holder, result, review, outputs, items, inputs, done, null, blocked.Count > 0);
            }

            if (blocked.Count > 0)
            {
                return new StepInstance(step, item, "blocked", blocked, Who(holder), holder, result, review, outputs, items, inputs, done, null);
            }

            StepInstance Make(string state, string reason, string? principal = null) =>
                new(step, item, state, pending is null || state is "in-review" ? [reason] : [reason, pending], principal ?? Who(holder), holder, result, review, outputs, items, inputs, done, null);

            if (step.Reserved is string act)
            {
                return Make("stopped", $"a reserved act, {act}: it waits on a person who holds it, the operator, and no agent carries it without a grant in force within its bounds, none of which this build issues");
            }

            // An item that exists and awaits its review, or changed after it, holds any step in review.
            if (step.Kind is not ("work" or "review") && doneMet && items.FirstOrDefault(i => !i.Satisfied && i.State != "off") is { State: "in-review" or "stale" })
            {
                return Make("in-review", pending!);
            }

            if (step.Kind == "work" && result is not null && findings is null)
            {
                return Make("in-review", pending ?? $"its result {result.Id} is {outcome}{(doneMet ? string.Empty : ", and its done predicates do not hold")}: a review with findings sends it back for another attempt");
            }

            if (TJson.Object(step.When, "watch") is { ValueKind: JsonValueKind.Object } watch)
            {
                DateTimeOffset? started = waits.Select(w => w.DoneAt).Where(t => t is not null).Max() ?? Activated();
                TimeSpan limit = FlowContract.ParseDuration(TJson.Text(watch, "limit")) ?? TimeSpan.Zero;
                string watched = done.FirstOrDefault(d => d.Verdict.Value != "met")?.Verdict.Reason ?? "its done predicates";
                return started is DateTimeOffset since && since + limit < at
                    ? Make("stopped", $"the watch passed its limit at {Time(since + limit)} without its done predicates holding ({watched}): required attention")
                    : Make("next", $"watched every {TJson.Text(watch, "every")} until its done predicates hold ({watched}){(started is DateTimeOffset begin ? $", until {Time(begin + limit)}" : string.Empty)}");
            }

            string? contributor = holder is null ? null : EstateModel.BodyString(holder, "contributor");
            if (contributor is not null && step.IsRouted)
            {
                return GoalRules.IsPerson(model, contributor)
                    ? Make("stopped", $"a person holds it: assigned to {Routing.NameOf(model, contributor)} by assignment {holder!.Id}{(findings is null ? string.Empty : $", whose result {result!.Id} review {findings.Id} sent back")}")
                    : Make("next", findings is null
                        ? $"{Routing.NameOf(model, contributor)} holds it by assignment {holder!.Id} and goes on with it"
                        : $"review {findings.Id} found what to change in result {result!.Id}: {Routing.NameOf(model, contributor)} goes on with another attempt");
            }

            (string? principal, string role) = ByRole(step);
            string reason = step.Kind switch
            {
                "effect" => $"an outside effect by the {TJson.Text(TJson.Object(step.Element, "effect"), "route")} route, which a person carries in this build; the step counts once its done predicates hold",
                "exchange" => "an exchange: its export and its intake are carried by a person in this build, and the step counts once its done predicates hold",
                "flow" => $"another run, of flow {TJson.Text(TJson.Object(step.Element, "flow"), "id")} {TJson.Text(TJson.Object(step.Element, "flow"), "version")}, which its sponsor proposes and activates; the step counts once its done predicates hold",
                _ => role,
            };
            if (step.Kind is "effect" or "exchange" or "flow")
            {
                return Make("stopped", reason, principal);
            }

            if (principal is null || GoalRules.IsPerson(model, principal))
            {
                return Make("stopped", reason, principal);
            }

            return Make("next", step.Kind is "operation" or "decision"
                ? $"{role}; it is carried by its action{(step.Action is null ? string.Empty : ": " + step.Action)}, and counts once its done predicates hold"
                : role, principal);
        }

        /// <summary>
        /// Whether a step closes, derived in this one place (design/flow.md § 5): every required item satisfied, each with its provenance,
        /// evidence and review, the step's review the default for an item that declares none; its done predicates holding; for a work step
        /// its result completed with no findings standing; for a review step, its review of the exact result it reviews. A work step that
        /// declares no item takes the step's review on its result.
        /// </summary>
        private (bool Complete, List<Output> Outputs, List<ItemState> Items, RecordEnvelope? Review, RecordEnvelope? Findings, string? Pending) Completion(
            FlowStep step, string? item, List<RecordEnvelope> assignments, RecordEnvelope? result, List<Check> done)
        {
            bool doneMet = done.All(d => d.Verdict.Value == "met");
            IReadOnlySet<string> authors = Authors(assignments);
            RecordEnvelope? findings = null;
            RecordEnvelope? stepReview = null;
            if (step.Kind == "review")
            {
                StepInstance? reviewed = step.Inputs.Where(i => i.Step is not null).Select(i => Producer(i, item)).FirstOrDefault();
                RecordEnvelope? subject = reviewed?.Result;
                stepReview = subject is null ? null : Passing(subject, Authors(held.GetValueOrDefault(reviewed!.Ref) ?? []), flow.Role(step.Role)?.Holder == "person", Reviewers(step, assignments));
            }
            else if (step.Kind == "work" && result is not null)
            {
                findings = LatestReview(result, authors) is { } latest && EstateModel.BodyString(latest, "outcome") is "findings" or "fail" ? latest : null;
            }

            List<ItemState> items = step.Outputs.Select(o => Judge(step, o, result, done, assignments, authors, stepReview)).ToList();
            List<Output> outputs = items.Where(i => i.Value is not null && i.State != "off").Select(i => i.Value!).ToList();
            ItemState? open = items.FirstOrDefault(i => !i.Satisfied && i.State != "off");
            string? pending = open is null ? null : $"item {open.Declared.Name} is {open.State}: {open.Reasons[0]}";
            bool closed = doneMet && open is null;
            if (step.Kind == "review")
            {
                return (closed && stepReview is not null, outputs, items, stepReview, null, stepReview is null ? null : pending);
            }

            if (step.Kind != "work")
            {
                return (closed, outputs, items, null, null, pending);
            }

            bool completed = result is not null && EstateModel.BodyString(result, "outcome") == "completed" && findings is null;
            if (!completed || !closed || step.Outputs.Count > 0)
            {
                return (completed && closed, outputs, items, null, findings, completed ? pending : null);
            }

            FlowReview required = ReviewOf(step, null);
            if (required.Independence == "none")
            {
                return (true, outputs, items, null, null, null);
            }

            RecordEnvelope? review = Reviewed([(result!, result!.ComputedHash)], authors, result.By, required, out _, out _);
            return review is not null
                ? (true, outputs, items, review, null, null)
                : (false, outputs, items, null, null, $"its result awaits {Describe(required)} before it is handed on: a review of result {result.Id} with outcome pass");
        }

        /// <summary>
        /// One item's state (design/flow.md § 5): off when an extension or configuration turns it off; missing until it exists at an
        /// exact hash and is of its kind; unproven while the record that produced it does not show the declared provenance; unverified
        /// while a named check does not pass at its exact hash; and its review, the step's when it declares none, recorded at that hash
        /// by a principal of the reviewer role with the declared independence: in review until one passes, stale when only an earlier
        /// version passed, satisfied once one passes.
        /// </summary>
        private ItemState Judge(FlowStep step, FlowOutput output, RecordEnvelope? result, List<Check> done, List<RecordEnvelope> assignments, IReadOnlySet<string> authors, RecordEnvelope? stepReview)
        {
            if (configuration.ItemsOff.TryGetValue(step.Key + "." + output.Name, out string? off))
            {
                return new ItemState(output, "off", [off], null, null, null, []);
            }

            Output? value;
            string? missing;
            if (step.Kind == "review")
            {
                (value, missing) = stepReview is null
                    ? (null, $"no review of the exact result it reviews passes, by a principal of the {step.Role} role other than its author")
                    : (new Output(output.Name, output.Kind, stepReview, stepReview.ComputedHash, null), (string?)null);
            }
            else
            {
                (value, missing) = Resolve(output, result, done);
            }

            if (value is null)
            {
                return new ItemState(output, "missing", [missing!], null, null, null, []);
            }

            string producer = output.Kind == "decision" ? EstateModel.BodyString(value.Carrier, "decidedBy") ?? value.Carrier.By : value.Carrier.By;
            var reasons = new List<string>();
            string? state = null;
            List<string> unproven = Provenance(step, output, value, producer, assignments);
            if (unproven.Count > 0)
            {
                state = "unproven";
                reasons.AddRange(unproven);
            }

            // The versions of the item: for a file, every result of the step that carries it at the same hash; for a record, itself.
            List<(RecordEnvelope Subject, string Hash)> versions = output.Kind == "file"
                ? Results(assignments).Where(r => FileHash(r, output.Name) == value.Hash).Select(r => (r, r.ComputedHash)).ToList()
                : [(value.Carrier, value.Hash)];
            var evidence = new List<RecordEnvelope>();
            foreach (FlowEvidence declared in output.Evidence)
            {
                RecordEnvelope? verified = model.Current("verification").Where(v => v.Subject is not null && versions.Any(x => x.Subject.Id == v.Subject.Id && x.Hash == v.Subject.Hash)
                        && EstateModel.BodyString(v, "outcome") == "passed" && EstateModel.BodyString(v, "method") == declared.Method
                        && (declared.Command is null || EstateModel.BodyString(v, "command") == declared.Command)
                        && (declared.Role is null || EstateModel.BodyString(v, "role") == declared.Role)
                        && !(EstateModel.BodyString(v, "expires") is string expires && string.CompareOrdinal(expires, Time(at)) <= 0))
                    .OrderBy(v => v.At, StringComparer.Ordinal).ThenBy(v => v.Id, StringComparer.Ordinal).LastOrDefault();
                if (verified is null)
                {
                    state ??= "unverified";
                    reasons.Add($"its evidence {declared.Name} has no passing verification by {declared.Method}{(declared.Command is null ? string.Empty : $" running {declared.Command}")}{(declared.Role is null ? string.Empty : $" in the {declared.Role} role")} at its exact hash");
                }
                else
                {
                    evidence.Add(verified);
                }
            }

            RecordEnvelope? review = null;
            if (step.Kind != "review")
            {
                FlowReview required = ReviewOf(step, output);
                if (required.Independence != "none")
                {
                    var producers = new HashSet<string>(authors, StringComparer.Ordinal) { producer };
                    List<(RecordEnvelope Subject, string Hash)> earlier = output.Kind == "file"
                        ? Results(assignments).Where(r => FileHash(r, output.Name) is string hash && hash != value.Hash).Select(r => (r, r.ComputedHash)).ToList()
                        : model.Chain(value.Carrier).Where(id => id != value.Carrier.Id && model.ById.ContainsKey(id)).Select(id => (model.ById[id], model.ById[id].ComputedHash)).ToList();
                    review = Reviewed(versions, producers, producer, required, out RecordEnvelope? sentBack, out _);
                    RecordEnvelope? before = review is null ? Reviewed(earlier, producers, producer, required, out _, out _) : null;
                    if (review is null)
                    {
                        state ??= sentBack is null && before is not null ? "stale" : "in-review";
                        reasons.Add(sentBack is not null ? $"review {sentBack.Id} found what to change in it"
                            : before is not null ? $"it changed after review {before.Id} passed an earlier version, and awaits {Describe(required)} at its exact hash"
                            : $"it awaits {Describe(required)} at its exact hash");
                    }
                }
            }

            return new ItemState(output, state ?? "satisfied", reasons, value, producer, review, evidence);
        }

        /// <summary>
        /// Why an item's provenance does not hold: its producer is not the declared principal, of the declared holder kind or named in the
        /// declared role; or the record that produced it does not show the declared inputs by hash, provider, tool or settings, as a
        /// result's or an operation's provenance records them, and else its assignment's pins and the run that carried it.
        /// </summary>
        private List<string> Provenance(FlowStep step, FlowOutput output, Output value, string producer, List<RecordEnvelope> assignments)
        {
            var reasons = new List<string>();
            bool person = GoalRules.IsPerson(model, producer);
            if (output.Kind is "attestation" or "decision" && !person)
            {
                reasons.Add($"a{(output.Kind == "attestation" ? "n attestation" : " decision")} is a person's, and {Routing.NameOf(model, producer)} is not a person");
            }

            if (output.Provenance is not FlowProvenance provenance)
            {
                return reasons;
            }

            if (provenance.Principal is string principal)
            {
                if (model.CurrentPrincipal(principal) is not RecordEnvelope named)
                {
                    reasons.Add($"it is to be produced by {principal}, which is not a principal of this estate");
                }
                else if (model.Chain(named)[0] != producer && named.Id != producer && principal != producer)
                {
                    reasons.Add($"it is to be produced by {Routing.NameOf(model, principal)}, and {Routing.NameOf(model, producer)} produced it");
                }
            }

            if (provenance.Holder is "person" && !person || provenance.Holder is "agent" && (model.CurrentPrincipal(producer) is not RecordEnvelope who || EstateModel.BodyString(who, "kind") != "agent"))
            {
                reasons.Add($"it is to be produced by {(provenance.Holder == "person" ? "a person" : "an agent")}, and {Routing.NameOf(model, producer)} produced it");
            }

            if (provenance.Role is string role && !Routing.Principals(model, goal, role).Principals.Contains(producer, StringComparer.Ordinal)
                && !(role == step.Role && assignments.Any(a => EstateModel.BodyString(a, "contributor") == producer)))
            {
                reasons.Add($"it is to be produced in the {role} role, and {Routing.NameOf(model, producer)} is not named in it");
            }

            // The record that produced it: a file's result, or a record item's own record, a result or an operation recording its provenance.
            JsonElement recorded = TJson.Object(value.Carrier.Body, "provenance");
            RecordEnvelope? assignment = output.Kind == "file" && WorkView.AttemptOfResult(model, value.Carrier) is RecordEnvelope attempt ? WorkView.AssignmentOfAttempt(model, attempt) : null;
            foreach (string input in provenance.Inputs)
            {
                if (!TJson.Array(recorded, "inputs").Any(p => TJson.Text(p, "name") == input)
                    && (assignment is null || !TJson.Array(TJson.Object(assignment.Body, "step"), "inputs").Any(p => TJson.Text(p, "name") == input)))
                {
                    reasons.Add($"it is to come from input {input}, and the record that produced it pins no hash of it");
                }
            }

            AgentRuns.Run? run = null;
            if (assignment is not null && (provenance.Provider is not null || provenance.Tool is not null || provenance.Settings.ValueKind == JsonValueKind.Object))
            {
                IReadOnlyList<string> chain = model.Chain(model.Latest(assignment));
                run = AgentRuns.All(model).Where(r => r.Assignment is not null && chain.Contains(r.Assignment) && r.Intent.AtTime <= value.Carrier.AtTime)
                    .OrderBy(r => r.Intent.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
            }

            JsonElement requested = run is null ? default : TJson.Object(TJson.Object(run.Intent.Body, "execution"), "requested");
            var wanted = new List<(string Name, string Value, string? Recorded)>();
            if (provenance.Provider is string provider)
            {
                wanted.Add(("provider", provider, TJson.Text(recorded, "provider") ?? (value.Carrier.Type == "operation" ? EstateModel.BodyString(value.Carrier, "provider") : null) ?? TJson.Text(requested, "provider")));
            }

            if (provenance.Tool is string tool)
            {
                wanted.Add(("tool", tool, TJson.Text(recorded, "tool") ?? run?.Launcher));
            }

            if (provenance.Settings.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty setting in provenance.Settings.EnumerateObject())
                {
                    wanted.Add(("setting " + setting.Name, setting.Value.GetString()!, TJson.Text(TJson.Object(recorded, "settings"), setting.Name) ?? TJson.Text(requested, setting.Name)));
                }
            }

            foreach ((string name, string expected, string? actual) in wanted.Where(w => w.Recorded != w.Value))
            {
                reasons.Add(actual is null
                    ? $"it is to be produced with {name} {expected}, and the record that produced it shows none"
                    : $"it is to be produced with {name} {expected}, and the record that produced it shows {actual}");
            }

            return reasons;
        }

        /// <summary>
        /// The latest review that passes one of an item's versions at its exact hash under its requirement; and the latest qualifying
        /// review of them that found what to change, when it is later than every pass.
        /// </summary>
        private RecordEnvelope? Reviewed(List<(RecordEnvelope Subject, string Hash)> versions, IReadOnlySet<string> producers, string producer, FlowReview required, out RecordEnvelope? sentBack, out RecordEnvelope? latest)
        {
            HashSet<string> named = required.Role is string role ? Routing.Principals(model, goal, role).Principals.ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
            List<RecordEnvelope> qualifying = model.Current("review")
                .Where(r => r.Subject is not null && versions.Any(v => v.Subject.Id == r.Subject.Id && v.Hash == r.Subject.Hash) && Qualifies(r.By, producers, producer, required) && (required.Role is null || named.Contains(r.By)))
                .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
            latest = qualifying.LastOrDefault();
            sentBack = latest is not null && EstateModel.BodyString(latest, "outcome") is "findings" or "fail" ? latest : null;
            return latest is not null && EstateModel.BodyString(latest, "outcome") == "pass" ? latest : null;
        }

        /// <summary>Whether a reviewer meets an independence: self admits anyone, the author's own check; independent another principal than the producers; person a person among them; other-harness an agent on another harness than the producer's.</summary>
        private bool Qualifies(string reviewer, IReadOnlySet<string> producers, string producer, FlowReview required)
        {
            if (required.Independence == "self")
            {
                return true;
            }

            if (producers.Contains(reviewer) || reviewer == producer)
            {
                return false;
            }

            return required.Independence switch
            {
                "person" => GoalRules.IsPerson(model, reviewer),
                "other-harness" => model.CurrentPrincipal(reviewer) is RecordEnvelope agent && EstateModel.BodyString(agent, "kind") == "agent"
                    && Harness(agent) is string harness && Harness(model.CurrentPrincipal(producer)) != harness,
                _ => true,
            };

            static string? Harness(RecordEnvelope? principal) => principal is null ? null : TJson.Text(TJson.Object(principal.Body, "agent"), "product");
        }

        private static string Describe(FlowReview review) => review.Independence switch
        {
            "self" => "the author's own review, labeled self",
            "person" => "a person's review",
            "other-harness" => "an independent review on another harness",
            _ => "an independent review",
        } + (review.Role is null ? string.Empty : $" in the {review.Role} role");

        /// <summary>Every current result of a step's assignments, oldest first.</summary>
        private List<RecordEnvelope> Results(List<RecordEnvelope> assignments) =>
            assignments.SelectMany(a => WorkView.AttemptsOf(model, a)).Select(a => WorkView.ResultOf(model, a)).OfType<RecordEnvelope>()
                .Where(r => EstateModel.BodyString(r, "outcome") == "completed").OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();

        private static string? FileHash(RecordEnvelope result, string name) =>
            TJson.Text(TJson.Array(result.Body, "outputs").FirstOrDefault(o => TJson.Text(o, "name") == name), "hash");

        /// <summary>
        /// One declared item as it exists now, at an exact hash and of its kind, or why it does not: a file by name and hash among a work
        /// step's completed result's outputs, of its declared media type; any other kind, the newest record of its type (and record kind)
        /// the done predicates count: an observation an operation observed to succeed or an observation record, a verification that
        /// passed, a decision, an attestation or a record of its declared type.
        /// </summary>
        private (Output? Output, string? Missing) Resolve(FlowOutput output, RecordEnvelope? result, List<Check> done)
        {
            if (output.Kind == "file")
            {
                JsonElement named = result is null || EstateModel.BodyString(result, "outcome") != "completed" ? default : TJson.Array(result.Body, "outputs").FirstOrDefault(o => TJson.Text(o, "name") == output.Name);
                string? hash = TJson.Text(named, "hash");
                string? media = TJson.Text(named, "mediaType");
                return hash is not null && (output.MediaType is null || media == output.MediaType)
                    ? (new Output(output.Name, "file", result!, hash, media), null)
                    : (null, hash is null ? $"its result names no output {output.Name}" : $"its output {output.Name} is {media ?? "of no stated media type"}, not {output.MediaType}");
            }

            string[] types = (output.Counts ?? string.Empty).Split('|');
            RecordEnvelope? record = done.SelectMany(d => GoalPredicates.Matching(model, d.Predicate))
                .Where(r => types.Contains(r.Type, StringComparer.Ordinal) && (output.RecordKind is null || EstateModel.BodyString(r, "kind") == output.RecordKind))
                .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
            if (record is not null && output.Kind == "verification" && EstateModel.BodyString(record, "outcome") != "passed")
            {
                return (null, $"verification {record.Id} is {EstateModel.BodyString(record, "outcome")}, not passed");
            }

            if (record is not null && output.Kind == "observation" && record.Type == "operation" && EstateModel.BodyString(record, "status") != "observed-success")
            {
                return (null, $"operation {record.Id} is {EstateModel.BodyString(record, "status")}, not observed to succeed");
            }

            return record is null
                ? (null, $"no {string.Join(" or ", types)} record its done predicates count is its item {output.Name}")
                : (new Output(output.Name, output.Kind, record, record.ComputedHash, null), null);
        }

        /// <summary>The role's principal from the records, as the roster names it, skipping an agent in a role the flow marks person; and in words.</summary>
        private (string? Principal, string Reason) ByRole(FlowStep step)
        {
            (IReadOnlyList<string> principals, Routing.Source source) = Routing.Principals(model, goal, step.Role);
            bool person = flow.Role(step.Role)?.Holder == "person";
            string? first = principals.FirstOrDefault(p => model.CurrentPrincipal(p) is not null && (!person || GoalRules.IsPerson(model, p)));
            if (first is null)
            {
                return (null, $"no principal of the {step.Role} role is named{(person ? ", a person's role," : string.Empty)} by {source.Description}: the sponsor names one in the roster or the goal's roles");
            }

            return GoalRules.IsPerson(model, first)
                ? (first, $"a person holds the {step.Role} role: {Routing.NameOf(model, first)}, from {source.Description}")
                : (first, $"routed by the {step.Role} role to {Routing.NameOf(model, first)}, from {source.Description}");
        }

        /// <summary>The steps an instance waits on: those it names or the step before it, through any that are off, and the producers of its inputs.</summary>
        private IEnumerable<StepInstance> Dependencies(FlowStep step, string? item)
        {
            var seen = new HashSet<FlowRuns.StepRef>();
            foreach (StepInstance wait in step.After.SelectMany(key => Through(key, step.Each, item, seen)))
            {
                yield return wait;
            }

            foreach (FlowInput input in step.Inputs.Where(i => i.Step is not null))
            {
                if (Producer(input, item) is StepInstance producer && seen.Add(producer.Ref))
                {
                    yield return producer;
                }
            }

            if (TJson.Text(TJson.Object(step.When, "due"), "from") is string from)
            {
                foreach (StepInstance wait in Through(from, step.Each, item, seen))
                {
                    yield return wait;
                }
            }
        }

        private IEnumerable<StepInstance> Through(string key, string? each, string? item, HashSet<FlowRuns.StepRef> seen)
        {
            FlowStep? waited = flow.Step(key);
            if (waited is null)
            {
                yield break;
            }

            IEnumerable<StepInstance> instances = waited.Each is not null && waited.Each == each
                ? derived.Values.Where(d => d.Step.Key == key && d.Instance == item)
                : derived.Values.Where(d => d.Step.Key == key);
            foreach (StepInstance instance in instances.ToList())
            {
                if (instance.State == "off")
                {
                    foreach (StepInstance earlier in waited.After.SelectMany(k => Through(k, waited.Each, instance.Instance, seen)))
                    {
                        yield return earlier;
                    }
                }
                else if (seen.Add(instance.Ref))
                {
                    yield return instance;
                }
            }
        }

        /// <summary>The instance of an input's producing step: the same item for a step that runs over the same list, or its one instance.</summary>
        private StepInstance? Producer(FlowInput input, string? item) => input.Step is null ? null : InstanceOf(input.Step, item);

        private StepInstance? InstanceOf(string key, string? item) =>
            derived.GetValueOrDefault(new FlowRuns.StepRef(key, flow.Step(key)?.Each is null ? null : item));

        /// <summary>
        /// The step's inputs now, against the pins of its holding assignment's brief; a step done without an assignment used its inputs
        /// as they stood when it was done, so an input carried by a later record changed after use.
        /// </summary>
        private List<Input> Inputs(FlowStep step, string? item, RecordEnvelope? holder, DateTimeOffset? usedAt)
        {
            Dictionary<string, RecordReference> pins = holder is null ? [] : TJson.Array(TJson.Object(holder.Body, "step"), "inputs")
                .Where(i => TJson.Text(i, "name") is not null && TJson.Text(i, "type") is not null && TJson.Text(i, "id") is not null)
                .GroupBy(i => TJson.Text(i, "name")!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => new RecordReference(TJson.Text(g.First(), "type")!, TJson.Text(g.First(), "id")!, TJson.Text(g.First(), "hash")), StringComparer.Ordinal);
            var inputs = new List<Input>();
            JsonElement subject = TJson.Object(FlowRuns.Plan(goal), "subject");
            foreach (FlowInput input in step.Inputs)
            {
                RecordReference? pinned = pins.GetValueOrDefault(input.Name);
                if (input.From == "subject")
                {
                    JsonElement bound = TJson.Object(subject, input.Field!);
                    RecordEnvelope? record = TJson.Text(bound, "id") is string id && model.ById.TryGetValue(id, out RecordEnvelope? found) ? found : null;
                    inputs.Add(new Input(input.Name, "subject", "subject " + input.Field, record, record?.ComputedHash, pinned, null));
                    continue;
                }

                StepInstance? producer = Producer(input, item);
                Output? now = producer?.Outputs.FirstOrDefault(o => o.Name == input.Output);
                string source = $"{producer?.Name ?? input.Step} output {input.Output}";
                string? change = null;
                if (now is not null && pinned is not null)
                {
                    string? before = now.Kind == "file" && model.ById.TryGetValue(pinned.Id, out RecordEnvelope? carrier)
                        ? TJson.Text(TJson.Array(carrier.Body, "outputs").FirstOrDefault(o => TJson.Text(o, "name") == input.Output), "hash")
                        : pinned.Hash;
                    if (now.Kind == "file" ? before != now.Hash : (pinned.Id != now.Carrier.Id || pinned.Hash != now.Hash))
                    {
                        change = $"its input {input.Name} changed after use: its brief pinned {pinned.Type} {pinned.Id}{(before is null ? string.Empty : " at " + before)}, and {source} is now {now.Hash} in {now.Carrier.Type} {now.Carrier.Id}";
                    }
                }
                else if (now is not null && pinned is null && holder is not null && now.Carrier.AtTime > holder.AtTime)
                {
                    // An assignment made without a pin of the input cannot have used an output recorded after it.
                    change = $"its input {input.Name} changed after use: {source} is now {now.Carrier.Type} {now.Carrier.Id} of {now.Carrier.At}, after assignment {holder.Id}, which pins no input {input.Name}, was made";
                }
                else if (now is not null && usedAt is DateTimeOffset used && now.Carrier.AtTime > used)
                {
                    change = $"its input {input.Name} changed after use: {source} is now {now.Carrier.Type} {now.Carrier.Id} of {now.Carrier.At}, after the step was done at {Time(used)}";
                }

                inputs.Add(new Input(input.Name, "step", source, now?.Carrier, now?.Hash, pinned, change));
            }

            return inputs;
        }

        /// <summary>The review an item takes: its own; else its step's; else, for a work step, the flow's review option; else none.</summary>
        private FlowReview ReviewOf(FlowStep step, FlowOutput? output) =>
            output?.Review ?? step.Review ?? (step.Kind == "work" && configuration.Value("review") is string option && FlowContract.Reviews.Contains(option) ? new FlowReview(option, null) : new FlowReview("none", null));

        /// <summary>
        /// The latest current review of a result by a principal other than its authors, at any revision of it, among the admitted reviewers
        /// when they are given; null when there is none.
        /// </summary>
        private RecordEnvelope? LatestReview(RecordEnvelope result, IReadOnlySet<string> authors, IReadOnlySet<string>? admitted = null)
        {
            IReadOnlyList<string> chain = model.Chain(model.Latest(result));
            return model.Current("review").Where(r => r.Subject is not null && chain.Contains(r.Subject.Id) && !authors.Contains(r.By) && r.By != result.By
                    && (admitted is null || admitted.Contains(r.By)))
                .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
        }

        /// <summary>
        /// Who may carry a review step's review: a principal its role names, through the goal's roles or the roster, or a contributor of
        /// the review step's own assignments.
        /// </summary>
        private HashSet<string> Reviewers(FlowStep step, List<RecordEnvelope> assignments)
        {
            var reviewers = Routing.Principals(model, goal, step.Role).Principals.ToHashSet(StringComparer.Ordinal);
            reviewers.UnionWith(assignments.Select(a => EstateModel.BodyString(a, "contributor")).OfType<string>());
            return reviewers;
        }

        /// <summary>
        /// The latest independent review of a result by an admitted reviewer when it passes the result's exact hash, by a person when one
        /// is required; else null.
        /// </summary>
        private RecordEnvelope? Passing(RecordEnvelope result, IReadOnlySet<string> authors, bool person, IReadOnlySet<string> admitted)
        {
            RecordEnvelope? latest = LatestReview(result, authors, admitted);
            return latest is not null && EstateModel.BodyString(latest, "outcome") == "pass" && latest.Subject?.Id == result.Id && latest.Subject.Hash == result.ComputedHash
                && (!person || GoalRules.IsPerson(model, latest.By)) ? latest : null;
        }

        /// <summary>The authors of a step's work: each assignment's contributor and who recorded its attempts and results.</summary>
        private HashSet<string> Authors(IEnumerable<RecordEnvelope> assignments)
        {
            var authors = new HashSet<string>(StringComparer.Ordinal);
            foreach (RecordEnvelope assignment in assignments)
            {
                if (EstateModel.BodyString(assignment, "contributor") is string contributor)
                {
                    authors.Add(contributor);
                }

                foreach (RecordEnvelope attempt in WorkView.AttemptsOf(model, assignment))
                {
                    authors.Add(attempt.By);
                    if (WorkView.ResultOf(model, attempt) is RecordEnvelope result)
                    {
                        authors.Add(result.By);
                    }
                }
            }

            return authors;
        }

        private RecordEnvelope? LatestResult(RecordEnvelope assignment)
        {
            IReadOnlyList<RecordEnvelope> attempts = WorkView.AttemptsOf(model, assignment);
            return attempts.Count == 0 ? null : WorkView.ResultOf(model, attempts[^1]);
        }

        private List<string> Failing(IEnumerable<JsonElement> predicates, IReadOnlyDictionary<string, string> bindings) =>
            predicates.Select(p => GoalPredicates.EvaluatePredicate(model, FlowRuns.Bind(p, bindings))).Where(v => v.Value != "met").Select(v => $"{v.Value}: {v.Reason}").ToList();

        private static string? Who(RecordEnvelope? holder) => holder is null ? null : EstateModel.BodyString(holder, "contributor");

        private DateTimeOffset? Activated() =>
            GoalRules.Statuses(model, goal).FirstOrDefault(s => EstateModel.BodyString(s, "status") == "active")?.AtTime;

        private static string Completed(FlowStep step, RecordEnvelope? result, RecordEnvelope? review, List<Check> done)
        {
            var parts = new List<string>();
            if (result is not null && step.Kind == "work")
            {
                parts.Add($"result {result.Id} completed");
            }

            if (review is not null)
            {
                parts.Add($"review {review.Id} passed it");
            }

            if (done.Count > 0)
            {
                parts.Add("its done predicates hold");
            }

            return parts.Count == 0 ? "done" : string.Join("; ", parts);
        }
    }

    private static string Time(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>The run's position as text: the flow, its configuration and every step with its state and why.</summary>
    public static string RenderText(EstateModel model, Position position)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(position);
        var text = new StringBuilder();
        TemplatePin? pin = FlowRuns.Pin(position.Goal);
        if (position.Flow is null)
        {
            text.Append("Flow: ").Append(pin is null ? "none" : $"{pin.Id} {pin.Version} ({pin.Hash})").Append(", not run here: ").Append(position.Unavailable).Append('\n');
            return text.ToString();
        }

        List<StepInstance> running = position.Running.ToList();
        text.Append("Flow: ").Append(position.Flow.Pin.Id).Append(' ').Append(position.Flow.Pin.Version).Append(" (").Append(position.Flow.Pin.Hash).Append("), ")
            .Append(running.Count(s => s.State == "done").ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(running.Count.ToString(CultureInfo.InvariantCulture))
            .Append(running.Count == 1 ? " step" : " steps").Append(" done").Append(position.Complete ? "; the run is complete, and the goal's evaluation and acceptance remain" : string.Empty).Append('\n');
        if (position.Configuration!.Until is string until)
        {
            text.Append("  Ends at: ").Append(until).Append('\n');
        }

        List<FlowRuns.Setting> configured = position.Configuration.Values.Values.Where(v => v.Record is not null).OrderBy(v => v.Option, StringComparer.Ordinal).ToList();
        text.Append("  Configuration: ").Append(configured.Count == 0 ? "the flow's defaults" : string.Join("; ", configured.Select(v => $"{v.Option} {v.Value}, from {v.Source}"))).Append('\n');
        List<StepInstance> next = running.Where(s => s.State == "next").ToList();
        text.Append("  Next: ").Append(next.Count == 0 ? "none" : string.Join(", ", next.Select(s => s.Name))).Append('\n');
        int index = 0;
        foreach (StepInstance step in position.Steps)
        {
            text.Append("  ").Append((++index).ToString(CultureInfo.InvariantCulture)).Append(". ").Append(step.Name).Append("  ").Append(step.State).Append("  ").Append(step.Step.Title)
                .Append(" [").Append(step.Step.Kind).Append(", ").Append(step.Step.Role).Append(step.Step.Required ? string.Empty : ", optional").Append(step.Step.Reserved is null ? string.Empty : ", reserved " + step.Step.Reserved).Append("]\n");
            foreach (string reason in step.Reasons)
            {
                text.Append("       ").Append(reason).Append('\n');
            }

            foreach (ItemState item in step.Items)
            {
                text.Append("       item ").Append(item.Declared.Name).Append(item.Declared.Required ? string.Empty : " (optional)").Append(": ").Append(item.State);
                if (item.Value is Output value)
                {
                    text.Append(", ").Append(value.Hash).Append(" in ").Append(value.Carrier.Type).Append(' ').Append(value.Carrier.Id);
                }

                text.Append(item.Reasons.Count == 0 ? string.Empty : "; " + string.Join("; ", item.Reasons)).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>The run's position as JSON, the same on every machine for the same records and time.</summary>
    public static JsonObject RenderJson(EstateModel model, Position position)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(position);
        TemplatePin? pin = position.Flow?.Pin ?? FlowRuns.Pin(position.Goal);
        var json = new JsonObject
        {
            ["pin"] = pin is null ? null : new JsonObject { ["id"] = pin.Id, ["version"] = pin.Version, ["hash"] = pin.Hash },
            ["available"] = position.Flow is not null,
            ["unavailable"] = position.Unavailable,
        };
        if (position.Flow is null)
        {
            return json;
        }

        json["chain"] = new JsonArray(position.Flow.Chain.Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["version"] = c.Version, ["hash"] = c.Hash }).ToArray());
        json["work"] = position.Work is null ? null : new JsonObject { ["id"] = position.Work.Id, ["key"] = EstateModel.BodyString(position.Work, "key") };
        json["until"] = position.Configuration!.Until;
        json["configuration"] = new JsonArray(position.Configuration.Values.Values.OrderBy(v => v.Option, StringComparer.Ordinal).Select(v => (JsonNode)new JsonObject
        {
            ["option"] = v.Option,
            ["value"] = v.Value,
            ["source"] = v.Source,
            ["record"] = v.Record is null ? null : new JsonObject { ["type"] = v.Record.Type, ["id"] = v.Record.Id, ["hash"] = v.Record.ComputedHash },
        }).ToArray());
        json["complete"] = position.Complete;
        json["next"] = new JsonArray(position.Steps.Where(s => s.State == "next").Select(s => (JsonNode)s.Name).ToArray());
        json["steps"] = new JsonArray(position.Steps.Select(s => (JsonNode)new JsonObject
        {
            ["key"] = s.Step.Key,
            ["instance"] = s.Instance,
            ["title"] = s.Step.Title,
            ["kind"] = s.Step.Kind,
            ["role"] = s.Step.Role,
            ["required"] = s.Step.Required,
            ["reserved"] = s.Step.Reserved,
            ["state"] = s.State,
            ["reasons"] = new JsonArray(s.Reasons.Select(r => (JsonNode)r).ToArray()),
            ["principal"] = s.Principal,
            ["principalName"] = s.Principal is null ? null : Routing.NameOf(model, s.Principal),
            ["assignment"] = Reference(s.Assignment),
            ["result"] = Reference(s.Result),
            ["review"] = Reference(s.Review),
            ["outputs"] = new JsonArray(s.Outputs.Select(o => (JsonNode)new JsonObject { ["name"] = o.Name, ["kind"] = o.Kind, ["hash"] = o.Hash, ["mediaType"] = o.MediaType, ["carrier"] = Reference(o.Carrier) }).ToArray()),
            ["items"] = new JsonArray(s.Items.Select(i => (JsonNode)new JsonObject
            {
                ["name"] = i.Declared.Name,
                ["kind"] = i.Declared.Kind,
                ["required"] = i.Declared.Required,
                ["state"] = i.State,
                ["reasons"] = new JsonArray(i.Reasons.Select(r => (JsonNode)r).ToArray()),
                ["hash"] = i.Value?.Hash,
                ["carrier"] = Reference(i.Value?.Carrier),
                ["producer"] = i.Producer,
                ["review"] = Reference(i.Review),
                ["evidence"] = new JsonArray(i.Evidence.Select(e => (JsonNode)Reference(e)!).ToArray()),
            }).ToArray()),
            ["inputs"] = new JsonArray(s.Inputs.Select(i => (JsonNode)new JsonObject
            {
                ["name"] = i.Name,
                ["from"] = i.From,
                ["source"] = i.Source,
                ["hash"] = i.Hash,
                ["carrier"] = Reference(i.Carrier),
                ["pinned"] = i.Pinned is null ? null : new JsonObject { ["type"] = i.Pinned.Type, ["id"] = i.Pinned.Id, ["hash"] = i.Pinned.Hash },
                ["change"] = i.Change,
            }).ToArray()),
            ["done"] = new JsonArray(s.Done.Select(d => (JsonNode)new JsonObject { ["description"] = GoalPredicates.DescribePredicate(d.Predicate), ["verdict"] = d.Verdict.Value, ["reason"] = d.Verdict.Reason }).ToArray()),
            ["doneAt"] = s.DoneAt is DateTimeOffset doneAt ? Time(doneAt) : null,
            ["recovery"] = s.Step.Recovery,
        }).ToArray());
        return json;

        static JsonObject? Reference(RecordEnvelope? record) => record is null ? null : new JsonObject { ["type"] = record.Type, ["id"] = record.Id, ["hash"] = record.ComputedHash };
    }
}
