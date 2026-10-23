# Vericairn

Vericairn is a local-first creation and operations platform. It helps you, alone or as an organization,
carry work from an intention to a reviewed, delivered and maintained result, with its decisions, sources
and history kept as records you own: by default in a git repository beside your work, or in a store on one
machine, with hosted options planned. People and agents do the work, and nothing is released until you
accept it. Software is its first domain, and the one that ships today.

**Status:** pre-1.0, Community edition (Apache-2.0). The current published release is 0.8.2, from
October 22, 2026.
[Latest release](https://github.com/Vericairn/vericairn/releases/latest) ·
[NuGet](https://www.nuget.org/packages/dotnet-vc) · [Install](docs/install.md) ·
[Limitations](docs/limitations-and-platforms.md) ·
[Support](https://github.com/Vericairn/.github/blob/main/SUPPORT.md) · [Roadmap](ROADMAP.md)

Vericairn's official destinations are [vericairn.com](https://vericairn.com),
[vericairn.org](https://vericairn.org), the [Vericairn organization](https://github.com/Vericairn) on GitHub
and the [dotnet-vc](https://www.nuget.org/packages/dotnet-vc) and
[Vericairn](https://www.nuget.org/packages/Vericairn) packages on nuget.org. Other sites using the name are
not Vericairn's.

## Who it helps today

A developer, alone or in a small team, who builds and ships software with coding agents such as Codex and
Claude Code, and wants to know afterwards what each agent was asked to do, what it did, what was checked
and who accepted it.

## What you do with it

You write a work order: what to build, its scope and the question that decides when it is done. You
assign it to an agent with an exact brief. Vericairn's worker runs Codex or Claude Code on that brief in a
workspace repository of its own, within a spending cap recorded before the run, and a coding agent that
supports MCP can work in the estate through Vericairn's MCP server. You review the result and accept it or
ask for another attempt. A release is built as a draft on GitHub Releases and published only after you
accept it.

The outcome is the release you accepted and an estate: the records, in your git repository by default, of
every work order, brief, run, result, review, decision and release behind it. `dotnet vc status` and
`dotnet vc progress` show where it stands.

## Why records, reviews and acceptance

An agent's report that work is done is a claim, not evidence. In Vericairn each step is a record with a
content hash and, from an admitted device, a signature. A review names the exact result it read and an
acceptance the exact hash it accepts, so a changed result needs a new review and acceptance, and nothing is
released until you accept it. Later, you can still see what was asked, what was done, what was checked and
who decided.

Vericairn's own development runs this way: its work orders, reviews, decisions and releases are records in
a Vericairn estate, and every release ships only after its operator accepts it.

## Begin

With the .NET 10 SDK and git on Linux, macOS or Windows:

```sh
dotnet tool install --global dotnet-vc
mkdir my-venture && cd my-venture && git init
dotnet vc estate init --name "My Venture" --slug my-venture \
  --purpose "Build and ship one product with coding agents." \
  --operator-name "Your Name" --organization "Your Organization" \
  --commit e000a12ea980a4ac0c7e7a127a714f6423e27d7f
dotnet vc status
```

`estate init` records you as the operator and the estate's charter, and writes a manifest that pins the
Vericairn source it names, here 0.8.2's commit. `status` shows the new, empty estate. The
[operator's guide](docs/operator-guide.md) continues from there: your device and its key, a work order, an
agent's run, its review and acceptance, and a release. The [install guide](docs/install.md) also covers the
self-contained archives for machines without the .NET SDK, checking a download, Workbench and the MCP server.

In a .NET 10 project, `dotnet add package Vericairn` adds the SDK, the typed client of Vericairn's API; see
the [SDK guide](docs/api-v1.md#the-sdk).

## Limits that affect your start

- **Pre-1.0.** Commands and record formats still change between releases. Upgrading an estate is
  forward-only, and when a release changes the record format, every writer and clone of the estate moves to
  it together.
- **Unsigned builds.** The archives are developer builds without code signing or notarization. Verify each
  download against the release's `SHA256SUMS`, as the [install guide](docs/install.md) shows.
- **Platforms.** The command line is qualified on Linux x64, macOS on Apple silicon and Windows x64. The
  other archives are provided but unqualified, and Workbench is not qualified on any platform.
- **Agents.** The worker runs only exact coding-agent versions, configured on each machine: the release's
  baselines, or another version a person qualifies there. Live runs have qualified one Codex configuration,
  which the [software guide](docs/software.md#native-inventory-qualification-run-02-and-run-03) describes.
  Spending caps are soft: unknown usage is charged at the cap.
- **Not built yet.** Remote, the phone client, and every entry of the planned catalog below.
- **Support.** Help is self-service, with no promised response time.

The [limitations and platforms](docs/limitations-and-platforms.md) guide gives every current limit, and the
[release index](docs/releases/README.md#readiness-disclosures) gives the current release's readiness
disclosures and security notices.

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
- Vericairn's catalog, at later milestones: more than 3,000 planned entries, including 1,290 domain packs,
  992 foundries, 301 adapters and 575 providers. The creator domains above come first. Every entry is
  planned; a name in the catalog is scope, not availability. The catalog covers:
  - Creative and technical work: the web, games and Unity, books and book series, courses and course series,
    video and video series, worldbuilding, media, documentation, marketing, canon, adaptation, continuity,
    franchises, rights, localization, release readiness, analytics, crowdfunding, AI applications and
    extension ecosystems.
  - 27 industries: aerospace and defense; chemicals; construction; e-commerce and online retail; education
    institutions; environmental consulting; finance, accounting and audit; food and agriculture; governance,
    risk and compliance; information technology services and operations; insurance; land management; legal
    practice operations; life sciences quality and regulatory; manufacturing quality and operations; marine;
    marketing and advertising; medical; mining and minerals; nonprofits and grants; oil field; public sector;
    real estate and property management; refinery; telecommunications; transportation and logistics; and
    utilities and power.
  - In regulated fields, agents prepare and check the work, and licensed or authorized people make the
    decisions and perform the acts reserved to them.
- The [roadmap](ROADMAP.md) gives the order.

## Releases

The published releases are on the [Releases page](https://github.com/Vericairn/vericairn/releases). Each
carries the command line's archives for Linux, macOS and Windows, `SHA256SUMS`, a software bill of materials
and the third-party notices; from 0.3.1 a release also carries Workbench's archives and its documentation
set. The [release index](docs/releases/README.md) keeps every set from 0.2.0 on, with each release's notes
and security sections, and names the candidates withdrawn before publication and why. The documents at
`docs/` are the maintained drafts for the next release; a released set is never edited.

Nothing here is a release until a release record in the Vericairn estate says so. The estate records every
release and pins the one it runs on.

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
