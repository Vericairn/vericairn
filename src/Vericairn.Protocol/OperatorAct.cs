// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>
/// From 0.9.4 (OPS-24): an operator item whose command is ready, as one reviewed act lists it: its kind, who signs it, what its signature
/// records, the record type and exact subject revision it is about, the exact basis it rests on, whether the operator authored the subject,
/// and whether it may be dismissed with a reason. An answer also carries the ask's question, recommendation and that recommendation's hash.
/// </summary>
public sealed record OperatorActItem(string Id, string Kind, string By, string Title, string Records, string RecordType, string SubjectType,
    OperatorClosePin Subject, IReadOnlyList<OperatorClosePin> Basis, bool Self, bool Dismissable,
    string? Question = null, string? Recommendation = null, string? RecommendationHash = null, IReadOnlyList<OperatorActReview>? Reviews = null)
{
    /// <summary>Whether the item's signature records an acceptance: a judgment the operator selects, never one chosen for them by default.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsAcceptance => RecordType == "acceptance";

    /// <summary>The operator's choice for this item, pinned to the exact subject, basis and recommendation shown: sign, wait or dismiss.</summary>
    public OperatorActChoice Choose(string choice = "sign", string? reason = null) => new(Id, choice, Subject, Basis, RecommendationHash, reason);
}

/// <summary>A current review an acceptance rests on: who reviewed, its outcome and independence, and the exact subject hash it reviewed.</summary>
public sealed record OperatorActReview(string Id, string Hash, string By, string Outcome, string Independence, string SubjectHash);

/// <summary>Every ready operator item of one operator, or the findings that prevent their use. Reading signs nothing.</summary>
public sealed record OperatorActListResponse(string? By, IReadOnlyList<OperatorActItem> Items, Problem? Problem = null)
{
    /// <summary>Whether the listing is valid.</summary>
    public bool Ok => Problem is null;
}

/// <summary>One listed item's choice: sign its record, wait (sign nothing) or dismiss it with a reason, pinned to what was shown.</summary>
public sealed record OperatorActChoice(string Item, string Choice, OperatorClosePin Subject, IReadOnlyList<OperatorClosePin> Basis, string? RecommendationHash = null, string? Reason = null);

/// <summary>
/// Sign the chosen items as the operator in one act. With all, the choices cover every ready item exactly, each signed, waiting or
/// dismissed, and the act refuses when an item arrived. Stake is the stake of each answer's decision: low, medium, high or critical.
/// </summary>
public sealed record OperatorActRequest(string By, IReadOnlyList<OperatorActChoice> Choices, bool All = false, string Stake = "low");

/// <summary>What one record of the act is: the item it is for, the choice it carries out and its receipt.</summary>
public sealed record OperatorActReceipt(string Item, string Choice, RecordWritten Record);

/// <summary>The act's identity and each record it stored, or a refusal. A storage interruption can return receipts with a reconciliation problem.</summary>
public sealed record OperatorActResponse(string? Act, IReadOnlyList<OperatorActReceipt> Written, Problem? Problem = null)
{
    /// <summary>Whether every chosen record was stored.</summary>
    public bool Ok => Problem is null;
}

public partial interface IEstateApi
{
    /// <summary>From 0.9.4 (OPS-24): every operator item whose command is ready for the operator named, or the root operator, with what each signature records. Reading signs nothing.</summary>
    OperatorActListResponse OperatorActItems(string? principal = null) => new(principal, [], new("unsupported", "This API implementation does not list ready operator items.", []));

    /// <summary>From 0.9.4 (OPS-24): signs the chosen items as the operator in one reviewed act; nothing outside the choices is signed.</summary>
    OperatorActResponse OperatorAct(OperatorActRequest request) => new(null, [], new("unsupported", "This API implementation does not sign operator acts.", []));

    /// <summary>Cancellable read of the ready operator items.</summary>
    Task<OperatorActListResponse> OperatorActItemsAsync(string? principal = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OperatorActItems), () => OperatorActItems(principal), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>Cancellable act; cancellation after submission does not undo the records.</summary>
    Task<OperatorActResponse> OperatorActAsync(OperatorActRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OperatorAct), () => OperatorAct(request), true, this as IClientOperationLifetime, cancellationToken);
}
