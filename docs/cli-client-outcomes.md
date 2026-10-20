# CLI client outcomes

The interrupted-request reference below is generated from `ClientFailure` by `vericairn-docs generate`.

## Stored records awaiting companion recovery

When a record was stored but its companion publication was interrupted, the daemon answers HTTP `200` with both `written` (path, id, hash) and a `reconcile-required` problem. The SDK preserves the receipt and recovery problem. `WriteOutcome.Ok` is false because companion publication remains incomplete; the record is already stored.

The CLI reports the same outcome through the embedded runtime and the daemon: the receipt on standard output, the recovery diagnostic on standard error, and exit code `2`. With `--json`, standard output remains the existing object with `path`, `id` and `hash`; the diagnostic is not added to that object. Workbench shows the written identity and the recovery action. MCP retains both `written` and `problem` in its JSON text, with `isError: true` to signal incomplete recovery, never a refusal of the stored record.

The batch journal retains the pending companion outputs. Reopen the estate or run `docs regenerate` to recover them under the original identity; never resubmit the stored record. A genuine refusal still has no written receipt and keeps its existing status and diagnostic. See [API outcomes](api-v1.md#outcomes-and-problems).

## Interrupted client requests

An interrupted client request writes its diagnostic to standard error. It does not emit a replacement JSON result on standard output. Existing command names, options, help and successful JSON responses are unchanged.

| Outcome | Exit code | Diagnostic |
| --- | --- | --- |
| `timed-out` | `124` | `error: The request timed out.` |
| `cancelled` | `130` | `error: The request was cancelled.` |
| `unavailable` | `2` | `error: The runtime is unavailable.` |

When an effect may have happened, the diagnostic also says: `The outcome is unknown; reconcile the original operation before retrying.`

A definite refusal keeps its diagnostic and exit code `2`; it is never called unavailable or of unknown outcome. Existing structured refusal responses keep their representation.

Caller cancellation (including Ctrl+C for ordinary requests) and a per-operation deadline are separate outcomes. Cancellation does not undo an effect, and these clients do not retry it. Foreground worker following still interprets Ctrl+C as a request to stop the original run; runtime serving still interprets it as a request to stop the service. Their requests remain subject to deadlines.
