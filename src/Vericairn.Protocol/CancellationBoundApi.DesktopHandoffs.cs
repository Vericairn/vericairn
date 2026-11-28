// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public DesktopHandoffsResponse DesktopHandoffs(string principal) => inner.DesktopHandoffsAsync(principal, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public DesktopHandoffResponse PrepareDesktopHandoff(DesktopHandoffRequest request) => inner.PrepareDesktopHandoffAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public DesktopHandoffResponse OpenDesktopHandoff(DesktopHandoffOpenRequest request) => inner.OpenDesktopHandoffAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public DesktopHandoffRemoveResponse RemoveDesktopHandoff(DesktopHandoffRemoveRequest request) => inner.RemoveDesktopHandoffAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<DesktopHandoffsResponse> DesktopHandoffsAsync(string principal, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.DesktopHandoffsAsync(principal, token), cancellationToken);
    /// <inheritdoc />
    public Task<DesktopHandoffResponse> PrepareDesktopHandoffAsync(DesktopHandoffRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.PrepareDesktopHandoffAsync(request, token), cancellationToken);
    /// <inheritdoc />
    public Task<DesktopHandoffResponse> OpenDesktopHandoffAsync(DesktopHandoffOpenRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.OpenDesktopHandoffAsync(request, token), cancellationToken);
    /// <inheritdoc />
    public Task<DesktopHandoffRemoveResponse> RemoveDesktopHandoffAsync(DesktopHandoffRemoveRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.RemoveDesktopHandoffAsync(request, token), cancellationToken);
}
