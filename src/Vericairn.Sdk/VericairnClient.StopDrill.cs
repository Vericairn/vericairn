// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public StopDrillResponse StopDrill(StopDrillRequest request) => Post<StopDrillRequest, StopDrillResponse>("/v1/runs/drill", request)!;
    /// <inheritdoc />
    public Task<StopDrillResponse> StopDrillAsync(StopDrillRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<StopDrillResponse>(nameof(StopDrill), async token => (await PostAsync<StopDrillRequest, StopDrillResponse>("/v1/runs/drill", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(StopDrill)), cancellationToken);
}
