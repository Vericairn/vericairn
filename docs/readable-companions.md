# Readable companion contracts

Companions extend the READ-01 immutable generator and verification contracts.
They introduce no command, API operation or record field. Source selection and
authorization precede rendering; a rendition grants no authority or acceptance.

## Record coverage

`DocumentCoverage.RecordKinds` inventories every admitted type, its top-level
arrays and the owned nested collections in `RecordCollections`. The foundation
registers item and list bindings through `RecordCompanions`. A new admitted type
must explicitly join that supported set. Fixture conformance also checks nested
collections and that every retained body field appears in its reader.

`vericairn.records/items` contract 1 accepts `format=1`, `audience=Internal`,
`selection=item|list`, `field`, `index` and `page`. `field=body` reads a record or
lists the complete selected revision inventory; `index=all` applies to that root
view and to nested lists. A nested field uses its registered schema path, such as
`duties[].proofs`; an item selects a positive, one-based index in recorded order
within one exact parent revision. The positive, one-based page selects bounded
detail. An unavailable member or page returns unavailable, with no output bytes.
Missing optional collections say that they were not recorded; an empty selected
array says that none were recorded.

Readers explain fields using the owning schema and preserve nested values,
array order and exact references. Open observation facts and policy contribution
parameters retain their nested structure without claiming to evaluate it.
Contributor claims, observations, reviews and acceptances have distinct notices.
Units, missing values, unreported usage and exact reference hashes remain visible.
Long values continue across explicitly labeled pages without splitting Unicode
scalars or escaped markup. Full detail is available at the same immutable basis.

The implementation digest covers retained embedded renderer and schema sources,
ordered by resource identity, independently of the enclosing output commit.
Canonical output and ordinary content checks remain owned by `DocumentRendering`.
