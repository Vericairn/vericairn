// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;

namespace Vericairn.Protocol;

/// <summary>
/// From 0.9.4 (ID-11; design/events-and-presence.md § 8): an agent session's registration with the runtime that keeps the estate. Its
/// transport, never its model, registers it and renews the lease while its process and connection live. People are never tracked by
/// their connections, so only an agent or a service principal registers.
/// </summary>
/// <param name="Principal">The principal the session acts for: on the local socket the one named; a device registers only its own principal.</param>
/// <param name="Label">What the session is called on surfaces, named without machine paths: at most 100 characters.</param>
/// <param name="Roles">The roster roles the session offers, at most 16; none offers every role the roster names its principal for.</param>
/// <param name="Capacity">How many steps routing may give the session at once, 1 to 16, by default 1.</param>
/// <param name="LeaseSeconds">How long the lease lasts without a heartbeat, 30 to 900 seconds, by default 90.</param>
public sealed record PresenceRegisterRequest(string? Principal = null, string? Label = null, IReadOnlyList<string>? Roles = null, int? Capacity = null, int? LeaseSeconds = null);

/// <summary>
/// From 0.9.4 (ID-11): a lease as its holder sees it, on registration and on each heartbeat. The lease identity is the holder's alone:
/// it renews and releases the lease, only for the principal (and the device) that registered it, and no listing shows it.
/// </summary>
/// <param name="Lease">The lease identity, which the holder's heartbeats and release name.</param>
/// <param name="Session">The session's public identity, which listings, attention and events name.</param>
/// <param name="Principal">The principal the session acts for, by the id of its first record.</param>
/// <param name="Label">The session's label.</param>
/// <param name="Roles">The roles it offers; empty for every role the roster names its principal for.</param>
/// <param name="Capacity">How many steps routing may give it at once.</param>
/// <param name="LeaseSeconds">How long the lease lasts without a heartbeat.</param>
/// <param name="HeartbeatSeconds">How often the transport renews it: a third of the lease.</param>
/// <param name="RegisteredAt">When it was registered (RFC 3339 UTC).</param>
/// <param name="ExpiresAt">When it lapses without another heartbeat (RFC 3339 UTC).</param>
public sealed record PresenceLease(string Lease, string Session, string Principal, string? Label, IReadOnlyList<string> Roles, int Capacity, int LeaseSeconds,
    int HeartbeatSeconds, string RegisteredAt, string ExpiresAt);

/// <summary>From 0.9.4 (ID-11): a registration's, heartbeat's or release's answer: the lease, or why there is none.</summary>
/// <param name="Lease">The lease after the operation; null when it failed, or after a release.</param>
/// <param name="Message">What happened, in words.</param>
/// <param name="Problem">Why nothing was done: not-found for a lease that lapsed and was dropped, was released or was never made, which the transport answers by registering again.</param>
public sealed record PresenceLeaseResponse(PresenceLease? Lease, string Message, Problem? Problem = null)
{
    /// <summary>True when the operation was done.</summary>
    public bool Ok => Problem is null;
}

/// <summary>
/// From 0.9.4 (ID-11): one registered session as surfaces show it, beside liveness and progress claims as a separate fact: a runtime's
/// local observation, never a record.
/// </summary>
/// <param name="Session">The session's public identity.</param>
/// <param name="Principal">The principal it acts for.</param>
/// <param name="Label">Its label.</param>
/// <param name="Roles">The roles it offers.</param>
/// <param name="Capacity">How many steps routing may give it at once.</param>
/// <param name="State">live, or lapsed once its lease expired without a heartbeat.</param>
/// <param name="RegisteredAt">When it was registered.</param>
/// <param name="RenewedAt">Its last heartbeat, or its registration.</param>
/// <param name="ExpiresAt">When its lease lapses, or lapsed.</param>
/// <param name="Holds">The assignments it holds that still hold their steps and are not settled: those routing gave it, and those it recorded attempts or progress on.</param>
/// <param name="Asks">The asks recorded about its lapse, one per assignment it holds, recommending reassignment.</param>
public sealed record PresenceEntry(string Session, string Principal, string? Label, IReadOnlyList<string> Roles, int Capacity, string State, string RegisteredAt,
    string RenewedAt, string ExpiresAt, IReadOnlyList<string> Holds, IReadOnlyList<string> Asks);

/// <summary>From 0.9.4 (ID-11): the sessions this runtime has registered, oldest first, as of a time.</summary>
/// <param name="Sessions">Each session, live or lapsed.</param>
/// <param name="AsOf">When they were read (RFC 3339 UTC).</param>
/// <param name="Problem">Why none were read, such as a runtime that keeps no presence.</param>
public sealed record PresenceResponse(IReadOnlyList<PresenceEntry> Sessions, string AsOf, Problem? Problem = null);

/// <summary>From 0.9.4 (ID-11): the bounds of presence leases.</summary>
public static class PresenceVocabulary
{
    /// <summary>A lease's length when a registration names none, in seconds.</summary>
    public const int DefaultLeaseSeconds = 90;

    /// <summary>The shortest lease, in seconds.</summary>
    public const int MinLeaseSeconds = 30;

    /// <summary>The longest lease, in seconds.</summary>
    public const int MaxLeaseSeconds = 900;

    /// <summary>The most steps one session takes at once.</summary>
    public const int MaxCapacity = 16;

    /// <summary>The most roles one session offers.</summary>
    public const int MaxRoles = 16;

    /// <summary>The longest label.</summary>
    public const int MaxLabelLength = 100;

    /// <summary>How long a lapse holding work stands before the runtime asks about reassignment, when neither goal nor roster sets limits.watch.reassignAfterSeconds.</summary>
    public const int DefaultReassignAfterSeconds = 600;

    /// <summary>How long a step routing gave a live session may wait for its first attempt before it counts as a lapse, in seconds.</summary>
    public const int SessionAttemptSeconds = 300;

    /// <summary>The states a session is in.</summary>
    public static ImmutableArray<string> States { get; } = ["live", "lapsed"];
}

public partial interface IEstateApi
{
    /// <summary>
    /// From 0.9.4 (ID-11): registers an agent session's presence and returns its lease, renewed by its transport's heartbeats. The
    /// daemon keeps presence; an embedded runtime answers with a problem.
    /// </summary>
    PresenceLeaseResponse PresenceRegister(PresenceRegisterRequest request) => throw new NotSupportedException("Presence is unavailable.");

    /// <summary>The cancellable bridge of <see cref="PresenceRegister"/>.</summary>
    Task<PresenceLeaseResponse> PresenceRegisterAsync(PresenceRegisterRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(PresenceRegister), () => PresenceRegister(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>
    /// From 0.9.4 (ID-11): renews a lease, by its holder only; a lapsed lease the runtime still keeps is restored, which withdraws the
    /// asks its lapse raised. A lease the runtime no longer keeps answers not-found, and the transport registers again.
    /// </summary>
    PresenceLeaseResponse PresenceHeartbeat(string lease) => throw new NotSupportedException("Presence is unavailable.");

    /// <summary>The cancellable bridge of <see cref="PresenceHeartbeat"/>.</summary>
    Task<PresenceLeaseResponse> PresenceHeartbeatAsync(string lease, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(PresenceHeartbeat), () => PresenceHeartbeat(lease), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>From 0.9.4 (ID-11): releases a lease, by its holder only, when its session ends; a release is no lapse.</summary>
    PresenceLeaseResponse PresenceRelease(string lease) => throw new NotSupportedException("Presence is unavailable.");

    /// <summary>The cancellable bridge of <see cref="PresenceRelease"/>.</summary>
    Task<PresenceLeaseResponse> PresenceReleaseAsync(string lease, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(PresenceRelease), () => PresenceRelease(lease), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>From 0.9.4 (ID-11): the sessions this runtime registered, live and lapsed, or one principal's; never a lease identity.</summary>
    PresenceResponse Presence(string? principal = null) => throw new NotSupportedException("Presence is unavailable.");

    /// <summary>The cancellable bridge of <see cref="Presence"/>.</summary>
    Task<PresenceResponse> PresenceAsync(string? principal = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Presence), () => Presence(principal), false, this as IClientOperationLifetime, cancellationToken);
}
