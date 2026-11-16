// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// The decisions of decision transitions (from 0.9.4, PROC-19; the estate's design/flow.md § 5, decision 01M75YYK958AFMS24W71BHRH26). A
/// step's decision is its item of kind decision, always on record. A role's principal, an agent or a person, records it as the result of
/// its current assignment of the step, whose decision field names the path or paths taken, the reasons and the evidence by hash, signed by
/// the decider's own device. A rule's decision, and the default a deadline takes, is recorded by the principal who takes the run's step, as
/// the runtime evaluated it: an observation of kind flow-decision naming the path, the rule that matched and the records it read by id and
/// hash; the position reads it and never evaluates the rule again. The first decision of a round that takes effect is final: a further one
/// in the same round counts for nothing and validation reports it, and a new round starts only by the flow's own course, a path back, a
/// hold or what the step waits on done again. A step's decision is distinct from the estate's decision records, which stay the operator's,
/// and choosing a path performs nothing reserved and never turns off a step an estate policy requires or a required reserved step.
/// </summary>
public static partial class FlowDecisions
{
    /// <summary>The longest reasons a decision records.</summary>
    public const int MaximumReasons = 4000;

    /// <summary>The observation kind that records a rule's decision, or the default a deadline takes.</summary>
    public const string ObservationKind = "flow-decision";

    /// <summary>
    /// A decision on record: a principal's, the result of its assignment, or a rule's, an observation of kind flow-decision; the path or
    /// paths taken, the reasons and the evidence by hash, or for a rule's the records it read.
    /// </summary>
    /// <param name="Record">The record that carries it.</param>
    /// <param name="Paths">The path or paths taken, as recorded.</param>
    /// <param name="Reasons">The decider's reasons, or the rule's.</param>
    /// <param name="Evidence">The evidence it rests on, or the records the rule read, each an exact record by hash.</param>
    public sealed record Recorded(RecordEnvelope Record, IReadOnlyList<string> Paths, string Reasons, IReadOnlyList<RecordReference> Evidence)
    {
        /// <summary>Who recorded it: the decider, or the principal who took the step for a rule.</summary>
        public string By => Record.By;

        /// <summary>True for a rule's decision, or a deadline's default, which the runtime evaluated.</summary>
        public bool Ruled => Record.Type == "observation";

        /// <summary>For a rule's decision, the rule that matched, its number from 1, default or deadline.</summary>
        public string? Rule { get; init; }

        /// <summary>For a principal's decision, the assignment it was recorded under.</summary>
        public RecordEnvelope? Assignment { get; init; }

        /// <summary>When it was recorded.</summary>
        public DateTimeOffset At => Record.AtTime;
    }

    /// <summary>A result's decision, or a flow-decision observation's, when the record carries one; null otherwise.</summary>
    public static Recorded? Of(RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Type == "observation" && EstateModel.BodyString(record, "kind") == ObservationKind)
        {
            JsonElement facts = TJson.Object(record.Body, "facts");
            return new Recorded(record, TJson.Strings(facts, "paths").ToList(), EstateModel.BodyString(record, "summary") ?? string.Empty, TJson.Strings(facts, "read").Select(ReadOf).OfType<RecordReference>().ToList())
            { Rule = TJson.Text(facts, "rule") };
        }

        if (record.Type != "result" || TJson.Object(record.Body, "decision") is not { ValueKind: JsonValueKind.Object } decision)
        {
            return null;
        }

        List<RecordReference> evidence = TJson.Array(decision, "evidence")
            .Where(e => TJson.Text(e, "type") is not null && TJson.Text(e, "id") is not null)
            .Select(e => new RecordReference(TJson.Text(e, "type")!, TJson.Text(e, "id")!, TJson.Text(e, "hash"))).ToList();
        return new Recorded(record, TJson.Strings(decision, "paths").ToList(), TJson.Text(decision, "reasons") ?? string.Empty, evidence);
    }

    /// <summary>A record a rule read, as a flow-decision observation names it: its type, id and hash separated by spaces.</summary>
    public static string ReadText(RecordReference read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return $"{read.Type} {read.Id} {read.Hash}";
    }

    private static RecordReference? ReadOf(string text) =>
        text.Split(' ') is [string type, string id, string hash] ? new RecordReference(type, id, hash) : null;

    /// <summary>
    /// Why a decision naming these paths is refused for a step's transition: no path, a path the transition does not declare or named
    /// twice, several paths where the flow allows one, or several where one of them goes back, ends the run or hands it to another flow,
    /// which is taken alone. Empty when it holds.
    /// </summary>
    public static List<string> PathRefusals(FlowDefinition flow, FlowStep step, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(paths);
        var refusals = new List<string>();
        if (step.Transition is not FlowTransition transition)
        {
            refusals.Add($"step {step.Key} declares no decision transition");
            return refusals;
        }

        string declared = string.Join(", ", transition.Paths.Select(p => $"{p.Name} ({p.Leads})"));
        if (paths.Count == 0)
        {
            refusals.Add($"a decision names the path it takes, one of: {declared}");
        }

        foreach (string path in paths.Where(p => transition.Path(p) is null))
        {
            refusals.Add($"'{path}' is not a path of step {step.Key}, whose paths are {declared}");
        }

        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Count)
        {
            refusals.Add("each path is named once");
        }

        if (paths.Count > 1 && !transition.Several)
        {
            refusals.Add($"step {step.Key}'s transition takes one path; the flow does not let one decision take several");
        }
        else if (paths.Count > 1)
        {
            foreach (FlowPath alone in paths.Select(transition.Path).OfType<FlowPath>().Where(p => p.Ends || FlowTransition.IsBack(p, flow.IndexOf, step.Key)))
            {
                refusals.Add($"path {alone.Name} leads to {alone.Leads}, which is taken alone, never with other paths");
            }
        }

        return refusals;
    }

    /// <summary>True when only a person decides a step: its transition requires one or is never delegated, or the flow marks its role a person's.</summary>
    public static bool PersonDecides(FlowDefinition flow, FlowStep step)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(step);
        return step.Transition is { PersonOnly: true } || flow.Role(step.Role)?.Holder == "person";
    }

    /// <summary>
    /// The steps no decision ever turns off (from 0.9.4, PROC-19): each step an active estate policy requires of the flow, and each
    /// required reserved step, by key with why.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Protected(EstateModel model, FlowDefinition flow, string? product, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(flow);
        var kept = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, RecordEnvelope policy) in FlowRuns.PolicyRequired(model, flow, product, before).Where(p => !p.Key.Contains('.', StringComparison.Ordinal)))
        {
            kept.TryAdd(key, $"estate policy {EstateModel.BodyString(policy, "key") ?? policy.Id} requires it");
        }

        foreach (FlowStep step in flow.Steps.Where(s => s.Required && s.Reserved is not null))
        {
            kept.TryAdd(step.Key, $"it is a required reserved step ({step.Reserved})");
        }

        return kept;
    }

    /// <summary>
    /// The steps a decision taking these paths would turn off (from 0.9.4, PROC-19): none for a path back, after which the step decides
    /// again; for a path that ends the run, every step that is not done, or, given no position, every step the deciding step does not wait
    /// on; otherwise each step a path not taken leads to, each step of a branch a path not taken enters, and every step that waits only on
    /// steps turned off.
    /// </summary>
    public static IReadOnlyList<string> WouldTurnOff(FlowDefinition flow, FlowStep step, IReadOnlyList<string> paths, Func<string, bool>? notDone = null)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(paths);
        if (step.Transition is not FlowTransition transition)
        {
            return [];
        }

        List<FlowPath> taken = paths.Select(transition.Path).OfType<FlowPath>().ToList();
        if (taken.Any(p => FlowTransition.IsBack(p, flow.IndexOf, step.Key)))
        {
            // A path back turns nothing off: the step decides again once the run comes back to it.
            return [];
        }

        if (taken.Any(p => p.Ends))
        {
            HashSet<string> waited = Ancestors(flow, step.Key);
            return flow.Steps.Where(s => s.Key != step.Key && (notDone is null ? !waited.Contains(s.Key) : notDone(s.Key))).Select(s => s.Key).ToList();
        }

        var off = new HashSet<string>(StringComparer.Ordinal);
        int at = flow.IndexOf(step.Key);
        foreach (FlowPath path in transition.Paths.Where(p => !taken.Contains(p)))
        {
            if (path.Step is string target && flow.IndexOf(target) > at && taken.All(t => t.Step != target))
            {
                off.Add(target);
            }

            if (path.Branch is string branch && taken.All(t => t.Branch != branch) && flow.Branches.FirstOrDefault(b => b.Name == branch) is FlowBranch entered)
            {
                off.UnionWith(entered.Steps);
            }
        }

        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (FlowStep later in flow.Steps.Skip(at + 1).Where(s => !off.Contains(s.Key)))
            {
                List<string> waits = later.After.Concat(later.Inputs.Select(i => i.Step).OfType<string>()).Distinct(StringComparer.Ordinal).ToList();
                if (waits.Count > 0 && waits.All(off.Contains))
                {
                    grew |= off.Add(later.Key);
                }
            }
        }

        return flow.Steps.Where(s => off.Contains(s.Key)).Select(s => s.Key).ToList();
    }

    /// <summary>The steps a step waits on, directly or through the steps they wait on, by after, inputs or timing.</summary>
    internal static HashSet<string> Ancestors(FlowDefinition flow, string key)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([key]);
        while (pending.Count > 0)
        {
            if (flow.Step(pending.Pop()) is not FlowStep step)
            {
                continue;
            }

            foreach (string waited in step.After.Concat(step.Inputs.Select(i => i.Step).OfType<string>()).Append(FlowTiming.WaitedOf(step.When)).OfType<string>())
            {
                if (seen.Add(waited))
                {
                    pending.Push(waited);
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// The principals' decisions that count on record for a step, oldest first: each a completed result of one of the step's assignments,
    /// by its contributor, naming paths its transition offers as the flow allows, by a person where only a person decides, and recorded under
    /// the step's current assignment when it was recorded, never a displaced one.
    /// </summary>
    public static List<Recorded> PrincipalDecisions(EstateModel model, FlowDefinition flow, FlowStep step, IReadOnlyList<RecordEnvelope> assignments)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(assignments);
        bool person = PersonDecides(flow, step);
        var found = new List<Recorded>();
        foreach (RecordEnvelope assignment in assignments)
        {
            string? holder = EstateModel.BodyString(assignment, "contributor");
            foreach (RecordEnvelope attempt in WorkView.AttemptsOf(model, assignment))
            {
                if (WorkView.ResultOf(model, attempt) is not RecordEnvelope result || result.By != holder || EstateModel.BodyString(result, "outcome") != "completed"
                    || Of(result) is not Recorded decision || PathRefusals(flow, step, decision.Paths).Count > 0 || (person && !GoalRules.IsPerson(model, result.By))
                    || Displaced(model, assignments, assignment, result))
                {
                    continue;
                }

                found.Add(decision with { Assignment = assignment });
            }
        }

        return found.OrderBy(d => d.Record.At, StringComparer.Ordinal).ThenBy(d => d.Record.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>True when a later assignment of the step, recorded before a decision, displaced the assignment it was recorded under.</summary>
    private static bool Displaced(EstateModel model, IReadOnlyList<RecordEnvelope> assignments, RecordEnvelope assignment, RecordEnvelope decided)
    {
        IReadOnlyList<string> own = model.Chain(model.Latest(assignment));
        RecordEnvelope? current = assignments.Where(a => GoalRules.Before(a, decided)).OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();
        return current is not null && !own.Contains(current.Id);
    }

    /// <summary>The rule's decisions on record for a run's step: current flow-decision observations about any revision of its goal naming the step, oldest first.</summary>
    public static List<Recorded> RuleDecisions(EstateModel model, RecordEnvelope goal, string step)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        IReadOnlyList<string> chain = model.Chain(model.Latest(goal));
        return model.Current("observation")
            .Where(o => EstateModel.BodyString(o, "kind") == ObservationKind && o.Subject is not null && chain.Contains(o.Subject.Id) && TJson.Text(TJson.Object(o.Body, "facts"), "step") == step)
            .OrderBy(o => o.At, StringComparer.Ordinal).ThenBy(o => o.Id, StringComparer.Ordinal)
            .Select(Of).OfType<Recorded>().ToList();
    }

    /// <summary>
    /// The decisions of a step that stand, oldest first, one at most per round of each assignment: a principal's decision a review sent back
    /// before the next was recorded never stands, a decision recorded while an earlier one of the same assignment stood counts for nothing,
    /// and a hold that took effect opens a new round; and the first rule decision recorded for each round, by the time it read the records.
    /// </summary>
    public static List<Recorded> Standing(EstateModel model, FlowDefinition flow, FlowStep step, IReadOnlyList<Recorded> principals, IReadOnlyList<Recorded> rules)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(rules);
        var kept = new List<Recorded>();
        foreach (IGrouping<string, Recorded> held in principals.GroupBy(d => model.Chain(d.Assignment!)[0], StringComparer.Ordinal))
        {
            Recorded? open = Open(model, flow, step, held.ToList(), null, kept);
            if (open is not null)
            {
                kept.Add(open);
            }
        }

        kept.AddRange(rules.GroupBy(r => EstateModel.BodyString(r.Record, "asOf") ?? r.Record.At, StringComparer.Ordinal).Select(g => g.First()));
        return kept.OrderBy(d => d.Record.At, StringComparer.Ordinal).ThenBy(d => d.Record.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Walks one assignment's decisions, oldest first, as of a record when one is given: each hold that took effect is kept, and the decision
    /// still standing at the end is returned, the one a further decision recorded then would conflict with; null when none stands.
    /// </summary>
    private static Recorded? Open(EstateModel model, FlowDefinition flow, FlowStep step, List<Recorded> decisions, RecordEnvelope? before, List<Recorded>? holds)
    {
        Recorded? current = null;
        foreach (Recorded decision in decisions)
        {
            if (current is not null)
            {
                if (SentBack(model, current, decision.Record))
                {
                    current = null;
                }
                else if (IsHold(step, current) && TookEffect(model, flow, step, current, decision.Record))
                {
                    holds?.Add(current);
                    current = null;
                }
                else
                {
                    continue;
                }
            }

            current = decision;
        }

        if (current is not null && (SentBack(model, current, before) || (before is not null && IsHold(step, current) && TookEffect(model, flow, step, current, before))))
        {
            return null;
        }

        return current;
    }

    /// <summary>True for a decision that takes the path back to the deciding step itself, a hold.</summary>
    private static bool IsHold(FlowStep step, Recorded decision) =>
        decision.Paths.Select(p => step.Transition?.Path(p)).Any(p => p?.Step == step.Key);

    /// <summary>True when a review of a principal's decision found what to change in it or failed it, recorded before a record when one is given.</summary>
    private static bool SentBack(EstateModel model, Recorded decision, RecordEnvelope? before) =>
        !decision.Ruled && Reviews(model, decision, before).LastOrDefault() is RecordEnvelope latest && EstateModel.BodyString(latest, "outcome") is "findings" or "fail";

    /// <summary>
    /// True when a decision took effect, as of a record when one is given: a rule's, a person's, and an agent's that takes no review or whose
    /// latest review of its exact hash, by a principal other than its decider who meets the review's independence, passes.
    /// </summary>
    public static bool TookEffect(EstateModel model, FlowDefinition flow, FlowStep step, Recorded decision, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Ruled || GoalRules.IsPerson(model, decision.By) || step.Transition?.Review is not { Independence: not "none" } required)
        {
            return true;
        }

        RecordEnvelope? latest = Reviews(model, decision, before).LastOrDefault(r => required.Independence switch
        {
            "person" => GoalRules.IsPerson(model, r.By),
            "other-harness" => FlowRuns.Harness(model, r.By) is string harness && !string.Equals(harness, FlowRuns.Harness(model, decision.By), StringComparison.OrdinalIgnoreCase),
            _ => true,
        });
        return latest is not null && EstateModel.BodyString(latest, "outcome") == "pass";
    }

    /// <summary>The reviews of a principal's decision at its exact hash by a principal other than its decider, oldest first, before a record when one is given.</summary>
    private static List<RecordEnvelope> Reviews(EstateModel model, Recorded decision, RecordEnvelope? before) =>
        model.About(decision.Record.Id, "review").Where(r => model.IsCurrent(r) && r.Subject?.Hash == decision.Record.ComputedHash && r.By != decision.By && (before is null || GoalRules.Before(r, before)))
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Validates a result's decision field (record format 3): the paths, the reasons and the evidence, each an exact record by hash; and,
    /// where the run's flow resolves here, that the result is a completed result of the current assignment of a decision step whose
    /// transition its role decides in the run's configuration, naming its declared paths as the flow allows, by a person where only a person
    /// decides; no further decision after one that stands in the same round; and no path that would turn off a step an estate policy
    /// requires or a required reserved step.
    /// </summary>
    internal static void Validate(ValidationContext context, BodyReader body, string? outcome)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(body);
        if (!body.Since(3, "decision") || body.Nested("decision", false) is not BodyReader decision)
        {
            return;
        }

        IReadOnlyList<string> paths = decision.StringArray("paths", true, allowEmpty: false, maxItemLength: 60, itemPattern: Patterns.Slug, patternHint: "a path name") ?? [];
        string? reasons = decision.Text("reasons", true, MaximumReasons);
        if (reasons is not null && string.IsNullOrWhiteSpace(reasons))
        {
            decision.Error("result.decision", "reasons", "A decision records its decider's reasons, not blank text.");
        }

        foreach (BodyReader evidence in decision.NestedArray("evidence", true, allowEmpty: true) ?? [])
        {
            string? type = evidence.Text("type", true, 100, Patterns.Slug, "a record type");
            string? id = evidence.Ulid("id", true);
            string? hash = evidence.Hash("hash", true);
            evidence.NoOtherKeys();
            if (type is not null && context.RequireReference("decision.evidence", id, type) is RecordEnvelope pinned && pinned.ComputedHash != hash)
            {
                evidence.Error("result.decision", "hash", "The evidence does not have the recorded content hash.");
            }
        }

        decision.NoOtherKeys();
        if (outcome is not null && outcome != "completed")
        {
            context.Error("result.decision", "A decision is the completed result of its step's assignment: its outcome is completed.");
        }

        RecordEnvelope? attempt = context.Record.Subject is null ? null : context.Find(context.Record.Subject.Id);
        RecordEnvelope? assignment = attempt?.Subject is null ? null : context.Find(attempt.Subject.Id);
        FlowRuns.StepRef? named = assignment is null ? null : FlowRuns.StepOf(assignment);
        RecordEnvelope? order = assignment?.Subject is null ? null : context.Find(assignment.Subject.Id);
        RecordEnvelope? run = order is { Type: "work-order" } ? FlowRuns.RunOf(context.Model, order, context.Record) : null;
        if (attempt is not { Type: "attempt" } || named is null || named.Review || run is null)
        {
            context.Error("result.decision", "A decision is recorded on an attempt of an assignment that carries a decision step of a run's flow, never on a review or on other work.");
            return;
        }

        if (FlowRuns.Resolve(context.Model, run).Flow is not FlowDefinition flow)
        {
            return;
        }

        FlowStep? step = flow.Step(named.Key);
        if (step is not { Decides: true })
        {
            context.Error("result.decision", $"Step {named.Key} of flow {flow.Pin.Id} is not a decision step whose role decides its transition.");
            return;
        }

        string decider = FlowRuns.Configure(context.Model, run, flow, context.Record).Decider(step);
        if (decider == "rule")
        {
            context.Error("result.decision", $"Step {step.Key}'s transition is decided by a rule in this run, from the records; no principal records its decision.");
        }

        foreach (string refusal in PathRefusals(flow, step, paths))
        {
            context.Error("result.decision", char.ToUpperInvariant(refusal[0]) + refusal[1..] + ".");
        }

        if (PersonDecides(flow, step) && !GoalRules.IsPerson(context.Model, context.Record.By))
        {
            context.Error("result.decision", $"Step {step.Key}'s decision is a person's{(step.Transition!.Undelegable is string act ? $", an act the operating-modes design never delegates ({act})" : step.Transition.PersonOnly ? ", as the flow requires" : $", since the flow marks the {step.Role} role a person's")}, and {Routing.NameOf(context.Model, context.Record.By)} is not a person.");
        }

        // Only the step's current assignment decides: a decision under a displaced one counts for nothing.
        IReadOnlyList<RecordEnvelope> assignments = Routing.Assignments(context.Model, order!, named).Where(a => GoalRules.Before(a, context.Record)).ToList();
        if (Displaced(context.Model, assignments, assignment!, context.Record))
        {
            context.Error("result.decision", $"Assignment {assignment!.Id} no longer holds step {step.Key}: a later assignment of it displaced it before this decision, and only the step's current assignment decides.");
            return;
        }

        // The first decision of a round that stands is final: a further one counts for nothing.
        List<Recorded> earlier = PrincipalDecisions(context.Model, flow, step, assignments)
            .Where(d => context.Model.Chain(context.Model.Latest(d.Assignment!)).Contains(assignment!.Id) && GoalRules.Before(d.Record, context.Record) && d.Record.Id != context.Record.Id).ToList();
        if (Open(context.Model, flow, step, earlier, context.Record, null) is Recorded standing)
        {
            context.Error("result.decision", $"Step {step.Key}'s decision in this round is result {standing.Record.Id}, path {string.Join(", ", standing.Paths)}, which stands and is final: a further decision counts for nothing until the run's own course opens a new round, a path back, a hold or what the step waits on done again.");
        }

        // A decision never turns off a step an estate policy requires or a required reserved step.
        IReadOnlyDictionary<string, string> kept = Protected(context.Model, flow, FlowRuns.Product(context.Model, run, flow), context.Record);
        foreach (string off in WouldTurnOff(flow, step, paths).Where(kept.ContainsKey))
        {
            context.Error("result.decision-protected", $"Step {step.Key}'s decision takes {string.Join(", ", paths)}, which would turn off step {off}: {kept[off]}, and a decision never turns it off.");
        }

    }

    /// <summary>
    /// Validates a flow-decision observation (record format 3), a rule's decision or a deadline's default as the runtime evaluated it: its
    /// goal is a run, its author takes the run's steps, its step's transition is decided by a rule in the run's configuration, or takes its
    /// default at a deadline, the path is the one the rule names, each record it read exists at its hash, and no other rule decision was
    /// recorded for the same round.
    /// </summary>
    internal static void ValidateObservation(ValidationContext context, BodyReader? facts, string? asOf)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (facts is null)
        {
            return;
        }

        string? key = facts.Text("step", true, 100, Patterns.Slug, "a step key");
        IReadOnlyList<string> paths = facts.StringArray("paths", true, allowEmpty: false, maxItemLength: 60, itemPattern: Patterns.Slug, patternHint: "a path name") ?? [];
        string? rule = facts.Text("rule", true, 20, RulePattern(), "a rule's number from 1, default or deadline");
        IReadOnlyList<string> read = facts.StringArray("read", true, allowEmpty: true, maxItemLength: 300) ?? [];
        foreach (string entry in read)
        {
            if (ReadOf(entry) is not RecordReference reference)
            {
                facts.Error("observation.flow-decision", "read", $"'{entry}' is not a record read as its type, id and hash separated by spaces.");
            }
            else if (context.Find(reference.Id) is not RecordEnvelope found || found.Type != reference.Type || found.ComputedHash != reference.Hash)
            {
                facts.Error("observation.flow-decision", "read", $"{reference.Type} {reference.Id} at {reference.Hash} is not a record of this estate at that hash.");
            }
        }

        RecordEnvelope? goal = context.Record.Subject is null ? null : context.Find(context.Record.Subject.Id);
        if (goal is not { Type: "goal" } || !FlowRuns.IsRun(goal))
        {
            context.Error("observation.flow-decision", "A flow decision is about the exact revision of a goal that is a run of a flow.");
            return;
        }

        if (!Routing.MayTakeStep(context.Model, goal, context.Record.By, context.Record.AtTime, context.Record))
        {
            context.Error("observation.flow-decision", $"{Routing.NameOf(context.Model, context.Record.By)} does not take steps of goal {goal.Id}, so does not record its rule's decision: an operator, the goal's sponsor when a person, or a principal holding local-engineering does.");
        }

        if (FlowRuns.Resolve(context.Model, goal).Flow is not FlowDefinition flow || key is null || rule is null)
        {
            return;
        }

        if (flow.Step(key) is not { Transition: FlowTransition transition } step)
        {
            context.Error("observation.flow-decision", $"Flow {flow.Pin.Id} has no decision step '{key}'.");
            return;
        }

        string decider = FlowRuns.Configure(context.Model, goal, flow, context.Record).Decider(step);
        string? expected = rule switch
        {
            "default" => decider != "role" ? transition.Default : null,
            "deadline" => transition.Deadline == "default" && decider != "rule" ? transition.Default : null,
            _ => decider != "role" && int.TryParse(rule, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number >= 1 && number <= transition.Rules.Count ? transition.Rules[number - 1].Path : null,
        };
        if (expected is null)
        {
            context.Error("observation.flow-decision", rule == "deadline"
                ? $"Step {key}'s transition takes no default at a deadline in this run, or a rule decides it."
                : $"Step {key}'s transition is not decided by a rule in this run, or has no rule {rule}.");
        }
        else if (paths.Count != 1 || paths[0] != expected)
        {
            context.Error("observation.flow-decision", $"Step {key}'s {(rule is "default" or "deadline" ? "default" : "rule " + rule)} takes path {expected}, not {string.Join(", ", paths)}.");
        }

        // A round's rule decision is final: a later one is for a round that began after the earlier decision was recorded.
        IReadOnlyList<string> chain = context.Model.Chain(context.Model.Latest(goal));
        if (asOf is not null && RecordParser.IsTimestamp(asOf))
        {
            DateTimeOffset began = DateTimeOffset.Parse(asOf, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            if (began > context.Record.AtTime)
            {
                context.Error("observation.flow-decision", "A rule's decision reads the records as they stood when its round began, which is no later than it is recorded.");
            }

            if (context.Model.Current("observation").FirstOrDefault(o => o.Id != context.Record.Id && GoalRules.Before(o, context.Record) && EstateModel.BodyString(o, "kind") == ObservationKind
                && o.Subject is not null && chain.Contains(o.Subject.Id) && TJson.Text(TJson.Object(o.Body, "facts"), "step") == key && o.AtTime >= began) is RecordEnvelope earlier)
            {
                context.Error("observation.flow-decision", $"Step {key}'s rule decision for the round from {asOf} is already recorded in observation {earlier.Id}, and it is final.");
            }
        }
    }

    [GeneratedRegex(@"^([1-9][0-9]{0,3}|default|deadline)$")]
    private static partial Regex RulePattern();

    /// <summary>The flow-decision observation that records a rule's decision, or a deadline's default, as the position derives it pending.</summary>
    public static JsonObject RuleBody(FlowPosition.StepInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        FlowPosition.StepDecision decision = instance.Decision ?? throw new ArgumentException("The step has no decision to record.", nameof(instance));
        string rule = decision.Rule ?? "default";
        string who = rule switch { "deadline" => "its deadline's default", "default" => "its rule's default", _ => "its rule " + rule };
        string summary = $"Step {instance.Name} takes path {string.Join(", ", decision.Paths)} by {who}, as the records stood at {RecordParser.FormatTimestamp(decision.At ?? decision.Since)}: {decision.Reasons}";
        return new JsonObject
        {
            ["kind"] = ObservationKind,
            ["asOf"] = RecordParser.FormatTimestamp(decision.At ?? decision.Since),
            ["summary"] = summary.Length > 4000 ? summary[..3999] + "…" : summary,
            ["facts"] = new JsonObject
            {
                ["step"] = instance.Step.Key,
                ["paths"] = new JsonArray(decision.Paths.Select(p => (JsonNode)p).ToArray()),
                ["rule"] = rule,
                ["read"] = new JsonArray(decision.Evidence.Where(e => e.Hash is not null).Take(200).Select(e => (JsonNode)ReadText(e)).ToArray()),
            },
            ["limitations"] = new JsonArray("A decision only makes the steps its path leads to eligible; it performs nothing reserved, and the records read after the round began never change it."),
        };
    }

    /// <summary>A decision the runtime may record: the step, the assignment it is recorded under, an attempt to reuse when one is open, and the decision; or why not.</summary>
    /// <param name="Step">The step instance decided, in words.</param>
    /// <param name="Assignment">The assignment that holds the step for its decider.</param>
    /// <param name="Attempt">The decider's open attempt on it, which the result answers; null when a new attempt is recorded with it.</param>
    /// <param name="Paths">The path or paths taken.</param>
    /// <param name="Reasons">The reasons.</param>
    /// <param name="Evidence">The evidence, each an exact record by hash.</param>
    /// <param name="Refusals">Every reason it is refused, all at once; empty when it may be recorded.</param>
    public sealed record Plan(string Step, RecordEnvelope? Assignment, RecordEnvelope? Attempt, IReadOnlyList<string> Paths, string Reasons, IReadOnlyList<RecordReference> Evidence, IReadOnlyList<string> Refusals)
    {
        /// <summary>The attempt body recorded with the decision when the decider has no open attempt on the assignment.</summary>
        public JsonObject AttemptBody(DateTimeOffset at) => new()
        {
            ["startedAt"] = RecordParser.FormatTimestamp(at),
            ["workspace"] = "the run's decision, recorded through the runtime",
            ["note"] = $"Decide step {Step}'s transition.",
        };

        /// <summary>The result body that records the decision: completed, its summary the paths and reasons, and its decision field.</summary>
        public JsonObject ResultBody()
        {
            string taken = string.Join(", ", Paths);
            string summary = $"Decided step {Step}: path{(Paths.Count == 1 ? string.Empty : "s")} {taken}. {Reasons}";
            return new JsonObject
            {
                ["outcome"] = "completed",
                ["summary"] = summary.Length > 8000 ? summary[..7999] + "…" : summary,
                ["outputs"] = new JsonArray(),
                ["evidence"] = new JsonArray(),
                ["checks"] = new JsonArray(),
                ["limitations"] = new JsonArray("A decision only makes the steps its path leads to eligible; it performs nothing reserved."),
                ["unresolvedEffects"] = new JsonArray(),
                ["nextStep"] = $"The run takes path {taken} when goal step is called.",
                ["decision"] = new JsonObject
                {
                    ["paths"] = new JsonArray(Paths.Select(p => (JsonNode)p).ToArray()),
                    ["reasons"] = Reasons,
                    ["evidence"] = new JsonArray(Evidence.Select(e => (JsonNode)new JsonObject { ["type"] = e.Type, ["id"] = e.Id, ["hash"] = e.Hash }).ToArray()),
                },
            };
        }
    }

    /// <summary>The code of a refused decision that would turn off a step no decision turns off, which the refusal names.</summary>
    public const string ProtectedCode = "flow.decision-protected";

    /// <summary>
    /// Plans a principal's decision of a run's step (from 0.9.4, PROC-19), refusing every problem at once before anything is written: a goal
    /// that is not an active run, a step that is not a decision step its role decides in this run, one not waiting on its decider (not yet
    /// ready, already decided, decided by its rule, in review, or held by attention), a decider that does not hold the step's current
    /// assignment for this round, a decider who is not a person where only a person decides, a path the transition does not offer, a path
    /// that would turn off a step an estate policy requires or a required reserved step, blank reasons, and evidence that is not a record of
    /// the estate. The evidence is named by id, or type:id, and pinned at its current hash.
    /// </summary>
    public static Plan Prepare(EstateModel model, RecordEnvelope goal, string by, string step, IReadOnlyList<string> paths, string? reasons, IReadOnlyList<string> evidence, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(evidence);
        var refusals = new List<string>();
        RecordEnvelope current = model.Latest(goal);
        var pinned = new List<RecordReference>();
        foreach (string named in evidence)
        {
            string id = named.Contains(':', StringComparison.Ordinal) ? named[(named.IndexOf(':', StringComparison.Ordinal) + 1)..] : named;
            string? type = named.Contains(':', StringComparison.Ordinal) ? named[..named.IndexOf(':', StringComparison.Ordinal)] : null;
            if (!model.ById.TryGetValue(id, out RecordEnvelope? record) || (type is not null && record.Type != type))
            {
                refusals.Add($"Evidence {named} is not a record of this estate{(type is null ? string.Empty : $" of type {type}")}.");
                continue;
            }

            pinned.Add(new RecordReference(record.Type, record.Id, record.ComputedHash));
        }

        if (string.IsNullOrWhiteSpace(reasons))
        {
            refusals.Add("A decision records its reasons: why this path, from the evidence it names.");
        }
        else if (reasons.Length > MaximumReasons)
        {
            refusals.Add($"A decision's reasons are at most {MaximumReasons} characters.");
        }

        Plan Refused(params string[] more) => new(step, null, null, paths, reasons ?? string.Empty, pinned, [.. refusals, .. more]);
        if (!FlowRuns.IsRun(current))
        {
            return Refused($"Goal {current.Id} is not a run of a flow; only a run's steps are decided.");
        }

        string status = GoalRules.Status(model, current);
        if (status != "active")
        {
            return Refused($"Goal {current.Id} is {status}: a step of a run is decided only while its goal is active.");
        }

        FlowPosition.Position position = FlowPosition.Derive(model, current, at);
        if (position.Flow is not FlowDefinition flow)
        {
            return Refused($"The run's {position.Unavailable}: none of its steps is decided on this machine.");
        }

        FlowPosition.StepInstance? instance = position.Steps.FirstOrDefault(s => s.Name == step);
        if (instance is null)
        {
            return Refused($"Run {current.Id} has no step {step}; its steps are {string.Join(", ", position.Steps.Select(s => s.Name))}.");
        }

        if (!instance.Step.Decides || instance.Step.Transition is not FlowTransition transition)
        {
            return Refused($"Step {step} is not a decision step whose role decides its transition.");
        }

        if (position.Configuration!.Decider(instance.Step) == "rule")
        {
            return Refused($"Step {step}'s transition is decided by a rule in this run, from the records; no principal decides it.");
        }

        refusals.AddRange(PathRefusals(flow, instance.Step, paths).Select(r => char.ToUpperInvariant(r[0]) + r[1..] + "."));
        if (instance.Decision is not { State: "pending" or "held", Record: null } waiting || waiting.Decider is "rule" or "default" or "deadline")
        {
            refusals.Add(instance.Decision is { State: "pending", Decider: "rule" or "default" or "deadline" } ruled
                ? $"Step {step} is decided by {(ruled.Decider == "deadline" ? "its deadline's default" : "its rule")} this round, path {string.Join(", ", ruled.Paths)}: goal step records that decision, and no principal decides it."
                : instance.Decision is { } decided
                ? $"Step {step} is not waiting on its decider: its decision is {decided.State}{(decided.Paths.Count == 0 ? string.Empty : $", path {string.Join(", ", decided.Paths)}")} ({(instance.Reasons.Count > 0 ? instance.Reasons[0] : instance.State)})."
                : $"Step {step} is not waiting on its decider: it is {instance.State} ({(instance.Reasons.Count > 0 ? instance.Reasons[0] : "no reason given")}).");
        }

        if (PersonDecides(flow, instance.Step) && !GoalRules.IsPerson(model, by))
        {
            refusals.Add($"Step {step}'s decision is a person's{(transition.Undelegable is string act ? $", an act the operating-modes design never delegates ({act})" : transition.PersonOnly ? ", as the flow requires" : $", since the flow marks the {instance.Step.Role} role a person's")}, and {Routing.NameOf(model, by)} is not a person.");
        }

        RecordEnvelope? assignment = instance.Rerun ? null : instance.Assignment;
        string? holder = assignment is null ? null : EstateModel.BodyString(assignment, "contributor");
        if (assignment is null)
        {
            refusals.Add($"No assignment holds step {step} for this round: vericairn goal step {current.Id} routes it to its decider in the {instance.Step.Role} role first.");
        }
        else if (holder != by)
        {
            refusals.Add($"Step {step} is assigned to {Routing.NameOf(model, holder)} (assignment {assignment.Id}), who decides it; {Routing.NameOf(model, by)} does not hold it.");
        }

        // A decision never turns off a step an estate policy requires or a required reserved step.
        IReadOnlyDictionary<string, string> kept = Protected(model, flow, FlowRuns.Product(model, current, flow));
        foreach (string off in WouldTurnOff(flow, instance.Step, paths, key => position.Steps.Where(s => s.Step.Key == key).Any(s => s.State != "done")).Where(kept.ContainsKey))
        {
            refusals.Add($"{ProtectedCode}: path {string.Join(", ", paths)} would turn off step {off}: {kept[off]}, and a decision never turns it off.");
        }

        RecordEnvelope? open = assignment is null ? null : WorkView.AttemptsOf(model, assignment).LastOrDefault(a => a.By == by && WorkView.ResultOf(model, a) is null);
        return new Plan(step, refusals.Count == 0 ? assignment : null, open, paths, reasons ?? string.Empty, pinned, refusals);
    }
}
