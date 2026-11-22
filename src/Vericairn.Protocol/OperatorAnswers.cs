// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>
/// From 0.9.4 (OPS-16): an open ask waiting on an operator, which an act may answer with its recommendation: the exact ask revision, its
/// recommendation and that recommendation's SHA-256, shown first, then the question, the consequence of leaving it unanswered and its options.
/// </summary>
public sealed record OperatorAnswerItem(OperatorClosePin Ask, string To, string Recommendation, string RecommendationHash, string Question,
    string Consequence, IReadOnlyList<string> Options, string State)
{
    /// <summary>Copies the exact ask revision and recommendation hash for submission.</summary>
    public OperatorAnswerSelection ToSelection() => new(Ask, RecommendationHash);
}

/// <summary>The asks an act may answer, or the findings that prevent their use.</summary>
public sealed record OperatorAnswerCandidatesResponse(IReadOnlyList<OperatorAnswerItem> Items, Problem? Problem = null)
{
    /// <summary>Whether the listing is valid.</summary>
    public bool Ok => Problem is null;
}

/// <summary>One selected ask, pinned to its displayed revision and the hash of the recommendation the act takes.</summary>
public sealed record OperatorAnswerSelection(OperatorClosePin Ask, string Recommendation);

/// <summary>
/// Answer the selected asks with their recommendations in one act as the operator they are put to. All also requires every ask waiting on
/// that operator to be selected and unchanged. Stake is each decision's stake: low, medium, high or critical.
/// </summary>
public sealed record OperatorAnswerRequest(string By, IReadOnlyList<OperatorAnswerSelection> Selection, bool All = false, string Stake = "low");

/// <summary>The act's identity and each decision's receipt, or a refusal. A storage interruption can return receipts with a reconciliation problem.</summary>
public sealed record OperatorAnswerResponse(string? Act, IReadOnlyList<RecordWritten> Written, Problem? Problem = null)
{
    /// <summary>Whether every selected ask's decision was stored.</summary>
    public bool Ok => Problem is null;
}

public partial interface IEstateApi
{
    /// <summary>From 0.9.4 (OPS-16): the asks waiting on an operator, or on the one named, that an act may answer with their recommendations. Reading answers nothing.</summary>
    OperatorAnswerCandidatesResponse OperatorAnswerCandidates(string? principal = null) => new([], new("unsupported", "This API implementation does not answer asks in one act.", []));

    /// <summary>From 0.9.4 (OPS-16): answers the selected asks with their recommendations in one act signed by the operator's own device, one decision for each ask.</summary>
    OperatorAnswerResponse OperatorAnswer(OperatorAnswerRequest request) => new(null, [], new("unsupported", "This API implementation does not answer asks in one act.", []));

    /// <summary>Cancellable read of the asks an act may answer.</summary>
    Task<OperatorAnswerCandidatesResponse> OperatorAnswerCandidatesAsync(string? principal = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OperatorAnswerCandidates), () => OperatorAnswerCandidates(principal), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>Cancellable act; cancellation after submission does not undo the decisions.</summary>
    Task<OperatorAnswerResponse> OperatorAnswerAsync(OperatorAnswerRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OperatorAnswer), () => OperatorAnswer(request), true, this as IClientOperationLifetime, cancellationToken);
}
