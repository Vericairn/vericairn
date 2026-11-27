# Android key-attestation certificates

The DER files contain public test certificates generated offline with a disposable fixture CA and a P-256 leaf key.
Both leaves contain the same public key and a KeyDescription with the 32-byte challenge `00` through `1f`, a locked
bootloader, verified boot, and synthetic Remote app and patch metadata. The `per-use` leaf has hardware authorizations
for user authentication and key generation. The `timed` leaf adds a 30-second authentication timeout. The verifier tests
use the fixture root and a fixed clock within the certificates' validity interval.

The PEM certificates come from [android/keyattestation testdata](https://github.com/android/keyattestation/tree/main/testdata)
under [Apache License 2.0](https://github.com/android/keyattestation/blob/main/LICENSE). Each certificate chain matches the
corresponding blob in [upstream Git tree d23d7f5](https://api.github.com/repos/android/keyattestation/git/trees/d23d7f575fcb4831fa096116c8fcc2bd9e342e35?recursive=1),
with PEM line endings normalized by Git where needed.

| Fixture | Upstream path under `testdata` |
| --- | --- |
| `akita_sdk34_TEE_EC_NONE.pem` | `akita/sdk34/TEE_EC_NONE.pem` |
| `caiman_sdk36_SB_EC_RKP.pem` | `caiman/sdk36/SB_EC_RKP.pem` |
| `blueline_sdk28_TEE_EC_NONE.pem` | `blueline/sdk28/TEE_EC_NONE.pem` |
| `tegu_sdk36_TEE_EC_2026_ROOT.pem` | `tegu/sdk36/TEE_EC_2026_ROOT.pem` |
| `sony-xperia10-iii_sdk33_TEE_EC.pem` | `sony-xperia10-iii/sdk33/TEE_EC.pem` |

Akita, Caiman and Blueline carry alternate certificates for the pinned RSA key. Tegu carries the pinned EC certificate.
Sony exercises refusal of a path with invalid CA constraints. The tests use dates when the relevant leaf and intermediates
were valid. The public fixtures contain no private key and do not attest a Vericairn Remote per-use key.
