# Synthetic Android key-attestation certificates

The three DER files contain public test certificates generated offline with a disposable fixture CA and a P-256 leaf key.
They contain no private keys, Android device identifier, device capture or Google credential. Both leaves contain the same
public key and a KeyDescription with the 32-byte challenge `00` through `1f`. The `per-use` leaf has hardware authorizations
for user authentication and key generation. The `timed` leaf adds a 30-second authentication timeout. The verifier tests use
the fixture root and a fixed clock within the certificates' validity interval.
