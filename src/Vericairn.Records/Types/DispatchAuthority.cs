// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>
/// The dispatcher and its <c>dispatch</c> grant (ID-07; design/standing-agents.md §§ 4 and 7, design/flow.md §§ 8 and 12, and the lapse
/// bound of design/events-and-presence.md § 10): unattended dispatch is signed by a service principal that holds no other authority,
/// under an expiring grant of <c>dispatch</c> alone whose typed bound names the goals and standing agents it carries and caps how many
/// assignments it records a day. The grant expires within 30 days of being made. A dispatcher's assignment is in its authority only
/// for a step of a goal the grant named when it was recorded, within the cap, outside a reserved role, and, for a step another
/// assignment still holds, only where the bound allows reassignment after a lapse: to a principal the roster names for the step's role,
/// never the holder it replaces, a person, a step a person holds or a role the roster gives a person. A dispatcher never assigns a step
/// to itself and never works one: its attempts, progress claims and results count for nothing, and so does an assignment it records
/// outside its grant. The roster, whether a principal is a person, the step's role and the dispatcher's own record are read as they
/// stood at the assignment's time. No other record type takes this scope.
/// </summary>
internal static class DispatchAuthority
{
    /// <summary>The longest a dispatch grant may run, from the time it is made to its expiry.</summary>
    internal static readonly TimeSpan MaxTerm = TimeSpan.FromDays(30);

    /// <summary>The most assignments a day a dispatch grant's cap may allow.</summary>
    internal const int MaxPerDay = 10000;

    /// <summary>Whether an authority record gives, or any revision of its line gave, the dispatch scope.</summary>
    internal static bool IsDispatchGrant(EstateModel model, RecordEnvelope record) =>
        model.Chain(record).Select(id => model.ById[id]).Any(r => PolicyRecords.Contains(PolicyRecords.Property(r.Body, "scope"), Scopes.Dispatch));

    /// <summary>
    /// Validates an authority record for the dispatch scope. A grant of dispatch gives that scope alone, with no conditions, to a service
    /// principal whose only role is service, at record format 3, with an expiry within 30 days of the grant and one typed bound: goals,
    /// the goals and standing agents it carries, each a goal record by its first revision; maxAssignmentsPerDay; and reassign, whether it
    /// covers reassignment after a lapse. The grantee then holds no other grant in force. A grant of any other scope to a principal that
    /// holds a dispatch grant in force is refused as well, so a dispatcher holds no other authority whichever is written first.
    /// </summary>
    internal static void ValidateGrant(ValidationContext context)
    {
        BodyReader body = context.Body;
        JsonElement scopes = PolicyRecords.Property(body.Element, "scope");
        string? grantee = EstateModel.BodyString(context.Record, "grantee");
        List<JsonElement> bounds = [.. PolicyRecords.Items(PolicyRecords.Property(body.Element, "bounds")).Where(b => PolicyRecords.Text(b, "scope") == Scopes.Dispatch)];
        if (!PolicyRecords.Contains(scopes, Scopes.Dispatch))
        {
            if (bounds.Count > 0) body.Error("authority.bounds", "bounds", "Bounds belong to an explicitly granted scope: this grant does not give dispatch.");
            if (grantee is not null && OtherGrant(context.Model, grantee, context.Record, dispatch: true) is { } held)
                body.Error("authority.dispatch", "scope", $"Principal {grantee} is a dispatcher under grant {held.Id}, and a dispatcher holds no other authority: it is granted dispatch alone.");
            return;
        }

        if (context.Format < 3) context.Error("authority.format", "A dispatch grant requires record format 3.");
        if (PolicyRecords.Items(scopes).Any(s => s.ValueKind != JsonValueKind.String || s.GetString() != Scopes.Dispatch))
            body.Error("authority.dispatch", "scope", "A dispatch grant gives dispatch alone: a dispatcher holds no other authority.");
        if (PolicyRecords.Items(PolicyRecords.Property(body.Element, "conditions")).Any())
            body.Error("authority.dispatch", "conditions", "A dispatch grant states no prose conditions: its typed bound is all that limits it.");
        if (grantee is not null && PrincipalAt(context.Model, grantee, context.Record.At) is { } principal &&
            (EstateModel.BodyString(principal, "kind") != "service" || !OnlyService(principal)))
            body.Error("authority.dispatch", "grantee", "A dispatch grant is made to a service principal whose only role is service, which signs unattended dispatch and nothing else.");

        string? expires = EstateModel.BodyString(context.Record, "expires");
        if (expires is null)
            body.Error("body.missing", "expires", "A dispatch grant expires, within 30 days of being made.");
        else if (DateTimeOffset.TryParse(expires, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset until) && until - context.Record.AtTime > MaxTerm)
            body.Error("authority.dispatch", "expires", "A dispatch grant expires within 30 days of being made; renew it with a new grant.");

        if (bounds.Count != 1)
        {
            body.Error("authority.bounds", "bounds", "Scope dispatch requires exactly one typed bound naming its goals and its daily cap.");
        }
        else
        {
            BodyReader? bound = body.NestedArray("bounds", true, false)?.FirstOrDefault(b => PolicyRecords.Text(b.Element, "scope") == Scopes.Dispatch);
            if (bound is not null)
            {
                bound.Enum("scope", true, Scopes.Dispatch);
                IReadOnlyList<string> goals = bound.StringArray("goals", true, false, 26) ?? [];
                PolicyRecords.Unique(bound, goals, "goals");
                foreach (string goal in goals)
                {
                    if (!Identity.Ulid.IsValid(goal))
                        bound.Error("authority.bounds", "goals", "Goals are named by the id of their first revision.");
                    else if (!context.Model.ById.TryGetValue(goal, out RecordEnvelope? named) || named.Type != "goal" || context.Model.Chain(named)[0] != goal)
                        bound.Error("authority.bounds", "goals", $"Goal {goal} is not the first revision of a goal recorded here.");
                }

                bound.Number("maxAssignmentsPerDay", true, 1, MaxPerDay);
                bound.Flag("reassign", false);
                bound.NoOtherKeys();
            }
        }

        if (grantee is not null && OtherGrant(context.Model, grantee, context.Record, dispatch: false) is { } other)
            body.Error("authority.dispatch", "grantee", $"Principal {grantee} holds grant {other.Id} of another scope, and a dispatcher holds no other authority.");
    }

    /// <summary>Whether a principal record's only role is service.</summary>
    private static bool OnlyService(RecordEnvelope principal) => AuthorityReplay.Roles(principal) is [Roles.Service];

    /// <summary>
    /// Another grant in force for a principal at a record's time, other than the record's own line: with <paramref name="dispatch"/>, one
    /// giving dispatch; otherwise one giving any other scope. Grants are read as their lines stood then, among those their authors may write.
    /// </summary>
    private static RecordEnvelope? OtherGrant(EstateModel model, string principal, RecordEnvelope record, bool dispatch)
    {
        string own = model.Chain(record)[0];
        foreach (string line in model.Authority.GrantLines)
        {
            if (line == own || model.Authority.At(line, record.At) is not { } grant || EstateModel.BodyString(grant, "grantee") != principal || EstateModel.Expired(grant, record.AtTime)) continue;
            List<string> given = [.. PolicyRecords.Items(PolicyRecords.Property(grant.Body, "scope")).Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()!)];
            if (dispatch ? given.Contains(Scopes.Dispatch) : given.Any(s => s != Scopes.Dispatch)) return grant;
        }

        return null;
    }

    /// <summary>
    /// The dispatch grant in force for a principal at a record's time: the revision of a grant line, among those their authors may write,
    /// that stood then, naming the principal the grantee of dispatch, unexpired when the record was authored and not closed by a closing
    /// that does not witness the record. Null when there is none.
    /// </summary>
    internal static RecordEnvelope? GrantAt(EstateModel model, string principal, RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(record);
        foreach (string line in model.Authority.GrantLines.Order(StringComparer.Ordinal))
        {
            if (model.Authority.At(line, record.At) is not { } grant || EstateModel.BodyString(grant, "grantee") != principal) continue;
            if (PolicyRecords.Contains(PolicyRecords.Property(grant.Body, "scope"), Scopes.Dispatch) && !EstateModel.Expired(grant, record.AtTime) &&
                EstateModel.BodyString(grant, "expires") is not null && !model.Trust.GrantClosedFor(grant, record, anchorsCount: true))
                return grant;
        }

        return null;
    }

    /// <summary>
    /// Whether a dispatch grant in force for a principal at a time names a goal, the principal being a service principal whose only role is
    /// service and holding no other grant then: what lets a dispatcher take the goal's steps, each assignment then judged on its own
    /// (<see cref="Refusal"/>).
    /// </summary>
    internal static bool CoversGoal(EstateModel model, string principal, RecordEnvelope goal, string at, DateTimeOffset atTime)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        if (PrincipalAt(model, principal, at) is not { } record || EstateModel.BodyString(record, "kind") != "service" || !OnlyService(record)) return false;
        string identity = model.Chain(goal)[0];
        bool covered = false;
        foreach (string line in model.Authority.GrantLines)
        {
            if (model.Authority.At(line, at) is not { } grant || EstateModel.BodyString(grant, "grantee") != principal || EstateModel.Expired(grant, atTime)) continue;
            List<string> given = [.. PolicyRecords.Items(PolicyRecords.Property(grant.Body, "scope")).Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()!)];
            if (given.Any(s => s != Scopes.Dispatch)) return false;
            covered |= EstateModel.BodyString(grant, "expires") is not null && PolicyRecords.Items(PolicyRecords.Property(grant.Body, "bounds"))
                .Any(b => PolicyRecords.Text(b, "scope") == Scopes.Dispatch && PolicyRecords.Contains(PolicyRecords.Property(b, "goals"), identity));
        }

        return covered;
    }

    /// <summary>The revision of a principal's line that stood at a time: its latest record its author may write dated at or before it.</summary>
    private static RecordEnvelope? PrincipalAt(EstateModel model, string? principal, string at) =>
        principal is not null && model.ById.TryGetValue(principal, out RecordEnvelope? record) && record.Type == "principal" ? model.Authority.At(model.Chain(record)[0], at) : null;

    /// <summary>
    /// The records of a type that count by their signatures and anchors, whether or not this rule withholds them (<see cref="Withheld"/>):
    /// what the cap and the held step are read from, so that judging them reads the same before and after the loader leaves out what the
    /// rule withholds, and so that no file anyone can add, such as an unsigned assignment stated as the dispatcher's, consumes the cap.
    /// </summary>
    private static IEnumerable<RecordEnvelope> Signed(EstateModel model, string type) => model.Records.Where(r => r.Type == type && model.Trust.Counts(r));

    /// <summary>Whether an assignment's holder settled it: a result, counting by its signatures, on an attempt on it, neither written by a dispatcher.</summary>
    private static bool Settled(EstateModel model, RecordEnvelope held)
    {
        IReadOnlyList<string> chain = model.Chain(held);
        HashSet<string> attempts = [.. Signed(model, "attempt").Where(a => a.Subject is not null && chain.Contains(a.Subject.Id) && !IsDispatcher(model, a.By)).Select(a => a.Id)];
        return attempts.Count > 0 && Signed(model, "result").Any(r => r.Subject is not null && attempts.Contains(r.Subject.Id) && !IsDispatcher(model, r.By));
    }

    /// <summary>Whether a principal was a person, a human principal, as its line stood at a time.</summary>
    private static bool PersonAt(EstateModel model, string? principal, string at) => PrincipalAt(model, principal, at) is { } record && EstateModel.BodyString(record, "kind") == "human";

    /// <summary>
    /// The principals a goal's roster named for a role at an assignment's time: the goal's own roles at its revision then when they name the
    /// role; otherwise the roster the goal names, or the estate's, at its latest revision dated at or before the assignment.
    /// </summary>
    private static IReadOnlyList<string> RosterAt(EstateModel model, RecordEnvelope goal, string role, RecordEnvelope assignment)
    {
        (IReadOnlyList<string> principals, Routing.Source source) = Routing.Principals(model, goal, role);
        if (source.Roster is not { } current) return principals;
        string line = model.Chain(current)[0];
        RecordEnvelope? then = model.OfType("roster").Where(r => model.Chain(r)[0] == line && string.CompareOrdinal(r.At, assignment.At) <= 0)
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
        if (then is null) return [];
        return [.. PolicyRecords.Items(PolicyRecords.Property(then.Body, "roles")).Where(r => PolicyRecords.Text(r, "role") == role)
            .SelectMany(r => PolicyRecords.Items(PolicyRecords.Property(r, "principals"))).Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!)];
    }

    /// <summary>
    /// The records a dispatcher wrote that count for nothing, which the loader leaves out of every rule: its assignments outside its
    /// dispatch authority (<see cref="Refusal"/>), and every attempt, progress claim and result it wrote, since a dispatcher never works a
    /// step. Judged once the counting records are known, since authority reads them.
    /// </summary>
    internal static IReadOnlySet<string> Withheld(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var withheld = new HashSet<string>(StringComparer.Ordinal);
        var dispatchers = new HashSet<string>(model.Authority.GrantLines.Select(model.Authority.Current).OfType<RecordEnvelope>()
            .Where(g => IsDispatchGrant(model, g)).Select(g => EstateModel.BodyString(g, "grantee")).OfType<string>(), StringComparer.Ordinal);
        if (dispatchers.Count == 0) return withheld;
        foreach (RecordEnvelope record in model.CountingRecords.Where(r => dispatchers.Contains(r.By) && r.Format >= 3))
        {
            if (record.Type is "attempt" or "result" or "work-progress") withheld.Add(record.Id);
            else if (record.Type == "assignment" && Refusal(model, record, model.ById.TryGetValue(record.Subject?.Id ?? string.Empty, out RecordEnvelope? order) && order.Type == "work-order" ? order : null) is not null)
                withheld.Add(record.Id);
        }

        return withheld;
    }

    /// <summary>The error on an attempt, progress claim or result a dispatcher wrote: it never works a step.</summary>
    internal static void RefuseWork(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Format >= 3 && IsDispatcher(context.Model, context.Record.By))
            context.Error("dispatch.work", $"Principal {context.Record.By} is a dispatcher, which records the assignments of unattended dispatch and never works a step: this {context.Record.Type} counts for nothing.");
    }

    /// <summary>Whether a principal is a dispatcher: it holds, or held, a dispatch grant its author may write.</summary>
    internal static bool IsDispatcher(EstateModel model, string? principal) =>
        principal is not null && model.Authority.GrantLines.Any(line => model.Authority.Current(line) is { } grant && EstateModel.BodyString(grant, "grantee") == principal &&
            IsDispatchGrant(model, grant));

    /// <summary>
    /// Why an assignment a dispatcher records is outside its authority, or null when it is within it: no dispatch grant in force at its
    /// time; a dispatcher that holds another role or another grant then; a work order that is a step of no goal the grant names; an
    /// assignment to the dispatcher itself; a step whose role is reserved; the grant's daily cap reached, counting the dispatcher's assignments dated that UTC day up to this one; or,
    /// for a step another assignment still holds, a reassignment the bound does not allow (<see cref="ReassignmentRefusal"/>).
    /// </summary>
    internal static string? Refusal(EstateModel model, RecordEnvelope assignment, RecordEnvelope? order)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        string by = assignment.By;
        if (GrantAt(model, by, assignment) is not { } grant)
            return "it has no dispatch grant in force at the assignment's time: one naming it, unexpired then and closed by no closing that does not witness it";
        if (PrincipalAt(model, by, assignment.At) is not { } principal || EstateModel.BodyString(principal, "kind") != "service" || !OnlyService(principal))
            return "a dispatcher is a service principal whose only role is service, and this one's record says otherwise at the assignment's time";
        if (OtherGrant(model, by, assignment, dispatch: false) is { } other)
            return $"a dispatcher holds no other authority, and grant {other.Id} gives it another scope at the assignment's time";
        JsonElement bound = PolicyRecords.Items(PolicyRecords.Property(grant.Body, "bounds")).FirstOrDefault(b => PolicyRecords.Text(b, "scope") == Scopes.Dispatch);
        if (bound.ValueKind != JsonValueKind.Object) return $"grant {grant.Id} has no dispatch bound";
        if (order is null) return "it assigns no work order";
        List<string> named = [.. PolicyRecords.Items(PolicyRecords.Property(bound, "goals")).Where(g => g.ValueKind == JsonValueKind.String).Select(g => g.GetString()!)];
        IReadOnlyList<RecordEnvelope> goals = Routing.GoalsOf(model, order, assignment);
        if (!goals.Any(g => named.Contains(model.Chain(g)[0], StringComparer.Ordinal)))
            return $"work order {EstateModel.BodyString(order, "key") ?? order.Id} is a step of no goal grant {grant.Id} names ({(named.Count == 0 ? "none" : string.Join(", ", named))})";
        if (EstateModel.BodyString(assignment, "contributor") == by)
            return "a dispatcher never assigns a step to itself: it holds no authority to do a step's work";
        // The role of the work order's revision the assignment pins, or for a run's step the role its flow gives it.
        string? role = FlowRuns.StepOf(assignment) is null ? Routing.RoleOf(order) : Routing.RoleOf(model, assignment);
        if (role is null || Routing.ReservedAction(role) is not null)
            return $"the step's role, {role ?? "unresolved"}, is a reserved action, which dispatch never assigns; it waits on a person";
        long cap = PolicyRecords.Property(bound, "maxAssignmentsPerDay").TryGetInt64Safe(out long limit) ? limit : 0;
        string day = assignment.At.Length >= 10 ? assignment.At[..10] : assignment.At;
        int count = Signed(model, "assignment").Count(a => a.By == by && a.At.StartsWith(day, StringComparison.Ordinal) && (a.Id == assignment.Id || GoalRules.Before(a, assignment)));
        if (count > cap)
            return $"grant {grant.Id} caps dispatch at {cap.ToString(CultureInfo.InvariantCulture)} assignments a day, and this is assignment {count.ToString(CultureInfo.InvariantCulture)} on {day}";
        return ReassignmentRefusal(model, assignment, order, goals.Where(g => named.Contains(model.Chain(g)[0], StringComparer.Ordinal)).ToList(), role, bound);
    }

    /// <summary>
    /// Why a dispatcher's assignment of a step another assignment still holds is outside the bound for reassignment after a lapse
    /// (design/events-and-presence.md § 10; design/goals.md § 6, step 4), or null when the step is held by none or the reassignment is
    /// within the bound. The step is held when the latest earlier assignment of the same work order and step, not stranded, has no result
    /// on any of its attempts. A step a person holds is never reassigned. Reassigning another needs the bound's reassign, and goes to a
    /// principal a goal's roster named for the step's role at the assignment's time, never the holder it replaces, a person, or a role the
    /// roster gave a person; the dispatcher itself is refused before (<see cref="Refusal"/>).
    /// </summary>
    private static string? ReassignmentRefusal(EstateModel model, RecordEnvelope assignment, RecordEnvelope order, IReadOnlyList<RecordEnvelope> goals, string role, JsonElement bound)
    {
        IReadOnlyList<string> orders = model.Chain(model.Latest(order));
        RecordEnvelope? held = Signed(model, "assignment")
            .Where(a => a.Id != assignment.Id && a.Subject is not null && orders.Contains(a.Subject.Id) && FlowRuns.StepOf(a) == FlowRuns.StepOf(assignment) &&
                GoalRules.Before(a, assignment) && !Routing.Stranded(model, a, assignment))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();
        if (held is null || Settled(model, held)) return null;
        string? contributor = EstateModel.BodyString(assignment, "contributor");
        string? holder = EstateModel.BodyString(held, "contributor");
        if (PersonAt(model, holder, assignment.At))
            return $"assignment {held.Id} is a person's, and a step a person holds is never reassigned by dispatch";
        if (PolicyRecords.Property(bound, "reassign").ValueKind != JsonValueKind.True)
            return $"assignment {held.Id} still holds the step, and the grant's bound does not cover reassignment after a lapse";
        if (contributor == holder) return "a reassignment after a lapse goes to the roster's next principal, not the holder it replaces";
        if (PersonAt(model, contributor, assignment.At)) return "a reassignment after a lapse never goes to a person: a person takes a step over by a hand-over they record";
        List<string> roster = [.. goals.SelectMany(g => RosterAt(model, g, role, assignment)).Distinct(StringComparer.Ordinal)];
        if (roster.Any(p => PersonAt(model, p, assignment.At))) return $"the roster gives role {role} to a person, and a person's step is never reassigned by dispatch";
        if (contributor is null || !roster.Contains(contributor, StringComparer.Ordinal))
            return $"a reassignment after a lapse goes to a principal the roster names for role {role}, and {contributor ?? "no contributor"} is not one";
        return null;
    }
}
