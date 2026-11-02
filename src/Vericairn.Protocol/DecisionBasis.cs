// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Serialization;

namespace Vericairn.Protocol;

/// <summary>
/// The estate's current basis for an answer, as the runtime observes its repository: the head commit and the design documents
/// changed since a base commit, committed, staged, unstaged or untracked. From 0.9.0 (REMOTE-11). Reading it writes nothing.
/// </summary>
/// <param name="Ok">True when the runtime observed the complete basis.</param>
/// <param name="BaseCommit">The base the documents are compared with: the one asked for, or the head when none was.</param>
/// <param name="Head">The repository's head commit at observation.</param>
/// <param name="Documents">Every design document changed since the base, with its current content hash; empty when none changed.</param>
/// <param name="Problem">Why the basis is unavailable: no repository source, a base that is not a full local commit, a conflict, a deletion or an unreadable file.</param>
public sealed record DecisionBasisResponse(bool Ok, string? BaseCommit = null, string? Head = null, IReadOnlyList<DecisionBasisDocument>? Documents = null, Problem? Problem = null)
{
    /// <summary>Whether any design document changed since the base. An answer with document changes pins them, on the desktop.</summary>
    [JsonIgnore]
    public bool DocumentsChanged => Documents is { Count: > 0 };
}

/// <summary>One design document changed since the basis's base commit.</summary>
/// <param name="Path">Its repository-relative path under design/.</param>
/// <param name="Hash">The SHA-256 of its current bytes.</param>
public sealed record DecisionBasisDocument(string Path, string Hash);

public partial interface IEstateApi
{
    /// <summary>
    /// The estate's current basis for an answer: its head commit and the design documents changed since <paramref name="baseCommit"/>,
    /// or since the head when none is given. An answer with no document changes uses the head as its change set's base and head.
    /// </summary>
    DecisionBasisResponse DecisionBasis(string? baseCommit = null) => new(false, Problem: new Problem("unsupported", "This API implementation does not supply the estate's decision basis.", []));

    /// <summary>The cancellable read of the decision basis; it writes nothing.</summary>
    Task<DecisionBasisResponse> DecisionBasisAsync(string? baseCommit = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(DecisionBasis), () => DecisionBasis(baseCommit), false, this as IClientOperationLifetime, cancellationToken);
}
