// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public RecordPrepareResponse PrepareRecord(NewRecordRequest request) => inner.PrepareRecordAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public WriteOutcome NewSignedRecord(SignedRecordRequest request) => inner.NewSignedRecordAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<RecordPrepareResponse> PrepareRecordAsync(NewRecordRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PrepareRecordAsync(request, token), cancellationToken);
    /// <inheritdoc />
    public Task<WriteOutcome> NewSignedRecordAsync(SignedRecordRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.NewSignedRecordAsync(request, token), cancellationToken);
}
