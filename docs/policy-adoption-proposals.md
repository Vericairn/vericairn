# Release-entry and reference-coverage adoption proposals

Revision 1, October 18, 2026. Propose the two first-party policy bodies and exact register mappings for POLICY-04.

These are proposed definitions for the builder and POLICY-04. Adoption belongs to the operator's exact signed decision and authorized declarations after ESTATE-01 reconciliation. D18-4 is decision `01M54ZFDD1MG1GS49CQA56J6QW` at `sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc`; it selects the levels, builder ownership, 30-day exception limit and publication cohort. The adoption decision must also identify the accepted ESTATE-01 result and DESIGN-18 acceptance.

The placeholders below intentionally prevent these examples from being submitted as records. POLICY-04 supplies the adoption UTC instant and exact decision references after approving each canonical definition hash, calculated with only `basis` omitted. The decision requirements named `release-devlog-definition` and `register-coverage-definition` must approve `declare` for the respective policy keys and those hashes. No future publication identity or approval is assumed.

## Proposed release-devlog body

Declare after the format-3 upgrade and before the observed publication of 0.8.0. If declaration follows that publication, replace `after-declaration` with its exact `{type: "release", id, hash}` before approval. Later policy revisions preserve the first declaration boundary. The source directory contains Internal draft devlogs whose first retained revisions follow their exact publications; each current revision needs an independent ordinary review.

```json
{
  "key": "release-devlog",
  "title": "A reviewed devlog for every publication",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Each publication leaves a historical account before the product's next release is submitted.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {"type": "decision", "id": "<exact adoption decision id>", "hash": "<exact adoption decision hash>"},
      "requirement": "release-devlog-definition"
    }
  ],
  "state": "active",
  "appliesTo": {"kind": "estate"},
  "rules": [
    {
      "key": "entry",
      "kind": "vericairn.estate/release-entry",
      "version": 1,
      "parameters": {
        "product": "01M4TRNE1GWEAQ17M4QCDHXEZ5",
        "entryKind": "devlog",
        "source": "estate-draft/1",
        "directory": "docs/knowledge/devlog",
        "fromPublication": "after-declaration",
        "review": "independent"
      }
    }
  ],
  "level": "refuse",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

## Proposed register-coverage body

The product repository resource is `01M4TRNE1GB7CYB7DND8GBTTM1`. The register belongs to estate repository `01M4TRNF0RZE2VEZQYP9WK3GA4`, at `design/vocabulary.md`. The source adapter supplies the retained documentation-set members under `reference-inventory.json` in the product repository snapshot namespace, with the exact selected build commit. It must supply the inventory and all three generated reference files together. At status this is the estate-pinned release; at submission it is the candidate's target commit. POLICY-03 owns source collection and application wiring.

```json
{
  "key": "register-coverage",
  "title": "Define every generated reference concept",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Every public command, record type and API operation needs a defined concept in the register, with editorial review remaining separate.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {"type": "decision", "id": "<exact adoption decision id>", "hash": "<exact adoption decision hash>"},
      "requirement": "register-coverage-definition"
    }
  ],
  "state": "active",
  "appliesTo": {"kind": "estate"},
  "rules": [
    {
      "key": "references",
      "kind": "vericairn.software/reference-coverage",
      "version": 1,
      "parameters": {
        "product": "01M4TRNE1GWEAQ17M4QCDHXEZ5",
        "repository": "01M4TRNE1GB7CYB7DND8GBTTM1",
        "referenceManifest": "reference-inventory.json",
        "registerRepository": "01M4TRNF0RZE2VEZQYP9WK3GA4",
        "registerPath": "design/vocabulary.md",
        "categories": ["command", "record-type", "api-operation"]
      }
    }
  ],
  "level": "warn",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

## Proposed register mappings

The rows below propose exact identities from the generated references. POLICY-04 should compare them with the adopted build's retained inventory, review their concepts and add or remove mappings attributably. Each command, including a family command, has its own row. A family row covers only that exact invocation. Several operations share a term when they concern the same concept.

The proposal uses these term names that need current definitions in the register before their rows can count: `Checked repository change`, `Credential reference`, `Estate check`, `Estate status`, `Goal status`, `Reconciliation`, `Record recovery`, `Resource`, `Runtime connection`, and `Work status`. Each needs a meaning, non-equivalents and an owning document section. These proposals do not declare new record types or product operations. They give names to existing reference concepts. An undefined proposed term remains an unmet coverage finding; inserting its mapping alone cannot clear it. Existing term definitions and owning sections also need editorial review for the exact mapped capabilities.

## Generated reference coverage

| Category | Reference | Term |
|---|---|---|
| api-operation | `GET /v1/asks` | Ask |
| api-operation | `GET /v1/check` | Estate check |
| api-operation | `GET /v1/communication` | Communication setting |
| api-operation | `GET /v1/companions` | Generated |
| api-operation | `GET /v1/companions/compare` | Generated |
| api-operation | `GET /v1/companions/render` | Generated |
| api-operation | `GET /v1/companions/sources` | Generated |
| api-operation | `GET /v1/context/original/{pin}` | Context package |
| api-operation | `GET /v1/context/work/{key}` | Context package |
| api-operation | `GET /v1/context/work/{key}/brief` | Brief |
| api-operation | `GET /v1/context/{manifest}/sources/{source}` | Context package |
| api-operation | `GET /v1/decisions` | Decision |
| api-operation | `GET /v1/decisions/{id}` | Decision |
| api-operation | `GET /v1/devices` | Device |
| api-operation | `GET /v1/documents` | Living document |
| api-operation | `GET /v1/goals` | Goal |
| api-operation | `GET /v1/goals/{id}` | Goal |
| api-operation | `GET /v1/goals/{id}/next` | Next-eligible view |
| api-operation | `GET /v1/handoff` | Hand-over |
| api-operation | `GET /v1/health` | Runtime |
| api-operation | `GET /v1/launchers` | Launcher |
| api-operation | `GET /v1/objects` | Object |
| api-operation | `GET /v1/objects/verify` | Object |
| api-operation | `GET /v1/objects/{hash}` | Object |
| api-operation | `GET /v1/operator/actions` | Reserved action |
| api-operation | `GET /v1/progress` | Progress report |
| api-operation | `GET /v1/providers` | Provider |
| api-operation | `GET /v1/reconcile` | Reconciliation |
| api-operation | `GET /v1/records` | Record |
| api-operation | `GET /v1/records/{id}` | Record |
| api-operation | `GET /v1/recover/inspect` | Record recovery |
| api-operation | `GET /v1/releases` | Release |
| api-operation | `GET /v1/runs` | Agent run |
| api-operation | `GET /v1/runtime/identity` | Runtime |
| api-operation | `GET /v1/status` | Estate status |
| api-operation | `GET /v1/validate` | Estate check |
| api-operation | `GET /v1/work/{key}` | Work order |
| api-operation | `GET /v1/work/{key}/brief` | Brief |
| api-operation | `POST /v1/changes/merge` | Checked repository change |
| api-operation | `POST /v1/changes/observe` | Checked repository change |
| api-operation | `POST /v1/changes/open` | Checked repository change |
| api-operation | `POST /v1/changes/push` | Checked repository change |
| api-operation | `POST /v1/communication` | Communication setting |
| api-operation | `POST /v1/communication/delegations` | Communication delegation |
| api-operation | `POST /v1/devices` | Device |
| api-operation | `POST /v1/devices/admit` | Admission |
| api-operation | `POST /v1/devices/recovery` | Recovery device |
| api-operation | `POST /v1/devices/rename` | Device |
| api-operation | `POST /v1/devices/revoke` | Admission |
| api-operation | `POST /v1/devices/rotate` | Device |
| api-operation | `POST /v1/documents/{id}/reviews` | Clearance |
| api-operation | `POST /v1/estate/export` | Export |
| api-operation | `POST /v1/estate/upgrade` | Migration |
| api-operation | `POST /v1/goals` | Goal |
| api-operation | `POST /v1/goals/{id}/accept` | Acceptance |
| api-operation | `POST /v1/goals/{id}/activate` | Goal activation |
| api-operation | `POST /v1/goals/{id}/evaluate` | Evaluation |
| api-operation | `POST /v1/goals/{id}/pause` | Goal status |
| api-operation | `POST /v1/goals/{id}/resume` | Goal status |
| api-operation | `POST /v1/goals/{id}/step` | Step |
| api-operation | `POST /v1/goals/{id}/stop` | Goal status |
| api-operation | `POST /v1/harness-qualifications` | Harness qualification |
| api-operation | `POST /v1/objects` | Object |
| api-operation | `POST /v1/pairing/confirm` | Pairing |
| api-operation | `POST /v1/pairing/hello` | Pairing |
| api-operation | `POST /v1/pairings` | Pairing |
| api-operation | `POST /v1/records` | Record |
| api-operation | `POST /v1/releases/observe` | Release |
| api-operation | `POST /v1/releases/submit` | Release |
| api-operation | `POST /v1/resume` | Resume |
| api-operation | `POST /v1/runs` | Agent run |
| api-operation | `POST /v1/runs/{id}/stop` | Stop request |
| api-operation | `POST /v1/runtime/certificate` | Runtime |
| api-operation | `POST /v1/shutdown` | Daemon |
| api-operation | `POST /v1/status/observations` | Estate observation |
| api-operation | `POST /v1/work/amend` | Amendment |
| api-operation | `POST /v1/work/assign` | Assignment |
| api-operation | `POST /v1/work/attempt` | Attempt |
| api-operation | `POST /v1/work/progress` | Progress claim |
| api-operation | `POST /v1/work/result` | Result |
| api-operation | `POST /v1/work/takeover` | Work takeover |
| command | `vericairn ask` | Ask |
| command | `vericairn changes` | Checked repository change |
| command | `vericairn changes merge` | Checked repository change |
| command | `vericairn changes observe` | Checked repository change |
| command | `vericairn changes open` | Checked repository change |
| command | `vericairn changes push` | Checked repository change |
| command | `vericairn check` | Verification |
| command | `vericairn check observe` | Verification |
| command | `vericairn check run` | Verification |
| command | `vericairn communication` | Communication setting |
| command | `vericairn communication delegate` | Communication delegation |
| command | `vericairn communication reset` | Communication setting |
| command | `vericairn communication set` | Communication setting |
| command | `vericairn communication show` | Communication setting |
| command | `vericairn connections` | Runtime connection |
| command | `vericairn connections add` | Runtime connection |
| command | `vericairn connections list` | Runtime connection |
| command | `vericairn connections remove` | Runtime connection |
| command | `vericairn context` | Context package |
| command | `vericairn context catalogue` | Context package |
| command | `vericairn context prepare` | Context package |
| command | `vericairn context read` | Context package |
| command | `vericairn decisions` | Decision |
| command | `vericairn devices` | Device |
| command | `vericairn devices admit` | Admission |
| command | `vericairn devices create` | Device |
| command | `vericairn devices list` | Device |
| command | `vericairn devices pair` | Pairing |
| command | `vericairn devices recovery` | Recovery device |
| command | `vericairn devices rename` | Device |
| command | `vericairn devices revoke` | Admission |
| command | `vericairn devices rotate` | Device |
| command | `vericairn devices writes` | Admission |
| command | `vericairn docs` | Frozen documentation set |
| command | `vericairn docs check` | Frozen documentation set |
| command | `vericairn docs companions` | Generated |
| command | `vericairn docs declare` | Living document |
| command | `vericairn docs living` | Living document |
| command | `vericairn docs notes` | Notes window |
| command | `vericairn docs regenerate` | Generated |
| command | `vericairn docs render` | Generated |
| command | `vericairn docs review` | Clearance |
| command | `vericairn doctor` | Runtime |
| command | `vericairn estate` | Estate |
| command | `vericairn estate check` | Estate check |
| command | `vericairn estate export` | Export |
| command | `vericairn estate init` | First authority |
| command | `vericairn estate reconcile` | Reconciliation |
| command | `vericairn estate restore` | Inventory-bound restore |
| command | `vericairn estate upgrade` | Migration |
| command | `vericairn goal` | Goal |
| command | `vericairn goal accept` | Acceptance |
| command | `vericairn goal activate` | Goal activation |
| command | `vericairn goal custody-exception` | Custody exception |
| command | `vericairn goal evaluate` | Evaluation |
| command | `vericairn goal next` | Next-eligible view |
| command | `vericairn goal pause` | Goal status |
| command | `vericairn goal propose` | Goal |
| command | `vericairn goal resume` | Goal status |
| command | `vericairn goal show` | Goal |
| command | `vericairn goal step` | Step |
| command | `vericairn goal stop` | Goal status |
| command | `vericairn handoff` | Hand-over |
| command | `vericairn mcp` | MCP server |
| command | `vericairn mcp orientation` | Context package |
| command | `vericairn mcp serve` | MCP server |
| command | `vericairn objects` | Object |
| command | `vericairn objects list` | Object |
| command | `vericairn objects put` | Object |
| command | `vericairn objects verify` | Object |
| command | `vericairn operator` | Operator |
| command | `vericairn operator list` | Operator |
| command | `vericairn progress` | Progress report |
| command | `vericairn records` | Record |
| command | `vericairn records describe` | Record |
| command | `vericairn records format` | Record format |
| command | `vericairn records hash` | Content hash |
| command | `vericairn records new` | Record |
| command | `vericairn records show` | Record |
| command | `vericairn records validate` | Record |
| command | `vericairn recover` | Record recovery |
| command | `vericairn recover compare` | Record recovery |
| command | `vericairn recover inspect` | Record recovery |
| command | `vericairn release` | Release |
| command | `vericairn release list` | Release |
| command | `vericairn release observe` | Release |
| command | `vericairn release providers` | Provider |
| command | `vericairn release submit` | Release |
| command | `vericairn resume` | Resume |
| command | `vericairn runtime` | Runtime |
| command | `vericairn runtime configure` | Runtime |
| command | `vericairn runtime install` | Daemon |
| command | `vericairn runtime rotate` | Runtime |
| command | `vericairn runtime serve` | Daemon |
| command | `vericairn runtime start` | Daemon |
| command | `vericairn runtime status` | Runtime |
| command | `vericairn runtime stop` | Daemon |
| command | `vericairn runtime uninstall` | Daemon |
| command | `vericairn sources` | Source |
| command | `vericairn sources adopt` | Source |
| command | `vericairn sources clone` | Source |
| command | `vericairn sources inspect` | Source |
| command | `vericairn sources observe` | Observation |
| command | `vericairn status` | Estate status |
| command | `vericairn updates` | Release notice |
| command | `vericairn updates configure` | Release notice |
| command | `vericairn version` | Release |
| command | `vericairn work` | Work order |
| command | `vericairn work amend` | Amendment |
| command | `vericairn work assign` | Assignment |
| command | `vericairn work attempt` | Attempt |
| command | `vericairn work brief` | Brief |
| command | `vericairn work observe` | Estate observation |
| command | `vericairn work result` | Result |
| command | `vericairn work takeover` | Work takeover |
| command | `vericairn worker` | Worker |
| command | `vericairn worker configure` | Worker configuration |
| command | `vericairn worker launchers` | Launcher |
| command | `vericairn worker qualify` | Harness qualification |
| command | `vericairn worker run` | Agent run |
| command | `vericairn worker runs` | Agent run |
| command | `vericairn worker stop` | Stop request |
| record-type | `acceptance` | Acceptance |
| record-type | `admission` | Admission |
| record-type | `amendment` | Amendment |
| record-type | `ask` | Ask |
| record-type | `assignment` | Assignment |
| record-type | `attempt` | Attempt |
| record-type | `authority` | Authority |
| record-type | `communication-delegation` | Communication delegation |
| record-type | `communication-setting` | Communication setting |
| record-type | `credential-reference` | Credential reference |
| record-type | `custody-exception` | Custody exception |
| record-type | `decision` | Decision |
| record-type | `device` | Device |
| record-type | `discharge` | Discharge |
| record-type | `document-review` | Clearance |
| record-type | `document-revision` | Pin |
| record-type | `estate` | Estate |
| record-type | `estate-policy` | Estate policy |
| record-type | `goal` | Goal |
| record-type | `goal-status` | Goal status |
| record-type | `harness-qualification` | Harness qualification |
| record-type | `living-document` | Living document |
| record-type | `migration` | Migration |
| record-type | `observation` | Observation |
| record-type | `operation` | Operation |
| record-type | `policy-exception` | Policy exception |
| record-type | `possibility` | Possibility |
| record-type | `principal` | Principal |
| record-type | `product` | Product |
| record-type | `qualification` | Qualification |
| record-type | `reconciliation` | Reconciliation |
| record-type | `release` | Release |
| record-type | `resource` | Resource |
| record-type | `result` | Result |
| record-type | `retirement` | Retirement |
| record-type | `review` | Review |
| record-type | `roster` | Roster |
| record-type | `verification` | Verification |
| record-type | `work-order` | Work order |
| record-type | `work-progress` | Progress claim |
| record-type | `work-status` | Work status |
| record-type | `writing-exception` | Writing exception |
