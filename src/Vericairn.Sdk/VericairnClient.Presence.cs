// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net.Http.Json;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public PresenceLeaseResponse PresenceRegister(PresenceRegisterRequest request) => Post<PresenceRegisterRequest, PresenceLeaseResponse>("/v1/presence", request)!;

    /// <inheritdoc />
    public Task<PresenceLeaseResponse> PresenceRegisterAsync(PresenceRegisterRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(PresenceRegister), async token => (await PostAsync<PresenceRegisterRequest, PresenceLeaseResponse>("/v1/presence", request, token).ConfigureAwait(false))!,
            true, OperationTimeout?.Invoke(nameof(PresenceRegister)), cancellationToken);

    /// <inheritdoc />
    public PresenceLeaseResponse PresenceHeartbeat(string lease) => Post<object, PresenceLeaseResponse>(LeasePath(lease) + "/heartbeat", new { })!;

    /// <inheritdoc />
    public Task<PresenceLeaseResponse> PresenceHeartbeatAsync(string lease, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(PresenceHeartbeat), async token => (await PostAsync<object, PresenceLeaseResponse>(LeasePath(lease) + "/heartbeat", new { }, token).ConfigureAwait(false))!,
            true, OperationTimeout?.Invoke(nameof(PresenceHeartbeat)), cancellationToken);

    /// <inheritdoc />
    public PresenceLeaseResponse PresenceRelease(string lease) => ReleaseAsync(lease, http, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<PresenceLeaseResponse> PresenceReleaseAsync(string lease, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(PresenceRelease), token => ReleaseAsync(lease, asyncHttp, token), true, OperationTimeout?.Invoke(nameof(PresenceRelease)), cancellationToken);

    /// <inheritdoc />
    public PresenceResponse Presence(string? principal = null) => Get<PresenceResponse>(PresencePath(principal))!;

    /// <inheritdoc />
    public Task<PresenceResponse> PresenceAsync(string? principal = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(Presence), async token => (await GetAsync<PresenceResponse>(PresencePath(principal), cancellationToken: token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(Presence)), cancellationToken);

    private static string LeasePath(string lease) => "/v1/presence/" + Uri.EscapeDataString(lease);

    private static string PresencePath(string? principal) => principal is null ? "/v1/presence" : "/v1/presence?principal=" + Uri.EscapeDataString(principal);

    private async Task<PresenceLeaseResponse> ReleaseAsync(string lease, HttpClient client, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(LeasePath(lease), UriKind.Relative));
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<PresenceLeaseResponse>(ProtocolJson.Options, cancellationToken).ConfigureAwait(false)
            ?? throw new System.Text.Json.JsonException("The runtime returned no result.");
    }
}
