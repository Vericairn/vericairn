# API version 1

One versioned API that every client uses. `Vericairn.Protocol` is its single definition: the operations
(`IEstateApi`), the request and response types, the state language, the problem codes and the OpenAPI
document (`OpenApiDocument.Json`, served at `/openapi/v1.json`). `Vericairn.Runtime` implements it once
(`EstateService`); the daemon (`Vericairn.Daemon`) serves that implementation; the SDK (`Vericairn.Sdk`)
calls it; the CLI uses the daemon's when one serves the estate and embeds the runtime otherwise. Authority
is evaluated behind the API, never in front of it: a client presents and requests, the runtime decides,
and every response is what the runtime observed.

## One owner

One daemon process owns one open estate's live state (its SQLite live store and its key custody on that
machine). Clients never open the live store; they call the daemon. The daemon publishes where it serves
in an endpoint file beside the live store:

```
<VericairnHome>/estates/<estate id>/<sha256(root)[:16]>/daemon.json
```

with `kind` (`unix` or `pipe`), `address`, `processId`, `startedAt`, `root`, `api` and, when served, the
`loopback` endpoint (`host`, `port`, `certificate`). A daemon refuses to start while another answers for
the same estate; a stale endpoint file or socket left by a crashed daemon never blocks the next owner.
The CLI follows the same rule on every command: it reads the endpoint file, checks that the daemon
answers, and otherwise opens the estate itself with the embedded runtime. Both routes produce the same
records, the same refusals and the same status bytes; nothing lives only in the daemon.

## Transports

| Profile | Transport | Authentication |
|---|---|---|
| Local socket | A Unix domain socket in the user's runtime directory (`$XDG_RUNTIME_DIR/vericairn`, else `$TMPDIR/vericairn`, else `/tmp/vericairn-<user>`), owner-only; a named pipe on Windows | Operating-system identity: only the same user reaches the socket |
| Loopback | TLS on `127.0.0.1` with an ephemeral port, pinned to the runtime certificate | Device-signed requests |

The runtime certificate is self-signed ECDSA P-256, valid 180 days and replaced at the next start when
fewer than 30 days remain, kept owner-only as `runtime-certificate.p12` beside the live store. Its SHA-256
fingerprint is published in the endpoint file; a client accepts that certificate and no other, and
consults no trust store.

A loopback request carries three headers: `Vericairn-Device` (the device record id), `Vericairn-Signed-At`
(RFC 3339 UTC seconds) and `Vericairn-Signature` (base64url, 64 bytes). The signature is ECDSA P-256 over
the SHA-256 of the line-feed-joined string `vericairn-request/1`, the upper-case method, the request target
(path and query exactly as sent), the signed-at time and the lower-case hex SHA-256 of the body (empty
for no body). The daemon accepts a signature only from a device that is current and admitted now in the
estate it serves, within five minutes of its own clock, and once. Anything else is answered with `401`
and a problem with code `denied`; revoking the device ends access from the next request on, with no
restart. The SDK signs with `DeviceRequestSigner`, which uses the device key in this machine's custody,
so a copied request grants nothing and a copied key file is useless where platform custody holds the key.

## Operations

| Method and path | Meaning |
|---|---|
| `GET /v1/health` | The daemon's identity: API and product version, estate, root, profile, record format, start time, process |
| `GET /v1/status` | Status text and the canonical status JSON that is identical on every machine |
| `GET /v1/validate`, `GET /v1/check` | Findings of validation, and of validation plus the repository checks |
| `GET /v1/records/{id}` | A record with its path, hash, whether it is current, and the whole envelope |
| `GET /v1/records?type=<type>` | The current records of one type, oldest first, each with its envelope; empty for a type with none |
| `POST /v1/records` | Author a record (`NewRecordRequest`): signed with the author's admitted device, validated with the estate, removed again when the estate would be invalid |
| `GET /v1/decisions`, `GET /v1/decisions/{id}` | Current decisions; one decision by the id of any revision |
| `GET /v1/work/{key}/brief`, `GET /v1/work/{key}` | The brief of a work order; what an observer sees |
| `POST /v1/work/assign`, `attempt`, `result`, `amend` | Delegate, start, submit, request an amendment |
| `GET /v1/devices`, `POST /v1/devices`, `POST /v1/devices/admit`, `POST /v1/devices/revoke`, `POST /v1/devices/rename`, `POST /v1/devices/rotate`, `POST /v1/devices/recovery` | Devices and admission; a device is created, renamed and rotated on the machine that holds its key, and the operator's recovery device is made there and its key written once for offline keeping. Admit and revoke accept the recovery key's path and passphrase. |
| `GET /v1/releases`, `POST /v1/estate/upgrade` | Release records; plan (`check`) or apply an upgrade to a published release |
| `GET /v1/providers` | The provider and adapter implementations available to the runtime on its machine (`ProviderRow`: provider, kind release or source, implementation, contract, status, operations, limitations, declared settings with defaults, the destination setting, the served locations). Being available selects nothing. |
| `POST /v1/releases/submit` | Submit a release as intent before effect: the provider is the request's `provider`, else the one the product record declares (neither is refused; a provider with no implementation on the runtime's machine is refused, never replaced); the settings are the provider's defaults, then the product's declared settings, then the request's `settings` (and `repository`, `ref`, `workflow`, kept from 0.3.0 as settings of those names). The operation record is written, then the build is dispatched; nothing is published. Returns a `ReleaseResponse`. |
| `POST /v1/releases/observe` | Observe a release's run, draft and publication through the provider its operation records and record what settled: the operation revision, the candidate release with every artifact classified by the product's profile (or the generic rule), and later the observed publication; or cancel an intent that had no effect. The operation is found by `provider` and `repository` (the destination), by `product` and `version`, or by a destination one current operation alone matches. Returns a `ReleaseResponse`. |
| `POST /v1/estate/export` | Write a verified export to a path on the runtime's machine; no keys or authority travel with it |
| `GET /v1/reconcile`, `GET /v1/recover/inspect` | Reconcile tasks left by diverged copies; what this copy holds |
| `GET /v1/objects`, `POST /v1/objects`, `GET /v1/objects/{hash}`, `GET /v1/objects/verify` | Content-addressed objects: list, store bytes, verified bytes, verify all |
| `POST /v1/shutdown` | Ask the daemon to stop; closing a client never stops it |

Restore and `recover compare` are not estate operations: they create or read other copies, so the CLI
performs them itself.

## Outcomes and problems

A write answers with a `WriteOutcome`: either `written` (path, id, hash) or `problem`, never both. A
problem has a stable `code`, a `message` that names the permitted next action, and `findings` when a
record was rejected:

| Code | Meaning | HTTP |
|---|---|---|
| `rejected` | The estate would be invalid with this record; the findings say why; nothing was written | 422 |
| `refused` | Malformed input, an unknown type, no admitted device key on this machine, or a limit | 422 (409 for upgrade and export, 413 for an object above the size limit) |
| `not-found` | The subject named does not exist | 404 |
| `invalid` | The estate has errors and the operation needs a valid estate | 409 |
| `denied` | A loopback request without a valid signature by a device admitted now | 401 |

Exit status and HTTP status report whether the operation itself completed; the observed state is in
the body, and partial, pending and changed-target states are never flattened into success.

## The runtime command family

| Command | Does |
|---|---|
| `vericairn runtime serve` | Serve the estate in this process until Ctrl+C or `runtime stop`; refuses when a daemon already serves it; `--no-loopback` serves the local socket only |
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
the embedded `EstateService`.

## Changes visible to scripts

`vericairn estate upgrade --json` now prints the plan with camel-case names (`fromPin`, `toPin`,
`fromFormat`, `toFormat`, `migrations`, `release`, `artifacts`, `applied`, `migration`), the same shape the
API returns.
