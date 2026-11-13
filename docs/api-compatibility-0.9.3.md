# API compatibility in 0.9.3

## Presence prompts, announced and counted (FIX-124)

API version 1 adds one operation, `POST /v1/presence/prompts`, a long poll that writes nothing: the presence notices of operator
actions on the runtime's machine after a position. `IEstateApi` adds `PresencePrompts(PresencePromptsRequest)` and
`PresencePromptsAsync(request, cancellationToken)`, each with a default implementation, so existing implementers stay
source-compatible; the default answers no notices and an `unavailable` problem. `EstateService`, `CancellationBoundApi` and the
SDK's `VericairnClient` implement both, and `ClientOperation.Deadline` gives `PresencePrompts` the one-minute deadline `Events` has.
The protocol adds `PresencePromptsRequest` (`after`, the position the reader holds, the runtime's newest when omitted; `wait`, 0 to
25 seconds, 0 by default), `PresencePromptsResponse` (`notices`, oldest first, `cursor` and `problem`), `PresenceNotice`
(`sequence`, `kind`, `action`, `count`, `made`, `signatures`, `text` and, for a prompt, `index` from 1), `PresenceSignature`
(`type`, `key`, `purpose` and `Describe()`) and `PresenceNoticeVocabulary` (`MaxWaitSeconds` 25, `Kept` 256 and `Kinds`:
`announced`, `added`, `revised`, `prompt` and `finished`). A position newer than the runtime's, as after a restart, delivers every
notice kept. A `wait` outside 0 to 25 answers 400 with code `invalid`. The OpenAPI document adds the route and the schemas
`PresencePromptsRequest`, `PresencePromptsResponse`, `PresenceNotice` and `PresenceSignature`.

The route is machine-local: the daemon serves it on its local socket, refuses it over the local network, and refuses every
device-signed request by its scope rule (401, `denied`). It is not exposed through MCP. The runtime that signs, embedded or the
daemon, keeps the newest 256 notices in memory; they are never records, and a runtime of an earlier release has none.

Every signature the runtime makes now passes one signing boundary, which counts it as a prompt when the key's custody enforces
presence and the device record says `presence: required`. Before the first prompt an action announces every record it will sign,
each with its type, the key a person knows it by and what signing it authorizes; each prompt is
`Signature k of n: <what it signs>`; a signature beyond the plan, such as an observation repeated while checks run, is announced as
`added`, with why, before its prompt; a changed path announces what remains as `revised`; and the action ends with how many it
signed, or where it stopped.
Nested operations count against the outer action. One presence check per signature, and every record signed, are unchanged. The
[operator's guide](operator-guide.md#presence-prompts-announced-and-counted-093) lists the count of each release and acceptance
step.

`Vericairn.Records` adds, each additively: `IDeviceKeyStore.Sign(deviceId, digest, reason)`, a default interface member that calls
the existing `Sign`, so existing stores compile unchanged and ignore the reason; its implementation in `SecureEnclaveKeyStore`,
which passes the reason to the `vericairn-enclave` helper for the system dialog, where every signature passed "sign a Vericairn
record" before; `StoreSigner.KeyId`, `StoreSigner.Custody` and `StoreSigner.Sign(digest, reason)`; and `PresenceReason`
(`Default`, `MaxLength` 200 and `Text`), which turns control characters and runs of white space into single spaces, replaces a word
that looks like a path on a machine with `[path]` and cuts the sentence to 200 characters.

The command line follows the notices on standard error while a command that may write runs, through the API that command uses, so
standard output, `--json` included, keeps its one answer. Read-only commands and `mcp serve` follow none, and a runtime that answers
the first read with a problem, or not at all, is not followed. `operator close` no longer prints its fixed line saying that the
signing device checks presence. Workbench's `ShellViewModel` adds `PresencePrompt`, the latest notice's text, and writes the notices
to its activity log, and each prompt's line to its status, while it sends an assignment, a review, an acceptance, a document
review, communication settings, a release submission or a release observation. No record
type, record format or generated document changes ([API](api-v1.md#presence-prompts-093),
[CLI](cli.md#presence-prompts-on-standard-error-093)).

## Release steps sign only what they store (FIX-130)

No route, interface member, protocol type or public member is added or changed. A release step now asks only for signatures it
stores, at every record format. Release evidence (the dispatch receipt, an observation's revisions, the candidate, the observed
publication, a cancellation and a withdrawn draft's observed absence) is written once through the record boundary; at format 3 it
is written only when the acting principal's admitted device held on this machine would sign it, found without signing, so the
refusal where no device would stands as before. The trial signature before the dispatch, made and thrown away in 0.9.2, is gone.

Before the dispatch, the runtime checks without signing that the device it would sign with is one named in the stored intent's
signatures, and that the intent's cancellation would be a valid record. Otherwise `release submit` (`POST /v1/releases/submit`) is
refused with the existing objection `release.signature`, and nothing is dispatched or signed. So a device revoked after the intent,
a key no longer held here, or another admitted device of the same principal that signed nothing in the submission dispatches no
build. The check applies at every record format to an intent that carries a signature: below format 3, such an intent whose device
is gone is no longer dispatched with an unsigned receipt, and an unsigned intent is judged as before. When no device here may sign
the cancellation, the submission's revised path announces and asks for nothing more.

With a device that needs presence, in an estate that has adopted history checkpoints, `release submit` asks 3 prompts where 0.9.2
asked 5 (4 where it asked 6 when the basis changes before dispatch and the reserved intent is recorded again), `release observe` of
a finished build 2 where 4, of a publication 1 where 2, and `release observe --withdrawal` 1 where 2; `release observe --cancel`
asks 1. Before adoption, a submission asks one fewer. The response shapes and objections are otherwise unchanged.

## Naming the estate's first authority (CODE-72)

API version 1 adds one operation, `POST /v1/estate/first-authority`. `IEstateApi` adds `FirstAuthority(FirstAuthorityRequest)` and
`FirstAuthorityAsync(request, cancellationToken)`, each with a default implementation that answers `unsupported`, so existing
implementers stay source-compatible; `EstateService`, `CancellationBoundApi` and the SDK's `VericairnClient` implement both. The SDK
treats a request that names a device or an admission without `check` as a write, with the deadline of a write. The
protocol adds `FirstAuthorityRequest` (`by`, `device`, `admission`, `check`, `expectPlan`), `FirstAuthorityCandidate` (`admission`,
`hash`, `at`, `path`, `device`, `name`, `platform`, `custody`, `presence`, `principal`, `nameable`, `notNameable`, `named`, `reads`,
`witnessedFirst`, `revoked`, `admits`, `recordsSigned`, `signatureVerifies` and `signatureProblem`), `FirstAuthorityChange`
(`manifest`, `before`, `after`) and `FirstAuthorityResponse` (`rootOperator`, `named`, `firstAuthority`, `adopted`, `unsettled`,
`findings`, `candidates`, `change`, `planHash`, `applied` and `remaining`). The OpenAPI document adds the route, its 409 answer and
the four schemas. A refusal is a `Problem` with code `refused` and status 409.

The route is machine-local, like `POST /v1/estate/export`: it is refused over the local network, and the service and the scope rule
refuse every device-signed request (401, `denied`), since the manifest is no record and nothing signs it. Without `device` or
`admission` it lists every self-admission that could be the first authority, with its evidence, and writes nothing. With one and
`check` it returns the `change` and its `planHash`, which binds the change, the manifest's bytes and everything the response shows.
Without `check` it writes only the manifest's `firstAuthority`, under the working tree's write lock, judged again there. It is
refused, writing nothing: for both `device` and `admission`, or `expectPlan` without either; while the manifest does not parse
without findings; for a target that is not an admission by the root operator of one of the root operator's own devices whose
signature by that device verifies (another principal's or an agent's device, a recovery device, a device with no such admission,
any other record); where the first authority already works, because the manifest names it or adopted history settles it; for a copy
that reads unsettled; where history checkpoints witness a candidate first while it is read as the first authority; for a missing or
wrong `by`, a missing or stale `expectPlan`; and in a read-only copy. A revoked first device stays nameable. Nothing is removed:
after a write, `remaining` lists the errors about the root of trust (`manifest.first-authority`, `history.root-ambiguous`,
`history.manifest-root` and each other self-admission's `signature.self-admission`) and every error on the admission named.

`Vericairn.Records` adds `EstateManifest.WithFirstAuthority(manifestBytes, admission)`, the manifest's bytes in canonical pretty
form with `firstAuthority` set, whatever it named before; `NamingFirstAuthority` is unchanged. How the root of trust is judged is
unchanged. The two `manifest.first-authority` messages now end by naming `vericairn estate first-authority`, and an upgrade's
refusal carries them. The command line adds `estate first-authority` with `--device`, `--admission`, `--check`, `--expect-plan` and
`--by`, whose `--json` writes `FirstAuthorityResponse`; naming without `--check` needs `--by` and `--expect-plan` before the runtime
is asked ([CLI](cli.md#vericairn-estate-first-authority-093)). No MCP tool is added, and no record type or record format changes.
[Enrollment troubleshooting](enrollment-troubleshooting.md), a new guide (DOCS-33) that changes no behavior, says when the command
applies to two first devices.

## Required merge queues (CODE-71)

No route, interface member of `IEstateApi` or protocol type is added. `POST /v1/changes/merge` (`changes merge`) keeps
`ChangesMergeRequest` and `ChangeResponse`. Where the target branch requires a merge queue, it now admits the change under the same
authority, required checks at the exact head and scanned push as a direct merge, enqueues the pinned head, and answers `status`
`dispatched` with `checkStatus` `queued` and `ok` false once the change's place in the queue is confirmed; before, such a repository
was refused. `checkStatus` adds `queued` beside `passed`, `failed`, `pending` and `uncertain`. `POST /v1/changes/observe` records the
queue's outcome against the same merge: merged, with its target run; still queued; a `known-failure` only on the host's positive
evidence about this attempt; or `uncertain`. A retry keeps its route, and a merge recorded through the queue is refused once the
branch no longer requires one. `OperatorUnfinishedAction.Stage` (`GET /v1/operator/unfinished`) adds `merge-queued`, and the OpenAPI
schema's enum with it, once the place is confirmed; `operator record` answers `waiting` while a change it merged is still queued
when its wait ends. Older clients see a new string value ([changes](changes.md#required-merge-queues-093)).

No record type or field is added: the queue's facts are target facts of existing records. A pull-request observation before a merge
adds `mergeQueue` (`required` or `none`), `queue` (one of `MergeQueueObservation.States`, or `unknown`) and `queueHead`, the pull
request's own head, and, when the host gives them, `queueGroup`, `queueReason` and `queueRemovedAt`. A merge through the queue
records `mergeQueue` `required`. A queued merge's target-run observation records `mergeQueue` and, for a merge commit the queue
landed inside a group, `queueGroup`, the group's head commit whose runs it read. For such a merge the target observation reads the
queue's `merge_group` runs of exactly that commit beside its push runs, never a newer commit's.

`Vericairn.Runtime` adds, each additively: `IPullRequestProvider.ObserveQueue(destination, number)`, `Enqueue(destination, number,
head)` and `ObserveQueuedTarget(destination, branch, commit)`, whose default implementations report no queue required, refuse an
enqueue before any request and read the push runs alone, so a provider written before them keeps working; `MergeQueueObservation`
(`Required`, `State`, `Head`, `Reason`, `Complete`, the init-only `Group` and `RemovedAt`, and `States`: `absent`, `queued`,
`unmergeable`, `failed` and `removed`), whose `States` the shared API values snapshot pins; the init-only `TargetObservation.Group`,
which takes part in the positional record's equality; `ChangeGate.QueuedRefusals(head, required, checks, complete)`, which counts a
check the push run skipped when a check of the same name passed at that commit; and `MemoryPullRequestProvider.QueueRequired`,
`SettleQueue` and `SetQueuedTarget`. `GitHubPullRequestProvider` implements the three members through `gh`, enqueueing with
`enqueuePullRequest` and `expectedHeadOid`. Both providers' descriptors add the operations `enqueue` and `observe-queue`, which
`GET /v1/providers` lists. Record format remains 3.

## An operator action's trail reaches the estate (FIX-126)

No route, interface member of `IEstateApi` or protocol type is added or changed. `POST /v1/operator/record` keeps its request and
response. Once an operator action is complete, its push and merge trail (the operation records of its push, opening and merge with
every revision, the observations that point to them and any target-failure ask) is settled, and the next operator action in the
same clone carries every settled trail record the working tree still holds untracked into its own commit, beside its own record and
members. The local checkpoint, which is machine state and no record, pins the carried paths and byte hashes, so a resumed action
commits exactly that set; a checkpoint written by 0.9.2 reads as before. The earlier action's record and commit are unchanged, and
any other untracked or changed file still refuses the action. A completed action's next step says how many trail records its
commit carried. `records new` and other writes make no commit.

The estate check (`GET /v1/check`), status, `estate reconcile`, `recover inspect` and a hand-over package's status add the warning
`operator.trail-uncommitted` at `operator-action:<record id>` for each completed action whose settled trail this clone holds
untracked, naming its type, record id, subject and how many records wait. Like `batch.incomplete`, it depends on the clone, so the
canonical status of a clone holding such a trail differs from a fresh clone's.

`IRecordCommitAdapter` in `Vericairn.Runtime` adds `Commit(directory, branch, basis, files, receipts, carried)`, `Verify(directory,
head, basis, files, carried)` and `Untracked(directory, paths)`. Their default implementations carry nothing: the two overloads
refuse a non-empty `carried` and `Untracked` answers null, so the runtime carries no earlier trail through an adapter that does not
override them. `GitRecordCommitAdapter` overrides all three
([operator actions](operator-actions.md#the-push-and-merge-trail-reaches-the-estate-093)).

## Work proposed for a goal waits for its sponsor (FIX-117)

No route, interface member or protocol type is added. Under a goal's `plan.approval` of `sponsor-approves`, a work order that names
the goal among its inputs now joins the goal's work only once who decides for the goal approves it: its sponsor when a person, an
operator when an agent or a service sponsors it. Approval is having written the work order's revision then current, or a
`work-status` of `planned` or `active` on that exact revision; a later revision by anyone else is proposed afresh. The plan's own work
orders always join, and `self-approves-ordinary` keeps its meaning. Judged at a record's place, only the records before it count, so
records already written keep their reading. Nothing routes or charges a proposed work order, since routing, next eligible work, run
admission and events read the goal's work; its authors still count for the goal's independence, a proposed milestone still withdraws
a delegated acceptance, and who would approve it may assign it, so no existing estate gains an error.

`GET /v1/status` adds `proposals` to its canonical JSON when there is a waiting proposal: each with `goal`, `goalTitle`, `sponsor`
(null when an operator approves), `key`, `title`, `id`, `hash` and `by`. An estate without one keeps its canonical status bytes. The
status text adds a section of the work orders waiting for a goal's sponsor. `GET /v1/goals/{id}` adds `proposed` to its JSON when the
goal has any: each with `key`, `id`, `hash`, `title`, `by`, `byName` and `approve`, the command that approves it, or null where the
view carries no commands; `goal show` lists them under Proposed. A proposed work order already accepted or retired waits for no one.
Required attention (`GET /v1/attention`) gives the sponsor, or every operator for a goal an agent or a service sponsors, a `decision
needed` notice, `waiting for the sponsor`, about the work order. Older clients ignore the added members; older servers omit them.

`Vericairn.Records` adds `GoalRules.Proposed(model, goal, before)`, `GoalRules.SponsorApproves(goal)`, `EstateStatus.WaitingProposal`
and `EstateStatus.WaitingProposals(model)`. `GoalRules.Work`, and with it `Routing.GoalsOf`, now leave out a work order that waits
for approval; `Routing.DecidesForStep` also holds for who would approve one. `Vericairn.Presentation` adds `ProposalRow`,
`EstateView.Proposals` (each also a `decision needed` attention item), `ProposedRow` (with `Heading` and `Detail`) and
`GoalPresentation.Proposed`. Workbench's `GoalsWorkspace` adds `Proposed` and `HasProposed`; Remote shows the attention. Record
format remains 3 ([goals](operator-guide.md#17-goals-predicates-and-evaluation)).

## A goal is impossible only on its evaluator's verdict (FIX-116)

No route, interface member, protocol type or public member is added or changed. A new `goal-status` of `impossible` is now refused
at every write surface, a record prepared for a device's signature included, with the finding `goal-status.verdict` at severity
`error` in the response's problem (code `rejected`), unless the goal's current evaluation of its exact revision, by a principal who
authored none of its steps and recorded before the status, judges at least one condition `impossible`. The message names what is
missing and the command that records the verdict, `vericairn goal evaluate <goal> --by <evaluator> --verdict <n>:impossible:<reason>`.
Validation and the estate check report an `impossible` status already in the estate without that verdict under the same code at
severity `info`; it adds no error, and the status keeps its reading and still ends its goal.

The goal-status type's `status` field description says so. Record format remains 3, and no record body changes
([record format 2](record-format-v2.md)).

## An operation keeps its intent and target (FIX-109)

No route, interface member, protocol type or public member is added or changed. A new `operation` revision that changes the
`intent` of the revision it supersedes is now refused with `operation.intent-kept`, and one that changes or drops a `target` fact
its predecessor recorded with `operation.target-kept`, naming each fact, each at severity `error` in the response's problem, at every
write surface, a record prepared for a device's signature included, in every record format. A revision may still add a target fact
it observed, such as the run a release dispatch started. Validation and the estate check report a revision already in the estate
under the same codes at severity `info`; it adds no error and keeps its reading.

Agent runs keep their own rule (`operation.agent-run-target-kept`). Repository changes and account-holder package actions keep
their targets by their own rules and now keep their intent too. A retry after a `known-failure` or a `canceled` outcome that names
its `reconciliation` reads as before and may pin new settings; a retry after `uncertain` keeps the intent and target. Record format
remains 3; no field is added ([record format](record-format.md#an-operation-keeps-its-intent-and-target-093)).

## Work closed by operator close reads as accepted (FIX-128)

No route, interface member, protocol type or record changes. A work order's derived state, which `EstateStatus.WorkItems` and
`EstateStatus.StatusRecord` give and status, progress, next eligible work, operator actions, routing, goal views, goal predicates,
flows, required attention, Workbench and Remote read, now also reads an ordinary work order as `accepted` when an acceptance names
its current result at its exact hash (the newest current result of any of its assignments, as `operator close` records it) and was
recorded while a revision that differs from the current one only in placement stood. An acceptance of an earlier result, of an
earlier revision of the result, or recorded before the work order changed otherwise, reads `accepted (earlier revision)`. A work
order that read accepted still does, and a milestone is accepted only by an acceptance of the milestone itself.

This changes a derived reading of existing estates: ordinary work closed by `operator close` since 0.8.4 now reads `accepted`
where it read `planned` or its latest work status. The canonical status JSON of such an estate changes, and with it a new estate
observation's `statusHash`; observations already recorded keep their facts. Goal predicates read the result's acceptance among the
records a state rests on, and a predicate over work orders whose `fields` name `placement.carrier` never counts the carrier itself,
by its original identity. No shipped flow template changes ([operator actions](operator-actions.md#close-finished-steps-in-one-action)).

## A body timestamp's form (FIX-114)

No route, interface member or protocol type is added or changed. `BodyReader.Timestamp` now checks the form first: any value other
than a whole-second UTC time, `YYYY-MM-DDTHH:MM:SSZ`, such as fractional seconds, a numeric offset, an empty string, a number or
null, is the finding `body.timestamp`, an error naming the field and the required form, where a value of another JSON type was
`body.type` and a string of the wrong length `body.length` before. A missing required timestamp stays `body.missing`, whose message
now gives the form. Nothing is converted. New records are refused as before, and a record already in the estate with such a value is
reported under `body.timestamp` instead of the earlier code, at the same severity.

`FieldDescription.Kind` of every timestamp field is now `timestamp (YYYY-MM-DDTHH:MM:SSZ)`, where it was `timestamp`, so `records
describe` and the generated record reference show the form, and `records describe` adds a line explaining it for a type with a
timestamp field. A caller that compared the kind with `timestamp` reads the new value. Readable companions render descriptions,
not kinds, so the kind changes no reader's bytes. Record format remains 3 ([record format](record-format.md)).

## What a document review's outcome declares (FIX-113)

No route, interface member, protocol type or public member is added or changed, and the independence rule is unchanged. When the
outcome `revised` alone makes a review a self review of a document declared `clearedBy` `independent`, the `document.cleared-by`
refusal now says that `revised` declares the reviewer revised the content, and points to `current`; a reviewer who did revise it
keeps the existing message. The `docs review` overview and `--outcome` help, and the MCP `document_review` tool's description and
its `outcome` argument, now say that `current` judges the current exact bytes, whoever revised them, and that `revised` declares the
reviewer revised the content itself, which counts as self review. `AGENTS.md` is regenerated for the tool's new description
([living documents](living-documents.md#what-each-outcome-declares)).

## A failed document check names what failed (FIX-122)

No route, interface member, protocol type or public member is added or changed. The `links-resolve` check's summary, which a
document review records in `checks[].summary`, now names the document's repository-relative path, the number of links that do not
resolve and the first of them that fit, each with its line and column, counting characters from 1, and whether it leads outside the
repository or names a path that holds nothing; it counts the rest. The `document.check-failed` message carries each failed check's
summary instead of its name alone.

A refusal of `DocumentReview` (`POST /v1/documents/{id}/reviews`, `docs review`, `document_review`) for a failed check adds, beside
that error, one finding `document.check-detail` at severity `info` per problem: for `links-resolve`, each offending link at
`<path>:<line>:<column>`; for any other failed check, its summary at the document's path. Neither names an absolute path. The
refusal's code and the clearance rules are unchanged ([living documents](living-documents.md#when-a-declared-check-fails)).

## Summary-only corrections and historical findings (FIX-120)

No route, interface member, protocol type or public member is added or changed. `NewRecord` (`POST /v1/records`, `records new`)
now refuses a `document-revision` whose file is present in the estate with another hash or size than its body claims, with the
finding `document.pin-content` at severity `error` in a `rejected` problem, so a correction pins only the bytes the file holds. It is
judged when the record is written; no validation finding is added. `docs pin` and template pins are unchanged, and their refusals
of unchanged bytes and of `--render-template` for a plain whole-file document now name the summary-only correction through
`records new document-revision`.

A finding of the repository and policy-section checks about a record that a later revision supersedes now ends with a sentence
saying it is historical and naming that revision, in status, its canonical JSON included, the progress report and the estate check.
The finding keeps its code, severity and location, and none is dropped. An estate holding such a finding therefore has other
canonical status bytes than under 0.9.2 ([living documents](living-documents.md#correcting-a-revisions-summary)).

## Owner instruction files at the estate root (CODE-70)

No route, interface member or protocol type is added or changed. `EstateLayout` adds `OwnerInstructionFiles`, a read-only set of
`AGENTS.md`, `CLAUDE.md` and `GEMINI.md`, which the shared API values snapshot pins; `AllowedTopLevel` keeps its values. The estate
check accepts each at the root only as a file of exactly that name and scans it as text, as it does `README.md`; any other root
entry, a differently cased name or a directory of that name stays `layout.unexpected`. A release before 0.9.3 reports such a file as
`layout.unexpected`, an error. No estate operation reads these files, Vericairn never writes them, and `estate export` does not
carry them.

`mcp orientation` now refuses to write into an estate's root, a directory holding `estate/manifest.json` however the path names it,
with exit code 2 and nothing written; `--check` compares as before ([MCP](mcp.md#owner-instructions-at-the-estate-root-093)).

## Export and restore keep each file's mode (FIX-112)

No route, interface member or protocol type is added or changed, and the export's format stays `vericairn.export/1`. Each inventory
entry of an export (`estate export`, `POST /v1/estate/export`) adds `mode`, `100755` for an executable file and `100644` for any
other, and the archive entry's attributes carry the same mode. Records and objects are never executable; on Windows the root
`bootstrap` is recorded as executable and every other file as not. An export's inventory, and the hash its summary records,
therefore differ from an earlier release's for the same estate.

`EstateExport.Verify` and `EstateExport.Restore` (`estate restore`) read the mode from the verified inventory alone and, on Linux and
macOS, create an executable member with execute permission filtered by the umask. Any other value refuses the export with the new
finding `export.entry-mode` before anything is written. An export without modes restores as before, and a release before 0.9.3
ignores `mode` and restores every file with the default mode ([record format 1](record-format-v1.md)).

## JSON from every command (FIX-119)

No route, interface member or protocol type is added or changed. `doctor --json` now writes one object, where it wrote its text
report: `version`, `runtime`, `git`, `dotnet`, `estate`, `custody`, `enclave`, `records`, `search` and `daemon`, each an object of
`state` (`present`, `missing`, `unavailable` or `not-applicable`), `value` and `detail`, and `stateWarnings`. `version --json` writes
`{version}`, `records describe --json` the type list (`reads`, `writesAtMost`, `types`, `reserved`) or one type with its `fields`,
`records hash --json` `{hash}`, `records format --json` `{formatted, unchanged}`, and `recover compare --json` `thisEstate`,
`otherEstate`, `sameEstate`, `onlyHere`, `onlyThere` and `differing`. Each wrote text with `--json` before. The text output and
standard error are unchanged.

`runtime serve`, `mcp serve` and `recover inspect` now refuse `--json` before they run, a parse error with exit code 1, where they
accepted it and wrote text ([CLI](cli.md#vericairn-doctor---json-and-json-from-every-command-093)).

## A flow extension binds its base's subject (FIX-127)

No route, interface member, protocol type or public member is added or changed. `goal propose --flow --subject` now binds against
the flow resolved with every base it extends, as the goal's validation and `goal show` read it, so a run of an extension, which
declares no subject of its own, binds its base's fields, where every field was refused before. A flow whose chain does not resolve
is refused with the reason. A base flow binds as before.

## Earlier releases named as the current one (FIX-125)

No route, interface member, protocol type or record type is added or changed. `docs pass --before` now reports a mention of an
earlier release only where it names that release as the current one: the value of `--from` or `--previous`, and a token just after
`follows`, `succeeds`, `replaces` or `supersedes`, pass, besides those it passed before. Each finding names its rule,
`current-claim`, `release-artifact`, `current-mention`, `preparation-phrase` or `unfinished-marker`, in its text and, with `--json`,
as `rule`. A recorded verification's summary now says that the guides hold no mention of the earlier releases, by version, as the
current release, where it said no token of them stood outside history sections and labels, and its evidence quotes the rule with
each location; the methods it records are unchanged.

## Keys in forms Vericairn does not use (FIX-132)

`RecordSigning.IsPublicKey` now returns false for any form of key it cannot use, where it threw for some, and
`RecordSigning.Verify` and `RuntimeIdentityScheme.Verify` return false instead of throwing for such a key; no route,
interface member, protocol type or record field is added or changed, and every key Vericairn makes reads as before. A
public key in any other form, an elliptic-curve key that is not a named P-256 key with a 65-byte uncompressed point, is
now not usable on every platform, even where a platform's import formerly took it, and a runtime identity pinned in such
a form verifies no proof; an estate holding such a key sees a `device.public-key` finding for it.

## Judging costs (FIX-107, FIX-121, FIX-131)

No route, interface member, protocol type or public member is added or changed, and every finding, refusal and reading is the same.
The work-order key check builds one map of the current work orders' keys instead of scanning every work order for each, so it runs in
linear time. The key-closing check recovers each device revision signature's keys once, and a key closing refuses the revision
exactly when its closed key is among them, so its cost is near-linear in a device's rotations. Which admissions count under a set of
closings is kept by the records the closings refuse and the grant closings among them, so a chain of closings no longer judges the
admissions once for each closing. No guide changes, since what a closing refuses is unchanged.

## Operations, the OpenAPI document, MCP and the record reader (FIX-124, CODE-72, FIX-113, FIX-116)

`IEstateApi` now has 151 synchronous operations, each with a cancellable counterpart, where 0.9.2 had 149: `PresencePrompts` and
`FirstAuthority` are added, each with a default implementation. The OpenAPI document adds `POST /v1/presence/prompts` and
`POST /v1/estate/first-authority` with their schemas, and adds `merge-queued` to `OperatorUnfinishedAction`'s stages; no other
operation or schema changes. No MCP tool is added or removed, and the server's instructions are unchanged; only the
`document_review` tool's description and its `outcome` argument change.

The generic record reader keeps its implementation identity, `vericairn.records/readers/8`, its hash and its golden fixtures. Its
explanation of a goal-status's `status` field carries FIX-116's added sentence, so a goal-status reader rendered by 0.9.3 differs
from 0.9.2's under the same identity. No other field description or record type summary changes, and FIX-114's timestamp kinds are
not part of a reader's explanation. The generator id and contract version 1 are unchanged.

## The embedded runtime

No route, interface member, protocol type or record changes. 0.9.3 is still built with SDK 10.0.404 (`global.json`), and the
self-contained distributables embed the .NET and ASP.NET Core 10.0.15 runtimes, unchanged from 0.9.2; no lock file changes, and the
phone heads' lock files still pin `Microsoft.NET.ILLink.Tasks` at 10.0.15. The NuGet packages still target `net10.0` and run on the
shared runtimes the installed .NET 10 SDK supplies.
