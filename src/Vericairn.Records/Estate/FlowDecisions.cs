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
/// hash; the position and validation evaluate its rules again only over the records it names, as of the time it names, so no later record
/// changes it. Each round takes one kind of decider, a rule's or a role's. The first decision of a round that takes effect is final: a
/// further one in the same round counts for nothing and validation reports it, and a new round starts only by the flow's own course, a
/// path back, a hold or what the step waits on done again. A step's decision is distinct from the estate's decision records, which stay the operator's,
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
    /// steps turned off. The flow's contract reads the same, so that no path a rule or a deadline may take turns off a required reserved step.
    /// </summary>
    public static IReadOnlyList<string> WouldTurnOff(FlowDefinition flow, FlowStep step, IReadOnlyList<string> paths, Func<string, bool>? notDone = null)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(paths);
        return FlowTransition.TurnedOff(flow.TransitionShapes, flow.BranchSteps, step.Key, paths, notDone);
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
    /// and a hold that took effect opens a new round; and every rule decision, and deadline's default, on record. Which of them counts in
    /// its round is the position's to judge: the first of the kind the round takes, a rule's or a role's, that holds there.
    /// </summary>
    internal static List<Recorded> Standing(EstateModel model, RecordEnvelope goal, FlowDefinition flow, FlowStep step, IReadOnlyList<Recorded> principals, IReadOnlyList<Recorded> rules)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(rules);
        var kept = new List<Recorded>();
        foreach (IGrouping<string, Recorded> held in principals.GroupBy(d => model.Chain(d.Assignment!)[0], StringComparer.Ordinal))
        {
            Recorded? open = Open(model, goal, flow, step, held.ToList(), null, kept);
            if (open is not null)
            {
                kept.Add(open);
            }
        }

        kept.AddRange(rules);
        return kept.OrderBy(d => d.Record.At, StringComparer.Ordinal).ThenBy(d => d.Record.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Walks one assignment's decisions, oldest first, as of a record when one is given: each hold that took effect is kept, and the decision
    /// still standing at the end is returned, the one a further decision recorded then would conflict with; null when none stands.
    /// </summary>
    private static Recorded? Open(EstateModel model, RecordEnvelope goal, FlowDefinition flow, FlowStep step, List<Recorded> decisions, RecordEnvelope? before, List<Recorded>? holds)
    {
        Recorded? current = null;
        foreach (Recorded decision in decisions)
        {
            if (current is not null)
            {
                if (Judge(model, goal, flow, step, current, decision.Record).SentBack is not null)
                {
                    current = null;
                }
                else if (IsHold(step, current) && TookEffect(model, goal, flow, step, current, decision.Record))
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

        if (current is not null && (Judge(model, goal, flow, step, current, before).SentBack is not null || (before is not null && IsHold(step, current) && TookEffect(model, goal, flow, step, current, before))))
        {
            return null;
        }

        return current;
    }

    /// <summary>True for a decision that takes the path back to the deciding step itself, a hold.</summary>
    private static bool IsHold(FlowStep step, Recorded decision) =>
        decision.Paths.Select(p => step.Transition?.Path(p)).Any(p => p?.Step == step.Key);

    /// <summary>
    /// True when a decision took effect, as of a record when one is given (from 0.9.4, PROC-19): a rule's, a person's, and an agent's that
    /// takes no review or whose latest admitted review of its exact hash passes. One definition with the position's: the review is admitted
    /// as the position admits it (<see cref="FlowPosition.DecisionReview"/>), by a principal the run names for the review's role, never by
    /// any reviewer but its decider.
    /// </summary>
    internal static bool TookEffect(EstateModel model, RecordEnvelope goal, FlowDefinition flow, FlowStep step, Recorded decision, RecordEnvelope? before = null) =>
        Judge(model, goal, flow, step, decision, before).TookEffect;

    /// <summary>
    /// How an agent's decision stands under its transition's review, as of a record when one is given: whether it took effect, the latest
    /// admitted review of its exact hash when it passes, and the latest when it found what to change or failed, which sent it back. A rule's
    /// and a person's decision take effect as recorded, and one whose transition takes no review.
    /// </summary>
    internal static (bool TookEffect, RecordEnvelope? Passed, RecordEnvelope? SentBack) Judge(EstateModel model, RecordEnvelope goal, FlowDefinition flow, FlowStep step, Recorded decision, RecordEnvelope? before)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Ruled || GoalRules.IsPerson(model, decision.By) || step.Transition?.Review is not { Independence: not "none" } required)
        {
            return (true, null, null);
        }

        RecordEnvelope? latest = FlowPosition.DecisionReview(model, goal, flow, required, decision.Record, decision.By, ReviewAssignments(model, goal, step), before);
        string? outcome = latest is null ? null : EstateModel.BodyString(latest, "outcome");
        return (outcome == "pass", outcome == "pass" ? latest : null, outcome is "findings" or "fail" ? latest : null);
    }

    /// <summary>The assignments of a run's step's review before hand-off, which name the principals who review its decision.</summary>
    private static List<RecordEnvelope> ReviewAssignments(EstateModel model, RecordEnvelope goal, FlowStep step) =>
        FlowRuns.Work(model, model.Latest(goal)) is RecordEnvelope work
            ? Routing.Assignments(model, work).Where(a => FlowRuns.StepOf(a) is { Review: true } named && named.Key == step.Key && named.Instance is null).ToList()
            : [];

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
        if (Open(context.Model, run, flow, step, earlier, context.Record, null) is Recorded standing)
        {
            context.Error("result.decision", $"Step {step.Key}'s decision in this round is result {standing.Record.Id}, path {string.Join(", ", standing.Paths)}, which stands and is final: a further decision counts for nothing until the run's own course opens a new round, a path back, a hold or what the step waits on done again.");
        }

        // One decider kind a round: where a rule matched when the round began, the round is the rule's, and no principal decides it. An
        // assignment made after a round began and before its rule's decision was recorded is of that round, whatever the times the
        // decision and the rule's state.
        if (decider == "rule-then-role")
        {
            RecordEnvelope root = context.Model.ById.GetValueOrDefault(context.Model.Chain(assignment!)[0]) ?? assignment!;
            foreach (Recorded ruled in RuleDecisions(context.Model, run, step.Key).Where(r => r.Rule is not ("default" or "deadline") && r.Record.Id != context.Record.Id))
            {
                if (AsOf(ruled.Record) is DateTimeOffset began && began <= root.AtTime && root.AtTime <= ruled.At && Refusals(context.Model, run, flow, step, decider, ruled).Count == 0)
                {
                    context.Error("result.decision", $"Step {step.Key}'s round from {RecordParser.FormatTimestamp(began)} is its rule's: rule {ruled.Rule} held as the records stood then, and observation {ruled.Record.Id} records its path {string.Join(", ", ruled.Paths)}. Assignment {root.Id} was made in that round, and a round its rule decides takes no principal's decision, whenever it is dated.");
                }
            }
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
    /// goal is a run and each record it read exists at its hash, and every refusal the position also reads (<see cref="Refusals"/>): its
    /// author takes the run's steps, its kind is one its step's transition takes in the run's configuration, its path is the one its rule
    /// names, and its rules, evaluated again as of its time over the records it names, hold as it says. Validation also reports a record its
    /// rules read that it leaves out, a second rule decision for the same round, and a deadline's default where a decision stood by the
    /// deadline it names. Whether a deadline had passed, and when a round began, the run's position derives; it ignores a decision that does
    /// not hold there.
    /// </summary>
    internal static void ValidateObservation(ValidationContext context, BodyReader? facts, string? asOf)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (facts is null)
        {
            return;
        }

        string? key = facts.Text("step", true, 100, Patterns.Slug, "a step key");
        facts.StringArray("paths", true, allowEmpty: false, maxItemLength: 60, itemPattern: Patterns.Slug, patternHint: "a path name");
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

        if (FlowRuns.Resolve(context.Model, goal).Flow is not FlowDefinition flow || key is null || rule is null || Of(context.Record) is not Recorded decision)
        {
            if (!Routing.MayTakeStep(context.Model, goal, context.Record.By, context.Record.AtTime, context.Record))
            {
                context.Error("observation.flow-decision", NotTaker(context.Model, goal, context.Record.By));
            }

            return;
        }

        if (flow.Step(key) is not { Transition: FlowTransition } step)
        {
            context.Error("observation.flow-decision", $"Flow {flow.Pin.Id} has no decision step '{key}'.");
            return;
        }

        string decider = FlowRuns.Configure(context.Model, goal, flow, context.Record).Decider(step);
        foreach (string refusal in Refusals(context.Model, goal, flow, step, decider, decision))
        {
            context.Error("observation.flow-decision", refusal);
        }

        // A rule's decision, or a deadline's default, never turns off a step an estate policy requires: the step stays on, and validation
        // reports the decision. A required reserved step no path a rule or a deadline may take turns off, which the flow's contract holds.
        IReadOnlyDictionary<string, string> kept = Protected(context.Model, flow, FlowRuns.Product(context.Model, goal, flow), context.Record);
        foreach (string off in WouldTurnOff(flow, step, decision.Paths).Where(kept.ContainsKey))
        {
            context.Error("observation.flow-decision-protected", $"Step {key}'s {(rule == "deadline" ? "deadline's default" : "rule's decision")} takes {string.Join(", ", decision.Paths)}, which would turn off step {off}: {kept[off]}, and a decision never turns it off; it stays on.");
        }

        if (asOf is null || !RecordParser.IsTimestamp(asOf))
        {
            return;
        }

        // Its rules read every record they count as of then: one left out could make a rule hold, or keep an earlier one from holding.
        foreach (RecordEnvelope omitted in Omitted(context.Model, flow, step, decision))
        {
            facts.Error("observation.flow-decision", "read", $"Its rules count {omitted.Type} {omitted.Id}, recorded at {omitted.At}, among the records as they stood at {asOf}, and it does not name it among the records it read.");
        }

        // A round's rule decision is final: a later one is for a round that began after the earlier decision was recorded.
        DateTimeOffset began = Parse(asOf);
        IReadOnlyList<string> chain = context.Model.Chain(context.Model.Latest(goal));
        if (rule != "deadline" && context.Model.Current("observation").FirstOrDefault(o => o.Id != context.Record.Id && GoalRules.Before(o, context.Record) && EstateModel.BodyString(o, "kind") == ObservationKind
            && o.Subject is not null && chain.Contains(o.Subject.Id) && TJson.Text(TJson.Object(o.Body, "facts"), "step") == key && o.AtTime >= began
            && TJson.Text(TJson.Object(o.Body, "facts"), "rule") != "deadline") is RecordEnvelope earlier)
        {
            context.Error("observation.flow-decision", $"Step {key}'s rule decision for the round from {asOf} is already recorded in observation {earlier.Id}, and it is final.");
        }

        // A deadline takes its default only when no decision stands by it: a principal's decision of the round, under the step's current
        // assignment and recorded by the deadline this observation names, is the round's, and no default replaces it.
        if (rule == "deadline" && FlowRuns.Work(context.Model, context.Model.Latest(goal)) is RecordEnvelope order)
        {
            FlowRuns.StepRef named = new(step.Key, null);
            List<RecordEnvelope> assignments = Routing.Assignments(context.Model, order, named).Where(a => GoalRules.Before(a, context.Record)).ToList();
            RecordEnvelope? holding = assignments.OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();
            List<Recorded> held = holding is null ? [] : PrincipalDecisions(context.Model, flow, step, assignments)
                .Where(d => context.Model.Chain(context.Model.Latest(d.Assignment!)).Contains(holding.Id) && d.At <= began).ToList();
            if (holding is not null && Open(context.Model, goal, flow, step, held, context.Record, null) is Recorded stood && !IsHold(step, stood)
                && !stood.Paths.Select(step.Transition!.Path).OfType<FlowPath>().Any(p => FlowTransition.IsBack(p, flow.IndexOf, step.Key)))
            {
                context.Error("observation.flow-decision", $"Step {key}'s decision by {Routing.NameOf(context.Model, stood.By)} in result {stood.Record.Id}, path {string.Join(", ", stood.Paths)}, stood by {asOf}, the deadline this observation names: a deadline takes its default only when no decision stands by it, and no default replaces the decision of the step's role.");
            }
        }
    }

    /// <summary>Why a principal does not record a run's rule decision: it does not take the run's steps.</summary>
    private static string NotTaker(EstateModel model, RecordEnvelope goal, string by) =>
        $"{Routing.NameOf(model, by)} does not take steps of goal {goal.Id}, so does not record its rule's decision: an operator, the goal's sponsor when a person, or a principal holding local-engineering does.";

    /// <summary>
    /// Why a flow-decision observation never counts, whatever the run's position (from 0.9.4, PROC-19), as the records alone show; the
    /// position reads the same, and validation reports each. Its author takes the run's steps. Its kind is one its step's transition takes
    /// in the run's configuration: a rule's number under a rule or a rule then the role; a rule's default under a rule alone, since under a
    /// rule then the role the role decides when no rule holds; a deadline's default only where a role decides, the transition takes its
    /// default at a deadline, the run's timing for the step sets a deadline, and no person alone decides, since nothing records a person's
    /// decision but the person. Its path is the one its rule names. It names no later time than it was recorded. And its rules, evaluated
    /// again as of that time over only the records it names at their hashes, with the as-of evaluation the position uses, hold as it says:
    /// its rule holds and no earlier rule does, since the first that holds wins, or for its default, none holds. A predicate over a derived
    /// state reads that state as it is now. Empty when it may count.
    /// </summary>
    internal static List<string> Refusals(EstateModel model, RecordEnvelope goal, FlowDefinition flow, FlowStep step, string decider, Recorded decision)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(decision);
        var refusals = new List<string>();
        if (!Routing.MayTakeStep(model, goal, decision.By, decision.At, decision.Record))
        {
            refusals.Add(NotTaker(model, goal, decision.By));
        }

        if (step.Transition is not FlowTransition transition || decision.Rule is not string rule)
        {
            refusals.Add($"Step {step.Key} declares no decision transition.");
            return refusals;
        }

        bool person = PersonDecides(flow, step);
        string? expected = rule switch
        {
            "default" => decider == "rule" ? transition.Default : null,
            "deadline" => decider != "rule" && transition.Deadline == "default" && !person ? transition.Default : null,
            _ => decider != "role" && int.TryParse(rule, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number >= 1 && number <= transition.Rules.Count ? transition.Rules[number - 1].Path : null,
        };
        if (expected is null)
        {
            refusals.Add(rule switch
            {
                "deadline" when person && decider != "rule" => $"Step {step.Key}'s decision is a person's{(transition.Undelegable is string act ? $", an act the operating-modes design never delegates ({act})" : transition.PersonOnly ? ", as the flow requires" : $", since the flow marks the {step.Role} role a person's")}: a deadline never takes its default, it only raises attention, and the person still decides.",
                "deadline" => $"Step {step.Key}'s transition takes no default at a deadline in this run, or a rule decides it.",
                "default" when decider == "rule-then-role" => $"Step {step.Key}'s transition is decided by a rule then its role in this run: when no rule holds the role decides, and no rule's default is taken.",
                "default" => $"Step {step.Key}'s transition is not decided by a rule alone in this run, so it takes no rule's default.",
                _ => $"Step {step.Key}'s transition is not decided by a rule in this run, or has no rule {rule}.",
            });
        }
        else if (decision.Paths.Count != 1 || decision.Paths[0] != expected)
        {
            refusals.Add($"Step {step.Key}'s {(rule is "default" or "deadline" ? "default" : "rule " + rule)} takes path {expected}, not {string.Join(", ", decision.Paths)}.");
        }

        if (rule == "deadline" && expected is not null && FlowRuns.Configure(model, goal, flow, decision.Record).Timing(step).Deadline is null)
        {
            refusals.Add($"Step {step.Key}'s timing in this run sets no deadline, so no deadline takes its default.");
        }

        DateTimeOffset? asOf = AsOf(decision.Record);
        if (asOf is null)
        {
            refusals.Add("A flow decision names the time it read the records as of, its asOf.");
        }
        else if (asOf > decision.At)
        {
            refusals.Add(rule == "deadline"
                ? "A deadline's default is recorded once its deadline passed: its asOf, the deadline, is no later than it is recorded."
                : "A rule's decision reads the records as they stood when its round began, which is no later than it is recorded.");
        }

        if (expected is not null && Unsound(model, flow, step, decision) is string unsound)
        {
            refusals.Add($"Step {step.Key}'s {(rule == "default" ? "default" : "rule " + rule)} does not hold when its rules are evaluated again: {unsound}.");
        }

        return refusals;
    }

    /// <summary>The time a flow-decision observation read the records as of, its asOf; null when it names none.</summary>
    internal static DateTimeOffset? AsOf(RecordEnvelope record) =>
        EstateModel.BodyString(record, "asOf") is string stated && RecordParser.IsTimestamp(stated) ? Parse(stated) : null;

    private static DateTimeOffset Parse(string timestamp) => DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    /// <summary>
    /// Why a recorded rule decision does not hold when its rules are evaluated again (from 0.9.4, PROC-19): as of the time it names, over
    /// only the records it names at their hashes, its rule must hold and no earlier rule may, or for its default no rule may. A rule naming
    /// an input an earlier step's output carries is not evaluated again: only the run's position binds it. Null when it holds.
    /// </summary>
    private static string? Unsound(EstateModel model, FlowDefinition flow, FlowStep step, Recorded decision)
    {
        if (Evaluated(model, flow, step, decision) is not { } evaluated)
        {
            return null;
        }

        int named = decision.Rule == "default" ? 0 : int.Parse(decision.Rule!, NumberStyles.None, CultureInfo.InvariantCulture);
        List<RecordEnvelope> within = decision.Evidence.Where(r => r.Hash is not null && model.ById.TryGetValue(r.Id, out RecordEnvelope? found) && found.Type == r.Type && found.ComputedHash == r.Hash)
            .Select(r => model.ById[r.Id]).ToList();
        string stated = RecordParser.FormatTimestamp(evaluated.AsOf);
        foreach ((int number, List<JsonElement> predicates) in evaluated.Rules)
        {
            List<GoalPredicates.Verdict> verdicts = predicates.Select(p => GoalPredicates.EvaluatePredicate(model, p, evaluated.AsOf, within)).ToList();
            bool holds = verdicts.All(v => v.Value == "met");
            string which = number.ToString(CultureInfo.InvariantCulture);
            if (number != named && holds)
            {
                return $"rule {which} holds over the records it names as they stood at {stated}, and the first rule that holds decides{(named == 0 ? ", so no default is taken" : string.Empty)}";
            }

            if (number == named && !holds)
            {
                GoalPredicates.Verdict first = verdicts.First(v => v.Value != "met");
                return $"rule {which} is {first.Value} over the records it names as they stood at {stated}: {first.Reason}";
            }
        }

        return null;
    }

    /// <summary>
    /// The records a recorded rule decision's rules count among as of its time that it does not name (from 0.9.4, PROC-19): current, made
    /// by then and recorded before it, each a record one of its rules up to the one it names reads.
    /// </summary>
    private static List<RecordEnvelope> Omitted(EstateModel model, FlowDefinition flow, FlowStep step, Recorded decision)
    {
        if (Evaluated(model, flow, step, decision) is not { } evaluated)
        {
            return [];
        }

        var named = decision.Evidence.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        return evaluated.Rules.SelectMany(r => r.Predicates).SelectMany(p => GoalPredicates.Candidates(model, p, evaluated.AsOf))
            .Where(r => !named.Contains(r.Id) && GoalRules.Before(r, decision.Record)).DistinctBy(r => r.Id)
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The rules a recorded rule decision is evaluated again by, each numbered from 1 with its predicates bound to the run's revision it is
    /// about: up to the rule it names, or every rule for its default, without a rule that names a binding only the run's position gives;
    /// and the time it read the records as of. Null for a deadline's default or a decision that names no rule of its step.
    /// </summary>
    private static (DateTimeOffset AsOf, List<(int Number, List<JsonElement> Predicates)> Rules)? Evaluated(EstateModel model, FlowDefinition flow, FlowStep step, Recorded decision)
    {
        if (step.Transition is not FlowTransition transition || decision.Rule is not string rule || rule == "deadline" || AsOf(decision.Record) is not DateTimeOffset asOf
            || decision.Record.Subject is null || !model.ById.TryGetValue(decision.Record.Subject.Id, out RecordEnvelope? goal))
        {
            return null;
        }

        int last = rule == "default" ? transition.Rules.Count : int.TryParse(rule, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number <= transition.Rules.Count ? number : 0;
        if (last == 0)
        {
            return null;
        }

        var bindings = new Dictionary<string, string>(FlowRuns.Bindings(model, goal, flow, null), StringComparer.Ordinal);
        JsonElement subject = TJson.Object(FlowRuns.Plan(goal), "subject");
        foreach (FlowInput input in step.Inputs.Where(i => i.From == "subject" && i.Field is not null))
        {
            if (TJson.Text(TJson.Object(subject, input.Field!), "id") is string id)
            {
                bindings["input." + input.Name + ".id"] = id;
            }
        }

        var rules = new List<(int, List<JsonElement>)>();
        for (int index = 0; index < last; index++)
        {
            List<JsonElement> bound = transition.Rules[index].When.Select(p => FlowRuns.Bind(p, bindings)).ToList();
            if (bound.All(b => !b.GetRawText().Contains("{unbound ", StringComparison.Ordinal)))
            {
                rules.Add((index + 1, bound));
            }
        }

        return (asOf, rules);
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
                ["read"] = new JsonArray(decision.Evidence.Where(e => e.Hash is not null).Select(e => (JsonNode)ReadText(e)).ToArray()),
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
