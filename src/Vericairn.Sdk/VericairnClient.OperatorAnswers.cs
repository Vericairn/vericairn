// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    private static string AnswersPath(string? principal) => "/v1/operator/answers" + (principal is null ? string.Empty : "?to=" + Uri.EscapeDataString(principal));

    /// <inheritdoc />
    public OperatorAnswerCandidatesResponse OperatorAnswerCandidates(string? principal = null) => Get<OperatorAnswerCandidatesResponse>(AnswersPath(principal))!;

    /// <summary>The cancellable OperatorAnswerCandidates operation.</summary>
    public Task<OperatorAnswerCandidatesResponse> OperatorAnswerCandidatesAsync(string? principal = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync<OperatorAnswerCandidatesResponse>(nameof(OperatorAnswerCandidates), async token => (await GetAsync<OperatorAnswerCandidatesResponse>(AnswersPath(principal), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(OperatorAnswerCandidates)), cancellationToken);

    /// <inheritdoc />
    public OperatorAnswerResponse OperatorAnswer(OperatorAnswerRequest request) => AnswerOutcome(request, Post<OperatorAnswerRequest, OperatorAnswerResponse>("/v1/operator/answers", request));

    /// <summary>The cancellable OperatorAnswer operation.</summary>
    public Task<OperatorAnswerResponse> OperatorAnswerAsync(OperatorAnswerRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<OperatorAnswerResponse>(nameof(OperatorAnswer), async token => AnswerOutcome(request, await PostAsync<OperatorAnswerRequest, OperatorAnswerResponse>("/v1/operator/answers", request, cancellationToken: token).ConfigureAwait(false)), true, OperationTimeout?.Invoke(nameof(OperatorAnswer)), cancellationToken);

    // A complete act names itself and returns one distinct receipt for each selected ask; receipts with any problem but a reconciliation are contradictory.
    private static OperatorAnswerResponse AnswerOutcome(OperatorAnswerRequest request, OperatorAnswerResponse? response)
    {
        if (response?.Written is null || response.Written.Any(r => r is null || string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Hash) || string.IsNullOrWhiteSpace(r.Path))
            || response.Written.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != response.Written.Count
            || response.Ok && (request.Selection is null || response.Written.Count != request.Selection.Count || response.Written.Count > 0 && string.IsNullOrWhiteSpace(response.Act))
            || response.Written.Count > 0 && response.Problem is not null and not { Code: "reconcile-required" })
            throw new JsonException("The runtime returned no definite outcome of the act. Inspect the selected asks' decisions before answering again.");
        return response;
    }
}
