# Record format 3

0.8.4 added signed `saved-prompt` and `prompt-preference`
records, plus optional `promptUse` on assignments, agent-run operations and
ask-answer decisions. These additions stay within format 3; older records and
their original bytes remain valid. See [Saved prompts](prompts.md) and the
[generated reference additions](prompt-record-reference.md).

The 0.8.4 operator-request feature adds `operator-request`,
`operator-request-close`, and the `operator-request` observation kind within this
format. Exact content hashes, signed outcomes, agent citations and carry-over
links are described in [Operator requests](operator-requests.md). Existing record
bytes and their validation remain unchanged; no format migration is needed.

Record format 3 adds attributable policy definitions, bounded exceptions and recurring-duty evidence. It is
the POLICY-01 record and migration contract for 0.8.0. A manifest at format 3 makes a
format-2 reader refuse the estate before opening a writable runtime. Each older record retains its original
format, bytes, hash, signatures and validation rules.

The `record-format-3` migration moves format 2 forward without inferring policies, authority, placement,
exceptions, completed duties or renewal dates. Once every migration step succeeds, the upgrade writes the
steps' files, then appends its migration record, then replaces the manifest by writing a temporary file
beside it and renaming it over the manifest. A failed step or a refused migration record restores the
previous repository files. A run that stops after its migration record leaves the manifest at the earlier
pin: the estate check warns `upgrade.incomplete`, and rerunning the upgrade to the same release replaces
the manifest without a second record. Reapplying an already installed target is a no-op, except that a
manifest pinned to a release no migration record names is refused with what to reconcile, because only an
upgrade that stopped before recording its move leaves one. Tests exercise disposable fixtures only.

Policy evaluation and the CLI, API, SDK and MCP policy flows are described in [Estate policies](policies.md).
Workbench's policy inspector remains separate work. An unrecognized rule contribution is retained as data; the record schema does not certify
that the rule ran or passed. This change does not declare any policy in an existing estate.

## Release review declarations and retained briefs (0.8.4)

A decision may add `releaseReview` with exact `candidate` and `workOrder` references
(`type`, `id`, `hash`), `scope` (`full` or `focused`), and `focus` and `probes` string
arrays. The candidate reference must name a release record with candidate status.
A focused scope has at least one focus item; a full scope leaves that array empty.
Both arrays are required and each item has the ordinary 2,000-character bound.
The normal decision rules still require `decidedBy` to hold the operator role.
The declaration itself assigns and launches nothing.

The review-brief command requires a current decision and its exact current candidate
and work order. It reads disposition decisions by their exact review subject; it
does not infer a disposition from unrelated decision prose. Its assignment carries
the source references in `brief.sources` and the unchanged generated text in optional
`brief.reviewBrief`, limited to 1,000,000 characters. This field is displayed in the
worker's work brief. All existing assignment fields and authority checks remain
required. No historical record is rewritten, and formats 1 and 2 do not admit these
fields. See [Preparing a release review](software.md#preparing-a-release-review-084).

## Definitions and authority

`estate-policy` has `key`, `title`, `owner`, `reason`, `basis`, `state`, `appliesTo`, `rules`, `level`, `startsAt`
and `maxExceptionDays`. Optional `predecessor` pins a retired policy of a different key. A retired successor
requires `retirement: {reason, findings, successor?}`. Retirement is terminal. Keys remain reserved by their
identity after retirement. Ownership records responsibility; it grants no permission.

`appliesTo` is exactly one of:

- `{kind: estate}`;
- `{kind: product, product}`;
- `{kind: work-orders, workOrders}`;
- `{kind: repository, repository, selectors}`.

Selection ids name the first record of a continuing identity. Repository selectors use `/`, literal path
components, `*` within a component and `**` across components. They cannot name an absolute path or traverse
parents. Rules have unique stable `key`, namespaced `kind`, positive integer `version` and object `parameters`.

A decision's optional `policies` list contains unique `{key, action, policy, change, priorHash?, definitionHash?}`
requirements. `action` is `declare`, `revise` or `retire`. Declaration names a policy key; revision and retirement
name its identity and exact prior hash. `definitionHash` hashes the canonical proposed policy body with only
`basis` omitted. A requirement without that hash is pending design or reconsideration, not executable approval.
An exact definition approval is signed and authored by the deciding operator. It supplies no management grant.

Policy `basis` is a nonempty list of `{decision, requirement?, authority?}`. References contain `type`, `id`
and `hash`. Initial declarations and behavioral changes must match an exact approved definition and prior
revision. Title/reason edits and ownership transfer still require an attributable management action. A change
that both transfers ownership and changes behavior needs both permissions.

The three scopes are independent. Each grant uses the existing `authority` record with an explicit expiry,
operator signature and `bounds`, one entry per granted policy scope:

| Scope | Required bounds |
|---|---|
| `manage-estate-policies` | `scope`, `actions`, exact policy keys or identities in `policies`, `levels`, and exact `appliesTo` selections |
| `grant-policy-exceptions` | `scope`, `actions`, `policies`, `levels`, exact `rules`, exact finite `targets`, `maxExceptionDays` |
| `manage-writing-exceptions` | `scope`, `actions`, exact `tokens` or `allTokens: true`, optional `coverage` using the writing scope below |

Management actions are `declare`, `revise`, `transfer`, `retire`, `reconcile`; policy-exception actions are
`grant`, `revoke`, `reconcile`; writing actions are `add`, `remove`, `reconcile`. A policy grant's prose
conditions are not presumed satisfied. A delegate with conditions that cannot be checked needs a different,
explicitly bounded grant. Reserved actions remain reserved. Scope containment is conservative: equal
selectors, `**`, and literal directory prefixes ending in `/**` prove containment; other unproved inclusion
is refused. Generic engineering or administration authority does not substitute for these scopes.

Authorization uses the cited grant as it existed at the action's authoring time. Future grants cannot
authorize earlier work. Expiry or a later supersession prevents subsequent delegated actions while preserving
historical attribution. A policy reconciliation requires format 3, a signature and the same bounded authority;
its optional `policyAuthority` pins the grant used to choose the kept branch. The branch's own definition
approval still applies. Discarded successors remain in the records.
Removing all policy scopes from a grant still requires an operator-signed format-3 successor.
Reconciling that grant's history also remains an operator action after those scopes are removed.
Operator role evidence must trace through signed appointments to the charter's bootstrap operator;
an unsigned role claim or a principal's self-promotion supplies no policy authority.

## Exceptions

`writing-exception` contains `token`, `code: writing.spacing`, `reason`, optional `scope`, `state` and `basis`.
A token is one case-sensitive whole scanner token, not a pattern. `basis` is `{kind: operator}` or
`{kind: authority, grant}` with an exact authority reference. A scope is a union of `paths` entries
`{repository, selectors}` and `records` entries `{record, field?}`; at least one list is nonempty. A record
selection names that exact record id and optionally one scanned prose field. It follows no revision.

Addition uses `state: active`. A signed removal uses `state: removed`, supersedes the addition and includes
`removalReason`. Token, code, original reason and scope stay fixed. A removed identity cannot be revived.
Concurrent entries for the same token are separate identities. Expiry and policy-revision fields are invalid
on writing entries.

`policy-exception` pins the exact policy in its envelope and has `rule`, nonempty `targets`, `reason`,
`expiresAt`, `basis: {decision, authority?}` and `state: granted`. Each target is a tagged exact record,
publication, repository/path/content hash, or duty occurrence. An optional `submission` carries an exact
`release` reference, `product` identity and `version`. A duty target carries its originating `policy`, `rule`,
exact examined `subject` and tagged `occurrence`. It contains no future occurrence or range wildcard.

Expiry follows the grant and fits the policy and delegated duration limits. Expiry is exclusive. A signed
successor with `state: revoked` retains the policy, rule, targets and expiry; renewal is a new grant. Only the
operator or the still-authorized original grantor revokes. Delegates cannot except their own work or duties;
file provenance that cannot establish independence requires the operator. An exception records deferred
enforcement, never completion or acceptance.

Every policy, policy exception and writing exception, including lifecycle successors, requires an
admitted-device signature even under optional charter signatures. Cited decision and bounded grant evidence
must also be attributable. A body copied into an unsigned record supplies none of these permissions.

## Duties and evidence

`vericairn.estate/recurring-duty`, version 1, takes `subject`, `schedule`, `graceDays`, `discharge` and `renewal`.
The subject is an estate, product, resource, device or credential-reference identity. Estate and product
selections are supported; a product resource must carry an explicit product relation.

| Schedule | Fields |
|---|---|
| `interval` | `unit: days|months`, positive `every`, UTC `firstDueAt` |
| `release` | product identity; occurrences pin the first observed publication |
| `date` | UTC `at`, nonnegative `leadDays`, exact `dateSource` |

Month arithmetic always uses the original anchor. Grace is nonnegative and cannot extend past a dated expiry.
`dateSource` is `{kind: credential-rotate-by, record}` or `{kind: reviewed-report, result, review}`. Typed
credential dates must match the selected credential. Reports retain hashed outputs and an exact signed
independent passing review. A date interpretation still needs exact definition approval.

`discharge` contains unique required check names in `checks`, `review: none|independent|operator`, and a `records`
list of unique named clauses. Clause kinds are `verification`, `device-rotation`, `credential-rotation`,
`observed-operation`, `retirement` and `obligation`. These are fixed record relationships, not executable queries.
`renewal: next-date-required` is valid only for a date schedule; other schedules use `none`.

`result.duties` contains `{policy, rule, occurrence, subject, scope, coverage, proofs}` entries. Evidence
references always pin type, id and hash. The occurrence is `{kind: interval, number}`, `{kind: release,
publication}` or `{kind: date, at}`. It names the originating schedule revision; an editorial revision is not
a new origin. Scope contains `repositories: [{record, commit}]`, retained hashes in `objects`, and exact
`records` references. Scope describes evidence and does not extend an assignment's permitted effects.

Proof bindings are `{clause, records}` in the clause's defined order, except a verification of the result
itself uses `{kind: result-verification, clause}`. This avoids a hash cycle. An ordinary result review can
carry unique exact references in `read`; it cites the later verification. Goal reviews keep their existing
`conditions.read`. Duty results and their reviews require admitted-device signatures. Recording a result
before verification, review or a next-date declaration is permitted; its completion evidence remains pending.
From 0.9.2 (FIX-94), a review requires that signature only when its subject result revision has `duties`.
Its `read` list alone no longer requires one, so an unsigned review of a result without duties may carry
`read`. Such a review is valid in 0.9.2 and later. An earlier build reports it as `signature.required`.

`estate-policy.dutyDispositions` entries contain `rule`, exact originating `policy`, subject identity,
`occurrence`, `disposition: ended-unmet` and `reason`. They are part of the approved definition. Entries name
unique already-due, uncompleted occurrences from that policy's history. A removed rule or retired policy must
account for every outstanding occurrence, including grace. Prior dispositions stay effective without being
repeated. Schedule changes begin strictly after their revision and carry old debt; postponing activation cannot
hide an occurrence already due. Ending debt never labels the work completed.

## Work placement and verification

`work-order.placement` is `{kind: release, carrier, product, version}` or `{kind: backlog, key}`.
`work-order.releasePlan` contains `{product, version}`. A release carrier can place itself in its own plan.
These fields organize work under its existing authority and do not create dependencies or acceptance.

A work-order revision that changes only `placement` keeps records pinned to its predecessor current.
Assignments, their attempts and results, reviews and acceptances retain their original ids and hashes;
moving the work does not require replacement records. Currency is derived from the recorded revision
chain, so existing placement-only `record.subject-stale` warnings clear when the estate is read again.
Every intervening revision must change `placement` and preserve the rest of the body. An identical-body
reissue still requires reconsideration. Any other body change, including
`releasePlan`, still requires reconsideration, even if a later revision restores the earlier content.
The original subject hash must still match. This exception does not apply to revisions of assignments,
attempts, results or other record types.

## Checked repository changes

A product's optional `changes` object has `provider`, a repository resource id in
`repository`, the target branch in `target`, and exact host check names in
`requiredChecks`. An absent or empty required-check declaration authorizes no merge.
`product.changes` is a format-3 field. Only the operator or a principal with the `administer` scope declares,
changes or removes it (`product.changes-author`), and only they move a format-3 repository resource's
`location` in a revision (`resource.location-author`); other fields stay open to their usual authors. Repository-change operations retain their
existing identity and intent/effect lifecycle and add `checks`, `requiredChecks`
and `scan`, also gated at format 3. Checks bind each reported name, head, status
and optional conclusion. A push scan retains only the tool, version, range,
verdict and optional finding count, never matched text or raw output. From 0.8.2
the version also names the scan configuration's SHA-256.

The runtime requires repository-write authority and exact source revisions;
[checked changes](changes.md) describes the scan, head lease, synchronous merge
and target-run observations. [Operator record](operator-actions.md) uses these
operations after signing one ordinary record with the operator's own device.
It requires repository-synced storage at format 3 and adds no record type. A
recorded successful merge and its required target checks remain separate from
acceptance or publication.

## Generated living documents

READ-01 extends `living-document` in format 3 with `pathPattern`. It is exclusive with `path`, requires
`kind: generated`, and cannot carry authored `regions`. The supported form is a literal portable directory
followed by `/{month}/{date}-{slug}-{id}.md`. The source revision supplies its UTC month and date, a bounded
ASCII title slug (or `item` when none remains), and its full revision id. The directory is owned by one
declaration; overlapping declarations, authored files and case collisions are refused. A pattern is never
evidence of membership by itself: the generator enumerates the complete selected source inventory.

A whole generated format-3 declaration permits `triggers: []` and has no `clearedBy`. Format-2 records still
require a concrete `path` and nonempty triggers, and refuse `pathPattern` with a format error. No new record
type or companion-status field is introduced. The signed golden fixture includes the new field, and its
inventory retains every fixture byte. Generator interface and output-contract versions are independent of
record format. Legacy region inputs retain their existing grammar; versioned generators use validated named
inputs, including an explicit `format`, `audience` and `selection`. Missing contracts and incomplete source
selections are unavailable, never a successful empty collection.

New format-3 whole generated declarations require admitted-device signatures and mandatory fresh member
and byte comparison. They require no output pin or authored clearance. Authored documents, including those
with generated regions, keep their exact-file pins. Upgrading the manifest or making an editorial successor
does not convert a legacy generated lineage. An explicit conversion submits canonical declaration-basis and
complete before/after preview outputs in an ordinary `result`, followed by an authentic independent operator
`review` of that exact result. These use existing output/evidence fields, not a new record or mode field.
The preview identifies the prior declaration, owned paths, existing pins, exact source digests and proposed
bytes. A successor adopts the reviewed definition; generator or ownership changes need a new reviewed
migration. Historical pins remain unchanged. Strict whole generated outputs refuse later pins and
`document-review` records, and neither missing implementations nor a matching historical pin can establish
currentness. External repository comparisons require an exact commit and a complete versioned source
inventory; they cannot substitute a clone's head for the release commit.

### Canonical rendering and contribution coverage

The immutable renderer interface, output contract, rendition response and coverage
descriptor have separate version numbers. `DocumentRendering` takes only selected,
verified snapshots and named inputs. Stored and on-demand views use that same
renderer. Input digests identify the relevant source revisions, generator and
implementation; the comparison commit is response metadata and never becomes an
input to its own output. Each pattern member receives only its own revision.
Generated source dependencies must be current at the same basis and acyclic.

Canonical Markdown is UTF-8 without a BOM, with LF endings, no trailing horizontal
whitespace and exactly one final newline. Source wording is escaped as literal
data, numbers and UTC dates use invariant formatting, and selections have explicit
ordinal order. The original source hashes still describe original bytes. Renderers
return explicit success, unavailable or error; failed renditions contain no bytes
or digest. Ordinary repository content checks and the declared size bound apply.

`DocumentCoverage` compares the authoritative kind inventory and shipped descriptors
in both directions. It includes admitted record types and declared collection
fields; owning contributions supply deeper nested and non-record collections.
Each exact kind names its reader task, item/list contracts, input schema, Internal
audience and destination, or a bounded no-reader reason. Missing and duplicate
coverage, missing implementations and unsupported versions fail conformance.
Descriptors do not certify prose quality. The READ-02 and READ-03 builds supply the
first-party readers and their coverage; this foundation does not claim that an
absent reader is covered by a raw JSON fallback.

Named-input schemas accept fixed literals and bounded typed alternatives for exact
identities, UTC instants, hashes, commits, repository paths and text. A date or
interval is an explicit input; no renderer reads the current clock. Unknown grammar
tokens, duplicate names and unsupported output versions are refused.

### Repair, retirement and interrupted writes

The existing `docs regenerate` operation selects a declaration, register directory
or proved member. It prepares the complete rendition before publishing any bytes,
refuses unavailable generators and malformed markers, and preserves authored bytes
outside regions. It neither prunes unexpected files nor writes pins. Authored files
still need their ordinary updated pin after a region changes.

Repository record writes prepare the affected generated members before storing a
record. A rendering failure writes neither records nor outputs. The existing local
batch journal carries prepared output changes until publication completes. Each
destination replacement is atomic and guarded by its observed hash. An I/O failure
after records were stored reports those records together with a recovery problem;
it never calls them rolled back. Reopening the estate or using the existing repair
route resumes verified pending outputs. Changed destinations or unavailable fresh
inputs hold recovery and further writes. The transient journal is removed after
completion and is not companion authority or a second status ledger.

The existing signed `retirement` record names the exact generated file or register
directory. Retiring generated ownership requires operator or local-engineering
authority and a complete current inventory; unexpected or unavailable members are
never pruned. Generator, destination and region-contract changes require a fresh
reviewed migration preview. Restoring authored ownership requires an exact pin in
the proposed batch. Source records and historical pins are retained unchanged.
Local-only records remain in their canonical live store; committed estate companion
adoption requires repository custody, while on-demand rendering needs no file.

## Template instances and section evidence

TPL-01 in 0.8.4 added optional fields and one type within
format 3. Estates with no template continue using their existing declarations,
reviews and result outputs. No migration rewrites older records, and no template
field is admitted in formats 1 or 2. The contract and workflows are described in
[Templates](templates.md); the [generated reference](reference/record-reference.md)
comes from the registered type and field descriptions.

| Record | Additive contract |
| --- | --- |
| `living-document` | Optional `template: {id, version, hash, owner, scope?, options, overrides}` pins canonical template content and records every effective choice. Optional `templateUpgrade: {offered, outcome, reason}` records exact acceptance or explicit decline of an offered version. |
| `document-review` | Optional `sections` array, required when reviewing a template instance. Each entry has `id`, `hash`, `sourceDigest`, `outcome`, `fidelity`, `sources`, `checks` and `cohesion`. |
| `result` | Optional `outputs[].section: {id, document: {type, id, hash}}` identifies exact payload bytes within a living document; `location` names the declared document path. |
| `template-defaults` | New type since format 3: exact `template: {id, version, hash}`, `scope: {kind, target?}`, `options` and `reason`; no envelope subject. |

The instance's owner or operator revises its declaration. An owner transfer needs
operator standing. A revision keeps its template contract; explicit required
omissions need the operator's exact decision. A changed pin needs an accepted
upgrade matching the offered content. A declined offer retains the previous pin
and its reason. Existing history and file bytes are not automatically migrated.

Review `sources` retain each source's id, kind, identity, hash, byte count, status
and exact record references. `checks` retain name, outcome and summary. Cohesion
entries retain id, kind, outcome and summary; evaluator entries also retain the
exact evidence hashes read. The runtime recomputes new review evidence before
storing it, including writes through generic record authoring. Historical review
validation checks its declared evidence structure without comparing old bytes
against today's files. Currentness separately reads current content and sources.

Fresh section results must match the actual payload hash and any stated UTF-8
byte count. Later edits retain the historical result and report output drift.
Missing retained template content prevents clearance. Installed role names,
options, defaults and all passing checks supply no authority or acceptance.
Defaults require existing operator or `administer` standing and are unique per
exact template and adoption scope; narrower defaults are materialized only at
creation.

`records describe template-defaults`, `records describe living-document`,
`records describe document-review` and `records describe result` include these
contracts. Registered readable companions cover the new type and nested
collections, retaining every source, check, judgment and option rather than
reducing them to a summary.

TPL-02 additionally admits root `README.md` as a `document-revision.path` in
format 3, so the estate's README template can retain exact revision history.
The `design/` and `docs/` grammar is unchanged, and formats 1 and 2 do not admit
the extra path. This adds no field or type and does not change the record format.
The registered `records describe document-revision` entry and the development
reference include that value; generic readable companions retain the complete
pin. [Adoption](templates.md#adopt-an-existing-estate-document) keeps existing
records and authored bytes and explicitly prepares any generated regions.

## Runs of a flow (0.9.2)

PROC-05 adds optional fields within format 3 and no record type. A goal without
`plan.flow` and an assignment without `step` behave as before; no migration
rewrites a record. A flow is template content of kind `flow`
([Templates](templates.md#flows-092)), not a record.

| Record | Additive contract |
| --- | --- |
| `goal` | Optional `plan.flow: {pin, subject, options?, until?}`. `pin` is the exact `{id, version, hash}` of a flow; `subject` binds every field of the flow's subject: a record as `{type, id, hash?}` of the declared type, a text, or a list of distinct instance names; `options` maps flow options, `step.<key>` and `item.<step>.<item>` to allowed values; `until` names the step the run ends at. A run's plan is `given`, with exactly one ordinary work order. |
| `assignment` | Optional `step: {key, instance?, inputs?}` names the flow step the assignment carries, its instance for a step that runs over a list under `each`, and each input its brief pins as `{name, type, id, hash}`. Every assignment of a run's work order names a work or review step of the run's flow, and no other assignment names one. A reserved step, or a step of a role the flow marks person, is never assigned to an agent. |
| `result`, `operation` | Optional `provenance: {inputs?, provider?, tool?, settings?}`: what produced it, where a flow item's provenance requires it and its other fields do not show it. `inputs` are `{name?, type, id, hash}` of exact records; `settings` are string values by name. A reader lacking the field shows the record and leaves an item that requires it unsatisfied. |
| `template-defaults` | Unchanged fields; for a flow's exact pin, `options` configure the flow's options, its optional steps' `step.<key>` and its optional items' `item.<step>.<item>` (`on` or `off`) at the estate, domain or product scope. |
| `estate-policy` | A rule of the foundation kind `vericairn.estate/flow-steps`, version 1, with parameters `{flow, steps?, items?}`, requires the named steps and items (`step.item`) of the flow ([policies](policies.md#required-flow-steps-092)). |

Validation refuses a configuration, in a goal's plan or in `template-defaults`,
that names an option the flow does not declare or a value it does not allow,
turns off a required step or item (whose reserved marking it can therefore never
remove), turns off a step or item an active estate policy requires or one an
extension turned off, or leaves a required or policy-required step without its input; a run whose
effective configuration leaves a policy-required step off; an `until`
that names no step, a step that is off, or ends the run before a policy-required
step; and a goal that names an agent in a role the flow marks `person`. An
assignment of a run's work order that names no step, a step that is not a work
or review step, an instance the run's list does not hold, or an undeclared input
is refused, and so is an agent's assignment to a reserved step or a step of a
role the flow marks `person`. A goal's `plan.flow` in an earlier format is refused as a later
format's field.

A reader that cannot resolve the pinned flow, with every base it extends, keeps
the records valid and warns `flow.unavailable`: it shows the goal, and neither
`goal step` nor a worker run takes any of its steps there. A step's state, and each of its
items' state, is derived from the records whenever it is read and never stored:
an item's value from the step's result or the record it names at an exact hash,
its provenance from that record, its evidence from `verification` records and
its review from `review` records of that exact hash; see the
[operator guide](operator-guide.md#runs-of-a-flow-092). `records describe goal`,
`records describe assignment`, `records describe result` and `records describe
operation` include these contracts.

### Hand-offs, reviews and failure routes (0.9.2)

PROC-10 adds one optional field within format 3, and no record type. An
assignment without `step.review` behaves as before.

| Record | Additive contract |
| --- | --- |
| `assignment` | Optional `step.review: {items?, subject}` marks an assignment of a step's review before hand-off rather than of the step itself. `items` names the step's items it reviews, omitted for a step that declares none, whose result it reviews; `subject` is the exact `{type, id, hash}` of the record that carries them: a result of the step's own work for a file or the step's result, or a record of the type a record item is counted from. Its contributor reviews, and is never among the step's authors. |
| `goal` | `limits.consecutiveFailures` holds a run of a flow in `goal next` and `goal step`: that many failed steps in a row hold the run until its sponsor answers. A run started directly with `worker start` is not held by it, and for any other goal it is still recorded and not enforced. |
| `operation` | An `agent-run` intent on an assignment of a run's flow step is refused (`operation.unreconciled`) while a result of that step's work, under any of its assignments, names `unresolvedEffects` at its current revision; revise the result once each effect is reconciled. Assignments of other work keep their earlier reading. |

Validation refuses a review assignment of a review step, of items the step does
not declare or of items that take different review requirements, and one whose
subject is not what carries its items. Its contributor is refused when it is an
author of the step's work (a contributor of one of the step's assignments, or
whoever recorded an attempt or a result under one), unless every item it reviews
takes a `self` review; when the review is a person's and it is not a person;
when the review is on another harness and it is not an agent whose harness none
of the authors used; when it is a person without the operator role, who records
no review of another's work in this build; and when it is an agent and the
review's role is one the flow marks `person`. A flow's `failure` route and a
role's `document` are template content, not record fields; see
[Templates](templates.md#flows-092). `records describe assignment` and
`records describe goal` include these contracts, and the generic record reader
advances to `vericairn.records/readers/8`.

## Search selections in agent runs (0.9.2)

CONTEXT-02 adds two optional fields to `agent-run` operations within format 3, and no record type. A run without them is
unchanged; an older release refuses a record that has them, and the manifest's pin keeps an estate on one release.

| Field | Contract |
| --- | --- |
| `execution.contextSelections` | Fixed at intent with the run's `execution`: each dispatch-time search of the run's package, at most five, each of at most 50 candidates. It requires the exact `contextPackage`. |
| `searchesObserved` | Recorded at settlement only, on an `observed-success` or `known-failure` revision, and kept by every later revision: the searches as logged by the run's MCP server, which is not tamper-evident under a harness that does not sandbox the agent (the run does not record whether it was sandboxed). `selections` (at most 100), `complete` (whether every search the log holds is kept; false, with no selections, when the log the worker created at the run's start is missing or cannot be read) and `digest`, SHA-256 of the canonical JSON array of every observed selection's hash in order, which a complete list's selections must equal. A run whose log is empty records no `searchesObserved`. |

Each selection is a `vericairn.search-selection/1` object with its own `hash`, SHA-256 of its canonical JSON without the
`hash` member: `method` (`keyword`, version `vericairn.search.keyword/1`, analyzer and engine), `query` (the exact `text`,
parsed `terms`, `phrases` and `filters`), `limit` and `selectLimit`, `reader` (`principal` or null, and the `audiences`
searched), `index` (`revision`, each segment's `audience` with its `records` and `documents` basis hashes, `current`, `commit`
or `uncommitted`, and `asOf`, the latest recorded time among the indexed records), the ranked `candidates` and `more`. The
analyzer string names the Unicode version of the tables it normalizes and folds with (`nfkc-casefold unicode-16.0.0; ...`). A
candidate holds its `rank`, integer `score`, `kind`, `audience`, `source` identity, `pin`, matched `fields`, `extent` and
`state` (`selected`, `duplicate`, `below-limit`, `excluded` or `index-fault`), with a `reason` for the last two; an index
fault's source names only what the index held. A record carries only candidates whose audience is Internal or Public, and a
selection that omits others says so in `limited`, without a count. Its `reader.audiences` and `index.segments` likewise name
only Internal and Public, with one opaque entry, `Restricted`, standing for every other segment searched: in the segments it
is `{"audience": "Restricted"}`, with no basis hashes. Validation refuses a hash that does not match, a value canonical JSON
cannot hold (a fraction or a duplicate member, reported as a finding), a candidate, audience or segment of another audience in
a record, more than one `Restricted` segment, more candidates or selections than these limits, an out-of-order rank and any
field not listed here.
The context manifest that holds a package's selections is `vericairn.context-manifest/2`; it is a derived artifact, not a
record. The operation's field descriptions change within `vericairn.records/readers/8`, which has not shipped.
