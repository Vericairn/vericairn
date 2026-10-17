# Vericairn 0.7.1

Vericairn is an estate of append-only, content-addressed, signed records: work, decisions, authority, evidence and releases remain attributable to the exact revision judged. The command line, Workbench, API/SDK and MCP server share one runtime. Agents produce claims to inspect; publication and milestone/release acceptance remain the operator's actions.

0.7.1 carries 0.7.0's work with FIX-12 and follows published 0.6.1. The operator withdrew 0.7.0's candidate before publication under decision `01M53S6DYFCPJMG0AQRSN8ZFSP`; withdrawal record `01M53S6FN1GANBY5HSNDKE5N08` records that outcome after review `01M53R2Y38J97MNSEWNK1965XA`. Its documentation set stays frozen and indexed in `../frozen-sha256.json`. It is not an upgrade target. The estate's release record is the authority for 0.7.1's publication, commit, workflow and artifact hashes.

NuGet installation becomes available when the operator publishes the release and its Trusted Publishing workflow succeeds. Name-reservation packages are not the product. The workflow uploads exactly the package assets the operator publishes, verified against that release's own `SHA256SUMS`; it does not independently compare them with the estate's accepted candidate. Publishing the accepted draft supplies that binding.

## What 0.7.1 offers

- **Distribution (DIST-01).** Self-contained CLI and Workbench archives continue on six platforms. Framework-dependent NuGet packages provide `dotnet-vc`, invoked as `dotnet vc`, the dependency-only `Vericairn` SDK front door and the `Vericairn.*` libraries. Publication uploads verified release assets without rebuilding them. The NuGet tool has no macOS Secure Enclave helper or Workbench.
- **Hand-over and resume (HANDOFF-01).** A portable package retains the copy's complete record history, pin, observations and derived work state. Resume reports identity changes, including imported records with older timestamps; it restarts nothing. Human observation is explicit. Workbench provides the local journey.
- **Watching (WATCH-01).** Agent progress claims are separate from observed liveness. Native event streams supply command, credential/path, loop and token-rate observations, attention and critical stop requests. Completed phases become records; interim notes stay local. Attention asks pin the original run intent and are reported stale after settlement supersedes it; settlement does not answer them.
- **Personal communication (COMMS-01, COMMS-02).** Signed settings and explicit delegations control optional progress and explanatory depth. Conflicts, contributing revisions and freshness are shown; required attention and complete originals survive every depth. The adoption procedure and qualification report distinguish explicit consent, tested behavior and deferred work.
- **Measured context (CONTEXT-01).** Work packages include required originals and a complete list of selected material withheld. Exact bytes, source identities, loss labels and pinned retrieval are shared across the CLI, API/SDK and MCP. Missing mandatory sources or byte overflow refuse preparation. Tokens and model fit remain unknown without a qualified local tokenizer; this build includes none.
- **Explicit and qualified execution (RUN-01, RUN-02, RUN-03).** Runs resolve model, native effort, provider, destination and data boundary under cumulative restrictions and an exactly admitted environment inventory. Compiled native baselines are codex-cli `0.159.0-alpha.12.1` and Claude Code `2.1.296`; another exact version requires an authorized person's recorded qualification and matching local probes. Generic dispatch is refused. Qualification supplies protocol evidence, not live-inference proof or work authority.
- **Follow-up corrections (FIX-09, FIX-10, FIX-11).** The notes generator can widen its record window with `--since`, and a checksum guard protects earlier documentation sets. Authentication reads have bounded concurrency. Deterministic permitted-path matching and isolated command-audit checks preserve independent findings. Machine-local worker defaults and allowlists narrow qualified choices; harness qualification is machine-local, and the generated CLI reference includes recursive global options without trailing spaces.
- **Workspace, hand-over and signing-field corrections (FIX-12).** A machine-local workspace root lets native runs work outside a home containing harness configuration. Every explicit or routed run checks its assignment's current holder before preparation and intent; hand-over refuses a new run on the old assignment without recording or charging it. The credential scan exempts only validated device public-key and envelope signature fields, preserving signature verification, machine-path checks and scanning of every other field.

Before a native run, configure the worker on that machine with `vericairn worker configure --launcher codex --model <model> --effort <effort>` (or `claude-code`); omit choices to show it. Configuration is per machine and is never recorded. Optional allowlists narrow permitted choices and never widen the qualified inventory. The Vericairn home must be outside every Git repository and estate; if your home directory is a repository, set `VERICAIRN_HOME` elsewhere for the CLI and daemon before starting them.

If the home holds harness configuration, use `vericairn worker configure --workspaces <absolute-directory>` to choose a root outside it; `vericairn worker configure` shows the root. The root and its ancestors cannot contain `.claude`, `.codex`, `.agents` or `.mcp.json`. Codex separately refuses the user's own `~/.agents` customization, even with a relocated workspace and fresh `CODEX_HOME`. Use a worker account without that discovery root; the native scan's empty-directory and instruction-only exceptions are described in [install.md](install.md). Harness qualification refuses paired local-network devices before probes or records.

The efficiency Adapter is absent until the planned 0.8.0 work. Remote is not built. Workbench's first estate listing becomes slower as records grow. The living-document release gate reads covered paths at the registered clone's HEAD before the separate content comparison at the release ref. Soft spending, process containment, credential isolation and native platform qualification remain limited; [limitations-and-platforms.md](limitations-and-platforms.md) describes these boundaries.

## This documentation set

The generated CLI, API and record references and `manifest.json` came from the 0.7.1 Release build at version commit `63c65762806b630f7ec3ea5715d880392f188322` on October 17, 2026 UTC, following FIX-12 at `8164307`. They list 111 commands, 72 API operations and 39 record types, with 17 required documents. This is generation provenance, not the eventual release commit. The emitted files are unchanged, including `worker configure --workspaces` and the recursive `--recipient`, `--for-assignment` and `--depth` options, with no trailing whitespace.

The maintained drafts were checked against FIX-12's code and regression cases; their main update is `dec69d6`, with final version-wording checks following it. As in 0.7.0 and 0.6.1, the API, both record-format documents, software, Workbench and MCP drafts are copied byte for byte into this set. `api.md` and `api-v1.md` are identical copies of `docs/api-v1.md`, preserving relative links. The communication adoption procedure and qualification report are also frozen copies. Installation, operator, limitations, format-index and overview guides are revised for 0.7.1.

The release notes render the estate's records with `--from 0.6.1 --to 0.7.1 --since 2026-10-16T04:29:42Z`. That exclusive start is one second before the record dated exactly 04:29:43Z, so all nine records missed by 0.6.1's notes enter this set. The withdrawn 0.7.0 set's 04:29:43Z start excluded the first one. The overlap with 0.6.1 is deliberate; both earlier sets remain unchanged. The generator records the chosen window in the manifest. Each current result of an accepted work order is listed separately, including partial or failed results; classification creates no acceptance of those results and uses current estate state. Superseded results are omitted.

| Document | What it answers |
|---|---|
| [install.md](install.md) | Bootstrap, archive CLI and Workbench, NuGet tool and SDK, upgrade and worker prerequisites |
| [operator-guide.md](operator-guide.md) | Estate, device, work and release journeys; configured workers, pairing, context, hand-over, communication and watching |
| [cli-reference.md](cli-reference.md) | Commands, arguments and options generated from the build |
| [api-reference.md](api-reference.md) | API v1 routes, bodies and responses generated from the build |
| [record-reference.md](record-reference.md) | Record types, subject rules and body fields generated from the build |
| [record-format.md](record-format.md) | How formats 1 and 2 relate; this release reads both and writes at most 2 |
| [record-format-v1.md](record-format-v1.md), [record-format-v2.md](record-format-v2.md) | Frozen format rules, including progress, communication, execution and harness qualification |
| [api.md](api.md), [api-v1.md](api-v1.md) | Identical frozen API drafts: transports, authentication, SDK, context and operations |
| [software.md](software.md) | Repositories, releases, packages and worker implementation boundaries |
| [workbench.md](workbench.md) | Desktop surfaces, keyboard routes, probing and qualification limits |
| [mcp.md](mcp.md) | Supported protocol, reads and five narrow writes, configuration and limitations |
| [communication-adoption.md](communication-adoption.md) | Explicit adoption of the first-party operator's recorded preference |
| [communication-qualification.md](communication-qualification.md) | Communication/context regression evidence and deferred qualifications |
| [release-notes.md](release-notes.md) | Estate records rendered with the widened 04:29:42Z start to recover all nine missed records |
| [limitations-and-platforms.md](limitations-and-platforms.md) | Configuration, publication, platform, custody, worker and network limits |
| [manifest.json](manifest.json) | Generation version/commit, required documents, inventories, format compatibility and the rendered notes' window |

## Checking the version and set

```sh
vericairn version
vericairn docs check docs/releases/0.7.1
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.7.1
```

With the NuGet tool, replace `vericairn` with `dotnet vc`. A packaged release prints `0.7.1+<commit>`; an ordinary source build retains `-dev`. `doctor` reports runtime, custody, format and daemon information. Workbench's target bar shows the estate's own pin, which changes only through upgrade. The structural docs check and comparison do not judge prose, qualify a platform or accept a release.

## Where to get it

After publication, the archives, packages, symbols, bill of materials, notices, documentation and `SHA256SUMS` are attached to the [0.7.1 release page](https://github.com/Vericairn/vericairn/releases/tag/v0.7.1). After its NuGet workflow succeeds, use `dotnet tool install --global dotnet-vc` or `dotnet add package Vericairn` as described in the installation guide. The release record supplies the authoritative commit, workflow and artifact hashes. Source and Community packages are under Apache License 2.0; copyright (c) 2026 Jason Furr.
