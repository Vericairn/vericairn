// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Protocol;

namespace Vericairn.Mcp;

/// <summary>
/// From 0.9.4 (GOAL-08; the estate's design/standing-agents.md § 8): the three write tools a standing agent records its outputs with,
/// open only to the contributor of the assignment that carries an occurrence. Each output names its agent, occurrence, assignment and run;
/// another principal disposes of it, and none of these tools activates, funds, adopts, answers, accepts or publishes anything.
/// </summary>
public sealed partial class ToolRegistry
{
    private static IEnumerable<ToolDefinition> StandingOutputTools(string by)
    {
        yield return Write("standing_possibility", "From 0.9.4: records a possibility raised by an occurrence of a standing agent, by the contributor of the assignment that carries it, open and naming the goal, occurrence, assignment and run: a recommendation citing its sources, or proposed work with a draft work order that a principal other than its author adopts with work new --from-possibility. It carries a fingerprint; a second open possibility of the agent with the same fingerprint is refused, and raising a declined one again names it in raisesAgain with new evidence. An occurrence records at most its plan's possibilities (five) and drafts (three); further ideas join one summary possibility listed in extras. No score is recorded, and nothing it records adopts, declines or parks anything.",
            StandingArguments("The possibility: title, idea, fingerprint (lower-case letters, digits and . : / _ -), sources (each {address, retrievedAt?, hash?, extents?}), and optionally origin, evidence (each {stance: for|against, statement, sources?: indexes}), reviewTriggers (each {at} or {condition}), links (each {relation: duplicate-of|supersedes, possibility, hash}), draft ({key?, title, intent, scope, deliverables, exitQuestion}), disclosure (held, for a security finding the Reviewer reviews), extras and raisesAgain."),
            ["assignment", "body"],
            async (api, a, token) => Outcome(await api.StandingOutputAsync(new StandingOutputRequest(by, Arg(a, "assignment"), "possibility", Body(a, "body")), token).ConfigureAwait(false)), ["body"]);
        yield return Write("standing_question", "From 0.9.4: records a question raised by an occurrence of a standing agent, by the contributor of the assignment that carries it, put to a person (who decides for the goal when to is omitted), its recommendation first, belonging to the goal and naming the occurrence, assignment and run. A decision of the person asked disposes of it; its author never withdraws it.",
            StandingArguments("The ask: question, recommendation, consequence, and optionally options and to (a person's principal id)."),
            ["assignment", "body"],
            async (api, a, token) => Outcome(await api.StandingOutputAsync(new StandingOutputRequest(by, Arg(a, "assignment"), "ask", Body(a, "body")), token).ConfigureAwait(false)), ["body"]);
        yield return Write("standing_publication", "From 0.9.4: records words a standing agent's occurrence would say outside the estate as a publication request to an operator: the exact text by hash, labeled the agent's draft by naming the goal, occurrence, assignment and run, pinning the agent's work order. The request dispatches nothing; the operator approves the exact text or not.",
            StandingArguments("The publication: destination (named without machine paths), text (the exact words), why, recommendation and consequence."),
            ["assignment", "body"],
            async (api, a, token) => Outcome(await api.StandingOutputAsync(new StandingOutputRequest(by, Arg(a, "assignment"), "publication", Body(a, "body")), token).ConfigureAwait(false)), ["body"]);
    }

    private static JsonObject StandingArguments(string body) =>
        Arguments(("assignment", "The assignment carrying the occurrence, held by the acting principal."), ("body", body));
}
