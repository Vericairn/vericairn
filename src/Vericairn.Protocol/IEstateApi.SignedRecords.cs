// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public partial interface IEstateApi
{
    /// <summary>
    /// Prepare a record the requesting device will sign with its own key: the runtime fixes its id, time and form, and keeps the plan
    /// for that device only. Writes nothing; only a device-signed request can prepare.
    /// </summary>
    RecordPrepareResponse PrepareRecord(NewRecordRequest request) => new(false, Problem: new("unsupported", "This runtime does not prepare device-signed records.", []));

    /// <summary>Write a prepared record with the requesting device's signature, verified against that admitted device; nothing on the runtime's machine signs it.</summary>
    WriteOutcome NewSignedRecord(SignedRecordRequest request) => WriteOutcome.Fail(new("unsupported", "This runtime does not write device-signed records.", []));

    /// <summary>The cancellable preparation; it writes nothing.</summary>
    Task<RecordPrepareResponse> PrepareRecordAsync(NewRecordRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(PrepareRecord), () => PrepareRecord(request), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable device-signed write; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> NewSignedRecordAsync(SignedRecordRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(NewSignedRecord), () => NewSignedRecord(request), true, this as IClientOperationLifetime, cancellationToken);
}
