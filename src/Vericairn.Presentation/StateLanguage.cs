// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.ObjectModel;

namespace Vericairn.Presentation;

/// <summary>
/// The state language of the cohesion contract as the graphical clients present it: one table of vocabularies, each the
/// exact strings the runtime renders (so the command line, the API and the clients never disagree), with the plain-language
/// label a surface shows and the rule that presentation may simplify wording but never merges two states. A golden file in
/// docs/ keeps this table and the runtime's strings identical, checked by a test.
/// </summary>
public static class StateLanguage
{
    /// <summary>A vocabulary: the subject, the states in the order they are usually reached, and each state's label.</summary>
    public sealed record Vocabulary(string Subject, IReadOnlyList<StateWord> States);

    /// <summary>One state: the runtime's exact string, the label a surface shows, and whether it means a person is needed.</summary>
    public sealed record StateWord(string Value, string Label, bool NeedsPerson = false, bool Uncertain = false);

    /// <summary>Every vocabulary the clients present.</summary>
    public static IReadOnlyList<Vocabulary> Vocabularies { get; } = new ReadOnlyCollection<Vocabulary>(
    [
        new("work-order", [new("planned", "planned"), new("active", "active"), new("paused", "paused"), new("submitted", "submitted, awaiting acceptance", NeedsPerson: true), new("retired", "retired"), new("accepted", "accepted"), new("accepted (earlier revision)", "accepted at an earlier revision; the current revision is not", NeedsPerson: true)]),
        new("work-order-kind", [new("ordinary", "ordinary work"), new("milestone", "milestone")]),
        new("assignment", [new("no attempt", "assigned, no attempt yet"), new("in progress", "in progress"), new("awaiting review", "result submitted, awaiting review", NeedsPerson: true), new("accepted", "accepted"), new("accepted (earlier revision)", "accepted at an earlier revision; the latest result is not", NeedsPerson: true)]),
        new("ask", [new("open", "open, awaiting an answer", NeedsPerson: true), new("notified", "notified, delivery unknown", NeedsPerson: true), new("withdrawn", "withdrawn by its asker"), new("answered", "answered by a decision")]),
        new("result-outcome", [new("none", "no result"), new("completed", "completed, a claim to inspect"), new("partial", "partial"), new("blocked", "blocked", NeedsPerson: true), new("abandoned", "abandoned")]),
        new("operation", [new("not-started", "intent recorded, not started"), new("dispatched", "dispatched, outcome pending", Uncertain: true), new("observed-success", "observed success"), new("known-failure", "known failure"), new("canceled", "canceled before dispatch"), new("uncertain", "uncertain: reconcile before any retry", NeedsPerson: true, Uncertain: true)]),
        new("release", [new("candidate", "candidate, awaiting the operator's acceptance", NeedsPerson: true), new("published", "published"), new("withdrawn", "withdrawn from distribution")]),
        new("review-independence", [new("self", "self-review"), new("independent-agent", "independent agent review"), new("operator", "operator review")]),
        new("review-outcome", [new("pass", "pass"), new("findings", "findings"), new("fail", "fail")]),
        new("verification", [new("passed", "passed"), new("failed", "failed"), new("unknown", "unknown", Uncertain: true)]),
        new("finding-severity", [new("error", "error"), new("warning", "warning"), new("info", "information")]),
        new("device-admission", [new("not admitted", "not admitted"), new("admitted", "admitted"), new("revoked", "revoked")]),
        new("runtime", [new("daemon", "a daemon serves this estate"), new("embedded", "no daemon; the command line runs the embedded runtime"), new("unavailable", "the estate is unavailable to this client")]),
        new("connection", [new("connecting", "connecting"), new("current", "current"), new("cached", "cached; freshness shown"), new("partial", "partial"), new("denied", "denied"), new("unavailable", "unavailable")]),
        new("attention", [new("decision needed", "decision needed", NeedsPerson: true), new("blocked", "blocked", NeedsPerson: true), new("uncertain effect", "uncertain effect", NeedsPerson: true, Uncertain: true), new("failed check", "failed check", NeedsPerson: true), new("incident", "incident or continuity concern", NeedsPerson: true), new("information", "information")]),
        new("goal", [new("proposed", "proposed, not yet activated by who decides for it", NeedsPerson: true), new("active", "active"), new("paused", "paused"), new("budget-limited", "budget-limited: its sponsor is asked to raise the budget or stop", NeedsPerson: true), new("blocked", "blocked by an ask outstanding", NeedsPerson: true), new("impossible", "impossible, as its sponsor confirmed"), new("abandoned", "abandoned"), new("achieved", "achieved: accepted")]),
        new("condition", [new("met", "met"), new("not-met", "not met"), new("unknown", "unknown here", Uncertain: true)]),
        new("evaluation", [new("met", "met"), new("not-yet", "not yet"), new("impossible", "impossible")]),
        new("step-route", [new("agent", "routed to an agent: its run starts within the custody precondition"), new("person", "routed to a person, who is assigned it and asked", NeedsPerson: true), new("reserved", "a reserved action: a person who holds it is assigned it and asked", NeedsPerson: true), new("unavailable", "no principal is available: the sponsor is asked", NeedsPerson: true), new("running", "a run of it is in flight"), new("assigned", "with the person it is assigned to"), new("review", "its result awaits review", NeedsPerson: true), new("asked", "waits on an ask", NeedsPerson: true), new("answered", "answered, and no principal is available yet"), new("attention", "required attention: the sponsor is asked", NeedsPerson: true), new("request", "a reserved act: an operator request with its exact command is recorded for the operator", NeedsPerson: true), new("requested", "a reserved act: its operator request waits on the operator", NeedsPerson: true), new("closed", "its operator request was closed, and is not made again while its command holds"), new("queued", "queued: three stops of the run already reach its person", NeedsPerson: true)]),
        new("flow-step", [new("done", "done"), new("next", "next"), new("blocked", "blocked by a failing predicate or an earlier step"), new("stale", "stale: an input changed after use, so it reruns"), new("in-review", "in review", NeedsPerson: true), new("stopped", "stopped for a person", NeedsPerson: true), new("off", "off in this run's configuration")]),
        new("flow-item", [new("satisfied", "satisfied"), new("missing", "missing"), new("unproven", "its provenance does not hold"), new("unverified", "a named check does not pass at its hash"), new("in-review", "in review", NeedsPerson: true), new("stale", "changed after its review"), new("off", "off in this run's configuration")]),
        new("flow-stop", [new("person", "a person holds its role or its review", NeedsPerson: true), new("reserved", "a reserved act: it waits on the operator with its exact command", NeedsPerson: true), new("attention", "required attention: the sponsor decides", NeedsPerson: true), new("accept", "the run is complete: the goal is ready to accept", NeedsPerson: true)]),
        new("history", [new("not adopted", "not adopted: no history checkpoint counts yet"), new("adopted", "adopted: history checkpoints witness the records")]),
        new("flow-delivery", [new("due", "due: the next goal step delivers it", NeedsPerson: true), new("delivered", "delivered: it waits on its person", NeedsPerson: true), new("closed", "closed: answered, and not asked again while that holds"), new("queued", "queued behind three stops that already reach its person", NeedsPerson: true), new("outside", "carried outside goal step: nothing is recorded to reach its person", NeedsPerson: true)]),
    ]);

    /// <summary>The label for a runtime string of a vocabulary; the string itself when the vocabulary does not know it, never a merged or invented state.</summary>
    public static string Label(string subject, string? value)
    {
        if (value is null)
        {
            return "unknown";
        }

        Vocabulary? vocabulary = Vocabularies.FirstOrDefault(v => v.Subject == subject);
        return vocabulary?.States.FirstOrDefault(s => s.Value == value)?.Label ?? value;
    }

    /// <summary>Whether a state of a vocabulary means a person is needed.</summary>
    public static bool NeedsPerson(string subject, string? value) =>
        Vocabularies.FirstOrDefault(v => v.Subject == subject)?.States.FirstOrDefault(s => s.Value == value)?.NeedsPerson ?? false;
}
