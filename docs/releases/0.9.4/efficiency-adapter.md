# Local efficiency adapter

**Built in 0.9.0; disabled by default; covered by 0.9.0's release security review.**
Enabling it stays the operator's choice for each launcher. Implementation and
fixture coverage do not constitute live provider qualification.

## Selection and removal

The machine's `efficiency-adapter` worker setting names `local-lossless` and the
launchers allowed to use it. The operator sets it outside a worker run, separately
from model defaults and workspace placement. After the security review:

```sh
vericairn worker configure --adapter local-lossless --allow-launcher codex --allow-launcher claude-code
```

This grants permission, without selecting the adapter for any run. A run must
also select `--execution adapter=local-lossless` through the existing execution
choice. A brief, goal, roster or launcher choice cannot supply the machine
permission. Unknown adapters and unlisted launchers refuse before intent.
Omitting the choice keeps `disabled`. Operator configuration can be revoked with:

```sh
vericairn worker configure --adapter disabled
```

Revocation is read afresh before the next intent and dispatch. Stop an active run
before changing or removing its implementation. Its listener closes at the end
of supervision, and its isolated configuration returns to the direct setting.
The operator's harness configuration is never edited. Existing run context and
measurements remain available after disablement or removal; they are history,
not adapter configuration. Updating the adapter requires the normal product
update and renewed qualification/review of the changed implementation.

## Representation and protected material

The foundation contract is `vericairn.representation/1`, in Records. The separate
`LosslessAdapter` implementation is in Runtime. Neither needs another package,
tool, tokenizer or service.

The native request reader understands Responses `input` tool outputs and
Messages `messages` tool results. Human messages, system prompts, instructions,
tool/function definitions and every unclassified field are protected. Unknown
request shapes, invalid JSON, duplicate JSON properties and requests beyond the
parser's structural bounds pass unreduced. Explicit exact/mandatory markings and
error tool results also bypass reduction. Authentication headers never enter
the representation reader.

Only a tool result containing an explicit representation envelope can nominate
eligible content. Its object has exactly `format` and `blocks`; each block has
exactly `fidelity` (`exact` or `eligible`), `text` and `original` (the existing
context-retrieval pin). A producer must mark instructions, brief content, code
that must be read verbatim, quoted originals, hashes, signatures and uncertain
content `exact`. Eligibility is also checked independently: the pin and UTF-8
text must match an optional document original already retained in this run's
context. Required originals, records, unknown pins and altered text cannot be
made eligible by a label. Structured content with exact, instruction,
authentication, credential, hash or signature fields bypasses reduction.

This is a deliberately narrow input contract. Ordinary tool output and current
brief renderings are not automatically relabelled as eligible. Selecting the
adapter does not promise a reduction on every request. No tool shipped with
Vericairn emits representation envelopes yet, so until a producer does, a run
through the adapter forwards every request unreduced and records zero eligible
bytes.

The adapter does two things:

- It replaces a sufficiently long repeated eligible block within the same
  envelope/request with a reference to its first block index (zero-based),
  retaining the original retrieval pin. It does not deduplicate across requests.
- It removes insignificant JSON whitespace outside strings in eligible object
  or array text. Property order, string bytes, escape spelling and number
  spelling remain unchanged. Invalid structured text is not normalized.

Changes are surgical replacements of eligible string spans. Protected wire bytes
retain their original whitespace and escape spelling. A transformation that
would enlarge the serialized request is discarded. There is no summarization,
paraphrase, content reordering, token estimation or additional source selection.

Every changed block retains its `original` pin. `context_read`, the existing
original API route and `vericairn context read <pin> --original` return the exact
original with the usual access check. If an optional working copy changes, the
runtime can recover that original from an adapter run's existing retained context
after verifying its immutable package hash, source revision and byte hash. It
never substitutes a newer revision. This fallback is local to a machine retaining
that run's context; exporting records alone does not export run snapshots.
`LosslessAdapter.Restore` restores received eligible blocks through those pins;
no copy of protected content is added to an adapter store.

## Route, failure and data handling

The worker owns one HTTP listener bound to **127.0.0.1 only**, with an assigned
port and a lifetime limited to the run. The upstream scheme, host, port and path
prefix come from the launcher's isolated provider configuration before intent.
There is no adapter endpoint option. Native Codex uses its built-in API provider
base only with a provisioned API-mode login; its fixed sign-in route refuses the
hook before intent. Claude Code uses its built-in API base through the run's
explicit settings. The hook changes only the run's Codex provider base URL or
Claude Code API-base setting. Unhookable providers refuse, without silently
starting direct.

Requests cannot choose a destination through their body, brief, target or
headers. Absolute request targets, path escapes and ambiguous HTTP framing
refuse. The forwarding Host is fixed, forwarding/routing headers are removed,
and upstream redirects are refused. No proxy discovery, redirect following,
optimizer routing or telemetry is used. TLS uses the fixed upstream host and
normal certificate validation. Request buffering is bounded to 64 MiB, headers
to 64 KiB and JSON nesting to 64 levels. Compressed bodies pass unreduced;
supported chunked request bodies are decoded only for HTTP framing. Responses,
including event streams, stream back without reduction.

Authentication lines are forwarded unchanged to that upstream. They exist only
in transient transport buffers: the adapter writes no headers or request/response
bodies to logs, records, files or measurements. It stores no key, imports no
credential and prints no transport exception text. Existing harness login
provisioning remains the launcher's responsibility. The adapter writes no
payload archive; originals remain in the context snapshot the worker already
retains. Loopback limits network reach, but does not authenticate other processes
on the same machine. Run isolation and the existing worker-user boundary still
apply.

If the listener cannot start, the hook is never applied and the harness starts
direct. If reduction fails on a request, the forwarding boundary sends its
original body once and disables reduction for subsequent requests. Classification
continues measuring eligible bytes when available. Reduction completes before
the only upstream connection is opened. After forwarding starts, an uncertain
transport outcome is returned as failure and is never retried by the adapter.
It cannot safely resend a request whose upstream might already have accepted it.
A worker/process failure follows ordinary run reconciliation; the adapter does
not restart a harness or recover a provider conversation. Native retry settings
are disabled for the hook, but live harness behavior remains part of qualification.

## Measurement

Record format stays 3. The immutable execution selection records the requested
adapter and, when engaged, `adapterAddress`: an HTTP authority at 127.0.0.1,
without a path or credential. Settlement's `executionObserved.adapter` records
`name`, `state`, `address`, `requests`, `fallbacks`, `eligibleBeforeBytes`,
`eligibleAfterBytes` and `tokens: unknown`. Counts sum classified eligible UTF-8
text per forwarded request, including reference text after reduction. They are
not whole-request byte counts, token savings, billing or model-fit claims.
Only completed request-body writes enter the counters; a partial send remains
uncertain and is never retried. Forwarding stops before the final snapshot, so
the retained settlement cannot miss a handler that completes during shutdown.

`engaged`, `direct-start-fallback` and `direct-request-fallback` distinguish the
route actually used. A startup fallback observes no requests, so zero counters
mean zero observed bytes, not a measurement of the direct conversation.
Unknown material is protected and contributes no eligible bytes. Worker run
detail shows live local counts and then the settlement. Status shows the
recorded counts, preserving its portable, record-only contract.
