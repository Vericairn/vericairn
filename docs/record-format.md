# Record formats, Vericairn 0.9.2

Vericairn 0.9.2 reads record formats 1, 2 and 3. It writes an estate's own format,
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

A record keeps the format in its own envelope, its original bytes, hash and signatures. Format-2 rules extend format 1, and
format-3 rules extend format 2. The admission rule is estate-wide: at format 2 or 3 an admission or revocation counts only when
it is signed by a device that an admission that counts admitted at the time it was signed, tracing back to the estate's first
authority, and its author may admit at its time, or when the signed move to format 2 or, from 0.9.2, an anchoring history
checkpoint anchors it, whatever format or date that admission claims. Those who may admit are the operator and any person
holding `administer`, read as they stood at the admission's time from the principal and grant revisions their authors may write,
as other rules read those records (signed, anchored, or, before the estate adopts history checkpoints, written unsigned while
the charter left signatures optional), and never through a grant revision a closing closed without witnessing the admission;
whether a holder of `administer` is a person is read only from revisions that are signed or anchored. The recovery device may
sign for them. An admission by anyone else is reported (`admission.author`) and admits and revokes nothing, also where
validation, reading roles as they are now, finds no fault, and then as a warning. One that would unseat what its own signature
or its author's authority rests on, such as a revocation stated as of a time before them, counts for nothing and revokes nothing
(the warning `admission.unseats-support`). Admissions are judged the same whatever order the records are read in, whichever is
written or numbered first. The root operator is the operator that the charter named by the estate's manifest
(`estate/manifest.json`, `estate`) states, and the estate's first authority is the root operator's admission of its own device
that the manifest names (`firstAuthority`), or the only one when it names none, until the estate adopts history checkpoints
([below](#history-checkpoints-092)). Another charter that supersedes nothing, or another such self-admission, is reported
(`estate.multiple`, `signature.self-admission`) and never takes its place, whatever its id or date, and one that comes with
history checkpoints of its own at most leaves the copy unsettled ([below](#history-checkpoints-092)); several self-admissions
with none named are reported (`manifest.first-authority`, listing each with its device) and none is the first authority until
the manifest names one. A reader without a manifest takes the first charter by id. Policy and writing changes and duty
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

## Release lines and channels (0.9.0)

These optional fields extend record format 3. Existing release bodies, hashes and
signatures stay unchanged; absence is read as `current`/`stable`, without writing defaults.
A format-2 record cannot carry these fields, and a format-2 reader refuses their
format-3 envelope. Upgrade participating readers before using the additions.

| Release field | Validation |
| --- | --- |
| `line` | A name matching `^[a-z0-9][a-z0-9.-]{0,39}$`; absent means `current`. |
| `channel` | `stable`, `lts`, `prerelease` or `nightly`; absent means `stable`. |
| `endOfSupport` | An RFC 3339 timestamp with time and UTC offset. Required for `lts`, forbidden on other channels, and no earlier than the release's `publishedAt`. |
| `backportOf` | Exactly `{id, hash}` of a current published release of the same product in another line. Candidates, withdrawn releases, stale revisions and mismatched hashes are refused. Every channel can carry a backport. |

A line keeps its channel across releases and product revisions. A `stable` or `lts`
line can also carry `prerelease` versions: the version has a prerelease suffix and
its numeric version is newer than that line's earlier stable or LTS publications.
A `nightly` line holds only nightly releases. A version belongs to one line;
duplicate current versions and published versions in different lines conflict,
including when releases reference different revisions of the product.

The product's optional `lines` field is a nonempty array of distinct declarations:
`{name, channel}` or, for LTS, `{name, channel, endOfSupport}`. Names, channels and
support dates follow the release rules. Releases referring to that product revision
use a declared line. Omitting `lines` retains the implicit-line behavior.

The optional product `cadence` field has shape
`{policy: {type: "estate-policy", id, hash}, rule}`. It pins an existing policy
revision and a rule key whose kind is `vericairn.estate/recurring-duty`, version 1.
Unknown keys, absent references, wrong types or hashes, and non-duty rules are
refused. The reference makes due occurrences release context in status, progress
and the submission gate; it never creates a release objection or performs an
action. Submission, candidate acceptance and publication remain separate actions.
See [release cadence](operator-guide.md#release-cadence-090).


## Estate release selection (0.9.0)

The `vericairn.estate-manifest/1` manifest's `vericairn` object accepts optional `line` and
`channel` strings in both source and release mode. Their names and values follow the release
field rules above, with absent fields reading as `current` and `stable`. The manifest has
no `endOfSupport` field: support comes from matching published releases and their product's
current line declarations. Existing manifests retain their exact bytes when read; no record
format migration or inferred publication accompanies these defaults.

Newer-release checks and upgrade targets require the same line and an equal or more stable
channel. `stable` and `lts` are equally stable, then `prerelease`, then `nightly`. Prerelease
versions are excluded for stable and LTS followers even when a historical record omitted its
channel. Version ordering uses semantic version precedence, ignoring build metadata.

Only explicit upgrade `line` and `channel` choices change the manifest's followed stream.
Ordinary upgrades preserve it even when the selected release is more stable. The upgrade
plan includes the resulting line and channel; the migration's `changes` describe an explicit
selection change. Pin descriptions include `[line/channel]` for a nondefault selection, so an
interrupted channel-only move is distinguishable and resumes with one migration. Default
`current/stable` pin descriptions are unchanged. Current status includes both effective manifest fields under `vericairn`.

`manifest.line-unsupported` is a check warning after the followed LTS line's support timestamp,
naming the supported lines known for the manifest's repository. It is evaluated at check time
and does not inject wall-clock or cached release knowledge into canonical status. No supported
lines known is reported explicitly. Legacy dependency observations and the shared latest-release
cache retain their `current/stable` meaning; other lines use published release records.

## Acceptance on passing reviews (0.9.2)

From 0.9.2, an acceptance by a principal other than the operator rests only on reviews of its subject whose
`outcome` is `pass`. The rule is prospective, as the decision-time rule added in 0.9.1 is: it adds no field and no format
number, and it binds the records written by a runtime that has it. Such a runtime refuses a new acceptance whose
`basis` names a review with outcome `findings` or `fail` (`acceptance.basis-outcome`, an error), in every record
format, whatever authority scope its author holds. An acceptance already in the estate keeps its reading: it still
accepts its subject, and `estate check` and `records validate` report it as information naming each such review and
its outcome. The operator's acceptance is unchanged; it may rest on any review of the subject, or on none.

## Output locations carry no scheme (0.9.2)

From 0.9.2, a result output's `location` names a repository-relative path and never carries a URL
scheme: letters, digits, `+`, `-` or `.` before a colon at its start, as in `https:`, `file:` or `s3:`.
The rule is prospective, as the decision-time rule added in 0.9.1 is: it adds no field and no format number,
and it binds the results written by a runtime that has it. Such a runtime refuses a new result, a
correction included, whose output location carries a scheme (`result.output-scheme`, an error), in
every record format and at every write surface. A result already in the estate keeps its reading:
`estate check` and `records validate` report it as one warning naming each such output, and the result
stays valid. A reference outside the repository goes in the result's `evidence`, whose `location` may be a
URL; an output held elsewhere is named by its hash and bytes without a `location`. Rooted paths,
backslashes and `..` remain errors (`result.output-path`), as before.

## Record authors (0.9.2)

An envelope's `by` names the authoring principal by its id, the id of that principal's first
record. From 0.9.2, a new record's `by` names a `principal` record the estate holds when the record is
written, whatever time the record's `at` states. The rule is prospective, as the decision-time rule
added in 0.9.1 is: it adds no field and no format number, and it binds the records written by a runtime that has
it. Such a runtime refuses a new record whose `by` names no record or names a record of another type,
such as the estate charter or a work order (`record.author`, an error), in every record format and at
every write surface. The root operator's own principal record names itself and is its own author. A
record already in the estate keeps its reading, whether historical, superseded or imported: `estate
check` and `records validate` report each unresolved author as information, and no record is rewritten.

What an unsigned `by` establishes is limited. Without a signature, `by` is the author's claim. From
0.9.2 it names a principal the estate holds, but it does not establish that this principal wrote the
record, nor that the principal was recorded by the time the record's `at` states: anyone able to add
records to a copy of the estate can write any principal's id and any time. Records that carry authority,
such as acceptances, admissions, authority grants and principal records, also check the claimed author's
roles and scopes, so a claimed author holds only what that principal holds; that check reads the claim
and does not authenticate it. Authorship is authenticated only by a signature from a device admitted for
that principal at the record's time. A charter with `policies.signatures: required` makes that signature
mandatory for every record dated after the current charter revision, so a copy without an admitted
device key is read-only. Once an estate has adopted history checkpoints, an unsigned record counts under
that charter only when anchored, whatever date it states, and a closing can refuse a signed record
whatever its date ([below](#history-checkpoints-092)). The author rule changes no admission, policy or signature check.

## Settling operations that share an identity (0.9.2)

From 0.9.2, a `reconciliation` also settles operations recorded independently under one identity, such as two
runtimes' concurrent intents written before the working tree's write lock, as it already settles independent first
communication profiles at one scope. Its subject is an operation; `kept` and every `withdrawn` entry name operations
under the subject's identity, each the latest revision of its own operation, and none a revision of the subject or of
another named operation (`reconciliation.identity`, an error, otherwise). The withdrawn operations count as superseded
by the kept one, so `operation.duplicate-identity` is no longer reported for them; no record is rewritten or deleted,
and `note` keeps the settling principal's reason. Only the operator or a principal with the `local-engineering` scope
settles operations this way (`reconciliation.author`), and the reconciliation is signed by an admitted device whatever
the charter's signature policy (`signature.required`), as a policy reconciliation is. A reconciliation whose records
supersede its subject keeps its earlier meaning. `operation.duplicate-identity` is now listed among the reconcile
tasks that `status` and `estate reconcile` show. The rule adds no field and no format number; an earlier release
reads such a reconciliation as an error and the withdrawn operations as current.

## An operation keeps its intent and target (0.9.3)

From 0.9.3, every revision of an `operation` keeps the `intent` of the revision it supersedes and every fact its `target`
recorded, with the same value. A revision may add a target fact it observed, such as the run a dispatch started, but never
changes or drops one: an operation with a different effect or destination takes its own `identity`. The rule is
prospective, as the decision-time rule added in 0.9.1 is: it adds no field and no format number, and it binds the records
written by a runtime that has it. Such a runtime refuses a new revision that changes its predecessor's intent
(`operation.intent-kept`, an error) or changes or drops one of its target facts (`operation.target-kept`, an error naming
each fact), in every record format and at every write surface, a record prepared for a device's signature included. A
revision already in the estate keeps its reading: `estate check` and `records validate` report it as information, and no
record is rewritten. Agent runs keep their own rule, under which the target and cap stay those of the run's intent
(`operation.agent-run-target-kept`). Repository changes keep their pinned target facts (`operation.change-target`) and
account-holder package actions their whole target (`operation.package-target`), as before, and both keep their intent under
this rule. The retry path is unchanged: a retry after a known failure or a cancellation that names its `reconciliation` pins
the settings it runs with, as a release retry that repairs its workflow or ref does, while a retry after an uncertain
outcome names its reconciliation and keeps the intent and target.

## History checkpoints (0.9.2)

From 0.9.2 an estate can hold signed `history-checkpoint` records, a type within
record format 3 ([contract](record-format-v3.md#history-checkpoints-092)). Each lists,
by path and hash in inventory objects, the records it first witnesses and names the
checkpoints it follows. What a counting checkpoint witnesses cannot be removed or
altered unnoticed: on any copy, an absent witnessed record is `history.removed`, one
with another hash is `history.altered` and a missing checkpoint or inventory object is
`history.checkpoint-missing`, each an error that leads `status` and holds release
submission until the bytes are restored or the root operator or a person holding
`administer` acknowledges the removal.

An estate that holds a counting checkpoint has adopted checkpoints. While its charter
requires signatures, an unsigned record there counts only when an anchoring checkpoint
of the root operator or a person holding `administer` lists its exact id and hash,
whatever date it states; an agent never anchors. An estate that has not adopted keeps
the earlier boundary: an unsigned record dated at or before the current required
charter revision counts. The first-party estate holds no checkpoint, so its reading
is unchanged until it adopts. A new estate adopts at birth, when its root operator's
first device admits itself; an existing one adopts when the root operator runs
`vericairn estate checkpoint --adopt`, and from then on the runtime writes checkpoints
at upgrades, release submissions, charter revisions and intervals ([operator's
guide](operator-guide.md#history-checkpoints-092)).

In every estate from 0.9.2, a record that does not count, such as an unsigned record refused by the signature policy, a record
whose signature does not verify by a device admitted when it was signed, or an altered witnessed record, is reported and kept as
evidence, and no rule that admits, authorizes, accepts, clears or holds reads it. An admission whose signature does not verify
admits and revokes nothing, and an unsigned device revision changes no key or name.

Witnessed history confirms the manifest's choice and never displaces it. Each charter that supersedes nothing is judged under
its own operator, and each self-admission of the root operator's device with it as the first authority; one is witnessed when,
so judged, the estate has adopted history checkpoints and they witnessed it, with its bytes present, before any rival. Since
added records can arrange that for a forged charter or self-admission with history checkpoints of its own, in an estate that has
adopted or not, a witnessed one counts only as the manifest's: the charter the manifest names, witnessed alone, is the first
charter, and the self-admission its `firstAuthority` names, witnessed alone, is the first authority. When another is witnessed,
alone or beside the manifest's, the estate check reports `history.root-ambiguous`, with `history.manifest-root` when the
manifest's own is not witnessed, and no record counts until the manifest or the history that does not belong is removed; neither
finding names an id to restore. A charter or self-admission that first appears after adoption is never witnessed first. When
none is witnessed, the manifest decides, as the introduction says. The runtime writes `firstAuthority` when the root operator's
first device admits itself, and the 0.9.2 upgrade writes it for an estate whose manifest names none, naming the first authority
the estate reads; a release before 0.9.2 refuses a manifest that carries it.

Canonical status adds `history`: whether the estate has adopted checkpoints, its heads
by id and hash, how many records they witness, the unwitnessed tail and the standing
problems, identical on every copy with the same records and objects. `estate check
--expect <checkpoint>` refuses a copy that lacks a checkpoint known elsewhere, and
warns `history.deleted-in-git` for unwitnessed record files deleted in the clone's
history ([CLI](cli.md#vericairn-estate-check---expect-092)).

A witnessed record that is removed is absent, and every rule reads the copy without it: a
removed revocation, for example, leaves its device admitted on that copy, bounded only by a
closing of that device or of its key ([below](#closings-and-authority-at-a-records-time-092)). While
`history.removed` stands, the runtime refuses every write that would leave it standing, since
it writes nothing that leaves the estate with an error, and release submission waits. An
acknowledgement settles the error and decides nothing else: it lists the witnessed version's
id and hash with a reason, the estate reads on without the record, and a record under that id,
written again or present with other bytes, is part of the unwitnessed tail and counts by its
own rules until a checkpoint lists it. An anchor likewise
exempts the exact bytes it lists from the signature requirement, from a closing's refusal except for a device revision, and from `history.altered` when only authors who may not anchor list other bytes for that id; every other rule still judges them, so an unsigned checkpoint, policy record or device revision, or an unsigned revision of a required charter, counts for nothing anchored or not. It authenticates nothing: the record's `by` stays a claim, which the rules judging
its author check against that principal's roles and grants as for any record, and its `at` is
read as any counting record's. A device revision a closing refused changes no key or name,
anchored or not. The runtime refuses an anchor of bytes not present, of a signed record no
closing refuses or of a device revision a closing refused, and an acknowledgement of a record
present with the listed hash or of a version no head witnesses.

## Closings and authority at a record's time (0.9.2)

From 0.9.2, in an estate that has adopted history checkpoints, a closing bounds what a
revoked device, a replaced key or a narrowed grant still accounts for, and roles and grants
are read as they stood at a record's time
([contract](record-format-v3.md#closings-and-authority-at-a-records-time-092)).

A closing is a history checkpoint with `closes`, written just before the act it closes:
`{kind: device, id}` before a device's revocation, `{kind: key, id}` naming the device
revision whose key a rotation replaces, or `{kind: grant, id}` naming the authority revision
that a narrowing revision replaces, one naming another grantee, dropping a scope, reserving
another action, adding a condition, adding or shortening an expiry, or changing a policy scope's
bounds; a grant closing closes that
revision and every revision of its line dated at or before it. Only someone who may perform
the act signs it: an operator, a person holding `administer` or an operator's recovery device
for a device, the device itself for its own key, and an operator for a grant. For a device
or a grant, the signer's role or `administer` grant must stand at the closing's time and
still stand, closed by no closing before it, read against the closings before it in
checkpoint order and never against the closing itself; the adoption checkpoint is judged
against the closings that act. Another author's closing is
`history.closing-author` and closes nothing, though it still witnesses what it lists.

From a closing on, a record signed by that device or with that key, or written by the
grantee while a closed grant revision was in force, counts only when the closing or a
checkpoint it follows witnesses it, whatever date the record states. The grant rule fails
closed: it refuses every such record of the grantee, not only those that needed the grant. Any other such record
is `history.after-closing`, an error that leads `status`, and counts for nothing until the
root operator or a person holding `administer` anchors it after reading it. That includes
a record from another clone written before the closing and merged after it. Such an
admission or revocation admits or revokes nothing until it is anchored, and such a device
revision changes no key or name, anchored or not. The closing acts by itself: deleting the
revocation after it reopens nothing, and a closing that no revocation, rotation or grant
revision follows still closes, which the estate check warns as `history.closing-unfollowed`. A
revocation, rotation or narrowing that counts without a closing naming its target, as when it
arrives as a file from a copy that had not adopted, is warned as `history.closing-missing`, unless
the adoption checkpoint witnesses it.

Roles and grants are read as they stood at a record's time: from each line, the latest
revision that counts dated at or before the record, unexpired when it was authored and
not closed by a closing that does not witness it. A later grant authorizes nothing earlier,
and a later narrowing withdraws nothing already written. A record the adoption checkpoint
witnesses keeps the reading it had then, read at the adoption's time. Anchoring, acknowledging
a removal, rooting an adoption and signing a device's or a grant's closing read more
strictly, as before: the role or grant must still stand, so a later narrowing withdraws those
powers for every checkpoint. They read a grant or principal revision as written when the root
operator wrote it, or an operator who held the role at its time and still holds it; as a
removal, whatever it says, when its author had been given the role by then but had lost it or
no longer holds it; and not at all when its author had never been given the role. A revision
read as a removal reads as written, except that it gives no operator role or `administer`, and
that on top of a revision read below it, it describes a human only where that revision does
too: so one saying the person is not human takes that away, and one with no revision read
below it, such as the first record of a person's line, describes a human when it says so. The
role is given by the root operator or by someone given it earlier, so a circle of appointments
gives nothing. A revision a closing
refuses is not read for anchoring, for the adoption or for the closings that follow that
closing. Removing an operator's role therefore takes, for those acts, what that person
appointed, granted and closed, and the warning `history.revision-author` names each grant or
appointment it takes; other records keep reading roles and grants as they stood. Roles and
scopes come only from principal and authority records their authors may write, judged in time
order from the root operator: only an operator gives the operator role, a principal's own
revision of its record other than the root operator's counts for nothing in every read, and a
removal keeps counting. Policy authority counts a person as the operator only while the role
still stands, or through a record that a checkpoint by the root operator or a standing operator
places before the role was taken away, and a policy record whose approval or authority fails
counts for nothing; where no checkpoint could place it before a removal, it is the warning
`policy.operator-removed` instead of an error. In an estate that has adopted checkpoints the
runtime writes a checkpoint before it writes a revision that takes a role away; in one that has
not, it refuses such a revision when any record would fail with it. In every
read, a grant or principal revision counts for nothing when it is dated before the nearest
record below it in its line that counts; a record below that does not count is skipped, and a
revision on top of one follows the line there. A role has no closing; role closings are planned. Close and revoke a person's devices when removing their operator
role or `administer` grant. Status and policy evaluation take their recorded time from the
records that count.

A signed record dated more than five minutes after a checkpoint that witnesses it is the
warning `history.dated-after-witness`; a record the adoption checkpoint witnesses is not
questioned. In every estate, the runtime refuses a stated time
more than five minutes past its own clock, such as `records new --at` or
`work attempt --at`. An estate that has not adopted checkpoints, such as the first-party
estate before it adopts, reads exactly as before.

## What a claimed time decides (0.9.2)

A record's `at` is its author's statement of when it was written. From 0.9.2, in every estate,
the runtime refuses a stated time more than five minutes past its own clock, and the runtime
writes a history checkpoint, a principal record and an authority record at its own time with a
fresh id, so `records new` refuses `--at` and an explicit id for them. Any earlier time can still
be stated for other records, and what it decides depends on whether the estate has
adopted history checkpoints.

- **In every estate**, `at` places a record that counts: its path month, its order, release-notes
  windows, progress, ages and duty sequence, ids breaking ties. A grant or principal
  revision dated before the nearest counting record below it in its line counts for nothing, and a new record's `by` must name a principal the estate holds when the record is
  written, whatever its `at` says.
- **Before adoption**, `at` also decides whether an unsigned record counts under required
  signatures, by comparison with the current charter revision, and which key and admission a
  signature verifies against. Roles and grants are read as they are now, apart from the rule
  above, the records that do not count and the principal and authority records their authors
  may not write; policy authority reads its own way ([above](#closings-and-authority-at-a-records-time-092)).
- **After adoption**, `at` decides neither alone. Under required signatures an unsigned record
  counts only when anchored, whatever its date. A signature still verifies against the key and
  admission of the stated time, but once a closing of that device or key acts, a record signed
  with it that the closing does not witness counts for nothing, whatever its date, until
  anchored. Roles and grants are read as they stood at the stated time, or at the adoption for a
  record it witnesses, bounded by grant closings; policy authority also needs the role to still
  stand, or a checkpoint to place the record before its removal; anchoring, acknowledging a removal, rooting an
  adoption and signing a device's or a grant's closing also need the role or grant to still
  stand (above). A checkpoint questions a signed record it witnesses, other than one the
  adoption witnesses, dated more than five minutes after the earliest-dated counting checkpoint
  that witnesses it, with a warning that refuses nothing. No checkpoint questions an earlier date: a signed record in
  the unwitnessed tail may state a time before the newest checkpoint, and is read at that time
  unless a closing refuses it. A revocation, rotation or narrowing stated more than five minutes
  before the newest record its closing would witness, or before the head checkpoint, is refused.
  A role has no closing; role closings are planned.

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
