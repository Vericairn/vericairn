# Limitations and platforms, Vericairn 0.4.2

Written before the release build, as the documentation convention requires from 0.3.0 on. The release record in the Vericairn estate, written when the draft is observed and superseded by the published revision, is the authority for the commit, the workflow run, every artifact's hash and the publication time; `SHA256SUMS` on the release page lists the same hashes. What follows is what was known when the set was frozen.

## Qualified platforms

The release workflow runs the tests on these platforms before packaging, so these are the platforms the release is qualified for:

- linux-x64 (ubuntu-latest)
- osx-arm64 (macos-latest)
- win-x64 (windows-latest)

The linux-arm64, osx-x64 and win-arm64 distributables are cross-published from the same commit and have not been executed anywhere. They are provided, not qualified. Workbench's platform qualification is separate from the command line's and is stated below.

## Signing

Unsigned developer build. The operator's signing material does not exist yet. Verify a download by its checksum against the estate's manifest or the release record, never against the tag name alone; `install.md` shows how. Workbench ships without an application bundle, an installer or a signature.

## What 0.4.2 changes and corrects

- The license texts, new in 0.4.2: every archive carries in `licenses/` the license and notice texts of every component it bundles, which the third-party notices list with their copyright lines: the .NET runtime and the ASP.NET Core shared framework with their own third-party notices, the Microsoft and other MIT components, SQLitePCLRaw under Apache-2.0, SQLite's public-domain statement, and in Workbench's archives also Avalonia, ANGLE, SkiaSharp, HarfBuzzSharp and the two fonts under the SIL Open Font License. The same work was withdrawn twice before acceptance and publication: 0.4.0 after its independent review found the fonts' licenses missing from the Workbench archives, and 0.4.1 after its review found ASP.NET Core's notices missing. The release workflow fails when a key license file is missing from an archive, and a test holds the lock files, the shared frameworks the projects reference, the notices and the license texts together.
- The release notes list a result as accepted only when an acceptance of its exact revision is on record; results still awaiting acceptance appear under their own heading, where earlier notes listed every result of the period as accepted.
- The brand direction (BRAND-01): the theme's roles are bound with the values adopted on October 14, 2026: Inter and JetBrains Mono bundled under the SIL Open Font License, one accent blue, one attention amber used only for the attention kinds, neutral surfaces and outlines. The probe measures the contrast of every pair the roles form in light and dark; the outline values were darkened when that measurement found the earlier placeholders too faint.
- Providers, adapters and the software pack out of the foundation (PLACEMENT-01): the GitHub Releases provider, the git adapter and the Software Domain Pack are modules of their own inside the Community distributable, registered by the command line and selected by a recorded configuration, never a compiled default. A product may declare its release destination and artifact set on its product record; `vericairn release providers` and `GET /v1/providers` list what the build carries; Workbench's release workspace shows the selected provider's declared settings. Reference generation is build tooling, not part of the shipped command line, and `vericairn docs check --documents` checks a set against the product's declaration.
- The first-launch fix of 0.3.1, carried to main: the daemon `runtime start` launches has streams of its own and writes to its log, and Workbench waits for that command's exit, never for the end of its output.
- The acting principal named truthfully (FIX-01): the target bar names the principal Workbench writes as by its current name, with the device the runtime signs with; a principal named with `--as` selects its own device in the selector, and when none of its devices is held here the bar says whether its writes are refused or unsigned. In 0.3.1 the bar showed such a principal under its first revision's name beside the first device held on the machine; the records were still authored and signed correctly.
- A failed daemon start explained (FIX-01): `runtime start` opens the daemon's log before it starts the daemon, and a daemon that stops before serving is reported with what it wrote there and where the log is; a detached daemon writes every failure there.
- The 0.3.1 command-line reference left out the `--version` option of `release submit` and `release observe`; this set's reference lists it.

## Record format

Reads record format 1, writes record format 1. The product record gains an optional `release` object, the product's release profile; published releases before 0.4.2 refuse a product record that carries one (the withdrawn 0.4.0 and 0.4.1 builds accept it), so an estate declares it only after every clone that writes to the estate runs 0.4.2. No record type was added. The upgrade from 0.3.1 pins the manifest and writes a migration record with no record migration.

## Workbench

- Qualification: Workbench is not qualified on any platform. When this set was frozen, the independent reviews of the withdrawn 0.4.0 and 0.4.1 candidates had observed their Workbench's native first launch on macOS and measured its release workspace (no target below 24 layout units; every contrast pair at or above its minimum), and the platform obligations recorded the accessibility probe's measurement of the shipped 0.3.1 release workspace on one macOS native desktop (no target below 24 layout units, the splitters exempt as pointer conveniences with keyboard alternatives), a native first launch on macOS by the Reviewer principal and by the operator, and the operator's acceptance of 0.3.1 written through the daemon; the keyboard-only journeys of the work loop and the release flow, the first launch through the real command line and the acting-principal display run headless on the three platforms as automation. No native desktop run with a person at the keyboard or a screen reader is recorded on any platform; the Windows and Linux native cells are non-claims. The verifications are in the Vericairn estate's platform obligations (`design/platforms.md`).
- Projects are not a record type of format 1; the products page says so instead of inventing them.
- The comparison view is a line difference of canonical JSON, not a field-aware merge.
- Docking is panel placement with keyboard alternatives, not free drag-and-drop of every panel.
- The release workspace offers the providers the runtime can reach; 0.4.2 carries one, GitHub Releases.
- Workbench finds the command line beside its executable, through `VERICAIRN_CLI` or on the `PATH`; it starts, stops, installs and uninstalls the daemon through that command line and never hosts the runtime.

## The MCP server

- Protocol revisions 2025-06-18, 2025-03-26 and 2024-11-05 over standard input and output; `initialize`, `ping`, `tools/list` and `tools/call` only. Resources, prompts, sampling and the HTTP transport are not implemented.
- The write tools (`work_attempt`, `work_result`, `work_amend`) are visible only for a principal whose admitted device key the machine holds; there is no tool that publishes, spends, creates or admits a device, runs a command or authors an arbitrary record type.
- The server trusts the operating-system identity of whoever runs it; it is not a network service.
- Every agent harness is a non-claim in the orientation file's matrix until a session with it is observed and recorded.

## Other known limitations carried from earlier releases

- The loopback profile is pinned to the runtime certificate and device-signed; the local-network profile and key custody beyond owner-only files on macOS and Linux are milestone M3.
- A process of the same operating-system user can sign with the device key held in file custody on macOS and Linux; that is the recorded strength of that custody.
- The GitHub Releases provider identifies a dispatched run by listing recent runs and records the operation as uncertain when none appears within thirty seconds; the provider runs on the runtime's machine under the user's own `gh` login. It is the only release provider 0.4.2 carries; the in-memory provider exists for tests only.
- Verifications record a command's exit, duration and output and do not parse test reports.
- The daemon does not yet run admitted work or supervise workers (milestone M4).
- The third-party notices and the `licenses` directory are maintained by hand beside the generated bill of materials; a test holds them to the lock files.
- A device cannot be renamed by a superseding record, because a device record is signed by its own key to prove possession; milestone M3 closes this gap.
- Live stores of estate clones that no longer exist stay in the per-user state until removed by hand; no command lists or removes them yet.

## The artifact set

The release workflow produces, for the version `0.4.2`:

| Artifact | What it is |
|---|---|
| `vericairn-0.4.2-<platform>.tar.gz` (`.zip` for Windows), six platforms | The command line, self-contained, with LICENSE, NOTICE, the third-party notices and `licenses/` |
| `vericairn-workbench-0.4.2-<platform>.tar.gz` (`.zip` for Windows), six platforms | Workbench with a copy of the command line beside it, self-contained, with LICENSE, NOTICE, the third-party notices and `licenses/` |
| `vericairn-0.4.2-sbom.cdx.json` | The software bill of materials (CycloneDX) |
| `vericairn-0.4.2-THIRD-PARTY-NOTICES.md` | The third-party notices |
| `vericairn-0.4.2-docs.zip` | This documentation set |
| `SHA256SUMS` | Every artifact's SHA-256 |

The sizes and hashes are in the release record and in `SHA256SUMS`; this frozen document does not repeat numbers it could not know before the build.
