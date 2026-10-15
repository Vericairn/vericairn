# The software journey: repositories, checks and releases

How a software product goes from a repository to a published release with its evidence as records,
intent recorded before every external effect, and publication left to the operator.

## Where each part lives

The foundation (`Vericairn.Records`, `Vericairn.Runtime`, `Vericairn.Protocol`, the daemon, the SDK, the
command line and Workbench) holds the record types, the release operations as intent then observed effect,
the provider and adapter contracts, and a registry of the implementations a host makes available. It knows
no particular product, provider or source host; a test holds that the records, runtime and protocol
assemblies reference none of the modules below. The command line, which hosts the modules, references all
three. The modules ship inside the Community distributable until packages exist (milestone M4):

| Module | Assembly | What it is |
|---|---|---|
| The git adapter (`vericairn.adapter.git`) | `Vericairn.Adapters.Git` | `GitCommandAdapter`: read-only observation of a repository over the git command; also `GitFileSource`, the software pack's living-document source `vericairn.software/git-file` |
| The GitHub provider | `Vericairn.Providers.GitHub` | `GitHubReleaseProvider`: releases built by a dispatched GitHub Actions workflow, observed as runs and drafts through the user's own GitHub CLI login |
| The Software Domain Pack (`vericairn.software`) | `Vericairn.Packs.Software` | Repositories as resources and observations, build and test checks as verification evidence, and from 0.6.0 the coding-agent launchers (Claude Code, Codex and the generic one) and git worktrees as the workspaces agents work in |

The command line is the host that registers them: being available selects nothing. A release goes through
the provider its product declares or its request names, and a provider with no implementation on the
runtime's machine is refused, never replaced by another (`vericairn release providers` lists what is
available, with the settings each declares).

## The git adapter

Contract `vericairn.adapter.git/1` (`ISourceAdapter`): read-only observation of a repository. `IsRepository`
says whether a path is inside a repository with at least one commit; `Inspect` returns the HEAD commit, the
branch, the origin remote, the root commit, whether the working tree is clean with modified and untracked
counts, the tags and the ten most recent commits, with an as-of time. The adapter never fetches, commits,
pushes or changes the index; a repository without commits is refused with the next step rather than
invented. Two implementations satisfy one conformance suite: `GitCommandAdapter` runs the git command on the
PATH (in `Vericairn.Adapters.Git`), and `RecordedSourceAdapter` (beside the contract in the runtime) replays
recorded observations for offline fixtures and tests; the recorded one qualifies no real repository.

Commands:

| Command | Does |
|---|---|
| `vericairn sources inspect <path>` | Prints the observation; records nothing |
| `vericairn sources adopt <path> --by <id> --name <n> --role <r> [--visibility] [--notes]` | Writes a `resource` (kind repository) and a first `observation`; the repository is identified by GitHub owner/name, https remote or root commit, never by the path |
| `vericairn sources observe <path> --resource <id> --by <id>` | Writes a fresh observation of an adopted repository |
| `vericairn sources clone <path> --resource <id>` | Registers this machine's clone of an adopted repository, checked against the resource's root commit or GitHub remote; the path is machine configuration beside the estate's per-machine state, never a record |

The software pack's living-document source `vericairn.software/git-file` reads a document of an adopted repository at a
commit from its registered clone, with git's own object ids at that commit as the covered paths' identity: `vericairn
docs review <path> --commit <commit>` reviews it there, and `vericairn docs living` compares its covered paths at the
clone's head. Every call is a read-only git command. The estate's records do not show who wrote such a document, so
`--independence` states the reviewer's independence of it, and a builder that states none is recorded as reviewing its
own work ([record-format-v2.md](record-format-v2.md#living-documents-due)).

## Checks as evidence

`vericairn check run --resource <id> --by <id> [--in <path>] [--role self] [--summary ...] -- <command...>`
runs the command in the repository, observes its exit code, duration and output, stores the output as an
object, and writes a `verification` of the resource at the exact commit the working tree was at, with the
platform and runtime as environment. The exit status of the command itself is in the record; `check run`
exits 0 once the record is written, because recording a failed check is the point. A check that does not
finish within `--timeout` is stopped and recorded as `unknown`.

`vericairn check observe --resource <id> --run <id> [--workflow ci.yml] [--repository <destination>] [--provider <id>] --by <id>`
asks the host for the run's status, conclusion and commit and writes a verification in the `automation`
role with the run as evidence, with the method and environment the provider reports (for GitHub,
`github-actions` on a `github-hosted` runner). The provider is the one named, else the one serving the
resource's recorded location (a repository located at `github` is served by the GitHub provider); the
destination is the one named, else the location's value. Continuous integration holds no estate key, so its
result is observed and recorded by the CLI, under the user's own login with the host; the logs are not
inspected by this command and the record says so.

In an estate at record format 2, `check run` records its verification as an observation by automation
(`evidenceClass: observation`, `observer: automation`), and `check observe` records its verification as the
provider's report (`evidenceClass: report`, `observer: provider`).

## Release providers and the product's release profile

Contract `vericairn.provider.release/1` (`IReleaseProvider`): a release is built by a dispatched build on a
ref of a destination (`Dispatch`, which returns the run id when the run could be identified), observed as a
run (`ObserveRun`) and as a draft with the checksums of its assets (`ObserveRelease`), and published by the
operator's own manual step, which `ManualPublicationCommand` names and no implementation runs. Each
implementation serves a provider id that is recorded on every operation, declares its settings with their
defaults, the setting that names the destination, and the resource locations it serves. The GitHub provider
serves `github-releases` and declares `repository` (required, the destination), `workflow` (default
`release.yml`), `ref` (default `main`) and `checksums` (default `SHA256SUMS`); it uses the GitHub CLI logged
in by the user on the runtime's machine, and Vericairn never sees or stores the token. `FakeReleaseProvider`
declares the same settings and simulates GitHub in memory for the conformance suite and the journeys. A
provider refuses before any effect (`ProviderRefusedException`, for example not logged in) or reports that it
cannot say whether an effect happened (`ProviderUncertainException`); the two are recorded differently. An
implementation also reads one asset of a release by its name (`ReadAsset`, from 0.6.0); one written for an
earlier release reads none.

A product declares how its releases are made on its own record, in an optional `release` object (read from
release 0.4.2, the first published release to read it; an estate records it only after upgrading): the
`destination` (the provider id and the settings the product fixes), the `checksums` asset, and the
`artifacts` a complete release carries, each a name with `{version}` and optionally `{platform}`, a role
(distributable, sbom, notices, documentation or other) and the platforms it is built for:

```json
{"release": {"destination": {"provider": "github-releases", "settings": {"repository": "Example/gadget"}},
             "checksums": "SHA256SUMS",
             "artifacts": [{"name": "gadget-{version}-{platform}.zip", "role": "distributable", "platforms": ["linux-x64", "win-x64"]},
                           {"name": "gadget-{version}.spdx.json", "role": "sbom"}]}}
```

A product that declares no artifacts is read by the generic rule: a platform identifier in an asset's name
gives its platform, a CycloneDX or SPDX file is the bill of materials, a notices file is the notices, a docs
archive is documentation, and a draft is complete when its checksum file names every other asset.

The release as intent, then observed effect:

1. `vericairn release submit --product <id> --version X --by <id> [--provider <id>] [--repository ...] [--ref ...] [--workflow ...] [--setting name=value]...`
   resolves the provider (the request's, else the product's declared one; neither is refused), the
   settings (the provider's defaults, then the product's declared settings, then the request's own; a
   missing required setting is refused), and writes an `operation` with status `not-started` under the
   identity `<provider>:<destination>:vX` with every setting in its target; then it dispatches and writes
   the revision that says what happened: `dispatched` with the run id, `uncertain` when no run was
   identified, or `known-failure` when the provider refused before any effect. While an operation under
   that identity is dispatched or uncertain, a second submit is refused, as it is after observed success (a
   corrective release is a new version); after a failure or a cancellation, a retry needs `--reconciliation`
   stating how the earlier outcome was settled. While a living document of the product is due at level
   `refuse`, or is unavailable while it declares `refuse`, the submission is refused before the intent,
   naming every such document ([record-format-v2.md](record-format-v2.md#living-documents-due)). Where a
   clone of its repository is registered here, each document of the product in another repository that
   declares a `refuse` level and was cleared by a review is also read at the commit the `ref` setting names
   in that clone. Nothing is fetched, so bring the clone to the commit the build will use. Bytes that differ
   from its latest review refuse the submission with both commits and hashes, and the intent records what
   was compared, or that a document could not be.
2. `vericairn release observe --version X --by <id> [--product <id>] [--provider <id> --repository <destination>] [--qualified rid]... [--limitation text]... [--signing ...]`
   finds the operation (by provider and destination, by product and version, or by a destination one
   current operation alone matches) and observes the run and the draft through the provider that
   operation records, never another. While the run is in progress or the draft is incomplete by the
   product's profile, nothing is recorded and what is missing is printed. When the run succeeded and the
   draft is complete, the operation becomes `observed-success` and a `release` record with status
   `candidate` is written with every asset's platform and the bill of materials and notices taken from
   the profile, with the run as evidence, and with the release's own record formats: those the manifest
   of the documentation set in the draft states (`recordFormat`, which the build tooling writes from the
   build), read through the provider and held to the hash the checksum file names. A draft whose set
   states none, as the sets of 0.5.2 and earlier do, gets the formats of the Vericairn that observes it,
   and the candidate's evidence and the command's answer say so, for a superseding revision to correct.
   When the run failed, the operation becomes `known-failure`. `--cancel <reason>` settles an intent that
   was never dispatched, an uncertain outcome reconciled as having had no effect, or a known failure.
3. The operator accepts the exact candidate (`vericairn records new acceptance --by <operator id> --subject release:<id>:<hash> --body <file>`,
   whose body states `statement`, `basis` and `self`) and publishes by hand with the command the CLI printed
   (for GitHub, `gh release edit vX -R <owner/name> --draft=false --latest`). Nothing in Vericairn publishes.
4. `vericairn release observe` again records the observed publication as the `published` revision of the
   candidate; without the operator's acceptance the runtime rejects it with `release.unaccepted`.
5. `vericairn estate upgrade --to X` pins the estate to the published release (for Vericairn itself; the
   bootstrap fetches `vericairn-<version>-<platform>` by the hash the published record names).

`vericairn status` lists every current operation with its state; one that is not observed-success or
canceled needs attention.

A goal can ask that no living document of a product be due, with the record predicate `{"type": "living-document",
"product": "<id>", "state": "due", "max": 0}` among its completion conditions. It follows the product across the
revisions of its record as the release gate does, counts every document due at any level, and is never met while a
document of the product names a contribution no installed pack provides, so a goal such as "release 0.6.0" is not done
while one of its documents is due ([record-format-v2.md](record-format-v2.md#a-goals-completion-conditions)).

A release's documentation set (for Vericairn, `docs/releases/<version>/`) carries a manifest the product's
build tooling writes from the build; from 0.6.0 it states the record formats the build reads and writes,
which step 2 reads from the draft. `vericairn docs notes --from <previous> --to <version> --out <set>`
writes the set's release notes from the estate's records and records their window in the manifest
(`notes: {from, to}`, the records dated after `from` and up to `to`). The notes begin where the notes of
the previous set, the directory `<previous>` names beside `<set>`, ended, so no record falls between two
sets. For a previous set that records no window, as 0.5.2's and earlier sets do, they begin at the end its
notes state, to the minute, and with no previous set at the previous publication; `--since <time>` names
the beginning instead. Until the version's publication is recorded they end at the second before they are
generated.

## Coding agents: the worker and its launchers

The worker contract, `vericairn.worker/1` (the estate's `design/goals.md` § 7), carries one assignment with a coding agent
toward its work order's exit question. It lives in the runtime; the launchers that start a harness (contract
`vericairn.launcher.agent/1`, `IAgentLauncher`) and the workspaces it works in (contract `vericairn.workspace/1`,
`IWorkspaceProvider`) are providers the host registers, listed as the release providers are: `vericairn worker
launchers` lists the launchers, and `vericairn release providers` lists them with every other implementation. The
Software Domain Pack ships three launchers and the git worktree workspace:

| Launcher | Runs |
|---|---|
| `claude-code` | `claude -p "/goal <condition>" --output-format stream-json --verbose --permission-mode auto --mcp-config <the worker's MCP configuration> --strict-mcp-config`, in the workspace: Claude Code's own `/goal`, in auto permission mode, with the vericairn MCP server as its only MCP server. It reads the tokens of the stream's final result. |
| `codex` | `codex exec --json --cd <workspace> --sandbox workspace-write -c mcp_servers.vericairn.command=<vericairn> -c mcp_servers.vericairn.args=[...] [-c mcp_servers.vericairn.env.<name>=<value>]... "/goal <condition>"`: Codex's own `/goal`. Before a run is recorded it reads `codex features list`, with the run's environment, and refuses, saying why, unless the `goals` feature shows `true`. It adds up the input and output tokens of each completed turn. |
| `generic` | The command its `command` setting names, then the arguments its `arguments` setting names (a JSON array) with `{condition}`, `{brief}`, `{mcp-config}` and `{workspace}` replaced. Generic process launch is not complete agent support: it knows nothing of the harness's goal, sign-in, usage or stop, and it is listed and run with that label. |

The condition is the work order's exit question, with the assignment's review criteria and an attempt and its result
recorded through the MCP tools as its checks, and the brief's prohibited effects, the goal's constraints and only the
changes the brief permits as its constraints, on one line. Every flag is one that `claude --help`, `codex --help` or
`codex exec --help` lists. The `command` setting of each launcher names another executable, or a JSON array of one and
its leading arguments. No launcher reads a price, so a cap in currency is charged in full.

`vericairn worker run <assignment> --launcher <id> --agent <principal> --by <principal> --repository <resource> [--goal <id>] [--branch <name>] [--base <ref>] [--wall-minutes <n>] [--setting name=value]... [--detach]`:

1. Refuses, before anything is recorded: an agent that is not the assignment's contributor, or has no admitted device
   whose key this machine holds (its MCP server would list only the read-only tools); a recording principal that is
   neither the operator nor holds `local-engineering`; work that belongs to no goal, or to several active goals unless
   `--goal` names one; a goal that is not active, or whose budget does not allow the run (record-format-v2.md, [An agent
   run and its goal's ledger](record-format-v2.md#an-agent-run-and-its-goals-ledger)), in which case a crossed cap marks
   the goal `budget-limited`; a branch another run in flight works on; a repository with no clone registered here
   (`vericairn sources clone`); the custody precondition, below; and a launcher that cannot run, such as Codex without
   its goals feature.
2. Prepares the workspace: a git worktree of the repository's registered clone on the work branch (`work/` and the work
   order's key unless `--branch` names another, made from `--base` or the clone's HEAD), beside the estate's state on this
   machine and never the clone's own working tree; a later run of the branch takes its worktree up again. The brief, as
   `vericairn work brief` gives it, is exported into the workspace as `.vericairn/brief.md`, in a directory that ignores
   itself so that nothing in it is committed.
3. Records the intent, the `agent-run` operation with the run's cap from the goal's budget, before anything starts.
4. Configures the MCP server for the agent, `vericairn --estate <estate root> mcp serve --as <agent>`, in a file outside
   the workspace, and launches the harness in the workspace with an environment built from an allowlist: the path, the
   home and temporary directories, the locale and the platform's own variables, what the MCP server needs to find the
   agent's key (`VERICAIRN_HOME`), and the harness's own configuration (`CLAUDE_CONFIG_DIR`, `CODEX_HOME`). No variable
   whose name marks a credential passes, whatever the worker's environment holds: no GitHub, cloud, package-registry or
   model-provider token, since a brief grants none. The harness signs in with its own login under the user's home. It is
   told `VERICAIRN_RUN`, `VERICAIRN_BRIEF`, `VERICAIRN_MCP_CONFIG`, `VERICAIRN_CONDITION` and `VERICAIRN_STOP_FILE`.
5. Observes the harness: a heartbeat every two seconds, the usage its launcher reads from its output, and its exit. Only
   the exit ends a run: output that closes, or a worker that stops observing, is no stop, and the run stays in flight,
   charged at its cap. A run approaching its wall or reported-token allowance is stopped by the worker, leaving headroom for usage and shutdown in flight; spending caps remain soft.
6. Settles the operation with the observed effect and usage: `observed-success` when the harness exited 0, which says
   only that it ran to its end, and `known-failure` when it failed or was stopped. The worker accepts nothing: the agent's
   result is a claim another principal reviews, and the goal's evaluation and acceptance stay theirs.

Without `--detach` the command waits until the run settles, and Ctrl+C asks for its stop; with it, the daemon that serves
the estate launches and observes the run, and the command returns.

**The custody precondition** (design/goals.md § 10). A key that a store of this machine holds for the operating-system
user the worker runs as can be used by the agent, which runs as that user. So the worker launches nothing while a device
of an operator, or of the sponsor of the goal the run is charged to, has its key held here in custody that signs without
its owner's presence: a file, keychain, secret-service or CNG key, or a key whose record claims presence while it is held
where presence cannot be enforced. Every such device counts, admitted, revoked or not yet admitted, since a revoked key
still signs records dated before its revocation, and the refusal names each device, its custody and the user. The agent's
own key is not one of them. Rotate the key into the Secure Enclave with presence (`vericairn devices rotate <device>
--custody secure-enclave --presence`), or run the worker as an operating-system user that cannot read it. Or the goal's
sponsor records an exception for the goal: `vericairn goal custody-exception <goal> --by <sponsor> --reason <why>
--expires <time>` writes a `custody-exception` record, with its reason and its expiry, about the goal's current revision,
which only the sponsor, or an operator for a goal an agent sponsors, may record. While it is in force the guard lets the
run launch unattended: the run's intent names the exception in its target and its evidence names the devices it lifted,
the response says so, and `vericairn goal next` shows it on every agent's route where the refusal would be. A revised goal
needs a fresh exception. The guard judges the run before its workspace is prepared and again, against the records and
the keys as they are then, when its intent is recorded: an exception revised or expired meanwhile lifts nothing, and the
intent names the one in force at that moment. The records judge that name as of the intent, so a run stays valid when its
exception is revised or expires afterwards, and one naming an exception already revised before it is refused. The guard
judges once more immediately before the launch, under the runtime's gate, so nothing changes between that judgment and
the start: an exception that lapsed after the intent, or a key of an operator or of the sponsor that arrived here, launches
nothing, and the run is recorded `canceled` before dispatch, naming the devices and the exception, never charged.

**Stop.** `vericairn worker stop <run> --by <principal> [--reason <text>] [--grace <seconds>]` asks the run to stop, and
prints what was observed. The request, its transmission, the harness's acknowledgment, an escalation and the observed
containment are separate facts: the worker that observes the run transmits the stop at its next heartbeat, by creating the
file `VERICAIRN_STOP_FILE` names and, on Linux and macOS, by SIGTERM to the harness; a harness acknowledges it by printing
`{"vericairn":"stop-acknowledged"}` on its output; if the harness has not exited when the grace period (ten seconds unless
named) has passed, the worker terminates its containment boundary; containment requires observing the whole boundary exited. The settlement records the facts
in the operation's `stop`. A stop that is not observed, because no worker here observes the run, is reported as requested,
and the run stays in flight.

**One routed step at a time.** `vericairn goal next [<goal>]` shows a goal's steps routed from this machine by its roster
([record-format-v2.md](record-format-v2.md#routing-a-goals-steps)): for each eligible step the principal it reaches, or why
none is available, whether the goal's budget allows it, and for an agent the custody guard here. It records and starts
nothing. `vericairn goal step [<goal>] --by <principal> [--repository <resource>] [--setting name=value]...
[--wall-minutes <n>] [--branch <name>] [--base <ref>] [--detach]` takes the goal's next step, the smallest form of
Community's conductor, one step in sequence; Pro's program plans and schedules many. The goal must be active, and the
principal an operator, the goal's sponsor when the sponsor is a person, or a principal holding `local-engineering`, which
is how the builder dispatches agents under the operator's authority; an agent that sponsors a goal takes its steps only
under `local-engineering`, like any agent, since an operator decides for that goal. A person who sponsors a goal assigns
its steps and records its agents' runs under the goal's activation, without `local-engineering`. An agent's step goes on
with the assignment that holds it; when none does, routing signs an assignment to the agent, with a brief made from the
work order and the goal (its scope, deliverables and exit question, the goal's constraints, and every reserved action
prohibited unless a grant in force covers it, with the grant that lets the agent carry a reserved step named apart), and
stores it only with the run's intent, after the worker's whole preflight, the two judged together, their content checks
included, before either is stored. The store is not atomic, so the runtime writes a journal of the pair on its machine
first, and routing marks the assignment `routedWith: run`: an assignment whose run's intent was never stored holds nothing
of its step, a step is never held by half a pair, and `vericairn reconcile` reports the pair as `batch.incomplete`, naming
what was stored, until the step is held again or is no longer planned; nothing stored is removed. The run starts through
the worker with the launcher routing chose and the settings given, all as `worker run` does, the custody precondition
included; without `--detach` the command follows the run until it settles. Every refusal before the intent records
nothing, routing's assignment included: no repository or an unknown one, a cap under one wall minute, a setting the
launcher does not take, the custody guard with no exception in force, the budget, the launcher's verification, or the
workspace's preparation. A harness that cannot start once the intent is recorded leaves the assignment and the run,
recorded as a known failure that spent nothing. A person's step is assigned to the person and asked of them, the two
judged and stored together in the same way, the ask naming the assignment, which is marked `routedWith: ask` and holds
nothing without it; a reserved action routed to a person who holds it likewise; and a step no principal is available for
is asked of who decides for the goal, with the recommendation that fits why, and assigned to no one. With every eligible
step held, the command says what holds each and records nothing. It activates, funds, accepts and answers nothing.

**A hand-over.** `vericairn work takeover <assignment> --by <principal> [--contributor <person>] [--note <text>]` hands a
step an agent holds over to a person: a new assignment of the work order to the person, the hand-over record, carries the
agent's brief and names the agent's assignment among its sources, and from then on holds the step. Every run of the
agent's assignment still in flight is asked to stop. A step the agent no longer holds, or whose work order is accepted or
retired, is not taken over, and a step goes over to a person only. The hand-over is recorded by a person too: the one
taking the step over, the sponsor of a goal the step belongs to, or an operator. An agent never records one, whatever
scope it holds, and the records refuse an assignment that takes a step over from an agent when anyone else wrote it.

`vericairn worker runs` lists every run with its cap, its usage once settled, what it is charged and its stop's facts, and
the ledger of every goal with a run or active now: each unit's budget, spend and remainder, the runs in flight, and why a
new run would be fenced. The runs' own files, their state, output and MCP configuration, stay under the Vericairn home on
the machine that ran them, never in a record or a repository.

Limitations: the launchers are implemented against the harnesses' `--help` and tested with a stand-in process that speaks
their streams, and none is yet qualified against a live harness. Windows has no signal a windowless process receives, so a
harness that does not watch the stop file is contained by escalation. Files of the operating-system user stay readable to
the harness, which only running the worker as another user changes. A process that deliberately leaves its POSIX process
group is outside that boundary; the worker is not an OS security sandbox. Codex replaces user configuration with its
per-run configuration and vericairn-only MCP table. No MCP tool launches or stops a run.

### Launch accounting and containment

A worker state-file write failure after launch leaves the run dispatched and reserved; supervision retries local state
writes and settles only after observing exit. `RunStart` authors the intent as its supplied recording caller.

Every physical agent launch requires a new `agent-run` intent, with fresh budget and concurrency admission. Agent-run
revisions cannot redispatch an existing identity; generic operations retain their reconciliation and retry rules.

Harnesses enter a containment boundary before executing: a Windows Job Object (no breakaway), or a new POSIX process
group on Linux and macOS. Stops signal the group; escalation terminates the whole boundary. Root exit alone does not
establish containment. The worker observes every remaining member exited (including kernel zombie exit state) before
settling. A failed escalation is retained in local state and settlement evidence and retried, without claiming a kill.
Failure to observe the boundary leaves the allowance reserved. A POSIX process that deliberately creates another
session can escape this same-user boundary; use OS isolation for hostile code.

Stop chronology is validated as observed facts: acknowledgment and escalation are at or after transmission (either may
precede the other), containment is at or after every earlier fact and no later than `observed.at`. Every previously
recorded stop property, including the request and explanatory text, is immutable across revisions
(`operation.stop-kept`); out-of-order facts are refused (`operation.stop-order`). Transmission is timestamped
after delivery attempts finish, rather than at an earlier heartbeat.

A failed dispatch write is recovered from the worker's launch observation before settlement. If it cannot be recorded,
the run remains in flight at its full reservation; it is never settled as an unstarted zero-charge failure.

### Harness configuration and workspace exports

Codex runs with a fresh per-run `CODEX_HOME`: only its provisioned harness `auth.json` login is imported, with
owner-only permissions on Unix. User settings, MCP credentials, plugins and hooks are not imported. The run
configuration and a complete `mcp_servers` command-line override name only `vericairn`; project configuration is
untrusted. Claude Code continues to use `--strict-mcp-config`. These are configuration boundaries, not filesystem
isolation: the harness still runs as the worker user and can read that user's files or keychain. Use a dedicated OS
identity with only the approved harness login for credential isolation. See the [Codex configuration
documentation](https://learn.chatgpt.com/docs/config-file/config-basic).

Workspace export refuses symbolic links and Windows reparse points anywhere in `.vericairn`, including dangling links
and nested entries, before recording a run. Unix writes use directory-relative no-follow opens and exclusive temporary
files; Windows holds non-reparse directory handles against replacement and creates files exclusively. Replacing a
regular exported file never writes through its old inode or a link. A refused export records no intent and launches
nothing.

### Usage and soft spending limits

Token usage is accepted only as complete nonnegative integer counts with checked sums (including sums across turns).
Claude results require input, output, cache-creation and cache-read counts; Codex completed turns require input and
output counts. A malformed or incomplete usage event, arithmetic overflow, or an unfinished Codex turn leaves the
whole token unit unreported, even if another turn reported valid usage. Settlement charges that unit at its reserved
cap; complete explicit zero counts remain known zero.

New worker intents state `target.capMode: "soft"` and evidence describing each operational threshold and the
launcher's native controls. The ledger reserves the full cap. The worker subtracts grace plus one heartbeat from wall
time, and ten percent (rounded up) from tokens and currency, to leave headroom for shutdown and usage in flight. It
refuses an allowance too small to leave any active token or currency unit. Claude Code receives `--max-budget-usd` for
USD at the reduced currency threshold; other currencies and total tokens have no native cap. Codex receives
`features.rollout_budget.enabled=true` and `features.rollout_budget.limit_tokens` at the reduced token threshold; this
is native tracking, not a guaranteed hard stop. Generic launchers supply no native spending control. All units remain
explicitly soft: a turn, provider cost estimate or failed shutdown can exceed the headroom and the reservation.
Unknown units are still charged at their full cap.

### Observed stalls and assignment scope

The worker observes stdout event-stream liveness with a monotonic clock; stderr activity and harness claims do not
certify progress. Each launcher defaults to a 600-second silence limit; its `stall-seconds` setting (1–86400) changes
that default, and the goal's `limits.stallSeconds` takes precedence. The intent pins the effective limit from the goal current when it is recorded
in `target.stallSeconds`. At one limit of silence the worker records an open attention `ask` to the goal's accountable
principal, about the run (no notification is sent). At twice the limit it transmits a stop and contains the whole
launch boundary. New stdout events reset the silence timer but never erase an earlier attention fact. A failed
attention write is retried and does not prevent containment. Settlement records `liveness`: `stallSeconds`,
`lastEventAt`, optional `attentionAt`, `attention` (the ask id), and `stoppedAt`; run-list responses expose the same
`liveness`, observed locally while in flight and recorded after settlement. A lost observer certifies neither liveness
nor containment.

At settlement the worker compares a file inventory captured after brief export and before launch with the workspace
after the whole process boundary exits. Content, symlink targets (without reading through the link), and Unix modes
are compared, including committed changes, deletions, renames, untracked files and ignored files. Preexisting
unchanged files are not attributed to the run. `brief.permittedChanges` entries are interpreted literally as
repository-relative files/directories or globs (`*` and `?` within a segment, `**` across segments); an optional
`path:` prefix is accepted. Prose grants no implicit wildcard, absolute and parent-traversing patterns grant nothing,
and directory matching respects path boundaries. The settling operation and run-list response expose `scope`:
`changed`, `outsideCount`, `outside` (up to 1000 relative names; unsafe names are hashed), and optional `problem` when
the audit is unavailable or incomplete, including submodules needing a separate audit. An outside change or incomplete
audit adds scope attention to the observed summary; it never accepts the work or silently treats an unavailable audit
as clean. This is a net workspace observation, not filesystem isolation or proof of which actor made a concurrent
change. Git administration and changes outside the workspace are not inventoried.

## The synthetic migration

The move from record format 1 to 2 (`record-format-2`, [record-format-v2.md](record-format-v2.md)) stages nothing:
no record is ever rewritten, so the step only moves the manifest's format, and the tests check that every record
file is byte for byte what it was. When the estate holds admissions recorded without a signature, the move anchors
them: the migration record lists them by id and hash, and the upgrade is refused unless the operator, or a
principal with the `administer` scope, applies it with an admitted device held on this machine. The rest of the
upgrade path is proven by a synthetic migration in the tests: a step from format 2 to a pretend format 3 stages a
file through `MigrationContext`, the upgrade writes the staged files, the manifest and the migration record only
once every step succeeded, a failing migration changes nothing, and a manifest beyond the formats this binary
knows is refused rather than misread. Migrations never rewrite records; they stage other files.
