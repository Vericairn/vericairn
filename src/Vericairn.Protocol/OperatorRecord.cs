// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>Record an operator's own signed judgment and carry its isolated commit through the recorded changes operations. Identical input resumes the original action.</summary>
public sealed record OperatorRecordRequest(string Type, string By, JsonElement Body, string? Subject = null,
    string? Supersedes = null, string? Product = null, int WaitSeconds = 120, string? Reconciliation = null);

/// <summary>The signed record, pinned branch and last recorded change, with an explicit next step when the action stops.</summary>
public sealed record OperatorRecordResponse(string Identity, string Status, string Message, bool Ok, string NextStep,
    RecordWritten? Record = null, string? Branch = null, string? Head = null, ChangeResponse? Change = null, Problem? Problem = null);

/// <summary>Finish this clone's unfinished operator action from its own records: its merge is recorded and its target checks pass. Nothing is signed, branched or committed.</summary>
public sealed record OperatorFinishRequest(string By, int WaitSeconds = 120);

/// <summary>This clone's unfinished operator action, or none; a missing local checkpoint is a problem.</summary>
public sealed record OperatorUnfinishedResponse(OperatorUnfinishedAction? Action, Problem? Problem = null);

/// <summary>
/// An unfinished operator action as its local checkpoint and its records show it: the signed record, branch, head, pull request and
/// stage, the recorded merge and its latest target-run observation, whether its records alone can finish it, and what finishing needs.
/// </summary>
public sealed record OperatorUnfinishedAction(string Identity, string RecordId, string? Type, string? Subject, string? By, RecordWritten? Record,
    string Branch, string? Head, string Stage, string? PullRequest, RecordWritten? Merge, string? MergeCommit, string? CheckStatus,
    RecordWritten? TargetObservation, RecordWritten? Ask, bool Finishable, string Summary, string NextStep);
