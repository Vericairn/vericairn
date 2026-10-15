# Vericairn

Vericairn is a local-first creation and operations platform: it helps a person or organization turn an
intention into an understood, authorized, produced, reviewed, delivered and maintained result, with its
decisions, sources, relationships and history intact.

This repository holds the Community edition (Apache-2.0): the record format, the runtime and its daemon, the
`vericairn` command-line interface, the SDK and the Workbench desktop. The Remote client follows in milestone M3.

## Releases

The published releases are on the [Releases page](https://github.com/Vericairn/vericairn/releases). Each
carries the command line's archives for Linux, macOS and Windows, `SHA256SUMS`, a software bill of materials and
the third-party notices; from 0.3.1 a release also carries Workbench's archives and its documentation set. The
sets, from 0.2.0 on, are also kept under [docs/releases](docs/releases/README.md): the overview, installation
and cold start, the operator's guide, the generated command-line, API and record references, frozen copies of
the maintained documents, the release notes and known limitations. The index there also names the versions
withdrawn before publication and why; their sets stay as the record of what was built and reviewed. The
documents at `docs/` are the drafts for the next release.

Nothing here is a release until a release record in the Vericairn estate says so. The estate records every
release and pins the one it runs on.

## What is here

- `Vericairn.Records`: record formats 1 and 2 (canonical JSON, ULIDs, content hashes, signed records, devices
  and admission, estate loading, validation, cleanliness checks, status, living documents), documented in
  [docs/record-format-v1.md](docs/record-format-v1.md) and, for what format 2 changes,
  [docs/record-format-v2.md](docs/record-format-v2.md). Device keys are held in the strongest custody the machine
  offers: CNG on Windows, the Secure Enclave on a Mac through the `vericairn-enclave` helper
  (`src/Vericairn.Enclave`, in Swift), the secret service on Linux, otherwise an owner-only file.
- `Vericairn.Runtime`: the live store, objects, storage profiles, export and restore, recovery, upgrades, and
  the one implementation of the estate operations.
- `Vericairn.Protocol`, `Vericairn.Daemon`, `Vericairn.Sdk`: the versioned API contract, the daemon that owns
  an estate's live state and serves it over a local socket and pinned loopback TLS, and the typed client,
  documented in [docs/api-v1.md](docs/api-v1.md).
- The software journey: `Vericairn.Packs.Software` (repositories as resources, build and test checks as
  verification evidence, and the coding-agent launchers and git worktree workspaces the worker uses),
  `Vericairn.Adapters.Git` (read-only observation of a repository) and
  `Vericairn.Providers.GitHub` (releases built by a GitHub Actions workflow and held as drafts, with intent
  recorded before effect and publication left to the operator), documented in [docs/software.md](docs/software.md).
  The foundation keeps their contracts; a release goes through the provider its request names or its product
  record declares.
- `vericairn` CLI: `records`, `estate`, `work`, `goal`, `worker`, `devices`, `release`, `sources`, `check`,
  `runtime`, `recover`, `objects`, `docs`, `mcp`, `status`, `progress`, `ask`, `decisions`, `doctor`, `version`; a client of
  the daemon when one serves the estate, the embedded runtime otherwise. `vericairn progress` renders the progress
  report from the records alone, the same on every machine: where the estate stands, the work orders by state, each
  published release and what it contained, what comes next by release, the next eligible work and what waits on the
  operator, every line naming the records it rests on; the API, an MCP tool, Workbench's home and a generated region
  of a document carry the same report. `vericairn goal` carries an objective: a
  goal is proposed and activated, record predicates and an evaluator who did none of its work judge it condition
  by condition, and its sponsor accepts it, or an operator when an agent sponsors it; no tool a coding agent is
  given can activate, fund or accept one, and only its sponsor, or an operator for a goal an agent sponsors,
  revises it. `vericairn worker` carries one of a goal's assignments with a coding agent (the worker contract
  `vericairn.worker/1`): through Claude Code's or Codex's own `/goal`, or a generic launch, in a git worktree on
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
  smallest form of Community's conductor, for an operator, the goal's sponsor or a principal holding
  `local-engineering`: an agent's run starts through the worker, its assignment recorded only with the run's intent,
  a person's step is assigned and asked, and a step nobody is available for is asked of the sponsor; `vericairn work
  takeover` hands a step an agent holds over to a person, a hand-over only a person records, never an agent. Pro's
  program plans and schedules many steps.
  `vericairn docs` writes a release's notes from an estate's records, refuses an incomplete documentation set,
  and keeps the estate's living documents: their declarations, what is due and why, reviews and generated
  regions. `vericairn mcp serve` is the Model Context Protocol server for coding agents, a client adapter over
  the API that lists only the tools the connected device may use, documented in [docs/mcp.md](docs/mcp.md);
  `Vericairn.Mcp` is the library behind it.
- `Vericairn.Workbench`: the desktop, a client of the daemon that starts it when none answers and never hosts
  the runtime: home with attention, the asks waiting among it, and the progress report, products, work orders, assignments,
  results, review and decisions, goals, releases, living documents and the runtime, documented in
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
every platform, and continuous integration builds and tests on all three operating systems and scans the
commits of every pushed branch and pull request for secrets with a pinned, checksum-verified gitleaks.
Building the iOS client and signing for Apple platforms will require macOS, which is Apple's constraint,
not ours; everything else, including the Android client, builds anywhere.

## Build

```sh
dotnet build
dotnet test
dotnet run --project src/Vericairn.Cli -- records describe
```

Build output goes to `artifacts/`, which is ignored.

## License

Apache License 2.0; see [LICENSE](LICENSE) and [NOTICE](NOTICE).
