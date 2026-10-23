# Record formats, Vericairn 0.8.3

Vericairn 0.8.3 reads record formats 1, 2 and 3. It writes an estate's own format,
at most 3; new estates start at 3. API version 1 remains the transport contract.
Context, hand-over, reference inventories and readable renditions have their own
artifact contracts and are not additional record formats.

- [Record format 1](record-format-v1.md) defines the envelope, canonical JSON,
  content hashes, signatures and the original records.
- [Record format 2](record-format-v2.md) defines goals, routing, asks, living
  documents, persistent devices, worker accounting, communication and execution.
- [Record format 3](record-format-v3.md) adds policies, bounded exceptions,
  recurring-duty evidence, decision requirements, typed work placement, checked
  repository changes and the generated-document `pathPattern` contract.

A record keeps the format in its own envelope, its original bytes, hash and
signatures. Format-2 rules extend format 1, and format-3 rules extend format 2.
The admission rule is estate-wide: at format 2 or 3 an admission or revocation
counts only when signed or anchored by the signed move to format 2, whatever
format or date that admission claims. Policy and writing changes and duty
results/reviews have additional signature and exact-approval requirements.

After the target's accepted publication is recorded, an upgrade from 0.7.3
applies `record-format-3`, updates the manifest and appends the migration record.
A format-1 estate applies `record-format-2` first. No original record is rewritten.
A format-2 reader refuses the upgraded manifest before opening a writable estate;
update and synchronize every participating writer. The migration is forward-only
and idempotent, and creates no policies, grants, exceptions, placements or duty
completions. Installing a binary is separate from upgrading an estate.
[Installation](install.md#6-pinning-and-upgrading-a-published-release) gives the operating steps.

`records describe <type>` and the generated `record-reference.md` expose the
same build registry and identify the format that introduced each field. An older
reader's recognition of a format number alone does not prove that it implements
every later additive field or behavior described here.


## Published Vericairn dependency observations (0.8.3)

An existing `observation` record may have kind `vericairn-release`. This additive kind uses the
existing flat `facts` object in formats 1, 2 and 3; it adds no record type or format number. Its exact
subject is the estate charter. It reports a dependency the estate observed, and supplies no product
release acceptance, publication authority or signature qualification.

| Fact | Value |
| --- | --- |
| `repository`, `version`, `tag`, `commit` | `Vericairn/vericairn`, stable version, `v<version>`, and the full lower-case tag commit |
| `url`, `checksumsUrl`, `checksumsHash` | Canonical GitHub release-page and `SHA256SUMS` asset addresses, and SHA-256 of that file's exact bytes |
| `artifactNames`, `artifactPlatforms`, `artifactHashes` | Nonempty parallel arrays of unique canonical CLI and Workbench distributable names, runtime identifiers and their published SHA-256 hashes |
| `reads`, `writes` | Readable format numbers as decimal strings, and the writable integer format, supplied by the running target build |
| `observer`, `verifiedPlatform`, `signing` | `vericairn <version>+<commit>`, the platform whose archive was verified, and `unsigned developer build` |

The ordinary observation `asOf`, `summary` and `limitations` fields supply the time, outcome and
trust limits. The command verifies the platform's CLI archive against `SHA256SUMS` and resolves the tag
before and after that read before authoring an observation. A later reader validates the arrays,
identity and formats; an upgrade applies the same format-compatibility checks as a product release.

The manifest's existing `vericairn` object gains an optional `release` reference containing
`type: observation`, `id` and `hash`. Its version, repository, artifacts and format must agree with
that exact observation. Existing source and release manifests remain valid without the reference.
The existing migration `release` field may name this observation; its envelope subject must also pin
the observation's exact hash. Original product release references remain valid. A fresh release-mode
initialization needs no migration record because its manifest already pins the verified observation.
The bootstrap continues to consume the existing version and per-platform archive hashes.
