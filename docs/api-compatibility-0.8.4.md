# API compatibility in 0.8.4

## Operator close (OPS-02)

| Added member | Purpose |
| --- | --- |
| `IEstateApi.OperatorCloseCandidates()` | Read finished, reviewed and unaccepted steps with exact work-order, result and review pins. The default returns an `unsupported` problem. |
| `IEstateApi.OperatorClose(OperatorCloseRequest)` | Sign the displayed selections as one validated batch. The default returns an `unsupported` problem. |
| `IEstateApi.OperatorCloseCandidatesAsync(CancellationToken = default)` | Default cancellable read bridge. |
| `IEstateApi.OperatorCloseAsync(OperatorCloseRequest, CancellationToken = default)` | Default cancellable write bridge. |
| `EstateService.OperatorCloseCandidates` / `OperatorClose` / `OperatorCloseAsync` | Derive the list, validate exact selections, sign and store their acceptances. The synchronous write also has a cancellation-token overload. |
| `VericairnClient.OperatorCloseCandidates` / `OperatorCloseCandidatesAsync` | Call `GET /v1/operator/close`. |
| `VericairnClient.OperatorClose` / `OperatorCloseAsync` | Call `POST /v1/operator/close`; the asynchronous operation uses the 15-minute operator-action deadline. |
| `CancellationBoundApi.OperatorCloseCandidates` / `OperatorCloseCandidatesAsync` / `OperatorClose` / `OperatorCloseAsync` | Forward the operation with the caller's cancellation lifetime. |
| `OperatorClosePin`, `OperatorCloseReview`, `OperatorCloseItem`, `OperatorCloseSelection` | Carry exact displayed evidence. `OperatorCloseItem.ToSelection()` copies its subject and review pins. |
| `OperatorCloseCandidatesResponse`, `OperatorCloseRequest`, `OperatorCloseResponse` | Carry the listing, explicit selection and acceptance receipts or refusal. Response `Ok` properties derive from `Problem`. |

All four new interface members have default implementations. Existing implementers remain
loadable; no new abstract member is required. Positional records expose their named properties,
constructors and generated record members. Existing constructors and record formats are unchanged.
The signing interface is unchanged and checks presence per signature where required.

The new routes return HTTP 200 with typed responses, including refusals. Callers inspect `Ok`
and `Problem`; a storage interruption can return `Written` receipts alongside a reconciliation
problem. Older servers have no fallback route. See [Operator actions](operator-actions.md#close-finished-steps-in-one-action)
for the selection contract and recovery behavior.

## Worker settlement recovery (FIX-65)

| Added member | Purpose |
| --- | --- |
| `IEstateApi.RunSettle(string run, RunSettleRequest request)` | Recover an exited run's unrecorded settlement from retained local observations. Its default implementation returns an `unsupported` problem. |
| `IEstateApi.RunSettleAsync(string run, RunSettleRequest request, CancellationToken cancellationToken = default)` | Default cancellable bridge, classified as a write. |
| `EstateService.RunSettle` | Apply the same authority as stopping the run and write its observed settlement. |
| `VericairnClient.RunSettle` / `RunSettleAsync` | Call `POST /v1/runs/{id}/settle`. |
| `CancellationBoundApi.RunSettle` / `RunSettleAsync` | Forward the operation with the caller's cancellation lifetime. |
| `RunSettleRequest(string By)` | Name the requesting principal. |
| `RunSettleResponse(string Run, string Status, string Message, bool Ok, RecordWritten? Operation = null, Problem? Problem = null)` | Return the settlement receipt or the reason recovery could not proceed. |
| `RunObservedState.Outcome`, `Scope`, `SettlementPrepared`, `MinuteTicks` | Add retained exit, final audit and accounting facts to the worker's local observation. Older snapshots remain readable. |

Both new interface members have default implementations; existing implementations
remain loadable. The positional request and response records also expose their
named properties and generated record members. Existing constructors and record
formats are unchanged.

The new retained recovery fields are local storage internals, excluded from the
run inspector's metadata projection. Existing inspector bytes and hashes remain
unchanged; the settlement and run API carry the recorded usage and audit facts.

The API returns HTTP 200 with the response, including refusals. Callers inspect
`Ok` and `Problem`; codes include `in-flight`, `already-settled`, `unresolved`,
`not-found`, `refused` and `denied`. A successful `Operation` is the settlement
revision. Cancellation after sending a write can leave its outcome unknown;
inspect the original run before retrying. An older server has no fallback route.

`EstateService.Open` now attempts recovery after judging interrupted batches.
`OpenReadOnly` does not recover or write. Startup uses the original run author's
current standing and ordinary record validation. Unreadable or insufficient
observations keep the reservation and appear with their reason in `worker runs`.
The command is `vericairn worker settle <run> --by <principal>`; `--json` preserves
both successful responses and refusals. See the [worker guide](software.md#coding-agents-the-worker-and-its-launchers).
