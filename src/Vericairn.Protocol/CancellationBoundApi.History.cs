// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public (HistoryCheckpointResponse? Checkpoint, Problem? Problem) HistoryCheckpoint(HistoryCheckpointRequest request) =>
        inner.HistoryCheckpointAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<(HistoryCheckpointResponse? Checkpoint, Problem? Problem)> HistoryCheckpointAsync(HistoryCheckpointRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.HistoryCheckpointAsync(request, token), cancellationToken);
}
