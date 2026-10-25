// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>An exact record revision displayed by a documentation pass.</summary>
public sealed record DocumentPassPin(string Id, string Hash);

/// <summary>A file added, modified or deleted between the publication commits.</summary>
public sealed record DocumentPassChange(string Path, string Kind);

/// <summary>A record in the publication window, retained as context for a reviewer.</summary>
public sealed record DocumentPassRecord(string Type, DocumentPassPin Record, string Summary);

/// <summary>An ordinary assignment whose pinned sources include the publication and document.</summary>
public sealed record DocumentPassAssignment(string Key, string Assignment, string Reviewer);

/// <summary>A currently due document whose release reason names this exact publication.</summary>
public sealed record DocumentPassItem(DocumentPassPin Declaration, string Path, string? Repository, string Title,
    string Level, IReadOnlyList<string> CoveredPaths, IReadOnlyList<DocumentReason> Reasons,
    IReadOnlyList<DocumentPassChange> Changes, IReadOnlyList<DocumentPassRecord> Records,
    IReadOnlyList<string> Notes, IReadOnlyList<DocumentPassAssignment> Assignments);

/// <summary>A proposed front-document patch against the named source bytes; applying and reviewing it are separate actions.</summary>
public sealed record DocumentPassUpdate(string Declaration, string Path, string? Repository, string? BaseCommit,
    string BeforeHash, string AfterHash, string Patch);

/// <summary>A read-only publication pass, including proposed patches and explicit comparison gaps.</summary>
public sealed record DocumentPassResponse(string Version, DocumentPassPin? Release, DocumentPassPin? Previous,
    string? Product, string? Commit, IReadOnlyList<DocumentPassItem> Documents, IReadOnlyList<DocumentPassRecord> Records,
    IReadOnlyList<DocumentPassUpdate> Updates, IReadOnlyList<string> Notes, Problem? Problem = null)
{
    /// <summary>Whether the publication was resolved and the pass could be derived.</summary>
    public bool Ok => Problem is null;
}

/// <summary>Assign an explicit displayed selection to one reviewer under a new ordinary work-order key.</summary>
public sealed record DocumentPassAssignRequest(string By, string Reviewer, string Key, DocumentPassPin Release,
    IReadOnlyList<DocumentPassPin> Documents);

/// <summary>The work-order and assignment receipts; a partial store keeps receipts with its reconciliation problem.</summary>
public sealed record DocumentPassAssignResponse(IReadOnlyList<RecordWritten> Written, Problem? Problem = null)
{
    /// <summary>Whether the complete assignment batch was stored.</summary>
    public bool Ok => Problem is null;
}
