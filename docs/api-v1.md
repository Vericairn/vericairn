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
`root`, `api`, when served the `loopback` endpoint (`host`, `port`, `certificate`) and, from 0.6.0, when
served the `localNetwork` endpoint (`host`, `port`, `identity`, the runtime identity's fingerprint). A daemon
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
| Local network, from 0.6.0 | Mutual TLS 1.2 at the address and port the operator configured; off unless configured, and on a wildcard or public address only when the operator allows it | A paired device: a client certificate its device key issued, the runtime identity's proof on every connection, and device-signed requests bound to the estate and the runtime identity, under the device's permitted profiles |

On Windows the pipe is created by Kestrel's named-pipe transport with its default, `CurrentUserOnly`, and no
other pipe security. Its access list allows only the owner of the daemon's process token: the account that
started the daemon or, for a daemon run elevated, the owner Windows gives elevated processes (by default the
Administrators group), so that only administrators' elevated processes reach it.

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

A loopback request carries three headers: `Vericairn-Device` (the device record id), `Vericairn-Signed-At` (RFC
3339 UTC seconds) and `Vericairn-Signature` (base64url, 64 bytes). The signature is ECDSA P-256 over the SHA-256
of the line-feed-joined string `vericairn-request/1`, the upper-case method, the request target (path and query
exactly as sent), the signed-at time and the lower-case hex SHA-256 of the body (empty for no body). A JSON body
is UTF-8: one whose `Content-Type` names another charset is refused, and so is one that begins with a byte order
mark; any other body is taken as the bytes it is. The daemon accepts a signature only from a device that is
current and admitted now in the estate it serves, within five minutes of its own clock when the request starts and
still when its body is in, and once: the body is read only until the signature is five minutes old, and a request
whose body has not arrived by then is refused. It remembers each device's accepted signatures until five minutes
after their signing time, when they would be refused as stale anyway, refuses a signature already stale, and
forgets none sooner: a device with 10,000 in that window is refused until older ones lapse, so no flood, by that
device or another, and no body held back, makes room for a replay. It remembers a signature by its 64 bytes with
`s` replaced by the smaller of `s` and `n - s`, `n` being the order of the P-256 group, so neither another
base64url spelling of the same bytes nor the malleated twin `(r, n - s)`, which verifies just as well, passes for
a new signature; the device signing the same request again makes a new one, which is accepted. Anything else is
answered with `401` and a problem with code `denied`; revoking the device ends access from the next request on,
with no restart. The SDK signs with `DeviceRequestSigner`, which uses the device key in this machine's custody, so
a copied request is refused, as stale after five minutes and as a repeat before then, and a copied key file is
useless where platform custody holds the key.

From 0.6.0 a device-signed request acts for its device's principal, on loopback as on the local network. When the
request an operation receives names the principal acting, in its `by` or `principal`, as records, assignments,
admissions, revocations, goals, releases, runs, new and recovery devices and pairing codes do, that principal
must be the device's own; a device renames and rotates only devices of its own principal; and stopping the daemon
or replacing its certificate or identity is an administrator's, the operator's or a principal's holding
`administer`. The runtime checks the request object the operation receives, after the body is bound, and never
reads the body a second time, so no encoding, spelling or repetition in the body makes the check and the
operation read different principals; a request of a type it does not know is refused. A refusal is `401` with
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
stays as well, and the listener refuses a client's renegotiation, which would carry a new certificate inside the
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
three headers, five minutes of skew and each signature accepted once in any of its forms, over the SHA-256 of
the line-feed-joined `vericairn-request/2`, the estate's id, the fingerprint of the runtime identity the device
paired with, and then the method, the request target, the signed-at time and the body's hash as on loopback. A
request signed for one runtime is refused by any other, even a runtime of the same estate on another machine,
which knows the same device. Until the connection's client certificate verifies with the current key of the
device a request names, every refusal is the same one, so nobody learns from it whether a device exists, is
revoked or is permitted the profile. The runtime reads the device from the records at every request, when it
starts and again once its body is in, and refuses it unless it is current, admitted now and permitted
`local-network` in its device record's `profiles`, and unless the signature verifies with its key. A revoked
device is therefore refused at its next request, on a connection it already has open as well, and a request whose
body arrives after the revocation is refused too; disconnection alone revokes nothing. The device acts for its own
principal, as on loopback. What acts on the runtime's own machine is refused: `POST /v1/shutdown`,
`/v1/devices`, `/v1/devices/rename`, `/v1/devices/rotate`, `/v1/devices/recovery`, `/v1/estate/export`,
`/v1/pairings`, `/v1/runtime/certificate`, `/v1/releases/submit` and `/v1/releases/observe` (which use the
release provider's credentials on that machine), `/v1/objects`, and `/v1/runs`, which launches a coding agent
there (stopping a run is not refused); applying an upgrade, though not its plan (`check`); a record of type
`device`; and any request that names a `recovery` key file. A refusal is `401` with a problem of code `denied`,
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
machine, are the design to come: the Remote profile, planned for 0.7.0, needs them, since a remote device's
writes cannot rest on a key of the runtime's machine. Loopback does not check `profiles` yet, so devices recorded
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
   of a code names it. A code works for ten minutes, pairs one device and survives five confirmations that do
   not match it; at most sixteen wait at once. The daemon keeps only the keys derived from a code, in memory,
   so a restart forgets every code. The code is shown once; the administrator gives it, with the address, to
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
   as far as 16 KiB (`413` beyond). A hello that proves none is refused alike, whatever was tried, and its
   connection is closed. An address that sends 50 such hellos within a minute is refused until its minute ends;
   an address is never forgotten while its minute runs, so while 1,024 addresses are counted a new one waits;
   and once 4,096 such hellos have come in all within a minute, every hello waits for the next, which bounds
   the work of trying codes. A code the hello proves is refused when it has expired, was used or has no
   attempts left. Otherwise the runtime answers a
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
6. The runtime refuses a confirmation of an exchange it did not open, of a code that expired, was used or has
   no attempts left meanwhile, and one whose MAC does not match, which ends the exchange and uses one of the
   code's five attempts; the last one closes every exchange of the code still open. It refuses a device name
   with control or formatting characters. It rebuilds the device record
   from its own challenge and the device's fields, verifies the signature with the key presented, writes the
   device record with that signature, the device's own, and then the admission by the administrator, signed
   by the administrator's admitted device; when the admission is refused the device record is removed again.
   The device's name, platform, custody and presence are its own statement, which the runtime cannot verify,
   and the admission's note says so. The code is used up. It answers `PairingCompleted`, the device and
   admission ids and the record's hash, with an HMAC-SHA256 under the runtime's key over the whole exchange.
7. The device verifies that MAC and keeps the connection: the address, the estate, the runtime identity's
   public key, and its device's id and public key. A failure on the device removes the key it made, and a
   failure anywhere records nothing. Pairing again replaces this machine's earlier connections to the same
   estate that pin another runtime identity, which the runtime replaced, or that are for the same principal;
   their devices stay admitted until an administrator revokes them.

Each transcript is unambiguous: the scheme and a label (`hello`, `challenge`, `confirm` or `completed`), then
each field as a 4-byte big-endian length and its UTF-8 bytes, a list preceded by its count and an optional field
by a byte saying whether it is present; each later transcript contains the earlier one and its MAC. MACs are
compared in constant time, and a hello is tried against every code, so how long a refusal takes says nothing of
which code, if any, came close. Every primitive is a standard one in the platform's implementation: HKDF-SHA256,
HMAC-SHA256, ECDSA P-256 with SHA-256, TLS.

### Threat model

- Someone watching the network sees TLS only.
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
  forget a signature before it is stale, and a copy whose body is held back until then is refused.
- No certificate makes either side connect anywhere: the local network speaks TLS 1.2 only, so a certificate that
  names an address is refused in clear before the platform's TLS reads it, on every platform, and nothing a
  certificate names is fetched.
- A runtime listens beyond a private network only where the operator allowed it with `--allow-public`.

Not covered here: discovery, since devices are given the address; the relay service; and records signed on a
paired device by its own key, the design the Remote profile in 0.7.0 needs.

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
| `GET /v1/devices`, `POST /v1/devices`, `POST /v1/devices/admit`, `POST /v1/devices/revoke`, `POST /v1/devices/rename`, `POST /v1/devices/rotate`, `POST /v1/devices/recovery` | Devices and admission; a device is created, renamed and rotated on the machine that holds its key, and the operator's recovery device is made there and its key written once for offline keeping. Admit and revoke accept the recovery key's path and passphrase. From 0.6.0 each `DeviceRow` gives the device's permitted `profiles`; a device on another machine is recorded by pairing. |
| `GET /v1/releases`, `POST /v1/estate/upgrade` | Release records; plan (`check`) or apply an upgrade to a published release (applying is refused over the local network) |
| `GET /v1/documents` | The declared living documents (`DocumentRow`: path, repository, declaration, title, state current, due or unavailable, level, reasons, latest clearance, unavailable contributions, notes) |
| `POST /v1/documents/{id}/reviews` | Review a living document by its declaration (`DocumentReviewRequest`: by, outcome, summary, findings, commit, and from 0.6.0 independence). The runtime reads the content, records its hash, the covered paths' identity and the triggers the review answers, records the reviewer's independence and runs the declared checks; a failed check, or the declaration's `clearedBy`, refuses a clearance. An estate file's revisions decide its reviewer's independence, and a stated one that differs is refused; for a document of another repository the runtime records the one stated, or without one `self` for a principal with the builder role and otherwise its judgement from the estate's records ([record-format-v2.md](record-format-v2.md#living-documents-due)). Returns a `WriteOutcome`. |
| `GET /v1/goals`, `GET /v1/goals/{id}` | The current goals (`GoalRow`: id, hash, title, status, sponsor and the sponsor's name), and one goal by the id of any of its revisions (`GoalResponse`: the text `vericairn goal show` prints and its canonical JSON, identical on every machine for the same records: the goal's record and revision, its sponsor, its status, `proposed` before any status and `achieved` once accepted, the budget its activation grants unit by unit, each condition's verdict, `met`, `not-met` or `unknown`, with its reason and the records it read, beside the current evaluation's verdict, the evaluation, the steps, who accepts it, whether it is ready for acceptance and what stands in the way, and its acceptance). An id that names no goal is answered 404 |
| `POST /v1/goals` | Propose a goal, or a revision of one (`GoalProposeRequest`: by, the goal record's `body`, `supersedes` naming the current revision): a proposal authorizes and funds nothing, and a revision starts again from `proposed`. Anyone may propose a new goal; a revision is refused, before anything is written, unless its author is the sponsor of the revision it supersedes, a person, or an operator when an agent sponsors that goal (decision 01M50A8KHJT1MSSS84KZKHKRHB); anyone else proposes the change as an ask to the sponsor. Returns a `WriteOutcome` |
| `POST /v1/goals/{id}/activate`, `pause`, `resume`, `stop` | A goal's lifecycle (`GoalActionRequest`: by, an optional note): activate a proposed goal, or resume a paused, budget-limited or blocked one, which grants its budget; pause an active goal; stop a goal that has not ended, which records it abandoned. Each is by who decides for the goal: its sponsor, a person, who to activate or resume holds the operator role or a grant of the `spending` scope, or an operator when an agent sponsors it; an agent never activates a goal. Each is refused out of order, and after the goal ended or was achieved. Returns a `WriteOutcome` |
| `POST /v1/goals/{id}/evaluate` | Record the evaluation of a goal's current revision (`GoalEvaluateRequest`: by, summary, `verdicts`, each a `GoalVerdict` of the condition's position from 1, the verdict `met`, `not-yet` or `impossible`, the reason and the ids of the records read). The runtime gives each record predicate its own verdict with the records it read and refuses an evaluator's verdict that contradicts it, except `impossible` for one it does not find met; each evidence statement needs the evaluator's verdict, and one found met names the records read. Refused for a principal that authored one of the goal's steps. Returns a `WriteOutcome` |
| `POST /v1/goals/{id}/accept` | Accept a goal's current revision, relying on its current evaluation (`GoalAcceptRequest`: by, statement). Refused, with every reason at once and before anything is written, unless the principal is the one who accepts it (decision 01M507XV4VBFYWMRN8WCXMXHX4: its sponsor when the sponsor is a person, or the principal the sponsor delegated an ordinary goal's acceptance to while it holds the `accept-work` scope; an operator, only an operator, when an agent sponsors it), the goal was activated and has not ended, its current evaluation finds every condition met and is not the principal's, the principal authored none of its steps, and every record predicate holds now. Returns a `WriteOutcome` |
| `GET /v1/providers` | The provider and adapter implementations available to the runtime on its machine (`ProviderRow`: provider, kind release or source, and from 0.6.0 launcher or workspace, implementation, contract, status, operations, limitations, declared settings with defaults, the destination setting, the served locations). Being available selects nothing. |
| `GET /v1/launchers` | The coding-agent launchers available to the runtime on its machine, providers of `vericairn.launcher.agent/1`, as `ProviderRow`s of kind launcher; the generic launcher's first limitation says that generic process launch is not complete agent support. From 0.6.0 |
| `POST /v1/runs` | Start a coding agent's run on an assignment (`RunStartRequest`: assignment, launcher, agent, by, repository, and optionally goal, branch, base, wallMinutes and the launcher's settings), the worker contract `vericairn.worker/1` ([software.md](software.md#coding-agents-the-worker-and-its-launchers)). Refused over the local network, and, before anything is recorded, when the agent is not the assignment's contributor or has no admitted device whose key the runtime's machine holds, when the recording principal is neither the operator nor holds `local-engineering`, when the work belongs to no active goal or the goal's budget does not allow the run, when a device of an operator or of the goal's sponsor can sign there without its owner's presence (the refusal names each device and why), when the launcher cannot run (Codex's goals feature is verified first), or when the workspace cannot be prepared. Otherwise the workspace is prepared, the intent recorded with the run's cap, and the harness launched; the response returns once it started, and the runtime observes and settles the run. Returns a `RunResponse` (run, status, message, ok, the operation revision, the cap, the workspace, problem). From 0.6.0 |
| `GET /v1/runs` | The agent runs (`RunsResponse`): each run (`RunRow`: run, identity, launcher, agent, assignment, goal, workspace, status, cap, usage once settled, what it is charged, a stop's facts, its last heartbeat here, whether a worker here observes it now, and whether its launcher is generic), and the ledger of every goal with a run or active now (`LedgerRow`: each charged unit's budget, spend and remainder, the runs in flight, the concurrency, the attempts per step, and why a new run would be fenced). From 0.6.0 |
| `POST /v1/runs/{id}/stop` | Ask a run to stop (`RunStopRequest`: by, reason, graceSeconds) and report what was observed (`RunStopResponse`: run, state, message, ok, stop): the request, its transmission to the harness, the harness's acknowledgment, an escalation after the grace period and the observed containment are separate facts in `stop`, and `state` is the furthest one observed. A stop that is not observed is reported as `requested`. On every profile, refused unless `by` is an operator, decides for the run's goal (its sponsor, or an operator for a goal an agent sponsors) or started the run. The request waits for what the worker observes for at most 30 seconds, whatever grace it names, and answers with what it has; the settlement records the rest. From 0.6.0 |
| `POST /v1/releases/submit` | Submit a release as intent before effect, on the runtime's machine (refused over the local network, as observing is, since both use the provider's credentials there): the provider is the request's `provider`, else the one the product record declares (neither is refused; a provider with no implementation on the runtime's machine is refused, never replaced); the settings are the provider's defaults, then the product's declared settings, then the request's `settings` (and `repository`, `ref`, `workflow`, kept from 0.3.1 as settings of those names). The operation record is written, then the build is dispatched; nothing is published. Before any intent, the product's living documents can refuse it ([software.md](software.md#release-providers-and-the-products-release-profile), step 1). Returns a `ReleaseResponse`. |
| `POST /v1/releases/observe` | Observe a release's run, draft and publication through the provider its operation records and record what settled: the operation revision, the candidate release with every artifact classified by the product's profile (or the generic rule), and later the observed publication; or cancel an intent that had no effect. The operation is found by `provider` and `repository` (the destination), by `product` and `version`, or by a destination one current operation alone matches. Returns a `ReleaseResponse`. |
| `POST /v1/estate/export` | Write a verified export to a path on the runtime's machine; no keys or authority travel with it. From 0.6.0 a device-signed export names its device's principal as `by`, which must be an operator or hold the `administer` scope; over the local network it is refused |
| `GET /v1/reconcile`, `GET /v1/recover/inspect` | Reconcile tasks left by diverged copies; what this copy holds |
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

## Outcomes and problems

A write answers with a `WriteOutcome`: either `written` (path, id, hash) or `problem`, never both. A
problem has a stable `code`, a `message` that names the permitted next action, and `findings` when a
record was rejected:

| Code | Meaning | HTTP |
|---|---|---|
| `rejected` | The estate would be invalid with this record; the findings say why; nothing was written | 422 |
| `refused` | Malformed input, an unknown type, no admitted device key on this machine where the estate requires signatures (or for an admission from record format 2), or a limit; from 0.6.0 also a run's stop asked by a principal who may not stop it, and a local-network write whose paired device the runtime cannot keep | 422 (409 for upgrade and export, for a pairing code when no local-network profile is served or sixteen codes wait, and for a rotation without TLS; 413 for an object above the size limit and a pairing message above 16 KiB; 503 for a local-network write whose paired device cannot be kept) |
| `not-found` | The subject named does not exist | 404 |
| `invalid` | The estate has errors and the operation needs a valid estate (upgrade), or an export request lacks its output path or names a malformed exporter | 409 |
| `denied` | A loopback request without a valid signature by a device admitted now; from 0.6.0 also a device-signed request naming a principal other than its device's, or stopping the daemon or replacing its certificate without an administrator's authority; a local-network request over a connection whose client certificate the device's key did not issue (one refusal, whatever the device), from a device revoked, not admitted or not permitted that profile, acting on the runtime's own machine, or whose write would be signed here by a device that needs its owner's presence; a JSON body in another charset than UTF-8 or with a byte order mark; a body that arrives after its signature is stale, or after its device was revoked; and a pairing hello that proves no code, or a code expired, used or out of attempts, or a confirmation that does not match | 401 |
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

`new VericairnClient(endpoint)` connects over the local socket; `VericairnClient.Loopback(endpoint, signer)`
connects over pinned loopback TLS and signs every request with the signer, which `DeviceRequestSigner.Find`
builds from a device key held on this machine. `VericairnClient.IsAlive(endpoint)` says whether a daemon
answers. The client implements `IEstateApi`, so code written against the interface runs unchanged against
the embedded `EstateService`. From 0.6.0 it also has `Launchers()`, `RunStart(request)`, `Runs()` and
`RunStop(run, request)`, the worker's operations; with the daemon, a run started through it goes on after
the request returns, and a client's absence never stops it.

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
whose principal is not the one the request names as acting, which earlier releases accepted, is refused; such a
request goes over the local socket, or is signed by a device of the principal it acts for.

`vericairn estate upgrade --json` prints the plan with camel-case names (`fromPin`, `toPin`, `fromFormat`,
`toFormat`, `migrations`, `release`, `artifacts`, `applied`, `migration`, `anchored`), the same shape the
API returns. `anchored`, new in 0.5.2, lists the admissions recorded without a signature that the move to
record format 2 anchors, and is `null` otherwise.
