// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>Push the named branch at an exact head through the product's declared repository, after its scan passes.</summary>
public sealed record ChangesPushRequest(string Product, string Branch, string Head, string By, string? Reconciliation = null);

/// <summary>Open a pull request from an exact head already pushed through a recorded, scanned operation.</summary>
public sealed record ChangesOpenRequest(string Product, string Branch, string Head, string Title, string Body, string By, string? Reconciliation = null);

/// <summary>Observe every check and merge only at this exact head, then observe the target's run.</summary>
public sealed record ChangesMergeRequest(string Product, string PullRequest, string Head, string By, string? Reconciliation = null);

/// <summary>Observe a recorded change under its original provider and pins; never retry an uncertain effect.</summary>
public sealed record ChangesObserveRequest(string Operation, string By, string? Cancel = null);

/// <summary>The recorded effect and its latest observation. A successful merge receipt does not claim its target's checks passed.</summary>
public sealed record ChangeResponse(string Identity, string Status, string Message, bool Ok, RecordWritten? Operation = null,
    RecordWritten? Observation = null, RecordWritten? Ask = null, string? PullRequest = null, string? Head = null,
    string? MergeCommit = null, string? CheckStatus = null, Problem? Problem = null);
