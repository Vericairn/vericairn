# Vericairn 0.8.2

Vericairn keeps work, decisions, authority, evidence and releases as append-only,
content-addressed, signed records. The command line, Workbench, API/SDK and MCP
server share one runtime. Results are claims to inspect; acceptance and publication
remain the operator's actions.

0.8.2 follows published 0.8.0 and carries the work of 0.8.1, whose candidate was
withdrawn before publication, with FIX-53 and FIX-54. That work is the corrections from
0.8.0's security review, the corrections found while adopting estate policies, test timing and the release's
packaging path in CI, package registry observations, the package icon, MCP Registry
discovery and exact-byte document pins. It reads record formats 1, 2 and 3 and writes
the estate's own format, at most 3. The estate's release record establishes the
release commit, workflow, artifact hashes, qualified platforms and publication; this
set establishes none of those release facts. NuGet availability follows publication
of the accepted draft and a successful package workflow. The tool remains
framework-dependent and includes neither Workbench nor the macOS Secure Enclave helper.

## What changes

- **FIX-53:** the worker's close-on-exec call names `libSystem.Native`, so the macOS
  and Linux command lines, self-contained single-file builds, start git, gh, gitleaks
  and harnesses again; 0.8.1's candidate named `System.Native`, which only a
  framework-dependent build resolves. CI runs the published linux-x64 command line on
  a command that starts a process.
- **FIX-54:** reading a retained release basis recomputes its record digest from the
  records it resolves, signatures included, so a basis reads only when its records are
  exactly those the gate judged.
- **FIX-34:** the worker's command audit removes quoting as a POSIX shell does before
  the workspace test, inspects the inner commands of more shells and of `eval`, warns
  about programs and shells it cannot inspect, and treats a recursive copy of the
  home's `estates` directory, an estate's directory or a clone's directory as critical.
  The audit remains a signal, not an isolation boundary.
- **FIX-35:** settlement applies only the run's starting ignore rules. A changed or
  added ignore file counts as a change outside the brief, and a nested repository
  anywhere marks the audit incomplete. Audit behavior depends on the running worker's version.
- **FIX-36:** a refuse-level document whose repository has no registered clone
  refuses the release, unless the operator signs an expiring exception for that
  document and version.
- **FIX-37:** only the operator or an administer grant changes a product's
  checked-changes declaration or moves a repository's location; a checked merge needs
  a scanned push of its exact head; the secret scan uses Vericairn's own configuration,
  gitleaks' default rules without their built-in allowlists, and needs gitleaks 8.25.0 or later.
- **FIX-38:** the upgrade records its migration before it replaces the manifest,
  replaces the manifest atomically, and resumes an interrupted upgrade.
- **CI-03:** every change runs the release's packaging path in CI, apart from creating
  the draft. Only the draft step holds a write token, checkouts drop their credentials,
  workflow inputs reach scripts through the environment, and the self-contained
  publishes restore locked under a bill of materials that names every package they ship.
- **FIX-39:** a record stored with an interrupted companion publication reads as
  stored on every client, with its recovery problem beside it.
- **FIX-40:** the maintained policy guides describe implementations by their
  compilation identity, as FIX-31 made it. 0.8.0's frozen guides keep the earlier
  description; the [limitations guide](limitations-and-platforms.md) states the correction.
- **FIX-41:** the estate check ignores top-level entries the repository's own ignore
  rules exclude, such as Finder's `.DS_Store`.
- **FIX-42:** policies match a release to its product by any revision of the product record.
- **FIX-43:** policy results are looked up per assignment, so parallel assignments'
  results are never ambiguous, and review-wait and work-state consistency take a
  creation boundary, so settled history raises no finding.
- **FIX-44:** `operator record` commits a record's generated register members with it.
  `IRecordCommitAdapter`'s members change; [API compatibility](api-compatibility-0.8.2.md) lists them.
- **FIX-45:** `vericairn policies hash` and `POST /v1/estate-policies/hash` print the
  definition hash of a prepared policy or duty body without writing anything.
- **FIX-46:** reference coverage reads the reference inventory the selected build produced.
- **FIX-48:** the writing check skips generated documents, whose text belongs to their source records.
- **FIX-29:** a release submission's recheck no longer counts a passing second as a changed basis.
- **FIX-51:** a retained release basis names records, sources and objects by hash, each
  source file's bytes kept once as an object of its own, so it stays within the estate's
  8 MiB object limit; 0.8.0's copied basis outgrew it and refused every submission from an
  estate with more than a few megabytes of records and documents. Reading a basis resolves
  and verifies every reference; 0.8.0's bases still read.
- **FIX-49:** the route-level policy adoption journey has a test hang ceiling sized for a
  whole journey, which a slow Windows runner exceeded.
- **FIX-32:** the test suites no longer depend on real time; the worker stamps its own
  stop request on its own clock, and on Apple silicon Macs it marks its observation
  pipes closed on launch, so a process started at the same time cannot inherit them.
- **CI-01:** each platform's tests run in three parallel jobs.
- **DIST-02:** `vericairn packages observe`, `unlist` and `deprecate` record a package
  registry's publication, listing and deprecation as observations, with the account
  holder's intent recorded before any registry change.
- **DIST-03:** the packages carry the cairn mark as their icon.
- **DIST-04:** the NuGet tool carries the metadata for the official MCP Registry and
  nuget.org's MCP server listing. Listing itself is the operator's publication.
- **DOCS-07:** `vericairn docs pin` records a document's next revision from its exact
  bytes, superseding the current pin; a pin that moved since it was read refuses.
  `IEstateApi` gains `DocumentPin` and, with DIST-02, three package members;
  [API compatibility](api-compatibility-0.8.2.md) lists them.
- **BRAND-02:** the repository README and the package README open with what Vericairn
  is, what works today and how to install it.

The [limitations guide](limitations-and-platforms.md) retains the worker custody,
credential isolation, billing, context accounting, transport, recovery and native
desktop limits. The efficiency Adapter (EFF-01, placed in 0.8.3) and Remote remain
unbuilt. These changes establish no additional live-harness or native-platform qualification.

## Upgrading from 0.8.0

An estate at format 3 upgrades from 0.8.0 without a format step. Once the target has a
published release record and its verified binary is installed, inspect and apply
`vericairn estate upgrade --to 0.8.2` as described in [installation](install.md). From
0.8.2 the upgrade appends its migration record before it moves the manifest. Restart
running workers after upgrading: a worker keeps the audit of the release it runs. An
estate at 0.7.3 or earlier follows 0.8.0's format-3 upgrade and adoption steps, which
the installation guide retains. An estate other than Vericairn's own cannot yet pin or upgrade to a
published Vericairn release, because only the estate that built a release holds its
release record ([issue 177](https://github.com/Vericairn/vericairn/issues/177)); the
installation guide gives the workaround.

## This documentation set

The references and manifest were generated on October 22, 2026 UTC from the Release
build of `479634f023cd377aba29861312a52ba855795eda`, whose implementation includes
the product changes listed above. They contain 147 commands, 104 API operations and
42 record types, with 32 required documents. That commit is
reference generation provenance, not the later documentation or final release commit.
The generated files retain the tool's bytes, including their retained inputs, reference
inventory, reference index and CLI client-outcome excerpt. This generation selected no
register snapshot; the index says so rather than asserting mappings. The withdrawn 0.8.1
candidate's set stays frozen in its own directory; this set was prepared for 0.8.2.

The maintained guides are copied byte for byte into this set; `api.md` and
`api-v1.md` both copy the maintained API v1 guide so their relative links resolve.
The installation, operator, limitations and format-index guides describe 0.8.2 and
the older behavior it retains. Earlier version directories and their checksum index
keep their original contents.

The notes command uses `--from 0.8.0 --to 0.8.2`. Its exclusive start follows the
end of 0.8.0's recorded notes window, and it writes the exact new window to the
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
| [required-attention.md](required-attention.md), [operator-actions.md](operator-actions.md) | Current obligations, exact resolution, signed operator actions and package registry observations |
| [communication-adoption.md](communication-adoption.md), [communication-qualification.md](communication-qualification.md) | Explicit personal settings and retained communication/context evidence |
| [client-operations.md](client-operations.md), [cli-client-outcomes.md](cli-client-outcomes.md) | Cancellation, deadlines and uncertain writes |
| [api-compatibility-0.8.0.md](api-compatibility-0.8.0.md), [api-compatibility-0.8.2.md](api-compatibility-0.8.2.md) | Public API compatibility of 0.8.0 and 0.8.2 |
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
vericairn docs check docs/releases/0.8.2 --documents docs/releases/documents.json
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.8.2
```

With the NuGet tool, replace `vericairn` with `dotnet vc`. A packaged release prints
`0.8.2+<commit>`; an ordinary source build retains `-dev`. Release-check disclosure
and notices go to stderr. The estate pin changes only through upgrade. Structural
checks and build comparison do not judge prose, qualify a platform or accept a
release. The frozen-set guard protects every indexed earlier set.

The release record and its evidence supply authoritative artifact hashes and
qualification. Source and Community packages are under Apache License 2.0;
copyright (c) 2026 Jason Furr.
