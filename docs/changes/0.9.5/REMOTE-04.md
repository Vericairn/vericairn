# REMOTE-04: Remote interventions, nodes and desktop tasks

## docs/operator-guide.md

### In "13. Workbench, the desktop", after the paragraph beginning "Communication in the Work menu"

> From 0.9.5 (REMOTE-04), **From Remote** in **Hand-over and resume** lists the phone's prepared tasks for the acting person
> on the runtime that received them. Open a task on that runtime's machine to inspect its exact subject, reason, draft and
> evidence. Workbench shows the source material it could read and identifies missing originals. Remote can refresh its list to
> observe the desktop opening separately from the subject's progress. Tasks expire after 30 days. The runtime keeps up to
> 32 tasks per person. **Remove selected handoff** in Workbench removes the task and its opening information. If a
> preparation response is lost, Remote keeps the task, draft and request identity across Back and restart.
>
> [Remote](remote.md#interventions-and-nodes) can request a pause of an active goal, a graceful cancellation of one run or a
> containment request with no additional grace before escalation. Review pins the exact operation and target. The phone
> signs the prepared record with the person's admitted presence key. A refused intervention explains why and offers
> inspection and a desktop handoff. The intervention list preserves each original and shows the receipt and effect observed.
> **Nodes** checks the direct
> connection separately from the serving runtime's observations of in-flight work.

## docs/api-v1.md

### In the Goals and Runs operation tables, after the goal and stop descriptions

> From 0.9.5 (REMOTE-04), an active goal read returns `activeStatusHash`. A reviewed pause may supply that value as
> `expectedActiveStatusHash` and use the exact goal revision's id. A run read returns each run's `operationHash`. A reviewed
> stop may supply that value as `expectedOperationHash` and use the exact run id. The runtime refuses when the reviewed basis
> changed before the write. `RunRow.operationRevision` identifies the exact operation record. A reviewed containment can
> also pin the earlier stop by its reason and tighten its grace.
>
> Remote uses `POST /v1/records/prepare` and `POST /v1/records/signed` for interventions in record format 3. The signed
> `goal-status` pause carries the reviewed `activeStatusHash`. A `run-stop-request` pins the exact operation and names the run,
> cancellation or containment, and the original request identity. The worker's stop facts are observed separately.
>
> `GET /v1/handoff/desktop` lists a person's retained desktop tasks, with `principal` identifying the authorized person.
> `POST /v1/handoff/desktop` prepares a task at an exact subject revision, with `by`, `requestId`, `subject`, `task`, `reason`,
> and optional `draft` and `evidence`. An admitted device prepares for its own principal. Repeating the same request identity
> and content from that device returns the original preparation. `POST /v1/handoff/desktop/open` observes the first opening
> signed by an admitted device of the same person over the serving runtime's local socket. Responses report the preparation,
> `preparedByDevice`, any observed opening and `openedByDevice`, and the subject state when it can be checked.
> `POST /v1/handoff/desktop/remove` requires the same local-socket proof of ownership.

## docs/api-compatibility-0.9.5.md

### With the 0.9.5 runtime and SDK additions

> REMOTE-04 adds revision checks to the existing pause and run-stop operations, via optional fields on `GoalResponse`,
> `GoalActionRequest`, `RunRow` and `RunStopRequest`. Existing callers can omit the new request fields. The four
> desktop-handoff operations and their SDK equivalents retain and read a person's desktop tasks on the serving runtime.
> Phone-signed interventions use the existing signed-record routes with the format-3 `run-stop-request` type and optional
> `goal-status.activeStatusHash` field.
