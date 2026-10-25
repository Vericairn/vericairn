# The MCP server

The 0.8.4 development build adds standard `prompts/list` and `prompts/get`, plus
library management tools. Offers are grouped by category and filtered for the
connected principal. `prompts/get` records a use with a new assignment, run or
ask answer before returning its message. See [MCP prompts](prompts.md#mcp-prompts)
for the required target and exact-definition arguments.

From 0.8.3, `release_submit_check` is a read tool available without an admitted
signing device or presence. Required inputs are `product`, `version`, `by` (the intended submitter),
`commit`, `expected` (the product hash) and `clientBuild` (the full submitting client build identity).
Optional inputs are `provider`, `repository`, `ref`, `workflow`, `settings` and `reconciliation`, as on
submission. It returns the API's `ReleaseResponse` with `check.objections`, each naming a reason and
`nextAction`, and marks a refused check as a tool error. It reports all admission objections, signs
nothing, creates no record or retained object, calls no release provider and never fetches. A passing
check grants no authority; submission evaluates again. See [API v1](api-v1.md#release-submission-check-083).

Vericairn 0.8.0 adds `policies_list`, `policy_show`, `policy_kinds`,
`policy_evaluate`, `writing_exceptions_list` and `writing_exception_show`.
They return the same basis, findings, duty evidence and writing locations as the API.
`release_basis_get` reads a retained release-admission evidence hash, including the original
source bytes, duty findings and exact proof references. It creates no record and grants no authority.
`work_result` preserves optional `duties` with their exact scope and proof bindings; those submissions
require `expected`, the current attempt hash. The operator-only `work_review` tool takes `result`,
`expected` (the current result hash) and the ordinary review `body`, including exact `read` references.
The server checks current operator roles and device admission when listing and calling `work_review`;
workers cannot list or call it. The runtime signs as the connection principal and validates the evidence and review independence.
Neither tool accepts work or a release. See [duty evidence transport](policies.md).
`policy_evaluate` accepts optional `asOf` and `release` preview inputs. Reads create no record
and offer no release acceptance operation. See [Inspecting estate policies](policies.md).

`vericairn mcp serve` exposes an estate to a coding agent over the Model Context Protocol: newline-delimited JSON-RPC 2.0 on standard input and output, the stdio transport used by compatible agent harnesses. The server is a client adapter over the estate API: the adapter itself opens no live store, holds no key of its own and adds no permission; every tool is a call through the daemon when one serves the estate, or through the embedded runtime otherwise, and the runtime evaluates authority behind each call exactly as it does for the command line. When a daemon serves the estate, the server stays its client and never a second owner of the estate's live state. Without one, `mcp serve` holds the embedded runtime, and with it the live state, for its whole session, and a daemon started meanwhile does not detect it, so start the daemon first (`vericairn runtime start`) when other clients use the estate at the same time.

## Discovery and dnx

From 0.8.2, the `dotnet-vc` package includes the `McpServer` package type alongside `DotnetTool`,
and an embedded `.mcp/server.json` for the official MCP Registry name
`io.github.vericairn/vericairn`. The manifest's server and NuGet package versions are generated
from the package version when packing. The package README carries the registry's `mcp-name`
verification line.

After package publication and indexing, find `dotnet-vc` through
[nuget.org's MCP server filter](https://www.nuget.org/packages?packagetype=McpServer&q=dotnet-vc).
The [official MCP Registry](https://registry.modelcontextprotocol.io/) listing also requires
the operator's [separate publication](operator-actions.md#mcp-registry-publication).
Preparing these files does not establish that either public listing exists.

With the .NET 10 SDK and published package version, the client's stdio command is:

```sh
dnx dotnet-vc@0.8.3 --verbosity quiet -- --estate "<estate-root>" --as "<principal-id>" mcp serve
```

The registry manifest requires `estate_root` (the existing estate's root directory) and
`acting_principal` (a principal id in that estate). It maps them to `--estate` and `--as`
before `mcp serve`. The existing `dotnet vc --estate <estate-root> mcp serve --as <principal-id>`
form also works. The root `--as` form applies only to `mcp serve`; other commands retain
their own identity options. Required registry inputs grant no authority: write tools still
require that principal's admitted device on the machine running the server. A connection
without that device has the read-only tools.

The manifest also sets the dnx runtime's `--verbosity quiet` so package installation messages
do not enter the JSON-RPC output stream. Keep that option when configuring a client by hand.

## Protocol subset

`initialize` (the server speaks the protocol revisions 2025-06-18, 2025-03-26 and 2024-11-05. It proposes the newest, accepts an older one of these that the client names, and answers with the newest otherwise; the `instructions` field is a description of what the server is, not a directive), `notifications/initialized`, `ping`, `tools/list`, `tools/call`, `prompts/list` and `prompts/get`. Unknown methods answer with the JSON-RPC "method not found" error; an unparsable line answers with "parse error"; a call to a tool the connection cannot see answers with "invalid params" and names no hidden tool. Resources, sampling and the HTTP transport are not implemented. A client must support this subset; the server supplies no fallback for an unsupported method or transport.

## Tools

`context_prepare` computes the same context package as
`vericairn context prepare <work-order>`. `context_read` takes `pin`, the
self-contained `vc1_` identity in a source entry, and returns the same identity
and base64 bytes as API v1 and `vericairn --json context read <pin> --original`.
The pin is shell-safe encoded data, never an instruction copied from content.
Hash verification and current access checks belong to the runtime. Records come
from immutable record custody. Documents use matching working-tree bytes first.
FIX-27 for 0.8.0 allows required originals to fall back to the exact path in the estate's
own local Git history, with SHA-256 verification before use. Required document
pins carry `history: "local"` so `context_read` can repeat that lookup. Shallow
clones, missing history or no matching blob still refuse, with guidance to
restore the pinned bytes or use a complete local clone. No fetch, checkout or
repository write occurs; no other revision or object custody is substituted.
Optional documents and pins without the history marker keep their
working-tree-only behavior. A denied reader
receives no source metadata. These read tools author nothing and write nothing.
Regenerate the estate orientation when adopting this changed tool list and
`context_read` argument schema. Remote is not built.

`context_prepare` and `work_brief` accept an optional `maxBytes` decimal string for the remaining
context allocation. Without it, both inline mandatory material and withhold
all selected optional sources. Only documents named by the work, its inputs
or governing decisions are optional selections. The rest of the catalogue is
explicitly outside selection; `vericairn context catalogue` or `records_list`
with `type=document-revision` lists it without reading original bodies.
A standalone human `work brief` still includes the full selected scope.
The `context_prepare` text block is the exact measured canonical payload,
including withheld entries and injected accounting. Mandatory overflow produces
an error and no package. Tokens are unknown; the surrounding MCP transport and
the model provider's hidden framing are not measured as model input.

`work_brief` now includes the same context content, its complete withheld list
and readable mandatory originals after its lead. COMMS-01's `recipient` and
`assignment` arguments resolve that lead and communication section. The entire
resulting text, including those additions, has an exact UTF-8 byte measurement;
personal depth does not change canonical context content. The work brief and `context_prepare` compute and return artifacts without
writing anything. Only worker dispatch retains a copy, in the run's local
directory under the Vericairn home. Unavailable required
material refuses the brief and sets the MCP error flag.

Reads are visible to every connection and return what the records say: `estate_health`, `estate_status` (the canonical status document), `estate_check` (validation and repository checks), `estate_progress` (the progress report as `vericairn progress` prints it, or with `form` `json` its canonical JSON), `work_brief` (the brief as Markdown, with the latest result's next step), `work_observe`, `record_get`, `records_list` (the current records of one type, as compact rows), `decisions_list`, `decision_get`, `releases_list`, `devices_list`, `documents_due` (the living documents that are due or unavailable, with their levels and reasons), `goal_get` (a goal in the machine form of `vericairn goal show`, or without an id the estate's goals with their status), `goal_progress` (a goal condition by condition: each verdict and its reason, the current evaluation, the steps and what still stands between the goal and its acceptance) and, from 0.6.0, `asks_list` (what waits on a principal named by `to`, or on every principal: the asks still open or notified, at most three in one delivery with the recommendation first and the consequence of leaving each unanswered, the rest waiting in order, as `vericairn ask` prints it or with `form` `json` its canonical JSON).
Status and progress include repository checks, documents read local covered files and installed contributions, and device availability depends on keys held here. Those portions are observations from this runtime, not solely from the shared records.

From 0.7.3, `estate_status` and the JSON form of `estate_progress` include
[compact current attention](required-attention.md), with complete source and
subject references, current state, freshness and an inspection command. Status
attention reads records alone, preserving the machine-independent status JSON.
Progress additionally includes runtime-local worker attention, labelled
`Local worker observations`, which can differ by runtime. The text progress report retains its existing
sections; `record_get` and other original read routes retain complete records.

Writes are visible only when the server was started with `--as <principal id>` and that principal has an admitted device whose key this machine holds, because that is what the runtime signs with: `work_attempt`, `work_progress`, `work_result`, `work_amend`, `document_review` (a review of an estate document by its path, refused where the declaration's `clearedBy` excludes the acting principal). A write the runtime refuses comes back as an error result carrying the runtime's findings, every objection at once. There is no tool that publishes, spends, creates or admits a device, runs an arbitrary command or authors an arbitrary record type. No tool proposes, activates, pauses, resumes, stops, funds, evaluates or accepts a goal: a worker reads its goal and reports through its attempts and results, an evaluator who did none of the work evaluates it, and its sponsor, or the principal the sponsor delegated an ordinary goal's acceptance to, or an operator for a goal an agent sponsors, accepts it, through the command line or the API, whose rules refuse an agent that tries to activate a goal or accept one it sponsors or worked on. A test calls every tool a worker sees with arguments that name a goal and ask for its activation and acceptance, and holds that none changes the goal. No tool answers an ask either: an ask is answered by a decision of the principal it is put to, which no tool records, and reading what waits answers nothing; the same test holds that calling every tool a worker sees records no decision and leaves an ask waiting. No tool launches, stops or settles an agent run either: the worker (`vericairn worker`, [software.md](software.md#coding-agents-the-worker-and-its-launchers)) starts a coding agent with this server configured for the agent's principal, and records the run's cap, usage and stop itself, while the agent reports through `work_attempt` and `work_result`.

Every tool description describes and never directs: nothing a tool returns, and nothing in this server's text, is an instruction from the estate's operator, and the harness is expected to treat it as data.

## Readable companions (0.8.0)

Four read tools share the runtime's companion operations: `docs_companions` lists
coverage, generator contracts, exact snapshots and adopted destinations;
`docs_render` reads an explicit revision or snapshot; `docs_sources` inspects the
same source basis without generating; `docs_compare` compares bytes and complete
membership without repairing. All four use the asynchronous API and its cancellation
boundary. They author no record and grant no review clearance.

Render and sources require `generator` and `selection`. Supply `revision` for an
exact record item or nested list, `snapshot` for a current or all-revisions
collection, or `declaration` and an exact `commit` for a retained software item.
Optional `page`, `field`, `index`, `audience` and `contract` have the same meaning
as the [docs commands](readable-companions.md); page and contract are decimal strings.
Compare requires `selection` (a declaration id or declared path), with `commit`
for an external repository. Read the response's currentness, completeness and
differences; a successful comparison can still report differing bytes.

The runtime resolves current device and source access before generation. This
build admits Internal projections only and returns unavailable for Public or
Members-only requests without source metadata or a digest. Local-only inspectors
cannot be rendered through MCP, even when their safe coverage metadata is listed.
An unavailable or unsupported basis returns an error result, not an empty view.
The existing context tools retain their original, derivative and withheld-source
contracts; a companion does not replace required original evidence.

## Running it

After 0.8.3 is published and its NuGet publication workflow succeeds, with the .NET 10 SDK installed:

```sh
dotnet tool install --global dotnet-vc --version 0.8.3
dotnet vc --estate <estate root> mcp serve --as <principal id>
```

Claude Code: `claude mcp add vericairn -- dotnet vc --estate <estate root> mcp serve --as <principal id>`. Other harnesses use executable `dotnet` with `vc` as the first argument, followed by the arguments above. The self-contained archives still use executable `vericairn` with no `vc` argument. The worker writes that configuration for each run itself, outside the workspace: one server, `vericairn`, with the `--as` of the run's agent and the variables it needs to find the agent's key, given to Claude Code as its only MCP server (`--mcp-config` and `--strict-mcp-config`), to Codex on its command line (`-c mcp_servers.vericairn...`). Generic worker dispatch is refused because it cannot establish a complete inventory. Diagnostics go to standard error; the output stream carries only protocol messages. `dotnet vc mcp orientation --out <directory>` writes `AGENTS.md` (from the estate's identity, its qualifications of agent harnesses and the tool registry: what the estate is, the rules it enforces, how to connect, the tools and an honest agent matrix, and nothing that changes with each record, such as work orders or the pinned release, which `dotnet vc status` shows) with `CLAUDE.md` and `GEMINI.md` as pointer files that carry nothing of their own; regenerate them rather than editing, and `--check` names every copy that is missing or differs from a fresh generation. Vericairn's own repository holds its copies to the generator with a test.

## Qualification

The transport profile (stdio, JSON-RPC 2.0, the protocol revisions above, authentication by the operating-system identity of the process plus the acting principal's admitted device for writes) has two test paths: the in-process conformance test in `tests/Vericairn.Cli.Tests/McpTests.cs`, which drives initialize, tools/list and tools/call over the stream framing in the three-platform workflow, and a different peer, `tests/mcp-peer/mcp_peer.py`, a Python script driving the built binary end to end over the real stream framing (`python3 -I tests/mcp-peer/mcp_peer.py artifacts/bin/Vericairn.Cli/release/vericairn.dll <estate root>`; the brief check reads work order MCP-01 where the estate holds it, as Vericairn's does, otherwise the first work order the estate's status lists, and is skipped in an estate without work orders, the output saying which; the command line's tests run the peer against fresh estates wherever Python 3 is on the path). Every agent harness is a non-claim in the orientation file's matrix until the estate qualifies it: a `qualification` record (available from record format 2) about the release the session used, whose profile is `<harness> over MCP` with the harness named as the matrix names it (for example `Claude Code over MCP`) and whose evidence is the recorded observation of the session. The harness's row then shows the outcome of its latest current qualification and the version of the release it is for, and a harness the matrix does not list by default gets a row of its own. Regenerate the files after a qualification is recorded; `--check` names the copies it leaves out of date. An estate at record format 1 holds no qualification records, so its matrix stays all non-claims.

## Limitations

- The five write tools are limited to attempts, progress claims, results, amendments and reviews of the estate's own living documents. Goals and asks are read views here; their lifecycle and decisions use the CLI or API under its authority rules.
- The server trusts the operating-system identity of whoever runs it, as the command line does; it is not a network service and must not be exposed as one.
- Long outputs (a large status document, a long brief) are returned whole; no paging.

## Agent progress claims (0.7.0)

`work_progress` takes `attempt`, a stable lower-case `phase`, `note`, and `state` (`active` or `completed`). It is available to an admitted contributor and publishes only that contributor's own attempt. Active notes replace local live state; completion creates one signed `work-progress` record for the phase. Retrying the identical completion returns that record. A completed phase cannot be reopened or recorded twice. A result closes further progress submissions, except an identical completion retry returning its existing record. Notes must pass the same portable-content checks as records.

`estate_progress` includes the latest claim for each attempt, labelled as a claim. It is never evidence of liveness, permitted scope, or acceptance. Interim notes are local to this runtime and survive its restart; another machine sees recorded phase completions only.

From 0.7.0, `estate_progress` also shows worker-observed command, rate and loop signals with their severity and attention records. These observations come from the worker's harness-event audit, not from `work_progress`. No progress tool changes a stop, a budget, an acceptance, or the worker's liveness clock.

Watching thresholds are recorded in goal and roster `limits.watch`, with bounded integer fields and goal-over-roster-over-launcher precedence. MCP progress notes cannot change those thresholds or the worker's observations. The run intent pins the settings and roster revision current at launch; [the software guide](software.md#recorded-watching-settings-070) gives defaults, ranges and examples.

Remote is not built. These tools expose claims and observations available to this estate runtime; WATCH-01 adds no Remote execution, observation or signing support.

## Communication read (COMMS-01)

The additive `communication_get` tool takes optional `recipient` and
`assignment` ids. It returns the effective progress frequency and four depths,
all contributing revisions and authors, conflicts and freshness. No recipient
means estate defaults, without inventing a personal choice. It is visible on
read-only connections and writes nothing. There is no MCP setting-write or
delegation tool. Unsupported runtimes return an explicitly unavailable profile.

`work_brief` accepts optional `recipient` and `assignment` ids. Its complete
original brief remains present, with the effective communication values and
their sources in a separate section. A worker's exported brief captures the
same values and asks it to refresh them at the next settled boundary.

This changes the MCP tool list. Regenerate `AGENTS.md` with
`vericairn mcp orientation` from the updated runtime; pointer files retain their
usual role. Remote is not built and is not qualified by this tool.

## Explicit run execution (RUN-01)

RUN-01 does not change the MCP tool list. No tool selects a model, enables the efficiency Adapter,
launches a run or expands a tool inventory. The worker reads the recorded roster, goal and assignment
execution policies and admits explicit run overrides through the existing CLI/API authority boundary.
`record_get` exposes the intent's immutable `execution` and settlement's `executionObserved`, including
selection provenance, admitted component identities, discrepancies and unknown facts. `estate_progress`
retains WATCH-01's execution and unexpected-tool signals without filtering the original command audit.
MCP servers and their dependencies are themselves part of the launcher's complete environment admission.

The stock CLI and daemon load machine-local model and effort defaults and optional allowlists for each
native worker run, including agent goal steps. Set or show them on the worker's machine with
`vericairn worker configure --launcher codex --model <model> --effort <effort>` (or `claude-code`);
omit model and effort to show it. FIX-18 adds Codex's `max` effort for 0.7.2; the
[launcher guide](software.md#explicit-execution-and-environment-admission-run-01-070) lists the vocabulary and limits of validation at launch.
Repeated `--allow-model` and `--allow-effort` options narrow the choices
and must include their defaults. Configuration is per machine and is never recorded in the estate.
The owner-only files stay under `VericairnHome`; reads and writes are refused if that home is inside a
Git repository or estate. If the user's home directory is a repository, set `VERICAIRN_HOME` outside all
repositories and estates for the CLI and daemon before starting them. Configuration narrows and never
widens the qualified inventory. It cannot grant tools, MCP servers, environment variables or routes:
native inventory admission still requires
the shipped baseline or a matching current harness qualification. No MCP tool is added or changed;
there is no configuration write tool, and configuration does not launch an agent or run probes.
FIX-12 adds the separate machine-local setting
`vericairn worker configure --workspaces <absolute-directory>`; omit arguments to show its root.
Choose a root outside repositories, estates and a home holding harness configuration. The root and its
ancestors cannot contain `.claude`, `.codex`, `.agents` or `.mcp.json`. This setting has no MCP or HTTP route,
applies to every launcher, is reread for each run and is never recorded. It does not remove Codex's separate
check of the user's own `~/.agents` customization; use a worker account without that discovery root.
The native scan permits an empty ordinary directory or recognized instruction files alone, but refuses
skills, other entries and uninspected links. Every run also rechecks its assignment's current holder before preparation
and intent; an assignment displaced by hand-over cannot launch explicitly or through routing.
Harness qualification also remains outside the MCP tool list. Its API operation is machine-local:
paired local-network devices are refused before probes; local socket and signed loopback requests
retain the existing human authority checks.

`estate_check` keeps signature verification and machine-path checks. FIX-12 exempts only validated device
public-key and envelope signature values from credential-pattern matching, not other fields or invalid
encodings. Settlement withdraws still-waiting process attention asks and pins those withdrawals to the exact
settlement, preserving observations and leaving answered asks answered. Closed asks no longer report a stale
subject. New `attempt-missing` and `scope-settlement` asks remain open for review at that exact settlement;
reads and progress claims never answer them. An unreadable recovery journal appears as `batch.unreadable`
and holds record writes, including these MCP writes, with `reconcile-required`; interim live progress stays
available. Single records are judged before publication, just as grouped records are.

Only portable identities, hashes and variable names enter these records; MCP configuration paths and
secret values stay local. CONTEXT-01 owns the context package; RUN-01 records its identity and allocation.
The efficiency Adapter is disabled and unsupported; EFF-01 remains unbuilt. Native worker
dispatch requires a complete inventory and either the exact codex-cli `0.159.0-alpha.12.1` or Claude Code
`2.1.296` baseline, or a current matching human RUN-03 qualification. This harness admission is separate from
the release-specific MCP matrix above; the admission protocol alone establishes no live-agent or sign-in
qualification. FIX-18's five live Codex runs exercised max effort, tools and corrected audits; the fifth
recorded its cap, execution, usage, signed attempt/result and permitted file change through estate-only MCP
pre-approval. The baseline's MCP list omits approvals and tool lists, so that approval check rests on the exact
generated configuration and isolation checks. Other accounts, versions and platforms remain unqualified
by these runs. In 0.7.3, FIX-19 gives the workspace its own repository and adds only its `.git` to Codex's
writable roots. The seventh live run committed on macOS and settlement fetched its audited branch into
the registered clone; this live qualification is macOS-only. FIX-20 requests a stop for recognized
credential and outside-write commands, while null-device output is exempt and unverified effects remain
warnings. The audit is a signal, not an enforcement boundary. UPDATE-01 does not initiate release checks
through MCP; `estate_check` can report the cached `manifest.release-behind` warning. FIX-21's certificate
preparation and handshake diagnostics apply when MCP reaches a daemon through TLS. The stock
CLI/daemon setup and admission checks are described in [software.md](software.md#explicit-execution-and-environment-admission-run-01-070).
Remote is not built or qualified by these features.
