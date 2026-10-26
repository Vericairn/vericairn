# Vericairn 0.8.4

Vericairn keeps work, decisions, authority, evidence and releases as append-only,
content-addressed, signed records. The command line, Workbench, API/SDK and MCP
server share one runtime. Results are claims to inspect; acceptance and publication
remain the operator's actions.

0.8.4 follows published 0.8.3. It adds a shared library of saved prompts, declarative
document templates for the estate's own documents and for communication, a signed
operator-close action and durable operator requests, a release-review brief generated
from the records and run as an assignment, a release documentation pass, and MCP
read-only resources and prompts. It also hardens how a checked push reads its local
objects, as the release notes' Security section describes. It reads record formats 1, 2
and 3 and writes the estate's own format, at most 3. The estate's release record
establishes the release commit, workflow, artifact hashes, qualified platforms and
publication; this set establishes none of those release facts. NuGet availability
follows publication of the accepted draft and a successful package workflow. The tool
remains framework-dependent and includes neither Workbench nor the macOS Secure Enclave
helper.

## What changes

- **PROMPT-01:** a shared, categorized prompt library, with built-in foundation and pack
  prompts and personal favorites, found by category across the estate, the foundation and
  installed packs, and used with new runs, assignments and ask answers; `prompts.md` and
  [prompt-record-reference.md](prompt-record-reference.md) describe it.
- **TPL-01, TPL-02, TPL-COMMS-01:** declarative document templates — the contract,
  instances, sections and their states, reviews, cohesion and briefs — with the estate's own
  design-document, open-decision, index, release-notes and README templates, and
  communication, context, commit and review templates; see [templates.md](templates.md) and
  [presentation-templates.md](presentation-templates.md).
- **OPS-02:** `vericairn operator close` closes selected finished steps with one signed
  action after showing their results and reviews.
- **OPS-03:** operator requests record what the builder or an agent asks the operator to do,
  stored durably and closed as done or dismissed; [operator-requests.md](operator-requests.md)
  describes them.
- **CODE-09:** a release-review brief is generated from the records and run as an assignment
  to the Reviewer, with requested progress.
- **CODE-10:** a guided decision from an answered ask records the operator's words and time, a
  document list held to the change, and the document pins.
- **DOCS-12:** a release documentation pass sends each document a publication makes due to a
  reviewer with the release's changes.
- **MCP-02:** briefs, records, pinned documents and context packages are read-only MCP
  resources, and saved prompts are MCP prompts; [mcp.md](mcp.md) lists them.
- **FIX-68:** every estate receives the first-party `bootstrap` launcher.
- **FIX-66:** `estate init --release` names the release observation it records.
- **FIX-64:** release notes render a superseded record only as its current revision.
- **FIX-65:** a run whose settlement write failed can be settled later.
- **FIX-69:** a charter revision leaves the release pin's observation current.
- **FIX-72:** the package commands record reserved placeholder versions, which stay current
  across a product revision; [API compatibility](api-compatibility-0.8.4.md) lists the change.
- **CI-05:** CI steps and jobs carry time limits.

The release notes also record work verified but not yet accepted at this set's window —
FIX-63 (status within its deadline on Windows), FIX-70 (J04 under a controlled clock),
FIX-71 (named and checked agent products) and FIX-67 (a checked push's local reads do not
trust derived object data; the Security section covers it).

The [limitations guide](limitations-and-platforms.md) retains the worker custody,
credential isolation, billing, context accounting, transport, recovery and native
desktop limits. The efficiency Adapter (EFF-01, placed in 0.9.0) and Remote remain
unbuilt. These changes establish no additional live-harness or native-platform
qualification.

## Upgrading from 0.8.3

An estate at format 3 upgrades from 0.8.3, 0.8.2 or 0.8.0 without a format step. Install
the verified 0.8.4 command line, restart any daemon that serves the estate with it, then
inspect and apply `vericairn estate upgrade --to 0.8.4` as described in
[installation](install.md). Vericairn's own estate upgrades against its release record; any
other estate verifies and records the published release itself. Restart running workers
after upgrading: a worker keeps the behavior of the release it runs. An estate at 0.7.3 or
earlier follows 0.8.0's format-3 upgrade and adoption steps, which the installation guide
retains.

## This documentation set

The references and manifest were generated on October 26, 2026 UTC from the Release
build of `3a9bd5f6a108f85017193ac308525a05263a4c4d`, whose implementation includes the
product changes listed above. They contain 180 commands, 134 API operations and 47
record types, with 41 required documents. That commit is reference generation
provenance, not the later documentation or final release commit. The generated files
retain the tool's bytes, including their retained inputs, reference inventory, reference
index and CLI client-outcome excerpt. This generation selected no register snapshot; the
index says so rather than asserting mappings.

The maintained guides are copied byte for byte into this set; `api.md` and `api-v1.md`
both copy the maintained API v1 guide so their relative links resolve. The installation,
operator, limitations and format-index guides describe 0.8.4 and the older behavior it
retains. Earlier version directories and their checksum index keep their original
contents.

The notes command uses `--from 0.8.3 --to 0.8.4`. Its exclusive start follows the end of
0.8.3's recorded notes window, and it writes the exact new window to the manifest.
Accepted work is grouped by work order with its latest accepted current result and
acceptance; later unaccepted results stay separate. Classification uses current estate
state and creates no acceptance. [Release notes](release-notes.md) are the estate-derived
part of the set.

| Document | What it covers |
|---|---|
| [install.md](install.md), [operator-guide.md](operator-guide.md) | Installation, upgrade, work, releases, workers and operator journeys |
| [cli-reference.md](cli-reference.md), [api-reference.md](api-reference.md), [record-reference.md](record-reference.md) | Generated commands, API operations and record types |
| [cli.md](cli.md) | The commands 0.8.4 adds to the command line |
| [record-format.md](record-format.md) | Compatibility and the relationship between formats 1–3 |
| [record-format-v1.md](record-format-v1.md), [record-format-v2.md](record-format-v2.md), [record-format-v3.md](record-format-v3.md) | Base format and the changes introduced by formats 2 and 3 |
| [api.md](api.md), [api-v1.md](api-v1.md) | Identical API guides, transports, authentication, SDK and recovery |
| [software.md](software.md), [changes.md](changes.md) | Repositories, checked changes, release providers, CI and retained companions |
| [workbench.md](workbench.md), [mcp.md](mcp.md) | Desktop and MCP surfaces, permissions and limits |
| [templates.md](templates.md), [presentation-templates.md](presentation-templates.md) | Declarative document templates, their sections, cohesion and the installed content forms |
| [prompts.md](prompts.md), [prompt-record-reference.md](prompt-record-reference.md) | The saved-prompt library, categories, favorites and the prompt record |
| [operator-requests.md](operator-requests.md) | Durable operator requests, their reasons and their recorded outcomes |
| [living-documents.md](living-documents.md) | Living documents, their covered paths and when they are due |
| [required-attention.md](required-attention.md), [operator-actions.md](operator-actions.md) | Current obligations, exact resolution, signed operator actions and package registry observations |
| [communication-adoption.md](communication-adoption.md), [communication-qualification.md](communication-qualification.md) | Explicit personal settings and retained communication/context evidence |
| [client-operations.md](client-operations.md), [cli-client-outcomes.md](cli-client-outcomes.md) | Cancellation, deadlines and uncertain writes |
| [api-compatibility-0.8.0.md](api-compatibility-0.8.0.md), [api-compatibility-0.8.2.md](api-compatibility-0.8.2.md), [api-compatibility-0.8.3.md](api-compatibility-0.8.3.md), [api-compatibility-0.8.4.md](api-compatibility-0.8.4.md) | Public API compatibility of 0.8.0, 0.8.2, 0.8.3 and 0.8.4 |
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
vericairn docs check docs/releases/0.8.4 --documents docs/releases/documents.json
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.8.4
```

With the NuGet tool, replace `vericairn` with `dotnet vc`. A packaged release prints
`0.8.4+<commit>`; an ordinary source build retains `-dev`. Release-check disclosure
and notices go to stderr. The estate pin changes only through upgrade. Structural
checks and build comparison do not judge prose, qualify a platform or accept a
release. The frozen-set guard protects every indexed earlier set.

The release record and its evidence supply authoritative artifact hashes and
qualification. Source and Community packages are under Apache License 2.0;
copyright (c) 2026 Jason Furr.
