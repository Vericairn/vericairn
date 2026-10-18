# Generated CLI reference: client outcomes

Generated from `ClientFailure` by `docs/cli-reference-generator`.

## Interrupted client requests

An interrupted client request writes its diagnostic to standard error. It does not emit a replacement JSON result on standard output. Existing command names, options, help and successful JSON responses are unchanged.

| Outcome | Exit code | Diagnostic |
| --- | --- | --- |
| `timed-out` | `124` | `error: The request timed out.` |
| `cancelled` | `130` | `error: The request was cancelled.` |
| `unavailable` | `2` | `error: The runtime is unavailable.` |

When an effect may have happened, the diagnostic also says: `The outcome is unknown; reconcile the original operation before retrying.`

Caller cancellation (including Ctrl+C for ordinary requests) and a per-operation deadline are separate outcomes. Cancellation does not undo an effect, and these clients do not retry it. Foreground worker following still interprets Ctrl+C as a request to stop the original run; runtime serving still interprets it as a request to stop the service. Their requests remain subject to deadlines.
