# Vericairn 0.9.1

Vericairn keeps work, decisions, authority, evidence and releases as append-only,
content-addressed, signed records. The command line, Workbench, Remote, API/SDK and MCP
server share one runtime. Results are claims to inspect; acceptance and publication
remain the operator's actions.

0.9.1 follows published 0.9.0 as a security patch. Every distributable is built with .NET
SDK 10.0.403 and embeds the .NET and ASP.NET Core 10.0.14 runtimes in place of the 10.0.11
runtimes 0.9.0 embedded, and a worker run gives Vericairn's MCP server its temporary
directory explicitly, never the run's own; the release notes' Security section describes
both. It also lets a worker run prepare its workspace before the agent starts, refuses a
decision dated after its own record, names the release's version in the bill of materials
and reads historical repository content for policy evaluation in batches. It reads record
formats 1, 2 and 3 and writes the estate's own format, at most 3. The estate's release
record establishes the release commit, workflow, artifact hashes, qualified platforms and
publication; this set establishes none of those release facts. NuGet availability follows
publication of the accepted draft and a successful package workflow. The NuGet tool remains
framework-dependent and includes neither Workbench nor the macOS Secure Enclave helper.

## What changes

- **FIX-91:** every command line and Workbench distributable is built with .NET SDK
  10.0.403 and embeds the .NET runtime, ASP.NET Core runtime and apphost 10.0.14, which fix
  the published advisories that affect 10.0.11. The NuGet tool uses the runtime installed
  with the SDK.
- **FIX-86:** `vericairn worker run --prepare-only` and `POST /v1/runs/prepare` prepare and
  register a run's workspace alone, so dependencies can be installed before the agent
  starts; a workspace setup command configured on the worker's machine, for a launcher or a
  repository, runs before a run's intent and can refuse it; an unregistered or altered
  workspace is refused with its path and the reason. [software.md](software.md) and
  [cli.md](cli.md) describe them.
- **FIX-87:** a new decision or revision whose `decidedAt` is after its own record's `at` is
  refused with both timestamps; existing decisions stay readable, and `estate check`
  reports the mismatch as a warning.
- **FIX-90:** the bill of materials' metadata component names Vericairn and the packaged
  release's version, where earlier bills named 0.0.0, and verification refuses another
  name or version.
- **FIX-92:** policy evaluation reads each exact historical commit from a registered clone
  once, its blobs through one Git process, and keeps a duty result's repositories as
  evidence only, out of current writing.
- [API compatibility](api-compatibility-0.9.1.md) lists the public API changes.

The release notes record work verified but not yet accepted at this set's window. The
Security section covers FIX-91 and FIX-88, the MCP server's temporary directory in a
worker run.

The [limitations guide](limitations-and-platforms.md) retains the worker custody,
credential isolation, billing, context accounting, transport, recovery and native
desktop limits. Remote remains a developer build with no app-store distribution and no
recorded real-device qualification. These changes establish no additional live-harness
or native-platform qualification.

## Upgrading from 0.9.0

An estate at format 3 upgrades from 0.9.0, 0.8.4, 0.8.3, 0.8.2 or 0.8.0 without a format
step. Install the verified 0.9.1 command line, stop any daemon the older release started
and restart it with 0.9.1, then inspect and apply `vericairn estate upgrade --to 0.9.1` as
described in [installation](install.md). Vericairn's own estate upgrades against its
release record; any other estate verifies and records the published release itself.
Restart running workers after upgrading: a worker keeps the behavior of the release it
runs, including the MCP server's temporary directory. A harness qualification recorded on
Linux without a temporary directory in the worker's environment must be redone, as the
Security section says. An estate at 0.7.3 or earlier follows 0.8.0's format-3 upgrade and
adoption steps, which the installation guide retains.

## This documentation set

The references and manifest were generated on November 6, 2026 UTC from the Release
build of `fbe48293b36a07622790935f0f0228041b037c58`, whose implementation includes the
product changes listed above. They contain 191 commands, 141 API operations and 47
record types, with 53 required documents. That commit is reference generation
provenance, not the later documentation or final release commit. The generated files
retain the tool's bytes, including their retained inputs, reference inventory, reference
index and CLI client-outcome excerpt. This generation selected no register snapshot; the
index says so rather than asserting mappings.

The maintained guides are copied byte for byte into this set; `api.md` and `api-v1.md`
both copy the maintained API v1 guide so their relative links resolve. The installation,
operator, limitations and format-index guides describe 0.9.1 and the older behavior it
retains. Earlier version directories and their checksum index keep their original
contents.

**Correction to the 0.9.0 set.** Its limitations guide, like 0.8.4's, says the bill of
materials still lists no embedded .NET or ASP.NET Core runtime. From 0.8.4 the publish
evidence adds the runtime and apphost packs each distributable embeds, which lock files do
not hold, and the check requires every one of them. Its Remote guide places PROMPT-02's
clarifications to running attempts in 0.9.1; they are planned for 0.9.2.

The notes command uses `--from 0.9.0 --to 0.9.1`. Its exclusive start follows the end of
0.9.0's recorded notes window, and it writes the exact new window to the manifest.
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
| [api-compatibility-0.8.0.md](api-compatibility-0.8.0.md), [api-compatibility-0.8.2.md](api-compatibility-0.8.2.md), [api-compatibility-0.8.3.md](api-compatibility-0.8.3.md), [api-compatibility-0.8.4.md](api-compatibility-0.8.4.md), [api-compatibility-0.9.0.md](api-compatibility-0.9.0.md), [api-compatibility-0.9.1.md](api-compatibility-0.9.1.md) | Public API compatibility of 0.8.0, 0.8.2, 0.8.3, 0.8.4, 0.9.0 and 0.9.1 |
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
vericairn docs check docs/releases/0.9.1 --documents docs/releases/documents.json
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.9.1
```

With the NuGet tool, replace `vericairn` with `dotnet vc`. A packaged release prints
`0.9.1+<commit>`; an ordinary source build retains `-dev`. Release-check disclosure
and notices go to stderr. The estate pin changes only through upgrade. Structural
checks and build comparison do not judge prose, qualify a platform or accept a
release. The frozen-set guard protects every indexed earlier set.

The release record and its evidence supply authoritative artifact hashes and
qualification. Source and Community packages are under Apache License 2.0;
copyright (c) 2026 Jason Furr.
