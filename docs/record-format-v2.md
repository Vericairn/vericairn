# Record format 2

Record format 2 is record format 1 ([record-format-v1.md](record-format-v1.md)) with rules for the records written from
now on. Nothing already written changes: every format-1 record keeps its bytes, its hash, its signatures and the rules
it was written under. A record states the format it was written in, and an estate at format 2 holds both. Format 2
carries the records goals need, the additions milestone M3 adopted, and the living-document records; releases 0.5.0 and
0.5.1 wrote it and were withdrawn before publication; release 0.5.2 writes it.

## Why a new format number

A release can add a type or an optional field without changing the format number; record format 1 grew that way from
0.1.0 to 0.4.2. Format 2 also adds requirements: a decision written in format 2 must say how much rides on it and what
it does not authorize, and a revision of a decision keeps the operator's words. A requirement cannot apply to records
written before it existed, so the rules follow the format each record states (except whether an unsigned admission
counts, which follows the estate's format; see Rules enforced in format 2), and an estate moves to format 2 by one
recorded step.

## The envelope and the estate

- `format` is `vericairn.record/2` for a record written in format 2. Every other key of the envelope, the canonical
  form and the hash are unchanged. A format the build does not read is refused (`record.format`).
- The manifest's `recordFormat` is the estate's format: the format its new records are written in. At format 1 the
  estate holds only format-1 records; a record stating a later format than its estate is an error
  (`record.format-ahead`). At format 2 the records already written stay format 1, and a clone that had not yet moved can
  still add format-1 records, read under format 1's rules, except that an admission among them recorded without a
  signature counts for nothing.
- A build reads the formats its release record lists in `recordFormat.reads` and writes an estate's own format, never
  one beyond `recordFormat.writes`. It refuses to open an estate whose format it does not read, instead of misreading
  it (`manifest.record-format`, naming the format); `vericairn doctor` prints the formats a build reads and writes.
- A build validates each record under the rules of the format the record states, with one exception: whether an
  admission recorded without a signature counts follows the estate's format (rules, below). A type or a field introduced
  in format 2 is refused in a format-1 record (`record.type-format`, `body.format`), with the upgrade that answers it.
- `vericairn records describe` and the record reference mark the types and fields format 2 introduced.
- A new estate starts at the newest format its build writes.
- A record's `at` is in whole seconds, and its identifier keeps the milliseconds of the moment it was made. Records of
  one second are ordered by identifier, so a build that writes them sorts them in the order they were made; records a
  build of 0.4.2 or earlier wrote within one second sort in no particular order among themselves.

## Moving an estate to format 2

`vericairn estate upgrade --to <release> --by <principal>` with a published release that writes format 2 moves the
estate as one change to commit: the manifest's `recordFormat` becomes 2 and its pin the release, and the migration
record, itself the first format-2 record, names the step `record-format-2`. No existing record is rewritten, so every
record's bytes and hash before and after are equal; `--check` shows the same without writing. The release must read
every format the estate's records use; an upgrade to a release that does not is refused, as is one to a release writing
a format this build cannot read.

Format 1 counted an admission recorded without a signature while the charter left signatures optional. The move lists
every admission recorded without a signature, revocations and superseded ones included, by id and hash, in the migration
record's `anchored`, and from then on an unsigned admission counts only when that list names it (rules, below). The list
counts only when the migration is signed by the root operator, or by a principal with the `administer` scope, with a
device whose own admission rests on signatures; a migration that lists admissions is refused when it is unsigned
(`migration.anchor-unsigned`) or written by anyone else, another operator included (`migration.anchor-author`). So
`estate upgrade`, when there is anything to list, refuses before changing anything unless the root operator or an
administrator applies it and holds such a device on this machine, and `--check` names the admissions the move would
list. An estate created at format 2 has no list, and in it no unsigned admission ever counts.

## Types new in format 2

`vericairn records describe <type>` and the record reference list every field; these are the meanings and the rules.

- **`goal`**: an objective the estate carries out with agents and people (the estate's `design/goals.md`). It states
  the objective in the sponsor's words, the `completion` conditions (at least one: a record predicate `{record: {type,
  subject, product, fields, state, min, max}}`, checked from current records, or an evidence statement `{evidence,
  check}`, judged by an evaluator who did none of the work; see [A goal's completion
  conditions](#a-goals-completion-conditions)), `constraints`, `kind` (finite or stewardship), the `sponsor`, the
  `delegate` a sponsor who is a person may name for an ordinary goal's acceptance, the `roster` or role overrides it
  uses, the `budget` (caps by unit: `wallMinutes` and `attemptsPerStep` always, and `tokens`, `concurrency` and
  `currency` where they apply; an absent unit is not an unlimited one), the `plan` (proposed by a planner or given as
  work orders, and who approves it), `limits` and `inputs`.
- **`goal-status`**: a change in the lifecycle of an exact goal revision, pinned by hash: `active`, `paused`,
  `budget-limited`, `blocked`, `impossible` or `abandoned`. A goal starts when it is activated, which grants its
  budget, or ends unstarted when it is abandoned; active is recorded by its sponsor, a person with the authority to
  spend, or by an operator when an agent sponsors it, and impossible and abandoned by the sponsor or that operator (see
  [A goal's activation and acceptance](#a-goals-activation-and-acceptance)); nothing follows impossible or abandoned;
  a revised goal starts again from its activation. Achievement is never a status: it is the goal's acceptance, by its
  sponsor when the sponsor is a person and by an operator when an agent sponsors it.
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
- **`living-document`**: the declaration of a living document, a document whose claims follow facts that change (the
  estate's `design/living-documents.md`). It names the document's `path` and, for a document outside the estate's own
  repository, the `repository` resource; whether it is `authored` or `generated` (a generated one names its
  `generator`); the `source` its content is read through (`estate-file`, or a pack's, such as
  `vericairn.software/git-file`); its `triggers`, each with a `level` (`remind`, `warn` or `refuse`); its generated
  `regions`, each with its generator and optional `inputs`; who may clear it (`clearedBy`: `any`, `independent` or `operator`); and the `checks` run when it is
  cleared. The foundation's triggers are `covers` with `paths`, `release` with a `product`, `upgrade`, `decision`, and
  `accepted` with work-order `keys`; its checks are `names-version`, `matches-generator` and `links-resolve`. A pack's
  trigger, check, source or generator is named `namespace/name`. One current declaration per document: a revision
  supersedes it and keeps its path and repository.
- **`document-review`**: the clearance of a living document, or the finding that it is stale, about the declaration
  at its exact hash. It records the `outcome` (`current`, `revised` or `needs-change`, which leaves it due), the
  reviewed `content` by hash and size (and `commit` for a document of another repository, never for an estate file),
  the `covered` paths' identity (required to clear a `covers` trigger), the triggers it `answers` (each one the
  declaration names), the reviewer's `independence` of the content, and the `checks` that ran. A failed check refuses
  a clearance. The reviewer is `self` when the review revises the content, or when the reviewer recorded the latest
  revision of the estate file or the latest revising review made before the review (by time, and records of one second
  by identifier); for a document of another repository, whose author the estate's records do not show, a reviewer may
  also state `self` (see Living documents due). `independent-agent` needs an agent principal and `operator` the operator
  role. A clearance meets the declaration's `clearedBy`, and a `self` review does not clear a document whose
  `clearedBy` is `independent`.

## Fields new in format 2

| Type | Field | Kind | Meaning |
|---|---|---|---|
| `migration` | `anchored` | object list, optional | Every admission recorded without a signature when the estate moved to format 2, each `{id, hash}`; only a migration that moves the estate to format 2, signed by the root operator or an administrator, lists them |
| `decision` | `stake` | `low`, `medium`, `high` or `critical`, required | How much rides on the decision; at medium and above the recorder asks with a recommendation first and at most three questions at a time |
| `decision` | `notAuthorized` | string list, required, may be empty | What this answer does not authorize, so no one reads more into it |
| `decision` | `answers` | ULID list, optional | The asks the decision answers; an ask is answered by the principal it was put to, and a withdrawn ask is not answered |
| `decision` | `obligations` | object list, optional | What the answer leaves owed: each with a `key`, a `summary`, its `owner`, how it is resolved (`resolution`: `action`, `evidence`, `review` or `decision`), what `discharge`s it and optionally what it `gates`; keys are unique within the decision |
| `decision` | `documents` | list of paths or declaration ids, optional | The living documents the decision changes; a declaration with a `decision` trigger becomes due when a newer decision names it |
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
| `review` | `conditions` | object list, required for a review of a goal and refused for any other | The goal's evaluation: one verdict per completion condition, in the goal's order, each with its `verdict` (`met`, `not-yet` or `impossible`), its `reason` and the records it `read`, each `{type, id, hash}` |

## Rules enforced in format 2

- A decision that supersedes another keeps its `statement` byte for byte: the operator's words are protected, a revision
  corrects what surrounds them, and different words are a new decision (`decision.statement`).
- An operation of action `agent-run` names its assignment, principal and workspace in its target and carries its cap
  in every revision; once it is observed or failed it carries its usage. Other operations carry neither.
- A device revision keeps its principal and its purpose, and keeps the device's identity (below).
- In an estate at format 2 an admission, admitting or revoking, is signed: by a device already admitted or, for the
  operator's first device, by the device it admits, the estate's first authority. An unsigned one is refused when it is
  written, and one found among the records counts only when the signed migration that moved the estate to format 2 lists
  it (above); otherwise, whatever format or date it states, it admits nothing, revokes nothing and unseats no admission
  it supersedes (`signature.admission-unsigned`). A record's format and date are what its author wrote, and a signature
  is the one thing an author without an admitted key cannot write.
- A grant's expiry is judged at the time a record was authored: a grant that had expired by then authorizes nothing.
  Otherwise authority is read from the current grants, as for every format-1 grant: a grant recorded after a record
  still authorizes it, and a revision that drops a scope withdraws it from records already written.
- Evidence is named by its exact bytes: a redacted copy is different evidence with its own hash, and a digest names
  bytes without granting access to them.
- Every rule of record format 1 applies to format-2 records too, except where this document says otherwise.

## Living documents due

`vericairn status` lists every declared living document that is due, and why: each trigger newer than the document's
latest clearance, which is a new revision of the estate file or a review with outcome `current` or `revised`. A
`release` trigger fires on the newest published release of its product, `upgrade` on the newest migration, `decision` on
the newest current decision that names the document by path or by declaration, and `accepted` on the newest acceptance
of a work order with one of its keys. `covers` compares the covered paths' identity in the working tree, uncommitted
changes included, with the one the latest clearing review recorded; only a review clears it. For a document of another
repository, `covers` is compared at the head of that repository's clone registered here (`vericairn sources clone`),
through its source; without one, it is compared at the commit reviewed or released. A document is due at the highest
level among its reasons: `remind` shows in status; `warn` is also a `document.due` warning of `vericairn estate check`;
`refuse` also refuses the next `vericairn release submit` of the product, before any intent is recorded, naming every
such document at once. `vericairn release submit` also refuses, before any intent is recorded, when a cleared document
of another repository that declares `refuse` and whose `release` trigger names the product differs, at the commit being
released, from the content its latest review cleared; it makes that comparison only where a clone of that repository is
registered here. A declaration that names a source, trigger, check or generator no installed pack provides is shown as
unavailable (`document.unavailable`) and, when it declares a `refuse` trigger, closes its product's release gate until
the pack is installed or the declaration is revised. Status in JSON lists every declared document with its state,
reasons and latest clearance.

A `release` trigger may name any revision of the product record. A product is the first record of its chain, so the
trigger fires on a release recorded under any revision of it, the release gate and the content comparison of `vericairn
release submit` match the product the same way, and revising the product record neither silences a trigger nor needs its
declaration revised.

A generated region is the content between `<!-- vericairn:generated <id> -->` and `<!-- vericairn:end <id> -->`, each on
its own line. For the estate's own files, `vericairn estate check` regenerates every region whose generator is
installed, and a generated document as a whole, and refuses a hand edit or lost markers (`document.generated-edited`);
`vericairn docs regenerate <path>` rewrites them and nothing outside the markers. The foundation's region generator
`vericairn.records/releases` tabulates the published releases of the products its inputs name, newest first. `vericairn
docs living` lists the declared documents; `vericairn docs declare` records or revises a declaration; and `vericairn
docs review <path> --by <id> --outcome current|revised|needs-change --summary <text>` (with `--commit <commit>`, and
optionally `--independence self|independent-agent|operator`, for a document of another repository) records a review in
which the runtime, not the reviewer, states the content's hash, the covered paths' identity and the triggers answered,
records the reviewer's independence (below), and runs the declared checks:
`names-version` (the document names the version of each release the review answers), `links-resolve` (every relative
link resolves within the repository) and `matches-generator` (generated content equals a fresh generation). A failed
check refuses the clearance; a check no installed pack provides is recorded as not performed. A declared estate document
that is not in the working tree is a `document.absent` warning. Like a `release` trigger, the releases generator takes
any revision of a product's record as the product.

How independent a reviewer is of a document's content follows what the estate's records can show. An estate file's
revisions are records, so they decide: the reviewer is `self` when the review revises the file or the reviewer recorded
its latest revision, and otherwise `operator` for a principal with the operator role and `independent-agent` for any
other; `docs review` refuses a stated independence that differs. Who wrote a document of another repository is not in
the estate: that repository's commits are not records, and the estate sees the document's revisions only in reviews
with outcome `revised`. So the review records the independence its reviewer states (`--independence`, or
`independence` in the API). Without a statement it records `self` for a principal that holds the builder role, which
writes the product's documents, and otherwise judges as for an estate file, from the latest review with outcome
`revised`. The record type holds a stated independence to its other rules: a reviewer who revised the content, by this
review or the latest revising one, is `self`; `independent-agent` needs an agent principal; `operator` needs the
operator role. A `self` review, stated or judged, does not clear a document whose `clearedBy` is `independent`.
Release 0.5.2 judged a document of another repository from the estate's records alone and refused `self` there, so a
builder's review of a document the builder wrote was recorded as `independent-agent`.

## A goal's completion conditions

A goal's `completion` lists what done looks like: record predicates, which the runtime checks from the current records,
and evidence statements, which an evaluator judges.

A record predicate `{record: {type, subject, product, fields, state, min, max}}` counts the current records of `type`
that match it, a superseded revision never counting, and holds when the count is within `min` and `max`: with neither
given, at least one record must match, and with `max` alone none need to. A record matches when each of these that the
predicate names holds:

- `subject`: the record is about the record named or any revision of it, the first record of its chain standing for
  them all, so a predicate about a product or a work order follows it across its revisions;
- `product`, for type `living-document` only: one of the document's `release` triggers names the product, by any
  revision of the product record, as the release gate matches it;
- `fields`: each body field named equals the string given, as the record states it;
- `state`: the record is in the state status derives for it. An ask is `answered` once a current decision names it in
  `answers`, and otherwise `open`, `notified` or `withdrawn` as its asker recorded; a work order is `planned`,
  `active`, `paused`, `submitted`, `retired`, `accepted` or `accepted (earlier revision)`; a living document is `due`
  while a trigger is newer than its latest clearance, and `current` otherwise. No other type has a derived state; its
  own fields are compared with `fields`.

So "release 0.5.0 of product P is published" is `{"type": "release", "subject": "<P>", "fields": {"version": "0.5.0",
"status": "published"}}`; "work order K is accepted" is `{"type": "work-order", "fields": {"key": "K"}, "state":
"accepted"}`; "no ask is open" is `{"type": "ask", "state": "open", "max": 0}`, where `"fields": {"state": "open"}`
would also count the asks a decision answered, whose recorded state stays open; and "no living document of product P
is due" is `{"type": "living-document", "product": "<P>", "state": "due", "max": 0}`. The language has no order and no
latest: a predicate about a commit names the commit.

The runtime gives each condition a verdict, `met`, `not-met` or `unknown`, with its reason and the records it read,
each at its exact hash; `vericairn goal show <goal>` shows them, and `vericairn goal` those of the one goal that is
neither achieved nor ended. A record predicate is `unknown` only when a record it might count cannot be decided here
and the verdict turns on it: a living document that names a contribution no installed pack provides, or whose covered
paths are in a repository with no clone registered here. A living document that is due is due whatever else could not
be evaluated. Like status, a living document's `covers` trigger compares its covered paths in the working tree; every
other input of a verdict is a record, so every machine gives the same verdicts for the same records. An evidence
statement is always `unknown` to the runtime: the evaluation judges it.

## A goal's evaluation

A review of a goal is its evaluation (the estate's `design/goals.md` § 5), pinned to the goal's exact revision. Its
`conditions` give, for each completion condition in the goal's order, a `verdict` (`met`, `not-yet` or `impossible`),
the `reason`, and the records the verdict `read`, each at the hash it pins, which must be the record's own
(`review.read`); an evidence condition found met names at least one record. One verdict is given for each condition
(`review.conditions`), and the review's `outcome` follows them: `pass` when every verdict is met, `fail` when one is
impossible, and `findings` otherwise (`review.outcome`).

The evaluator is a principal other than any step's author (`review.evaluator`). A goal's work is the work orders its
plan gives and the current ones that name the goal, by any revision, among their `inputs`; the authors of its steps
are the contributor of every assignment of that work and every principal that recorded an attempt or a result under
one. Reviewing a result is not authoring a step, so the principal that reviews the work may evaluate the goal. Who
authored a step is judged from the records before the evaluation, so work assigned afterwards leaves it standing. The
goal's current evaluation is the latest one of its exact revision that counts: one that gives a known verdict for
each condition and whose author authored no step when it was written. A revised goal is evaluated afresh.

`vericairn goal evaluate <goal> --by <id> --summary <text> [--verdict n:verdict:reason]... [--read n:record-id]...`
records an evaluation through the runtime, which gives each record predicate its own verdict, `met`, or `not-yet` when
it does not find it met, with the records it read, and refuses an evaluator's verdict that contradicts it; the
evaluator may still judge a predicate the runtime does not find met `impossible`. Each evidence statement takes the
evaluator's verdict, and one found met names the records read. The runtime refuses a step's author before anything is
written. The evaluator is told the conditions, never an expected answer.

## A goal's activation and acceptance

Who decides for a goal is its sponsor when the sponsor is a person, and an operator when an agent or a service
sponsors it: an agent may propose a goal, as its author or as its sponsor, but never activates, funds or accepts one.
Activating a goal, its first `active` status, and resuming it, an `active` status after `paused`, `budget-limited` or
`blocked`, grant its budget, which is spending, and are recorded by who decides for it: the sponsor, never another
principal, for a goal a person sponsors (`goal-status.sponsor`), and an operator for a goal an agent sponsors
(`goal-status.operator`). A person who is not an operator needs the authority to spend at the time
(`goal-status.authority`), a grant of the `spending` scope; an operator holds it. Like every grant, one recorded later
authorizes what came before it, and an expired one authorizes nothing written from its expiry on. What is granted is
the goal's budget, unit by unit: `wallMinutes` and `attemptsPerStep` always, and `tokens`, `concurrency` and
`currency` where the goal states them; a unit the goal does not state is not granted, never granted without limit.
Impossible and abandoned are recorded by the sponsor or by who decides for the goal. A status counts only when its
author could record it, so an activation an agent wrote around the runtime activates nothing.

A goal is achieved by an `acceptance` of its exact revision. Who accepts it is the operator's decision
01M507XV4VBFYWMRN8WCXMXHX4, kept in one place:

- its sponsor when the sponsor is a person, and an operator, only an operator, when an agent or a service sponsors it,
  so that no agent accepts a goal it sponsors (`acceptance.policy`);
- or, for an ordinary goal a person sponsors, the principal the sponsor delegates its acceptance to, named in the
  goal's `delegate`, while that principal holds the `accept-work` scope. The sponsor's activation of the exact revision
  that names the delegate is the delegation. A goal that includes a reserved action is never accepted by delegation:
  one whose completion has a condition about a release, whose acceptance and publication are the operator's, or about
  a milestone, or whose work includes a milestone, whose acceptance is the operator's. The goal refuses a delegate that
  is its sponsor, a delegate of a goal an agent sponsors, and a delegate of a goal whose conditions or given work
  include a reserved action (`goal.delegate`); a milestone that joins its work later withdraws the delegation from then
  on.

The acceptance also:

- comes after the goal's activation, and before it ends as impossible or abandoned (`acceptance.activation`);
- names in its `basis` the goal's current evaluation, which finds every condition met at the goal's exact hash
  (`acceptance.evaluation`); an evaluation by a step's author never counts;
- is never by that evaluation's author, nor by a principal that authored one of the goal's steps
  (`acceptance.independence`).

Each is judged from the records before the acceptance, so a later record never unmakes it. Until an acceptance passes
them all, the goal's status is the latest status of its exact revision, and `proposed` before any.

`vericairn goal propose` writes a goal from its options (`--done` takes each condition as JSON, `--budget` each cap as
`unit=value`, `--delegate` the delegate), `vericairn goal activate|pause|resume|stop <goal> --by <id>` record its
statuses in their order (activation and resumption by who decides for the goal, a pause or a stop by the sponsor too),
and `vericairn goal accept <goal> --by <id> --statement <text>` accepts it on its current evaluation. Records can
change after an evaluation, so the runtime also refuses an acceptance while a record predicate does not hold, before
anything is written; `vericairn goal show <goal>` says whether the goal is ready and, when it is not, why.

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

## A paired device

From 0.6.0 a device on another machine is paired over the local network ([api-v1.md](api-v1.md#pairing)
describes the exchange). Pairing writes the records `devices create` and `devices admit` write, in the estate's
own format, and adds no type and no field:

- The device record is authored by the device's principal and names the key the device presented, its
  descriptive name, platform, custody and presence as the device stated them, and the profiles the
  administrator permitted (`local-network` unless others were named). It is signed by that key over its
  content hash, a signature the device made on its own machine, which proves possession as every device's
  first record does.
- The admission is the administrator's, the operator's or a principal's holding `administer`, signed by the
  administrator's admitted device on the runtime's machine like every admission in format 2.

The runtime writes both or neither. The key a paired device proves on the local network, on every connection
and every request, is the one its device record names, and `profiles` decides where it connects: the local
network admits only a device whose current record permits `local-network`. A paired device's key stays on its
own machine, so `vericairn devices list` on the runtime's machine does not show it as held there.

## Emergency access

A recovery device is the operator's way back in when every other device is lost. It is a device of purpose `recovery`,
and its key is the only one whose custody is `offline` (`device.recovery`).

- `vericairn devices recovery --principal <operator> --name <name> --output <file>` makes it on a machine that holds an
  admitted device of the operator. The key is made in memory and written once to a new file outside the estate's
  repository, created readable by its owner only on Linux and macOS (on Windows the file takes the permissions it
  inherits from its directory). It is an encrypted PKCS#8 key: AES-256-CBC, with the key derived from a passphrase of at
  least twelve characters by PBKDF2-SHA256 at 600,000 iterations. No key store ever holds it. The operator's admitted
  device admits it at once. A recovery device is never the estate's first authority, even when it signs its own
  admission.
- With `--recovery <file>`, `vericairn devices admit` and `vericairn devices revoke` sign with that key, opened in
  memory with its passphrase: to admit a replacement device and to revoke a lost one. A recovery device signs device
  records, admissions and revocations, and nothing else (`signature.recovery-scope`).
- Each record it signs, other than its own device records, carries the warning `recovery.unreviewed`. The warning shows
  in `vericairn status` and `vericairn estate check` until an operator records a review of that exact record signed by
  a device that is not a recovery device and was admitted, by admissions that count, when the review was written. A
  review without a signature clears nothing, since anyone able to add records can write one in the operator's name
  while the charter leaves signatures optional.
- Losing every device without a recovery device leaves the estate readable and its history verifiable, but no device can
  be admitted in it again, so nothing in it can be signed by an admitted device again; where the charter requires
  signatures, nothing can be authored in it at all. The way on is a new estate that adopts the old one's records, said
  as such, never a silent self-admission.
