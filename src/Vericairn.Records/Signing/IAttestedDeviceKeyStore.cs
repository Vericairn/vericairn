// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Signing;

/// <summary>A device store that can request a platform attestation when creating a presence key.</summary>
public interface IAttestedDeviceKeyStore : IDeviceKeyStore
{
    /// <summary>Creates a key for the pairing challenge and returns its public key as SubjectPublicKeyInfo.</summary>
    byte[] Create(string deviceId, byte[] challenge);

    /// <summary>The certificate chain of the created key, leaf first in base64url DER.</summary>
    IReadOnlyList<string> Attestation(string deviceId);
}
