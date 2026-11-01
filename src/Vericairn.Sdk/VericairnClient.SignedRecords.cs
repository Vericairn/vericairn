// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public RecordPrepareResponse PrepareRecord(NewRecordRequest request) => Post<NewRecordRequest, RecordPrepareResponse>("/v1/records/prepare", request)!;
    /// <inheritdoc />
    public WriteOutcome NewSignedRecord(SignedRecordRequest request) => Post<SignedRecordRequest, WriteOutcome>("/v1/records/signed", request)!;
    /// <inheritdoc />
    public Task<RecordPrepareResponse> PrepareRecordAsync(NewRecordRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<RecordPrepareResponse>(nameof(PrepareRecord), async token => (await PostAsync<NewRecordRequest, RecordPrepareResponse>("/v1/records/prepare", request, cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(PrepareRecord)), cancellationToken);
    /// <inheritdoc />
    public Task<WriteOutcome> NewSignedRecordAsync(SignedRecordRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(NewSignedRecord), async token => (await PostAsync<SignedRecordRequest, WriteOutcome>("/v1/records/signed", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(NewSignedRecord)), cancellationToken);
}
