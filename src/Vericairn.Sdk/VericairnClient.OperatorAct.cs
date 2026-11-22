// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    private static string ActPath(string? principal) => "/v1/operator/act" + (principal is null ? string.Empty : "?by=" + Uri.EscapeDataString(principal));

    /// <inheritdoc />
    public OperatorActListResponse OperatorActItems(string? principal = null) => Get<OperatorActListResponse>(ActPath(principal))!;

    /// <summary>The cancellable OperatorActItems operation.</summary>
    public Task<OperatorActListResponse> OperatorActItemsAsync(string? principal = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync<OperatorActListResponse>(nameof(OperatorActItems), async token => (await GetAsync<OperatorActListResponse>(ActPath(principal), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(OperatorActItems)), cancellationToken);

    /// <inheritdoc />
    public OperatorActResponse OperatorAct(OperatorActRequest request) => ActOutcome(request, Post<OperatorActRequest, OperatorActResponse>("/v1/operator/act", request));

    /// <summary>The cancellable OperatorAct operation.</summary>
    public Task<OperatorActResponse> OperatorActAsync(OperatorActRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<OperatorActResponse>(nameof(OperatorAct), async token => ActOutcome(request, await PostAsync<OperatorActRequest, OperatorActResponse>("/v1/operator/act", request, cancellationToken: token).ConfigureAwait(false)), true, OperationTimeout?.Invoke(nameof(OperatorAct)), cancellationToken);

    // A complete act names itself and returns one distinct receipt for each item chosen to sign or dismiss, each for a chosen item; receipts with
    // any problem but a reconciliation are contradictory.
    private static OperatorActResponse ActOutcome(OperatorActRequest request, OperatorActResponse? response)
    {
        HashSet<string> chosen = request.Choices is null ? [] : [.. request.Choices.Where(c => c is not null && c.Choice != "wait").Select(c => c.Item)];
        if (response?.Written is null || response.Written.Any(r => r?.Record is null || string.IsNullOrWhiteSpace(r.Record.Id) || string.IsNullOrWhiteSpace(r.Record.Hash)
                || string.IsNullOrWhiteSpace(r.Record.Path) || !chosen.Contains(r.Item))
            || response.Written.Select(r => r.Record.Id).Distinct(StringComparer.Ordinal).Count() != response.Written.Count
            || response.Ok && (response.Written.Count != chosen.Count || response.Written.Count > 0 && string.IsNullOrWhiteSpace(response.Act))
            || response.Written.Count > 0 && response.Problem is not null and not { Code: "reconcile-required" })
            throw new JsonException("The runtime returned no definite outcome of the act. Inspect the chosen items' records before signing again.");
        return response;
    }
}
