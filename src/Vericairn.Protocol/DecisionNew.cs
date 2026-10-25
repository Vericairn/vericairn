// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>Record an operator's supplied answer and pin the design documents changed since an exact local base commit.</summary>
/// <param name="By">The recording principal; the ask identifies the operator who answered.</param>
/// <param name="Ask">The ask being answered, by any revision id.</param>
/// <param name="Statement">The operator's exact words, without trimming or rewriting.</param>
/// <param name="AnsweredAt">When the operator answered, as a UTC record timestamp.</param>
/// <param name="BaseCommit">Full local commit before the design changes.</param>
/// <param name="Title">Short decision title.</param>
/// <param name="Summary">What was decided.</param>
/// <param name="Synthesis">Context, decision, consequences and limits.</param>
/// <param name="Effects">Concrete effects of the answer.</param>
/// <param name="Documents">Exactly the changed design file paths; empty when no design file changed.</param>
/// <param name="Stake">How much rides on the decision.</param>
/// <param name="Affects">Affected work-order ids.</param>
/// <param name="NotAuthorized">What this answer does not authorize.</param>
/// <param name="DecidedAt">Defaults to AnsweredAt; a later time is refused.</param>
/// <param name="ExpectedAsk">Optional expected hash of the current ask revision.</param>
public sealed record DecisionNewRequest(string By, string Ask, string Statement, string AnsweredAt, string BaseCommit,
    string Title, string Summary, string Synthesis, IReadOnlyList<string> Effects, IReadOnlyList<string> Documents,
    string Stake = "low", IReadOnlyList<string>? Affects = null, IReadOnlyList<string>? NotAuthorized = null,
    string? DecidedAt = null, string? ExpectedAsk = null);

/// <summary>Stored decision and document pins. Any problem with receipts requires reconciliation before retrying.</summary>
/// <param name="Written">The decision receipt, when stored.</param>
/// <param name="Pins">The document-revision receipts actually stored.</param>
/// <param name="Problem">Refusal or incomplete batch information.</param>
public sealed record DecisionNewResponse(RecordWritten? Written, IReadOnlyList<RecordWritten> Pins, Problem? Problem = null)
{
    /// <summary>True only when the complete decision and pin batch was stored.</summary>
    public bool Ok => Written is not null && Problem is null;
}
