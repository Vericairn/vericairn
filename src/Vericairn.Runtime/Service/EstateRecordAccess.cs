// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Service;

/// <summary>
/// The shared record boundary used by owned feature services. The callbacks retain the estate's signing, validation and batch
/// journal implementation; feature services take the same gate around their own judgments and transitions. No callback here
/// starts a provider or owns a feature's state.
/// </summary>
internal sealed class EstateRecordAccess(
    object gate,
    EstateSession session,
    Func<RecordDraft, EstateModel, WriteOutcome> writeLocked,
    Func<RecordDraft, EstateModel, (AuthoredRecord? Record, string? Refusal)> authorLocked,
    Func<IReadOnlyList<AuthoredRecord>, string, string?, (IReadOnlyList<RecordWritten> Written, Problem? Problem)> storeTogetherLocked,
    Func<IReadOnlyList<AuthoredRecord>, EstateModel> loadWith,
    Func<EstateModel, Problem?> requestDeviceRefusal,
    Func<RecordDraft, EstateModel, IReadOnlyList<FindingDto>> checkReleaseRecord,
    Func<RecordDraft, EstateModel, (string? Device, string? Refusal)> heldSigner,
    Func<string, string, TriggeredCheckpoint?> writeCheckpointLocked,
    Func<EstateModel, IReadOnlyList<FindingDto>> historyObjections,
    PresenceCounter presence,
    Func<EstateModel, string, bool> signsWithPresence)
{
    internal object Gate { get; } = gate;

    internal EstateSession Session { get; } = session;

    // These operations require the caller to hold Gate, just as the underlying record boundary does.
    internal WriteOutcome WriteLocked(RecordDraft draft, EstateModel model) => writeLocked(draft, model);

    internal (AuthoredRecord? Record, string? Refusal) AuthorLocked(RecordDraft draft, EstateModel model) => authorLocked(draft, model);

    internal (IReadOnlyList<RecordWritten> Written, Problem? Problem) StoreTogetherLocked(IReadOnlyList<AuthoredRecord> records, string purpose, string? workOrder) =>
        storeTogetherLocked(records, purpose, workOrder);

    internal EstateModel LoadWith(IReadOnlyList<AuthoredRecord> records) => loadWith(records);

    internal IReadOnlyList<FindingDto> CheckReleaseRecord(RecordDraft draft, EstateModel model) => checkReleaseRecord(draft, model);

    /// <summary>
    /// The device on this machine that would sign a draft for its author, by its identity, found without signing or asking for presence
    /// (FIX-130), or null with the refusal that says why none would.
    /// </summary>
    internal (string? Device, string? Refusal) HeldSigner(RecordDraft draft, EstateModel model) => heldSigner(draft, model);

    internal Problem? RequestDeviceRefusal(EstateModel model) => requestDeviceRefusal(model);

    /// <summary>Writes a history checkpoint by itself, with a trigger and signed by the principal's device here: null before adoption (OPS-07).</summary>
    internal TriggeredCheckpoint? WriteCheckpointLocked(string trigger, string by) => writeCheckpointLocked(trigger, by);

    /// <summary>The presence prompts the shared signing boundary counts against an operator action (FIX-124).</summary>
    internal PresenceCounter Presence { get; } = presence;

    /// <summary>Whether the device this machine signs a principal's records with asks for its owner's presence at each signature (FIX-124).</summary>
    internal bool SignsWithPresence(EstateModel model, string by) => signsWithPresence(model, by);

    /// <summary>The release gate's objections from this machine's memory of the estate's history (OPS-07): none unless it may have been rewritten.</summary>
    internal IReadOnlyList<FindingDto> HistoryObjections(EstateModel model) => historyObjections(model);
}
