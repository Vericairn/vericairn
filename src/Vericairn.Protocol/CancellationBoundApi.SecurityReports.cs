// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public SecurityReportsResponse SecurityReportsPoll(SecurityReportsPollRequest request) => inner.SecurityReportsPollAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<SecurityReportsResponse> SecurityReportsPollAsync(SecurityReportsPollRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.SecurityReportsPollAsync(request, token), cancellationToken);
    /// <inheritdoc />
    public SecurityReportsResponse SecurityReportsTriage(SecurityReportTriageRequest request) => inner.SecurityReportsTriageAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<SecurityReportsResponse> SecurityReportsTriageAsync(SecurityReportTriageRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.SecurityReportsTriageAsync(request, token), cancellationToken);
}
