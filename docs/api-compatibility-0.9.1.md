# API compatibility in 0.9.1

## Runs of a flow (PROC-05)

No route, interface member or protocol type is added or changed. `GET /v1/goals/{id}` keeps its `GoalResponse` shape; for a
goal whose plan names a flow, its JSON adds a `flow` member with the run's position: the exact pin and the chain it extends,
whether it resolves here, the run's work order, `until`, every configured value with its source, `complete`, `next`, and each
step instance with its state, reasons, principal, assignment, result, review, outputs, inputs (as now and as pinned, with any
change after use), done verdicts and recovery. Goals without a flow have no `flow` member. `GET /v1/goals/{id}/next` keeps
`format` `vericairn.next/1`; for a run it adds `flow`, its `steps` are the flow's step instances (`key`, `item`, `kind`,
`status` with the step's derived state, `role`, `reasons`, `assignment` and `route` in the existing route shape), and `next`
names a step instance as `key` or `key[item]`. `POST /v1/goals/{id}/step` returns the same `GoalStepResponse`, whose `step` is
that step instance for a run. Older clients ignore the added members; older servers omit them, which is not evidence that a
goal is not a run.

The SDK's and the MCP server's goal reads carry the same JSON. The `goal_get` description now names a run's position; no tool
is added. The command line adds `goal propose --flow`, `--subject`, `--flow-option` and `--until`.

`Vericairn.Records` adds public types and members, each additive:

- `Templates.FlowContract`, the content contract's vocabularies and `IsFlow` and `ParseDuration`;
  `Templates.FlowDefinition`, the resolved flow with `Resolve(EstateModel, TemplatePin)` and
  `Resolve(TemplateDefinition, Func<TemplatePin, TemplateDefinition>)`; and the records `FlowRole`, `FlowInput`,
  `FlowOutput`, `FlowTrap`, `FlowBranch` and `FlowStep`.
- `TemplateDefinition.Kind` and `TemplateDefinition.IsFlow`; `TemplateCatalog.Resolve(TemplatePin,
  IEnumerable<TemplateDefinition>)`, which resolves installed or retained content without reading a working directory.
  `TemplateDefinition.Validate` and `Read` accept content of kind `flow` and validate it by the flow contract; document
  templates may state `"kind": "document"`, and content without a kind remains a document template.
- `Estate.FlowRuns`, a run's plan, configuration, policy-required steps and step references; `Estate.FlowPosition`, a
  run's derived position and its text and JSON; and `NextEligible.FlowRoutes`, `FlowRunRoutes`, `FlowStepRoute`,
  `FlowDispatching` and the `RenderText` and `RenderJson` overloads for a run's routes.
- `Routing.Assignments(EstateModel, RecordEnvelope, FlowRuns.StepRef?)` and `Routing.RoleOf(EstateModel,
  RecordEnvelope)`; `GoalPredicates.Matching`, the records a predicate counts.

`TemplateCatalog.All` no longer reads a `docs/templates` directory for a model without a root, such as a policy basis; such
a model's retained content comes from the basis. The policy basis's estate inventory retains `docs/templates` JSON content
beside its Markdown, so its digest differs from an earlier build's for an estate with templates. Record format remains 3; the
record additions are in [record format 3](record-format-v3.md#runs-of-a-flow-091).
