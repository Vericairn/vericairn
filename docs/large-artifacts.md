# Large private artifacts

New in 0.9.2. This guide keeps a private artifact larger than the estate's object limit, such as an
original recording, a video master or a dataset, with exact-byte custody, and recovers it cold. It
uses only records that exist today and adds no record type. The typed parts that
[DESIGN-42](#planned-what-design-42-adds) plans come later. In 0.9.3 the guide adds
[the three inventories of a cold recovery](#the-three-inventories-of-a-cold-recovery), which classify
every file of a restored estate.

Every identity and address below is synthetic: the store is `store.example.invalid`, and the
artifact is 9,437,184 bytes (9 MiB). Each JSON block names the file it is saved as, and the commands
use those names. Angle-bracketed values are yours to fill in: `<operator-id>` is the operator's
principal id, `<store-id>` is the id `records new` printed for the store, and so on. With `--json`,
`records new` and `work result` print the new record's `id` and `hash` as JSON. These examples run as
a check in the product's tests, so they validate against this release.

**At a glance**, for a person or an agent:

1. Hash and measure the artifact before it moves.
2. Register the store as a private `storage` resource, and name its credential by reference.
3. Record the intake as an operation: the intent first, then the copy, then the readback.
4. Name the artifact in the result by hash, bytes and media type, with no `location`, and give the
   store reference in the result's `evidence`.
5. Back up the store yourself, and keep the owner's agent instruction files with that backup.
   `estate export` carries neither.
6. After a restore, classify every file before anything writes to the restored copy: the exported
   payload, the supplemental sources and the housekeeping listed exactly. Preserve any other file
   and find where it came from.
7. Prove a recovery by reading the bytes back in an isolated place, and record that readback as a
   `verification`.

## Four kinds of content

| Kind | Where it lives | Size limit | In `estate export` |
| --- | --- | --- | --- |
| Signed records | `records/` in the estate repository | 1 MiB for each file outside `objects/` | Yes |
| Native objects | `objects/`, named by SHA-256 (`vericairn objects put`) | 8 MiB (8,388,608 bytes) each | Yes |
| Private external source media | A private store registered as a `storage` resource | The store's own | No |
| Distributable releases | Release destinations, through the release procedure | The provider's own | Only the release records |

Records hold claims, decisions and evidence. A native object holds a small output, which a result
names by its location, `objects/<64 hex digits>`. A private store holds working originals and
unreleased sources. A release is what you publish. `estate check` reports any file over 1 MiB outside
`objects/` and any object over 8 MiB (`layout.size`), so a large artifact never belongs in the estate
repository.

Custody confers neither adoption nor release status. A record that says a store holds an artifact
accepts nothing, adopts nothing and releases nothing. Acceptance is its own record, pinned to the exact
result. A release follows the release procedure, and publication stays the operator's. A working
original never needs to become a release to be kept.

## The custody descriptor

The descriptor is spread over existing records. The target keys below are this guide's conventions:
today's validator requires only that a target's values are strings of at most 500 characters. They
use the names DESIGN-42 plans for its typed descriptor, so moving to the typed field later is a copy.

| Descriptor part | Record and field | Rule |
| --- | --- | --- |
| Store identity | `resource` of kind `storage`, `location.url` | An absolute `https` URL, never a machine path or a mount point. A store with no https address, such as a removable volume, needs a stable https identity; DESIGN-42 plans `location.store` for it. |
| Reference and its type | The intake operation's `target.reference` and `target.referenceType` | The reference is store-relative, with no leading `/`, drive letter, backslash or `..`. The type is `content-addressed`, `immutable`, `versioned` (with `target.version`) or `mutable`. A mutable reference can't keep exact bytes; avoid it. |
| Hash, byte count and media type | `result.outputs[]`: `hash`, `bytes`, `mediaType`; repeated as strings in the target | `hash` is `sha256:` and 64 lower-case hex digits of the exact bytes. |
| Source and version | The operation's `identity`, `target.source` and `target.version`; the result's `provenance` (0.9.2) | `provenance.inputs` pins the intake revision by its hash. |
| Custody and visibility | The resource's `visibility: private`; `target.visibility` and `target.access` | `access` is `owner`, `estate`, `named` or `public`. |
| Authorization boundary | A `credential-reference` record, named in `target.credential`; the resource's `notes` | Only the operator, or a principal with the `local-engineering` scope, records an intake operation. |
| Never credentials | Every field | No secret, token, password, presigned or shared link, user information, query or fragment, anywhere. |

Name the store's credential by reference, without its value. Save as `store-credential.json`:

```json store-credential.json
{
  "name": "private-store-access",
  "purpose": "Read and write the private originals store.",
  "kind": "api-token",
  "holder": "<operator-id>",
  "store": "The operator's password manager, item Private originals store",
  "scopes": ["read vault/", "write vault/"],
  "check": "The store's access page lists the token as active.",
  "status": "active"
}
```

Register the store. Save as `store.json`:

```json store.json
{
  "kind": "storage",
  "name": "Private originals",
  "role": "Custody of unreleased originals above the object limit.",
  "location": {"url": "https://store.example.invalid/vault"},
  "visibility": "private",
  "notes": "Owned by the operator's account and reached under credential reference private-store-access, held outside the estate. Encrypted at rest; backed up to a second, separately keyed copy."
}
```

```sh
vericairn records new credential-reference --by <operator-id> --body store-credential.json
vericairn records new resource --by <operator-id> --body store.json
```

## Take in the exact bytes

First, hash and measure the artifact where it is now:

```sh
shasum -a 256 original.wav    # sha256sum original.wav on Linux
wc -c < original.wav
```

```powershell
(Get-FileHash -Algorithm SHA256 original.wav).Hash.ToLowerInvariant()
(Get-Item original.wav).Length
```

The records below carry the hash as `sha256:` followed by the 64 hex digits, and the byte count. The
artifact is too large to be a native object, and `objects put` refuses it:

```sh
vericairn objects put original.wav
```

```text
error: 9,437,184 bytes exceeds the 8,388,608-byte object limit; larger artifacts go to releases and are referenced by hash.
```

The refusal's advice is for distributable artifacts. A private original goes to the private store.

An intake is an operation, the record of an external effect. Record the intent before anything is
copied. Save as `intake.json`:

```json intake.json
{
  "provider": "manual",
  "action": "store-original",
  "identity": "private-store:vault/obj-0001",
  "intent": "Keep the original of candidate C-0001 in the private store, exact bytes.",
  "target": {
    "resource": "<store-id>",
    "reference": "vault/obj-0001",
    "referenceType": "immutable",
    "hash": "sha256:<sha256-hex>",
    "bytes": "9437184",
    "mediaType": "audio/wav",
    "source": "Candidate C-0001, final mix",
    "visibility": "private",
    "access": "owner",
    "credential": "<credential-id>"
  },
  "status": "not-started",
  "evidence": []
}
```

Copy the artifact to the store, then record the dispatch. Every later revision keeps the identity,
provider and action, as the record rules require, and keeps the target unchanged too. Save as
`intake-dispatched.json`:

```json intake-dispatched.json
{
  "provider": "manual",
  "action": "store-original",
  "identity": "private-store:vault/obj-0001",
  "intent": "Keep the original of candidate C-0001 in the private store, exact bytes.",
  "target": {
    "resource": "<store-id>",
    "reference": "vault/obj-0001",
    "referenceType": "immutable",
    "hash": "sha256:<sha256-hex>",
    "bytes": "9437184",
    "mediaType": "audio/wav",
    "source": "Candidate C-0001, final mix",
    "visibility": "private",
    "access": "owner",
    "credential": "<credential-id>"
  },
  "status": "dispatched",
  "dispatchedAt": "<dispatched-at>",
  "evidence": []
}
```

Then read the object back from the store into a fresh location, not the file you sent, and recompute
its hash and size. If both match the target, record the success. Save as `intake-observed.json`:

```json intake-observed.json
{
  "provider": "manual",
  "action": "store-original",
  "identity": "private-store:vault/obj-0001",
  "intent": "Keep the original of candidate C-0001 in the private store, exact bytes.",
  "target": {
    "resource": "<store-id>",
    "reference": "vault/obj-0001",
    "referenceType": "immutable",
    "hash": "sha256:<sha256-hex>",
    "bytes": "9437184",
    "mediaType": "audio/wav",
    "source": "Candidate C-0001, final mix",
    "visibility": "private",
    "access": "owner",
    "credential": "<credential-id>"
  },
  "status": "observed-success",
  "dispatchedAt": "<dispatched-at>",
  "observed": {
    "summary": "Read vault/obj-0001 back from the store: the SHA-256 and 9,437,184 bytes match the target.",
    "at": "<observed-at>",
    "reference": "vault/obj-0001"
  },
  "evidence": [
    {"summary": "Hash recomputed from the bytes read back", "hash": "sha256:<sha256-hex>", "location": "https://store.example.invalid/vault/obj-0001"}
  ]
}
```

```sh
vericairn records new operation --by <operator-id> --body intake.json
vericairn records new operation --by <operator-id> --supersedes <intent-id> --body intake-dispatched.json
vericairn records new operation --by <operator-id> --supersedes <dispatched-id> --body intake-observed.json
```

If the store has nothing at the reference, or returns other bytes, record a known failure instead,
with what you found. Save as `intake-failed.json`:

```json intake-failed.json
{
  "provider": "manual",
  "action": "store-original",
  "identity": "private-store:vault/obj-0001",
  "intent": "Keep the original of candidate C-0001 in the private store, exact bytes.",
  "target": {
    "resource": "<store-id>",
    "reference": "vault/obj-0001",
    "referenceType": "immutable",
    "hash": "sha256:<sha256-hex>",
    "bytes": "9437184",
    "mediaType": "audio/wav",
    "source": "Candidate C-0001, final mix",
    "visibility": "private",
    "access": "owner",
    "credential": "<credential-id>"
  },
  "status": "known-failure",
  "dispatchedAt": "<dispatched-at>",
  "observed": {
    "summary": "Read vault/obj-0001 back: 4,194,304 bytes with a different SHA-256. The copy is incomplete.",
    "at": "<observed-at>",
    "reference": "vault/obj-0001"
  },
  "evidence": [
    {"summary": "Hash recomputed from the bytes read back", "hash": "sha256:<found-sha256-hex>"}
  ]
}
```

```sh
vericairn records new operation --by <operator-id> --supersedes <dispatched-id> --body intake-failed.json
```

An operation goes from `not-started` to `dispatched` to `observed-success`; there is no shortcut from
the intent to success. Another copy after `known-failure` or `uncertain` is a new `dispatched`
revision under the same identity, with `reconciliation` saying how the earlier outcome was settled;
without it, the revision is refused.

Recording an operation performs nothing. `provider: manual` says that you act: Vericairn copies,
uploads and publishes nothing, and none of these records starts a release.

## Reconcile the target with the reference

The intake is successful only when the bytes at `target.reference` have the target's exact hash and
size. `observed.reference` repeats the reference, and the evidence carries the recomputed hash. If
you can't tell whether a copy landed, after a dropped connection for example, revise the operation to
`uncertain`. Then read the reference: record `observed-success` if it holds the exact bytes and
`known-failure` if it doesn't. Never start a second operation for the same reference; one current
operation holds each identity.

The work's result then names the same bytes. It needs an attempt; the
[operator's guide](operator-guide.md#5-the-work-loop-order-brief-assignment-attempt-result) covers the
work loop. Save as `result.json`:

```json result.json
{
  "outcome": "completed",
  "summary": "Took in the original of candidate C-0001. It is held in the private store, not in the estate.",
  "outputs": [
    {"name": "C-0001 original (external)", "hash": "sha256:<sha256-hex>", "bytes": 9437184, "mediaType": "audio/wav"}
  ],
  "evidence": [
    {"summary": "Held in the private store under vault/obj-0001; intake operation <intake-id>", "hash": "sha256:<sha256-hex>", "location": "https://store.example.invalid/vault/obj-0001"}
  ],
  "checks": [
    {"name": "readback", "outcome": "passed", "summary": "The store returned the exact hash and byte count."}
  ],
  "limitations": ["The payload is in neither the estate repository nor its export; recovering it needs the store's own backup."],
  "unresolvedEffects": [],
  "nextStep": "Review the result, and record a readback verification after each recovery drill.",
  "provenance": {
    "inputs": [{"name": "intake", "type": "operation", "id": "<intake-id>", "hash": "<intake-hash>"}]
  }
}
```

```sh
vericairn work result <attempt-id> --by <contributor-id> --body result.json
```

`<intake-id>` and `<intake-hash>` are the `observed-success` revision's. `provenance` is new in 0.9.2;
on 0.9.1, leave it out and rely on the evidence. Check the reconciliation before review: the output's
hash and bytes equal the target's, and the evidence names the same reference.

An output's `location` is a path in the estate repository, checked against the file when it is
there. For an external payload, leave it out. A location that starts with a URL scheme, such as
`"location": "https://store.example.invalid/vault/obj-0001"`, names no file. From 0.9.2 (FIX-97), a
new result with one is refused with `result.output-scheme`; a result already in the estate keeps its
reading and gets a warning.

## Refusals and failures

Vericairn never reads the external store, so it can't refuse a missing or changed external payload.
Your readback finds those cases, and you record them as failures.

| Case | What happens |
| --- | --- |
| An artifact over 8 MiB | `objects put` refuses it. `estate check` reports `layout.size` for a file over 1 MiB outside `objects/`. |
| A URL in an output `location` | A new result is refused with `result.output-scheme` (0.9.2). A rooted path, `..` or a backslash is refused with `result.output-path`. |
| Missing or other bytes at intake | Record `known-failure` with what you found, never `observed-success`. |
| Missing or other bytes at a readback | Record a `verification` with `outcome: failed`. |
| A native object or estate file with other bytes | `objects verify` reports the object. A new result naming a present file with other bytes is refused with `result.output-hash`. `estate restore` refuses an export whose member differs from its inventory. |

## Back up what the export doesn't carry

`estate export` writes a zip with an inventory of each member's path, byte count and SHA-256, and from
0.9.3 its mode, executable or not. It includes:

- every record file and every native object;
- the `estate/` directory with its manifest, `design/`, `docs/`, `README.md`, `LICENSE`, the bootstrap
  scripts, `.gitattributes`, `.gitignore`, `.mailmap` and `.github`.

It excludes keys, sessions, credentials and authority, the machine's live store and its local notes,
the housekeeping native operations write under `.vericairn/`, and git history. It doesn't carry the
owner's agent instruction files at the root yet. It also excludes every payload held outside the
estate repository: an external store's artifacts and a release's files. Today its summary doesn't
list the external references it leaves out.

So recovery needs a supplemental capture: the store's own backup, made after the intake, that holds
every reference the records name. A snapshot, a versioned bucket or a second store all work. Encrypt
it, keep its key apart from it, and keep it under an account that can restore it without the live
store. Keep the owner's instruction files in the capture too, until the export carries them. To check
the capture without the estate, keep the list of its references and paths, hashes and byte counts
with it. The records can always rebuild the store's part of that list: each result output without a
`location`, with the reference its evidence names.

## Recover cold and prove the bytes

On the live estate, commit the records, note the commit that holds the result, and export:

```sh
git rev-parse HEAD
vericairn estate export --out estate-export.zip --by <operator-id>
```

Then recover in an isolated place: an offline machine or an isolated network, never the live store
or the live estate's directory. Restore the export into an empty directory and the store's backup
beside it, and read the restored records to see which bytes to recover:

```sh
vericairn estate restore --from estate-export.zip --to <restore-directory>
vericairn --estate <restore-directory> records show <result-id>
vericairn --estate <restore-directory> records show <intake-id>
```

The restore reports records, objects and documents. It says nothing about the external payload,
because the export never carried it. Before anything writes to the restored copy, classify every
file in it, as [the three inventories](#the-three-inventories-of-a-cold-recovery) explain; reading
the records writes nothing there. For each external output, read the bytes at the intake's
reference from the restored backup, then recompute their SHA-256 and size. If both match the restored
result, record the readback. Save as `readback.json`:

```json readback.json
{
  "method": "manual",
  "commit": "<estate-commit>",
  "environment": {
    "platform": "isolated restore",
    "isolation": "offline",
    "backupEncryption": "encrypted",
    "keyCustody": "separate",
    "backupOwner": "<operator-id>"
  },
  "role": "operator",
  "outcome": "passed",
  "summary": "Restored the estate export and the store's backup offline, then read vault/obj-0001 from the restored store: the SHA-256 and 9,437,184 bytes match the result.",
  "evidence": [
    {"summary": "Hash recomputed from the restored store's bytes", "hash": "sha256:<sha256-hex>", "location": "https://store.example.invalid/vault/obj-0001"}
  ],
  "limitations": ["Covers this object only, from this backup."],
  "evidenceClass": "observation",
  "observer": "person",
  "expires": "<readback-due>"
}
```

A missing or mismatched payload fails, with what was found. Save as `readback-failed.json`:

```json readback-failed.json
{
  "method": "manual",
  "commit": "<estate-commit>",
  "environment": {
    "platform": "isolated restore",
    "isolation": "offline",
    "backupEncryption": "encrypted",
    "keyCustody": "separate",
    "backupOwner": "<operator-id>"
  },
  "role": "operator",
  "outcome": "failed",
  "summary": "Restored the estate export and the store's backup offline. vault/obj-0001 is missing from the restored store, so the result's payload is not recovered.",
  "evidence": [],
  "limitations": ["The restored records name the payload; they don't recover it."],
  "evidenceClass": "observation",
  "observer": "person"
}
```

```sh
vericairn records new verification --by <operator-id> --subject result:<result-id>:<result-hash> --body readback.json
vericairn records new verification --by <operator-id> --subject result:<result-id>:<result-hash> --body readback-failed.json
```

The verification pins the result by its hash, and `commit` is the estate commit that holds the
result. Record it in the estate of record. After a drill, that is the live estate. When the restored
copy replaces a lost estate, classify its files first, then put the copy under git, commit it, use
that commit and record there:

```sh
git -C <restore-directory> init
git -C <restore-directory> add -A
git -C <restore-directory> commit -m "Restore the estate from its export"
git -C <restore-directory> rev-parse HEAD
vericairn --estate <restore-directory> records new verification --by <operator-id> --subject result:<result-id>:<result-hash> --body readback.json
```

Signing there needs an admitted device held on that machine, as
[the operator's guide](operator-guide.md#10-export-restore-recovery-and-divergence) explains. The
environment keys `isolation`, `backupEncryption`, `keyCustody` and `backupOwner` are the ones
DESIGN-42 plans; today they are ordinary environment keys, with string values.

The recovery report is the set of these verifications. The restore's counts, the restored records and
`recover inspect` show recovered metadata, never recovered payload bytes. Only a verification whose
evidence carries the hash recomputed from bytes read in the isolated place shows the bytes came back.
Repeat the readback when it expires, and whenever the store, its owner, its key or its backup changes.

## The three inventories of a cold recovery

A restored estate is verified file by file. Every file under the restored directory belongs to one of
three inventories, or to the records you write there after the restore, and its class decides how
you verify it. A file in none of them is unexpected: you find where it came from, and you never skip
it by a pattern or delete it.

| Inventory | What it holds | Its list | How you verify a file |
| --- | --- | --- | --- |
| The exported payload | Every record and native object, `estate/`, `design/`, `docs/`, `README.md`, `LICENSE`, `bootstrap`, `bootstrap.ps1`, `.gitattributes`, `.gitignore`, `.mailmap` and `.github` | `export/inventory.json` in the export: each member's `path`, `bytes`, `hash` and, from 0.9.3, `mode` | The file at `path` has exactly `bytes` bytes and the SHA-256 `hash`, and every member is present. On Linux and macOS a member whose `mode` is `100755` is executable, and no other member is. |
| Supplemental private or external sources | The store's backup, restored beside the estate and never inside it; the owner's agent instruction files at the root, `AGENTS.md` and the two pointer files [the MCP guide](mcp.md#owner-instructions-at-the-estate-root-093) names; a mirror of the git history, if you keep one | The list kept with the supplemental capture: each reference or path, byte count and SHA-256 | A payload by its readback, recorded as a verification; restored records never count as recovered bytes. An instruction file by its byte count and SHA-256 against the list. |
| Machine-local housekeeping | The files native operations write under `.vericairn/` for their own use, and the temporary file a stopped write leaves | [The lists below](#housekeeping-listed-exactly) | The path matches an entry, and a file whose content the entry fixes has exactly that content. |

`export/inventory.json` and `export/export.json` describe the export and aren't restored. The restore
writes one member with other bytes, on purpose: from an export whose `export/export.json` names
`"profile": "local-only"`, it writes `estate/manifest.json` with `"profile": "repository-synced"` in
that one place, since a restore always makes a repository-synced estate. Check that manifest against
the exported one with that value changed. From a repository-synced export, the manifest is restored
unchanged.

The estate check allows the owner's instruction files at the root from 0.9.3, but `estate export`
doesn't carry them yet, so they come back from the supplemental capture. Keys, sessions, the live
store and the runtime's state stay under the Vericairn home, never in the estate directory. None of
them belongs in an export or in a capture meant to travel with it, and neither does housekeeping.

### Housekeeping, listed exactly

Vericairn's commands and the bootstrap scripts write these files, and no others, under `.vericairn/`
in a repository-synced estate's working tree. The directories' own `.gitignore` files and the estate's `.gitignore` line
`.vericairn/` keep them out of commits, and `estate export` leaves them out.

| Path | Content | Written by |
| --- | --- | --- |
| `.vericairn/record-staging/.gitignore` | Two bytes, `*` and a line feed (hex `2a0a`) | Every record stored, `estate init` included |
| `.vericairn/write-lock/.gitignore` | Hex `2a0a` | Every write that takes the working tree's write lock, from 0.9.2: a stored record, and a refused one too |
| `.vericairn/write-lock/write.lock` | Empty | The same writes; it stays after the lock is released |
| `.vericairn/write-lock/holder.json` | A JSON object whose `format` is `vericairn.write-lock/1`, with `pid`, `host`, `home` (a digest, never a path) and `since` | The writer that holds the lock. It removes the file on release; a writer stopped while holding the lock leaves it. |
| `.vericairn/document-staging/.gitignore` | Hex `2a0a` | The first write of a generated document |
| `.vericairn/toolchain/release/<version>/<platform>/` | The archive the manifest pins, its `.sha256` stamp and the archive's unpacked files | `bootstrap` and `bootstrap.ps1` in release mode |
| `.vericairn/toolchain/source/<commit>/bin/` | A build of the pinned source and its `.source` stamp | The bootstrap scripts in source mode |
| `.vericairn/toolchain/vericairn-src/` | A clone of the Vericairn source | The bootstrap scripts in source mode, without `VERICAIRN_SOURCE` |

A write that stops part way can leave its temporary file, which it otherwise removes at once. None of
these is a record, an object or a document, and nothing reads it:

| Path | Left by |
| --- | --- |
| `.vericairn/record-staging/<ulid>.tmp` | A record write; its bytes were never published |
| `.vericairn/write-lock/<ulid>.tmp` | A writer describing itself in `holder.json` |
| `.vericairn/document-staging/<ulid>.tmp` | A generated document's publication |
| `objects/.<64 hex digits>.tmp-<process id>` | `objects put`; `estate check` reports it as `objects.name` |
| `estate/manifest.json.upgrading` | `estate upgrade`, replacing the manifest |
| `estate/manifest.json.naming` | The root operator's first device admitting itself, which names that admission in the manifest |
| `<name>.<32 hex digits>.tmp`, where `<name>` is `bootstrap`, `bootstrap.ps1`, `.gitattributes` or `.gitignore` | `estate init` or `estate bootstrap`, replacing that file; `estate check` reports it as `layout.unexpected` |

A `<ulid>` is 26 characters: digits and upper-case letters other than I, L, O and U. Residue means a
write stopped, so check with `estate check` and `objects verify` that the write either completed or
left nothing before you move the residue out of the estate.

Nothing else is housekeeping. A file you or a tool put under `.vericairn/` isn't, such as the receipts
the [people and agents guide](people-and-agents.md) keeps in `.vericairn/guide/`. Neither is a listed
path with other content, nor an operating-system file such as `.DS_Store`.

### Classify before any write

The restore writes no housekeeping and no `.git`. Any write afterwards adds files: even a refused
`records new` takes the write lock and leaves `.vericairn/write-lock/`, and a stored record adds its
own file and `.vericairn/record-staging/.gitignore`. An upgrade, a generated document's refresh or the
first device's admission can change a member. So classify right after the restore, while every file
should be a member:

```sh
unzip -p estate-export.zip export/inventory.json
find <restore-directory> -type f -o -type l
```

```powershell
Expand-Archive estate-export.zip -DestinationPath export-contents
Get-Content export-contents/export/inventory.json
Get-ChildItem -Path <restore-directory> -Recurse -Force -File | ForEach-Object FullName
```

Hash each file as in [Take in the exact bytes](#take-in-the-exact-bytes), and compare its byte count
and SHA-256 with its member's. Every member must be present and match, and no other file may be there
yet; a restore never makes a link. Reading the restored copy, as `records show` and `estate check` do,
writes nothing to it.

The supplemental sources come back from the capture: the store's backup beside the estate, and the
owner's instruction files into its root, each checked against the capture's list.

### Classify again after you write

When the restored copy replaces a lost estate, you put it under git and record there, as
[Recover cold](#recover-cold-and-prove-the-bytes) shows. Then classify again. Each file is now a
member, unchanged; a supplemental file that matches its list; a record you wrote, at the `path` its
write printed with `--json` and holding the `id` and `hash` printed beside it; or housekeeping. A
record's `hash` is the record's own, written inside it, not the SHA-256 of its file. `.git/` is git's
own, and you leave it out by that one name; the restore never makes it.

In this guide's example, once `AGENTS.md` is back from the capture and the readback and the
classification are recorded in the restored copy, the files beyond the inventory are exactly these:

```text
AGENTS.md                                               supplemental, from the capture
records/verification/<yyyy-mm>/<readback-id>.json       written after the restore
records/verification/<yyyy-mm>/<restore-files-id>.json  written after the restore
.vericairn/record-staging/.gitignore                    housekeeping
.vericairn/write-lock/.gitignore                        housekeeping
.vericairn/write-lock/write.lock                        housekeeping
.git/                                                   git's own
```

### Reconcile an unexpected file

A file that fits no class stops the verification. Don't add a pattern that skips it, don't widen a
housekeeping entry to cover it, and don't delete it:

1. Note its path, byte count and SHA-256.
2. Compare its hash with the inventory's members, the result outputs and the capture's list. A match
   says what it is: a member at the wrong path, an external payload copied into the estate, or a
   supplemental file.
3. Move it, unchanged, out of the estate into a quarantine directory beside the restore, keeping its
   relative path, and remove any directory the move leaves empty. A payload belongs in the restored
   store, never in the estate.
4. Classify again, and name what you moved, with its hash, in the verification of the classification.

For example, a readback written into the restored estate by mistake leaves `vault/obj-0001` at its
root. It is 9,437,184 bytes with the result output's SHA-256, so it is the external payload. Moved
to the quarantine, it stays as evidence, the estate holds no payload bytes, and the readback still
reads the restored store's copy.

### Record the classification

Record the classification beside the readback, in the estate of record, as a verification of the
estate's charter. `<estate-id>` is the `estate` that `export/export.json` names, and
`records show <estate-id>` prints its hash, `<estate-hash>`. `<inventory-hash>` is the export's
`inventoryHash`, also in `export/export.json`: the SHA-256 of its inventory, against which the restore
verified every member. Save as `restore-files.json`:

```json restore-files.json
{
  "method": "manual",
  "commit": "<estate-commit>",
  "environment": {
    "platform": "isolated restore",
    "isolation": "offline"
  },
  "role": "operator",
  "outcome": "passed",
  "summary": "Classified every file of the restored estate. Before any write, each file was a member of the export's inventory with its bytes and SHA-256, and every member was present. AGENTS.md matches the supplemental capture's list. After the readback was recorded here, the other files are that record and the listed housekeeping. One unexpected file was moved out unchanged.",
  "evidence": [
    {"summary": "The export's inventory, export/inventory.json", "hash": "<inventory-hash>"},
    {"summary": "AGENTS.md, restored from the supplemental capture", "hash": "sha256:<agents-sha256-hex>"},
    {"summary": "vault/obj-0001, found at the restored estate's root: 9,437,184 bytes with the result's SHA-256, a misplaced copy of the external payload. Moved unchanged to the quarantine beside the restore.", "hash": "sha256:<sha256-hex>"}
  ],
  "limitations": ["Covers the files of this restored copy. The readback, not this classification, proves the external payload's bytes."],
  "evidenceClass": "observation",
  "observer": "person"
}
```

```sh
vericairn --estate <restore-directory> records new verification --by <operator-id> --subject estate:<estate-id>:<estate-hash> --body restore-files.json
```

After a drill, record it in the live estate, without `--estate`, and the restored copy keeps only the
exported payload and the supplemental files.

## Encryption, ownership and private access

- **Encryption.** Vericairn doesn't encrypt the external payload, its backup or an export; an export
  is a zip of plain bytes. Encrypt the store and its backup at rest, keep the backup's key apart from
  the backup, and state both in the readback's environment. An export of an estate with private
  custody names the store, its references and their hashes, so keep the export private and encrypted
  too.
- **The volume or account owner.** The account that owns the store or volume decides who can read it,
  and holds its credentials. The credential reference's `holder` names that person. The runtime runs
  as an operating-system user, and records give it no store credentials. Keep the store's mount and
  any staging copy readable only by the owner's user; a copy in a shared, synced or world-readable
  folder widens access. When ownership moves, record a revised credential reference and repeat the
  readback.
- **Private access.** Records carry references, not access. A reviewer or an agent reading the result
  sees the hash and the reference; reading the bytes needs the store's own access, which its owner
  grants. Choose opaque references such as `vault/obj-0001`, since reference names travel wherever
  the records do, exports included.
- **Agents.** An agent can produce or review the work, and submit a result naming the artifact by
  hash, without the store's credentials. The credential holder performs the intake and the readback.

## Planned: what DESIGN-42 adds

DESIGN-42, the accepted design for work beyond git, types what a check must read. Its store and
custody parts (`CODE-62` and `CODE-65`) wait in the backlog for the first 0.10.x release. All of this
is planned and not in 0.9.3:

- **Store identity.** `location.store`, an id minted when a store is created or adopted, for stores
  without an https address.
- **Intake.** `vericairn stores put` writes the intake operation this guide records by hand.
- **A typed descriptor.** `result.outputs[].external` names the `resource`, `reference`,
  `referenceType`, `version`, the `intake` operation, `visibility`, `access` and an optional
  credential reference. A reference is store-relative, or https without user information, query or
  fragment. `external` is refused beside `location` or `section`.
- **The export's list.** The export summary gains an `external` array, naming each reference it
  doesn't carry, which a restore checks against the records. `estate restore` and `recover inspect`
  show each external payload as unrecovered until it is read back. Outputs recorded with this guide's
  pattern, with neither `location` nor `external`, show as custody unknown.
- **Cold-recovery qualification.** `vericairn recover readback --store <resource>` refuses the live
  store's location, recomputes each payload's hash and size, and writes a verification of the store,
  with method `vericairn-cold-recovery` and an expiry. A store is cold-recoverable while its latest
  such verification passed, hasn't expired and covers every external output naming it. A private
  store must also show an encrypted backup with its key held separately.
