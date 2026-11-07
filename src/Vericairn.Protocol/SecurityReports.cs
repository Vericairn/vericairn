// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>Read every declared repository's security advisories now, as the operator. Nothing is recorded or posted.</summary>
public sealed record SecurityReportsPollRequest(string By);

/// <summary>
/// Record the operator's triage of exactly one advisory revision this machine observed, named by its identifier, repository and
/// content hash. The triage carries the disposition and an optional reason in the operator's own words, never the advisory's text.
/// </summary>
public sealed record SecurityReportTriageRequest(string By, string Advisory, string Repository, string Expected, string Disposition, string? Reason = null);

/// <summary>One declared repository's latest read on this machine: when, by which reader, and whether its listing was complete.</summary>
public sealed record SecurityReportRead(string Repository, string Provider, string? At, bool Complete, string? Problem);

/// <summary>One observed advisory revision: identity, state, severity and content hash only, and the triage that names it, if any.</summary>
public sealed record SecurityReportRow(string Advisory, string Repository, string State, string Severity, string ContentHash,
    string FirstSeen, string ChangedAt, string? Triage);

/// <summary>
/// The security reports this machine observed, for the operator only. Held is true while any declared repository is unconfirmed or
/// any observed revision is untriaged, which holds every release submission.
/// </summary>
public sealed record SecurityReportsResponse(bool Ok, string Message, IReadOnlyList<SecurityReportRead> Reads, IReadOnlyList<SecurityReportRow> Reports,
    bool Held = false, RecordWritten? Triage = null, Problem? Problem = null);

public partial interface IEstateApi
{
    /// <summary>Reads the declared repositories' security advisories now and returns what this machine observed; the operator's only.</summary>
    SecurityReportsResponse SecurityReportsPoll(SecurityReportsPollRequest request) => throw new NotSupportedException("Security reports are unavailable from this runtime.");

    /// <summary>Records the operator's triage of one observed advisory revision.</summary>
    SecurityReportsResponse SecurityReportsTriage(SecurityReportTriageRequest request) => throw new NotSupportedException("Security reports are unavailable from this runtime.");

    /// <summary>The cancellable read; a stopped client leaves this machine's observations as the read stored them.</summary>
    Task<SecurityReportsResponse> SecurityReportsPollAsync(SecurityReportsPollRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(SecurityReportsPoll), () => SecurityReportsPoll(request), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable triage; an interrupted receipt leaves the outcome unknown, so read the reports again before retrying.</summary>
    Task<SecurityReportsResponse> SecurityReportsTriageAsync(SecurityReportTriageRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(SecurityReportsTriage), () => SecurityReportsTriage(request), true, this as IClientOperationLifetime, cancellationToken);
}
