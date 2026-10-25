# API compatibility in 0.8.4

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
