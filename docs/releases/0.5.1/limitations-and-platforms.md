# Limitations and platforms, Vericairn 0.5.1

Written before the release build, as the documentation convention requires from 0.3.0 on. The release record in the Vericairn estate, written when the draft is observed and superseded by the published revision, is the authority for the commit, the workflow run, every artifact's hash and the publication time; `SHA256SUMS` on the release page lists the same hashes. What follows is what was known when the set was frozen.

## Qualified platforms

The release workflow runs the tests on these platforms before packaging, so these are the platforms the release is qualified for:

- linux-x64 (ubuntu-latest), whose tests run the secret-service key store against GNOME Keyring in a D-Bus session
- osx-arm64 (macos-latest), whose tests run the Secure Enclave key store through the helper built from the same commit, where the runner exposes an enclave
- win-x64 (windows-latest), whose tests run the CNG key store

The linux-arm64, osx-x64 and win-arm64 distributables are cross-published from the same commit and have not been executed anywhere; on osx-x64 the Secure Enclave helper is built for Intel Macs and has not run on one. They are provided, not qualified. Workbench's platform qualification is separate from the command line's and is stated below.

## Signing

Unsigned developer build. The operator's signing material does not exist yet. Verify a download by its checksum against the estate's manifest or the release record, never against the tag name alone; `install.md` shows how. Workbench and the macOS Secure Enclave helper ship without an application bundle, an installer or a signature; the helper needs none for CryptoKit's Secure Enclave keys.

## What 0.5.1 changes and corrects

- Record format 2 (GOAL-01): records state the format they were written in and are validated under that format's rules; format 2 adds goals, goal statuses, rosters, asks, possibilities, discharges, credential references, qualifications and the living-document records; a format-2 decision states its stake and what it does not authorize and keeps the operator's words across revisions; grants can expire, judged at each record's time; work orders can name what they start after and what blocks them; agent-run operations carry their cap and usage; verifications can state their evidence class, observer and expiry. `estate upgrade --to 0.5.1` moves an estate to format 2 without rewriting a record, and a new estate starts at format 2.
- The first authority (ID-01, completed by pull request 56): the root operator's first device admitting itself is the only self-admission accepted, and in record format 2 every other admission and every revocation is signed by a device already admitted, whatever the charter's signature policy; an unsigned one is refused when it is written and counts for nothing (`signature.admission-unsigned`). Releases 0.4.2 and earlier accepted any self-admission by the operator, so anyone able to add records could mint a device for the operator and sign as the operator, and the withdrawn 0.5.0 still let them do it with an unsigned admission while signatures were optional, the default for every new estate. Format-1 records keep format 1's rule: in an estate begun under format 1, a device admitted by an unsigned admission stays admitted until an administrator revokes it.
- Release 0.5.0, the same work, was withdrawn before acceptance and publication after its independent review found that gap; 0.5.1 is the first published release of this work.
- Key custody (ID-01): a new device gets the strongest custody its machine offers (CNG on Windows; the Secure Enclave on a Mac that has one, through the `vericairn-enclave` helper the macOS archives carry; the secret service on Linux where one runs; otherwise an owner-only file), where 0.4.2 used owner-only files on macOS and Linux. A person's device needs its owner's presence wherever its custody enforces it, which is Secure Enclave custody; an agent's never claims it.
- Device identity (ID-01): a device keeps its identity, its first record, across renames and key rotations; a rotation is signed by the previous key and the new one, each signature verifies against the key the device held at the record's time, and a rotation moves a key into other custody. In 0.4.2 a device could not be renamed or given a new key without recording a new device.
- Emergency access (ID-01): the operator's recovery device, its key kept offline in a file encrypted under a passphrase, admits a replacement device and revokes a lost one, and nothing else; each use stays in `status` until the operator reviews it.
- Living documents (DOCS-03): declarations with triggers and levels, due documents in `status` and `estate check`, reviews that clear a document at its exact content after its checks, generated regions held to their generators, documents of other repositories read through the software pack, and the release gate; `vericairn docs living`, `review`, `declare` and `regenerate`, `vericairn sources clone`, `GET /v1/documents`, `POST /v1/documents/{id}/reviews`, the MCP tools `documents_due` and `document_review`, and Workbench's Documents page.
- Record identifiers keep the milliseconds of the moment they were made, so the records a build writes within one second sort in the order they were made; records 0.4.2 or earlier wrote within one second sort in no particular order among themselves.
- `vericairn doctor` names the custody a new device gets in the estate it sees, the strongest record format 1 can name while the estate is at format 1, and says that presence is the default for a person's device.
- The release workflow runs the arm64 Secure Enclave helper it ships: a key made without presence, its public key read back and a digest signed.
- The release workflow's tests exercise the platform key stores as continuous integration does: the Secure Enclave helper on macOS and a secret service on Linux, so the release's own run covers the custody it ships.
- The agent orientation (DOCS-02): `AGENTS.md` names nothing that changes with each record, and `vericairn mcp orientation --check` compares copies with a fresh generation.
- The operator's guides of 0.2.0 to 0.4.2 said that `estate init` writes three records and that `estate check` then reports three; it writes two beside the manifest, which is not a record.
- `vericairn docs declare --by` was described as the reviewing principal, a description it shared with `docs review`; it is the authoring principal.

## Record format

Reads record formats 1 and 2. Writes an estate's own format, at most 2; a new estate starts at 2. The upgrade from 0.4.2 applies the step `record-format-2`, which moves the manifest's `recordFormat` to 2 and makes the migration record the estate's first format-2 record; no record is rewritten. A build reads the formats its release record lists and refuses an estate at a format it does not read rather than misread it: release 0.4.2 refuses an estate at format 2, with a refusal that names no format, while from 0.5.1 the refusal names it (`manifest.record-format`). A clone that has not pulled the upgrade can still add format-1 records, which the estate reads under format 1's rules. Device records and decisions written in format 2 need fields that format 1 lacks, so scripts that write decisions add `stake` and `notAuthorized` after the upgrade.

## Key custody

- Presence: built for Secure Enclave custody (Touch ID or the login password), with tests that refuse presence wherever the custody cannot enforce it and for every agent's device; no signature with presence had been observed when this set was frozen. Continuous integration proves the custody stores without presence, and the operator's own Mac is where presence is first verified.
- Windows presence (Windows Hello or a protection prompt) is not built: it cannot be qualified without a Windows device and a desktop session. Windows keys are not exportable and sign without presence, and their device records say presence none.
- Secret-service custody protects a key at rest; while the keyring is unlocked, other processes of the same user can read it, as the custody kind records. An owner-only file can be used by any process of the same operating-system user.
- In an estate still at record format 1, new devices take the strongest custody format 1 can name, so on a Mac an owner-only file, as `vericairn doctor` says.
- Secure Enclave keys without presence sign, and new ones are made, while the Mac's screen is locked: observed with the 0.5.0 build's command line and helper on the operator's Mac on October 15, 2026, in scratch estates. Whether a key with presence signs while the screen is locked has not been observed.
- A signature is judged by the time its record states, as revocation already was: a key that may have leaked is revoked by an administrator, not only rotated.
- Upgrading moves no key: each device keeps its custody until its owner rotates it on the machine that holds it.

## Living documents

- Not built yet: the software pack's own checks (the references and the third-party notices against the build, and a README naming no unreleased capability), packs proposing declarations, retiring a declaration, and Workbench showing a document's content beside what made it due (work order DOCS-04).
- A covered path of another repository is compared only where that repository's clone is registered on the machine; elsewhere at the commit reviewed or released. The release gate's content rule reads a document of another repository only through such a clone, and the intent records what it compared.
- When this set was frozen the first-party estate, at record format 1, declared no living documents; its declarations follow its upgrade to 0.5.1.

## Goals

Release 0.5.1 validates the goal, goal-status, roster and ask records and nothing acts on them: the predicate evaluator, the goal command, the worker contract and the coding-agent adapters, routing by roster and the next-eligible view are release 0.6.0 (GOAL-02 to GOAL-04), as are pairing and local-network connections (ID-02).

## Workbench

- Qualification: Workbench is not qualified on any platform. When this set was frozen, the platform obligations (`design/platforms.md` in the Vericairn estate) recorded, on macOS, the accessibility probe's measurement of six screens on a native desktop; native first launches of the shipped 0.3.1 and 0.4.2 by the Reviewer principal with no daemon serving and the probe of their release workspace (for 0.4.2, 47 targets, none below 24 layout units with 3 splitters exempt as pointer conveniences with keyboard alternatives, and 16 contrast pairs, none below its minimum); in the independent review of the withdrawn 0.5.0 (review 01M4YY52DR2GCV3EAXBWBFS4XE), a native first launch of its Workbench with no daemon serving (the estate open in 1.39 s) and the probe of its release workspace (51 targets, none below 24 layout units with 3 splitters exempt, 16 contrast pairs, none below its minimum, and the release form showing the provider choice and the provider's four setting fields); and the operator's native first launch and his acceptance of 0.3.1 written through the daemon. The keyboard-only journeys of the work loop and the release flow, the first launch through the real command line and the acting-principal display run headless on the three platforms as automation, and the Documents page is covered by headless tests of the desktop; journey J50, a product's living documents kept true across a release, runs at the command line. No native desktop run with a person at the keyboard or a screen reader is recorded on any platform; the Windows and Linux native cells are non-claims.
- Projects are not a record type of format 1 or 2; the products page says so instead of inventing them.
- The comparison view is a line difference of canonical JSON, not a field-aware merge.
- Docking is panel placement with keyboard alternatives, not free drag-and-drop of every panel.
- The release workspace offers the providers the runtime can reach; 0.5.1 carries one, GitHub Releases.
- Workbench finds the command line beside its executable, through `VERICAIRN_CLI` or on the `PATH`; it starts, stops, installs and uninstalls the daemon through that command line and never hosts the runtime.

## The MCP server

- Protocol revisions 2025-06-18, 2025-03-26 and 2024-11-05 over standard input and output; `initialize`, `ping`, `tools/list` and `tools/call` only. Resources, prompts, sampling and the HTTP transport are not implemented.
- The write tools (`work_attempt`, `work_result`, `work_amend`, `document_review`) are visible only for a principal whose admitted device key the machine holds; there is no tool that publishes, spends, creates or admits a device, runs a command or authors an arbitrary record type.
- The server trusts the operating-system identity of whoever runs it; it is not a network service.
- Every agent harness is a non-claim in the orientation file's matrix until a session with it is observed and recorded.

## Other known limitations carried from earlier releases

- The loopback profile is pinned to the runtime certificate and device-signed; pairing and the local-network profile are release 0.6.0.
- The GitHub Releases provider identifies a dispatched run by listing recent runs and records the operation as uncertain when none appears within thirty seconds; the provider runs on the runtime's machine under the user's own `gh` login. It is the only release provider 0.5.1 carries; the in-memory provider exists for tests only.
- Verifications record a command's exit, duration and output and do not parse test reports.
- The daemon does not yet run admitted work or supervise workers (milestone M4).
- The third-party notices and the `licenses` directory are maintained by hand beside the generated bill of materials; a test holds them to the lock files. The bill of materials names its component Vericairn 0.0.0, lists build-time and WebAssembly packages no archive carries, and omits the .NET runtime and the ASP.NET Core shared framework that the archives embed; the notices are the accurate inventory.
- The release notes do not say whose acceptance an accepted result rests on; this set's notes say it by hand for the work in the release.
- The notes start at the previous publication while the previous set's notes end where they were generated, before that build, so the records between appear in neither set's generated notes unless they are added by hand, as this set's are.
- The release observer records the record formats of the command line that observes the run, not the release's; a candidate observed by an older command line is corrected by a superseding revision, as 0.5.0's was.
- Live stores of estate clones that no longer exist stay in the per-user state until removed by hand; no command lists or removes them yet.

## The artifact set

The release workflow produces, for the version `0.5.1`:

| Artifact | What it is |
|---|---|
| `vericairn-0.5.1-<platform>.tar.gz` (`.zip` for Windows), six platforms | The command line, self-contained, with LICENSE, NOTICE, the third-party notices and `licenses/`; on macOS also `bin/vericairn-enclave` |
| `vericairn-workbench-0.5.1-<platform>.tar.gz` (`.zip` for Windows), six platforms | Workbench with a copy of the command line beside it (and on macOS the Secure Enclave helper), self-contained, with LICENSE, NOTICE, the third-party notices and `licenses/` |
| `vericairn-0.5.1-sbom.cdx.json` | The software bill of materials (CycloneDX) |
| `vericairn-0.5.1-THIRD-PARTY-NOTICES.md` | The third-party notices |
| `vericairn-0.5.1-docs.zip` | This documentation set |
| `SHA256SUMS` | Every artifact's SHA-256 |

The sizes and hashes are in the release record and in `SHA256SUMS`; this frozen document does not repeat numbers it could not know before the build.
