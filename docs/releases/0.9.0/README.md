# Vericairn 0.9.0

Vericairn keeps work, decisions, authority, evidence and releases as append-only,
content-addressed, signed records. The command line, Workbench, Remote, API/SDK and MCP
server share one runtime. Results are claims to inspect; acceptance and publication
remain the operator's actions.

0.9.0 follows published 0.8.4. It brings Remote, the phone companion for iOS and
Android, as a developer build: pairing, attention and work, decisions and acceptances
signed with the phone's presence key, and policy and recurring-duty attention. It adds
release lines, channels and cadence, the disclosure hold and blocked terms, a redacted
support bundle, requested progress and the overdue signal, generated revision lines,
locally prepared community build guides, an opt-in local efficiency adapter that is
disabled by default, and guides for the three operating modes and for agent frameworks.
It also hardens worker runs' temporary directories, charter revisions under required
signatures and which document revisions clear, as the release notes' Security section
describes. It reads record formats 1, 2 and 3 and writes the estate's own format, at
most 3. The estate's release record establishes the release commit, workflow, artifact
hashes, qualified platforms and publication; this set establishes none of those release
facts. NuGet availability follows publication of the accepted draft and a successful
package workflow. The NuGet tool remains framework-dependent and includes neither
Workbench nor the macOS Secure Enclave helper.

## What changes

- **REMOTE-01, REMOTE-02, REMOTE-03, REMOTE-11:** Remote's shell for iOS and Android,
  sharing Workbench's presentation, with pairing, attention and work on the phone, and
  decisions and acceptances signed only with the phone's presence key from a basis the
  runtime supplies; [remote.md](remote.md) describes the developer build and its limits.
- **POLICY-05:** estate policy and recurring-duty findings in Remote's attention, with
  their exact history and desktop handoff; [policies.md](policies.md) and
  [remote.md](remote.md) describe them.
- **CODE-12, CODE-13, CODE-14:** release lines and channels on the record and product,
  estates that follow a release line, and release cadence as a recurring duty.
- **CODE-16, CODE-17:** the disclosure hold, enforced by the public-repository push scan,
  and configured blocked terms refused in records and generated documents.
- **CODE-11:** `vericairn support bundle`, a redacted diagnostic bundle the user inspects
  before sharing.
- **WATCH-02, WATCH-03:** requested progress with its typed claim, its surfaces and the
  overdue signal.
- **OPS-04:** settled runs withdraw their warnings, and answered asks close.
- **DOCS-10:** revision lines generated from document-revision pins.
- **GUIDE-01, GUIDE-02:** community build guides from a source inventory, template and
  deterministic generator with private previews, and the public catalog repository's
  local preparation, contribution flow, request form and publication check;
  [community-build-guides.md](community-build-guides.md) describes them.
- **EFF-01:** an opt-in local efficiency adapter, disabled by default and admitted only by
  the operator's machine configuration; [efficiency-adapter.md](efficiency-adapter.md).
- **DOCS-13, DOCS-14:** guides for agent builders and for LangGraph, CrewAI and AutoGen
  over MCP ([agent-builders.md](agent-builders.md)), and for running Vericairn with
  people only, with people and agents, and with agents only
  ([people-only.md](people-only.md), [people-and-agents.md](people-and-agents.md),
  [agents-only.md](agents-only.md)).
- **DOCS-09:** a public example estate and product, upgraded with every release.
- **FIX-73:** the update check bounds its own timeout and fails soft.
- **FIX-75:** status stays fast as the estate grows, under a CI budget.
- **FIX-76, FIX-82:** a run whose settlement went unrecorded, or whose supervising process
  died, can still settle.
- **FIX-77:** building Vericairn from source sends no build telemetry.
- **FIX-78:** an authoring operation that can wait on a presence prompt observes for
  longer than the prompt.
- **FIX-80:** a new estate keeps its records' bytes on every platform.
- **FIX-81:** Codex workers keep running when the installed Codex moves past the baseline.
- **FIX-83:** an estate's registers stay valid across an upgrade whose generator output
  changes.
- [API compatibility](api-compatibility-0.9.0.md) lists the public API changes.

The release notes record work verified but not yet accepted at this set's window. The
Security section covers the hardening (FIX-74, FIX-79 and FIX-84).

The [limitations guide](limitations-and-platforms.md) retains the worker custody,
credential isolation, billing, context accounting, transport, recovery and native
desktop limits. Remote is a developer build with no app-store distribution and no
recorded real-device qualification. These changes establish no additional live-harness
or native-platform qualification.

## Upgrading from 0.8.4

An estate at format 3 upgrades from 0.8.4, 0.8.3, 0.8.2 or 0.8.0 without a format step.
Install the verified 0.9.0 command line, stop any daemon the older release started and
restart it with 0.9.0, then inspect and apply `vericairn estate upgrade --to 0.9.0` as
described in [installation](install.md). The upgrade rewrites generated register members
that an earlier generator wrote, and names them in its check, receipt and migration
record. Vericairn's own estate upgrades against its release record; any other estate
verifies and records the published release itself. Restart running workers after
upgrading: a worker keeps the behavior of the release it runs. An estate at 0.7.3 or
earlier follows 0.8.0's format-3 upgrade and adoption steps, which the installation
guide retains.

## This documentation set

The references and manifest were generated on November 2, 2026 UTC from the Release
build of `9f5cadc281839f43baa2ee6ebee1c0b944ffda1d`, whose implementation includes the
product changes listed above. They contain 191 commands, 140 API operations and 47
record types, with 52 required documents. That commit is reference generation
provenance, not the later documentation or final release commit. The generated files
retain the tool's bytes, including their retained inputs, reference inventory, reference
index and CLI client-outcome excerpt. This generation selected no register snapshot; the
index says so rather than asserting mappings.

The maintained guides are copied byte for byte into this set; `api.md` and `api-v1.md`
both copy the maintained API v1 guide so their relative links resolve. The installation,
operator, limitations and format-index guides describe 0.9.0 and the older behavior it
retains. Earlier version directories and their checksum index keep their original
contents.

**Correction to the 0.8.4 set.** Its operator, limitations and software guides say the release
notes' window follows 0.8.2's recorded end; it followed 0.8.3's, as that set's manifest records. This
set's guides name 0.8.4's recorded end for 0.9.0's window (decision `01M5YYKV6P054QZN9K5WM3ME32`).

The notes command uses `--from 0.8.4 --to 0.9.0`. Its exclusive start follows the end of
0.8.4's recorded notes window, and it writes the exact new window to the manifest.
Accepted work is grouped by work order with its latest accepted current result and
acceptance; later unaccepted results stay separate. Classification uses current estate
state and creates no acceptance. [Release notes](release-notes.md) are the estate-derived
part of the set.

| Document | What it covers |
|---|---|
| [install.md](install.md), [operator-guide.md](operator-guide.md) | Installation, upgrade, work, releases, workers and operator journeys |
| [people-only.md](people-only.md), [people-and-agents.md](people-and-agents.md), [agents-only.md](agents-only.md) | Running Vericairn in each operating mode |
| [cli-reference.md](cli-reference.md), [api-reference.md](api-reference.md), [record-reference.md](record-reference.md) | Generated commands, API operations and record types |
| [cli.md](cli.md) | The commands added since 0.8.3 |
| [record-format.md](record-format.md) | Compatibility and the relationship between formats 1–3 |
| [record-format-v1.md](record-format-v1.md), [record-format-v2.md](record-format-v2.md), [record-format-v3.md](record-format-v3.md) | Base format and the changes introduced by formats 2 and 3 |
| [api.md](api.md), [api-v1.md](api-v1.md) | Identical API guides, transports, authentication, SDK and recovery |
| [software.md](software.md), [changes.md](changes.md) | Repositories, checked changes, release providers, CI, workers and retained companions |
| [workbench.md](workbench.md), [remote.md](remote.md), [mcp.md](mcp.md) | Desktop, phone and MCP surfaces, permissions and limits |
| [agent-builders.md](agent-builders.md), [frameworks/langgraph.md](frameworks/langgraph.md), [frameworks/crewai.md](frameworks/crewai.md), [frameworks/autogen.md](frameworks/autogen.md) | Building agents on Vericairn over MCP |
| [efficiency-adapter.md](efficiency-adapter.md) | The opt-in local efficiency adapter's contract and limits |
| [community-build-guides.md](community-build-guides.md) | Community build guides and the catalog repository's local preparation |
| [templates.md](templates.md), [presentation-templates.md](presentation-templates.md) | Declarative document templates, their sections, cohesion and the installed content forms |
| [prompts.md](prompts.md), [prompt-record-reference.md](prompt-record-reference.md) | The saved-prompt library, categories, favorites and the prompt record |
| [operator-requests.md](operator-requests.md) | Durable operator requests, their reasons and their recorded outcomes |
| [living-documents.md](living-documents.md) | Living documents, their covered paths and when they are due |
| [required-attention.md](required-attention.md), [operator-actions.md](operator-actions.md) | Current obligations, exact resolution, signed operator actions and package registry observations |
| [communication-adoption.md](communication-adoption.md), [communication-qualification.md](communication-qualification.md) | Explicit personal settings and retained communication/context evidence |
| [client-operations.md](client-operations.md), [cli-client-outcomes.md](cli-client-outcomes.md) | Cancellation, deadlines and uncertain writes |
| [api-compatibility-0.8.0.md](api-compatibility-0.8.0.md), [api-compatibility-0.8.2.md](api-compatibility-0.8.2.md), [api-compatibility-0.8.3.md](api-compatibility-0.8.3.md), [api-compatibility-0.8.4.md](api-compatibility-0.8.4.md), [api-compatibility-0.9.0.md](api-compatibility-0.9.0.md) | Public API compatibility of 0.8.0, 0.8.2, 0.8.3, 0.8.4 and 0.9.0 |
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
vericairn docs check docs/releases/0.9.0 --documents docs/releases/documents.json
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.9.0
```

With the NuGet tool, replace `vericairn` with `dotnet vc`. A packaged release prints
`0.9.0+<commit>`; an ordinary source build retains `-dev`. Release-check disclosure
and notices go to stderr. The estate pin changes only through upgrade. Structural
checks and build comparison do not judge prose, qualify a platform or accept a
release. The frozen-set guard protects every indexed earlier set.

The release record and its evidence supply authoritative artifact hashes and
qualification. Source and Community packages are under Apache License 2.0;
copyright (c) 2026 Jason Furr.
