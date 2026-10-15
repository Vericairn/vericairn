// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Model;
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
public static class Routing
{
    /// <summary>The role a work order is carried in when it names none.</summary>
    public const string DefaultRole = "builder";

    /// <summary>
    /// The roles whose steps are reserved actions, and the action each holds: the publisher's is publication, which an operator holds
    /// and a grant in force of the 'publication' scope gives; the decider's is a decision, such as the acceptance of a release or a
    /// milestone or another reserved action, which only an operator makes.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReservedRoles { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["publisher"] = "publication",
        ["decider"] = "decision",
    };

    /// <summary>What this machine offers routing.</summary>
    /// <param name="Launchers">Each coding-agent launcher available here, by id, and the harness it starts, as an agent principal's product names it (Claude Code, Codex); null for a launcher that names no harness, such as the generic one, which serves no agent by itself.</param>
    /// <param name="Holds">Whether a key store of this machine holds a device's key, by the device's identity.</param>
    /// <param name="Custody">The custody guard here for an unattended run of a goal by an agent: one reason per device of an operator or of the goal's sponsor whose key signs here without its owner's presence; empty when none does.</param>
    /// <param name="Now">When availability is judged: the grants and the custody exceptions in force then.</param>
    public sealed record Machine(IReadOnlyList<(string Launcher, string? Harness)> Launchers, Func<string, bool> Holds, Func<RecordEnvelope, string, IReadOnlyList<string>> Custody, DateTimeOffset Now);

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
    }

    /// <summary>The role a work order is carried in: the role it names, or builder.</summary>
    public static string RoleOf(RecordEnvelope order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return EstateModel.BodyString(order, "role") ?? DefaultRole;
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
    /// The grant in force at a time that gives a principal a scope, such as publication: the latest current authority record that names
    /// it the grantee of the scope and has not expired then; null when none does, as for an operator, who holds every reserved action by
    /// its role.
    /// </summary>
    public static RecordEnvelope? Grant(EstateModel model, string? principal, string scope, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(scope);
        return principal is null ? null : model.Current("authority")
            .Where(a => EstateModel.BodyString(a, "grantee") == principal && !EstateModel.Expired(a, at)
                && a.Body.TryGetProperty("scope", out JsonElement scopes) && scopes.ValueKind == JsonValueKind.Array
                && scopes.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == scope))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal)
            .LastOrDefault();
    }

    /// <summary>
    /// Whether a principal the roster names for a role can carry a step of a goal from this machine, and why not. An agent needs a
    /// launcher here for its harness, a key of one of its admitted devices held here, and the goal's budget to leave something for a run
    /// of the step (<see cref="AgentRuns.Exhausted"/>), and for a reserved action a grant in force that covers it; a person needs its
    /// principal record, the operator role for the decider role, and for a reserved action to hold it. Whether the goal is active, and
    /// the runs in flight, decide when a run starts, not who carries the step.
    /// </summary>
    public static Candidate Evaluate(EstateModel model, RecordEnvelope goal, AgentRuns.Ledger ledger, string principal, string role, RecordEnvelope? assignment, Machine machine)
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

            return new Candidate(principal, name, kind, reasons.Count == 0, null, reasons);
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
        if (launcher is null)
        {
            List<string> named = machine.Launchers.Where(l => l.Harness is not null).Select(l => $"{l.Launcher} for {l.Harness}").ToList();
            reasons.Add($"no launcher for its harness, {product ?? "which its principal record does not name"}, is available on this machine ({(named.Count == 0 ? "none is" : string.Join(", ", named))})");
        }

        List<RecordEnvelope> admitted = model.Current("device").Where(d => EstateModel.BodyString(d, "principal") == principal && SignatureChecks.IsAdmitted(model, d)).ToList();
        if (admitted.Count == 0)
        {
            reasons.Add("it has no admitted device, so it could sign no attempt and no result");
        }
        else if (!admitted.Any(d => machine.Holds(model.Chain(d)[0])))
        {
            reasons.Add("no key of its admitted devices is held on this machine, so the MCP server here would list only the read-only tools");
        }

        reasons.AddRange(AgentRuns.Exhausted(model, ledger, assignment?.Id));
        return new Candidate(principal, name, kind, reasons.Count == 0, launcher, reasons);
    }

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
        return model.CurrentEstate is RecordEnvelope charter ? EstateModel.BodyString(charter, "operator") : null;
    }

    /// <summary>The current assignments of a work order, any revision of it, oldest first; the last one holds the step.</summary>
    public static IReadOnlyList<RecordEnvelope> Assignments(EstateModel model, RecordEnvelope order)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(order);
        IReadOnlyList<string> chain = model.Chain(model.Latest(order));
        return model.Current("assignment").Where(a => a.Subject is not null && chain.Contains(a.Subject.Id))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
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

    private static List<string>? Named(JsonElement body, string role)
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
