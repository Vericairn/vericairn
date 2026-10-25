// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>An exact record revision displayed before closing work.</summary>
public sealed record OperatorClosePin(string Id, string Hash);

/// <summary>A current passing review of the displayed result.</summary>
public sealed record OperatorCloseReview(string Id, string Hash, string Independence, string Summary);

/// <summary>A finished, reviewed step whose exact result has not been accepted.</summary>
public sealed record OperatorCloseItem(string Key, OperatorClosePin WorkOrder, string ExitQuestion,
    OperatorClosePin Result, string Outcome, string Summary, IReadOnlyList<OperatorCloseReview> Reviews)
{
    /// <summary>Copies the exact displayed subjects and complete passing-review basis for submission.</summary>
    public OperatorCloseSelection ToSelection() => new(Key, WorkOrder, Result, [.. Reviews.Select(r => new OperatorClosePin(r.Id, r.Hash))]);
}

/// <summary>The read-only close candidates, or the findings that prevent their use.</summary>
public sealed record OperatorCloseCandidatesResponse(IReadOnlyList<OperatorCloseItem> Items, Problem? Problem = null)
{
    /// <summary>Whether the candidate snapshot is valid.</summary>
    public bool Ok => Problem is null;
}

/// <summary>One selected step, pinned to its displayed work order, result and complete passing-review basis.</summary>
public sealed record OperatorCloseSelection(string Key, OperatorClosePin WorkOrder, OperatorClosePin Result, IReadOnlyList<OperatorClosePin> Reviews);

/// <summary>Close the displayed selections as the operator. All also requires the complete eligible membership to remain unchanged.</summary>
public sealed record OperatorCloseRequest(string By, IReadOnlyList<OperatorCloseSelection> Selection, bool All = false);

/// <summary>Signed acceptance receipts, or a refusal. A storage interruption can return receipts with a reconciliation problem.</summary>
public sealed record OperatorCloseResponse(IReadOnlyList<RecordWritten> Written, Problem? Problem = null)
{
    /// <summary>Whether every selected acceptance was stored.</summary>
    public bool Ok => Problem is null;
}
