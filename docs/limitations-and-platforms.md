# Limitations and platforms, Vericairn 0.8.2

The estate's release record, written when the candidate is observed and superseded when publication is observed, is the authority for its commit, workflow run, artifact hashes and publication. This set describes implementation and test boundaries. Platform and live-session qualifications require their own recorded observations; documentation and stand-in tests do not establish them.

0.8.2 follows published 0.8.0 and carries the work of 0.8.1, whose candidate was withdrawn before
publication, with the macOS and Linux single-file command lines able to start processes again (FIX-53)
and a retained basis's record digest rechecked when it is read (FIX-54). That work is the corrections
from 0.8.0's review (FIX-34 through FIX-38), the
policy and adoption corrections (FIX-39 through FIX-46 and FIX-48), test timing and the release's packaging
path in CI (FIX-29, FIX-32, FIX-49, CI-01 and CI-03), the release gate's retained basis by reference
(FIX-51), package registry observations (DIST-02), the package icon
(DIST-03), MCP Registry discovery (DIST-04) and exact-byte document pins (DOCS-07).
It reads record formats 1, 2 and 3 and writes the estate's own format, at most 3; an estate at format 3
upgrades from 0.8.0 without a format step. The format-3 upgrade from 0.7.3 preserves historical records
and requires explicit adoption afterwards. 0.8.0's features and earlier corrections remain. Earlier sets stay frozen.

The earlier custody, accounting and transport limits still hold. Tests and documentation do not replace
independent review or operator acceptance.

## Known issue in published 0.8.2

Each platform's 0.8.2 archive carries a separately compiled build of the same libraries: the build folder's
platform name is embedded in each assembly. Policy evaluation identifies its implementation by the compiled
assembly's identity, so the same estate's policy basis digest, and the implementation digests it covers,
differ by platform. Evaluation on one machine stays consistent; status and policy bases compared across
platforms differ only in those digests. It is not a security problem. The fix for 0.8.3 (FIX-61) builds
the libraries without platform-specific intermediate paths while keeping the applications' locked
per-platform restores. The shared packaging job reads the Vericairn library assemblies embedded in
all six platforms' CLI and Workbench bundles, including Workbench's copy of the CLI, and fails if
their library sets or assembly bytes differ. Libraries shared by the two applications must also match.
This holds the assembly names and module identities used by policy implementation digests equal
across platforms ([issue 184](https://github.com/Vericairn/vericairn/issues/184)). The fix changes no
policy digest formula or frozen release documentation; 0.8.2 remains the current published release.

## Machine-local worker configuration and qualification

FIX-11 supplies the stock CLI and daemon with native worker configuration. Before a native run, use `vericairn worker configure --launcher codex --model <model> --effort <effort>` (or `claude-code`); omit choices to show it. Both model and effort are required when setting. Repeated `--allow-model` and `--allow-effort` options narrow choices and must include their defaults. Without a model allowlist only the default is admitted; without an effort allowlist the launcher's supported efforts are admitted. Each set replaces that launcher's defaults and allowlists. Missing or malformed configuration refuses before intent, and an explicit run override cannot replace it or escape its restrictions.

Configuration is per machine and is never recorded in the estate. Owner-only `worker/codex.json` and `worker/claude-code.json` files live under the user's Vericairn home. Reads and writes are refused when that home lies inside a Git repository or estate. If the user's home directory is a repository, set `VERICAIRN_HOME` outside all repositories and estates for the CLI and daemon before starting them. The configuration file, worker directory and Vericairn home cannot be symbolic links or reparse points; resolved ancestor links cannot hide a repository or estate. The stock CLI and daemon read configuration for each run and agent goal step and recheck selection before intent and dispatch. Changing the choices needs no daemon restart. The local files are never copied into records; a run intent retains its selected portable choices, their provenance and configuration identity.

Configuration narrows and never widens the qualified inventory. It cannot add tools, servers, environment variables or routes. The stock environment policy derives only from inventory admitted by a shipped baseline or an exact current RUN-03 qualification. A host-supplied policy must still match that qualified inventory and may refuse more. `worker configure` runs no probes, launches no agent and has no HTTP or MCP write route.

Qualification is also machine-local. `POST /v1/harness-qualifications` refuses a paired local-network device with `401` / `denied` before probes or writes, even for an authorized human or a request with a local command override. The local socket and signed loopback retain the existing human qualification authority checks. Qualification records are portable only when every local probe digest matches; qualification grants no work authority or live-inference assurance.

FIX-12 supplies a separate workspace root: `vericairn worker configure --workspaces <absolute-directory>` sets it; `vericairn worker configure` shows it. This setting omits `--launcher` and applies to every launcher. The owner-only `worker/workspaces.json` contains the absolute `root`, per machine and never recorded. It lets a person put workspaces outside a home holding Claude Code's or Codex's own configuration. The root must be outside Git repositories (including bare repositories) and estates. Neither it nor its ancestors may contain `.claude`, `.codex`, `.agents` or `.mcp.json`. The root itself cannot be a symbolic link or reparse point; linked ancestors are resolved and the canonical root is stored. New roots are created owner-only. Invalid files, duplicate keys and unknown fields refuse without fallback.

The CLI and daemon reread the root for every run and recheck before intent and dispatch, without a restart. A changed root after intent yields an unstarted, zero-charge failure. New workspaces use `<root>/<estate>/<branch-slug>`, with branch slashes flattened to hyphens; the unconfigured default remains `<VericairnHome>/estates/<estate>/workspaces/<branch-slug>`. An existing independent workspace is reused only under the selected root. If outside it, preserve the work before removing that directory, or select a root containing it. Legacy linked worktrees require preservation and `git worktree remove` before retrying. Setting the root moves and deletes nothing.

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

The NuGet packages are framework-dependent: the `dotnet-vc` tool needs the .NET and ASP.NET Core 10 runtimes supplied by the .NET 10 SDK. It installs no Workbench and no macOS Secure Enclave helper. Use the macOS archive for the bundled helper. The worker's agent-side MCP command still resolves `vericairn`, so a NuGet-only worker installation is not qualified by package smoke tests. `Vericairn` is a dependency-only SDK front door; it does not bring an embedded runtime or daemon. The release workflow builds packages into the draft; NuGet Trusted Publishing happens only after the operator publishes that release. 0.8.2 package availability requires operator publication and a successful package workflow; the 0.8.0 and 0.8.2 packages are published. Name-reservation versions are not product releases.

Nuget.org receives exactly the package assets the operator publishes, verified against that release's own `SHA256SUMS`. The workflow checks completeness and hashes but does not compare with the estate's accepted candidate or require its acceptance record; a published prerelease also triggers it. Publishing the exact accepted draft supplies that binding. Changing both assets and their checksum file would pass this comparison. Upload never rebuilds packages. Duplicate versions are skipped, not replaced or independently verified against nuget.org's existing bytes.

FIX-13 checks action and reusable-workflow references in every workflow against the repository's
full-commit allow-list, including release-only workflows. It does not inspect the organization's live
Actions policy. The NuGet pull-request job builds packages and symbols and runs the publication verifier;
it has no login, identity-token permission or upload. Checksum verification detects missing, duplicate,
altered or mismatched package files, not a malicious producer that changes both assets and checksums.

## Package registry observations (0.8.2)

Vericairn 0.8.2 observes nuget.org publication, listing and deprecation through
`packages observe`. Each observation pins a product declaration and recorded release version,
retaining the exact public API URLs and SHA-256 digests of response bodies. A missing version is
`not-found`, with listing and deprecation unknown; it is never treated as unlisted. Registration and
package-content indexes must agree before a state is recorded. Index lag, incomplete metadata and
provider failures require another observation and establish no new registry state.

Unlisting and deprecation remain the account holder's acts on nuget.org. The commands record intent
before that act, print its exact step and observe the outcome separately against the original intent.
An identical rerun retains one intent. No command accepts, reads or stores a NuGet API key, performs
a registry write, or claims the identity or time of the account holder's actual act. A matching
deprecation includes its recorded reasons, message and alternate package/range. Later observations
can report relisting or changed metadata without erasing a historical observed success. A different
deprecation intent under an existing package/action/version identity is refused; revising or canceling
such intents is not provided by this command family.

The host registers the `nuget-org` provider behind `IPackageRegistryProvider`; the runtime chooses
only the declared provider. Its anonymous transport allows HTTPS requests only to `api.nuget.org`,
with no cookies, credentials, proxy, redirects or trace headers. Each package observation has a
twenty-second deadline, at most thirty-two requests, at most one MiB per response both before and
after gzip decompression, and eight MiB total including decompressed bytes. Response hashes cover
the received body bytes before decoding, with content encoding retained beside them.
The whole command's provider reads have a two-minute deadline. Oversized responses, unsupported
URLs, malformed metadata and incomplete page inventories refuse rather than truncate or infer state.
The provider supports three-component release versions with optional prerelease identifiers and
the semver2 registration resource; it does not download package archives or prove their equality
with an accepted release. Very large registry inventories may exceed these bounds.

No background watcher or release-workflow callback is installed. After each publication, the
operator must run the observation command; changes made outside this procedure do not acquire
records automatically. These commands require record format 3 and a build that understands the
package declaration and observation fields. Older builds sharing format 3 may not read those fields.
Observations and outcome revisions are validated together before storage; storage is journaled and
sequential, so an interrupted write can leave an incomplete batch requiring reconciliation. Intent
identity is serialized within one runtime writer, not across independent writers or unsynchronized
estate clones. Tests replay small retained API responses and transport failures, and make no live
nuget.org availability or account-holder qualification claim.

## Goals, routing and asks

0.8.2 retains goal proposal and lifecycle, record predicates, independent evaluations, acceptance, roster routing, asks and one-step dispatch, and retains record-derived hand-over and resume. Record format 3 arrived in 0.8.0; 0.5.2 was the first published format-2 release. The upgrade preserves original bytes and supplies a refusal boundary for older readers. The progress, communication, execution and harness-qualification additions shipped in 0.7.1 remain; the scope-read and attempt-missing signal values need readers that understand 0.7.2. A shared format number does not establish that an older writer understands them.

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

RUN-03 qualification is restricted to a human operator or a human explicitly granted `qualify`; agents and services cannot qualify, and `administer` or `local-engineering` alone is insufficient. The record pins six probe digests, not a vendor signature or a live-inference observation. Admission checks the version, declared flags, tools, configuration, login boundary and MCP facts locally before intent and again before dispatch. A non-baseline qualification must still be current and match all probes, and the independently admitted RUN-01 inventory must match. Revoking a record does not remove a compiled baseline. Protocol tests use stand-in processes and captured fixtures. The seven live Codex runs below provide separate, bounded evidence; neither kind establishes provider billing, arbitrary future event formats or vendor authenticity.

Every run requires explicit model, effort, provider, destination and data boundary. The local launcher configuration, roster, goal, assignment and run choices have recorded precedence; restrictions accumulate. Missing required choices or unknown exact revisions refuse rather than using ambient defaults. The environment inventory must cover tools, skills, plugins, hooks, MCP servers, helpers, dependencies and effective configuration, with matching admitted identities and inherited environment limits. Requested execution is separate from observed execution; an absent actual fact remains unknown. The efficiency Adapter is absent until EFF-01 in 0.8.3, and any request to enable it is refused.

The custody guard refuses unattended launch while a locally held key of an operator or the sponsor signs without presence, unless the accountable person recorded a current, expiring exception for the goal revision. Revoked and unadmitted keys count too, because possession still permits signatures. This guard does not isolate the harness from the operating-system user's files or credentials. Use a separate user when that isolation is needed.

Codex receives a fresh `CODEX_HOME` with only the approved harness login imported; effective MCP configuration is probed and extra servers or incompatible managed policy refuse launch. Claude Code receives a fresh `CLAUDE_CONFIG_DIR`, restricted settings, hooks/plugins disabled and strict MCP configuration; detected managed policy refuses launch. Remote or undetected managed policy and managed Claude accounts are unsupported. These controls do not stop deliberate same-user file or keychain access.

The worker reserves a run's whole allowance before launch. Unknown or incomplete token/currency usage is charged at the reserved cap; a lost observer or uncontained run remains in flight and reserved. Tokens and currency leave ten percent headroom, and wall time leaves a grace period and heartbeat. These are soft operational thresholds: a single turn, usage in flight or a failed shutdown can exceed them and make the remaining budget negative. Claude's USD budget flag and Codex's rollout token tracking do not turn every unit into a hard spending cap. Generic is not an admitted alternative. An uncertain prior run blocks retry for that assignment until its original operation identity is reconciled.

FIX-08 makes previously recorded settlement facts immutable, including usage, scope and containment. Later revisions may append evidence and add previously missing facts only under the record and chronology rules; they cannot change status or replace recorded facts. The ledger charges from the first settlement's usage, so adding a revision cannot refund it, including allowance charged at the cap because usage was unreported.

Stdout silence, not stderr or a harness assertion of progress, drives stall handling. The default limit is 600 seconds; precedence is goal `limits.stallSeconds`, roster limit, launcher request setting, then default. At one limit it records attention; at twice the limit it requests a stop and attempts containment. Root exit or closed output alone never establishes that the whole launch boundary exited.

WATCH-01 observes emitted command/tool events, loops and reported token rates; it does not interpret every program or prove absence of unreported effects. An invocation can already have started before its event is observed. Raw inputs stay local; records retain fixed summaries and hashes. Incomplete checks remain visible, and FIX-10 preserves independent verdicts when another check fails. Critical credential, confidently classified outside write, inventory, execution, loop and rate signals use the stop and containment path; warnings and the final scope audit create attention. Failed attention writes do not block critical stops and remain explicitly pending. Default rate observations need 60 seconds and 10,000 reported tokens; unknown usage cannot establish a rate. The full settings and bounds are in `software.md`. Agent progress claims never reset counters or establish liveness, scope or acceptance.

## Live Codex qualification (FIX-18 and FIX-19)

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

Seven builder live runs with codex-cli `0.159.0-alpha.12.1` exercised the selected model at `max`:

| Run and code commit | Observed outcome and its limit |
|---|---|
| First, `6bab07f` | The session reported model and max effort; 1 wall minute and 34,573 tokens were recorded. Every tool call failed with the code-mode host disabled. No attempt or result was recorded. |
| Second, `80423b5` | The first shell command ran, rollout model/effort matched admission, and settlement raised missing-attempt attention. The old audit stopped an outside-workspace read. |
| Third, `42f989d` | The workspace search and brief read ran. The old audit mistook a Python heredoc division operator for an outside write target and stopped the run. |
| Fourth, `8e272de` | The corrected audit warned for opaque Python input without stopping it. The run settled observed-success, with 2 wall minutes and 95,406 tokens; MCP approval under exec's never policy prevented an attempt/result, and no file changed. Missing-attempt attention appeared. |
| Fifth, `531d169` | The cap was recorded before launch, the admitted model and max effort were observed, and usage was 3 wall minutes and 205,767 tokens. Estate-only pre-approval allowed a signed attempt and result. Exactly the permitted `NOTES.md` changed (40 bytes). The commit failed at the sandbox boundary. |
| Sixth, `eb12a8e` | The cap, model at max, usage and signed attempt/result were recorded, and exactly `NOTES.md` changed. The independent repository alone was insufficient: Codex still protected its `.git`, so the commit failed. |
| Seventh, `36c90ba` | On macOS, Codex committed exactly the permitted `NOTES.md` with only its own `.git` added as a writable root. Settlement audited that commit and fetched its branch into the registered clone, whose `.git` remained unwritable by the agent. |

These runs qualify the reported live Codex path: inference at max, ordinary tools, rollout observation,
missing-attempt attention, brief access, the corrected audit and estate-only MCP attempt/result authoring,
and the seventh run's sandboxed commit and audited branch transfer on macOS only.
They do not qualify arbitrary commands, other versions, models, accounts or platforms, Claude Code live
sign-in, provider billing, hard spending caps, compaction/recovery, remote managed policy or acceptance of
an agent's work. Stand-in protocol coverage and the release's three-platform CI remain separate evidence.

FIX-19, retained from 0.7.3, gives each workspace its own repository. Its index, refs, logs and new objects stay
inside its own `.git`; it borrows the registered clone's objects read-only without importing remotes,
configuration or hook templates. Codex adds exactly that workspace-owned `.git` to its writable roots,
never the registered clone's Git administration. Linked or shared Git administration is refused.
The seventh live Codex run committed successfully on macOS; this live qualification is macOS-only.

After recording settlement, the worker fetches the exact audited run-branch head through a disposable
bare view of its objects. Worker Git disables hooks, ambient configuration, non-file transports and
automatic maintenance; it does not load agent-controlled repository configuration. The clone's branch
updates only if its old head still matches preparation. A concurrent move, deletion or creation,
a checked-out destination, a changed workspace head or incomplete scope audit refuses transfer.
Outside-scope commits are retained with their scope warning for review. A transfer refusal leaves the
workspace intact and adds a local `audit-unverified` warning to worker observations, without rewriting
the settlement. The clone's working tree and index stay unchanged; no push or acceptance occurs.

Uncommitted changes remain in the workspace. Reuse requires the selected root and a consistent branch;
changing the root does not relocate or delete a workspace. Preserve committed and uncommitted work before
removing an independent workspace directory. For a legacy linked worktree, preserve it and use
`git worktree remove` from the registered clone before retrying; there is no silent conversion.
Borrowed objects depend on the registered clone: preserve work before removing it or pruning objects
needed by the workspace. The scope audit does not inventory Git administration, prove attribution or
recover unreachable commits or reverted uncommitted changes. See [the software guide](software.md#coding-agents-the-worker-and-its-launchers).

## Command-audit classification (FIX-18 and FIX-20)

FIX-18 distinguishes reads, known write targets and unclassified effects. Reads inside the workspace
need no change permission; ordinary reads outside it warn with `scope-read`. Changes inside the workspace
are checked against the brief at settlement. Critical `scope` requires a confidently identified write
target outside the workspace and the temporary directory supplied at launch. The audit grants no ambient
temporary boundary or one claimed in a tool event.

FIX-20, retained from 0.7.3, recognizes reads, copies and listings of the Vericairn home's `keys/`, `connections/`,
per-clone runtime certificate and identity files, and listings of the runtime directory. It also recognizes
`gh auth token`, `gh auth status --show-token`/`-t`, `git credential fill` and Git credential-helper `get`.
These produce critical `credentials` signals and request containment. `gh auth status` without token
output retains its network warning; `git credential reject` is not a credential-output command.
The audit resolves simple captured environment substitutions, assignments and known directory changes;
it reads no credential content. An unresolved target remains uncertain unless independently recognized.

Recognized outside writes now include in-place `sed`/`perl`, `rsync`/`install` destinations, archive
extraction and output targets, `patch` targets, `dd of=`, Git init/clone/worktree destinations and literal
interpreter writes. Known wrappers do not hide the inner command. Interpreter code still warns with
`audit-unverified` alongside any independently recognized critical signal. Calls in common quoted samples
or comments are not write invocations. Output to `/dev/null`, or `NUL` and `$null` on Windows, is exempt
from outside-write classification; other devices retain their treatment. Input redirects, heredocs and
here-strings are input, with interpreter bodies inspected as code.

Warning-only cases remain: ordinary outside reads, recognizable network attempts, unresolved expansions
or command substitutions, unclassified outside-looking operands, unknown directories, uncertain physical
containment, remote `rsync` operands, opaque interpreter input (including `osascript`) and `xargs` inputs
not visible in the invocation. An independently recognized credential target or literal outside write
still produces its critical signal. Git message text and Git object selectors are not filesystem targets.
The audit does not interpret archive contents, patch bodies, arbitrary programs, computed filenames,
aliases, every option or every shell expansion. It cannot prove that an unreported effect did not occur.
Events may arrive after execution: this is a signal, not an enforcement boundary. Operating-system
isolation, credential separation and the settlement scope audit remain necessary. Stand-in tests report
commands without executing them; they establish no new live-harness or platform qualification.
[Command observations](software.md#command-observations-070) gives the complete recognized forms and limits.

### Command audit and settlement in 0.8.0

FIX-24 recognizes whole-home recursive copy, archive and sync sources, credential-helper actions
after their options, and additional outside write forms. Home-root listings, including recursive
listings, and per-clone `live.db` reads remain warning-only unless an independently recognized
credential target is present. Recognized credential access and outside writes request containment;
ordinary outside reads, network attempts and unverified effects remain warnings.

FIX-26 permits location-only home reads and matches the harness's own tools only to native identities
in its admitted inventory. This behavior belongs to the running worker: a worker still at 0.7.3
keeps its older audit and tool inventory even when it builds 0.8.0 source. Ignored build output and
untracked worker brief exports are excluded from settlement's file inventory; tracked files and
other untracked changes still count. Committed history remains separately audited. A complete
committed audit can retain the branch for review when the file inventory is incomplete. Timed-out
checks retry once; a credential check that still cannot finish requests containment.

FIX-25 removes a newly prepared, unused workspace after pre-dispatch refusal only when its complete
inventory still matches preparation. Reused, changed, concurrently used or incompletely inspected
workspaces are preserved with a warning. Windows opens use extended paths for the longer staging
name. This is guarded cleanup, not permission to delete work that cannot be proved unused.

FIX-28 publishes complete run-state and stop-request files before readers see them, coordinates
Windows readers and replacement writes, and keeps supervision alive through observation failures.
Unreadable observations remain unavailable; they never become proof of exit or absence of a stop.
A published stop request remains recorded even if its confirmation cannot be read.
The SDK also checks elapsed time when enforcing client deadlines. These corrections do not establish
new live-harness or native-platform qualification. The audit observes reported invocations, may run
after an effect, and cannot enforce credential isolation. See [software](software.md) and
[client operations](client-operations.md).

### Command audit and settlement in 0.8.2

FIX-34 resolves partly quoted paths as a POSIX shell does, inspects the inner commands of more shells
and of `eval`, and warns when a program or shell cannot be inspected. These still give no signal:
commands a script file runs, earlier shell functions and aliases, programs reached through a changed
`PATH`, and unclassified programs whose operands look like workspace paths. FIX-35 applies only the run's
starting ignore rules at settlement; an ignore file the run changed counts as a change outside the brief,
and a nested repository anywhere marks the audit incomplete. Both belong to the running worker.

## Worker containment and the workspace audit

| Platform or condition | Limit |
|---|---|
| Linux and macOS | The boundary is a POSIX process group. A process can escape by leaving the group or session; there is no independent guardian after a worker crash. |
| Linux process inventory | Unavailable or filtered procfs can prevent proving containment. A failed inventory leaves the reservation in flight and is retried. |
| macOS process inventory | An inventory error also leaves containment unknown and the reservation in flight. |
| POSIX embedding | PID 1, ignored `SIGCHLD` or `SA_NOCLDWAIT` are refused; an embedding host with a separate child reaper is unsupported. The worker retains its child identity until final inventory and signaling. |
| Windows | A Job Object prevents breakaway after assignment and kills members when its last handle closes. A crash between suspended process creation and job assignment can leave a process behind. There is no graceful signal for a windowless process: a harness that does not watch the stop file needs escalation. |
| Stop observation | Request, transmission, acknowledgment, escalation and containment are separate facts. The command can return after its bounded wait before containment; inspect `worker runs`. Failed signals or queries are not evidence of exit. |

The scope audit compares physical workspace inventories before launch and after containment, excluding ignored build output and untracked worker brief exports, while retaining tracked and other untracked files. Since 0.6.1 it also compares the run branch's head and every commit reachable from it but not from the prepared baseline against that baseline, including merged side branches. Restoring a committed change in the worktree or a later commit, or checking out a different head, does not hide its paths. The audit pins the original Git object/reference directory and reads through isolated metadata without agent-writable configuration, replacement refs, grafts, attributes or external diff drivers. Missing history or an unreadable reference makes it incomplete.

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
- Local-machine actions are refused through this listener: worker launch and goal step, harness qualification, release-provider operations, local device creation/rotation/recovery, issuing pairing codes, runtime certificate changes, exporting files, object uploads and applying upgrades. Reads, permitted record operations and authorized worker stops are available; `api-v1.md` lists the exact route restrictions.
- Replacing the TLS certificate preserves the identity pin; `runtime rotate --compromised` closes open connections and handshakes. Replacing the identity requires every client to pair again. Client transport keys are temporary, but a crash can leave temporary key material in macOS or Windows stores. Unix file modes are owner-only; Windows configuration and connection files inherit directory ACLs.
- Saved connections are consumed by the SDK. Workbench and ordinary estate CLI commands have no saved-connection selector.

### Concurrent certificate use (FIX-21)

FIX-21, retained from 0.7.3, corrects the concurrent certificate first-use race present since 0.6.1. Loaded and
rotated certificates have their typed extension caches initialized before TLS listeners receive them,
including Server Authentication usage. When `DaemonOptions.Log` is configured, failed handshakes report
exception type and message, without stacks, inner exceptions, peer certificates or request data.
A daemon started through `runtime start` retains these diagnostics in its local log. Trust, authentication,
network and resource-limit failures can still reject a handshake. This changes no pin, permission,
protocol, rotation or replay rule. See [API transports](api-v1.md#transports).

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

Upgrading moves no key. Format-1 estates cannot name Secure Enclave custody or presence until their move to format 2. Admissions and revocations at format 2 or 3 require signatures or an anchored unsigned admission from the signed migration. The migration lists all unsigned admissions, including superseded ones and revocations, without rewriting any record. A signature policy still uses the date stated by the record, and record removal is visible in repository history or `recover compare`, not necessarily in `estate check`. A potentially leaked key is revoked, not merely renamed or rotated. Recovery keys need separate offline custody; exports carry no private keys.

FIX-13 orders generated identifiers within one process, including concurrent calls and repeated or
backward clock readings. Independent processes, machines and explicitly supplied ids have no such order.
An exact hash-pinned result acceptance establishes causality; other judgments use recorded time and id
tie-breaking. This changes no historical record bytes and is not a distributed clock.

FIX-12's credential-pattern exemption is limited to validated device `body.publicKey` and envelope `signatures[].signature` values in record JSON. A key must decode as a P-256 SubjectPublicKeyInfo key; a signature must decode to 64 bytes. This validates the encoding for the scan, not the signature's cryptographic truth. Other fields, nested lookalike names, invalid encodings and non-record files remain scanned. The machine-path scan reads the original text and signature verification still applies; no record bytes change.

## Workbench, MCP and living documents

Workbench's Goals workspace, asks and progress are read views. It has no goal activation/acceptance, worker launch/stop or pairing form. Headless journeys cover the work loop, releases, Documents, Goals and progress. They do not establish native desktop, screen-reader or interactive-presence qualification; consult the estate's qualification records for the observed cells. There is no claim here of a new native qualification for 0.8.2. Projects are not a record type. Record comparison is a line difference of canonical JSON, and docking is panel placement with keyboard alternatives. The probe has no direct open option for every workspace and can run before a newly started daemon has supplied a page; start the daemon first.

FIX-13 reduces Workbench's first-screen work by sharing status and progress observations, avoiding a
duplicate refresh after daemon startup and reusing parsing and hashes for unchanged original bytes.
First-screen API reads no longer grow with unshown record history, but opening still validates every
record once. Large estates can still take time. Wait for loading before treating an absent page as empty;
headless tests establish no fixed native startup-time bound or desktop responsiveness qualification.

MCP supports protocol revisions 2025-06-18, 2025-03-26 and 2024-11-05 over stdio, with `initialize`, `ping`, `tools/list` and `tools/call`. Resources, prompts, sampling and HTTP transport are not implemented. The contributor writes are `work_attempt`, `work_progress`, `work_result`, `work_amend` and `document_review`. From 0.8.0 it also exposes bounded policy and writing-exception writes after authority checks, and `work_review` only to an admitted operator connection. The runtime checks current authority and exact bounds again at each call. Communication and context tools read; they do not change preferences or grant authority. Goal and ask tools do not activate, fund, accept, step or answer. There is no harness-qualification or worker-launch MCP tool. The orientation's observed MCP harness matrix is distinct from RUN-03's native worker qualification.

Workbench adds local communication and hand-over/resume views. It has no context-allocation editor, pinned-original retrieval control or harness-qualification form. The communication qualification report covers headless fixture behavior, not native desktop, screen-reader, live harness, Remote or compaction qualification. Optional progress frequency changes presentation, while required attention remains visible; an external agent's compliance with the brief's wording and refresh instruction is not enforced.

Living-document checks depend on local covered files, registered external clones and installed contributions. Missing contributions can make a document unavailable and hold its product's release. The software pack checks exact retained references and companions and evaluates reference coverage. It has no semantic notices/README checks or automatic declaration proposals. The existing retirement path can retire proved generated ownership; it refuses unknown or unexpected members. Workbench does not show a document's content beside its triggers. The normal progress report includes repository checks; its generated region excludes them to avoid reading itself.

FIX-23 selects each living document's own repository commit. Documents in the release repository
use the explicitly prepared release commit at submission and the actual build commit at observation.
For another registered repository, the gate uses its HEAD, or the latest clearing review's commit
when Git proves that HEAD is its ancestor. Covered paths and cleared bytes use the same selection.
Changed bytes, covered paths, absent reviews and unreadable registered clones refuse the applicable
gate. A refuse-level document whose repository has no clone registered here also refuses, naming the repository and
`vericairn sources clone <path> --resource <id>`; only the operator's signed, expiring grant of scope
`release-document-uncompared:<declaration>@<version>` to the submitting principal excepts it, and the release evidence
names that grant. A warn-level document without a registered clone is reported as not compared. A refuse-level policy
with unavailable required source bytes still holds the release. No gate fetches or checks out sources.
Normal `docs living` uses clone HEAD, and estate files use the estate's working tree. Current estate
records supply triggers and reviews; they are not a historical estate snapshot at the release commit.

POLICY-03 combines built-in objections, living-document gates and applicable policies before a build
can start. Format 3 requires `--commit <exact-commit>`, `--expected <current-product-hash>` and an
admitted held signing device. A refusal reports all known objections and starts no provider operation.
Sources, authority, exceptions and deadlines are rechecked after preparation and before dispatch.
The retained basis is inspectable with `release basis <evidence-hash>`. From 0.8.2 (FIX-51) it names records, sources
and objects by hash instead of copying them, so it no longer outgrows the estate's 8 MiB object limit: 0.8.0's
copied basis did, which refused every submission from an estate with more than a few megabytes of records and
documents. Reading a basis needs the estate's records and objects; one whose reference no longer resolves is
unavailable, as is one whose records, signatures included, differ from those its stored record digest covers
(FIX-54, 0.8.2), and a single source file over the object limit is named but not kept. A preview grants no submission
authority. An effect already dispatched remains under its original identity if later observation fails;
a client cancellation does not roll it back. See [estate policies](policies.md).

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

The repository adapter and release provider share one bounded runner: 30 seconds per adapter Git
command, two minutes per provider gh command, and
4,194,304 characters per stdout or stderr stream. The deadline includes launch, concurrent output reads
and exit. Overflow fails instead of accepting partial output. Timeout or cancellation terminates the
owned POSIX process group or Windows Job Object, including descendants holding output pipes. Cleanup
waits at most one additional second; unconfirmed exit retains native ownership for later cleanup.
On POSIX the runtime must run under an init process, not as PID 1, without ignored `SIGCHLD`,
`SA_NOCLDWAIT` or a separate child reaper. Unsupported hosts refuse before launch. Process-group escape
and the existing containment limits still apply.

## Release-check limits (UPDATE-01)

UPDATE-01 reaches only GitHub's public latest-release endpoint, directly and without system proxies.
The request is unauthenticated, with constant `User-Agent: Vericairn`; it sends no installed version,
user, machine or estate identifier, estate data or usage telemetry. Cookies, credentials, redirects and
trace propagation are disabled. Drafts, prereleases and malformed responses do not replace the cache.
The timeout is two seconds, with a bounded response; failure is silent and preserves cached knowledge.
A network requiring a proxy may therefore have no fresh answer.

CLI `version`, `doctor`, `status` and Workbench share at most one attempted check per rolling 24 hours
under one Vericairn home, including failures. Separate homes have separate limits. Workbench checks on
opening and hourly while open; the saved attempt time and lock enforce the daily request limit.
A future attempt timestamp also suppresses checks until enough time has elapsed. If settings are invalid
or unreadable, state cannot be saved or the lock is unavailable, the client makes no request.
The daemon, MCP and ordinary SDK estate calls do not initiate checks.

`vericairn updates configure --enabled false` disables requests, retaining cached information that can
still produce a notice or `manifest.release-behind` warning. Both clients reread the setting before a
check; Workbench's next hourly check or reopening reflects it. CLI disclosure and notices use stderr,
leaving stdout and JSON intact. Workbench shows them even before opening an estate. The warning reads
matching published release records and, for `Vericairn/vericairn`, the machine-local cache, with no
network request or error exit. It is outside canonical status. No check installs software, grants
authority, authors a record or moves the estate's pin; use the explicit upgrade routes in [installation](install.md#release-check-and-upgrades).

## Attention resolution limits (FIX-22)

FIX-22 derives required attention from obligations still open in the available records. Every depth
and frequency, including `none`, includes compact items with complete source and subject references,
current state, recorded freshness, reason and an inspection command. Original records and evidence
remain complete behind those references; the timestamp is evidence freshness, not live provider health.

A later passing verification resolves only the same exact subject, method, environment and command.
A later result under the same exact assignment, acceptance of the exact result, or acceptance of its
work order's current revision resolves result attention. A later review of the same exact subject,
acceptance or supersession of that subject, its typed withdrawal, or acceptance of its current work
order resolves review attention. Retiring a work order does not withdraw the current result a review
judges. Operations resolve through their own settled revisions. Ordinary `after` planning stays in the
held list; open `blockedBy` relations and paused, retired, withdrawn or failed predecessors require attention.

These rules do not infer disposition from prose, an unrelated acceptance, a changed implementation or a
release. A changed verification environment does not match an old check. Missing records, mismatched
pins, unavailable sources, malformed projections or incomplete resolution chains keep the affected item
unknown. Quiet settings, acknowledgement or dismissal resolve nothing. Open asks retain their queue;
worker signals can remain continuity concerns after the operation settles. There is no new dismissal,
acknowledgement or expiry state. [Required attention](required-attention.md) states the exact rules.

Canonical status JSON derives attention from records alone and is identical on machines with the same
records. Progress also reads this runtime's worker observations; human status, progress and Workbench
label additional concerns `Local worker observations`. No reader can infer another machine's unrecorded
facts. The estate observation's existing status-hash contract is unchanged; these API v1 projections add
no record fields, CLI commands or permissions.

## Estates other than Vericairn's own (0.8.2)

Only the estate that built and observed a release holds its release record
([issue 177](https://github.com/Vericairn/vericairn/issues/177)). An estate other than Vericairn's own therefore
cannot yet pin or upgrade to a published Vericairn release: `estate upgrade --to` refuses there, and its source
pin does not move. Until a later release adds a supported record of a published Vericairn release that an
estate depends on, initialize such an estate with `estate init --commit` set to the published release's tag
commit, run the checksum-verified published binary with `--estate`, and never write a release record by hand.

## Format 3, policies and adoption

0.8.0 added format 3; a format-2 runtime refuses its manifest before opening a
writable estate. The upgrade appends its migration and preserves original records.
It creates no policy, grant, exception, placement or duty completion, and cannot
be used to downgrade the format. Coordinate the upgrade across writers; an old
clone that has not received the new manifest still has its old view.

Policy reads use the greatest valid recorded timestamp unless an explicit preview
is selected. They do not advance an idle estate to today's date. A preview cannot
authorize release submission; live admission samples fresh time after acquiring
sources and checks again before dispatch. Missing or unsupported contributions,
source bytes and history remain unavailable. At refuse level they hold applicable
releases; at warn level they stay visible without becoming a pass. An exception
can defer exact unmet findings only within its approved scope and exclusive expiry.
It cannot repair unavailable evidence or override built-in or document gates.

Recurring duties preserve every due occurrence, grace period and missed deadline.
Completion needs exact result bindings, declared passing checks, native proofs and
the required review. Late completion retains lateness; ending unmet debt is not
completion. The evaluator is bounded: 100,000 records per kind, 10,000 duty findings
including the next scheduled occurrence, and 1,000,000 proof-replay steps per rule.
Exhaustion is unavailable, never a partial successful inventory. Reads neither
schedule work nor authorize spending or external effects.

POLICY-04's first-party operating guide and proposals are product deliverables.
Adoption still needs the operator's exact signed definitions, the builder's bounded
writing grant, actual release/publication identities, faithful obligation-gate
interpretations and review capacity. The twelve policies and seven initial duties
are not automatically declared by the upgrade. Seven further menu duties remain
undeclared. The release-devlog cohort must include the exact first 0.8.0 publication
when it already exists at adoption. The four writing-content objects lack their
future grant basis. A policy's independence check does not select the required
security reviewer by name. [The proposals](policy-adoption-proposals.md) retain
these unresolved inputs and the operator work; no live adoption is established here.

## Policy implementation identity: correction for 0.8.2

Since FIX-31, policy kind implementation digests identify a compilation through
the assembly's simple name and deterministic module version identifier. Together
with the registered kind and version, this binds the compiled kind, version and
module identity admitted by application composition. It is not a hash of installed
bytes and does not detect a modified installation. [Policy evaluator](policy-evaluator.md)
specifies the digest calculation.

The documentation correction for 0.8.2 brings the maintained policy guides and
registry comment into agreement with that implementation; it changes no behavior.
0.8.0's frozen policy guides retain the earlier description, and the frozen set
remains unchanged. The compilation identity described here is the correct account
of the behavior introduced by FIX-31.

## Readable companions and build provenance

The generators cover admitted record items and collections, six revision registers,
the estate manifest, allowlisted local metadata and eleven software reader kinds.
Coverage describes implemented contracts; a missing implementation, stale snapshot,
unsupported audience or incomplete selection remains unavailable. Only the authorized
Internal projection is supported through the public companion read operations.
Members and Public projections, mobile and public-site caches are outside 0.8.2.
Local inspectors cannot be invoked through companion API or MCP routes. Metadata
reads grant neither source access nor authority to act on what they render.

Strict whole generated members require complete source and byte comparison, with
no output pins or authored clearances. Existing authored regions retain their pins
and review requirements. Legacy whole-file conversion requires an exact independently
reviewed preview; an editorial revision or format upgrade cannot convert it.
Unexpected members are never silently pruned. A rendering failure stores nothing;
an interrupted publication after storing records retains guarded journal recovery
and reports the already-stored records honestly. Changed destinations or unavailable
sources can hold recovery and further writes. There is no cross-process writer lease.

Retained reference inputs and generated bytes are compared against the exact local
build and candidate commits. A missing retained commit, including in a shallow clone,
is unavailable; generation and comparison fetch nothing. A documentation-only commit
can keep its build pin only while the complete build sources agree. Reference indexes
use only an explicitly selected register. No register selection means no mapping claim.
Software implementation digests bind source bytes; first-party record readers use
explicit versioned implementation identities and golden-byte fixtures.

The serialized build's companion archive is retained by CI and covered by its final
checksums. The draft-release attachment list does not establish it as a permanent
release asset. SBOM, checksum and workflow reports explain retained inputs; they
prove neither vulnerability absence nor rights, platform qualification, signatures,
release acceptance or publication authority. See [retained companions](software.md#retained-build-companions).

## Client and SDK compatibility

Async cancellation and deadlines stop client observation, not necessarily the remote
operation. Embedded synchronous work can continue after cancellation. A write with a
lost, truncated or unusable response can have an unknown outcome and must be reconciled
under its original identity before retrying. No client automatically retries it.
Definite refusals remain definite; cancellation requested before invocation dispatches
nothing. Synchronous SDK methods preserve their earlier timeouts and exception behavior.

Workbench marks interrupted retained views as cached and retains uncertain-effect
notices. Companion access loss or an unavailable refresh clears protected content.
Headless behavior does not establish native screen-reader, keyboard or desktop
qualification for 0.8.2. Policies have CLI/API/SDK/MCP inspection; a separate Workbench
policy editor or inspector is not supplied by this release.

Shared public array fields become `ImmutableArray` values, and the runtime's fake
release provider moves to internal test support without a type forwarder. Consumers
of those exports must account for source and binary compatibility changes. Option
collections are copied, but record equality keeps collection equality semantics;
it is not element-value equality. Opaque dictionary views lose an undisclosed
comparer and use ordinal lookup. These changes are listed in
[API compatibility](api-compatibility-0.8.0.md); they do not change record bytes or
API v1 wire contracts. 0.8.2 adds four members to `IEstateApi` and changes three members of
`IRecordCommitAdapter`, so implementations outside Vericairn must change; callers of the
commit adapter change too. [API compatibility for 0.8.2](api-compatibility-0.8.2.md) lists them.

## Checked changes and operator actions

Checked pushes need an installed Gitleaks scanner, 8.25.0 or later, and complete local
history of the exact range. Aggregate scan success is no claim about every possible secret or an
unobserved future commit. From 0.8.2 the scan applies gitleaks' rules without their built-in
allowlists, so it can report a finding that gitleaks' defaults pass. A finding refuses the push,
and with it the merge; there is no per-estate allowance, so a change refused for a value that is
not a secret is pushed and merged outside the checked flow. Merges require exact declared check names and every reported
check successful at the expected head; forks and required merge queues are unsupported.
A confirmed merge with pending or failed target checks remains incomplete. Git and gh
use the runtime user's existing configuration and login. No permission, credential
or publication authority is inferred from a check result.

`operator record` requires repository-synced format 3, the operator's own admitted
device and a matching changes declaration. It commits exactly the signed record
and its required generated register members, verifying their paths and bytes;
operation receipts remain separate, uncommitted estate records. Resume keeps the
original input, stored signed record, generated members and operation identity
without signing again. Known failures need reconciliation;
uncertain effects need observation before retrying. Local checkpoints belong to that
clone, and independently running services are not coordinated by one service's gate.
The queue and one-step action are absent from MCP. See [operator actions](operator-actions.md)
and [checked changes](changes.md).

## Other limitations and artifacts

The generated CLI reference includes every visible option at its declaring command, including the recursive root options and `worker configure`, without trailing whitespace. The structural docs check and build comparison verify document presence, declared command, operation, type and format inventories, retained inputs and complete reference bytes; they do not judge prose or prove behavior. Source regression tests check visible-option coverage and generated whitespace.

The GitHub Releases provider runs through the runtime user's `gh` login. It identifies a dispatched run by listing recent runs and can report uncertainty when none appears within thirty seconds. It is the only shipped release provider. Verifications preserve a command's exit, duration and output; they do not parse test reports. Third-party notices and license texts remain hand-maintained beside the generated SBOM and checked against lock files. The 0.6.0 review found its SBOM named the component 0.0.0, included packages absent from archives and omitted the embedded .NET and ASP.NET Core runtimes. From 0.8.2 (CI-03) the SBOM is generated from the self-contained distributables' locked platform restores and checked to name every package they ship; it still lists no embedded .NET or ASP.NET Core runtime, and this set establishes no complete inventory from it. Use the notices for bundled components and the release review's artifact observations. Live stores for clones no longer present are not listed or removed by a command.

The notes command renders the estate's records with `--from 0.8.0 --to 0.8.2`.
Its exclusive start follows 0.8.0's recorded end; `docs notes` writes the exact window to the manifest.
FIX-13 gives accepted work one entry per work order, naming the latest accepted current result in the
window by recorded time and then identifier, with its acceptance. Earlier results and attempts remain
available through `work observe`; later unaccepted results remain separate. Superseded results are omitted.
Classification uses current estate state, not a snapshot at the window's end; work-order acceptance does
not accept each result. Later imports with older timestamps can require another explicit widening.
Earlier sets and their checksums remain unchanged.

## Context and hand-over limits

Context packages measure exact UTF-8 payload bytes including accounting overhead. Tokens, harness framing and model fit remain unknown without a qualified local tokenizer; this build includes none and makes no network tokenization call. A context byte allocation is not a spending cap. Mandatory material must fit; optional selected sources have a complete withheld list. The remainder of the document catalogue is outside the selection, so an empty withheld list is not a whole-estate read claim. A human `work brief` keeps its full selected scope with no byte limit set; preparation and MCP briefs without an allocation withhold named optional bodies.

Pinned record retrieval verifies immutable originals. Required document originals use matching
working-tree bytes first, then FIX-27 searches the exact path in the estate repository's local Git
history for the pinned SHA-256 bytes. Their retrieval pins carry `history: local`; optional documents
and pins without that marker remain working-tree-only. Missing or shallow history and mismatched
bytes refuse with recovery guidance. Nothing fetches, checks out, substitutes a newer revision or
writes the estate. This changes original retrieval, not the context allocation or token accounting.

Hand-over retains all records in the copy, including superseded history, with the full estate pin and recorded observations. Source-file bodies, output objects and unrecorded external state are outside that scope. A byte cap refuses overflow rather than truncating. Resume compares identities, not timestamps, but cannot establish that an external provider or the world stayed unchanged. It restarts no run and grants no authority. Interim progress claims remain local; only completed phase records enter the portable history. Saved packages and canonical JSON are unchanged by communication depth. Harness compaction/recovery, the efficiency Adapter and Remote remain unbuilt or unqualified as stated above.

The workflow is configured to produce:

| Artifact | Contents |
|---|---|
| `vericairn-0.8.2-<platform>.tar.gz` (`.zip` on Windows), six platforms | Self-contained CLI, LICENSE, NOTICE, notices and `licenses/`; macOS includes `bin/vericairn-enclave` |
| `vericairn-workbench-0.8.2-<platform>.tar.gz` (`.zip` on Windows), six platforms | Workbench with the CLI, macOS helper where applicable, and license material |
| `vericairn-0.8.2-sbom.cdx.json` | CycloneDX bill of materials of the packages and of every platform graph of the self-contained distributables |
| `vericairn-0.8.2-THIRD-PARTY-NOTICES.md` | Third-party notices |
| `vericairn-0.8.2-docs.zip` | This documentation set, including the estate-derived release notes and their recorded window |
| `dotnet-vc.0.8.2.nupkg` | Portable .NET tool, invoked as `dotnet vc`; no Workbench or macOS helper |
| `Vericairn.0.8.2.nupkg` | Dependency-only SDK front door |
| `Vericairn.*.0.8.2.nupkg` and corresponding `.snupkg` where produced | Community libraries and portable symbols, published from these exact verified files after release publication |
| `SHA256SUMS` | Artifact SHA-256 values |

Hashes, sizes, observations and acceptance belong to the release record and its evidence. The structural documentation check verifies the set's completeness; release review and acceptance remain separate steps.
