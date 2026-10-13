// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using Vericairn.Protocol;
using Vericairn.Records.Signing;

namespace Vericairn.Sdk;

/// <summary>Signs requests with a device key held in one of this machine's key stores.</summary>
public sealed class DeviceRequestSigner : IRequestSigner
{
    private readonly IDeviceKeyStore store;

    /// <summary>A signer for a device whose key the store holds.</summary>
    public DeviceRequestSigner(IDeviceKeyStore store, string deviceId)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (!store.Has(deviceId))
        {
            throw new InvalidOperationException($"This machine holds no key for device {deviceId} in the {store.Kind} store.");
        }

        DeviceId = deviceId;
    }

    /// <inheritdoc />
    public string DeviceId { get; }

    /// <summary>The signer for a device whose key any store on this machine holds, or null.</summary>
    public static DeviceRequestSigner? Find(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        IDeviceKeyStore? store = DeviceKeyStores.All().FirstOrDefault(s => s.Has(deviceId));
        return store is null ? null : new DeviceRequestSigner(store, deviceId);
    }

    /// <inheritdoc />
    public byte[] Sign(ReadOnlySpan<byte> digest) => store.Sign(DeviceId, digest);
}
