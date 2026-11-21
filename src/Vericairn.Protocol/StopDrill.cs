// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>
/// From 0.9.4 (OPS-21): run the timed stop drill on the runtime's machine. Its worker starts a scratch run, asks it to stop as
/// <c>worker stop</c> does, and records the time from the stop to containment and to the run's last write as a verification of the estate
/// (method <c>stop-drill</c>) signed by the acting principal's admitted device on that machine.
/// </summary>
/// <param name="By">The principal running the drill, whose admitted device on the runtime's machine signs its verification.</param>
/// <param name="GraceSeconds">The grace the stop names, 0 to 3600 seconds; the worker's default when null.</param>
/// <param name="LimitSeconds">How long from the stop to containment passes, 1 to 3600 seconds; 60 when null. A run not contained within it fails the drill, which is attention.</param>
public sealed record StopDrillRequest(string By, int? GraceSeconds = null, int? LimitSeconds = null);

/// <summary>
/// What the stop drill measured and recorded. Passed is true when the scratch run was contained within the limit; the times are null when
/// they could not be observed. Verification names the recorded evidence, which is written whenever there is a measurement to record.
/// </summary>
/// <param name="Ok">Whether the drill ran and its verification was recorded, whatever it measured.</param>
/// <param name="Message">What happened, in a sentence.</param>
/// <param name="Passed">Whether the scratch run was contained within the limit.</param>
/// <param name="RequestedAt">When the stop was requested.</param>
/// <param name="ContainedAt">When containment was observed, or null.</param>
/// <param name="StopToContainedMilliseconds">From the stop request to the observed containment, or null.</param>
/// <param name="LastWriteAt">When the scratch run last wrote, to the millisecond, or null.</param>
/// <param name="StopToLastWriteMilliseconds">From the stop request to the scratch run's last write, or null.</param>
/// <param name="GraceSeconds">The grace the stop named.</param>
/// <param name="LimitSeconds">The limit the drill held containment to.</param>
/// <param name="Verification">The recorded verification, or null.</param>
/// <param name="Problem">Why the drill was refused or could not measure, or null.</param>
public sealed record StopDrillResponse(bool Ok, string Message, bool Passed, string? RequestedAt, string? ContainedAt, long? StopToContainedMilliseconds,
    string? LastWriteAt, long? StopToLastWriteMilliseconds, int GraceSeconds, int LimitSeconds, RecordWritten? Verification, Problem? Problem = null);

public partial interface IEstateApi
{
    /// <summary>From 0.9.4 (OPS-21): runs the timed stop drill on the runtime's machine and records what it measured.</summary>
    StopDrillResponse StopDrill(StopDrillRequest request) => throw new NotSupportedException("The stop drill is unavailable from this runtime.");

    /// <summary>The cancellable drill; an interrupted receipt leaves the outcome unknown, so read the estate's verifications before running it again.</summary>
    Task<StopDrillResponse> StopDrillAsync(StopDrillRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(StopDrill), () => StopDrill(request), true, this as IClientOperationLifetime, cancellationToken);
}
