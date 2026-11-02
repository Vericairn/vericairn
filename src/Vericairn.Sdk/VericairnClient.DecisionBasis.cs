// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public DecisionBasisResponse DecisionBasis(string? baseCommit = null) => Get<DecisionBasisResponse>(DecisionBasisPath(baseCommit))!;

    /// <inheritdoc />
    public Task<DecisionBasisResponse> DecisionBasisAsync(string? baseCommit = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(DecisionBasis), async token => (await GetAsync<DecisionBasisResponse>(DecisionBasisPath(baseCommit), cancellationToken: token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(DecisionBasis)), cancellationToken);

    private static string DecisionBasisPath(string? baseCommit) => "/v1/decisions/basis" + (string.IsNullOrEmpty(baseCommit) ? "" : "?base=" + Uri.EscapeDataString(baseCommit));
}
