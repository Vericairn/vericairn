# API version 1

This draft describes 0.7.2 after published 0.7.1. FIX-13 adds exact-commit release document checks
and settlement of process attention asks; FIX-15 claims dispatch identities before provider effects;
FIX-16 validates before publishing records and exposes unreadable recovery journals; FIX-18 adds
Codex max effort, run-owned execution observations and corrected worker signals. FIX-14 hardens the
CLI/runtime export restore path without adding an HTTP restore route. API v1 remains additive and
record format 2 is unchanged. The 0.7.0 candidate's features shipped in 0.7.1 with FIX-12;
the release index retains the withdrawn candidates' history. NuGet availability follows publication
and the successful package workflow for that version.

One versioned API that every client uses. `Vericairn.Protocol` is its single definition: the operations
(`IEstateApi`), the request and response types, the `Problem` shape and the OpenAPI document
(`OpenApiDocument.Json`, served at `/openapi/v1.json`). The problem codes are those listed under Outcomes
and problems. The state language that clients present is the table in `Vericairn.Presentation`, which
tests hold to `docs/state-language.json` and to the runtime's own strings. `Vericairn.Runtime` implements
it once (`EstateService`); the daemon (`Vericairn.Daemon`) serves that implementation; the SDK
(`Vericairn.Sdk`) calls it; the CLI uses the daemon's when one serves the estate and embeds the runtime
otherwise. Authority is evaluated behind the API, never in front of it: a client presents and requests,
the runtime decides, and every response is what the runtime observed.

## Required attention (0.7.3 addition)

Status and progress JSON add a compact `attention` array, independent of response
depth and recipient settings. Each item preserves the source type, id and complete
hash, subject, current state, recorded freshness, reason and inspection command.
It carries no envelope. Status JSON remains identical on every machine for the
same records; its attention reads records alone. Progress additionally includes
runtime-local worker concerns, labelled `Local worker observations`, as do human
status and Workbench. API, SDK, MCP and CLI machine reads share these projections;
record reads remain complete. [Required attention](required-attention.md) specifies
the fields, resolution rules, unknown cases and compatibility boundaries.

## Record publication and recovery (0.7.2)

Single and grouped writes judge the proposed estate, including content checks, before publishing any
new record. Validation failure publishes nothing. A single `EstateService` gate serializes judgment
through commit; it is not a cross-instance or cross-process writer lease. Route writes through one
runtime and do not overlap embedded commands, daemon startup or repository synchronization.
Grouped storage remains sequential and can be interrupted.

Status, check and reconciliation expose malformed, incomplete or unreadable local journals as
`batch.unreadable`, with a safe `batch-journal:<ULID>` or filename-digest identifier, never a machine
path or parser error. The journal is retained and retried. Its unknown contents hold all further record
writes with `reconcile-required`; reads, local configuration and interim live progress notes continue.
A readable interrupted batch instead reports `batch.incomplete` under its existing reconciliation rules.
The operator can restore readable journal data or inspect stored effects and preserve the damaged journal
outside the batches directory before retrying. Separate service reads can reconcile journals too;
[software.md](software.md#record-publication-and-local-recovery-072) states the single-service boundary.

Release submit resolves the provider's exact build commit for repository document checks and claims
its operation identity before dispatch. A receiptless intent is uncertain, so observe and reconcile it
before retrying. Observation persists that uncertainty even when it subsequently waits for a run or
draft; the response can include the recorded operation. Observation checks documents at the actual
build commit again before recording success or a candidate. Missing commits refuse with fetch guidance,
without substituting clone HEAD. Provider I/O runs outside the service gate; this is not distributed
exactly-once execution across independently running services.

## Context artifacts (0.7.0 addition)

`GET /v1/context/work/{key}` (`Context`) computes and returns a
`vericairn.context/1` package. It authors no record and writes nothing.
`GET /v1/context/original/{pin}` (`ContextRead`) returns the pinned identity and
exact original bytes (base64 in JSON). The shell-safe `vc1_` pin is unpadded
base64url of canonical JSON containing the estate and source identity. A record
pin contains its id, canonical record hash, path and byte hash; a document pin
contains its path, revision record id and hash, and byte hash. It is an identity,
not authority, and requires no retained manifest.

Both operations recheck the requesting device under the runtime gate. A reader
who loses access receives only `Completeness cannot be confirmed.` from the
context operation, without source metadata. Transport authentication can refuse
the request before the operation. Embedded/local-socket reads retain their OS
authority. The existing boundary is estate-wide; no per-source ACL is claimed.

Records are read from immutable record custody (the repository's `records/`,
or the live store for the local-only profile). Documents are read from normalized
`docs/` or `design/` working-tree paths, without following symbolic links, only
while their bytes match the pinned revision. No object-store fallback, retained
manifest, latest revision or other source is substituted. Unavailable or changed
bytes return `original-unavailable`. The earlier unreleased manifest/source
route remains callable but returns unavailable after access checking; prepare
again for a self-contained pin. The SDK exposes both API v1 routes. No new record
type is introduced. Remote is not built.

Briefs, preparation, hand-over and retrieval do not retain artifacts in the
estate repository or object store. At dispatch only, the worker retains the
complete machine brief in `context.json` and its human rendering in `brief.md`
in the run's existing local directory under the Vericairn home. Its exported
workspace brief remains git-ignored. These local files are not records, object
custody or an editable authority ledger. Context payloads are not object-store
objects and are not limited by that store's per-object limit.

The optional `maxBytes` query on context preparation is the admitted **context
input allocation**, separate from any run spending cap. Without an allocation,
only mandatory originals are inlined; named optional originals have withheld
entries with sizes and retrieval pins, even when they are small. Accounting
says `limit not set`, not that it fits a model. With an allocation, mandatory
originals remain complete and optional bodies are included deterministically
when they fit. The whole manifest, withheld entries and accounting
must fit too, or `context-over-budget` returns no package. `accounting.bytes`
measures the exact final canonical pretty UTF-8 payload including that report;
`remainingBytes` and `overageBytes` use the same count. The package sidecar pins
the payload hash and count; edits invalidate it. A counted serialization-padding
field is possible where decimal digit boundaries otherwise oscillate.

Tokens and model are explicitly unknown: no qualified local tokenizer is
available, no byte-to-token estimate is used, and no counting call uploads
content. Model fit and autonomous dispatch remain unqualified by this byte
measurement. The caller must resolve existing conversation, harness/tool
framing, model limits and reserves before budget-dependent dispatch. Transport
framing and provider-billed usage are separate unknowns, not zero. The MCP
`context_prepare` text and ordinary `context prepare` stdout are exactly this
measured payload; API/SDK JSON envelopes and `--json` sidecars are transport.

Context preparation and retrieval answer handled requests with HTTP 200 and a
`ContextResponse` or `ContextReadResponse`; callers must inspect `problem`.
`incomplete-context` means required source material is missing or invalid,
`context-over-budget` means the complete mandatory payload cannot fit.
Retrieval uses `original-unavailable` for a missing pinned original and
`invalid-context` for failed verification, always without returned bytes.
`invalid`, `not-found` and `denied` also appear in these response bodies;
transport authentication may instead reject the request with HTTP 401.

Selection includes mandatory working material and optional documents explicitly
named by the work, its inputs or governing decisions. The remaining catalogue
is outside the selection, with `vericairn context catalogue`,
`GET /v1/records?type=document-revision` and the equivalent `records_list` call
provided for listing its identities without reading bodies.

`GET /v1/context/work/{key}/brief` (`BriefContext`) is the agent-facing brief.
Its default is mandatory originals plus the withheld list; `maxBytes` can admit
optional bodies when they fit. It accepts the same recipient, assignment and
communication view queries as the human brief. MCP `work_brief` uses this route
and keeps COMMS-01's recipient and assignment arguments. Personal depth changes
only the measured Markdown presentation; canonical JSON and its context
identities remain independent of the recipient. The standalone
human `GET /v1/work/{key}/brief` and `work brief`, including its JSON form, keep
the full selected scope with no cap. Neither form imports unmentioned
catalogue bodies.

`GET /v1/work/{key}/brief` keeps its existing machine fields and human lead and
adds the package under `json.context`, including its withheld list and measured
payload. The complete machine brief has its own `json.accounting`; the additive
`jsonMeasurement` sidecar hashes and measures that entire canonical JSON. The
complete Markdown includes the resolved communication lead and section,
readable originals, the manifest and withheld list;
`markdownMeasurement` pins its exact UTF-8 bytes, including the accounting line
in the text. Neither human depth nor a change to the lead can silently reduce
the context contract. A context refusal appears as the additive `problem` and
does not yield a complete brief. Existing JSON fields retain their meaning.

## One owner

One daemon process owns one open estate's live state (its SQLite live store and its key custody on that
machine). Clients never open the live store; they call the daemon. The daemon publishes where it serves
in an endpoint file beside the live store:

```
<VericairnHome>/estates/<estate id>/<key>/daemon.json
```

where `<key>` is `local` for a local-only estate and otherwise the first 16 hexadecimal digits of the
SHA-256 of the root's full path, with `kind` (`unix` or `pipe`), `address`, `processId`, `startedAt`,
`root`, `api`, when served the `loopback` endpoint (`host`, `port`, `certificate`) and, from 0.6.0, when
served the `localNetwork` endpoint (`host`, `port`, `identity`, the runtime identity's fingerprint). A daemon
refuses to start while another answers for the same estate; a stale endpoint file or socket left by a
crashed daemon never blocks the next owner. The CLI follows the same rule on every estate command except
the four named at the end of Operations: it reads the endpoint file, checks that the daemon answers, and
otherwise opens the estate itself with the embedded runtime. Both routes use the same estate operations and
validation. The daemon also owns listeners, pairing exchanges and supervision that can continue after a client exits;
those do not survive a one-command embedded runtime. Status and progress include repository checks, and goal predicates
over living documents can read local covered paths, so equal records alone do not guarantee equal views when those
files, registered clones or installed contributions differ.

## Transports

| Profile | Transport | Authentication |
|---|---|---|
| Local socket | A Unix domain socket in the user's runtime directory (`$XDG_RUNTIME_DIR/vericairn`, else `$TMPDIR/vericairn`, else `/tmp/vericairn-<user>`), owner-only; a named pipe on Windows | Operating-system identity: only the same user reaches the socket |
| Loopback | TLS 1.2 only on `127.0.0.1` with an ephemeral port, pinned to the runtime certificate | Device-signed requests |
| Local network, from 0.6.0 | Mutual TLS 1.2 at the address and port the operator configured; off unless configured, and on a wildcard or public address only when the operator allows it | A paired device: a client certificate its device key issued, the runtime identity's proof on every connection, and device-signed requests bound to the estate and the runtime identity, under the device's permitted profiles |

On Windows the pipe is created by Kestrel's named-pipe transport with its default, `CurrentUserOnly`, and no
other pipe security. Its access list allows only the owner of the daemon's process token: the account that
started the daemon or, for a daemon run elevated, the owner Windows gives elevated processes (by default the
Administrators group), so that only administrators' elevated processes reach it.

The owner-only file modes described here are set on Unix. On Windows these configuration and connection files inherit
their directories' access controls; use a user-private state directory. Platform key stores have their own custody rules.

The runtime certificate is self-signed ECDSA P-256, valid 180 days, kept with its key as `runtime-certificate.p12`
beside the live store, in a file owner-only from the moment it is written and a directory the daemon makes
owner-only at every start, and presented on both TLS profiles. While a daemon presents it, its key is also in
memory on Linux, in a temporary keychain file in the user's owner-only temporary directory on macOS, whose TLS
takes keys from a keychain only, and in the user's key store on Windows, whose TLS uses no ephemeral key; the
daemon disposes of it when it stops, and a daemon that crashes may leave that file or stored key behind. It is
replaced when fewer than 30 days remain: at the next start and, from 0.6.0, by a running daemon, which checks
every hour; `vericairn runtime rotate` replaces it at once. A replacement writes the new file whole and publishes
the new fingerprint in the endpoint file. A loopback client pins the fingerprint the endpoint file names when it is
made, accepts that certificate and no other, and consults no trust store, so a loopback client made before a
rotation refuses the new certificate on its next connection and is made again from the endpoint file, as every
command makes its client. Paired devices on the local network pin the runtime identity instead (below), so a
rotation leaves them working.

Before a loaded or replacement certificate is made available to either TLS listener, its managed extensions
are decoded on the loading thread. Concurrent first handshakes therefore read complete usage values, including
Server Authentication. Server-side handshake exceptions are reported through `DaemonOptions.Log` with only
the exception type and message, without exception stacks, inner exceptions, peer certificates or request data.

Loopback, like the local-network listener, accepts TLS 1.2 only. Offers limited to TLS 1.0, 1.1 or 1.3
are refused during the handshake, before any HTTP request is read.

A loopback request carries three headers: `Vericairn-Device` (the device record id), `Vericairn-Signed-At` (RFC
3339 UTC seconds) and `Vericairn-Signature` (base64url, 64 bytes). The signature is ECDSA P-256 over the SHA-256
of the line-feed-joined string `vericairn-request/1`, the upper-case method, the request target (path and query
exactly as sent), the signed-at time and the lower-case hex SHA-256 of the body (empty for no body). A JSON body
is UTF-8: one whose `Content-Type` names another charset is refused, and so is one that begins with a byte order
mark; any other body is taken as the bytes it is. The daemon accepts a signature only from a device that is
current and admitted now in the estate it serves, no more than five minutes old by its own clock when the request starts and
still when its body is in, and once within this runtime instance. From 0.6.1 authentication allows at most 30 seconds to
read the body after the device's header check, or the signature's remaining five-minute validity if shorter;
a request whose body has not arrived by that deadline is refused. It remembers each device's accepted signatures until five minutes
after their signing time, when they would be refused as stale anyway, refuses a signature already stale, and
forgets none sooner: a device with 10,000 in that window is refused until older ones lapse, so no flood, by that
device or another, and no body held back, makes room for a replay. It remembers a signature by its 64 bytes with
`s` replaced by the smaller of `s` and `n - s`, `n` being the order of the P-256 group, so neither another
base64url spelling of the same bytes nor the malleated twin `(r, n - s)`, which verifies just as well, passes for
a new signature; the device signing the same request again makes a new one, which is accepted.

On every device-authenticated profile, including the local-network identity proof, the signing time must also
be at or after the instant this runtime instance began accepting requests and no more than five seconds ahead of its clock.
A request signed before that cutoff is refused with `401` / `denied` and a message to **sign the request afresh**.
The cutoff retains its subsecond precision: rounding it down would admit a replay after a restart within the
same second. Startup publishes readiness after the next whole second, so clients using the existing timestamp
format can sign immediately. The replay cache remains in memory; with a nondecreasing runtime clock, a request
whose signing time predates the new instance cannot pass its cutoff. A signing time more than five seconds ahead is refused,
since a request signed ahead could be replayed after a restart before its signed time; the five seconds allow
for a client clock that runs a little fast, and bound that replay to a restart within them. Synchronize client
and runtime clocks: clients may lag by up to five minutes, but never past the startup cutoff; a client further
ahead must correct its clock and sign afresh. There is no clock-skew allowance across the startup cutoff.

Anything else is answered with `401` and a problem with code `denied`; revoking the device ends access from
the next request on, with no restart. From 0.6.1 loopback checks the headers' signing time and resolves the device's
current admitted standing before reading any body, as the local-network profile already does. The body-bound
signature is verified only after the bounded body read; the headers alone do not prove possession of the key.
After reading, admission and freshness are checked again, and a loopback key rotation during the read refuses the
request. A failed header check takes the early-refusal path, with only the bounded discard described below.
An already authenticated request also loses its right to write: under
the estate's write lock, the runtime rechecks that the requesting device is current and admitted, not revoked,
and still holds the exact key that verified the request. A rename with the same key still passes; a rotation
requires signing afresh with the new key. Another admitted device of the same principal does not rescue the
request. This check covers single records, grouped records and object storage, and precedes key or upgrade
file changes and exports. A stop request is checked under the same lock before it is stored for the worker.
Writes completed before a revocation remain; the refused write produces nothing. Local-socket and embedded
operations continue to use the caller's operating-system authority.

The SDK signs each request when it sends it, including the identity proof on a new connection, with
`DeviceRequestSigner`, which uses the device key in this machine's custody. A copied request is refused as stale after
five minutes and as a repeat within the same runtime instance; the startup cutoff and five-second lead bound the
restart exception described above. A copied key file is useless where platform custody
holds the key.

Every HTTP refusal (status 400 or above) completes its response before cleanup: authentication, pairing,
request-size limits, binding and endpoint refusals all share this path. A refusal with an unread body sends
`Connection: close`; a fully read refusal retains its existing connection policy, including a runtime identity
proof refused after an ordinary certificate rotation. On the local socket, loopback and the local network,
the daemon discards at most 64 KiB of the unread body for at most ten seconds before returning. Connections
marked close then close. Discarded body bytes are never parsed as API content, bound, decoded or acted on,
and never extend a signature's validity. Cleanup cancels only a pending pipe read, preserving the transport and
the completed response. Closing Windows TCP with unread data can otherwise reset it and discard the response.
A peer that sends more than the discard bound, or keeps sending beyond its deadline, cannot require delivery
after that bounded close.

From 0.6.1 device-signed bodies on loopback and the local network are limited to 8 MiB (8,388,608 bytes), or Kestrel's
configured limit if smaller. Their reads have an absolute 30-second deadline from the header check, shortened to the
signature's remaining five-minute validity when less; received bytes do not restart it. Timeout returns `401` before
bounded discard and cancels only the pending pipe read. Local-socket bodies retain Kestrel's configured limit
(30,000,000 bytes by default); pairing hello and confirm remain limited to 16 KiB. A declared length above the limit is refused before reading; a streaming body is offered
to authentication or binding only within its limit. The original body reader remains available for discard
instead of becoming unreadable after a size-limit failure. Size refusals are `413`; cleanup does not raise the
parsing limit.

Loopback permits at most eight simultaneous body reads awaiting signature verification across all devices.
The slot is held through signature verification and released on every outcome, including a timeout, disconnect,
or size refusal. With all slots occupied, a further admitted-device request receives `503` with problem code
`refused` before any authentication body read; it follows the same completed-response and bounded-discard path.
There is no queue of waiting bodies. Clients may retry with a fresh signature when capacity is available.
Kestrel admits at most 32 simultaneous connections across all listeners, including handshakes and idle
connections. Excess connections close at the transport before TLS or HTTP; no HTTP response exists at that stage.

From 0.6.0 a device-signed request acts for its device's principal, on loopback as on the local network. When the
request an operation receives names the principal acting, in its `by` or `principal`, as records, assignments,
admissions, revocations, goals (including taking a step), work takeovers, releases, runs, new and recovery devices
and pairing codes do, that principal must be the device's own; a device renames and rotates only devices of its own principal; and stopping the daemon
or replacing its certificate or identity is an administrator's, the operator's or a principal's holding
`administer`. The runtime checks the request object the operation receives, after the body is bound, and never
reads the body a second time, so no encoding, spelling or repetition in the body makes the check and the
operation read different principals. Every served route has an explicit scope rule; a route or request type
it does not know is refused. Asks and next-step routing are read views: `to` selects whose asks to read and
`id` selects the goal, and neither makes that principal act or answers an ask. A work takeover is recorded
only by a person; its `contributor` selects the person receiving the assignment, never the actor. A refusal is `401` with
code `denied`. The local socket carries the operating system's identity of its caller instead, and acts for any
principal as before: the command line, Workbench and the MCP server reach the daemon there.

## The local-network profile

From 0.6.0 a runtime can serve paired devices on other machines over the local network. Nothing listens
beyond the local socket and loopback unless the operator configures an address: `vericairn runtime
configure --local-network <address:port>` keeps an IP address of the machine and a port (`192.168.1.20:7443`,
`[fe80::1]:7443`; port 0 asks for any free port, which only tests want) in `runtime.json` beside the live
store, owner-only, and the runtime listens there from its next start; `--local-network off` turns it off. The
address is one on a private network: loopback, `10/8`, `172.16/12`, `192.168/16`, link-local, or a unique local
IPv6 address (`fc00::/7`). A wildcard address (`0.0.0.0`, `::`), which listens on every interface, or one that
other networks may reach is refused unless the operator adds `--allow-public`, which `runtime.json` records as
`allowPublic`; a runtime whose settings name such an address without it, as when the file was edited by hand,
does not start. The address is machine configuration, never a record. Devices reach the runtime at that
explicit address; discovery is not built.

**The runtime identity.** A runtime that serves the local network has an identity of its own: a P-256 key
made in the strongest custody the machine offers, as a device key is (CNG on Windows, the Secure Enclave
through the helper on a Mac, the secret service on Linux where one runs, otherwise an owner-only file), never
asking for presence and never used by TLS. `runtime-identity.json` beside the live store names the key and
its custody, never the key. A device pins the identity's public key when it pairs; the endpoint file and
`vericairn runtime status` show its fingerprint. The identity outlives restarts and certificate rotations;
when its key is no longer held, the runtime makes a new identity and every paired device pairs again.

**A connection.** The local network speaks TLS 1.2 only, at both ends, with ECDHE, ECDSA and AES-GCM:
`TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384`, then `TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256` (on Windows, whose TLS
takes no list from an application, the system's own order, which puts these first). A client that offers no TLS
1.2 is refused. The runtime presents its certificate and asks for a client certificate, which only the pairing
exchange may go without, and never resumes a TLS session. A paired device presents a certificate it issues for
its connections: a fresh P-256 key, valid one day and replaced in its last hour, signed ECDSA with SHA-256 by the
device's own key wherever that key is held. Its subject and issuer are `CN=vericairn device` for every device,
since TLS 1.2 shows certificates in clear: the device is named only inside the connection, by the requests it
signs, though whoever holds the estate's device records could still tell from the certificate's signature which
device's key issued it. The connection's key is held as the runtime certificate's is while presented: in memory
on Linux, in a temporary keychain file on macOS, in the user's key store on Windows, until the SDK disposes of it,
which it does when the credential that replaced it is replaced in turn.

TLS 1.2 is chosen because its certificates travel in clear, where Vericairn reads them before the platform's TLS
does. Neither side builds a peer's certificate chain online: revocation is checked nowhere and nothing is
downloaded. But a platform's TLS can evaluate a peer's certificate before any check the caller configures, as
macOS's does, asking the system's trust service, which fetches the issuer a certificate names; under TLS 1.3 the
certificate would be encrypted, out of reach. So the runtime and the SDK read each handshake while it travels in
clear and refuse, before this machine's TLS sees it, a certificate that names an address to fetch (authority or
subject information access, CRL distribution points, a freshest CRL), one that does not parse, a list of more than
one, and an offer to fetch certificates from addresses (`certificate_url`). The policy that downloads nothing
stays as well. When the server's screen refuses before TLS is established, it flushes a fatal TLS 1.2
`handshake_failure` alert before discarding at most 64 KiB of raw transport bytes for at most ten seconds.
That remainder bypasses both the certificate screen and the TLS engine; there is no HTTP request or response
at this stage. The listener also refuses a client's renegotiation, which would carry a new certificate inside the
encrypted channel, out of the screen's sight. What TLS 1.2 gives up against 1.3 is certificates hidden from whoever
watches the network, and one round trip on each new connection. The constant subject answers the first in part
only: the client certificate names no device, but its signature is the device key's and travels in clear too, so
whoever holds the estate's device records can tell which device connects.

Before any other request on a connection the device asks `GET /v1/runtime/identity?nonce=<32 random bytes,
base64url>`, signed as on loopback (`vericairn-request/1`), since nothing has yet proven which runtime answers,
and verifies the `RuntimeIdentityProof` it gets: the identity it pinned signed, ECDSA P-256 in IEEE P1363 form,
the SHA-256 of the line-feed-joined `vericairn-runtime-identity/1`, the estate's id, the fingerprint of the
certificate its own handshake showed (`sha256:` and hex), the fingerprint of its own client certificate on that
connection, and its nonce. The runtime gives a proof only over the local network, to a paired device whose
request it authenticated, for the connection that asked, and only while the certificate that connection began
under is the current one (`409` otherwise). A connection whose proof fails is closed with nothing else sent. A
certificate is therefore accepted only while the runtime identity signs it: rotating the certificate keeps
every paired device working, a replaced certificate is refused by every paired device, and a proof obtained on
one connection is worth nothing on another. The runtime keeps each connection in view from the moment it
accepts it, before TLS, so `vericairn runtime rotate --compromised` also closes connections still in their
handshake.

**Every request.** Every request after the proof is device-signed with scheme `vericairn-request/2`: the same
three headers, at most five minutes of age and each signature accepted once in any of its forms, over the SHA-256 of
the line-feed-joined `vericairn-request/2`, the estate's id, the fingerprint of the runtime identity the device
paired with, and then the method, the request target, the signed-at time and the body's hash as on loopback. A
request signed for one runtime is refused by any other, even a runtime of the same estate on another machine,
which knows the same device. The startup cutoff and clock rule above apply here too: a signing time before
this instance began accepting requests, or more than five seconds ahead of its clock, is refused. Local-network clients must synchronize
their clocks; a slow client can be refused after a restart until its signing time reaches the cutoff. Until
the connection's client certificate verifies with the current key of the
device a request names, every refusal is the same one, so nobody learns from it whether a device exists, is
revoked or is permitted the profile. The runtime reads the device from the records at every request, when it
starts and again once its body is in, and refuses it unless it is current, admitted now and permitted
`local-network` in its device record's `profiles`, and unless the signature verifies with its key. A revoked
device is therefore refused at its next request, on a connection it already has open as well, and a request whose
body arrives after the revocation is refused too. Before an authenticated request writes, the runtime rechecks
its device under the estate's write lock: it must still be admitted, on the key that verified this request,
and permitted `local-network`. A revocation, rotation or profile withdrawal recorded between authentication
and the write therefore refuses that write with `401` / `denied`; the local-network ledger keeps its outcome
as failed. Disconnection alone revokes nothing. The device acts for its own principal, as on loopback.
What acts on the runtime's own machine is refused: `POST /v1/shutdown`,
`/v1/devices`, `/v1/devices/rename`, `/v1/devices/rotate`, `/v1/devices/recovery`, `/v1/estate/export`,
`/v1/pairings`, `/v1/runtime/certificate`, `/v1/releases/submit` and `/v1/releases/observe` (which use the
release provider's credentials on that machine), `/v1/objects`, `/v1/runs` and `/v1/goals/{id}/step`, which
launch coding agents there (stopping a run is not refused); `/v1/harness-qualifications`, which runs local
probes and accepts a local command override; applying an upgrade, though not its plan (`check`);
a record of type `device`; and any request that names a `recovery` key file. A refusal is `401` with a problem of code `denied`,
and the connection is closed. The daemon always runs as a production host, so no answer carries a stack trace.

A record a paired device asks for is authored by the device's principal and signed as every API write is: by
an admitted device of that principal whose key the runtime's machine holds; where there is none it is written
unsigned when the charter leaves signatures optional, and refused when the charter requires them. A paired
device's key signs its requests and its connection certificates, never the records the runtime writes, so a
record's signature names a device on the runtime's machine, not the paired device that asked for it. The runtime
refuses such a write when that device's every signature needs its owner's presence, rather than ask the person at
its machine to approve what someone elsewhere sent. It keeps each write a paired device asks for in
`local-network-writes.jsonl` beside the live store, an owner-only file it only appends to, outside the records,
where the device's revocation leaves it: before the write, its intent (when, which device, for which principal,
the operation), flushed to disk, and when that cannot be kept the write is refused with `503` and nothing is
written; after the write, its outcome under the same id, written with the record's id, hash and path (for a run's
stop, the run's id), or failed. When the runtime starts, it closes every intent left without an outcome as
interrupted, naming the records its principal wrote since, one of which that write may have produced. `vericairn
devices writes [<device>]` lists each write with its state. Records signed by the paired device itself, on its own
machine, remain a future design: a remote device's writes cannot rest on a key of the runtime's machine.
Remote is not built; WATCH-01 and HANDOFF-01 do not implement or qualify its signing, transport or run observation.
Loopback does not check `profiles` yet, so devices recorded
before 0.6.0 with the default `embedded` keep their loopback access.

## Pairing

A device on another machine pairs with the runtime under an administrator's authority: the operator, or a
principal holding the `administer` scope, whose admitted device on the runtime's machine signs the
admission as `vericairn devices admit` signs it. The exchange is scheme `vericairn-pairing/1`.

1. On the runtime's machine the administrator asks for a code: `vericairn devices pair --principal <id> --by
   <administrator> [--profile <profile>]...` (`POST /v1/pairings`, refused over the local network, and over
   loopback unless the signing device is the administrator's own). The runtime checks the principal, the
   profiles (`local-network` unless others are named), the administrator's authority and that the admission
   can be signed here, and issues 16 characters of Crockford's base32 alphabet, shown in four groups of four,
   all of them secret: 80 bits from the system's cryptographic random generator, and nothing else, so no part
   of a code names it. A code works for ten minutes, pairs one device and is exhausted after five confirmations that do
   not match it; at most sixteen wait at once. The daemon keeps only the keys derived from a code, in memory,
   so a restart forgets every code. From 0.6.1 it also keeps the issuing device, that device's admission revision,
   and the operator-role revision or `administer` grant used to issue it. On loopback the issuer is the requesting
   device; on the local socket it is the admitted device selected to sign admissions for the administrator.
   The issuance log names that device and authority, never the code. Losing that device's admission or revoking or
   expiring the recorded authority invalidates the code and its open exchanges. A later readmission or restored
   grant does not revive it; another device or grant of the administrator cannot rescue it. A rename or key rotation
   that retains admission, or an authority revision that retains the role or scope without an expiry gap, does not
   invalidate it. Invalidated codes do not occupy a waiting-code slot. The code is shown once; the administrator gives it, with the address, to
   the person at the new device, who types it (case, hyphens and spaces do not matter, and I, L and O read as
   1, 1 and 0).
2. Each side derives two keys from the code with HKDF-SHA256 (RFC 5869): extract from the code's sixteen
   characters with the salt `vericairn-pairing/1`, and expand 32 bytes for the runtime (info
   `vericairn-pairing/1 runtime`) and 32 for the device (`vericairn-pairing/1 device`). The code itself never
   crosses the network.
3. The device sends `POST /v1/pairing/hello` with a 32-byte nonce and an HMAC-SHA256 under the device's key
   over the nonce and the fingerprint of the certificate its own handshake showed.
4. The runtime tries every code it holds against that MAC, with the certificate it presented on this
   connection; its MAC is exactly 32 bytes, refused unread otherwise, and a pairing message's body is read only
   as far as 16 KiB for parsing (`413` beyond), for hello and confirm alike. A MAC of the wrong length in
   either message is `422` before decoding it or processing its device. Refusals complete before bounded discard
   of any remainder, as above. A hello that proves none is refused alike, whatever was tried, and its
   connection is closed. An address that sends 50 such hellos within a minute is refused until its minute ends;
   an address is never forgotten while its minute runs, so while 1,024 addresses are counted a new one waits;
   and once 4,096 such hellos have come in all within a minute, every hello waits for the next, which bounds
   the work of trying codes. A code the hello proves is refused when it has expired, was used or has no
   attempts left, or its issuing device or authority no longer permits it. Otherwise the runtime answers a
   `PairingChallenge`: both nonces, the estate with its name, the runtime identity's public key, the
   fingerprint of the certificate it presented on this connection, the principal with its name and kind, the
   profiles, and the id, time and record format of the device record it will write, with an HMAC-SHA256 under
   the runtime's key over the hello and all of it. None of it reaches anyone who has not proved the code. A
   hello uses none of the code's attempts; at most four exchanges of a code are open at once, and a further
   hello closes the oldest.
5. The device verifies that MAC and that the certificate named is the one its own handshake showed; only
   then does it make its key, in the strongest custody of its machine unless told otherwise, held under the
   record's id. It builds the device record exactly as the runtime will write it (the principal, its name and
   platform, its custody and presence, its public key, the profiles), signs the record's content hash with
   the new key, and sends `POST /v1/pairing/confirm` with its key, its descriptive identity and that signature,
   with an HMAC-SHA256 under the device's key over the challenge, the challenge's MAC and its own fields.
6. The runtime refuses a confirmation of an exchange it did not open, of a code that expired, was used, lost its
   issuing device or authority, or has no attempts left meanwhile, and one whose MAC does not match, which ends the exchange and uses one of the
   code's five attempts; the last one closes every exchange of the code still open. It refuses a device name
   with control or formatting characters. It rebuilds the device record
   from its own challenge and the device's fields, verifies the signature with the key presented, writes the
   device record with that signature, the device's own, and then the admission by the administrator, signed
   by the administrator's admitted device; when the admission is refused the device record is removed again.
   The issuer's admission and recorded authority are rechecked under the estate write lock before either record
   is written, so revocation after confirmation validation still refuses both records with `401`.
   The device's name, platform, custody and presence are its own statement, which the runtime cannot verify,
   and the admission's note says so and names the issuing device and authority. The code is used up. It answers `PairingCompleted`, the device and
   admission ids and the record's hash, with an HMAC-SHA256 under the runtime's key over the whole exchange.
7. The device verifies that MAC and keeps the connection: the address, the estate, the runtime identity's
   public key, and its device's id and public key. A failure on the device removes the key it made, and a
   failed or lost final confirmation does not prove that the runtime recorded nothing: it may already hold the
   device and admission. Inspect `devices list` on the runtime and revoke an unwanted admission before pairing again.
   Pairing again replaces this machine's earlier connections to the same
   estate that pin another runtime identity, which the runtime replaced, or that are for the same principal;
   their devices stay admitted until an administrator revokes them.

Each transcript is unambiguous: the scheme and a label (`hello`, `challenge`, `confirm` or `completed`), then
each field as a 4-byte big-endian length and its UTF-8 bytes, a list preceded by its count and an optional field
by a byte saying whether it is present; each later transcript contains the earlier one and its MAC. MACs are
compared in constant time, and a hello is tried against every code, so how long a refusal takes says nothing of
which code, if any, came close. Every primitive is a standard one in the platform's implementation: HKDF-SHA256,
HMAC-SHA256, ECDSA P-256 with SHA-256, TLS.

### Threat model

- Someone watching the network sees TLS traffic and the TLS 1.2 handshake's certificates, including their signatures,
  as described above; the pairing messages are encrypted.
- Someone between the device and the runtime who ends TLS with a certificate of their own cannot make the
  runtime's MAC name that certificate without the code, and the device's hello names the certificate the
  device saw, so the runtime finds no code for a hello relayed to it either. The device stops at step 5,
  before it makes a key, and nothing is recorded. A test runs such a relay.
- Whoever only reaches the pairing endpoint learns nothing of a waiting code: no MAC under a code's keys, no
  estate or principal name, and no difference between a code that waits and one that never existed. Whoever
  stands between, ending TLS, obtains one MAC under a key derived from the code, the hello's, and can test
  guesses against it offline: the exchange uses no password-authenticated key exchange, so the secret is long
  enough instead. Each guess succeeds with probability 2^-80, so even odds take some 2^79 guesses, each a
  chain of HMAC-SHA256 computations, beyond reach in a code's ten minutes; and a code once used or expired is
  worth nothing. Online guesses are bounded tighter still, by 50 failed hellos an address a minute and 4,096
  in all.
- Nobody without the code can use up its attempts: a hello that proves no code uses none, and only a
  confirmation of an exchange that a proving hello opened can. An address that floods hellos is refused a
  minute at a time, and enough addresses flooding together make every hello wait a minute: a denial of
  service, as using up a code is, after which the device pairs again. A few mistyped codes from behind a shared
  address refuse no other device there.
- Whoever learns a code before its device uses it can pair a key of their own in its place, once and within
  ten minutes. The genuine device is then refused as "used already", which names no device, and `vericairn
  devices list` shows the device that did pair; the administrator revokes it. A code is shown only to the
  person pairing.
- Whoever holds a copy of the runtime certificate and its key can end TLS as the runtime, but cannot prove to
  be it: the identity, which never enters TLS, signs a proof only for a paired device that authenticated over
  the local network, only for that connection's own client certificate and only under the current
  certificate, so a proof the holder obtains, over loopback or with a device of their own, verifies on no
  connection of another device. `vericairn runtime rotate --compromised` replaces the certificate and closes
  every open connection, handshakes under way included; from then on the runtime presents and proves only the
  new one. Tests have such a holder relay the runtime's genuine proofs, and ask for proofs as another device,
  and be refused. The certificate's key is in an owner-only file on the runtime's machine, where whoever acts
  as the runtime's user also reaches that user's keys in file custody.
- An identity whose key may have leaked is replaced with `vericairn runtime rotate --identity`, after which
  every paired device refuses the runtime until it pairs again. The identity's custody decides how hard its
  key is to copy, as a device key's custody does.
- A connection's key and certificate serve a day at most and are useless alone, since every request is also
  signed by the device key. The key is in memory on Linux; macOS and Windows hold it in a keychain file or the
  user's key store while it is used, so a crash may leave it on disk there. A device key in an owner-only file
  can be copied, which is why the strongest custody is the default; a lost or copied device is revoked and
  refused at its next request.
- A captured request is accepted once, within five minutes of its signing time, only over a connection whose
  certificate the device's key issued, and only by the runtime it was signed for; no flood makes the runtime
  forget a signature before it is stale, and a copy whose body is held back until then is refused. With a
  nondecreasing runtime clock, requests whose signing time predates the new instance's startup are refused.
  Signing times up to five seconds ahead are accepted, so a restart within that lead can admit a captured request
  again; the startup rule and its clock requirement above describe that bound.
- No certificate makes either side connect anywhere: the local network speaks TLS 1.2 only, so a certificate that
  names an address is refused in clear before the platform's TLS reads it, on every platform, and nothing a
  certificate names is fetched.
- A runtime listens beyond a private network only where the operator allowed it with `--allow-public`.

Not covered here: discovery, since devices are given the address; the relay service; and records signed on a
paired device by its own key. Remote is not built; this API makes no claim of Remote support, and local-network
pairing does not qualify a Remote client.

## Operations

| Method and path | Meaning |
|---|---|
| `GET /v1/health` | The daemon's identity: API and product version, estate, root, profile, record format, start time, process |
| `GET /v1/status` | Status text and canonical status JSON, including the findings of this copy's repository checks |
| `POST /v1/status/observations` | Record a person's explicit observation of estate status (`EstateObserveRequest`); ordinary reads and agent claims never advance it. See [a person's last observation](#a-persons-last-observation). |
| `GET /v1/handoff[?maxBytes=<n>]` | Prepare a complete retained package from the records, or refuse missing sources and overflow (`HandoffResponse`). See [hand-over](#hand-over-handoff-01). |
| `POST /v1/resume` | Compare a retained `HandoffPackage` with current records (`ResumeResponse`). This POST is a read: no write-ledger entry, record or work dispatch. See [hand-over](#hand-over-handoff-01). |
| `GET /v1/progress` | The progress report, from 0.6.0 (`ProgressResponse`: the text `vericairn progress` prints, each record-derived line naming its sources; its canonical JSON, format `vericairn.progress/1`; and whether the check it shows found errors). Record-derived sections agree for the same records; repository-check findings also depend on local files. From 0.7.0, latest local agent claims and worker observations appear separately ([details](#agent-progress-claims-070-additive)). Its recorded sections: where the estate stands (the pin and the record format with the records that set them, and status's check); the work orders by the state status derives; each published release and what it contained, the release notes' derivation over the records after the publication before it up to its own, omitting superseded results and counting a current result as accepted work when a current acceptance pins its exact hash or its work order's current revision and hash (including another principal's results without accepting those result records; an earlier work-order revision's acceptance does not count); what comes next by release, which until goals plan releases is each release work order not yet accepted (a work order whose key begins with `REL-`) and the work orders among its inputs; the next eligible work, the planned work orders that nothing holds ([record-format-v2.md](record-format-v2.md#what-holds-a-work-order)); and what waits on the operator: the asks put to an operator and awaiting an answer, with their recommendations, and the acceptances only an operator gives (milestone work orders submitted, results awaiting review toward milestones not accepted, release candidates without an operator's acceptance of their exact hash, and goals ready for an operator's acceptance) |
| `GET /v1/validate`, `GET /v1/check` | Findings of validation, and of validation plus the repository checks. `record.subject-stale` and `acceptance.unresolved` apply only while the referring record is current; superseded judgments retain historical integrity validation but no longer need reconsideration. A stale replacement is still flagged. |
| `GET /v1/records/{id}` | A record with its path, hash, whether it is current, and the whole envelope |
| `GET /v1/records?type=<type>` | The current records of one type, oldest first, each with its envelope; empty for a type with none |
| `POST /v1/records` | Author a record (`NewRecordRequest`): signed with the author's admitted device held on the runtime's machine. When none is held and the estate's policy leaves signatures optional (the default), it is written unsigned, except an admission from record format 2, which is always signed. The record is validated with the estate and removed again when the estate would be invalid. |
| `GET /v1/decisions`, `GET /v1/decisions/{id}` | Current decisions; one decision by the id of any revision |
| `GET /v1/work/{key}/brief`, `GET /v1/work/{key}` | The brief of a work order; what an observer sees |
| `POST /v1/work/assign`, `attempt`, `result`, `amend` | Delegate, start, submit, request an amendment |
| `POST /v1/work/progress` | Publish the contributor's latest claim on its own attempt (`WorkProgressRequest` / `WorkProgressResponse`); active notes stay local and a completed phase is recorded once. See [agent progress claims](#agent-progress-claims-070-additive). |
| `POST /v1/work/takeover` | Hand a step an agent holds over to a person, from 0.6.0 (`TakeoverRequest`: the agent's assignment, by, and optionally the person who takes it over, the recording principal when omitted, and a note). Refused, before anything is recorded, unless the assignment is an agent's and still holds its work order, which is neither accepted nor retired, the person is a person, and the recording principal is a person too: the one taking the step over, the sponsor of a goal the step belongs to, or an operator; an agent never records a hand-over, whatever scope it holds, and the records refuse one it writes by any route (`assignment.handover`). The hand-over is a record: a new assignment of the work order to the person, carrying the agent's brief with the agent's assignment among its sources, which holds the step from then on. Every run of the agent's assignment still in flight is asked to stop, as `POST /v1/runs/{id}/stop` asks it. A new run on the old assignment is refused before intent, whether named explicitly or supplied by routing; nothing is recorded or charged. Returns a `TakeoverResponse` (message, ok, the assignment written, each stop's `RunStopResponse`, problem) |
| `GET /v1/devices`, `POST /v1/devices`, `POST /v1/devices/admit`, `POST /v1/devices/revoke`, `POST /v1/devices/rename`, `POST /v1/devices/rotate`, `POST /v1/devices/recovery` | Devices and admission; a device is created, renamed and rotated on the machine that holds its key, and the operator's recovery device is made there and its key written once for offline keeping. Admit and revoke accept the recovery key's path and passphrase. From 0.6.0 each `DeviceRow` gives the device's permitted `profiles`; a device on another machine is recorded by pairing. |
| `GET /v1/releases`, `POST /v1/estate/upgrade` | Release records; plan (`check`) or apply an upgrade to a published release (applying is refused over the local network) |
| `GET /v1/documents` | The declared living documents (`DocumentRow`: path, repository, declaration, title, state current, due or unavailable, level, reasons, latest clearance, unavailable contributions, notes) |
| `POST /v1/documents/{id}/reviews` | Review a living document by its declaration (`DocumentReviewRequest`: by, outcome, summary, findings, commit, and from 0.6.0 independence). The runtime reads the content, records its hash, the covered paths' identity and the triggers the review answers, records the reviewer's independence and runs the declared checks; a failed check, or the declaration's `clearedBy`, refuses a clearance. An estate file's revisions decide its reviewer's independence, and a stated one that differs is refused; for a document of another repository the runtime records the one stated, or without one `self` for a principal with the builder role and otherwise its judgement from the estate's records ([record-format-v2.md](record-format-v2.md#living-documents-due)). Returns a `WriteOutcome`. |
| `GET /v1/goals`, `GET /v1/goals/{id}` | The current goals (`GoalRow`: id, hash, title, status, sponsor and the sponsor's name), and one goal by the id of any of its revisions (`GoalResponse`: the text `vericairn goal show` prints and its canonical JSON (living-document predicates also depend on the content and contributions available here): the goal's record and revision, its sponsor, its status, `proposed` before any status and `achieved` once accepted, the budget its activation grants unit by unit, each condition's verdict, `met`, `not-met` or `unknown`, with its reason and the records it read, beside the current evaluation's verdict, the evaluation, the steps, who accepts it, whether it is ready for acceptance and what stands in the way, and its acceptance). An id that names no goal is answered 404 |
| `GET /v1/asks`, `GET /v1/asks?to=<principal>` | What waits on a principal, or on every principal, from 0.6.0 (`AsksResponse`: the text `vericairn ask` prints and its canonical JSON, format `vericairn.asks/1`, the same on every machine for the same records). For each principal with an ask still open or notified: its waiting asks oldest first, at most three in one delivery (`delivered`, each with its position, its recommendation first, the question, the consequence of leaving it unanswered, its options, its goal and its subject) and the rest waiting in order behind them (`queued`, by id and position). An answered or withdrawn ask waits on no one. Reading it answers nothing: an ask is answered by a decision of the principal it is put to that names it in its `answers`, and no MCP tool records one |
| `GET /v1/goals/{id}/next` | A goal's steps routed from the runtime's machine, from 0.6.0 (`NextResponse`: the text `vericairn goal next` prints and its canonical JSON, format `vericairn.next/1`): the goal's ledger and why a new run of it would be fenced; each step of its work with the state status derives, what holds each planned one, and for each eligible one its route, the principal its role's roster reaches or why none is available ([record-format-v2.md](record-format-v2.md#routing-a-goals-steps)), with every candidate and why it is or is not available, whether the goal's budget allows the step now, and for an agent the custody guard on the runtime's machine; the step that comes next, the first eligible one whose route acts; and the goal's asks. It reads that machine's launchers and keys, so it is a view of that machine's ability to carry the work. Reading it records and starts nothing; an id that names no goal is answered 404 |
| `POST /v1/goals/{id}/step` | Take an active goal's next step, one at a time, from 0.6.0 (`GoalStepRequest`: by, and for an agent's step the repository, the launcher's settings, wallMinutes, branch and base): the first eligible step whose route acts, as `GET /v1/goals/{id}/next` routes it from the runtime's machine. Refused over the local network because it runs the worker on this machine; on loopback `by` must be the signing device's principal. A step is taken by an operator, by the goal's sponsor when the sponsor is a person, or by a principal holding `local-engineering`, which is how the builder dispatches agents under the operator's authority; anyone else is refused before anything is recorded. An agent's step goes on with the assignment that holds it, or, when none does, routing signs an assignment to the agent, marked `routedWith: run`, and stores it only with the run's intent, after the worker's whole preflight, the two judged together, their content checks included, before either is stored, under a journal on the runtime's machine; an assignment whose run's intent was not stored holds nothing of its step, and `GET /v1/reconcile` reports such a batch as `batch.incomplete` until the step is held again or is no longer planned: a refusal of that preflight (no repository or an unknown one, a cap under one wall minute, a setting the launcher does not take, the custody guard with no custody exception in force, the goal's budget, the launcher's verification, the workspace's preparation) records nothing, the assignment included. A harness that cannot start after the intent is recorded leaves the assignment and the run, recorded as a known failure that spent nothing. A person's step, or a reserved one routed to a person who holds it, is assigned to that person and asked of them, the assignment (marked `routedWith: ask`) and the ask naming it stored the same way; a step no principal is available for is asked of who decides for the goal and assigned to no one. Every routed brief prohibits the reserved actions unless a grant in force covers one, and names the grant that lets its principal carry a reserved step. Every ask states its recommendation and the consequence of leaving it unanswered, and its subject is the work order. With every eligible step held it records nothing and says what holds each. It activates, funds, accepts and answers nothing. Returns a `GoalStepResponse` (goal, outcome `run`, `ask` or `none`, message, ok, the step's key, its route, the principal reached, the assignment and the ask written, the `RunResponse`, problem) |
| `POST /v1/goals` | Propose a goal, or a revision of one (`GoalProposeRequest`: by, the goal record's `body`, `supersedes` naming the current revision): a proposal authorizes and funds nothing, and a revision starts again from `proposed`. Anyone may propose a new goal; a revision is refused, before anything is written, unless its author is the sponsor of the revision it supersedes, a person, or an operator when an agent sponsors that goal (decision 01M50A8KHJT1MSSS84KZKHKRHB); anyone else proposes the change as an ask to the sponsor. Returns a `WriteOutcome` |
| `POST /v1/goals/{id}/activate`, `pause`, `resume`, `stop` | A goal's lifecycle (`GoalActionRequest`: by, an optional note): activate a proposed goal, or resume a paused, budget-limited or blocked one, which grants its budget; pause an active goal; stop a goal that has not ended, which records it abandoned. Activation and resumption are by who decides for the goal: its sponsor, a person holding the operator role or a grant of `spending`, or an operator when an agent sponsors it. Pausing and stopping are also available to the sponsor when the sponsor is an agent; an agent never activates a goal. Each is refused out of order, and after the goal ended or was achieved. Returns a `WriteOutcome` |
| `POST /v1/goals/{id}/evaluate` | Record the evaluation of a goal's current revision (`GoalEvaluateRequest`: by, summary, `verdicts`, each a `GoalVerdict` of the condition's position from 1, the verdict `met`, `not-yet` or `impossible`, the reason and the ids of the records read). The runtime gives each record predicate its own verdict with the records it read and refuses an evaluator's verdict that contradicts it, except `impossible` for one it does not find met; each evidence statement needs the evaluator's verdict, and one found met names the records read. Refused for a principal that authored one of the goal's steps. Returns a `WriteOutcome` |
| `POST /v1/goals/{id}/accept` | Accept a goal's current revision, relying on its current evaluation (`GoalAcceptRequest`: by, statement). Refused, with every reason at once and before anything is written, unless the principal is the one who accepts it (decision 01M507XV4VBFYWMRN8WCXMXHX4: its sponsor when the sponsor is a person, or the principal the sponsor delegated an ordinary goal's acceptance to while it holds the `accept-work` scope; an operator, only an operator, when an agent sponsors it), the goal was activated and has not ended, its current evaluation finds every condition met and is not the principal's, the principal authored none of its steps, and every record predicate holds now. Returns a `WriteOutcome` |
| `GET /v1/providers` | The provider and adapter implementations available to the runtime on its machine (`ProviderRow`: provider, kind release or source, and from 0.6.0 launcher or workspace, implementation, contract, status, operations, limitations, declared settings with defaults, the destination setting, the served locations). Being available selects nothing. |
| `GET /v1/launchers` | The registered coding-agent launchers, providers of `vericairn.launcher.agent/1`, as `ProviderRow`s of kind launcher. Registration is not dispatch admission: RUN-01 requires explicit configuration and a complete inventory; generic dispatch is refused. |
| `POST /v1/harness-qualifications` | Qualify an exact native harness using local probes (`HarnessQualifyRequest`: launcher, by, optional settings); returns `WriteOutcome`. Machine-local: paired local-network devices are refused before probes or writes. Only a human operator or human with an explicit `qualify` grant may author it. See [harness qualification](#harness-qualification-run-03). |
| `POST /v1/runs` | Start a coding agent's run on an assignment (`RunStartRequest`: assignment, launcher, agent, by, repository, and optionally goal, branch, base, wallMinutes and the launcher's settings), the worker contract `vericairn.worker/1` ([software.md](software.md#coding-agents-the-worker-and-its-launchers)). Refused over the local network, and, before anything is recorded, when the agent is not the assignment's contributor or has no admitted device whose key the runtime's machine holds, when the recording principal is neither the operator, nor holds `local-engineering`, nor is the sponsor of the goal the run is charged to when the sponsor is a person (from 0.6.0), when the work belongs to no active goal or the goal's budget does not allow the run, when a device of an operator or of the goal's sponsor can sign there without its owner's presence (the refusal names each device and why) and the goal's sponsor recorded no custody exception in force for the goal (from 0.6.0; judged before the workspace is prepared and again when the intent is recorded, so an exception revised or expired meanwhile lifts nothing; a run launched under one names the exception in force when its intent was recorded, and the response says what it lifted; judged a last time immediately before the launch, when an exception that lapsed or a key that arrived after the intent launches nothing and the run's next revision is `canceled`, neither in flight nor charged, the response's status `canceled`), when the launcher cannot run (Codex's goals feature is verified first), or when the workspace cannot be prepared. Otherwise the workspace is prepared, the intent recorded with the run's cap, and the harness launched; the response returns once it started, and the runtime observes and settles the run. Returns a `RunResponse` (run, status, message, ok, the operation revision, the cap, the workspace, problem). From 0.6.0 |
| `GET /v1/runs` | The agent runs (`RunsResponse`): each run (`RunRow`: run, identity, launcher, agent, assignment, goal, workspace, status, cap, usage once settled, what it is charged, a stop's facts, its last heartbeat here, whether a worker here observes it now, whether its launcher is generic, optional event-stream `liveness` and settled workspace `scope`), and the ledger of every goal with a run or active now (`LedgerRow`: each charged unit's budget, spend and remainder, the runs in flight, the concurrency, the attempts per step, and why a new run would be fenced). From 0.6.0 |
| `POST /v1/runs/{id}/stop` | Ask a run to stop (`RunStopRequest`: by, reason, graceSeconds) and report what was observed (`RunStopResponse`: run, state, message, ok, stop): the request, its transmission to the harness, the harness's acknowledgment, an escalation after the grace period and the observed containment are separate facts in `stop`, and `state` is the furthest one observed. A stop that is not observed is reported as `requested`. On every profile, refused unless `by` is an operator, decides for the run's goal (its sponsor, or an operator for a goal an agent sponsors) or started the run. The main observation wait is capped at 30 seconds, whatever grace the request names, with a bounded settlement wait after exit; the response reports what is observed so far, and clients poll for later facts. From 0.6.0 |
| `POST /v1/releases/submit` | Submit a release as intent before effect, on the runtime's machine (refused over the local network, as observing is, since both use the provider's credentials there): the provider is the request's `provider`, else the one the product record declares (neither is refused; a provider with no implementation on the runtime's machine is refused, never replaced); the settings are the provider's defaults, then the product's declared settings, then the request's `settings` (and `repository`, `ref`, `workflow`, kept from 0.3.1 as settings of those names). The operation record is written, then the build is dispatched; nothing is published. Before any intent, the product's living documents can refuse it ([software.md](software.md#release-providers-and-the-products-release-profile), step 1). Returns a `ReleaseResponse`. |
| `POST /v1/releases/observe` | Observe a release's run, draft and publication through the provider its operation records and record what settled: the operation revision, the candidate release with every artifact classified by the product's profile (or the generic rule), and later the observed publication; or cancel an intent that had no effect. The operation is found by `provider` and `repository` (the destination), by `product` and `version`, or by a destination one current operation alone matches. Returns a `ReleaseResponse`. |
| `POST /v1/estate/export` | Write a verified export to a path on the runtime's machine; no keys or authority travel with it. From 0.6.0 a device-signed export names its device's principal as `by`, which must be an operator or hold the `administer` scope; over the local network it is refused |
| `GET /v1/reconcile`, `GET /v1/recover/inspect` | Reconcile tasks left by diverged copies and, from 0.6.0, `batch.incomplete`: records the runtime began storing together on its machine and stopped storing part-way, naming what was stored and what was not; what this copy holds |
| `GET /v1/objects`, `POST /v1/objects`, `GET /v1/objects/{hash}`, `GET /v1/objects/verify` | Content-addressed objects: list, store bytes (refused over the local network), verified bytes, verify all |
| `POST /v1/shutdown` | Ask the daemon to stop; closing a client never stops it |
| `POST /v1/pairings` | From 0.6.0, issue a pairing code (`PairingIssueRequest`: principal, by, profiles) under an administrator's authority, on the runtime's machine only. Returns `PairingIssued`: the code, the principal and its name, the profiles, the expiry, the attempts, the address and the runtime identity's fingerprint; 409 when the runtime serves no local-network profile or sixteen codes wait, 422 when refused |
| `POST /v1/pairing/hello`, `POST /v1/pairing/confirm` | From 0.6.0, the pairing exchange (`PairingHello`, `PairingChallenge`, `PairingConfirm`, `PairingCompleted`), over the local network only and without a device credential, since the code authenticates it |
| `GET /v1/runtime/identity?nonce=<nonce>` | From 0.6.0, over the local network only, to a paired device: the runtime identity's proof for a nonce over the certificate this connection presented and the client certificate presented on it (`RuntimeIdentityProof`); 404 on the other profiles, 409 on a connection that began under a certificate since replaced |
| `POST /v1/runtime/certificate` | From 0.6.0, replace the runtime certificate now (`CertificateRotateRequest`: compromised, reason, identity), on the runtime's machine only. Returns `CertificateRotated`: the new and the replaced fingerprints, the new expiry, whether open connections were closed and how many, and the new identity's fingerprint when it was replaced; 409 when the daemon serves no TLS profile |

The pairing routes, the identity proof and the certificate's rotation are served by a running daemon only:
the embedded runtime has no listener and keeps nothing between commands. So `vericairn devices pair` is
refused when no daemon answers, and `vericairn runtime rotate` without one replaces the certificate file
for the next start.

Restore, `recover compare`, `docs notes` and `docs regenerate` do not go through the API. Restore
creates another copy. The other three load this copy (and, for compare, the other copy too) directly
from its files and live store, even while a daemon serves it.

## Worker run observations

Before a native run, configure model and effort defaults on the runtime's machine with
`vericairn worker configure --launcher codex --model <model> --effort <effort>` (or `claude-code`).
FIX-18 adds `max` to Codex's advertised native efforts for 0.7.2, including API run admission; the harness
validates model/effort support at launch, and an observed nonzero exit is recorded as `known-failure`.
The stock CLI and daemon read that machine's configuration for each run and agent goal step. Missing or
invalid configuration refuses before intent; explicit API run choices cannot replace it. Optional allowlists
narrow choices without widening the qualified inventory. See [explicit execution](#explicit-agent-run-execution-run-01-070)
for the local configuration boundary and the recorded execution snapshot.

The runtime also rereads the machine-local workspace root set by
`vericairn worker configure --workspaces <absolute-directory>`. This CLI setting has no HTTP or MCP route
and applies to every launcher. The default location is unchanged; a configured root places new workspaces
at `<root>/<estate>/<branch-slug>`. Invalid roots refuse before intent. A root changed after intent refuses
dispatch and settles as an uncharged known failure. Workspace paths never enter records. See the
[software guide](software.md#explicit-execution-and-environment-admission-run-01-070) for root validation
and reuse of an existing branch worktree.
Choose a root outside a home holding `.claude`, `.codex`, `.agents` or `.mcp.json`; the root and its ancestors
cannot contain these markers. Codex separately inspects the user's own `~/.agents`, so moving a workspace
does not admit user skills or other customization there. An empty ordinary directory or recognized instruction
files alone are allowed by that native scan; use a worker account without the discovery root for native runs.

Every run's assignment is checked against its work order's current holder before preparation and again
before intent. An assignment displaced by hand-over is refused on `POST /v1/runs` as well as routed goal
steps, without an intent or budget charge. No request or response field changes.

Settlement withdraws the run's still-waiting process attention asks, preserving the questions and
observations and pinning each withdrawal to the exact settlement. Already answered asks stay answered;
closed asks no longer produce stale-subject warnings. `attempt-missing` and `scope-settlement` asks
are created with the settlement and remain open for review at that exact revision and hash. The
settlement, new review asks and withdrawals are validated together and journaled; storage can still
be interrupted. This is withdrawal of obsolete process attention, not an answer or work acceptance.
FIX-12's credential scan still exempts only validated device `body.publicKey` and envelope
`signatures[].signature` values; other fields, signature verification and machine-path checks remain.

A goal's pause or stop changes its recorded lifecycle and prevents new runs; it does not signal runs already in
flight. Use `vericairn worker stop <run> --by <principal>` for each run that must stop, then inspect `worker runs` for
observed containment. In 0.7.2 `limits.deadline` (including `goal propose --deadline`) and `limits.consecutiveFailures`
are stored and validated but not enforced by routing or the worker. The worker enforces its run thresholds and
`stallSeconds`; operators must act on deadlines and consecutive failures themselves.

`RunStart` authors the run as its supplied recording caller. Every physical launch needs a new `agent-run` intent and
fresh budget admission. A settled identity cannot reopen, change status or acquire another dispatch timestamp; its
target and cap stay pinned to the intent. Every record newly reserving allowance is checked for budget admission.
From 0.6.1 all previously recorded settlement facts stay unchanged, including usage, scope and containment; the
ledger takes usage from the first settlement. Evidence may only be appended, and missing stop facts or optional
fields may be added only under the existing record and chronology rules, without granting a new status transition.
Before admission the worker refuses linked `.vericairn` metadata and exports files without following links.

Codex and Claude Code receive fresh configuration directories containing only their approved harness login. Codex
uses the canonical workspace path for project trust and verifies the effective MCP list: merged ambient servers or
changed transport settings refuse launch. Claude Code excludes user, project and local setting sources, disables hooks
and enables no plugins, alongside strict MCP configuration. Detected managed settings and known organization logins
refuse launch; remote or undetected managed policy is not qualified and managed accounts are unsupported. A
configuration refusal after intent but before process launch settles as an uncharged known failure. These controls
do not isolate same-user files or keychains. FIX-18's five live Codex runs qualify only the reported run
path; they do not qualify other accounts, versions or platforms, or Claude Code live sign-in. See
[software.md](software.md#native-inventory-qualification-run-02-and-run-03) for those observations and
[the configuration limits](software.md#harness-configuration-and-workspace-exports).

A state or dispatch write failure after launch keeps the allowance reserved. The worker retries state writes and
recovers an observed dispatch before recording settlement; an unavailable recovery leaves the intent in flight. An
uncertain response is not proof that no process was created. `ok` means a launch was observed and supervision
established; `status` may still be `not-started` if the dispatch record could not be persisted. The response explains
this case.

New intents state `target.capMode: "soft"` and record their native controls and thresholds in evidence. The full cap
is reserved, while operational thresholds leave grace plus a heartbeat of wall headroom and ten percent of
token/currency headroom for usage in flight. Native controls can overshoot and unsupported units are explicitly soft;
the ledger's remainder can therefore become negative. Elapsed wall usage and stop grace use a monotonic clock.
Malformed, incomplete or overflowing token usage, including an unfinished turn, leaves the token unit unreported and
charged at its cap.

`GET /v1/runs` adds two optional observations to each `RunRow`:

- `liveness`: `stallSeconds`, `lastEventAt`, optional `attentionAt`, `attention` (ask id) and `stoppedAt`. The worker
  observes stdout events with a monotonic clock. The launcher default is 600 seconds, changed by its `stall-seconds`
  setting (1–86400), then overridden by the current roster's `limits.stallSeconds` and finally the goal's. The goal and roster are read again when the intent pins the limit. One limit of silence raises an attention
  ask; twice the limit stops and contains the run. Events reset silence but never erase attention. An in-flight row
  reads local observations; settlement records them in the operation.

- `scope`: `changed`, `outsideCount`, `outside`
  and optional `problem`. Settlement compares file inventories before launch and after boundary exit against
  repository-relative paths/globs in `brief.permittedChanges`. The inventory reads the physical workspace independently
  of Git configuration and verifies its root identity against the launch baseline. From 0.6.1 it also compares the run
  branch's head and every commit reachable from it but not from the prepared baseline commit against that baseline,
  including merged side branches. All changed paths count, including changes restored in the worktree or a later
  commit. The pinned original object/reference directory is read through isolated Git metadata that loads no
  agent-writable configuration, replacement refs, grafts, attributes or external diff drivers. Renamed, deleted,
  untracked and ignored changes count. Missing history, non-regular files or references, an unavailable helper or a deadline mark the audit incomplete;
  each inventory and the pattern comparison have a 30-second deadline, with bounded helper cleanup. Outside changes or
  incomplete audit are flagged in the observed summary. At most 1000 outside names are listed; unsafe names are hashed.
  Committed history shares the settlement inventory's deadline, with at most 10,000 new commits and 100,000 distinct
  committed paths and bounded metadata and Git output reads. With `problem` present, counts can be partial and zero
  outside paths does not mean clean. Git administration itself, changes outside the workspace, uncommitted changes
  reverted before settlement and commits no longer reachable from the run branch are excluded. This is an observation, not
  attribution, acceptance or a filesystem sandbox.

A stop's acknowledgment and escalation follow transmission; containment follows all prior facts and does not follow
`observed.at`. Stop facts remain unchanged across revisions. Transmission is timestamped after delivery attempts
finish, rather than at an earlier heartbeat. Windows transmission creates the stop file; POSIX also attempts SIGTERM
to the owned process group. The stop endpoint's bounded wait can return before containment; clients poll
`GET /v1/runs` for later observations. Containment requires a successful inventory showing the whole Windows Job
Object or POSIX process group exited, not just the root process. Failed inventories and escalations are retried without
claiming containment or a kill. POSIX group signals require the unreaped leader's ownership. A lost observer leaves
the reservation in flight while a detached lifecycle owner retains native cleanup responsibility. POSIX group/session
escape and worker crash are not contained; PID 1 and auto-reaping hosts refuse launch. Windows job assignment has a
creation gap before the job owns the suspended child. See [platform limits](software.md#launch-accounting-and-containment).

## Outcomes and problems

A write answers with a `WriteOutcome`: either `written` (path, id, hash) or `problem`, never both. A
problem has a stable `code`, a `message` that names the permitted next action, and `findings` when a
record was rejected:

| Code | Meaning | HTTP |
|---|---|---|
| `rejected` | The estate would be invalid with this record; the findings say why; nothing was written | 422 |
| `refused` | Malformed input, an unknown type, no admitted device key on this machine where the estate requires signatures (or for an admission from record format 2), or a limit; from 0.6.0 also a run's stop asked by a principal who may not stop it, and a local-network write whose paired device the runtime cannot keep; with FIX-09, a loopback request when all eight slots for body reads awaiting signature verification are occupied | 422 (409 for upgrade and export, for a pairing code when no local-network profile is served or sixteen codes wait, and for a rotation without TLS; 413 for an object above the size limit, a request above the HTTP body limit or a pairing message above 16 KiB; 503 for a local-network write whose paired device cannot be kept or full loopback authentication capacity) |
| `not-found` | The subject named does not exist | 404 |
| `invalid` | The estate has errors and the operation needs a valid estate (upgrade), or an export request lacks its output path or names a malformed exporter | 409 |
| `denied` | A loopback request without a valid signature by a device admitted now; from 0.6.0 also a device-signed request naming a principal other than its device's, or stopping the daemon or replacing its certificate without an administrator's authority; a local-network request over a connection whose client certificate the device's key did not issue (one refusal, whatever the device), from a device revoked, not admitted or not permitted that profile, acting on the runtime's own machine, or whose write would be signed here by a device that needs its owner's presence; a JSON body in another charset than UTF-8 or with a byte order mark; a signing time before this runtime instance started accepting requests or more than five seconds ahead of its clock; a body that arrives after its signature is stale, or after its device was revoked; a write whose requesting device lost admission, rotated its key or lost its required profile before the estate write lock was taken; and a pairing hello that proves no code, or a code expired, used or out of attempts, or a confirmation that does not match | 401 |
| `provider` | Observing a release, the provider refused or could not say what happened; a receiptless intent may already have been revised to uncertain before that provider call. Starting a run, the harness could not be started, and the run is recorded as a known failure that spent nothing | 200, inside the `ReleaseResponse` or the `RunResponse` |
| `reconcile-required` | An unreadable local batch journal holds all further single and grouped record writes until local reconciliation; reads remain available | 422 for `WriteOutcome` routes; inside the response on composite routes |
| `conflict` | From COMMS-01, the communication scope changed since the expected hash, or has divergent current revisions; refresh and reconcile before submitting | 409 |

The release routes (`POST /v1/releases/submit` and `POST /v1/releases/observe`) answer every request
they handle with 200 and a `ReleaseResponse`: `ok` says whether the request advanced as asked or there
is nothing to record yet, and a refusal is its `problem`, not an HTTP status. The run routes (`POST
/v1/runs` and `POST /v1/runs/{id}/stop`, from 0.6.0) answer the same way, with a `RunResponse` or a
`RunStopResponse`; a run that is not found is a `problem` with code `not-found` inside. So do the step and the
hand-over (`POST /v1/goals/{id}/step` and `POST /v1/work/takeover`, from 0.6.0), with a `GoalStepResponse` or a
`TakeoverResponse`. Other reads of a missing subject answer 404 with an empty body; the context responses above carry their own problem.

Exit status and HTTP status report whether the operation itself completed, except on the release, run, goal-step, takeover, context and hand-over/resume routes, which answer the requests they handle with `200` and carry a refusal in the body's `problem`; the observed state is
in the body, and partial, pending and changed-target states are never flattened into success.

## The runtime command family

| Command | Does |
|---|---|
| `vericairn runtime serve` | Serve the estate in this process until Ctrl+C or `runtime stop`; refuses when a daemon already serves it; `--no-loopback` disables loopback TLS; a configured local-network listener still serves; `--detached` writes the daemon's output to `daemon.log` beside the live store instead of the standard streams, which is how `runtime start` runs it |
| `vericairn runtime start` | Start a daemon in the background and wait until it answers; the daemon writes to `daemon.log` beside the estate's live store, and one that stops before serving is reported with what it wrote there and where the log is |
| `vericairn runtime stop` | Ask the daemon to stop and wait until it has |
| `vericairn runtime status` | Whether a daemon serves the estate and where (the local socket, loopback, and the local network with the runtime identity's fingerprint), or whether commands run the embedded runtime; and the local-network address configured |
| `vericairn runtime install [--plan]` | Install the daemon as a per-user service that starts at login and restarts after failure: a launchd agent (`~/Library/LaunchAgents`), a systemd user unit (`~/.config/systemd/user`) or a Task Scheduler logon task; `--plan` shows the unit and the commands without installing |
| `vericairn runtime uninstall` | Remove that service |
| `vericairn runtime configure [--local-network <address:port>\|off] [--allow-public]` | From 0.6.0, the address the local-network profile listens on from the runtime's next start, or off; without options, the configuration. A wildcard address, or one other networks may reach, needs `--allow-public`. Kept in `runtime.json` beside the live store |
| `vericairn runtime rotate [--compromised] [--identity] [--reason <text>]` | From 0.6.0, replace the runtime certificate now: through the daemon when one serves the estate, otherwise in the file the next start serves. `--compromised` also closes every open connection; `--identity` also replaces the runtime identity, and every paired device pairs again |

The service unit lives in the user's own configuration and names this machine's paths, because a service
manager needs them; nothing of it enters a repository.

## Pairing and connections on the command line

| Command | Does |
|---|---|
| `vericairn devices pair --principal <id> --by <administrator> [--profile <profile>]...` | On the runtime's machine, with the runtime serving the local-network profile: issue a pairing code and show it once, with its expiry, its attempts, the runtime identity's fingerprint and the command to run on the new device |
| `vericairn devices writes [<device>]` | On the runtime's machine: what paired devices wrote over the local network, when, which device, for which principal, the operation and the record it produced, from `local-network-writes.jsonl` beside the live store |
| `vericairn connections add <address> --code <code> --name <name> [--custody <custody>] [--presence true\|false]` | On the new device: pair with the runtime at the address, keep the connection, check it, and name the earlier connections to the same estate it replaced |
| `vericairn connections list` | Check every connection now and show its state |
| `vericairn connections remove <id>` | Forget a connection on this machine; the device stays admitted until an administrator revokes it, and its key stays in this machine's custody |

Connections are kept for the user under the Vericairn home (`connections/<id>.json`, owner-only), never in a
repository. Their states are the state language's connection vocabulary: `connecting` while a check waits for
its first answer (the SDK reports it; a command shows where the check ended), `current` when the runtime
proved the paired identity, accepted the device and answered every read, `partial` when it proved its
identity and answered only some reads, `denied` when it refused the device, `cached` when nothing answered but
an earlier check was current, shown with when, and `unavailable` otherwise. A connection proves nothing about
the state of workers.

## The SDK

In a .NET 10 project, install the Community SDK with:

```sh
dotnet add package Vericairn
```

The `Vericairn` package brings `Vericairn.Sdk`, whose namespace and assembly stay unchanged, along with
`Vericairn.Protocol` and `Vericairn.Records`. Records is required by public SDK types such as
`DeviceRequestSigner` and `PairingOptions.KeyStore`. The front door adds no assembly; keeping the SDK's
existing package identity avoids duplicating its types. `Vericairn.Runtime` and `Vericairn.Daemon` remain
separate packages for embedded implementations and daemon hosts, and `Vericairn.Mcp` remains the protocol
adapter for MCP hosts. Daemon hosts also need the ASP.NET Core shared framework. None contains Pro code.

Install and start the command line for a local estate with:

```sh
dotnet tool install --global dotnet-vc
dotnet vc doctor
dotnet vc --estate <estate root> runtime start
```

The command tables in this draft also show the archive spelling, `vericairn ...`; a NuGet installation
uses `dotnet vc ...` with the same arguments. See the [installation draft](install.md) for local tool
manifests and self-contained archives.

`new VericairnClient(endpoint)` connects over the local socket; `VericairnClient.Loopback(endpoint, signer)`
connects over pinned loopback TLS and signs every request with the signer, which `DeviceRequestSigner.Find`
builds from a device key held on this machine. `VericairnClient.IsAlive(endpoint)` says whether a daemon
answers. The client implements `IEstateApi`, so code written against the interface runs unchanged against
the embedded `EstateService`. From 0.6.0 it also has `Launchers()`, `RunStart(request)`, `Runs()` and
`RunStop(run, request)`, the worker's operations; with the daemon, a run started through it goes on after
the request returns, and a client's absence never stops it. `GoalNext(goal)` reads a goal's steps routed from the
daemon's machine, and `Asks(principal)` what waits on a principal, or on every principal when it is null;
`GoalStep(goal, request)` takes a goal's next step, and `WorkTakeover(request)` hands a step an agent holds over to a
person.

From 0.6.0, `VericairnClient.LocalNetwork(connection, signer)` connects to a paired runtime over the local
network. `RuntimeConnections` keeps this machine's connections: `Add` pairs and keeps one, `List`, `Find` and
`Remove` manage them, `Probe` checks one (reporting `connecting` first, then the state it came to) and
`Connect` returns a client for one. `RuntimePairing.Pair` is the device's side of the exchange alone, and
`LocalNetworkCredential` issues a connection's client certificate with a device key; its `KeyStorage` says
where each platform's TLS holds the connection's key, and a `TimeProvider` may drive its renewal. The client
signs every local-network request after the identity proof with `vericairn-request/2`, for the estate and the
identity it pinned. `TlsScreenStream`, which the SDK and the runtime put under their local-network TLS, refuses a
peer certificate that names an address to fetch before the platform's TLS reads it; its `Protocols` (TLS 1.2)
and `CipherSuites` are what both ends of the local network speak. No SDK client follows an
HTTP redirect. On the runtime's machine `IssuePairing` asks for a code and `RotateCertificate` replaces the
certificate; `RuntimeIdentity(nonce)` asks for a proof, which a runtime gives over the local network only.
`ConnectionStates` names the states. A refusal while a local-network connection is set up surfaces as an
`HttpRequestException` whose inner `RuntimeConnectionException` says `denied` or `unavailable`.

## Changes visible to scripts

From 0.6.0, `GET /v1/devices` and `vericairn devices list --json` give each device's `profiles`; `vericairn
runtime status --json` adds `localNetwork`, the address configured, and its `daemon` carries the
`localNetwork` endpoint when one is served, as the endpoint file does. A loopback request signed by a device
whose principal is not the one the request names as acting is refused. Sign a device-authenticated request with a device of the principal it acts for.

`vericairn estate upgrade --json` prints the plan with camel-case names (`fromPin`, `toPin`, `fromFormat`,
`toFormat`, `migrations`, `release`, `artifacts`, `applied`, `migration`, `anchored`), the same shape the
API returns. `anchored`, new in 0.5.2, lists the admissions recorded without a signature that the move to
record format 2 anchors, and is `null` otherwise.

## Agent progress claims (0.7.0, additive)

`POST /v1/work/progress` accepts `{attempt, by, phase, note, completed?}`. The default `completed` is false. It returns `WorkProgressResponse` with `claim`, optional `written`, `problem`, and `ok`; refusals are in the response. Only the attempt's contributor may submit. A phase completion is idempotent for the same note and cannot be revised or reopened. Other submissions after the attempt's result are refused.

For a paired device, the local-network write ledger records successful interim notes without a record identity,
completed phases with the returned record id, hash and path, and refusals as failed. Hand-over and resume remain
reads and add no write-ledger entry.

`GET /v1/progress` adds `json.agentClaims`, the latest claim per attempt: `attempt`, `by`, `phase`, `note`, `at`, `completed`, and optional `record`. Its text and Workbench show the same labelled claims. Interim notes are local live state and are not worker observations; recorded completions provide the fallback on another machine. These additions do not change any existing field.

`GET /v1/runs` adds optional `signals` on each `RunRow`: worker observations with `code`, `severity`, `at`, `summary`, optional `fingerprint`, `count` and `attention`. The same observations are retained in the settled run record. No raw invocation input is exposed in these fields. FIX-18 adds the `attempt-missing` warning at settlement
when the agent recorded no new attempt on its exact assignment during the run. It is also retained in
operation evidence and shown by CLI settlement and `worker runs`, with a compact reference in status attention, without changing `observed-success`
or CLI exit-code semantics.

FIX-18 also adds `scope-read`, a warning for a classified read outside the run's workspace; it raises
attention without requesting a stop. Reads inside the workspace do not require change permission.
Confidently identified write targets outside the workspace and launch-supplied temporary directory retain critical
`scope` signals. Outside-looking operands of unclassified invocations and opaque code produce `audit-unverified`
warnings, without a stop. Changes inside the workspace are checked against the brief at settlement. Network detections warn, while credential
detections remain critical for reads and writes. Run admission refuses a brief with no
interpretable repository-relative `permittedChanges` pattern, explaining forms such as `path:NOTES.md`;
prose can remain beside a pattern. No request or response fields change.

Rate signals describe the observed sample, launcher reference, historical median, multiplier and reported tokens against the cap. Loop signals include the triggering occurrence count and an input fingerprint. Neither signal is derived from `work_progress` claims.

`GET /v1/progress` also adds `json.runSignals`: entries with `run`, `status` and `signals`. Its text labels these worker observations separately from `agentClaims`. Each process signal's `attention` names an ask about the original run intent; `attempt-missing` and `scope-settlement` name review asks pinned to the exact settlement. A missing id means attention recording is pending, not that the signal is absent. Critical signals use the existing run stop representation; the reservation is held until observed containment and settlement.

A goal's existing proposal/revision body and the generic roster record endpoint accept `limits.watch` partial threshold overrides; roster limits also accept `stallSeconds`. The resolved order is launcher, current roster revision, then goal, as described in [the software guide](software.md#recorded-watching-settings-070). Invalid merged loop limits refuse run start before an intent or harness launch. The run operation's immutable target includes `watch` (JSON encoded as a string), optional `watchRoster` (the roster revision), and `watchHistoryTokensPerMinute`. No new run-request override bypasses these recorded limits.

Watching observes the process boundary on the worker's machine. Remote execution and observation are not built or qualified by these additions.

## Hand-over (HANDOFF-01)

`GET /v1/handoff[?maxBytes=<positive integer>]` is additive to API v1. The SDK's
`Handoff(long? maxBytes = null)` returns `HandoffResponse`: `text`, `package`,
`problem` and the additive `textMeasurement`. A refusal returns HTTP 200 with `package: null` and a problem naming
all missing or invalid sources; clients must inspect `problem`. This read authors
no record and dispatches no work.

The package is a `vericairn.handoff/1` payload with a sidecar `hash` and `bytes`.
That discriminator and the original resume fields remain for API v1 compatibility;
`contextFormat: vericairn.context/1` identifies the shared context content contract.
Its manifest, source custody, derivative identities, withheld list and accounting
are assembled by the same implementation as work context. New resumes validate
the shared manifest and accounting as well as the retained originals; earlier
`vericairn.handoff/1` baselines remain readable.
The hash and byte count measure the payload's canonical pretty UTF-8 serialization, including all
originals, source identities, derived presentation, accounting and framing;
they exclude the sidecar and the separately rendered response text.
The human text is independently measured and hashed by `textMeasurement`,
including its accounting line. `maxBytes` governs the canonical payload; it is
not a fit claim for the alternate human rendering or a model's hidden framing.
The pin, record format, latest recorded timestamp and observed state come from the same
record snapshot. No wall-clock timestamp is mistaken for a human observation.
Selection is deterministic: all records held by this copy, including superseded
records, sorted by id. Every original has its exact record hash, retained byte
hash and byte count, base64 content, repository-relative path, exact-id CLI
retrieval command and API link. Record content hashes and retained byte hashes
are different identities. Originals remain separate from the generated view;
synthesis is off. Goals, work, assignments, results, runs, decisions, asks,
recorded observations and next steps remain inspectable in full.

`withheld` is empty for this full-record selection. Source-file contents and
output objects are explicitly outside its scope; it does not certify their
availability for a dependent action. Without a cap it reports **limit not set**;
tokens and model fit are unknown. `maxBytes` refuses the entire hand-over if it
does not fit, including mandatory originals and manifest overhead. It never
silently truncates, substitutes a summary or claims fit to a model context.
Malformed, missing or conflicting records and incomplete local record batches
refuse a package with every finding. A self-contained package can be retained
by the caller. Preparing it writes nothing: no objects, records, status ledger
or authority are stored.
Remote is not built.

`POST /v1/resume` accepts the retained `HandoffPackage` itself (`hash`, `bytes`,
`payload`). The SDK operation is `ResumeHandoff(package)`. Despite its POST body,
this is a read: signed requests use their ordinary read access and create no
write-ledger entry, signature or record. `ResumeResponse` contains `text`, `json`,
`hasErrors` and `problem`. An invalid, truncated, changed or foreign baseline
returns HTTP 200 with an `invalid-handoff` problem and no comparison. The hash is
an integrity check, not a signature or proof of who generated the artifact.
Existing request body limits still apply; large packages can be compared through
the embedded runtime or local socket within that transport's limit.

A successful `vericairn.resume/1` comparison lists every added record (including
backdated arrivals and intermediate revisions), every new supersession edge,
missing originals, records changed in place or moved (with their previous path), the complete pin before and after
(including artifact hashes and record format), and every observed-state change
with before/after values. Changed arrays are kept whole. Invalid current records
remain visible through findings and source changes; their derived state is
explicitly unavailable and `hasErrors` is true. Missing and replaced originals
also set `hasErrors`. A missing record is not assumed to have been superseded.
An already-open runtime can compare even when the current pin becomes unreadable:
the pin's `after` is null and findings name the problem. An interrupted record
batch is incomplete for both hand-over and resume, with every missing record named.
Even an unchanged view says **the world may have moved**. Reading a resume never
starts work, retries an effect, imports records or accepts a result; the old
package grants nothing and current authority must be re-established.

## A person's last observation

`GET /v1/status` adds `json.lastObservedByPerson`: null when no explicit estate
observation is recorded, otherwise `at` (the observation's `asOf`), `by`, `name`,
`record`, `hash`, `subject`, `subjectHash` and the observation's `facts`. The text
also names the timestamp and its source. This is derived from records on every
copy; GET status, hand-over and resume never advance it. Repository observations
by automation do not count as a person viewing the estate.

`POST /v1/status/observations` accepts `EstateObserveRequest` (`by`) and returns a
`WriteOutcome`. The SDK operation is `ObserveEstate(request)`. Only a human may
observe, device-signed requests act for their own principal, and the usual
signing and admission rules apply. The runtime generates the time, exact estate
subject and hashes of the pre-write record set, full pin and status. It records
an `observation` with kind `estate`, requiring record format 2; format 1 is
refused with an explicit-upgrade instruction. This is an attributable observation
of recorded status, not evidence of live external health or acceptance.

## Communication settings (COMMS-01)

These additive API v1 operations require a COMMS-01-capable runtime:

| Operation | Meaning |
|---|---|
| `GET /v1/communication` | Effective values, their sources, all scoped revisions, conflicts, observation time and revision-set hash. Optional `recipient` and `assignment` select the personal scope. Optional `progress`, `status`, `decision`, `result`, `handoff` are one-view overrides only. |
| `POST /v1/communication` | Replace the complete values of a scope; `values: {}` resets it to inheritance. |
| `POST /v1/communication/delegations` | The recipient explicitly grants or revokes changes to one personal scope. |

A write names `by` (the actual author), `scope`, `recipient` (except estate
defaults), `assignment` (assignment scope only), and `expected` (the prior
record's hash, omitted only for a first setting). A setting also supplies
`values`, optional `authority` (an explicit delegation record) and optional
`basis` (an existing decision). The runtime pins authority and basis hashes.
A delegation instead supplies `grantee`, `status` (`active` or `revoked`) and an
optional `expires` timestamp. A stale or divergent edit returns HTTP 409 with
problem code `conflict`; refresh and reconcile rather than silently retrying.
Signed requests bind `by` to the authenticated device's principal.

Each returned revision includes its exact setting hash and actual author,
plus `authority`/`authorityHash` and `basis`/`basisHash` when present. A brief
refuses to apply an override belonging to another work order; its Markdown
labels that configuration unavailable while retaining complete content.

The SDK exposes `Communication`, `CommunicationSet` and
`CommunicationDelegate`. Existing API implementations inherit explicit
unsupported responses for these new methods. An older HTTP runtime's 404 on
the read becomes an unavailable profile, never a claim that defaults were
applied. Unknown recipients, assignments and unsupported values are labeled
unavailable. Missing personal records use the stated defaults. Reads author
nothing. Existing canonical status, progress, brief and record JSON do not
vary with any recipient or one-view selection. Remote is not built.

`GET /v1/work/{key}/brief` additionally accepts the same optional recipient,
assignment and five one-view parameters. The SDK method is `BriefFor`.
Its Markdown carries the effective profile, source revisions and freshness;
its canonical `json` member is unchanged and its original obligations remain
complete. This is presentation input, never authority for an extra effect.

Unknown fields in communication write requests or their `values` are rejected
instead of being silently ignored. Invalid imported configuration is reported
as unavailable; conflicts keep all competing source identities. Generic record
writes cannot bypass current delegation checks by supplying an earlier `at`.
The existing reconciliation operation allows the recipient to resolve their
own communication branches, including independent first profiles at one scope;
an administrator cannot choose another person's preference.

## Explicit agent-run execution (RUN-01, 0.7.0)

API v1 adds optional fields to `RunStartRequest` and to an agent's `GoalStepRequest`:

| Field | Meaning |
| --- | --- |
| `configuration` | Named local launcher configuration; an unknown name refuses the run. |
| `execution` | Explicit choice object: optional `model`, `effort`, `provider`, `destination`, `dataBoundary`, `adapter`. Values are portable identities. |
| `allocation` | Optional nonnegative safe-integer `inputTokens` and `outputTokens`; their sum cannot exceed the run's token reservation when present. Optional positive safe-integer `maxBytes` bounds the CONTEXT-01 payload independently. Omitted token counts stay unknown. |
| `contextPackage` | Optional expected `sha256:` identity of the context assembled for this run. A mismatch refuses before intent; omission still records the actual package hash. |

`ProviderRow.execution` describes a launcher's `scopes`, native `efforts`, `providers`, `destinations`,
`dataBoundaries`, `providerDefault` and `closedEnvironment` capabilities. It is absent for other provider
kinds. `RunRow.execution` exposes the immutable intent snapshot and `RunRow.executionObserved` exposes
settled actual reports, discrepancies and unknowns. Both are absent on older runs with no such record;
absence is not an inferred choice. FIX-18 reads Codex's run-owned rollout after exit to observe model
and effort; missing or unreadable rollout facts stay unknown, and mismatches remain in `discrepancies`.
The OpenAPI document includes these additive properties.

Selection precedence is launcher configuration, roster and its role, goal and its role, assignment, then
explicit run choices. Every contributing record id and exact hash is retained, together with overwritten
choices. Restrictions accumulate across all scopes. Neither API override nor goal role replacement can
remove a required model/effort, exact revision pin, permitted provider/destination, data boundary or cost
bound. Missing required choices, unsupported native effort, an unknown revision under an exact pin, an
unexpected component, undeclared route or incomplete inventory returns a refused `RunResponse` before
an intent or routed assignment is stored. The same state is rechecked before dispatch. There is no silent
fallback to the harness's environment. `provider-default` is explicitly requested and only allowed when
declared; it never claims a known effort. `not-supported` requires a declared absence of effort control.

The stock runtime reads native launcher defaults and optional model/effort allowlists from owner-only
`worker/<launcher>.json` files under its user's `VericairnHome` for each run, including an agent goal step.
Set or show them on that machine with `vericairn worker configure --launcher codex` (setting also requires
`--model` and `--effort`; repeated `--allow-model` and `--allow-effort` narrow the allowed choices).
This local CLI operation has no HTTP route and runs no probes. No restart is required. Missing
configuration refuses before intent and names the setup command; API overrides cannot replace it or
escape its allowlists. Native environment policy derives only from the inventory admitted by the shipped
baseline or an exact current harness qualification; local configuration cannot add tools, servers,
variables or routes. Configuration is per machine and is never recorded in the estate. Files contain portable
choice identities only. Reads and writes are refused if the Vericairn home is inside a Git repository or estate.
If the user's home directory is a repository, set `VERICAIRN_HOME` to a directory outside all repositories and
estates for both the CLI and daemon before starting them. The run intent records the selected portable choices
and configuration identity, not the local file or its location.

`POST /v1/harness-qualifications` is machine-local: a paired local-network device receives `401` / `denied`
before any probe or record write, even when it acts for an authorized human. Local socket and signed
loopback requests remain available under the existing human qualification authority checks. Qualification
does not launch an agent. The worker configuration command adds no API route.

The intent records configuration identity, requested choices, provenance, resolved revision, harness
version, role, disabled Adapter, environment policy and admitted inventory, allocation and package identity.
`execution.contextPackage` identifies the CONTEXT-01 payload retained inside the run's local `context.json`.
`execution.allocation.context` copies its accounting's `units: bytes`, exact `bytes` and `maxBytes`
(null when no byte limit was set). This describes the canonical payload, not the complete human brief,
model tokens, provider framing or a model-fit claim. The complete context and rendered brief remain in
local run custody; no context objects are added to the estate. The CLI accepts the byte bound as
`--context-bytes` for both `worker run` and `goal step`.
The inventory uses portable ids and hashes, environment-variable names, dependency ids and logical routes;
secret values and machine paths are never returned in these fields. Settlement adds reports of actual
model/revision, effort, provider, destination, harness version, tools and complete inventory hash when the
harness supplies them. Missing fields stay unknown. Differences reported while the harness runs are preserved and signal WATCH-01 through
`execution`; a tool invocation outside the admitted inventory signals `tool-inventory`. Both are critical
and use the existing attention/stop behavior. No original command audit is filtered.

Codex and Claude Code qualify only for the exact versions and isolation evidence documented in the
[software guide](software.md#native-inventory-qualification-run-02-and-run-03). A different exact version requires a current human qualification with matching local probes;
the generic launcher and versions without such admission refuse. Automated contract tests use stand-ins; the software guide
records the builder's first live Codex observation and the required rerun.
The efficiency Adapter is unsupported (EFF-01, 0.8.0); only `disabled` is allowed. Remote is not built.
These additions preserve API v1, record format 2, existing authority checks and local-only dispatch.
See [the software guide](software.md#explicit-execution-and-environment-admission-run-01-070).

## Harness qualification (RUN-03)

`POST /v1/harness-qualifications` and the SDK's `QualifyHarness(request)` run the native launcher's probes
without starting an agent or MCP server, then author a format-2 `harness-qualification`. The request names
`launcher` (`codex` or `claude-code`), `by` and optional launcher `settings`. A human operator or a human
with the operator's explicit `qualify` grant may request it; agents and services may not. The actual author
and admitted signing device belong to the resulting record. A refused `WriteOutcome` uses the ordinary
write outcome mapping; authentication can refuse before the operation.

The six digests cover version, flags, tools, configuration, login boundary and MCP configuration. No raw
probe output or credentials enter the record. A non-baseline run must match a current unrevoked qualification
before intent and again before dispatch; its immutable `execution.harnessAdmission` pins the exact record
and hash. A baseline admission records `kind: baseline` and the probe digests instead. Revocation supersedes
the qualification, preserving launcher, version and probes; requalification is a new record. This is separate
from release/platform `qualification` and the MCP orientation matrix. It proves neither live inference nor
work acceptance; see [the format rules](record-format-v2.md#harness-qualifications-run-03).

Qualification is machine-local. A paired local-network device receives `401` with problem code `denied`
before probes or record writes, even when acting for an authorized human or supplying a local `command`
override. The local socket and signed loopback retain the existing human authority checks. A qualification
record can be used on another machine only when that machine's exact local probe digests match; the record
does not permit network-triggered probes. Remote remains unbuilt.
