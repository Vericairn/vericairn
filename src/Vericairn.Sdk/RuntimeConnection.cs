// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json.Serialization;

namespace Vericairn.Sdk;

/// <summary>
/// A runtime this machine reaches over the local network, as its pairing pinned it, from 0.6.0: the address, the estate, the runtime
/// identity it must prove on every connection, and the device this machine is to it. Kept by <see cref="RuntimeConnections"/>.
/// </summary>
/// <param name="Id">The connection's id on this machine.</param>
/// <param name="Address">Where the runtime listens: address and port, an IPv6 address in brackets.</param>
/// <param name="Estate">The estate's id.</param>
/// <param name="EstateName">The estate's name.</param>
/// <param name="Identity">The runtime identity's public key, base64url SubjectPublicKeyInfo, pinned at pairing.</param>
/// <param name="Device">The device this machine pairs as: its record's id, which names its key here.</param>
/// <param name="DevicePublicKey">That device's public key, base64url SubjectPublicKeyInfo.</param>
/// <param name="Principal">The principal the device acts for.</param>
/// <param name="PrincipalName">Its name.</param>
/// <param name="Profiles">The connectivity profiles the device was permitted.</param>
/// <param name="PairedAt">When this machine paired.</param>
/// <param name="Observed">What the latest check found.</param>
/// <param name="LastCurrent">The latest check that found the connection current, which a cached state shows the freshness of.</param>
public sealed record RuntimeConnection(string Id, string Address, string Estate, string? EstateName, string Identity, string Device, string DevicePublicKey, string Principal, string? PrincipalName, IReadOnlyList<string> Profiles, string PairedAt, ConnectionObservation? Observed = null, ConnectionObservation? LastCurrent = null)
{
    /// <summary>The host part of the address.</summary>
    [JsonIgnore]
    public string Host => TryParseAddress(Address, out string host, out _) ? host : Address;

    /// <summary>The port part of the address.</summary>
    [JsonIgnore]
    public int Port => TryParseAddress(Address, out _, out int port) ? port : 0;

    /// <summary>Reads an explicit address: a host name or an IP address, and a port; an IPv6 address in brackets, [fe80::1]:7443.</summary>
    public static bool TryParseAddress(string? text, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        string portText;
        if (text.StartsWith('['))
        {
            int close = text.IndexOf("]:", StringComparison.Ordinal);
            if (close < 2)
            {
                return false;
            }

            host = text[1..close];
            portText = text[(close + 2)..];
        }
        else
        {
            int colon = text.LastIndexOf(':');
            if (colon < 1 || text.IndexOf(':', StringComparison.Ordinal) != colon)
            {
                return false;
            }

            host = text[..colon];
            portText = text[(colon + 1)..];
        }

        return int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is > 0 and <= 65535 && host.Length > 0;
    }
}

/// <summary>What one check of a connection found.</summary>
/// <param name="State">One of the connection states: current, partial, denied, cached or unavailable.</param>
/// <param name="At">When.</param>
/// <param name="Message">What it means, in words.</param>
public sealed record ConnectionObservation(string State, string At, string Message);

/// <summary>A connection with the state its latest check found.</summary>
/// <param name="Connection">The connection, with that check recorded.</param>
/// <param name="State">connecting, current, cached, partial, denied or unavailable.</param>
/// <param name="Message">What it means, in words.</param>
public sealed record ConnectionStatus(RuntimeConnection Connection, string State, string Message);

/// <summary>
/// The connection states of the state language: what a client can truthfully say about its connection to a runtime. A connection
/// proves nothing about the state of workers, and disconnection alone is never revocation.
/// </summary>
public static class ConnectionStates
{
    /// <summary>A check is under way and nothing has answered yet.</summary>
    public const string Connecting = "connecting";

    /// <summary>The runtime proved the identity this device paired with, accepted the device, and answered every read.</summary>
    public const string Current = "current";

    /// <summary>No answer now; what the last current check saw is all there is, shown with its time.</summary>
    public const string Cached = "cached";

    /// <summary>The runtime proved its identity and accepted the device, but did not answer every read.</summary>
    public const string Partial = "partial";

    /// <summary>The runtime proved its identity and refused the device: revoked, not admitted, or not permitted the profile.</summary>
    public const string Denied = "denied";

    /// <summary>No answer, or none that proved the paired identity, and nothing cached.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>Every state, in the order a connection usually reaches them.</summary>
    public static IReadOnlyList<string> All { get; } = [Connecting, Current, Cached, Partial, Denied, Unavailable];

    /// <summary>
    /// The state a check comes to: denied when the runtime refused the device; current when it answered every read; partial when
    /// it proved its identity and answered some; otherwise cached when an earlier check was current, else unavailable.
    /// </summary>
    public static string Of(bool answered, bool denied, bool complete, bool cached) =>
        denied ? Denied : answered ? (complete ? Current : Partial) : cached ? Cached : Unavailable;
}

/// <summary>Why a connection to a runtime on the local network ended before any request: the runtime refused the device, or it did not prove its identity.</summary>
public sealed class RuntimeConnectionException : Exception
{
    /// <summary>Creates the exception.</summary>
    public RuntimeConnectionException()
        : this(ConnectionStates.Unavailable, "The connection to the runtime ended.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public RuntimeConnectionException(string message)
        : this(ConnectionStates.Unavailable, message)
    {
    }

    /// <summary>Creates the exception with a message and a cause.</summary>
    public RuntimeConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
        State = ConnectionStates.Unavailable;
    }

    /// <summary>Creates the exception for a state: denied or unavailable.</summary>
    public RuntimeConnectionException(string state, string message)
        : base(message)
    {
        State = state;
    }

    /// <summary>denied when the runtime refused the device; unavailable when it did not prove its identity or answer.</summary>
    public string State { get; }
}
