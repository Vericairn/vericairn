// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Providers;

/// <summary>A clean local starting point for a single-record branch.</summary>
public sealed record RecordCommitBasis(string Branch, string Head);

/// <summary>Local repository operations only. Remote effects belong to the existing push adapter and pull-request provider.</summary>
public interface IRecordCommitAdapter
{
    /// <summary>Require the estate itself to be a clean repository root with a named branch and complete history.</summary>
    RecordCommitBasis Prepare(string directory);

    /// <summary>Create the branch at the pinned base with only the prepared record and members present, or recognize that branch after interruption; never switch away from unrelated work.</summary>
    void Branch(string directory, RecordCommitBasis basis, string branch, IReadOnlyDictionary<string, string> files);

    /// <summary>Commit exactly the record and generated members named by path and byte hash, or recognize their existing commit after a lost receipt. Other changes refuse.</summary>
    string Commit(string directory, string branch, string basis, IReadOnlyDictionary<string, string> files, IReadOnlySet<string> receipts);

    /// <summary>Verify the retained commit contains exactly the original record and members on its pinned base, without changing the working tree.</summary>
    void Verify(string directory, string head, string basis, IReadOnlyDictionary<string, string> files);

    /// <summary>Require the exact branch and head with no changes except untracked, recorded receipts belonging to this action.</summary>
    void Check(string directory, string branch, string head, IReadOnlySet<string> receipts);
}
