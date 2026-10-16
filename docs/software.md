# The software journey: repositories, checks and releases

How a software product goes from a repository to a published release with its evidence as records,
intent recorded before every external effect, and publication left to the operator.

## Personal communication (0.7.0, COMMS-01)

Communication has five independent fields: optional progress frequency, status
depth, decision lead depth, result explanation depth and handoff introduction
depth. Shipped defaults are `completion-and-need`, `short`, `comprehensive`,
`comprehensive`, `comprehensive`, respectively. The frequencies are `none`,
`completion-and-need`, `milestones` and `verbose`; depths are `short`, `standard`
and `comprehensive`. Resolution is field by field: shipped defaults, estate
defaults, the recipient's estate setting, their assignment override, then a
one-view override. An omitted field inherits; `none` explicitly suppresses
optional progress. These choices confer no execution permission.

```sh
vericairn communication show --recipient <person-id> --preview
vericairn communication set --as <person-id> --status short --progress completion-and-need
vericairn communication set --as <person-id> --for-assignment <assignment-id> --handoffs standard
vericairn communication set --as <person-id> --status inherit
vericairn communication reset --as <person-id>
vericairn status --recipient <person-id> --depth comprehensive
vericairn records show <record-id>
```

Settings and delegations are signed format-2 records with retained revisions.
The person changes their own settings; an explicit `communication delegate
--as <person-id> --to <principal-id>` grants only that scope, and its exact
record must be supplied as `--authority` by the actual delegated author.
Revisions require the prior hash; stale or divergent writes report conflict.
CLI named-field edits first read that prior hash and preserve other fields.
`--expected` pins a previously inspected revision. Reset preserves history.

Human CLI status, progress, decisions, work observations, briefs and handoffs
use the effective depth. Short status omits optional principal navigation;
protected decisions, results, findings, evidence and handoff originals remain
complete behind the chosen explanatory lead. `--json`, canonical API JSON,
record contents and saved handoff packages never vary with depth. CLI reads
are on demand and show all progress. Workbench's periodic updates apply
frequency only to optional claims. Required decisions, failed or unknown
checks, blockers, uncertain effects, incidents and exhausted budgets always
appear. A missing source remains unknown and visible.

A worker brief captures the goal sponsor's settings when that sponsor is a
person, plus that assignment's override; otherwise it explicitly uses estate
defaults. The complete original brief is retained, with recipient, revisions,
source hashes and freshness added. It instructs the worker to refresh through
the read-only MCP tool `communication_get` at the next settled boundary.
Enforcement covers stored authority, conflicts and first-party rendering;
following the brief is an instruction to an external agent, not an observed
guarantee of its prose. Run configuration and model selection are unchanged.
Remote is not built; no Remote communication behavior is claimed.

The [first-party adoption procedure](communication-adoption.md) preserves the
operator's exact existing decision as an explicit personal record. It is a
separate authorized write after reader deployment; builds and reads never
perform that migration or invent anyone else's preference.

The protection boundaries are explicit:

| Protection | What enforces it |
|---|---|
| Own setting, delegated consent and attribution | Record validation and the runtime's current-authority check, including generic record writes |
| Stale and merged edits | Expected hashes and visible conflicts; the recipient reconciles personal branches |
| Complete originals and stable machine output | Shared first-party renderers and byte-for-byte regression checks |
| Required attention at every frequency | Structured asks, checks, blocked results, uncertain effects and worker/budget observations in first-party surfaces |
| Agent wording, frequency and refresh | Instructions in the captured brief; compliance by an external agent is not enforced or certified |
| Remote | Not built; no implementation or qualification claimed |

This draft describes the 0.6.1 code with the unreleased FIX-09 corrections and 0.7.0 additions described here, including COMMS-01. Its worker and launchers first appeared
in the 0.6.0 candidate, withdrawn before publication. 0.6.1 preserves settled run facts, audits reachable committed paths as well as the worktree, and corrects
the release-note and progress classification of accepted work, as the sections below describe. The NuGet distribution
path below is prepared for 0.7.0.

## Context packages (0.7.0)

For 0.7.0, the foundation's context package preserves exact originals apart from
derived presentation. `vericairn context prepare <work-order>` computes the step's
working instructions, effects and pinned inputs. Only optional documents named
by the work, its inputs or governing decisions enter the selection.
Required record and document originals come first. Optional originals not fully included have
a withheld entry with their identity, reason, availability, measured or unknown
size, dependency, reduction and generated retrieval route. Excerpts carry byte
extents and say `irreversible reduction of this representation; original retained`;
unavailable originals say `irreversible reduction; original unavailable`.

The selection follows this work's assignments, results, decisions and effects.
The remaining document catalogue is explicitly outside the selection.
`vericairn context catalogue` lists its current revision identities without
reading document bodies; MCP uses `records_list` with `type=document-revision`.
A prerequisite work record is a pinned input; sharing a decision does not import
another assignment or its historical file inputs. Required document references
remain exact, including an older revision explicitly needed by this step. The
package states this scope; nothing withheld never means the whole estate was read.

`vericairn context read <pin> --original` returns verified original bytes on
standard output; `--out <file>` creates a new file without overwriting and
`--json` returns identity plus base64 bytes. Each generated, shell-safe `vc1_`
pin encodes the estate and exact source identity, so no retained manifest is
needed. Records are read by id, record hash and byte hash from immutable record
custody. Documents are read by path, revision record and byte hash from the
working tree only while those bytes still match. Changed, missing or unsupported
sources are unavailable in this copy; neither the latest revision nor an object
store is substituted. Missing mandatory originals refuse the dependent step.
Remote is not built or qualified.

Briefs on every surface, preparation, retrieval and hand-over author nothing and
write nothing. At run dispatch, the worker retains `context.json` (the complete
machine brief) and `brief.md` in its existing local run directory under the
Vericairn home. That is the dispatch custody chosen here; it never adds context
objects or records to the estate repository. The existing workspace export is
still git-ignored. Retention is not required for read-time retrieval.

`context prepare --max-bytes <allocation>` uses exact UTF-8 bytes of the final
payload, including accounting and omission overhead. The allocation is supplied
separately from a run's spending cap. Optional sources fit in deterministic
order after mandatory material; a required original or complete manifest that
cannot fit refuses the package. With no allocation, preparation and MCP briefs
inline mandatory material and withhold named optional originals, with their
sizes and retrieval commands. A standalone human `work brief` keeps the full
selected scope and reports `limit not set`, never a model-fit claim. Token counts are unknown:
this build has no qualified local tokenizer and adds no package or network
tokenization call. A worker must establish its model's input bound, existing
context, framing and reserves before relying on a fit claim.

`work brief` keeps the existing lead and adds the package's content, complete
withheld list, and readable originals. Markdown and the full machine brief each
have an exact measurement and content identity; their accounting is included in
the count. The worker exports this same content contract and pins the exported
text in the existing run-intent evidence. A routed run's preflight can include
its signed pending assignment, explicitly labeled as requiring admission with
the run intent before launch. This does not grant authority or resolve a partial
record batch; the existing grouped-write checks still own that boundary.
Model-specific context allocation and qualified autonomous fit remain a launcher
integration responsibility, separate from the existing run spending cap.

The local contract is exercised through CLI, MCP, API/SDK and both repository-synced and local-only record custody. Those fixtures do not qualify a live model or harness. Provider
billed usage, model fit, compaction recovery and any later adapter transformation
need their own measurements and qualification. There is no new Remote reader,
transport, signing capability or user interface: Remote is not built.
COMMS-01 resolves the recipient's presentation depth for both human and MCP
briefs. The final text is measured after that lead and communication section are
added; depth changes neither the canonical context identity nor original bytes.
CLI and Workbench hand-over presentations also count their final communication
lead, preserving the protected original rendering in full. API v1 additions preserve the legacy hand-over discriminator and
resume input, and the artifacts introduce no record type or format upgrade.

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
sets for records present when generation runs. To include records imported later with earlier dates, use
`--since <timestamp>` to widen the next window. The manifest records that exact start, and the introduction
states it and explains that the overlap includes records missed by the previous set. A start later than the
previous end is refused before writing either file, as is a previous set whose end cannot be read.
For a previous set that records no window, as 0.5.2's and earlier sets do, they begin at the end its
notes state, to the minute, and with no previous set at the previous publication (also the latest permitted
explicit start). Until the version's publication is recorded they end at the second before they are generated.

The next set after 0.6.1 must use `--since 2026-10-16T04:29:43Z`: 0.6.1 was rendered from a clone
missing nine records dated 04:29–04:34 UTC. For 0.7.0, run
`vericairn docs notes --from 0.6.1 --to 0.7.0 --out docs/releases/0.7.0 --since 2026-10-16T04:29:43Z`
against the estate that holds those records. This deliberately overlaps 0.6.1; its frozen set stays unchanged.

The product's CLI test suite checks every file of each frozen set against
`docs/releases/frozen-sha256.json`: changed bytes, missing files and added files fail the guard. Published and withdrawn
sets stay indexed; corrections belong in the next set, never in an indexed file or its recorded hash. A new set remains
editable until frozen and indexed. This is a test guard, not a write barrier in `docs notes` or the reference generator:
run generation only against a set under preparation.

Generated API parameter rows omit the description separator when there is no description and carry no trailing
spaces. Admission rows in the notes name a scope only when the record supplies one; format-2 admissions have no
legacy scope label.

When a review or acceptance names an earlier work-order revision, the estate check asks for reconsideration only
while that judgment remains current. Superseding it with a judgment of the current revision clears the earlier
`record.subject-stale` and `acceptance.unresolved` findings, as in FIX-05. A replacement that still names an earlier
revision remains flagged, and historical records still have their integrity and authority checked.

Only current results in that window are listed: a result superseded by a later result is omitted, even when the later
result is outside the window. A current result appears under **Work accepted** when a current acceptance pins either
that result's exact hash or its work order's current revision and hash. This includes another principal's results with
no acceptance of their own; it classifies the work and does not accept those result records. An acceptance of an earlier
work-order revision does not count for the current revision. Every remaining current result appears only under **Work
recorded, not yet accepted**. The progress report uses the same classification for each published release.

## Vericairn's NuGet publication

From 0.7.0, `.github/workflows/release.yml` packs the Community command line and libraries with the release
version checked against `VersionPrefix` in `Directory.Build.props`, clearing the development suffix.
It attaches the `.nupkg` and `.snupkg` files to the draft release and lists every file in `SHA256SUMS`,
alongside the self-contained archives. No package is pushed while the release is a candidate, so a
withdrawn draft has not occupied that version on NuGet.

Only the operator's publication triggers `.github/workflows/nuget.yml` (`release: types: [published]`).
That workflow downloads the published release's package assets and `SHA256SUMS` by release id, requires
every package and symbol asset to appear exactly once in the checksums, and verifies each hash before
requesting credentials. Missing, extra or changed package assets refuse publication. Non-package archive
entries remain in the release's checksum file; this workflow downloads and pushes only the packages.

The workflow has `contents: read` and `id-token: write`. The pinned `NuGet/login` action exchanges its
GitHub identity through nuget.org Trusted Publishing, using `NUGET_USER`, a repository variable, and
returns the short-lived key used by `dotnet nuget push`. No stored API key is used, and the workflow never
checks out or rebuilds the packages. It pushes the verified package files and then their verified symbol
files. A failed or partly completed publication needs operator inspection before any retry; the workflow
does not silently skip an existing version. See the [operator setup note](releases/README.md#nuget-publication-setup).

Once publication succeeds, a .NET 10 SDK installation uses:

```sh
dotnet tool install --global dotnet-vc
dotnet vc version
dotnet vc doctor
dotnet add package Vericairn
```

Run the last command in the project that will use the SDK. `Vericairn` brings `Vericairn.Sdk`, Protocol
and Records; Runtime, Daemon and Mcp are separate host and adapter packages. The module names and
`vericairn.*` contribution identifiers earlier in this document are unchanged. The software pack, git
adapter and GitHub provider still ship inside the command line; they are not separate NuGet packages.

## Coding agents: the worker and its launchers

The worker contract, `vericairn.worker/1` (the estate's `design/goals.md` § 7), carries one assignment with a coding agent
toward its work order's exit question. It lives in the runtime; the launchers that start a harness (contract
`vericairn.launcher.agent/1`, `IAgentLauncher`) and the workspaces it works in (contract `vericairn.workspace/1`,
`IWorkspaceProvider`) are providers the host registers, listed as the release providers are: `vericairn worker
launchers` lists the launchers, and `vericairn release providers` lists them with every other implementation. The
Software Domain Pack ships three launchers and the git worktree workspace:

| Launcher | Runs |
|---|---|
| `claude-code` | `claude -p "/goal <condition>" --output-format stream-json --verbose --permission-mode auto --mcp-config <the worker's MCP configuration> --strict-mcp-config --setting-sources "" --settings '{"hooks":{},"enabledPlugins":{}}' --tools Bash,Read,Write,Edit,Glob,Grep,TaskOutput,TaskStop,EndConversation`, in the workspace, with a fresh configuration directory and only its approved login imported. It watches provisional message tokens and settles tokens from the stream's complete final result. |
| `codex` | `codex exec --json --cd <canonical workspace> --sandbox workspace-write -c 'mcp_servers={vericairn={...}}' -c 'projects={"<canonical workspace>"={trust_level="untrusted"}}' --ignore-rules <qualified feature overrides> "/goal <condition>"`, with a fresh configuration home. Before a run is recorded, `codex features list` must show `goals` enabled. Before launch, an effective `codex mcp list --json` probe must match exactly the approved server and transport. It adds up the input and output tokens of each completed turn. |
| `generic` | Describes the command its `command` setting names, then the arguments its `arguments` setting names (a JSON array) with `{condition}`, `{brief}`, `{mcp-config}` and `{workspace}` replaced. Worker dispatch remains refused because the generic launcher has no complete inventory qualification. |

The condition is the work order's exit question, with the assignment's review criteria and an attempt and its result
recorded through the MCP tools as its checks, and the brief's prohibited effects, the goal's constraints and only the
changes the brief permits as its constraints, on one line. The launcher argument construction and configuration checks are covered by the launcher tests; the limits of
that qualification are stated below. The `command` setting of each launcher names another executable, or a JSON array of one and
its leading arguments. Native qualification refuses leading option flags in that setting: the launcher supplies every
harness option. Positional wrapper arguments and entry-file content have inventory identities, and local command
bindings are rechecked before dispatch. No launcher reads a price, so a cap in currency is charged in full.

`vericairn worker run <assignment> --launcher <id> --agent <principal> --by <principal> --repository <resource> [--goal <id>] [--branch <name>] [--base <ref>] [--wall-minutes <n>] [--setting name=value]... [--detach]`:

1. Refuses, before anything is recorded: an agent that is not the assignment's contributor, or has no admitted device
   whose key this machine holds (its MCP server would list only the read-only tools); a recording principal that is
   neither the operator, nor holds `local-engineering`, nor is the goal's sponsor when the sponsor is a person; work that belongs to no goal, or to several active goals unless
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
   model-provider token, since a brief grants none. Codex and Claude Code receive fresh configuration directories with
   only their approved harness login imported, as described below. Configuration verification can refuse after the
   intent; a refusal before any process launch settles as an uncharged known failure. The harness is told
   `VERICAIRN_RUN`, `VERICAIRN_BRIEF`, `VERICAIRN_MCP_CONFIG`, `VERICAIRN_CONDITION` and `VERICAIRN_STOP_FILE`.
5. Observes the harness: a heartbeat every two seconds, the usage its launcher reads from its output, and the whole
   containment boundary's exit. Root exit or closed output alone does not end a run. A worker that stops observing
   leaves the run in flight, charged at its cap. A run approaching its wall or reported-token allowance is stopped by
   the worker, leaving headroom for usage and shutdown in flight; spending caps remain soft.
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
file `VERICAIRN_STOP_FILE` names and, on Linux and macOS, by SIGTERM to its process group; a harness acknowledges it by printing
`{"vericairn":"stop-acknowledged"}` on its output; if the harness has not exited when the grace period (ten seconds unless
named) has passed, the worker terminates its containment boundary; containment requires observing the whole boundary exited. The settlement records the facts
in the operation's `stop`. A stop that is not observed, because no worker here observes the run, is reported as requested,
and the run stays in flight. Only an operator, whoever decides for the run's goal (its sponsor, or an operator for a goal
an agent sponsors) and the principal who started the run may stop it, on every profile; anyone else is refused before
anything is asked of the run. For a device-signed request, the runtime rechecks the requesting device's admission and
authenticated key (and its local-network permission on that profile) under the estate's write lock before storing the
stop for the worker; revocation after authentication cannot queue a new stop. The command's observation wait is capped
at 30 seconds, whatever grace it names, with a bounded wait for settlement after exit. It can return before containment;
poll `worker runs` for the later facts. On Windows the stop file is the transmission; there is no graceful process signal.

**One routed step at a time.** `vericairn goal next [<goal>]` shows a goal's steps routed from this machine by its roster
([record-format-v2.md](record-format-v2.md#routing-a-goals-steps)): for each eligible step the principal it reaches, or why
none is available, whether the goal's budget allows it, and for an agent the custody guard here. It records and starts
nothing. `vericairn goal step [<goal>] --by <principal> [--repository <resource>] [--setting name=value]...
[--wall-minutes <n>] [--branch <name>] [--base <ref>] [--detach]` takes the goal's next step, the smallest form of
Community's conductor, one step per call; multi-step planning and scheduling are outside this implementation. Like `worker run`,
it runs on the runtime's machine and is refused through the local-network listener. On loopback, its `by`
is the signing device's principal, and its admission and authenticated key are rechecked under the estate's
write lock before its records are stored, including a grouped assignment and run intent or ask. The local
socket uses its caller's operating-system identity. The goal must be active, and the
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
of its step, a step is never held by half a pair, and `vericairn estate reconcile` reports the pair as `batch.incomplete`, naming
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

Limitations: the launchers are tested with stand-in processes; no live coding agent or live sign-in has been qualified.
RUN-02 qualifies the launcher protocol for exactly Codex `0.159.0-alpha.12.1` and Claude Code `2.1.296`, using
stand-in executables for version, help, feature and MCP probes, admission, dispatch and refusal. Installed harness
help established the flag baselines; these qualification tests do not start a real coding agent or its configured MCP
server. Windows has no graceful signal for a windowless process, so a harness that
does not watch the stop file needs escalation. Files and keychains accessible to the worker's operating-system user
remain accessible to the harness; use a dedicated user for credential isolation. The process boundaries and managed
configuration limitations below apply. No MCP tool launches or stops a run.

### Launch accounting and containment

A goal's pause or stop changes its recorded lifecycle and prevents new runs; it does not signal runs already in
flight. Use `vericairn worker stop <run> --by <principal>` for each run that must stop, then inspect `worker runs` for
observed containment. In 0.6.1 `limits.deadline` (including `goal propose --deadline`) and `limits.consecutiveFailures`
are stored and validated but not enforced by routing or the worker. The worker enforces its run thresholds and
`stallSeconds`; operators must act on deadlines and consecutive failures themselves.

A worker state-file write failure after launch leaves the run dispatched and reserved; supervision retries local state
writes and settles only after observing exit. `RunStart` authors the intent as its supplied recording caller.

Every physical agent launch requires a new `agent-run` intent, with fresh budget and concurrency admission. A settled
identity (`observed-success`, `known-failure` or `canceled`) cannot reopen or change to another status. Its target and
cap remain those of its intent, and a dispatch timestamp cannot change or be added after settlement. Every record that
newly reserves allowance is checked for admission, including a revision; an existing reservation is recognized only
for the same in-flight target. Generic operations retain their reconciliation and retry rules.

From 0.6.1 a later revision cannot rewrite any settled fact, including usage, the observation, liveness, scope or
containment. Evidence may only be appended; missing stop facts and previously absent optional fields may be added
only under the existing record and chronology rules, without granting a new status transition. The ledger uses the
first settlement's usage, so a revision cannot return spent allowance to the goal.

Harnesses enter a containment boundary before executing: a Windows Job Object with no breakaway, or a new POSIX process
group on Linux and macOS. Escalation terminates that boundary. Root exit alone does not establish containment: a
successful inventory must show every remaining member exited, including kernel zombie exit state. Failed job or
process queries, unavailable or filtered Linux procfs, and macOS inventory errors leave the run uncontained and
reserved, and are retried. A failed escalation is retained as evidence and retried without claiming a kill.

After root exit, the worker allows one shutdown grace for the boundary to empty naturally, keeping its reservation and
retrying inventory throughout. This includes a Windows console host finishing after its client. A boundary still not
observed empty after that grace receives a stop and, if necessary, escalation; only observed containment settles it.

On POSIX the worker observes its leader without reaping it, holding its process-group identity through the last
inventory and signal. It refuses a numeric group signal after losing child ownership or closing that owner. Native
cleanup reaps the leader once. A detached lifecycle owner retains this responsibility after supervision is abandoned,
without claiming settlement or releasing the reservation. Windows process and job handles have SafeHandle ownership;
the detached owner holds them until boundary exit.

These guarantees have platform limits. POSIX processes can escape by leaving the group or session, and a worker crash
has no independent POSIX guardian. Hosts running the worker as PID 1, ignoring SIGCHLD or using `SA_NOCLDWAIT` are
refused; an embedding host with a separate child reaper is unsupported. Windows kills an assigned job when its last
handle closes, but a crash between suspended process creation and job assignment can leave that process behind.

Stop chronology is validated as observed facts: acknowledgment and escalation are at or after transmission (either may
precede the other), containment is at or after every earlier fact and no later than `observed.at`. Every previously
recorded stop property, including the request and explanatory text, is immutable across revisions
(`operation.stop-kept`); out-of-order facts are refused (`operation.stop-order`). Transmission is timestamped
after delivery attempts finish, rather than at an earlier heartbeat.

A failed dispatch write is recovered from the worker's launch observation before settlement. If it cannot be recorded,
the run remains in flight at its full reservation; it is never settled as an unstarted zero-charge failure.

### Harness configuration and workspace exports

Codex runs with a fresh per-run `CODEX_HOME`: only its provisioned harness `auth.json` login is imported, with
owner-only permissions on Unix. User settings, MCP credentials, plugins and hooks are not imported. The workspace is
canonicalized, including filesystem aliases, before writing its project-trust key. A whole `projects` value encodes
the path as a TOML key, so dots or quotes in it are not interpreted as CLI key separators.

Codex merges configuration tables across layers; a complete `mcp_servers` override alone cannot remove ambient
servers. The launcher therefore probes the effective configuration with `mcp list --json` and refuses unless it
contains exactly the enabled `vericairn` stdio server with the approved command, arguments and environment, no inherited
environment-variable list and no changed working directory. Extra entries, even disabled ones, merged transport
settings, and incompatible managed policy refuse launch. Project trust is set to untrusted using the canonical
workspace key. The probe does not start a coding agent or an MCP server. See the [Codex configuration
documentation](https://learn.chatgpt.com/docs/config-file/config-basic).

Claude Code receives a fresh `CLAUDE_CONFIG_DIR` containing only the provisioned `claudeAiOauth` login from its
credentials file. Preflight requires its subscription type to be `pro` or `max`, and rechecks that classification before
dispatch. Missing or unreadable login material, unknown subscription types, implicit keychain login, and managed
accounts are not qualified. No credential content or credential digest enters the inventory.

`--setting-sources ""` excludes user, project and local settings, and `--strict-mcp-config` restricts MCP to the explicit
run file. `--settings '{"hooks":{},"enabledPlugins":{}}'` declares no external hooks or enabled plugins. The launcher
also refuses repository customization sources, as detailed below. It deliberately does not set `disableAllHooks`:
that setting disables the built-in `/goal` evaluator too. The evaluator is included as a built-in hook in the inventory;
its native small-model inference uses the same provider route. Its exact model revision is not inferred.
See [Claude Code goal requirements](https://code.claude.com/docs/en/goal#requirements).

Detected managed files, macOS managed preferences and Windows policy registry keys refuse launch, including on the
second preflight. Remote or otherwise undetected managed policy is not qualified. These controls do not prevent
deliberate same-user filesystem or keychain access, and live sign-in from the isolated login has not been qualified.
See [Claude Code settings](https://code.claude.com/docs/en/settings).

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
Unknown units are still charged at their full cap. Reservation admission prevents allocating the same available
allowance twice; it does not guarantee actual spend stays within the budget. A soft overrun can make the remainder
negative. Elapsed wall usage and stop grace use a monotonic clock; recorded observation times remain UTC facts.

### Observed stalls and assignment scope

The worker observes stdout event-stream liveness with a monotonic clock; stderr activity and harness claims do not
certify progress. Each launcher defaults to a 600-second silence limit; its `stall-seconds` setting (1–86400) changes
that default, the roster's `limits.stallSeconds` overrides that setting, and the goal's `limits.stallSeconds` takes precedence. The intent pins the effective limit from the records current when it is recorded
in `target.stallSeconds`. At one limit of silence the worker records an open attention `ask` to the goal's accountable
principal, about the run (no notification is sent). At twice the limit it requests a stop and attempts to contain the whole
launch boundary; containment is recorded only after exit is observed. New stdout events reset the silence timer but
never erase an earlier attention fact. A failed
attention write is retried and does not prevent containment. Settlement records `liveness`: `stallSeconds`,
`lastEventAt`, optional `attentionAt`, `attention` (the ask id), and `stoppedAt`; run-list responses expose the same
`liveness`, observed locally while in flight and recorded after settlement. A lost observer certifies neither liveness
nor containment.

At settlement the worker compares a physical file inventory captured after brief export and before launch with the
workspace after the whole process boundary exits. It enumerates the workspace itself, independent of `core.worktree`,
`GIT_DIR`, replaced Git administration files or other agent-writable Git configuration. The physical root's identity
must still match the baseline. Content, symlink targets (without reading through the link), and Unix modes
are compared, including deletions, renames, untracked files and ignored files. From 0.6.1 the audit also compares the
run branch's head and every commit reachable from it but not from the prepared baseline commit against that baseline,
including side branches of merges. The union of all changed paths counts, even when the worktree or a later commit
restored a path, or the worktree checked out a different head. It pins the original Git object/reference directory
before launch and reads objects through isolated Git metadata, without loading agent-writable Git configuration,
replacement refs, grafts, attributes or external diff drivers. Missing history or an unreadable branch marks the
audit incomplete. Preexisting
unchanged files are not attributed to the run. `brief.permittedChanges` entries are interpreted literally as
repository-relative files/directories or globs (`*` and `?` within a segment, `**` across segments); an optional
`path:` prefix is accepted. Prose grants no implicit wildcard, absolute and parent-traversing patterns grant nothing,
and directory matching respects path boundaries. The settling operation and run-list response expose `scope`:
`changed`, `outsideCount`, `outside` (up to 1000 relative names; unsafe names are hashed), and optional `problem` when
the audit is unavailable or incomplete, including submodules needing a separate audit. An outside change or incomplete
audit adds scope attention to the observed summary; it never accepts the work or silently treats an unavailable audit
as clean. Non-regular files are rejected before content reads; no-follow acquisition and handle checks reject leaf
replacement races. Each inventory runs in a helper process with a 30-second deadline, at most 100,000 files and 1 GiB
of content. The settlement inventory's same deadline includes committed history, limited to 10,000 new commits and
100,000 distinct committed paths, with bounded metadata and Git output reads. Non-regular reference files are refused
without blocking. Timeout terminates the helper and its Git children, with bounded cleanup and stream-drain waits; missing helper support also
marks the audit incomplete. Pattern comparison has its own 30-second deadline and bounded non-backtracking matching.
With `problem` present, counts can be partial and zero outside paths does not establish a clean scope. This is a
workspace and reachable-history observation, not filesystem isolation or proof of which actor made a concurrent
change. Uncommitted changes reverted before settlement and commits no longer reachable from the run branch are
invisible. Git administration itself and changes outside the workspace are not inventoried as changed paths.

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

## Progress claims (0.7.0)

An agent may submit `work_progress` through MCP while carrying its own attempt. `vericairn progress`, its API, and Workbench show its latest claim with the attempt and phase. Interim notes stay in the runtime's local live store; only a completed phase creates a signed record, at most once. Workbench polls these claims every five seconds while its window is open; F5 also refreshes them. Worker liveness continues to come from the harness event stream, independently of these claims.

Hand-over packages retain completed phase records. Interim notes stay local and do not change the package's identity;
resume reports a newly completed phase as an added record.

## Command observations (0.7.0)

The worker reads Claude Code assistant `tool_use` blocks from stream-json and Codex `item.started`, `item.updated`, and `item.completed` JSON events for command execution, file changes, MCP calls and web searches. It ignores assistant prose, tool results and command output. Invocation identity plus a canonical input hash deduplicates lifecycle events; the recent identity cache is bounded to 4096 entries.

Recognizable network commands, structured network URLs and web tools, credential paths, common secret environment variables and secret-store commands, and invocation paths outside the assignment's permitted paths produce worker signals. Common `env` and `sudo` wrapper options do not hide the command. Quoted shell operators and literal variable names are not treated as executable operations. Path checks share the settlement audit's literal-directory and glob rules, normalize traversal and refuse workspace links. Reading the exported brief remains possible. Fixed summaries and hashes are recorded with the run; raw inputs remain in its local output log.

This audit recognizes common commands and structured path inputs. It does not interpret arbitrary programs, shell expansions, aliases or every harness extension, and it cannot prove that no network or credential access occurred. Malformed events and opaque inline scripts raise an incomplete-audit signal. Isolation, credential separation and the settlement scope audit remain necessary.

## Rate and loop observations (0.7.0)

Loop detection compares the tool and canonical JSON input within the last 20 distinct invocations: five identical calls warn, ten are critical. Alternating calls are counted too; duplicate lifecycle events do not increase the count. The window and thresholds are bounded.

Token-rate detection uses monotonic samples covering at least 60 seconds and at least 10,000 reported tokens. A rate above four times the greater of the launcher reference and the goal's recent median warns. It is critical once that anomalous run has consumed 25 percent of its token cap; without a token cap, eight times the reference is critical. Reference rates are configured safety baselines, not measured performance claims: Claude Code 20,000 tokens/minute, Codex 30,000, generic 10,000. Generic launch reads no actions or usage and therefore cannot produce these observations itself.

History is the last 20 successful, fully reported runs of the same launcher and goal, across goal revisions. Its median uses recorded tokens per rounded wall minute; unknown usage and failed runs are excluded. The intent pins the settings and historical reference before launch. Claude message usage is provisional and deduplicated by message id; only its complete final result settles token accounting. Codex completed-turn usage is accumulated. Unknown usage cannot establish a rate and is still charged conservatively under the existing cap rules. Monetary rates are unavailable where launchers report no price; this is not a hard spending limit.

## Attention and containment (0.7.0)

Every new signal kind and severity gets one ask about the run's exact intent, addressed to the goal's decider, and its id is retained with the run. Network attempts, incomplete audits, early loop/rate thresholds and the final scope audit are warnings. Credential-store access, live path violations, the loop stop threshold and critical burn rates request a worker stop immediately. They use the existing stop-file, signal, grace, escalation and whole-boundary containment path. Reservations remain until observed settlement. The final scope audit runs after containment and can only raise attention about changes already made.

Attention writes are retried while observing and at settlement. A failed attention write never prevents a critical stop. If recording stays unavailable, the run retains the observation with no ask id and surfaces show `attention recording pending`; that gap requires reconciliation, not an assumption that an ask exists. The existing silence attention and twice-limit stop retain their own liveness record. Progress claims never reset silence or other worker counters.

`vericairn worker runs`, `vericairn progress`, their API responses and Workbench show the worker signals and attention references. Workbench polls the progress and attention views every five seconds while open. A recorded signal remains visible after the runtime restarts.

## Recorded watching settings (0.7.0)

Goals and rosters hold partial overrides in `limits.watch`. Each field inherits from the launcher, then the current revision of the goal's named roster (or the estate's current roster), then the goal. A goal's role overrides do not remove roster limits. The operator or authorized administrator revises the roster; the goal's existing revision authority controls its limits. Request settings cannot replace these thresholds.

| Field | Launcher default | Recorded range |
|---|---|---|
| `normalTokensPerMinute` | Claude Code 20,000; Codex 30,000; generic 10,000 | 1–1,000,000,000 |
| `burnMultiplier` | 4 | 2–100 |
| `burnMinimumTokens` | 10,000 | 1–1,000,000,000,000 |
| `burnWindowSeconds` | 60 | 1–86,400 |
| `burnCapPercent` | 25 | 1–100 |
| `loopWarning` | 5 | 2–1,000 |
| `loopStop` | 10 | 3–10,000 |
| `loopWindow` | 20 | 3–10,000 |

All fields are integers. The merged loop limits must satisfy `loopWarning < loopStop <= loopWindow`; incompatible partial overrides refuse launch before an intent is recorded. The worker resolves them again after workspace preparation, under the record lock, and pins the resolved values in `target.watch`, the roster revision in `target.watchRoster` when present, and the historical rate in `target.watchHistoryTokensPerMinute`. Later revisions affect later runs.

For example, a roster may record `"limits":{"stallSeconds":300,"watch":{"loopWarning":3,"loopStop":6}}`; a goal may record `"limits":{"watch":{"burnMultiplier":3}}`. Every other field keeps its launcher default. `stallSeconds` follows goal, roster, launcher request setting, then the 600-second launcher default; its range remains 1–86,400.

## Local scope of watching

Remote is not built. WATCH-01 observes only a run launched and contained on the worker's own machine. It adds no remote execution, remote containment, remote record signing, relay, or Remote qualification. The local-network API remains its existing transport and authority model.

Stand-in harness tests exercise the supported event shapes, rate and loop thresholds, attention failures and containment. They do not qualify a real Claude Code or Codex installation; a changed harness event format needs its own qualification. The command audit is an observation of emitted invocation events, which can arrive after an action began. It cannot prevent an already executed action, establish that an unreported action did not occur, or replace an isolation boundary.

## Handing work over

`vericairn handoff` prepares the same hand-over as `GET /v1/handoff`: a generated
view of goals, work orders, assignments, attempts and results, runs, decisions,
asks, next steps, the estate's pin and recorded observations, followed by complete
original records. No actor supplies the recap. Missing decisions or open work
referenced by the records refuse the hand-over and are named together.

Use `vericairn handoff --out handoff.json` to retain its portable package in a
new file. Existing files are never overwritten. `--json` prints that same package;
`--max-bytes <n>` sets a payload byte cap and refuses overflow without dropping
required material. A refused command exits 2 and creates no package file.
Without a cap, the full selected scope is retained and the limit is not set.
The package includes every record held by this copy and its history; its
`withheld` list is empty. Referenced source files, output objects and unrecorded
provider state are outside that selection. The sidecar measures and hashes the
canonical payload; token counts and model-context fit are not claimed. A copy
with the same records and pin produces the same package. Records remain originals,
apart from the deterministic presentation, with synthesis off.

Hand-over uses the same context manifest, source identities, loss labels and
accounting as work briefs. The `vericairn.handoff/1` marker and resume fields stay
as an API v1 compatibility view; `contextFormat` names `vericairn.context/1`.
The original `records show` link is kept for older clients, while
`originalRetrieve` is the pinned, verified `context read` route. New and earlier
retained hand-overs can be compared; changed source identities never rewrite
the old basis. Its human rendering has a separate exact byte count and hash.

`vericairn resume handoff.json` compares that retained basis to the current copy.
Use `--json` for the full machine comparison or `resume -` to read the package
from standard input. It includes added and superseded records, missing or
changed originals, the full pin and observed-state differences. It compares
identities rather than time windows, so an older timestamp on an imported record
cannot hide it. Unchanged records do not prove an unchanged external world.
Resume exits 0 for a valid comparison, 1 when the current view has errors or
missing/changed originals, and 2 for an invalid baseline. It submits nothing and
never rewrites the retained package. Review changes and current authority before
continuing under the existing assignment and original operation identities.

`vericairn status` shows when a person last explicitly observed the estate,
including the observation record and hash, or says unknown. To record an
observation, a person runs `vericairn status --observe --by <principal-id>`.
The runtime supplies the timestamp and snapshot identities; ordinary status
reads, agent reads and repository probes do not update them. Both flags are
required, and an agent cannot author an estate observation. Format-1 estates
must be explicitly upgraded before this write is available.

Remote is not built. HANDOFF-01 provides the runtime, API, CLI and local desktop
Workbench journey only. Moving a saved package between copies is an explicit
file transfer; this work adds no relay, mobile client, Remote transport or
remote-device record signing. It makes no qualification claim for those surfaces,
live external health, or an agent harness's compaction and session recovery.

### Explicit execution and environment admission (RUN-01, 0.7.0)

Every new worker run resolves its model, effort, provider, destination and data boundary before dispatch.
The precedence, from lowest to highest, is the named local launcher configuration, estate roster defaults
and its role policy, goal defaults and its role policy, assignment choices, then explicit run choices.
Each contributing record is pinned by id and hash, including defaults later overridden. Communication
preferences do not participate. All restrictions accumulate: a later choice cannot weaken a required
role model or effort, an exact model revision, permitted providers or destinations, a data boundary or
a currency reservation bound. Existing goal budgets, concurrency, custody and watching limits still apply.

An optional format-2 `execution` policy on a roster, goal, assignment or roster/goal role has this shape:

```json
{
  "choices": {"model": "model-alias", "effort": "high"},
  "restrictions": {
    "requiredModel": "model-alias",
    "exactRevision": "model-revision",
    "permittedProviders": ["provider"],
    "permittedDestinations": ["inference"],
    "dataBoundary": "worker-user",
    "maxCurrency": {"amount": 100, "unit": "USD"}
  }
}
```

Choices omit fields they inherit. `requiredEffort` is also supported. Currency amounts use the same units
as the goal's recorded budget. A bound requires a corresponding reservation in that currency; a soft
spending threshold is still not a guarantee of actual cost. Conflicting restrictions refuse the run.
A model alias remains recorded as requested; an observable resolved revision is recorded separately.
An unobservable revision stays `unknown` and cannot satisfy an exact pin.

A launcher declares its supported scopes, native effort values, providers, destinations, data boundaries,
whether explicit `provider-default` effort is allowed, and whether it can establish a complete environment
inventory. `worker launchers --json` and the API expose these capabilities. Effort is never translated
onto a universal scale. `not-supported` is admitted only when the launcher declares no effort control;
`provider-default` means an explicit choice to leave effort to the provider, not a known effort. Missing
required choices or unsupported values refuse dispatch, without ambient fallback. The Codex command
adapter passes an explicit model and native reasoning-effort configuration; the Claude Code adapter
passes an explicit model and native effort flag. An explicit provider default omits only the effort flag.

`worker run` and `goal step` accept `--configuration <name>` and repeated `--execution name=value` for
`model`, `effort`, `provider`, `destination`, `dataBoundary` and `adapter`. They also accept
`--input-tokens`, `--output-tokens` and `--context-package <sha256:...>`. For example, the execution part
of a run command can be `--execution model=model-alias --execution effort=high`. These are choices,
not permission to change an inherited restriction. The runtime host supplies named `RunConfiguration`
entries through `WorkerOptions.Configurations`, or a qualified launcher supplies its own configuration.
Each has an explicit model catalog, choices/restrictions, local settings and an environment admission
policy. Naming an unknown configuration is refused. There is no shipped ambient model configuration.

The complete environment inventory covers the harness, tools, skills, plugins, hooks, MCP servers,
shell helpers, dependencies, and effective user and repository configuration. Preflight must account for
every loaded component and route and establish that its children inherit the same environment limits.
Each component has a portable id, kind, version (or `unknown`), content hash, effective-configuration hash,
required environment-variable names, destinations and dependency ids. Every dependency must be present;
every route must be the selected inference destination, `estate` or `local`. The observed inventory must
match the configured admission policy exactly; an installed or discovered component grants no authority.
The worker filters inherited variables, including credentials and language/runtime loader directives,
and refuses a launcher that reintroduces an unadmitted variable. Records contain identities, hashes and
variable names only. Local settings, machine paths, login material and environment values stay local.

Selection and inventory are checked before the intent and again immediately before dispatch. A change
requires fresh admission. A refusal before the intent writes no run or routed assignment. A change found
after the intent but before launch settles an unstarted failure with zero usage. The intent's `execution`
snapshot is immutable and includes the requested choices and provenance, configuration identity, resolved
revision, harness version, role, `adapter: disabled`, environment inventory, allocation and context-package
identity. Missing allocation counts and package identity stay explicitly unknown. RUN-01 records identity
and allocation only; CONTEXT-01 owns package construction, withheld content and measurements.

Settlement's `executionObserved` keeps distinct harness reports, discrepancies and still-unknown facts.
The worker never fills missing actual model, effort, revision, provider, destination, version or inventory
facts from its request. Reports are bounded and portable; unsafe or excessive reports leave an explicit
incomplete-report observation. A discrepancy remains even if a later report returns to the requested
choice. WATCH-01 receives the admitted selection and inventory: execution discrepancies and unadmitted
tool invocations produce critical signals and use its existing attention and stop path. All original
command, credentials, network, scope, loop and burn-rate auditing remains in place.

### Native inventory qualification (RUN-02)

Qualification belongs to a launcher and an exact version range, not to a harness name in general:

| Launcher | Qualified version range | Admission evidence |
|---|---|---|
| `codex` | `0.159.0-alpha.12.1` only; both endpoints are that exact version | Exact `--version` response; root, exec and MCP-list help; effective feature and MCP probes in the prepared run environment; executable and entry-file hashes; configuration and instruction inspection. |
| `claude-code` | `2.1.296` only; both endpoints are that exact version | Exact `--version` response and documented flags in `--help`; fixed built-in tool selection; fresh login-only home; no external setting sources; strict, verified explicit MCP file; configuration and instruction inspection. |
| `generic` | None | No complete inventory implementation. Worker dispatch stays refused, before executing its command. |

A missing flag, failed or malformed probe, missing required feature, unexpected enabled feature, unlisted version,
extra MCP server (even disabled), altered transport, duplicate MCP field or unaccounted MCP option refuses admission.
Help must declare the short aliases used by the commands too: Codex's `-c` and Claude Code's `-p`.
The generic launcher cannot inherit either native launcher's qualification. No version before or after the two exact
versions above is implicitly qualified, including another Codex alpha build.

Codex's qualified built-in inventory is `exec_command`, `write_stdin`, `apply_patch`, `view_image`, `update_plan`,
`request_user_input`, `request_user_input_async`, `send_message_to_user_async`, `clock.curr_time`, `tool_search`,
`list_mcp_resources`, `list_mcp_resource_templates`, `read_mcp_resource`, `create_goal`, `get_goal` and `update_goal`.
It includes native MCP discovery and the model-conditional clock and user-input utilities; model and mode restrictions
can hide their schemas on a particular turn. The inventory identifies the built-in capabilities, not a guarantee that
every tool is callable on every turn. See the [Codex tool registry](https://github.com/openai/codex/blob/main/codex-rs/core/src/tools/spec_plan.rs).
The launcher requires
`goals`, `shell_tool`, `unified_exec` and `view_image` enabled, disables web search, ignores exec-policy rules, and
explicitly disables these extension/tool features:

`apps`, `plugins`, `remote_plugin`, `hooks`, `multi_agent`, `multi_agent_v2`, `browser_use`, `browser_use_external`,
`browser_use_full_cdp_access`, `computer_use`, `in_app_browser`, `image_generation`, `code_mode`, `code_mode_host`,
`code_mode_only`, `memories`, `workspace_dependencies`, `skill_search`, `skill_mcp_dependency_install`, `tool_suggest`,
`sleep_tool`, `shell_snapshot`.

The schema-defined `skills.bundled.enabled=false` also prevents bundled skill installation and discovery; it is set
in both the generated file and session overrides. This prevents a fresh home from gaining an unlisted bundled skill
at startup. See the [Codex configuration schema](https://github.com/openai/codex/blob/main/codex-rs/core/config.schema.json).

The effective feature listing must confirm each required state. The remaining non-tool feature allowlist is explicit
in [CodexInventory](../src/Vericairn.Packs.Software/CodexInventory.cs); the entire observed feature table and help
identity contribute to the inventory hash. Only `auth.json` is imported into `CODEX_HOME`; the launcher generates
`config.toml`. Preflight permits only those files and empty probe scratch directories. It compares the generated
configuration exactly and refuses detected system configuration or managed preference roots. Feature probes use the
same overrides as dispatch, after preparation; the ambient user feature configuration is never the qualification.
See the [Codex CLI flags](https://learn.chatgpt.com/docs/developer-commands?surface=cli) and
[configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference).

Claude Code's `--tools` selects exactly `Bash`, `Read`, `Write`, `Edit`, `Glob`, `Grep`, `TaskOutput`, `TaskStop` and
`EndConversation`. The inventory also identifies the built-in goal evaluator, the `vericairn` MCP server and the
`claude-code-setting-sources-none` configuration. It verifies the presence of every option it relies on: `--print`,
`--output-format`, `--verbose`, `--permission-mode`, `--mcp-config`, `--strict-mcp-config`, `--setting-sources`,
`--settings`, `--tools`, `--model`, `--effort`, and `--max-budget-usd`. All must be declarations in help, not prefixes
or mentions in prose or another option's description. No MCP health command is run: strict configuration is established from the documented flag
and the exact explicit server file, without starting its server. See the
[Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference) and
[tools reference](https://code.claude.com/docs/en/tools-reference).

Both preflights inspect the workspace, nested directories, ancestors and a linked worktree's common checkout.
Claude Code refuses `.claude` settings, hooks, skills, plugins, agents, commands and other customization entries, plus
`.mcp.json`. This qualification conservatively refuses those sources even where an individual file could be excluded
by a flag. Codex refuses `.codex` project configuration and `.agents` customization sources, including separate user
`.agents` skills outside `CODEX_HOME`. Uninspected links, inaccessible discovery roots or an inspection exceeding
100,000 entries, 128 instruction files or 30 seconds also refuse. A customization directory containing only recognized
instruction files is allowed. `AGENTS.md`, `AGENTS.override.md`, `CLAUDE.md` and `CLAUDE.local.md` are hashed and
recorded as `configuration` components with version `content`, never as tools or executable authority. Readable
instruction-file links are permitted. Refusals name discovered configuration using logical relative locations.

The operator's `RunEnvironmentPolicy` must still exactly match the established inventory; observing it does not grant
permission to admit it. The worker records its components, canonical `inventoryHash` and harness version in the intent.
It repeats the entire preflight immediately before dispatch. Native preflight also checks that its evidence did not
change while probing. The additive `RunPreflight.LocalBindingHash` compares command arguments, entry files, explicit
MCP content and environment values locally; it is never copied into the intent or any other record. Thus machine
bindings cannot silently change while the portable inventory remains the same. Any difference after intent refuses
dispatch and records an unstarted, zero-charge failure.

This is a qualification of the launcher inventory protocol using stand-in executables, not a live-agent or sign-in
qualification. Real model calls, provider/account-dependent behavior and remote managed policy have not been qualified.
The executable and entry-file hashes must be explicitly admitted by the operator; version output is not vendor
attestation. Environment and filesystem inspection is not an operating-system sandbox or a hard network firewall.
Programs deliberately invoked by a shell tool and changes made after the dispatch boundary remain subject to the
existing `worker-user`, workspace-audit and process-containment limitations. Cross-platform release qualification
still requires the builder's three-platform CI run. API v1 stays additive, record format 2 is unchanged, and RUN-02
adds no MCP tools.

The efficiency Adapter remains disabled and has no implementation here. Any request to enable it is
refused as unsupported, even if a later scope selects `disabled`. EFF-01 belongs to 0.8.0. Remote is not
built: RUN-01 adds no remote launch, environment admission, observation, signing or qualification.
