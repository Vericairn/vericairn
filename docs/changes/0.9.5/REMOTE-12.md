# REMOTE-12: Verify an Android presence key during pairing

## docs/limitations-and-platforms.md

### In "Pairing and the local-network profile", after the bullet beginning "Pairing codes contain"

> From 0.9.5 (REMOTE-12), the runtime verifies that an Android presence key was generated in hardware and requires authentication at each use. The certificate chain must lead to a pinned Google attestation root, pass Google's certificate status check and match the exchange's fresh challenge and the signing key being paired. If verification fails, the runtime refuses the pairing. The runtime retrieves the status list from Google without a phone or estate identifier. iOS App Attest cannot establish the per-use access controls of a separate Secure Enclave key, so its presence requirement is recorded as `declared`. Earlier paired phone keys without verification are also reported as declared. A declared key does not qualify as a verified presence signer.

## docs/api-compatibility-0.9.5.md

### Under the 0.9.5 changes

> REMOTE-12 adds optional `presenceAttestation` to `PairingHello` and `PairingChallenge`, and `androidAttestation` to `PairingDevice`. `IAttestedDeviceKeyStore` supplies the platform evidence for a newly generated key. The device record can distinguish `presence: declared` from `presence: required` and include `presenceVerification: android-key-attestation`. Device lists may report `declared`. An earlier client's request-only pairing remains supported. Update a client before pairing any new presence key.
