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

## A reconciliation settles operations that share an identity (FIX-100)

No route, interface member or protocol type is added or changed. A `reconciliation` whose subject is an operation and whose
`kept` and `withdrawn` records are not revisions of that subject now settles operations recorded independently under one
identity: each named record must be an operation under the subject's identity and the latest revision of its own operation,
or the finding `reconciliation.identity` is an error; it is accepted only from the operator or under the `local-engineering`
scope (`reconciliation.author`) and only when signed by an admitted device (`signature.required`), whatever the charter's
signature policy. The withdrawn operations then read as superseded by the kept one. Before, such a reconciliation was
refused with `reconciliation.successor`. Reconciliations of revisions of one record are unchanged.
`EstateStatus.ReconcileCodes` adds `operation.duplicate-identity`, so status's `reconcile` list, `estate reconcile` and the
required-attention incidents include it. Record format remains 3; the reconciliation type's summary and field descriptions,
and with them the generic record reader's identity, are unchanged.

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

## The event journal and the API stream (WATCH-05)

`IEstateApi` adds `Events(after, filter, principal, wait)` and `EventGet(key, principal)`, with their cancellable counterparts
`EventsAsync` and `EventGetAsync`; each has a default implementation, so existing implementers stay source-compatible, and
`CancellationBoundApi` and the SDK's `VericairnClient` implement all four. `Events` has a one-minute deadline, since a long
poll waits up to 25 seconds. The daemon adds `GET /v1/events`, which answers with server-sent events when the request accepts
`text/event-stream` and otherwise as a long poll, and `GET /v1/events/{key}`; both are reads, with a request scope and no
write ledger. The protocol adds `EventEnvelope` (format `vericairn.event/1`), `EventReference`, `EventFilter`,
`EventsResponse`, `EventStreamItem` and `EventVocabulary`; the OpenAPI document adds the two routes and the schemas
`EventEnvelope`, `EventReference` and `EventsResponse`. The SDK adds `VericairnClient.StreamEventsAsync` and
`WatchEventsAsync`, which return `IAsyncEnumerable<EventStreamItem>`. The command line adds `events watch`; no MCP tool or
notification is added (`MCP-05` adds those).

`StatusResponse`, `ProgressResponse`, `AttentionResponse`, `AsksResponse` and `GoalResponse` add the optional `EventsCursor`
property, serialized as `eventsCursor` only when the runtime keeps a journal, which the daemon does and the embedded runtime
does not. It sits beside the canonical JSON, which is unchanged, so the status document stays the same on every machine. These
are positional records, so the property takes part in their equality, and each read's cursor differs: compare their `Text`
and `Json` instead. Older clients ignore the property and older servers omit it.

`EventBatch.Closed` is true when the journal closed under a reader; a long poll then answers 503 with `unavailable`, and a stream
ends. A state read whose derivation fails answers without `eventsCursor` instead of failing. `Vericairn.Runtime` adds
`EstateService.EnableEvents(directory, time, capacity)`, which the daemon calls before it serves,
`EventsEnabled`, `DeriveEvents()`, `RunEventDerivationAsync(tick, log, cancellationToken)`, `OpenEvents(after, filter,
principal, device, devicePublicKey, profile)` and `EventByKeyFor(key, principal, device, devicePublicKey, profile)`, and in
`Vericairn.Runtime.Events` the types `EventSubscription` and `EventBatch`. `DaemonHost` keeps the journal in `events.db`
beside the live store, and the key that names subjects that are not records in `events.key`, kept across epochs, both
owner-only from creation, and runs the derivation for as long as it serves. A state read through the daemon now
also derives what that read shows and may write the journal; it writes no record. No record type, record format or
generated document changes.

## Network MCP over Streamable HTTP (MCP-03)

No route, OpenAPI operation, request or response type of API version 1 is added or changed. The daemon serves the Model
Context Protocol over Streamable HTTP at `/mcp` on its loopback and local-network listeners when the operator turned it on,
outside the API's route table; each tool call runs the API's own route in the daemon's process for the requesting device,
with that route's request scope. A message to `/mcp` holds at most 1 MiB, refused before authentication, and a network
write is kept in `local-network-writes.jsonl` on loopback as on the local network. `DaemonEndpoint` gains the optional
`Mcp`, serialized as `mcp` (the path served) only while it is on; older clients ignore it and older daemons omit it.

`Vericairn.Daemon` now depends on `Vericairn.Mcp`. `DaemonOptions` adds `Mcp` and `McpAllowedOrigins`, which override the
runtime settings when set. `RuntimeSettings` adds the init-only `Mcp` and `AllowedOrigins` and the static
`TryNormalizeOrigin`; `runtime.json` gains `mcp` and `allowedOrigins`, written on every save, and an older build ignores
them. A file whose `allowedOrigins` names something other than an origin is refused as unreadable settings.

`Vericairn.Runtime` adds `DeviceSigning` and `DeviceSigningMode` (`Read`, `Prepare`, `Sign`), the overload
`EstateService.ForDeviceRequest(device, publicKey, profile, signing)`, and `EstateService.ReadStandings(devices, principals)`, which
returns a `DeviceStandingsRead`: each device's standing and each principal's kind from one reading of the records. Under a
`DeviceSigning`, no key on the runtime's machine signs and no record is written unsigned: an operation writes nothing,
prepares the one record it authors for the requesting device, or writes that record only with the device's signature over
the plan's exact record, re-authored from the same request with the plan's id and time; every other store is refused.
An attempt's `startedAt` and a completed progress phase's `completedAt` state the plan's time in both steps. Prepared
records share one store with `POST /v1/records/prepare`, and from 0.9.2 one device holds at most 32 of them at once,
answered `unavailable` beyond that; the store's limit of 256 is unchanged. `PrepareRecord` otherwise behaves as before.

`Vericairn.Mcp` adds, each additive: `McpServer.ProtocolVersions` and `McpServer.Instructions` (init-only),
`McpServer.NetworkDescription` and `McpServer.RespondAsync(id, method, parameters, cancellationToken)`;
`ToolRegistry.ForNetwork(api, network)`, `ToolRegistry.Network`, `ToolRegistry.NetworkWrites`,
`ToolRegistry.OfferedOverNetwork(tool)`, `ToolRegistry.Describe(tool)`, the `CallAsync(name, arguments, meta,
cancellationToken)` overload and the constants `PrepareMeta`, `PlanMeta` and `SignatureMeta`; `ToolCallResult.Meta`;
and the types `IMcpNetworkSession` and `McpSigning`. `tools/call` now refuses a `_meta` that is not an object and returns
a result's `_meta` when it has one; over standard input and output `_meta` is not read, and every stdio answer is unchanged.

The command line adds `runtime configure --mcp on|off` and `--mcp-origin`, whose `--json` output adds `mcp`,
`allowedOrigins` and `mcpServed`, and keeps the other settings when one changes; `runtime status` adds the `mcp` line and,
in `--json`, `mcp`; and `mcp serve --connection`, whose bridge signs only a write's prepared record and only after binding
the call's arguments, author, subject, type, time and supersession; fields the runtime computes, such as a document
review's subject and checks or a result's `progressClaim`, are signed on trust in that runtime. The SDK's additions for
the bridge are internal. Record format remains 3;
no record type changes. `AGENTS.md` gains one paragraph on reaching the estate from another machine.

## Preparing a documentation set (DOCS-11)

No route, interface member, protocol type, shipped command or record type is added or changed. `vericairn-docs`, the build
tooling that no distributable ships, adds `assemble`, `insert`, `freeze`, `index` and `prepare`. `generate` and `compare` keep
their options, outputs and refusals: `generate` still writes into any directory. Each new command takes a set whose
directory is named for its version, and `assemble`, unless it keeps the references, refuses one that is not the build's.

A set's manifest, references and notes keep their formats. `freeze` writes `docs/releases/frozen-sha256.json` in its existing
format, `vericairn.frozen-docs/1`, indented by two spaces with one final line break. It adds a set's entries after the
earlier sets and changes none of them. `index` changes `docs/releases/README.md` only by adding a paragraph under its
documentation sets heading. The frozen-set test in the command line's suite no longer lists the frozen versions. It
requires every set the release index links, every set directory except the build's own version until it is frozen, and
each set before 0.6.0 to stay indexed, so freezing a set no longer edits the test.

## Stops and their surfaces (PROC-06)

No route or interface member is added. `GET /v1/goals/{id}` keeps its shape; a run's `flow` adds `stops`, and each step
instance adds `stop` (why a stopped step waits: `person`, `reserved`, `attention` or `accept`) and `command` (for the reserved
step that is next, its exact command; otherwise null). Each stop has `kind`, `name`, `title`, `step` (`key`, `instance`,
`review`), `principal`, `principalName`, `reason`, `delivery` (`due`, `delivered`, `closed`, `queued` or `outside`), `record`,
`act` and `command`. `GET /v1/goals/{id}/next` keeps `vericairn.next/1`; its `flow` adds the same `stops`, the route vocabulary
adds `request`, `requested`, `closed` and `queued`, and each route adds `request`, the operator request it names.
`POST /v1/goals/{id}/step` returns the same `GoalStepResponse`; its outcome may be `request`, and it adds the init-only `Request`,
serialized as `request` only when this call wrote an operator request. The OpenAPI schema `GoalStepResponse` adds `request`.
`GET /v1/status` adds `runs` to its canonical JSON when the estate has a run of a flow that is active, paused, budget-limited or
blocked: each with its `goal`, `pin`, `asOf`, `available`, `unavailable`, `done`, `steps`, `complete`, `next` and `stops`. Its
text adds a `Runs:` section when there is one. The operator request queue, in `GET /v1/operator/requests`, status's
`operatorRequests` and required attention, delivers a request that names a run's step (`flowStep`) only while it is due. It adds
`withdrawn` only when a run no longer waits on an open request: each such request with `id`, `revision`, `hash`, `state`
(`settled` or `withdrawn`) and `reason`. Its text adds the step's command to a delivered step's request, and a line for each open
request that is not delivered. An estate without runs therefore keeps its earlier canonical status bytes, and the `statusHash` a
person's observation pins. Canonical status reads no clock: a run, its stops, and the delivery of its requests in
`operatorRequests` and in status's attention are derived as of the newest record (`asOf`), so the document is byte-identical
whenever and wherever it is read. Human status, `GET /v1/goals/{id}`, `/next`, `GET /v1/operator/requests`, `GET /v1/attention`
and progress's attention read a run now, so a due time, backoff or watch limit shows there at once, and in canonical status once a
later record is written. A hand-over package derives a run's position as of the newest record too. Canonical status and a
hand-over package trust each record's claimed time, so their stops and steps carry `command` null; only the live reads show a
reserved step's command. A step request's text in the queue, in `operator list` and in attention shows its checked command first
and labels its recommendation and other prose as the requester's, not checked; a client that cannot read the queue, from an older
runtime, shows no step request as open. `operator list` (`GET /v1/operator/actions`) reads the runtime's clock, and its
operator-request items add `stepCommand` for a run's step request. `request` events follow the queue's delivery: a run's request is `open` while it is delivered, `withdrawn` or `settled` otherwise, with category
`information` once it is not delivered. Older clients ignore the
added members and values; older servers omit them, which is not evidence that a run has no stops. The SDK's and the MCP
server's goal, next, status and request reads carry the same JSON; no tool is added and no description changes.

The state language's `step-route` vocabulary adds `request`, `requested`, `closed` and `queued`, and it adds the `flow-stop`
and `flow-delivery` vocabularies. `Vericairn.Presentation` adds `StopRow` and `FlowRunRow`, `GoalPresentation.Stops`, and
`EstateView.Runs`, `Stops` and `StopsAsOf`, and `FlowRunRow.AsOf`. Workbench's `ShellViewModel` adds `Stops`, `HasStops` and
`StopsAsOf`, its `GoalsWorkspace` `Stops` and `HasStops`, and Remote's `RemoteViewModel` `Stops`, `HasStops` and `StopsAsOf`. `Vericairn.Protocol`'s attention derivation reads the request queue when the API offers it, and
derives it from the records otherwise.

`Vericairn.Records` adds public types and members:

- `Estate.FlowStops`: `Kinds`, `Deliveries`, `RequestStates` and `PerDelivery`; the records `RunStop`, `FlowStepReference` and
  `StepRequest`; and `StepOf`, `CommandOf`, `Requests`, `Classify`, `RequestTo`, `Why`, `RecordedTime`, `Of`, `Deliver`,
  `Runs`, `RenderJson` and `AppendText` (each with an optional `commands`), `RunsJson` and `RunsText`.
- `Estate.FlowPosition.HoldsReservedAct` and `IsReservedAct`; `StepInstance.Stop` and `Command`; `Position.Stops`, `At` and
  `Reaching`; and an optional `commands` on `RenderText` and `RenderJson`. `FlowPosition` is now `partial`.
- `Estate.EstateStatus.Render` and `Estate.GoalView.RenderText` and `RenderJson` overloads at a time, the goal view's with an
  optional `commands`; `Estate.OperatorActions.Derive` at a time.
- `Estate.GoalPredicates.EvaluatePredicate` and `Matching` overloads taking the records a rule admits.
- `Estate.OperatorRequests.Held`, `Waiting`, `WaitingOf`, and `Derive` and `RenderText` overloads at a time; `Open` now
  leaves out a run's request that is not due.
- `NextEligible.Route.Request`, an optional trailing parameter; `NextEligible.RouteStates` adds `request`, `requested`,
  `closed` and `queued`, and `FlowDispatching` adds `request`.
- `Templates.FlowContract.Placeholders`.

Behavior changes to the runs that PROC-05 and PROC-10 add, each within record format 3. A flow's reserved step must declare its
`action`, the exact command the operator runs, and its `done` predicates (`flow.reserved`), and the command's placeholders must
be bindings of the run (`flow.binding`), and its done predicates name no `state` (`flow.reserved`); a flow content that lacks
them no longer validates. A reserved step's done predicates and record items count a record only when its signatures are
authentic and its author was a person with the operator role when it was made (`FlowPosition.IsReservedAct`), and a result only
on the same terms: a reserved step that an agent's record, an unsigned record or a record made before its author became an
operator satisfied under PROC-05 is no longer done, and an agent's attestation item of a reserved step is `missing` rather than
`unproven`. A predicate evaluated with a rule that admits records never holds by a `state`. `Patterns.Slug` and the predicate
binding pattern now end at the value's last character, refusing a value with a final newline. A reserved step whose input changed after it was done shows `stopped`, waiting on the
operator again, rather than `stale`. A reserved step whose command cannot be bound from the records shows `blocked`. `goal step`
records a reserved step's operator request instead of answering `none`, and queues a fourth stop to one person across every run.
A request counts as closed only when its addressed operator's own authentic close closes it, and is delivered only when its
author takes the run's steps and it is put to a person who holds the act. Record format
remains 3; its addition, an operator request's `flowStep`, is in
[record format 3](record-format-v3.md#stops-092). The generic record reader stays `vericairn.records/readers/8`, whose
explanation of `operator-request` now includes `flowStep`.

## Security reports before a release (OPS-11)

Two routes are added, each with its protocol types and an `IEstateApi` member with a default implementation, so existing
implementers stay source-compatible. `POST /v1/security-reports/poll` takes `SecurityReportsPollRequest` (`by`) and
`POST /v1/security-reports/triage` takes `SecurityReportTriageRequest` (`by`, `advisory`, `repository`, `expected`,
`disposition`, `reason`); both return `SecurityReportsResponse` (`ok`, `message`, `reads` of `SecurityReportRead`, `reports`
of `SecurityReportRow`, `held`, `triage`, `problem`). Each row's `holds` says whether the advisory holds every release now:
a report (in `triage` or `draft` state, or an unknown state) untriaged at its current revision, or a closed or withdrawn
advisory no operator triage names at any revision; a published advisory never holds. `IEstateApi` adds `SecurityReportsPoll` and `SecurityReportsTriage`,
which throw `NotSupportedException` by default, with `SecurityReportsPollAsync` and `SecurityReportsTriageAsync`;
`CancellationBoundApi` and the SDK's `VericairnClient` implement all four. The daemon binds `by` to the authenticated
acting device, and a principal other than the operator receives the same `denied` problem whatever exists.
`ClientOperation.Deadline` gives `SecurityReportsPoll` and `ReleaseCheck` three minutes, since a check may now read GitHub;
`ReleaseCheck` had the 30-second default.

`ReleaseSubmitRequest` adds the init-only `SecurityReportsUnread` (JSON `securityReportsUnread`, default false): when true,
declared security reports are not read and a declaration alone answers with the single `release.security-reports`
objection. It only ever narrows what a caller learns. The MCP server's `release_submit_check` now checks the submission of
the principal its connection acts as, falling back to the `by` argument only on a connection that acts as none, and sets
`SecurityReportsUnread` unless that principal is the operator, so no other connection starts a read or sees which reports
hold. `release submit`, `POST /v1/releases/check` and `release_submit_check` are otherwise unchanged in shape. While a current product
declares `securityReports`, they read the declared repositories before admission, unless a complete read is less than a
minute old (a check stores nothing it read, so it leaves the live store unchanged), and add the objections `release.security-report` (an advisory that holds, named) and
`release.security-reports-unavailable` (a repository that could not be read completely, or a declared resource that is no
longer a repository on GitHub), or, for any principal other than
the operator, the single `release.security-reports`. These objections are never retained in a basis or written into an
intent. Without a declaration, nothing is read and no objection is added.

`AttentionGet` adds, for the operator only, notices of kind `incident` (an advisory that holds) or `information` (any other new or
changed advisory, for seven days) with subject type `security-report` (subject id
`<owner/name>:<GHSA identifier>`, target the repository resource) and of kind `failed check` with subject type
`security-reports` (subject id `<owner/name>`). They come from this machine's observations, so canonical `status --json`
and `GET /v1/status` are unchanged. Through WATCH-05's event journal they become attention events for the operator alone,
categories `incident`, `information` and `failed-check`, whose subjects are opaque keyed names rather than these ids; a
stored read asks the journal to derive at once. `GET /v1/providers` adds rows of kind `security-advisories`.

`Vericairn.Runtime` adds the contract `vericairn.provider.security-advisories/1`: `Providers.ISecurityAdvisoryReader`,
`SecurityAdvisory` (with `States`, `Severities`, `SettledStates` and `Validate`), `SecurityAdvisoryListing` and the in-memory
`MemorySecurityAdvisoryReader`; `ProviderRegistry.Register(ISecurityAdvisoryReader)`, `RegisterIfAbsent`,
`SecurityAdvisories(string)` and `SecurityAdvisoryReaders`; and `EstateService.SecurityReportsScheduled` and
`SecurityReportsInterval`, which the daemon uses to read every five minutes, 30 seconds after it starts.
`Vericairn.Providers.GitHub` adds `GitHubSecurityAdvisoryReader`, provider id `github-security-advisories`, registered by
the command line. It runs `gh api -i rate_limit` to confirm the token's `X-OAuth-Scopes` (`repo` or `repository_advisories:read`)
and the allowance, then reads the listing page by page with `gh api -i`, following each `Link` header's `after` cursor, each
page and the whole listing bounded. It follows a next page only on `api.github.com`'s own advisory listing, with exactly one
`after` cursor of base64 characters, which it decodes, checks and encodes again; anything else fails closed. `Vericairn.Records` adds `Types.Patterns.GitHubAdvisory` and `Types.SecurityReportRules` (`ProductField`,
`TriageKind`, `MaxRepositories`, `MaxReason`, `Dispositions`, `TriageFacts`, `Product`, `Triage` and `GitHubLocation`).
`ObservationType.Kinds` adds `security-triage`, and `ProductType.Fields` adds `securityReports`. `ResourceType` judges each
revision against the declarations and grants in force at its time: while a security-reports declaration names a resource,
only the operator changes its kind or location (`resource.security-reports-author`), and a repository's location moves only
by the operator or an `administer` grant, as before (`resource.location-author`). Validation reports, as information under
the same codes, a revision already in the estate that turns a resource into or out of a repository without that authority,
or that a later declaration would forbid; the write boundary refuses such a revision when it is new, as it does for
FIX-97's and FIX-98's rules. `ResourceType` adds the constants `LocationAuthorCode` and `SecurityReportsAuthorCode`, and
`SecurityReportRules` adds `DeclaresAt`. Record format remains 3; the record
additions are in [record format 3](record-format-v3.md#security-reports-092). The generic record reader's
`vericairn.records/readers/8`, unreleased and introduced for PROC-10, also explains the product's new field; its golden
fixtures include it. The command line adds `security-reports poll` and `security-reports triage`
([CLI](cli.md#vericairn-security-reports-092)).

## History checkpoints (ID-05)

No route, interface member of `IEstateApi` or protocol type is added or removed. Record format remains 3; it gains the record
type `history-checkpoint` ([record format 3](record-format-v3.md#history-checkpoints-092)), which `records describe`,
`records new` and the readable companions cover. An earlier format-3 reader reports it as `record.unknown-type`. The generic
record reader's implementation identity is unchanged: no existing rendition changes.

The canonical status JSON (`status --json`, `GET /v1/status`, the `estate_status` tool) adds a `history` member:
`{adopted, heads: [{id, hash}], witnessed, tail, problems: [{code, location, message}]}`. Its bytes, and so a new estate
observation's `statusHash`, change for every estate; observations already recorded keep their facts. The text status lists
standing history problems first. `estate check` (`GET /v1/check`) adds the check-time warning `history.deleted-in-git`, or
information when the clone is shallow; the canonical status does not include it. The command line adds `estate check
--expect <checkpoint>`, evaluated by the client against `Records("history-checkpoint")` and `Record(id)`, and the estate
bootstrap scripts accept `--expect <checkpoint>` before their other arguments.

`NewRecord` (`POST /v1/records`, `records new`) refuses `at` and `id` for a history checkpoint, and every write surface,
a device-prepared record included, refuses a checkpoint that does not follow exactly the copy's heads, list exactly the
records no head witnesses in objects held here, and state their count and set hash, or that anchors a record not present
unsigned with that hash or acknowledges the removal of a record that is not witnessed and absent. New findings, each an
error unless stated: `history.removed`, `history.altered`, `history.checkpoint-missing`, `history.checkpoint-invalid`,
`history.anchor-author`, `history.adoption-author`, `history.unanchored`, `history.expected-missing`,
`history.listing-mismatch` (a warning, only for a record that the root operator or a person holding administer signed, or
that is anchored with its present bytes; any other record a checkpoint lists with other bytes is `history.altered`, an agent's
own signed record included), `record.subject-not-counting` and
`record.reference-not-counting`; `history.deleted-in-git` is a warning and `history.expected` information.
`signature.required` now also reports an unsigned history checkpoint or device revision. Status lists the history errors,
`history.unanchored`, and, once the estate has adopted checkpoints, `signature.required`, first. `estate check --expect`
accepts a record id, a full content hash or at least sixteen hexadecimal digits of it. A device's recovery purpose is
read from its latest revision that counts.

What counts changes in every estate, without a format step. `EstateModel.OfType`, `Current`, `About`, `IsCurrent`,
`SupersededBy`, `RolesOf`, `HasRole` and `HasScope` read only records that count. `Latest` follows only counting
successors and returns the record it starts from when none follows it, which may itself not count; `CurrentPrincipal`
returns the newest counting revision of a principal's line, or null when none of its revisions counts, so a principal
record that does not count grants no role. `Records` and `ById` keep every record read, as evidence. A record that does not count is, among others, an unsigned record the signature
policy refuses (after adoption, any unanchored unsigned record under required signatures), an unsigned policy record,
checkpoint or device revision, or a witnessed record present with another hash. `SignatureChecks.Anchored` includes anchoring checkpoints'
anchors; a device revision changes the device's key or name only when its device signed it. A policy basis
holds only records that count, and its recorded time is the greatest `at` among them. An estate without checkpoints and
without signature errors, such as the first-party estate, reads as before.

`Vericairn.Records` adds, each additively: the namespace `Vericairn.Records.History` with `HistoryInventory`,
`InventoryEntry`, `InventoryObject`, `RecordSets`, `HistoryState`, `CheckpointDraft` and `SignedHistory` (`Of`,
`Adopted`, `Prepare`, `AuthoringRefusal`, `Expect`); `Types.HistoryCheckpointType`;
`EstateModel.CountingRecords` and `EstateModel.Counts`; `EstateLoader.Load(string, IReadOnlyList<RecordFile>,
Func<string, byte[]?>)`, which reads checkpoint inventories through the given object reader, and `EstateLoader.FileObjects`;
and `EstateStatus.HistoryProblems` and `EstateStatus.HistoryJson`. `Vericairn.Runtime` adds the default interface member
`ISourceAdapter.DeletedRecordFiles(string)` with the records `RecordDeletion` and `RecordDeletions`, implemented by the git
adapter; existing implementations compile unchanged and report the warning unavailable by returning null.
