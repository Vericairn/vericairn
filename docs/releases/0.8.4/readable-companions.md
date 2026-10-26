# Readable companion contracts

Saved prompts and prompt preferences have record readers and collection coverage.
Assignment, run-operation and ask-answer readers retain the exact prompt source,
revision or version, definition, placeholder values and rendered text. Nested
placeholder, tag, role and value collections are registered with the same coverage
inventory. Records without a prompt use keep their existing rendering.

Vericairn 0.8.0 exposes the immutable generator and verification
contracts through the docs commands, API, SDK, MCP and Workbench. These are read
models, with no new record type or record field. Source selection and authorization
precede rendering; a rendition grants no authority, acceptance or authored clearance.

## Reading and comparing

`vericairn docs companions` lists installed item and list coverage, input contracts,
bounded no-reader reasons and actual current declarations. A possible register path
in a generator descriptor is not an adopted destination. Use `--json` to retain the
exact `snapshot` for record collections or `manifestSnapshot` for the manifest.
Coverage also names the existing brief, context, handoff and communication readers;
their original-content, withholding and measurement contracts remain intact.

```sh
vericairn docs render vericairn.records/items --selection item --revision <revision-id>
vericairn docs render vericairn.records/items --selection list --revision <revision-id> --field effects
vericairn docs render vericairn.records/decisions --selection current --snapshot <snapshot-digest>
vericairn docs render vericairn.estate/manifest --selection item --snapshot <manifest-snapshot-digest>
vericairn docs render vericairn.records/items --selection item --revision <revision-id> --sources
vericairn docs regenerate <declared-path-or-declaration-id> --compare
```

An item retains the exact immutable revision even after a successor appears.
Collections require `current` or `all-revisions` and an explicit snapshot from the
runtime; an obsolete snapshot returns unavailable. Refresh coverage explicitly to
choose a new basis. `--page` and `--index` are one-based; a nonexistent page or nested
member is unavailable, not an empty list. Use a nested field's registered schema
path, such as `duties[].proofs`. `--contract` selects the exact installed contract.
`--sources` selects and checks the same source basis without invoking the generator.

A retained software companion uses `--selection item --declaration <declaration-id>`
and `--commit <full-commit>`. The runtime reads that exact source selection through
its registered adapter. Missing source commits never fall back to the clone head.
These reads do not select an ambient file or accept supplied source bytes.

Responses identify the audience, exact source ids and hashes, record lineage at
the selected snapshot, generator implementation, input digest and rendition hash.
`current` describes the selected record lineage or collection observation;
`historical` describes an earlier revision, an all-revisions collection or a fixed
declared source basis. A judgment about a superseded or withdrawn subject is also
historical; the response carries the owning model's explanation. Neither label is
an acceptance. Completeness is explicit:
`selected-page`, `bounded-overview`, `source-basis`, or `unavailable`; the rendition
labels its page or partial overview and how to inspect complete authoritative detail.

`docs regenerate --compare` checks bytes and complete membership without writing
output, a journal, a pin or a review. Missing, changed and unexpected members are
distinct. It exits zero only when current, one for differences, and nonzero when
unavailable or invalid. External comparisons require `--commit <full-commit>`.
Without `--compare`, the existing local repair behavior applies. Repair of an
authored generated region preserves everything outside the markers and cannot
clear its authored review. Whole generated members acquire no document-revision
pins. Strict adoption and explicit legacy conversion remain separate operations.

## Surface contract and access

| CLI | GET route | SDK sync / async | MCP read tool |
|---|---|---|---|
| `docs companions` | `/v1/companions` | `Companions` / `CompanionsAsync` | `docs_companions` |
| `docs render` | `/v1/companions/render` | `CompanionRender` / `CompanionRenderAsync` | `docs_render` |
| `docs render --sources` | `/v1/companions/sources` | `CompanionSources` / `CompanionSourcesAsync` | `docs_sources` |
| `docs regenerate --compare` | `/v1/companions/compare` | `CompanionCompare` / `CompanionCompareAsync` | `docs_compare` |

Render and sources accept the same `CompanionRequest` query fields: `generator`,
`selection`, `revision`, `snapshot`, `page`, `field`, `index`, `audience`, `contract`,
`declaration` and `commit`. Compare takes `selection`, `commit` and `audience`.
Defaults are Internal, contract 1, page 1, field `body` and index `all`. MCP uses
decimal strings for page and contract. SDK async operations share the normal
operation deadline, cancellation and read-failure semantics. No operation is a write.

The runtime checks current device access and source validity before generation.
Only the complete authorized Internal projection is supported in this build;
Public and Members-only requests return unavailable without source metadata,
counts or a digest. Requesting an audience grants no access. Local inspectors
remain in their owning local surfaces; these routes and tools cannot render them.
Missing generators or unsupported contracts return unavailable and direct readers
to source inspection. An unavailable basis never becomes a successful empty view.

Workbench's **Work → Readable views** (`Ctrl+Shift+V`, or `Cmd+Shift+V`) opens the
collection catalog. Enter opens a collection or a selected exact record source.
The read-only rendition sits beside labeled source rows and the exact record
envelope or file basis. F5 renews access and freshness; page navigation retains
the selected snapshot. Failed refresh clears cached content and currentness.
Successful refresh retains the prior rendition with its own historical basis and
a bounded line comparison. Correct the authoritative source, then refresh;
generated text has no editor or submission action. See [Workbench](workbench.md).

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

Each generator has its own explicit versioned implementation identity. Its
implementation digest is SHA-256 of that identity's UTF-8 bytes alone; it does not
hash source files, assembly resources, build metadata or the enclosing output
commit. Comment-only edits and changes to unrelated record types therefore leave
existing members unchanged. The estate and installed package pins identify the
implementation that supplies the version.

Bump the affected generator's implementation version whenever its emitted bytes
or meaning change, including changes in shared rendering code that affect it.
Golden-byte fixtures under `tests/fixtures/readable-companions` are keyed by that
identity and compare exact bytes, including the input digest. A byte change under
the same version fails; a version bump requires new fixtures. Retain earlier
versions and regenerate into a new version directory. Canonical output and
ordinary content checks remain owned by `DocumentRendering`.

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
record. The runtime exposes these exact selections through the read operations above.

## Local metadata inspectors

`LocalCompanions` supplies runtime read models for clone identity observations,
device custody, effective worker choices and launcher capabilities, runtime
settings, certificate dates, connections, write outcomes, run observations,
retained context accounting and recovery conditions. Adapters supply an explicit
observation instant and a complete selected snapshot. Readers never open ambient
storage, probe a launcher, contact an endpoint or infer present liveness.

`vericairn.local/inspectors` contract 1 takes `format=1`, `audience=Internal`,
`selection=item|list`, `field`, `index` and `page`. The runtime projection removes
all fields outside each metadata schema before calculating the input digest.
Missing or invalid metadata remains unknown. Root rows have canonical metadata
order; nested observations retain source order. Registered nested collections
support list and one-based item views. An incomplete snapshot is unavailable,
distinct from an empty complete list. Large lists use labeled bounded pages.

Coverage includes bounded no-reader reasons for private keys, recovery secrets,
pairing codes, bearer values, replay entries, copied environments, raw logs,
context bodies, tool configuration, machine bindings and storage internals.
All renditions omit machine paths, including local and shareable views. Only
projected canonical metadata can enter the local generator; direct submissions
containing extra fields fail with no bytes or digest. Error messages omit the
original exception and payload. Changes to excluded fields cannot change a
metadata rendition or its digest.

Worker choices show their explicit origin and observed compatibility. Launcher
availability stays unknown until separately observed. A clone adapter checks a
binding against the supplied repository identity before supplying its finding;
bindings and exception messages never enter the output. The write ledger keeps
intent/outcome identities and existing operation route templates, with no request
arguments. Certificate dates use the requested instant and establish no trust.
Device admission remains distinct from key possession; run observations remain
distinct from contributor claims or recorded acceptance. Retained context shows
hashes, byte accounting and unknown tokens without originals or presentations.
These local read models are not callable through the companion API or MCP tools.
