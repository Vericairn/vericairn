# API compatibility in 0.9.2

## Runs of a flow (PROC-05)

No route, interface member or protocol type is added or changed. `GET /v1/goals/{id}` keeps its `GoalResponse` shape; for a
goal whose plan names a flow, its JSON adds a `flow` member with the run's position: the exact pin and the chain it extends,
whether it resolves here, the run's work order, `until`, every configured value with its source, `complete`, `next`, and each
step instance (`key` and, under `each`, `instance`) with its state, reasons, principal, assignment, result, review, outputs,
`items` (each declared item with its `kind`, whether it is required, its state, reasons, hash and producing record, producer,
review and evidence), inputs (as now and as pinned, with any change after use), done verdicts and recovery. Goals without a
flow have no `flow` member. `GET /v1/goals/{id}/next` keeps
`format` `vericairn.next/1`; for a run it adds `flow`, its `steps` are the flow's step instances (`key`, `instance`, `kind`,
`status` with the step's derived state, `role`, `reasons`, `assignment` and `route` in the existing route shape), and `next`
names a step instance as `key` or `key[instance]`. `POST /v1/goals/{id}/step` returns the same `GoalStepResponse`, whose `step` is
that step instance for a run. Older clients ignore the added members; older servers omit them, which is not evidence that a
goal is not a run.

The SDK's and the MCP server's goal reads carry the same JSON. The `goal_get` description now names a run's position; no tool
is added. The command line adds `goal propose --flow`, `--subject`, `--flow-option` and `--until`. The state language adds
the `flow-step` and `flow-item` vocabularies, each with its values' words and tones.

`Vericairn.Records` adds public types and members, each additive:

- `Templates.FlowContract`, the content contract's vocabularies (step kinds, holders, reserved acts, review independence,
  item kinds, routes, subject types, `StepOption`, `ItemOption`, `OnOff`), `IsFlow`, `Counted` and `ParseDuration`;
  `Templates.FlowDefinition`, the resolved flow with `Resolve(EstateModel, TemplatePin)` and
  `Resolve(TemplateDefinition, Func<TemplatePin, TemplateDefinition>)`, its options and the steps and items its extensions
  turn off (`TurnedOff`, `TurnedOffItems`); and the records `FlowRole`, `FlowInput`, `FlowReview`, `FlowProvenance`,
  `FlowEvidence`, `FlowOutput` (an item: name, kind, media or record type, `Required`, `DefaultOn`, provenance, evidence and
  review), `FlowTrap`, `FlowBranch` and `FlowStep` (with its own review and `ItemOption`).
- `TemplateDefinition.Kind` and `TemplateDefinition.IsFlow`; `TemplateCatalog.Resolve(TemplatePin,
  IEnumerable<TemplateDefinition>)`, which resolves installed or retained content without reading a working directory.
  `TemplateDefinition.Validate` and `Read` accept content of kind `flow` and validate it by the flow contract; document
  templates may state `"kind": "document"`, and content without a kind remains a document template.
- `Estate.FlowRuns`, a run's plan, configuration (with `ItemsOff`), policy-required steps and items, and step references
  (`StepRef` with `Key` and `Instance`); `Estate.FlowPosition`, a run's derived position (`StepInstance`, `ItemState` with
  `ItemStates`, `Output`, `Input`, `Check`) and its text and JSON; and `NextEligible.FlowRoutes`, `FlowRunRoutes`, `FlowStepRoute`,
  `FlowDispatching` and the `RenderText` and `RenderJson` overloads for a run's routes.
- `Routing.Assignments(EstateModel, RecordEnvelope, FlowRuns.StepRef?)` and `Routing.RoleOf(EstateModel,
  RecordEnvelope)`; `GoalPredicates.Matching`, the records a predicate counts.

`TemplateCatalog.All` no longer reads a `docs/templates` directory for a model without a root, such as a policy basis; such
a model's retained content comes from the basis. The policy basis's estate inventory retains `docs/templates` JSON content
beside its Markdown, so its digest differs from an earlier build's for an estate with templates. The foundation policy kind
`vericairn.estate/flow-steps` is added. Record format remains 3; its additions, a goal's `plan.flow`, an assignment's
`step`, and an optional `provenance` on results and operations, are in
[record format 3](record-format-v3.md#runs-of-a-flow-092).

The generic record reader advances its implementation identity to `vericairn.records/readers/6`: the declared `provenance`
field of results and operations and an assignment's `step` change their schema explanations. Its hash changes;
declarations pinned to an earlier implementation require the ordinary explicit generator update before regeneration, and
earlier golden fixtures remain retained. The generator id and contract version 1 are unchanged.

## Live-store diagnostics and the cache rebuild (FIX-93)

No route, interface member, protocol type or public member is added, removed or changed. The public `LiveStore`
operations, and with them `EstateSession` and `EstateService.Open`, now report a SQLite failure as an `IOException` whose
message names the storage profile, the store's path and the recovery route, with the `SqliteException` as its inner
exception. They no longer throw the `SqliteException` itself. A failed `EstateService.Open` disposes its session before the
exception leaves. The command line, which already reported an `IOException` as a bounded `error:` line with exit code `2`,
reports these failures the same way.

A writable `EstateSession` now holds `live.lock`, beside the live store, open with shared access for its lifetime. Sessions
coexist as before. `EstateSession.Open` fails with an `IOException` while a cache rebuild holds that lock alone. Read-only
sessions take no lock.

The command line adds `estate cache rebuild`, which is not offered over the API, the SDK or MCP. It runs only on the
estate's own machine with no daemon serving the estate. Its JSON output is `{estate, cache, records, retained,
retainedFiles}`. No record type, record format or generated document changes.

## Review evidence route (FIX-94)

No route, interface member, protocol type or command option is added or removed. `NewRecord` (`POST /v1/records`,
`records new`) classifies a review as duty evidence only when its subject result revision carries `duties`. Before, any
`read` list did. A review of a result without duties is written as an ordinary record, with or without `read`. It needs no
`expected` hash and no admitted device, and it is signed only as every ordinary write is, when the author holds an admitted
device. An `expected` supplied for an ordinary result or review is now checked against the record it supersedes, or else the
exact current subject. A stale one is a `conflict`, where it was ignored before.

Duty evidence through `NewRecord`, `Result` or `ReviewResult` that lacks its exact subject pin, its `expected` hash or an
admitted device whose key the runtime holds now fails with a `refused` problem naming each one missing. Before, a missing
`expected` was reported as the `conflict` used for a stale hash. A supplied hash that is no longer current remains a
`conflict`. `ReviewResult` (`work review`, `work_review`) still always signs, whatever its result.

Record validation follows the same rule: `signature.required` applies to a review only when its subject result has
`duties`. An unsigned review of a result without duties that carries `read` is valid from 0.9.2. An earlier build reports it as
`signature.required`, so an estate that records one should not be opened by an earlier release. Duty completion still reads
only signed reviews of the exact duty result.

The review type's summary and its `read` field description say which reviews need `expected` and an admitted device, and
so do the `records new --expected` and `work review` help. Because the field description is part of the generic record
reader's schema explanation, that reader advances to `vericairn.records/readers/7`. Its hash changes, and declarations
pinned to an earlier implementation need the ordinary explicit generator update before regeneration. Earlier golden
fixtures remain. The generator id and contract version 1 are unchanged.

## Finishing an operator action after its merge (FIX-89)

`IEstateApi` adds `OperatorUnfinished()` and `OperatorFinish(OperatorFinishRequest)`, with their cancellable counterparts
`OperatorUnfinishedAsync` and `OperatorFinishAsync`; each has a default implementation, so existing implementers stay
source-compatible. The daemon adds `GET /v1/operator/unfinished` and `POST /v1/operator/unfinished`, the SDK's `VericairnClient`
implements all four, and `OperatorFinish` has the 15-minute deadline `OperatorRecord` has. The protocol adds
`OperatorFinishRequest`, `OperatorUnfinishedResponse` and `OperatorUnfinishedAction`. The command line adds `operator
unfinished`; no MCP tool is added.

`IRecordCommitAdapter` in `Vericairn.Runtime` adds overloads of `Prepare` and `Branch` that take the untracked receipts to
tolerate: those of the earlier action the runtime finished from its records. Their default implementations call the existing
members and tolerate none, so an adapter that does not override them refuses those receipts, as before.
`GitRecordCommitAdapter` overrides both.

`POST /v1/operator/record` keeps `OperatorRecordRequest` and `OperatorRecordResponse`. Its behavior changes: an action whose
merge is recorded resumes by observing its target run only, without branch, commit, push, opening or merge, wherever the clone
is; and while an earlier action is unfinished, a new one first finishes it when its merge is recorded and its target checks
pass, and otherwise is refused with `status` `refused`, naming the earlier action's record id, type and subject. The local
checkpoint, which is machine state and no record, also keeps the action's type, subject and author and the finished action
whose receipts the next one tolerates; checkpoints written by earlier builds are read as before.

## An acceptance rests only on reviews that passed (FIX-85)

No route, interface member or protocol type is added or changed. A new acceptance by a principal other than the operator
whose `basis` names a review of its subject with outcome `findings` or `fail` is now refused at every write surface (the
command line, the daemon, the SDK and a device-prepared record) with the finding `acceptance.basis-outcome` at severity
`error` in the response's problem. The operator's acceptances are unchanged. Validation and the estate check report an
acceptance already in the estate that names such a review with the same code at severity `info`; it adds no error and the
acceptance keeps accepting its subject. Record format remains 3, and no record body changes.

## A release basis keeps evidence files by hash (FIX-95)

No route, interface member or protocol type is added or changed. A basis that submission retains now has format
`vericairn.release-basis/3`, the shape of `vericairn.release-basis/2`, and `ReleaseBasisEvidence.Format` carries that value.
The basis retains as objects only the files read from a working tree; each file of a repository read at an exact commit is
named by path and hash. `GET /v1/release-bases/{hash}`, the SDK, the CLI and the `release_basis_get` tool still return the
complete evidence, reading each such file's bytes from a clone registered on the serving machine at its commit. When none
holds it and no earlier copy is retained, the file is a `release-basis.missing` finding whose message names the repository's
Git objects at the recorded commit. Servers before 0.9.2 answer HTTP 404 for a format-3 basis, as for any unsupported object.

Release admission, through `release submit --check`, `POST /v1/releases/check` and submission, adds the objection
`release.basis-retained` with its next action when a basis would retain more than 4,096 files or 67,108,864 bytes;
`release.basis-file-size` now applies only to files the basis retains. Bases of formats 1 and 2 read as before, and a format-2
basis whose exact-commit copies are absent from the estate reads those files, and any evidence object of the same hash, from Git.

## A workspace setup command runs with a filtered environment (FIX-96)

No route or protocol type changes. `Vericairn.Runtime.Workers.WorkspaceSetupConfiguration` adds `Variables`, an optional list of
variable names (null when none), and the constant `MaximumVariables` (64). A setup file holds `variables` only when it names some,
so files without them keep 0.9.1's shape and read in either version; a file naming `variables` is refused by 0.9.1 as an unknown
field. `WorkerConfigurationStore.WriteSetup` and `ReadSetup` refuse a list of more than 64 names, a name given twice, a name that is
not a portable variable name, or one starting with `VERICAIRN_`.

The setup command's environment changes: it is the one `WorkerEnvironment.Build` gives a harness, without launcher names, plus the
worker's variables the configuration names, and never a `VERICAIRN_` variable. In 0.9.1 it was the worker's whole environment less
the `VERICAIRN_` variables, so a setup command that relied on another ambient variable needs it named. The command line adds
`worker configure --setup-variable <name>`, repeatable and given with `--setup-command`; `worker configure --json` adds `variables` to
each setup entry, an empty array when the command names none and null when its file cannot be read.

## Output locations carry no scheme (FIX-97)

No route, interface member, protocol type or public member is added or changed. A new result whose `outputs[].location`
begins with a URL scheme (letters, digits, `+`, `-` or `.` before a colon, such as `https:` or `file:`) is now refused at
every write surface (`NewRecord`, `Result`, a batch, a device-prepared record, the command line, the daemon, the SDK and MCP)
with the finding `result.output-scheme` at severity `error` in the response's problem; before, such a location was accepted.
Validation and the estate check report a result already in the estate with such a location under the same code at severity
`warning`, once per result, at `#body.outputs`, naming each such output and its scheme; it adds no error and the result
keeps its reading. Evidence locations, which may be URLs, are unchanged. Record
format remains 3, and no record body or field description changes.

## One writer at a time per working tree (FIX-99)

No route, interface member or protocol type is added or changed. Every record write of a writable `EstateSession`, through
`EstateService` in the daemon or an embedded command, now takes an exclusive write lock in the working tree's
`.vericairn/write-lock/` directory, whatever the runtime's Vericairn home, and holds it from judging the proposed records
until they and their companions are stored. A write that cannot take the lock within 30 seconds fails with a `conflict`
problem whose message names the holder's process id, host, home digest and the time it took the lock; the daemon answers
it with HTTP 409 on `WriteOutcome` routes, and a composite response carries it as its problem. The public
`EstateSession.Write` and `EstateSession.Discard` take the same lock when their caller does not hold it, and throw an
`IOException` naming the holder after the wait. A local-only estate, whose records live in the live store, takes the
same lock in `write-lock/` beside that store. Read-only sessions take no lock. The lock is separate from FIX-93's
per-home `live.lock`. The holder's description, `holder.json`, is machine-local diagnosis beside the lock and is not a
record. No record type, record format or generated document changes.

## A new record names a known principal as its author (FIX-98)

No route, interface member, protocol type or public member is added or changed. A new record whose envelope `by` names no
record, or names a record of a type other than `principal`, is now refused at every write surface (`NewRecord` and the typed
operations, a batch, a device-prepared record, the command line, the daemon, the SDK and MCP) with the finding
`record.author` at severity `error` in the response's problem. The check is made when the record is written, whatever `at`
it states. Before, any well-formed ULID was accepted for a record type with no author check of its own, such as a resource.
The root operator's self-authored principal record is unchanged. Validation and the estate check report a record already in
the estate whose author does not resolve under the same code at severity `info`; it adds no error and the record keeps its
reading. Type-specific author errors, such as `result.author`, are unchanged. Record format remains 3, and no record body
changes.

## An upgrade replaces an older runtime (OPS-05)

No route, interface member, protocol type or public member is added, removed or changed. `GET /v1/health` keeps its
`HealthResponse`, whose `vericairn` the command line now compares, without build metadata, with the release the estate's
manifest pins. Comparison follows SemVer precedence, so a prerelease of the pinned version is older than it.

The command line changes. `estate upgrade`, when it applies an upgrade or runs again once applied, ends with a runtime step:
a daemon serving the estate on this machine whose release is older than the new pin is asked to stop through the existing
`POST /v1/shutdown`, and when the command line is the pinned release's build, by version and by the source commit the
pinned release record or dependency observation names, it starts that release's daemon as `runtime start` does. A daemon of the pinned or a newer release, of another estate or root, or one that refuses the connection is not
stopped, and none is started where none served. Its `--json` output, the `UpgradeResponse` as before, adds `runtime` with
`outcome`, `served`, `started`, `done` and `warning`; `--check` adds nothing. `runtime status --json` adds
`servingRelease`, `pin`, `pinnedRelease` and `mismatch`. Human `status` adds a final `Serving runtime (this machine):` line,
and a `Mismatch:` line, when a daemon served it; `status --json` is unchanged, with a mismatch on stderr. Older clients
ignore the added members. The exit codes are unchanged: a warning from the runtime step leaves an applied upgrade at 0.
No record type, record format or generated document changes.

## Hand-offs, reviews and failure routes (PROC-10)

No route, interface member or protocol type is added or changed. `GET /v1/goals/{id}` keeps its shape; a run's `flow` adds the
run's `attention`, and each step instance adds `failure` (a work step's failure route), `rerun`, `attemptsUsed`,
`attemptsAllowed`, `attempts` (each with its assignment, attempt, result and outcome), `reviews` (each review before hand-off
with its items, independence, role, subject and assignment), `attention` and `failed`. `GET /v1/goals/{id}/next` keeps
`vericairn.next/1`; for a run it adds `attention` and `held`, each step entry adds `review` and `attention`, a review before
hand-off is a step entry of its own whose `next` name is `<step> review`, and the route vocabulary adds `attention`.
`POST /v1/goals/{id}/step` returns the same `GoalStepResponse`; for required attention its `outcome` is `ask` with no
assignment, and its `step` is the step, or `the run` for the run's own attention. Older clients ignore the added members and
values. The SDK's and the MCP server's goal reads carry the same JSON; no tool is added. The state language's `step-route`
vocabulary adds `attention`.

`Vericairn.Records` adds public types and members:

- `Templates.FlowContract.FailureRoutes`; `Templates.FlowFailure` with `Stop`; `FlowStep.Failure`, an optional trailing
  parameter, and `FlowStep.FailureRoute`; and `FlowRole.Document`, an optional trailing parameter.
- `Estate.FlowRuns.ReviewRef`, `ReviewOf`, `ReviewRequirement`, `Consequential`, `Authors`, `Harness` and
  `ReviewerRefusals`; `FlowRuns.StepRef` gains an optional trailing `Review` parameter and `Work`, and its `ToString` adds
  `review` for a review's step.
- `Estate.FlowPosition.AttentionKinds`, `RetryBackoff`, `RetryBackoffLimit` and the records `AttemptState`,
  `PendingReview`, `Attention` and `Failure`; `StepInstance.Attempts`, `AttemptsUsed`, `AttemptsAllowed`, `Reviews`,
  `Attention`, `Failed` and `Rerun`; `ItemState.Requirement`; and `Position.RunAttention`. `Position.Find` finds a review's
  step by its step.
- `NextEligible.AttentionRoute`; `FlowStepRoute.Review`, an optional trailing parameter, `Name` and `Attention`;
  `FlowRunRoutes.Attention`, `Held` and `AttentionNext`. `NextEligible.RouteStates` and `FlowDispatching` add `attention`.
- `AgentRuns.StepAssignments`, the assignments whose runs count as one step's attempts.

Behavior changes to the runs that PROC-05 adds, each within record format 3. A run's attempts per step count every agent run
of a step's work across all of its assignments: a stale, reopened or rerun step's fresh assignment no longer starts the
count again, and the record type refuses a run beyond them. Findings and failures that send a run's work back count only
from principals who judge the step. A review that names no reviewer role clears an item only when it is by a principal of
the estate's `reviewer` role, an operator, or the contributor of a review assignment of that item; PROC-05 accepted any
principal other than the authors. A review in a role the flow marks `person` counts only from a person. A result naming
`unresolvedEffects` no longer closes its step, and stops it even when stale or reopened; the record type refuses an
`agent-run` intent on any assignment of that step while such a result is current (`operation.unreconciled`). The
consequential independent default and routed reviews apply to work steps only. A failure route that returns to an `effect`
or `exchange` step, and a failure route through a branch whose `returnTo` is a step after the failed one, are refused with
the flow. `goal next` and `goal step` hold a failed step's route, its backoff and the run's `limits.consecutiveFailures`; a
run started directly with `worker start` is not held by them. Record format remains 3; its addition, an assignment's
`step.review`, and these rules are in [record format 3](record-format-v3.md#hand-offs-reviews-and-failure-routes-092).

The generic record reader advances its implementation identity to `vericairn.records/readers/8`: an assignment's `step` and
a goal's `limits` change their schema explanations. Its hash changes; declarations pinned to an earlier implementation
require the ordinary explicit generator update before regeneration, and earlier golden fixtures remain retained. The
generator id and contract version 1 are unchanged.
