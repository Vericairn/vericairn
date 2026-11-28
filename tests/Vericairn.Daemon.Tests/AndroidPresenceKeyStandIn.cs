// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Signing;
using Vericairn.Runtime.Tests;

namespace Vericairn.Daemon.Tests;

internal sealed class AndroidPresenceKeyStandIn(string home, SyntheticAndroidAttestation fixture) : IAttestedDeviceKeyStore
{
    private readonly PresenceKeyStandIn keys = new(home, DeviceKeyStores.AndroidKeystoreKind);
    private readonly Dictionary<string, IReadOnlyList<string>> chains = new(StringComparer.Ordinal);

    public string Kind => keys.Kind;
    internal int Signatures => keys.Signatures;
    internal bool Decline { get => keys.Decline; set => keys.Decline = value; }
    public bool Has(string deviceId) => keys.Has(deviceId);
    public byte[] Create(string deviceId) => keys.Create(deviceId);
    public byte[]? PublicKey(string deviceId) => keys.PublicKey(deviceId);
    public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest) => keys.Sign(deviceId, digest);
    public void Promote(string keyId, string deviceId) => keys.Promote(keyId, deviceId);

    public byte[] Create(string deviceId, byte[] challenge)
    {
        byte[] publicKey = keys.Create(deviceId);
        chains.Add(deviceId, fixture.Issue(publicKey, challenge));
        return publicKey;
    }

    public IReadOnlyList<string> Attestation(string deviceId) => chains[deviceId];

    public void Remove(string deviceId)
    {
        chains.Remove(deviceId);
        keys.Remove(deviceId);
    }
}
