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

    /// <summary>
    /// As <see cref="Prepare(string)"/>, tolerating only the named untracked receipts: those of the earlier action the runtime finished from
    /// its records. An adapter that does not override this tolerates none of them.
    /// </summary>
    RecordCommitBasis Prepare(string directory, IReadOnlySet<string> receipts) => Prepare(directory);

    /// <summary>Create the branch at the pinned base with only the prepared record and members present, or recognize that branch after interruption; never switch away from unrelated work.</summary>
    void Branch(string directory, RecordCommitBasis basis, string branch, IReadOnlyDictionary<string, string> files);

    /// <summary>
    /// As <see cref="Branch(string, RecordCommitBasis, string, IReadOnlyDictionary{string, string})"/>, also tolerating the named untracked
    /// receipts. An adapter that does not override this tolerates none of them.
    /// </summary>
    void Branch(string directory, RecordCommitBasis basis, string branch, IReadOnlyDictionary<string, string> files, IReadOnlySet<string> receipts) =>
        Branch(directory, basis, branch, files);

    /// <summary>Commit exactly the record and generated members named by path and byte hash, or recognize their existing commit after a lost receipt. Other changes refuse.</summary>
    string Commit(string directory, string branch, string basis, IReadOnlyDictionary<string, string> files, IReadOnlySet<string> receipts);

    /// <summary>
    /// As <see cref="Commit(string, string, string, IReadOnlyDictionary{string, string}, IReadOnlySet{string})"/>, also committing the
    /// carried records, named by path and byte hash: the settled push and merge trail of earlier actions, still untracked in the
    /// working tree (FIX-126). An adapter that does not override this carries none of them and refuses a non-empty set.
    /// </summary>
    string Commit(string directory, string branch, string basis, IReadOnlyDictionary<string, string> files, IReadOnlySet<string> receipts, IReadOnlyDictionary<string, string> carried)
    {
        ArgumentNullException.ThrowIfNull(carried);
        return carried.Count == 0 ? Commit(directory, branch, basis, files, receipts)
            : throw new ProviderRefusedException("This record commit adapter cannot carry an earlier action's trail.");
    }

    /// <summary>Verify the retained commit contains exactly the original record and members on its pinned base, without changing the working tree.</summary>
    void Verify(string directory, string head, string basis, IReadOnlyDictionary<string, string> files);

    /// <summary>
    /// As <see cref="Verify(string, string, string, IReadOnlyDictionary{string, string})"/>, with the carried records the commit holds
    /// beside them (FIX-126). An adapter that does not override this refuses a non-empty set.
    /// </summary>
    void Verify(string directory, string head, string basis, IReadOnlyDictionary<string, string> files, IReadOnlyDictionary<string, string> carried)
    {
        ArgumentNullException.ThrowIfNull(carried);
        if (carried.Count != 0) throw new ProviderRefusedException("This record commit adapter cannot verify an earlier action's carried trail.");
        Verify(directory, head, basis, files);
    }

    /// <summary>Require the exact branch and head with no changes except untracked, recorded receipts belonging to this action.</summary>
    void Check(string directory, string branch, string head, IReadOnlySet<string> receipts);

    /// <summary>
    /// Of the named repository-relative paths, those the working tree holds untracked, read without changing anything (FIX-126). Null
    /// when the adapter cannot say, as for an adapter that does not override this: the runtime then carries no earlier trail.
    /// </summary>
    IReadOnlySet<string>? Untracked(string directory, IReadOnlySet<string> paths) => null;
}
