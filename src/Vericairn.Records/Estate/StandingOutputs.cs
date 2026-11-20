// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
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
/// No standing agent adopts, declines, parks, answers or withdraws its own output.
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

    /// <summary>A standing plan's output caps: its plan.standing.outputs, or five possibilities, three drafts and ten open outputs.</summary>
    public static OutputCaps CapsOf(RecordEnvelope goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        JsonElement outputs = TJson.Object(StandingAgents.Plan(goal), "outputs");
        return new OutputCaps(Number(outputs, "possibilities") ?? DefaultPossibilities, Number(outputs, "drafts") ?? DefaultDrafts, Number(outputs, "open") ?? DefaultOpen);
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

            bool counts = standing.Type switch
            {
                "possibility" => EstateModel.BodyString(standing, "disposition") == "open",
                "ask" => EstateModel.BodyString(standing, "state") is "open" or "notified"
                    && !model.DecisionsAnswering(model.Chain(standing)).Any(d => before is null || GoalRules.Before(d, before)),
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
    /// in, in flight when it does; an occurrence already settled records no further output. A revision keeps what its first revision named.
    /// </summary>
    public static RaisedBy? ValidateRaisedBy(ValidationContext context, BodyReader? raised)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (raised is null)
        {
            return null;
        }

        string? goalId = raised.Ulid("goal", true);
        string? key = raised.Text("occurrence", true, 600);
        string? assignmentId = raised.Ulid("assignment", true);
        string? runId = raised.Ulid("run", false);
        raised.NoOtherKeys();
        if (goalId is null || key is null || assignmentId is null)
        {
            return null;
        }

        var named = new RaisedBy(goalId, key, assignmentId, runId);
        RecordEnvelope? previous = context.Find(context.Record.Supersedes) is { } prior && prior.Type == context.Record.Type ? prior : null;
        if (previous is not null)
        {
            // A revision keeps what raised the output exactly; the checks below were made when it was first recorded.
            if (CanonicalJson.Serialize(TJson.Object(previous.Body, "raisedBy")) != CanonicalJson.Serialize(raised.Element))
            {
                context.Error("standing.raised-by", "A revision of a standing output keeps its raisedBy exactly: what raised it does not change.");
            }

            return named;
        }

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
    /// fingerprint, and names a declined one it raises again with new evidence. A revision keeps its fingerprint, and a revision that
    /// disposes of it is never the agent's own: not its author, no agent that carried one of the standing agent's occurrences before it,
    /// and no agent the goal's role names; a held finding is disposed of only by an operator who is a person.
    /// </summary>
    public static void ValidatePossibility(ValidationContext context, RaisedBy? raised, string? disposition)
    {
        ArgumentNullException.ThrowIfNull(context);
        RecordEnvelope record = context.Record;
        RecordEnvelope? previous = context.Find(record.Supersedes) is { Type: "possibility" } prior ? prior : null;
        RecordEnvelope? first = previous is null ? null : context.Model.ById.GetValueOrDefault(context.Model.Chain(previous)[0]);
        if (previous is not null)
        {
            ValidateRevision(context, previous, first!, disposition);
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

        if (context.Find(raised.Goal) is not { Type: "goal" } goal)
        {
            return;
        }

        // Caps per occurrence: its plan's possibilities, drafts among them, and one summary possibility that the extras join.
        OutputCaps caps = CapsOf(context.Model.Latest(goal));
        List<RecordEnvelope> earlier = [.. Raised(context.Model, raised.Goal, record).Where(r => r.Type == "possibility" && Of(r)?.Occurrence == raised.Occurrence)];
        bool summary = record.Body.TryGetProperty("extras", out _);
        bool draft = record.Body.TryGetProperty("draft", out _);
        if (summary && earlier.Any(r => r.Body.TryGetProperty("extras", out _)))
        {
            context.Error("standing.output-cap", $"Occurrence {raised.Occurrence} has its summary possibility already: its extras join that one, which another principal disposes of.");
        }
        else if (!summary && earlier.Count(r => !r.Body.TryGetProperty("extras", out _)) >= caps.Possibilities)
        {
            context.Error("standing.output-cap", $"Occurrence {raised.Occurrence} has recorded {caps.Possibilities.ToString(CultureInfo.InvariantCulture)} possibilities, its cap: further ideas join one summary possibility, listed in its extras.");
        }
        else if (draft && earlier.Count(r => r.Body.TryGetProperty("draft", out _)) >= caps.Drafts)
        {
            context.Error("standing.output-cap", $"Occurrence {raised.Occurrence} has recorded {caps.Drafts.ToString(CultureInfo.InvariantCulture)} possibilities with a draft work order, its cap: further proposed work joins its summary possibility.");
        }

        if (fingerprint is null)
        {
            return;
        }

        // A second open possibility of the agent with the same fingerprint is refused; a declined one is raised again only by naming it.
        List<RecordEnvelope> same = [.. Raised(context.Model, raised.Goal, record).Where(r => r.Type == "possibility" && EstateModel.BodyString(r, "fingerprint") == fingerprint)];
        if (same.Select(r => AsOf(context.Model, r, record)).FirstOrDefault(r => r is not null && EstateModel.BodyString(r, "disposition") == "open") is RecordEnvelope open)
        {
            context.Error("standing.duplicate-output", $"Possibility {context.Model.Chain(open)[0]} of this standing agent with fingerprint '{fingerprint}' is open: a second open one is refused; add to that one instead.");
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

    /// <summary>A revision of a possibility: the fingerprint and raisedBy kept, and a disposition by its own agent refused.</summary>
    private static void ValidateRevision(ValidationContext context, RecordEnvelope previous, RecordEnvelope first, string? disposition)
    {
        RecordEnvelope record = context.Record;
        if (Of(first) is not RaisedBy raised)
        {
            if (record.Body.TryGetProperty("raisedBy", out _))
            {
                context.Error("standing.raised-by", "A standing output is raised as one: a revision of a possibility no standing agent raised names no raisedBy.");
            }

            return;
        }

        if (!record.Body.TryGetProperty("raisedBy", out _) || EstateModel.BodyString(record, "fingerprint") != EstateModel.BodyString(previous, "fingerprint"))
        {
            context.Error("standing.raised-by", "A revision of a standing agent's possibility keeps its raisedBy and its fingerprint.");
        }

        bool disposes = disposition != EstateModel.BodyString(previous, "disposition")
            || CanonicalJson.Serialize(TJson.Object(record.Body, "adoptedAs")) != CanonicalJson.Serialize(TJson.Object(previous.Body, "adoptedAs"));
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

        if (context.Find(raised.Goal) is { Type: "goal" } goal && FlowRuns.Work(context.Model, context.Model.Latest(goal)) is RecordEnvelope order && !GoalRules.IsPerson(context.Model, by))
        {
            IReadOnlyList<string> chain = context.Model.Chain(order);
            if (context.Model.OfType("assignment").Any(a => a.Subject is not null && chain.Contains(a.Subject.Id) && EstateModel.BodyString(a, "contributor") == by && GoalRules.Before(a, record)))
            {
                context.Error("standing.own-output", $"{by} carried occurrences of standing agent {raised.Goal}: no standing agent disposes of its own outputs, so another principal does.");
            }
            else if (StandingAgents.Resolve(context.Model, context.Model.Latest(goal)).Flow is { } flow && StandingAgents.WorkStep(flow) is { } step
                && Routing.Principals(context.Model, context.Model.Latest(goal), step.Role).Principals.Contains(by))
            {
                // Who the goal's role names is read as the roster stands: the write boundary refuses a new disposition by such an agent.
                context.Findings.Info(BoundaryCode, context.Location, $"{by} is an agent the role {step.Role} of standing agent {raised.Goal} names: no standing agent disposes of its own outputs, so another principal does.");
            }
        }

        if (EstateModel.BodyString(first, "disclosure") == "held" && disposition is "adopted" && (!GoalRules.IsPerson(context.Model, by) || !context.HasRole(by, Roles.Operator)))
        {
            context.Error("standing.held", $"Possibility {first.Id} is a held finding: an operator who is a person adopts it, after the Reviewer's review.");
        }
    }

    /// <summary>
    /// Validates what a standing agent's ask adds (§ 8): it is put to a person other than its author, belongs to the agent's goal, never
    /// opens with the words the runtime's own asks open with, and is never withdrawn by its author: a decision of the person asked disposes
    /// of it.
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
                context.Error("standing.own-output", "A standing agent's ask is disposed of by a decision of the person asked; its author does not withdraw it.");
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
        if (new[] { NextEligible.AttentionQuestion, StandingAgents.RenewalAsk, StandingAgents.PeriodAsk }.Any(p => question.StartsWith(p, StringComparison.Ordinal)))
        {
            context.Error("standing.ask", "A standing agent's ask does not open with the words of the runtime's own asks about its goal.");
        }
    }

    /// <summary>
    /// Validates what a standing agent's operator request adds (§ 8): words outside the estate, a publication request pinning the agent's
    /// work order, whose exact text the operator approves; it dispatches nothing.
    /// </summary>
    public static void ValidateOperatorRequest(ValidationContext context, RaisedBy? raised)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (raised is null || context.Find(context.Record.Supersedes) is { Type: "operator-request" })
        {
            return;
        }

        if (EstateModel.BodyString(context.Record, "kind") != "publication")
        {
            context.Error("standing.publication", "A standing agent's operator request is a publication request: its words outside the estate, labeled its draft, which the operator approves.");
        }

        if (context.Find(raised.Goal) is { Type: "goal" } goal && FlowRuns.Work(context.Model, context.Model.Latest(goal)) is RecordEnvelope order
            && (context.Record.Subject is null || !context.Model.Chain(order).Contains(context.Record.Subject.Id)))
        {
            context.Error("standing.publication", $"A standing agent's publication request pins its work order {EstateModel.BodyString(order, "key") ?? order.Id}.");
        }
    }

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
