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
