# Vericairn 0.9.2

Vericairn keeps work, decisions, authority, evidence and releases as append-only,
content-addressed, signed records. The command line, Workbench, Remote, API/SDK and MCP
server share one runtime. Results are claims to inspect; acceptance and publication
remain the operator's actions.

0.9.2 is the release after 0.9.1. A goal can run a declared flow, routed one step per
command and stopping where a person holds the role, and the software pack declares the
release and upgrade flows. An estate signs its history with checkpoints, closings and
authority read as it stood at a record's time. Clients follow an event journal instead of
polling, an agent on another machine works an estate over MCP's Streamable HTTP transport,
and keyword search selects an agent's context. Security reports hold a release until they
are triaged, and an upgrade replaces an older runtime. Every distributable is built with
.NET SDK 10.0.404 and embeds the .NET and ASP.NET Core 10.0.15 runtimes. 0.9.2 also
hardens how admissions and the root of trust are read, which records give roles, policy
authority after a role is removed, per-user state on Linux, held-term matching and request
authentication; the release notes' Security section describes each. It reads record
formats 1, 2 and 3 and writes the estate's own format, at most 3. The estate's release
record establishes the release commit, workflow, artifact hashes, qualified platforms and
publication; this set establishes none of those release facts. NuGet availability follows
publication of the accepted draft and a successful package workflow. The NuGet tool remains
framework-dependent and includes neither Workbench nor the macOS Secure Enclave helper.

## What changes

- **Flows (PROC-05, PROC-06, PROC-07, PROC-10):** a template of kind `flow` declares roles
  and ordered steps; `goal show`, `goal next` and `goal step` derive each step's position,
  hand-offs, reviews, failure routes and stops from the records, and a reserved step shows
  its exact command only when it is next. The software pack's release and upgrade flows,
  `docs pass --before` and `estate verify` are described in [software.md](software.md),
  [templates.md](templates.md) and the [operator's guide](operator-guide.md).
- **Signed history (ID-05, ID-06, OPS-07, FIX-108, DOCS-15):** `history-checkpoint`
  records witness what an estate holds, so a removed, altered or backdated record no longer
  passes unnoticed; closings bound what a revoked device, a replaced key or a narrowed
  grant still accounts for; `estate checkpoint` writes and adopts checkpoints, and `estate
  check --expect` checks a copy against a head known elsewhere.
  [record-format.md](record-format.md) and [record-format-v3.md](record-format-v3.md) give
  the rules.
- **Events (WATCH-05, MCP-05):** the daemon keeps an event journal that `GET /v1/events`,
  `events watch`, MCP resource subscriptions and the `events_read` tool follow from a
  cursor, carrying no content and granting nothing.
- **Network MCP (MCP-03):** the daemon serves MCP at `/mcp` over Streamable HTTP, each call
  judged as its API operation for the paired device that signs it; `mcp serve --connection`
  bridges a harness's standard input and output to it.
- **Search (CONTEXT-02):** `search` and `--search` on context, briefs, runs and goal steps
  select pinned candidates from a machine-local keyword index, filtered by what the reader
  may read.
- **Security reports (OPS-11):** `security-reports poll` and `triage` read a product's
  declared advisories, and an untriaged report holds release submission.
- **Upgrades and tooling (OPS-05, DOCS-11, CI-06):** an upgrade's runtime step replaces an
  older daemon; the build tooling assembles, freezes and indexes a documentation set, as it
  did this one; continuous integration runs through a merge queue.
- **Large private artifacts (DOCS-29):** [large-artifacts.md](large-artifacts.md) keeps an
  artifact above the object limit with today's records.
- **Fixes:** FIX-85, FIX-89, FIX-93 to FIX-101, FIX-103 and FIX-123, among them an
  acceptance resting only on reviews that passed, finishing a stopped operator action, a
  diagnosable live store with a cache rebuild, one writer per working tree and output
  locations without a scheme.
- **FIX-115:** every command line and Workbench distributable is built with .NET SDK
  10.0.404 and embeds the .NET runtime, ASP.NET Core runtime and apphost 10.0.15, which fix
  the published advisories that affect 10.0.14. The NuGet tool uses the runtime installed
  with the SDK.
- [API compatibility](api-compatibility-0.9.2.md) lists the public API and record changes,
  among them the manifest's new `firstAuthority`.

The release notes record work verified but not yet accepted at this set's window. The
Security section covers FIX-102, FIX-104, FIX-105, FIX-106, FIX-110, FIX-111 and FIX-115.

The [limitations guide](limitations-and-platforms.md) retains the worker custody,
credential isolation, billing, context accounting, transport, recovery and native
desktop limits, and adds what signed history, security reports, network MCP and events leave
open.
Remote remains a developer build with no app-store distribution and no recorded
real-device qualification. These changes establish no additional live-harness or
native-platform qualification.

## Upgrading from 0.9.1

An estate at format 3 upgrades from 0.9.1, 0.9.0, 0.8.4, 0.8.3, 0.8.2 or 0.8.0 without a
format step. Install the verified 0.9.2 command line, then inspect and apply `vericairn
estate upgrade --to 0.9.2` as described in [installation](install.md); from 0.9.2 the
upgrade's runtime step stops a daemon of an older release and starts the pinned release's,
or says exactly what to run. The upgrade names the estate's first authority in the
manifest's `firstAuthority` when the manifest names none, and releases before 0.9.2 refuse
a manifest that carries it, so upgrade every clone that reads or writes the estate. An
estate whose manifest names none while it holds more than one self-admission of the root
operator's device is refused until its first authority is named by hand, as the Security
section says. Adopting history checkpoints is then the operator's step, once:
`vericairn estate checkpoint --adopt --check`, then the adoption with the plan hash it
prints. Restart running workers after upgrading. An estate at 0.7.3 or earlier follows
0.8.0's format-3 upgrade and adoption steps, which the installation guide retains.

## This documentation set

The references and manifest were generated on November 12, 2026 UTC from the Release
build of `3ed419acd5a157cea43a2d86a346f710d05cf986`, whose implementation includes the
product changes listed above. They contain 205 commands, 154 API operations and 48
record types, with 55 required documents. That commit is reference generation
provenance, not the later documentation or final release commit. The generated files
retain the tool's bytes, including their retained inputs, reference inventory, reference
index and CLI client-outcome excerpt. This generation selected no register snapshot; the
index says so rather than asserting mappings.

The maintained guides are copied byte for byte into this set; `api.md` and `api-v1.md`
both copy the maintained API v1 guide so their relative links resolve. The installation,
operator, limitations and format-index guides describe 0.9.2 and the older behavior it
retains. Earlier version directories and their checksum index keep their original
contents.

**Correction to the 0.9.1 set.** Its Remote guide places PROMPT-02's clarifications to
running attempts in 0.9.2; they are planned for 0.9.3.

The notes command uses `--from 0.9.1 --to 0.9.2`. Its exclusive start follows the end of
0.9.1's recorded notes window, and it writes the exact new window to the manifest.
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
| [software.md](software.md), [changes.md](changes.md) | Repositories, checked changes, release providers, the release and upgrade flows, CI, workers and retained companions |
| [workbench.md](workbench.md), [remote.md](remote.md), [mcp.md](mcp.md) | Desktop, phone and MCP surfaces, permissions and limits |
| [agent-builders.md](agent-builders.md), [frameworks/langgraph.md](frameworks/langgraph.md), [frameworks/crewai.md](frameworks/crewai.md), [frameworks/autogen.md](frameworks/autogen.md) | Building agents on Vericairn over MCP |
| [efficiency-adapter.md](efficiency-adapter.md) | The opt-in local efficiency adapter's contract and limits |
| [community-build-guides.md](community-build-guides.md) | Community build guides and the catalog repository's local preparation |
| [large-artifacts.md](large-artifacts.md) | Custody and cold recovery of large private artifacts with today's records |
| [templates.md](templates.md), [presentation-templates.md](presentation-templates.md) | Declarative document templates, flows, their sections, cohesion and the installed content forms |
| [prompts.md](prompts.md), [prompt-record-reference.md](prompt-record-reference.md) | The saved-prompt library, categories, favorites and the prompt record |
| [operator-requests.md](operator-requests.md) | Durable operator requests, their reasons and their recorded outcomes |
| [living-documents.md](living-documents.md) | Living documents, their covered paths and when they are due |
| [required-attention.md](required-attention.md), [operator-actions.md](operator-actions.md) | Current obligations, exact resolution, signed operator actions and package registry observations |
| [communication-adoption.md](communication-adoption.md), [communication-qualification.md](communication-qualification.md) | Explicit personal settings and retained communication/context evidence |
| [client-operations.md](client-operations.md), [cli-client-outcomes.md](cli-client-outcomes.md) | Cancellation, deadlines and uncertain writes |
| [api-compatibility-0.8.0.md](api-compatibility-0.8.0.md), [api-compatibility-0.8.2.md](api-compatibility-0.8.2.md), [api-compatibility-0.8.3.md](api-compatibility-0.8.3.md), [api-compatibility-0.8.4.md](api-compatibility-0.8.4.md), [api-compatibility-0.9.0.md](api-compatibility-0.9.0.md), [api-compatibility-0.9.1.md](api-compatibility-0.9.1.md), [api-compatibility-0.9.2.md](api-compatibility-0.9.2.md) | Public API compatibility of 0.8.0, 0.8.2, 0.8.3, 0.8.4, 0.9.0, 0.9.1 and 0.9.2 |
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
vericairn docs check docs/releases/0.9.2 --documents docs/releases/documents.json
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.9.2
```

With the NuGet tool, replace `vericairn` with `dotnet vc`. A packaged release prints
`0.9.2+<commit>`; an ordinary source build retains `-dev`. Release-check disclosure
and notices go to stderr. The estate pin changes only through upgrade. Structural
checks and build comparison do not judge prose, qualify a platform or accept a
release. The frozen-set guard protects every indexed earlier set.

The release record and its evidence supply authoritative artifact hashes and
qualification. Source and Community packages are under Apache License 2.0;
copyright (c) 2026 Jason Furr.
