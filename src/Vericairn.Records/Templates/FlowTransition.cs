// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Types;

namespace Vericairn.Records.Templates;

/// <summary>
/// One named path of a decision transition (the estate's design/flow.md § 5, decision 01M75YYK958AFMS24W71BHRH26; from 0.9.4, PROC-19):
/// where taking it leads, exactly one of a step, a branch, another flow or the end of the run, and for a path back to an earlier step,
/// or to the deciding step itself, the most times the run takes it before attention is raised.
/// </summary>
/// <param name="Name">The path's name within its step.</param>
/// <param name="Step">The step it leads to: a later step that waits on the deciding step, an earlier work or review step the deciding step waits on, or the deciding step itself; null when it leads elsewhere.</param>
/// <param name="Branch">The branch it enters; null when it leads elsewhere.</param>
/// <param name="Flow">The flow whose run it hands the subject to, ending this run at the deciding step; null when it leads elsewhere.</param>
/// <param name="End">True for a path that ends the run at the deciding step.</param>
/// <param name="Limit">For a path back, the most times the run takes it before attention is raised; null for a forward path.</param>
public sealed record FlowPath(string Name, string? Step, string? Branch, TemplatePin? Flow, bool End, int? Limit)
{
    /// <summary>Where the path leads, in words.</summary>
    public string Leads => Step is not null ? $"step {Step}" : Branch is not null ? $"branch {Branch}" : Flow is not null ? $"a run of flow {Flow.Id} {Flow.Version}" : "the end of the run";

    /// <summary>True for a path that ends this run at the deciding step: the end, or another flow's run.</summary>
    public bool Ends => End || Flow is not null;
}

/// <summary>One rule of a decision transition decided by a rule: the path it takes when every one of its record predicates holds.</summary>
/// <param name="Path">The path it takes.</param>
/// <param name="When">Its record predicates, the completion language of design/goals.md § 5, bound to the run.</param>
public sealed record FlowRule(string Path, IReadOnlyList<JsonElement> When);

/// <summary>
/// A step's decision transition (the estate's design/flow.md § 5, decision 01M75YYK958AFMS24W71BHRH26; from 0.9.4, PROC-19): two or more
/// named paths, each leading to a step, a branch, another flow or the end; one path taken, or several at once with an optional join where
/// the flow allows; and its decider, declared by the flow and changed by a venture only among the deciders the flow allows: a rule (the
/// existing record predicates, the first that holds winning, a default path required), the step's role, whose principal, an agent or a
/// person, decides and records the path with its reasons and evidence by hash, or a rule first and the role when no rule holds. The phase
/// gate is one predefined set of paths: go, recycle, hold and kill. Choosing a path performs nothing reserved: a path only makes the steps
/// it leads to eligible. A flow may require a person, and a decision the operating-modes design never delegates is a person's in every
/// configuration. An agent's decision may require review before it takes effect; a deadline under the step's timing takes the default
/// path or raises attention; and a path back carries a maximum count, past which attention is raised. A transition is content and grants
/// nothing.
/// </summary>
/// <param name="Paths">The named paths, in their declared order.</param>
/// <param name="Default">The path a rule takes when no rule holds and a deadline takes under its default rule; null when none is declared.</param>
/// <param name="Several">True when one decision may take several forward paths at once.</param>
/// <param name="Join">The step where several paths taken at once meet, when the flow names one.</param>
/// <param name="Decider">The decider the flow declares: rule, role or rule-then-role.</param>
/// <param name="Deciders">The deciders a venture may choose, the declared one first; only it when the flow allows no other.</param>
/// <param name="Rules">The rules, in order, the first that holds winning.</param>
/// <param name="Person">True when the flow requires a person to decide.</param>
/// <param name="Undelegable">The act the operating-modes design never delegates that the decision is, which makes it a person's; null when it is none.</param>
/// <param name="Review">The review an agent's decision takes before it takes effect; null when it takes none.</param>
/// <param name="Deadline">What a deadline under the step's timing does when it passes before a decision: default (takes the default path) or attention.</param>
/// <param name="Gate">True for the phase gate's predefined paths: go, recycle, hold and kill.</param>
public sealed record FlowTransition(IReadOnlyList<FlowPath> Paths, string? Default, bool Several, string? Join, string Decider, IReadOnlyList<string> Deciders,
    IReadOnlyList<FlowRule> Rules, bool Person, string? Undelegable, FlowReview? Review, string Deadline, bool Gate)
{
    /// <summary>The deciders a transition names: a rule, the step's role, or a rule first and the role when no rule holds.</summary>
    public static readonly ImmutableArray<string> DeciderKinds = ["rule", "role", "rule-then-role"];

    /// <summary>What a deadline does when it passes before a decision: take the default path, or raise attention (the default).</summary>
    public static readonly ImmutableArray<string> DeadlineRules = ["default", "attention"];

    /// <summary>
    /// The acts the operating-modes design never delegates (design/operating-modes.md § 6) that a decision may be: administering principals
    /// and devices, changing the charter or the operating mode, issuing or widening a grant, a legal commitment, a transfer of ownership,
    /// and a destructive operation on the estate's own records or repositories. Such a decision is a person's in every configuration.
    /// </summary>
    public static readonly ImmutableArray<string> Undelegables = ["administration", "charter", "grant", "legal-commitment", "ownership-transfer", "destructive"];

    /// <summary>The phase gate's predefined paths: go on, recycle to an earlier step, hold at the gate to decide again, and kill the run.</summary>
    public static readonly ImmutableArray<string> GatePaths = ["go", "recycle", "hold", "kill"];

    /// <summary>The prefix of the option by which a venture chooses a step's decider: decider.&lt;key&gt;.</summary>
    public const string DeciderOption = "decider.";

    /// <summary>The most times a phase gate's recycle and hold are taken when the gate names no limit.</summary>
    public const int GateLimit = 3;

    /// <summary>The most times any path back may be taken before attention is raised.</summary>
    public const int MaximumLimit = 100;

    /// <summary>True when a venture's decider may be the step's role: its principal then decides and records the path.</summary>
    public bool MayBeRole => Deciders.Any(d => d != "rule");

    /// <summary>True when only a person decides: the flow requires one, or the decision is an act never delegated.</summary>
    public bool PersonOnly => Person || Undelegable is not null;

    /// <summary>The path with a name, or null.</summary>
    public FlowPath? Path(string? name) => Paths.FirstOrDefault(p => p.Name == name);

    /// <summary>True for a path back: to the deciding step itself or to a step before it in the flow's order.</summary>
    public static bool IsBack(FlowPath path, Func<string, int> index, string self)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(index);
        return path.Step is string target && (target == self || index(target) < index(self));
    }

    /// <summary>A step's transition as declared, its phase gate expanded into its four paths; null when it declares none.</summary>
    public static FlowTransition? Read(JsonElement transition, string step)
    {
        if (transition.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        List<FlowPath> paths;
        bool gate = TJson.Object(transition, "gate") is { ValueKind: JsonValueKind.Object };
        if (gate)
        {
            JsonElement declared = TJson.Object(transition, "gate");
            int limit = Number(declared, "limit") ?? GateLimit;
            paths =
            [
                new FlowPath("go", TJson.Text(declared, "go"), null, null, false, null),
                new FlowPath("recycle", TJson.Text(declared, "recycle"), null, null, false, limit),
                new FlowPath("hold", step, null, null, false, limit),
                new FlowPath("kill", null, null, null, true, null),
            ];
        }
        else
        {
            paths = TJson.Array(transition, "paths").Select(p => new FlowPath(TJson.Text(p, "name")!, TJson.Text(p, "step"), TJson.Text(p, "branch"),
                TJson.Object(p, "flow") is { ValueKind: JsonValueKind.Object } flow ? new TemplatePin(TJson.Text(flow, "id")!, TJson.Text(flow, "version")!, TJson.Text(flow, "hash")!) : null,
                TJson.Flag(p, "end"), Number(p, "limit"))).ToList();
        }

        string decider = TJson.Text(transition, "decider") ?? "role";
        List<string> deciders = [decider, .. TJson.Strings(transition, "configurable").Where(d => d != decider).Distinct(StringComparer.Ordinal)];
        return new FlowTransition(paths, TJson.Text(transition, "default"), TJson.Flag(transition, "several"), TJson.Text(transition, "join"), decider, deciders,
            TJson.Array(transition, "rules").Select(r => new FlowRule(TJson.Text(r, "path")!, TJson.Array(r, "when").ToList())).ToList(),
            TJson.Flag(transition, "person"), TJson.Text(transition, "undelegable"), ReviewOf(TJson.Object(transition, "review")),
            TJson.Text(transition, "deadline") ?? "attention", gate);
    }

    /// <summary>
    /// What one step's transition must hold within its own content: its paths, each named once and leading to exactly one of a step, a
    /// branch, another flow by its exact pin or the end, a limit only on a path to a step; or the phase gate's go and recycle; a decider and
    /// the deciders a venture may choose; the rules and the default a rule needs, each naming a declared path and never one back to the
    /// step itself; a role's decision only on a decision step, never on a reserved one or under a rule alone where a person is required;
    /// several paths only with a join that is one of them or after them; and the review and deadline rule. How its paths relate to the
    /// flow's other steps is checked once every step is read (<see cref="Structure"/>).
    /// </summary>
    internal static void Check(BodyReader transition, string? key, string? kind, string? reserved, string? each, bool required, Action<BodyReader> predicate, Func<string, bool> roles)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(roles);
        var names = new List<string>();
        BodyReader? gate = transition.Nested("gate", false);
        IReadOnlyList<BodyReader>? declared = transition.NestedArray("paths", gate is null, allowEmpty: false);
        if (gate is not null)
        {
            if (declared is not null)
            {
                transition.Error("flow.transition", "gate", "A transition declares its own paths or the phase gate's, not both.");
            }

            if (kind is not null && kind != "decision")
            {
                transition.Error("flow.transition", "gate", "The phase gate's paths are a decision step's: go, recycle, hold and kill.");
            }

            gate.Text("go", true, 100, Patterns.Slug, "a step key");
            gate.Text("recycle", true, 100, Patterns.Slug, "a step key");
            gate.Number("limit", false, 1, MaximumLimit);
            gate.NoOtherKeys();
            names.AddRange(GatePaths);
        }

        var backward = new List<string>();
        foreach (BodyReader path in declared ?? [])
        {
            string? name = path.Text("name", true, 60, Patterns.Slug, "a path name");
            string? step = path.Text("step", false, 100, Patterns.Slug, "a step key");
            string? branch = path.Text("branch", false, 100, Patterns.Slug, "a branch name");
            BodyReader? flow = path.Nested("flow", false);
            if (flow is not null)
            {
                TemplateRecords.ReadPin(flow);
                flow.NoOtherKeys();
            }

            bool? end = path.Flag("end", false);
            long? limit = path.Number("limit", false, 1, MaximumLimit);
            path.NoOtherKeys();
            if (new[] { step is not null, branch is not null, flow is not null, end == true }.Count(b => b) != 1 || end == false)
            {
                path.Error("flow.transition", "name", "A path leads to exactly one of a step, a branch, another flow by its exact pin, or the end (end: true).");
            }

            if (limit is not null && step is null)
            {
                path.Error("flow.transition", "limit", "Only a path to a step carries a limit: the most times the run goes back to it.");
            }

            if (step is not null && step == key)
            {
                backward.Add(name ?? step);
            }

            if (name is not null)
            {
                if (names.Contains(name, StringComparer.Ordinal))
                {
                    path.Error("flow.duplicate", "name", "Each path of a transition is named once.");
                }

                names.Add(name);
            }
        }

        if (declared is not null && declared.Count < 2)
        {
            transition.Error("flow.transition", "paths", "A decision transition declares two or more named paths.");
        }

        string? decider = transition.Enum("decider", true, DeciderKinds);
        IReadOnlyList<string>? configurable = transition.StringArray("configurable", false, allowEmpty: false, maxItemLength: 40);
        foreach (string choice in configurable ?? [])
        {
            if (!DeciderKinds.Contains(choice, StringComparer.Ordinal))
            {
                transition.Error("body.enum", "configurable", $"A venture chooses among the deciders {string.Join(", ", DeciderKinds)}; '{choice}' is none of them.");
            }
        }

        List<string> options = decider is null ? [] : [decider, .. (configurable ?? []).Where(DeciderKinds.Contains)];
        bool? person = transition.Flag("person", false);
        string? undelegable = transition.Enum("undelegable", false, Undelegables);
        bool personOnly = person == true || undelegable is not null;
        if (personOnly && options.Any(o => o != "role"))
        {
            transition.Error("flow.transition", "decider", "A decision the flow requires of a person, or one the operating-modes design never delegates, is decided by a person in the step's role, never by a rule: its decider is role, and no venture chooses another.");
        }

        if (options.Any(o => o != "rule") && kind is not null && kind != "decision")
        {
            transition.Error("flow.transition", "decider", $"A role decides a decision step's transition; a {kind} step's transition is decided by a rule, read once the step is done.");
        }

        if (options.Any(o => o != "rule") && reserved is not null)
        {
            transition.Error("flow.transition", "decider", "A reserved step's act is the operator's, and a role's choice never performs it: its transition is decided by a rule, read once the act is recorded.");
        }

        if (each is not null)
        {
            transition.Error("flow.transition", "transition", "A decision transition is declared on a step that runs once; a step that runs over a list does not decide its run's path.");
        }

        if (!required)
        {
            transition.Error("flow.transition", "transition", "A step that decides its run's path is required: turned off, its paths' steps would run unchosen.");
        }

        if (person == false)
        {
            transition.Error("flow.transition", "person", "person is true when the flow requires a person to decide; leave it out otherwise.");
        }

        string? fallback = transition.Text("default", false, 60, Patterns.Slug, "a path name");
        if (fallback is not null && !names.Contains(fallback, StringComparer.Ordinal))
        {
            transition.Error("flow.transition", "default", $"The default path '{fallback}' is not one of the transition's paths.");
        }
        else if (fallback is not null && (backward.Contains(fallback, StringComparer.Ordinal) || (gate is not null && fallback == "hold")))
        {
            transition.Error("flow.transition", "default", "The default path is never the one back to the deciding step itself: a rule or a deadline that took it would decide the same way again.");
        }

        IReadOnlyList<BodyReader> rules = transition.NestedArray("rules", false, allowEmpty: false) ?? [];
        foreach (BodyReader rule in rules)
        {
            string? path = rule.Text("path", true, 60, Patterns.Slug, "a path name");
            foreach (BodyReader when in rule.NestedArray("when", true, allowEmpty: false) ?? [])
            {
                predicate(when);
            }

            rule.NoOtherKeys();
            if (path is not null && !names.Contains(path, StringComparer.Ordinal))
            {
                rule.Error("flow.transition", "path", $"The rule takes path '{path}', which is not one of the transition's paths.");
            }
            else if (path is not null && (backward.Contains(path, StringComparer.Ordinal) || (gate is not null && path == "hold")))
            {
                rule.Error("flow.transition", "path", "A rule never takes the path back to the deciding step itself: it would decide the same way again.");
            }
        }

        if (options.Any(o => o != "role"))
        {
            if (rules.Count == 0)
            {
                transition.Error("flow.transition", "rules", "A rule decides by its rules: a transition a rule may decide declares at least one, the first that holds winning.");
            }

            if (fallback is null)
            {
                transition.Error("flow.transition", "default", "A transition a rule may decide declares its default path, taken when no rule holds.");
            }
        }
        else if (rules.Count > 0)
        {
            transition.Error("flow.transition", "rules", "Only a transition a rule may decide declares rules.");
        }

        bool? several = transition.Flag("several", false);
        string? join = transition.Text("join", false, 100, Patterns.Slug, "a step key");
        if (join is not null && several != true)
        {
            transition.Error("flow.transition", "join", "A join is where several paths taken at once meet: it goes with several: true.");
        }

        if (transition.Has("review"))
        {
            JsonElement review = TJson.Object(transition.Element, "review");
            if (review.ValueKind == JsonValueKind.String)
            {
                transition.Enum("review", false, FlowContract.Reviews);
            }
            else if (transition.Nested("review", false) is BodyReader reviewer)
            {
                reviewer.Enum("independence", true, FlowContract.Reviews);
                string? role = reviewer.Text("role", false, 60, Patterns.Slug, "a role of the flow");
                reviewer.NoOtherKeys();
                if (role is not null && !roles(role))
                {
                    reviewer.Error("flow.role", "role", $"'{role}' is not one of the flow's roles.");
                }
            }

            if (!options.Any(o => o != "rule"))
            {
                transition.Error("flow.transition", "review", "Only an agent's decision takes a review before it takes effect, and a rule decides this transition.");
            }
        }

        string? deadline = transition.Enum("deadline", false, DeadlineRules);
        if (deadline == "default" && fallback is null)
        {
            transition.Error("flow.transition", "deadline", "A deadline that takes the default path needs the transition's default.");
        }

        transition.NoOtherKeys();
    }

    /// <summary>One step as the transitions' structure reads it: its key and kind, what it waits on by after and by its inputs and timing, and its transition.</summary>
    internal sealed record StepShape(string Key, string? Kind, IReadOnlyList<string> After, IReadOnlyList<string> Also, string? Each, FlowTransition? Transition);

    /// <summary>
    /// How every transition's paths relate to the flow's steps and branches: a path forward leads to a later step that waits on the
    /// deciding step by after, and every step that waits on it so is one of its paths' steps or its join; a path back leads to the deciding
    /// step itself, under a role, or to an earlier work or review step the deciding step waits on, running once, with a limit; a branch
    /// path enters a declared branch whose steps come after the deciding step; and a join comes after every step of the paths it joins.
    /// Each problem is returned with the step it belongs to.
    /// </summary>
    internal static List<(string Step, string Message)> Structure(IReadOnlyList<StepShape> steps, IReadOnlyDictionary<string, IReadOnlyList<string>> branches)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(branches);
        var found = new List<(string Step, string Message)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < steps.Count; i++)
        {
            index.TryAdd(steps[i].Key, i);
        }

        foreach (StepShape step in steps.Where(s => s.Transition is not null))
        {
            FlowTransition transition = step.Transition!;
            int at = index[step.Key];
            if (branches.FirstOrDefault(b => b.Value.Contains(step.Key, StringComparer.Ordinal)) is { Key: string within })
            {
                found.Add((step.Key, $"Step '{step.Key}' belongs to branch '{within}', which is off until it is entered: a branch's step does not decide its run's path."));
            }

            var forward = new List<string>();
            foreach (FlowPath path in transition.Paths)
            {
                if (path.Step is string target)
                {
                    if (!index.TryGetValue(target, out int to))
                    {
                        found.Add((step.Key, $"Path {path.Name} of step '{step.Key}' leads to '{target}', which is not a step of the flow."));
                        continue;
                    }

                    StepShape led = steps[to];
                    if (to > at)
                    {
                        forward.Add(target);
                        if (!led.After.Contains(step.Key, StringComparer.Ordinal))
                        {
                            found.Add((step.Key, $"Path {path.Name} of step '{step.Key}' leads forward to '{target}', which does not wait on '{step.Key}': a path's step names its deciding step in after."));
                        }

                        if (path.Limit is not null)
                        {
                            found.Add((step.Key, $"Path {path.Name} of step '{step.Key}' leads forward to '{target}' and carries a limit, which only a path back carries."));
                        }
                    }
                    else if (target == step.Key)
                    {
                        if (!transition.MayBeRole)
                        {
                            found.Add((step.Key, $"Path {path.Name} of step '{step.Key}' leads back to the step itself, which only a role decides again; a rule would decide the same way."));
                        }
                    }
                    else
                    {
                        if (led.Kind is not ("work" or "review"))
                        {
                            found.Add((step.Key, $"Path {path.Name} of step '{step.Key}' leads back to '{target}', a {led.Kind} step: a path back returns to a work or review step, carried again from a fresh assignment."));
                        }

                        if (led.Each is not null)
                        {
                            found.Add((step.Key, $"Path {path.Name} of step '{step.Key}' leads back to '{target}', which runs over a list; a path back returns to a step that runs once."));
                        }

                        if (!Waits(steps, index, step.Key, target))
                        {
                            found.Add((step.Key, $"Path {path.Name} of step '{step.Key}' leads back to '{target}', which '{step.Key}' does not wait on: a path back returns to a step the deciding step waits on, so that it decides again once that step is done again."));
                        }
                    }

                    if (to <= at && path.Limit is null)
                    {
                        found.Add((step.Key, $"Path {path.Name} of step '{step.Key}' leads back to '{target}' and carries no limit: a path back names the most times the run takes it, past which attention is raised."));
                    }
                }
                else if (path.Branch is string branch)
                {
                    if (!branches.TryGetValue(branch, out IReadOnlyList<string>? members))
                    {
                        found.Add((step.Key, $"Path {path.Name} of step '{step.Key}' enters branch '{branch}', which the flow does not declare."));
                    }
                    else if (members.Any(m => !index.TryGetValue(m, out int member) || member <= at))
                    {
                        found.Add((step.Key, $"Path {path.Name} of step '{step.Key}' enters branch '{branch}', whose steps do not all come after '{step.Key}'."));
                    }
                }
            }

            foreach (StepShape waiting in steps.Skip(at + 1).Where(s => s.After.Contains(step.Key, StringComparer.Ordinal)))
            {
                if (!forward.Contains(waiting.Key, StringComparer.Ordinal) && waiting.Key != transition.Join)
                {
                    found.Add((step.Key, $"Step '{waiting.Key}' waits on '{step.Key}', whose transition decides what runs after it, and is none of its paths' steps nor its join: name it as a path, or have it wait on a path's step."));
                }
            }

            if (transition.Join is string join)
            {
                if (!index.TryGetValue(join, out int joined) || joined <= at)
                {
                    found.Add((step.Key, $"The join '{join}' of step '{step.Key}' is not a step after it."));
                }
                else
                {
                    foreach (string target in forward.Where(t => t != join && !Waits(steps, index, join, t)))
                    {
                        found.Add((step.Key, $"The join '{join}' of step '{step.Key}' does not wait on '{target}', the step of one of the paths it joins."));
                    }
                }
            }
        }

        return found;
    }

    /// <summary>True when a step waits on another, directly or through the steps it waits on, by after, its inputs or its timing.</summary>
    private static bool Waits(IReadOnlyList<StepShape> steps, Dictionary<string, int> index, string from, string on)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([from]);
        while (pending.Count > 0)
        {
            string key = pending.Pop();
            if (!index.TryGetValue(key, out int at) || !seen.Add(key))
            {
                continue;
            }

            foreach (string waited in steps[at].After.Concat(steps[at].Also))
            {
                if (waited == on)
                {
                    return true;
                }

                pending.Push(waited);
            }
        }

        return false;
    }

    private static FlowReview? ReviewOf(JsonElement review) => review.ValueKind switch
    {
        JsonValueKind.String => new FlowReview(review.GetString()!, null),
        JsonValueKind.Object => new FlowReview(TJson.Text(review, "independence")!, TJson.Text(review, "role")),
        _ => null,
    };

    private static int? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;
}
