# Vericairn 0.9.3

Vericairn keeps work, decisions, authority, evidence and releases as append-only,
content-addressed, signed records. The command line, Workbench, Remote, API/SDK and MCP
server share one runtime. Results are claims to inspect; acceptance and publication
remain the operator's actions.

0.9.3 is the release after 0.9.2. It corrects what the estates running 0.9.2 reported and
readies the release flow for the next release. Every operator action announces and counts
its presence prompts, and release steps sign only what they store. A change merges through
a repository's required merge queue with its outcome recorded, an operator action's push
and merge trail reaches the estate, and a work order whose current result is accepted reads
as accepted, so a release can run on its declared flow. A command names an estate's first
authority, a guide tells an expired, refused or timed-out presence prompt apart during
enrollment, and an estate with many key rotations or closings is judged in near-linear
time. Every distributable is still built with .NET SDK 10.0.404 and embeds the .NET and
ASP.NET Core 10.0.15 runtimes. 0.9.3 also reads a key only in the form Vericairn makes;
the release notes' Security section describes it. It reads record formats 1, 2 and 3 and
writes the estate's own format, at most 3. The estate's release record establishes the
release commit, workflow, artifact hashes, qualified platforms and publication; this set
establishes none of those release facts. NuGet availability follows publication of the
accepted draft and a successful package workflow. The NuGet tool remains
framework-dependent and includes neither Workbench nor the macOS Secure Enclave helper.

## What changes

- **Presence prompts (FIX-124, FIX-130):** an operator action, a release step or a single
  write that signs with a device needing its owner's presence announces every record it
  will sign before the first prompt, names each prompt `Signature k of n` on standard
  error, in Workbench and in the system dialog, and ends with how many it signed;
  `POST /v1/presence/prompts` serves the notices on the runtime's machine. Release steps
  make no throwaway trial signature: in an estate that has adopted history checkpoints,
  `release submit` asks for 3 prompts where it asked for 5, and a release dispatches only
  from the device that signed its intent. The
  [operator's guide](operator-guide.md) lists each step's count.
- **The release flow's readiness (CODE-71, FIX-126, FIX-128, FIX-125, FIX-127):** `changes
  merge` enqueues into a required merge queue and `changes observe` records the queue's
  outcome; the next operator action carries the earlier action's push and merge trail into
  its commit; ordinary work closed by `operator close` reads as accepted; `docs pass
  --before` reports an earlier release only where it is named as current; and `goal
  propose --subject` binds an extension flow's subject. [changes.md](changes.md) and
  [operator-actions.md](operator-actions.md) describe them.
- **The first authority and enrollment (CODE-72, DOCS-33):** `vericairn estate
  first-authority` lists the self-admissions that could be the estate's first authority
  with their evidence and names one of the root operator's devices in the manifest;
  [enrollment-troubleshooting.md](enrollment-troubleshooting.md) reconciles an enrollment
  whose presence prompt expired, was refused or timed out.
- **Goals (FIX-116, FIX-117):** a goal is recorded impossible only after its evaluator's
  verdict, and under `sponsor-approves` work proposed for a goal waits for its sponsor's
  approval, shown in status, `goal show`, attention, Workbench and Remote.
- **Fixes from the estates' reports (FIX-107, FIX-109, FIX-112, FIX-113, FIX-114, FIX-119,
  FIX-120, FIX-122, CODE-70):** export and restore keep each file's executable mode; a
  timestamp in the wrong form is refused with the form required; `doctor --json` returns
  one documented result and every command that takes `--json` writes JSON; document
  reviews, pins and checks say what they declare, need and found; an operation keeps its
  intent and target in every revision; the work-order key check runs in linear time; and
  an estate's root can hold its owner's `AGENTS.md`, `CLAUDE.md` and `GEMINI.md`.
- **Judging costs (FIX-121, FIX-131):** key closings and admissions under a device's
  closings are judged in near-linear time, with the same answers.
- **Cold recovery (DOCS-32):** [large-artifacts.md](large-artifacts.md) separates the
  three inventories of a restored estate and classifies every file before anything writes
  to the copy.
- **Continuous integration and tests (CI-07, FIX-118, FIX-133):** main's push run skips a
  commit the merge queue already tested, each Runtime test class has its own Vericairn
  home, and the release-submission presence test holds its own clock.
- [API compatibility](api-compatibility-0.9.3.md) lists the public API, protocol, record
  and finding changes, among them the two new operations, `MergeQueueObservation`,
  `doctor`'s JSON result and the new refusals.

The release notes record work verified but not yet accepted at this set's window. The
Security section covers FIX-132. Below record format 3, a signed release intent whose
device is gone is now refused before dispatch instead of being dispatched with an unsigned
receipt (FIX-130).

The [limitations guide](limitations-and-platforms.md) retains the worker custody,
credential isolation, billing, context accounting, transport, recovery and native
desktop limits. Remote remains a developer build with no app-store distribution and no
recorded real-device qualification. These changes establish no additional live-harness or
native-platform qualification.

## Upgrading from 0.9.2

An estate at format 3 upgrades from 0.9.2, 0.9.1, 0.9.0, 0.8.4, 0.8.3, 0.8.2 or 0.8.0
without a format step. Install the verified 0.9.3 command line, then inspect and apply
`vericairn estate upgrade --to 0.9.3` as described in [installation](install.md); the
upgrade's runtime step stops a daemon of an older release and starts the pinned release's,
or says exactly what to run. Releases before 0.9.3 report an owner's `AGENTS.md`,
`CLAUDE.md` or `GEMINI.md` at the estate's root as `layout.unexpected`, so upgrade every
clone before adding them. An estate whose manifest names no first authority while it holds
more than one self-admission of the root operator's device names its first device's with
`vericairn estate first-authority`, then removes every other such self-admission from the
copy. Adopting history checkpoints, where an estate has not, remains the operator's step,
once. Restart running workers after upgrading. An estate at 0.7.3 or earlier follows
0.8.0's format-3 upgrade and adoption steps, which the installation guide retains.

## This documentation set

The references and manifest were generated on November 13, 2026 UTC from the Release
build of `05cbc6aca523e6ac47a16196afaa43cb995a7e5d`, whose implementation includes the
product changes listed above. They contain 206 commands, 156 API operations and 48
record types, with 57 required documents. That commit is reference generation
provenance, not the later documentation or final release commit. The generated files
retain the tool's bytes, including their retained inputs, reference inventory, reference
index and CLI client-outcome excerpt. This generation selected no register snapshot; the
index says so rather than asserting mappings.

The maintained guides are copied byte for byte into this set; `api.md` and `api-v1.md`
both copy the maintained API v1 guide so their relative links resolve. The installation,
operator, limitations and format-index guides describe 0.9.3 and the older behavior it
retains. The set adds two declared guides: the API compatibility notes for 0.9.3 and
enrollment troubleshooting. Earlier version directories and their checksum index keep
their original contents.

**Correction to the 0.9.2 set.** Its README, release notes, install guide and operator's
guide state FIX-102's remedy for an estate with several self-admissions of the root
operator's device without the removal of the extra one: naming the first device's
self-admission makes the devices' admissions count again, but each other self-admission
still reads `signature.self-admission`, an error, and the upgrade refuses until it is
removed (decision `01M78HY4YZWESCWM83GSXJCMDN`). The maintained guides in this set say so.

The notes command uses `--from 0.9.2 --to 0.9.3`. Its exclusive start follows the end of
0.9.2's recorded notes window, and it writes the exact new window to the manifest.
Accepted work is grouped by work order with its latest accepted current result and
acceptance; later unaccepted results stay separate. Classification uses current estate
state and creates no acceptance. [Release notes](release-notes.md) are the estate-derived
part of the set.

| Document | What it covers |
|---|---|
| [install.md](install.md), [operator-guide.md](operator-guide.md) | Installation, upgrade, work, releases, workers and operator journeys |
| [enrollment-troubleshooting.md](enrollment-troubleshooting.md) | An enrollment step whose presence prompt expired, was refused or timed out, told apart and reconciled |
| [people-only.md](people-only.md), [people-and-agents.md](people-and-agents.md), [agents-only.md](agents-only.md) | Running Vericairn in each operating mode |
| [cli-reference.md](cli-reference.md), [api-reference.md](api-reference.md), [record-reference.md](record-reference.md) | Generated commands, API operations and record types |
| [cli.md](cli.md) | The commands added since 0.8.3 |
| [record-format.md](record-format.md) | Compatibility and the relationship between formats 1–3 |
| [record-format-v1.md](record-format-v1.md), [record-format-v2.md](record-format-v2.md), [record-format-v3.md](record-format-v3.md) | Base format and the changes introduced by formats 2 and 3 |
| [api.md](api.md), [api-v1.md](api-v1.md) | Identical API guides, transports, authentication, SDK and recovery |
| [software.md](software.md), [changes.md](changes.md) | Repositories, checked changes and merge queues, release providers, the release and upgrade flows, CI, workers and retained companions |
| [workbench.md](workbench.md), [remote.md](remote.md), [mcp.md](mcp.md) | Desktop, phone and MCP surfaces, permissions and limits |
| [agent-builders.md](agent-builders.md), [frameworks/langgraph.md](frameworks/langgraph.md), [frameworks/crewai.md](frameworks/crewai.md), [frameworks/autogen.md](frameworks/autogen.md) | Building agents on Vericairn over MCP |
| [efficiency-adapter.md](efficiency-adapter.md) | The opt-in local efficiency adapter's contract and limits |
| [community-build-guides.md](community-build-guides.md) | Community build guides and the catalog repository's local preparation |
| [large-artifacts.md](large-artifacts.md) | Custody and cold recovery of large private artifacts with today's records, and the three inventories of a restored estate |
| [templates.md](templates.md), [presentation-templates.md](presentation-templates.md) | Declarative document templates, flows, their sections, cohesion and the installed content forms |
| [prompts.md](prompts.md), [prompt-record-reference.md](prompt-record-reference.md) | The saved-prompt library, categories, favorites and the prompt record |
| [operator-requests.md](operator-requests.md) | Durable operator requests, their reasons and their recorded outcomes |
| [living-documents.md](living-documents.md) | Living documents, their covered paths and when they are due |
| [required-attention.md](required-attention.md), [operator-actions.md](operator-actions.md) | Current obligations, exact resolution, signed operator actions and package registry observations |
| [communication-adoption.md](communication-adoption.md), [communication-qualification.md](communication-qualification.md) | Explicit personal settings and retained communication/context evidence |
| [client-operations.md](client-operations.md), [cli-client-outcomes.md](cli-client-outcomes.md) | Cancellation, deadlines and uncertain writes |
| [api-compatibility-0.8.0.md](api-compatibility-0.8.0.md), [api-compatibility-0.8.2.md](api-compatibility-0.8.2.md), [api-compatibility-0.8.3.md](api-compatibility-0.8.3.md), [api-compatibility-0.8.4.md](api-compatibility-0.8.4.md), [api-compatibility-0.9.0.md](api-compatibility-0.9.0.md), [api-compatibility-0.9.1.md](api-compatibility-0.9.1.md), [api-compatibility-0.9.2.md](api-compatibility-0.9.2.md), [api-compatibility-0.9.3.md](api-compatibility-0.9.3.md) | Public API compatibility of 0.8.0, 0.8.2, 0.8.3, 0.8.4, 0.9.0, 0.9.1, 0.9.2 and 0.9.3 |
| [policies.md](policies.md), [policy-evaluator.md](policy-evaluator.md) | Policy surfaces, recurring duties, evidence and the combined release gate |
| [policy-adoption.md](policy-adoption.md), [policy-adoption-proposals.md](policy-adoption-proposals.md) | Adoption procedure, exact proposed bodies and unresolved operator inputs |
| [readable-companions.md](readable-companions.md), [reference-inventory.md](reference-inventory.md) | Generator contracts, reading surfaces, inventories and coverage rules |
| [reference-index.md](reference-index.md), [reference-inventory.json](reference-inventory.json) | Generated reference membership and explicit register-selection status |
| [release-notes.md](release-notes.md) | The estate's records in the release window, opening with the Security section |
| [limitations-and-platforms.md](limitations-and-platforms.md) | Remaining limits, historical qualifications and expected artifacts |
| [manifest.json](manifest.json) | Generation provenance, required documents, inventories and record formats |

## Checking the version and set

```sh
vericairn version
vericairn docs check docs/releases/0.9.3 --documents docs/releases/documents.json
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.9.3
```

With the NuGet tool, replace `vericairn` with `dotnet vc`. A packaged release prints
`0.9.3+<commit>`; an ordinary source build retains `-dev`. Release-check disclosure
and notices go to stderr. The estate pin changes only through upgrade. Structural
checks and build comparison do not judge prose, qualify a platform or accept a
release. The frozen-set guard protects every indexed earlier set.

The release record and its evidence supply authoritative artifact hashes and
qualification. Source and Community packages are under Apache License 2.0;
copyright (c) 2026 Jason Furr.
