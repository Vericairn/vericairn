// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

namespace Vericairn.Protocol;

/// <summary>The loopback profile of a daemon: TLS on the loopback interface pinned to the runtime certificate; requests are device-signed.</summary>
/// <param name="Host">Loopback address.</param>
/// <param name="Port">Port.</param>
/// <param name="Certificate">SHA-256 fingerprint of the runtime certificate as sha256:hex; clients accept no other.</param>
public sealed record LoopbackEndpoint(string Host, int Port, string Certificate);

/// <summary>Where a daemon serves an estate on a machine, as published beside the estate's live store.</summary>
/// <param name="Kind">unix (a socket file) or pipe (a named pipe).</param>
/// <param name="Address">Socket path or pipe name.</param>
/// <param name="ProcessId">The daemon's process id.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="Root">The estate root it serves.</param>
/// <param name="Api">API version.</param>
/// <param name="Loopback">The loopback profile, when served.</param>
public sealed record DaemonEndpoint(string Kind, string Address, int ProcessId, string StartedAt, string Root, string Api, LoopbackEndpoint? Loopback = null);
