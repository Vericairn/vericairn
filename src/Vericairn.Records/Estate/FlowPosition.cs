// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// A run's position (the estate's design/flow.md §§ 5 to 8; design/release-procedure.md § 5): each step of its flow, once or once
/// per item, with its state derived from the records whenever it is asked and never stored, so a clone, a restore or a second machine
/// derives the same position. A step is done when its done predicates hold and each declared output exists at an exact hash, reviewed
/// where declared; stale when an input it used changed after use; blocked, with the failing predicate, while what it waits on is not
/// done or its preconditions fail; in review while its output awaits review; stopped while a person holds its role, it is a reserved
/// act, or required attention arises; next otherwise; and off when the run's configuration leaves it out. Retries go by outcome
/// (§ 7): an uncertain effect stops the step until it is reconciled by reading; a failed run backs off within the step's attempts;
/// findings start another attempt within them; a failed step takes its failure route; and a spent retry budget stops it for the
/// sponsor, only what depends on it waiting. Every attempt stays recorded and shown. Reading grants nothing.
/// </summary>
public static partial class FlowPosition
{
    /// <summary>The states of a step of a run, in the state language's flow-step vocabulary.</summary>
    public static readonly ImmutableArray<string> States = ["done", "next", "blocked", "stale", "in-review", "stopped", "off"];

    /// <summary>
    /// The states of an item a step produces or collects (design/flow.md § 5): satisfied; missing; unproven (its provenance does not
    /// hold); unverified (a declared verification does not pass at its exact hash); in review (it awaits its review, or its review found
    /// what to change); stale (it changed after its review); and off (configured off, an optional item only).
    /// </summary>
    public static readonly ImmutableArray<string> ItemStates = ["satisfied", "missing", "unproven", "unverified", "in-review", "stale", "off"];

    /// <summary>
    /// The kinds of required attention a run raises, each an ask to the sponsor (design/flow.md §§ 7 and 8): an uncertain effect, a failed
    /// step whose failure route stops, a spent retry budget, the goal's limit of consecutive failed steps, and an evaluation of impossible.
    /// </summary>
    public static readonly ImmutableArray<string> AttentionKinds = ["uncertain", "failed", "exhausted", "consecutive-failures", "impossible"];

    /// <summary>The wait before the first run after a failed run; each further failed run in a row doubles it (design/flow.md § 7).</summary>
    public static readonly TimeSpan RetryBackoff = TimeSpan.FromMinutes(1);

    /// <summary>The longest wait between runs after failed runs.</summary>
    public static readonly TimeSpan RetryBackoffLimit = TimeSpan.FromHours(1);

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

        /// <summary>The review it takes, its step's when it declares none, with the reviewer role it names or the estate's reviewer role.</summary>
        public FlowReview? Requirement { get; init; }
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

    /// <summary>One attempt at a step instance's work, as recorded: a later attempt never removes it (design/flow.md § 7).</summary>
    /// <param name="Assignment">The assignment it was made under.</param>
    /// <param name="Attempt">The attempt.</param>
    /// <param name="Result">Its result, at its current revision; null while it has none.</param>
    public sealed record AttemptState(RecordEnvelope Assignment, RecordEnvelope Attempt, RecordEnvelope? Result)
    {
        /// <summary>Its result's outcome, or open while it has none.</summary>
        public string Outcome => Result is null ? "open" : EstateModel.BodyString(Result, "outcome") ?? "?";
    }

    /// <summary>
    /// A review before hand-off a step instance waits on, which routing dispatches to its reviewer role (design/flow.md § 6): the items
    /// it covers, their one review requirement, the exact subject that carries them, the step's authors, who never review it unless the
    /// requirement is self, and the assignments that already carry it.
    /// </summary>
    /// <param name="Items">The items, by name; empty for a step that declares none, whose result it reviews.</param>
    /// <param name="Requirement">Their review requirement.</param>
    /// <param name="Role">The role whose principals review: the one the requirement names, or the estate's reviewer role.</param>
    /// <param name="Subject">The exact record that carries the items: a file's result, or the item's record.</param>
    /// <param name="Authors">The authors of the step's work.</param>
    /// <param name="Assignments">The review assignments of this subject and these items, oldest first; the last one holds the review.</param>
    public sealed record PendingReview(IReadOnlyList<string> Items, FlowReview Requirement, string Role, RecordEnvelope Subject, IReadOnlySet<string> Authors, IReadOnlyList<RecordEnvelope> Assignments)
    {
        /// <summary>The subject's exact hash.</summary>
        public string Hash => Subject.ComputedHash;

        /// <summary>What is reviewed, in words.</summary>
        public string What => Items.Count == 0 ? $"result {Subject.Id}" : $"item{(Items.Count == 1 ? string.Empty : "s")} {string.Join(", ", Items)} in {Subject.Type} {Subject.Id}";
    }

    /// <summary>Required attention a run raises (design/flow.md §§ 7 and 8), which becomes an ask to the sponsor.</summary>
    /// <param name="Kind">One of <see cref="AttentionKinds"/>.</param>
    /// <param name="Trigger">The record that raised it, which the ask names as its subject.</param>
    /// <param name="Summary">Why, in words.</param>
    public sealed record Attention(string Kind, RecordEnvelope Trigger, string Summary);

    /// <summary>A failure the latest work of a step instance stands on, and the route it takes (design/flow.md § 7).</summary>
    /// <param name="Record">The failing record: a result that is blocked or abandoned, or a review that failed it.</param>
    /// <param name="Summary">Why, in words.</param>
    /// <param name="Route">Its failure route.</param>
    public sealed record Failure(RecordEnvelope Record, string Summary, FlowFailure Route);

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

        /// <summary>Every attempt at its work, under each assignment that carried it, oldest first: no attempt or result is ever lost.</summary>
        public IReadOnlyList<AttemptState> Attempts { get; init; } = [];

        /// <summary>The agent runs its work has had, charged, counted against the goal's attempts per step across every assignment of it.</summary>
        public int AttemptsUsed { get; init; }

        /// <summary>The attempts per step the goal's budget grants.</summary>
        public int AttemptsAllowed { get; init; }

        /// <summary>The reviews before hand-off it waits on, which routing dispatches.</summary>
        public IReadOnlyList<PendingReview> Reviews { get; init; } = [];

        /// <summary>The required attention it raises, which becomes an ask to the sponsor; null when it raises none.</summary>
        public Attention? Attention { get; init; }

        /// <summary>The failure its latest work stands on; null when it stands on none.</summary>
        public Failure? Failed { get; init; }

        /// <summary>True when it reruns from a fresh assignment: stale, reopened by a failure route, or rerun after one.</summary>
        public bool Rerun { get; init; }

        /// <summary>
        /// Why a stopped step waits on a person, one of <see cref="FlowStops.Kinds"/> (design/flow.md § 8): a person holds its role or
        /// its assignment, it is a reserved act, or required attention arises; null when it is not stopped.
        /// </summary>
        public string? Stop { get; init; }

        /// <summary>
        /// For a reserved step that is next, the exact command the operator runs, computed from the records as they stand now and never
        /// before the step is next (design/release-procedure.md § 6); null for every other step.
        /// </summary>
        public string? Command { get; init; }

        /// <summary>
        /// For a reserved step, when it was asked: the current goal revision, or the latest time a step it waits on was done, whichever is
        /// later; a close of its request made earlier never answers this ask. Null for every other step.
        /// </summary>
        public DateTimeOffset? AskedSince { get; init; }
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
        public StepInstance? Find(FlowRuns.StepRef step) => Steps.FirstOrDefault(s => s.Ref == step.Work);

        /// <summary>
        /// The required attention the whole run raises: the goal's limit of consecutive failed steps reached, or an evaluation of
        /// impossible. Each fences the run's dispatch until the sponsor answers its ask.
        /// </summary>
        public IReadOnlyList<Attention> RunAttention { get; init; } = [];

        /// <summary>
        /// The run's stops, in the order the flow reaches them (design/flow.md § 8): each step, review or run-wide matter that waits on a
        /// person, with who it waits on, why and how it reaches them, at most three delivered to each person at a time.
        /// </summary>
        public IReadOnlyList<FlowStops.RunStop> Stops { get; init; } = [];

        /// <summary>The time the position was derived at: the read's, or for canonical status the records' own (<see cref="FlowStops.RecordedTime"/>).</summary>
        public DateTimeOffset At { get; init; }

        /// <summary>
        /// How many stops reach each person across every run, delivered or due: at most <see cref="FlowStops.PerDelivery"/>, the rest
        /// queued; what routing counts before it lets one more reach a person.
        /// </summary>
        public IReadOnlyDictionary<string, int> Reaching { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Whether a principal holds a reserved act now (design/flow.md § 8; design/operating-modes.md § 6): a person with the operator role,
    /// to whom a reserved step's request is put. No grant of a reserved act takes effect before ID-04 makes its scope a checked name, so
    /// no agent holds one in this build. Whether a record is the act itself is <see cref="IsReservedAct"/>.
    /// </summary>
    public static bool HoldsReservedAct(EstateModel model, string? principal)
    {
        ArgumentNullException.ThrowIfNull(model);
        return GoalRules.IsPerson(model, principal) && model.HasRole(principal, Roles.Operator);
    }

    /// <summary>
    /// Whether a record is an act of a reserved step's holder (design/flow.md § 8): its signatures are authentic, so its author is who it
    /// claims, and that author was a person with the operator role, by an authentic chain of principal revisions, when it was made. A
    /// record's claimed author, an author's role granted later, and an unsigned or altered record never count; a past act of an operator
    /// whose role was revoked afterwards still does when signed history places it before the revocation (PolicyAuthority.OperatorAt).
    /// </summary>
    public static bool IsReservedAct(EstateModel model, RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(record);
        return SignatureChecks.IsAuthentic(model, record) && PolicyAuthority.OperatorAt(model, record);
    }

    /// <summary>Derives a run's position from the records at a time (due times, watches and backoffs read it); nothing is stored.</summary>
    public static Position Derive(EstateModel model, RecordEnvelope goal, DateTimeOffset at) => Derive(model, goal, at, deliver: true);

    /// <summary>
    /// A run's position, with its stops delivered across every run (at most three reach one person at a time, design/cohesion-contract.md
    /// § 5) or, for that delivery itself, each run's stops before it.
    /// </summary>
    internal static Position Derive(EstateModel model, RecordEnvelope goal, DateTimeOffset at, bool deliver)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        RecordEnvelope current = model.Latest(goal);
        (FlowDefinition? flow, string? unavailable) = FlowRuns.Resolve(model, current);
        RecordEnvelope? work = FlowRuns.Work(model, current);
        if (flow is null)
        {
            return new Position(current, null, unavailable, null, work, []) { At = at };
        }

        FlowRuns.Configuration configuration = FlowRuns.Configure(model, current, flow);
        var deriving = new Deriving(model, current, flow, configuration, work, at, []);
        deriving.All();

        // A failure route through a branch returns the run to the branch's returnTo step once the branch's steps are done, which only a
        // derivation can tell: the position is derived once more with those returns.
        if (deriving.BranchReturns() is { Count: > 0 } returns)
        {
            deriving = new Deriving(model, current, flow, configuration, work, at, returns);
            deriving.All();
        }

        var position = new Position(current, flow, null, configuration, work, deriving.Instances) { RunAttention = deriving.RunAttention(), At = at };
        position = position with { Stops = FlowStops.Of(model, position, at) };
        if (!deliver)
        {
            return position;
        }

        (IReadOnlyList<FlowStops.RunStop> stops, IReadOnlyDictionary<string, int> reaching) = FlowStops.Deliver(model, position, at);
        return position with { Stops = stops, Reaching = reaching };
    }

    /// <summary>A return of the run to an earlier step by a failure route: the step, its instance (null for every one), when, and why.</summary>
    /// <param name="Target">The step returned to; null for a branch that returns to none.</param>
    /// <param name="Instance">Its instance, for a step that runs over the failed step's list.</param>
    /// <param name="Every">True when every instance of the step is returned to.</param>
    /// <param name="At">When: the failure, or when the branch's steps were done.</param>
    /// <param name="From">The failed step instance, in words.</param>
    /// <param name="Failure">The failure that takes the route.</param>
    /// <param name="Entry">True for the entry of a branch by a failure route, which reopens the branch's steps from the failure.</param>
    private sealed record Return(string? Target, string? Instance, bool Every, DateTimeOffset At, string From, Failure Failure, bool Entry = false);

    private sealed class Deriving
    {
        private readonly EstateModel model;
        private readonly RecordEnvelope goal;
        private readonly FlowDefinition flow;
        private readonly FlowRuns.Configuration configuration;
        private readonly DateTimeOffset at;
        private DateTimeOffset? shapedAt;
        private readonly Dictionary<FlowRuns.StepRef, StepInstance> derived = [];
        private readonly Dictionary<FlowRuns.StepRef, List<RecordEnvelope>> held;
        private readonly Dictionary<FlowRuns.StepRef, List<RecordEnvelope>> reviewing;
        private readonly Dictionary<FlowRuns.StepRef, Failure> failures = [];
        private readonly List<Return> returns;
        private readonly HashSet<string> entered = new(StringComparer.Ordinal);
        private readonly AgentRuns.Ledger ledger;
        private readonly List<AgentRuns.Run> runs;
        private IReadOnlyList<LivingDocuments.State>? documents;

        public Deriving(EstateModel model, RecordEnvelope goal, FlowDefinition flow, FlowRuns.Configuration configuration, RecordEnvelope? work, DateTimeOffset at, IReadOnlyList<Return> branchReturns)
        {
            this.model = model;
            this.goal = goal;
            this.flow = flow;
            this.configuration = configuration;
            this.at = at;
            List<(FlowRuns.StepRef Ref, RecordEnvelope Assignment)> stepped = work is null ? []
                : Routing.Assignments(model, work).Where(a => FlowRuns.StepOf(a) is not null).Select(a => (FlowRuns.StepOf(a)!, a)).ToList();
            held = stepped.Where(s => !s.Ref.Review).GroupBy(s => s.Ref).ToDictionary(g => g.Key, g => g.Select(s => s.Assignment).ToList());
            reviewing = stepped.Where(s => s.Ref.Review).GroupBy(s => s.Ref.Work).ToDictionary(g => g.Key, g => g.Select(s => s.Assignment).ToList());
            ledger = AgentRuns.Of(model, goal);
            runs = AgentRuns.All(model).Where(r => r.Assignment is not null).ToList();

            // The failures each work step instance's latest work stands on, read before any step is derived: a failure route reopens an
            // earlier step, and enters a branch whose steps are otherwise off.
            foreach (FlowStep step in flow.Steps.Where(s => s.Kind == "work" && !configuration.Off.ContainsKey(s.Key)))
            {
                foreach (string? item in step.Each is null ? [null] : FlowRuns.Items(goal, step.Each).Select(i => (string?)i))
                {
                    if (FailureOf(step, item) is Failure failure)
                    {
                        failures[new FlowRuns.StepRef(step.Key, item)] = failure;
                        if (failure.Route.Branch is string branch)
                        {
                            entered.Add(branch);
                        }
                    }
                }
            }

            // A route to an earlier step returns there at once; a branch's entry reopens its steps from the failure, so that a branch entered
            // again after an earlier failure runs again rather than counting what it did before.
            returns = failures.Where(f => f.Value.Route.Step is not null).Select(f => ReturnTo(f.Value.Route.Step!, f.Key, f.Value.Record.AtTime, f.Value))
                .Concat(failures.Where(f => f.Value.Route.Branch is not null).SelectMany(f => flow.Branches.First(b => b.Name == f.Value.Route.Branch).Steps
                    .Select(member => ReturnTo(member, f.Key, f.Value.Record.AtTime, f.Value) with { Entry = true })))
                .Concat(branchReturns).ToList();
        }

        public List<StepInstance> Instances { get; } = [];

        /// <summary>Derives every step instance in the flow's order.</summary>
        public void All()
        {
            foreach (FlowStep step in flow.Steps)
            {
                IReadOnlyList<string> items = step.Each is null ? [] : FlowRuns.Items(goal, step.Each);
                if (step.Each is null)
                {
                    Add(Derive(step, null));
                }
                else if (items.Count == 0)
                {
                    Add(Off(step, null, $"the run's {step.Each} names no items"));
                }
                else
                {
                    foreach (string item in items)
                    {
                        Add(Derive(step, item));
                    }
                }
            }
        }

        private void Add(StepInstance instance)
        {
            Instances.Add(instance);
            derived[instance.Ref] = instance;
        }

        /// <summary>
        /// The returns of failure routes through branches whose steps are now all done: each to the branch's returnTo step, or to none,
        /// from when the last of them was done; those this derivation already knows are not returned again.
        /// </summary>
        public List<Return> BranchReturns()
        {
            var found = new List<Return>();
            foreach ((FlowRuns.StepRef from, Failure failure) in failures.Where(f => f.Value.Route.Branch is not null))
            {
                FlowBranch? branch = flow.Branches.FirstOrDefault(b => b.Name == failure.Route.Branch);
                if (branch is null || BranchDone(branch, failure) is not DateTimeOffset done || returns.Any(r => !r.Entry && r.From == from.ToString() && r.Failure == failure))
                {
                    continue;
                }

                // A branch that names no step to return to returns to none, and the failed step stops for the sponsor.
                found.Add(branch.ReturnTo is string target ? ReturnTo(target, from, done, failure) : new Return(null, null, false, done, from.ToString(), failure));
            }

            return found;
        }

        /// <summary>
        /// When every step of a branch entered by a failure was done after it, or at once when every one of them is off; null while one
        /// is not.
        /// </summary>
        private DateTimeOffset? BranchDone(FlowBranch branch, Failure failure)
        {
            List<StepInstance> steps = Instances.Where(i => branch.Steps.Contains(i.Step.Key, StringComparer.Ordinal)).ToList();
            if (steps.Count == 0 || steps.Any(s => s.State is not ("done" or "off")))
            {
                return null;
            }

            if (steps.All(s => s.State == "off"))
            {
                return failure.Record.AtTime;
            }

            DateTimeOffset? last = steps.Where(s => s.State == "done").Select(s => s.DoneAt).Max();
            return last is DateTimeOffset time && time >= failure.Record.AtTime ? time : null;
        }

        private Return ReturnTo(string target, FlowRuns.StepRef from, DateTimeOffset when, Failure failure)
        {
            FlowStep? step = flow.Step(target);
            FlowStep? failed = flow.Step(from.Key);
            bool same = step?.Each is not null && step.Each == failed?.Each;
            return new Return(target, same ? from.Instance : null, step?.Each is not null && !same, when, from.ToString(), failure);
        }

        /// <summary>The required attention the whole run raises: the goal's limit of consecutive failed steps reached, or an evaluation of impossible.</summary>
        public List<Attention> RunAttention()
        {
            var raised = new List<Attention>();
            if (TJson.Object(goal.Body, "limits") is { ValueKind: JsonValueKind.Object } limits && limits.TryGetProperty("consecutiveFailures", out JsonElement limit)
                && limit.TryGetInt32(out int allowed) && allowed > 0)
            {
                // Each step that failed, and each step done, in time order: the failures since the last step done are consecutive.
                var events = new List<(DateTimeOffset At, RecordEnvelope? Failure)>();
                foreach (StepInstance instance in Instances.Where(i => i.Step.Kind == "work"))
                {
                    events.AddRange(FailuresOf(instance.Step, instance.Instance).Select(f => (f.AtTime, (RecordEnvelope?)f)));
                }

                events.AddRange(Instances.Where(i => i.State == "done" && i.DoneAt is not null).Select(i => (i.DoneAt!.Value, (RecordEnvelope?)null)));
                List<RecordEnvelope> trailing = events.OrderBy(e => e.At).ThenBy(e => e.Failure is null ? 0 : 1).Reverse().TakeWhile(e => e.Failure is not null).Select(e => e.Failure!).ToList();
                if (trailing.Count >= allowed)
                {
                    raised.Add(new Attention("consecutive-failures", trailing[0], $"the run has had {trailing.Count.ToString(CultureInfo.InvariantCulture)} failed steps in a row, the limit of {allowed.ToString(CultureInfo.InvariantCulture)} consecutive failures its goal sets: nothing more of it is dispatched until the sponsor answers"));
                }
            }

            if (GoalRules.CurrentEvaluation(model, goal) is RecordEnvelope evaluation && GoalRules.Verdicts(evaluation).Contains("impossible", StringComparer.Ordinal))
            {
                raised.Add(new Attention("impossible", evaluation, $"evaluation {evaluation.Id} judges a completion condition impossible: nothing more of the run is dispatched until the sponsor answers, ending the goal or revising it"));
            }

            return raised;
        }

        public static StepInstance Off(FlowStep step, string? item, string reason) => new(step, item, "off", [reason], null, null, null, null, [], [], [], [], null);

        private StepInstance Derive(FlowStep step, string? item)
        {
            if (configuration.Off.TryGetValue(step.Key, out string? off))
            {
                return Off(step, item, off);
            }

            IReadOnlyDictionary<string, string> bindings = FlowRuns.Bindings(model, goal, flow, step, item, input => Carrier(input, item));
            DateTimeOffset? asked = step.Reserved is null ? null : AskedSince(step, item);
            if (asked is DateTimeOffset askedAt && FlowStops.ClosesByRequest(step) && CommandOf(step, bindings, EarlyInputs(step, item)).Command is string now
                && FlowStops.ClosedRequest(model, goal, new FlowRuns.StepRef(step.Key, item), now, askedAt, at, done: true, closesOnly: true) is RecordEnvelope closed)
            {
                // {request}: the request the operator closed as done for this ask, with the step's current command, after it was asked.
                bindings = new Dictionary<string, string>(bindings, StringComparer.Ordinal) { ["request"] = model.Chain(closed)[0] };
            }

            if (flow.BranchOf(step.Key) is FlowBranch branch && !entered.Contains(branch.Name) && Failing(branch.When, bindings) is { Count: > 0 } entering)
            {
                return Off(step, item, $"its branch {branch.Name} is not entered: {entering[0]}");
            }

            if (!step.Required && step.Inputs.Select(i => (Input: i, Producer: Producer(i, item))).FirstOrDefault(p => p.Producer is { State: "off" }) is { Producer: not null } fed)
            {
                return Off(step, item, $"its input {fed.Input.Name} comes from {fed.Producer.Name}, which is off");
            }

            var reference = new FlowRuns.StepRef(step.Key, item);
            List<RecordEnvelope> assignments = held.GetValueOrDefault(reference) ?? [];
            List<RecordEnvelope> reviews = reviewing.GetValueOrDefault(reference) ?? [];
            RecordEnvelope? holder = assignments.Count == 0 ? null : assignments[^1];
            RecordEnvelope? result = holder is null ? null : LatestResult(holder);
            // A reserved act's done predicates count only the records of a principal who holds it, the operator (design/flow.md § 8).
            Func<RecordEnvelope, bool>? admit = Admit(step);
            List<Check> done = step.Done.Select(p => FlowRuns.Bind(p, bindings)).Select(p => new Check(p, GoalPredicates.EvaluatePredicate(model, p, admit))).ToList();
            string? outcome = result is null ? null : EstateModel.BodyString(result, "outcome");
            Closing completion = Completion(step, item, assignments, reviews, result, done);
            (bool complete, List<Output> outputs, List<ItemState> items, RecordEnvelope? review, RecordEnvelope? findings, string? pending) =
                (completion.Complete, completion.Outputs, completion.Items, completion.Review, completion.Findings, completion.Pending);
            bool doneMet = done.All(d => d.Verdict.Value == "met");
            DateTimeOffset? doneAt = complete ? new[] { step.Reserved is null ? result : null, review }.Concat(outputs.Select(o => o.Carrier)).Concat(done.SelectMany(d => GoalPredicates.Matching(model, d.Predicate, admit)))
                .OfType<RecordEnvelope>().Select(r => (DateTimeOffset?)r.AtTime).Max() : null;
            List<Input> inputs = Inputs(step, item, holder, complete && holder is null ? doneAt : null);
            List<AttemptState> attempts = AttemptsOf(assignments);
            List<AgentRuns.Run> carried = RunsOf(assignments);
            int used = carried.Count(r => r.Charged);
            StepInstance Shaped(StepInstance instance) => instance with { Attempts = attempts, AttemptsUsed = used, AttemptsAllowed = ledger.AttemptsPerStep, AskedSince = asked };

            // A review step's own review names the exact result it judged, so a review of the current result is never stale.
            bool stale = inputs.Any(i => i.Change is not null) && (complete || holder is not null) && !(step.Kind == "review" && complete);
            Return? reopened = complete && !stale ? Reopened(step, item, doneAt) : null;
            if (complete && !stale && reopened is null)
            {
                return Shaped(new StepInstance(step, item, "done", [Completed(step, result, review, done)], Who(holder), holder, result, review, outputs, items, inputs, done, doneAt));
            }

            // Retries by outcome (design/flow.md § 7): an uncertain effect is reconciled by reading before any retry, so it stops the step
            // before anything would rerun it, stale, reopened or waiting.
            if (Uncertain(step, result, carried, done) is Attention uncertain)
            {
                StepInstance stopped = Shaped(new StepInstance(step, item, "stopped", [$"required attention: {uncertain.Summary}"], Who(holder), holder, result, review, outputs, items, inputs, done, null)) with { Stop = "attention" };
                return step.IsRouted ? stopped with { Attention = uncertain } : stopped;
            }

            // Blocked: what it waits on is not done, or a precondition, a trap, a due time or its role brief does not hold; every reason is
            // listed. A stale step shows stale, and reruns once nothing it waits on holds it.
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
                    blocked.Add($"trap {trap.Name}: {trap.Summary} ({sprung[0]}){Sprung(trap, bindings)}");
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

            // A role brief due at refuse holds a step before it is briefed: unassigned, or to rerun from a fresh assignment; a holder already
            // briefed goes on.
            string? briefDue = step.IsRouted ? RoleBriefDue(step) : null;
            if (briefDue is not null && (holder is null || stale))
            {
                blocked.Add(briefDue);
            }

            // A reserved act whose input changed after it was done waits on the operator again, with the command the records give now.
            bool actAgain = stale && step.Reserved is not null && blocked.Count == 0;
            if (stale && !actAgain)
            {
                List<string> changed = inputs.Where(i => i.Change is not null).Select(i => i.Change!).ToList();
                changed.AddRange(blocked);
                changed.Add($"it reruns under its recovery: {step.Recovery}");
                if (blocked.Count == 0 && Spent(inputs.First(i => i.Change is not null).Carrier ?? holder!) is StepInstance exhausted)
                {
                    return exhausted with { Reasons = [.. changed, .. exhausted.Reasons] };
                }

                return Shaped(new StepInstance(step, item, "stale", changed, Who(holder), holder, result, review, outputs, items, inputs, done, null, blocked.Count > 0) { Rerun = blocked.Count == 0 && step.IsRouted });
            }

            // A step whose failure stands says so first, whatever else holds it, such as the earlier step its failure route returns to.
            if (blocked.Count > 0)
            {
                Failure? standing = failures.GetValueOrDefault(reference);
                return Shaped(new StepInstance(step, item, "blocked", standing is null ? blocked : [$"it failed: {standing.Summary}", .. blocked], Who(holder), holder, result, review, outputs, items, inputs, done, null)) with { Failed = standing };
            }

            StepInstance Make(string state, string reason, string? principal = null) =>
                Shaped(new(step, item, state, pending is null || state is "in-review" ? [reason] : [reason, pending], principal ?? Who(holder), holder, result, review, outputs, items, inputs, done, null));

            // A reserved act is never run: it stops for the operator, and its exact command is computed now, from the records as they
            // stand, only because the step is next (design/flow.md § 8; design/release-procedure.md § 6).
            if (step.Reserved is string act)
            {
                (string? command, IReadOnlyList<string> unbound) = CommandOf(step, bindings, inputs);
                if (command is null)
                {
                    return Shaped(new StepInstance(step, item, "blocked", [$"its command cannot be computed from the records now: {string.Join("; ", unbound)}"], Who(holder), holder, result, review, outputs, items, inputs, done, null));
                }

                var waiting = new List<string> { $"a reserved act, {act}: it waits on a person who holds it, the operator, and no agent carries it without a grant in force within its bounds, none of which this build issues" };
                if (actAgain)
                {
                    waiting.AddRange(inputs.Where(i => i.Change is not null).Select(i => i.Change! + ", so the act is carried again"));
                }

                if (pending is not null)
                {
                    waiting.Add(pending);
                }

                waiting.AddRange(done.Where(d => d.Verdict.Value != "met").Select(d => "it is done once the operator's act is recorded: " + d.Verdict.Reason));
                return Shaped(new StepInstance(step, item, "stopped", waiting, Who(holder), holder, result, review, outputs, items, inputs, done, null)) with { Stop = "reserved", Command = command };
            }

            // Reopened by a later step's failure route: it reruns from a fresh assignment, routed by its role, within its attempts.
            if (reopened is not null)
            {
                string why = reopened.Entry
                    ? $"reopened: step {reopened.From} failed ({reopened.Failure.Summary}), and its failure route enters branch {reopened.Failure.Route.Branch} again at {Time(reopened.At)}; it reruns under its recovery: {step.Recovery}"
                    : $"reopened: step {reopened.From} failed ({reopened.Failure.Summary}), and its failure route returns the run here at {Time(reopened.At)}; it reruns under its recovery: {step.Recovery}";
                return Fresh(why, reopened.Failure.Record);
            }

            Failure? failed = failures.GetValueOrDefault(reference);
            if (failed is not null)
            {
                return Failed(failed);
            }

            // A failed run of the holder's, with no result since, retries after a backoff; findings start another attempt; both within the
            // goal's attempts per step, and a spent budget stops the step for the sponsor.
            List<AgentRuns.Run> own = holder is null ? [] : RunsOf([holder]);
            List<AgentRuns.Run> failedRuns = FailedRuns(own, assignments);
            bool agent = holder is not null && step.IsRouted && EstateModel.BodyString(holder, "contributor") is string contributor && !GoalRules.IsPerson(model, contributor);
            bool running = own.Any(r => r.InFlight);
            bool again = result is null || findings is not null || failedRuns.Count > 0;
            if (agent && again && !running && used >= ledger.AttemptsPerStep)
            {
                RecordEnvelope trigger = findings ?? failedRuns.LastOrDefault()?.Latest ?? holder!;
                string spent = $"its retry budget is spent: its work has had {used.ToString(CultureInfo.InvariantCulture)} of the {ledger.AttemptsPerStep.ToString(CultureInfo.InvariantCulture)} runs the goal's attempts per step grant"
                    + (findings is not null ? $", and review {findings.Id} found what to change in result {result!.Id}" : failedRuns.Count > 0 ? $", and run {failedRuns[^1].Id} failed" : string.Empty)
                    + "; the sponsor is asked, and only what depends on it waits";
                return Make("stopped", "required attention: " + spent) with { Attention = new Attention("exhausted", trigger, spent), Stop = "attention" };
            }

            if (agent && failedRuns.Count > 0 && !running)
            {
                AgentRuns.Run last = failedRuns[^1];
                DateTimeOffset settled = last.Latest.AtTime;
                TimeSpan wait = Backoff(failedRuns.Count);
                if (settled + wait > at)
                {
                    return Shaped(new StepInstance(step, item, "blocked", [$"it backs off after run {last.Id} failed ({Observed(last)}), {failedRuns.Count.ToString(CultureInfo.InvariantCulture)} failed run{(failedRuns.Count == 1 ? string.Empty : "s")} in a row: another run after {Time(settled + wait)}, within its attempts ({used.ToString(CultureInfo.InvariantCulture)} of {ledger.AttemptsPerStep.ToString(CultureInfo.InvariantCulture)} used)"],
                        Who(holder), holder, result, review, outputs, items, inputs, done, null));
                }
            }

            // An item that exists and awaits its review, or changed after it, holds any step in review.
            if (step.Kind is not ("work" or "review") && doneMet && items.FirstOrDefault(i => !i.Satisfied && i.State != "off") is { State: "in-review" or "stale" })
            {
                return Make("in-review", pending!) with { Reviews = completion.Reviews };
            }

            if (step.Kind == "work" && result is not null && findings is null)
            {
                return Make("in-review", pending ?? $"its result {result.Id} is {outcome}{(doneMet ? string.Empty : ", and its done predicates do not hold")}: a review with findings sends it back for another attempt") with { Reviews = completion.Reviews };
            }

            if (TJson.Object(step.When, "watch") is { ValueKind: JsonValueKind.Object } watch)
            {
                DateTimeOffset? started = waits.Select(w => w.DoneAt).Where(t => t is not null).Max() ?? Activated();
                TimeSpan limit = FlowContract.ParseDuration(TJson.Text(watch, "limit")) ?? TimeSpan.Zero;
                string watched = done.FirstOrDefault(d => d.Verdict.Value != "met")?.Verdict.Reason ?? "its done predicates";
                return started is DateTimeOffset since && since + limit < at
                    ? Make("stopped", $"the watch passed its limit at {Time(since + limit)} without its done predicates holding ({watched}): required attention") with { Stop = "attention" }
                    : Make("next", $"watched every {TJson.Text(watch, "every")} until its done predicates hold ({watched}){(started is DateTimeOffset begin ? $", until {Time(begin + limit)}" : string.Empty)}");
            }

            string? holding = holder is null ? null : EstateModel.BodyString(holder, "contributor");
            if (holding is not null && step.IsRouted)
            {
                string retry = failedRuns.Count > 0 ? $" after run {failedRuns[^1].Id} failed ({Observed(failedRuns[^1])}), within its attempts ({used.ToString(CultureInfo.InvariantCulture)} of {ledger.AttemptsPerStep.ToString(CultureInfo.InvariantCulture)} used)" : string.Empty;
                return GoalRules.IsPerson(model, holding)
                    ? Make("stopped", $"a person holds it: assigned to {Routing.NameOf(model, holding)} by assignment {holder!.Id}{(findings is null ? string.Empty : $", whose result {result!.Id} review {findings.Id} sent back")}") with { Stop = "person" }
                    : Make("next", findings is null
                        ? $"{Routing.NameOf(model, holding)} holds it by assignment {holder!.Id} and goes on with it{retry}"
                        : $"review {findings.Id} found what to change in result {result!.Id}: {Routing.NameOf(model, holding)} goes on with another attempt, within its attempts ({used.ToString(CultureInfo.InvariantCulture)} of {ledger.AttemptsPerStep.ToString(CultureInfo.InvariantCulture)} used)");
            }

            return ByKind(null);

            // A step not held, or held afresh: routed by its role, or carried as its kind is.
            StepInstance ByKind(string? first)
            {
                (string? principal, string role) = ByRole(step);
                string reason = step.Kind switch
                {
                    "effect" => $"an outside effect by the {TJson.Text(TJson.Object(step.Element, "effect"), "route")} route, which a person carries in this build; the step counts once its done predicates hold",
                    "exchange" => "an exchange: its export and its intake are carried by a person in this build, and the step counts once its done predicates hold",
                    "flow" => $"another run, of flow {TJson.Text(TJson.Object(step.Element, "flow"), "id")} {TJson.Text(TJson.Object(step.Element, "flow"), "version")}, which its sponsor proposes and activates; the step counts once its done predicates hold",
                    _ => role,
                };
                StepInstance Shown(string state, string text, string? who) =>
                    first is null ? Make(state, text, who) : Shaped(new StepInstance(step, item, state, [first, text], who, holder, result, review, outputs, items, inputs, done, null));
                if (step.Kind is "effect" or "exchange" or "flow")
                {
                    return Shown("stopped", reason, principal) with { Stop = "person" };
                }

                // No one named for the role is a stall the sponsor settles by naming one; a person named holds the step.
                if (principal is null || GoalRules.IsPerson(model, principal))
                {
                    return Shown("stopped", reason, principal) with { Stop = principal is null ? "attention" : "person" };
                }

                return Shown("next", step.Kind is "operation" or "decision"
                    ? $"{role}; it is carried by its action{(step.Action is null ? string.Empty : ": " + step.Action)}, and counts once its done predicates hold"
                    : role, principal);
            }

            // Held afresh: a routed step reruns from a fresh assignment, within the attempts its work has had; any other kind is carried again.
            StepInstance Fresh(string why, RecordEnvelope trigger)
            {
                if (briefDue is not null)
                {
                    return Shaped(new StepInstance(step, item, "blocked", [why, briefDue], Who(holder), holder, result, review, outputs, items, inputs, done, null));
                }

                if (Spent(trigger) is StepInstance spent)
                {
                    return spent with { Reasons = [why, .. spent.Reasons] };
                }

                return ByKind(why) with { Rerun = step.IsRouted };
            }

            // A routed step to run again from a fresh assignment, whose work has had every run the goal's attempts per step grant, by an
            // agent its role reaches: its retry budget is spent, and it stops for the sponsor.
            StepInstance? Spent(RecordEnvelope trigger)
            {
                if (!step.IsRouted || used < ledger.AttemptsPerStep || ByRole(step).Principal is not string next || GoalRules.IsPerson(model, next))
                {
                    return null;
                }

                string spent = $"its retry budget is spent: its work has had {used.ToString(CultureInfo.InvariantCulture)} of the {ledger.AttemptsPerStep.ToString(CultureInfo.InvariantCulture)} runs the goal's attempts per step grant, and it is to run again; the sponsor is asked, and only what depends on it waits";
                return Shaped(new StepInstance(step, item, "stopped", ["required attention: " + spent], next, holder, result, review, outputs, items, inputs, done, null)) with { Attention = new Attention("exhausted", trigger, spent), Stop = "attention" };
            }

            // A failed step takes its failure route (design/flow.md § 7): a stop for the sponsor, an earlier step the run returns to, or a branch.
            StepInstance Failed(Failure failure)
            {
                string first = $"it failed: {failure.Summary}";
                StepInstance Stop(string why) =>
                    Shaped(new StepInstance(step, item, "stopped", [first, "required attention: " + why], Who(holder), holder, result, review, outputs, items, inputs, done, null))
                    with { Attention = new Attention("failed", failure.Record, $"step {reference} failed: {failure.Summary}; {why}"), Failed = failure, Stop = "attention" };
                StepInstance Wait(string why) =>
                    Shaped(new StepInstance(step, item, "blocked", [first, why], Who(holder), holder, result, review, outputs, items, inputs, done, null)) with { Failed = failure };
                StepInstance Again(string why) => Fresh($"{first}; {why}", failure.Record) with { Failed = failure };
                switch (failure.Route.Route)
                {
                    case "step":
                        if (AllOff(failure.Route.Step!, item))
                        {
                            return Stop($"its failure route returns the run to {failure.Route.Step}, which is off in this run, so nothing can move: the sponsor is asked, and only what depends on it waits");
                        }

                        return ReturnedSince(failure.Route.Step!, item, failure.Record.AtTime) is DateTimeOffset back
                            ? Again($"its failure route returned the run to {failure.Route.Step}, done again at {Time(back)}, and it reruns from a fresh assignment under its recovery: {step.Recovery}")
                            : Wait($"its failure route returns the run to {failure.Route.Step}, which reruns first");
                    case "branch":
                        FlowBranch entered = flow.Branches.First(b => b.Name == failure.Route.Branch);
                        Return? returned = returns.LastOrDefault(r => !r.Entry && r.From == reference.ToString() && r.Failure == failure);
                        if (returned is null)
                        {
                            return Wait($"its failure route entered branch {entered.Name}: it waits on the branch's steps ({string.Join(", ", entered.Steps)}), then the run returns to {entered.ReturnTo ?? "no step, and the sponsor is asked"}");
                        }

                        if (returned.Target is null)
                        {
                            return Stop($"its failure route entered branch {entered.Name}, whose steps were done at {Time(returned.At)}, and the branch returns to no step: the sponsor is asked, and only what depends on it waits");
                        }

                        if (returned.Target != step.Key && AllOff(returned.Target, item))
                        {
                            return Stop($"branch {entered.Name}, its failure route, is done and returns the run to {returned.Target}, which is off in this run, so nothing can move: the sponsor is asked, and only what depends on it waits");
                        }

                        return returned.Target == step.Key
                            ? Again($"its failure route entered branch {entered.Name}, whose steps were done at {Time(returned.At)}, and the branch returns the run here: it reruns from a fresh assignment under its recovery: {step.Recovery}")
                            : ReturnedSince(returned.Target, item, returned.At) is DateTimeOffset since
                                ? Again($"branch {entered.Name} returned the run to {returned.Target}, done again at {Time(since)}, and it reruns from a fresh assignment under its recovery: {step.Recovery}")
                                : Wait($"branch {entered.Name}, its failure route, is done and returns the run to {returned.Target}, which reruns first");
                    default:
                        return Stop("its failure route stops here: the sponsor is asked, and only what depends on it waits");
                }
            }
        }

        /// <summary>True when every derived instance of a step a failure route returns to is off in this run, so the route cannot move.</summary>
        private bool AllOff(string key, string? item)
        {
            List<StepInstance> instances = derived.Values.Where(d => d.Step.Key == key && (flow.Step(key)?.Each is null || d.Instance == item || !derived.Values.Any(o => o.Step.Key == key && o.Instance == item))).ToList();
            return instances.Count > 0 && instances.All(i => i.State == "off");
        }

        /// <summary>When every instance of a step a failure route returns to was done again after a time; null while one is not.</summary>
        private DateTimeOffset? ReturnedSince(string key, string? item, DateTimeOffset since)
        {
            List<StepInstance> instances = derived.Values.Where(d => d.Step.Key == key).ToList();
            if (flow.Step(key)?.Each is not null && instances.Any(i => i.Instance == item))
            {
                instances = instances.Where(i => i.Instance == item).ToList();
            }

            if (instances.Count == 0 || instances.Any(i => i.State != "done" || i.DoneAt is not DateTimeOffset done || done <= since))
            {
                return null;
            }

            return instances.Max(i => i.DoneAt);
        }

        /// <summary>The latest return of a failure route to this step instance that its completion does not postdate; null when none reopens it.</summary>
        private Return? Reopened(FlowStep step, string? item, DateTimeOffset? doneAt) =>
            returns.Where(r => r.Target is not null && r.Target == step.Key && (r.Every || r.Instance == item) && doneAt is DateTimeOffset done && done <= r.At).OrderBy(r => r.At).LastOrDefault();

        /// <summary>
        /// The failure a work step instance's latest work stands on (design/flow.md § 7): the holder's latest result blocked or abandoned,
        /// or the latest review of its current result, or of a record item it produced, by a principal who judges the step, that failed it.
        /// A newer result, or a fresh assignment, leaves no failure standing.
        /// </summary>
        private Failure? FailureOf(FlowStep step, string? item)
        {
            List<RecordEnvelope> assignments = held.GetValueOrDefault(new FlowRuns.StepRef(step.Key, item)) ?? [];
            RecordEnvelope? holder = assignments.Count == 0 ? null : assignments[^1];
            RecordEnvelope? result = holder is null ? null : LatestResult(holder);
            if (result is null)
            {
                return null;
            }

            if (EstateModel.BodyString(result, "outcome") is "blocked" or "abandoned")
            {
                return new Failure(result, $"its result {result.Id} is {EstateModel.BodyString(result, "outcome")}", step.FailureRoute);
            }

            RecordEnvelope? verdict = Verdicts(step, item, assignments, result).Where(r => EstateModel.BodyString(r, "outcome") == "fail")
                .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
            return verdict is null ? null : new Failure(verdict, $"review {verdict.Id} failed {verdict.Subject!.Type} {verdict.Subject.Id}", step.FailureRoute);
        }

        /// <summary>Every failing record of a work step instance's work, oldest first: results blocked or abandoned, and reviews that failed them.</summary>
        private IEnumerable<RecordEnvelope> FailuresOf(FlowStep step, string? item)
        {
            List<RecordEnvelope> assignments = held.GetValueOrDefault(new FlowRuns.StepRef(step.Key, item)) ?? [];
            Func<RecordEnvelope, bool> judges = Judges(step, item);
            IReadOnlySet<string> authors = FlowRuns.Authors(model, assignments);
            foreach (AttemptState attempt in AttemptsOf(assignments).Where(a => a.Result is not null))
            {
                if (attempt.Outcome is "blocked" or "abandoned")
                {
                    yield return attempt.Result!;
                }
                else
                {
                    IReadOnlyList<string> chain = model.Chain(attempt.Result!);
                    foreach (RecordEnvelope failed in model.Current("review").Where(r => r.Subject is not null && chain.Contains(r.Subject.Id) && EstateModel.BodyString(r, "outcome") == "fail"
                        && !authors.Contains(r.By) && judges(r)))
                    {
                        yield return failed;
                    }
                }
            }
        }

        /// <summary>
        /// The verdicts that stand on a work step instance's latest work: for each subject, its current result or a record item it produced,
        /// the latest review of it by a principal who judges the step and is not its author.
        /// </summary>
        private IEnumerable<RecordEnvelope> Verdicts(FlowStep step, string? item, List<RecordEnvelope> assignments, RecordEnvelope result)
        {
            IReadOnlySet<string> authors = FlowRuns.Authors(model, assignments);
            Func<RecordEnvelope, bool> judges = Judges(step, item);
            var subjects = new List<IReadOnlyList<string>> { model.Chain(model.Latest(result)) };
            IReadOnlyDictionary<string, string> bindings = FlowRuns.Bindings(model, goal, flow, step, item, input => Carrier(input, item));
            List<Check> done = step.Done.Select(p => FlowRuns.Bind(p, bindings)).Select(p => new Check(p, GoalPredicates.EvaluatePredicate(model, p))).ToList();
            foreach (FlowOutput output in step.Outputs.Where(o => o.Kind != "file"))
            {
                if (Resolve(step, output, result, done).Output is Output value)
                {
                    subjects.Add(model.Chain(value.Carrier));
                }
            }

            foreach (IReadOnlyList<string> chain in subjects)
            {
                RecordEnvelope? latest = model.Current("review").Where(r => r.Subject is not null && chain.Contains(r.Subject.Id) && !authors.Contains(r.By) && r.By != result.By && judges(r))
                    .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
                if (latest is not null)
                {
                    yield return latest;
                }
            }
        }

        /// <summary>
        /// Who judges a step instance's work, whose findings send it back and whose failure takes its failure route: an operator; a
        /// principal of the estate's reviewer role, by the roster or its own roles; a principal of the reviewer role an item's review
        /// names; a principal of a review step's role that reviews it, or the contributor of such a step's assignment; and the contributor
        /// of one of its own review assignments. Never a principal outside them, whatever it records.
        /// </summary>
        private Func<RecordEnvelope, bool> Judges(FlowStep step, string? item)
        {
            var named = new HashSet<string>(StringComparer.Ordinal);
            // A role the flow marks a person's judges only by its persons.
            void Add(string role, IEnumerable<string> principals) => named.UnionWith(principals.Where(p => !PersonHeld(role) || GoalRules.IsPerson(model, p)));
            List<FlowReview> requirements = step.Outputs.Select(o => ReviewOf(step, o)).Append(ReviewOf(step, null)).Where(r => r.Independence != "none").Distinct().ToList();
            foreach (FlowReview requirement in requirements)
            {
                Add(requirement.Role ?? Roles.Reviewer, Routing.Principals(model, goal, requirement.Role ?? Roles.Reviewer).Principals);
            }

            foreach (FlowStep reviewer in flow.Steps.Where(s => s.Kind == "review" && s.Inputs.Any(i => i.Step == step.Key)))
            {
                Add(reviewer.Role, Routing.Principals(model, goal, reviewer.Role).Principals);
                Add(reviewer.Role, held.Where(h => h.Key.Key == reviewer.Key).SelectMany(h => h.Value).Select(a => EstateModel.BodyString(a, "contributor")).OfType<string>());
            }

            Add(Roles.Reviewer, Routing.Principals(model, goal, Roles.Reviewer).Principals);
            named.UnionWith((reviewing.GetValueOrDefault(new FlowRuns.StepRef(step.Key, item)) ?? []).Select(a => EstateModel.BodyString(a, "contributor")).OfType<string>());
            bool reviewerHeld = PersonHeld(Roles.Reviewer);
            // Roles are read at each review's place, so a later promotion makes no earlier review a judgment.
            return review => named.Contains(review.By) || model.HasRoleAt(review.By, Roles.Operator, review)
                || (model.HasRoleAt(review.By, Roles.Reviewer, review) && (!reviewerHeld || GoalRules.IsPerson(model, review.By)));
        }

        /// <summary>
        /// Whether a principal may clear an item's review: a principal of the reviewer role it names, through the goal's roles or the
        /// roster; for a review that names none, a principal of the estate's reviewer role, by the roster or its own roles, or an operator;
        /// the contributor of a review assignment of the step instance that names this item and one of its versions; and for a self
        /// review, its producers. Its independence is judged apart.
        /// </summary>
        private Func<RecordEnvelope, bool> Admits(FlowReview required, List<RecordEnvelope> reviews, string? item, List<(RecordEnvelope Subject, string Hash)> versions, IReadOnlySet<string> producers)
        {
            HashSet<string> named = Routing.Principals(model, goal, required.Role ?? Roles.Reviewer).Principals.ToHashSet(StringComparer.Ordinal);
            named.UnionWith(reviews.Where(a => FlowRuns.ReviewOf(a) is FlowRuns.ReviewRef carried && (item is null ? carried.Items.Count == 0 : carried.Items.Contains(item, StringComparer.Ordinal))
                    && versions.Any(v => v.Subject.Id == carried.Subject.Id))
                .Select(a => EstateModel.BodyString(a, "contributor")).OfType<string>());
            if (required.Independence == "self")
            {
                named.UnionWith(producers);
            }

            // A review in a role the flow marks a person's is a person's, whoever the roster or an assignment names.
            bool person = PersonHeld(required.Role ?? Roles.Reviewer);
            return review => (!person || GoalRules.IsPerson(model, review.By))
                && (named.Contains(review.By) || (required.Role is null && (model.HasRoleAt(review.By, Roles.Reviewer, review) || model.HasRoleAt(review.By, Roles.Operator, review))));
        }

        /// <summary>A record's operation outcome in words, for a failed run: what was observed when it settled.</summary>
        private static string Observed(AgentRuns.Run run) =>
            TJson.Text(TJson.Object(run.Latest.Body, "observed"), "summary") is string summary ? $"{run.Status}: {(summary.Length > 160 ? summary[..159] + "…" : summary)}" : run.Status;

        /// <summary>
        /// The holder's failed runs in a row with no result since: the latest runs that settled as a known failure, newest last; empty
        /// when its latest run did not fail or a result was recorded after it.
        /// </summary>
        private List<AgentRuns.Run> FailedRuns(List<AgentRuns.Run> own, List<RecordEnvelope> assignments)
        {
            DateTimeOffset? lastResult = AttemptsOf(assignments).Where(a => a.Result is not null).Select(a => (DateTimeOffset?)a.Result!.AtTime).Max();
            var failed = new List<AgentRuns.Run>();
            foreach (AgentRuns.Run run in own.OrderByDescending(r => r.Intent.At, StringComparer.Ordinal))
            {
                if (run.Status != "known-failure" || (lastResult is DateTimeOffset after && after >= run.Intent.AtTime))
                {
                    break;
                }

                failed.Insert(0, run);
            }

            return failed;
        }

        /// <summary>The wait before another run after a number of failed runs in a row: doubling from <see cref="RetryBackoff"/>, at most <see cref="RetryBackoffLimit"/>.</summary>
        private static TimeSpan Backoff(int failed)
        {
            double seconds = RetryBackoff.TotalSeconds * Math.Pow(2, Math.Min(Math.Max(failed - 1, 0), 20));
            return TimeSpan.FromSeconds(Math.Min(seconds, RetryBackoffLimit.TotalSeconds));
        }

        /// <summary>
        /// An uncertain effect of a step (design/flow.md § 7), reconciled by reading before any retry: its holder's latest result naming
        /// effects whose outcome is not known, a run of its work left uncertain, or for a step carried by its action an operation its done
        /// predicates read left uncertain.
        /// </summary>
        private Attention? Uncertain(FlowStep step, RecordEnvelope? result, List<AgentRuns.Run> carried, List<Check> done)
        {
            if (result is not null && TJson.Strings(result.Body, "unresolvedEffects").ToList() is { Count: > 0 } effects)
            {
                return new Attention("uncertain", result, $"an uncertain effect: result {result.Id} names effects whose outcome is not known ({string.Join("; ", effects)}): each is reconciled by reading the outside state, and the result revised without it, before any retry");
            }

            if (carried.FirstOrDefault(r => r.Status == "uncertain") is AgentRuns.Run run)
            {
                return new Attention("uncertain", run.Latest, $"an uncertain effect: run {run.Id} of its work is uncertain: its effect is reconciled under its original identity by reading, before any retry");
            }

            if (step.IsRouted)
            {
                return null;
            }

            foreach (Check check in done.Where(d => TJson.Text(d.Predicate, "type") == "operation"))
            {
                JsonNode relaxed = JsonNode.Parse(check.Predicate.GetRawText())!;
                relaxed.AsObject().Remove("state");
                relaxed.AsObject().Remove("min");
                relaxed.AsObject().Remove("max");
                (relaxed["fields"] as JsonObject)?.Remove("status");
                if (GoalPredicates.Matching(model, JsonSerializer.SerializeToElement(relaxed)).LastOrDefault(o => EstateModel.BodyString(o, "status") == "uncertain") is RecordEnvelope operation)
                {
                    return new Attention("uncertain", operation, $"an uncertain effect: operation {operation.Id} ({EstateModel.BodyString(operation, "identity")}) is uncertain: it is reconciled under its identity by reading, before any retry");
                }
            }

            return null;
        }

        /// <summary>
        /// Why the role brief a step's brief carries holds it (design/process-profiles.md § 11): the estate's copy of the brief, a living
        /// document, due at level refuse, so that a role's instructions never silently fall behind the estate. Null when it does not hold.
        /// </summary>
        private string? RoleBriefDue(FlowStep step)
        {
            if (flow.Role(step.Role)?.Document is not string path)
            {
                return null;
            }

            documents ??= LivingDocuments.Derive(model);
            LivingDocuments.State? state = documents.FirstOrDefault(d => d.Path == path && d.Repository is null);
            return state is { Closes: true }
                ? $"its role brief, {state.Describe()}: a revision of it, or a review that clears it, comes before the {step.Role} role is briefed from it"
                : null;
        }

        /// <summary>The records a step's done predicates count: for a reserved act only those of a principal who holds it; every record otherwise.</summary>
        private Func<RecordEnvelope, bool>? Admit(FlowStep step) => step.Reserved is null ? null : record => IsReservedAct(model, record);

        /// <summary>
        /// A reserved step's exact command, its declared action with each placeholder bound from the records as they stand now: the run's
        /// subject, instance, goal and work order, and the id or hash of each of its inputs as handed on. Null, with why, when a value is
        /// missing or is not one a command takes unquoted, so that no bound text can change what the command does.
        /// </summary>
        private static (string? Command, IReadOnlyList<string> Unbound) CommandOf(FlowStep step, IReadOnlyDictionary<string, string> bindings, List<Input> inputs)
        {
            if (step.Action is not string action)
            {
                return (null, ["the step declares no action"]);
            }

            var unbound = new List<string>();
            string command = FlowContract.Placeholder().Replace(action, match =>
            {
                string name = match.Groups["name"].Value;
                string? value;
                if (name.StartsWith("input.", StringComparison.Ordinal) && name.Split('.') is [_, string input, string part])
                {
                    Input? handed = inputs.FirstOrDefault(i => i.Name == input);
                    value = part == "id" ? handed?.Carrier?.Id : part == "hash" ? handed?.Hash : null;
                }
                else
                {
                    value = bindings.GetValueOrDefault(name);
                }

                if (value is null)
                {
                    unbound.Add($"{{{name}}} has no value in the records");
                    return match.Value;
                }

                if (!CommandValue().IsMatch(value))
                {
                    unbound.Add($"{{{name}}} is a value with characters a command never takes unquoted, so it is not put in");
                    return match.Value;
                }

                return value;
            });
            return unbound.Count == 0 ? (command, []) : (null, unbound);
        }

        /// <summary>Every attempt under a step's assignments, oldest first, each with its result at its current revision.</summary>
        private List<AttemptState> AttemptsOf(IEnumerable<RecordEnvelope> assignments) =>
            assignments.SelectMany(a => WorkView.AttemptsOf(model, a).Select(t => new AttemptState(a, t, WorkView.ResultOf(model, t))))
                .OrderBy(t => t.Attempt.At, StringComparer.Ordinal).ThenBy(t => t.Attempt.Id, StringComparer.Ordinal).ToList();

        /// <summary>The agent runs on a step's assignments, any revision of them, oldest first.</summary>
        private List<AgentRuns.Run> RunsOf(IEnumerable<RecordEnvelope> assignments)
        {
            var ids = assignments.SelectMany(a => model.Chain(model.Latest(a))).ToHashSet(StringComparer.Ordinal);
            return runs.Where(r => ids.Contains(r.Assignment!)).OrderBy(r => r.Intent.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        }

        private sealed record Closing(bool Complete, List<Output> Outputs, List<ItemState> Items, RecordEnvelope? Review, RecordEnvelope? Findings, string? Pending, IReadOnlyList<PendingReview> Reviews);

        /// <summary>
        /// Whether a step closes, derived in this one place (design/flow.md § 5): every required item satisfied, each with its provenance,
        /// evidence and review, the step's review the default for an item that declares none; its done predicates holding; for a work step
        /// its result completed with no findings or failure standing; for a review step, its review of the exact result it reviews. A work
        /// step that declares no item takes the step's review on its result. The reviews it still waits on are grouped for routing.
        /// </summary>
        private Closing Completion(FlowStep step, string? item, List<RecordEnvelope> assignments, List<RecordEnvelope> reviews, RecordEnvelope? result, List<Check> done)
        {
            bool doneMet = done.All(d => d.Verdict.Value == "met");
            IReadOnlySet<string> authors = FlowRuns.Authors(model, assignments);

            // A reserved act closes only on the records of the operator's act: its done predicates, counted from the operator's records,
            // and its items; a result counts only when the operator recorded it. No agent's work or claim closes it (design/flow.md § 8).
            if (step.Reserved is not null)
            {
                RecordEnvelope? own = result is not null && IsReservedAct(model, result) ? result : null;
                List<ItemState> acted = step.Outputs.Select(o => Judge(step, o, own, done, assignments, reviews, authors, null)).ToList();
                ItemState? unmet = acted.FirstOrDefault(i => !i.Satisfied && i.State != "off");
                return new Closing(doneMet && unmet is null, acted.Where(i => i.Value is not null && i.State != "off").Select(i => i.Value!).ToList(), acted, null, null,
                    unmet is null ? null : $"item {unmet.Declared.Name} is {unmet.State}: {unmet.Reasons[0]}", []);
            }

            RecordEnvelope? findings = null;
            RecordEnvelope? stepReview = null;
            bool failed = false;
            if (step.Kind == "review")
            {
                StepInstance? reviewed = step.Inputs.Where(i => i.Step is not null).Select(i => Producer(i, item)).FirstOrDefault();
                RecordEnvelope? subject = reviewed?.Result;
                stepReview = subject is null ? null : Passing(subject, FlowRuns.Authors(model, held.GetValueOrDefault(reviewed!.Ref) ?? []), flow.Role(step.Role)?.Holder == "person", Reviewers(step, assignments));
            }
            else if (step.Kind == "work" && result is not null)
            {
                RecordEnvelope? latest = LatestReview(result, authors, Judges(step, item));
                findings = latest is not null && EstateModel.BodyString(latest, "outcome") == "findings" ? latest : null;
                failed = latest is not null && EstateModel.BodyString(latest, "outcome") == "fail";
            }

            List<ItemState> items = step.Outputs.Select(o => Judge(step, o, result, done, assignments, reviews, authors, stepReview)).ToList();
            List<Output> outputs = items.Where(i => i.Value is not null && i.State != "off").Select(i => i.Value!).ToList();
            ItemState? open = items.FirstOrDefault(i => !i.Satisfied && i.State != "off");
            string? pending = open is null ? null : $"item {open.Declared.Name} is {open.State}: {open.Reasons[0]}";
            bool closed = doneMet && open is null;
            List<PendingReview> waiting = Pending(step, items, reviews, authors);
            if (step.Kind == "review")
            {
                return new Closing(closed && stepReview is not null, outputs, items, stepReview, null, stepReview is null ? null : pending, []);
            }

            if (step.Kind != "work")
            {
                return new Closing(closed, outputs, items, null, null, pending, waiting);
            }

            // A result naming effects whose outcome is not known closes nothing until they are reconciled (design/flow.md § 7).
            bool completed = result is not null && EstateModel.BodyString(result, "outcome") == "completed" && findings is null && !failed && !TJson.Strings(result.Body, "unresolvedEffects").Any();
            if (!completed || !closed || step.Outputs.Count > 0)
            {
                return new Closing(completed && closed, outputs, items, null, findings, completed ? pending : null, completed ? waiting : []);
            }

            FlowReview required = ReviewOf(step, null);
            if (required.Independence == "none")
            {
                return new Closing(true, outputs, items, null, null, null, []);
            }

            RecordEnvelope? review = Reviewed([(result!, result!.ComputedHash)], authors, result.By, required, reviews, null, out _, out _);
            if (review is not null)
            {
                return new Closing(true, outputs, items, review, null, null, []);
            }

            string role = required.Role ?? Roles.Reviewer;
            return new Closing(false, outputs, items, null, null, $"its result awaits {Describe(required)} before it is handed on: a review of result {result.Id} with outcome pass",
                [new PendingReview([], required with { Role = role }, role, result, authors, Carrying(reviews, result, []))]);
        }

        /// <summary>
        /// The reviews before hand-off a step instance waits on, for routing: each item that exists, holds its provenance and evidence, and
        /// awaits its review at its exact hash, grouped by the subject that carries it and its one review requirement.
        /// </summary>
        private static List<PendingReview> Pending(FlowStep step, List<ItemState> items, List<RecordEnvelope> reviews, IReadOnlySet<string> authors)
        {
            // Only a work step's review is routed as an assignment; another kind's item is reviewed by whoever carries it.
            if (step.Kind != "work" || step.Reserved is not null)
            {
                return [];
            }

            var pending = new List<PendingReview>();
            foreach (IGrouping<(string Subject, string Independence, string Role), ItemState> group in items
                .Where(i => i.State is "in-review" or "stale" && i.Value is not null && i.Requirement is { Independence: not "none" } && !i.Reasons.Any(r => r.StartsWith("review ", StringComparison.Ordinal)))
                .GroupBy(i => (i.Value!.Carrier.Id, i.Requirement!.Independence, i.Requirement.Role ?? Roles.Reviewer)))
            {
                RecordEnvelope subject = group.First().Value!.Carrier;
                List<string> names = group.Select(i => i.Declared.Name).ToList();
                pending.Add(new PendingReview(names, group.First().Requirement! with { Role = group.Key.Role }, group.Key.Role, subject, authors, Carrying(reviews, subject, names)));
            }

            return pending;
        }

        /// <summary>The review assignments that carry one review: of this exact subject and these items.</summary>
        private static List<RecordEnvelope> Carrying(List<RecordEnvelope> reviews, RecordEnvelope subject, List<string> items) =>
            reviews.Where(a => FlowRuns.ReviewOf(a) is FlowRuns.ReviewRef carried && carried.Subject.Id == subject.Id && carried.Subject.Hash == subject.ComputedHash
                && carried.Items.Order(StringComparer.Ordinal).SequenceEqual(items.Order(StringComparer.Ordinal), StringComparer.Ordinal)).ToList();

        /// <summary>
        /// One item's state (design/flow.md § 5): off when an extension or configuration turns it off; missing until it exists at an
        /// exact hash and is of its kind; unproven while the record that produced it does not show the declared provenance; unverified
        /// while a named check does not pass at its exact hash; and its review, the step's when it declares none, recorded at that hash
        /// by a principal of the reviewer role with the declared independence: in review until one passes, stale when only an earlier
        /// version passed, satisfied once one passes.
        /// </summary>
        private ItemState Judge(FlowStep step, FlowOutput output, RecordEnvelope? result, List<Check> done, List<RecordEnvelope> assignments, List<RecordEnvelope> reviews, IReadOnlySet<string> authors, RecordEnvelope? stepReview)
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
                (value, missing) = Resolve(step, output, result, done);
            }

            FlowReview? requirement = step.Kind == "review" ? null : ReviewOf(step, output);
            if (value is null)
            {
                return new ItemState(output, "missing", [missing!], null, null, null, []) { Requirement = requirement };
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
            if (requirement is { Independence: not "none" } required)
            {
                var producers = new HashSet<string>(authors, StringComparer.Ordinal) { producer };
                List<(RecordEnvelope Subject, string Hash)> earlier = output.Kind == "file"
                    ? Results(assignments).Where(r => FileHash(r, output.Name) is string hash && hash != value.Hash).Select(r => (r, r.ComputedHash)).ToList()
                    : model.Chain(value.Carrier).Where(id => id != value.Carrier.Id && model.ById.ContainsKey(id)).Select(id => (model.ById[id], model.ById[id].ComputedHash)).ToList();
                review = Reviewed(versions, producers, producer, required, reviews, output.Name, out RecordEnvelope? sentBack, out _);
                RecordEnvelope? before = review is null ? Reviewed(earlier, producers, producer, required, reviews, output.Name, out _, out _) : null;
                if (review is null)
                {
                    state ??= sentBack is null && before is not null ? "stale" : "in-review";
                    reasons.Add(sentBack is not null ? $"review {sentBack.Id} found what to change in it"
                        : before is not null ? $"it changed after review {before.Id} passed an earlier version, and awaits {Describe(required)} at its exact hash"
                        : $"it awaits {Describe(required)} at its exact hash");
                }
            }

            return new ItemState(output, state ?? "satisfied", reasons, value, producer, review, evidence) { Requirement = requirement };
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
        /// The latest review that passes one of an item's versions at its exact hash under its requirement, by a principal it admits; and
        /// the latest such review of them that found what to change, when it is later than every pass.
        /// </summary>
        private RecordEnvelope? Reviewed(List<(RecordEnvelope Subject, string Hash)> versions, IReadOnlySet<string> producers, string producer, FlowReview required, List<RecordEnvelope> reviews, string? item, out RecordEnvelope? sentBack, out RecordEnvelope? latest)
        {
            Func<RecordEnvelope, bool> admits = Admits(required, reviews, item, versions, new HashSet<string>(producers, StringComparer.Ordinal) { producer });
            List<RecordEnvelope> qualifying = model.Current("review")
                .Where(r => r.Subject is not null && versions.Any(v => v.Subject.Id == r.Subject.Id && v.Hash == r.Subject.Hash) && Qualifies(r.By, producers, producer, required) && admits(r))
                .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
            latest = qualifying.LastOrDefault();
            sentBack = latest is not null && EstateModel.BodyString(latest, "outcome") is "findings" or "fail" ? latest : null;
            return latest is not null && EstateModel.BodyString(latest, "outcome") == "pass" ? latest : null;
        }

        /// <summary>True when the flow marks a role a person's, so that only a person carries its steps and its reviews.</summary>
        private bool PersonHeld(string role) => flow.Role(role)?.Holder == "person";

        /// <summary>
        /// Whether a reviewer meets an independence: self admits anyone, the author's own check; independent another principal than the
        /// producers; person a person among them; other-harness an agent on another harness than the producer's. A review in a role the
        /// flow marks a person's is never an agent's.
        /// </summary>
        private bool Qualifies(string reviewer, IReadOnlySet<string> producers, string producer, FlowReview required)
        {
            if (PersonHeld(required.Role ?? Roles.Reviewer) && !GoalRules.IsPerson(model, reviewer))
            {
                return false;
            }

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
                "other-harness" => FlowRuns.Harness(model, reviewer) is string harness
                    && producers.Append(producer).Select(p => FlowRuns.Harness(model, p)).OfType<string>().All(used => !string.Equals(used, harness, StringComparison.OrdinalIgnoreCase)),
                _ => true,
            };
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
        private (Output? Output, string? Missing) Resolve(FlowStep step, FlowOutput output, RecordEnvelope? result, List<Check> done)
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
            RecordEnvelope? record = done.SelectMany(d => GoalPredicates.Matching(model, d.Predicate, Admit(step)))
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

            if (record is null && step.Reserved is not null && done.SelectMany(d => GoalPredicates.Matching(model, d.Predicate))
                .Where(r => types.Contains(r.Type, StringComparer.Ordinal)).Select(r => r.Id).Distinct(StringComparer.Ordinal).ToList() is { Count: > 0 } refused)
            {
                return (null, $"no {string.Join(" or ", types)} record of the operator's act its done predicates count is its item {output.Name}; {string.Join(", ", refused)}, recorded by a principal who does not hold the act, does not count");
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

        /// <summary>
        /// When a reserved step was asked: the time the run took its current shape (<see cref="ShapedAt"/>), or the latest time a step it
        /// waits on was done, whichever is later; the latest time there is when one of them is not done or off, or is done at no time the
        /// records give, so that no close answers an ask not yet made.
        /// </summary>
        private DateTimeOffset AskedSince(FlowStep step, string? item)
        {
            DateTimeOffset since = shapedAt ??= ShapedAt();
            foreach (StepInstance wait in Dependencies(step, item))
            {
                if (wait.State == "off")
                {
                    continue;
                }

                if (wait.State != "done" || wait.DoneAt is not DateTimeOffset done)
                {
                    return DateTimeOffset.MaxValue;
                }

                if (done > since)
                {
                    since = done;
                }
            }

            return since;
        }

        /// <summary>
        /// The time of the oldest goal revision since which the run's flow pin, subject and options are unchanged: a revision that only
        /// grants more budget or changes the objective asks no reserved step again, while one that changes the version asks each anew.
        /// </summary>
        private DateTimeOffset ShapedAt()
        {
            string shape = Shape(goal);
            RecordEnvelope since = goal;
            IReadOnlyList<string> chain = model.Chain(goal);
            for (int i = chain.Count - 2; i >= 0 && model.ById.TryGetValue(chain[i], out RecordEnvelope? earlier) && earlier.Type == goal.Type && Shape(earlier) == shape; i--)
            {
                since = earlier;
            }

            return since.AtTime;
        }

        private static readonly string[] ShapeMembers = ["pin", "subject", "options"];

        /// <summary>A goal revision's flow pin, subject and options, each as canonical JSON.</summary>
        private static string Shape(RecordEnvelope revision)
        {
            JsonElement plan = FlowRuns.Plan(revision);
            return string.Join('\n', ShapeMembers.Select(name =>
                TJson.Object(plan, name) is { ValueKind: JsonValueKind.Object } value ? CanonicalJson.Serialize(value) : "-"));
        }

        /// <summary>A step's inputs as they are handed on now, by the records that carry them and their exact hashes, for its command before the step is derived.</summary>
        private List<Input> EarlyInputs(FlowStep step, string? item) => step.Inputs.Select(input =>
        {
            if (input.From == "subject")
            {
                RecordEnvelope? record = Carrier(input, item);
                return new Input(input.Name, "subject", "subject " + input.Field, record, record?.ComputedHash, null, null);
            }

            Output? now = Producer(input, item)?.Outputs.FirstOrDefault(o => o.Name == input.Output);
            return new Input(input.Name, "step", input.Step + " output " + input.Output, now?.Carrier, now?.Hash, null, null);
        }).ToList();

        /// <summary>The record that carries an input now: the subject field's record, or the producing step instance's output.</summary>
        private RecordEnvelope? Carrier(FlowInput input, string? item) => input.From == "subject"
            ? TJson.Text(TJson.Object(TJson.Object(FlowRuns.Plan(goal), "subject"), input.Field!), "id") is string id && model.ById.TryGetValue(id, out RecordEnvelope? record) ? record : null
            : Producer(input, item)?.Outputs.FirstOrDefault(o => o.Name == input.Output)?.Carrier;

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
                    if (now.Kind == "file" ? before != now.Hash : (pinned.Id != now.Carrier.Id || pinned.Hash != now.Hash) && Published(now.Carrier)?.Id != pinned.Id)
                    {
                        change = $"its input {input.Name} changed after use: its brief pinned {pinned.Type} {pinned.Id}{(before is null ? string.Empty : " at " + before)}, and {source} is now {now.Hash} in {now.Carrier.Type} {now.Carrier.Id}";
                    }
                }
                else if (now is not null && pinned is null && holder is not null && now.Carrier.AtTime > holder.AtTime && !(Published(now.Carrier)?.AtTime <= holder.AtTime))
                {
                    // An assignment made without a pin of the input cannot have used an output recorded after it.
                    change = $"its input {input.Name} changed after use: {source} is now {now.Carrier.Type} {now.Carrier.Id} of {now.Carrier.At}, after assignment {holder.Id}, which pins no input {input.Name}, was made";
                }
                else if (now is not null && usedAt is DateTimeOffset used && now.Carrier.AtTime > used && !(Published(now.Carrier)?.AtTime <= used))
                {
                    change = $"its input {input.Name} changed after use: {source} is now {now.Carrier.Type} {now.Carrier.Id} of {now.Carrier.At}, after the step was done at {Time(used)}";
                }

                inputs.Add(new Input(input.Name, "step", source, now?.Carrier, now?.Hash, pinned, change));
            }

            return inputs;
        }

        /// <summary>
        /// The candidate a release record publishes, when it is the publication of its direct predecessor (ReleaseType.IsPublicationOf):
        /// it differs from the candidate only in its status, publication time and address, and an acceptance of the candidate applies to it
        /// by design, so it is no change of an input a step used as the candidate. Null for any other record.
        /// </summary>
        private RecordEnvelope? Published(RecordEnvelope carrier) =>
            carrier.Type == "release" && carrier.Supersedes is string previous && model.ById.TryGetValue(previous, out RecordEnvelope? candidate) && ReleaseType.IsPublicationOf(candidate, carrier)
                ? candidate : null;

        /// <summary>The review an item takes, as <see cref="FlowRuns.ReviewRequirement"/> derives it for this run's configuration.</summary>
        private FlowReview ReviewOf(FlowStep step, FlowOutput? output) => FlowRuns.ReviewRequirement(flow, configuration, step, output);

        /// <summary>
        /// The latest current review of a result by a principal other than its authors, at any revision of it, among the admitted reviewers
        /// when they are given; null when there is none.
        /// </summary>
        private RecordEnvelope? LatestReview(RecordEnvelope result, IReadOnlySet<string> authors, Func<RecordEnvelope, bool>? admitted = null)
        {
            IReadOnlyList<string> chain = model.Chain(model.Latest(result));
            return model.Current("review").Where(r => r.Subject is not null && chain.Contains(r.Subject.Id) && !authors.Contains(r.By) && r.By != result.By
                    && (admitted is null || admitted(r)))
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
            Func<string, bool> admits = admitted.Contains;
            RecordEnvelope? latest = LatestReview(result, authors, r => admits(r.By));
            return latest is not null && EstateModel.BodyString(latest, "outcome") == "pass" && latest.Subject?.Id == result.Id && latest.Subject.Hash == result.ComputedHash
                && (!person || GoalRules.IsPerson(model, latest.By)) ? latest : null;
        }

        private RecordEnvelope? LatestResult(RecordEnvelope assignment)
        {
            IReadOnlyList<RecordEnvelope> attempts = WorkView.AttemptsOf(model, assignment);
            return attempts.Count == 0 ? null : WorkView.ResultOf(model, attempts[^1]);
        }

        private List<string> Failing(IEnumerable<JsonElement> predicates, IReadOnlyDictionary<string, string> bindings) =>
            predicates.Select(p => GoalPredicates.EvaluatePredicate(model, FlowRuns.Bind(p, bindings))).Where(v => v.Value != "met").Select(v => $"{v.Value}: {v.Reason}").ToList();

        /// <summary>
        /// What sprang a trap, in its own words: the summary of the newest record a bounded predicate of it counts beyond its bound, such as a
        /// failed check that lists each location it found, when a principal who takes the run's steps recorded it, shown as plain text on one
        /// line; empty when the records say nothing more, or say it through anyone else.
        /// </summary>
        private string Sprung(FlowTrap trap, IReadOnlyDictionary<string, string> bindings)
        {
            foreach (JsonElement predicate in trap.Holds.Where(p => p.TryGetProperty("max", out _)))
            {
                JsonElement bound = FlowRuns.Bind(predicate, bindings);
                IReadOnlyList<RecordEnvelope> matching = GoalPredicates.Matching(model, bound);
                if (GoalPredicates.EvaluatePredicate(model, bound).Value != "met" && matching.Count > 0 && Routing.MayTakeStep(model, goal, matching[^1].By, matching[^1].AtTime)
                    && EstateModel.BodyString(matching[^1], "summary") is string summary)
                {
                    RecordEnvelope found = matching[^1];
                    return $": {found.Type} {found.Id} says: {SafeText(summary, 1000)}";
                }
            }

            return string.Empty;
        }

        /// <summary>A record's text as plain text on one line: control and format characters, such as line breaks and direction overrides, become spaces; clipped to a length.</summary>
        private static string SafeText(string text, int length)
        {
            var plain = new StringBuilder(Math.Min(text.Length, length + 3));
            foreach (char c in text)
            {
                if (plain.Length >= length)
                {
                    plain.Append("...");
                    break;
                }

                plain.Append(char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator ? ' ' : c);
            }

            return plain.ToString();
        }

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

    /// <summary>A value a command takes unquoted: letters, digits and . _ : / @ + = , -, not starting with a dash, so a bound value is never an option or a shell word.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9._:/@+=,][A-Za-z0-9._:/@+=,\-]{0,499}\z")]
    private static partial Regex CommandValue();

    /// <summary>
    /// The run's position as text: the flow, its configuration and every step with its state and why; a record-derived document passes
    /// <paramref name="commands"/> false, and its reserved step's command is left out.
    /// </summary>
    public static string RenderText(EstateModel model, Position position, bool commands = true)
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
        foreach (Attention attention in position.RunAttention)
        {
            text.Append("  Required attention (").Append(attention.Kind).Append("): ").Append(attention.Summary).Append(" [").Append(attention.Trigger.Type).Append(' ').Append(attention.Trigger.Id).Append("]\n");
        }

        FlowStops.AppendText(text, model, position.Stops, "  ", commands);

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

            foreach (PendingReview review in step.Reviews)
            {
                text.Append("       review before hand-off: ").Append(review.What).Append(" at ").Append(review.Hash).Append(", ").Append(review.Requirement.Independence)
                    .Append(" in the ").Append(review.Role).Append(" role").Append(review.Assignments.Count == 0 ? ", not yet assigned" : $", assignment {review.Assignments[^1].Id}").Append('\n');
            }

            if (step.Attempts.Count > 0 || step.AttemptsUsed > 0)
            {
                text.Append("       attempts: ").Append(step.Attempts.Count.ToString(CultureInfo.InvariantCulture)).Append(" recorded, ").Append(step.AttemptsUsed.ToString(CultureInfo.InvariantCulture))
                    .Append(" of ").Append(step.AttemptsAllowed.ToString(CultureInfo.InvariantCulture)).Append(" agent runs used");
                text.Append(step.Attempts.Count == 0 ? string.Empty : "; " + string.Join("; ", step.Attempts.Select(a => $"attempt {a.Attempt.Id} under assignment {a.Assignment.Id}: {a.Outcome}{(a.Result is null ? string.Empty : $" (result {a.Result.Id})")}"))).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// The run's position as JSON, the same on every machine for the same records and time; a record-derived document passes
    /// <paramref name="commands"/> false, and its reserved step's command is left out.
    /// </summary>
    public static JsonObject RenderJson(EstateModel model, Position position, bool commands = true)
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
        json["attention"] = new JsonArray(position.RunAttention.Select(a => (JsonNode)AttentionJson(a)).ToArray());
        json["next"] = new JsonArray(position.Steps.Where(s => s.State == "next").Select(s => (JsonNode)s.Name).ToArray());
        json["stops"] = FlowStops.RenderJson(model, position.Stops, commands);
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
            ["stop"] = s.Stop,
            ["command"] = commands ? s.Command : null,
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
            ["traps"] = new JsonArray(s.Step.Traps.Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t.Name,
                ["summary"] = t.Summary,
                ["check"] = t.Check,
                ["records"] = t.Holds.Count > 0,
            }).ToArray()),
            ["failure"] = s.Step.Kind == "work" ? s.Step.FailureRoute.Route : null,
            ["rerun"] = s.Rerun,
            ["attemptsUsed"] = s.AttemptsUsed,
            ["attemptsAllowed"] = s.AttemptsAllowed,
            ["attempts"] = new JsonArray(s.Attempts.Select(a => (JsonNode)new JsonObject
            {
                ["assignment"] = Reference(a.Assignment),
                ["attempt"] = Reference(a.Attempt),
                ["result"] = Reference(a.Result),
                ["outcome"] = a.Outcome,
            }).ToArray()),
            ["reviews"] = new JsonArray(s.Reviews.Select(r => (JsonNode)new JsonObject
            {
                ["items"] = new JsonArray(r.Items.Select(i => (JsonNode)i).ToArray()),
                ["independence"] = r.Requirement.Independence,
                ["role"] = r.Role,
                ["subject"] = Reference(r.Subject),
                ["assignment"] = r.Assignments.Count == 0 ? null : Reference(r.Assignments[^1]),
            }).ToArray()),
            ["attention"] = s.Attention is null ? null : AttentionJson(s.Attention),
            ["failed"] = s.Failed is null ? null : new JsonObject { ["record"] = Reference(s.Failed.Record), ["summary"] = s.Failed.Summary, ["route"] = s.Failed.Route.Route, ["branch"] = s.Failed.Route.Branch, ["step"] = s.Failed.Route.Step },
        }).ToArray());
        return json;

        static JsonObject? Reference(RecordEnvelope? record) => record is null ? null : new JsonObject { ["type"] = record.Type, ["id"] = record.Id, ["hash"] = record.ComputedHash };

        static JsonObject AttentionJson(Attention attention) => new() { ["kind"] = attention.Kind, ["summary"] = attention.Summary, ["trigger"] = Reference(attention.Trigger) };
    }
}
