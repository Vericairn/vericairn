# Record formats, Vericairn 0.9.1

Vericairn 0.9.1 reads record formats 1, 2 and 3. It writes an estate's own format,
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
counts only when signed or anchored by the signed move to format 2, or from 0.9.2
by an anchoring history checkpoint, whatever format or date that admission claims. Policy and writing changes and duty
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
`outcome` is `pass`. The rule is prospective, as 0.9.1's rule for decision times is: it adds no field and no format
number, and it binds the records written by a runtime that has it. Such a runtime refuses a new acceptance whose
`basis` names a review with outcome `findings` or `fail` (`acceptance.basis-outcome`, an error), in every record
format, whatever authority scope its author holds. An acceptance already in the estate keeps its reading: it still
accepts its subject, and `estate check` and `records validate` report it as information naming each such review and
its outcome. The operator's acceptance is unchanged; it may rest on any review of the subject, or on none.

## Output locations carry no scheme (0.9.2)

From 0.9.2, a result output's `location` names a repository-relative path and never carries a URL
scheme: letters, digits, `+`, `-` or `.` before a colon at its start, as in `https:`, `file:` or `s3:`.
The rule is prospective, as 0.9.1's rule for decision times is: it adds no field and no format number,
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
written, whatever time the record's `at` states. The rule is prospective, as 0.9.1's rule for decision
times is: it adds no field and no format number, and it binds the records written by a runtime that has
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
device key is read-only. The author rule changes no admission, policy or signature check.

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

In every estate from 0.9.2, a record that does not count, such as an unsigned record
refused by the signature policy or an altered witnessed record, is reported and kept as evidence, and no rule that admits,
authorizes, accepts, clears or holds reads it. Format 2 already read admissions this
way; 0.9.2 reads every type this way. An unsigned device revision changes no key
or name.

Canonical status adds `history`: whether the estate has adopted checkpoints, its heads
by id and hash, how many records they witness, the unwitnessed tail and the standing
problems, identical on every copy with the same records and objects. `estate check
--expect <checkpoint>` refuses a copy that lacks a checkpoint known elsewhere, and
warns `history.deleted-in-git` for unwitnessed record files deleted in the clone's
history ([CLI](cli.md#vericairn-estate-check---expect-092)).

## Closings and authority at a record's time (0.9.2)

From 0.9.2, in an estate that has adopted history checkpoints, a closing bounds what a
revoked device, a replaced key or a narrowed grant still accounts for, and roles and grants
are read as they stood at a record's time
([contract](record-format-v3.md#closings-and-authority-at-a-records-time-092)).

A closing is a history checkpoint with `closes`, written just before the act it closes:
`{kind: device, id}` before a device's revocation, `{kind: key, id}` naming the device
revision whose key a rotation replaces, or `{kind: grant, id}` naming the authority revision
that a revision dropping a scope or shortening an expiry narrows; a grant closing closes that
revision and every revision of its line dated at or before it. Only someone who may perform
the act signs it: an operator, a person holding `administer` or an operator's recovery device
for a device, the device itself for its own key, and an operator for a grant. For a device
or a grant, the signer's role or `administer` grant must stand at the closing's time and
still stand, closed by no closing before it. Another author's closing is
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
revision follows still closes, which the estate check warns as `history.closing-unfollowed`.

Roles and grants are read as they stood at a record's time: from each line, the latest
revision that counts dated at or before the record, unexpired when it was authored and
not closed by a closing that does not witness it. A later grant authorizes nothing earlier,
and a later narrowing withdraws nothing already written. A record the adoption checkpoint
witnesses keeps the reading it had then, read at the adoption's time. Anchoring, acknowledging
a removal, rooting an adoption and signing a device's or a grant's closing read more
strictly, as before: the role or grant must still stand, so a later narrowing withdraws those
powers for every checkpoint. They read authority only from the root operator or an operator
who still stands, and a grant or principal revision dated before the record it supersedes
counts for nothing; ordinary records keep the reading as it stood. A role has no closing, so
close and revoke a person's devices when removing their operator role or `administer` grant. Status and policy
evaluation take their recorded time from the records that count.

A signed record dated more than five minutes after a checkpoint that witnesses it is the
warning `history.dated-after-witness`. In every estate, the runtime refuses a stated time
more than five minutes past its own clock, such as `records new --at` or
`work attempt --at`. An estate that has not adopted checkpoints, such as the first-party
estate before it adopts, reads exactly as before.

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
