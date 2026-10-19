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
