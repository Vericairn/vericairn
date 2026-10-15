# Record format 2

Record format 2 is record format 1 ([record-format-v1.md](record-format-v1.md)) with rules for the records written
from now on. Nothing already written changes: every format-1 record keeps its bytes, its hash, its signatures and
the rules it was written under. A record states the format it was written in, and an estate at format 2 holds both.
Format 2 carries the records goals need, the additions milestone M3 adopted and, with DOCS-03, the living-document
records; this document grows with them until release 0.5.0 ships the format.

## Why a new format number

A release can add a type or an optional field without changing the format number; record format 1 grew that way from
0.1.0 to 0.4.2. Format 2 also adds requirements: a decision written in format 2 must say how much rides on it and what
it does not authorize, and a revision of a decision keeps the operator's words. A requirement cannot apply to records
written before it existed, so the rules follow the format each record states, and an estate moves to format 2 by one
recorded step.

## The envelope and the estate

- `format` is `vericairn.record/2` for a record written in format 2. Every other key of the envelope, the canonical
  form and the hash are unchanged. A format the build does not read is refused (`record.format`).
- The manifest's `recordFormat` is the estate's format: the format its new records are written in. At format 1 the
  estate holds only format-1 records; a record stating a later format than its estate is an error
  (`record.format-ahead`). At format 2 the records already written stay format 1, and a clone that had not yet moved
  can still add format-1 records, read under format 1's rules.
- A build reads the formats its release record lists in `recordFormat.reads` and writes an estate's own format, never
  one beyond `recordFormat.writes`. It refuses to open an estate whose format it does not read, instead of misreading
  it (`manifest.record-format`, naming the format); `vericairn doctor` prints the formats a build reads and writes.
- A build validates each record under the rules of the format the record states. A type or a field introduced in
  format 2 is refused in a format-1 record (`record.type-format`, `body.format`), with the upgrade that answers it.
- `vericairn records describe` and the record reference mark the types and fields format 2 introduced.
- A new estate starts at the newest format its build writes.

## Moving an estate to format 2

`vericairn estate upgrade --to <release>` with a release that writes format 2 moves the estate in one reviewable
commit: the manifest's `recordFormat` becomes 2 and its pin the release, and the migration record, itself the first
format-2 record, names the step `record-format-2`. No existing record is rewritten, so every record's bytes and hash
before and after are equal; `--check` shows the same without writing. The release must read every format the
estate's records use; an upgrade to a release that does not is refused, as is one to a release writing a format this
build cannot read.

## Types new in format 2

`vericairn records describe <type>` and the record reference list every field; these are the meanings and the rules.

- **`goal`**: an objective the estate carries out with agents and people (the estate's `design/goals.md`). It states
  the objective in the sponsor's words, the `completion` conditions (at least one: a record predicate `{record: {type,
  fields, min, max}}`, checked from current records, or an evidence statement `{evidence, check}`, judged by an
  evaluator who did none of the work), `constraints`, `kind` (finite or stewardship), the `sponsor`, the `roster` or
  role overrides it uses, the `budget` (caps by unit: `wallMinutes` and `attemptsPerStep` always, and `tokens`,
  `concurrency` and `currency` where they apply; an absent unit is not an unlimited one), the `plan` (proposed by a
  planner or given as work orders, and who approves it), `limits` and `inputs`.
- **`goal-status`**: a change in the lifecycle of an exact goal revision, pinned by hash: `active`, `paused`,
  `budget-limited`, `blocked`, `impossible` or `abandoned`. A goal starts when its sponsor activates it, which grants
  its budget, or ends unstarted when the sponsor abandons it; only the sponsor records active, impossible and
  abandoned; nothing follows impossible or abandoned; a revised goal starts again from its sponsor's activation.
  Achievement is never a status: it is the sponsor's acceptance of the goal.
- **`roster`**: who fills each role of the estate's work (planner, builder, reviewer, evaluator, release manager,
  publisher, decider, or a role the estate names), principals in order of preference. One current roster per estate,
  recorded by the operator or under `administer`; the decider role is held by operators, because decisions and
  acceptances stay theirs.
- **`ask`**: a question put to a principal, kept apart from any notification of it: `to`, the `question`, the
  `recommendation` first, the `consequence` of leaving it unanswered, optional `options` and `goal`, and its `state`.
  It starts `open`; the asker alone revises it, to `notified` (delivery stays unknown) or `withdrawn`, never changing
  the question or whom it is put to. A decision that names it in `answers` answers it; waiting never does.
- **`possibility`**: an idea captured with its disposition, explicitly not an approval: the `idea`, its `origin`, and
  its `disposition` (open, parked, adopted or declined, with the `reason` unless it is open). An adopted idea names
  what it became in `adoptedAs`: a work order, a decision or a goal. Nothing is authorized by a possibility.
- **`discharge`**: the discharge of one obligation a decision left (see the decision's `obligations` below), about
  the decision, naming the obligation's key and the evidence that discharges it. Only the obligation's owner or the
  operator records it, and an obligation has one current discharge; until one exists it stays open.
- **`credential-reference`**: a credential named by reference and never by its value: its `name`, `purpose`, `kind`,
  the `holder` responsible, the `store` where it is kept (described, never a value or a machine path), the `scopes`
  it permits, how its presence is `check`ed without using it (a presence check is not authentication), its `status`,
  and when it was rotated and is due again. Names are unique among current references.
- **`qualification`**: a platform profile's qualification for a release, about the release record: the `profile` as
  `design/platforms.md` names it, the `outcome` (qualified, measured-in-part, not-qualified or non-claim), the
  `evidence` it rests on (verifications, reviews and observations; a qualified or partly measured profile needs at
  least one, a non-claim needs none), a `summary` and its `limitations`. Qualification is derived from observations,
  never asserted beside them.

## Fields new in format 2

| Type | Field | Kind | Meaning |
|---|---|---|---|
| `decision` | `stake` | `low`, `medium`, `high` or `critical`, required | How much rides on the decision; at medium and above the recorder asks with a recommendation first and at most three questions at a time |
| `decision` | `notAuthorized` | string list, required, may be empty | What this answer does not authorize, so no one reads more into it |
| `decision` | `answers` | ULID list, optional | The asks the decision answers; an ask is answered by the principal it was put to, and a withdrawn ask is not answered |
| `decision` | `obligations` | object list, optional | What the answer leaves owed: each with a `key`, a `summary`, its `owner`, how it is resolved (`action`, `evidence`, `review` or `decision`), what `discharge`s it and optionally what it `gates`; keys are unique within the decision |
| `verification` | `evidenceClass` | `observation`, `report`, `attestation` or `inference`, optional | How the evidence was obtained: `vericairn check run` records an observation, `vericairn check observe` a host's report |
| `verification` | `observer` | `person`, `agent`, `automation` or `provider`, optional | What kind of observer made the claim |
| `verification` | `expires` | timestamp, optional | When the claim stops counting as current and needs observing again; it is later than the record |
| `device` | `presence` | `required` or `none`, required | Whether each signature needs the owner's presence; `required` only where the custody enforces it (`macos-secure-enclave`), and never for an agent's or a service's device |
| `device` | `purpose` | `signing` or `recovery`, required | Ordinary signing, or the operator's offline recovery device; a recovery device belongs to an operator |
| `device` | `custody` | gains `macos-secure-enclave` and `offline` | A key generated inside the Secure Enclave and kept as its enclave-wrapped blob, which signs nothing on any other Mac; or a recovery device's key, exported once and kept off every machine |
| `work-order` | `after` | ULID list, optional | Work orders this one starts after, so the next eligible work can be derived; never itself and never a cycle |
| `work-order` | `blockedBy` | reference list, optional | What holds it: asks, decisions, operations or other work orders |
| `authority` | `expires` | timestamp, optional | When the grant stops being in force: a record authored from then on cannot rely on it |
| `operation` | `cap` | object, required for `agent-run` | What a coding agent's run may spend, recorded before it starts and kept in every revision; `wallMinutes` is required |
| `operation` | `usage` | object, required once an `agent-run` settles | What the run spent as its harness reported it, and the units it did not report (`unreported`), which are charged at the cap |

## Rules enforced in format 2

- A decision that supersedes another keeps its `statement` byte for byte: the operator's words are protected, a revision
  corrects what surrounds them, and different words are a new decision (`decision.statement`).
- An operation of action `agent-run` names its assignment, principal and workspace in its target and carries its cap
  in every revision; once it is observed or failed it carries its usage. Other operations carry neither.
- A device revision keeps its principal and its purpose, and keeps the device's identity (below).
- Authority is judged at the time a record was authored: a grant that had expired by then authorizes nothing. Grants
  without an expiry, as every format-1 grant is, behave as they always did.
- Evidence is named by its exact bytes: a redacted copy is different evidence with its own hash, and a digest names
  bytes without granting access to them.
- Every rule of record format 1 applies to format-2 records too, except where this document says otherwise.

## A device keeps its identity

From format 2 a device is its first record: that record's id is the device's identity, and a rename or a new key is a
revision of it that keeps the identity, the principal, the purpose and the admission. Format 1 had no other way to
give a device a new key than to record a new device.

- Signatures name the identity, and each verifies against the key the device held at the record's `at`: the key of
  its latest revision dated at or before it. Times are whole seconds, so a record dated in the very second of a
  rotation verifies with either key. A signature that verifies only with a key the device held at another time is
  refused as `signature.key-time`, naming the revision whose key it was: a key used after its rotation, or a
  rotation dated earlier than it was made.
- A device revision is signed by the device itself in the name of its identity (`device.unsigned`). A rotation, a
  revision with a new public key, is signed by the previous key, which proves continuity, and by the new key, which
  proves possession (`device.rotation`, `signature.rotation`). Custody and presence change only with the key, by a
  rotation (`device.custody`). A device first recorded in format 1 reads as presence `none` and purpose `signing`.
- An admission names a device by any of its records and stays in force across its renames and rotations, without
  being reported as referring to an earlier revision. A self-signed admission is recognized by identity, so no
  revision hides one: the estate's first authority stays the only self-admission accepted.
- Records signed before a rotation stay valid, and the replaced key signs nothing dated after it. Like revocation,
  this rests on the times records state: a key that may have leaked is revoked by an administrator, not only rotated.
- `vericairn devices rename` and `vericairn devices rotate` write these revisions on the machine that holds the
  device's key. A rotation makes the new key beside the old one, records the revision, and only then lets the new key
  take the old one's place, so a refused rotation leaves the device as it was. Moving a key into other custody, from
  a file into the Secure Enclave for example, is a rotation with `--custody`; the new key keeps the device's presence
  where its custody enforces it, and `--presence` sets it.

## Emergency access

A recovery device is the operator's way back in when every other device is lost. It is a device of purpose `recovery`,
and its key is the only one whose custody is `offline` (`device.recovery`).

- `vericairn devices recovery --principal <operator> --name <name> --output <file>` makes it on a machine that holds an
  admitted device of the operator. The key is made in memory and written once to a new file outside the estate's
  repository, readable by its owner only. It is an encrypted PKCS#8 key: AES-256-CBC, with the key derived from a
  passphrase of at least twelve characters by PBKDF2-SHA256 at 600,000 iterations. No key store ever holds it. The
  operator's admitted device admits it at once. A recovery device is never the estate's first authority, even when it
  signs its own admission.
- With `--recovery <file>`, `vericairn devices admit` and `vericairn devices revoke` sign with that key, opened in
  memory with its passphrase: to admit a replacement device and to revoke a lost one. A recovery device signs its own
  device records, admissions and revocations, and nothing else (`signature.recovery-scope`).
- Each record it signs, other than its own device records, carries the warning `recovery.unreviewed`. The warning shows
  in `vericairn status` and `vericairn estate check` until an operator records a review of that exact record, signed by
  a device other than a recovery device.
- Losing every device without a recovery device leaves the estate readable and its history verifiable, but nothing can
  author in it again. The way on is a new estate that adopts the old one's records, said as such, never a silent
  self-admission.
