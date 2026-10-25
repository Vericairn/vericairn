// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public DocumentPassResponse DocumentPass(string version, string? product = null) => DocumentPassAsync(version, product).GetAwaiter().GetResult();
    /// <inheritdoc />
    public DocumentPassAssignResponse DocumentPassAssign(string version, DocumentPassAssignRequest request) =>
        DocumentPassAssignAsync(version, request).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<DocumentPassResponse> DocumentPassAsync(string version, string? product = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(DocumentPass), async token => (await GetAsync<DocumentPassResponse>(DocumentPassPath(version, product), cancellationToken: token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(DocumentPass)), cancellationToken);
    /// <inheritdoc />
    public Task<DocumentPassAssignResponse> DocumentPassAssignAsync(string version, DocumentPassAssignRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(DocumentPassAssign), async token => (await PostAsync<DocumentPassAssignRequest, DocumentPassAssignResponse>(DocumentPassPath(version) + "/assignments", request, cancellationToken: token).ConfigureAwait(false))!,
            true, OperationTimeout?.Invoke(nameof(DocumentPassAssign)), cancellationToken);

    private static string DocumentPassPath(string version, string? product = null) => "/v1/documents/pass/" + Uri.EscapeDataString(version)
        + (product is null ? "" : "?product=" + Uri.EscapeDataString(product));

    private static void CheckPassAssignmentOutcome(DocumentPassAssignResponse response)
    {
        if (response.Written is null || response.Written.Any(r => r is null)
            || (response.Problem is null && response.Written.Count != 2)
            || (response.Written.Count > 0 && response.Problem is not null and not { Code: "reconcile-required" }))
            throw new JsonException("The runtime returned no definite documentation assignment outcome.");
    }
}
