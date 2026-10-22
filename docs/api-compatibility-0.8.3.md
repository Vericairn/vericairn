# API compatibility for 0.8.3

This page lists the public interface additions in the 0.8.3 development build, work order by work order.
It is an interface inventory, not a claim of publication or a completed package compatibility check against
the published 0.8.2 packages. The API remains version 1, and no record format or existing method signature
changes. Earlier interface changes remain documented in [API compatibility for 0.8.2](api-compatibility-0.8.2.md).

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
author named by `By`, like `POST /v1/records`. [API v1](api-v1.md#work-order-creation-083-development)
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

## Release submission check (FIX-55)

| Added member | Purpose |
| --- | --- |
| `IEstateApi.ReleaseCheck(ReleaseSubmitRequest)` | Return every admission objection without effects. |
| `IEstateApi.ReleaseCheckAsync(ReleaseSubmitRequest, CancellationToken = default)` | Default asynchronous bridge, classified as a read. |
| `VericairnClient.ReleaseCheck` / `ReleaseCheckAsync` | Call the dedicated `POST /v1/releases/check` route. |
| `CancellationBoundApi.ReleaseCheck` / `ReleaseCheckAsync` | Preserve read cancellation and lifetime behavior. |
| `EstateService.ReleaseCheck` | Execute the shared submission admission gate. |
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
Workbench offers **Check submission (nothing recorded)**. See [API v1](api-v1.md#release-submission-check-083-development).
