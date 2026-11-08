// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    private const string HistoryCheckpointPath = "/v1/history/checkpoints";

    /// <inheritdoc />
    public (HistoryCheckpointResponse? Checkpoint, Problem? Problem) HistoryCheckpoint(HistoryCheckpointRequest request) =>
        PostOrProblem<HistoryCheckpointRequest, HistoryCheckpointResponse>(HistoryCheckpointPath, request);

    /// <inheritdoc />
    public Task<(HistoryCheckpointResponse? Checkpoint, Problem? Problem)> HistoryCheckpointAsync(HistoryCheckpointRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<(HistoryCheckpointResponse? Checkpoint, Problem? Problem)>(nameof(HistoryCheckpoint),
            async token => await PostOrProblemAsync<HistoryCheckpointRequest, HistoryCheckpointResponse>(HistoryCheckpointPath, request, cancellationToken: token).ConfigureAwait(false),
            request is not { Check: true }, OperationTimeout?.Invoke(nameof(HistoryCheckpoint)), cancellationToken);
}
