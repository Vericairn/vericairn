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
/// never the dispatcher itself, the holder it replaces, a person, or a role the roster gives a person. Anything else the dispatcher
/// assigns is refused, and no other record type takes this scope.
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
        if (grantee is not null && context.Model.CurrentPrincipal(grantee) is { } principal &&
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
        if (model.CurrentPrincipal(principal) is not { } record || EstateModel.BodyString(record, "kind") != "service" || !OnlyService(record)) return false;
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

    /// <summary>Whether a principal is a dispatcher: it holds, or held, a dispatch grant its author may write.</summary>
    internal static bool IsDispatcher(EstateModel model, string? principal) =>
        principal is not null && model.Authority.GrantLines.Any(line => model.Authority.Current(line) is { } grant && EstateModel.BodyString(grant, "grantee") == principal &&
            IsDispatchGrant(model, grant));

    /// <summary>
    /// Why an assignment a dispatcher records is outside its authority, or null when it is within it: no dispatch grant in force at its
    /// time; a dispatcher that holds another role or another grant then; a work order that is a step of no goal the grant names; a step
    /// whose role is reserved; the grant's daily cap reached, counting the dispatcher's assignments dated that UTC day up to this one; or,
    /// for a step another assignment still holds, a reassignment the bound does not allow (<see cref="ReassignmentRefusal"/>).
    /// </summary>
    internal static string? Refusal(EstateModel model, RecordEnvelope assignment, RecordEnvelope? order)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        string by = assignment.By;
        if (GrantAt(model, by, assignment) is not { } grant)
            return "it has no dispatch grant in force at the assignment's time: one naming it, unexpired then and closed by no closing that does not witness it";
        if (model.PrincipalAt(by, assignment) is not { } principal || EstateModel.BodyString(principal, "kind") != "service" || !OnlyService(principal))
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
        string? role = Routing.RoleOf(model, assignment);
        if (role is null || Routing.ReservedAction(role) is not null)
            return $"the step's role, {role ?? "unresolved"}, is a reserved action, which dispatch never assigns; it waits on a person";
        long cap = PolicyRecords.Property(bound, "maxAssignmentsPerDay").TryGetInt64Safe(out long limit) ? limit : 0;
        string day = assignment.At.Length >= 10 ? assignment.At[..10] : assignment.At;
        int count = model.OfType("assignment").Count(a => a.By == by && a.At.StartsWith(day, StringComparison.Ordinal) && (a.Id == assignment.Id || GoalRules.Before(a, assignment)));
        if (count > cap)
            return $"grant {grant.Id} caps dispatch at {cap.ToString(CultureInfo.InvariantCulture)} assignments a day, and this is assignment {count.ToString(CultureInfo.InvariantCulture)} on {day}";
        return ReassignmentRefusal(model, assignment, order, goals.Where(g => named.Contains(model.Chain(g)[0], StringComparer.Ordinal)).ToList(), role, bound);
    }

    /// <summary>
    /// Why a dispatcher's assignment of a step another assignment still holds is outside the bound for reassignment after a lapse
    /// (design/events-and-presence.md § 10; design/goals.md § 6, step 4), or null when the step is held by none or the reassignment is
    /// within the bound. The step is held when the latest earlier assignment of the same work order and step, not stranded, has no result
    /// on any of its attempts. Reassigning it needs the bound's reassign, and goes to a principal a goal's roster names for the step's
    /// role, never the dispatcher itself, the holder it replaces, a person, or a role the roster gives a person.
    /// </summary>
    private static string? ReassignmentRefusal(EstateModel model, RecordEnvelope assignment, RecordEnvelope order, IReadOnlyList<RecordEnvelope> goals, string role, JsonElement bound)
    {
        IReadOnlyList<string> orders = model.Chain(model.Latest(order));
        RecordEnvelope? held = model.OfType("assignment")
            .Where(a => a.Id != assignment.Id && a.Subject is not null && orders.Contains(a.Subject.Id) && FlowRuns.StepOf(a) == FlowRuns.StepOf(assignment) &&
                GoalRules.Before(a, assignment) && !Routing.Stranded(model, a, assignment))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();
        if (held is null || WorkView.AttemptsOf(model, held).Any(attempt => WorkView.ResultOf(model, attempt) is not null)) return null;
        string? contributor = EstateModel.BodyString(assignment, "contributor");
        string? holder = EstateModel.BodyString(held, "contributor");
        if (PolicyRecords.Property(bound, "reassign").ValueKind != JsonValueKind.True)
            return $"assignment {held.Id} still holds the step, and the grant's bound does not cover reassignment after a lapse";
        if (contributor == assignment.By) return "a reassignment after a lapse never goes to the dispatcher itself";
        if (contributor == holder) return "a reassignment after a lapse goes to the roster's next principal, not the holder it replaces";
        if (GoalRules.IsPerson(model, contributor)) return "a reassignment after a lapse never goes to a person: a person takes a step over by a hand-over they record";
        List<string> roster = [.. goals.SelectMany(g => Routing.Principals(model, g, role).Principals).Distinct(StringComparer.Ordinal)];
        if (roster.Any(p => GoalRules.IsPerson(model, p))) return $"the roster gives role {role} to a person, and a person's step is never reassigned by dispatch";
        if (contributor is null || !roster.Contains(contributor, StringComparer.Ordinal))
            return $"a reassignment after a lapse goes to a principal the roster names for role {role}, and {contributor ?? "no contributor"} is not one";
        return null;
    }
}
