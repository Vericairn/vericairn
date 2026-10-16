// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>The loopback profile of a daemon: TLS on the loopback interface pinned to the runtime certificate; requests are device-signed.</summary>
/// <param name="Host">Loopback address.</param>
/// <param name="Port">Port.</param>
/// <param name="Certificate">SHA-256 fingerprint of the runtime certificate as sha256:hex; clients accept no other.</param>
public sealed record LoopbackEndpoint(string Host, int Port, string Certificate);

/// <summary>
/// The local-network profile of a daemon, from 0.6.0: mutual TLS at the address the operator configured, served only when one is
/// configured. A paired device presents a certificate its device key issued, checks the runtime's identity on every connection,
/// and signs every request.
/// </summary>
/// <param name="Host">The address it listens on, as configured.</param>
/// <param name="Port">The port it listens on.</param>
/// <param name="Identity">The fingerprint of the runtime identity that paired devices pin, as sha256:hex of its public key.</param>
public sealed record LocalNetworkEndpoint(string Host, int Port, string Identity);

/// <summary>Where a daemon serves an estate on a machine, as published beside the estate's live store.</summary>
/// <param name="Kind">unix (a socket file) or pipe (a named pipe); local-network for a client of a runtime on another machine.</param>
/// <param name="Address">Socket path or pipe name.</param>
/// <param name="ProcessId">The daemon's process id.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="Root">The estate root it serves.</param>
/// <param name="Api">API version.</param>
/// <param name="Loopback">The loopback profile, when served.</param>
/// <param name="LocalNetwork">The local-network profile, when the operator configured an address; from 0.6.0.</param>
public sealed record DaemonEndpoint(string Kind, string Address, int ProcessId, string StartedAt, string Root, string Api, LoopbackEndpoint? Loopback = null, LocalNetworkEndpoint? LocalNetwork = null);
