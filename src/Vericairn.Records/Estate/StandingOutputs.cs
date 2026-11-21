// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// Standing outputs (from 0.9.4, GOAL-08; the estate's design/standing-agents.md §§ 8 to 10, revised by design/flow.md): every output of
/// a standing agent's occurrence is a record another principal disposes of. A possibility, an ask or a publication request names what
/// raised it in <c>raisedBy</c>: the standing agent's goal, the occurrence's key, the assignment that carries it and, for an agent, the run
/// that recorded it, and only that assignment's contributor records it. A possibility carries a fingerprint: a second open possibility of
/// the same agent with the same fingerprint is refused, and raising a declined one again names it with new evidence. An occurrence records
/// at most its plan's possibilities and drafts, extras joining one summary possibility; open outputs past the plan's cap defer dispatch.
/// No standing agent adopts, declines, parks, answers, withdraws or closes its own output, no standing agent answers the runtime's own asks
/// about it, its failure pause's among them, and no agent disposes of a held finding; no author adopts its own possibility. Who counts as
/// the standing agent itself is every agent tied to its goal (<see cref="TieOf"/>), read from the records and never from a date. Every
/// revision is judged by the chain it revises, so what its own body leaves out stops no rule from applying.
/// </summary>
internal static partial class StandingOutputs
{
    /// <summary>The possibilities one occurrence records when its plan states none (§ 10).</summary>
    public const int DefaultPossibilities = 5;

    /// <summary>The possibilities with a draft work order one occurrence records when its plan states none (§ 10).</summary>
    public const int DefaultDrafts = 3;

    /// <summary>The open outputs past which a standing agent's dispatch defers when its plan states none (§ 10).</summary>
    public const int DefaultOpen = 10;

    /// <summary>The code of the checks the write boundary refuses for a new record, read as the records stand.</summary>
    public const string BoundaryCode = "standing.output-now";

    /// <summary>The record types a standing agent's outputs are.</summary>
    private static readonly string[] OutputTypes = ["possibility", "ask", "operator-request"];

    /// <summary>The relations one possibility names another by.</summary>
    public static readonly string[] Relations = ["duplicate-of", "supersedes"];

    /// <summary>The words the runtime's own asks about a goal open with: required attention, the failure pause's among them, renewal and a period cap.</summary>
    private static readonly string[] RuntimeWords = [NextEligible.AttentionQuestion, StandingAgents.RenewalAsk, StandingAgents.PeriodAsk];

    /// <summary>A possibility's fields new in 0.9.4, which no earlier estate wrote.</summary>
    private static readonly string[] PossibilityFields = ["raisedBy", "fingerprint", "sources", "evidence", "reviewTriggers", "links", "draft", "disclosure", "extras", "raisesAgain"];

    /// <summary>What a standing output names as what raised it.</summary>
    /// <param name="Goal">The standing agent's goal, its first revision.</param>
    /// <param name="Occurrence">The occurrence key.</param>
    /// <param name="Assignment">The assignment that carries the occurrence.</param>
    /// <param name="Run">The agent run that recorded it, when an agent did.</param>
    public sealed record RaisedBy(string Goal, string Occurrence, string Assignment, string? Run);

    /// <summary>A standing plan's output caps: possibilities and drafts per occurrence, and open outputs before dispatch defers.</summary>
    /// <param name="Possibilities">The possibilities one occurrence records, the summary possibility aside.</param>
    /// <param name="Drafts">How many of them carry a draft work order.</param>
    /// <param name="Open">The open outputs past which dispatch defers.</param>
    public sealed record OutputCaps(int Possibilities, int Drafts, int Open);

    /// <summary>A record's raisedBy, or null when it names none.</summary>
    public static RaisedBy? Of(RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        JsonElement raised = TJson.Object(record.Body, "raisedBy");
        return TJson.Text(raised, "goal") is string goal && TJson.Text(raised, "occurrence") is string occurrence && TJson.Text(raised, "assignment") is string assignment
            ? new RaisedBy(goal, occurrence, assignment, TJson.Text(raised, "run"))
            : null;
    }

    /// <summary>
    /// True when a record states a format before 3 and revises a record of its type that states 3 or later: a revision cannot leave the
    /// standing rules behind by stating the format before them, so its chain's rules judge it as they judge a format-3 revision.
    /// </summary>
    public static bool RevisesLaterFormat(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Format < 3 && context.Find(context.Record.Supersedes) is { } previous && previous.Type == context.Record.Type && previous.Format >= 3;
    }

    /// <summary>
    /// The first revision of a record's chain of its own type: the record itself when it supersedes none of its type. Its raisedBy is the
    /// chain's, whatever a later revision's body says.
    /// </summary>
    public static RecordEnvelope FirstOf(EstateModel model, RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(record);
        RecordEnvelope current = record;
        var seen = new HashSet<string>(StringComparer.Ordinal) { current.Id };
        while (current.Supersedes is string prior && model.ById.TryGetValue(prior, out RecordEnvelope? earlier) && earlier.Type == record.Type && seen.Add(earlier.Id))
        {
            current = earlier;
        }

        return current;
    }

    /// <summary>A standing plan's output caps: its plan.standing.outputs, or five possibilities, three drafts and ten open outputs.</summary>
    public static OutputCaps CapsOf(RecordEnvelope goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        JsonElement outputs = TJson.Object(StandingAgents.Plan(goal), "outputs");
        return new OutputCaps(Number(outputs, "possibilities") ?? DefaultPossibilities, Number(outputs, "drafts") ?? DefaultDrafts, Number(outputs, "open") ?? DefaultOpen);
    }

    /// <summary>
    /// The revision of a standing agent's goal in force when a record was written, whose caps judge it: the latest revision written before it
    /// that an activation counting before it made active, else the latest revision written before it, else the revision named. A later
    /// revision, such as a sponsor's lowering the caps, judges only what is written after it is in force.
    /// </summary>
    public static RecordEnvelope InForce(EstateModel model, RecordEnvelope goal, RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(record);
        List<RecordEnvelope> written = [.. model.Chain(model.Latest(goal)).Select(model.ById.GetValueOrDefault).OfType<RecordEnvelope>()
            .Where(r => r.Type == "goal" && GoalRules.Before(r, record))];
        return written.LastOrDefault(r => GoalRules.Statuses(model, r, record).Any(s => EstateModel.BodyString(s, "status") == "active")) ?? written.LastOrDefault() ?? goal;
    }

    /// <summary>Validates a plan's outputs caps: possibilities per occurrence, drafts among them and open outputs.</summary>
    public static void ValidateCaps(BodyReader? outputs)
    {
        if (outputs is null)
        {
            return;
        }

        long? possibilities = outputs.Number("possibilities", false, 1, 20);
        long? drafts = outputs.Number("drafts", false, 0, 20);
        outputs.Number("open", false, 1, 1000);
        outputs.NoOtherKeys();
        if (drafts is not null && drafts > (possibilities ?? DefaultPossibilities))
        {
            outputs.Error("standing.outputs", "drafts", "An occurrence's drafts are among its possibilities, so their cap is no larger than the possibilities' cap.");
        }
    }

    /// <summary>
    /// The open outputs of a standing agent, judged from the records before a record when one is given: its possibilities still open, its
    /// asks still waiting and its publication requests not closed, each read at its revision as it stood then.
    /// </summary>
    public static int OpenCount(EstateModel model, RecordEnvelope goal, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        string identity = model.ById.ContainsKey(goal.Id) ? model.Chain(model.Latest(goal))[0] : goal.Id;
        int open = 0;
        foreach (RecordEnvelope first in Raised(model, identity, before))
        {
            if (AsOf(model, first, before) is not RecordEnvelope standing)
            {
                continue;
            }

            // An ask is answered only by a decision no agent tied to the goal recorded (Counting), as Asks.State reads it; judged before a
            // record, by the ties written before it, so a later tie makes no earlier record wrong.
            bool counts = standing.Type switch
            {
                "possibility" => EstateModel.BodyString(standing, "disposition") == "open",
                "ask" => EstateModel.BodyString(standing, "state") is "open" or "notified"
                    && !Counting(model, standing, model.DecisionsAnswering(model.Chain(standing)), before).Any(d => before is null || GoalRules.Before(d, before)),
                "operator-request" => !OperatorRequests.Closures(model, standing).Any(c => before is null || GoalRules.Before(c, before)),
                _ => false,
            };
            open += counts ? 1 : 0;
        }

        return open;
    }

    /// <summary>
    /// The first revisions of every output a standing agent raised, oldest first, judged from the records before a record when one is
    /// given: possibilities, asks and operator requests whose raisedBy names its goal.
    /// </summary>
    public static IReadOnlyList<RecordEnvelope> Raised(EstateModel model, string identity, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        return [.. OutputTypes.SelectMany(model.OfType)
            .Where(r => (r.Supersedes is null || !model.ById.TryGetValue(r.Supersedes, out RecordEnvelope? prior) || prior.Type != r.Type) && Of(r)?.Goal == identity && (before is null || GoalRules.Before(r, before)))
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The earlier possibilities a standing agent raised that are open or declined now, at their current revisions, oldest first: what an
    /// occurrence's brief names so that its agent neither repeats an open one nor raises a declined one again without new evidence.
    /// </summary>
    public static IReadOnlyList<RecordEnvelope> OpenAndDeclined(EstateModel model, RecordEnvelope goal)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        string identity = model.Chain(model.Latest(goal))[0];
        return [.. Raised(model, identity).Where(r => r.Type == "possibility").Select(model.Latest)
            .Where(p => EstateModel.BodyString(p, "disposition") is "open" or "declined")];
    }

    /// <summary>A record's revision as it stood before another record: the latest revision of its chain written before it; null when none was.</summary>
    public static RecordEnvelope? AsOf(EstateModel model, RecordEnvelope first, RecordEnvelope? before)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(first);
        if (before is null)
        {
            return model.Latest(first);
        }

        if (!GoalRules.Before(first, before))
        {
            return null;
        }

        RecordEnvelope current = first;
        var seen = new HashSet<string>(StringComparer.Ordinal) { current.Id };
        while (model.SupersededBy.TryGetValue(current.Id, out string? next) && model.ById.TryGetValue(next, out RecordEnvelope? successor) && successor.Type == first.Type
            && seen.Add(successor.Id) && GoalRules.Before(successor, before))
        {
            current = successor;
        }

        return current;
    }

    /// <summary>
    /// Validates a record's raisedBy (§ 8): the standing agent's goal by its first revision, the occurrence key, the assignment of that goal's
    /// work order that names the key and whose contributor records this output, and, for an agent, the run of that assignment it records it
    /// in, in flight when it does; an occurrence already settled records no further output. A revision is judged against the chain it
    /// revises: it keeps exactly the raisedBy its first revision names, or names none when that names none, and what is returned is the
    /// chain's, so leaving raisedBy out of a revision stops no standing rule from applying to it.
    /// </summary>
    public static RaisedBy? ValidateRaisedBy(ValidationContext context, BodyReader? raised)
    {
        ArgumentNullException.ThrowIfNull(context);
        string? goalId = null, key = null, assignmentId = null, runId = null;
        if (raised is not null)
        {
            goalId = raised.Ulid("goal", true);
            key = raised.Text("occurrence", true, 600);
            assignmentId = raised.Ulid("assignment", true);
            runId = raised.Ulid("run", false);
            raised.NoOtherKeys();
        }

        if (context.Find(context.Record.Supersedes) is { } previous && previous.Type == context.Record.Type)
        {
            // The checks below were made when the output was first recorded; a revision changes nothing of what raised it.
            RecordEnvelope first = FirstOf(context.Model, previous);
            JsonElement original = TJson.Object(first.Body, "raisedBy");
            if (!Same(original, raised?.Element ?? default))
            {
                context.Error("standing.raised-by", original.ValueKind == JsonValueKind.Undefined
                    ? "A revision of a record no standing agent raised names no raisedBy: a standing output is raised as one, by its occurrence's contributor."
                    : $"A revision of a standing output keeps the raisedBy its first revision {first.Id} names, exactly: what raised it does not change, and leaving it out stops no rule from applying.");
            }

            return Of(first);
        }

        if (goalId is null || key is null || assignmentId is null)
        {
            return null;
        }

        var named = new RaisedBy(goalId, key, assignmentId, runId);
        RecordEnvelope? goal = context.Find(goalId);
        if (goal is not { Type: "goal" } || context.Model.Chain(goal)[0] != goal.Id || !StandingAgents.IsStanding(context.Model.Latest(goal)))
        {
            context.Error("standing.raised-by", $"raisedBy.goal names a standing agent by its goal's first revision; {goalId} is not one.");
            return named;
        }

        RecordEnvelope? assignment = context.Find(assignmentId);
        RecordEnvelope? order = FlowRuns.Work(context.Model, context.Model.Latest(goal));
        if (assignment is not { Type: "assignment" } || StandingAgents.KeyOf(assignment) != key || order is null || assignment.Subject is null
            || !context.Model.Chain(order).Contains(assignment.Subject.Id) || !GoalRules.Before(assignment, context.Record))
        {
            context.Error("standing.raised-by", $"raisedBy.assignment names the assignment of standing agent {goalId}'s work order that carries occurrence {key}, recorded before this output.");
            return named;
        }

        string? contributor = EstateModel.BodyString(assignment, "contributor");
        if (contributor != context.Record.By)
        {
            context.Error("standing.raised-by", $"Only the contributor of assignment {assignment.Id}, which carries occurrence {key}, records its outputs; {context.Record.By} is not it.");
        }

        bool person = GoalRules.IsPerson(context.Model, contributor);
        if (runId is null && !person)
        {
            context.Error("standing.raised-by", $"An agent's output names the run it records it in: raisedBy.run, a run of assignment {assignment.Id}.");
        }
        else if (runId is not null)
        {
            IReadOnlyList<string> chain = context.Model.Chain(context.Model.Latest(assignment));
            AgentRuns.Run? run = AgentRuns.All(context.Model, context.Record).FirstOrDefault(r => r.Id == runId);
            if (run is null || run.Assignment is null || !chain.Contains(run.Assignment) || run.Principal != context.Record.By)
            {
                context.Error("standing.raised-by", $"raisedBy.run names a run of assignment {assignment.Id} by {context.Record.By}, started before this output; {runId} is not one.");
            }
            else if (!run.InFlight)
            {
                context.Error("standing.raised-by", $"Run {runId} had ended ({run.Status}) before this output was recorded: an agent records its outputs while its run is in flight.");
            }
        }

        if (StandingAgents.Settled(context.Model, key, context.Record) is RecordEnvelope settled)
        {
            context.Error("standing.raised-by", $"Occurrence {key} was settled by result {settled.Id} before this output: an occurrence's outputs are recorded before its result, and again only when a review returns it.");
        }

        return named;
    }

    /// <summary>
    /// Validates what a standing agent's possibility adds to a possibility (§§ 8 and 10): on its first revision it is open, carries a
    /// fingerprint and at least one source, stays within its occurrence's caps, is no second open possibility of the agent with that
    /// fingerprint, and names a declined one it raises again with new evidence. A revision keeps its fingerprint, counts against the caps
    /// for what it adds, and one that disposes of it is never the agent's own: not its author and no agent tied to the goal (<see cref="TieOf"/>,
    /// reported as <see cref="Judged"/> says). Every possibility's held finding (§§ 8 and 9) is disposed of by a
    /// person only, adopted only by an operator who is one, and its hold ends only by an operator who is a person.
    /// </summary>
    public static void ValidatePossibility(ValidationContext context, RaisedBy? raised, string? disposition)
    {
        ArgumentNullException.ThrowIfNull(context);
        RecordEnvelope record = context.Record;
        if (context.Find(record.Supersedes) is { Type: "possibility" } previous)
        {
            RecordEnvelope first = FirstOf(context.Model, previous);
            bool disposes = disposition != EstateModel.BodyString(previous, "disposition") || !Same(TJson.Object(record.Body, "adoptedAs"), TJson.Object(previous.Body, "adoptedAs"));
            ValidateHeld(context, previous, first, disposition, disposes);
            if (raised is not null)
            {
                ValidateRevision(context, previous, first, raised, disposes);
            }

            return;
        }

        if (raised is null)
        {
            return;
        }

        string? fingerprint = EstateModel.BodyString(record, "fingerprint");
        if (disposition != "open")
        {
            context.Error("standing.possibility", "A standing agent's possibility is raised open; another principal disposes of it.");
        }

        if (fingerprint is null)
        {
            context.Error("standing.possibility", "A standing agent's possibility carries a fingerprint, the flow's rule for what makes two of them the same.");
        }

        if (!TJson.Array(record.Body, "sources").Any())
        {
            context.Error("standing.possibility", "A standing agent's possibility cites at least one source: where it was found, or how it reproduces.");
        }

        ValidateOutputCaps(context, raised, null, null);
        if (fingerprint is null)
        {
            return;
        }

        // A second open possibility of the agent with the same fingerprint is refused; a declined one is raised again only by naming it.
        List<RecordEnvelope> same = [.. Raised(context.Model, raised.Goal, record).Where(r => r.Type == "possibility" && EstateModel.BodyString(r, "fingerprint") == fingerprint)];
        if (same.Select(r => AsOf(context.Model, r, record)).FirstOrDefault(r => r is not null && EstateModel.BodyString(r, "disposition") == "open") is RecordEnvelope open)
        {
            context.Error("standing.duplicate-output", $"Possibility {context.Model.Chain(open)[0]} of this standing agent with fingerprint '{fingerprint}' is open: a second open one is refused; add to that one instead, by a revision that keeps its raisedBy and fingerprint.");
        }

        List<RecordEnvelope> declined = [.. same.Where(r => AsOf(context.Model, r, record) is RecordEnvelope standing && EstateModel.BodyString(standing, "disposition") == "declined")];
        string? again = EstateModel.BodyString(record, "raisesAgain");
        RecordEnvelope? named = again is null ? null : context.Find(again);
        if (again is not null && (named is null || !declined.Any(d => context.Model.Chain(context.Model.Latest(d)).Contains(named.Id))))
        {
            context.Error("standing.raises-again", $"raisesAgain names a declined possibility of this standing agent with fingerprint '{fingerprint}'; {again} is not one.");
        }
        else if (declined.Count > 0 && again is null)
        {
            context.Error("standing.raises-again", $"Possibility {declined[^1].Id} of this standing agent with fingerprint '{fingerprint}' was declined: raising it again names it in raisesAgain, with the new evidence.");
        }
        else if (again is not null && !TJson.Array(record.Body, "evidence").Any())
        {
            context.Error("standing.raises-again", "Raising a declined possibility again states the new evidence for it.");
        }
    }

    /// <summary>
    /// The caps of an occurrence (§ 10), as the goal revision in force when the output was first raised states them: its plan's
    /// possibilities, drafts among them and one summary possibility that the extras join. Each other possibility of the occurrence counts
    /// at its revision as it stood before this record; a first revision counts for all it carries, and a revision for what it adds: a draft,
    /// extras, or a summary possibility that drops its extras and so joins the possibilities.
    /// </summary>
    private static void ValidateOutputCaps(ValidationContext context, RaisedBy raised, RecordEnvelope? previous, RecordEnvelope? first)
    {
        RecordEnvelope record = context.Record;
        bool summary = Has(record, "extras");
        bool addsSummary = summary && (previous is null || !Has(previous, "extras"));
        bool addsPossibility = !summary && (previous is null || Has(previous, "extras"));
        bool addsDraft = Has(record, "draft") && (previous is null || !Has(previous, "draft"));
        if ((!addsSummary && !addsPossibility && !addsDraft) || context.Find(raised.Goal) is not { Type: "goal" } goal)
        {
            return;
        }

        OutputCaps caps = CapsOf(InForce(context.Model, goal, first ?? record));
        string own = first?.Id ?? record.Id;
        List<RecordEnvelope> others = [.. Raised(context.Model, raised.Goal, record)
            .Where(r => r.Type == "possibility" && r.Id != own && Of(r)?.Occurrence == raised.Occurrence)
            .Select(r => AsOf(context.Model, r, record)).OfType<RecordEnvelope>()];
        if (addsSummary && others.Any(r => Has(r, "extras")))
        {
            context.Error("standing.output-cap", $"Occurrence {raised.Occurrence} has its summary possibility already: its extras join that one, which another principal disposes of.");
        }
        else if (addsPossibility && others.Count(r => !Has(r, "extras")) >= caps.Possibilities)
        {
            context.Error("standing.output-cap", $"Occurrence {raised.Occurrence} has recorded {caps.Possibilities.ToString(CultureInfo.InvariantCulture)} possibilities, its cap: further ideas join one summary possibility, listed in its extras.");
        }
        else if (addsDraft && others.Count(r => Has(r, "draft")) >= caps.Drafts)
        {
            context.Error("standing.output-cap", $"Occurrence {raised.Occurrence} has recorded {caps.Drafts.ToString(CultureInfo.InvariantCulture)} possibilities with a draft work order, its cap: further proposed work joins its summary possibility.");
        }
    }

    /// <summary>A revision of a standing agent's possibility: its fingerprint kept, what it adds within the caps, and a disposition by its author or a tied agent refused.</summary>
    private static void ValidateRevision(ValidationContext context, RecordEnvelope previous, RecordEnvelope first, RaisedBy raised, bool disposes)
    {
        RecordEnvelope record = context.Record;
        if (EstateModel.BodyString(record, "fingerprint") != EstateModel.BodyString(first, "fingerprint"))
        {
            context.Error("standing.raised-by", $"A revision of a standing agent's possibility keeps the fingerprint its first revision {first.Id} names.");
        }

        ValidateOutputCaps(context, raised, previous, first);
        if (!disposes)
        {
            return;
        }

        string by = record.By;
        string? title = EstateModel.BodyString(first, "title");
        if (by == first.By)
        {
            context.Error("standing.own-output", $"Possibility {first.Id} ({title}) was raised by {by}: no standing agent adopts, declines or parks its own output; another principal disposes of it.");
            return;
        }

        if (context.Find(raised.Goal) is not { Type: "goal" } goal || TieOf(context.Model, goal, by) is not Tie tie)
        {
            return;
        }

        (string code, string message) = Judged(tie, by, raised.Goal, Later(record, previous), "no standing agent adopts, declines or parks its own output, so another principal disposes of it",
            "the write boundary refuses a new one");
        if (code == BoundaryCode)
        {
            context.Findings.Info(code, context.Location, message);
        }
        else
        {
            context.Error(code, message);
        }
    }

    /// <summary>
    /// How an agent is tied to a standing agent's goal (§ 8), so that the rules against the agent disposing of or answering its own outputs
    /// refuse it: Carried when an assignment of the goal's work names it as contributor; else Role, the role of the goal's work step that a
    /// revision of the goal's own roles, or a revision of the roster it reads, named it for. Records are every record that ties it.
    /// </summary>
    /// <param name="Carried">True when an assignment of the goal's work names the agent as contributor.</param>
    /// <param name="Role">The role it was named for, when it carried none.</param>
    /// <param name="Records">Every record that ties it: those assignments, and the goal and roster revisions naming it for the role.</param>
    public sealed record Tie(bool Carried, string? Role, IReadOnlyList<RecordEnvelope> Records);

    /// <summary>Each loaded model's ties, by the goal's first revision and then the agent: a model is one read of the records.</summary>
    private static readonly ConditionalWeakTable<EstateModel, ConcurrentDictionary<string, IReadOnlyDictionary<string, Tie>>> TiesRead = [];

    /// <summary>
    /// How an agent is tied to a standing agent's goal, read from the records themselves and never from a date the writer chooses or the
    /// roster as it stands: an agent is tied when any recorded assignment of the goal's work, in any revision of the goal and at any time,
    /// names it as contributor, or when any revision of the goal's role ever named it, in the goal's own roles or in any revision of the
    /// roster the goal reads when its own roles do not name the role. Both sets only grow: a later record ties an agent and none unties
    /// one. Null for a person, and for an agent nothing ties.
    /// </summary>
    public static Tie? TieOf(EstateModel model, RecordEnvelope goal, string by)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(by);
        if (GoalRules.IsPerson(model, by) || !model.ById.ContainsKey(goal.Id))
        {
            return null;
        }

        string identity = model.Chain(model.Latest(goal))[0];
        return TiesRead.GetValue(model, _ => new(StringComparer.Ordinal)).GetOrAdd(identity, _ => Ties(model, goal)).GetValueOrDefault(by);
    }

    /// <summary>Every agent tied to a standing agent's goal, with the records that tie it (<see cref="TieOf"/>).</summary>
    private static Dictionary<string, Tie> Ties(EstateModel model, RecordEnvelope goal)
    {
        List<RecordEnvelope> revisions = [.. model.Chain(model.Latest(goal)).Select(model.ById.GetValueOrDefault).OfType<RecordEnvelope>().Where(r => r.Type == "goal")];
        var carried = new Dictionary<string, List<RecordEnvelope>>(StringComparer.Ordinal);
        var named = new Dictionary<string, List<RecordEnvelope>>(StringComparer.Ordinal);
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        static void Add(Dictionary<string, List<RecordEnvelope>> ties, string principal, RecordEnvelope record)
        {
            if (!ties.TryGetValue(principal, out List<RecordEnvelope>? records))
            {
                records = [];
                ties[principal] = records;
            }

            records.Add(record);
        }

        void Name(IEnumerable<string> principals, string role, RecordEnvelope record)
        {
            foreach (string principal in principals)
            {
                roles.TryAdd(principal, role);
                Add(named, principal, record);
            }
        }

        var orders = new HashSet<string>(StringComparer.Ordinal);
        foreach (RecordEnvelope revision in revisions)
        {
            if (FlowRuns.Work(model, revision) is RecordEnvelope order)
            {
                orders.UnionWith(model.Chain(order));
            }
        }

        foreach (RecordEnvelope assignment in model.OfType("assignment"))
        {
            if (assignment.Subject is not null && orders.Contains(assignment.Subject.Id) && EstateModel.BodyString(assignment, "contributor") is string contributor)
            {
                Add(carried, contributor, assignment);
            }
        }

        // As Routing.Principals reads a role: the goal's own roles when they name it, else the roster; here every revision of each, ever written.
        foreach (string role in revisions.Select(r => StandingAgents.Resolve(model, r).Flow is { } flow && StandingAgents.WorkStep(flow) is { } step ? step.Role : null).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            bool roster = false;
            foreach (RecordEnvelope revision in revisions)
            {
                if (Routing.Named(revision.Body, role) is { } own)
                {
                    Name(own, role, revision);
                }
                else
                {
                    roster = true;
                }
            }

            if (roster)
            {
                foreach (RecordEnvelope record in model.OfType("roster"))
                {
                    Name(Routing.Named(record.Body, role) ?? [], role, record);
                }
            }
        }

        var ties = new Dictionary<string, Tie>(StringComparer.Ordinal);
        foreach (string principal in carried.Keys.Union(named.Keys, StringComparer.Ordinal))
        {
            bool carrying = carried.TryGetValue(principal, out List<RecordEnvelope>? assignments);
            List<RecordEnvelope> records = [.. assignments ?? [], .. named.GetValueOrDefault(principal) ?? []];
            ties[principal] = new Tie(carrying, carrying ? null : roles[principal], [.. records.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)]);
        }

        return ties;
    }

    /// <summary>The later of two records, by time and then identifier: what a record that names or revises the other cannot precede.</summary>
    private static RecordEnvelope Later(RecordEnvelope a, RecordEnvelope b) => GoalRules.Before(a, b) ? b : a;

    /// <summary>
    /// How a tied agent's disposal or answer is reported (§ 8): an error (standing.own-output) when a record that ties it was written before
    /// the later of the record judged and the record it answers or revises, which it cannot precede, so no date its writer chooses lowers
    /// it; otherwise, where only records written after it tie the agent, a report of the records as they stand now (standing.output-now),
    /// which the write boundary refuses for a new record, so that a later roster or assignment change turns no earlier record into an error
    /// and is never refused for one. Either way the agent is tied, whatever its record claims.
    /// </summary>
    private static (string Code, string Message) Judged(Tie tie, string by, string goal, RecordEnvelope pivot, string rule, string later)
    {
        string how = tie.Carried ? $"{by} carried occurrences of standing agent {goal}" : $"{by} was named for the role {tie.Role} of standing agent {goal}";
        RecordEnvelope earliest = tie.Records[0];
        return GoalRules.Before(earliest, pivot)
            ? ("standing.own-output", $"{how} ({earliest.Type} {earliest.Id}): {rule}.")
            : (BoundaryCode, $"{how}, as a record written after this one shows ({earliest.Type} {earliest.Id}): {rule}; {later}.");
    }

    /// <summary>
    /// The standing agent's goal, by its first revision, that guards an ask against the agent's own answer (§ 8): a standing agent's ask,
    /// by the raisedBy of its chain, or one of the runtime's own asks about a standing agent's goal (its renewal ask, a period cap's ask or
    /// its failure pause's ask, by its opening words and <see cref="StandingAgents.RuntimeAsk"/>). Null for every other ask, whose answers
    /// stay as they were.
    /// </summary>
    public static RecordEnvelope? GuardedBy(EstateModel model, RecordEnvelope ask)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(ask);
        RecordEnvelope first = FirstOf(model, ask);
        if (Of(first) is RaisedBy raised)
        {
            return model.ById.TryGetValue(raised.Goal, out RecordEnvelope? standing) && standing.Type == "goal" ? standing : null;
        }

        RecordEnvelope latest = model.Latest(ask);
        string question = EstateModel.BodyString(latest, "question") ?? string.Empty;
        if (!RuntimeWords.Any(p => question.StartsWith(p, StringComparison.Ordinal)) || EstateModel.BodyString(latest, "goal") is not string named
            || !model.ById.TryGetValue(named, out RecordEnvelope? goal) || goal.Type != "goal")
        {
            return null;
        }

        RecordEnvelope current = model.Latest(goal);
        return StandingAgents.IsStanding(current) && StandingAgents.RuntimeAsk(model, current, ask) && model.ById.TryGetValue(model.Chain(current)[0], out RecordEnvelope? identity) ? identity : null;
    }

    /// <summary>
    /// Why a decision that answers an ask a standing agent's goal guards (<see cref="GuardedBy"/>) answers nothing (§ 8: no standing agent
    /// answers its own output, and none ends its own failure pause), whatever its decidedBy names: it is recorded by the agent that raised
    /// a standing agent's ask (the author of its first revision, which names raisedBy), or by an agent tied to the goal (<see cref="TieOf"/>),
    /// read from the records themselves and never from the decision's date or the roster as it stands. The author of one of the runtime's
    /// asks, the principal whose goal step wrote it, is not refused for being its author: when nothing ties it, it records the person's
    /// decision as it records the person's answers everywhere. Since a tie only grows, a later roster or assignment change can make such a
    /// decision answer nothing and never makes one answer. The code is standing.own-output when the records show the agent raised the ask
    /// or was tied before the later of the decision and the ask revision it names, else standing.output-now (<see cref="Judged"/>). With
    /// <paramref name="asOf"/>, the estate as it stood before that record judges an earlier record, such as a run's intent: only ties
    /// written before it count, so that a later roster or assignment change makes no record already in the estate wrong. Null when it
    /// answers the ask as any other decision of the person asked does.
    /// </summary>
    public static (string Code, string Message)? SelfAnswer(EstateModel model, RecordEnvelope decision, RecordEnvelope ask, RecordEnvelope goal, RecordEnvelope? asOf = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(ask);
        ArgumentNullException.ThrowIfNull(goal);
        RecordEnvelope first = FirstOf(model, ask);
        string by = decision.By;
        const string Rule = "no standing agent answers its own ask or the runtime's asks about it, or ends its own pause, whatever decidedBy names: the person asked records the decision, or a principal nothing ties to the agent records it for them";
        if (Of(first) is not null && by == first.By)
        {
            return ("standing.own-output", $"Ask {first.Id} was raised by {by} as an output of standing agent {goal.Id}: {Rule}, so it answers nothing.");
        }

        return TieOf(model, goal, by) is Tie tie && (asOf is null || GoalRules.Before(tie.Records[0], asOf))
            ? Judged(tie, by, goal.Id, Later(decision, ask), $"{Rule}, so it answers nothing of ask {first.Id}", "the write boundary refuses a new one")
            : null;
    }

    /// <summary>
    /// The decisions that answer an ask, leaving out those <see cref="SelfAnswer"/> finds on an ask a standing agent's goal guards: what
    /// <see cref="Asks.State"/> and the open outputs read, so such a decision neither answers the ask, frees an open output nor ends a
    /// failure pause, even when it reached the records by hand with a date its writer chose. A roster or assignment change can only re-open
    /// an ask for the person this way, never close one. With <paramref name="asOf"/>, as the estate stood before that record.
    /// </summary>
    internal static List<RecordEnvelope> Counting(EstateModel model, RecordEnvelope ask, IEnumerable<RecordEnvelope> answers, RecordEnvelope? asOf = null)
    {
        List<RecordEnvelope> all = [.. answers];
        return all.Count == 0 || GuardedBy(model, ask) is not RecordEnvelope goal ? all : all.FindAll(d => SelfAnswer(model, d, ask, goal, asOf) is null);
    }

    /// <summary>
    /// Validates a decision's answer to an ask (§ 8): an answer <see cref="SelfAnswer"/> finds on an ask a standing agent's goal guards is an
    /// error (standing.own-output) where the records show its author raised the ask or was tied to the goal before it, and otherwise a report
    /// (standing.output-now) that the write boundary refuses for a new decision, so that a later roster or assignment change turns no
    /// earlier decision into an error. Answers to every other ask are judged as before.
    /// </summary>
    public static void ValidateAnswer(ValidationContext context, RecordEnvelope ask)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ask);
        if (GuardedBy(context.Model, ask) is not RecordEnvelope goal || SelfAnswer(context.Model, context.Record, ask, goal) is not { } refused)
        {
            return;
        }

        if (refused.Code == BoundaryCode)
        {
            context.Findings.Info(BoundaryCode, context.Location, refused.Message);
        }
        else
        {
            context.Error(refused.Code, refused.Message);
        }
    }

    /// <summary>
    /// From 0.9.4: a possibility's author never adopts it, whoever raised it, as work new --from-possibility refuses (§ 8). A standing agent's
    /// possibility is judged with its revision (<see cref="ValidatePossibility"/>). Any other possibility's revision that adopts it, by the
    /// author of its first revision, is refused (standing.own-output) when its chain carries a field new in 0.9.4, which no earlier estate
    /// wrote; otherwise it is reported as the records stand and the write boundary refuses it (standing.output-now), so that an adoption
    /// an earlier estate recorded keeps its reading.
    /// </summary>
    public static void ValidateAdoption(ValidationContext context, string? disposition)
    {
        ArgumentNullException.ThrowIfNull(context);
        RecordEnvelope record = context.Record;
        if (disposition != "adopted" || context.Find(record.Supersedes) is not { Type: "possibility" } previous)
        {
            return;
        }

        RecordEnvelope first = FirstOf(context.Model, previous);
        bool adopts = EstateModel.BodyString(previous, "disposition") != "adopted" || !Same(TJson.Object(record.Body, "adoptedAs"), TJson.Object(previous.Body, "adoptedAs"));
        if (!adopts || record.By != first.By || Of(first) is not null)
        {
            return;
        }

        string message = $"Possibility {first.Id} ({EstateModel.BodyString(first, "title")}) was raised by {record.By}: its author does not adopt it, whoever raised it; a principal other than its author adopts it, as work new --from-possibility does.";
        IEnumerable<RecordEnvelope> chain = context.Model.Chain(previous).Select(context.Model.ById.GetValueOrDefault).OfType<RecordEnvelope>().Append(record);
        if (chain.Any(r => PossibilityFields.Any(f => Has(r, f))))
        {
            context.Error("standing.own-output", message);
        }
        else
        {
            context.Findings.Info(BoundaryCode, context.Location, message);
        }
    }

    /// <summary>
    /// A held finding (§§ 8 and 9; design/estate-advisor.md § 8): a possibility whose revision as it stands carries disclosure held, whoever
    /// raised it and whether a standing agent did or not. No agent disposes of it, a person does; only an operator who is a person adopts it;
    /// and only an operator who is a person ends its hold, by a revision without disclosure, since what is disclosed is the operator's choice.
    /// </summary>
    private static void ValidateHeld(ValidationContext context, RecordEnvelope previous, RecordEnvelope first, string? disposition, bool disposes)
    {
        RecordEnvelope record = context.Record;
        bool held = EstateModel.BodyString(previous, "disclosure") == "held";
        if (!held && EstateModel.BodyString(record, "disclosure") != "held")
        {
            return;
        }

        bool person = GoalRules.IsPerson(context.Model, record.By);
        bool operatorPerson = person && context.HasRole(record.By, Roles.Operator);
        if (held && EstateModel.BodyString(record, "disclosure") != "held" && !operatorPerson)
        {
            context.Error("standing.held", $"Possibility {first.Id} is a held finding: only an operator who is a person ends its hold, by a revision without disclosure.");
        }

        if (disposes && !person)
        {
            context.Error("standing.held", $"Possibility {first.Id} is a held finding: a person disposes of it, never an agent; {record.By} neither adopts, declines nor parks it.");
        }
        else if (disposes && disposition == "adopted" && !operatorPerson)
        {
            context.Error("standing.held", $"Possibility {first.Id} is a held finding: an operator who is a person adopts it, after the Reviewer's review.");
        }
    }

    /// <summary>
    /// Validates what a standing agent's ask adds (§ 8): it is put to a person other than its author, belongs to the agent's goal, never
    /// opens with the words the runtime's own asks open with, and is never withdrawn: a decision of the person asked disposes of it, and a
    /// revision is judged by the raisedBy of the chain it revises, so leaving it out of a withdrawal changes nothing.
    /// </summary>
    public static void ValidateAsk(ValidationContext context, RaisedBy? raised, string? to, string? goal, string? state)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (raised is null)
        {
            return;
        }

        RecordEnvelope? previous = context.Find(context.Record.Supersedes) is { Type: "ask" } prior ? prior : null;
        if (previous is not null)
        {
            if (state == "withdrawn")
            {
                context.Error("standing.own-output", "A standing agent's ask is disposed of by a decision of the person asked; withdrawing it is a disposal, and its author does not make it.");
            }

            return;
        }

        if (to is null || to == context.Record.By || !GoalRules.IsPerson(context.Model, to))
        {
            context.Error("standing.ask", "A standing agent's ask is put to a person other than its author, whose decision disposes of it.");
        }

        if (goal is null || context.Find(goal) is not { Type: "goal" } named || context.Model.Chain(named)[0] != raised.Goal)
        {
            context.Error("standing.ask", $"A standing agent's ask belongs to its goal: goal names a revision of {raised.Goal}.");
        }

        string question = EstateModel.BodyString(context.Record, "question") ?? string.Empty;
        if (RuntimeWords.Any(p => question.StartsWith(p, StringComparison.Ordinal)))
        {
            context.Error("standing.ask", "A standing agent's ask does not open with the words of the runtime's own asks about its goal.");
        }
    }

    /// <summary>
    /// Validates what a standing agent's operator request adds (§ 8): words outside the estate, a publication request pinning the agent's
    /// work order, put to an operator other than its author, whose exact text the operator approves; it dispatches nothing. Every revision
    /// stays one, judged by the raisedBy of the chain it revises.
    /// </summary>
    public static void ValidateOperatorRequest(ValidationContext context, RaisedBy? raised)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (raised is null)
        {
            return;
        }

        if (EstateModel.BodyString(context.Record, "kind") != "publication")
        {
            context.Error("standing.publication", "A standing agent's operator request is a publication request: its words outside the estate, labeled its draft, which the operator approves.");
        }

        if (EstateModel.BodyString(context.Record, "to") == context.Record.By)
        {
            context.Error("standing.publication", "A standing agent's publication request is put to an operator other than its author, who closes it; no one closes their own output.");
        }

        if (context.Find(raised.Goal) is { Type: "goal" } goal && FlowRuns.Work(context.Model, context.Model.Latest(goal)) is RecordEnvelope order
            && (context.Record.Subject is null || !context.Model.Chain(order).Contains(context.Record.Subject.Id)))
        {
            context.Error("standing.publication", $"A standing agent's publication request pins its work order {EstateModel.BodyString(order, "key") ?? order.Id}.");
        }
    }

    /// <summary>
    /// Validates a closure of an operator request, an operator-request-close or an operator-request observation (§ 8): one of a standing
    /// agent's publication request, by the raisedBy of its chain, is closed only by an operator who is a person, directly; never by its
    /// raiser and never by an agent, whatever words of the operator it cites, so no standing agent records its own words as published or
    /// dismissed, or frees its open-output cap.
    /// </summary>
    public static void ValidateClosure(ValidationContext context, RecordEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        RecordEnvelope first = FirstOf(context.Model, request);
        if (Of(first) is null)
        {
            return;
        }

        string by = context.Record.By;
        if (by == first.By || by == EstateModel.BodyString(first, "askedBy"))
        {
            context.Error("standing.own-output", $"Operator request {first.Id} is a standing agent's publication request {by} raised: no standing agent closes its own output, whatever words it cites; an operator who is a person closes it.");
        }
        else if (!GoalRules.IsPerson(context.Model, by) || !context.HasRole(by, Roles.Operator))
        {
            context.Error("standing.publication", $"Operator request {first.Id} is a standing agent's publication request: an operator who is a person closes it directly, and {by} does not, whatever words of the operator it cites.");
        }
    }

    /// <summary>True when two body values are the same: both absent, or present with one canonical form. An absent value is never serialized.</summary>
    private static bool Same(JsonElement a, JsonElement b) =>
        a.ValueKind == JsonValueKind.Undefined || b.ValueKind == JsonValueKind.Undefined ? a.ValueKind == b.ValueKind : CanonicalJson.Serialize(a) == CanonicalJson.Serialize(b);

    private static bool Has(RecordEnvelope record, string name) => record.Body.ValueKind == JsonValueKind.Object && record.Body.TryGetProperty(name, out _);

    /// <summary>Validates a possibility's fingerprint: lower-case letters, digits and . : / _ -, up to 200 characters.</summary>
    public static void Fingerprint(BodyReader body) => body.Text("fingerprint", false, 200, FingerprintPattern(), "lower-case letters, digits and . : / _ -");

    /// <summary>Validates a possibility's sources: each an address, with its retrieval time, content hash and the extents relied on.</summary>
    public static void Sources(BodyReader body)
    {
        foreach (BodyReader source in body.NestedArray("sources", false) ?? [])
        {
            source.Text("address", true, 2000);
            source.Timestamp("retrievedAt", false);
            source.Hash("hash", false);
            source.Text("extents", false, 1000);
            source.NoOtherKeys();
        }
    }

    /// <summary>Validates a possibility's evidence for and against: each a statement and the sources it rests on, by their index; no scores.</summary>
    public static void Evidence(BodyReader body)
    {
        int sources = TJson.Array(body.Element, "sources").Count();
        foreach (BodyReader item in body.NestedArray("evidence", false) ?? [])
        {
            item.Enum("stance", true, "for", "against");
            item.Text("statement", true, 2000);
            item.Allow("sources");
            if (item.Element.TryGetProperty("sources", out JsonElement indexes) && (indexes.ValueKind != JsonValueKind.Array
                || indexes.EnumerateArray().Any(i => i.ValueKind != JsonValueKind.Number || !i.TryGetInt32(out int index) || index < 0 || index >= sources)))
            {
                item.Error("possibility.evidence", "sources", "Evidence names its sources by their index in the possibility's sources.");
            }

            item.NoOtherKeys();
        }
    }

    /// <summary>Validates a possibility's review triggers: each a time or a condition that calls for it to be read again.</summary>
    public static void ReviewTriggers(BodyReader body)
    {
        foreach (BodyReader trigger in body.NestedArray("reviewTriggers", false) ?? [])
        {
            string? at = trigger.Timestamp("at", false);
            string? condition = trigger.Text("condition", false, 500);
            trigger.NoOtherKeys();
            if ((at is null) == (condition is null))
            {
                trigger.Error("possibility.review-trigger", "at", "A review trigger is a time (at) or a condition, one of them.");
            }
        }
    }

    /// <summary>Validates a possibility's links to other possibilities: duplicate-of or supersedes, each naming one at its exact hash.</summary>
    public static void Links(ValidationContext context, BodyReader body)
    {
        foreach (BodyReader link in body.NestedArray("links", false) ?? [])
        {
            link.Enum("relation", true, Relations);
            string? id = link.Ulid("possibility", true);
            string? hash = link.Hash("hash", true);
            link.NoOtherKeys();
            if (id is null || hash is null)
            {
                continue;
            }

            if (context.Find(id) is not { Type: "possibility" } named || named.ComputedHash != hash)
            {
                link.Error("possibility.link", "possibility", $"A link names a possibility at its exact hash; {id} at {hash} is not one.");
            }
            else if (context.Model.ById.ContainsKey(context.Record.Id) ? context.Model.Chain(context.Model.Latest(named)).Intersect(context.Model.Chain(context.Model.Latest(context.Record))).Any() : context.Record.Supersedes is string s && context.Model.Chain(context.Model.Latest(named)).Contains(s))
            {
                link.Error("possibility.link", "possibility", "A possibility links to another possibility, never to a revision of itself.");
            }
        }
    }

    /// <summary>Validates a possibility's draft work order: the fields work new takes, which adoption by work new --from-possibility uses.</summary>
    public static void Draft(BodyReader body)
    {
        BodyReader? draft = body.Nested("draft", false);
        if (draft is null)
        {
            return;
        }

        draft.Text("key", false, 40, Patterns.WorkKey, "an upper-case key such as FIX-1");
        draft.Text("title", true, 200);
        draft.Text("intent", true, 4000);
        draft.StringArray("scope", true, allowEmpty: false, maxItemLength: 2000);
        draft.StringArray("deliverables", true, allowEmpty: false, maxItemLength: 2000);
        draft.Text("exitQuestion", true, 1000);
        draft.NoOtherKeys();
    }

    /// <summary>Validates a summary possibility's extras: the further ideas of an occurrence past its cap, one line each.</summary>
    public static void Extras(BodyReader body) => body.StringArray("extras", false, allowEmpty: false, maxItemLength: 2000);

    private static int? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;

    [GeneratedRegex("^[a-z0-9][a-z0-9.:/_-]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintPattern();
}
