// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public partial interface IEstateApi
{
    /// <summary>Read the current claim, effective request and freshness for an attempt or run, beside worker liveness. Null means unknown identity; older implementations explicitly report unsupported.</summary>
    RequestedProgress? WorkProgressGet(string id) => throw new NotSupportedException("Current requested progress is unavailable.");

    /// <summary>The cancellable read bridge for an attempt's or run's current progress claim.</summary>
    Task<RequestedProgress?> WorkProgressGetAsync(string id, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(WorkProgressGet), () => WorkProgressGet(id), false, this as IClientOperationLifetime, cancellationToken);
}
