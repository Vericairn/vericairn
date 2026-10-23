# Vericairn 0.8.3

Vericairn keeps work, decisions, authority, evidence and releases as append-only,
content-addressed, signed records. The command line, Workbench, API/SDK and MCP
server share one runtime. Results are claims to inspect; acceptance and publication
remain the operator's actions.

0.8.3 follows published 0.8.2. A first estate needs no hand-written JSON, an estate other
than Vericairn's own can pin and upgrade to the published Vericairn release it depends on,
a release can be checked before submission, withdrawn and replanned with single commands,
and every platform's archive carries the same library builds. It also corrects how a
checked push runs git, as the release notes' Security section describes. It reads record
formats 1, 2 and 3 and writes the estate's own format, at most 3. The estate's release
record establishes the release commit, workflow, artifact hashes, qualified platforms and
publication; this set establishes none of those release facts. NuGet availability follows
publication of the accepted draft and a successful package workflow. The tool remains
framework-dependent and includes neither Workbench nor the macOS Secure Enclave helper.

## What changes

- **FIX-47:** `estate init` pins the running build's exact source commit, as
  `vericairn --version` reports it, when `--commit` is omitted; a development build without
  a recorded commit still requires the option.
- **CODE-07:** `vericairn work new` records a work order from options, with the same
  validation and refusals as `records new work-order`. `IEstateApi` gains `WorkNew`;
  [API compatibility](api-compatibility-0.8.3.md) lists it.
- **CODE-08:** `vericairn release withdraw` records a candidate's withdrawal, and
  `vericairn work move-plan` moves a release plan's planned work to another version in one
  step.
- **FIX-52:** `estate upgrade --to <version>`, run with the target release's command line,
  verifies a published Vericairn release from GitHub, its tag and `SHA256SUMS` and this
  machine's platform archive, before it records a `vericairn-release` observation with the
  manifest move. A source pin moves to release mode the same way, `--from <directory>`
  verifies downloaded evidence offline, `estate init --release` starts an estate in release
  mode, and `manifest.release-behind` also warns for source pins
  ([issue 177](https://github.com/Vericairn/vericairn/issues/177)). The record format is
  unchanged.
- **FIX-55:** `vericairn release submit --check`, `POST /v1/releases/check`, the MCP tool
  `release_submit_check` and Workbench's Check submission run the whole admission gate and
  report every objection with its next action, recording nothing. A format-3 submission
  requires the client build to equal the runtime's.
- **FIX-60:** a checked push runs git without system or global configuration, reaches the
  remote from a private repository that ignores the clone's configuration and hooks, and
  allows only the declared remote's protocol; see the release notes' Security section and
  [checked changes](changes.md).
- **FIX-61:** library projects build without a runtime identifier, so every platform's
  archive carries byte-identical Vericairn libraries and a policy basis is the same on every
  platform ([issue 184](https://github.com/Vericairn/vericairn/issues/184)); the packaging
  job compares the embedded libraries of all six platforms' command lines and Workbench.
- **FIX-62:** after an upgrade, the command line warns when an older background runtime
  still serves the estate.
- **FIX-50:** a living document's own file is always among its covered paths, so a change
  to it makes the document due everywhere the release gate looks.
- **FIX-56:** a work-order revision that changes only its placement keeps the
  assignments, attempts, results and reviews on its predecessor current.
- **FIX-57:** retrying a canceled submission intent no longer records an extra
  pre-dispatch revision.
- **FIX-58:** `release basis` tells an unknown hash apart from a retained basis that
  cannot resolve, naming each failed reference; `ReleaseBasisResponse.Evidence` becomes
  nullable.
- **FIX-59:** the documentation set's client-outcomes page keeps the section on stored
  records awaiting companion recovery.
- **CI-04:** CI runs each platform's published command line on its own runner, validates
  the packages against the published 0.8.2 packages, scans the repository with the product's
  secret-scan configuration and lists the runtime packs in the bill of materials.
- **BRAND-04:** the repository README leads with present value, first steps and limits,
  and names the official destinations.

The [limitations guide](limitations-and-platforms.md) retains the worker custody,
credential isolation, billing, context accounting, transport, recovery and native
desktop limits. The efficiency Adapter (EFF-01, placed in 0.8.4) and Remote remain
unbuilt. These changes establish no additional live-harness or native-platform qualification.

## Upgrading from 0.8.2

An estate at format 3 upgrades from 0.8.2 or 0.8.0 without a format step. Install the
verified 0.8.3 command line, restart any daemon that serves the estate with it, then inspect
and apply `vericairn estate upgrade --to 0.8.3` as described in [installation](install.md).
Vericairn's own estate upgrades against its release record; any other estate now verifies
and records the published release itself. Restart running workers after upgrading: a
worker keeps the behavior of the release it runs. An estate at 0.7.3 or earlier follows
0.8.0's format-3 upgrade and adoption steps, which the installation guide retains.

## This documentation set

The references and manifest were generated on October 23, 2026 UTC from the Release
build of `a1aac5a1849570994a6f676dfd5242e5ee19a748`, whose implementation includes
the product changes listed above. They contain 150 commands, 108 API operations and
42 record types, with 34 required documents. That commit is
reference generation provenance, not the later documentation or final release commit.
The generated files retain the tool's bytes, including their retained inputs, reference
inventory, reference index and CLI client-outcome excerpt. This generation selected no
register snapshot; the index says so rather than asserting mappings.

The maintained guides are copied byte for byte into this set; `api.md` and
`api-v1.md` both copy the maintained API v1 guide so their relative links resolve.
The installation, operator, limitations and format-index guides describe 0.8.3 and
the older behavior it retains. Earlier version directories and their checksum index
keep their original contents.

The notes command uses `--from 0.8.2 --to 0.8.3`. Its exclusive start follows the
end of 0.8.2's recorded notes window, and it writes the exact new window to the
manifest. Accepted work is grouped by work order with its latest accepted current
result and acceptance; later unaccepted results stay separate. Classification uses
current estate state and creates no acceptance. [Release notes](release-notes.md)
are the estate-derived part of the set.

| Document | What it covers |
|---|---|
| [install.md](install.md), [operator-guide.md](operator-guide.md) | Installation, upgrade, work, releases, workers and operator journeys |
| [cli-reference.md](cli-reference.md), [api-reference.md](api-reference.md), [record-reference.md](record-reference.md) | Generated commands, API operations and record types |
| [record-format.md](record-format.md) | Compatibility and the relationship between formats 1–3 |
| [record-format-v1.md](record-format-v1.md), [record-format-v2.md](record-format-v2.md), [record-format-v3.md](record-format-v3.md) | Base format and the changes introduced by formats 2 and 3 |
| [api.md](api.md), [api-v1.md](api-v1.md) | Identical API guides, transports, authentication, SDK and recovery |
| [software.md](software.md), [changes.md](changes.md) | Repositories, checked changes, release providers, CI and retained companions |
| [workbench.md](workbench.md), [mcp.md](mcp.md) | Desktop and MCP surfaces, permissions and limits |
| [living-documents.md](living-documents.md) | Living documents, their covered paths and when they are due |
| [required-attention.md](required-attention.md), [operator-actions.md](operator-actions.md) | Current obligations, exact resolution, signed operator actions and package registry observations |
| [communication-adoption.md](communication-adoption.md), [communication-qualification.md](communication-qualification.md) | Explicit personal settings and retained communication/context evidence |
| [client-operations.md](client-operations.md), [cli-client-outcomes.md](cli-client-outcomes.md) | Cancellation, deadlines and uncertain writes |
| [api-compatibility-0.8.0.md](api-compatibility-0.8.0.md), [api-compatibility-0.8.2.md](api-compatibility-0.8.2.md), [api-compatibility-0.8.3.md](api-compatibility-0.8.3.md) | Public API compatibility of 0.8.0, 0.8.2 and 0.8.3 |
| [policies.md](policies.md), [policy-evaluator.md](policy-evaluator.md) | Policy surfaces, recurring duties, evidence and the combined release gate |
| [policy-adoption.md](policy-adoption.md), [policy-adoption-proposals.md](policy-adoption-proposals.md) | Adoption procedure, exact proposed bodies and unresolved operator inputs |
| [readable-companions.md](readable-companions.md), [reference-inventory.md](reference-inventory.md) | Generator contracts, reading surfaces, inventories and coverage rules |
| [reference-index.md](reference-index.md), [reference-inventory.json](reference-inventory.json) | Generated reference membership and explicit register-selection status |
| [release-notes.md](release-notes.md) | The estate's records in the release window |
| [limitations-and-platforms.md](limitations-and-platforms.md) | Remaining limits, historical qualifications and expected artifacts |
| [manifest.json](manifest.json) | Generation provenance, required documents, inventories and record formats |

## Checking the version and set

```sh
vericairn version
vericairn docs check docs/releases/0.8.3 --documents docs/releases/documents.json
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.8.3
```

With the NuGet tool, replace `vericairn` with `dotnet vc`. A packaged release prints
`0.8.3+<commit>`; an ordinary source build retains `-dev`. Release-check disclosure
and notices go to stderr. The estate pin changes only through upgrade. Structural
checks and build comparison do not judge prose, qualify a platform or accept a
release. The frozen-set guard protects every indexed earlier set.

The release record and its evidence supply authoritative artifact hashes and
qualification. Source and Community packages are under Apache License 2.0;
copyright (c) 2026 Jason Furr.
