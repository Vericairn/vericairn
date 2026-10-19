# Generated CLI reference: client outcomes

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

Build commit: `9393a977a6b7d7f2b09a93cf5c087260e22dd784`.

Generator: `vericairn.software/cli-client-outcomes/1`; implementation: `vericairn.software.companions/1`; source digest: `sha256:c3997f1a7bfe9d21f850d7887766cc3159a696ef78174357f71746e10a71f55e`.

Input digest: `sha256:225dcf9024add1c7e5fd912cb47bc3e93c97f3f63e26a0d24e1e15e10a0ad6bd`.

| Input | SHA-256 | Bytes |
|---|---|---|
| inputs&#47;commands&#46;json | `sha256:9e1ca0f03bbc6b356c2871e9f692833555ec97404ebdb328328ea9d6cac4fd6b` | 87830 |
