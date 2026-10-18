# Record format 2

Record format 2 is record format 1 ([record-format-v1.md](record-format-v1.md)) with rules for the records written from
now on. Nothing already written changes: every format-1 record keeps its bytes, its hash, its signatures and the rules
it was written under. A record states the format it was written in, and an estate at format 2 holds both. Format 2
carries the records goals need, the additions milestone M3 adopted, and the living-document records; releases 0.5.0 and
0.5.1 wrote it and were withdrawn before publication; 0.5.2 was the first published release to write it. The 0.6.0
candidate implemented goal evaluation and execution, routing and asks, and pairing, but was also withdrawn before
publication. The 0.7.0 candidate was also withdrawn before publication; 0.7.1 carries its work with FIX-12.
This maintained document describes 0.7.3, which reads formats 1 and 2, writing at most
format 2. The additions first present in 0.7.0 are work-progress, communication-setting, communication-delegation and harness-qualification types,
estate observations, watching settings and execution admission fields. These additions require a 0.7.0-capable
reader before authoring them, but no format-3 migration; older records retain their original bytes and rules.

## Derived context artifacts (0.7.0)

`vericairn.context/1` is a versioned derived artifact, not a record type or an
editable ledger. Its `vericairn.context-manifest/1` pins the declared basis and
exact source identities; it is distinct from the estate release manifest.
Retrieval pins carry the source identity themselves: record id, record hash and
byte hash, or document path, revision record and byte hash. Reads write nothing
and need no retained manifest. In 0.7.3, documents require matching working-tree
bytes. FIX-27 for 0.8.0 also allows required originals from the estate's own local
Git history, verified against the pinned SHA-256 hash; their pins carry
`history: "local"`. Shallow or missing history and hash mismatches still refuse,
with guidance to restore the pinned bytes or use a complete local clone. No
fetch or revision substitution occurs. Optional documents and pins without the
marker keep their working-tree-only behavior; record custody is immutable.
Only dispatch retains context, in the
worker's local run directory under the Vericairn home, never estate objects or
records.
Record canonical hashes and original-file byte hashes are separate. Originals
remain separate from attributed derivatives, whose synthesis defaults to off.
The package's content hash and serialized byte count are a sidecar over the
canonical pretty UTF-8 payload. Changing the payload invalidates that identity.
Mandatory originals remain inline. Default agent packages withhold optional
material explicitly named by the work, its inputs or governing decisions; the
remaining catalogue is outside selection and has a listing command. Standalone
human briefs include the full selected scope. Context allocation is independent
of a run spending cap and never authorizes truncating mandatory material.
This contract needs no estate upgrade and introduces no record format 3.

Context manifests and payloads are returned, never stored by a read, in both
repository-synced and local-only estates. Preparation and retrieval author no
records and write no files. The work brief carries this contract; hand-over
keeps its API v1 discriminator and comparison fields as a compatibility view
of the same manifest, source and accounting implementation. Run-intent evidence
continues to pin the exported brief's byte hash through its existing field.
Neither the context artifact nor a pending assignment it describes admits work,
accepts a result, clears an uncertain effect or changes any existing record.

## Why a new format number

A release can add a type or an optional field without changing the format number; record format 1 grew that way from
0.1.0 to 0.4.2. Format 2 also adds requirements: a decision written in format 2 must say how much rides on it and what
it does not authorize, and a revision of a decision keeps the operator's words. A requirement cannot apply to records
written before it existed, so the rules follow the format each record states (except whether an unsigned admission
counts, which follows the estate's format; see Rules enforced in format 2), and an estate moves to format 2 by one
recorded step.

FIX-13, FIX-14, FIX-15, FIX-16 and FIX-18 change validation, runtime behavior and worker observations
within the existing formats. They introduce no format-3 migration. Restore's inventory and filesystem
limits are in [record-format-v1.md](record-format-v1.md#export-and-restore). Single-record publication
now judges the proposed estate before storage; an unreadable local recovery journal holds record
writes with `reconcile-required`, without becoming a portable record or changing existing bytes.

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
- For records of either format, reconsideration findings apply only to current records. A review or acceptance
  that pins an earlier subject revision is flagged with `record.subject-stale` (and an acceptance also with
  `acceptance.unresolved`) while that judgment is current. Once the judgment itself is superseded, those findings
  stop: its successor is judged against the subject's current revision. A replacement still pinning an earlier
  subject revision remains flagged. Every historical record still undergoes structural, hash, signature and
  authority validation; supersession does not repair an invalid pinned hash. FIX-05's replacement acceptance of
  its current work-order revision therefore answers the superseded acceptance of the earlier revision.
- A new estate starts at the newest format its build writes.
- A record's `at` is in whole seconds. From 0.7.2, generated identifiers increase in one process's generation order,
  across threads and within a millisecond. A repeated or backwards clock retains the last millisecond and increments
  the suffix. This does not establish causal order across processes or machines. An exact result acceptance's hash pin
  establishes that it saw the result; judgments without such a reference use recorded time, with the identifier only
  a deterministic tie-break. Earlier builds did not guarantee identifier order within a millisecond (0.4.2 and earlier,
  within a second). Explicitly supplied identifiers carry no generation-order guarantee.

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
  work orders, and who approves it), `limits` and `inputs`. Anyone may propose a goal. A revision of an existing goal, a
  goal record that supersedes one, is recorded only by who decides for the revision it supersedes: its sponsor when the
  sponsor is a person, and an operator when an agent or a service sponsors it (`goal.revision`, the operator's decision
  01M50A8KHJT1MSSS84KZKHKRHB). Anyone else proposes the change as an ask to the sponsor.
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
- **`custody-exception`**, from 0.6.0: the sponsor's exception to the custody precondition for one goal (the estate's
  `design/goals.md` § 10), about the goal's exact revision: the `reason` and the time it `expires`, later than the record.
  While it is in force, a coding agent's run of the goal may launch unattended on a machine where a key of an operator or
  of the sponsor signs without its owner's presence, and the run names it (see [An agent run and its goal's
  ledger](#an-agent-run-and-its-goals-ledger)). It is recorded only by who decides for the goal, its sponsor when the
  sponsor is a person and an operator when an agent sponsors it (`custody-exception.sponsor`); a revision is its author's,
  about the same goal revision (`custody-exception.revision`), and can bring the expiry forward; a revised goal needs a
  fresh one.
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

The 0.7.0 additions are `work-progress` ([completed phase claims](#work-progress-claims-070)),
`communication-setting` and `communication-delegation` ([personal settings and consent](#communication-configuration-comms-01)),
and `harness-qualification` ([exact installed harness admission](#harness-qualifications-run-03)).

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
| `work-order` | `role` | string, optional | The roster role whose principals carry it, `builder` when omitted; a `publisher` or `decider` step is a reserved action (see [Routing a goal's steps](#routing-a-goals-steps)); from 0.6.0 |
| `assignment` | `routedWith` | `run` or `ask`, optional | Set by routing on an assignment it stores together with the record that carries it: `run`, the `agent-run` intent of a run on it, or `ask`, the ask that names it in `assignment`; until that record exists the assignment holds nothing of its step, so a store interrupted between the two never holds a step; from 0.6.0 |
| `ask` | `assignment` | ULID, optional | The assignment routing stored together with the ask, of the principal asked (`ask.assignment`); from 0.6.0 |
| `authority` | `expires` | timestamp, optional | When the grant stops being in force: a record authored from then on cannot rely on it |
| `operation` | `cap` | object, required for `agent-run` | What a coding agent's run may spend, recorded before it starts and kept unchanged in every revision; `wallMinutes` is required, and a run charged to a goal states exactly the units its budget grants (see [An agent run and its goal's ledger](#an-agent-run-and-its-goals-ledger)) |
| `operation` | `usage` | object, required once an `agent-run` settles | What the run spent as its harness reported it: for each of `wallMinutes`, `tokens` and `currency` its cap states, the amount spent or the unit listed in `unreported`, which is charged at the cap |
| `operation` | `stop` | object, optional, `agent-run` only | A stop's facts, each recorded only once observed: `requestedAt` and `requestedBy` (a principal, or `worker` for a cap the run reached) with an optional `reason`, then `transmittedAt` with `transmission`, `acknowledgedAt` with `acknowledgment`, `escalatedAt` with `escalation`, and `containedAt`, which only a settled revision records; from 0.6.0 |
| `operation` | `liveness` | object, optional, `agent-run` only | Worker-observed `stallSeconds`, `lastEventAt`, optional `attentionAt`, `attention` (ask id), and `stoppedAt` |
| `operation` | `scope` | object, optional, settled `agent-run` only | Workspace audit: `changed`, `outsideCount`, `outside` (at most 1000 relative names), optional `problem` when incomplete |
| `review` | `conditions` | object list, required for a review of a goal and refused for any other | The goal's evaluation: one verdict per completion condition, in the goal's order, each with its `verdict` (`met`, `not-yet` or `impossible`), its `reason` and the records it `read`, each `{type, id, hash}` |

## Rules enforced in format 2

- A decision that supersedes another keeps its `statement` byte for byte: the operator's words are protected, a revision
  corrects what surrounds them, and different words are a new decision (`decision.statement`).
- An operation of action `agent-run` names its assignment, principal and workspace in its target and carries its cap
  in every revision, unchanged from its intent (`operation.cap`); once it is observed or failed it carries its usage,
  which states every charged unit of the cap or lists it as unreported (`operation.usage`). Its intent, when its target
  names the goal it is charged to, starts only while that goal is active and within its budget, and a stop it records
  keeps its facts apart (see [An agent run and its goal's ledger](#an-agent-run-and-its-goals-ledger)). Other operations
  carry none of these.
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

## What holds a work order

Release notes and the progress report omit superseded results. They list a current result in their release window as
accepted work when a current acceptance pins the result's exact hash or the current revision and hash of its work
order, following the attempt and assignment. The latter includes results written by another principal, without
creating an acceptance of those results or changing who may accept them. Acceptance of an earlier work-order revision
does not count for its current revision. From 0.7.2, accepted results are grouped by work order, with one entry naming
the latest accepted current result in the window, ordered by recorded time and then identifier. Earlier results remain
reachable through `vericairn work observe <key>`. Later unaccepted results and results of other unaccepted work remain
listed as recorded, not yet accepted. Frozen documentation sets are unchanged.

A work order's `after` and `blockedBy` order the work, and the next eligible work is derived from them (the estate's
`design/goals.md` § 7): the planned work orders that nothing holds. A work order it starts after (`after`, by any
revision) holds it until it is accepted. A record in its `blockedBy` holds it while it is open: an ask while it is
`open` or `notified`, until a decision answers it or its asker withdraws it; a work order until it is accepted or
retired; an operation until its effect is observed (`observed-success`) or it is canceled; a decision while one of the
obligations it leaves has no discharge. A record the estate does not hold holds it, since nothing shows it free. The
view reads the records alone, so every machine derives the same one. `vericairn progress` lists the eligible work and,
for each work order held, every relation with its state; `vericairn goal next` routes a goal's eligible steps, below.

## Routing a goal's steps

From 0.6.0 the next-eligible view routes each eligible step of a goal (the estate's `design/goals.md` § 6): the work
orders of the goal's work that are planned and that nothing holds. A step is carried in a role, the work order's `role`
or `builder`, and the goal's roster names the principals of the role in order of preference: the goal's own `roles` when
they name it, otherwise the roster the goal names at its current revision, otherwise the estate's current roster. The
first of them that is available takes the step:

- an agent, when a launcher for its harness is available on the machine routing it (the launcher's harness is the agent
  principal's `agent.product`, such as Claude Code or Codex; the generic launcher serves no agent by itself), a key of
  one of its admitted devices is held there, and the goal's budget leaves something for a run of the step: no charged
  unit spent and, for a step it already holds, attempts left. Whether the goal is active and how many runs are in flight
  decide when its run starts, not who carries the step;
- a person, when the estate holds its principal record and it holds the role (the decider role is an operator's).

A step whose role is `publisher` or `decider` is a reserved action: publication, or a decision such as a release's or a
milestone's acceptance. It reaches only a person who holds the action, an operator or, for publication, a principal with
a grant of the `publication` scope in force, or an agent such a grant covers. A grant in force covers the action for
every goal while it lasts.

A step a principal holds stays with it: a run of it in flight; the latest current assignment of its work order, which is
a person's (the step is with that person) or an agent's (its result goes to review, a review by another principal with
outcome `findings` or `fail` sends it back to the agent for another attempt, and otherwise the agent goes on with
another run while it is available); or an ask about it, an ask whose subject is the work order and whose `goal` is the
goal, while the ask waits. An agent that holds a step and cannot go on is never replaced by another contributor, since
another assignment would start its attempts afresh.

When no principal is available the step is an ask: to who decides for the goal (its sponsor when a person, otherwise the
operator who activated it), or, for a reserved action that principal does not hold, to the estate's root operator. The view
never falls back to a principal the roster does not name, and a principal who answered an ask about the step is not
asked again while the answer holds. Each eligible step's route is one of `agent`, `person`, `reserved` or `unavailable`,
which the one-step operation acts on, or a state that holds it: `running`, `assigned`, `review`, `asked` or `answered`.
The view also says whether the goal's budget allows each step now. It reads the machine's launchers and keys, so it is a view of that machine's ability to carry the work; it is a
read-only query, and it records and starts nothing. The progress report's eligibility section uses records alone,
though its repository-check findings also depend on local files.

The one-step operation (`vericairn goal step`) records what a route produces: for a step routed to a principal it chose,
an `assignment` to that principal, whose brief names the work order and the goal among its sources, prohibits every
reserved action unless a grant in force covers it, names the grant that lets its principal carry a reserved step, and
whose note says why it was routed; for a person, and for a step no principal is available for, an `ask` whose subject is
the work order (without a hash) and whose `goal` is the goal, with its recommendation and the consequence of leaving it
unanswered. An agent's assignment is stored only with its run's `agent-run` intent, and a person's only with its ask:
the two are judged together, their content checks included, before either is stored. Storing them is not atomic, so
routing marks the assignment with `routedWith`, and an assignment whose `run` intent or `ask` does not exist holds
nothing of its step: a store that stops between the two never holds a step, and the stranded assignment stays a
record, as every record does. The runtime that stores them keeps a journal of the pair on its machine and, until the
step is held again or is no longer planned, reports a pair it stopped storing part-way as a reconcile task,
`batch.incomplete`, naming what was stored and what was not.
From 0.6.0 an `assignment` of a step of a goal may also be recorded by the goal's sponsor when the sponsor is a person,
beside the operator and a principal holding `local-engineering`; the goal and its work are judged as they were when the
assignment was recorded.

A hand-over of a step an agent holds to a person (`vericairn work takeover`) is an `assignment` of the step to the person,
whose brief carries the agent's brief with the agent's assignment among its sources. From 0.6.0 any `assignment` of a
person to a work order whose latest assignment recorded before it is an agent's or a service's is a hand-over, whatever its
brief says, and is recorded by a person only: the person taking the step over, the sponsor of a goal the step belongs to,
or an operator (`assignment.handover`). An agent never records one, whatever scope it holds.

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
such document at once. `vericairn release submit` also refuses, before any intent is recorded, when a repository document
that declares `refuse` and whose `release` trigger names the product differs from the content its latest review cleared,
or has no clearing review. Both document bytes and repository `covers` checks use a commit of the document's own
repository. In the release repository this is the exact commit the build provider resolves at the
effective destination; an unresolved commit refuses submission, and the clone's HEAD is never substituted. In another
repository it is that repository's registered HEAD, or the reviewed commit when HEAD is its ancestor. A newer or
divergent HEAD with changed content still refuses. Resource revisions retain the repository's identity. The comparison
runs where a clone is registered; if that clone lacks the required commit,
the refusal names the repository to fetch. An unregistered clone is reported as not compared, for the independent
review to read at a commit of that document's repository. Observation checks again before recording a candidate,
using the actual built commit for the release repository and the same ancestry rule in each other repository.
A declaration that names a source, trigger, check or generator no installed pack provides is shown as
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
`vericairn.records/releases` tabulates the published releases of the products its inputs name, newest first;
`vericairn.records/progress`, from 0.6.0, carries the progress report that `vericairn progress` prints, as a fenced text
block, and takes no inputs. Its check shows the records' own validation and says that it leaves out the repository
checks, which read the document that carries the region; and since the report follows the records, a new record can
make the region differ from a fresh generation until `vericairn docs regenerate` renews it. `vericairn
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

For example, the condition "release 1.0.0 of product P is published" is `{"type": "release", "subject": "<P>", "fields": {"version": "1.0.0",
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
plan gives and those of which any revision names the goal, by any revision, among its `inputs`, each represented by
its current revision; the authors of its steps
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

## An agent run and its goal's ledger

A goal's pause or stop changes its recorded lifecycle and prevents new runs; it does not signal runs already in
flight. Use `vericairn worker stop <run> --by <principal>` for each run that must stop, then inspect `worker runs` for
observed containment. In 0.7.3 `limits.deadline` (including `goal propose --deadline`) and `limits.consecutiveFailures`
are stored and validated but not enforced by routing or the worker. The worker enforces its run thresholds and
`stallSeconds`; operators must act on deadlines and consecutive failures themselves.

A coding agent's run is an operation of action `agent-run` (the estate's `design/goals.md` §§ 7 and 9, decision
01M4YBR5PGCPQD94BVNC547EBA). The worker, `vericairn worker run` ([software.md](software.md#coding-agents-the-worker-and-its-launchers)),
records its intent before anything starts: its identity is `agent-run:` and the intent's id, which is the run's id; its
`provider` the launcher; its `target` the `assignment`, the agent `principal`, the `workspace` as a repository and branch
(never a machine path), the `repository` resource, the `branch`, the `goal` it is charged to, the `workOrder` key, the
worker contract (`worker`: `vericairn.worker/1`) and, for a run launched under it, the sponsor's `custodyException`, which
must have covered the goal's revision when the intent was recorded, unexpired and not yet replaced by a revision recorded
before the intent (`operation.custody`), judged as of the intent so that a later revision or expiry leaves the run valid;
and its `cap`, taken from the goal's budget. Beside the operator and a principal holding `local-engineering`, who record
every external operation, the sponsor of the goal a run is charged to records the run's operation when the sponsor is a
person, from 0.6.0 (`operation.author`). The intent is the
`not-started` revision; `dispatched` records the harness's start, and the settlement records the observed effect
(`observed-success` when the harness exited 0, `known-failure` when it failed or was stopped) and the `usage`. A run
whose custody precondition no longer holds when its harness is about to start, judged again then, is `canceled` before
any dispatch, from 0.6.0, with what was observed naming the devices and the exception its intent named: it is neither
in flight nor charged. The operation never records that the work is done: the agent's result is a claim another
principal reviews.

A run is charged to its goal, unit by unit:

- a settled run, what its usage reports, and its cap in every unit its usage lists as `unreported`: unknown usage is
  never zero;
- a run in flight (not started, dispatched or uncertain), its whole cap, so that a run holds its allowance until it
  settles and admission cannot allocate that reserved allowance again; actual soft-limit overruns can exceed it;
- a run that ended before anything was dispatched (no revision records `dispatchedAt`), nothing.

A goal's spend is the sum of the charges of every run that names any revision of the goal, against the budget of its
current revision. Every record newly reserving allowance, including a revision, is refused unless, judged from the
records written before it (so a later record never unmakes it):

- the goal's revision then current is `active` (`operation.fenced`);
- the cap states `wallMinutes` and `attemptsPerStep` and, exactly where the budget states them, `tokens` and `currency`
  in the budget's currency; never `concurrency`, and no more attempts per step than the budget (`operation.cap`);
- no charged unit is spent, and each is capped within what remains (`operation.budget`);
- fewer of the goal's runs are in flight than its `concurrency`, one when the budget states none, since an absent cap is
  not an unlimited one (`operation.budget`);
- the step, the assignment, has had fewer runs than the goal's `attemptsPerStep` (`operation.budget`);
- no prior run of that assignment remains uncertain without reconciliation under its original identity. A new branch
  or spare budget does not authorize retry. A later reconciliation does not retroactively admit an earlier retry.

The worker takes a run's cap from the goal's budget: the wall minutes asked for or what remains, whichever is less, the
goal's attempts per step, and what remains of `tokens` and `currency` where the budget states them. A cap is reserved as
the run goes; operational thresholds leave headroom for shutdown and usage in flight, with spending controls explicitly soft. When the
settled runs' charges reach the budget in a unit, or a run is asked for a step whose attempts are used up, the worker
records the goal `budget-limited`, which fences new runs and asks the sponsor to raise the budget or stop. Resuming a
budget-limited goal grants its budget again but does not refill it: the spend still counts every run, so the budget is
raised by a revision of the goal, which its sponsor activates.

A stop's facts are kept apart in the settling revision's `stop`: when it was requested and by whom (`worker` for a cap
the run reached), when and how it was transmitted, when and how the harness acknowledged it, when it was escalated after
the grace period and what the escalation did, and when containment was observed. Each is present only once observed, never
before the request, and containment only in a settled revision; a stop that was not observed stays requested.

### Run identity, observed usage and stops

A worker state-file write failure after launch leaves the run dispatched and reserved; supervision retries local state
writes and settles only after observing exit. `RunStart` authors the intent as its supplied recording caller.

Every physical agent launch requires a new `agent-run` intent, with fresh budget and concurrency admission. A settled
agent-run identity (`observed-success`, `known-failure` or `canceled`) cannot return to an in-flight state or change to
another status, including through reconciliation (`operation.agent-run-relaunch`). A dispatch timestamp is immutable
once assigned and cannot be introduced after settlement (`operation.agent-run-dispatch-kept`). The target and cap stay
those of the original intent (`operation.agent-run-target-kept`, `operation.cap`); changing the goal cannot move a
reservation. Admission recognizes an already
reserved revision only when its predecessor was in flight with the identical target. Generic operations retain their
reconciliation and retry rules.

From 0.6.1, every revision after settlement also keeps every previously recorded body field unchanged
(`operation.agent-run-settlement-kept`), including usage, the observation, liveness and the scope audit. Evidence may
only be appended, and missing stop facts may be added under the existing chronology rules; recorded stop facts,
including containment, cannot change. Previously absent optional fields may be added only where the record rules
already permit them. This does not grant a new status transition. The ledger reads usage from the first settlement,
so a later revision cannot refund a settled run's charges, including units charged at the cap because they were unreported.

Token usage is accepted only as complete nonnegative integer counts with checked sums (including sums across turns).
Claude results require input, output, cache-creation and cache-read counts; Codex completed turns require input and
output counts. A malformed or incomplete usage event, arithmetic overflow, or an unfinished Codex turn leaves the
whole token unit unreported, even if another turn reported valid usage. Settlement charges that unit at its reserved
cap; complete explicit zero counts remain known zero.

Stop chronology is validated as observed facts: acknowledgment and escalation are at or after transmission (either may
precede the other), containment is at or after every earlier fact and no later than `observed.at`. Every previously
recorded stop property, including the request and explanatory text, is immutable across revisions
(`operation.stop-kept`); out-of-order facts are refused (`operation.stop-order`). Transmission is timestamped
after delivery attempts finish, rather than at an earlier heartbeat.

New worker intents state `target.capMode: "soft"` and evidence describing each operational threshold and the
launcher's native controls. The ledger reserves the full cap. The worker subtracts grace plus one heartbeat from wall
time, and ten percent (rounded up) from tokens and currency, to leave headroom for shutdown and usage in flight. It
refuses an allowance too small to leave any active token or currency unit. Claude Code receives `--max-budget-usd` for
USD at the reduced currency threshold; other currencies and total tokens have no native cap. Codex receives
`features.rollout_budget.enabled=true` and `features.rollout_budget.limit_tokens` at the reduced token threshold; this
is native tracking, not a guaranteed hard stop. Generic launchers supply no native spending control. All units remain
explicitly soft: a turn, provider cost estimate or failed shutdown can exceed the headroom and the reservation.
Unknown units are still charged at their full cap. A soft overrun can make the goal's remainder negative; reservations
prevent double allocation, not all overspend. Elapsed wall usage and stop grace use a monotonic clock, while the
record's observation timestamps remain UTC facts.

Containment means a successful inventory showed the whole launch boundary exited, including descendants after root
exit. An unavailable or failed process inventory cannot establish containment and is retried with the reservation
held. Failed escalation attempts likewise remain evidence and are retried. On Windows transmission is the stop file;
on POSIX it also attempts SIGTERM to the owned process group. A POSIX leader stays unreaped through the last inventory
and signal so its numeric group identity cannot be reused, and signaling refuses lost child ownership. Abandoning
supervision retains a native lifecycle owner to reap the child or close Windows SafeHandles after exit; that cleanup
does not author settlement. A dispatch write lost after launch must be recovered before settlement; until then the
intent holds its full reservation. These are process boundaries, not an OS security sandbox: POSIX group/session
escape and worker crash are not contained, and unsupported child-reaping hosts refuse launch. See [the
worker](software.md#launch-accounting-and-containment) for the Windows creation gap and other platform limits.

### Worker liveness and assignment scope

The worker observes stdout event-stream liveness with a monotonic clock; stderr activity and harness claims do not
certify progress. Each launcher defaults to a 600-second silence limit; its `stall-seconds` setting (1–86400) changes
that default, the roster's `limits.stallSeconds` overrides that setting, and the goal's `limits.stallSeconds` takes precedence. The intent pins the effective limit from the records current when it is recorded
in `target.stallSeconds`. At one limit of silence the worker records an open attention `ask` to the goal's accountable
principal, about the run (no notification is sent). At twice the limit it requests a stop and attempts to contain the whole
launch boundary; containment is recorded only after exit is observed. New stdout events reset the silence timer but
never erase an earlier attention fact. A failed
attention write is retried and does not prevent containment. Settlement records `liveness`: `stallSeconds`,
`lastEventAt`, optional `attentionAt`, `attention` (the ask id), and `stoppedAt`; run-list responses expose the same
`liveness`, observed locally while in flight and recorded after settlement. A lost observer certifies neither liveness
nor containment.

From 0.7.2, settlement withdraws the still-open or notified process asks named by its `liveness.attention` and
`signals[].attention`. The asker authors the withdrawal, whose subject pins the exact settlement and whose
recommendation records that attention is no longer needed. The original ask and observation remain in history.
An ask a person already answered stays answered; unrelated questions about the run stay open. Closed asks about
the settled run no longer need reconsideration merely because settlement superseded its intent. New
`attempt-missing` and `scope-settlement` asks stay open, with subjects pinned to the exact settled operation
revision and hash. Settlement, these new review asks and process-ask withdrawals are validated together and
journaled, so a partial storage failure remains visible for reconciliation.

At settlement the worker compares a physical file inventory captured after brief export and before launch with the
workspace after the whole process boundary exits. Enumeration does not consult agent-writable Git configuration or
Git environment variables, and the physical root must retain the baseline's identity. Content, symlink targets
(without reading through the link), and Unix modes
are compared, including deletions, renames, untracked files and ignored files. From 0.6.1 the audit also compares the
run branch's head and every commit reachable from it but not from the prepared baseline commit against that baseline,
including side branches of merges. Their changed paths are unioned with the physical comparison, so restoring a path
in the worktree or a later commit does not hide it, nor does checking out a different head. The original Git
object/reference directory is pinned before launch; isolated Git metadata excludes agent-writable configuration,
replacement refs, grafts, attributes and external diff drivers. Missing history or an unreadable branch makes the
audit incomplete. Preexisting
unchanged files are not attributed to the run. `brief.permittedChanges` entries are interpreted literally as
repository-relative files/directories or globs (`*` and `?` within a segment, `**` across segments).
Bare patterns contain no whitespace; use `path:` for an explicit pattern, including a path containing spaces.
For example, `path:NOTES.md` permits that file at the repository root; "NOTES.md at the repository's root"
is prose and grants no path permission. Worker admission refuses a brief with no interpretable pattern before
recording intent or launching a harness. Prose may accompany at least one pattern. Absolute paths, parent traversal
and shell expansions grant nothing, and directory matching respects path boundaries. The settling operation and run-list response expose `scope`:
`changed`, `outsideCount`, `outside` (up to 1000 relative names; unsafe names are hashed), and optional `problem` when
the audit is unavailable or incomplete, including submodules needing a separate audit. An outside change or incomplete
audit adds scope attention to the observed summary; it never accepts the work or silently treats an unavailable audit
as clean. Non-regular files are rejected without reading their contents, and handle checks reject leaf replacement
races. Each inventory runs in a helper process under a 30-second deadline and work limits, with bounded cleanup after
timeout. Committed history shares the settlement inventory's deadline, with at most 10,000 new commits and 100,000
distinct committed paths and bounded metadata and Git output reads. Non-regular reference files are refused without
blocking. Pattern comparison has its own 30-second deadline. Missing helper support, timeouts and special files mark
the result incomplete. With `problem` present, counts can be partial and zero outside paths does not mean clean.
This observes the workspace and reachable history, not filesystem isolation or which actor made a concurrent change.
Git administration itself, changes outside the workspace, uncommitted changes reverted before settlement and commits
no longer reachable from the run branch are not inventoried as changed paths.

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
  first record does. The name, platform, custody and presence are the device's own statement, which the
  runtime cannot verify; a name with control or formatting characters is refused.
- The admission is the administrator's, the operator's or a principal's holding `administer`, signed by the
  administrator's admitted device on the runtime's machine like every admission in format 2. Its note says
  that the device's descriptive fields are its own statement. From 0.6.1 it also names the device that obtained
  the pairing code and the operator-role revision or `administer` grant used at issuance; this may differ from
  the device signing the admission.

The in-memory code retains that issuing device, its admission revision and its authority record. Loopback uses the
requesting device; the local socket uses the admitted device selected to sign the administrator's admissions.
Revoking that device's admission, or revoking or expiring that authority, invalidates the code and open exchanges;
readmission, a restored grant or another admitted device cannot revive it. Renames, key rotations retaining admission
and authority revisions retaining the role or scope without an expiry gap leave it valid. Hello and confirmation
check this, and the estate write lock protects the final recheck before either record is written. The issuance log
also names the device and authority; codes and their derived keys never enter records.

On a normal completion the runtime writes both records; if admission validation refuses, it removes the newly
written device record. The writes are sequential, not a crash-atomic transaction: an interruption between them can
leave a device record without admission. A lost final reply can leave both recorded even though the client removes
its new key; inspect the devices and revoke an unwanted admission. The key a paired device proves on the local network, on every connection
and every request, is the one its device record names, and `profiles` decides where it connects: the local
network admits only a device whose current record permits `local-network`. A paired device's key stays on its
own machine, so `vericairn devices list` on the runtime's machine does not show it as held there.

Authentication does not reserve admission for a later write. The runtime rechecks the requesting device under
the estate's write lock, against the records then: it must still be admitted, not revoked, on the key that
authenticated the request and permitted `local-network`. A revocation, key rotation or profile withdrawal
recorded after authentication prevents the request's write, even if another device of its principal can sign
records on the runtime's machine. The failed request is kept in the local-network ledger; it adds no record.

A paired device's key signs no record after its own. A record it asks for over the local network is authored
by its principal and signed, as every API write is, by an admitted device of that principal whose key the
runtime's machine holds, or written unsigned where the charter allows it; the runtime refuses the write when
that device's every signature needs its owner's presence. So a record's signature names a device on the
runtime's machine, never the paired device that asked for it. The runtime keeps the paired device behind each
such write outside the records, in `local-network-writes.jsonl` beside its live store, which `vericairn devices
writes` lists ([api-v1.md](api-v1.md#the-local-network-profile)). Records signed by the paired device itself,
on its own machine, remain a future design. Remote is not built; WATCH-01 and HANDOFF-01 add no Remote signing,
transport or run-observation support.

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

## Work progress claims (0.7.0)

`work-progress` has an exact-hash `attempt` subject and body `{phase, note, completedAt}`. Only the attempt's author writes it. `phase` is a stable lower-case name (up to 100 characters), `note` a portable claim (up to 2000 characters), and `completedAt` a UTC timestamp. There is at most one record per attempt and phase, including superseded records; a completion cannot be superseded. The claim establishes neither liveness nor acceptance. Active notes replace local live state and create no records.

The `scope-read` and `attempt-missing` signal codes and settlement-review attention references require 0.7.2 readers, although the record-format number remains 2. The workspace, release-notice, command-audit, concurrent TLS handshake and current-attention changes in 0.7.3 introduce no record-format migration.

Agent-run operations may carry `signals`, a bounded array with one entry per code and severity: `code` (`network`, `credentials`, `scope`, `scope-read`, `audit-unverified`, `burn-rate`, `loop`, `stall`, `scope-settlement`, `attempt-missing`), `severity` (`warning` or `critical`), `at`, `summary`, optional SHA-256 `fingerprint`, positive `count`, and `attention` (an ask id). These are worker observations; raw commands, machine paths and secret values are excluded. `scope-read` warns about a read outside the workspace and does not stop the run; `scope` remains critical for confidently identified write targets outside the workspace and launch-supplied temporary directory. Outside-looking operands of unclassified invocations and opaque code produce `audit-unverified` warnings; a warning alone does not request a stop. Changes inside the workspace are checked against the brief at settlement. Network detections warn; recognized credential detections remain critical for reads and writes alike. Recognition is incomplete. FIX-20 in 0.7.3 recognizes Vericairn credential stores, token-output commands (`gh auth token`, `gh auth status --show-token`, `git credential fill` and Git credential-helper `get`) and more write forms, including literal interpreter writes that produce a critical signal alongside the opaque-code warning. Null-device output is exempt from outside-write classification. These remain observations of reported invocations, not an enforcement boundary. [The software guide](software.md#command-observations-070) describes those forms and limits.

A watched run's immutable `target.watch` is a JSON-encoded string of its resolved thresholds, and `target.watchHistoryTokensPerMinute` pins the recent historical median or `unavailable`. History uses only fully reported successful runs of that goal and launcher; no current run reports its own baseline.

A signal's `attention` refers to an ask about that run's original intent, except that `attempt-missing` and `scope-settlement` also allow an exact-hash settled revision in the same run's chain. The worker creates these review asks in the settlement batch, pinned to that settlement; later evidence revisions may retain the signals and their pinned asks. Original-intent references remain valid for historical records. One ask is recorded per signal kind and severity, including after a lifecycle event is repeated. Critical observations retain the existing `stop` facts through observed containment. The final `scope-settlement` warning describes a boundary that has already exited. FIX-18 also admits `attempt-missing`: a settlement warning that the agent recorded no new attempt on the run's exact assignment during the run. The same statement appears in operation evidence; the existing status meaning is unchanged. A missing attention id explicitly preserves a recording failure; it is not evidence that an ask was recorded.

Goals and rosters may record `limits.watch`, with optional integer fields `normalTokensPerMinute` (1–1,000,000,000), `burnMultiplier` (2–100), `burnMinimumTokens` (1–1,000,000,000,000), `burnWindowSeconds` (1–86,400), `burnCapPercent` (1–100), `loopWarning` (2–1,000), `loopStop` (3–10,000) and `loopWindow` (3–10,000). Specified loop fields must be ordered; the fully inherited settings must satisfy `loopWarning < loopStop <= loopWindow` before launch. Unknown fields, fractions and out-of-range values are refused. Rosters also accept `limits.stallSeconds` (1–86,400). These additions use the existing goal and roster revision authority, without another grant.

Each watching field resolves from launcher defaults through the current roster revision to the goal. The goal's named roster follows revisions; otherwise the estate's current roster applies, even with goal-specific roles. The intent records that roster revision in `target.watchRoster` when present, alongside the complete effective `target.watch`. A revision after launch cannot change that run's thresholds. [The software guide](software.md#recorded-watching-settings-070) lists defaults and examples.

## Estate observations (HANDOFF-01)

Record format 2 adds `estate` to the existing `observation.kind` values. This
kind requires a human author and an exact, hash-pinned `estate` subject. `asOf`
is no later than the observation record's authoring time. Its flat `facts` object
requires `records` (a nonnegative count), `recordSetHash`, `pinHash`, and
`statusHash` (SHA-256 hashes); the existing `summary` and `limitations` remain
required. Other observation kinds keep their existing rules, and record
format 1 does not admit the estate kind. Earlier builds that do not know this
kind refuse it; they must not silently treat it as an observation by automation.
No existing record is rewritten and the estate record format is not renumbered.

The runtime's explicit status-observation operation supplies all these facts
from the state before writing the observation: the record-set hash covers a
canonical compact JSON array of `{id, hash}` for every record, sorted by id;
the pin hash covers the canonical compact JSON serialization of `EstateManifest`
(including artifacts and storage profile); the status hash covers the canonical
compact status document with its checks. These are snapshot identities, not
claims of live external health. The write observes and accepts nothing else.
The compact `attention` projection added to runtime status responses in 0.7.3 is
not part of this recorded `statusHash`; the record-derived snapshot and existing
observation records keep their prior meaning.

Status derives `lastObservedByPerson` from current valid estate observations
about this charter or a predecessor, ordered by `asOf`, authoring time and id.
It includes source identities and facts and is null when unknown. A newer
record reporting an older observation does not advance freshness. Ordinary
reads and automation never manufacture a human observation. Hand-over and
resume are derived artifacts, not new record types or editable status records.

## Communication configuration (COMMS-01)

`communication-setting` and `communication-delegation` are additive format-2
record types. A pre-COMMS-01 loader reports `record.unknown-type` for either type;
it does not silently interpret the setting as an observation or ignore it. An
estate must use a COMMS-01-capable reader before authoring these records. This
adds no format 3 and changes no older record's meaning.

A setting has `scope` (`estate`, `person`, `assignment`), `values`, and a
`recipient` except for estate defaults. Assignment scope also names the exact
`assignment`. Values are independently optional: `progress` is `none`,
`completion-and-need`, `milestones` or `verbose`; `status`, `decision`, `result`
and `handoff` are `short`, `standard` or `comprehensive`. Omission inherits;
`values: {}` resets the scope. Revisions supersede the previous record and keep
the scope and recipient. Concurrent current profiles are explicit conflicts.

A person authors their own settings. An administrator can set estate defaults,
which cannot replace a person's explicit values. Another author must pin an
active, unexpired `communication-delegation` with `authority` and
`authorityHash`. That grant is authored by the recipient, names the `grantee`
and exact personal or assignment scope, and has `status: active` or `revoked`.
A revision revokes it without invalidating earlier authorized history. A
setting's optional `basis` and `basisHash` preserve an exact decision of its
recipient. The envelope always identifies and signs the actual author.

Resolution uses shipped defaults, estate defaults, the recipient's estate
values, their assignment values and a one-view override, in that order, for
each field. One-view values are never records. Full records, evidence and
canonical output remain unchanged by presentation depth.

Communication conflicts also appear in `reconcile`. A reconciliation about a
personal setting or delegation is the recipient's action, even when another
principal holds estate administration. Estate-default reconciliation requires
administration. It may keep one of the direct successors of a profile, or
settle independent first profiles at exactly the same scope (including the
same grantee for a delegation). It cannot cross recipients or scopes. Nothing
is deleted. An invalid imported preference or reconciliation is unavailable
for presentation; it cannot quietly select an untrusted value.

New writes recheck that delegated consent is current and unexpired at the
runtime's present time, including generic `records/new` writes. Historical
validation still checks when an existing record was authored, so a later
revocation preserves its earlier authorized history. Backdating a new record
cannot reuse revoked consent. Unknown fields in the new setting, values and
delegation request contracts are refused explicitly. Remote is not built.

## Explicit execution policies and run observations (RUN-01)

These are additive format-2 fields; no format 3 is introduced and earlier records are not rewritten.
`roster`, `goal` and `assignment` accept optional `execution`. Roster and goal role entries accept the
same policy. It contains optional `choices` and `restrictions` objects. Choices are portable string
identities for `model`, `effort`, `provider`, `destination`, `dataBoundary`, and `adapter`. Restrictions
are `requiredModel`, `requiredEffort`, `exactRevision`, `dataBoundary`, nonempty `permittedProviders` and
`permittedDestinations` arrays, and `maxCurrency: {amount, unit}` with a nonnegative safe integer and a
three-letter uppercase currency. Unknown keys, invalid types and nonportable identities are refused.

The worker resolves launcher configuration, roster defaults/role, goal defaults/role, assignment, then
explicit run choices. Restrictions are cumulative, never overridden by choices. Only a launcher's native
effort values are accepted; `provider-default` requires explicit support and `not-supported` requires no
effort control. Unknown model resolution never meets an exact pin. The worker refuses unresolved choices
or incomplete environment admission before authoring an intent.
The literal `unknown` cannot stand for a resolved required choice, even in a local model catalog.

An `operation` whose action is `agent-run` can carry `execution`, fixed at its intent and immutable across
revisions. It includes `configuration`, `configurationHash`, `requested`, `provenance`, `resolvedRevision`,
`harnessVersion`, `role`, `adapter: disabled`, `contextPackage`, `allocation` and `environment`:

- `provenance` entries contain scope, source, policy, and the exact contributing record id and hash for
  recorded policies. Overwritten choices remain inspectable.
- `allocation` contains known input/output token reservations and an `unknown` list. Its optional
  `context` object contains CONTEXT-01 accounting's `units: bytes`, positive safe-integer `bytes` and
  `maxBytes` (null for an unset limit, otherwise a positive safe integer at least as large as `bytes`).
  With that accounting, `contextPackage` must be an exact content hash; older snapshots may say
  `unknown` and omit context accounting. New runs pin the assembled payload, never a caller's
  unverified label. No package body or withheld original enters the run record or estate objects.
  Byte measurements do not establish model tokens or fit; token reservations remain separate.
- `environment` contains policy id, inventory hash, admitted variable names and the complete inventory.
  Each component has id, kind, version, content hash, configuration hash, environment names, destinations
  and dependencies. Kinds are harness, tool, skill, plugin, hook, mcp, helper, dependency and configuration.
  No machine paths, secret values or raw environment values are stored.
  Component variable names use the admitted environment's spelling, including Windows case matching,
  so the resulting record validates identically on every platform.

A settled admitted run also records `executionObserved` with distinct `reports`, `unknown` facts and
`discrepancies`. Reports can contain model, modelRevision, effort, provider, destination, harnessVersion,
tools and inventoryHash, only when actually reported. Missing facts are never copied from intent.
Unsafe or excessive reports leave `complete-report` unknown. A changed fact remains a discrepancy after
it changes back. These observations are immutable once settled under the existing settlement rules.
Older agent-run records may omit both fields; they establish no execution selection or environment claim.

Validation also checks that each pinned policy is the policy actually present in its source record,
that requested choices follow the recorded precedence and satisfy its restrictions, and that the
inventory hash matches its canonical contents with every dependency and route declared. Imported
intents receive these checks too. The worker selects the current work-order role and rechecks it
before dispatch, even when the assignment pins an earlier work-order revision.

WATCH-01 `signals` additionally admit `execution` and `tool-inventory` codes. They record critical
execution discrepancies or invocations outside the admitted tool inventory; existing command audit
signals remain intact. The efficiency Adapter stays disabled and absent; enabling it is unsupported.
Remote is not built. A native launcher without complete inventory qualification cannot dispatch.

## Harness qualifications (RUN-03)

`harness-qualification` is an additive format-2 type with no subject. Its required body is `launcher`
(`codex` or `claude-code`), one exact `version`, `status` (`qualified` initially), and `probes`, an object
containing exactly six SHA-256 digests: `version`, `flags`, `tools`, `configuration`, `login` and `mcp`.
A version is a bounded token starting with a digit and containing a dot, with ASCII letters, digits, dots,
hyphens and plus signs only; it expresses no range. Raw output, machine bindings and credential material are
not fields. The runtime's `worker qualify` operation signs the record with the actual author's admitted device.

Only a human operator or a human with the operator's explicit `qualify` grant may qualify or revoke.
`administer` and `local-engineering` do not confer that scope; an agent or service cannot use it. Historical
validation checks authority at authorship, including grant expiry and supersession. A later grant does not
authorize an earlier act; withdrawing a grant does not erase a qualification legitimately written before it.

A superseding record may only revoke a qualified record, with `status: revoked` and identical launcher,
version and probe digests. Requalification creates a new record from fresh probes. This differs from the
existing release/platform `qualification` type, whose subject is a release and which feeds the MCP matrix.

The worker admits the compiled codex-cli `0.159.0-alpha.12.1` and Claude Code `2.1.296` baselines or another
exact version whose local probe digests match a current unrevoked qualification. Its immutable
`execution.harnessAdmission` contains `kind` (`baseline` or `qualification`), launcher, version and probes;
a qualification admission also pins `record` and `hash`. The runtime rechecks admission after the dispatch
probes. Revoking a qualification preserves historical run evidence and does not withdraw a compiled baseline.
Qualification does not supply RUN-01's model configuration or environment policy and never authorizes work,
proves vendor provenance, qualifies live inference or accepts a result. Generic dispatch remains refused.
