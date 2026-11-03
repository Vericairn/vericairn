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

    /// <summary>A step's output as it exists now: the record that carries it and its exact hash.</summary>
    /// <param name="Name">The output's name.</param>
    /// <param name="Type">file, record, decision or review.</param>
    /// <param name="Carrier">The record that carries it: a file's result, or the record itself.</param>
    /// <param name="Hash">A file's content hash, or the record's hash.</param>
    /// <param name="MediaType">A file's media type, when its result names one.</param>
    public sealed record Output(string Name, string Type, RecordEnvelope Carrier, string Hash, string? MediaType);

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

    /// <summary>One instance of a step: the step, once or for one item, with its derived state.</summary>
    /// <param name="Step">The flow's step.</param>
    /// <param name="Item">The item, for a step that runs over a list.</param>
    /// <param name="State">One of <see cref="States"/>.</param>
    /// <param name="Reasons">Why it is in that state, the first reason first; every failing predicate of a blocked step.</param>
    /// <param name="Principal">Who holds it or would carry it by its role, from the records; null when no one is named.</param>
    /// <param name="Assignment">The assignment that holds it, the latest of its step.</param>
    /// <param name="Result">The holding assignment's latest result.</param>
    /// <param name="Review">The review that clears it: a work step's review before hand-off, or a review step's own review.</param>
    /// <param name="Outputs">Its outputs as they exist now.</param>
    /// <param name="Inputs">Its inputs, now and as pinned.</param>
    /// <param name="Done">Its done predicates and their verdicts.</param>
    /// <param name="DoneAt">When it was done: its newest carrying record; null when it is not.</param>
    /// <param name="Waiting">For a stale step, whether something it waits on still holds it, so it reruns only once that is done.</param>
    public sealed record Instance(FlowStep Step, string? Item, string State, IReadOnlyList<string> Reasons, string? Principal, RecordEnvelope? Assignment, RecordEnvelope? Result,
        RecordEnvelope? Review, IReadOnlyList<Output> Outputs, IReadOnlyList<Input> Inputs, IReadOnlyList<Check> Done, DateTimeOffset? DoneAt, bool Waiting = false)
    {
        /// <summary>The step instance an assignment names.</summary>
        public FlowRuns.StepRef Ref => new(Step.Key, Item);

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
    public sealed record Position(RecordEnvelope Goal, FlowDefinition? Flow, string? Unavailable, FlowRuns.Configuration? Configuration, RecordEnvelope? Work, IReadOnlyList<Instance> Steps)
    {
        /// <summary>True when every step of the run is done or off: the run is complete, and the goal's evaluation and acceptance remain.</summary>
        public bool Complete => Flow is not null && Steps.Count > 0 && Steps.All(s => s.State is "done" or "off");

        /// <summary>The instances that are part of the run.</summary>
        public IEnumerable<Instance> Running => Steps.Where(s => s.State != "off");

        /// <summary>The instance a step reference names, or null.</summary>
        public Instance? Find(FlowRuns.StepRef step) => Steps.FirstOrDefault(s => s.Ref == step);
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
        private readonly Dictionary<FlowRuns.StepRef, Instance> derived = [];
        private readonly Dictionary<FlowRuns.StepRef, List<RecordEnvelope>> held = work is null ? []
            : Routing.Assignments(model, work).Where(a => FlowRuns.StepOf(a) is not null).GroupBy(a => FlowRuns.StepOf(a)!).ToDictionary(g => g.Key, g => g.ToList());

        public FlowRuns.Configuration Configuration => configuration;

        public List<Instance> Instances { get; } = [];

        public void Add(Instance instance)
        {
            Instances.Add(instance);
            derived[instance.Ref] = instance;
        }

        public static Instance Off(FlowStep step, string? item, string reason) => new(step, item, "off", [reason], null, null, null, null, [], [], [], null);

        public Instance Derive(FlowStep step, string? item)
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
            bool doneMet = done.All(d => d.Verdict.Value == "met");
            var outputs = new List<Output>();
            var missing = new List<string>();
            string? outcome = result is null ? null : EstateModel.BodyString(result, "outcome");
            foreach (FlowOutput output in step.Outputs.Where(o => o.Type != "review"))
            {
                if (output.Type == "file")
                {
                    JsonElement named = result is null || outcome != "completed" ? default : TJson.Array(result.Body, "outputs").FirstOrDefault(o => TJson.Text(o, "name") == output.Name);
                    string? hash = TJson.Text(named, "hash");
                    string? media = TJson.Text(named, "mediaType");
                    if (hash is not null && (output.MediaType is null || media == output.MediaType))
                    {
                        outputs.Add(new Output(output.Name, "file", result!, hash, media));
                    }
                    else
                    {
                        missing.Add(hash is null ? $"its result names no output {output.Name}" : $"its output {output.Name} is {media ?? "of no stated media type"}, not {output.MediaType}");
                    }

                    continue;
                }

                RecordEnvelope? record = done.SelectMany(d => GoalPredicates.Matching(model, d.Predicate))
                    .Where(r => r.Type == output.Counts && (output.Kind is null || EstateModel.BodyString(r, "kind") == output.Kind))
                    .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
                if (record is null)
                {
                    missing.Add($"no {output.Counts} record its done predicates count is its output {output.Name}");
                }
                else
                {
                    outputs.Add(new Output(output.Name, output.Type, record, record.ComputedHash, null));
                }
            }

            // Complete: what the step's kind needs exists. A work step's result completed with every output, its done predicates, and the
            // review it declares before hand-off; a review step's review of the exact result it reviews; any other kind its done predicates.
            RecordEnvelope? review = null;
            RecordEnvelope? findings = null;
            string? pending = null;
            bool complete;
            if (step.Kind == "work")
            {
                IReadOnlySet<string> authors = Authors(assignments);
                findings = result is null ? null : LatestReview(result, authors) is { } latest && EstateModel.BodyString(latest, "outcome") is "findings" or "fail" ? latest : null;
                string required = ReviewOf(step);
                complete = result is not null && outcome == "completed" && missing.Count == 0 && doneMet && findings is null;
                if (complete && required != "none")
                {
                    review = Passing(result!, authors, required == "person");
                    if (review is null)
                    {
                        complete = false;
                        pending = $"its output awaits {(required == "person" ? "a person's" : "an independent")} review before it is handed on: a review of result {result!.Id} with outcome pass, by a principal other than its author";
                    }
                }
            }
            else if (step.Kind == "review")
            {
                Instance? reviewed = step.Inputs.Where(i => i.Step is not null).Select(i => Producer(i, item)).FirstOrDefault();
                RecordEnvelope? subject = reviewed?.Result;
                bool person = flow.Role(step.Role)?.Holder == "person";
                review = subject is null ? null : Passing(subject, Authors(held.GetValueOrDefault(reviewed!.Ref) ?? []), person);
                foreach (FlowOutput output in step.Outputs.Where(o => o.Type == "review"))
                {
                    if (review is not null)
                    {
                        outputs.Add(new Output(output.Name, "review", review, review.ComputedHash, null));
                    }
                }

                complete = review is not null && doneMet && missing.Count == 0;
            }
            else
            {
                complete = doneMet && missing.Count == 0;
            }

            DateTimeOffset? doneAt = complete ? new[] { result, review }.Concat(outputs.Select(o => o.Carrier)).Concat(done.SelectMany(d => GoalPredicates.Matching(model, d.Predicate)))
                .OfType<RecordEnvelope>().Select(r => (DateTimeOffset?)r.AtTime).Max() : null;
            List<Input> inputs = Inputs(step, item, holder, complete && holder is null ? doneAt : null);
            // A review step's own review names the exact result it judged, so a review of the current result is never stale.
            bool stale = inputs.Any(i => i.Change is not null) && (complete || holder is not null) && !(step.Kind == "review" && complete);
            if (complete && !stale)
            {
                return new Instance(step, item, "done", [Completed(step, result, review, done)], Who(holder), holder, result, review, outputs, inputs, done, doneAt);
            }

            // Blocked: what it waits on is not done, or a precondition, a trap or a due time does not hold; every reason is listed. A stale
            // step shows stale, and reruns once nothing it waits on holds it.
            var blocked = new List<string>();
            List<Instance> waits = Dependencies(step, item).ToList();
            foreach (Instance wait in waits.Where(w => w.State != "done"))
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
                Instance? from = Instance(TJson.Text(due, "from")!, item);
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
                return new Instance(step, item, "stale", changed, Who(holder), holder, result, review, outputs, inputs, done, null, blocked.Count > 0);
            }

            if (blocked.Count > 0)
            {
                return new Instance(step, item, "blocked", blocked, Who(holder), holder, result, review, outputs, inputs, done, null);
            }

            Instance Make(string state, string reason, string? principal = null) => new(step, item, state, [reason], principal ?? Who(holder), holder, result, review, outputs, inputs, done, null);

            if (step.Reserved is string act)
            {
                return Make("stopped", $"a reserved act, {act}: it waits on a person who holds it, the operator, and no agent carries it without a grant in force within its bounds, none of which this build issues");
            }

            if (step.Kind == "work" && result is not null && findings is null)
            {
                return Make("in-review", pending ?? $"its result {result.Id} is {outcome}{(missing.Count > 0 ? ", and " + missing[0] : string.Empty)}{(doneMet ? string.Empty : ", and its done predicates do not hold")}: a review with findings sends it back for another attempt");
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
        private IEnumerable<Instance> Dependencies(FlowStep step, string? item)
        {
            var seen = new HashSet<FlowRuns.StepRef>();
            foreach (Instance wait in step.After.SelectMany(key => Through(key, step.Each, item, seen)))
            {
                yield return wait;
            }

            foreach (FlowInput input in step.Inputs.Where(i => i.Step is not null))
            {
                if (Producer(input, item) is Instance producer && seen.Add(producer.Ref))
                {
                    yield return producer;
                }
            }

            if (TJson.Text(TJson.Object(step.When, "due"), "from") is string from)
            {
                foreach (Instance wait in Through(from, step.Each, item, seen))
                {
                    yield return wait;
                }
            }
        }

        private IEnumerable<Instance> Through(string key, string? each, string? item, HashSet<FlowRuns.StepRef> seen)
        {
            FlowStep? waited = flow.Step(key);
            if (waited is null)
            {
                yield break;
            }

            IEnumerable<Instance> instances = waited.Each is not null && waited.Each == each
                ? derived.Values.Where(d => d.Step.Key == key && d.Item == item)
                : derived.Values.Where(d => d.Step.Key == key);
            foreach (Instance instance in instances.ToList())
            {
                if (instance.State == "off")
                {
                    foreach (Instance earlier in waited.After.SelectMany(k => Through(k, waited.Each, instance.Item, seen)))
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
        private Instance? Producer(FlowInput input, string? item) => input.Step is null ? null : Instance(input.Step, item);

        private Instance? Instance(string key, string? item) =>
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

                Instance? producer = Producer(input, item);
                Output? now = producer?.Outputs.FirstOrDefault(o => o.Name == input.Output);
                string source = $"{producer?.Name ?? input.Step} output {input.Output}";
                string? change = null;
                if (now is not null && pinned is not null)
                {
                    string? before = now.Type == "file" && model.ById.TryGetValue(pinned.Id, out RecordEnvelope? carrier)
                        ? TJson.Text(TJson.Array(carrier.Body, "outputs").FirstOrDefault(o => TJson.Text(o, "name") == input.Output), "hash")
                        : pinned.Hash;
                    if (now.Type == "file" ? before != now.Hash : (pinned.Id != now.Carrier.Id || pinned.Hash != now.Hash))
                    {
                        change = $"its input {input.Name} changed after use: its brief pinned {pinned.Type} {pinned.Id}{(before is null ? string.Empty : " at " + before)}, and {source} is now {now.Hash} in {now.Carrier.Type} {now.Carrier.Id}";
                    }
                }
                else if (now is not null && usedAt is DateTimeOffset used && now.Carrier.AtTime > used)
                {
                    change = $"its input {input.Name} changed after use: {source} is now {now.Carrier.Type} {now.Carrier.Id} of {now.Carrier.At}, after the step was done at {Time(used)}";
                }

                inputs.Add(new Input(input.Name, "step", source, now?.Carrier, now?.Hash, pinned, change));
            }

            return inputs;
        }

        /// <summary>The review a work step's output takes: its own, or the flow's review option, or none.</summary>
        private string ReviewOf(FlowStep step) =>
            step.Review ?? (configuration.Value("review") is string option && FlowContract.Reviews.Contains(option) ? option : "none");

        /// <summary>The latest current review of a result by a principal other than its authors, at any revision of it; null when there is none.</summary>
        private RecordEnvelope? LatestReview(RecordEnvelope result, IReadOnlySet<string> authors)
        {
            IReadOnlyList<string> chain = model.Chain(model.Latest(result));
            return model.Current("review").Where(r => r.Subject is not null && chain.Contains(r.Subject.Id) && !authors.Contains(r.By) && r.By != result.By)
                .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
        }

        /// <summary>The latest independent review of a result when it passes the result's exact hash, by a person when one is required; else null.</summary>
        private RecordEnvelope? Passing(RecordEnvelope result, IReadOnlySet<string> authors, bool person)
        {
            RecordEnvelope? latest = LatestReview(result, authors);
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

        List<Instance> running = position.Running.ToList();
        text.Append("Flow: ").Append(position.Flow.Pin.Id).Append(' ').Append(position.Flow.Pin.Version).Append(" (").Append(position.Flow.Pin.Hash).Append("), ")
            .Append(running.Count(s => s.State == "done").ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(running.Count.ToString(CultureInfo.InvariantCulture))
            .Append(running.Count == 1 ? " step" : " steps").Append(" done").Append(position.Complete ? "; the run is complete, and the goal's evaluation and acceptance remain" : string.Empty).Append('\n');
        if (position.Configuration!.Until is string until)
        {
            text.Append("  Ends at: ").Append(until).Append('\n');
        }

        List<FlowRuns.Setting> configured = position.Configuration.Values.Values.Where(v => v.Record is not null).OrderBy(v => v.Option, StringComparer.Ordinal).ToList();
        text.Append("  Configuration: ").Append(configured.Count == 0 ? "the flow's defaults" : string.Join("; ", configured.Select(v => $"{v.Option} {v.Value}, from {v.Source}"))).Append('\n');
        List<Instance> next = running.Where(s => s.State == "next").ToList();
        text.Append("  Next: ").Append(next.Count == 0 ? "none" : string.Join(", ", next.Select(s => s.Name))).Append('\n');
        int index = 0;
        foreach (Instance step in position.Steps)
        {
            text.Append("  ").Append((++index).ToString(CultureInfo.InvariantCulture)).Append(". ").Append(step.Name).Append("  ").Append(step.State).Append("  ").Append(step.Step.Title)
                .Append(" [").Append(step.Step.Kind).Append(", ").Append(step.Step.Role).Append(step.Step.Required ? string.Empty : ", optional").Append(step.Step.Reserved is null ? string.Empty : ", reserved " + step.Step.Reserved).Append("]\n");
            foreach (string reason in step.Reasons)
            {
                text.Append("       ").Append(reason).Append('\n');
            }

            foreach (Output output in step.Outputs)
            {
                text.Append("       output ").Append(output.Name).Append(": ").Append(output.Hash).Append(" in ").Append(output.Carrier.Type).Append(' ').Append(output.Carrier.Id).Append('\n');
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
            ["item"] = s.Item,
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
            ["outputs"] = new JsonArray(s.Outputs.Select(o => (JsonNode)new JsonObject { ["name"] = o.Name, ["type"] = o.Type, ["hash"] = o.Hash, ["mediaType"] = o.MediaType, ["carrier"] = Reference(o.Carrier) }).ToArray()),
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
