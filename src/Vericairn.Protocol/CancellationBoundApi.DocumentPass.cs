// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public DocumentPassResponse DocumentPass(string version, string? product = null) => inner.DocumentPassAsync(version, product, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public DocumentPassAssignResponse DocumentPassAssign(string version, DocumentPassAssignRequest request) => inner.DocumentPassAssignAsync(version, request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<DocumentPassResponse> DocumentPassAsync(string version, string? product = null, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.DocumentPassAsync(version, product, token), cancellationToken);
    /// <inheritdoc />
    public Task<DocumentPassAssignResponse> DocumentPassAssignAsync(string version, DocumentPassAssignRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.DocumentPassAssignAsync(version, request, token), cancellationToken);
}
