# Limitations and platforms, Vericairn 0.7.2

The estate's release record, written when the candidate is observed and superseded when publication is observed, is the authority for its commit, workflow run, artifact hashes and publication. This set describes implementation and test boundaries. Platform and live-session qualifications require their own recorded observations; documentation and stand-in tests do not establish them.

0.7.2 follows published 0.7.1 with five fixes: FIX-13's identifiers, exact-commit release gate, grouped notes,
settled attention, workflow checks and Workbench first screen; FIX-14's inventory-bound export restore
(F-01); FIX-15's claimed release dispatch and bounded Git/gh runner; FIX-16's validation before record
publication and visible unreadable journals; and FIX-18's corrected Codex worker at max effort.
The withdrawn 0.7.0 candidate's work shipped in 0.7.1 with FIX-12. All earlier sets stay frozen and indexed.

The earlier custody, accounting and transport limits still hold. Tests and documentation do not replace
independent review or operator acceptance.

## Machine-local worker configuration and qualification

FIX-11 supplies the stock CLI and daemon with native worker configuration. Before a native run, use `vericairn worker configure --launcher codex --model <model> --effort <effort>` (or `claude-code`); omit choices to show it. Both model and effort are required when setting. Repeated `--allow-model` and `--allow-effort` options narrow choices and must include their defaults. Without a model allowlist only the default is admitted; without an effort allowlist the launcher's supported efforts are admitted. Each set replaces that launcher's defaults and allowlists. Missing or malformed configuration refuses before intent, and an explicit run override cannot replace it or escape its restrictions.

Configuration is per machine and is never recorded in the estate. Owner-only `worker/codex.json` and `worker/claude-code.json` files live under the user's Vericairn home. Reads and writes are refused when that home lies inside a Git repository or estate. If the user's home directory is a repository, set `VERICAIRN_HOME` outside all repositories and estates for the CLI and daemon before starting them. The configuration file, worker directory and Vericairn home cannot be symbolic links or reparse points; resolved ancestor links cannot hide a repository or estate. The stock CLI and daemon read configuration for each run and agent goal step and recheck selection before intent and dispatch. Changing the choices needs no daemon restart. The local files are never copied into records; a run intent retains its selected portable choices, their provenance and configuration identity.

Configuration narrows and never widens the qualified inventory. It cannot add tools, servers, environment variables or routes. The stock environment policy derives only from inventory admitted by a shipped baseline or an exact current RUN-03 qualification. A host-supplied policy must still match that qualified inventory and may refuse more. `worker configure` runs no probes, launches no agent and has no HTTP or MCP write route.

Qualification is also machine-local. `POST /v1/harness-qualifications` refuses a paired local-network device with `401` / `denied` before probes or writes, even for an authorized human or a request with a local command override. The local socket and signed loopback retain the existing human qualification authority checks. Qualification records are portable only when every local probe digest matches; qualification grants no work authority or live-inference assurance.

FIX-12 supplies a separate workspace root: `vericairn worker configure --workspaces <absolute-directory>` sets it; `vericairn worker configure` shows it. This setting omits `--launcher` and applies to every launcher. The owner-only `worker/workspaces.json` contains the absolute `root`, per machine and never recorded. It lets a person put workspaces outside a home holding Claude Code's or Codex's own configuration. The root must be outside Git repositories (including bare repositories) and estates. Neither it nor its ancestors may contain `.claude`, `.codex`, `.agents` or `.mcp.json`. The root itself cannot be a symbolic link or reparse point; linked ancestors are resolved and the canonical root is stored. New roots are created owner-only. Invalid files, duplicate keys and unknown fields refuse without fallback.

The CLI and daemon reread the root for every run and recheck before intent and dispatch, without a restart. A changed root after intent yields an unstarted, zero-charge failure. New workspaces use `<root>/<estate>/<branch-slug>`, with branch slashes flattened to hyphens; the unconfigured default remains `<VericairnHome>/estates/<estate>/workspaces/<branch-slug>`. An existing worktree is reused only under the selected root. If outside it, preserve the work and explicitly remove the worktree using the refusal's guidance, or select a root containing it. Setting the root moves and deletes nothing.

Codex still independently inspects the user's own `~/.agents`, outside `CODEX_HOME`. A run is refused while it contains skills or other customization, even with relocated workspaces and a fresh harness home. Use a worker account without that discovery root. The native scan permits an empty ordinary directory or one containing only recognized instruction files; it refuses other entries, child directories and uninspected links. Workspace-root validation itself rejects any `.agents` marker in the root or its ancestors, including an empty directory. Moving workspaces does not admit managed policy or other unqualified customization.

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

The NuGet packages are framework-dependent: the `dotnet-vc` tool needs the .NET and ASP.NET Core 10 runtimes supplied by the .NET 10 SDK. It installs no Workbench and no macOS Secure Enclave helper. Use the macOS archive for the bundled helper. The worker's agent-side MCP command still resolves `vericairn`, so a NuGet-only worker installation is not qualified by package smoke tests. `Vericairn` is a dependency-only SDK front door; it does not bring an embedded runtime or daemon. The release workflow builds packages into the draft; NuGet Trusted Publishing happens only after the operator publishes that release. Availability of the 0.7.2 packages follows its successful publication workflow; 0.7.1 packages are already published. Name-reservation versions are not product releases.

Nuget.org receives exactly the package assets the operator publishes, verified against that release's own `SHA256SUMS`. The workflow checks completeness and hashes but does not compare with the estate's accepted candidate or require its acceptance record; a published prerelease also triggers it. Publishing the exact accepted draft supplies that binding. Changing both assets and their checksum file would pass this comparison. Upload never rebuilds packages. Duplicate versions are skipped, not replaced or independently verified against nuget.org's existing bytes.

FIX-13 checks action and reusable-workflow references in every workflow against the repository's
full-commit allow-list, including release-only workflows. It does not inspect the organization's live
Actions policy. The NuGet pull-request job builds packages and symbols and runs the publication verifier;
it has no login, identity-token permission or upload. Checksum verification detects missing, duplicate,
altered or mismatched package files, not a malicious producer that changes both assets and checksums.

## Goals, routing and asks

0.7.2 retains goal proposal and lifecycle, record predicates, independent evaluations, acceptance, roster routing, asks and one-step dispatch, and retains record-derived hand-over and resume. It keeps record format 2; 0.5.2 was the first published format-2 release. The progress, communication, execution and harness-qualification additions shipped in 0.7.1 remain; new worker signal values need readers that understand 0.7.2. A shared format number does not establish that an older writer understands them.

- The evaluator must have authored no step; the accepter must be neither a step's author nor the evaluator. Activation and spending need the accountable person's authority. Goal acceptance is not an automatic consequence of a passing predicate or an exited harness.
- `goal next` depends on this machine's launchers, keys and custody. Predicates involving covered files or unavailable living-document contributions can also differ by machine. Record-only predicates and the eligibility relations derive from the records.
- `goal step` takes one eligible step per call. There is no continuous multi-step planner or scheduler in this Community implementation. It neither activates nor accepts a goal, funds more work, nor answers an ask.
- An assignment and its run intent or ask are validated together and journaled, but sequential storage is not atomic. A stranded routed assignment holds no step, and reconciliation reports an incomplete pair.
- Routing does not silently replace an unavailable contributor. A person takes over with a new assignment; the old runs receive stop requests, whose containment still must be observed.
- FIX-12 checks the work order's current holder for every run, before workspace preparation and again under the write lock before intent. A new run on an assignment displaced by hand-over is refused whether explicitly named or routed, with nothing recorded or charged. The check grants no new authority and does not certify containment of an existing run.
- Asks deliver at most three per principal in a read response, with the remainder queued. A stalled run can record an attention ask, but there is no external notification delivery here. A decision by the principal asked answers it; reading, dismissing or waiting does not.
- Settlement withdraws the run's still-open or notified process attention asks, including stall and command-audit asks, as no longer needed. Withdrawals pin the exact settlement and preserve the question and observations; already answered asks stay answered. Closed asks no longer produce stale-subject warnings. The new `attempt-missing` and `scope-settlement` asks remain open for review, pinned to the settled operation's exact revision and hash. The settlement, new asks and withdrawals are validated together and journaled; interrupted storage still requires reconciliation. Withdrawal is not an answer by the person asked and accepts no work.
- Pausing or abandoning a goal prevents new launches; it does not stop runs already in flight. `goal stop` records abandonment; use `worker stop` for each run that must stop and inspect the stop facts. `limits.deadline` and `limits.consecutiveFailures` are validated and recorded but not yet enforced by the routing or worker code. Operators must monitor and act on them.
- Resumption and goal revision do not erase run charges: the ledger includes runs naming any revision of that goal. Concurrency defaults to one when not stated; omitted token or currency allowance is not unlimited spending authority.

## Worker launchers, limits and custody

The software pack lists `claude-code`, `codex` and `generic`. The native launchers configure the harness's goal mode and estate MCP server, and load the machine-local configuration described above. Generic dispatch is refused before executing its command because it cannot establish a complete inventory. The only compiled native baselines are codex-cli `0.159.0-alpha.12.1` and Claude Code `2.1.296`; other exact versions need a person's authorized RUN-03 qualification and matching local probes. No version range is inferred.

RUN-03 qualification is restricted to a human operator or a human explicitly granted `qualify`; agents and services cannot qualify, and `administer` or `local-engineering` alone is insufficient. The record pins six probe digests, not a vendor signature or a live-inference observation. Admission checks the version, declared flags, tools, configuration, login boundary and MCP facts locally before intent and again before dispatch. A non-baseline qualification must still be current and match all probes, and the independently admitted RUN-01 inventory must match. Revoking a record does not remove a compiled baseline. Protocol tests use stand-in processes and captured fixtures. The five live Codex runs below provide separate, bounded evidence; neither kind establishes provider billing, arbitrary future event formats or vendor authenticity.

Every run requires explicit model, effort, provider, destination and data boundary. The local launcher configuration, roster, goal, assignment and run choices have recorded precedence; restrictions accumulate. Missing required choices or unknown exact revisions refuse rather than using ambient defaults. The environment inventory must cover tools, skills, plugins, hooks, MCP servers, helpers, dependencies and effective configuration, with matching admitted identities and inherited environment limits. Requested execution is separate from observed execution; an absent actual fact remains unknown. The efficiency Adapter is absent until the planned 0.8.0 work, and any request to enable it is refused.

The custody guard refuses unattended launch while a locally held key of an operator or the sponsor signs without presence, unless the accountable person recorded a current, expiring exception for the goal revision. Revoked and unadmitted keys count too, because possession still permits signatures. This guard does not isolate the harness from the operating-system user's files or credentials. Use a separate user when that isolation is needed.

Codex receives a fresh `CODEX_HOME` with only the approved harness login imported; effective MCP configuration is probed and extra servers or incompatible managed policy refuse launch. Claude Code receives a fresh `CLAUDE_CONFIG_DIR`, restricted settings, hooks/plugins disabled and strict MCP configuration; detected managed policy refuses launch. Remote or undetected managed policy and managed Claude accounts are unsupported. These controls do not stop deliberate same-user file or keychain access.

The worker reserves a run's whole allowance before launch. Unknown or incomplete token/currency usage is charged at the reserved cap; a lost observer or uncontained run remains in flight and reserved. Tokens and currency leave ten percent headroom, and wall time leaves a grace period and heartbeat. These are soft operational thresholds: a single turn, usage in flight or a failed shutdown can exceed them and make the remaining budget negative. Claude's USD budget flag and Codex's rollout token tracking do not turn every unit into a hard spending cap. Generic is not an admitted alternative. An uncertain prior run blocks retry for that assignment until its original operation identity is reconciled.

FIX-08 makes previously recorded settlement facts immutable, including usage, scope and containment. Later revisions may append evidence and add previously missing facts only under the record and chronology rules; they cannot change status or replace recorded facts. The ledger charges from the first settlement's usage, so adding a revision cannot refund it, including allowance charged at the cap because usage was unreported.

Stdout silence, not stderr or a harness assertion of progress, drives stall handling. The default limit is 600 seconds; precedence is goal `limits.stallSeconds`, roster limit, launcher request setting, then default. At one limit it records attention; at twice the limit it requests a stop and attempts containment. Root exit or closed output alone never establishes that the whole launch boundary exited.

WATCH-01 observes emitted command/tool events, loops and reported token rates; it does not interpret every program or prove absence of unreported effects. An invocation can already have started before its event is observed. Raw inputs stay local; records retain fixed summaries and hashes. Incomplete checks remain visible, and FIX-10 preserves independent verdicts when another check fails. Critical credential, confidently classified outside write, inventory, execution, loop and rate signals use the stop and containment path; warnings and the final scope audit create attention. Failed attention writes do not block critical stops and remain explicitly pending. Default rate observations need 60 seconds and 10,000 reported tokens; unknown usage cannot establish a rate. The full settings and bounds are in `software.md`. Agent progress claims never reset counters or establish liveness, scope or acceptance.

## Live Codex qualification (FIX-18)

FIX-18 admits Codex's `max` effort as well as `none`, `minimal`, `low`, `medium`, `high` and `xhigh`.
The harness checks whether the selected model supports the effort at launch; an observed refusal settles
as `known-failure`. The launcher enables `code_mode_host` for ordinary shell and MCP tools while
`code_mode` and `code_mode_only` stay disabled. It reads model and effort from the run's own rollout after
exit, never assistant prose or ambient sessions; missing or unreadable facts stay unknown, and mismatches
remain discrepancies. A run with no new attempt by the agent on its exact assignment gets the
`attempt-missing` settlement warning and review ask, without changing the meaning of `observed-success`.

Only the estate MCP server's tools are pre-approved, in both generated configuration and launch overrides.
Global approval policy stays unset; the server still checks the run principal's authority. The baseline's
MCP list omits approval settings and tool lists, so approval verification rests on the exact generated
configuration and isolation checks, not an independent report of those settings.

Five builder live runs with codex-cli `0.159.0-alpha.12.1` exercised `gpt-6-astra` at `max`:

| Run and code commit | Observed outcome and its limit |
|---|---|
| First, `6bab07f` | The session reported model and max effort; 1 wall minute and 34,573 tokens were recorded. Every tool call failed with the code-mode host disabled. No attempt or result was recorded. |
| Second, `80423b5` | The first shell command ran, rollout model/effort matched admission, and settlement raised missing-attempt attention. The old audit stopped an outside-workspace read. |
| Third, `42f989d` | The workspace search and brief read ran. The old audit mistook a Python heredoc division operator for an outside write target and stopped the run. |
| Fourth, `8e272de` | The corrected audit warned for opaque Python input without stopping it. The run settled observed-success, with 2 wall minutes and 95,406 tokens; MCP approval under exec's never policy prevented an attempt/result, and no file changed. Missing-attempt attention appeared. |
| Fifth, `531d169` | The cap was recorded before launch, the admitted model and max effort were observed, and usage was 3 wall minutes and 205,767 tokens. Estate-only pre-approval allowed a signed attempt and result. Exactly the permitted `NOTES.md` changed (40 bytes). The commit failed at the sandbox boundary. |

These runs qualify the reported live Codex path: inference at max, ordinary tools, rollout observation,
missing-attempt attention, brief access, the corrected audit and estate-only MCP attempt/result authoring.
They do not qualify arbitrary commands, other versions, models, accounts or platforms, Claude Code live
sign-in, provider billing, hard spending caps, compaction/recovery, remote managed policy or acceptance of
an agent's work. Stand-in protocol coverage and the release's three-platform CI remain separate evidence.

A worker-launched Codex run cannot yet commit: the workspace is a git worktree whose Git metadata lies
outside the sandbox. Its changes stay uncommitted in the worktree for review. FIX-19 makes the workspace
a repository of its own; that change is not in 0.7.2.

## Command-audit classification (FIX-18)

FIX-18 distinguishes reads, known write targets and unclassified effects. Reads inside the workspace
need no change permission; a read outside it raises `scope-read` warning and continues. Critical `scope`
requires a confidently identified write target outside the workspace and the temporary directory supplied
at launch, such as a known mutator's operand, output redirection, patch path or explicit Git write target.
Changes inside the worktree are checked against the brief at settlement. The audit accepts only the
captured launch temporary variables, not an ambient default or a boundary claimed in a tool event.

Opaque interpreter input, heredocs, here-strings, unresolved expansions, unclassified outside-looking
operands and uncertain physical containment yield `audit-unverified` warnings instead of speculative
scope stops. Recognizable credential access remains critical for reads and writes; network attempts warn.
Git messages are text, and Git object selectors are not filesystem paths. The classifier recognizes
common command forms, not arbitrary programs, aliases, every option or every shell expansion; it cannot
prove that unreported network, credential or filesystem effects did not occur. Events can arrive after
an action starts. The final scope audit, operating-system isolation and credential separation remain
necessary. This change narrows scope stops, not the independent inventory, execution, loop or rate stops.

## Worker containment and the workspace audit

| Platform or condition | Limit |
|---|---|
| Linux and macOS | The boundary is a POSIX process group. A process can escape by leaving the group or session; there is no independent guardian after a worker crash. |
| Linux process inventory | Unavailable or filtered procfs can prevent proving containment. A failed inventory leaves the reservation in flight and is retried. |
| macOS process inventory | An inventory error also leaves containment unknown and the reservation in flight. |
| POSIX embedding | PID 1, ignored `SIGCHLD` or `SA_NOCLDWAIT` are refused; an embedding host with a separate child reaper is unsupported. The worker retains its child identity until final inventory and signaling. |
| Windows | A Job Object prevents breakaway after assignment and kills members when its last handle closes. A crash between suspended process creation and job assignment can leave a process behind. There is no graceful signal for a windowless process: a harness that does not watch the stop file needs escalation. |
| Stop observation | Request, transmission, acknowledgment, escalation and containment are separate facts. The command can return after its bounded wait before containment; inspect `worker runs`. Failed signals or queries are not evidence of exit. |

The scope audit compares physical workspace inventories before launch and after containment, including ignored and untracked files. Since 0.6.1 it also compares the run branch's head and every commit reachable from it but not from the prepared baseline against that baseline, including merged side branches. Restoring a committed change in the worktree or a later commit, or checking out a different head, does not hide its paths. The audit pins the original Git object/reference directory and reads through isolated metadata without agent-writable configuration, replacement refs, grafts, attributes or external diff drivers. Missing history or an unreadable reference makes it incomplete.

`permittedChanges` means relative paths/directories or globs; prose is not a wildcard. FIX-18 requires at least one interpretable pattern before intent. Bare patterns contain no whitespace; `path:` permits literal names with spaces, for example `path:docs/operator guide.md`. Prose can accompany a pattern, but absolute paths, parent traversal and shell expansion do not grant permission. The audit reports outside paths and incomplete observations. It is not isolation, cannot identify who made a concurrent change, and cannot see uncommitted changes reverted before settlement or commits no longer reachable from the run branch. Git administration itself and files outside the workspace are excluded; submodules require separate auditing. Each inventory has a 30-second deadline, a 100,000-file and 1 GiB content limit. Committed history shares the settlement inventory's deadline, with at most 10,000 new commits and 100,000 distinct paths and bounded metadata and Git output. Pattern comparison has its own 30-second deadline. A `problem` means counts may be partial, even when `outsideCount` is zero. Export rejects links and reparse points inside `.vericairn`; this protects exported worker files, not the rest of the user's filesystem. `software.md` describes these mechanisms in full.

FIX-10 makes path matching deterministic, ordinal and case-sensitive on every platform. `**/` matches zero or more directories. Dynamic programming removes regex timeout ambiguity; it does not remove the audit's overall time, file, history or byte bounds.

## Pairing and the local-network profile

The listener is off by default and needs an explicit address, restart, network reachability and an administrator-issued pairing code. There is no discovery, relay or firewall configuration. Public/wildcard binding requires `--allow-public`. `--no-loopback` does not disable a configured local-network listener. The Remote profile is not implemented.

- Pairing codes contain 16 secret characters (80 bits), displayed in four groups, valid ten minutes for one device; five failed confirmations invalidate a code. Pairing is not a password-authenticated key exchange: a man-in-the-middle who obtains a hello can test guesses offline, which is why the code has that entropy. Whoever learns a code can consume its one admission. Inspect the device list and revoke an unwanted device.
- Since 0.6.1 a code retains its issuing device, admission revision and the operator-role revision or `administer` grant used at issuance. Loss of admission, or revocation or expiry of that authority, invalidates the code and its open exchanges. Readmission or restored authority does not revive it; another device or grant cannot rescue it. A rename, key rotation retaining admission, or authority revision retaining its scope without an expiry gap leaves it valid. The runtime rechecks the issuer under the estate write lock before writing either pairing record. This corrects code revocation, not the interruption limits below.
- The local-network transport uses mutual TLS 1.2, with runtime-identity proof bound to that connection and signed requests bound to the runtime identity. TLS 1.2 exposes handshake certificates and signatures to observers; application messages are encrypted. A network observer with the device records can correlate a connecting device from that signature.
- Loopback now checks signing time and the device's current admission before reading a body, as the local-network profile already does. Headers alone do not prove possession of the key; signature verification follows the bounded read, with freshness and admission checked again. Device-signed bodies are capped at 8 MiB (8,388,608 bytes), or Kestrel's lower configured limit, and a 30-second read deadline, shortened when the signature expires sooner. Body-size refusal is `413`; timeout is `401`. Local-socket bodies retain the configured limit (30,000,000 bytes by default); pairing messages are capped at 16 KiB. Refusals complete before bounded discard of at most 64 KiB for ten seconds. This bounds work; it is not a denial-of-service guarantee.
- FIX-09 admits at most eight concurrent loopback body reads awaiting signature verification across all devices, with no waiting queue (`503` when full). Kestrel limits concurrent connections to 32. Neither bound claims that the service is immune to denial of service.
- Request freshness allows five minutes of age and at most five seconds of clock lead, and refuses signing times before the runtime instance's startup cutoff. Replay protection holds within one runtime instance: its process-local replay cache is lost on restart. The startup cutoff rejects older signatures, but a request signed ahead of the clock within the permitted five-second lead can be replayed after a restart before its signing time. Clock synchronization matters; there is no durable exactly-once guarantee across restarts or clock rollback.
- Admission, current key and local-network permission are checked per request and again under the write lock before a write. Revoking a device, rotating its key or withdrawing the profile invalidates an authenticated request that has not yet written. Closing a connection or deleting local configuration revokes nothing.
- A paired key signs requests and client certificates. Requested records are signed by the same principal's admitted key on the runtime's machine; a presence-required key is refused for such a write. With no local key, optional-signature policy can permit unsigned records; required signatures refuse them. The local write ledger names the requesting device and survives its revocation; it is machine state, not a signed estate record.
- Pairing is not crash-atomic. It writes the validated device before validating and writing its admission, removing the new device on a normal admission refusal. A process failure between stores can leave an unadmitted device. A lost final reply can leave both records while the client removes its key. Inspect the server before retrying; revocation, not removing a saved connection, ends the old device's authority.
- Local-machine actions are refused through this listener: worker launch and goal step, harness qualification, release-provider operations, local device creation/rotation/recovery, issuing pairing codes, runtime certificate changes, exporting files, object uploads and applying upgrades. Reads, permitted record operations and authorized worker stops are available; `api.md` lists the exact route restrictions.
- Replacing the TLS certificate preserves the identity pin; `runtime rotate --compromised` closes open connections and handshakes. Replacing the identity requires every client to pair again. Client transport keys are temporary, but a crash can leave temporary key material in macOS or Windows stores. Unix file modes are owner-only; Windows configuration and connection files inherit directory ACLs.
- Saved connections are consumed by the SDK. Workbench and ordinary estate CLI commands have no saved-connection selector.

## Export restore, record publication and recovery

FIX-14 (F-01) binds each restored file to the export inventory. Verification refuses duplicate inventory
paths or archive members (including metadata), unsafe relative paths and file/directory or normalized-name
collisions before creating destination material. Case aliases are refused on every platform; macOS also
checks canonical Unicode equivalence and case folding, and Windows checks its trailing-dot/space aliases
and device names. Restore keeps one archive open, extracts only the members it verified, rechecks each
buffer's size and hash before writing and creates targets without overwriting. The manifest's conversion
to the repository-synced profile remains deliberate.

Restore is not an all-or-nothing filesystem transaction. An I/O failure or source change during extraction
can leave earlier verified files in the destination; the operation does not report success. Inspect a
failed destination and preserve it as needed before choosing another empty directory. The inventory
does not establish who produced an export or grant authority. Record signatures are checked separately,
and no private keys travel with the export.

FIX-16 judges single records against the proposed estate, including content checks, before publication,
as grouped records already are. A validation failure publishes nothing; repository records are staged
outside the published tree and renamed only after judgment. The gate belongs to one `EstateService`
instance, not all services or processes. Route clients through one daemon, or stop it and run embedded
commands sequentially; do not overlap daemon startup, embedded commands or repository synchronization.
A separate service's reads can reconcile journals too. Grouped storage remains sequential and can stop
between records, with partially stored batches visible to independent readers.

Unreadable, malformed or incomplete local journal data remains visible as `batch.unreadable` in status,
check and reconciliation, with a safe journal id or filename digest. The runtime keeps the journal and
retries it on later checks and reopen. Because its effects cannot be identified, every further single
or grouped record write is refused with `reconcile-required`. Reads, local configuration and interim
live progress notes remain available. Restore readable journal data, or have the operator inspect the
stored records and preserve the damaged journal outside the local batches directory before retrying.
A readable interrupted batch still reports `batch.incomplete` under its existing rules; it does not impose
the unreadable journal's write hold. This is local recovery state, not a new record format.

## Key custody and records

Only Secure Enclave custody enforces interactive presence, for a person's device. Windows CNG has no presence prompt in this implementation; agents and services never claim presence. Automated enclave tests use keys without presence and do not qualify Touch ID, password prompts or behavior with a locked screen. CNG and Secure Enclave keys are non-exportable through these stores; an unlocked secret service or a file key remains reachable by the same operating-system user. File permissions are owner-only on Unix; Windows files inherit their parent directory ACLs.

In the first-party estate, decision `01M51CDST3GS2CDFXD22Z3NKJP` carried the limitation of procedural, rather than enforced, separation of the Reviewer principal's key from the builder's into 0.6.1, returning it to CRED-01 and ID-03. This documentation work establishes no new credential isolation. Separate principals and review records alone do not establish operating-system credential isolation.

Upgrading moves no key. Format-1 estates cannot name Secure Enclave custody or presence until their move to format 2. Admissions and revocations at format 2 require signatures or an anchored unsigned admission from the signed migration. The migration lists all unsigned admissions, including superseded ones and revocations, without rewriting any record. A signature policy still uses the date stated by the record, and record removal is visible in repository history or `recover compare`, not necessarily in `estate check`. A potentially leaked key is revoked, not merely renamed or rotated. Recovery keys need separate offline custody; exports carry no private keys.

FIX-13 orders generated identifiers within one process, including concurrent calls and repeated or
backward clock readings. Independent processes, machines and explicitly supplied ids have no such order.
An exact hash-pinned result acceptance establishes causality; other judgments use recorded time and id
tie-breaking. This changes no historical record bytes and is not a distributed clock.

FIX-12's credential-pattern exemption is limited to validated device `body.publicKey` and envelope `signatures[].signature` values in record JSON. A key must decode as a P-256 SubjectPublicKeyInfo key; a signature must decode to 64 bytes. This validates the encoding for the scan, not the signature's cryptographic truth. Other fields, nested lookalike names, invalid encodings and non-record files remain scanned. The machine-path scan reads the original text and signature verification still applies; no record bytes change.

## Workbench, MCP and living documents

Workbench's Goals workspace, asks and progress are read views. It has no goal activation/acceptance, worker launch/stop or pairing form. Headless journeys cover the work loop, releases, Documents, Goals and progress. They do not establish native desktop, screen-reader or interactive-presence qualification; consult the estate's qualification records for the observed cells. There is no claim here of a new native qualification for 0.7.2. Projects are not a record type. Record comparison is a line difference of canonical JSON, and docking is panel placement with keyboard alternatives. The probe has no direct open option for every workspace and can run before a newly started daemon has supplied a page; start the daemon first.

FIX-13 reduces Workbench's first-screen work by sharing status and progress observations, avoiding a
duplicate refresh after daemon startup and reusing parsing and hashes for unchanged original bytes.
First-screen API reads no longer grow with unshown record history, but opening still validates every
record once. Large estates can still take time. Wait for loading before treating an absent page as empty;
headless tests establish no fixed native startup-time bound or desktop responsiveness qualification.

MCP supports protocol revisions 2025-06-18, 2025-03-26 and 2024-11-05 over stdio, with `initialize`, `ping`, `tools/list` and `tools/call`. Resources, prompts, sampling and HTTP transport are not implemented. Its five writes are `work_attempt`, `work_progress`, `work_result`, `work_amend` and `document_review`, exposed only when the acting principal has an admitted key here. Communication and context tools read; they do not change preferences or grant authority. Goal and ask tools do not activate, fund, accept, step or answer. There is no harness-qualification or worker-launch MCP tool. The orientation's observed MCP harness matrix is distinct from RUN-03's native worker qualification.

Workbench adds local communication and hand-over/resume views. It has no context-allocation editor, pinned-original retrieval control or harness-qualification form. The communication qualification report covers headless fixture behavior, not native desktop, screen-reader, live harness, Remote or compaction qualification. Optional progress frequency changes presentation, while required attention remains visible; an external agent's compliance with the brief's wording and refresh instruction is not enforced.

Living-document checks depend on local covered files, registered external clones and installed contributions. Missing contributions can make a document unavailable and hold its product's release. The software pack still has no dedicated reference/notices/README semantic checks, automatic declaration proposals or declaration-retirement operation. Workbench does not show a document's content beside its triggers. The normal progress report includes repository checks; its generated region excludes them to avoid reading itself.

FIX-13 judges repository covered paths and cleared document bytes at the exact commit the provider
resolves for the requested release ref. Clone HEAD may differ. An unresolved commit, a missing local
Git object or an unavailable required contribution refuses the applicable gate; fetch the exact commit
into the registered clone and review there. Observation checks the actual build commit again before
recording success or a candidate, because a ref can move after submission. The runtime fetches and
checks out nothing automatically. Normal `docs living` still uses clone HEAD; estate-file checks still
read the estate's working tree. Current estate records supply triggers and reviews, not a historical
snapshot of all estate state at the release commit.

## Release dispatch and bounded commands

FIX-15 claims `<provider>:<destination>:v<version>` under the service gate before calling the provider,
then releases the gate during provider I/O so unrelated requests can answer. A receiptless `not-started`
intent has an unknown effect and blocks another submission, including after restart or failure to store
the receipt. `release observe` records it as uncertain before querying the provider, even if observation
then waits for a run or draft. Observe and reconcile the original identity before retrying. A known
failure or cancellation needs `--reconciliation`; that retry pins its settings and records an uncertain
claim before dispatch. An active provider call cannot be canceled or reconciled until it returns.
A gh refusal before workflow-command launch is a known refusal; interruption after launch or failure to
identify the run is uncertain. These rules are not a distributed exactly-once guarantee between separate
runtime instances.

Git and gh share one bounded runner: 30 seconds per Git command, two minutes per gh command, and
4,194,304 characters per stdout or stderr stream. The deadline includes launch, concurrent output reads
and exit. Overflow fails instead of accepting partial output. Timeout or cancellation terminates the
owned POSIX process group or Windows Job Object, including descendants holding output pipes. Cleanup
waits at most one additional second; unconfirmed exit retains native ownership for later cleanup.
On POSIX the runtime must run under an init process, not as PID 1, without ignored `SIGCHLD`,
`SA_NOCLDWAIT` or a separate child reaper. Unsupported hosts refuse before launch. Process-group escape
and the existing containment limits still apply.

## Other limitations and artifacts

The generated CLI reference includes every visible option at its declaring command, including the recursive root options and `worker configure`, without trailing whitespace. The structural docs check and manifest comparison verify document presence and declared command, operation, type and format inventories; they do not judge prose or prove behavior. Source regression tests check visible-option coverage and generated whitespace.

The GitHub Releases provider runs through the runtime user's `gh` login. It identifies a dispatched run by listing recent runs and can report uncertainty when none appears within thirty seconds. It is the only shipped release provider. Verifications preserve a command's exit, duration and output; they do not parse test reports. Third-party notices and license texts remain hand-maintained beside the generated SBOM and checked against lock files. The 0.6.0 review found its SBOM named the component 0.0.0, included packages absent from archives and omitted the embedded .NET and ASP.NET Core runtimes. The generation command is unchanged; this set establishes no correction or complete inventory from that SBOM. Use the notices for bundled components and the release review's artifact observations. Live stores for clones no longer present are not listed or removed by a command.

The release notes render the estate's records with `--from 0.7.1 --to 0.7.2 --since 2026-10-17T05:25:00Z`,
with the operator's approved Security section on F-01 first. The start is exclusive and widens 0.7.1's
recorded end, 05:25:37Z, by 37 seconds; overlap can repeat records. The manifest records the exact window.
FIX-13 gives accepted work one entry per work order, naming the latest accepted current result in the
window by recorded time and then identifier, with its acceptance. Earlier results and attempts remain
available through `work observe`; later unaccepted results remain separate. Superseded results are omitted.
Classification uses current estate state, not a snapshot at the window's end; work-order acceptance does
not accept each result. Later imports with older timestamps can require another explicit widening.
Earlier sets and their checksums remain unchanged.

## Context and hand-over limits

Context packages measure exact UTF-8 payload bytes including accounting overhead. Tokens, harness framing and model fit remain unknown without a qualified local tokenizer; this build includes none and makes no network tokenization call. A context byte allocation is not a spending cap. Mandatory material must fit; optional selected sources have a complete withheld list. The remainder of the document catalogue is outside the selection, so an empty withheld list is not a whole-estate read claim. A human `work brief` keeps its full selected scope with no byte limit set; preparation and MCP briefs without an allocation withhold named optional bodies.

Pinned record retrieval verifies immutable originals; document retrieval succeeds only while this copy's working-tree bytes match the pinned revision. There is no silent substitution of the latest revision or fallback object store. Missing required originals refuse the dependent preparation. Reads and preparation author nothing; dispatch retains the package in local run custody. No context persistence into the estate or Remote reader is added.

Hand-over retains all records in the copy, including superseded history, with the full estate pin and recorded observations. Source-file bodies, output objects and unrecorded external state are outside that scope. A byte cap refuses overflow rather than truncating. Resume compares identities, not timestamps, but cannot establish that an external provider or the world stayed unchanged. It restarts no run and grants no authority. Interim progress claims remain local; only completed phase records enter the portable history. Saved packages and canonical JSON are unchanged by communication depth. Harness compaction/recovery, the efficiency Adapter and Remote remain unbuilt or unqualified as stated above.

The workflow is configured to produce:

| Artifact | Contents |
|---|---|
| `vericairn-0.7.2-<platform>.tar.gz` (`.zip` on Windows), six platforms | Self-contained CLI, LICENSE, NOTICE, notices and `licenses/`; macOS includes `bin/vericairn-enclave` |
| `vericairn-workbench-0.7.2-<platform>.tar.gz` (`.zip` on Windows), six platforms | Workbench with the CLI, macOS helper where applicable, and license material |
| `vericairn-0.7.2-sbom.cdx.json` | CycloneDX bill of materials |
| `vericairn-0.7.2-THIRD-PARTY-NOTICES.md` | Third-party notices |
| `vericairn-0.7.2-docs.zip` | This documentation set, including the estate-derived release notes and their recorded window |
| `dotnet-vc.0.7.2.nupkg` | Portable .NET tool, invoked as `dotnet vc`; no Workbench or macOS helper |
| `Vericairn.0.7.2.nupkg` | Dependency-only SDK front door |
| `Vericairn.*.0.7.2.nupkg` and corresponding `.snupkg` where produced | Community libraries and portable symbols, published from these exact verified files after release publication |
| `SHA256SUMS` | Artifact SHA-256 values |

Hashes, sizes, observations and acceptance belong to the release record and its evidence. The structural documentation check verifies the set's completeness; release review and acceptance remain separate steps.
