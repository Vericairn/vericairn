// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public RequestedProgress? WorkProgressGet(string id) => inner.WorkProgressGetAsync(id, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<RequestedProgress?> WorkProgressGetAsync(string id, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.WorkProgressGetAsync(id, token), cancellationToken);
}
