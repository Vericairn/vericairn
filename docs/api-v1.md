# API version 1

One versioned API that every client uses. `Vericairn.Protocol` is its single definition: the operations
(`IEstateApi`), the request and response types, the `Problem` shape and the OpenAPI document
(`OpenApiDocument.Json`, served at `/openapi/v1.json`). The problem codes are those listed under Outcomes
and problems. The state language that clients present is the table in `Vericairn.Presentation`, which
tests hold to `docs/state-language.json` and to the runtime's own strings. `Vericairn.Runtime` implements
it once (`EstateService`); the daemon (`Vericairn.Daemon`) serves that implementation; the SDK
(`Vericairn.Sdk`) calls it; the CLI uses the daemon's when one serves the estate and embeds the runtime
otherwise. Authority is evaluated behind the API, never in front of it: a client presents and requests,
the runtime decides, and every response is what the runtime observed.

## One owner

One daemon process owns one open estate's live state (its SQLite live store and its key custody on that
machine). Clients never open the live store; they call the daemon. The daemon publishes where it serves
in an endpoint file beside the live store:

```
<VericairnHome>/estates/<estate id>/<key>/daemon.json
```

where `<key>` is `local` for a local-only estate and otherwise the first 16 hexadecimal digits of the
SHA-256 of the root's full path, with `kind` (`unix` or `pipe`), `address`, `processId`, `startedAt`,
`root`, `api` and, when served, the `loopback` endpoint (`host`, `port`, `certificate`). A daemon
refuses to start while another answers for the same estate; a stale endpoint file or socket left by a
crashed daemon never blocks the next owner. The CLI follows the same rule on every estate command except
the four named at the end of Operations: it reads the endpoint file, checks that the daemon answers, and
otherwise opens the estate itself with the embedded runtime. Both routes produce the same records, the
same refusals and the same status bytes; nothing lives only in the daemon.

## Transports

| Profile | Transport | Authentication |
|---|---|---|
| Local socket | A Unix domain socket in the user's runtime directory (`$XDG_RUNTIME_DIR/vericairn`, else `$TMPDIR/vericairn`, else `/tmp/vericairn-<user>`), owner-only; a named pipe on Windows | Operating-system identity: only the same user reaches the socket |
| Loopback | TLS on `127.0.0.1` with an ephemeral port, pinned to the runtime certificate | Device-signed requests |

On Windows the pipe is created by Kestrel's named-pipe transport with its default, `CurrentUserOnly`, and no
other pipe security. Its access list allows only the owner of the daemon's process token: the account that
started the daemon or, for a daemon run elevated, the owner Windows gives elevated processes (by default the
Administrators group), so that only administrators' elevated processes reach it.

The runtime certificate is self-signed ECDSA P-256, valid 180 days and replaced at the next start when
fewer than 30 days remain, kept owner-only as `runtime-certificate.p12` beside the live store. Its SHA-256
fingerprint is published in the endpoint file; a client accepts that certificate and no other, and
consults no trust store.

A loopback request carries three headers: `Vericairn-Device` (the device record id), `Vericairn-Signed-At`
(RFC 3339 UTC seconds) and `Vericairn-Signature` (base64url, 64 bytes). The signature is ECDSA P-256 over the
SHA-256 of the line-feed-joined string `vericairn-request/1`, the upper-case method, the request target (path
and query exactly as sent), the signed-at time and the lower-case hex SHA-256 of the body (empty for no body).
The daemon accepts a signature only from a device that is current and admitted now in the estate it serves,
within five minutes of its own clock, and once (a repeat is refused while the signature is among the last
4,096 the daemon accepted). It remembers a signature by its 64 bytes with `s` replaced by the smaller of `s`
and `n - s`, `n` being the order of the P-256 group, so neither another base64url spelling of the same bytes
nor the malleated twin `(r, n - s)`, which verifies just as well, passes for a new signature; the device
signing the same request again makes a new one, which is accepted. Anything else is answered with `401` and a
problem with code `denied`; revoking the device ends access from the next request on, with no restart. The SDK
signs with `DeviceRequestSigner`, which uses the device key in this machine's custody, so a copied request is
refused, as stale after five minutes and as a repeat before then while it is among the last 4,096, and a
copied key file is useless where platform custody holds the key.

## Operations

| Method and path | Meaning |
|---|---|
| `GET /v1/health` | The daemon's identity: API and product version, estate, root, profile, record format, start time, process |
| `GET /v1/status` | Status text and the canonical status JSON that is identical on every machine |
| `GET /v1/progress` | The progress report, from 0.6.0 (`ProgressResponse`: the text `vericairn progress` prints, every line naming the records it rests on; its canonical JSON, format `vericairn.progress/1`, identical on every machine for the same records; and whether the check it shows found errors). Its sections: where the estate stands (the pin and the record format with the records that set them, and status's check); the work orders by the state status derives; each published release and what it contained, the release notes' derivation over the records after the publication before it up to its own; what comes next by release, which until goals plan releases is each release work order not yet accepted (a work order whose key begins with `REL-`) and the work orders among its inputs; the next eligible work, the planned work orders that nothing holds ([record-format-v2.md](record-format-v2.md#what-holds-a-work-order)); and what waits on the operator: the asks put to an operator and awaiting an answer, with their recommendations, and the acceptances only an operator gives (milestone work orders submitted, results awaiting review toward milestones not accepted, release candidates without an operator's acceptance of their exact hash, and goals ready for an operator's acceptance) |
| `GET /v1/validate`, `GET /v1/check` | Findings of validation, and of validation plus the repository checks |
| `GET /v1/records/{id}` | A record with its path, hash, whether it is current, and the whole envelope |
| `GET /v1/records?type=<type>` | The current records of one type, oldest first, each with its envelope; empty for a type with none |
| `POST /v1/records` | Author a record (`NewRecordRequest`): signed with the author's admitted device held on the runtime's machine. When none is held and the estate's policy leaves signatures optional (the default), it is written unsigned, except an admission from record format 2, which is always signed. The record is validated with the estate and removed again when the estate would be invalid. |
| `GET /v1/decisions`, `GET /v1/decisions/{id}` | Current decisions; one decision by the id of any revision |
| `GET /v1/work/{key}/brief`, `GET /v1/work/{key}` | The brief of a work order; what an observer sees |
| `POST /v1/work/assign`, `attempt`, `result`, `amend` | Delegate, start, submit, request an amendment |
| `GET /v1/devices`, `POST /v1/devices`, `POST /v1/devices/admit`, `POST /v1/devices/revoke`, `POST /v1/devices/rename`, `POST /v1/devices/rotate`, `POST /v1/devices/recovery` | Devices and admission; a device is created, renamed and rotated on the machine that holds its key, and the operator's recovery device is made there and its key written once for offline keeping. Admit and revoke accept the recovery key's path and passphrase. |
| `GET /v1/releases`, `POST /v1/estate/upgrade` | Release records; plan (`check`) or apply an upgrade to a published release |
| `GET /v1/documents` | The declared living documents (`DocumentRow`: path, repository, declaration, title, state current, due or unavailable, level, reasons, latest clearance, unavailable contributions, notes) |
| `POST /v1/documents/{id}/reviews` | Review a living document by its declaration (`DocumentReviewRequest`: by, outcome, summary, findings, commit, and from 0.6.0 independence). The runtime reads the content, records its hash, the covered paths' identity and the triggers the review answers, records the reviewer's independence and runs the declared checks; a failed check, or the declaration's `clearedBy`, refuses a clearance. An estate file's revisions decide its reviewer's independence, and a stated one that differs is refused; for a document of another repository the runtime records the one stated, or without one `self` for a principal with the builder role and otherwise its judgement from the estate's records ([record-format-v2.md](record-format-v2.md#living-documents-due)). Returns a `WriteOutcome`. |
| `GET /v1/goals`, `GET /v1/goals/{id}` | The current goals (`GoalRow`: id, hash, title, status, sponsor and the sponsor's name), and one goal by the id of any of its revisions (`GoalResponse`: the text `vericairn goal show` prints and its canonical JSON, identical on every machine for the same records: the goal's record and revision, its sponsor, its status, `proposed` before any status and `achieved` once accepted, the budget its activation grants unit by unit, each condition's verdict, `met`, `not-met` or `unknown`, with its reason and the records it read, beside the current evaluation's verdict, the evaluation, the steps, who accepts it, whether it is ready for acceptance and what stands in the way, and its acceptance). An id that names no goal is answered 404 |
| `GET /v1/goals/{id}/next` | A goal's steps routed from the runtime's machine, from 0.6.0 (`NextResponse`: the text `vericairn goal next` prints and its canonical JSON, format `vericairn.next/1`): the goal's ledger and why a new run of it would be fenced; each step of its work with the state status derives, what holds each planned one, and for each eligible one its route, the principal its role's roster reaches or why none is available ([record-format-v2.md](record-format-v2.md#routing-a-goals-steps)), with every candidate and why it is or is not available, whether the goal's budget allows the step now, and for an agent the custody guard on the runtime's machine; the step that comes next, the first eligible one whose route acts; and the goal's asks. It reads that machine's launchers and keys, so unlike the goal view it is not the same on every machine. Reading it records and starts nothing; an id that names no goal is answered 404 |
| `POST /v1/goals` | Propose a goal, or a revision of one (`GoalProposeRequest`: by, the goal record's `body`, `supersedes` naming the current revision): a proposal authorizes and funds nothing, and a revision starts again from `proposed`. Anyone may propose a new goal; a revision is refused, before anything is written, unless its author is the sponsor of the revision it supersedes, a person, or an operator when an agent sponsors that goal (decision 01M50A8KHJT1MSSS84KZKHKRHB); anyone else proposes the change as an ask to the sponsor. Returns a `WriteOutcome` |
| `POST /v1/goals/{id}/activate`, `pause`, `resume`, `stop` | A goal's lifecycle (`GoalActionRequest`: by, an optional note): activate a proposed goal, or resume a paused, budget-limited or blocked one, which grants its budget; pause an active goal; stop a goal that has not ended, which records it abandoned. Each is by who decides for the goal: its sponsor, a person, who to activate or resume holds the operator role or a grant of the `spending` scope, or an operator when an agent sponsors it; an agent never activates a goal. Each is refused out of order, and after the goal ended or was achieved. Returns a `WriteOutcome` |
| `POST /v1/goals/{id}/evaluate` | Record the evaluation of a goal's current revision (`GoalEvaluateRequest`: by, summary, `verdicts`, each a `GoalVerdict` of the condition's position from 1, the verdict `met`, `not-yet` or `impossible`, the reason and the ids of the records read). The runtime gives each record predicate its own verdict with the records it read and refuses an evaluator's verdict that contradicts it, except `impossible` for one it does not find met; each evidence statement needs the evaluator's verdict, and one found met names the records read. Refused for a principal that authored one of the goal's steps. Returns a `WriteOutcome` |
| `POST /v1/goals/{id}/accept` | Accept a goal's current revision, relying on its current evaluation (`GoalAcceptRequest`: by, statement). Refused, with every reason at once and before anything is written, unless the principal is the one who accepts it (decision 01M507XV4VBFYWMRN8WCXMXHX4: its sponsor when the sponsor is a person, or the principal the sponsor delegated an ordinary goal's acceptance to while it holds the `accept-work` scope; an operator, only an operator, when an agent sponsors it), the goal was activated and has not ended, its current evaluation finds every condition met and is not the principal's, the principal authored none of its steps, and every record predicate holds now. Returns a `WriteOutcome` |
| `GET /v1/providers` | The provider and adapter implementations available to the runtime on its machine (`ProviderRow`: provider, kind release or source, and from 0.6.0 launcher or workspace, implementation, contract, status, operations, limitations, declared settings with defaults, the destination setting, the served locations). Being available selects nothing. |
| `GET /v1/launchers` | The coding-agent launchers available to the runtime on its machine, providers of `vericairn.launcher.agent/1`, as `ProviderRow`s of kind launcher; the generic launcher's first limitation says that generic process launch is not complete agent support. From 0.6.0 |
| `POST /v1/runs` | Start a coding agent's run on an assignment (`RunStartRequest`: assignment, launcher, agent, by, repository, and optionally goal, branch, base, wallMinutes and the launcher's settings), the worker contract `vericairn.worker/1` ([software.md](software.md#coding-agents-the-worker-and-its-launchers)). Refused, before anything is recorded, when the agent is not the assignment's contributor or has no admitted device whose key the runtime's machine holds, when the recording principal is neither the operator nor holds `local-engineering`, when the work belongs to no active goal or the goal's budget does not allow the run, when a device of an operator or of the goal's sponsor can sign there without its owner's presence (the refusal names each device and why), when the launcher cannot run (Codex's goals feature is verified first), or when the workspace cannot be prepared. Otherwise the workspace is prepared, the intent recorded with the run's cap, and the harness launched; the response returns once it started, and the runtime observes and settles the run. Returns a `RunResponse` (run, status, message, ok, the operation revision, the cap, the workspace, problem). From 0.6.0 |
| `GET /v1/runs` | The agent runs (`RunsResponse`): each run (`RunRow`: run, identity, launcher, agent, assignment, goal, workspace, status, cap, usage once settled, what it is charged, a stop's facts, its last heartbeat here, whether a worker here observes it now, and whether its launcher is generic), and the ledger of every goal with a run or active now (`LedgerRow`: each charged unit's budget, spend and remainder, the runs in flight, the concurrency, the attempts per step, and why a new run would be fenced). From 0.6.0 |
| `POST /v1/runs/{id}/stop` | Ask a run to stop (`RunStopRequest`: by, reason, graceSeconds) and report what was observed (`RunStopResponse`: run, state, message, ok, stop): the request, its transmission to the harness, the harness's acknowledgment, an escalation after the grace period and the observed containment are separate facts in `stop`, and `state` is the furthest one observed. A stop that is not observed is reported as `requested`. From 0.6.0 |
| `POST /v1/releases/submit` | Submit a release as intent before effect: the provider is the request's `provider`, else the one the product record declares (neither is refused; a provider with no implementation on the runtime's machine is refused, never replaced); the settings are the provider's defaults, then the product's declared settings, then the request's `settings` (and `repository`, `ref`, `workflow`, kept from 0.3.1 as settings of those names). The operation record is written, then the build is dispatched; nothing is published. Before any intent, the product's living documents can refuse it ([software.md](software.md#release-providers-and-the-products-release-profile), step 1). Returns a `ReleaseResponse`. |
| `POST /v1/releases/observe` | Observe a release's run, draft and publication through the provider its operation records and record what settled: the operation revision, the candidate release with every artifact classified by the product's profile (or the generic rule), and later the observed publication; or cancel an intent that had no effect. The operation is found by `provider` and `repository` (the destination), by `product` and `version`, or by a destination one current operation alone matches. Returns a `ReleaseResponse`. |
| `POST /v1/estate/export` | Write a verified export to a path on the runtime's machine; no keys or authority travel with it |
| `GET /v1/reconcile`, `GET /v1/recover/inspect` | Reconcile tasks left by diverged copies; what this copy holds |
| `GET /v1/objects`, `POST /v1/objects`, `GET /v1/objects/{hash}`, `GET /v1/objects/verify` | Content-addressed objects: list, store bytes, verified bytes, verify all |
| `POST /v1/shutdown` | Ask the daemon to stop; closing a client never stops it |

Restore, `recover compare`, `docs notes` and `docs regenerate` do not go through the API. Restore
creates another copy. The other three load this copy (and, for compare, the other copy too) directly
from its files and live store, even while a daemon serves it.

## Outcomes and problems

A write answers with a `WriteOutcome`: either `written` (path, id, hash) or `problem`, never both. A
problem has a stable `code`, a `message` that names the permitted next action, and `findings` when a
record was rejected:

| Code | Meaning | HTTP |
|---|---|---|
| `rejected` | The estate would be invalid with this record; the findings say why; nothing was written | 422 |
| `refused` | Malformed input, an unknown type, no admitted device key on this machine where the estate requires signatures (or for an admission from record format 2), or a limit | 422 (409 for upgrade and export, 413 for an object above the size limit) |
| `not-found` | The subject named does not exist | 404 |
| `invalid` | The estate has errors and the operation needs a valid estate (upgrade), or an export request lacks its output path or names a malformed exporter | 409 |
| `denied` | A loopback request without a valid signature by a device admitted now | 401 |
| `provider` | Observing a release, the provider refused or could not say what happened; nothing was recorded. Starting a run, the harness could not be started, and the run is recorded as a known failure that spent nothing | 200, inside the `ReleaseResponse` or the `RunResponse` |

`conflict`, which the OpenAPI description has named from the start, is reserved: nothing answers with it yet,
and it stays named so that no client that knows it breaks.

The release routes (`POST /v1/releases/submit` and `POST /v1/releases/observe`) answer every request
they handle with 200 and a `ReleaseResponse`: `ok` says whether the request advanced as asked or there
is nothing to record yet, and a refusal is its `problem`, not an HTTP status. The run routes (`POST
/v1/runs` and `POST /v1/runs/{id}/stop`, from 0.6.0) answer the same way, with a `RunResponse` or a
`RunStopResponse`; a run that is not found is a `problem` with code `not-found` inside. A read of a
missing subject answers 404 with an empty body.

Exit status and HTTP status report whether the operation itself completed, except on the release and run routes,
which answer the requests they handle with `200` and carry a refusal in the body's `problem`; the observed state is
in the body, and partial, pending and changed-target states are never flattened into success.

## The runtime command family

| Command | Does |
|---|---|
| `vericairn runtime serve` | Serve the estate in this process until Ctrl+C or `runtime stop`; refuses when a daemon already serves it; `--no-loopback` serves the local socket only; `--detached` writes the daemon's output to `daemon.log` beside the live store instead of the standard streams, which is how `runtime start` runs it |
| `vericairn runtime start` | Start a daemon in the background and wait until it answers; the daemon writes to `daemon.log` beside the estate's live store, and one that stops before serving is reported with what it wrote there and where the log is |
| `vericairn runtime stop` | Ask the daemon to stop and wait until it has |
| `vericairn runtime status` | Whether a daemon serves the estate and where, or whether commands run the embedded runtime |
| `vericairn runtime install [--plan]` | Install the daemon as a per-user service that starts at login and restarts after failure: a launchd agent (`~/Library/LaunchAgents`), a systemd user unit (`~/.config/systemd/user`) or a Task Scheduler logon task; `--plan` shows the unit and the commands without installing |
| `vericairn runtime uninstall` | Remove that service |

The service unit lives in the user's own configuration and names this machine's paths, because a service
manager needs them; nothing of it enters a repository.

## The SDK

`new VericairnClient(endpoint)` connects over the local socket; `VericairnClient.Loopback(endpoint, signer)`
connects over pinned loopback TLS and signs every request with the signer, which `DeviceRequestSigner.Find`
builds from a device key held on this machine. `VericairnClient.IsAlive(endpoint)` says whether a daemon
answers. The client implements `IEstateApi`, so code written against the interface runs unchanged against
the embedded `EstateService`. From 0.6.0 it also has `Launchers()`, `RunStart(request)`, `Runs()` and
`RunStop(run, request)`, the worker's operations; with the daemon, a run started through it goes on after
the request returns, and a client's absence never stops it. `GoalNext(goal)` reads a goal's steps routed from the
daemon's machine.

## Changes visible to scripts

`vericairn estate upgrade --json` prints the plan with camel-case names (`fromPin`, `toPin`, `fromFormat`,
`toFormat`, `migrations`, `release`, `artifacts`, `applied`, `migration`, `anchored`), the same shape the
API returns. `anchored`, new in 0.5.2, lists the admissions recorded without a signature that the move to
record format 2 anchors, and is `null` otherwise.
