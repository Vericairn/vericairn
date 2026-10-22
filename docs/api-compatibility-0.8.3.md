# API compatibility for 0.8.3: daemon release diagnostics

These are the FIX-62 additions planned for 0.8.3. Package compatibility against the published 0.8.2
packages has not yet been measured for this change. No API v1 route, record format or existing method
signature changes.

## Added diagnostic members

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
