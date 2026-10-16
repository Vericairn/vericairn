# Limitations and platforms, Vericairn 0.6.1

Prepared before the release build. The estate's release record, written when the candidate is observed and superseded when publication is observed, is the authority for its commit, workflow run, artifact hashes and publication. This set describes the code and tests; it does not claim that the release workflow, native desktop qualification or a live coding-agent session has already run for this candidate.

0.6.0 was withdrawn before publication after independent review (withdrawal `01M51CE4J8Q2XR92Z8M2EPZF13`, decision `01M51CDST3GS2CDFXD22Z3NKJP`). 0.6.1 includes FIX-08's corrections, described below. Its tests and documentation do not replace the independent review and operator acceptance of the new candidate.

## Platforms and the release workflow

The release workflow requires a Release build and the suite on three runners before packaging:

| Target exercised by the workflow | Platform-specific coverage required there |
|---|---|
| `linux-x64` on `ubuntu-latest` | The secret-service store, with GNOME Keyring and `secret-tool` in a D-Bus session; worker containment and daemon tests on Linux |
| `osx-arm64` on `macos-latest` | The Secure Enclave helper, creating and signing with a key without presence; the workflow requires an available enclave rather than silently skipping it; worker containment and daemon tests on macOS |
| `win-x64` on `windows-latest` | CNG key custody, Windows Job Object containment and named-pipe/daemon tests |

A passing release run supplies this automation evidence; the release record states which platforms were qualified. `linux-arm64`, `osx-x64` and `win-arm64` are cross-published by the workflow and are not executed by that matrix. The Intel Mac helper is compiled for its architecture, but that job executes only the arm64 helper. Providing an archive does not qualify its architecture.

The archives are self-contained .NET applications and require no separate .NET runtime, but still depend on the operating system's native libraries. Workbench needs a desktop; on Linux its Avalonia desktop uses X11 or XWayland. Secret-service custody needs a running service and `secret-tool`. The macOS helper is compiled for macOS 13.0 and later and probes enclave availability at runtime. A machine that cannot use it falls back to other available custody. No iOS or Android client is built by this solution.

The workflow packages unsigned developer builds. There is no application-signing or notarization step, application bundle or installer. Check downloaded bytes against `SHA256SUMS` and the estate's recorded hashes; see `install.md` for quarantine and SmartScreen behavior. These checks establish the downloaded bytes, not a platform signature.

## Goals, routing and asks

0.6.1 implements goal proposal and lifecycle, record predicates, independent evaluations, acceptance, roster routing, asks, one-step dispatch and hand-over. It keeps record format 2; 0.5.2 was the first published format-2 release. New fields and `custody-exception` still need a build that understands them, so a shared format number does not establish that an older writer understands every 0.6.1 record.

- The evaluator must have authored no step; the accepter must be neither a step's author nor the evaluator. Activation and spending need the accountable person's authority. Goal acceptance is not an automatic consequence of a passing predicate or an exited harness.
- `goal next` depends on this machine's launchers, keys and custody. Predicates involving covered files or unavailable living-document contributions can also differ by machine. Record-only predicates and the eligibility relations derive from the records.
- `goal step` takes one eligible step per call. There is no continuous multi-step planner or scheduler in this Community implementation. It neither activates nor accepts a goal, funds more work, nor answers an ask.
- An assignment and its run intent or ask are validated together and journaled, but sequential storage is not atomic. A stranded routed assignment holds no step, and reconciliation reports an incomplete pair.
- Routing does not silently replace an unavailable contributor. A person takes over with a new assignment; the old runs receive stop requests, whose containment still must be observed.
- Asks deliver at most three per principal in a read response, with the remainder queued. A stalled run can record an attention ask, but there is no external notification delivery here. A decision by the principal asked answers it; reading, dismissing or waiting does not.
- Pausing or abandoning a goal prevents new launches; it does not stop runs already in flight. `goal stop` records abandonment; use `worker stop` for each run that must stop and inspect the stop facts. `limits.deadline` and `limits.consecutiveFailures` are validated and recorded but not yet enforced by the routing or worker code. Operators must monitor and act on them.
- Resumption and goal revision do not erase run charges: the ledger includes runs naming any revision of that goal. Concurrency defaults to one when not stated; omitted token or currency allowance is not unlimited spending authority.

## Worker launchers, limits and custody

The software pack provides `claude-code`, `codex` and `generic`. The first two configure their harness's goal mode and the estate's MCP server; generic supplies process launch only. The tests use stand-in processes. Configuration probes have been checked against synthetic Codex `mcp list` output and Claude Code help; they do not qualify a live harness, live sign-in or provider billing. Install, authenticate and qualify the intended harness separately. Codex's goals feature must be available before launch.

The custody guard refuses unattended launch while a locally held key of an operator or the sponsor signs without presence, unless the accountable person recorded a current, expiring exception for the goal revision. Revoked and unadmitted keys count too, because possession still permits signatures. This guard does not isolate the harness from the operating-system user's files or credentials. Use a separate user when that isolation is needed.

Codex receives a fresh `CODEX_HOME` with only the approved harness login imported; effective MCP configuration is probed and extra servers or incompatible managed policy refuse launch. Claude Code receives a fresh `CLAUDE_CONFIG_DIR`, restricted settings, hooks/plugins disabled and strict MCP configuration; detected managed policy refuses launch. Remote or undetected managed policy and managed Claude accounts are unsupported. These controls do not stop deliberate same-user file or keychain access.

The worker reserves a run's whole allowance before launch. Unknown or incomplete token/currency usage is charged at the reserved cap; a lost observer or uncontained run remains in flight and reserved. Tokens and currency leave ten percent headroom, and wall time leaves a grace period and heartbeat. These are soft operational thresholds: a single turn, usage in flight or a failed shutdown can exceed them and make the remaining budget negative. Claude's USD budget flag and Codex's rollout token tracking do not turn every unit into a hard spending cap. Generic has no native spending control.

FIX-08 makes previously recorded settlement facts immutable, including usage, scope and containment. Later revisions may append evidence and add previously missing facts only under the record and chronology rules; they cannot change status or replace recorded facts. The ledger charges from the first settlement's usage, so adding a revision cannot refund it, including allowance charged at the cap because usage was unreported.

Stdout silence, not stderr or a harness assertion of progress, drives stall handling. The default limit is 600 seconds, configurable by the launcher and overridden by the goal's `limits.stallSeconds`. At one limit it records attention; at twice the limit it requests a stop and attempts containment. Root exit or closed output alone never establishes that the whole launch boundary exited.

## Worker containment and the workspace audit

| Platform or condition | Limit |
|---|---|
| Linux and macOS | The boundary is a POSIX process group. A process can escape by leaving the group or session; there is no independent guardian after a worker crash. |
| Linux process inventory | Unavailable or filtered procfs can prevent proving containment. A failed inventory leaves the reservation in flight and is retried. |
| macOS process inventory | An inventory error also leaves containment unknown and the reservation in flight. |
| POSIX embedding | PID 1, ignored `SIGCHLD` or `SA_NOCLDWAIT` are refused; an embedding host with a separate child reaper is unsupported. The worker retains its child identity until final inventory and signaling. |
| Windows | A Job Object prevents breakaway after assignment and kills members when its last handle closes. A crash between suspended process creation and job assignment can leave a process behind. There is no graceful signal for a windowless process: a harness that does not watch the stop file needs escalation. |
| Stop observation | Request, transmission, acknowledgment, escalation and containment are separate facts. The command can return after its bounded wait before containment; inspect `worker runs`. Failed signals or queries are not evidence of exit. |

The scope audit compares physical workspace inventories before launch and after containment, including ignored and untracked files. From 0.6.1 it also compares the run branch's head and every commit reachable from it but not from the prepared baseline against that baseline, including merged side branches. Restoring a committed change in the worktree or a later commit, or checking out a different head, does not hide its paths. The audit pins the original Git object/reference directory and reads through isolated metadata without agent-writable configuration, replacement refs, grafts, attributes or external diff drivers. Missing history or an unreadable reference makes it incomplete.

`permittedChanges` means relative paths/directories or globs; prose is not a wildcard. The audit reports outside paths and incomplete observations. It is not isolation, cannot identify who made a concurrent change, and cannot see uncommitted changes reverted before settlement or commits no longer reachable from the run branch. Git administration itself and files outside the workspace are excluded; submodules require separate auditing. Each inventory has a 30-second deadline, a 100,000-file and 1 GiB content limit. Committed history shares the settlement inventory's deadline, with at most 10,000 new commits and 100,000 distinct paths and bounded metadata and Git output. Pattern comparison has its own 30-second deadline. A `problem` means counts may be partial, even when `outsideCount` is zero. Export rejects links and reparse points inside `.vericairn`; this protects exported worker files, not the rest of the user's filesystem. `software.md` describes these mechanisms in full.

## Pairing and the local-network profile

The listener is off by default and needs an explicit address, restart, network reachability and an administrator-issued pairing code. There is no discovery, relay or firewall configuration. Public/wildcard binding requires `--allow-public`. `--no-loopback` does not disable a configured local-network listener. The Remote profile is not implemented.

- Pairing codes contain 16 secret characters (80 bits), displayed in four groups, valid ten minutes for one device; five failed confirmations invalidate a code. Pairing is not a password-authenticated key exchange: a man-in-the-middle who obtains a hello can test guesses offline, which is why the code has that entropy. Whoever learns a code can consume its one admission. Inspect the device list and revoke an unwanted device.
- From 0.6.1 a code retains its issuing device, admission revision and the operator-role revision or `administer` grant used at issuance. Loss of admission, or revocation or expiry of that authority, invalidates the code and its open exchanges. Readmission or restored authority does not revive it; another device or grant cannot rescue it. A rename, key rotation retaining admission, or authority revision retaining its scope without an expiry gap leaves it valid. The runtime rechecks the issuer under the estate write lock before writing either pairing record. This corrects code revocation, not the interruption limits below.
- The local-network transport uses mutual TLS 1.2, with runtime-identity proof bound to that connection and signed requests bound to the runtime identity. TLS 1.2 exposes handshake certificates and signatures to observers; application messages are encrypted. A network observer with the device records can correlate a connecting device from that signature.
- Loopback now checks signing time and the device's current admission before reading a body, as the local-network profile already does. Headers alone do not prove possession of the key; signature verification follows the bounded read, with freshness and admission checked again. Device-signed bodies are capped at 8 MiB (8,388,608 bytes), or Kestrel's lower configured limit, and a 30-second read deadline, shortened when the signature expires sooner. Body-size refusal is `413`; timeout is `401`. Local-socket bodies retain the configured limit (30,000,000 bytes by default); pairing messages are capped at 16 KiB. Refusals complete before bounded discard of at most 64 KiB for ten seconds. This bounds work; it is not a denial-of-service guarantee.
- Request freshness allows five minutes of age and at most five seconds of clock lead, and refuses signing times before the runtime instance's startup cutoff. Replay protection holds within one runtime instance: its process-local replay cache is lost on restart. The startup cutoff rejects older signatures, but a request signed ahead of the clock within the permitted five-second lead can be replayed after a restart before its signing time. Clock synchronization matters; there is no durable exactly-once guarantee across restarts or clock rollback.
- Admission, current key and local-network permission are checked per request and again under the write lock before a write. Revoking a device, rotating its key or withdrawing the profile invalidates an authenticated request that has not yet written. Closing a connection or deleting local configuration revokes nothing.
- A paired key signs requests and client certificates. Requested records are signed by the same principal's admitted key on the runtime's machine; a presence-required key is refused for such a write. With no local key, optional-signature policy can permit unsigned records; required signatures refuse them. The local write ledger names the requesting device and survives its revocation; it is machine state, not a signed estate record.
- Pairing is not crash-atomic. It writes the validated device before validating and writing its admission, removing the new device on a normal admission refusal. A process failure between stores can leave an unadmitted device. A lost final reply can leave both records while the client removes its key. Inspect the server before retrying; revocation, not removing a saved connection, ends the old device's authority.
- Local-machine actions are refused through this listener: worker launch and goal step, release-provider operations, local device creation/rotation/recovery, issuing pairing codes, runtime certificate changes, exporting files, object uploads and applying upgrades. Reads, permitted record operations and authorized worker stops are available; `api.md` lists the exact route restrictions.
- Replacing the TLS certificate preserves the identity pin; `runtime rotate --compromised` closes open connections and handshakes. Replacing the identity requires every client to pair again. Client transport keys are temporary, but a crash can leave temporary key material in macOS or Windows stores. Unix file modes are owner-only; Windows configuration and connection files inherit directory ACLs.
- Saved connections are consumed by the SDK. Workbench and ordinary estate CLI commands have no saved-connection selector.

## Key custody and records

Only Secure Enclave custody enforces interactive presence, for a person's device. Windows CNG has no presence prompt in this implementation; agents and services never claim presence. Automated enclave tests use keys without presence and do not qualify Touch ID, password prompts or behavior with a locked screen. CNG and Secure Enclave keys are non-exportable through these stores; an unlocked secret service or a file key remains reachable by the same operating-system user. File permissions are owner-only on Unix; Windows files inherit their parent directory ACLs.

In the first-party estate, the Reviewer principal's key is kept apart from the builder's by procedure, not enforced isolation. Decision `01M51CDST3GS2CDFXD22Z3NKJP` carries that limitation into 0.6.1 and returns it to CRED-01 and ID-03. Separate principals and review records alone do not establish operating-system credential isolation.

Upgrading moves no key. Format-1 estates cannot name Secure Enclave custody or presence until their move to format 2. Admissions and revocations at format 2 require signatures or an anchored unsigned admission from the signed migration. The migration lists all unsigned admissions, including superseded ones and revocations, without rewriting any record. A signature policy still uses the date stated by the record, and record removal is visible in repository history or `recover compare`, not necessarily in `estate check`. A potentially leaked key is revoked, not merely renamed or rotated. Recovery keys need separate offline custody; exports carry no private keys.

## Workbench, MCP and living documents

Workbench's Goals workspace, asks and progress are read views. It has no goal activation/acceptance, worker launch/stop or pairing form. Headless journeys cover the work loop, releases, Documents, Goals and progress. They do not establish native desktop, screen-reader or interactive-presence qualification; consult the estate's qualification records for the observed cells. There is no claim here of a new native qualification for 0.6.1. Projects are not a record type. Record comparison is a line difference of canonical JSON, and docking is panel placement with keyboard alternatives. The probe has no direct open option for every workspace and can run before a newly started daemon has supplied a page; start the daemon first.

MCP supports protocol revisions 2025-06-18, 2025-03-26 and 2024-11-05 over stdio, with `initialize`, `ping`, `tools/list` and `tools/call`. Resources, prompts, sampling and HTTP transport are not implemented. Its four writes are `work_attempt`, `work_result`, `work_amend` and `document_review`, exposed only when the acting principal has an admitted key here. Goal and ask tools read; they do not activate, fund, accept, step or answer. Harness qualification requires a recorded observation, not a compatible-looking configuration.

Living-document checks depend on local covered files, registered external clones and installed contributions. Missing contributions can make a document unavailable and hold its product's release. The software pack still has no dedicated reference/notices/README semantic checks, automatic declaration proposals or declaration-retirement operation. Workbench does not show a document's content beside its triggers. The normal progress report includes repository checks; its generated region excludes them to avoid reading itself.

## Other limitations and artifacts

The GitHub Releases provider runs through the runtime user's `gh` login. It identifies a dispatched run by listing recent runs and can report uncertainty when none appears within thirty seconds. It is the only shipped release provider. Verifications preserve a command's exit, duration and output; they do not parse test reports. Third-party notices and license texts remain hand-maintained beside the generated SBOM and checked against lock files. The 0.6.0 review found its SBOM named the component 0.0.0, included packages absent from archives and omitted the embedded .NET and ASP.NET Core runtimes; the generation command is unchanged, and no 0.6.1 SBOM has been inspected for this pre-build set. Use the notices for the bundled-component inventory and inspect the new artifacts during release review. Live stores for clones no longer present are not listed or removed by a command.

The notes are rendered by the 0.6.1 command line from the end of 0.5.2's notes, with the approved Security section inserted unchanged. FIX-08 omits superseded results and recognizes acceptance of the current work order as accepted work without accepting each result record. The classification uses current estate state, not a historical snapshot of acceptance at the window's end. Windows use record timestamps: a later-imported backdated record can be missed, and the minute-precision end stated by 0.5.2 can duplicate a record from that minute. The manifest records the new window precisely for the next set.

The workflow is configured to produce:

| Artifact | Contents |
|---|---|
| `vericairn-0.6.1-<platform>.tar.gz` (`.zip` on Windows), six platforms | Self-contained CLI, LICENSE, NOTICE, notices and `licenses/`; macOS includes `bin/vericairn-enclave` |
| `vericairn-workbench-0.6.1-<platform>.tar.gz` (`.zip` on Windows), six platforms | Workbench with the CLI, macOS helper where applicable, and license material |
| `vericairn-0.6.1-sbom.cdx.json` | CycloneDX bill of materials |
| `vericairn-0.6.1-THIRD-PARTY-NOTICES.md` | Third-party notices |
| `vericairn-0.6.1-docs.zip` | This documentation set, including release notes rendered by 0.6.1 and the approved Security section |
| `SHA256SUMS` | Artifact SHA-256 values |

Hashes, sizes, observations and acceptance belong to the release record and its evidence. The structural documentation check verifies the set's completeness; release review and acceptance remain separate steps.
