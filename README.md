# Vericairn

Vericairn is a local-first creation and operations platform. It helps you, alone or as an organization,
carry work from an intention to a reviewed, delivered and maintained result, with its decisions, sources
and history kept as records you own: by default in a git repository beside your work, or in a store on one
machine, with hosted options planned. People and agents do the work, and nothing is released until you
accept it.

Software is its first domain. Today Vericairn helps you build and ship software with coding agents: agents
work from exact briefs, their results are checked and reviewed, and each work order, attempt, review,
decision and release is a record with a content hash and, from an admitted device, a signature. Beyond
software, Vericairn's catalog plans more than 3,000 entries across creative and technical work and 27
industries: domain packs that describe a field's work, foundries that start or reshape a project, adapters
for the formats and tools you use, and providers for the services you deliver through. None of them ships
yet; websites and web applications come first, then books, book series and world building, courses and
video, and crowdfunding.

Vericairn's own development runs this way. Its work orders, reviews, decisions and releases are records in a
Vericairn estate, and every release ships only after its operator accepts it.

This repository holds the Community edition (Apache-2.0): the record format, the runtime and its daemon, the
`vericairn` command line, the SDK, the MCP server and the Workbench desktop. The Remote client is not
implemented here.

**Status:** pre-1.0. The current published release is [0.8.2](https://github.com/Vericairn/vericairn/releases/tag/v0.8.2),
published on October 22, 2026. The [public roadmap](ROADMAP.md) lists what comes next, with no dates.

## Try it

With the .NET 10 SDK on Linux, macOS or Windows:

```sh
dotnet tool install --global dotnet-vc
mkdir my-venture && cd my-venture && git init
dotnet vc estate init --name "My Venture" --slug my-venture \
  --purpose "Build and ship one product with coding agents." \
  --operator-name "Your Name" --organization "Your Organization" \
  --commit e000a12ea980a4ac0c7e7a127a714f6423e27d7f
dotnet vc status
```

`estate init` writes two records, you as the operator and the estate's charter, and a manifest that pins the
Vericairn source it names, here 0.8.2's commit. From 0.8.3, `--commit` is optional: `estate init` pins the
running build's exact source commit, as `dotnet vc --version` reports it, and a development build without a
recorded commit still requires the option. `status` shows the new, empty estate. The
[operator's guide](docs/operator-guide.md) continues from there: your device and its key, a builder agent,
decisions, a work order, an agent's run, its review and acceptance, and a release.

## What works today

- **Records in git.** Canonical, content-addressed JSON records, one file each, checked by
  `vericairn estate check`, with export and verified restore.
- **Work with agents.** Work orders with exit questions, exact briefs, assignments, attempts, results, reviews
  and acceptance pinned to exact hashes. The worker runs Codex on an assignment within a recorded cap, with a
  command audit and a scope check; live runs qualify one Codex version. The MCP server gives a coding agent,
  such as Claude Code, an estate's reads and the narrow writes an agent is meant to have.
- **Releases.** Candidates, a release gate that needs the operator's acceptance, intent recorded before every
  external effect, and publication through GitHub Releases.
- **Estate policies.** Policies, recurring duties and writing exceptions, evaluated from the records.
- **Platforms.** The command line is qualified on Linux x64, macOS on Apple silicon and Windows x64. Workbench
  is included but not yet qualified on any platform.

## Planned

- Remote, the phone client for accepting releases and steering work, in 0.9.0, with a community build guide
  for every planned catalog entry and a way to request the implementations you need most.
- The first creator domains in a 0.10.x series: websites and web applications, then books, book series and
  world building, courses and video, and crowdfunding.
- Paid editions on the same software, with Community staying free: Standard for one person and Pro for teams
  and organizations, with no offers or prices yet. Every paid service is planned as software you can run
  yourself at the edition that sells it, no access to your estate will require a Vericairn account, and you
  can move between Vericairn's hosting and your own either way; the [roadmap](ROADMAP.md#editions--planned-direction)
  lists all five no-lock-in rules.
- Separately installed extensions and the package registry, at milestone M4.
- Vericairn's catalog, at later milestones: more than 3,000 planned entries, including 1,289 domain packs,
  989 foundries, 291 adapters and 566 providers. The creator domains above come first. Every entry is
  planned; a name in the catalog is scope, not availability. The catalog covers:
  - Creative and technical work: the web, games and Unity, books and book series, courses and course series,
    video and video series, worldbuilding, media, documentation, marketing, canon, adaptation, continuity,
    franchises, rights, localization, release readiness, analytics, crowdfunding and extension ecosystems.
  - 27 industries: aerospace and defense; chemicals; construction; e-commerce and online retail; education
    institutions; environmental consulting; finance, accounting and audit; food and agriculture; governance,
    risk and compliance; information technology services and operations; insurance; land management; legal
    practice operations; life sciences quality and regulatory; manufacturing quality and operations; marine;
    marketing and advertising; medical; mining and minerals; nonprofits and grants; oil field; public sector;
    real estate and property management; refinery; telecommunications; transportation and logistics; and
    utilities and power.
  - In regulated fields, agents prepare and check the work, and licensed or authorized people make the
    decisions and perform the acts reserved to them.
- The [roadmap](ROADMAP.md) gives the order, and [the limitations](docs/limitations-and-platforms.md) give
  the current limits.

## The current release

The current published release is [0.8.2](https://github.com/Vericairn/vericairn/releases/tag/v0.8.2),
published on October 22, 2026 at 11:24:13 UTC, tag `v0.8.2`, commit `e000a12`. It follows published 0.8.0
and carries the work of the 0.8.1 candidate, which was withdrawn before publication: FIX-53 corrects the
macOS and Linux command lines, which could not start other programs, and FIX-54 rechecks a retained release
basis's record digest. Its release notes open with a [Security](docs/releases/0.8.2/release-notes.md#security)
section: FIX-34 through FIX-38 harden the worker's command audit and settlement, the release gate, checked
pushes and merges, and the upgrade, and CI-03 builds releases with only the tokens each step needs. FIX-39
through FIX-46 and FIX-48 correct what adopting estate policies found; FIX-29, FIX-32, FIX-49 and CI-01
settle test timing and split each platform's tests; FIX-51 keeps a retained release basis small; DOCS-07
adds `vericairn docs pin`, DIST-02 NuGet observations, DIST-03 the package icon and DIST-04 the MCP Registry
listing. [API compatibility](docs/api-compatibility-0.8.2.md) lists two interface changes. One known issue:
each platform's archive carries a separately compiled build of the same libraries, so a policy basis differs
by platform ([issue 184](https://github.com/Vericairn/vericairn/issues/184)); 0.8.3 fixes it. The
[0.8.2 documentation set](docs/releases/0.8.2/README.md) describes the behavior and remaining limits; the
estate's release record establishes publication and qualification.

0.7.2's release notes open with [Security](docs/releases/0.7.2/release-notes.md#security): F-01 affected export
restoration in every published release from 0.1.0 through 0.7.1. Upgrade to 0.7.2 or later before restoring an export.
The fix binds restored bytes to the verified inventory; it does not authenticate the export's producer.

Release 0.7.1 carried the withdrawn 0.7.0 candidate's work with
FIX-12: a machine-local workspace root, current assignment-holder checks and precise treatment of validated
public keys and signatures. Its earlier frozen set remains unchanged. All eight command-line and library
packages are available at 0.8.2 on nuget.org; [the release index](docs/releases/README.md#nuget-publication-setup)
records publication and the earlier 0.7.1 recovery.

Ordinary source builds retain `0.8.2-dev`. The maintained guides describe 0.8.2; earlier release sets
keep their original bytes. Upgrading an estate to record format 3 is forward-only, and a format-2-only binary
refuses the upgraded estate, so every writer and clone moves to 0.8.0 or later together. The upgrade declares no policy
or duty; the operator adopts them explicitly, as the [adoption guide](docs/policy-adoption.md) describes.
Release checks reach only GitHub, ignore system proxies, are disabled with `vericairn updates configure --enabled false`
and retain cached information when disabled. Command observations remain signals, not an enforcement boundary.
Required attention cannot infer closure from prose or missing evidence; its [rules](docs/required-attention.md)
keep undecidable items visible as unknown.

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
is absent until the planned 0.8.4 work, Remote is not built, and context tokens remain unknown without a
qualified local tokenizer. Spending and containment limits remain as described in the guides.
The seventh live Codex run committed the permitted file in its independent workspace on macOS; settlement
audited the commit and fetched its branch into the registered clone. Only the workspace's own `.git` is
added to Codex's writable roots. This live qualification covers the reported macOS path, not other harness
versions, accounts, platforms, provider billing or acceptance of the work. 0.8.2's worker changes, FIX-32
through FIX-35, are covered by tests only: the estate's own live runs have used 0.8.0's worker, not yet 0.8.2's. The
[software guide](docs/software.md#native-inventory-qualification-run-02-and-run-03) retains all seven observations.

## Releases

The [public roadmap](ROADMAP.md) lists the current published release and planned releases and later
milestones, with no dates or promises.

The published releases are on the [Releases page](https://github.com/Vericairn/vericairn/releases). Each
carries the command line's archives for Linux, macOS and Windows, `SHA256SUMS`, a software bill of materials and
the third-party notices; from 0.3.1 a release also carries Workbench's archives and its documentation set. The
sets, from 0.2.0 on, are also kept under [docs/releases](docs/releases/README.md): the overview, installation
and cold start, the operator's guide, the generated command-line, API and record references, frozen copies of
the maintained documents, the release notes and known limitations. The index there also names the versions
withdrawn before publication and why; their sets stay as the record of what was built and reviewed. The
documents at `docs/` are the maintained drafts. The 0.8.2 set documents the work described above; the earlier sets remain frozen,
including the withdrawn 0.7.0 and 0.8.1 candidates'.

For 0.8.2, the command line is qualified on `linux-x64`, `osx-arm64` and `win-x64`, where the release's tests ran.
The `linux-arm64`, `osx-x64` and `win-arm64` archives are provided but unqualified. Workbench is not qualified on
any platform. The archives are unsigned developer builds, without code signing or notarization; verify downloads
as the [install guide](docs/releases/0.8.2/install.md) describes. 0.8.2's `SHA256SUMS` also lists
`vericairn-0.8.2-companions.zip`, the readable build reports, which the release run keeps in its CI artifact
and does not attach; check the attached assets with `--ignore-missing`, as the guide does.

Nothing here is a release until a release record in the Vericairn estate says so. The estate records every
release and pins the one it runs on.

## Install the command line and SDK

The 0.8.2 NuGet packages are available. Install the command line with the .NET 10 SDK on Linux, macOS or Windows:

```sh
dotnet tool install --global dotnet-vc --version 0.8.2
dotnet vc version
dotnet vc doctor
dotnet vc records describe
```

The global tool directory must be on PATH, as the installer describes. A repository can instead keep a
local tool manifest; see the [0.8.2 install guide](docs/releases/0.8.2/install.md#9-nuget-command-line-and-sdk).
The tool is framework-dependent; the self-contained archives continue to ship `vericairn` for machines without the SDK, and the macOS archives
also carry the Secure Enclave helper. The NuGet tool does not bundle that helper or Workbench.
The worker's agent-side MCP command still resolves `vericairn`; keep the archive executable on PATH for worker
runs. A NuGet-only worker installation is not qualified by the package installation checks.

In a .NET 10 project, install the SDK with:

```sh
dotnet add package Vericairn --version 0.8.2
```

`Vericairn` brings `Vericairn.Sdk`, `Vericairn.Protocol` and `Vericairn.Records`, preserving their namespaces
and assemblies. `Vericairn.Runtime`, `Vericairn.Daemon` and `Vericairn.Mcp` are also available at 0.8.2;
the SDK front door does not embed a runtime or daemon. See the [SDK guide](docs/api-v1.md#the-sdk).
The packages contain Community code only.
Commands written as `vericairn ...` in the archive documentation take the same arguments as `dotnet vc ...`.

## What is here

The following capabilities are in 0.8.2.

- `Vericairn.Records`: record formats 1, 2 and 3 (canonical JSON, ULIDs, content hashes, signed records, devices
  and admission, estate loading, validation, cleanliness checks, status, living documents), documented in
  [docs/record-format-v1.md](docs/record-format-v1.md) and, for what format 2 changes,
  [docs/record-format-v2.md](docs/record-format-v2.md), with policies, duties and generated-document contracts in
  [docs/record-format-v3.md](docs/record-format-v3.md). Device keys are held in the strongest custody the machine
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
  verification evidence, and the coding-agent launchers and independent Git workspaces the worker uses),
  `Vericairn.Adapters.Git` (repository observation, exact source reads and checked workspace/push operations) and
  `Vericairn.Providers.GitHub` (releases built by a GitHub Actions workflow and held as drafts, with intent
  recorded before effect and publication left to the operator, and the pull requests and merges of checked
  changes), documented in [docs/software.md](docs/software.md) and [docs/changes.md](docs/changes.md).
  The foundation keeps their contracts; a release goes through the provider its request names or its product
  record declares.
- `vericairn` CLI: `records`, `estate`, `work`, `goal`, `worker`, `devices`, `connections`, `release`, `sources`,
  `check`, `runtime`, `recover`, `objects`, `docs`, `mcp`, `status`, `progress`, `ask`, `decisions`, `handoff`, `resume`,
  `communication`, `context`, `policies`, `operator`, `changes`, `doctor`, `version`, `updates`; a
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
  `vericairn.worker/1`): through an admitted Claude Code or Codex `/goal` run, in a workspace repository of
  its own on a work branch, with each run's cap from the goal's budget recorded before it starts and its observed usage
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
  lists and renders readable companions and keeps the estate's living documents: their declarations, what is
  due and why, reviews and generated regions. `vericairn mcp serve` is the Model Context Protocol server for
  coding agents, a client adapter over the API that lists only the tools the connected device may use,
  documented in [docs/mcp.md](docs/mcp.md); `Vericairn.Mcp` is the library behind it.
- Worker spending limits are soft, and unknown usage is charged at the reserved cap. The launchers are tested with
  stand-in processes; live coding-agent sessions and sign-in are not qualified by those tests. Process containment
  and the workspace audit have platform limits, detailed in [docs/software.md](docs/software.md#launch-accounting-and-containment).
- `Vericairn.Workbench`: the desktop, a client of the daemon that starts it when none answers and never hosts
  the runtime: home with attention, the asks waiting among it, and the progress report, products, work orders, assignments,
  results, review and decisions, goals, releases, living documents, readable views, communication settings,
  hand-over/resume and the runtime, documented in
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
operating systems for main, pull requests and manual runs. In CI and in the release workflow, a job retries up to five
failed tests once each; every retried test is named in the run's summary, and a second failure fails the job. A
separate workflow scans the commits of every pushed branch and pull request for secrets with a pinned,
checksum-verified gitleaks.
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
