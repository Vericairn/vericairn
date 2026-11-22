# Vericairn 0.9.4

Vericairn keeps work, decisions, authority, evidence and releases as append-only,
content-addressed, signed records. The command line, Workbench, Remote, API/SDK and MCP
server share one runtime. Results are claims to inspect; acceptance and publication remain
the operator's actions.

0.9.4 is the release after 0.9.3. It readies an estate for work that goes on between the
commands a person runs. A standing agent carries each occurrence its triggers make due,
within its caps, and every output it raises lands as a possibility, question or
publication request that another principal disposes of. A flow's step takes its timing and
a decision among named paths, and a run that will never complete is closed with a reason
for each step it left unsettled. Concurrent assignments are compared by their permitted
paths, the daemon keeps presence leases for agent sessions with a person's availability
and quiet hours, and an operator action may ask one presence check for a list of
signatures it announces first. The operator answers asks with their recommendations and
signs every ready item in one reviewed act, an observer that stops reporting becomes
attention, progress is kept at a target such as a GitHub discussion, and an upgrade has a
restore point and a stated way back. Every distributable is still built with .NET SDK
10.0.404 and embeds the .NET and ASP.NET Core 10.0.15 runtimes. 0.9.4 also runs git on a
repository it did not create without letting that repository's configuration start a
program, gives the secret scan of a checked push only the inputs it writes itself and
starts every program by its resolved absolute path; the release notes' Security section
describes them. It reads record formats 1, 2 and 3 and writes the estate's own format, at
most 3. The estate's release record establishes the release commit, workflow, artifact
hashes, qualified platforms and publication; this set establishes none of those release
facts. NuGet availability follows publication of the accepted draft and a successful
package workflow. The NuGet tool remains framework-dependent and includes neither
Workbench nor the macOS Secure Enclave helper.

## What changes

- **Standing agents (GOAL-07, GOAL-08, PROC-08, FIX-155):** a standing goal's triggers
  make its occurrences due, each carried once within the plan's caps by `goal step` and
  shown by `goal due`; a standing agent's possibilities, questions and publication
  requests wait for another principal, and `work new --from-possibility` adopts the work
  it proposes. The duty carrier, researcher and bug hunter ship as built-in flows at
  1.0.0, and no standing agent disposes of its own output by a backdated record.
- **Flows (PROC-18, PROC-19, FIX-142, FIX-143, FIX-144, FIX-146):** a step starts
  instantly, after a delay, at a time, on a schedule, in a window or on an event, with its
  due time derived from the records; a decision step takes the paths a rule, an agent or a
  person in a role decides (`goal decide`); `goal close`, or `goal accept --reason`,
  closes a run that will never complete with a reason for each unsettled step; a dismissed
  optional step settles as skipped, a close counts only once its step was asked, and a
  running run keeps its configuration. [templates.md](templates.md) states the contracts.
- **Concurrent work (CODE-73, POLICY-08):** `work overlap` compares the active assignments
  by their permitted paths before they start, and a `concurrent-paths` policy holds an
  unresolved overlap.
- **Presence (ID-11, GOAL-11, DOCS-25, ID-15, ID-16, CODE-80, DOCS-37):** the daemon keeps
  presence leases for agent sessions, with the displaced holder's fence, a person's
  availability and quiet hours and a hand-over when a session lapses; an operator action
  may ask one presence check for a list of signatures it announces first
  (`devices presence`, `devices sessions`), which opens only with a release's signed
  helper on a macOS version the release lists, so until a release lists one every
  signature keeps its own check. [events-and-presence.md](events-and-presence.md) and
  [presence-session-qualification.md](presence-session-qualification.md) describe them.
- **The operator's acts (OPS-16, OPS-24, OPS-22):** `operator answer` answers open asks
  with their recommendations in one signed act, `operator act` signs every ready item in
  one reviewed act, and an optional operator request is a possible todo.
- **Runs and their oversight (OPS-20, OPS-21, CODE-82, RUN-04, ID-07, ID-14):** a declared
  observer that stops reporting becomes attention ([observers.md](observers.md));
  `worker drill` times stopping a run before unattended work; a run's record shows what it
  could reach, with public retrieval for research runs; and the dispatcher's scope and
  role closings are recorded and checked.
- **Progress (WATCH-06, FIX-149):** `progress target` keeps the estate's progress at a
  target, a GitHub discussion first ([progress-targets.md](progress-targets.md)), and
  progress counts a release's placed work.
- **Upgrades (FIX-145):** `estate upgrade --check` says whether the release pinned before
  can still read the estate, a one-way upgrade waits for `--one-way`, and the applied
  upgrade records a restore point and prints the way back.
- **Reads (FIX-136, FIX-148, FIX-152):** status, the goal reads, attention and the work
  brief judge the estate once per command or request, with each signature verified once
  per process.
- **Checked changes and releases (CODE-81, FIX-137, FIX-140, FIX-150, FIX-154, FIX-156,
  FIX-159, CI-08, CI-09):** a checked push refuses the estate's blocked terms in its
  commits' messages and covers every commit its target does not hold, and its secret scan
  reads a history checkpoint's inventory as public data; a disclosure hold ends where the
  release procedure says it does; every file a release's SHA256SUMS names is attached to
  the release; a record whose identifier another already carries is never read in its
  place, and no record counts because the record that refused it is unread; publishing
  waits for the operator's approval, workflow actions are pinned by repository identity,
  and dependency updates wait a cooldown.
- **Fixes (FIX-134, FIX-135, FIX-138, FIX-139, FIX-141, FIX-157):** the first authority
  counts only when its self-admission's signature verifies; export and restore carry the
  owner's agent instruction files, which the estate check reads for hooks, automatic
  approvals and secret-like values; the links-resolve check skips code blocks; an
  acceptance of a work order's current revision accepts its current result; and a
  `document.drift` finding names the digest pinned and the current one.
- **Documentation and MCP (OPS-17, DOCS-38, MCP-07, DOCS-45, DOCS-46, with DOCS-35,
  DOCS-41 and DOCS-48):** the held guides take their changes as fragments; every set
  carries `llms.txt` and `llms-full.txt`; MCP tools carry behavior annotations, and
  `initialize` names the tool definitions' count and hash; the enrollment guide covers a
  signing capability unavailable to the invoking process; and the record reference spells
  out fixed-shape nested fields.
- [API compatibility](api-compatibility-0.9.4.md) lists the public API, protocol, record
  and finding changes, among them the nineteen new operations, the record fields a reader
  before 0.9.4 refuses and the MCP tools' annotations.

The release notes record work verified but not yet accepted at this set's window. The
Security section covers FIX-147, FIX-160 and FIX-161.

The [limitations guide](limitations-and-platforms.md) retains the worker custody,
credential isolation, billing, context accounting, transport, recovery and native desktop
limits. Standing agents and timed steps are carried one `goal step` at a time until the
carrier planned for 0.9.6. Remote remains a developer build with no app-store distribution
and no recorded real-device qualification. These changes establish no additional
live-harness or native-platform qualification.

## Upgrading from 0.9.3

An estate at format 3 upgrades from 0.9.3, 0.9.2, 0.9.1, 0.9.0, 0.8.4, 0.8.3, 0.8.2 or
0.8.0 without a format step. Install the verified 0.9.4 command line, then inspect and
apply `vericairn estate upgrade --to 0.9.4` as described in [installation](install.md).
`--check` says whether the release pinned before can still read the estate, and a one-way
upgrade is refused until it is confirmed with `--one-way`; the applied upgrade records a
restore point and ends with the way back to the previous release, and its runtime step
stops a daemon of an older release and starts the pinned release's, or says exactly what
to run. Releases before 0.9.4 refuse the record fields 0.9.4 adds, among them a decision's
`act`, an optional request and a run's closing, so an estate writes them only once every
clone that writes it has upgraded. Adopting history checkpoints, where an estate has not,
remains the operator's step, once. Restart running workers after upgrading. An estate at
0.7.3 or earlier follows 0.8.0's format-3 upgrade and adoption steps, which the
installation guide retains.

## This documentation set

The references and manifest were generated on November 22, 2026 UTC from the Release build
of `3c7c31c8547e52173e388fc583e24cfd62689ac9`, whose implementation includes the product
changes listed above. They contain 227 commands, 175 API operations and 48 record types,
with 64 required documents. That commit is reference generation provenance, not the later
documentation or final release commit. The generated files retain the tool's bytes,
including their retained inputs, reference inventory, reference index and CLI
client-outcome excerpt. This generation selected no register snapshot; the index says so
rather than asserting mappings.

The maintained guides are copied byte for byte into this set; `api.md` and `api-v1.md`
both copy the maintained API v1 guide so their relative links resolve. The installation,
operator, limitations and format-index guides describe 0.9.4 and the older behavior it
retains. The set adds five declared guides: the API compatibility notes for 0.9.4, events
and presence, automated observers, progress targets and presence-session qualification.
From 0.9.4 a set also carries `llms.txt`, an index of its documents, and `llms-full.txt`,
every document in one file. Earlier version directories and their checksum index keep
their original contents.

**Correction to the 0.9.3 set.** Its API compatibility notes say that after a write
`estate first-authority`'s `remaining` lists each other self-admission of the root
operator's device. A self-admission whose signature does not verify, a forged one, reads
`signature.invalid` instead, so it is not in `remaining`; `vericairn estate check` reports
it, and the operator removes it as any other. The maintained notes in this set say so.

The notes command uses `--from 0.9.3 --to 0.9.4`. Its exclusive start follows the end of
0.9.3's recorded notes window, and it writes the exact new window to the manifest.
Accepted work is grouped by work order with its latest accepted current result and
acceptance; later unaccepted results stay separate. Classification uses current estate
state and creates no acceptance. [Release notes](release-notes.md) are the estate-derived
part of the set; their Security section is inserted byte for byte as the operator approved
it.

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
| [events-and-presence.md](events-and-presence.md) | Events, presence leases, availability and quiet hours, and the displaced holder |
| [presence-session-qualification.md](presence-session-qualification.md) | Qualifying a release candidate's signed helper for presence sessions |
| [observers.md](observers.md) | Automated observers and the attention a silent one raises |
| [progress-targets.md](progress-targets.md) | The estate's progress kept at a target, such as a GitHub discussion |
| [agent-builders.md](agent-builders.md), [frameworks/langgraph.md](frameworks/langgraph.md), [frameworks/crewai.md](frameworks/crewai.md), [frameworks/autogen.md](frameworks/autogen.md) | Building agents on Vericairn over MCP |
| [efficiency-adapter.md](efficiency-adapter.md) | The opt-in local efficiency adapter's contract and limits |
| [community-build-guides.md](community-build-guides.md) | Community build guides and the catalog repository's local preparation |
| [large-artifacts.md](large-artifacts.md) | Custody and cold recovery of large private artifacts with today's records, and the three inventories of a restored estate |
| [templates.md](templates.md), [presentation-templates.md](presentation-templates.md) | Declarative document templates, flows, their timing and decisions, their sections, cohesion and the installed content forms |
| [prompts.md](prompts.md), [prompt-record-reference.md](prompt-record-reference.md) | The saved-prompt library, categories, favorites and the prompt record |
| [operator-requests.md](operator-requests.md) | Durable operator requests, their reasons, optional requests and their recorded outcomes |
| [living-documents.md](living-documents.md) | Living documents, their covered paths and when they are due |
| [required-attention.md](required-attention.md), [operator-actions.md](operator-actions.md) | Current obligations, exact resolution, signed operator actions and package registry observations |
| [communication-adoption.md](communication-adoption.md), [communication-qualification.md](communication-qualification.md) | Explicit personal settings and retained communication/context evidence |
| [client-operations.md](client-operations.md), [cli-client-outcomes.md](cli-client-outcomes.md) | Cancellation, deadlines and uncertain writes |
| [api-compatibility-0.8.0.md](api-compatibility-0.8.0.md), [api-compatibility-0.8.2.md](api-compatibility-0.8.2.md), [api-compatibility-0.8.3.md](api-compatibility-0.8.3.md), [api-compatibility-0.8.4.md](api-compatibility-0.8.4.md), [api-compatibility-0.9.0.md](api-compatibility-0.9.0.md), [api-compatibility-0.9.1.md](api-compatibility-0.9.1.md), [api-compatibility-0.9.2.md](api-compatibility-0.9.2.md), [api-compatibility-0.9.3.md](api-compatibility-0.9.3.md), [api-compatibility-0.9.4.md](api-compatibility-0.9.4.md) | Public API compatibility of 0.8.0, 0.8.2, 0.8.3, 0.8.4, 0.9.0, 0.9.1, 0.9.2, 0.9.3 and 0.9.4 |
| [policies.md](policies.md), [policy-evaluator.md](policy-evaluator.md) | Policy surfaces, recurring duties, evidence and the combined release gate |
| [policy-adoption.md](policy-adoption.md), [policy-adoption-proposals.md](policy-adoption-proposals.md) | Adoption procedure, exact proposed bodies and unresolved operator inputs |
| [readable-companions.md](readable-companions.md), [reference-inventory.md](reference-inventory.md) | Generator contracts, reading surfaces, inventories and coverage rules |
| [reference-index.md](reference-index.md), [reference-inventory.json](reference-inventory.json) | Generated reference membership and explicit register-selection status |
| [release-notes.md](release-notes.md) | The estate's records in the release window, opening with the Security section |
| [limitations-and-platforms.md](limitations-and-platforms.md) | Remaining limits, historical qualifications and expected artifacts |
| [llms.txt](llms.txt), [llms-full.txt](llms-full.txt) | An index of the set's documents, and every document in one file |
| [manifest.json](manifest.json) | Generation provenance, required documents, inventories and record formats |

## Checking the version and set

```sh
vericairn version
vericairn docs check docs/releases/0.9.4 --documents docs/releases/documents.json
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.9.4
```

With the NuGet tool, replace `vericairn` with `dotnet vc`. A packaged release prints
`0.9.4+<commit>`; an ordinary source build retains `-dev`. Release-check disclosure and
notices go to stderr. The estate pin changes only through upgrade. Structural checks and
build comparison do not judge prose, qualify a platform or accept a release. The
frozen-set guard protects every indexed earlier set.

The release record and its evidence supply authoritative artifact hashes and
qualification. Source and Community packages are under Apache License 2.0; copyright (c)
2026 Jason Furr.
