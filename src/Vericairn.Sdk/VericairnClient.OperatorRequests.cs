// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public OperatorRequestsResponse OperatorRequests(string? principal = null) => Get<OperatorRequestsResponse>("/v1/operator/requests" + (principal is null ? string.Empty : "?to=" + Uri.EscapeDataString(principal)))!;

    /// <summary>The cancellable OperatorRequests operation.</summary>
    public Task<OperatorRequestsResponse> OperatorRequestsAsync(string? principal = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync<OperatorRequestsResponse>(nameof(OperatorRequests), async token => (await GetAsync<OperatorRequestsResponse>("/v1/operator/requests" + (principal is null ? string.Empty : "?to=" + Uri.EscapeDataString(principal)), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(OperatorRequests)), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome OperatorRequestWrite(OperatorRequestWriteRequest request) => Post<OperatorRequestWriteRequest, WriteOutcome>("/v1/operator/requests", request)!;

    /// <summary>The cancellable OperatorRequestWrite operation.</summary>
    public Task<WriteOutcome> OperatorRequestWriteAsync(OperatorRequestWriteRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(OperatorRequestWrite), async token => (await PostAsync<OperatorRequestWriteRequest, WriteOutcome>("/v1/operator/requests", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(OperatorRequestWrite)), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome OperatorRequestCarry(string ask, OperatorRequestCarryRequest request) => Post<OperatorRequestCarryRequest, WriteOutcome>("/v1/operator/asks/" + Uri.EscapeDataString(ask) + "/carry", request)!;

    /// <summary>The cancellable OperatorRequestCarry operation.</summary>
    public Task<WriteOutcome> OperatorRequestCarryAsync(string ask, OperatorRequestCarryRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(OperatorRequestCarry), async token => (await PostAsync<OperatorRequestCarryRequest, WriteOutcome>("/v1/operator/asks/" + Uri.EscapeDataString(ask) + "/carry", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(OperatorRequestCarry)), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome OperatorRequestDone(string id, OperatorRequestCloseRequest request) => Post<OperatorRequestCloseRequest, WriteOutcome>("/v1/operator/requests/" + Uri.EscapeDataString(id) + "/done", request)!;

    /// <summary>The cancellable OperatorRequestDone operation.</summary>
    public Task<WriteOutcome> OperatorRequestDoneAsync(string id, OperatorRequestCloseRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(OperatorRequestDone), async token => (await PostAsync<OperatorRequestCloseRequest, WriteOutcome>("/v1/operator/requests/" + Uri.EscapeDataString(id) + "/done", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(OperatorRequestDone)), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome OperatorRequestDismiss(string id, OperatorRequestCloseRequest request) => Post<OperatorRequestCloseRequest, WriteOutcome>("/v1/operator/requests/" + Uri.EscapeDataString(id) + "/dismiss", request)!;

    /// <summary>The cancellable OperatorRequestDismiss operation.</summary>
    public Task<WriteOutcome> OperatorRequestDismissAsync(string id, OperatorRequestCloseRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(OperatorRequestDismiss), async token => (await PostAsync<OperatorRequestCloseRequest, WriteOutcome>("/v1/operator/requests/" + Uri.EscapeDataString(id) + "/dismiss", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(OperatorRequestDismiss)), cancellationToken);

}
