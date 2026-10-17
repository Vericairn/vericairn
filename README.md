# Vericairn

Vericairn is a local-first creation and operations platform: it helps a person or organization turn an
intention into an understood, authorized, produced, reviewed, delivered and maintained result, with its
decisions, sources, relationships and history intact.

This repository holds the Community edition (Apache-2.0): the record format, the runtime and its daemon, the
`vericairn` command-line interface, the SDK and the Workbench desktop. The Remote client is not implemented here.

The 0.7.2 source includes five fixes after published 0.7.1. FIX-13 orders identifiers within one process,
checks release documents at the exact build commit, groups accepted notes by work order, settles process
attention asks, checks workflow action pins and NuGet assets on pull requests, and reduces repeated Workbench
first-screen reads. FIX-14 binds restored files to an export's inventory (F-01). FIX-15 claims release dispatch
before the provider call and bounds Git and gh processes. FIX-16 validates records before publication and
keeps unreadable recovery journals visible. FIX-18 corrects the Codex worker at max effort, its tool host,
execution observations, command audit and estate-only MCP approval. The [0.7.2 documentation set](docs/releases/0.7.2/README.md)
describes the changes and their remaining limits.

The current published release is [0.7.1](https://github.com/Vericairn/vericairn/releases/tag/v0.7.1), published on
October 17, 2026, following 0.6.1. The 0.7.0 candidate was withdrawn
before publication by decision `01M53S6DYFCPJMG0AQRSN8ZFSP`, recorded in withdrawal
`01M53S6FN1GANBY5HSNDKE5N08`. Its frozen set remains indexed; 0.7.1 carries its work with FIX-12. This includes
FIX-09's bounded loopback authentication and recoverable notes window, and FIX-10's deterministic permitted-path
matching and independent audit checks. FIX-11 adds machine-local worker configuration, restricts harness
qualification to the runtime's own machine and completes the generated CLI option reference.
FIX-12 adds a machine-local workspace root, enforces hand-over for explicit worker runs, and keeps validated
public keys and signatures out of credential-pattern matching. The 0.7.1 command-line tool and SDK packages are
available on nuget.org; [the release index](docs/releases/README.md#nuget-publication-setup) records their
Trusted Publishing run and the manual workflow path used after publication.

The native worker baselines are codex-cli `0.159.0-alpha.12.1` and Claude Code `2.1.296`; another exact version
needs a person's authorized RUN-03 qualification. Generic worker dispatch is refused. Before a native run,
configure that machine's worker with `vericairn worker configure --launcher codex --model <model> --effort <effort>`
(or `--launcher claude-code`); omit model and effort to show it. Repeated `--allow-model` and `--allow-effort`
options narrow the permitted choices and never widen the qualified harness inventory. FIX-18 adds Codex's `max`
effort in 0.7.2; the harness validates model/effort support at launch, and an observed refusal is recorded as a
failed run. Configuration is per machine and is never recorded in the estate. Its Vericairn home must be outside
every Git repository and estate;
if your home directory is a repository, set `VERICAIRN_HOME` elsewhere for the CLI and daemon before starting them.
If your home holds `.claude` or `.codex` settings, move workspaces outside it with
`vericairn worker configure --workspaces <absolute-directory>`; `vericairn worker configure` shows the root.
The root applies to every launcher, must be outside repositories, estates and configured ancestors, and is
reread by the CLI and daemon for each run. New roots are owner-only. The harness configuration scan remains strict.
Codex also refuses the user's own `~/.agents` customization, independently of workspace placement or
`CODEX_HOME`; use a worker account without that discovery root. An empty ordinary directory or one containing
only recognized instruction files is allowed by the native scan; skills, other entries and uninspected links are refused.
Harness qualification is machine-local too: paired local-network devices are refused. The
[software guide](docs/software.md#explicit-execution-and-environment-admission-run-01-070) describes the setup and
admission checks. The efficiency Adapter
is absent until the planned 0.8.0 work, Remote is not built, and context tokens remain unknown without a
qualified local tokenizer. Spending and containment limits remain as described in the guides.
Five live Codex runs exercised the corrected path; the fifth recorded its cap, model and max effort, usage,
signed attempt and result, and the permitted file change. A worker-launched Codex run cannot yet commit:
its git worktree metadata is outside the sandbox. Changes stay uncommitted in the worktree for review;
FIX-19 makes the workspace a repository of its own. These observations qualify only the reported path,
not other harness versions, accounts, platforms, provider billing or acceptance of the work.

## Releases

The published releases are on the [Releases page](https://github.com/Vericairn/vericairn/releases). Each
carries the command line's archives for Linux, macOS and Windows, `SHA256SUMS`, a software bill of materials and
the third-party notices; from 0.3.1 a release also carries Workbench's archives and its documentation set. The
sets, from 0.2.0 on, are also kept under [docs/releases](docs/releases/README.md): the overview, installation
and cold start, the operator's guide, the generated command-line, API and record references, frozen copies of
the maintained documents, the release notes and known limitations. The index there also names the versions
withdrawn before publication and why; their sets stay as the record of what was built and reviewed. The
documents at `docs/` are the maintained drafts. The published 0.7.1 set carries 0.7.0's work and FIX-12;
the earlier sets remain frozen, including 0.7.0's withdrawn candidate.

For 0.7.1, the command line is qualified on `linux-x64`, `osx-arm64` and `win-x64`, where the release's tests ran.
The `linux-arm64`, `osx-x64` and `win-arm64` archives are provided but unqualified. Workbench is not qualified on
any platform. The archives are unsigned developer builds, without code signing or notarization; verify downloads
as the [install guide](docs/releases/0.7.1/install.md) describes.

Nothing here is a release until a release record in the Vericairn estate says so. The estate records every
release and pins the one it runs on.

## Install the command line and SDK

The 0.7.1 NuGet packages are available. Install the command line with the .NET 10 SDK on Linux, macOS or Windows:

```sh
dotnet tool install --global dotnet-vc --version 0.7.1
dotnet vc version
dotnet vc doctor
dotnet vc records describe
```

The global tool directory must be on PATH, as the installer describes. A repository can instead keep a
local tool manifest; see the [0.7.1 install guide](docs/releases/0.7.1/install.md#9-nuget-command-line-and-sdk).
The tool is framework-dependent; the self-contained archives continue to ship `vericairn` for machines without the SDK, and the macOS archives
also carry the Secure Enclave helper. The NuGet tool does not bundle that helper or Workbench.
The worker's agent-side MCP command still resolves `vericairn`; keep the archive executable on PATH for worker
runs. A NuGet-only worker installation is not qualified by the package installation checks.

In a .NET 10 project, install the SDK with:

```sh
dotnet add package Vericairn --version 0.7.1
```

`Vericairn` brings `Vericairn.Sdk`, `Vericairn.Protocol` and `Vericairn.Records`, preserving their namespaces
and assemblies. `Vericairn.Runtime`, `Vericairn.Daemon` and `Vericairn.Mcp` are also available at 0.7.1;
the SDK front door does not embed a runtime or daemon. See the [SDK guide](docs/api-v1.md#the-sdk).
The packages contain Community code only.
Commands written as `vericairn ...` in the archive documentation take the same arguments as `dotnet vc ...`.

## What is here

- `Vericairn.Records`: record formats 1 and 2 (canonical JSON, ULIDs, content hashes, signed records, devices
  and admission, estate loading, validation, cleanliness checks, status, living documents), documented in
  [docs/record-format-v1.md](docs/record-format-v1.md) and, for what format 2 changes,
  [docs/record-format-v2.md](docs/record-format-v2.md). Device keys are held in the strongest custody the machine
  offers: CNG on Windows, the Secure Enclave on a Mac through the `vericairn-enclave` helper
  (`src/Vericairn.Enclave`, in Swift), the secret service on Linux, otherwise a file in per-user state (owner-only permissions on Unix; inherited directory ACLs on Windows).
- `Vericairn.Runtime`: the live store, objects, storage profiles, export and restore, recovery, upgrades, and
  the one implementation of the estate operations.
- `Vericairn.Protocol`, `Vericairn.Daemon`, `Vericairn.Sdk`: the versioned API contract, the daemon that owns
  an estate's live state and serves it over a local socket, pinned loopback TLS and, when the operator
  configures a private address, the local network to paired devices with mutual TLS and requests signed for that
  runtime, and the typed client with the device's side of pairing and its connections, documented in
  [docs/api-v1.md](docs/api-v1.md) with the threat model of pairing.
- The software journey: `Vericairn.Packs.Software` (repositories as resources, build and test checks as
  verification evidence, and the coding-agent launchers and git worktree workspaces the worker uses),
  `Vericairn.Adapters.Git` (read-only observation of a repository) and
  `Vericairn.Providers.GitHub` (releases built by a GitHub Actions workflow and held as drafts, with intent
  recorded before effect and publication left to the operator), documented in [docs/software.md](docs/software.md).
  The foundation keeps their contracts; a release goes through the provider its request names or its product
  record declares.
- `vericairn` CLI: `records`, `estate`, `work`, `goal`, `worker`, `devices`, `connections`, `release`, `sources`,
  `check`, `runtime`, `recover`, `objects`, `docs`, `mcp`, `status`, `progress`, `ask`, `decisions`, `handoff`, `resume`,
  `communication`, `context`, `doctor`, `version`; a
  client of the daemon when one serves the estate, the embedded runtime otherwise. `vericairn progress` renders the progress
  report from the records, with repository-check findings: where the estate stands, the work orders by state, each
  published release and what it contained, what comes next by release, the next eligible work and what waits on the
  operator, every line naming the records it rests on; the API, an MCP tool, Workbench's home and a generated region
  of a document expose this report; the generated region omits repository checks to avoid reading itself. `vericairn goal` carries an objective: a
  goal is proposed and activated, record predicates and an evaluator who did none of its work judge it condition
  by condition, and its sponsor accepts it (or a delegate holding `accept-work` for an ordinary goal), or an operator
  when an agent sponsors it; no tool a coding agent is
  given can activate, fund or accept one, and only its sponsor, or an operator for a goal an agent sponsors,
  revises it. `vericairn worker` carries one of a goal's assignments with a coding agent (the worker contract
  `vericairn.worker/1`): through an admitted Claude Code or Codex `/goal` run, in a git worktree on
  a work branch, with each run's cap from the goal's budget recorded before it starts and its observed usage
  after, unknown usage charged at the cap, a stop's facts kept apart, and nothing launched while an operator's
  or the sponsor's key on the machine signs without presence, unless the sponsor recorded a custody exception for
  the goal, with its reason and its expiry. `vericairn goal next` routes a goal's eligible steps by
  its roster from the machine it runs on: each reaches the first available principal of its role, an agent where a
  launcher for its harness, its admitted key and the goal's budget allow and a person where needed, while a reserved
  action reaches only a person who holds it or an agent under a grant in force; when none is available the sponsor is
  asked, never another contributor. `vericairn ask` shows what waits on a principal: its open asks, at most three
  at a time with the recommendation first and the consequence of leaving each unanswered, which a decision of the
  principal asked answers and waiting never does. `vericairn goal step` takes a goal's next step, one at a time, the
  smallest form of Community's conductor, for an operator, the goal's sponsor when a person, or a principal holding
  `local-engineering`: an agent's run starts through the worker, its assignment recorded only with the run's intent,
  a person's step is assigned and asked, and a step nobody is available for is asked of the sponsor; `vericairn work
  takeover` hands a step an agent holds over to a person, a hand-over only a person records, never an agent.
  Explicit and routed runs on the old assignment are then refused before intent, without a charge. Multi-step
  planning and scheduling are outside this Community implementation.
  `vericairn devices pair` issues a short-lived pairing
  code under an administrator's authority, `vericairn connections add` pairs another machine with the runtime over
  the local network, and `vericairn connections list` shows each connection's state; a revoked device is refused at
  its next request, a device acts only for its own principal, and `vericairn devices writes` shows what paired
  devices wrote through the runtime, whose own devices sign those records until paired devices sign their own.
  `vericairn docs` writes a release's notes from an estate's records, refuses an incomplete documentation set,
  and keeps the estate's living documents: their declarations, what is due and why, reviews and generated
  regions. `vericairn mcp serve` is the Model Context Protocol server for coding agents, a client adapter over
  the API that lists only the tools the connected device may use, documented in [docs/mcp.md](docs/mcp.md);
  `Vericairn.Mcp` is the library behind it.
- Worker spending limits are soft, and unknown usage is charged at the reserved cap. The launchers are tested with
  stand-in processes; live coding-agent sessions and sign-in are not qualified by those tests. Process containment
  and the workspace audit have platform limits, detailed in [docs/software.md](docs/software.md#launch-accounting-and-containment).
- `Vericairn.Workbench`: the desktop, a client of the daemon that starts it when none answers and never hosts
  the runtime: home with attention, the asks waiting among it, and the progress report, products, work orders, assignments,
  results, review and decisions, goals, releases, living documents, communication settings, hand-over/resume and the runtime, documented in
  [docs/workbench.md](docs/workbench.md). `Vericairn.Presentation` holds
  the state language Workbench shares with the command line and the clients to come.
- `tools/Vericairn.DocsTool`: build tooling, never shipped. It generates a release's command-line, API and record
  references and its manifest from the build, and refuses a set that disagrees with the build. The release
  workflow runs that comparison and `vericairn docs check` before it builds a draft.
- `brand/`: the wordmark and the mark ([brand/README.md](brand/README.md)). `licenses/` and
  [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md): every third-party component the distributables bundle,
  with its license text.

## Developing on Windows, Linux or macOS

Development is supported on all three. Requires the .NET SDK pinned in `global.json` and git on the PATH.
Files are LF in every working tree (`.gitattributes`), console output is UTF-8 with LF line endings on
every platform. Continuous integration builds, tests, packs and checks local tool and SDK installation on all three
operating systems for main, pull requests and manual runs. A separate workflow scans the commits of every pushed
branch and pull request for secrets with a pinned, checksum-verified gitleaks.
The release workflow builds the Secure Enclave helper on macOS. This repository's solution contains no iOS or Android
client, so its build and tests make no mobile-platform qualification claim.

## Build

```sh
dotnet build
dotnet test
dotnet run --project src/Vericairn.Cli -- records describe
```

Build output goes to `artifacts/`, which is ignored.

## License

Apache License 2.0; see [LICENSE](LICENSE) and [NOTICE](NOTICE).
