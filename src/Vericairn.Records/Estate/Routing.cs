// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.ObjectModel;
using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// Routing by roster (the estate's design/goals.md § 6), the rules the next-eligible view routes a goal's steps with. A step is carried
/// in a role, the role its work order names or builder, and the goal's roster names the principals of each role in order of
/// preference: the goal's own roles first, then the roster it names, then the estate's. The first of them that is available takes the
/// step. An agent is available when a launcher for its harness is available on this machine, a key of one of its admitted devices is
/// held here, and the goal's budget leaves something for a run; a person when the estate holds its principal record and it holds the
/// role. A step whose role holds a reserved action (the publisher's publication, the decider's decisions and reserved acceptances)
/// reaches only a person who holds that action, an operator or the grantee of a grant in force, or an agent such a grant covers.
/// When none is available the step is an ask; nothing here ever falls back to a principal the roster does not name.
/// </summary>
public static partial class Routing
{
    /// <summary>The role a work order is carried in when it names none.</summary>
    public const string DefaultRole = "builder";

    /// <summary>
    /// The roles whose steps are reserved actions, and the action each holds: the publisher's is publication, which an operator holds
    /// and a grant in force of the 'publication' scope gives; the decider's is a decision, such as the acceptance of a release or a
    /// milestone or another reserved action, which only an operator makes.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReservedRoles { get; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["publisher"] = "publication",
        ["decider"] = "decision",
    });

    /// <summary>What this machine offers routing.</summary>
    /// <param name="Launchers">Each coding-agent launcher available here, by id, and the harness it starts, as an agent principal's product names it (Claude Code, Codex); null for a launcher that names no harness, such as the generic one, which serves no agent by itself.</param>
    /// <param name="Holds">Whether a key store of this machine holds a device's key, by the device's identity.</param>
    /// <param name="Custody">The custody guard here for an unattended run of a goal by an agent: one reason per device of an operator or of the goal's sponsor whose key signs here without its owner's presence; empty when none does.</param>
    /// <param name="Now">When availability is judged: the grants and the custody exceptions in force then.</param>
    public sealed record Machine(IReadOnlyList<(string Launcher, string? Harness)> Launchers, Func<string, bool> Holds, Func<RecordEnvelope, string, IReadOnlyList<string>> Custody, DateTimeOffset Now)
    {
        /// <summary>From 0.9.4 (GOAL-11): the agent sessions this runtime registered whose leases are live, for the session route; none when it keeps no presence.</summary>
        public IReadOnlyList<LiveSession> Sessions { get; init; } = [];
    }

    /// <summary>
    /// From 0.9.4 (GOAL-11; design/events-and-presence.md § 10): an agent session registered with this runtime whose lease is live,
    /// which routing may give a step of a role it offers while it holds fewer steps than its capacity.
    /// </summary>
    /// <param name="Session">The session's public identity.</param>
    /// <param name="Principal">The principal it acts for, by the id of its first record.</param>
    /// <param name="Label">Its label.</param>
    /// <param name="Roles">The roles it offers; empty for every role the roster names its principal for.</param>
    /// <param name="Capacity">How many steps it takes at once.</param>
    /// <param name="Holding">How many open steps it holds now.</param>
    public sealed record LiveSession(string Session, string Principal, string? Label, IReadOnlyList<string> Roles, int Capacity, int Holding)
    {
        /// <summary>The open assignments it holds, by the id of each one's first record.</summary>
        public IReadOnlyList<string> Assignments { get; init; } = [];

        /// <summary>True when it offers a role and has room for another step.</summary>
        public bool Takes(string role) => Holding < Capacity && (Roles.Count == 0 || Roles.Contains(role, StringComparer.Ordinal));
    }

    /// <summary>Where a role's principals come from.</summary>
    /// <param name="Roster">The roster record at its current revision; null when the goal's own roles name them, or when there is no roster.</param>
    /// <param name="Description">In words.</param>
    public sealed record Source(RecordEnvelope? Roster, string Description);

    /// <summary>A principal the roster names for a step, and whether it is available to carry it from this machine.</summary>
    /// <param name="Principal">The principal's identity, the id of its first record.</param>
    /// <param name="Name">Its name.</param>
    /// <param name="Kind">agent, human or service, or missing when the estate holds no such principal.</param>
    /// <param name="Available">Whether it can carry the step now.</param>
    /// <param name="Launcher">For an agent, the launcher here that starts its harness.</param>
    /// <param name="Reasons">Why it is not available; empty when it is.</param>
    public sealed record Candidate(string Principal, string Name, string Kind, bool Available, string? Launcher, IReadOnlyList<string> Reasons)
    {
        /// <summary>True for an agent principal.</summary>
        public bool IsAgent => Kind == "agent";

        /// <summary>True for a person: a human principal.</summary>
        public bool IsPerson => Kind == "human";

        /// <summary>From 0.9.4 (GOAL-11): for a person away now, as their communication setting declares, when they are back.</summary>
        public string? AwayUntil { get; init; }

        /// <summary>From 0.9.4 (GOAL-11): for an agent available through a live session rather than a launcher, the session's identity.</summary>
        public string? Session { get; init; }
    }

    /// <summary>The role a work order is carried in: the role it names, or builder.</summary>
    public static string RoleOf(RecordEnvelope order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return EstateModel.BodyString(order, "role") ?? DefaultRole;
    }

    /// <summary>
    /// The role an assignment is carried in: for a run's step, the role its flow gives the step; otherwise its work order's role. Null
    /// for a run's step whose flow does not resolve here, which nothing runs.
    /// </summary>
    public static string? RoleOf(EstateModel model, RecordEnvelope assignment)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        if (WorkView.OrderOfAssignment(model, assignment) is not RecordEnvelope order)
        {
            return DefaultRole;
        }

        if (FlowRuns.StepOf(assignment) is not FlowRuns.StepRef step)
        {
            // From 0.9.4 (GOAL-07) an occurrence of a standing agent is carried in the role of its flow's work step; null when that flow
            // does not resolve here, which nothing runs.
            if (StandingAgents.KeyOf(assignment) is not null && StandingAgents.StandingOf(model, order) is RecordEnvelope standing)
            {
                return StandingAgents.Resolve(model, standing).Flow is Templates.FlowDefinition flow && StandingAgents.WorkStep(flow) is Templates.FlowStep work ? work.Role : null;
            }

            return RoleOf(model.Latest(order));
        }

        return FlowRuns.RunOf(model, order) is RecordEnvelope run && FlowRuns.Resolve(model, run).Flow?.Step(step.Key) is Templates.FlowStep declared ? declared.Role : null;
    }

    /// <summary>The reserved action a role holds, or null for a role whose steps are not reserved actions.</summary>
    public static string? ReservedAction(string role) => ReservedRoles.TryGetValue(role, out string? action) ? action : null;

    /// <summary>A reserved action in words.</summary>
    public static string Describe(string action) => action == "decision" ? "a decision, which only an operator makes" : action;

    /// <summary>
    /// The principals a goal's roster names for a role, in order of preference, and where they come from: the goal's own roles when
    /// they name the role, else the roster the goal names at its current revision, else the estate's current roster. Empty when none
    /// names the role.
    /// </summary>
    public static (IReadOnlyList<string> Principals, Source Source) Principals(EstateModel model, RecordEnvelope goal, string role)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(role);
        if (Named(goal.Body, role) is { } own)
        {
            return (own, new Source(null, "the goal's own roles"));
        }

        RecordEnvelope? roster = EstateModel.BodyString(goal, "roster") is string named && model.ById.TryGetValue(named, out RecordEnvelope? record) && record.Type == "roster"
            ? model.Latest(record)
            : model.Current("roster").OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
        if (roster is null)
        {
            return ([], new Source(null, "no roster: the estate records none, and the goal names none and no role of its own"));
        }

        var source = new Source(roster, EstateModel.BodyString(goal, "roster") is null ? "the estate's roster" : "the roster the goal names");
        return (Named(roster.Body, role) ?? [], source);
    }

    /// <summary>
    /// True when a principal holds a reserved action at a time: an operator holds every one; another principal holds publication under a
    /// grant in force whose scope names it; a decision is an operator's alone.
    /// </summary>
    public static bool Holds(EstateModel model, string? principal, string action, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(action);
        return principal is not null && (model.HasRole(principal, Roles.Operator) || (action != "decision" && model.HasScope(principal, action, at)));
    }

    /// <summary>
    /// The grant in force at a time that gives a principal a scope, such as publication: the latest current authority record, among those
    /// their authors may write (EstateModel.Authority), that names it the grantee of the scope and has not expired then; null when none
    /// does, as for an operator, who holds every reserved action by its role.
    /// </summary>
    public static RecordEnvelope? Grant(EstateModel model, string? principal, string scope, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(scope);
        return principal is null ? null : model.Authority.GrantLines.Select(model.Authority.Current).OfType<RecordEnvelope>()
            .Where(a => EstateModel.BodyString(a, "grantee") == principal && !EstateModel.Expired(a, at)
                && a.Body.TryGetProperty("scope", out JsonElement scopes) && scopes.ValueKind == JsonValueKind.Array
                && scopes.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == scope))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal)
            .LastOrDefault();
    }

    /// <summary>
    /// The goals a work order is a step of, each at its revision when a record was written and only when its work then included the work
    /// order; at their current revisions without a record. The work order is named by any of its revisions.
    /// </summary>
    public static IReadOnlyList<RecordEnvelope> GoalsOf(EstateModel model, RecordEnvelope order, RecordEnvelope? before = null) => GoalsOf(model, order, before, proposed: false);

    /// <summary>
    /// The goals a work order is a step of, as <see cref="GoalsOf(EstateModel, RecordEnvelope, RecordEnvelope?)"/>, and with
    /// <paramref name="proposed"/> also those it is proposed for and that wait for their sponsor's approval.
    /// </summary>
    private static List<RecordEnvelope> GoalsOf(EstateModel model, RecordEnvelope order, RecordEnvelope? before, bool proposed)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(order);
        string identity = model.Chain(model.Latest(order))[0];
        var goals = new List<RecordEnvelope>();
        foreach (RecordEnvelope latest in model.Current("goal").OrderBy(g => g.Id, StringComparer.Ordinal))
        {
            RecordEnvelope? revision = before is null ? latest : AgentRuns.GoalAt(model, latest.Id, before);
            if (revision is not null && (proposed ? GoalRules.WorkAndProposed(model, revision, before) : GoalRules.Work(model, revision, before)).Any(o => model.Chain(o)[0] == identity))
            {
                goals.Add(revision);
            }
        }

        return goals;
    }

    /// <summary>
    /// True when a principal decides for a goal a work order is a step of, or is proposed for and waits for its sponsor's approval, as of
    /// a record when one is given: the goal's sponsor when the sponsor is a person, or an operator when an agent sponsors it (decision
    /// 01M507XV4VBFYWMRN8WCXMXHX4). Who would approve a proposed work order may also assign or hand over its step.
    /// </summary>
    public static bool DecidesForStep(EstateModel model, string? principal, RecordEnvelope order, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(order);
        return principal is not null && GoalsOf(model, order, before, proposed: true).Any(g => GoalRules.AccepterOf(model, g, before).Decides(model, principal, before));
    }

    /// <summary>
    /// When an assignment is a hand-over (design/goals.md § 6, step 4), the assignment it takes the step over from: the latest assignment
    /// of the same work order, by any revision, and of the same flow step for a run's, or from 0.9.4 of the same occurrence for a standing
    /// agent's, recorded before it, when that one's contributor is
    /// an agent or a service and this one's is a person. Null for any other assignment.
    /// </summary>
    public static RecordEnvelope? TakesOverFrom(EstateModel model, RecordEnvelope assignment)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        if (!GoalRules.IsPerson(model, EstateModel.BodyString(assignment, "contributor")) || assignment.Subject is null || !model.ById.TryGetValue(assignment.Subject.Id, out RecordEnvelope? order))
        {
            return null;
        }

        IReadOnlyList<string> orders = model.Chain(model.Latest(order));
        RecordEnvelope? previous = model.OfType("assignment")
            .Where(a => a.Id != assignment.Id && a.Subject is not null && orders.Contains(a.Subject.Id) && FlowRuns.StepOf(a) == FlowRuns.StepOf(assignment) && StandingAgents.KeyOf(a) == StandingAgents.KeyOf(assignment) && GoalRules.Before(a, assignment) && !Stranded(model, a, assignment))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal)
            .LastOrDefault();
        string kind = previous is not null && model.CurrentPrincipal(EstateModel.BodyString(previous, "contributor")) is RecordEnvelope holder ? EstateModel.BodyString(holder, "kind") ?? "?" : "?";
        return kind is "agent" or "service" ? previous : null;
    }

    /// <summary>
    /// Whether a principal may record a hand-over of a work order's step to a person, as of a record when one is given: a person only,
    /// the one taking the step over, who decides for a goal the step belongs to (its sponsor when a person), or an operator; never an agent.
    /// </summary>
    public static bool MayHandOver(EstateModel model, string? principal, string? person, RecordEnvelope order, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(order);
        return GoalRules.IsPerson(model, principal)
            && (principal == person || (before is null ? model.HasRole(principal, Roles.Operator) : model.HasRoleAt(principal, Roles.Operator, before)) || DecidesForStep(model, principal, order, before));
    }

    /// <summary>
    /// Whether a principal may take a step of a goal: an operator; the goal's sponsor when the sponsor is a person, under the goal's
    /// activation; a principal holding the 'local-engineering' scope at the time, which is how the builder dispatches agents under the
    /// operator's authority; or, from 0.9.4 (ID-07), a dispatcher whose dispatch grant in force names the goal, whose every assignment is
    /// judged against that grant. An agent that sponsors a goal takes its steps only as such a principal, since an operator decides for
    /// that goal.
    /// </summary>
    public static bool MayTakeStep(EstateModel model, RecordEnvelope goal, string? principal, DateTimeOffset at) => MayTakeStep(model, goal, principal, at, null);

    /// <inheritdoc cref="MayTakeStep(EstateModel, RecordEnvelope, string?, DateTimeOffset)"/>
    /// <param name="model">The estate.</param>
    /// <param name="goal">The goal.</param>
    /// <param name="principal">The principal.</param>
    /// <param name="at">When.</param>
    /// <param name="record">The record judged, whose place roles and grants are read at; null to read them as they are now.</param>
    public static bool MayTakeStep(EstateModel model, RecordEnvelope goal, string? principal, DateTimeOffset at, RecordEnvelope? record)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        return principal is not null && model.CurrentPrincipal(principal) is not null
            && ((record is null
                ? model.HasRole(principal, Roles.Operator) || GoalRules.AccepterOf(model, goal).Decides(model, principal) || model.HasScope(principal, Scopes.LocalEngineering, at)
                : model.HasRoleAt(principal, Roles.Operator, record) || GoalRules.AccepterOf(model, goal).Decides(model, principal, record) || model.HasScopeAt(principal, Scopes.LocalEngineering, record))
              || DispatchAuthority.CoversGoal(model, principal, goal, record?.At ?? Model.RecordParser.FormatTimestamp(at), record?.AtTime ?? at));
    }

    /// <summary>
    /// Whether a principal the roster names for a role can carry a step of a goal from this machine, and why not. An agent needs a
    /// launcher here for its harness, a key of one of its admitted devices held here, and the goal's budget to leave something for a run
    /// of the step (<see cref="AgentRuns.Exhausted"/>), and for a reserved action a grant in force that covers it; a person needs its
    /// principal record, the operator role for the decider role, and for a reserved action to hold it. Whether the goal is active, and
    /// the runs in flight, decide when a run starts, not who carries the step.
    /// </summary>
    public static Candidate Evaluate(EstateModel model, RecordEnvelope goal, AgentRuns.Ledger ledger, string principal, string role, RecordEnvelope? assignment, Machine machine) =>
        Evaluate(model, goal, ledger, principal, role, assignment, machine, null);

    /// <summary>
    /// <see cref="Evaluate(EstateModel, RecordEnvelope, AgentRuns.Ledger, string, string, RecordEnvelope?, Machine)"/>, and from 0.9.4
    /// (GOAL-11): a person away now is unavailable, and an agent without a launcher here is available through a live session that offers
    /// the role and has room, when the step's exposure reads nothing. <paramref name="sessionExposure"/> says why it reads (empty when it
    /// reads nothing); null when it is not known, and then no session takes the step (design/holds-reads-sends.md § 7).
    /// </summary>
    /// <param name="model">The estate.</param>
    /// <param name="goal">The goal.</param>
    /// <param name="ledger">The goal's agent-run ledger.</param>
    /// <param name="principal">The principal the roster names.</param>
    /// <param name="role">The step's role.</param>
    /// <param name="assignment">The assignment that holds the step, when one does.</param>
    /// <param name="machine">What this machine offers routing.</param>
    /// <param name="sessionExposure">Why the step's exposure reads, from <see cref="SessionExposure"/>; null when unknown.</param>
    public static Candidate Evaluate(EstateModel model, RecordEnvelope goal, AgentRuns.Ledger ledger, string principal, string role, RecordEnvelope? assignment, Machine machine, IReadOnlyList<string>? sessionExposure)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(machine);
        RecordEnvelope? record = model.CurrentPrincipal(principal);
        if (record is null)
        {
            return new Candidate(principal, principal, "missing", false, null, ["the estate holds no principal record of it"]);
        }

        string name = EstateModel.BodyString(record, "name") ?? principal;
        string kind = EstateModel.BodyString(record, "kind") ?? "?";
        string? action = ReservedAction(role);
        var reasons = new List<string>();
        if (kind == "human")
        {
            if (role == "decider" && !model.HasRole(principal, Roles.Operator))
            {
                reasons.Add("the decider role is an operator's, and it holds the operator role no longer");
            }
            else if (action is not null && !Holds(model, principal, action, machine.Now))
            {
                reasons.Add($"the step is a reserved action, {Describe(action)}, and it holds neither the operator role nor a grant of {action} in force");
            }

            // From 0.9.4 (GOAL-11): a person declares an absence; routing passes over them while it lasts.
            string? away = AwayUntil(model, principal, machine.Now);
            if (away is not null)
            {
                reasons.Add($"away until {away}, as their communication setting declares");
            }

            return new Candidate(principal, name, kind, reasons.Count == 0, null, reasons) { AwayUntil = away };
        }

        if (kind != "agent")
        {
            return new Candidate(principal, name, kind, false, null, [$"a {kind} principal carries no step"]);
        }

        if (action is not null && (action == "decision" || !model.HasScope(principal, action, machine.Now)))
        {
            reasons.Add(action == "decision" ? "the step is a decision, which only an operator makes, never an agent" : $"the step is a reserved action, {action}, and no grant of {action} in force covers it");
        }

        string? product = record.Body.TryGetProperty("agent", out JsonElement agent) && agent.ValueKind == JsonValueKind.Object && agent.TryGetProperty("product", out JsonElement p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        string? launcher = machine.Launchers.FirstOrDefault(l => product is not null && (string.Equals(l.Harness, product, StringComparison.OrdinalIgnoreCase) || string.Equals(l.Launcher, product, StringComparison.OrdinalIgnoreCase))).Launcher;
        var launching = new List<string>();
        if (launcher is null)
        {
            List<string> named = machine.Launchers.Where(l => l.Harness is not null).Select(l => $"{l.Launcher} for {l.Harness}").ToList();
            launching.Add($"no launcher for its harness, {product ?? "which its principal record does not name"}, is available on this machine ({(named.Count == 0 ? "none is" : string.Join(", ", named))})");
        }

        List<RecordEnvelope> admitted = model.Current("device").Where(d => EstateModel.BodyString(d, "principal") == principal && SignatureChecks.IsAdmitted(model, d)).ToList();
        if (admitted.Count == 0)
        {
            reasons.Add("it has no admitted device, so it could sign no attempt and no result");
        }
        else if (!admitted.Any(d => machine.Holds(model.Chain(d)[0])))
        {
            launching.Add("no key of its admitted devices is held on this machine, so the MCP server here would list only the read-only tools");
        }

        // From 0.9.4 (GOAL-11): without a launcher here, a live session registered for the role carries the step, unless the step's
        // exposure reads: the runtime cannot see a session's own facts (design/holds-reads-sends.md § 7).
        string? session = null;
        if (launching.Count > 0 && admitted.Count > 0 && machine.Sessions.FirstOrDefault(s => s.Principal == principal && s.Takes(role)) is LiveSession live)
        {
            if (sessionExposure is { Count: 0 })
            {
                session = live.Session;
                launching.Clear();
            }
            else
            {
                launching.Add($"its live session {live.Label ?? live.Session} takes no step whose exposure reads, and this one's does: {(sessionExposure is null ? "its exposure is not known here, which counts as reading" : string.Join("; ", sessionExposure))}");
            }
        }

        reasons.AddRange(launching);
        reasons.AddRange(AgentRuns.Exhausted(model, ledger, assignment?.Id));
        return new Candidate(principal, name, kind, reasons.Count == 0, session is null ? launcher : null, reasons) { Session = session };
    }

    /// <summary>
    /// From 0.9.4 (GOAL-11): when a person is away at a time, as their current own-scope communication setting declares, when they are
    /// back; null when they are available, declare nothing, or have divergent current revisions, which declare nothing until reconciled.
    /// </summary>
    public static string? AwayUntil(EstateModel model, string? person, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.CurrentPrincipal(person) is not RecordEnvelope who)
        {
            return null;
        }

        string identity = model.Chain(who)[0];
        List<RecordEnvelope> settings = model.Current("communication-setting")
            .Where(r => EstateModel.BodyString(r, "scope") == "person" && model.CurrentPrincipal(EstateModel.BodyString(r, "recipient")) is { } recipient && model.Chain(recipient)[0] == identity)
            .ToList();
        if (settings.Count != 1 || !settings[0].Body.TryGetProperty("values", out JsonElement values) || values.ValueKind != JsonValueKind.Object
            || !values.TryGetProperty("availability", out JsonElement availability) || availability.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? Text(string name) => availability.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        string? until = Text("until");
        string? from = Text("from");
        string now = Model.RecordParser.FormatTimestamp(at);
        return Text("state") == "away" && until is not null && string.CompareOrdinal(now, until) < 0 && (from is null || string.CompareOrdinal(now, from) >= 0) ? until : null;
    }

    /// <summary>
    /// From 0.9.4 (GOAL-11; design/holds-reads-sends.md § 7): why a step's exposure reads, so that no live session takes it, since the
    /// runtime cannot see a session's own facts; empty when it reads nothing. Until the exposure algorithm is built (RUN-06) this judges
    /// conservatively from what the records show: a step reads when its goal, a record the goal names among its inputs, its work order, a
    /// record the work order names among its inputs, or a record the step carries (a record it pins, and for a step already held its
    /// assignments, their attempts and results and the reviews of those results) was written by any principal that is not a person
    /// (an agent's or a service's records are outside-derived, a session's exposure being unknown), is a conversation observation
    /// (outside text), or cannot be resolved here (unknown counts as present).
    /// </summary>
    public static IReadOnlyList<string> SessionExposure(EstateModel model, RecordEnvelope goal, RecordEnvelope? order, IEnumerable<RecordEnvelope?>? pinned = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        var reasons = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Judge(RecordEnvelope? record, string what)
        {
            if (record is null)
            {
                reasons.Add($"{what} cannot be resolved here, which counts as reading");
                return;
            }

            if (!seen.Add(record.Id))
            {
                return;
            }

            if (!GoalRules.IsPerson(model, record.By))
            {
                reasons.Add($"{what} ({record.Type} {record.Id}) was written by {NameOf(model, record.By)}, which is no person, so it is outside-derived");
            }
            else if (record.Type == "observation" && EstateModel.BodyString(record, "kind") == "conversation")
            {
                reasons.Add($"{what} ({record.Type} {record.Id}) is a conversation observation, outside text");
            }
        }

        RecordEnvelope latest = model.Latest(goal);
        Judge(latest, "the goal");
        foreach (JsonElement input in TJson.Array(latest.Body, "inputs"))
        {
            Judge(TJson.Text(input, "id") is string id && model.ById.TryGetValue(id, out RecordEnvelope? named) ? model.Latest(named) : null, "an input of the goal");
        }

        if (order is not null)
        {
            RecordEnvelope current = model.Latest(order);
            Judge(current, "the work order");
            foreach (string id in GoalRules.Inputs(current))
            {
                Judge(model.ById.TryGetValue(id, out RecordEnvelope? input) ? model.Latest(input) : null, "an input of the work order");
            }
        }

        foreach (RecordEnvelope? record in pinned ?? [])
        {
            Judge(record, "a record the step carries");
        }

        return reasons;
    }

    /// <summary>
    /// From 0.9.4 (GOAL-11): what a step already held carries for its exposure: each assignment, its attempts, their results and the
    /// reviews of those results, which a session would read when the step goes on with it.
    /// </summary>
    public static IEnumerable<RecordEnvelope> StepRecords(EstateModel model, IEnumerable<RecordEnvelope> assignments)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignments);
        foreach (RecordEnvelope assignment in assignments)
        {
            yield return assignment;
            foreach (RecordEnvelope attempt in WorkView.AttemptsOf(model, assignment))
            {
                yield return attempt;
                if (WorkView.ResultOf(model, attempt) is RecordEnvelope result)
                {
                    yield return result;
                    foreach (RecordEnvelope review in model.About(result.Id, "review").Where(model.IsCurrent))
                    {
                        yield return review;
                    }
                }
            }
        }
    }

    /// <summary>
    /// From 0.9.4 (GOAL-11; design/events-and-presence.md § 10): true when an ask is a standing lapse ask about an assignment, as of a
    /// record when one is given: the ask the runtime's presence sweep records when the session holding the assignment lapsed, about that
    /// exact assignment and signed by the principal that recorded it (the sweep signs with no other key), in the sweep's words, still
    /// open or notified, neither withdrawn nor answered by a decision. A hand-over's basis is such an ask; an ask any other principal
    /// wrote, a withdrawn one and an answered one are not.
    /// </summary>
    public static bool LapseAsk(EstateModel model, RecordEnvelope ask, RecordEnvelope assignment, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(ask);
        ArgumentNullException.ThrowIfNull(assignment);
        if (ask.Type != "ask" || !model.ById.ContainsKey(ask.Id))
        {
            return false;
        }

        IReadOnlyList<string> revisions = model.Chain(model.Latest(ask));
        RecordEnvelope first = model.ById[revisions[0]];
        RecordEnvelope? standing = revisions.Select(id => model.ById.GetValueOrDefault(id)).OfType<RecordEnvelope>().LastOrDefault(r => before is null || GoalRules.Before(r, before));
        if (standing is null || first.Subject is not { Type: "assignment" } about || !model.Chain(model.Latest(assignment)).Contains(about.Id)
            || !model.ById.TryGetValue(about.Id, out RecordEnvelope? named) || first.By != named.By
            || EstateModel.BodyString(first, "question") is not string question || !LapseQuestion().IsMatch(question) || !question.Contains($" assignment {about.Id} ", StringComparison.Ordinal))
        {
            return false;
        }

        return EstateModel.BodyString(standing, "state") is "open" or "notified"
            && !model.DecisionsAnswering(revisions).Any(d => before is null || GoalRules.Before(d, before));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\ASession .+ (?:which holds|was given) assignment [0-9A-HJKMNP-TV-Z]{26} .+ Hand the step to another principal of its role\?\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex LapseQuestion();

    /// <summary>
    /// Who decides for a goal: its sponsor when the sponsor is a person, and otherwise the operator who activated it, or the estate's
    /// root operator before any activation. Null when the estate names none.
    /// </summary>
    public static string? Decider(EstateModel model, RecordEnvelope goal)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        string? sponsor = EstateModel.BodyString(goal, "sponsor");
        if (GoalRules.IsPerson(model, sponsor))
        {
            return sponsor;
        }

        RecordEnvelope? activation = GoalRules.Statuses(model, goal).FirstOrDefault(s => EstateModel.BodyString(s, "status") == "active");
        return activation?.By ?? RootOperator(model);
    }

    /// <summary>
    /// Who is asked about a step no principal is available for: who decides for the goal (<see cref="Decider"/>), the sponsor; for a step
    /// that is a reserved action, that principal when it holds the action, and otherwise the estate's root operator, who holds every
    /// reserved action. Null when the estate names none.
    /// </summary>
    public static string? Asked(EstateModel model, RecordEnvelope goal, string? reserved, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        string? decider = Decider(model, goal);
        return decider is not null && (reserved is null || Holds(model, decider, reserved, at)) ? decider : RootOperator(model) ?? decider;
    }

    /// <summary>The estate's root operator, as its charter names it; null without a charter.</summary>
    public static string? RootOperator(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.RootOperator;
    }

    /// <summary>
    /// The current assignments of a work order that hold its step, any revision of it, oldest first; the last one holds the step. An
    /// assignment routing stored without the record it was routed with holds nothing (<see cref="Stranded(EstateModel, RecordEnvelope, RecordEnvelope?)"/>).
    /// From 0.9.4 (ID-11; design/events-and-presence.md § 10) neither does one that would take the step from another principal's
    /// holding without a person's act (<see cref="Holding"/>).
    /// </summary>
    public static IReadOnlyList<RecordEnvelope> Assignments(EstateModel model, RecordEnvelope order)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(order);
        IReadOnlyList<string> chain = model.Chain(model.Latest(order));
        return Holding(model, model.Current("assignment").Where(a => a.Subject is not null && chain.Contains(a.Subject.Id) && !Stranded(model, a))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal));
    }

    /// <summary>
    /// From 0.9.4 (ID-11; design/events-and-presence.md § 10): of a work order's assignments in order, those that hold their step, which
    /// each step's assignments pass in turn. The first holds it; a later one of the same principal goes on holding it; a later one of
    /// another principal takes it over only when a person recorded it (a hand-over, which a lapse ask's basis also needs, or an operator's
    /// or a sponsor's assignment) and that person was not displaced from the step before. An assignment an agent or a service recorded,
    /// whatever its scope or grant, or one the displaced principal recorded, takes nothing from the holder and holds nothing itself, so its
    /// writes are fenced as a displaced holder's are, and it never makes its author the holder again.
    /// </summary>
    private static List<RecordEnvelope> Holding(EstateModel model, IEnumerable<RecordEnvelope> ordered)
    {
        var holding = new List<RecordEnvelope>();
        var steps = new Dictionary<string, (RecordEnvelope Holder, string? Principal, HashSet<string?> Displaced)>(StringComparer.Ordinal);
        foreach (RecordEnvelope assignment in ordered)
        {
            string key = HoldingKey(assignment.Body);
            string? contributor = Identity(model, EstateModel.BodyString(assignment, "contributor"));
            // A step whose holder routing had stored without its record when this one was written had no holder then: this one holds it.
            if (!steps.TryGetValue(key, out (RecordEnvelope Holder, string? Principal, HashSet<string?> Displaced) step) || Stranded(model, step.Holder, assignment))
            {
                steps[key] = (assignment, contributor, step.Displaced ?? []);
                holding.Add(assignment);
                continue;
            }

            if (contributor == step.Principal)
            {
                steps[key] = (assignment, contributor, step.Displaced);
                holding.Add(assignment);
                continue;
            }

            string? author = Identity(model, assignment.By);
            if (GoalRules.IsPerson(model, author) && !step.Displaced.Contains(author))
            {
                step.Displaced.Add(step.Principal);
                steps[key] = (assignment, contributor, step.Displaced);
                holding.Add(assignment);
            }
        }

        return holding;
    }

    /// <summary>
    /// From 0.9.4 (ID-11): the step an assignment body carries, as the holder rule tells steps apart: its flow step and item, a review's
    /// subject and items, and a standing agent's occurrence; the same for every assignment of a work order that is neither.
    /// </summary>
    internal static string HoldingKey(JsonElement body)
    {
        JsonElement step = TJson.Object(body, "step");
        JsonElement review = TJson.Object(step, "review");
        return string.Join('\n', TJson.Text(step, "key") ?? string.Empty, TJson.Text(step, "instance") ?? string.Empty, review.ValueKind == JsonValueKind.Object ? "review" : string.Empty,
            TJson.Text(TJson.Object(review, "subject"), "id") ?? string.Empty, string.Join(',', TJson.Strings(review, "items").Order(StringComparer.Ordinal)),
            TJson.Text(TJson.Object(body, "occurrence"), "key") ?? string.Empty);
    }

    /// <summary>
    /// From 0.9.4 (ID-11): the assignment that holds the step a new assignment would carry, when it is another principal's and the new
    /// assignment's author is no person, so that it would take nothing and hold nothing; null otherwise.
    /// </summary>
    internal static RecordEnvelope? WouldHoldNothing(EstateModel model, RecordEnvelope order, string by, JsonElement body)
    {
        string key = HoldingKey(body);
        RecordEnvelope? holder = Assignments(model, order).LastOrDefault(a => HoldingKey(a.Body) == key);
        string? contributor = TJson.Text(body, "contributor");
        return holder is not null && Identity(model, EstateModel.BodyString(holder, "contributor")) != Identity(model, contributor) && !GoalRules.IsPerson(model, Identity(model, by))
            ? holder : null;
    }

    private static string? Identity(EstateModel model, string? principal) =>
        model.CurrentPrincipal(principal) is RecordEnvelope record ? model.Chain(record)[0] : principal;

    /// <summary>
    /// The current assignments that hold one step: of a run's work order, those that name that flow step and item; of any other work
    /// order, those that name no step. Oldest first; the last one holds the step.
    /// </summary>
    public static IReadOnlyList<RecordEnvelope> Assignments(EstateModel model, RecordEnvelope order, FlowRuns.StepRef? step)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(order);
        return Assignments(model, order).Where(a => FlowRuns.StepOf(a) == step).ToList();
    }

    /// <summary>
    /// From 0.9.4 (ID-11; design/events-and-presence.md § 10): the current assignment that holds the step an assignment carries, the
    /// last of the current assignments of its work order, by any revision, that carry the same step: for a run's work order the same
    /// flow step and item (and for a review before hand-off the same reviewed subject and items), for a standing agent's the same
    /// occurrence, and for any other work order the work order itself. Null when none holds it, as when routing stored the only one
    /// without the record it was routed with.
    /// </summary>
    public static RecordEnvelope? HolderOf(EstateModel model, RecordEnvelope assignment)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        if (WorkView.OrderOfAssignment(model, assignment) is not RecordEnvelope order)
        {
            return null;
        }

        RecordEnvelope latest = model.Latest(assignment);
        FlowRuns.StepRef? step = FlowRuns.StepOf(latest);
        string? occurrence = StandingAgents.KeyOf(latest);
        FlowRuns.ReviewRef? review = FlowRuns.ReviewOf(latest);
        return Assignments(model, order, step).LastOrDefault(a => StandingAgents.KeyOf(a) == occurrence && SameReview(FlowRuns.ReviewOf(a), review));

        static bool SameReview(FlowRuns.ReviewRef? one, FlowRuns.ReviewRef? other) =>
            one is null ? other is null
            : other is not null && one.Subject.Id == other.Subject.Id && one.Items.Order(StringComparer.Ordinal).SequenceEqual(other.Items.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    /// <summary>
    /// From 0.9.4 (ID-11; design/events-and-presence.md § 10): the assignment that displaced an assignment, or null while it holds its
    /// step: the step's current holder (<see cref="HolderOf"/>) when that is a later assignment of the same step to another principal, as a
    /// hand-over is. A later assignment of the step to the same principal keeps that principal the holder, so it displaces nothing.
    /// </summary>
    public static RecordEnvelope? DisplacedBy(EstateModel model, RecordEnvelope assignment)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        if (HolderOf(model, assignment) is not RecordEnvelope holder || model.Chain(model.Latest(assignment)).Contains(holder.Id))
        {
            return null;
        }

        string? Identity(RecordEnvelope record) =>
            EstateModel.BodyString(record, "contributor") is string contributor && model.CurrentPrincipal(contributor) is { } principal ? model.Chain(principal)[0] : EstateModel.BodyString(record, "contributor");
        return Identity(holder) == Identity(model.Latest(assignment)) ? null : holder;
    }

    /// <summary>
    /// From 0.9.4 (ID-11; design/events-and-presence.md § 10): true when an assignment no longer holds its step because a later assignment
    /// of the same step to another principal holds it (<see cref="DisplacedBy"/>): a hand-over, or any later assignment of that step to
    /// someone else. Only the step's current holder acts: the runtime refuses an attempt, progress claim or result on a displaced assignment
    /// whatever time it states, and a result another runtime recorded on one before the hand-over reached it is labeled displaced, and
    /// review routing skips it.
    /// </summary>
    public static bool Displaced(EstateModel model, RecordEnvelope assignment) => DisplacedBy(model, assignment) is not null;

    /// <summary>
    /// From 0.9.4 (ID-11; design/events-and-presence.md §§ 8 and 12): how long a lapsed session holding a step of a goal stays lapsed
    /// before the runtime asks about reassignment: the goal's limits.watch.reassignAfterSeconds, else its roster's (the roster the goal
    /// names, or the estate's), else 600 seconds. Without a goal, the estate's roster's, else 600.
    /// </summary>
    public static int ReassignAfterSeconds(EstateModel model, RecordEnvelope? goal)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (goal is not null && ReassignAfter(model.Latest(goal).Body) is int own)
        {
            return own;
        }

        RecordEnvelope? roster = goal is not null && EstateModel.BodyString(model.Latest(goal), "roster") is string named && model.ById.TryGetValue(named, out RecordEnvelope? record) && record.Type == "roster"
            ? model.Latest(record)
            : model.Current("roster").OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
        return roster is not null && ReassignAfter(roster.Body) is int inherited ? inherited : 600;

        static int? ReassignAfter(JsonElement body) =>
            body.TryGetProperty("limits", out JsonElement limits) && limits.ValueKind == JsonValueKind.Object
            && limits.TryGetProperty("watch", out JsonElement watch) && watch.ValueKind == JsonValueKind.Object
            && watch.TryGetProperty("reassignAfterSeconds", out JsonElement seconds) && seconds.ValueKind == JsonValueKind.Number && seconds.TryGetInt32(out int value)
            && value is >= 60 and <= 86_400 ? value : null;
    }

    /// <summary>
    /// True when routing stored an assignment without the record it was routed with: one whose routedWith is run has no agent-run
    /// intent on it, and one whose routedWith is ask has no ask that names it; judged as the records stand, or as they stood when a
    /// later record was written. Such an assignment holds nothing of its step, so a store interrupted between the two never holds a
    /// step, and the record stays, as every record does. An assignment routing did not mark is never stranded.
    /// </summary>
    public static bool Stranded(EstateModel model, RecordEnvelope assignment, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        return Stranded(model, assignment, model.Chain(model.Latest(assignment)), before);
    }

    /// <summary>
    /// <see cref="Stranded(EstateModel, RecordEnvelope, RecordEnvelope?)"/> over an explicit chain of the assignment's revisions: a
    /// reconciliation judges each assignment it names on its own chain, not on the kept one's that a withdrawal folds it into.
    /// </summary>
    internal static bool Stranded(EstateModel model, RecordEnvelope assignment, IReadOnlyList<string> chain, RecordEnvelope? before)
    {
        string? with = EstateModel.BodyString(assignment, "routedWith");
        // From 0.9.4 (GOAL-11) an assignment routed to a live session is stored alone, so it holds its step at once.
        if (with is null or "session")
        {
            return false;
        }

        bool Counts(RecordEnvelope record) => before is null || GoalRules.Before(record, before);
        return with switch
        {
            // A run's intent, the first record of an agent-run operation, names the assignment it carries in its target.
            "run" => !model.OfType("operation").Any(o => o.Supersedes is null && EstateModel.BodyString(o, "action") == OperationType.AgentRun && Carried(o) is string carried && chain.Contains(carried) && Counts(o)),
            "ask" => !model.OfType("ask").Any(a => EstateModel.BodyString(a, "assignment") is string named && chain.Contains(named) && Counts(a)),
            _ => false,
        };

        static string? Carried(RecordEnvelope intent) =>
            intent.Body.TryGetProperty("target", out JsonElement target) && target.ValueKind == JsonValueKind.Object && target.TryGetProperty("assignment", out JsonElement named) && named.ValueKind == JsonValueKind.String ? named.GetString() : null;
    }

    /// <summary>
    /// The current asks about a step of a goal, oldest first: those whose subject is the work order, by any revision, and whose goal is
    /// the goal, by any revision.
    /// </summary>
    public static IReadOnlyList<RecordEnvelope> AsksAbout(EstateModel model, RecordEnvelope goal, RecordEnvelope order)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(order);
        IReadOnlyList<string> orders = model.Chain(model.Latest(order));
        IReadOnlyList<string> goals = model.Chain(model.Latest(goal));
        return model.Current("ask")
            .Where(a => a.Subject is not null && orders.Contains(a.Subject.Id) && EstateModel.BodyString(a, "goal") is string g && goals.Contains(g))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>The current asks that name a goal, by any revision, oldest first.</summary>
    public static IReadOnlyList<RecordEnvelope> AsksOf(EstateModel model, RecordEnvelope goal)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        IReadOnlyList<string> goals = model.Chain(model.Latest(goal));
        return model.Current("ask").Where(a => EstateModel.BodyString(a, "goal") is string g && goals.Contains(g))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>A principal's name, or its id.</summary>
    public static string NameOf(EstateModel model, string? principal)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.CurrentPrincipal(principal) is RecordEnvelope record ? EstateModel.BodyString(record, "name") ?? principal ?? "?" : principal ?? "?";
    }

    /// <summary>The principals a body's roles name for a role, in order, once each; null when they do not name it.</summary>
    internal static List<string>? Named(JsonElement body, string role)
    {
        if (!body.TryGetProperty("roles", out JsonElement roles) || roles.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement entry in roles.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object))
        {
            if (entry.TryGetProperty("role", out JsonElement name) && name.ValueKind == JsonValueKind.String && name.GetString() == role
                && entry.TryGetProperty("principals", out JsonElement principals) && principals.ValueKind == JsonValueKind.Array)
            {
                return principals.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!).Distinct(StringComparer.Ordinal).ToList();
            }
        }

        return null;
    }
}
