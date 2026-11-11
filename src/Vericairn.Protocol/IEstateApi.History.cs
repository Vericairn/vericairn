// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Serialization;

namespace Vericairn.Protocol;

/// <summary>
/// Write a history checkpoint now, or adopt history checkpoints (0.9.2; the estate's design/signed-history.md §§ 9 and 10).
/// </summary>
/// <param name="By">The principal whose admitted device on the runtime's machine signs; required unless <paramref name="Check"/>.</param>
/// <param name="Adopt">Adopt checkpoints: the estate's first checkpoint, which witnesses every record present and anchors the unsigned records that count today. Only the root operator or a person holding administer adopts, never an agent.</param>
/// <param name="Check">Show what the checkpoint would witness and, with <paramref name="Adopt"/>, anchor, without signing or writing anything.</param>
/// <param name="ExpectPlan">With <paramref name="Adopt"/>, the plan hash the check printed: the adoption is refused unless it anchors exactly that set. Required when the adoption anchors anything.</param>
public sealed record HistoryCheckpointRequest(string? By, bool Adopt = false, bool Check = false, string? ExpectPlan = null);

/// <summary>A record a checkpoint or its check names.</summary>
/// <param name="Id">Record id.</param>
/// <param name="Type">Record type.</param>
/// <param name="At">The time its author states.</param>
/// <param name="Path">Repository-relative path.</param>
/// <param name="Commit">The commit that added its file, where the git adapter can tell; null otherwise.</param>
/// <param name="Device">For a record signed by a revoked device, that device.</param>
/// <param name="CommitAt">When the commit that added its file was committed, where the git adapter can tell.</param>
/// <param name="Note">What the reader should know before signing: a record dated well before the commit that added it, a file no commit holds, or an admission an anchor already lists.</param>
public sealed record HistoryRecordItem(string Id, string Type, string At, string Path, string? Commit = null, string? Device = null, string? CommitAt = null, string? Note = null);

/// <summary>A record file deleted in the repository's history, which no checkpoint can witness.</summary>
/// <param name="Path">Repository-relative path under records/.</param>
/// <param name="Commit">The newest commit that deleted it.</param>
public sealed record HistoryDeletedFile(string Path, string Commit);

/// <summary>What a history checkpoint witnesses and anchors, or would: the plan of a check, or the checkpoint written.</summary>
/// <param name="Trigger">adoption or manual.</param>
/// <param name="Applied">Whether a checkpoint was written.</param>
/// <param name="Adopted">Whether the estate has adopted checkpoints after this request.</param>
/// <param name="Witnessed">How many records the checkpoint witnesses, through the checkpoints it follows too.</param>
/// <param name="Listed">How many records its own inventory lists: the unwitnessed tail.</param>
/// <param name="Heads">The checkpoints it follows, or follows after writing: the heads, each by id and hash.</param>
/// <param name="Anchored">For adoption, the unsigned records that count today and that it anchors, each with the commit that added it.</param>
/// <param name="NotCounting">For adoption, the unsigned records that count for nothing today and stay so.</param>
/// <param name="ByRevokedDevices">For adoption, the records signed by a device revoked now.</param>
/// <param name="DeletedInHistory">For adoption, the record files deleted in the repository's history.</param>
/// <param name="Unavailable">What the git adapter could not tell, such as adding commits or deletions in a shallow clone; null when it told everything asked.</param>
/// <param name="Written">The checkpoint written, when applied.</param>
public sealed record HistoryCheckpointResponse(string Trigger, bool Applied, bool Adopted, int Witnessed, int Listed, IReadOnlyList<HistoryCheckpointPin> Heads,
    IReadOnlyList<HistoryRecordItem> Anchored, IReadOnlyList<HistoryRecordItem> NotCounting, IReadOnlyList<HistoryRecordItem> ByRevokedDevices,
    IReadOnlyList<HistoryDeletedFile> DeletedInHistory, string? Unavailable, RecordWritten? Written)
{
    /// <summary>For adoption, the hash of the anchored set, which <c>--adopt --expect-plan</c> names; null otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlanHash { get; init; }

    /// <summary>For adoption, the record files present that no commit holds yet, which adoption witnesses all the same.</summary>
    public IReadOnlyList<string> Uncommitted { get; init; } = [];

    /// <summary>
    /// For adoption, the records that read otherwise when roles and grants are read at each record's own time, as an adopted estate reads
    /// every record its adoption does not witness, each with a note saying what differs; adoption keeps, for every record it witnesses,
    /// the reading it has at adoption. Advisory: <see cref="PlanHash"/> does not cover it, and adoption computes it again from the copy
    /// it signs, so the adoption's list can differ from its check's.
    /// </summary>
    public IReadOnlyList<HistoryRecordItem> AuthorityChanges { get; init; } = [];
}

/// <summary>A history checkpoint named by id and hash.</summary>
/// <param name="Id">Record id.</param>
/// <param name="Hash">Content hash.</param>
public sealed record HistoryCheckpointPin(string Id, string Hash);

/// <summary>
/// The history checkpoint an act's trigger wrote in the same batch (0.9.2): at an upgrade, a release submission, a charter revision, an
/// estate's birth or once the unwitnessed tail is due, or the closing written before a revocation, a key rotation or a narrowing grant
/// revision; or, when none could be, why. A checkpoint that could not be written never blocks the act it would have followed without
/// saying so, and a closing that could not be written refuses its act.
/// </summary>
/// <param name="Trigger">adoption, interval, upgrade, release, charter or closing.</param>
/// <param name="Written">The checkpoint, when written.</param>
/// <param name="Skipped">Why none was written; null when one was.</param>
public sealed record TriggeredCheckpoint(string Trigger, RecordWritten? Written, string? Skipped);

public partial interface IEstateApi
{
    /// <summary>
    /// Write a history checkpoint (trigger manual) in an estate that has adopted checkpoints, or adopt them; with Check, show what it
    /// would witness and anchor, signing and writing nothing. Adoption is the root operator's or a human administrator's, never an agent's.
    /// </summary>
    (HistoryCheckpointResponse? Checkpoint, Problem? Problem) HistoryCheckpoint(HistoryCheckpointRequest request) =>
        (null, new Problem("unsupported", "This API implementation does not write history checkpoints.", []));

    /// <summary>The cancellable history checkpoint request; a write unless Check.</summary>
    Task<(HistoryCheckpointResponse? Checkpoint, Problem? Problem)> HistoryCheckpointAsync(HistoryCheckpointRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(HistoryCheckpoint), () => HistoryCheckpoint(request), request is not { Check: true }, this as IClientOperationLifetime, cancellationToken);
}
