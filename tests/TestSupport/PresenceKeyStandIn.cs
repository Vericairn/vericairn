// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Signing;

namespace Vericairn.TestSupport;

/// <summary>
/// A test stand-in for a phone's presence store: owner-only file keys kept apart from the request keys, recorded under the phone
/// custody the real store states. It asks nobody for presence, so it proves the routing of signatures, never a prompt; a declined
/// prompt is simulated by <see cref="Decline"/>, and every signature it makes is counted.
/// </summary>
public sealed class PresenceKeyStandIn(string home, string kind = DeviceKeyStores.IosSecureEnclaveKind) : IDeviceKeyStore
{
    private readonly FileKeyStore inner = new(Path.Combine(home, "presence"));
    private int signatures;

    /// <inheritdoc />
    public string Kind => kind;

    /// <summary>How many digests this store signed.</summary>
    public int Signatures => Volatile.Read(ref signatures);

    /// <summary>When set, every signature fails as a declined presence prompt does, and nothing is signed.</summary>
    public bool Decline { get; set; }

    /// <inheritdoc />
    public bool Has(string deviceId) => inner.Has(deviceId);

    /// <inheritdoc />
    public byte[] Create(string deviceId) => inner.Create(deviceId);

    /// <inheritdoc />
    public byte[]? PublicKey(string deviceId) => inner.PublicKey(deviceId);

    /// <inheritdoc />
    public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest)
    {
        if (Decline) throw new InvalidOperationException("The person declined the presence prompt.");
        Interlocked.Increment(ref signatures);
        return inner.Sign(deviceId, digest);
    }

    /// <inheritdoc />
    public void Remove(string deviceId) => inner.Remove(deviceId);

    /// <inheritdoc />
    public void Promote(string keyId, string deviceId) => inner.Promote(keyId, deviceId);
}
