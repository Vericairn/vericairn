// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public PresenceLeaseResponse PresenceRegister(PresenceRegisterRequest request) => inner.PresenceRegisterAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<PresenceLeaseResponse> PresenceRegisterAsync(PresenceRegisterRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.PresenceRegisterAsync(request, token), cancellationToken);

    /// <inheritdoc />
    public PresenceLeaseResponse PresenceHeartbeat(string lease) => inner.PresenceHeartbeatAsync(lease, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<PresenceLeaseResponse> PresenceHeartbeatAsync(string lease, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.PresenceHeartbeatAsync(lease, token), cancellationToken);

    /// <inheritdoc />
    public PresenceLeaseResponse PresenceRelease(string lease) => inner.PresenceReleaseAsync(lease, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<PresenceLeaseResponse> PresenceReleaseAsync(string lease, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.PresenceReleaseAsync(lease, token), cancellationToken);

    /// <inheritdoc />
    public PresenceResponse Presence(string? principal = null) => inner.PresenceAsync(principal, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<PresenceResponse> PresenceAsync(string? principal = null, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.PresenceAsync(principal, token), cancellationToken);
}
