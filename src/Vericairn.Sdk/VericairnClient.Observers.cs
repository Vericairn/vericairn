// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public ObserversResponse Observers() => Get<ObserversResponse>("/v1/observers")!;
    /// <inheritdoc />
    public ObserversResponse ObserverDeclare(ObserverDeclareRequest request) => Post<ObserverDeclareRequest, ObserversResponse>("/v1/observers/declare", request)!;
    /// <inheritdoc />
    public ObserversResponse ObserverReport(ObserverReportRequest request) => Post<ObserverReportRequest, ObserversResponse>("/v1/observers/report", request)!;
    /// <inheritdoc />
    public ObserversResponse ObserverWithdraw(ObserverWithdrawRequest request) => Post<ObserverWithdrawRequest, ObserversResponse>("/v1/observers/withdraw", request)!;
    /// <inheritdoc />
    public Task<ObserversResponse> ObserversAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<ObserversResponse>(nameof(Observers), async token => (await GetAsync<ObserversResponse>("/v1/observers", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Observers)), cancellationToken);
    /// <inheritdoc />
    public Task<ObserversResponse> ObserverDeclareAsync(ObserverDeclareRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ObserversResponse>(nameof(ObserverDeclare), async token => (await PostAsync<ObserverDeclareRequest, ObserversResponse>("/v1/observers/declare", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(ObserverDeclare)), cancellationToken);
    /// <inheritdoc />
    public Task<ObserversResponse> ObserverReportAsync(ObserverReportRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ObserversResponse>(nameof(ObserverReport), async token => (await PostAsync<ObserverReportRequest, ObserversResponse>("/v1/observers/report", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(ObserverReport)), cancellationToken);
    /// <inheritdoc />
    public Task<ObserversResponse> ObserverWithdrawAsync(ObserverWithdrawRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ObserversResponse>(nameof(ObserverWithdraw), async token => (await PostAsync<ObserverWithdrawRequest, ObserversResponse>("/v1/observers/withdraw", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(ObserverWithdraw)), cancellationToken);
}
