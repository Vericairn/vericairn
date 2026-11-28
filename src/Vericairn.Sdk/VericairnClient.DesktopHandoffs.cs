// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    private const string DesktopHandoffsPath = "/v1/handoff/desktop";
    private const string DesktopHandoffOpenPath = DesktopHandoffsPath + "/open";
    private const string DesktopHandoffRemovePath = DesktopHandoffsPath + "/remove";

    /// <inheritdoc />
    public DesktopHandoffsResponse DesktopHandoffs(string principal) =>
        Get<DesktopHandoffsResponse>(DesktopHandoffsPath + "?principal=" + Uri.EscapeDataString(principal))!;

    /// <inheritdoc />
    public DesktopHandoffResponse PrepareDesktopHandoff(DesktopHandoffRequest request) =>
        Post<DesktopHandoffRequest, DesktopHandoffResponse>(DesktopHandoffsPath, request)!;

    /// <inheritdoc />
    public DesktopHandoffResponse OpenDesktopHandoff(DesktopHandoffOpenRequest request) =>
        Post<DesktopHandoffOpenRequest, DesktopHandoffResponse>(DesktopHandoffOpenPath, request)!;

    /// <inheritdoc />
    public DesktopHandoffRemoveResponse RemoveDesktopHandoff(DesktopHandoffRemoveRequest request) =>
        Post<DesktopHandoffRemoveRequest, DesktopHandoffRemoveResponse>(DesktopHandoffRemovePath, request)!;

    /// <inheritdoc />
    public Task<DesktopHandoffsResponse> DesktopHandoffsAsync(string principal, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(DesktopHandoffs), async token =>
            (await GetAsync<DesktopHandoffsResponse>(DesktopHandoffsPath + "?principal=" + Uri.EscapeDataString(principal), cancellationToken: token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(DesktopHandoffs)), cancellationToken);

    /// <inheritdoc />
    public Task<DesktopHandoffResponse> PrepareDesktopHandoffAsync(DesktopHandoffRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(PrepareDesktopHandoff), async token =>
            (await PostAsync<DesktopHandoffRequest, DesktopHandoffResponse>(DesktopHandoffsPath, request, cancellationToken: token).ConfigureAwait(false))!,
            true, OperationTimeout?.Invoke(nameof(PrepareDesktopHandoff)), cancellationToken);

    /// <inheritdoc />
    public Task<DesktopHandoffResponse> OpenDesktopHandoffAsync(DesktopHandoffOpenRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(OpenDesktopHandoff), async token =>
            (await PostAsync<DesktopHandoffOpenRequest, DesktopHandoffResponse>(DesktopHandoffOpenPath, request, cancellationToken: token).ConfigureAwait(false))!,
            true, OperationTimeout?.Invoke(nameof(OpenDesktopHandoff)), cancellationToken);

    /// <inheritdoc />
    public Task<DesktopHandoffRemoveResponse> RemoveDesktopHandoffAsync(DesktopHandoffRemoveRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(RemoveDesktopHandoff), async token =>
            (await PostAsync<DesktopHandoffRemoveRequest, DesktopHandoffRemoveResponse>(DesktopHandoffRemovePath, request, cancellationToken: token).ConfigureAwait(false))!,
            true, OperationTimeout?.Invoke(nameof(RemoveDesktopHandoff)), cancellationToken);
}
