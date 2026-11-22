// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public StopDrillResponse StopDrill(StopDrillRequest request) => inner.StopDrillAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<StopDrillResponse> StopDrillAsync(StopDrillRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.StopDrillAsync(request, token), cancellationToken);
}
