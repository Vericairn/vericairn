# API compatibility for 0.8.3

This page lists the public interface additions in 0.8.3, work order by work order.
It is an interface inventory, not a claim of publication. From CI-04, each change's package job validates
the packages against the published 0.8.2 packages; the members added without a default implementation are
listed in `.github/api-compatibility/Vericairn.Protocol.xml`. The API remains version 1, and no record format
or existing method signature changes. FIX-60 changes no public interface. Earlier interface changes remain documented in [API compatibility for 0.8.2](api-compatibility-0.8.2.md).

## Published dependency release pins (FIX-52)

`IReleaseSource.VerifyAsync(ReleaseBuild, string platform, CancellationToken = default)` is additive.
Its default implementation returns a failed task naming the unsupported verification operation and
the offline `--from` alternative. Existing sources remain loadable and keep their `LatestAsync`
behavior; they do not silently claim archive verification. Both GitHub release-source entry points
implement the new operation. It uses the anonymous release-check transport rules, bounded metadata,
streamed archive hashing and a second tag read. No new interface member lacks a default implementation,
and FIX-52 adds no MCP tool or API route.

| Added public surface | Purpose |
| --- | --- |
| `ReleaseBuild(Version, Commit, Reads, Writes)`, `Identity`, `Parse`, `Platform` | Target build identity and its own format capabilities |
| `ReleaseArchive(Name, Platform, Hash)`, `FileName`, `WorkbenchFileName` | A canonical distributable and its published hash |
| `VerifiedRelease`, `ObservationBody`, `PageUrl`, `AssetUrl` | Immutable verified evidence, observation facts and canonical addresses; instances come from verification |
| `PublishedReleaseVerifier.VerifyAsync`, `FromDirectoryAsync`, `HashArchiveAsync`, size limits | Shared online/offline verification with no scratch writes |
| `VericairnReleaseObservation.Is`, `ReleaseBody` | Recognition and projection of validated flat dependency facts |
| `ProviderRegistry.VericairnReleases` | Explicit host selection of the anonymous dependency source |
| `EstateInitialization.Release`, `PreparedEstate.Release` | Optional verified dependency and its prepared observation; existing constructors and deconstruction remain unchanged |
| `EstateLoader.LoadPrepared` | Validate prepared records against manifest bytes before initialization writes |
| `EstateManifest.Release` | Optional exact observation reference in a release pin |
| `EstateUpgrade.Plan(EstateModel, RecordEnvelope)` | Plan against a verified observation or a published product release |
| `UpgradeRequest.From`, `UpgradeRequest.ClientBuild` | Optional runtime-local offline directory and invoking build identity; the existing constructor and deconstruction remain unchanged |

`POST /v1/estate/upgrade` retains its route and response shape. `release` in its response may now
identify a `vericairn-release` observation. Dependency verification requires the runtime and supplied
client identity to match the target build. A `check` observes without writing; an apply pins the
observation by hash and retains the existing forward migration and interrupted-manifest recovery.
The `From` directory is input only and never enters a record. Local-network apply remains refused.
See [the record-format guide](record-format.md#published-vericairn-dependency-observations-083)
for the additive observation kind and optional manifest reference, and [installation](install.md#offline-inputs)
for the offline evidence contract.

## Work-order creation (CODE-07)

### Members added to `IEstateApi`

`Vericairn.Protocol.IEstateApi` gains two members. Code that calls existing members is
unaffected. A type outside Vericairn that implements the interface must implement
`WorkNew` to compile against 0.8.3; an implementation compiled against 0.8.2 must be
updated and rebuilt. `WorkNewAsync` has a default implementation that calls `WorkNew`
with the existing cancellation and operation-lifetime behavior.

| Added member | Work order |
| --- | --- |
| `WriteOutcome WorkNew(WorkNewRequest request)` | CODE-07 |
| `Task<WriteOutcome> WorkNewAsync(WorkNewRequest request, CancellationToken cancellationToken = default)` | CODE-07 |

`WorkNewRequest` carries `By`, `Key`, `Title`, `Intent`, `Scope`, `ExitQuestion`,
`Deliverables`, `Kind`, `After` and `Inputs`. `Kind` defaults to `ordinary`, `Inputs`
defaults to an empty list and omitted `After` adds no dependencies. Input references
are `type:id` strings; dependencies are work-order record ids. Required body fields
are checked by the existing work-order validator, with the same refusal messages.

`VericairnClient` and `CancellationBoundApi` expose both members. The SDK calls
`POST /v1/work/new`, which returns the existing `WriteOutcome` and signs as the
author named by `By`, like `POST /v1/records`. [API v1](api-v1.md#work-order-creation-083)
describes the request and the command it matches.

## Daemon release diagnostics (FIX-62)

### Added diagnostic members

| Added member | Work order |
| --- | --- |
| `Vericairn.Protocol.IDaemonVersion` with `string? DaemonVersionWarning { get; }` | FIX-62 |
| `Vericairn.Sdk.VericairnClient.DaemonVersionWarning` | FIX-62 |
| `Vericairn.Protocol.CancellationBoundApi.DaemonVersionWarning` | FIX-62 |
| `Vericairn.Mcp.ToolCallResult.Warning { get; init; }` | FIX-62 |

The SDK client implements `IDaemonVersion`. Read `Health` or `HealthAsync` when connecting, then present
`DaemonVersionWarning` if it is non-null. It names the client and serving daemon releases and the
stop/start commands to run on the daemon's machine in the estate directory. Build metadata after `+`
does not make a release different; prerelease identifiers do. Another health read refreshes the
diagnostic. Before a health response, or when the releases match, the property is null. The
cancellation bridge forwards the diagnostic without owning the client or querying health itself.

The CLI presents the warning on standard error, including before an estate upgrade check or apply.
Workbench includes it in the connection description, and a local-network connection probe includes
it in its observation. Supported operations remain available and no daemon is stopped automatically.
MCP adds a separate text content item when `ToolCallResult.Warning` is present; the original result
text and error flag keep their meaning.

An unstructured HTTP 404 from a required operation now raises `ClientRefusedException` with the
operation, serving daemon release when known, client release and restart instructions. The SDK may
read health to identify the daemon after that refusal; it never retries the failed operation. The
HTTP status remains 404 and the outcome is known to be refused. Structured not-found write outcomes
keep their existing meaning. For a nullable lookup after a differing release was observed, the SDK
consults that daemon's OpenAPI routes: an absent route gets the missing-operation diagnostic, while
an advertised lookup still returns null for an absent resource. If the route description is unavailable,
the client preserves the nullable result rather than guessing; the release warning remains available.

## Release planning (CODE-08)

CODE-08's additions keep the record format and the existing release-submission admission checks unchanged. See [API v1](api-v1.md#release-planning-083) and the
[software guide](software.md#withdrawing-a-candidate-and-moving-a-release-plan-083)
for behavior and refusal handling.

### Members added to `IEstateApi`

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

### Request and response types

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

### API and command additions

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

## Release submission check (FIX-55)

| Member | Purpose |
| --- | --- |
| `IEstateApi.ReleaseCheck(ReleaseSubmitRequest)` | Return every admission objection without effects. |
| `IEstateApi.ReleaseCheckAsync(ReleaseSubmitRequest, CancellationToken = default)` | Default asynchronous bridge, classified as a read. |
| `VericairnClient.ReleaseCheck` / `ReleaseCheckAsync` | Call the dedicated `POST /v1/releases/check` route. |
| `CancellationBoundApi.ReleaseCheck` / `ReleaseCheckAsync` | Preserve read cancellation and lifetime behavior. |
| `EstateService.ReleaseCheck` | Execute the shared submission admission gate. |
| `EstateService.Standing` | Observe canonical device standing without refreshing live storage, including during check-request authentication. |
| `ReleaseSubmitRequest.ClientBuild { get; init; }` | Carry the client's full informational version, including its source commit. |
| `ReleaseResponse.Check { get; init; }` | Optional `ReleaseCheckDetails` for a check response. |
| `ReleaseCheckDetails(Commit, Expected, ClientBuild, RuntimeBuild, BasisBytes, Objections)` | Exact checked inputs, serialized basis size and objections. |
| `ReleaseObjection(Code, Location, Message, NextAction)` | Portable finding with its next action. |
| `ISourceAdapter.InspectRelease(string path)` | Read local clone cleanliness and default-branch identity without effects; the default implementation returns unavailable. |
| `ReleaseSourceObservation(bool Clean, string? DefaultBranch, string? DefaultHead)` | Source adapter's local observation. |
| `GitCommandAdapter.InspectRelease(string path)` | Inspect Git without fetching, checking out or refreshing its index. |
| `ReleaseWorkspace.CheckAsync(IEstateApi, string?, CancellationToken = default)` | Check the current Workbench release form. |
| `ShellViewModel.CheckReleaseAsync(ReleaseWorkspace?, CancellationToken = default)` | Run that check without a submission lifecycle or status refresh. |

External `IEstateApi` implementations must implement `ReleaseCheck` and rebuild; the asynchronous
member has a default implementation. Existing `ISourceAdapter` implementations still compile and
return unavailable until they implement inspection. An unavailable required source is an objection.
The positional constructors and deconstruction members of `ReleaseSubmitRequest` and `ReleaseResponse`
are unchanged. `ClientBuild` is additive on the wire, but format-3 check and submit now require it to
match the runtime's complete informational version and source commit. Format-1/2 callers that omit
it retain their prior compatibility behavior; supplied identities are checked in every format.

The route returns HTTP 200 with the existing `ReleaseResponse`: status `checked` or `check-refused`,
the ordinary `ok` and `problem`, and `check`. It is a read for presence, cancellation and write-ledger
purposes, including on authenticated local-network connections. Connection authentication and
principal binding still apply. There is no fallback to `/v1/releases/submit` on an older server.
The new MCP read tool is `release_submit_check`; the CLI option is `release submit --check`;
Workbench offers **Check submission (nothing recorded)**. See [API v1](api-v1.md#release-submission-check-083).

## Retained release-basis diagnostics (FIX-58)

| Changed member | Change |
| --- | --- |
| `ReleaseBasisResponse.Problem { get; init; }` | Added nullable `Problem`, naming every failed reference when a basis cannot resolve. Omitted from successful JSON responses. |
| `ReleaseBasisResponse.Evidence` | Now annotated `ReleaseBasisEvidence?`; null when resolution fails. |
| `ReleaseBasisResponse(string Hash, ReleaseBasisEvidence? Evidence)` and its generated `Deconstruct` | The evidence parameter's nullable annotation changes; the CLR types and two-argument constructor remain the same. |

`IEstateApi.ReleaseBasis`, `ReleaseBasisAsync`, `VericairnClient` and `CancellationBoundApi` keep
their signatures. Unknown hashes and objects without a supported basis format still return null
(HTTP 404). A retained basis that cannot resolve now returns `ReleaseBasisResponse` (HTTP 200)
with its hash, null evidence and a `release-basis-unavailable` problem. Its findings distinguish
missing references from bytes that do not match their hash, identify source files by repository,
revision, path and hash, and identify proof objects and records by their retained references.
The record-set digest check still covers signatures; a mismatch names that digest. No partial
evidence is returned. The retained `/1` and `/2` object formats do not change.

Callers must check `Evidence` and `Problem` before using a non-null response. Code compiled
against an earlier package can deserialize this response but must be updated to handle null
evidence; the previous null-response check alone no longer establishes that evidence resolved.
Recompilation with nullable analysis exposes accesses that need this check.

`release basis` exits 2 for both absence and resolution failure, with distinct messages naming
the requested hash. For resolution failure, `--json` preserves the API response and standard
error prints all findings. `release_basis_get` preserves the same response and sets `isError`
when evidence cannot resolve. Successful CLI and MCP reads retain their existing behavior.
See the [release guide](operator-guide.md#8-releases) and
[API v1](api-v1.md#release-basis-diagnostics-083) for the read contract.
