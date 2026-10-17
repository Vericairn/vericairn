# Vericairn 0.7.0

Vericairn is an estate of append-only, content-addressed, signed records: work, decisions, authority, evidence and releases remain attributable to the exact revision judged. The command line, Workbench, API/SDK and MCP server share one runtime. Agents produce claims to inspect; publication and milestone/release acceptance remain the operator's actions.

This set prepares 0.7.0 after published 0.6.1. It describes the code at this head, not an already published release or a completed platform qualification. NuGet installation becomes available when the operator publishes the release and its Trusted Publishing workflow succeeds. Name-reservation packages are not the product.

## What 0.7.0 offers

- **Distribution (DIST-01).** Self-contained CLI and Workbench archives continue on six platforms. New framework-dependent NuGet packages provide `dotnet-vc`, invoked as `dotnet vc`, the dependency-only `Vericairn` SDK front door, and the `Vericairn.*` libraries. The release workflow builds and checks the exact packages; publication triggers their NuGet upload without rebuilding them. The NuGet tool has no macOS Secure Enclave helper or Workbench.
- **Hand-over and resume (HANDOFF-01).** A portable package retains the copy's complete record history, pin, observations and derived work state. Resume reports identity changes, including imported records with older timestamps; it restarts nothing. Human observation is explicit. Workbench provides the local journey.
- **Watching (WATCH-01).** Agent progress claims are separate from observed liveness. Native worker event streams supply command, credential/path, loop and token-rate observations, attention and critical stop requests. Completed phases become records; interim notes stay local. Limits and incomplete observations remain visible.
- **Personal communication (COMMS-01, COMMS-02).** Signed settings and explicit delegations control optional progress and explanatory depth. Conflicts, contributing revisions and freshness are shown; required attention and complete originals survive every depth. The adoption procedure and qualification report distinguish explicit consent, tested behavior and deferred work.
- **Measured context (CONTEXT-01).** Work packages include required originals and a complete list of selected material withheld. Exact bytes, source identities, loss labels and pinned retrieval are shared across the CLI, API/SDK and MCP. Missing mandatory sources or byte overflow refuse preparation. Tokens and model fit remain unknown without a qualified local tokenizer; this build includes none.
- **Explicit and qualified execution (RUN-01, RUN-02, RUN-03).** Runs resolve model, native effort, provider, destination and data boundary under cumulative restrictions and an exactly admitted environment inventory. Compiled native baselines are codex-cli `0.159.0-alpha.12.1` and Claude Code `2.1.296`; another exact version requires an authorized person's recorded qualification and matching local probes. Generic dispatch is refused. Qualification supplies protocol evidence, not live-inference proof or work authority.
- **Follow-up corrections (FIX-09, FIX-10, FIX-11).** The notes generator can widen its record window with `--since`, and a checksum guard protects earlier documentation sets. Authentication reads have bounded concurrency. Deterministic permitted-path matching and isolated command-audit checks preserve independent findings when another check is incomplete. FIX-11 supplies machine-local worker defaults and allowlists, refuses harness qualification over the local network, and includes every visible CLI option in generated references without trailing whitespace.

Before a native run, configure the worker on that machine with `vericairn worker configure --launcher codex --model <model> --effort <effort>` (or `claude-code`); omit choices to show it. Configuration is per machine and is never recorded in the estate. Optional allowlists narrow permitted choices and never widen the qualified inventory. The Vericairn home must be outside every Git repository and estate; if your home directory is a repository, set `VERICAIRN_HOME` elsewhere for the CLI and daemon before starting them. Harness qualification is machine-local: paired local-network devices are refused before probes or records. The [installation guide](install.md) gives the setup procedure.

The efficiency Adapter is absent until the planned 0.8.0 work. Remote is not built. Soft spending limits, process-containment limits, credential-isolation limits and native platform qualification still apply; [limitations-and-platforms.md](limitations-and-platforms.md) describes them.

## This documentation set

Prepared on October 16, 2026 UTC, before the release build, following the same sequence as 0.6.1 and then refreshed for FIX-11. The generated CLI, API and record references and `manifest.json` came from the 0.7.0 Release build at merge commit `5dbc5022ae4878bc53e7818d4d788cd1a739aeff`, which incorporates FIX-11 from main at `447ddc8`. They list 111 commands, 72 API operations and 39 record types, with 17 required documents. This is their generation provenance, not the eventual release commit.

The generated files are retained as emitted. The CLI reference includes `worker configure` and the recursive `--recipient`, `--for-assignment` and `--depth` root options, with no trailing whitespace. The structural comparison checks command identities; regression tests cover visible options at their declaring commands.

The repository README and maintained drafts were checked against code and regression cases and committed at `10f7c22`, then reconciled with FIX-11 at `ddec89e`. As in 0.6.1, the API, both record-format documents, software, Workbench and MCP drafts are copied byte for byte into this set. `api.md` and `api-v1.md` are identical copies of `docs/api-v1.md`, preserving the drafts' relative links. The communication adoption procedure and qualification report are also frozen copies. Installation, operator, limitations, format-index and overview guides are revised for this release. The installation guide incorporates the new NuGet draft alongside the archive journeys.

`release-notes.md` renders the estate's records through the 0.7.0 command line, from a fresh clone of the estate's main at `5750ebe` taken after every pending record was merged. Its window begins at 2026-10-16T04:29:43Z, widened with `--since` so the nine records that the clone used for 0.6.1's notes lacked land in this set, and ends at 2026-10-16T23:45:03Z; both bounds are in `manifest.json`. Records dated between 04:29:43 and 04:39:05 UTC that 0.6.1's notes already listed may appear in both sets. The notes classify current results as accepted work when an acceptance pins their exact hash or their work order's current revision and hash; that classification creates no acceptance of a result. The set is frozen for its release build, and its checksums are indexed in `../frozen-sha256.json`. Earlier published and withdrawn sets remain unchanged.

| Document | What it answers |
|---|---|
| [install.md](install.md) | Bootstrap, archive CLI and Workbench, NuGet tool and SDK, upgrade and prerequisites |
| [operator-guide.md](operator-guide.md) | Estate, device, work and release journeys; goals, configured workers, pairing, context, hand-over, communication and watching |
| [cli-reference.md](cli-reference.md) | Commands, arguments and options generated from the build |
| [api-reference.md](api-reference.md) | API v1 routes, bodies and responses generated from the build |
| [record-reference.md](record-reference.md) | Record types, subject rules and body fields generated from the build |
| [record-format.md](record-format.md) | How the two record formats relate; 0.7.0 still reads 1 and 2 and writes at most 2 |
| [record-format-v1.md](record-format-v1.md), [record-format-v2.md](record-format-v2.md) | Frozen format rules, including progress, communication, execution and harness qualification |
| [api.md](api.md), [api-v1.md](api-v1.md) | Identical frozen API drafts: ownership, transports, authentication, SDK, context and operations |
| [software.md](software.md) | Repositories, checks, releases, packages, communication/context and worker implementation boundaries |
| [workbench.md](workbench.md) | Desktop surfaces, keyboard routes, probing and qualification limits |
| [mcp.md](mcp.md) | Supported protocol, reads and five narrow writes, configuration and limitations |
| [communication-adoption.md](communication-adoption.md) | Explicit adoption of the first-party operator's recorded preference |
| [communication-qualification.md](communication-qualification.md) | Communication/context regression evidence and deferred qualifications |
| [release-notes.md](release-notes.md) | Estate records rendered by 0.7.0 from 2026-10-16T04:29:43Z, widened to recover the records 0.6.1's notes missed |
| [limitations-and-platforms.md](limitations-and-platforms.md) | Machine-local configuration, automation versus qualification, platform, custody, worker and network limits |
| [manifest.json](manifest.json) | Generation version/commit, required documents, commands, operations, types, format compatibility and the notes' record window |

## Checking the version and set

```sh
vericairn version
vericairn docs check docs/releases/0.7.0
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.7.0
```

With the NuGet tool, replace `vericairn` with `dotnet vc`. A packaged release prints `0.7.0+<commit>`; an ordinary source build retains `-dev`. `doctor` reports runtime, custody, format and daemon information. Workbench's target bar shows the estate's own pin, which changes only through upgrade. The structural docs check and comparison do not judge prose, qualify a platform or accept a release.

## Where to get it

After publication, the archives, packages, symbols, bill of materials, notices, documentation and `SHA256SUMS` are attached to the [0.7.0 release page](https://github.com/Vericairn/vericairn/releases/tag/v0.7.0). After its NuGet workflow succeeds, use `dotnet tool install --global dotnet-vc` or `dotnet add package Vericairn` as described in the installation guide. The release record supplies the authoritative commit, workflow and artifact hashes. Source and Community packages are under Apache License 2.0; copyright (c) 2026 Jason Furr.
