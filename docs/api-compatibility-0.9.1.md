# API compatibility in 0.9.1

## IEstateApi.RunPrepare (FIX-86)

`IEstateApi` adds `RunPrepare(RunStartRequest)` and its cancellable counterpart
`RunPrepareAsync(RunStartRequest, CancellationToken)`, both with default
implementations. The synchronous default returns a refused `RunResponse` with status
`not-started` and an `unsupported` problem; the asynchronous one bridges to it like every
other operation. An existing implementer therefore compiles and keeps its behavior, and
answers that preparing a workspace alone is unavailable. `EstateService`, `VericairnClient`
and `CancellationBoundApi` implement both. No interface member is added without a default
implementation.

`POST /v1/runs/prepare` takes the existing `RunStartRequest` and answers with the existing
`RunResponse`, adding no contract type or field. A prepared workspace answers with status
`prepared`, `ok` true and its location on the runtime's machine in `workspace`, with no
operation and no cap; a refusal answers as a refused run start does. The route acts on the
runtime's own machine, so it is refused over the local network, as `POST /v1/runs` is;
on loopback its `by` is the signing device's principal. `ClientOperation` gives
`RunPrepare` the fifteen-minute deadline of `RunStart`. Earlier servers have no such route,
and a client calling one receives its not-found answer.

## Workspace setup before dispatch (FIX-86)

No contract, route, response field or interface member changes. A workspace setup command
configured on the runtime's machine runs before a run's intent, so `POST /v1/runs` and an
agent's `POST /v1/goals/{id}/step` can refuse for it before anything is recorded: when it
fails, times out or cannot be observed to its end, or when it leaves a new link or a change
outside the assignment's permitted changes. A run's intent then holds one more `evidence`
entry, with the command's status, exit code and duration and never its command,
environment or output. Readers of the intent's evidence treat it as any other summary.
`WorkerConfigurationStore` adds `ReadSetup`, `ListSetup`, `WriteSetup`, `ClearSetup` and
`SetupCommand`, with the `WorkspaceSetupConfiguration` and `WorkspaceSetupEntry` records;
they are additions to a class, not an interface. Unchanged runs record exactly what they did.

## Clearer refusal of an unregistered or altered workspace (FIX-86)

No contract changes. A directory at a run's workspace location that the worker did not
prepare and register, or a registered workspace that was altered, is refused with its path
and the reason, where 0.9.0 could answer that a scope entry was not a regular file. The
refusal's code and its place, before anything is recorded, are unchanged; only its message
differs.

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
[record format 3](record-format-v3.md#runs-of-a-flow-091).

The generic record reader advances its implementation identity to `vericairn.records/readers/6`: the declared `provenance`
field of results and operations and an assignment's `step` change their schema explanations. Its hash changes;
declarations pinned to an earlier implementation require the ordinary explicit generator update before regeneration, and
earlier golden fixtures remain retained. The generator id and contract version 1 are unchanged.
