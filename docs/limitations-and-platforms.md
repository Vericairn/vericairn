# Limitations and platforms, Vericairn 0.9.4

The estate's release record, written when the candidate is observed and superseded when publication is observed, is the authority for its commit, workflow run, artifact hashes and publication. This set describes implementation and test boundaries. Platform and live-session qualifications require their own recorded observations; documentation and stand-in tests do not establish them.

0.9.4 is the release after 0.9.3. It readies an estate for work that goes on between the commands a person runs. A
standing agent carries each occurrence its triggers make due, within its caps, and every output it raises lands as a
possibility, question or publication request that another principal disposes of; the duty carrier, researcher and bug
hunter ship as built-in standing roles (GOAL-07, GOAL-08 and PROC-08, with FIX-155). A flow's step takes its timing and a
decision among named paths (PROC-18 and PROC-19). Concurrent assignments are compared by their permitted paths, and a
concurrent-paths policy holds an unresolved overlap (CODE-73 and POLICY-08). The daemon keeps presence leases for agent
sessions, with a person's availability and quiet hours and a hand-over when a session lapses (ID-11, GOAL-11 and
DOCS-25); the dispatcher's scope and role closings are recorded and checked (ID-07 and ID-14); and a run's record shows
what it could reach, with public retrieval for research runs (CODE-82 and RUN-04). An operator action may ask one
presence check for a list of signatures it announces first (ID-15, ID-16, CODE-80 and DOCS-37), the operator answers
asks with their recommendations and signs every ready item in one reviewed act (OPS-16 and OPS-24), optional requests
become possible todos (OPS-22), an observer that stops reporting becomes attention (OPS-20), a timed stop drill measures
stopping a run (OPS-21), progress is kept at a target such as a GitHub discussion (WATCH-06), and an upgrade has a
restore point and a stated way back (FIX-145). Status and the other reads judge the estate once per command (FIX-136,
FIX-148 and FIX-152), progress counts a release's placed work (FIX-149), an acceptance of a work order's current revision
accepts its current result (FIX-141), export and restore carry the owner's agent instruction files, which the estate
check reads for hooks, automatic approvals and secret-like values (FIX-135 and FIX-139), the held guides take their
changes as fragments (OPS-17), each documentation set carries `llms.txt` and `llms-full.txt` (DOCS-38), and MCP tools
carry behavior annotations (MCP-07), with CODE-81, FIX-134, FIX-137, FIX-138, FIX-140, FIX-142 to FIX-144, FIX-146,
FIX-150, FIX-154, FIX-156, FIX-157, FIX-159, CI-08, CI-09, DOCS-35, DOCS-41, DOCS-45, DOCS-46 and DOCS-48. It runs git on
a repository it did not create without letting that repository's configuration start a program (FIX-147), gives the
secret scan of a checked push only the inputs it writes itself (FIX-160) and starts every program by its resolved
absolute path (FIX-161), as its release notes' Security section describes. Every distributable is still built with SDK
10.0.404 and embeds the .NET and ASP.NET Core 10.0.15 runtimes.
In 0.9.3 Vericairn corrected what the estates reported in 0.9.2 and readied the release flow for the next release.
Export and restore kept each file's executable mode (FIX-112), a timestamp in the wrong form was refused with the form
required (FIX-114), `doctor --json` returned one documented result and every command that takes `--json` wrote JSON
(FIX-119), document reviews, pins and checks said what they declare, need and found (FIX-113, FIX-120 and FIX-122), an
operation kept its intent and target in every revision (FIX-109), a goal was recorded impossible only after its
evaluator's verdict (FIX-116), and work proposed for a goal waited for its sponsor's approval (FIX-117). Every operator
action announced and counted its presence prompts (FIX-124), release steps signed only what they store (FIX-130), and
the work-order key check, key closings and admissions were judged in near-linear time (FIX-107, FIX-121 and FIX-131). A
change merged through a required merge queue with its outcome recorded (CODE-71), an operator action's push and merge
trail reached the estate (FIX-126), and a work order whose current result is accepted read as accepted (FIX-128), with
FIX-125 and FIX-127, so a release could run on its declared flow. `vericairn estate first-authority` named an estate's
first authority (CODE-72), guides covered enrollment's presence prompts and a cold recovery's inventories (DOCS-33 and
DOCS-32), an estate's root could hold its owner's agent instruction files (CODE-70), and main's push run skipped a
commit the merge queue already tested (CI-07), with FIX-118 and FIX-133. It read a key only in the form Vericairn makes
(FIX-132), as its release notes' Security section describes. Earlier published releases, and the withdrawn candidates
0.5.0, 0.5.1, 0.6.0, 0.7.0 and 0.8.1, are recorded in their own frozen sets.
0.9.4 reads record formats 1, 2 and 3 and writes the estate's own format, at most 3; an estate at format 3
upgrades from 0.8.0, 0.8.2, 0.8.3, 0.8.4, 0.9.0, 0.9.1, 0.9.2 or 0.9.3 without a format step. The format-3 upgrade
from 0.7.3 preserves historical records and requires explicit adoption afterwards. The features and corrections
of 0.8.0 through 0.9.3 remain. Earlier sets stay frozen.

Before 0.9.2, the shipped builds, which run without ICU, compared held keys, held phrases and blocked terms with case folded but
compatibility forms unchanged, so text in full-width, ligature or other compatibility forms of a held key or a blocked term was
not matched; ordinary text was. From 0.9.2 the comparison uses Vericairn's own Unicode folding and matches those forms in every
build.

The earlier custody, accounting and transport limits still hold. Tests and documentation do not replace
independent review or operator acceptance.

## Policy basis across platforms: fixed in 0.8.3

Each platform's 0.8.2 archive carried a separately compiled build of the same libraries, with the build
folder's platform name embedded in each assembly. Policy evaluation identifies its implementation by the
compiled assembly's identity, so the same estate's policy basis digest, and the implementation digests it
covers, differed by platform ([issue 184](https://github.com/Vericairn/vericairn/issues/184)). It was not
a security problem. 0.8.3 (FIX-61) builds the libraries without platform-specific intermediate paths. Its
shared packaging job reads the Vericairn library assemblies embedded in all six platforms' CLI and
Workbench bundles, including Workbench's copy of the CLI, and fails if their library sets or assembly
bytes differ, which holds the identities used by policy implementation digests equal across platforms.
The fix changes no policy digest formula or frozen release documentation.

## Known issue in published 0.8.3

On a slower Windows machine, `vericairn status` can exceed its 30-second read deadline for a large estate and
fail with "The request timed out." (exit code 124). Vericairn's own estate, about 4,100 records and 70 living
documents, takes over 30 seconds on GitHub's Windows runner with 0.8.3, against about 22 seconds with 0.8.2;
macOS and Linux take 16 and 25 seconds, as before, and render byte-identical status, policy basis included.
`estate check` and other commands are not affected. 0.8.4 keeps status within its deadline on Windows
(FIX-63, [issue 205](https://github.com/Vericairn/vericairn/issues/205)).

## Reads on a large estate (0.9.4)

From 0.9.4 a command without a daemon judges the estate once and verifies each signature once (FIX-148). Within that one
command, a working-tree file the records do not name (a document, a result's output, a duty's proof, an object) that another
program changes after the command's first read is read as it was at that read, until the command next writes, as two reads
within one command could already differ before. The command keeps its judgment while the record files and the estate's
manifest (`estate/manifest.json`) keep their sizes and modification times. A record file changed in place keeping both is not
noticed, by the command or by a daemon, as the live store's own synchronisation already assumes. The manifest changed in place
keeping both is not noticed by the command, only by the next one, while a daemon and `mcp serve`, which judge at every call,
read it at their next call. Every write judges the estate afresh under the working tree's write lock, and reads the manifest
as it is then.

A read that stops at its request limit, 30 seconds for `vericairn status` and the other interactive reads, still fails with "The
request timed out." (exit code 124). From 0.9.4 the diagnostic also names the limit, what the runtime was judging when the read
stopped, as far as the runtime reported it (a daemon serving the command reports nothing to it), and the lighter reads to use
meanwhile: `vericairn operator list`, `vericairn goal` and `vericairn estate check`. The limit is not raised. On a copy of
Vericairn's own estate (11,181 records) status, human and `--json`, answers in about 6 seconds, well within it.

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

The archives are self-contained .NET applications and require no separate .NET runtime, but still depend on the operating system's native libraries. 0.9.4's are built with SDK 10.0.404 and embed the .NET and ASP.NET Core 10.0.15 runtimes, unchanged since 0.9.2 (FIX-115). Workbench needs a desktop; on Linux its Avalonia desktop uses X11 or XWayland. Secret-service custody needs a running service and `secret-tool`. The macOS helper is compiled for macOS 13.0 and later and probes enclave availability at runtime. A machine that cannot use it falls back to other available custody. The release carries no iOS or Android client: [Remote](remote.md#building-from-source), the phone companion, is a developer build made from `src/Vericairn.Mobile.slnx`, outside this solution. 0.9.4's phone heads are built with workload sets 10.0.401 (iOS) and 10.0.404 (Android); they ship in no release artifact.

The workflow packages unsigned developer builds. There is no application-signing or notarization step, application bundle or installer. Check downloaded bytes against `SHA256SUMS` and the estate's recorded hashes; see `install.md` for quarantine and SmartScreen behavior. These checks establish the downloaded bytes, not a platform signature.

The NuGet packages are framework-dependent: the `dotnet-vc` tool needs the .NET and ASP.NET Core 10 runtimes supplied by the .NET 10 SDK. It installs no Workbench and no macOS Secure Enclave helper. Use the macOS archive for the bundled helper. The worker's agent-side MCP command still resolves `vericairn`, so a NuGet-only worker installation is not qualified by package smoke tests. `Vericairn` is a dependency-only SDK front door; it does not bring an embedded runtime or daemon. The release workflow builds packages into the draft; NuGet Trusted Publishing happens only after the operator publishes that release. The packages of 0.8.0 and of each later published release up to 0.9.4 are published. Name-reservation versions are not product releases.

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

0.9.4 retains goal proposal and lifecycle, record predicates, independent evaluations, acceptance, roster routing, asks and one-step dispatch, and retains record-derived hand-over and resume. Record format 3 arrived in 0.8.0; 0.5.2 was the first published format-2 release. The upgrade preserves original bytes and supplies a refusal boundary for older readers. The progress, communication, execution and harness-qualification additions shipped in 0.7.1 remain; the scope-read and attempt-missing signal values need readers that understand 0.7.2. A shared format number does not establish that an older writer understands them.

- The evaluator must have authored no step; the accepter must be neither a step's author nor the evaluator. Activation and spending need the accountable person's authority. Goal acceptance is not an automatic consequence of a passing predicate or an exited harness.
- `goal next` depends on this machine's launchers, keys and custody. Predicates involving covered files or unavailable living-document contributions can also differ by machine. Record-only predicates and the eligibility relations derive from the records.
- `goal step` takes one eligible step per call. There is no continuous multi-step planner or scheduler in this Community implementation. It neither activates nor accepts a goal, funds more work, nor answers an ask.
- An assignment and its run intent or ask are validated together and journaled, but sequential storage is not atomic. A stranded routed assignment holds no step, and reconciliation reports an incomplete pair.
- Routing does not silently replace an unavailable contributor. A person takes over with a new assignment; the old runs receive stop requests, whose containment still must be observed.
- FIX-12 checks the work order's current holder for every run, before workspace preparation and again under the write lock before intent. A new run on an assignment displaced by hand-over is refused whether explicitly named or routed, with nothing recorded or charged. The check grants no new authority and does not certify containment of an existing run.
- Asks deliver at most three per principal in a read response, with the remainder queued. A stalled run can record an attention ask, but there is no external notification delivery here. A decision by the principal asked answers it; reading, dismissing or waiting does not.
- Presence (0.9.4) is one runtime's observation: the daemon keeps leases in its live store, and only a running daemon notices a lapse, sweeping every five seconds. A session served by the embedded runtime, or whose transport cannot reach the daemon, is not tracked. A lease the daemon keeps survives a clean restart; a session whose lease the daemon dropped registers again at its next heartbeat. A live lease says the session's transport runs, not that work moves; the stall rule and requested progress judge that.
- A session holds what its principal records while it is live; with several live sessions of one principal each holds it, and a holding lapses only when none is live. An attempt recorded on another runtime and found after a pull is not attributed to any session here.
- The lapse ask is signed only by the principal that recorded the assignment, as the stall rule signs with the principal that started the run. When that write is refused, as when this machine holds no key of that principal, the attention says so and the sweep tries again after a minute, then twice as long each time, up to an hour. A key needing its owner's presence prompts for that write as for any other. The ask is withdrawn by the next sweep after the lapse ends, unless a decision answered it.
- The displaced-holder refusal is applied by the runtime that receives the write. A record another runtime wrote before a hand-over reached it, or one written by hand, cannot be refused when it arrives by pull: its assignment reads `displaced` and no review is asked for its result, but nothing removes it. Who holds a step is decided by who recorded each assignment, never by stated times across kinds of writer: once a person has assigned a step, the person's latest assignment holds it, and a person may move it again; an agent's or a service's assignment never takes a step a person assigned and holds only a step no person has assigned (the first such holds; one to another principal holds nothing). A dispatching agent or service can therefore no longer move a held step to another principal, and a lapse basis does not let it; the carrier's lapse hand-over (0.9.6) has to reopen this. Existing records are read by the same rule. The runtime refuses a new assignment dated before the step's latest one; a record written by hand is not refused, and among the assignments of a step no person has assigned stated time decides, so a hand-written agent's or service's assignment dated before the others holds the step until a person assigns it. A step's history keeps every assignment: a displaced agent that was assigned the step or recorded work on it stays an author of that work and is never its independent reviewer. For a run of a flow, a decision dated before a hand-over keeps deciding the earlier round it decided; only decisions under the step's current holder count in the current round, judged by the holder rule over the step's assignments recorded before the decision, so a decision under an assignment that holds nothing never counts and never keeps the holder's own decision from counting. A standing agent's occurrence, its route and the operator's list of reserved work read their holder by the same rule.
- A hand-over's basis must be the sweep's standing lapse ask about the very assignment taken over, signed by that assignment's author, open, neither withdrawn nor answered; the record rule judges it wherever the hand-over is written. Whether the session is still lapsed is runtime state, which only `work takeover` on the runtime keeping presence checks; a hand-written hand-over is judged by the ask alone, so a recorded basis is evidence of a lapse only where the runtime recorded the hand-over. The assignment's author, often an agent, can word an ask as the sweep does; since a person's plain assignment moves a step without a basis, this gives no one a step they could not already move.
- Quiet hours (0.9.4) are evaluated in the person's IANA zone with this machine's time-zone data; a zone this machine does not know holds nothing. No notification is delivered in this build, so quiet hours hold nothing yet; Remote's notifications and reminders (0.9.5) apply them.
- The session route (0.9.4) judges a step's exposure conservatively until the exposure algorithm (`RUN-06`, 0.9.5) is built: any goal, goal input, work order, work-order input, pinned step input or, for a held step, assignment, attempt, result or review that a principal other than a person wrote, any conversation observation and anything unresolved counts as reading, so in an estate where agents write work orders few steps reach sessions. What a session writes is not yet marked outside-derived; `RUN-06` adds that.
- A principal holds at most eight live sessions; a heartbeat restores a lapsed lease only within them, and a principal keeps at most sixteen leases in all.
- Settlement withdraws the run's still-open or notified process attention asks, including stall and command-audit asks, as no longer needed. Withdrawals pin the exact settlement and preserve the question and observations; already answered asks stay answered. Closed asks no longer produce stale-subject warnings. The new `attempt-missing` and `scope-settlement` asks remain open for review, pinned to the settled operation's exact revision and hash. The settlement, new asks and withdrawals are validated together and journaled; interrupted storage still requires reconciliation. Withdrawal is not an answer by the person asked and accepts no work.
- Pausing or abandoning a goal prevents new launches; it does not stop runs already in flight. `goal stop` records abandonment; use `worker stop` for each run that must stop and inspect the stop facts. `limits.deadline` is validated and recorded but not yet enforced by the routing or worker code, and so is `limits.consecutiveFailures` for a goal that runs no flow; operators must monitor and act on them. From 0.9.2, `goal next` and `goal step` hold a run of a flow that reaches its `limits.consecutiveFailures` in failed steps in a row until its sponsor answers; a run started directly with `worker start` is not held by it.
- Resumption and goal revision do not erase run charges: the ledger includes runs naming any revision of that goal. Concurrency defaults to one when not stated; omitted token or currency allowance is not unlimited spending authority.
- From 0.9.2, a run's reserved step waits on the operator as an operator request, and no grant of a reserved act takes effect before `ID-04`, so no agent carries or completes one. A reserved step's done predicates count only authentic records whose author was a person with the operator role when they were made, and name no derived `state`. An agent's record of the operator's outside act therefore does not complete the step, even a faithful one, such as a publication an agent observes under `local-engineering`; the operator records it, signed even where the charter makes signatures optional, and the step's reasons name each record that does not count.

- From 0.9.2 (PROC-07), the software pack's release flow publishes through the GitHub Releases provider's stable channel:
  its `publish` command is `gh release edit v<version> -R <owner/name> --draft=false --latest`, and its `stable-channel` trap
  holds the step, with no command, for a candidate of another channel, which is published with the command its `release
  observe` shows. Another release provider's product has no publication command in this flow yet.
- The release flow's machine checks are enforced by the commands that carry their steps, not read from the records:
  `release submit --check` refuses a client of another build, a clone without a default head and a held document, and the
  upgrade's runtime step replaces an older daemon. `goal show` lists them as traps with their `check`. The upgrade flow's
  `runtime` step reads what `estate verify` recorded on the machine that ran it; another machine is verified there.
- `docs pass --before` finds tokens and phrases; it never judges whether a sentence is true. A version standing as history
  outside a history section, a release label or a marker such as `From`, `in`, `follows`, `--from` or `or earlier` is
  reported, and a claim worded as history, such as "From 0.9.1 the current release is…", is not; mark a history section with
  `<!-- vericairn:history -->` under its heading. From 0.9.3 each location names the rule it matched, and `current-mention`,
  a mention no other rule explains, is reported because the pass cannot tell it from a claim. It reads only the declared
  documents the guides directory holds, through no link. The flow's `security-section` trap holds the set only while an ask
  about the carrier still waits, open or notified; it proves no approval. It cannot tell whether a release needs a section,
  what the deciding decision says, or whether an ask was withdrawn instead of answered, and a set frozen with none asked, or
  after one was withdrawn, passes it.
- From 0.9.4 (FIX-137), a checked push lets the carrying release's documentation set carry a held work order's text once the ask about the carrier that begins "Approve the Security section of <version>" is answered by the operator's decision recording "The Security section of <version> is approved for <key>." and naming that work order among those it affects, after its current revision, with none still waiting. It reads those forms, not the section: nothing binds the decision to the section's bytes, which `insert --expected-hash` checks when the set is prepared, and the forms are as strong as the practice of recording the operator's answers, since a decision an agent records naming the operator as its decider counts. It knows the set by its layout and its commits: a `vericairn.docs/1` manifest at `docs/releases/<version>/manifest.json` that the push's head holds and its target branch does not, and every commit the target does not hold either beginning its subject with a held key of that release and a colon, or changing only Markdown, JSON and plain-text files under `docs/`, `README.md`, `llms.txt`, and `Directory.Build.props` in its `<VersionPrefix>` line alone, compared with the target branch's file. A held fix's commit is trusted by its subject: it may change any path. A product that keeps its sets elsewhere, a set whose pull request changes other paths or other build settings, and a `Directory.Build.props` that differs from the target's in other lines because the target moved on are refused until publication, or until the branch takes in the target. A push adapter that does not implement `FileText`, `CommitMessages` and `ChangedPaths` never lets a set pass; a commit whose paths exceed the adapter's 4 MiB of output cannot pass as the set, and the refusal says the paths could not be read.
- From 0.9.4 (FIX-137, decision 01M7PNCMA3V2ZQG2ZK9V7NBEJK), the disclosure hold reads every message and every commit's own added lines, as text whatever the clone's attributes and big-file threshold, in UTF-8 whatever the clone's output encoding, and a merge's lines added against every parent, or the push is refused. A line holding NUL characters is read again with them removed, so UTF-16 and UTF-32 text is matched by its letters, but text in other encodings, compressed or encoded files and images is not read as text; and binary data whose bytes, NULs removed, happen to spell a held key would refuse the push. The patches are read by git's plumbing, which the clone's log and diff display settings do not change, and a commit whose tree changed must show a file. A file larger than 512 MiB, patches beyond the hold's own bound of 64 MiB together, or messages beyond the adapter's 4 MiB of output are unreadable, so a push carrying them is refused while a hold is in force. A submodule's own content is not read: its pointer is. Before 0.9.4 text one commit added and a later one removed, the added lines of a file git called binary, an added line whose text began with `++`, text in a UTF-16 or UTF-32 file, and every message under a clone that set `i18n.logOutputEncoding` to UTF-16 were not read, so held text in them passed. From 0.9.4 `operator record`'s check of a resumed commit also reads its real parents, whatever grafts the clone holds.
- A corrective version after a withdrawal is a revision of the run's goal naming the new version; the run then derives afresh
  from `docs-before-freeze`. The release flow's `gate` step does not itself look for the history checkpoint with trigger
  `release` that `release submit` writes before its own admission check in an estate that adopted checkpoints; the submission
  is refused with `history.checkpoint` when none can be written, and `release submit --check` writes none.
- Vericairn's first-party extension, with its NuGet, MCP Registry and announcement steps, is the first-party estate's own flow
  under its `docs/templates`; no other estate has it, and none needs it.
- A reserved step's command is bound from the records only when the step is next. A bound value with characters outside letters, digits and `. _ : / @ + = ,` and `-`, or one starting with a dash, is never put in: the step shows `blocked` until the value changes. A text subject field with a space blocks the command in the same way.
- A request that is no longer due, `withdrawn` or `settled`, is derived when it is read and stays open. No record withdraws it, and only its operator closes it. Live reads derive a run's position when they are read: human status, `goal show`, `goal next`, the request queue, required attention and request events. A due time, backoff or watch limit can therefore change them between two reads of the same records. Canonical status JSON reads no clock: it derives a run, its stops and the delivery of its requests as of its newest record (`asOf`). Until a later record is written, it can show a step as blocked that a live read already shows as due. The progress report, which also fills generated documents, lists no runs or stops.
- At most three stops of the estate's runs reach one person at a time; `goal step` queues the rest. The asks queue and the operator request queue still each deliver three per principal across every goal, so a person can see up to three asks and three requests at once. Canonical status, and Workbench's home and Remote, which read it, show a reserved stop without its command; a live read shows it. A stop carried outside `goal step`, such as an effect a person carries, a watch past its limit, a step no one is named for, or the goal's acceptance, is shown but nothing is recorded to reach its person.
- From 0.9.4 (PROC-18) a step's timing is evaluated when the run is read: a due step is taken by `goal step` or a script that calls it, and nothing takes one unattended until the carrier (GOAL-10, 0.9.6). A step's due time is derived from the records and the clock and stored nowhere, so, like a backoff, it changes between two reads of the same records, and canonical status shows it as of its newest record. Time zones are the IANA names the Unicode CLDR maps to Windows zones: Linux and macOS read each zone's rules from their own time zone database, and Windows from the Windows zone the name maps to, so two platforms whose databases disagree, as for a past or newly changed rule, can give a local time a different instant. A reserved act armed ahead of its time is checked again from the principal record of its author as it stood at that time.
- From 0.9.4 (PROC-19) a decision transition's rule reads the records made at or before the step's round began, a state its predicates name, such as a work order's, as status derives it when the rule is read; its decision counts once `goal step`, or a script that calls it, records it, and nothing takes a path unattended until the carrier (GOAL-10, 0.9.6). A recorded rule decision is evaluated again over the records it names; a state its predicates name, and whether it names every record its rules read, are checked only when it is written, and a rule naming an input an earlier step's output carries is not evaluated again. Both checks read the records as they stood when the decision was recorded; one written around the runtime that fails either is information in the estate check, and the run's position ignores it, saying why. When a round began, and whether a step's deadline had passed, are derived from the run's position, which validation does not derive: the position ignores a rule decision reading the records as of another time and a deadline's default dated before the deadline, while validation reports only what the records alone show. A person decides with `goal decide` from the command line, the API or the SDK, and an agent also with the MCP `goal_decide` tool, which is not offered over the network. Workbench and Remote show the decisions and record none in this build: Workbench's Goals workspace shows and never acts, and Remote's phone-held presence key signs each record itself, while a decision is two records the runtime prepares together. Whether a decision would turn off a protected step is judged, for a path that ends the run, by the steps the deciding step does not wait on when the record is validated, and by the run's position when `goal decide` plans it.
- From 0.9.4 (CODE-73) the overlap check compares two active assignments exactly over the worker's pattern grammar, the selection's selectors and the valid paths, by a search bounded at 100,000 states (the patterns' own automata, their subset states and the product states) and at 5,000,000 steps of work; a pair it cannot decide within either bound counts as overlapping, labeled undetermined, and up to 20 tracked witnesses are sought within a bound of their own, so a very large clone may show fewer. Matching is ordinal: on a case-insensitive filesystem two paths that differ only in case name one file, which the comparison does not see, and the view says so for a clone there. Status and progress read the records alone, so they show neither tracked witnesses nor a change's integration seen only in a clone; `work overlap`, run admission and new attempts read this machine's registered clones, whose answers differ by machine and are labeled as observed here. A repository is compared by its identity: an estate that registers its own repository as a resource as well has two names for one repository, and assignments naming one are not compared with those naming the other, so name one. An assignment recorded before the check names no repository: it is compared under the one its agent runs named, or as possibly in any repository, labeled, and its completed result counts as released at completion, since nothing recorded its integration. Which results come before the check rests on the times the records state: the check begins at its first record (the first brief naming a repository, exclusions or `after`, or result naming a change), and the command line and the API take a record's time from the caller, bounded below only by the signing device's admission, so an attempt and a result dated before that first record count as completed before the check, without a change. A change's integration is observed from the main line of this machine's registered clone: the branch origin's `HEAD` names, else `main`, else `master`, which the clone's owner sets and moves, and which a run naming it as its branch moves at settlement, so a main line an agent run worked on shows no integration. A head a `change-merge` operation merged stands as the records show it where no clone shows its base is no ancestor of it or its range touches none of the assignment's paths, so status and progress, which read the records alone, can show released what `work overlap` on a machine with the clone shows mismatched and active. What ties a result's change to its assignment is a claim: its repository, its range and one of the paths that range changed must be the assignment's, but a result that names a range on the main line of its own repository touching one of the assignment's paths still releases it, though the work is no part of that range. That takes a false claim, deliberate or mistaken: a result naming its fork point and the main line's tip while its own work is still unmerged releases the assignment whenever another change in that range touched one of its paths, which is when an overlap was there to guard. A squash merge keeps a main-line range nameable, and reporting the paths a change takes outside its brief is CODE-74's, not this build's. Each reading that consults the clones asks about every open assignment whose completed result names a change, so its cost grows with the distinct changes such results name: about 300 ms and 9 git processes each on one machine, 12.7 s and 360 processes for `work overlap` with 40 of them, and `work assign` and `work attempt` pay it while holding the runtime's other operations. Acceptance ends that cost for each; caching it across readings is DESIGN-64's. The `concurrent-paths` kind's findings, assignment-start refusals and pair exceptions are in this build, as the next bullet describes (POLICY-08). The carrier's honoring of `after` unattended (GOAL-10, 0.9.6) is not: until then a held overlap is sequenced by `goal step`, the runs and attempts it holds wait, and the rest is reported.
- From 0.9.4 (POLICY-08) a `concurrent-paths` policy's findings in the estate check and status read the records alone, while every assignment-start refusal, of a new assignment, a revision, a run admission or a new attempt, also reads this machine's registered clones for what they show of each assignment's release, as the overlap check's waits do; a pair the clone shows released on one machine can be a finding on another until a `change-merge` operation or an acceptance records it. Only writes through the runtime are refused: an assignment recorded before the policy, in another clone or by an older runtime is reported and held when work on it starts. An exception pins revisions that already exist, so a new assignment under a refuse-level policy is first recorded resolved, in sequence or with the shared paths excluded. Its findings never hold a release submission, so a release check passes while a pair is open. A format-3 reader that predates 0.9.4 refuses a policy exception with a `pair` target (`body.enum`). For every kind, an exception whose delegated authority reference does not resolve exactly is not applied, failing closed.
- From 0.9.4 (ID-07) the `dispatch` scope and the dispatcher are records and checks only: nothing in this release dispatches unattended. The carrier (`GOAL-10`, 0.9.6) signs as the dispatcher. A lapse that lets the carrier reassign a step is runtime state (presence leases, `ID-11`), which records do not show, so the check reads a dispatcher's reassignment against its grant's `reassign` and its target rules, not against the lapse itself; and "the roster's next available principal" is checked as a principal the roster names for the role other than the holder, since availability is this machine's. The expiry is bounded at 30 days; the operating mode's checkpoint cadence, which an `agents`-mode estate would also bound it by, is not recorded in this release (`ESTATE-02`), so it is not checked.

### Standing agents and their outputs (0.9.4)

- From 0.9.4 Community carries a standing agent's occurrences one per `goal step`, by hand or by the owner's script; nothing watches triggers or dispatches unattended until the carrier (GOAL-10, 0.9.6). `runsOn` is recorded, validated and shown; the carrier enforces it.
- A standing flow carries each occurrence as one assignment: it declares exactly one routed step, of kind work. Multi-step standing flows wait for the carrier.
- A duty trigger's occurrences are evaluated by the runtime's policy evaluation, in `goal due`, `goal next` and `goal step`; canonical status and progress, which read the records alone, list only the duty occurrences already carried.
- A condition trigger fires once for each new set of records its predicate reads while it fails; a predicate that fails with the same set again, after it held in between, does not fire again. A condition's failing set and an event record's authorship are checked for a new assignment as the records stand, at the write boundary; a merged copy keeps the reading its records had.
- An agent's records trigger another agent only through an event trigger naming the first agent's standing role, and only from an agent whose own triggers name no role, so chains end at depth two.
- Activation is not refused in `people` mode, which the operating modes' build adds.
- The open-output cap (ten open outputs defer dispatch) follows the standing outputs, whose possibilities name their agent and occurrence; GOAL-07 builds the awaiting-review cap only.

Standing outputs (0.9.4) are recorded by the occurrence's contributor and disposed of by another principal; they are not offered
over network MCP in 0.9.4. A fingerprint is the agent's, by its flow's rule: the runtime refuses an exact repeat, not a reworded
one. The holds, reads and sends rule for outputs written by a reading run (an outside-derived standing output, and an adoption that
cuts a hand-off only as a person's cutting review) arrives with GOAL-15.

A held finding is kept out of public pushes once it is adopted as a held work order. Before that, the checked push does not match
an unadopted held possibility's own title and text, and its text is not kept in custody. The estate advisor's build (GOAL-13, with
CODE-25's custody store) carries the matcher and the custody for the advisor's own held possibilities, and from decision 01M7Y5VRW6M2CS4X42E4MJ3M4Z for every
standing agent's held findings, a researcher's and a bug hunter's included, though the bug hunter commits its reproductions on its
occurrence's branch. The agent's next occurrence names its held findings among its sources, which exposes nothing more in 0.9.4, where every
agent may read every record, and GOAL-13's never-shown rule covers it before held readers exist. The ask to the operator that goes with a held
finding is the role brief's instruction to the agent, not a rule the runtime enforces; GOAL-16, in the future-work backlog, enforces it
and gives the runtime's own asks a marker no agent can set.

A person who is not an operator may decline or park a held finding, since a person disposes of each, and an operator who is a
person can reopen it. Such a decline is only as strong as the custody of that person's key: the worker's custody guard refuses an
unattended launch only while a key of an operator or of the goal's sponsor is held on the machine and signs without presence, so
another person's file-custody key on a dispatching machine is within an agent's reach until RUN-06 (0.9.5) extends the guard to every
person's key for every agent run.

goal step writes the renewal, period-cap and failure-pause asks as its caller and signs them with the caller's key, and no record
marks them as the runtime's: an ask in their words counts as the runtime's when its author may take the goal's steps. An agent
that carries the occurrences and may itself take those steps (one holding `local-engineering`) can therefore write one, as it
could by calling goal step, which holds back the runtime's own ask of that kind; its own answer to any of them still answers
nothing.

A tied agent's disposition of a possibility that reached the records by hand, dated before the record that ties the agent, is
reported at load, and from FIX-155 it disposes of nothing, as its decisions on the agent's asks answer nothing, whatever their date;
nor does any attempt, result or run such an agent records end the agent's failure pause (FIX-155's held additions state both).

No record an agent tied to a standing agent writes disposes of the standing agent's outputs or ends its failure pause, whatever its
date: its declines, parks and adoptions dispose of nothing, its decisions answer nothing, and its attempts, results and runs never
end failures. Ties only grow, so this fails toward you: an agent once assigned the agent's work or named for its role, a builder once
on the roster for that role included, can never again record your decision or a disposition that counts on that agent's outputs;
and where the principal that records an agent's runs is itself tied to it, as an agent that steps its own standing goal under
`local-engineering` is, none of its runs ever succeeds, so only your answer ends its failures, and a stop you ask of its runs, which
that principal records, counts as the agent's failure.

Two record files carrying one identifier leave their line unread, and so do two new files with one identifier that supersede a record.
No record counts that a reading of the unread records would refuse (judged with them left out and read each way they can be, up to 16
readings, past which no record counts); a standing agent's readings also count what they cannot read against the agent (its failures,
open outputs and ties, every record that does not count while one is unread, and the hold of every standing agent); elsewhere, outside
counting, the line reads as absent, as if its files had been removed, so a goal status or a review in it restricts nothing in the
readings and a flow goal's ledger of runs does not count a run in it, and nothing acts on those readings, since every acting surface
refuses while the copy stands. The estate reports every copy as an error and refuses every write until all but one is removed. The estate view and
Workbench label every held standing agent "held by its failures"; the agent's reason, which `goal due` shows, names the record that
is not read when that is what holds it.

Judging the readings of unread records reads the estate's trust once for each reading, so an estate with several copies loads more
slowly until they are removed; an estate with none loads as before.

A run the runtime saw succeed counts as a success only when its settlement names the result its agent recorded during it, as the
worker records it at the run's exit; a run settled from the worker's retained observation after a restart, or by hand, names none
unless whoever settles it names one, and so counts for nothing. A success is not a judgment of the work, which a review makes.

The first standing roles (0.9.4) state the authority each effect needs in their role brief and never lines; the flow contract has
no machine-read `needs` field yet, so a grant is still checked where the effect happens (the run's admission, the publication
request), not when the agent is activated. In Community, `goal step` carries one occurrence per command, by hand or by the owner's
script; the issue resolver and customer service arrive with conversation intake in 0.9.6.

## Worker launchers, limits and custody

The software pack lists `claude-code`, `codex` and `generic`. The native launchers configure the harness's goal mode and estate MCP server, and load the machine-local configuration described above. Generic dispatch is refused before executing its command because it cannot establish a complete inventory. The only compiled native baselines are codex-cli `0.159.0-alpha.12.1` (from 0.9.0 also `0.163.0-alpha.10.2`, bundled with the desktop app, and `0.163.0`; FIX-81) and Claude Code `2.1.296`; other exact versions need a person's authorized RUN-03 qualification and matching local probes. No version range is inferred. A desktop app update that moves its bundled Codex to another version stops Codex workers that run it; the [software guide](software.md#desktop-app-updates-and-a-pinned-codex-fix-81) gives the pinned-install route.

RUN-03 qualification is restricted to a human operator or a human explicitly granted `qualify`; agents and services cannot qualify, and `administer` or `local-engineering` alone is insufficient. The record pins six probe digests, not a vendor signature or a live-inference observation. Admission checks the version, declared flags, tools, configuration, login boundary and MCP facts locally before intent and again before dispatch. A non-baseline qualification must still be current and match all probes, and the independently admitted RUN-01 inventory must match. Revoking a record does not remove a compiled baseline. Protocol tests use stand-in processes and captured fixtures. The seven live Codex runs below provide separate, bounded evidence; neither kind establishes provider billing, arbitrary future event formats or vendor authenticity.

Every run requires explicit model, effort, provider, destination and data boundary. The local launcher configuration, roster, goal, assignment and run choices have recorded precedence; restrictions accumulate. Missing required choices or unknown exact revisions refuse rather than using ambient defaults. The environment inventory must cover tools, skills, plugins, hooks, MCP servers, helpers, dependencies and effective configuration, with matching admitted identities and inherited environment limits. Requested execution is separate from observed execution; an absent actual fact remains unknown. The [local efficiency adapter](efficiency-adapter.md) is built, disabled by default and refused without the operator's machine admission of the adapter and launcher. 0.9.0's release security review covers it. Protected and unclassified payloads pass unchanged; eligible bytes are measured, with tokens unknown.

The custody guard refuses unattended launch while a locally held key of an operator or the sponsor signs without presence, unless the accountable person recorded a current, expiring exception for the goal revision. Revoked and unadmitted keys count too, because possession still permits signatures. This guard does not isolate the harness from the operating-system user's files or credentials. Use a separate user when that isolation is needed.

From 0.9.4 (FIX-165), on Windows a harness command, a workspace setup command or a `check run` command that is a batch program
(`.cmd`, `.bat`) runs through the system directory's `cmd.exe`, which reads no percent sign and no line break or other control
character as data: an argument or a script path holding one is refused before anything starts, and so is a command longer than
the 8191 characters `cmd.exe` reads. The native launchers pass the goal condition as an argument, so a condition holding a
percent sign or a line break refuses a run started through a `.cmd` shim npm installs for a harness; name the harness's own
executable to start it without `cmd.exe`. A double quote inside an argument reaches a program the script hands its arguments on
to (`%*`) as given only when that program reads its command line the C runtime's way, as Node.js does; the script's own `%~1`
reads it doubled, since `cmd.exe` has no other way to write one. What reaches the script is data on `cmd.exe`'s own command
line; a script that expands an argument unquoted (`%~1`), passes it to `call` or turns delayed expansion on reads it again as a
command line, and a program that reads its command line by rules older than the C runtime's current ones may split an argument
holding a doubled quote. Every program Vericairn starts on Windows receives `NoDefaultCurrentDirectoryInExePath=1`, so `cmd.exe`
and the system's own search find a program named bare in `PATH` only; a program that searches in its own way, without reading
that variable, can still look in its working directory. The launch binding hashes the script, not `cmd.exe` and, for an npm
shim, not the runtime and script the shim starts. An absolute path whose file name ends with a dot or a space, or that names a
stream of a file, is refused. A file name holding a dot, such as `harness.v2`, has an extension as Windows reads it, so it is
started as named and gains no `.exe`.

Codex receives a fresh `CODEX_HOME` with only the approved harness login imported; effective MCP configuration is probed and extra servers or incompatible managed policy refuse launch. Claude Code receives a fresh `CLAUDE_CONFIG_DIR`, restricted settings, hooks/plugins disabled and strict MCP configuration; detected managed policy refuses launch. Remote or undetected managed policy and managed Claude accounts are unsupported. These controls do not stop deliberate same-user file or keychain access.

From 0.9.4 (CODE-82), each run's record shows what it could reach, and nothing is blocked by the showing. Before the intent the worker plants a marker file outside the workspace and the run's temporary directory and a test host on the loopback interface, and a probe in the harness's place (the vericairn executable, as the same operating-system user, with the harness's environment and working directory) reports whether it read each; a format-3 intent records `execution.reach` with `readable`, `blocked` or `unknown` for the file and `reachable`, `blocked` or `unknown` for the host, and the count and kinds of the secret-bearing paths the user can open. Qualification records the same marker and host outcome. On today's launchers both read `readable` and `reachable`: neither confines its harness process, and Codex's sandbox confines only its shell commands' writes and network. The probe stands where the harness process stands; it does not run inside Codex's shell sandbox, and it cannot show what a harness tool would do that the probe does not (another protocol, another host). The test host is on the loopback interface, so a network control that admits loopback and blocks other hosts reads `reachable`. The secret-path inventory covers conventional locations under the harness's home, the run's imported harness login and the Vericairn home's device keys; secrets elsewhere (an estate clone's files, other repositories, browser profiles, a keychain item's own access control) are not listed, and a listed path is one the user could open, not one the run did open. The inventory opens without blocking and stops, incomplete, after ten seconds. The list of paths stays on the machine that ran the run, in `estates/<estate>/reach/<run>.json` under the Vericairn home, outside the run's directory, whose temporary directory and stop file the harness is told of; a process of the same user can still read it.

From 0.9.4 (RUN-04), retrieval is an interim admission, not confinement. The check reads records as recorded: a resource's visibility is trusted as recorded, not verified at the forge, and the resource type does not reserve a visibility change to the operator; a credential named only inside another record the run reads (two hops away, such as a brief source whose own inputs name one) is not found, while one in the run's own sources, inputs, context package or dispatch-time searches is; a repository that turns private after dispatch does not stop the run; and a public name that resolves to a private address (DNS) is not caught. The estate's own repository is found only by its recorded identity (its origin remote, with any recorded root commit agreeing, or a root commit for a repository with no remote), and an estate no resource records that way is refused. An address is read as a WHATWG URL parser reads it; a non-ASCII host is refused rather than mapped, so an internationalized name must be given in its `xn--` form. Only a brief that permits it gives a run web tools, and the check refuses a run whose records show a private workspace or estate repository resource, an estate repository no resource records, or a named credential; it does not check what the run's operating-system user can read on the machine (same-user reach), which waits for qualified confined reads (RUN-07), so a retrieval run in a public estate still holds what its user can read, and its web tools can send what it holds. The retained original of a `WebFetch` is the harness's reduction of the page as given to the agent, not the page's bytes; the worker fetches nothing to compare. A web tool asked for a loopback, private, link-local or local-only host, or an address with user information, stops the run, but the stop follows the event, which can arrive after the tool ran, and a public name that resolves to a private address is not caught. The source log in the run's directory is not tamper-evident against the same user. Codex's web search is never enabled: its events report a search's query, not what it retrieved. Third parties' words in retained sources stay in the run's directory, outside records and repositories; they are not yet masked or erased on a retention schedule, which the custody store (`CODE-25`) brings.

The worker reserves a run's whole allowance before launch. Unknown or incomplete token/currency usage is charged at the reserved cap; a lost observer or uncontained run remains in flight and reserved. From 0.9.0, a run whose supervising worker died settles as exited without observation once that worker's process and the harness's boundary are gone and nothing was observed for longer than its stall limit, charged the usage observed up to its last heartbeat and its cap in each unit not observed. Tokens and currency leave ten percent headroom, and wall time leaves a grace period and heartbeat. These are soft operational thresholds: a single turn, usage in flight or a failed shutdown can exceed them and make the remaining budget negative. Claude's USD budget flag and Codex's rollout token tracking do not turn every unit into a hard spending cap. Generic is not an admitted alternative. An uncertain prior run blocks retry for that assignment until its original operation identity is reconciled.

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
| Linux and macOS | The boundary is a POSIX process group. A process can escape by leaving the group or session; there is no independent guardian after a worker crash, and the run settles as exited without observation only after the whole group has exited. |
| Linux process inventory | Unavailable or filtered procfs can prevent proving containment. A failed inventory leaves the reservation in flight and is retried. |
| macOS process inventory | An inventory error also leaves containment unknown and the reservation in flight. |
| POSIX embedding | PID 1, ignored `SIGCHLD` or `SA_NOCLDWAIT` are refused; an embedding host with a separate child reaper is unsupported. The worker retains its child identity until final inventory and signaling. |
| Windows | A Job Object prevents breakaway after assignment and kills members when its last handle closes. A crash between suspended process creation and job assignment can leave a process behind. There is no graceful signal for a windowless process: a harness that does not watch the stop file needs escalation. |
| Stop observation | Request, transmission, acknowledgment, escalation and containment are separate facts. The command can return after its bounded wait before containment; inspect `worker runs`. Failed signals or queries are not evidence of exit. |

The scope audit compares physical workspace inventories before launch and after containment, excluding ignored build output and untracked worker brief exports, while retaining tracked and other untracked files. Since 0.6.1 it also compares the run branch's head and every commit reachable from it but not from the prepared baseline against that baseline, including merged side branches. Restoring a committed change in the worktree or a later commit, or checking out a different head, does not hide its paths. The audit pins the original Git object/reference directory and reads through isolated metadata without agent-writable configuration, replacement refs, grafts, attributes or external diff drivers. Missing history or an unreadable reference makes it incomplete.

`permittedChanges` means relative paths/directories or globs; prose is not a wildcard. FIX-18 requires at least one interpretable pattern before intent. Bare patterns contain no whitespace; `path:` permits literal names with spaces, for example `path:docs/operator guide.md`. Prose can accompany a pattern, but absolute paths, parent traversal and shell expansion do not grant permission. The audit reports outside paths and incomplete observations. It is not isolation, cannot identify who made a concurrent change, and cannot see uncommitted changes reverted before settlement or commits no longer reachable from the run branch. Git administration itself and files outside the workspace are excluded; submodules require separate auditing. Each inventory has a 30-second deadline, a 100,000-file and 1 GiB content limit. Committed history shares the settlement inventory's deadline, with at most 10,000 new commits and 100,000 distinct paths and bounded metadata and Git output. Pattern comparison has its own 30-second deadline. A `problem` means counts may be partial, even when `outsideCount` is zero. Export rejects links and reparse points inside `.vericairn`; this protects exported worker files, not the rest of the user's filesystem. `software.md` describes these mechanisms in full.

FIX-10 makes path matching deterministic, ordinal and case-sensitive on every platform. `**/` matches zero or more directories. Dynamic programming removes regex timeout ambiguity; it does not remove the audit's overall time, file, history or byte bounds.

## Pairing and the local-network profile

The listener is off by default and needs an explicit address, restart, network reachability and an administrator-issued pairing code. There is no discovery, relay or firewall configuration. Public/wildcard binding requires `--allow-public`. `--no-loopback` does not disable a configured local-network listener. [Remote](remote.md), the phone companion, connects through this profile only; the relay that would reach a runtime from elsewhere is a design.

- Pairing codes contain 16 secret characters (80 bits), displayed in four groups, valid ten minutes for one device, with, from 0.9.0, that phone's presence device in the same exchange; five failed confirmations invalidate a code. Pairing is not a password-authenticated key exchange: a man-in-the-middle who obtains a hello can test guesses offline, which is why the code has that entropy. Whoever learns a code can consume its one admission. Inspect the device list and revoke an unwanted device.
- Since 0.6.1 a code retains its issuing device, admission revision and the operator-role revision or `administer` grant used at issuance. Loss of admission, or revocation or expiry of that authority, invalidates the code and its open exchanges. Readmission or restored authority does not revive it; another device or grant cannot rescue it. A rename, key rotation retaining admission, or authority revision retaining its scope without an expiry gap leaves it valid. The runtime rechecks the issuer under the estate write lock before writing either pairing record. This corrects code revocation, not the interruption limits below.
- The local-network transport uses mutual TLS 1.2, with runtime-identity proof bound to that connection and signed requests bound to the runtime identity. TLS 1.2 exposes handshake certificates and signatures to observers; application messages are encrypted. A network observer with the device records can correlate a connecting device from that signature.
- Loopback now checks signing time and the device's current admission before reading a body, as the local-network profile already does. Headers alone do not prove possession of the key; signature verification follows the bounded read, with freshness and admission checked again. Device-signed bodies are capped at 8 MiB (8,388,608 bytes), or Kestrel's lower configured limit, and a 30-second read deadline, shortened when the signature expires sooner. Body-size refusal is `413`; timeout is `401`. Local-socket bodies retain the configured limit (30,000,000 bytes by default); pairing messages are capped at 16 KiB. Refusals complete before bounded discard of at most 64 KiB for ten seconds. This bounds work; it is not a denial-of-service guarantee.
- FIX-09 admits at most eight concurrent loopback body reads awaiting signature verification across all devices, with no waiting queue (`503` when full). Kestrel limits concurrent connections to 32. Neither bound claims that the service is immune to denial of service.
- Request freshness allows five minutes of age and at most five seconds of clock lead, and refuses signing times before the runtime instance's startup cutoff. Replay protection holds within one runtime instance: its process-local replay cache is lost on restart. The startup cutoff rejects older signatures, but a request signed ahead of the clock within the permitted five-second lead can be replayed after a restart before its signing time. Clock synchronization matters; there is no durable exactly-once guarantee across restarts or clock rollback.
- Admission, current key and local-network permission are checked per request and again under the write lock before a write. From 0.9.2 the per-request check reads a model kept for the estate's records and read again only when they change, so a request does not reload the estate before its signature is checked; for a repository-synced estate, each signed request still reads the record files' names, sizes and modification times to see whether they changed, and for a local-only estate it asks the live store, briefly under the runtime's lock. Revoking a device, rotating its key or withdrawing the profile invalidates an authenticated request that has not yet written. Closing a connection or deleting local configuration revokes nothing.
- A paired key signs requests and client certificates. Requested records are signed by the same principal's admitted key on the runtime's machine; a presence-required key is refused for such a write. A device-signed record is signed by the paired device itself or, from 0.9.0, by its presence device; the write ledger names the requesting device. With no local key, optional-signature policy can permit unsigned records; required signatures refuse them. The local write ledger names the requesting device and survives its revocation; it is machine state, not a signed estate record.
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

An export carries the estate repository only: records, native objects and documents. A payload held in an
external store, and a release's files, are not in it, and the export doesn't list their references. Restored
records name such a payload; they don't recover its bytes. From 0.9.2, [Large private artifacts](large-artifacts.md)
describes the store's own backup and the readback verification that a cold recovery needs. A typed custody
descriptor and an export list of external references are planned (DESIGN-42).

From 0.9.4 (FIX-135), an export carries the owner's agent instruction files at the root, each one present as a file, and the
restore writes them back with their bytes and mode. An estate where a link, a FIFO or anything else that is not a regular file
stands in place of one is refused by `estate export`, which writes nothing; an export written before 0.9.4 lacks them, so keep them with the supplemental capture
while you keep such an export. An export leaves out the machine-local housekeeping that native operations write under
`.vericairn/`, on purpose. No command lists a restored copy's files by class; the large private artifacts guide lists that
housekeeping exactly and shows the [classification](large-artifacts.md#the-three-inventories-of-a-cold-recovery) as a
tested example.

An export carries no permission beyond the executable mode: its inventory records `100755` for an executable file and `100644`
for every other file. On Linux and macOS a restore creates files and directories under the umask of the shell that runs it, so a
file kept at `0600` restores as `0644` under `umask 022`; on Windows it applies no mode, and each file takes the access control
its folder passes on. Exact permissions need a mode inventory of your own, protected apart from the export and applied after the
restore; [the operator guide](operator-guide.md#10-export-restore-recovery-and-divergence) shows the umask's effect with a worked
example.

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

On a desktop, only Secure Enclave custody enforces interactive presence, for a person's device. From 0.9.0 a phone's presence key, made in an iPhone's Secure Enclave with user presence or in Android Keystore with per-use authentication on Android 11 or later, enforces it too; the phone's request key, the iOS simulator's owner-only file and earlier Android versions do not, and claim none. Presence on a real phone is recorded as qualified only when a platform qualification observes it on a device. Windows CNG has no presence prompt in this implementation; agents and services never claim presence. Automated enclave tests use keys without presence and do not qualify Touch ID, password prompts or behavior with a locked screen. A presence prompt that expires, after the 300 seconds Vericairn waits for the `vericairn-enclave` helper, and one the person cancels end with the same refusal, so a person tells them apart by the time it took and by what they did; an authenticated prompt is no receipt that the record was stored. [Enrollment troubleshooting](enrollment-troubleshooting.md) reconciles such an ending from what the estate holds, and its examples run with a recording stand-in for custody, not the macOS dialog. From 0.9.4 (DOCS-45) it also covers a signed command that fails at once because the process that ran it cannot reach the Secure Enclave, such as a sandboxed agent process: from 0.9.2 the command prints the same refusal as an expired or refused prompt, since the runtime describes a signing failure by its kind and not by the helper's message, so `vericairn doctor`, run in the same context, tells them apart. Its availability probe describes the context it ran in, not the machine, and it neither signs nor asks for presence. That example also runs with the recording stand-in, which fails as the helper does; no sandbox is exercised. CNG and Secure Enclave keys are non-exportable through these stores; an unlocked secret service or a file key remains reachable by the same operating-system user. File permissions are owner-only on Unix; Windows files inherit their parent directory ACLs. From 0.9.2 this includes the Vericairn home and each estate's live store, also when no daemon runs. Before 0.9.2, on Linux, a command run without a daemon created the home and the live store with the process's default permissions, so another local user could read the live store's record bytes; a daemon narrowed only the live store's directory, from 0.6.1. Upgrading and running any command that opens the estate narrows them. A read-only check changes nothing. A live store that belongs to another user is refused. On a filesystem without Unix permissions, such as FAT, exFAT, NTFS through FUSE, some network mounts or WSL's `/mnt`, permissions cannot be restricted: the state is used with a warning on standard error, in `daemon.log` and from `doctor`, and other users of the machine may be able to read it.

**Presence sessions (0.9.4).** A presence check may cover an operator action's announced list (a presence session; the
[operator's guide](operator-guide.md#presence-sessions-094) states the guarantee). Sessions open only with a release build's helper
signed with Vericairn's Developer ID and the hardened runtime, checked against its designated requirement before each session, on a
macOS major version the release lists as qualified. The signing is planned with `DIST-21`, and a release lists a macOS version only once
its candidate's signed helper passes the [qualification](presence-session-qualification.md) on a real Mac; until a release does, the list
is empty and every signature keeps its own check on every Mac. A development build, a build from source and a release on an unlisted
macOS version keep one check per signature, and a development build honors `VERICAIRN_ENCLAVE_HELPER`, which a release build ignores.
The helper is started with an empty environment and no inherited descriptor but its three standard streams. Release builds start the
runtime processes the command line launches (the daemon and its launchd or systemd service) with the .NET diagnostics channel off
(`DOTNET_EnableDiagnostics=0`), which .NET reads only from a process's environment at its start, never from its runtime configuration
file. The command line itself, which hosts the embedded runtime and holds a presence session whenever no daemon runs, keeps the channel
its own environment gives it: it does not restart itself to close it. Set `DOTNET_EnableDiagnostics=0` (or
`COMPlus_EnableDiagnostics=0`) in the environment that starts it to close the channel there; `DOTNET_EnableDiagnostics_IPC=0` closes the
diagnostic socket alone and leaves the debugger's channel. `doctor` says which of these closed it in its own process, or that it is on;
it cannot see a later command's or a running daemon's environment. This narrows, without closing, injection into the runtime by a
process of the same user. From 0.9.4 the closed vocabulary of presence session actions holds `operator act` (decision `01M8001M0T9XRGF7SKHW3B45KF`); a release
helper that does not know a name refuses its list, and a release's helper and runtime ship together. The qualification of 0.9.4's
candidate helper exercises it (docs/presence-session-qualification.md, point 3).

**The same-user limit.** Nothing binds a presence key to the helper's code. Any process running as your operating-system user can load
the key's enclave-wrapped blob in its own process, write its own dialog text (a fingerprint copied from the notices included) and, after
one approval of that dialog, sign anything, without limit. This was true of one check per signature up to 0.9.3 and stays true with
sessions; the only control is refusing a dialog you did not expect. Binding presence keys to the release helper's code identity is
future work, which needs keys in the data protection keychain under an access group only Vericairn's team can use and a rotation of
every presence device.

The residual risks of presence sessions are these:

- Code running as you can use a presence key after one approval of a dialog it wrote, as it can today, and nothing in this release or
  the one before it binds the key to the helper.
- A runtime with a defect, or with a compromise confined to its own logic, can sign false mechanics facts within a listed kind without a
  dialog per record: an observation stating that checks passed, a release checkpoint's inventory, a candidate's artifacts. The helper
  cannot verify them.
- A mistaken approval of an action an agent running as you requested on the local socket yields up to 16 records you judge by exact hash
  and did not read, where one check per signature yields one named record. The runtime sees only agent runs its own worker started; it
  cannot see a coding agent you run by hand under your user, and there per-signature presence is the setting for it.
- The per-signature settings are not enforced by the hardware: the owner's setting is kept in your machine state, which a process of
  your user can change, and the charter's `policies.presence` is honored by every runtime except a compromised one. One that cannot be
  read counts as per-signature.
- Where the command line hosts the runtime, the list and fingerprint it prints are computed in the same process as the runtime, so the
  terminal is not independent of it; the dialog's own text is what stays.
- Context reuse, binding to one process and the end at lock and sleep are platform behavior, qualified per macOS release and never
  assumed.

The runtime cannot see an agent it did not start; the same-user agent condition covers only runs this machine's worker started, and a
run whose state cannot be listed or read counts as under way. The runtime keeps a ledger of its sessions, `presence-sessions.jsonl`
beside the estate's live store, owner-only: a diagnostic, never a record, and in no export or support bundle; a process of the same
user could alter it. Automated tests drive sessions with an in-process stand-in for the helper on all three platforms and the helper's
test build on macOS with keys without presence; the Developer ID signature, the dialog as shown, context reuse, and the ends at lock,
sleep and user switching are shown only by the qualification of a release candidate's signed helper on a real Mac.

In the first-party estate, decision `01M51CDST3GS2CDFXD22Z3NKJP` carried the limitation of procedural, rather than enforced, separation of the Reviewer principal's key from the builder's into 0.6.1, returning it to CRED-01 and ID-03. This documentation work establishes no new credential isolation. Separate principals and review records alone do not establish operating-system credential isolation.

Upgrading moves no key. Format-1 estates cannot name Secure Enclave custody or presence until their move to format 2. Admissions and revocations at format 2 or 3 require signatures or an anchored unsigned admission from the signed migration. The migration lists all unsigned admissions, including superseded ones and revocations, without rewriting any record. In an estate that has not adopted history checkpoints, a signature policy still uses the date stated by the record, and record removal is visible in repository history or `recover compare`, not necessarily in `estate check`. A potentially leaked key is revoked, not merely renamed or rotated. Recovery keys need separate offline custody; exports carry no private keys.

**History checkpoints (0.9.2).** In an estate that holds a counting `history-checkpoint`, removing or altering a witnessed record is an error on every copy, and under required signatures an unsigned record counts only when the root operator or a person holding `administer` anchors it, whatever its date ([operator's guide](operator-guide.md#history-checkpoints-092)). What remains:

- A fresh machine facing a rewritten history with no reference from elsewhere cannot detect that the newest checkpoints were removed together with the records after them; `estate check --expect <checkpoint>` checks a copy against a head known elsewhere. A machine that checked the estate before remembers the newest checkpoint it verified and warns `history.rolled-back` when the main line no longer holds it, but only for the clone it checked: the memory is kept per clone directory, beside its live store, so a clone in another directory or another machine starts with none, while a fresh clone into the same directory keeps it, and a clone whose main line cannot be found (a local branch the origin's `HEAD` names, `main` or `master`) reports the check unavailable. While it warns, that clone neither adopts nor submits a release; when an intended rewrite left no checkpoint that `estate check --expect` could name, only a fresh clone in another directory, which remembers nothing, adopts again. Anyone who can change the user's Vericairn home can remove the memory.
- Records written after the newest checkpoint, the unwitnessed tail, have no protection beyond `history.deleted-in-git`, a check-time warning that a shallow clone reports unavailable and that sees only what the clone's git history holds.
- A witnessed record that is removed is absent, and the copy is read without it: a removed revocation leaves its device admitted on that copy, bounded only by a closing of that device or of its key. While `history.removed` stands, the runtime refuses every write that would leave it standing and release submission waits, so the removal cannot pass unnoticed, but what the copy reads meanwhile is what is present. An acknowledgement settles the error and keeps that reading: the estate goes on without the record, and a record put back under its id afterwards is part of the unwitnessed tail and counts by its own rules.
- A record's date remains its author's claim. A checkpoint questions only a signed record it witnesses, other than one the adoption witnesses, dated more than five minutes after the earliest-dated counting checkpoint that witnesses it, with the warning `history.dated-after-witness`, which refuses nothing; a signed record in the unwitnessed tail may state a time before the newest checkpoint, and is read at that time unless a closing refuses it. An anchor exempts the record it lists from the signature requirement, from a closing's refusal except for a device revision, and from `history.altered` when only authors who may not anchor list other bytes for it, without authenticating its author or its date; every other rule still judges it.
- **Closings (0.9.2).** A closing bounds what a revoked device, a replaced key or a narrowed grant still accounts for, and roles and grants are read as they stood at a record's time, only in an estate that has adopted checkpoints ([operator's guide](operator-guide.md#closings-and-authority-at-a-records-time-092)). A key may have been used while it was trusted, and a clone that lacks a closing can witness records signed after it until it receives the closing; those records are refused on every copy that holds it. Without a closing, a revoked device's record dated before its revocation still counts.
- From 0.9.4 (ID-14), in an estate that has adopted checkpoints, removing the operator role from a person's principal record writes the person's role closing in the same batch, and a removal without it is refused; the closing refuses everything the person writes afterwards that it does not witness, at any date and whatever device signs it, in every read and for every closing, even if they are given a role back. Removing any other role writes no closing, and that role is read at each record's time, so a person who loses only a lesser role can still write records dated into the time it stood, read as it stood (decision 01M7JSJSHTTX86F10007GDQ238). Only the root operator or another standing operator signs a role closing, so a person holding `administer` but not the operator role writes none. In an estate that has not adopted, and for a removal written without a role closing (`history.closing-missing`), a person whose operator role is removed, or whose `administer` grant is narrowed without a closing, can still write records dated into the time it stood, which read as it stood, apart from policy authority and from principal and authority records written through the runtime, which takes no stated time for them; close and revoke that person's devices when removing the role there. Role closings bound a person's records, not an agent's or a service's: removing a role from an agent's record writes none, and an agent's authority is bounded by grant closings and device revocation. Anchoring, acknowledging a removal, rooting an adoption and signing a device's or a grant's closing are read strictly: a later narrowing, revocation or closing of the grant, or removal of the role, withdraws them for every checkpoint, earlier ones included, except, from 0.9.4, what the person's own role closing witnesses: their closings, appointments and grants from before it. Those acts read a grant or principal revision as written only when the root operator, or an operator who held the role at its time and still holds it, wrote it; one whose author had been given the role but had lost it or no longer holds it reads as a removal, and one whose author was never given the role is not read. A revision read as a removal reads as written, except that it gives no operator role or `administer`, and that on top of a revision read below it, it describes a human only where that revision does too: so one saying the person is not human takes that away, and one with no revision read below it, such as the first record of a person's line, describes a human when it says so. A revision a closing refuses is not read for anchoring, for the adoption or for the closings that follow that closing.
- Without a role closing, removing an operator's role takes, for those acts, what that person appointed, granted and closed, and `history.revision-author` warns on each such grant or appointment; those the person appointed keep the operator role in ordinary reads, and a removal they write still counts for those acts, until the root operator writes their appointments again or closes their devices. From 0.9.4 a role closing keeps what the person appointed, granted and closed before it, the records it witnesses, for anchoring, acknowledging a removal, rooting an adoption and the closings, and keeps a closing's force when its author is removed later with their own role closing. A removal written without a role closing still withdraws its author's closings from those acts, since without one a closing the removed person signs on a copy that does not hold the removal could not be told from one signed before it. An operator whose own role rests on the person's appointment cannot close that person's role with its removal, since no closing is read against itself.
- Policy authority counts a person's records as an operator's after their role is removed only where a checkpoint by the root operator or a standing operator, before the removal, lists them or follows a checkpoint that lists them. In an estate that has adopted checkpoints, the runtime writes such a checkpoint, following every checkpoint before it, before it writes a revision that takes the role away. A record listed only on a fork beside the removal, or by a checkpoint written after it, is not placed before it, whoever signs a later checkpoint. A removal written with no such checkpoint after an adoption that a person holding `administer` signed leaves the records that adoption witnesses counting for nothing, each the warning `policy.operator-removed`. In an estate that has not adopted, the runtime refuses a removal that would make any record fail. A removal written otherwise there leaves the person's policy records, and those resting on their grants or appointments, counting for nothing, each the warning `policy.operator-removed`, and records that read roles as they are now in such an estate, a decision among them, fail; adopting checkpoints after the removal does not bring them back.
- No closing is read against itself, since its author chooses what it witnesses: otherwise a removed operator could close the remover's device on a copy from before the removal. So only a closing by the root operator keeps a removed operator's later revisions, whatever their date, from taking another operator's standing, and only for the closings that follow it, the adoption and anchoring; a closing written before it is judged without it. Once the root operator has removed a person and closed and revoked their device, that person can therefore still write, at any time and repeatedly, a removal of an operator other than the root operator or a narrowing of an administrator's grant, dated any time from their own appointment until their device's revocation: every closing that does not follow the root operator's then closes nothing if that operator or administrator wrote it, or anyone whose operator role or `administer` rests on that operator's appointments or grants, directly or through others', and records it alone refused count again. Closing again does not undo it, since the new closing witnesses those records. Removing those records does, with an anchoring checkpoint that acknowledges their removal when a checkpoint witnesses them; writing the operator's role again gives back the closings of those whose role or `administer` rests on that operator's appointments or grants, and the operator's own earlier closings only when it is dated after the removal and before them. Either holds only until the person writes more such records, among them a removal on top of the role written again or forking the line below it; the root operator's own closings cannot be reopened this way; from 0.9.4 a role closing ends it: once the person's role is closed, nothing they write afterwards, at any date, takes another operator's standing or voids a closing, those written before the role closing included, because the role closings that act are read when every other closing is judged. Another operator's closing of the removed operator's device closes nothing once that person writes a removal of its author dated any time from their own appointment on; the root operator settles it by closing the device and writing the role again. Two operators other than the root operator who remove each other both lose those acts, a closing either writes of the other's device included, until the root operator settles it.
- A grant closing refuses every record of the grantee dated while a closed revision was in force, not only those that needed the grant; the operator anchors any that stand after reading them.
- Concurrent closings, neither following the other, each act, and a closing may follow any checkpoint back to the adoption. So whoever holds the closed key of an operator's or an administrator's device can still write a closing on such a fork that closes another device, which then signs nothing that counts. Closings only refuse; none makes a record count. Removing the forked closing ends it, with an anchoring checkpoint acknowledging the removal when a checkpoint witnesses it.
- A key closing bounds the replaced key but cannot tell its owner from whoever else holds it: the lowest-id rotation replacing that key is the one the device follows. When a key may have leaked, close and revoke its device instead of rotating the key.
- Closings are judged with the format-2 migration's anchors alone: a closing signed by a device admitted only through an anchoring checkpoint closes nothing (`history.closing-author`), so write closings from devices admitted by signed admissions.
- In an estate that has adopted, the runtime writes each revocation, key rotation and narrowing grant revision after its closing, in the same batch, signed by the key that performs the act, and refuses the act, writing nothing, when the closing cannot be written or would close nothing. So an agent holding `administer` revokes no device there, and an act presigned on a phone, signed by a device-signed request or recorded through `operator record`, none of which can carry a closing, is refused; revoke on a machine that holds a device of one who may, or with the operator's recovery key. A grant revision that adds a condition, reserves another action or changes a policy scope's bounds in any way is read as narrowing and carries a closing, whatever else it widens. An act stated more than five minutes before what its closing would witness is refused; write it at the present time.
- Only the runtime's own stores require a closing. A revocation, rotation or narrowing that arrives as a file without one, merged from a copy that had not adopted yet or added by hand, takes effect, and a record its device, key or grantee signs with an earlier date still counts; `estate check` warns `history.closing-missing` until a closing names that target, which `records new history-checkpoint` writes.
- A batch that stops after a closing and before its act leaves the closing when it cannot be removed again, or when the process stopped between the two writes: it closes all the same, so the closed device or key signs nothing that counts until the act is written again by its author, which then follows that closing alone.
- Adoption's check lists the records that read otherwise at their own time by comparing the records' findings under both readings. A reading that is no finding, such as which acceptances a release counts or who may take a step, is not listed. The list is advisory: the plan hash covers only the anchored set, and adoption computes the list again from the copy it signs, so the adoption's list can differ from the check's under the same plan hash.
- An estate that has not adopted keeps the fixed boundary of the current required charter revision. A new estate adopts at birth, with its root operator's first device; an existing one adopts only when the root operator or a person holding `administer` runs `estate checkpoint --adopt`, which `operator list` shows as waiting only in an estate pinned in release mode to a version after 0.9.1. An adoption checkpoint anchors at most about 9,000 unsigned records within the 1 MiB record limit; an estate with more unsigned records that count cannot adopt in one checkpoint.
- The runtime writes checkpoints at upgrades, release submissions, charter revisions and intervals only in an estate that has adopted, and only with a device of the acting principal held on its own machine. The interval trigger rides on the next single record write that this machine signs for its author with a device needing no person's presence; records stored together in one batch by other operations, device-signed requests over network MCP and writes with a recovery key carry none, so in an estate where only people with presence write, the tail grows until another trigger or `estate checkpoint`. A charter revision moving to `required` anchors the unsigned records that count before it only when the root operator or a person holding `administer` writes it; any other author's such revision is refused while unsigned records would stop counting.
- Checkpoints forked by an admitted principal are outside this model; each fork is signed and attributable.
- A checkpoint counts only on an intact path of counting checkpoints back to the adoption checkpoint. A copy from which a checkpoint on that path was removed reports `history.checkpoint-missing`, an error that holds writes and release submission, and witnesses only what the intact part of the graph lists until the checkpoint is restored; a copy without its adoption checkpoint reads, meanwhile, under the earlier boundary. `estate check --expect` against a head known elsewhere shows such a copy.
- Any admitted device, an agent's included, may sign a checkpoint. Its listing of a record that is not here is `history.removed`, an error until the record is restored or the root operator or a person holding administer acknowledges the listed version's removal. Its listing of other bytes for a record that the root operator or a person holding administer signed, or that is anchored with its present bytes, is only the warning `history.listing-mismatch`, and the record keeps counting. For any other record, one an agent signed included, such a listing is `history.altered` and the record counts for nothing: a signer that rewrites its own witnessed record under the same id, even with its own key, is caught, and a later checkpoint does not witness the new bytes.
- From 0.9.2, in every estate, a record that does not count is left out of every rule that admits, authorizes, accepts, clears or holds. A record whose signature does not verify grants nothing, an admission counts only when it rests on the estate's first authority through admissions that count, does not unseat what it rests on, and its author is an operator or a person holding `administer` at its time, and an unsigned device revision changes no key or name.
- The manifest is not a record and is not signed. Before an estate adopts history checkpoints, the root operator is read from the charter the manifest names and the first authority from its `firstAuthority`, so a copy whose records and manifest can both be edited can be pointed at another charter or another self-admission; the estate check then reports the extra record (`estate.multiple`, `signature.self-admission`). After adoption, an edited manifest, whichever of its keys it changes and whatever records come with it, leaves the copy unsettled (`history.root-ambiguous`, `history.manifest-root`), with no record counting, instead of moving the root of trust.
- Added records that bring a charter or a self-admission of the root operator's device with history checkpoints of their own, signed by a device they admit themselves, never take the estate's root operator or first authority, whether or not the estate has adopted; they can leave it unsettled (`history.root-ambiguous`), with no record counting, until they are removed. `estate check --expect` against a head known elsewhere tells which history is the estate's.
- An estate whose manifest names no first authority while it holds more than one self-admission of the root operator's device, stray or forged, reads `manifest.first-authority` with no device admitted, and `vericairn estate upgrade` refuses it, as it refuses any estate with errors. From 0.9.3, `vericairn estate first-authority` names the first device's self-admission in the manifest's `firstAuthority`, showing the change and its plan hash first; removing every other one from the copy, which otherwise still reads `signature.self-admission`, or `signature.invalid` for a forged one that the command's `remaining` does not list, stays the operator's own act, and no command does it. The command refuses where history checkpoints speak to the first authority, so a copy whose own history witnesses a self-admission first is compared with a head known elsewhere before anything is named. From 0.9.4 (FIX-134), a self-admission whose signature does not verify under its device's key at its stated time is never the first authority, whether the manifest names it or it is the only one: the copy reads `manifest.first-authority` naming it and nothing signed under it counts until the manifest names one that verifies. In an estate that has adopted, a manifest naming such an admission instead of the one its checkpoints witnessed reads unsettled, as for any other admission history does not confirm, with that finding beside `history.root-ambiguous`.
- Before an estate adopts history checkpoints, an unsigned principal or grant record dated no later than the charter first required signatures counts, for who may admit devices as for every other rule, and a stated date is only a claim, so before adoption who may admit rests in part on records that anyone able to add records can write. An admission that counts only through such a record is the warning `admission.author-unanchored`. Adopting checkpoints closes this: from adoption on such a record counts only when a checkpoint anchors it. Whether a holder of `administer` is a person is read without such records before adoption too.

FIX-13 orders generated identifiers within one process, including concurrent calls and repeated or
backward clock readings. Independent processes, machines and explicitly supplied ids have no such order.
An exact hash-pinned result acceptance establishes causality; other judgments use recorded time and id
tie-breaking. This changes no historical record bytes and is not a distributed clock.

FIX-12's credential-pattern exemption is limited to validated device `body.publicKey` and envelope `signatures[].signature` values in record JSON. A key must decode as a P-256 SubjectPublicKeyInfo key; a signature must decode to 64 bytes. This validates the encoding for the scan, not the signature's cryptographic truth. Other fields, nested lookalike names, invalid encodings and non-record files remain scanned. The machine-path scan reads the original text and signature verification still applies; no record bytes change.

## Workbench, MCP and living documents

Workbench's Goals workspace, asks and progress are read views. It has no goal activation/acceptance, worker launch/stop or pairing form. Headless journeys cover the work loop, releases, Documents, Goals and progress. They do not establish native desktop, screen-reader or interactive-presence qualification; consult the estate's qualification records for the observed cells. There is no claim here of a new native qualification for 0.9.4. Projects are not a record type. Record comparison is a line difference of canonical JSON, and docking is panel placement with keyboard alternatives. The probe has no direct open option for every workspace and can run before a newly started daemon has supplied a page; start the daemon first.

FIX-13 reduces Workbench's first-screen work by sharing status and progress observations, avoiding a
duplicate refresh after daemon startup and reusing parsing and hashes for unchanged original bytes.
First-screen API reads no longer grow with unshown record history, but opening still validates every
record once. Large estates can still take time. Wait for loading before treating an absent page as empty;
headless tests establish no fixed native startup-time bound or desktop responsiveness qualification.

MCP supports protocol revisions 2025-06-18, 2025-03-26 and 2024-11-05 over stdio, with `initialize`, `ping`, `tools/list`, `tools/call`, `resources/list`, `resources/templates/list`, `resources/read`, `prompts/list` and `prompts/get`, and from 0.9.2, with a running daemon, `resources/subscribe` and `resources/unsubscribe` ([MCP subscription limits](#mcp-subscription-limits-092)). Resources and prompts are read-only. Sampling is not implemented; from 0.9.2 the daemon also serves Streamable HTTP ([Network MCP limits](#network-mcp-limits-092)). The contributor writes are `work_attempt`, `work_progress`, `work_result`, `work_amend` and `document_review`. From 0.8.0 it also exposes bounded policy and writing-exception writes after authority checks, and `work_review` only to an admitted operator connection. The runtime checks current authority and exact bounds again at each call. Communication and context tools read; they do not change preferences or grant authority. Goal and ask tools do not activate, fund, accept, step or answer. There is no harness-qualification or worker-launch MCP tool. The orientation's observed MCP harness matrix is distinct from RUN-03's native worker qualification.

Workbench adds local communication and hand-over/resume views. It has no context-allocation editor, pinned-original retrieval control or harness-qualification form. The communication qualification report covers headless fixture behavior, not native desktop, screen-reader, live harness, Remote or compaction qualification. Optional progress frequency changes presentation, while required attention remains visible; an external agent's compliance with the brief's wording and refresh instruction is not enforced.

Living-document checks depend on local covered files, registered external clones and installed contributions. Missing contributions can make a document unavailable and hold its product's release. The software pack checks exact retained references and companions and evaluates reference coverage. It has no semantic notices/README checks or automatic declaration proposals. The existing retirement path can retire proved generated ownership; it refuses unknown or unexpected members. Workbench does not show a document's content beside its triggers. The normal progress report includes repository checks; its generated region excludes them to avoid reading itself.

From 0.9.4 (FIX-139), `estate check` reads the owner's agent instruction files at the root by a fixed list of shapes, over the whole text so that a key, its colon and its value may sit on separate lines, and in time linear in the file's length: a hooks key, table or event name, an editor task that runs when a folder opens and a Git hooks path; a key, flag, mode or setting that approves without asking; and a secret-named key with a literal value, a bearer or basic credential, a password in a URL or after `-u`, or a long run of token characters mixing cases and digits. It understands no agent tool's configuration as a whole, so a hook, an approval or a secret written in a form it does not know goes unreported, and an ordinary line can match a shape. A key counts only at the start of a line or in quotes. A sentence that opens by forbidding ("Never", "Do not", "Avoid" and the like, followed by a space, at the start of a sentence: after a blank line, an ATX heading, a list marker or a line that ends with a sentence mark) is not reported for a flag, mode, policy or Git setting it names before its clause ends. The clause ends at a period, question or exclamation mark when a space, the line's end or an ASCII capital follows it, possibly after closing characters from a fixed set (`*`, `_`, a straight or curly closing quote, `)`, `]`, a backtick); at a colon or semicolon also before any ASCII letter; and at one of a fixed list of turning words such as "without", "forget" or "by". Everything else continues it: text glued onto the mark (a code span, a lower-case word, an opening bracket, a `--flag`), an ellipsis character, guillemets or another quote style, a footnote marker, a comma, a dash or "and", and a double negative outside the list; an author can hide a mention that way. It also over-reports a forbidding line straight after a bold pseudo-heading, a setext heading, a thematic break, front matter, HTML or a table, and a clause holding a capitalized word glued onto a mark, such as `.NET`. Its warnings refuse nothing, and a failure of the check on one file is that file's `instructions.unreadable` warning, never a failure of the whole `estate check`. A repository's own instruction files, at the root of a clone registered on this machine and named in any case, or a link of such a name, are reported as untrusted input without being read; no record marks such a file reviewed yet, so the warning stays while the file is there, and a machine without a registered clone reports nothing for that repository.

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

From 0.9.4 (FIX-163), a basis that still outgrows the 8 MiB object limit, as one naming the records of an estate with many
thousands of them does, is no longer refused: it is retained in parts of the limit, named in order by a small root object
with the whole basis's length and SHA-256, and reads as before. A basis is at most 256 MiB (32 parts); a longer one is
refused with `release.basis-size` before anything is written. Its reader refuses a root past that bound, or naming parts
that do not follow from its length, before it reads any part. A client before 0.9.4 reports such a basis as unsupported.

## Release dispatch and bounded commands

FIX-15 claims `<provider>:<destination>:v<version>` under the service gate before calling the provider,
then releases the gate during provider I/O so unrelated requests can answer. A receiptless `not-started`
intent has an unknown effect and blocks another submission, including after restart or failure to store
the receipt. `release observe` records it as uncertain before querying the provider, even if observation
then waits for a run or draft. Observe and reconcile the original identity before retrying. A known
failure or cancellation needs `--reconciliation`; that retry pins its settings and records an uncertain
claim before dispatch. An active provider call cannot be canceled or reconciled until it returns.

The extra pre-dispatch revision reported for a retry of a canceled intent in 0.8.2 is fixed in 0.8.3
(FIX-57). The comparison excludes the submission's own revisions and their exact supersession links,
so an unchanged retry records one claim before dispatch and one receipt afterwards, like a first
submission. Another successor, record revision, source change or changed admission finding still
counts. Retained evidence keeps its original timestamps and digests.

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
worker signals can remain continuity concerns after the operation settles. From 0.9.0 an acknowledgement is
per-principal presentation state that clears when the notice changes; there is no dismissal or expiry state. [Required attention](required-attention.md) states the exact rules.

Canonical status JSON derives attention from records alone and is identical on machines with the same
records. Progress also reads this runtime's worker observations; human status, progress and Workbench
label additional concerns `Local worker observations`. No reader can infer another machine's unrecorded
facts. The estate observation's existing status-hash contract is unchanged; these API v1 projections add
no record fields, CLI commands or permissions.

## Estates other than Vericairn's own

Only the estate that built and observed a release holds its release record
([issue 177](https://github.com/Vericairn/vericairn/issues/177)), so through 0.8.2 an estate other than
Vericairn's own could not pin or upgrade to a published Vericairn release. This is fixed in 0.8.3 (FIX-52):
`estate upgrade --to` and `estate init --release`, run with the target release's own command line, verify
the published release's tag commit, `SHA256SUMS` and this platform's CLI archive, then pin a
`vericairn-release` observation by hash; `--from <directory>` supplies the same evidence offline. Only the
current platform's archive is verified, the builds remain unsigned developer builds, and a release record
is still never written by hand.

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

From 0.9.4 (DOCS-46) the record reference spells out the members of an assignment's brief only; other fields of fixed members,
such as an assignment's `step` and `occurrence`, are still described in prose. No command validates a proposed record without
writing it.

From 0.9.4 the policy kinds listing includes `vericairn.estate/commit-messages`, version 1, which configures the commit-message
check of checked pushes and merges and selects no records. A build before 0.9.4 keeps such a rule intact, reports it unavailable
when it evaluates the estate's policies, and checks no commit message; declare it at level `remind` while such a build still
evaluates the estate's policies for a release submission.

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
Members and Public projections, and mobile and public-site caches of companions, are outside 0.9.4.
Local inspectors cannot be invoked through companion API or MCP routes. Metadata
reads grant neither source access nor authority to act on what they render.

Strict whole generated members require complete source and byte comparison, with
no output pins or authored clearances. Existing authored regions retain their pins
and review requirements. Legacy whole-file conversion requires an exact independently
reviewed preview; an editorial revision or format upgrade cannot convert it.
Unexpected members are never silently pruned. A rendering failure stores nothing;
an interrupted publication after storing records retains guarded journal recovery
and reports the already-stored records honestly. Changed destinations or unavailable
sources can hold recovery and further writes. There is no cross-process writer lease. From 0.9.2, record
writers on one working tree take its exclusive write lock in `.vericairn/write-lock/` (FIX-99); it serializes
writers on that tree on one machine, and makes no claim across clones, machines or repository synchronization.

Retained reference inputs and generated bytes are compared against the exact local
build and candidate commits. A missing retained commit, including in a shallow clone,
is unavailable; generation and comparison fetch nothing. A documentation-only commit
can keep its build pin only while the complete build sources agree. Reference indexes
use only an explicitly selected register. No register selection means no mapping claim.
Software implementation digests bind source bytes; first-party record readers use
explicit versioned implementation identities and golden-byte fixtures.

The serialized build's companion archive is retained by CI and covered by its final
checksums. From 0.9.4 (FIX-140) it is also attached to the release, so every file
`SHA256SUMS` names can be downloaded from the release; releases up to 0.9.3 list it
without attaching it, and their archive exists only in the retained CI artifact, which
GitHub keeps for 90 days. A release provider written before 0.9.4 reports no checksum
names, and the observation then cannot refuse a draft that leaves a listed file out.
SBOM, checksum and workflow reports explain retained inputs; they
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
qualification for 0.9.4. Policies have CLI/API/SDK/MCP inspection, and [Remote](remote.md#policy-conditions-and-recurring-duties) shows policy
and recurring-duty conditions; a separate Workbench
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
0.8.4 adds `IEstateApi` members, all with default implementations, so implementations outside
Vericairn need supply none, and adds reserved placeholder package versions (FIX-72);
[API compatibility for 0.8.4](api-compatibility-0.8.4.md) lists these changes.
0.9.0 adds members to `IEstateApi` and other interfaces, all with default implementations, so
implementations outside Vericairn need supply none;
[API compatibility for 0.9.0](api-compatibility-0.9.0.md) lists these changes.
0.9.1 adds `RunPrepare` and its asynchronous counterpart to `IEstateApi`, both with default
implementations, so implementations outside Vericairn need supply none;
[API compatibility for 0.9.1](api-compatibility-0.9.1.md) lists these changes.
In 0.9.2 `IEstateApi` gained members for unfinished operator actions, events, security reports, history checkpoints,
search and search-selected context, and `ISourceAdapter` members, all with default implementations, so
implementations outside Vericairn need supply none; [API compatibility for 0.9.2](api-compatibility-0.9.2.md) lists these changes.
In 0.9.3 `IEstateApi` gained members for presence prompts and the estate's first authority, and `IDeviceKeyStore`,
`IRecordCommitAdapter` and `IPullRequestProvider` gained members, all with default implementations, so implementations outside
Vericairn need supply none; [API compatibility for 0.9.3](api-compatibility-0.9.3.md) lists these changes.
0.9.4 adds `IEstateApi` members for presence leases, standing agents and their outputs, a decision step's decision, concurrent
assignments, the operator's answers and reviewed acts, automated observers and the timed stop drill, members of
`IDeviceKeyStore`, `ISourceAdapter`, `IWorkspaceProvider` and `IAgentLauncher`, and the progress-target adapter contract, all
with default implementations where they extend an existing interface, so implementations outside Vericairn need supply none;
[API compatibility for 0.9.4](api-compatibility-0.9.4.md) lists these changes.

## Reading repositories Vericairn did not create (0.9.4)

From 0.9.4 (FIX-147), every git process Vericairn starts on a repository it did not create runs with the overrides the
[operator guide](operator-guide.md#7-repositories-and-checks-as-evidence) describes, and inspection and release admission read the
working tree through a git directory Vericairn writes. What that changes:

- **Your git configuration does not apply to inspection.** Settings that live only in your system or global git configuration are not
  used when Vericairn reads a repository it did not create. In particular:
  - `safe.directory` entries in your global configuration do not apply, so a repository owned by another account is refused, with a
    message naming the ownership; inspect it as its owner, or from a clone you own.
  - Line-ending conversion set only in the system or global configuration (Git for Windows sets `core.autocrlf=true` in its system
    configuration by default) does not apply. A working tree checked out with conversion can count files as modified when git re-reads
    them (for example after their timestamps change, or while an entry is racy just after checkout). Set `core.autocrlf` or `core.eol`
    in the repository's own configuration to keep it (see the next item).
  - A custom global excludes file (`core.excludesFile` in your global configuration) does not apply, so files it ignores count as
    untracked. The default location (`$XDG_CONFIG_HOME/git/ignore`, usually `~/.config/git/ignore`) and the repository's own
    `.gitignore` files and `.git/info/exclude` still apply. On macOS, a `.DS_Store` ignored only through a custom global file makes
    the working tree read as changed.
- **Only ten of the repository's own settings apply to the working-tree read.** Inspection and release admission carry the
  repository's `core.autocrlf`, `core.eol`, `core.ignorecase`, `core.precomposeunicode`, `core.filemode`, `core.symlinks`,
  `core.checkStat` and `core.trustctime` when their values are plain words, and `core.sparseCheckout` and `core.sparseCheckoutCone`
  when they are plain booleans, with a copy of `.git/info/sparse-checkout`. No other setting of the repository's configuration
  applies to that read. In particular:
  - a repository-level `core.excludesFile` does not apply, so files it alone ignores count as untracked;
  - a repository-level `core.attributesFile` does not apply (`.gitattributes` files and `.git/info/attributes` still do);
  - `status.renames` and `diff.renames` do not apply, so a staged rename counts as one change where git with renames turned off
    counts two (whether the tree is clean is unchanged);
  - `index.sparse` does not apply: a sparse index is expanded for the read, which is slower and gives the same result.
- **Partial clones.** No missing object is fetched. A partial clone whose working-tree read needs an object it does not hold is
  refused as unreadable, where earlier releases fetched the object from the promisor remote.
- **What a changed working tree affects.** The clean observation decides: `docs pass --before`, which refuses while the guides' clone
  has modified or untracked files; `check run` and `estate verify`, which add a limitation to their recorded result; release
  admission, which objects to a registered clone with changes (`release.clone-dirty`); the runtime's search, which pins the commit
  only when the working tree is clean; and the `clean` value of adoption and observation records. Each of the limitations above can
  therefore make one of these see a change that `git status` in your own shell does not show.
- **An unreadable working tree is refused.** When git cannot read the working tree (for example an index it cannot parse, or a
  directory it cannot open), inspection and adoption refuse with "git could not read the working tree completely" and record nothing;
  release admission reports that the registered clone cannot be inspected. Earlier releases recorded such a repository as clean. A
  directory git cannot open is recognized from git's English warning text; with a git that prints translated messages, that one case
  still reads as clean, its contents not counted.
- **The ownership message.** The refusal that names ownership appears only for inspection (release admission reports that the
  clone cannot be inspected; document reads of such a clone are unavailable), and it is recognized from git's English message text;
  with a git that prints translated messages, the refusal says only that the folder is not inside a git working tree.
- **Filter drivers do not run while reading the working tree.** Files that git re-reads are compared as stored on disk, so a file kept
  through a filter (Git LFS, for example) can count as modified when its timestamp changed. A repository whose `.git/info/exclude`,
  `.git/info/attributes` or `.git/info/sparse-checkout` is a symbolic link, is not a regular file once opened, is larger than 1 MiB,
  or cannot be read within ten seconds, is not observed.
- **Submodules are observed by their checked-out commit only.** A submodule checked out at a commit other than the recorded one counts
  as a modified entry; changes and untracked files inside a submodule's own working tree are not counted, by inspection or by the
  release admission check of registered clones. (Release admission reads no file inside a submodule.)
- **Older git.** The overrides travel as `-c` options and work with any supported git. Git older than 2.32 does not read
  `GIT_CONFIG_GLOBAL`, so your own global configuration still applies there (it is yours, not the repository's). On git older than
  2.36, `core.fsmonitor=false` names a hook command `false`, which the shell runs harmlessly. The secret scanner's own git processes
  take the overrides on their command line; only gitleaks' own git receives them through git's configuration environment, which
  needs git 2.31 or later, as checked pushes already did.

## Checked changes and operator actions

Checked pushes need an installed Gitleaks scanner, 8.25.0 or later, and complete local
history of the exact range. Aggregate scan success is no claim about every possible secret or an
unobserved future commit. From 0.8.2 the scan applies gitleaks' rules without their built-in
allowlists, so it can report a finding that gitleaks' defaults pass. A finding refuses the push,
and with it the merge; there is no per-estate allowance, so a change refused for a value that is
not a secret is pushed and merged outside the checked flow.
From 0.9.4 (FIX-150), the scan passes a history checkpoint's inventory, whose random record ids can spell a key word just
before a hash, through one allowance of Vericairn's own: a line of exactly an inventory line's shape, in a file named by 64
lower-case hexadecimal digits directly in the repository's `objects/` folder, applied only when every such file the pushed
commits add or change holds nothing but such lines. A range in which one of them mixes inventory lines with any other line is
scanned without the allowance. The allowance trusts the shape, not the checkpoint's signature: any value in an inventory
line's hash position passes, as a 64-digit hexadecimal secret written there would. That is the only position that passes a
value: the line's other positions take only a record type the estate format defines, a month, a record id and `.json`, so a
credential in any of them is found, and a line naming a record type the running release does not define is scanned as
before. Every other line and path is scanned as before, and there is still no per-estate allowance. Also from 0.9.4, a scan
whose read of the pushed history fails or stops early, at a missing, corrupt or truncated object, is incomplete and refuses
the push; before 0.9.4 gitleaks reported no finding for the part it could not read, and the scan passed. The scan reads each
merge as its patch against its first parent whatever the clone's `log.diffMerges` setting, and its git processes drop the
routing variables the push's own processes drop, such as `GIT_DIR` and `GIT_SHALLOW_FILE`, so neither a clone setting nor the
pushing process's environment can show the scan less of the history than the push carries; before 0.9.4 either could
([changes](changes.md#push-and-open)). From 0.9.4 (FIX-160) gitleaks applies only the rules and the empty ignore file the
scan writes, never a `.gitleaksignore` or `.gitleaks.toml` from the clone. Merges require exact declared check names and
every reported check successful at the expected head; forks are unsupported. From 0.9.3 a required merge queue is entered
with the pinned head, and its outcome is read from the pull request and its place in the queue. A change out of the queue
with no removal the host dates after its dispatch leaves the merge uncertain until it is reconciled; the GitHub provider
tells a failure from a removal by the host's latest removal reason, compared with a local dispatch time, so a clock far from
the host's leaves more merges uncertain. A merge commit inside a queue's group is judged by the group that landed it only
when the provider finds that group among the queue's latest twenty pushes; otherwise its target checks stay pending. A
confirmed merge with pending or failed target checks remains incomplete. gh uses the runtime user's existing configuration
and login, and so does git where `operator record` commits in the estate's own clone, with no hooks and no fsmonitor. A
checked push's git takes from the user's system and global configuration only the credential helpers and TLS trust settings
its remote access needs; from 0.9.4 (FIX-147) its reads of the clone, like every git process Vericairn starts on a repository
it inspects or adopts or on a registered clone, run without the system configuration, with an empty global configuration and
hooks path of their own, and with the repository's fsmonitor, pager and other program settings overridden
([changes](changes.md#push-and-open)). No permission, credential or publication authority is inferred from a check result.

From 0.9.4 (CODE-81), a checked push matches the message of every commit it carries that its target branch does not already
hold against the estate's blocked terms, and a checked merge the message of the merge commit it would create, at the level an
estate policy sets, `refuse` unless one sets `warn`; and every such pushed commit must carry each trailer line the estate requires.
A commit the target branch held when the push was prepared is not checked again, so a term or a missing trailer already on the
target, such as in the merge commits a checked merge creates, never refuses a branch that takes in the target. The check matches
a term whole, as records do, so a term split by other characters or spelled with substituted letters passes it. It reads commit
messages only: a term in a file's content or a tag is not refused by it, and a branch whose name holds a term is refused only
because the operation recording the push would hold it. A merge through a required merge queue cannot set its merge commit's
message: the check reads the message the queue composes with the pull request's title and description as observed when the
merge observes the pull request, so a title or description edited after that observation, before the queue builds its merge
commit, is not seen; and a provider that does not implement the merge with a message (`IPullRequestProvider.Merge` with four
arguments) merges with the host's own message. The required trailer is not required of the merge commit the host creates. While a
term or a trailer is declared, a push whose messages the adapter cannot read is refused, as is a range whose messages exceed the
adapter's 4 MiB of output. Pushes made with git directly, outside `vericairn changes`, are not checked; a guard for them in
registered clones is designed separately and planned.

From 0.9.4 (CODE-81, decision 01M7PNCMA3V2ZQG2ZK9V7NBEJK), the push adapter's git processes and the secret scan's, gitleaks' own
git included, read grafts from a file that never exists, and the scan's also ignore replace objects, the commit-graph, bitmaps and
the multi-pack-index, as the adapter's already did. Both the adapter's reads of the clone and the scan's processes run without the
system configuration and with an empty global configuration and hooks path of their own, and read the clone's own configuration
only with its fsmonitor, pager and other program settings overridden (FIX-147). Before 0.9.4 a graft in the clone could make the
fast-forward check accept a remote head that was not an ancestor, so the leased push overwrote the remote branch, and could hide a
commit of the range from the scan, the message check and the disclosure hold while the push delivered it; a clone used with
`changes push` before 0.9.4 can be checked for an `info/grafts` file, and each pushed branch compared with the head Vericairn
recorded.

The coverage of a checked push rests on the records and the remote's own refs. A commit that reaches a branch outside checked
changes is scanned by the next checked push that carries it or builds on it, not when it arrives; until then it is on the remote
unscanned. The guard that refuses such a plain push in each clone is `CODE-98`'s, in 0.9.5.

`operator record` requires repository-synced format 3, the operator's own admitted
device and a matching changes declaration. It commits exactly the signed record
and its required generated register members, verifying their paths and bytes.
Its operation receipts are written after that commit; from 0.9.3 the next operator
action in the same clone carries them into its own commit, and until then they exist
only in that clone, which status and the estate check report. Resume keeps the
original input, stored signed record, generated members and operation identity
without signing again. Known failures need reconciliation;
uncertain effects need observation before retrying. Local checkpoints belong to that
clone, and independently running services are not coordinated by one service's gate.
The queue and one-step action are absent from MCP. See [operator actions](operator-actions.md)
and [checked changes](changes.md).

## Security reports before a release (OPS-11)

From 0.9.2 the `github-security-advisories` reader lists a declared repository's security advisories through the runtime
user's own `gh` login, read-only, with the same bounded runner as the release provider: one minute per gh command. It reads
the listing page by page, 100 advisories a page and at most 30 pages, each page's answer at most 16,777,216 characters and all
of them at most 100,663,296, so a page or listing larger than that is refused rather than truncated. It reads only as an
administrator of the repository through a token whose `repo` or `repository_advisories:read` scope it can confirm, since GitHub
hides drafts and private reports from other logins; a fine-grained or app token shows no scopes and is refused. It reads only
while GitHub's core allowance keeps a fifth of its limit, and at least 100 requests, for the person's own use, and stops
before a further page once a page leaves no more than that. GitHub keeps repository advisories only for
public repositories, so a declared private repository's listing is refused and holds releases. It does not use
conditional requests or webhooks. It follows next pages only on `api.github.com`, so it reads `github.com` and not a GitHub
Enterprise Server. A repository whose
listing cannot be completed holds every release until it can; there is no override other than the operator's product
revision without the declaration.

Observations stay on the machine that read them, in its live store: another machine, or a rebuilt cache, starts with none
and reads again before a release. An advisory absent from a later complete listing is no longer observed and no longer
holds. The daemon reads every 5 minutes, and an embedded command only when it polls or checks a release, so attention on a
machine without a running daemon is as fresh as its last read and says when that was. Each advisory revision is told apart
by a hash of the whole response, so any edit to a report, its text or credits included, needs a new triage. Reports, in
`triage` or `draft` state or a state this release does not know, hold a release until triaged at their current revision, and a
closed or withdrawn advisory until triaged at any revision; a published advisory, or a closed one already triaged, is
information for a week and holds nothing, whoever changed it. A non-operator learns from a refusal only whether releases are
held. Triage is the
operator's alone until a security role exists; agents and other principals cannot record one, see one, or learn from a
refusal whether a report exists. A triage record names the advisory's identifier and repository, so an estate whose records
are public should not declare security reports. The estate's GitHub App replaces this reader in a later release, with the
App's own allowance.

## Restore points, observers and the stop drill (0.9.4)

- An upgrade's readability check (FIX-145) knows what the upgrade itself can add, the record format, the manifest's fields and the kinds of record the upgrade writes, against the release that first reads each; it does not judge records written after the upgrade, which may use what the new release added. A source pin's release is unknown, so its reading of anything added is unconfirmed, and only a known one-way upgrade needs `--one-way`. The restore point is this machine's: another clone or machine keeps none, and the export holds the clone's live store and the files beside it, not run directories, workspaces, other clones' state or keys. The way back is stated, not performed: no command reverts the repository or copies the state back, and a restore point is never removed by Vericairn once the upgrade is applied.
- Observers (OPS-20) are this machine's: their declarations and reports are kept beside the clone's live store, never recorded, so another machine, or a fresh Vericairn home, knows none of them and shows no attention for their silence. A host that runs CI elsewhere cannot report to this machine; a watcher that reads the host's runs reports for it, and reports only what the host's answer says. A report is a claim of the principal named by `--by`, which the runtime does not verify beyond its being a principal of the estate; the reporting process runs as the same user as the runtime. Silence is judged by the runtime's clock when status or attention is read, and the event journal notices it at its next derivation pass while a reader listens.
- The stop drill (OPS-21) measures the worker of the runtime's machine with a scratch run that ignores the stop it is sent, so it measures containment by escalation, the longest path; an agent that stops when asked is contained sooner, and a run another machine's worker observes is measured only by a drill there. Its times come from this machine's monotonic clock and, for the last write, from the time the scratch run wrote into its own file, to within its write interval of 50 milliseconds; a real run's effects outside its workspace, such as a push or a provider call already sent, are not observed. On Windows there is no signal a windowless process receives, so the stop file is the transmission there and containment is always by escalation. The drill runs only on the runtime's machine, over its local socket.

## Other limitations and artifacts

The generated CLI reference includes every visible option at its declaring command, including the recursive root options and `worker configure`, without trailing whitespace. The structural docs check and build comparison verify document presence, declared command, operation, type and format inventories, retained inputs and complete reference bytes; they do not judge prose or prove behavior. Source regression tests check visible-option coverage and generated whitespace.

The GitHub Releases provider runs through the runtime user's `gh` login. It identifies a dispatched run by listing recent runs and can report uncertainty when none appears within thirty seconds. It is the only shipped release provider. Verifications preserve a command's exit, duration and output; they do not parse test reports. Third-party notices and license texts remain hand-maintained beside the generated SBOM and checked against lock files. The 0.6.0 review found its SBOM named the component 0.0.0, included packages absent from archives and omitted the embedded .NET and ASP.NET Core runtimes. From 0.8.2 (CI-03) the SBOM is generated from the self-contained distributables' locked platform restores and checked to name every package they ship; from 0.8.4 the publish evidence adds the .NET and ASP.NET Core runtime and apphost packs each distributable embeds, which lock files do not hold, and the check requires every one of them; from 0.9.1 (FIX-90) its metadata component names Vericairn and the release's version, where earlier bills named 0.0.0, and the check refuses another name or version. This set establishes no complete inventory from it beyond those checks. Use the notices for bundled components and the release review's artifact observations. Live stores for clones no longer present are not listed or removed by a command.

The notes command renders the estate's records with `--from 0.9.3 --to 0.9.4`.
Its exclusive start follows 0.9.3's recorded end; `docs notes` writes the exact window to the manifest.
FIX-13 gives accepted work one entry per work order, naming the latest accepted current result in the
window by recorded time and then identifier, with its acceptance. Earlier results and attempts remain
available through `work observe`; later unaccepted results remain separate. Superseded results are omitted.
Classification uses current estate state, not a snapshot at the window's end; work-order acceptance does
not accept each result. Later imports with older timestamps can require another explicit widening.
Earlier sets and their checksums remain unchanged.

## Event journal limits (0.9.2)

The daemon's event journal (`WATCH-05`) keeps at most seven days and 100,000 events. A cursor older than
that, from another epoch or sealed for another principal answers `reset`, and the client re-reads state. A
crash or a lost `events.db` starts a new epoch. A copy of a cleanly stopped journal restored from a backup
is not detected: it continues its epoch, and a client holding a cursor from after the copy can miss events.
After such a restore, delete `events.db` beside the live store before starting the daemon. When the daemon
restarts, events derived from a time-based state that appeared and cleared while it was down, or while no
reader consumed that state, are not raised; the records behind them still raise their own events. Device
streams share 16 slots, at most four per device and eight per principal, with two more kept for the local
socket. A reset tells a client only that its cursor could not be honored; how often one comes depends on the
journal's whole volume, including events that client may not read. The embedded runtime keeps no journal.

## Network MCP limits (0.9.2)

Network MCP (`MCP-03`) serves signed mode only: every request and every record is signed by an admitted
agent's or service's device, so a client must be Vericairn-aware, through `vericairn mcp serve
--connection` or its own signing code. Token mode for OAuth-only clients, the public endpoint and its MCP
gateway, and MCP over the relay are planned for 0.9.5 (`MCP-04`, `REMOTE-10`, the relay); until then `/mcp`
is reached on loopback and on the local network only, and a device on the local network pairs there first.
A request must name the runtime by the IP address and port it listens on, or `localhost` on loopback: a
connection paired by a host name reaches `/mcp` only when that name is the address itself. Sessions live in
the daemon's memory and end when it restarts; the bridge opens a new one once, and other clients initialize
again after a `404`. A session has one server stream, which carries its resource notifications
(`MCP-05`) and keep-alive comments, and the bridge holds it for its client. Requests are answered with one
JSON body, never a stream; long outputs are returned whole. A device holds four sessions and runs four
requests at once, all devices eight, and server streams share the event streams' slots. Every
`<harness> over MCP HTTP` row is a non-claim until an estate qualifies it.

## MCP subscription limits (0.9.2)

Resource subscriptions and `events_read` (`MCP-05`) need the estate's running daemon, which keeps the event
journal; a server over the embedded runtime declares no subscriptions, and `events_read` answers that the
daemon keeps the journal. A notification is a hint: it says a subscribed resource or the listing may have
changed, carries no content, and comes at most once a second per URI and once every five seconds for the
listing, so several changes inside an interval are told once. Over standard input and output a session
follows the journal only while it holds a subscription, with one selecting long poll a round. Those polls, and
`events_read`'s waits, take the MCP servers' own four slots of the local socket, never the command line's; beyond
four sessions waiting at once they take turns of five seconds, so a notification can come up to a turn later, and
the runtime holds up to four more long connections than before, of the 32 it admits. A session holds at most 32 subscriptions, and a subscription lives
with its session: a network session's ends on DELETE, an idle hour, its device's revocation or the daemon's
restart, and a stdio session's when its input ends. Only changes the runtime observes after a subscription
count, and after a gap the journal cannot honor every subscribed resource and the listing are told once.
Context packages, operator requests and their closings, and another principal's asks are not subscribable;
operator-request events reach no notification and no `events_read` answer. What counts as a change follows the
journal's relations: a record or brief is told of records that rest on it up to eight steps away, and a document
pin of a later revision although its pinned bytes never change. Whether a harness shows notifications or needs
`events_read` is qualified per harness version by an estate's own observations; this build qualifies no
harness, and every `<harness> over MCP notifications` and `<harness> over MCP events_read` row is a non-claim
until an estate records one.

## Progress target limits (0.9.4)

- A progress target is configured on one machine, beside the estate's live store, and is no record: another machine refreshing the same target needs the same configuration, and finds the same discussion by its marker. Run one follower per target.
- Refreshes and comments are outward effects charged with every record their text carries (`--json` lists them), but no refresh or comment is stopped for its exposure until 0.9.5 (`GOAL-15`), which derives an effect's exposure and holds it for a person.
- A comment that `gh` may have added and could not confirm is never added again, and an event missed while no follower ran is covered by the refresh when following starts, not by a comment; a release's state change in that time is not commented on.
- The GitHub Discussions adapter uses the operator's own `gh` login, which must be able to create discussions in the category; it finds the target's discussion among at most 1,000 discussions of the category, refuses a text or comment over 65,536 characters, and leaves pinning to you. A public or internal repository takes the public form.
- The public form leaves out a whole work order whose text would disclose held work or carry a blocked term, a record identifier, a machine path, a credential or a contact address; it does not rewrite the line.

## Context and hand-over limits

Context packages measure exact UTF-8 payload bytes including accounting overhead. Tokens, harness framing and model fit remain unknown without a qualified local tokenizer; this build includes none and makes no network tokenization call. A context byte allocation is not a spending cap. Mandatory material must fit; optional selected sources have a complete withheld list. The remainder of the document catalogue is outside the selection, so an empty withheld list is not a whole-estate read claim. A human `work brief` keeps its full selected scope with no byte limit set; preparation and MCP briefs without an allocation withhold named optional bodies.

Pinned record retrieval verifies immutable originals. Required document originals use matching
working-tree bytes first, then FIX-27 searches the exact path in the estate repository's local Git
history for the pinned SHA-256 bytes. Their retrieval pins carry `history: local`; optional documents
and pins without that marker remain working-tree-only. Missing or shallow history and mismatched
bytes refuse with recovery guidance. Nothing fetches, checks out, substitutes a newer revision or
writes the estate. This changes original retrieval, not the context allocation or token accounting.

Hand-over retains all records in the copy, including superseded history, with the full estate pin and recorded observations. Source-file bodies, output objects and unrecorded external state are outside that scope. A byte cap refuses overflow rather than truncating. Resume compares identities, not timestamps, but cannot establish that an external provider or the world stayed unchanged. It restarts no run and grants no authority. Interim progress claims remain local; only completed phase records enter the portable history. Saved packages and canonical JSON are unchanged by communication depth. Harness compaction/recovery and Remote remain unqualified as stated above. The local efficiency adapter is built and disabled by default; 0.9.0's release security review covers it, and it runs only with the operator's explicit admission of the adapter and launcher; it does not qualify compaction or provider-conversation recovery.

The workflow is configured to produce:

| Artifact | Contents |
|---|---|
| `vericairn-0.9.4-<platform>.tar.gz` (`.zip` on Windows), six platforms | Self-contained CLI, LICENSE, NOTICE, notices and `licenses/`; macOS includes `bin/vericairn-enclave` |
| `vericairn-workbench-0.9.4-<platform>.tar.gz` (`.zip` on Windows), six platforms | Workbench with the CLI, macOS helper where applicable, and license material |
| `vericairn-0.9.4-sbom.cdx.json` | CycloneDX bill of materials of the packages and of every platform graph of the self-contained distributables |
| `vericairn-0.9.4-THIRD-PARTY-NOTICES.md` | Third-party notices |
| `vericairn-0.9.4-docs.zip` | This documentation set, including the estate-derived release notes and their recorded window |
| `vericairn-0.9.4-companions.zip` | The serialized build's companion archive, also retained by CI, attached from 0.9.4 (FIX-140) |
| `dotnet-vc.0.9.4.nupkg` | Portable .NET tool, invoked as `dotnet vc`; no Workbench or macOS helper |
| `Vericairn.0.9.4.nupkg` | Dependency-only SDK front door |
| `Vericairn.*.0.9.4.nupkg` and corresponding `.snupkg` where produced | Community libraries and portable symbols, published from these exact verified files after release publication |
| `SHA256SUMS` | Artifact SHA-256 values |

Hashes, sizes, observations and acceptance belong to the release record and its evidence. The structural documentation check verifies the set's completeness; release review and acceptance remain separate steps.
