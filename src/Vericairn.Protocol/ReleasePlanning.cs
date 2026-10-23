// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>Withdraw an exact candidate and record a draft-deletion intent. No provider effect is performed.</summary>
/// <param name="Release">Candidate release record id.</param>
/// <param name="By">Acting principal.</param>
/// <param name="Expected">Expected current release hash.</param>
/// <param name="Provider">Provider responsible for the draft; recorded explicitly, never inferred from current defaults.</param>
/// <param name="Reason">Why the candidate is withdrawn.</param>
/// <param name="Settings">Provider settings for the recorded destination, when needed; the candidate's repository is retained.</param>
public sealed record ReleaseWithdrawRequest(string Release, string By, string Expected, string Provider, string Reason,
    IReadOnlyDictionary<string, string>? Settings = null);

/// <summary>Move the complete prepared set of planned work to another carrier, or revise this carrier's version and its planned placements together.</summary>
/// <param name="By">Acting principal.</param>
/// <param name="Expected">Expected current source carrier hash.</param>
/// <param name="ExpectedWork">Every planned member's exact current revision id and hash. Membership and hashes must still match.</param>
/// <param name="To">Destination carrier key or id; exclusive with Version.</param>
/// <param name="ExpectedTarget">Expected current destination carrier hash; required with To.</param>
/// <param name="Version">New version for the source carrier; exclusive with To.</param>
public sealed record WorkMovePlanRequest(string By, string Expected, IReadOnlyDictionary<string, string> ExpectedWork,
    string? To = null, string? ExpectedTarget = null, string? Version = null);

/// <summary>Records stored by a planned-work move, including any partial storage before an interrupted batch.</summary>
/// <param name="Written">Every record actually stored; empty on a conflict or validation refusal.</param>
/// <param name="Problem">Why the request failed, if it did. A partial batch must be reconciled before retrying.</param>
public sealed record WorkMovePlanResponse(IReadOnlyList<RecordWritten> Written, Problem? Problem = null)
{
    /// <summary>Whether the complete batch was stored successfully.</summary>
    public bool Ok => Problem is null;
}
