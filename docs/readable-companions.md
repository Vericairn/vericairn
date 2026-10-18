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

## Revision registers

For repository members the following contract-1 generators accept `format=1`,
`audience=Internal` and `selection=all-revisions`. Their declarations use the listed directory followed
by `/{month}/{date}-{slug}-{id}.md`.

| Generator | Type | Directory |
|---|---|---|
| `vericairn.records/decisions` | `decision` | `docs/registers/decisions` |
| `vericairn.records/work-orders` | `work-order` | `docs/registers/work` |
| `vericairn.records/releases` | `release` | `docs/registers/releases` |
| `vericairn.estate/policies` | `estate-policy` | `docs/registers/policies` |
| `vericairn.estate/policy-exceptions` | `policy-exception` | `docs/registers/policy-exceptions` |
| `vericairn.estate/writing-exceptions` | `writing-exception` | `docs/registers/writing-exceptions` |

Each file reads its exact revision and, when it supersedes another, the exact
predecessor identified by that record and its verified hash. It links both source
records, with no successor links or current labels. Other references retain their
recorded identities and hashes without joining current titles. Adding independent
records therefore adds disjoint files; a new month needs no new declaration.

The UTC envelope date supplies the month and date. ASCII letters and digits form
a lowercase slug, other runs collapse to a hyphen, and the slug is trimmed to
60 characters, with `item` as the empty fallback. The full revision id keeps equal
and non-ASCII titles distinct. The source title is retained inside the file.

Full fields use the same structured reader as on-demand detail. A member exceeding
1 MiB becomes an explicitly partial overview containing only the metadata named
in its descriptor and the exact source and full-detail routes. No statement is
silently truncated. Policy terms, duty schedules and occurrence targets remain
historical; expiry and other current findings require the owning evaluator.

The existing releases region overload still accepts its original product-identity
list and emits the original table. Named inputs select the versioned revision
contract and cannot silently reinterpret a legacy region declaration.

## On-demand collections and manifest

`DocumentOnDemand.Records` requires an explicit record snapshot digest and either
`all-revisions` or `current`. The existing estate model supplies current lineage
selection, including reconciliation; an invalid or conflicting basis is unavailable.
The corresponding registered generator renders the collection. The selected
snapshot, selection, row count and page appear in the rendition. A stale snapshot
fails rather than silently refreshing. Only a complete authorized selection can
render an empty list. Collection requests cannot acquire repository ownership.

`vericairn.estate/manifest` contract 1 takes `format=1` and `audience=Internal`.
`DocumentOnDemand.Manifest` supplies the exact manifest bytes, matching verified
estate basis and explicit snapshot. The reader explains the runtime and artifact
pins, the recorded or default storage profile, the original and selected current
charter, their original formats, and migration history. The manifest's current
record format is distinct from the format in which a charter was recorded.
Manifest and artifact-map coverage is registered through `DocumentCoverage`.
This generator refuses repository declarations; neither reader writes a file or
record. Snapshot selection is a read model for future surfaces, not a new operation.
