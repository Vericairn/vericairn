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
    Func<EstateModel, Problem?> requestDeviceRefusal)
{
    internal object Gate { get; } = gate;

    internal EstateSession Session { get; } = session;

    // These operations require the caller to hold Gate, just as the underlying record boundary does.
    internal WriteOutcome WriteLocked(RecordDraft draft, EstateModel model) => writeLocked(draft, model);

    internal (AuthoredRecord? Record, string? Refusal) AuthorLocked(RecordDraft draft, EstateModel model) => authorLocked(draft, model);

    internal (IReadOnlyList<RecordWritten> Written, Problem? Problem) StoreTogetherLocked(IReadOnlyList<AuthoredRecord> records, string purpose, string? workOrder) =>
        storeTogetherLocked(records, purpose, workOrder);

    internal EstateModel LoadWith(IReadOnlyList<AuthoredRecord> records) => loadWith(records);

    internal Problem? RequestDeviceRefusal(EstateModel model) => requestDeviceRefusal(model);
}
