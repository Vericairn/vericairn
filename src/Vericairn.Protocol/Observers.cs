// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>
/// From 0.9.4 (OPS-20): declare an automated observer on this machine, such as CI on the main line, a scheduled workflow, an intake watcher
/// or a status refresher, with the interval it reports at and the grace its silence is allowed. The operator's; nothing is recorded.
/// </summary>
/// <param name="By">The operator declaring it.</param>
/// <param name="Name">A lower-case kebab-case name of at most 64 characters, unique on this machine for this estate.</param>
/// <param name="Kind">ci, scheduled-workflow, intake, status-refresh or other.</param>
/// <param name="IntervalSeconds">How often it reports, from 60 seconds to 31 days.</param>
/// <param name="GraceSeconds">How long past the interval its silence is allowed, up to 7 days; a quarter of the interval when null.</param>
/// <param name="Summary">What it observes, in a sentence of at most 300 characters.</param>
public sealed record ObserverDeclareRequest(string By, string Name, string Kind, int IntervalSeconds, int? GraceSeconds = null, string? Summary = null);

/// <summary>
/// From 0.9.4 (OPS-20): one report of a declared observer. An observer that could not observe, because its quota is exhausted, its schedule
/// is disabled or it is otherwise unavailable, reports not-observing with that reason, which is attention, never a pass.
/// </summary>
/// <param name="By">The principal the reporter acts for.</param>
/// <param name="Name">The declared observer's name.</param>
/// <param name="Outcome">passed, failed or not-observing.</param>
/// <param name="Reason">For not-observing only, and required there: quota-exhausted, schedule-disabled or unavailable.</param>
/// <param name="At">When the observation was made, RFC 3339 UTC seconds; now when null, and never more than a minute ahead of the runtime's clock.</param>
/// <param name="Detail">What was observed, in at most 300 characters.</param>
public sealed record ObserverReportRequest(string By, string Name, string Outcome, string? Reason = null, string? At = null, string? Detail = null);

/// <summary>From 0.9.4 (OPS-20): withdraw a declared observer, so that its silence stops being attention. The operator's, with a reason.</summary>
public sealed record ObserverWithdrawRequest(string By, string Name, string Reason);

/// <summary>An observer's latest report on this machine.</summary>
/// <param name="At">When the observation was made.</param>
/// <param name="Outcome">passed, failed or not-observing.</param>
/// <param name="Reason">Why it did not observe, for not-observing.</param>
/// <param name="By">The principal the reporter acted for.</param>
/// <param name="Detail">What was observed, when given.</param>
/// <param name="ReceivedAt">When this machine received it.</param>
public sealed record ObserverReport(string At, string Outcome, string? Reason, string By, string? Detail, string ReceivedAt);

/// <summary>
/// One declared observer as this machine sees it now. State is awaiting (declared, its first report not yet due), observing (its latest
/// report passed and is recent enough), failed (its latest report failed), not-observing (its latest report says it could not observe)
/// or silent (no report within its interval and grace); every state but awaiting and observing is attention for the operator.
/// </summary>
/// <param name="Name">Its name.</param>
/// <param name="Kind">ci, scheduled-workflow, intake, status-refresh or other.</param>
/// <param name="IntervalSeconds">How often it reports.</param>
/// <param name="GraceSeconds">How long past the interval its silence is allowed.</param>
/// <param name="Summary">What it observes, when declared.</param>
/// <param name="DeclaredAt">When it was declared.</param>
/// <param name="DeclaredBy">The operator who declared it.</param>
/// <param name="LastReport">Its latest report on this machine, or null.</param>
/// <param name="State">awaiting, observing, failed, not-observing or silent.</param>
/// <param name="ExpectedBy">When its silence becomes attention: its latest report, or its declaration, plus its interval and grace.</param>
/// <param name="Attention">Whether its state is attention for the operator.</param>
/// <param name="Why">Its state, in a sentence.</param>
public sealed record ObserverRow(string Name, string Kind, int IntervalSeconds, int GraceSeconds, string? Summary, string DeclaredAt, string DeclaredBy,
    ObserverReport? LastReport, string State, string ExpectedBy, bool Attention, string Why);

/// <summary>The observers declared on this machine for this estate, each with its latest report and state as of the runtime's clock.</summary>
public sealed record ObserversResponse(bool Ok, string Message, string AsOf, IReadOnlyList<ObserverRow> Observers, Problem? Problem = null);

public partial interface IEstateApi
{
    /// <summary>From 0.9.4 (OPS-20): the observers declared on this machine, with their latest reports and states. Reading changes nothing.</summary>
    ObserversResponse Observers() => throw new NotSupportedException("Observers are unavailable from this runtime.");

    /// <summary>From 0.9.4 (OPS-20): declares an automated observer on this machine, or changes its declaration; the operator's.</summary>
    ObserversResponse ObserverDeclare(ObserverDeclareRequest request) => throw new NotSupportedException("Observers are unavailable from this runtime.");

    /// <summary>From 0.9.4 (OPS-20): keeps one report of a declared observer on this machine.</summary>
    ObserversResponse ObserverReport(ObserverReportRequest request) => throw new NotSupportedException("Observers are unavailable from this runtime.");

    /// <summary>From 0.9.4 (OPS-20): withdraws a declared observer on this machine; the operator's, with a reason.</summary>
    ObserversResponse ObserverWithdraw(ObserverWithdrawRequest request) => throw new NotSupportedException("Observers are unavailable from this runtime.");

    /// <summary>The cancellable read; it changes nothing.</summary>
    Task<ObserversResponse> ObserversAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Observers), Observers, false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable declaration; an interrupted receipt leaves the outcome unknown, so read the observers before retrying.</summary>
    Task<ObserversResponse> ObserverDeclareAsync(ObserverDeclareRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ObserverDeclare), () => ObserverDeclare(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable report; an interrupted receipt leaves the outcome unknown, so read the observers before retrying.</summary>
    Task<ObserversResponse> ObserverReportAsync(ObserverReportRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ObserverReport), () => ObserverReport(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable withdrawal; an interrupted receipt leaves the outcome unknown, so read the observers before retrying.</summary>
    Task<ObserversResponse> ObserverWithdrawAsync(ObserverWithdrawRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ObserverWithdraw), () => ObserverWithdraw(request), true, this as IClientOperationLifetime, cancellationToken);
}
