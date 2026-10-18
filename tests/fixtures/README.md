# Test fixtures

`format-1-estate/` is a small estate written by the released Vericairn 0.4.2 binary in record format 1: an operator
and a builder, a decision, a grant, a work order, a product and a release with its acceptance and publication. It is
golden: its bytes never change. The tests read it with the current build, which must find it valid, rewrite none of
its records and move it to record format 2 with a migration that adds only the migration record.

`format-3-policy-estate/` is a synthetic estate covering policy declarations and retirement, exact definition
approvals, all three bounded grants, writing additions and removals, reconciliation, policy exceptions and
revocation, placement, duty schedules, result evidence, ordinary review reads and ended-unmet debt. Its admitted
devices were generated for this fixture; only public keys and signatures are retained. No private key or runtime
storage is part of the fixture. `inventory.sha256` pins every retained byte. Records tests validate the estate,
require coverage of every format-3 field in the registry and reconstruct every envelope hash without changing
its body or signatures. Runtime migration tests use independently created, disposable estates.
READ-01 adds a signed pattern living-document declaration. `PolicyGoldenFixtureWriter` regenerates this
synthetic fixture with fresh in-memory test keys, rebinds exact references and approved definition hashes,
validates the complete result and writes a new inventory. This refresh changes fixture signatures and public
keys; it retains no private keys and touches neither the format-1 fixture nor frozen release documentation.
