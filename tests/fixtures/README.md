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

`canonical-status/` holds the canonical status JSON (`EstateStatus.RenderJson` without repository checks) of each estate
above, as main rendered it before PROC-06 (0.9.2). An estate without runs keeps these exact bytes, and so the
`statusHash` a person's observation pins; a test renders each estate with the current build and compares, the
policy estate's `documents` member apart, since its living document names a generator only tests install. A later
change to canonical status for estates without runs updates these files deliberately.

`flows/vericairn-first-party-release-1.0.0.json` is a copy of Vericairn's first-party extension of the software pack's
release flow, the flow file the first-party estate keeps under its own `docs/templates`; it is never part of the software
pack. Records tests install it byte for byte into a test estate, check that it pins the shipped release flow by hash and run
its steps. A change to the shipped release flow changes that hash, so the copy and the estate's file are updated together.
