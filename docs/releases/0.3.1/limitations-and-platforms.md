# Limitations and platforms, Vericairn 0.3.1

Written before the release build, as the documentation convention requires from 0.3.1 on. The release record in the Vericairn estate, written when the draft is observed and superseded by the published revision, is the authority for the commit, the workflow run, every artifact's hash and the publication time; `SHA256SUMS` on the release page lists the same hashes. What follows is what was known when the set was frozen.

## Qualified platforms

The release workflow runs the tests on these platforms before packaging, so these are the platforms the release is qualified for:

- linux-x64 (ubuntu-latest)
- osx-arm64 (macos-latest)
- win-x64 (windows-latest)

The linux-arm64, osx-x64 and win-arm64 distributables are cross-published from the same commit and have not been executed anywhere. They are provided, not qualified. Workbench's platform qualification is separate from the command line's and is stated below.

## Signing

Unsigned developer build. The operator's signing material does not exist yet. Verify a download by its checksum against the estate's manifest or the release record, never against the tag name alone; `install.md` shows how. Workbench ships without an application bundle, an installer or a signature.

## What 0.3.1 corrects

Release 0.3.0 was withdrawn before acceptance and publication because Workbench never opened an estate on a first launch with no daemon running: it waited for the end of the output of `runtime start`, which the daemon inherited and held open. In 0.3.1 the daemon gets streams of its own and writes to its log (on Windows the command line also keeps its own standard handles from being inherited), Workbench's runtime manager waits for the command's exit and collects its output for a moment afterwards, and two tests run the real command line as a separate process, one through Workbench's own runtime manager, on Ubuntu, macOS and Windows. When it observes a release, 0.3.1 also labels each `vericairn-workbench-<version>-<platform>` distributable with its platform instead of `any`.

## Record format

Reads record format 1, writes record format 1. No record type was added since 0.2.0; an estate on 0.2.0 reads every record a 0.3.1 estate writes. The upgrade from 0.2.0 pins the manifest and writes a migration record with no record migration.

## Workbench

- Qualification: the desktop's screens (first run, home, products, inbox, release and runtime workspaces) were measured by the accessibility probe at compact density on one macOS native desktop, with no target below 24 layout units and the splitters exempt as pointer conveniences with keyboard alternatives; the keyboard-only journeys of the work loop and the release flow run headless on the three platforms as automation. No native desktop run with a person at the keyboard or a screen reader is recorded on any platform; the Windows and Linux native cells are non-claims. The verifications are in the Vericairn estate's platform obligations (`design/platforms.md`).
- The brand's token roles are bound (eight color roles in light and dark variants, four type roles, three scale roles, two motion roles); the values are neutral placeholders until the brand document's second revision records the operator's direction.
- Projects are not a record type of format 1; the products page says so instead of inventing them.
- The comparison view is a line difference of canonical JSON, not a field-aware merge.
- Docking is panel placement with keyboard alternatives, not free drag-and-drop of every panel.
- The release workspace observes one destination, GitHub Releases.
- Workbench finds the command line beside its executable, through `VERICAIRN_CLI` or on the `PATH`; it starts, stops, installs and uninstalls the daemon through that command line and never hosts the runtime.

## The MCP server

- Protocol revisions 2025-06-18, 2025-03-26 and 2024-11-05 over standard input and output; `initialize`, `ping`, `tools/list` and `tools/call` only. Resources, prompts, sampling and the HTTP transport are not implemented.
- The write tools (`work_attempt`, `work_result`, `work_amend`) are visible only for a principal whose admitted device key the machine holds; there is no tool that publishes, spends, creates or admits a device, runs a command or authors an arbitrary record type.
- The server trusts the operating-system identity of whoever runs it; it is not a network service.
- Every agent harness is a non-claim in the orientation file's matrix until a session with it is observed and recorded.

## Other known limitations carried from 0.2.0

- The loopback profile is pinned to the runtime certificate and device-signed; the local-network profile and key custody beyond owner-only files on macOS and Linux are milestone M3.
- A process of the same operating-system user can sign with the device key held in file custody on macOS and Linux; that is the recorded strength of that custody.
- The GitHub Releases provider identifies a dispatched run by listing recent runs and records the operation as uncertain when none appears within thirty seconds; the provider runs on the runtime's machine under the user's own `gh` login.
- Verifications record a command's exit, duration and output and do not parse test reports.
- The daemon does not yet run admitted work or supervise workers (milestone M4).
- The third-party notices are maintained by hand beside the generated bill of materials.
- A device cannot be renamed by a superseding record, because a device record is signed by its own key to prove possession.

## The artifact set

The release workflow produces, for the version `0.3.1`:

| Artifact | What it is |
|---|---|
| `vericairn-0.3.1-<platform>.tar.gz` (`.zip` for Windows), six platforms | The command line, self-contained, with LICENSE, NOTICE and the third-party notices |
| `vericairn-workbench-0.3.1-<platform>.tar.gz` (`.zip` for Windows), six platforms | Workbench with a copy of the command line beside it, self-contained |
| `vericairn-0.3.1-sbom.cdx.json` | The software bill of materials (CycloneDX) |
| `vericairn-0.3.1-THIRD-PARTY-NOTICES.md` | The third-party notices |
| `vericairn-0.3.1-docs.zip` | This documentation set |
| `SHA256SUMS` | Every artifact's SHA-256 |

The sizes and hashes are in the release record and in `SHA256SUMS`; this frozen document does not repeat numbers it could not know before the build.
