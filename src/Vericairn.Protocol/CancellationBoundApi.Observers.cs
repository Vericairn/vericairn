// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public ObserversResponse Observers() => inner.ObserversAsync(cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<ObserversResponse> ObserversAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ObserversAsync(token), cancellationToken);
    /// <inheritdoc />
    public ObserversResponse ObserverDeclare(ObserverDeclareRequest request) => inner.ObserverDeclareAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<ObserversResponse> ObserverDeclareAsync(ObserverDeclareRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ObserverDeclareAsync(request, token), cancellationToken);
    /// <inheritdoc />
    public ObserversResponse ObserverReport(ObserverReportRequest request) => inner.ObserverReportAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<ObserversResponse> ObserverReportAsync(ObserverReportRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ObserverReportAsync(request, token), cancellationToken);
    /// <inheritdoc />
    public ObserversResponse ObserverWithdraw(ObserverWithdrawRequest request) => inner.ObserverWithdrawAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<ObserversResponse> ObserverWithdrawAsync(ObserverWithdrawRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ObserverWithdrawAsync(request, token), cancellationToken);
}
