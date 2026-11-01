// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public RequestedProgress? WorkProgressGet(string id) => Get<RequestedProgress>("/v1/progress/" + Uri.EscapeDataString(id));

    /// <inheritdoc />
    public Task<RequestedProgress?> WorkProgressGetAsync(string id, CancellationToken cancellationToken = default) =>
        RunOperationAsync<RequestedProgress?>(nameof(WorkProgressGet), token =>
            GetAsync<RequestedProgress>("/v1/progress/" + Uri.EscapeDataString(id), cancellationToken: token),
            false, OperationTimeout?.Invoke(nameof(WorkProgressGet)), cancellationToken);
}
