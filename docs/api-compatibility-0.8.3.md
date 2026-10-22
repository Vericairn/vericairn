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
[API v1](api-v1.md#release-basis-diagnostics-083-development) for the read contract.
