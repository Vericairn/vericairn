// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// A test stand-in for custody that asks its owner's presence at every signature (FIX-124): owner-only file keys under the signing home,
/// stated under the custody kind given, the macOS Secure Enclave's by default. It asks nobody: it keeps the reason each prompt would
/// show, in order, can run a check just before a signature, and can decline one as a person declining the prompt does.
/// </summary>
public sealed class RecordingCustody(string home, string kind = SecureEnclaveCustody.Kind) : IDeviceKeyStore
{
    private readonly FileKeyStore inner = new(home);
    private readonly List<string?> reasons = [];

    /// <summary>The reason each signature was asked with, in order; null for one asked without a reason.</summary>
    public IReadOnlyList<string?> Reasons
    {
        get
        {
            lock (reasons) return [.. reasons];
        }
    }

    /// <summary>The signature, counted from 1, that is declined as a person declining the prompt; 0 declines none.</summary>
    public int DeclineAt { get; set; }

    /// <summary>Runs just before each signature, with its number counted from 1.</summary>
    public Action<int>? BeforeSigning { get; set; }

    /// <inheritdoc />
    public string Kind => kind;

    /// <inheritdoc />
    public bool Has(string deviceId) => inner.Has(deviceId);

    /// <inheritdoc />
    public byte[] Create(string deviceId) => inner.Create(deviceId);

    /// <inheritdoc />
    public byte[]? PublicKey(string deviceId) => inner.PublicKey(deviceId);

    /// <inheritdoc />
    public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest) => Signed(deviceId, digest, null);

    /// <inheritdoc />
    public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest, string reason) => Signed(deviceId, digest, reason);

    /// <inheritdoc />
    public void Remove(string deviceId) => inner.Remove(deviceId);

    /// <inheritdoc />
    public void Promote(string keyId, string deviceId) => inner.Promote(keyId, deviceId);

    private byte[] Signed(string deviceId, ReadOnlySpan<byte> digest, string? reason)
    {
        int number;
        lock (reasons)
        {
            reasons.Add(reason);
            number = reasons.Count;
        }

        BeforeSigning?.Invoke(number);
        if (number == DeclineAt) throw new InvalidOperationException("The person declined the presence prompt.");
        return inner.Sign(deviceId, digest);
    }
}
