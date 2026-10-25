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

## Saved prompts (PROMPT-01)

`IEstateApi` adds `Prompts(PromptQuery)`, `PromptSave(PromptSaveRequest)`,
`PromptPreference(PromptPreferenceRequest)`, `PromptCopy(PromptCopyRequest)` and
`PromptUse(PromptUseRequest)`, plus each operation's `Async` counterpart with an
optional cancellation token. All ten interface members have default
implementations: synchronous methods return an explicit unsupported response;
asynchronous methods use the cancellable bridge. There is no new interface member
without a default implementation. `CancellationBoundApi` forwards all ten members.
`EstateService` implements the operations and `VericairnClient` exposes the same
synchronous and asynchronous routes. No existing API constructor is removed.

New protocol records are `PromptRow`, `PromptsResponse`, `PromptQuery`,
`PromptSaveRequest`, `PromptPreferenceRequest`, `PromptCopyRequest`,
`PromptSelection`, `PromptUseRequest` and `PromptUseResponse`. `PromptUseRequest`
has optional init property `Favor`; existing `AssignRequest` and `RunStartRequest`
gain optional init property `Prompt` while retaining their positional constructors
and deconstruction contracts. The new prompt request types refuse unknown fields.

`ProviderRegistry.Prompts` owns the runtime's `PromptCatalog`, whose registration
accepts immutable versioned contributions. `SoftwarePrompts.All` supplies the
software pack's review drafts. `ToolRegistry` adds `PromptsListAsync` and
`PromptGetAsync`; MCP advertises standard prompts with no change notifications.
Saved definitions, preferences and uses are additive within record format 3.

See [Saved prompts](prompts.md) for request fields, authority rules, receipts and
the explicit partial outcome when a use succeeds but its favorite write fails.
An older server has no prompt fallback route. Cancellation after a write was sent
can leave its outcome unknown; inspect the target before repeating the action.

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

## Guided decisions from operator answers (CODE-10)

| Added member | Purpose |
| --- | --- |
| `IEstateApi.DecisionFromAsk(DecisionNewRequest)` | Record an answer and the complete changed design document pins. Default returns an `unsupported` problem with no receipts. |
| `IEstateApi.DecisionFromAskAsync(DecisionNewRequest, CancellationToken)` | Default cancellable bridge, classified as a write. |
| `EstateService.DecisionFromAsk` | Validate the supplied answer, derive the change set, and store the signed decision and pins as one journaled batch. |
| `VericairnClient.DecisionFromAsk` / `DecisionFromAskAsync` | Call `POST /v1/decisions/new`. |
| `CancellationBoundApi.DecisionFromAsk` / `DecisionFromAskAsync` | Forward with the caller's cancellation lifetime. |
| `ISourceAdapter.InspectDesignChanges(string root, string baseCommit)` | Read the complete changed design files from an exact local base through the working tree. Default returns null (unsupported). |
| `GitCommandAdapter.InspectDesignChanges` | Observe local Git membership and exact current file hashes without fetching or writing. |
| `DesignChangeSet`, `DesignFileChange` | Carry the base, observed head, relative paths and file hashes. |
| `DecisionNewRequest`, `DecisionNewResponse` | Supplied answer and decision fields; decision and pin receipts, problem and completion status. |
| `DecisionContent.BlockedProseField` | Inspect prose against execution identities derived from recorded runs and qualification evidence; returns only the field name. |

Every new interface member has a default implementation. Existing implementations remain loadable; an unavailable adapter refuses the guided write. New positional records also expose their named properties and generated record members. Existing constructors are unchanged.

Guided decisions add optional `answer` and `changeSet` fields to decision bodies in record formats 2 and 3. `answer` holds `ask`, `hash` and `at`; `changeSet` holds `baseCommit`, `head` and `pins`, whose members hold `path`, `id` and `hash`. Both are required together. Existing decisions without these fields keep their validation rules. Older builds that do not know these fields cannot validate guided decision records; upgrade readers before sharing them.

The complete proposed decision and pin set passes ordinary estate validation before storage. Pins use the same preparation as `docs pin`. The storage journal can report a partial batch; receipts accompanied by `reconcile-required` must be inspected before retrying. No receipt is rolled back. An older API server has no fallback route; the SDK reports a missing operation. The [CLI contract](cli.md#vericairn-decisions-new) specifies response status and all request fields.

Decision companion implementation `vericairn.records/decisions/2` and generic record reader implementation `vericairn.records/readers/3` account for the added answer and change-set fields. Their implementation hashes change, so declarations pinned to older generator implementations need the ordinary explicit generator update before regeneration.
