# Generated CLI reference: client outcomes

## Stored records awaiting companion recovery

When a record was stored but its companion publication was interrupted&#44; the daemon answers HTTP &#96;200&#96; with both &#96;written&#96; &#40;path&#44; id&#44; hash&#41; and a &#96;reconcile&#45;required&#96; problem&#46; The SDK preserves the receipt and recovery problem&#46; &#96;WriteOutcome&#46;Ok&#96; is false because companion publication remains incomplete&#59; the record is already stored&#46;

The CLI reports the same outcome through the embedded runtime and the daemon&#58; the receipt on standard output&#44; the recovery diagnostic on standard error&#44; and exit code &#96;2&#96;&#46; With &#96;&#45;&#45;json&#96;&#44; standard output remains the existing object with &#96;path&#96;&#44; &#96;id&#96; and &#96;hash&#96;&#59; the diagnostic is not added to that object&#46; Workbench shows the written identity and the recovery action&#46; MCP retains both &#96;written&#96; and &#96;problem&#96; in its JSON text&#44; with &#96;isError&#58; true&#96; to signal incomplete recovery&#44; never a refusal of the stored record&#46;

The batch journal retains the pending companion outputs&#46; Reopen the estate or run &#96;docs regenerate&#96; to recover them under the original identity&#59; never resubmit the stored record&#46; A genuine refusal still has no written receipt and keeps its existing status and diagnostic&#46; See [API outcomes](api-v1.md#outcomes-and-problems).

## Interrupted client requests

An interrupted client request writes its diagnostic to standard error&#46; It does not emit a replacement JSON result on standard output&#46; Existing command names&#44; options&#44; help and successful JSON responses are unchanged&#46;

| Outcome | Exit code | Diagnostic |
|---|---|---|
| `timed-out` | `124` | `error: The request timed out.` |
| `cancelled` | `130` | `error: The request was cancelled.` |
| `unavailable` | `2` | `error: The runtime is unavailable.` |

When an effect may have happened, the diagnostic also says: `The outcome is unknown; reconcile the original operation before retrying.`

A definite refusal keeps its diagnostic and exit code &#96;2&#96;&#59; it is never called unavailable or of unknown outcome&#46; Existing structured refusal responses keep their representation&#46;

Caller cancellation &#40;including Ctrl&#43;C for ordinary requests&#41; and a per&#45;operation deadline are separate outcomes&#46; Cancellation does not undo an effect&#44; and these clients do not retry it&#46; Foreground worker following still interprets Ctrl&#43;C as a request to stop the original run&#59; runtime serving still interprets it as a request to stop the service&#46; Their requests remain subject to deadlines&#46;

## Generation basis

Build commit: `3c7c31c8547e52173e388fc583e24cfd62689ac9`.

Generator: `vericairn.software/cli-client-outcomes/1`; implementation: `vericairn.software.companions/1`; source digest: `sha256:da60e7f17012bdfc621f3d22a3180cf8345a1efe066dbb23f4192ff28ac910c6`.

Input digest: `sha256:7ab95c1ff3bdeb03ac6d408f1d8193fa3e9c266e60d80ee9b1c4a326bd22c7d7`.

| Input | SHA-256 | Bytes |
|---|---|---|
| inputs&#47;commands&#46;json | `sha256:5fea44fcbd921956722aad7c64ef82e14d5ff0db15db4b5902ba05f95d67c0ca` | 173829 |
