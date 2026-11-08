// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public SecurityReportsResponse SecurityReportsPoll(SecurityReportsPollRequest request) => Post<SecurityReportsPollRequest, SecurityReportsResponse>("/v1/security-reports/poll", request)!;
    /// <inheritdoc />
    public SecurityReportsResponse SecurityReportsTriage(SecurityReportTriageRequest request) => Post<SecurityReportTriageRequest, SecurityReportsResponse>("/v1/security-reports/triage", request)!;
    /// <inheritdoc />
    public Task<SecurityReportsResponse> SecurityReportsPollAsync(SecurityReportsPollRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<SecurityReportsResponse>(nameof(SecurityReportsPoll), async token => (await PostAsync<SecurityReportsPollRequest, SecurityReportsResponse>("/v1/security-reports/poll", request, cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(SecurityReportsPoll)), cancellationToken);
    /// <inheritdoc />
    public Task<SecurityReportsResponse> SecurityReportsTriageAsync(SecurityReportTriageRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<SecurityReportsResponse>(nameof(SecurityReportsTriage), async token => (await PostAsync<SecurityReportTriageRequest, SecurityReportsResponse>("/v1/security-reports/triage", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(SecurityReportsTriage)), cancellationToken);
}
