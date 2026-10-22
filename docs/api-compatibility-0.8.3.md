# API compatibility for 0.8.3: release planning additions

This page lists the CODE-08 public surface additions relative to 0.8.2. It is not a
package compatibility check or a publication claim. Other 0.8.3 changes may add their
own entries. The record format and existing release-submission admission checks are
unchanged. See [API v1](api-v1.md#release-planning-083-additions) and the
[software guide](software.md#withdrawing-a-candidate-and-moving-a-release-plan-083-additions)
for behavior and refusal handling.

## Members added to `IEstateApi`

| Added member | Work order |
| --- | --- |
| `ReleaseResponse ReleaseWithdraw(ReleaseWithdrawRequest request)` | CODE-08 |
| `Task<ReleaseResponse> ReleaseWithdrawAsync(ReleaseWithdrawRequest request, CancellationToken cancellationToken = default)` | CODE-08 |
| `WorkMovePlanResponse WorkMovePlan(string key, WorkMovePlanRequest request)` | CODE-08 |
| `Task<WorkMovePlanResponse> WorkMovePlanAsync(string key, WorkMovePlanRequest request, CancellationToken cancellationToken = default)` | CODE-08 |

The synchronous interface methods have default implementations returning an
`unsupported` problem; existing implementations can still load. The asynchronous
defaults follow the existing client-operation contract. `EstateService` implements
both synchronous operations, and `VericairnClient` and `CancellationBoundApi` expose
both synchronous and asynchronous counterparts. Cancellation after a write was sent
can leave its outcome unknown; inspect the returned or original identities before
retrying.

## Request and response types

| Public member | Meaning |
| --- | --- |
| `ReleaseWithdrawRequest(string Release, string By, string Expected, string Provider, string Reason, IReadOnlyDictionary<string, string>? Settings = null)` | Exact candidate, explicit draft provider, reason and optional provider settings. |
| `WorkMovePlanRequest(string By, string Expected, IReadOnlyDictionary<string, string> ExpectedWork, string? To = null, string? ExpectedTarget = null, string? Version = null)` | Exact source carrier and complete planned-member snapshot, with an existing destination carrier or a new source version. |
| `WorkMovePlanResponse(IReadOnlyList<RecordWritten> Written, Problem? Problem = null)` and `bool Ok` | All stored receipts, including any partial batch, and its refusal. |
| `ReleaseObserveRequest.Withdrawal { get; init; }` (`string?`) | Optional deletion-intent id on the existing observe route. The existing constructor and deconstruction signatures are retained. |
| `ReleaseFlow.DeleteDraftAction` | Constant `delete-release-draft` for the existing operation record type. |

The new positional records also expose their named properties and generated record
members. `ReleaseResponse` is reused unchanged: `Release` identifies the withdrawn
revision and `Operation` the deletion intent. No provider deletion interface is
added. Observation uses the existing release provider contract.

## API and command additions

| Surface | Addition |
| --- | --- |
| API v1 | `POST /v1/releases/withdraw`, `POST /v1/work/{key}/move-plan` |
| Existing API v1 observe route | Optional `withdrawal` field in `ReleaseObserveRequest` |
| CLI | `release withdraw`, `work move-plan` |
| Existing CLI observation | `release observe --withdrawal <intent-id>` |

Both commands validate the full proposed record batch before any record is stored.
The planned-work move refuses changed membership as well as stale hashes. The
withdrawal records intent only; the operator's deletion and dispatch record precede
observation. No MCP authoring tool is added by this change.
