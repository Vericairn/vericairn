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

**The dispatch scope (0.9.4).** From 0.9.4 (ID-07), unattended dispatch is signed by a dispatcher: a `service` principal whose
only role is `service` and that holds no other grant. Its `authority` record gives `dispatch` alone, is signed, states no
`conditions`, expires no more than 30 days after it is made, and carries one bound, `{scope: "dispatch", goals, maxAssignmentsPerDay,
reassign?}`: `goals` names the goals and standing agents it carries by the id of each goal's first revision,
`maxAssignmentsPerDay` (1 to 10,000) caps the assignments it dates in one UTC day, and `reassign`, false when omitted, lets it
reassign a step another assignment still holds, with no result on it, as after a lapse. A dispatch grant is made only to a principal
with no history before it: no grant of any scope its author may write and no assignment to it by another principal who may assign,
dated before the grant, unless an earlier dispatch grant to it is valid, as a renewal; the principal's own records, and records their
authors had no authority to write, never count as such history. A grant of another
scope to a principal holding a dispatch grant, or that held a valid one at or before it, during its term or after it expired, a
dispatch grant to a principal holding another, and a dispatch grant with an earlier history are `authority.dispatch`. A dispatcher's
assignment is `assignment.dispatch`, and counts for nothing, unless, at its time, a valid dispatch grant is in force, its work order
is a step of a goal the bound names, its role is no reserved action, the day's cap is not passed, and it goes to a principal the
goal's roster names for the step's role, as `goal step` routes it, never the dispatcher itself or a person; a reassignment also takes
over no step a person holds, needs `reassign`, and goes neither to the holder it replaces nor in a role the roster gives a person.
Nothing else gives a dispatcher authority to assign. The roster, whether a principal is a person, the step's role (from the work
order's revision the assignment pins) and the dispatcher's own record are read as they stood at the assignment's time. A dispatcher
never works a step: an attempt, progress claim or result it writes is `dispatch.work` and counts for nothing. Backdating fails
closed: a principal holding a valid dispatch grant is a dispatcher for every record it writes, at any date, so an assignment or work
it dates before its grant counts for nothing too; and a dispatch grant refused only for an earlier history, whether that history was
written before the grant or dated before it afterwards by another principal, gives no authority to assign yet confines what the
principal writes from the grant's date on. What a principal with such a history wrote dated before the grant keeps its reading, and
a record it dates back into an earlier grant's term is read as any principal's record dated into an expired grant's term. No field or
type is added; an older reader reports the bound as errors and a dispatcher's assignment as `assignment.author`.

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

### Stops (0.9.2)

PROC-06 adds one optional field within format 3, and no record type. An operator
request without `flowStep` behaves as before.

| Record | Additive contract |
| --- | --- |
| `operator-request` | Optional `flowStep: {goal, key, instance?}` names the reserved step of a run the request waits on: the run's goal by its first revision, a step of its flow marked `reserved`, and for a step that runs over a list one of the run's instances. Its subject is the run's work order, its content exactly one item of kind `command`, and it carries no ask over. A revision keeps its `flowStep`. |

Validation refuses a `flowStep` that names a goal that is not a run, or the goal
by a later revision; a step the flow does not declare or does not mark reserved;
an instance a step that runs once does not take, or one the run does not name;
a subject other than the run's work order; content other than one command; and a
carried ask. Whether such a request is due, carrying exactly the command the
records give now while its step waits on the operator, is derived when it is
read, never stored; the request grants nothing, and its close never completes
the step. A flow's reserved step declares its `action` and `done` predicates,
template content; see [Templates](templates.md#flows-092). `records describe
operator-request` includes this contract, within the generic record reader
`vericairn.records/readers/8`.

### Closing a run (0.9.4)

FIX-143 adds one status and one optional field to `goal-status` within format 3,
and no record type. A goal without a closing reads as before.

| Record | Additive contract |
| --- | --- |
| `goal-status` | Status `closed` closes a run of a flow that will never complete. Its `unsettled`, required with `closed` and refused with any other status, names each step instance of the run that was neither done nor off when it was closed, once, as `{key, instance?, reason}`, `reason` being the closer's, at most 2,000 characters. |

Validation refuses `closed` for a goal that is not a run, or in an earlier
format; a closing recorded by anyone but who decides for the goal, a person (its
sponsor when a person, an operator when an agent or a service sponsors it), so an
agent's closing is refused and counts for nothing; a step the run's flow does not
declare, an instance a step that runs once does not take or one the run does not
name, a step named twice and a blank reason; a closing as the goal's first status;
and any status after it but `impossible` or `abandoned`. Which steps were unsettled
is the runtime's to check when it writes the closing; reading derives the rest: a
closed run's steps that are neither done nor off read `unsettled` with their
reasons, never `done`, and the goal may still be accepted. `goal close` and
`goal accept --reason` write it ([operator guide](operator-guide.md#closing-a-run-094)).

### Decisions of a run's steps (0.9.4)

PROC-19 adds one optional field to `result` and one observation kind within
format 3, and no record type. A result without `decision` and every other
observation behave as before, and a flow's decision transition is template
content of kind `flow` ([Templates](templates.md#decision-transitions-094)),
not a record.

| Record | Additive contract |
| --- | --- |
| `result` | Optional `decision: {paths, reasons, evidence}` records the decision of a run's decision step by the principal holding its current assignment: `paths`, the path or paths of the step's transition it takes; `reasons`, never blank; and `evidence`, each `{type, id, hash}` of an exact record, possibly none. It is the step's item of kind decision, distinct from the estate's `decision` records, and it performs nothing reserved. |
| `observation` | Kind `flow-decision` records a rule's decision of a run's decision step, or the default its missed deadline takes, as the runtime evaluated it: its subject is the goal's exact revision, its author a principal who takes the run's steps, `asOf` the round's start, when the rule read the records, and its `facts` are `step`, `paths` (the one path taken), `rule` (the matching rule's number from 1, `default`, `deadline`, or `none` with no path, recording under `rule-then-role` that no rule held, so the role decides the round) and `read` (each record the rule read as its type, id and hash, separated by spaces). |

Validation refuses a `decision` on a result that is not completed; on a result
whose attempt is not on an assignment of a decision step of a run's flow, or is
on a review assignment; for a step whose transition its role does not decide in
the run's configuration; naming a path the transition does not declare, a path
twice, or several paths where the flow takes one or where one of them goes back,
ends the run or hands it to another flow; by an agent where the decision is a
person's, because the flow requires a person, the decision is one the
operating-modes design never delegates or the flow marks the step's role a
person's; on an assignment a later assignment of the step displaced before it;
after an earlier decision of the same assignment that still stands in its round,
not sent back by a review and not a hold that took effect; and evidence that is
not an estate record at its recorded hash. It reports a decision whose paths
would turn off a step an estate policy requires or a required reserved step
(`result.decision-protected`); such a step stays on. Under `rule-then-role` it
also refuses a decision under an assignment made after a round began and before
that round's rule decision was recorded, since a round in which a rule held is
the rule's, and one with no `flow-decision` observation recording that no rule
held at or before it. Validation refuses a `flow-decision` observation whose goal is not a
run, whose author does not take the run's steps (for `none`, nor holds the step's
assignment), whose step's transition a rule
does not decide in the run's configuration, whose `default` is recorded where a
rule alone does not decide (under `rule-then-role` the role decides when no rule
holds), whose `none` is recorded where the step is not decided by a rule then
its role or names a path, whose `deadline` is recorded where a rule decides, the transition takes
no default at a deadline, the run's timing sets no deadline or the decision is a
person's, whose path is not the one its rule names, whose read records are not
estate records at their hashes, whose `asOf` is later than it is recorded, or
for a round whose rule decision is already recorded. Its rules are evaluated
again as of `asOf` over only the records it read, each at its hash, with the
as-of evaluation the position uses: validation refuses one whose rule does not
hold there or after an earlier rule that holds, and a `default` or `none` when
any rule holds. A predicate over a derived state is left out of that
evaluation, which stays fixed; the write boundary refuses a new one whose rules
do not hold with the states they name read when it is written
(`observation.flow-decision-state`), or whose `read` leaves out a record its
rules count as of `asOf` (`observation.flow-decision-read`), both read as the
records stood when it was recorded, so a later record or state change never
invalidates it or blocks an unrelated write. On one already in the estate,
written around the runtime, either is information, and the run's position
ignores that record. A result under `rule-then-role` is refused unless the
first kind recorded for the latest round start named at or before it, one the
position counts, records that no rule held, and no hold or path back of the step
taken since opened another round. It reports a
deadline's default naming a deadline by which a decision of the step's current
assignment stood, and a rule's decision or deadline's default whose path would
turn off a step an estate policy requires (`observation.flow-decision-protected`);
such a step stays on. Whether a deadline had passed, and when a round began, are
the run's position's to derive: it counts a deadline's default only when it is
recorded at or after the deadline it derives and names it, and a rule's decision
only when its `asOf` is its round's start. An assignment of a decision step is
refused where a rule decides it in the run, and an agent's where the decision is
a person's. Which decision counts, its review before it takes effect and a path
back past its limit are derived when the run is read, never stored. A reader of
format 3 that predates the field refuses a result that carries it as an unknown
key, as any later format-3 field; a reader of it that cannot resolve the run's
flow shows the result and reads the step undecided. `records describe result`
includes this contract, and the generic record reader advances to
`vericairn.records/readers/9`.

## Concurrent assignments compared by their permitted paths (0.9.4)

CODE-73 adds three optional fields to an assignment's `brief` and one optional
field to `result` within format 3, and no record type. An assignment or result
without them behaves as before: none is refused or warned for lacking one, and
the write-boundary rules below apply only to new records.

| Record | Additive contract |
| --- | --- |
| `assignment` | `brief.repository`: the identity of the repository the work changes, the estate's own (its first charter's id) or a repository resource's (its first record's id). `brief.excludedChanges`: patterns in the grammar of `permittedChanges` that the work leaves out, each an interpretable pattern, never prose, since prose excludes nothing. `brief.after`: at most 20 assignment ids whose work this one waits for; each is followed to its current revision, and validation refuses one that names the assignment itself, names it twice or makes a cycle (`assignment.after`), and an exclusion that is prose (`assignment.excluded`) or a repository that is no such identity (`assignment.repository`). |
| `result` | Optional `change: {repository, base, head, target}`: the repository's identity as above, the base and head commits (40 lower-case hex digits) and the branch the change integrates into, a plain branch name (`result.change`). The runtime derives the change's paths from the registered clone, by name without rename detection, so a rename is a removal and an addition; a clone missing either commit makes them unavailable, never empty. The change releases its assignment only as that assignment's work: in the repository its brief names (for an older brief, the one its runs named), merged by a `change-merge` operation at that head or reachable from the main line of the registered clone, never from the branch named as `target`, and, where the clone shows them, with a base that is a proper ancestor of its head and touching one of the assignment's effective permitted paths; a reachable head needs the clone to show both, and a merge the records show stands where no clone shows otherwise. Any other change keeps the assignment active. An assignment whose brief holds no path pattern is released when its result is completed, whatever change it names. |

From 0.9.4 the runtime's write boundary refuses a new format-3 assignment whose
permitted changes hold a path pattern and whose brief names no repository
(`assignment.repository-required`); `work assign`, `goal step`, routing and
flows fill it from the run's repository, the one named with `--repository`,
or, when the estate names no repository resource, the estate's own. It refuses
a revision that changes an assignment's `repository`, `permittedChanges`,
`excludedChanges` or `after` while a run of it is in flight
(`assignment.narrowing-in-flight`), and a new attempt on an assignment while one
it names in `after` is not released (`attempt.after`). Narrowing by exclusion or
`after` grants less, so it needs no amendment. A new completed result whose
assignment holds a path pattern and that names no `change`, or a `change` in
another repository, with the same commit as base and head, or for an older
assignment that names no repository, is written with a warning in the write's
notices, since its assignment then stays active until acceptance; a result the
check counts as released at completion, recorded before the check's first
record, is not warned.

The overlap check reads these fields; status and progress show its open
overlaps from the records alone, and `vericairn work overlap` adds what this
machine's registered clones show ([CLI](cli.md#vericairn-work-overlap-094)). A
reader of format 3 that predates the fields refuses an assignment or a result
that carries one as an unknown key, as any later format-3 field, so the estate's
pin keeps every machine on a release that reads them; an older reader keeps
reading every record without them. A format-2 estate refuses them, as every
format-3 field. `records describe assignment` and `records describe result`
include this contract, and the generic record reader advances to
`vericairn.records/readers/10`.

POLICY-08 adds a rule kind and a policy-exception target within format 3, and no
field or record type. An `estate-policy` rule of the foundation kind
`vericairn.estate/concurrent-paths`, version 1, with empty `parameters` and a
`repository` selection, holds the unresolved pairs of active assignments whose
effective permitted paths meet within its selectors
([policies](policies.md#concurrent-paths-094)); a runtime without the kind reports
the rule unavailable. A `policy-exception` target of kind `pair` carries
`assignments`, the exact references of two revisions of two different
assignments, and no `submission`; a concurrent-paths rule takes only pair targets
and only such a rule takes one (`exception.pair`). A reader of format 3 that
predates the target refuses an exception carrying it (`body.enum`), so readers
upgrade before an estate writes one; every other record keeps its reading.

## What a worker run can reach (0.9.4)

CODE-82 adds one optional field to `harness-qualification` and one to an
`agent-run` operation's `execution` within format 3, and no record type. A
record without them is unchanged, and none is refused or warned for lacking one.

| Record | Additive contract |
| --- | --- |
| `harness-qualification` | `reach: {file, host}`: what a probe in the harness's place reached during qualification. `file`, a marker planted outside the probe workspace and its temporary directory, is `readable`, `blocked` or `unknown`; `host`, a test host on the loopback interface, is `reachable`, `blocked` or `unknown`. It is no admission identity, so a revocation may omit it. |
| `operation` (agent-run) | `execution.reach: {outsideFile, testHost, secretPaths, secretKinds, secretPathsComplete}`, fixed at intent with the rest of `execution`: the same measurement made before the run started, the count of secret-bearing paths its operating-system user could open for reading (at most 512), their kinds (`ssh-key`, `cloud-credentials`, `registry-credentials`, `forge-credentials`, `signing-key`, `harness-login`, `device-key`, `environment-file`, `keychain`; each named once and counted at least once) and whether that inventory was complete. The paths themselves stay in the run's custody on the machine that ran it, never in a record. |

`unknown` means the probe did not run or report, never that it was blocked. A
reader of format 3 that predates the fields refuses a record that carries one as
an unknown key, as any later format-3 field, so the estate's pin keeps every
machine on a release that reads them; a format-2 estate refuses them. `records
describe harness-qualification` and `records describe operation` include these
contracts.

## Public retrieval for research runs (0.9.4)

RUN-04 adds one optional field to an assignment's `brief`, one to an `agent-run`
operation's `execution` and one top-level field to that operation within format
3, and no record type. A record without them is unchanged.

| Record | Additive contract |
| --- | --- |
| `assignment` | `brief.retrieval: "public"`: the work's runs may retrieve public sources with their harness's web tools, never with credentials. It is the only value, and nothing else permits retrieval. |
| `operation` (agent-run) | `execution.retrieval: "public"`, fixed at intent: the run's web tools were admitted. Validation refuses it unless the brief of the assignment revision the operation names permits retrieval (`operation.retrieval`), and only with it may an inventory component name the route `public-web` (`operation.execution`). |
| `operation` (agent-run) | `sourcesObserved: {sources, count, complete, digest}`, recorded at settlement only, on an `observed-success` or `known-failure` revision of a run whose execution admitted retrieval, and kept by every later revision (`operation.sources-observed`): each source, in the order invoked, with its `tool`, exactly one of `address` (absolute http or https, no user information), `query` or `withheld: true`, `at`, `retained`, and its content's `hash` and `bytes` when received (a retained source names both); at most 100 in full, `count` of every source, `complete` (false when the run's log was missing, unreadable or could not be written) and `digest`, SHA-256 of the canonical JSON array of every entry. The retrieved text stays in the run's custody, never in a record. |

A reader of format 3 that predates these fields refuses a record that carries
one as an unknown key, as any later format-3 field, so the estate's pin keeps
every machine on a release that reads them; a format-2 estate refuses them.
`records describe assignment` and `records describe operation` include these
contracts.

## Standing agents (0.9.4)

GOAL-07 adds a goal's `plan.standing` and an assignment's `occurrence` within
format 3, and no record type: a standing agent is a stewardship goal, which
already carries its sponsor, budget, lifecycle and ledger. A goal or assignment
without them behaves as before.

| Record | Additive contract |
| --- | --- |
| `goal` | Optional `plan.standing: {pin, subject, triggers, period, until, runsOn?}`, on a stewardship goal only and never beside `plan.flow`, with a `given` plan of exactly one ordinary work order (`standing.kind`, `standing.flow`, `standing.work`). `pin` is the exact `{id, version, hash}` of a flow that declares the trigger kinds the plan uses and exactly one routed step, of kind work (`standing.flow`, `standing.trigger`); `subject` binds the flow's subject as a run's does (`standing.subject`); each trigger has its own slug `key` and a `kind`: `schedule {unit: days|months, every, firstAt}`, `duty {policy, rule}`, `event {recordType, fields?, roles?}`, `condition {record}` with a record predicate, or `request` (`standing.trigger`); `period {unit: days|months, every?, runs, wallMinutes, tokens?, currency?}` caps the units the budget caps, which are then each run's caps (`standing.period`); `until` is after the goal is recorded (`standing.until`); optional `awaitingReview` (1 to 100, three when absent) is how many results may await review before dispatch defers; `runsOn` is a runtime identity's public-key hash. A flow's `triggers` declare kinds from the same five. |
| `assignment` | Optional `occurrence: {key, trigger, revision, item}` on, and only on, an assignment of a standing agent's work order (`assignment.occurrence`). `revision` is the goal revision the trigger's declaration originates from; `item` is `{interval, dueAt}`, `{record: {type, id, hash}}`, `{failing, records}`, `{at, by}` or the duty's `{occurrence, policy, rule, dueAt, deadline, schedule}`; `key` is the goal's identity, the trigger, the revision and the item joined by `/`, or `duty/` and the duty occurrence's identity. A revision keeps its assignment's occurrence exactly, and a person's hand-over carries it exactly. A new occurrence is refused unless its agent is active and before its `until` at the assignment's time, and its item is one its trigger makes then: a schedule's latest due interval with its `dueAt`; an event's record of the trigger's type and filters, at its exact hash, recorded after the trigger was activated and before the assignment; a request by the assignment's author, no later than it; a duty occurrence's exact identity from the trigger's policy at its originating revision, its rule, its subject's identity and the duty's own schedule item (`{kind, number}`, `{kind, at}` or `{kind, publication}`, built from the policy's schedule, which the item's `schedule` equals exactly in canonical form; for a release schedule, the first published revision of a release of the schedule's product, published within the schedule's window, as the duty's evaluation opens one), with the schedule's due time, due by then, and its deadline. Any other current assignment naming the same key refuses it (`standing.duplicate`, a reconcile task) unless one is a person's hand-over of the other's step or holds nothing (an earlier revision, withdrawn by a reconciliation, or stored without the record it was routed with). |

The runtime's write boundary also refuses a new assignment, with
`assignment.occurrence-now`, whose event record is the agent's own or an agent's
the trigger does not admit, or whose condition does not fail over the records its
item names, read as the records stand; assignments already in the estate keep
their reading. A `reconciliation` whose subject is an assignment naming an
occurrence, keeping and withdrawing assignments of that occurrence (the latest
revision of each, none a revision of the subject), settles `standing.duplicate`,
recorded by the operator or under `local-engineering`, only while two or more of
them hold the key (recorded before it, withdrawn by no earlier reconciliation and
not stranded) and the one kept is one of those (`reconciliation.occurrence`). An
occurrence's settlement is its key's: the latest result on any assignment of the
key, a withdrawn one included, settles it and counts toward `awaitingReview`.

`goal-status` with status `active` is refused for a standing agent past its
`until`, or while another active standing agent of the same flow or work role
carries the same subject (`goal-status.standing`). An `agent-run` operation whose
goal is a standing agent is refused, judged by the records before it, past
`until`, while its consecutive failed runs reach the goal's
`limits.consecutiveFailures` (three when unset; counted as below) and no
answered ask of the pause lets it go on, past the period's
`runs`, for an assignment naming no occurrence or a settled one, while results
awaiting review reach `awaitingReview` (`operation.standing`, `operation.budget`),
or with a cap above the budget's per-run cap; the ledger of a standing agent
charges each period's runs against the period's caps.

The consecutive failed runs count each settled `agent-run` of the goal once, in
the order the runs settled (each at the later of its intent and its first
settling revision no tied agent wrote, first by the number of revisions no tied
agent wrote above it on its own `supersedes` chain, then by `at` and identifier;
or, without one, its first settling revision). A run is read
from its lineage: every `operation` whose `supersedes` chain reaches the run's
intent, on every branch, whatever divergent successors, the order of identifiers
or a `reconciliation` make current. Where a revision no agent tied to the goal (as
the `ask` row of the standing outputs below reads ties) wrote settles the run or
cancels it, the run's untied heads decide it and the revisions tied agents wrote
count for nothing. From 0.9.4 (FIX-156) an untied head is a revision no tied
agent wrote that no other such revision follows on its own `supersedes` chain,
directly or through revisions tied agents wrote. Each head reads as the run
would by it alone: a success, a failure, or nothing when it leaves the run
canceled or unsettled. On one line, as the worker and `records new` write every
revision on the run's latest, the head is its latest untied revision, so an
untied principal's correction written on the settlement it corrects counts as
written, however many revisions a tied agent put between them. Where heads on
different branches read differently, the reading least favorable to the agent
decides: a failure over nothing, nothing over a success. Neither the number of
`supersedes` steps nor `at` ranks one untied revision over another, so a branch a
tied agent padded with revisions of its own ranks nothing. Only where no such revision settles
or cancels the run does a `known-failure` revision a tied agent wrote count, as a
failure. A head reads as a success when it is `observed-success` with
`observed.reference` naming a `completed` or `partial` result on an attempt of
its assignment, recorded after its intent, and no tied agent wrote its intent;
otherwise as a failure when a `result` with outcome `blocked` or `abandoned` was
recorded on an attempt on its assignment, any revision of either, after its intent
and before the intent of the assignment's next run, whatever a later result or a
correction says, or when it is `known-failure`, unless its stop
was requested by the goal's sponsor, when a person, or an operator, as they stood
when the stop was recorded, in a revision no tied agent wrote; any other
principal's stop and the worker's for a cap count. Any other run counts for
nothing, one that exited cleanly with no result included. Records the estate
leaves unread because their line holds an identifier more than one record carries
(see [Identifiers more than one record carries](#identifiers-more-than-one-record-carries-094))
count against the agent: a line of the goal's `agent-run` operations that is not
read counts as one failed run for each intent it holds, at least one, at the
latest `at` among its records; a run that an unread record names, by its
assignment, an attempt or result on it or a record of its lineage, counts as its
own records decide it when they make it a success or a failure, and otherwise as
failed; and while any record is unread, a record that does not count, whose
device, admission, principal or grant may be the unread one, is read as a tied
agent's record: an intent, a revision, an attempt or a blocked or abandoned
result counts toward a failure, and none makes a success, cancels, corrects or
stops a run. From 0.9.4 the worker
names in `observed.reference` of an observed success the latest `completed` or
`partial` result its agent recorded during the run, on an attempt recorded during
the run, as it sees them at the run's exit, and names none otherwise; a reader
that predates this reads it as the free text the field already was. A `completed`
or `partial` result never makes a run succeed by itself: only the settlement names
it, so no result written after the settlement, whatever its `at`, does. A later
run whose intent a tied agent wrote never ends the window of a run no tied agent
wrote. From 0.9.4 (FIX-155) the failures end in two ways only: a success ends the
failures before it while they are fewer than the limit, and once they reach it
only an answer of the pause's ask ends them, an `ask` goal step recorded naming one
of the failures, answered by a decision the `ask` row counts, after which no run
settled before the earlier of the ask and that decision counts. So no attempt,
result, run revision, divergent revision, reconciliation or decision a tied agent
writes ends the pause or turns a success an untied settlement names into a
failure, whatever its `at`, and no record carrying another's identifier does,
whatever its `at` or path.

A reader of format 3 that predates these fields refuses an assignment that
carries `occurrence` as an unknown key and refuses a goal whose plan carries
`standing` as an unknown key of `plan`, as any later format-3 field, so the
estate's pin keeps every machine on a release that reads them: such a reader
shows neither record and dispatches nothing of the agent. A reader that reads
them but cannot resolve the flow shows the goal with a warning
(`standing.unavailable`) and dispatches none of its occurrences. A format-2
estate refuses them. `records describe goal` and `records describe assignment`
include this contract, and the generic record reader advances to
`vericairn.records/readers/11`, which carries the fields of this section and the
two before it.

## Standing outputs (0.9.4)

GOAL-08 adds fields to `possibility`, `ask` and `operator-request`, and a goal's
`plan.standing.outputs`, within format 3, and no record type: every output of a
standing agent's occurrence is a record another principal disposes of. A record
without them behaves as before.

| Record | Additive contract |
| --- | --- |
| `possibility` | Optional `raisedBy: {goal, occurrence, assignment, run?}` names a standing agent's goal by its first revision, the occurrence key, the assignment of the goal's work order that carries that key, recorded before it, whose contributor is the record's author, and, for an agent, the agent run of that assignment by the author, in flight when the record is written; a result that already settles the occurrence refuses it (`standing.raised-by`). Such a possibility is raised `open` with a `fingerprint` and at least one source (`standing.possibility`); its occurrence records at most `plan.standing.outputs.possibilities` (five when unset) possibilities, of which at most `drafts` (three) carry a `draft`, and one summary possibility, the one with `extras`, read in the goal revision in force when the possibility was first raised (the latest activated before it) and counting each other possibility of the occurrence at its revision as it stood, a revision counting for the draft or extras it adds (`standing.output-cap`); a second open possibility of the same agent with the same fingerprint, read as each stood when it is written, is refused (`standing.duplicate-output`); and where one with that fingerprint was declined, a new one names it in `raisesAgain` with at least one `evidence` item (`standing.raises-again`). A revision keeps exactly the `raisedBy` and `fingerprint` its chain's first revision names, and a revision of a possibility no standing agent raised names no `raisedBy` (`standing.raised-by`); the rules apply by the first revision's `raisedBy`, whatever the revision's body says. One that changes the disposition or `adoptedAs` is refused by the possibility's author and by an agent tied to the goal, as the `ask` row reads ties (`standing.own-output`; `standing.output-now` where only records written after it tie the agent), and from 0.9.4 (FIX-155) such a revision disposes of nothing whatever its `at`: the possibility reads as the last revision of its chain by another principal set its disposition, open until one does, counted among the open outputs and adopted only while it reads open. On any possibility, a revision of one whose revision before it carries `disclosure: held` that changes its disposition or `adoptedAs` is refused for an agent, adopting it takes an operator who is a person, and so does a revision that drops `disclosure` (`standing.held`). On any possibility, at any format, a revision that adopts it, recorded by the author of its first revision, is refused (`standing.own-output`) when its chain carries a field of this section; otherwise the write boundary refuses it (`standing.output-now`), and an adoption already in the estate keeps its reading. Optional on any possibility: `fingerprint` (lower-case letters, digits and `. : / _ -`, up to 200 characters); `sources`, each `{address, retrievedAt?, hash?, extents?}`; `evidence`, each `{stance: for|against, statement, sources?}`, the sources by index, with no scores (`possibility.evidence`); `reviewTriggers`, each `{at}` or `{condition}` (`possibility.review-trigger`); `links`, each `{relation: duplicate-of|supersedes, possibility, hash}` naming another possibility at its exact hash (`possibility.link`); `draft: {key?, title, intent, scope, deliverables, exitQuestion}`; `disclosure: held`; `extras`; `raisesAgain`. |
| `ask` | Optional `raisedBy`, as a possibility's, kept by every revision as its first revision names it (`standing.raised-by`). Such an ask is put to a person other than its author, names a revision of the agent's goal in `goal`, and does not open with the words of the runtime's own asks about a goal (`standing.ask`); no revision withdraws it, with `raisedBy` or without (`standing.own-output`): a decision of the person asked disposes of it, and from FIX-155 a withdrawal reads as the state its chain recorded before it, so a decision of the person asked still answers it (`decision.answers` refuses an answer only to an ask that reads withdrawn). A `decision` whose `answers` names such an ask, or one of the runtime's own asks about a standing agent's goal below, answers nothing when its author raised the ask or is an agent tied to the goal, whatever its `decidedBy` names, so the ask stays waiting, counted among the open outputs, and a failure pause holds. An agent is tied when any `assignment` of the work order of any revision of the goal names it as `contributor`, or any revision of the goal's `roles`, or of the `roster` where the goal's roles do not name its work step's role, has named it for that role; ties are read from the records whatever their `at`, and only grow. Such a decision is an error (`standing.own-output`) when its author raised the ask or a record tying it was written before the later of the decision and the ask revision it names, and otherwise reported (`standing.output-now`). The author of one of the runtime's asks is not refused for being its author: a principal nothing ties to the goal, such as the builder whose goal step wrote the ask, records the person's decision on it. |
| `operator-request` | Optional `raisedBy`, as a possibility's, kept by every revision as its first revision names it (`standing.raised-by`). Such a request, and each revision of it, is of kind `publication`, pins the agent's work order and is put to an operator other than its author (`standing.publication`): its exact text, labeled the agent's draft. An `operator-request-close` or an `operator-request` observation of it is recorded by an operator who is a person, never by its author or `askedBy` (`standing.own-output`) and never by an agent, whatever `operatorWords` it cites (`standing.publication`); from FIX-155 one by anyone else closes nothing. |
| `goal` | Optional `plan.standing.outputs: {possibilities?, drafts?, open?}`: possibilities per occurrence (1 to 20, five when unset), drafts among them (no more than the possibilities, three when unset; `standing.outputs`) and open outputs (1 to 1,000, ten when unset). |

A standing agent's open outputs are its possibilities still open, its asks still
waiting and its publication requests not closed. While they reach `open`, its due
occurrences are deferred, and an `agent-run` operation of the goal is refused,
judged by the records before it (`operation.standing`). The runtime's write
boundary also refuses, with `standing.output-now`, a new disposition of a
standing agent's possibility, or a new decision answering a standing agent's ask
or one of the runtime's asks about its goal, by an agent tied to the goal only by
records written after it. Because ties only grow, a later roster or
assignment change makes no record already in the estate an error and is never
refused for one; it can make an answered ask wait again for the person, as a
decision by an agent it ties answers nothing from then on, and it never makes a
waiting ask answered. The runtime takes
an ask for its own renewal, period-cap or failure-pause ask only when its first
revision names no `raisedBy`, it is put to a person and its author took the
goal's steps as the records stood when it was written (an operator, the sponsor
when a person, a holder of `local-engineering` or a dispatcher whose grant names
the goal); another principal's ask in the same words holds back none of them.
goal step writes and signs these asks as its caller, so an agent that carries the
occurrences and may itself take the goal's steps can write one in their words, as
it could by calling goal step; its own answer to one answers nothing.

A reader of format 3 that predates these fields refuses a record that carries
one of them as an unknown key, as any later format-3 field, so the estate's pin
keeps every machine on a release that reads them. A format-2 estate refuses them.
`records describe possibility`, `records describe ask` and `records describe
operator-request` include this contract.

## Events and presence (0.9.4)

ID-11 and GOAL-11 add fields within format 3 and no record type; each is optional:

- `goal` and `roster`: `limits.watch.reassignAfterSeconds`, 60 to 86,400, how long a lapsed session holding a step stays lapsed
  before the runtime asks about reassignment (600 when neither sets it).
- `communication-setting`, person scope only: `values.availability` (`state` `available`, or `away` with `until` and an optional
  earlier `from`) and `values.quietHours` (`zone`, an IANA time zone; `windows`, 1 to 14 of `days`, `start` and `end` as `HH:MM`;
  `breakthrough`, attention categories).
- `assignment`: `routedWith` `session` (routing gave the step to a live session; stored alone, it holds its step at once) and
  `basis`, the lapse ask a hand-over answers, recorded by a person and about an assignment of the same step.

A format-2 record cannot carry them. A reader of format 3 that predates them refuses each as an unknown key or value, as any later
format-3 field, so the estate's pin keeps every machine on a release that reads them. The generic record reader advances to
`vericairn.records/readers/12`, which carries `basis` and the session value. See [events and presence](events-and-presence.md).

## Answering asks in one act (0.9.4)

OPS-16 adds one optional field to `decision` within format 3, and no record type. A decision without `act` behaves as before.

| Type | Field |
| --- | --- |
| `decision` | Optional `act: {id, ask, hash, recommendation, count}` names the operator's act that answered this ask with its recommendation: `id`, the act's ULID, which each of its decisions names; `ask` and `hash`, the exact ask revision `answers` names; `recommendation`, the SHA-256 of that revision's recommendation as UTF-8; and `count`, 1 to 256, how many asks the act set out to answer: those it showed and the operator chose before its first signature. An act that stops part-way, as a phone's may, writes fewer decisions than its count, never more. `decidedAt` is the act's time. |

Validation refuses an `act` whose `answers` is not exactly its ask, whose hash is not that revision's or whose recommendation
hash is not that revision's recommendation's; one recorded by anyone but its `decidedBy`, or whose decider is not a person; an
unsigned one; one carrying `answer`, `changeSet`, `documents`, `obligations`, `policies`, `releaseReview` or `promptUse`; decisions
of one act that differ in `decidedBy`, `decidedAt` or `count`, answer one ask twice or number more than `count`; and `act` in an
earlier format. That the ask revision is still the current one and still waits on its operator is the runtime's to check when it
writes the decision. A reader before 0.9.4 refuses a decision carrying `act`, since a decision admits no other member, so an estate
records an act only after every writer upgrades.

## Optional requests (0.9.4)

OPS-22 adds one optional field to `operator-request` within format 3, and no record type. A request without `optional` behaves as
before.

| Type | Field |
| --- | --- |
| `operator-request` | Optional `optional: true` marks the request a possible todo: shown in its own section, never holding a release's closing or a goal's evaluation, closed, done or dismissed, whenever the operator chooses. Its requester may create it optional; a required request becomes optional only by a revision the operator it is put to, a person, writes, changing nothing but the mark. |

Validation refuses `optional: true` on a run's step request (`flowStep`), whose step says whether it is required, and a value that
is not a boolean; a revision by the requester that turns a required request optional; and a revision by the operator it is put to
that changes anything but the mark, or by an operator who is not a person. Reading counts a turn to optional only when that operator
wrote it, so a refused mark holds nothing back; a revision back to required always counts. That every file a request pins is held
by the estate's objects is the runtime's to check when it writes the request. A reader before 0.9.4 refuses a request carrying
`optional`, since a request admits no other member, so an estate marks requests optional only after every writer upgrades. See
[operator requests](operator-requests.md#optional-requests-possible-todos-094).

## Identifiers more than one record carries (0.9.4)

A record's identifier is its own: the write boundary refuses a record whose
identifier the estate already holds, so two record files carry one identifier only
when one was written by hand or a copy was left behind. Before 0.9.4 the loader
read the first of them in path order, so a record dated in an earlier month, whose
path sorts first, was read in place of the record it copied. From 0.9.4 (FIX-156)
such an identifier is unread, whatever the records' `at`, paths or contents, and so
is every record of its line: every record joined to one of those files through
`supersedes`, either way, including the record each copy names as the one it
supersedes. None of them enters the estate as read. No chain, lineage, lookup or
supersession reads any of them, so no copy is read in another's place, no earlier
revision of the line reads as its latest, and no later one reads as a line of its
own. Each copy is reported (`record.duplicate-id`, an error and a reconcile task)
and each other record of its line noted (`record.duplicate-line`, a warning), so
the estate refuses every write while they stand; removing all but one of the
records carrying the identifier makes the line read again. An estate with no such
identifier reads exactly as before.

From 0.9.4 (FIX-159) leaving a record unread never makes another count. A record
can refuse others when read: a charter's signature requirement refuses unsigned
records and its operator roots every admission; a revocation ends what its device
signs; a history checkpoint's closing refuses what its device, key, grant or person
signs and it does not witness; an adoption refuses unsigned records no anchoring
checkpoint lists; a witnessed listing refuses other bytes under its identifier.
Left out, such a record refuses nothing. So while records are unread the loader
judges counting again with the unread records read each way they can be: each
identifier more than one file carries read as one of the records carrying it (copies
with the same content and signatures once) or as none of them, with the rest of its
line. A record counts only when it counts in the reading that leaves the unread
records out and in every such reading. One of those readings is the estate as it
was before the copies were written, so whatever it refused stays refused, by every
reading, check and write; and since the reading that leaves them out is among them,
a copy can only take counting away. That reading is the earlier estate exactly only
for the files that share an identifier: a new file with an identifier of its own
that joins an unread line, as one superseding a record of it does, is in every
reading, and it gains nothing its writer could not gain by writing it without a
duplicate. The records that count for nothing only for
this reason are reported once (`record.duplicate-refused`, a warning). Past 16
readings which records a reading would refuse is not established, and no record
counts (`record.duplicate-unsettled`, an error) until all but one record carrying
each identifier is removed. Only counting is judged this way; in every other respect
the records read keep the reading that leaves the unread records out.

What holds while records are unread:

- no record counts that a reading of the unread records refuses, so an unsigned
  record in the operator's name stays refused when a copy of the charter's or its
  revision's identifier leaves the requirement unread, and a record a closed
  device signed stays refused when a copy of the closing's identifier, or two new
  files superseding it, leaves the closing unread;
- a record left unread can make others stop counting: the device, admission,
  principal or grant a record's signature or authority rests on may be the unread
  one, and a copy of a device's identifier, or two new files superseding it, leaves
  every record that device signed counting for nothing; with the charter left
  unread no admission counts, so no signed record does;
- an answer an unread record carried answers nothing, so its ask stays waiting, and
  a disposition of a standing agent's output it carried disposes of nothing;
- a standing agent's failures count an unread line of the goal's agent runs as
  failed, and a run an unread record names as its own records decide it or as
  failed (above); a standing agent's output whose line is unread stays among its
  open outputs, one for each first revision the line holds, and is adopted by
  nobody; an output or an ask of a standing agent whose goal is unread is disposed
  of and answered by nobody; an unread assignment of the goal's work, or revision
  of a roster, ties the agents it names to the goal, as if it were read;
- a record that does not count is read against standing agents as a tied agent's
  record is, and never for them: its blocked or abandoned results, intents and
  revisions count toward failures, its outputs stay open until a revision that
  counts disposes of them, its assignments and roster revisions tie, and it makes
  no success, cancellation, correction, stop, answer or disposition;
- every standing agent reads `held` while the estate leaves any record unread,
  whatever its status reads, unless it is stopped or not dispatched here: what its
  records rest on reaches, through their devices, admissions, principals and grants,
  records anywhere in the estate. `goal due` names the unread record (one that names
  a record of the agent when there is one), and no run of the agent starts: goal
  step dispatches nothing of it and an agent run's intent is refused for the hold
  (`operation.standing`), whatever its failures read. A goal whose own line is
  unread is not shown as missing: `goal due` says its state, failures and open
  outputs are not established;
- nothing acts: release submission, every record a device signs on the runtime's
  machine or that is prepared for a device elsewhere to sign, a pairing code, run
  admission, preparation and launch, a goal's step by any route and a repository
  operation (a checked push, opening or merge, or its observation) refuse before
  they act, naming the finding, and so does every MCP tool and daemon route that
  reaches them. While the estate reports any other error, release submission, run
  admission and launch, a goal's step and a repository operation refuse the same
  way, and a record is judged by the write boundary with the estate as it would be with
  the record, as before, and stored only when it clears every error, so that an
  estate can still be repaired by its records. Stopping a run is never refused.

Outside counting an unread line reads as absent, as if its files had been removed:
no reading takes any of its records, or any copy's content, in another's place. What
an unread record would restrict other than by refusing to count a record, such as a
goal status, a review or a reconciliation's withdrawal, restricts nothing in the
readings while it is unread, and a reading outside a standing agent's that counts
records against a principal, such as a flow goal's ledger of runs, does not count
it; nothing acts on such a reading while the duplicate stands, since every acting
surface refuses.

This adds no field and no record type within format 3. A reader that predates it
reads the first of the records in path order, as before, and reports the others; a
development build before FIX-159 counts a record a reading of the unread records
refuses, and does not refuse acts while the estate reports an error.

## Security reports (0.9.2)

OPS-11 adds one optional product field and one observation kind within format 3, and no record type, and narrows who may change a
repository resource's kind. A product without
`securityReports` behaves as before, and no migration rewrites a record. What the reader observes about each advisory
(identifier, state, severity and content hash) is machine-local state in the live store, not a record.

| Record | Additive contract |
| --- | --- |
| `product` | Optional `securityReports: {provider, repositories}`. `provider` is a provider id (a lower-case slug; the shipped reader is `github-security-advisories`), and `repositories` lists one to eight distinct `resource` ids, each a `repository` with a `github` location. Declaring, changing or removing it is the operator's alone: any other author's revision that changes it is refused (`product.security-reports-author`). While any current product declares it, release submission reads those repositories and refuses while one cannot be read completely, while a declared resource is no longer a repository on GitHub, or while an advisory holds: a report (in `triage` or `draft` state, or an unknown state) with no triage of its current revision, or a closed or withdrawn advisory no triage names at any revision. A published advisory holds nothing. |
| `resource` | Each revision is judged against the declarations and grants in force at its own time. While a product's `securityReports` names the resource, a revision changing its `kind` or `location` is refused unless the operator records it (`resource.security-reports-author`). A repository's location still moves only by the operator or an `administer` grant (`resource.location-author`). From 0.9.2 the write boundary also refuses a new revision that turns a resource into or out of a repository, or moves a former repository, without that authority, and a new revision a declaration made since would forbid; revisions already in the estate keep their reading, and validation reports them as information under the same codes. Other fields stay open to their usual authors. |
| `observation` | Kind `security-triage`, by the operator only (`security-triage.author`). Its subject pins the exact `repository` resource on GitHub whose advisory it names (`security-triage.subject`), and its `facts` carry exactly `advisory` (a GHSA identifier), `repository` (owner/name, matching the subject's location), `contentHash` (the reader's hash of the provider's response for that advisory), `disposition` (`security`, `duplicate` or `no-action`) and `reason` (at most 500 characters, required for `no-action`). Any other fact is refused (`security-triage.facts`), and `no-action` without a reason is refused (`security-triage.reason`), so an advisory's summary, description, reporter and credits cannot enter it. |

A triage names one revision: it clears a report's hold and its attention notice only while the report's observed content hash
is the one it names, so a later change of its severity or text needs another triage. For a closed or withdrawn advisory, a triage
of any of its revisions ends the hold, and publishing a report ends it without a triage. The kind is named `security-triage`
so that it stays apart from the conversation triage that conversation intake plans. A triage the estate check refuses, or
one by any principal other than the operator, clears nothing. A reader that lacks the kind refuses the record, and a
product's `securityReports` in an earlier format is refused as a later format's field. `records describe product` and
`records describe observation` include these contracts, and `vericairn.records/readers/8` explains the product's new field.

## Split objects (0.9.4)

From 0.9.4 (FIX-163), content larger than the 8 MiB object limit can be kept under `objects/` as a split object, and a
release basis past the limit is its first user. The content's bytes, in order, are stored as parts, each an object: every
part but the last exactly 8 MiB, and the last at least one byte and no longer. A root object, the canonical JSON
`{"bytes":<length>,"digest":"sha256:<hex>","format":"vericairn.split-object/1","parts":[<part hash>,...]}` (members in
this order, no whitespace, no other member), names them, and the root's hash names the content wherever a record would
name an object, such as an evidence `hash`. The count of parts is the length divided by 8 MiB, rounded up, so the length
fixes the count, every part's length and the root's bytes: one content has one reference. Content within the limit is
stored as that one object, as before, and its own hash names it, unless it would itself read as a root (one JSON object
whose top-level `format` is `vericairn.split-object/1`); such content is wrapped in a root of one part, the only root of
one part a reader accepts, so that reading what was written always returns it. The parts are stored before the root, so a
root is only stored once every part it names is.

A reader treats a root as untrusted and reads no more than it has checked. It measures an object before it reads it and
does not read one past the object limit. Before it reads any part it refuses a root larger than the root of the longest
content it accepts (19,093 bytes for the longest array, 2,516 for a release basis's 256 MiB), a length past its caller's
maximum, a count that does not follow from the length, and a root that is not canonical; then it measures every part and
refuses one missing or of another length than its place holds. Only then does it allocate the stated length once, read
each part into it, check each part against its hash and the whole against the root's digest. A missing, extra, reordered
or altered part fails, and the content is then unreadable rather than partly read. A part may repeat, as identical
stretches of content give identical parts; the work stays bounded by the stated length.

No record field, record type or format number changes: a record names a root as it names any object, by hash. The estate
check sees ordinary objects, each within the limit, so an estate holding split objects passes an earlier release's check; an
earlier reader that expects the content takes the root for an object it does not support. From 0.9.4 the estate check also
reads each object small enough to be a canonical root and reports `objects.split` when a part it names is missing or of the
wrong length. Its work is bounded by the bytes stored, not the length a root states: it measures each distinct part once and
reads none. Each part's bytes are judged by `objects.hash`, once per object, as every object's are, and the whole digest is a
reader's to check. An object of a root's shape that is not a canonical root is not judged by the check, and a reader refuses
it as an invalid root. Wrapped content that is itself a canonical root is stored under its own hash and judged like any root,
since a reader that reads it by that hash takes it for one; it arises only when the content written is another root whose
parts the estate does not hold. `SplitObject` in `Vericairn.Records.Estate` writes and reads them; every reader states the
longest content it accepts.

## History checkpoints (0.9.2)

ID-05 adds the `history-checkpoint` type within format 3, as 0.8.4 added
`template-defaults` and `operator-request`; no record changes and no format number
is added. An earlier format-3 reader reports a checkpoint as `record.unknown-type`
rather than ignoring it, so update every participating reader before an estate
writes one. The design is the estate's signed history design, decision
`01M6CKCGVFN67GT24703NW01AQ`.

| Field | Contract |
| --- | --- |
| `previous` | `{id, hash}` of every checkpoint head the signer's copy held, sorted by id and each once; empty only for the adoption checkpoint, an estate's first. |
| `inventory` | Hashes of the objects listing the records this checkpoint first witnesses, each once. |
| `records`, `recordSetHash` | The count and record set hash of everything it witnesses: its inventory and those of the checkpoints it follows, less the removals they acknowledge. The set hash is an estate observation's: SHA-256 of the canonical JSON array of `{hash, id}` sorted by id. |
| `trigger` | `adoption` exactly when `previous` is empty; `closing` exactly when `closes` is present; otherwise `interval`, `upgrade`, `release`, `charter` or `manual`. |
| `closes` | Optional `{kind, id}`, `kind` `device`, `key`, `grant` or, from 0.9.4, `role`: what a closing closes ([below](#closings-and-authority-at-a-records-time-092)). An older reader refuses a `role` closing's `kind` (`body.enum`), so readers upgrade before an estate writes one. |
| `anchored` | Optional, nonempty `{id, hash}`, sorted by id: unsigned records that count under required signatures, and signed records a closing refused, because this checkpoint lists them. |
| `removed` | Optional, nonempty, sorted by id. `{id, hash, reason}` acknowledges a witnessed removal. From 0.9.5 (ID-19), `{id, hash, commit, reason}` acknowledges an unwitnessed record deletion in Git. |

A checkpoint has no subject and supersedes nothing, and none of its lists names the
checkpoint itself. An inventory object is UTF-8 text with one line per record and a
line feed after each: the record's path under `records/`, one space and its content
hash, as in `admission/2026-10/<record id>.json sha256:<64 hex digits>`.
Lines are sorted by record id across the checkpoint's objects in the order
`inventory` names them, each record is listed once, and no object exceeds 8 MiB.
Objects are content-addressed under `objects/`, so a checkpoint's signature fixes
every line, and merges of record and object files stay pure additions.

**Signed always.** A checkpoint is signed by an admitted device even under an
optional charter, as policy records are; an unsigned one is `signature.required`
and witnesses nothing. Any admitted device, an agent's included, may sign one
without `anchored` or `removed`: witnessing grants nothing.

**The graph.** A checkpoint counts when its signatures verify and it is either an
adoption checkpoint by an author who may anchor (below) or follows, through hashes
that match, only checkpoints that count. A checkpoint whose predecessor is missing,
altered or does not count witnesses nothing (`history.checkpoint-missing`,
`history.altered` or `history.checkpoint-invalid`). A checkpoint that counts
witnesses what its inventory lists and what every checkpoint it follows lists. Two clones that
each write one after the same head leave two heads after git merges them; the
next checkpoint names both, and what it witnesses is the union, with no
reconciliation. The heads, and the records and set hash each head states, are
checked whenever the estate loads.

**What must be present.** Every record a counting checkpoint witnesses is present
with its listed hash. An absent record is `history.removed`, another hash or two
checkpoints listing it with different hashes is `history.altered`, and a missing
predecessor or inventory object is `history.checkpoint-missing`; each is an error
naming the path and the witnessing checkpoint, and an altered record counts for
nothing. Restoring the witnessed bytes settles it, and so does an anchoring
checkpoint's `removed` entry for that id and hash; only the removals an author who
may anchor lists count. A record re-added after its removal was acknowledged is part
of the tail until a checkpoint lists it. When the only listings that disagree with a
present record are by authors who may not anchor, and the record is signed by one
who may anchor (with devices whose own admissions rest on signed admissions) or
is anchored with its present hash, it keeps counting and the estate check warns
`history.listing-mismatch`, naming each such listing's checkpoint and author. Any
other record listed with other bytes is `history.altered`, whoever signed it, its
own signer included. `history.checkpoint-invalid`
reports a malformed inventory, a checkpoint that lists itself, and a head whose
`records` or `recordSetHash` differ from what it witnesses.

**Anchoring.** Only the root operator, or a human principal holding `administer`
through a signed grant from the root operator or an operator whose role rests on
signatures, whose own principal record rests on signatures and was written by such
an operator, whose grant stood at the checkpoint's time, and whose grant no such operator has since revised away and no closing that does not witness the checkpoint has closed ([below](#closings-and-authority-at-a-records-time-092)), anchors or acknowledges, never an agent, and only with devices whose
own admissions rest on signed admissions; another author's lists are
`history.anchor-author` and count for nothing, while its checkpoint still witnesses.
An estate's first checkpoint, its adoption, counts only when one who may anchor signs
it; another author's is `history.adoption-author` and adopts nothing. An estate has
adopted checkpoints when it holds a counting checkpoint, that is, when the graph of
counting checkpoints reaches a present adoption checkpoint by an author who may
anchor; a checkpoint that does not reach one adopts nothing. There, while the
charter requires signatures, an unsigned record counts only when an anchoring
checkpoint lists its exact id and hash, whatever its date; one that is not anchored
is `history.unanchored`, or `signature.required` when dated after the current charter
revision. An unsigned admission also counts when an anchoring checkpoint lists it,
besides the format-2 migration's list. Before adoption the earlier rule holds:
an unsigned record dated at or before the current required revision counts.

**What an anchor decides.** An anchor exempts the exact bytes it lists from the signature requirement, from a closing's refusal except for a device revision, and from `history.altered` when only authors who may not anchor list other bytes for that id; every other rule still judges them, so an unsigned checkpoint, policy record or device revision, or an unsigned revision of a required charter, counts for nothing anchored or not.
It authenticates nothing: an anchored record's `by` stays its author's claim, and the
rules that judge its author read that principal's roles and grants as for any record.
Its `at` is read as any counting record's. A device revision a closing refused changes
no key or name, anchored or not. The runtime refuses an anchor of bytes not present, of
a signed record no closing refuses and of a device revision a closing refused.

**What a removal leaves.** A removed witnessed record is absent, so every rule reads
the copy without it: a removed revocation leaves its device admitted there, bounded
only by a closing of that device or of its key ([below](#closings-and-authority-at-a-records-time-092)).
The error holds release submission, and the runtime refuses every write that would
leave it standing. An acknowledgement ends the error for the version it names and
decides nothing else: that version is no longer witnessed, the estate reads on without
the record, and a record under that id, written again or present with other bytes, is
part of the tail and counts by its own rules until a checkpoint lists it. The runtime
refuses a witnessed-removal entry for a record present with its listed hash or for a
version no head witnesses.

**Unwitnessed Git deletions (0.9.5).** An entry with `commit` requires the root
operator's admitted presence device. It names the full deleting commit, the absent
record's id and content hash from that commit's parent, and a non-blank reason. A
complete local history must show the deletion at the record's canonical path in
that commit and reach the commit from the local main line. The entry leaves the
inventory and record set hash unchanged. `estate check` reports
`history.deleted-in-git` as acknowledged and names the checkpoint when it can
verify the same deletion. Every other unwitnessed deletion still warns. Earlier
readers refuse the added `commit` field.

**Who writes them (OPS-07).** `vericairn estate checkpoint` writes one with trigger `manual` and, with `--adopt`, the
adoption checkpoint, anchoring the unsigned records that count; a new estate's root operator's first device writes the
adoption when it admits itself. In an estate that has adopted, the runtime writes one in the same batch as the act, signed by
the device then writing: `upgrade` at an estate upgrade, `release` at a release submission before its own admission check
(a submission's check writes none), `charter` after a charter revision, anchoring the unsigned records that count before a
revision to required signatures by one who may anchor, and `interval` once the unwitnessed tail holds 500 records or its
oldest record is 24 hours old. It writes `closing` before a revocation, a key rotation or a narrowing grant revision, in the
same batch (below). No field changes; a reader judges every checkpoint the same way, whatever its trigger.

**Records that do not count.** A record that does not count is reported and kept: an unsigned record the rule above refuses, a
record whose signature does not verify by a device admitted when it was signed, an unsigned policy record, checkpoint or device
revision, an altered witnessed record and a record a closing refuses (below). From 0.9.2 the model's type and subject lookups,
currency and supersession leave such a record out, in every estate, so no rule that admits, authorizes, accepts, clears or holds
reads it, and a record that does not count supersedes nothing. A record whose subject or body reference does not count reports
`record.subject-not-counting` or `record.reference-not-counting`. A device revision changes its key or name only when its device
signed it. An admission counts from the estate's first authority outward: a signed admission counts only when its signatures
verify by a device admitted, at its time, through admissions that count in turn, and its author may admit at its time: the root
operator, a person holding the operator role or a person holding `administer`. That authority is read from the root operator's
first principal record and from the principal and grant revisions their authors may write, as the records that give roles read
them: signed with signatures that verify that way, anchored, or, before adoption, unsigned and dated no later than the charter
first required signatures; a principal revision by an operator, or by a person holding `administer` for a line whose revision
they wrote and giving no operator role; a grant revision by an operator; and a revision that takes a role away or narrows a
grant, written by one who held the operator role earlier; never a principal's own record, nor one dated before the revision
below it. Each revision follows the nearest such revision below it in its line. A person's line is read as it stood at the
admission's time, and a grant revision in force then, unexpired then and closed by no closing that does not witness the
admission; a holder of `administer` is a person only when their line says so read from signed or anchored revisions too. So
admissions that rest only on one another admit nobody, an agent's admission, even signed with its own admitted key, admits
nothing, a grant written later or closed before authorizes no admission, and a revision written later restores no earlier
admission. Where this reading refuses an admission's author, the admission is reported as `admission.author`. An admission whose
signature does not verify admits and revokes nothing, and one that would unseat what its own signature or its author's authority
rests on counts for nothing and unseats nothing (`admission.unseats-support`): the admissions are judged again without it, a
device's revocations together, so copies of one cannot hide each other.

**The first charter.** The root operator is the operator that the first charter names: the charter the manifest's `estate`
names, which another charter that supersedes nothing never displaces (that one is `estate.multiple`). Witnessed history only
confirms it: a charter that supersedes nothing is witnessed when, judged under its own operator, the estate has adopted
checkpoints and the counting checkpoints witnessed it first, with its bytes present. The manifest's charter witnessed alone is
the first; any other witnessed, alone or beside it, is reported as `history.root-ambiguous` (and, when the manifest's is not
witnessed, `history.manifest-root`), and the estate then has no root operator and no record counts. A reader without a manifest
takes the first such charter by id.

**The first authority.** Admissions count outward from the first authority, an admission by the root operator of its own device
that the device signed, never a recovery device. It is never chosen by its stated date or id. It is the one the manifest's
`firstAuthority` names, or, when the manifest names none, the only one; a manifest naming an admission that is not one, or
naming none while there are several, is `manifest.first-authority`, whose message lists each with the device it admits, and
there is then no first authority, so no signed admission counts until the manifest names the right one. Witnessed history only
confirms it, as for the first charter: the one the manifest names, witnessed alone, stands; another witnessed, judged with it as
the first authority, is `history.root-ambiguous`, with no record counting. Every other such self-admission is
`signature.self-admission`. From 0.9.4 (FIX-134), a self-admission is the first authority only when, read as the first authority,
its content has the hash its envelope declares and its signature verifies as every signature does: under its device's key at its
stated time, by a device of its author. One the manifest names that does not, or the only one when it names none, is
`manifest.first-authority` naming it, there is no first authority and nothing signed under it counts until the manifest names
one that verifies; it is never witnessed first under any reading either. Before 0.9.4 such an admission was read as the first
authority, flagged only by its own `signature.invalid`.

## Closings and authority at a record's time (0.9.2)

ID-06 gives `closes` its meaning and reads authority as it stood at a record's time, in an
estate that has adopted history checkpoints. No field, type or format number is added. An
estate that has not adopted reads exactly as before, except that the runtime refuses a
stated time more than five minutes past its clock in every estate (below).

**Closings.** A closing is a checkpoint with `trigger` `closing` and `closes`, written just
before the act it closes. In an estate that has adopted, the runtime writes it in the same batch as the act, dated no later
than the act and signed by the key that performs it, and refuses an act whose closing it cannot write or that comes without
one, such as a record signed elsewhere, and an act stated more than five minutes before what its closing would witness. An act
written again after a batch stopped between the two is stored alone after the closing its author stored then, which counts,
acts and no act follows yet:

| `closes.kind` | `closes.id` names | The act it precedes | Who signs it |
| --- | --- | --- | --- |
| `device` | Any device record of the device's line | Its revocation | An operator, a person holding `administer`, or an operator's recovery device |
| `key` | The device revision whose public key is replaced | A rotation to another key | The device itself, for its own principal |
| `grant` | The authority revision being narrowed; the closing closes it and every revision of its line dated at or before it | A revision naming another grantee, dropping a scope, reserving another action, adding a condition, adding or shortening an expiry, or changing a policy scope's bounds | An operator, who may revise the grant |
| `role` (0.9.4) | The principal revision that gives a person the operator role; the closing closes it and every revision of its line dated at or before it | A revision of that person's record taking the operator role away, dropping it or describing another kind of principal; never for the root operator; removing any other role writes no closing | The root operator or another operator, the role still standing, never the person |

**Role closings (0.9.4).** From 0.9.4 (ID-14), removing the operator role from a person writes a role
closing; removing any other role writes none, and that role is read at each record's time, as
before (decision `01M7JSJSHTTX86F10007GDQ238`). A `role` closing naming a revision that gives no
operator role closes nothing (`history.closing-author`). A `role` closing closes a person: from it on,
a record the person wrote, signed or unsigned, that the closing or a checkpoint it follows does
not witness and no anchor lists counts for nothing (`history.after-closing`), whatever date it
states and whatever device signed it, in every read: a decision, a grant, a revision of their own
or another principal's line, an admission or a closing. Such a record never takes another
person's role or voids another's closing: the role closings that act are read when every other
closing is judged, those before them in checkpoint order included, though never against
themselves. Until the removal follows, the check warns `history.closing-unfollowed`. What the
person gave as operator before the closing, the principal and authority revisions and the
closings it witnesses, keeps counting for anchoring, acknowledging a removal, rooting an
adoption and the closings, as written, where the person held the role when writing it; so a
closing keeps the force it had when it was signed when its author is later removed with their own
role closing. A removal of the operator role in an adopted estate that no role closing names is the
warning `history.closing-missing`, and the runtime refuses to write one without its closing; such a
removal, and only such a one, still withdraws its author's closings from these acts. Since a role
closing is signed by the root operator or another standing operator, and only an operator gives or
takes the operator role, a person holding `administer` but not the operator role does not write
one; removing another role from a person they admitted is written as before, with no closing.

Closings are judged in checkpoint order: every checkpoint after those it follows, ids
breaking ties. A closing acts when the checkpoints it follows, present with the hashes it
names, reach an adoption checkpoint by one who may anchor; when its signatures verify
against the closings before it in that order; and when its author may perform the act. For a
device, a grant or a role that power reads as before signed history: the signer's operator role
or `administer` grant stood at the closing's time and still stands, no trusted revision having
since narrowed, revoked or removed it and no closing before this one having closed that
grant's line, and a recovery device signs only for an operator; from 0.9.4 an operator whose
role was since removed still holds that power for a closing their own role closing witnesses
(below). That power is read against the closings before this one, and the role closings that
act, never against this closing itself, since its author chooses what it witnesses. Whether a checkpoint following no other may anchor is read against the
closings that act, and those act only when reached from such a checkpoint, so the two are
settled together: from every such checkpoint that verifies, the closings reached are judged,
each such checkpoint whose author may not anchor with those closings acting is dropped, and
this is repeated until none is. The checkpoints between it and the adoption need not count,
since a closing acts by itself. One whose author may not perform the act is
`history.closing-author`, as is one that verifies only without the closings before it,
because one of them closed its signer's device or key or refused that device's admission; it
closes nothing, anchored or not, and still witnesses what it lists if it counts. Closings are
judged with the format-2 migration's anchors alone, so one signed by a device admitted only
through an anchoring checkpoint closes nothing either, and is `history.closing-author` when it
counts. One naming a record of another type is `history.checkpoint-invalid`. Two concurrent closings, neither
following the other, each act, each judged against the closings before it. The recovery
device signs a device closing and no other checkpoint (`signature.recovery-scope`).

What a closing witnesses is what it and every checkpoint it follows list, with the bytes
present. From a closing that acts on, these records count only when it witnesses them,
whatever date they state:

- for a device: every record signed by a device of its line, except a revocation of that
  device whose `revokedAt` is at or after the closing's time, the act it precedes;
- for a key: every record with a signature that verifies with the closed key, except the
  lowest-id revision of the device that replaces that key with another, the rotation it
  precedes;
- for a grant: every record by the grantee of a closed revision, other than admissions,
  devices, grants and checkpoints, dated while one of the closed revisions was in force: at or
  after it, before the first revision replacing it that counts, and before it expires. Since
  the closed revisions run from the line's first to the one named, the window starts at the
  line's first revision. The rule fails closed: it refuses every such record of the grantee,
  not only those that needed the grant.

Any other such record is `history.after-closing`, an error that leads status and holds
release submission. It is kept and counts for nothing until an anchoring checkpoint of the
root operator or a person holding `administer` lists its exact id and hash, which,
from 0.9.2, it may do for a signed record a closing refuses as well as for an unsigned one. Such
an admission or revocation admits or revokes nothing until anchored. A device revision a
closing refuses changes no key or name, and an anchor does not restore it, so an anchoring
checkpoint naming one is refused; the device's line skips it. A checkpoint a closing refuses witnesses nothing until anchored, nor do those
following it. Deleting the revocation, rotation or narrowing after a closing reopens
nothing. A closing that acts while no revocation of its device that counts, no rotation
replacing its key or no revision of its grant follows it is the warning
`history.closing-unfollowed`; it closes all the same. A key's closing is followed as well by a
revocation of its device that counts or a closing of its device that acts. A revocation, a rotation to another key or
a narrowing grant revision that counts, that the adoption checkpoint does not witness, and whose
device, key or grant revision no closing that acts names, is the warning `history.closing-missing`:
it arrived without its closing, so what its target signs with an earlier date still counts. A
closing that names the target, written later, ends it.

A grant revision is compared with, and its closing names, the revision it replaces: the one it
supersedes when that counts, otherwise the nearest revision below it in its line that counts,
the record it follows there; with none below it that counts, it narrows nothing. A key closing
needs no such reading, since a device revision counts only on top of one that counts.

**Authority as it stood.** In an adopted estate, the roles and grants a record's rules read
are read at its authority time: its own `at`, or, for a record the adoption checkpoint
witnesses with its present bytes, the adoption's `at`, so that such a record keeps the
reading it had then. A principal's role comes from the latest revision of its line that
counts dated at or before that time, and none before its first record. A scope comes from
each authority line's latest revision that counts dated at or before that time, unexpired at
the record's own `at`, and not closed by a closing that does not witness the record, unless
the record is anchored. A later grant authorizes nothing earlier, and a later narrowing withdraws nothing
already written. Validators read roles and scopes this way, as do the acceptance, review,
clearance, declaration, hand-over and step rules that judge one record's author, and the
rules for who cleared a flow step's review, each at that review's place. Policy authority,
from 0.9.2, reads its own way. A person other than the root operator acts as the operator for a policy record
when their line gives the operator role at the record's time and still gives it now. For this, only principal records authentic by
their signatures give the role, while any revision that takes it away keeps counting, signed or not, whoever of those who held the
role wrote it and whatever became of its author; and an appointment by a person who no longer stands counts only where signed
history places it before that person's removal. A person who has lost the role since acted as the operator only through a record
that signed history places before the loss: a counting history checkpoint signed by the root operator, or by an operator who held
the role when it was written and stands now as the strongest acts read it, lists the record with its bytes or follows, through the
checkpoints it names, a counting checkpoint that does; is dated no later than the revision that took the role away and does not
list it; and precedes every counting checkpoint that lists that revision. So a checkpoint by anyone else places a record before the
removal only through such a checkpoint that follows it before the removal, and one beside the removal or after it places nothing
before it, whoever follows it later. A policy, exception or writing record, a prompt preference, a shared prompt or a
reconciliation whose own validation finds its approval or authority missing (`policy.approval`, `policy.authority`,
`prompt.authority`) counts for nothing. Where nothing could place a record before a removal that came first, in an estate that has
not adopted checkpoints, or for a record its adoption checkpoint witnesses when someone else than the root operator or a standing
operator signed that adoption and no checkpoint by one of them follows it before the removal, a policy record that fails only
because an operator role it rests on was taken away counts for nothing and is the warning `policy.operator-removed`, not an error,
so an estate holding such a removal stays valid; so do the policy records of a person whom the removed operator appointed,
directly or through another appointee. A record written after adoption that no checkpoint places before the removal stays an
error. The runtime writes a revision that takes away the operator role or `administer` as follows. In an estate that has adopted
checkpoints, it first writes, in the same batch, a checkpoint signed by the acting operator's device on its machine, which follows
every checkpoint before it, so what any earlier checkpoint lists keeps its reading: for a grant revision that needs a closing, the
closing it is written with, and otherwise one of its own, with trigger `manual`, as for a grant revision that OPS-07 does not read
as narrowing. A removal signed on another device is written alone, apart from a grant revision that needs a closing, which is not
written from another device. In an estate that has not, it refuses the revision when any record would fail with it, whether the
record would count for nothing, as a policy record or its approval would, or its checks would find errors, as a decision by the
person would, since such an estate reads roles as they are now; the refusal names the records and says that adopting checkpoints
first keeps them. A removal no record fails with is written. `operator record`, which carries one signed record through a checked
merge, refuses a revision that takes a role away in an estate that has adopted checkpoints, since it cannot carry the checkpoint,
and names `records new`, which writes the checkpoint and the revision together; in one that has not, it refuses such a revision
when any record would fail with it, as the runtime does, and names adopting checkpoints and then `records new`. An action stopped
at its record stage is judged again when it resumes. The strongest acts read more strictly, as
before signed history: anchoring, acknowledging a removal, rooting an adoption and signing a
device's or a grant's closing need the signer's operator role or `administer` grant to stand
at the checkpoint's time and still stand, so a later narrowing, revocation or closing
withdraws those powers for every checkpoint, except that from 0.9.4 a role closing keeps, for
the checkpoints and the principal and authority revisions it witnesses, the operator role its
person held when writing them. From 0.9.2 (FIX-108) those acts read grant and
principal revisions as follows. The revisions considered are those authentic by their
signatures against the closings in force for the act, so none such a closing refuses, and not
refused for their date against the nearest revision below them considered (below), with the
root operator's first record. The closings in force are, for a closing, those before it in
checkpoint order, and for the adoption checkpoint, anchoring and every other read, those
that act. A
person has been given the operator role by a time when a revision considered of their line,
dated by then, gives it and was written by the root operator or by a person given the role by
an earlier revision, taken in time order, ids breaking ties; removals do not count here. Taking
every line in that order, a revision is read as written when its author is the root operator,
or stands now and held the role as their line stood before the revision; as a removal,
whatever it says, when its author had been given the role by then but had lost it or does not
stand now, after which the line gives no operator role or `administer` until a revision read
as written follows it; and not at all when its author had never been given the role, which
leaves the line as it was: a revision follows the nearest revision below it that is read. A
line describes a person at a time by the single revision read, dated by then, that no other
follows; a revision read as a removal reads as written, except that it gives no operator role
or `administer`, and that on top of a revision read below it, it describes a human only where
that revision does too: so one saying the person is not human takes that away, and one with no
revision read below it, such as a line's first record, describes a human when it says so. Who
stands now is the least set holding the root operator and every person whose line gives the
operator role at the end when only members of the set give, built up from the root operator;
grants do not count towards it. So a removal keeps counting after its author is removed, a
revision by one never given the role changes nothing, a circle of appointments gives nothing,
and two operators other than the root operator who remove each other both lose these acts,
closings of each other's devices included, until the root operator settles it. Since a
closing is never read against itself, an operator's closing of another's device does not
settle a removal of its author answered after it; a closing by the root operator refuses
such an answer for the closings that follow it, the adoption and anchoring, and a closing
before it is judged without it. So a person whose role the root operator removed, and whose
device it closed and revoked, can still write, at any time and repeatedly, a removal of an
operator other than the root operator or a narrowing of an administrator's grant, dated any
time from their appointment until that device's revocation; every closing that does not follow
the root operator's closing then closes nothing if that operator or administrator wrote it, or
anyone whose operator role or `administer` rests on that operator's appointments or grants,
directly or through others', and records it alone refused count again. A new closing does not
undo it, since it witnesses those records. Removing those records does, with an anchoring
checkpoint that acknowledges their removal when a checkpoint witnesses them; writing the
operator's role again gives back the closings of those whose role or `administer` rests on that
operator's appointments or grants, and the operator's own earlier closings only when it is dated
after the removal and before them. Either holds only until more such records are written, among
them a removal on top of the role written again or forking the line below it. The root
operator's own closings cannot be reopened this way, and from 0.9.4 a role closing ends it for
everything the person writes after the closing ([above](#closings-and-authority-at-a-records-time-092)).
Removing an operator's role without a role closing takes, for these acts, what that
person appointed, granted and closed; those the person appointed keep the role in ordinary
reads, and a removal they write still counts for these acts, until the root operator writes
their appointments again or closes their devices, after which what they sign with a closed
device counts for nothing. In an estate holding a history checkpoint,
each grant revision giving `administer`, and each principal revision giving the operator role
or describing a human these acts do not read as one, that counts but that these acts read as
a removal is the warning `history.revision-author`: that no longer counts for anchoring and
closings, and a standing operator should write it again. Other records keep the reading as it
stood.

In every estate, principal and authority records give roles and scopes only when their authors may write them, read in time
order, ids breaking ties, from the root operator, each judged by the records accepted before it. A principal record is written by
an operator or a principal holding `administer`, and one that gives the operator role only by an operator; a principal holding
`administer` amends only a line whose revision it wrote; a grant is written by an operator. A person who held the operator role
earlier may still take it away from a line that gives it, or narrow a grant, after losing the role, so a removal keeps counting. A
principal record written by the principal it describes, at any revision, other than the root operator's, counts for nothing in
every read, admissions, anchoring and closings included, and a revision on top of it attaches to the nearest record below it that
counts: it is the error `principal.self-authored` when it would give a role or change the kind of principal, and the same code as
a warning when it would give nothing the line did not, such as a rename. An agent's or a service's record gives no operator role
(`principal.operator`). A record that fails these checks stays reported and gives no role or scope, so no record's own validation
reads a role from that record. Harness qualifications, repository changes, routing, pairing and the records the runtime reports as
current read roles and grants the same way. Admissions read an author's authority from these records as they stood at the
admission's time (above). The runtime writes principal and authority records at its own time with a fresh id; `records new`
refuses `--at` and `--id` for them.

In every estate, a grant or principal revision dated before the record it attaches to counts
for nothing, whoever signed it. It attaches to the nearest record below it in its line that
counts, skipping any that does not: unsigned, refused this way or not counting otherwise. A
revision on top of one that does not count is compared with, and follows, the line there, so
its line has one current record in every read. The strongest acts, which consider only the
revisions above, compare a revision with the nearest one below it that they consider. From 0.9.4 a removal of a person's operator role
in an estate that has adopted is written after the person's role closing ([above](#closings-and-authority-at-a-records-time-092)), which
refuses everything the person writes that it does not witness, whatever date it states. In an estate that has not adopted, and
for a removal no role closing names, a former operator's records dated into the time the role stood read as it stood, apart from
policy authority, which reads them only where signed history places them before the removal; close and revoke a person's devices
when removing their role there.

**What a claimed time decides.** Path month, order, release-notes windows, progress, ages
and duty sequence still follow `at` for records that count, ids breaking ties. The
required-signature boundary is decided by anchoring. The key and admission a signature
verifies against is the signer's claim of time, bounded by closings. Canonical status and
policy evaluation take their recorded time from the records that count.
Roles and grants are read at the stated time as above, bounded by grant closings, while the
strongest acts also need the role or grant to still stand, so a stated time carries none of those
powers past a later removal or narrowing; what a person's role closing witnesses keeps them, and
what it does not witness counts for nothing.

**Dates checkpoints question.** A signed record dated more than five minutes after the
earliest-dated counting checkpoint that witnesses it, the checkpoint listing it or one that
follows, is the warning `history.dated-after-witness`, naming that checkpoint; a record
the adoption checkpoint witnesses is not questioned. No checkpoint questions an earlier
date: a signed record in the unwitnessed tail may state a time before the newest checkpoint,
and is read at that time unless a closing refuses it. The runtime refuses `records new --at`
and `work attempt --at` more than five minutes past its own clock, and `records new` refuses
`--at` and an explicit id for a history checkpoint, which the runtime dates itself.

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
selection that omits others says so in `limited`, and ranks the candidates it keeps again 1, 2, 3 in their order, so no gap
counts the omitted ones. One count remains: when `more` is true, `limit` minus the number of candidates kept is the number
omitted. Its `reader.audiences` and `index.segments` likewise name only Internal and Public, with one opaque entry,
`Restricted`, standing for every other segment searched: in the segments it is `{"audience": "Restricted"}`, with no basis
hashes, and its `index.revision` is recomputed over the method and the segments it names, so it confirms no restricted label
and does not change when only restricted content does. Validation refuses a hash that does not match, a value canonical JSON
cannot hold (a fraction or a duplicate member, reported as a finding), a candidate, audience or segment of another audience in
a record, more than one `Restricted` segment, more candidates or selections than these limits, an out-of-order rank and any
field not listed here.
The context manifest that holds a package's selections is `vericairn.context-manifest/2`; it is a derived artifact, not a
record. The operation's field descriptions change within `vericairn.records/readers/8`, which has not shipped.
