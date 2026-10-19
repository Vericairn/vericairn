# Vericairn 0.8.0

Vericairn keeps work, decisions, authority, evidence and releases as append-only,
content-addressed, signed records. The command line, Workbench, API/SDK and MCP
server share one runtime. Results are claims to inspect; acceptance and publication
remain the operator's actions.

0.8.0 follows published 0.7.3 with estate policies and recurring duties, readable
companions, cancellable client operations, checked repository changes and signed
operator actions. The estate's release record establishes the release commit,
workflow, artifact hashes, qualified platforms and publication. This set establishes
none of those release facts. NuGet availability follows publication of the accepted
draft and a successful package workflow. The tool remains framework-dependent and
includes neither Workbench nor the macOS Secure Enclave helper.

## What changes

- **POLICY-01:** record format 3 adds estate-policy declarations and writing
  exceptions. The evaluator binds its findings to recorded revisions, retained
  evidence, local source bytes and installed rule implementations. Unsupported or
  inaccessible evidence stays unavailable. Reads make no changes and start no work.
- **POLICY-02:** recurring duties retain each occurrence, deadline, grace period,
  missed history and exact completion evidence. Completion requires the declared
  proofs and review. Late completion retains its lateness; an exception preserves
  unmet debt. Evaluation does not schedule work or authorize spending.
- **POLICY-03:** release submission combines built-in conditions, living-document
  gates and applicable policy holds. It rechecks the exact commit, product revision,
  authority, evidence and fresh time at preparation, intent and dispatch. Retained
  basis objects explain the findings. A policy exception cannot waive other gates,
  unavailable evidence or publication authority.
- **POLICY-04:** the [operating guide](policy-adoption.md) and
  [first-party proposals](policy-adoption-proposals.md) describe explicit adoption.
  They do not declare policies. Exact approved definitions, the writing grant,
  release identities, obligation-gate interpretations and review capacity remain
  operator inputs after upgrading.
- **DOCS-05:** the `release-entry` and `register-coverage` rule kinds evaluate exact
  release entries and generated-reference coverage against retained evidence.
  Their findings establish only the declared rule's result, not prose quality,
  policy adoption or release acceptance.
- **CODE-02:** CLI command classes own their arguments and registration, sharing
  the command context. The generated command reference describes the shipped tree.
- **CODE-03:** SDK async operations carry caller cancellation and per-operation
  deadlines through embedded and daemon transports. CLI and Workbench distinguish
  cancellation, timeout, unavailability and definite refusal. An interrupted write
  can have an unknown outcome; cancellation does not undo it and no client retries it.
- **CODE-04:** public API ownership uses immutable shared vocabularies and copied
  option collections. Some exported field types and test-provider availability
  change source or binary compatibility; [API compatibility](api-compatibility-0.8.0.md)
  lists them. Collection equality is not element equality, and API v1 wire and record
  bytes keep their contracts.
- **CODE-05:** the runtime separates run admission, run dispatch, release coordination
  and record access behind the existing `EstateService` facade.
- **CODE-06:** the Git adapter owns worker workspace preparation, auditing and
  retention. Release discovery uses `IReleaseSource`, with GitHub implementation in
  its provider package. These ownership changes do not add providers or permissions.
- **READ-01:** registered generators render exact selected sources under a versioned
  contract. Format 3's `pathPattern` supports bounded generated collections. Strict
  whole generated members compare complete bytes without output pins or authored
  clearances. Legacy conversion requires an exact independently reviewed preview.
- **READ-02:** first-party readers cover record items and collections, six revision
  registers, the estate manifest and allowlisted local metadata. Authored regions
  retain their existing pins and review requirements. Missing or incomplete inputs
  stay unavailable; unexpected members are not silently removed.
- **READ-03:** software readers and retained reference inputs bind generated
  companions to source and implementation identities. Reference comparison checks
  the exact local build, inventory, inputs and bytes. An explicitly selected
  register supplies mappings; absence of that selection makes no coverage claim.
- **READ-04:** CLI, API, synchronous and async SDK, MCP and Workbench expose companion
  list, source, render and compare reads for the authorized Internal projection.
  Reads do not publish records. Members/Public projections and remote invocation of
  local inspectors are outside these surfaces; loss of access clears protected views.
- **FIX-23:** release document checks select each document's own repository commit.
  The release repository uses the exact target. Authored documents in another
  registered repository use its HEAD, or the clearing review's commit when HEAD is
  its ancestor. Unregistered authored clones are explicitly not compared; strict
  generated checks and refuse-level policies still hold on unavailable required inputs.
- **FIX-24:** command observations recognize additional recursive credential copies,
  credential-helper actions and outside writes. Reports can arrive after execution;
  the audit remains a signal rather than a credential-isolation boundary.
- **FIX-25:** pre-dispatch refusal cleans up only a newly prepared, unused workspace
  whose complete inventory still matches. Changed, reused or uncertain workspaces
  are preserved. Windows staging opens support extended paths.
- **FIX-26:** settlement excludes ignored untracked output and worker brief exports
  while auditing committed history separately. Native harness tool identities must
  match the admitted inventory. A complete committed audit can retain a branch when
  the file inventory is incomplete; a persistently unavailable credential check
  requests containment. Audit behavior depends on the running worker's version.
- **FIX-27:** required context originals can resolve their pinned bytes from the
  exact path in local repository history. Optional sources remain working-tree-only.
  Missing history refuses; retrieval fetches nothing and writes nothing.
- **FIX-28:** run-state and stop-request publication uses complete replacement files,
  Windows readers coordinate with replacement, and supervision survives unavailable
  observations. SDK deadlines also check elapsed monotonic time so a late response
  cannot evade an expired deadline. Unreadable state never proves exit.
- **FIX-30:** the release workflow's package job checks out the complete history, so its
  documentation comparison can read the commit a set's references were generated from.
  0.8.0's first release run stopped there, before packaging.
- **FIX-31:** an installed policy implementation is named by its assembly and its
  deterministic build's module version identifier, not by its file, which a single-file
  application lacks, so the self-contained command line and Workbench evaluate policies.
  CI now publishes both single-file applications on every change. 0.8.0's second release
  run stopped there, before packaging.
- **OPS-01:** `operator record` signs and commits one approved record through declared
  checked changes. It needs repository-synced format 3 and the operator's admitted
  device. Receipts remain separate records; resume preserves the original operation
  identity, and unknown effects require observation before another attempt.
- **MERGE-01:** the software pack implements checked push, pull-request observation
  and merge with exact heads, declared checks and local secret scanning. A confirmed
  merge with pending target checks remains incomplete. Forks and required merge
  queues are unsupported, and a check result supplies no permission to publish.

The [limitations guide](limitations-and-platforms.md) retains the worker custody,
credential isolation, billing, context accounting, transport, recovery and native
desktop limits. The efficiency Adapter and Remote remain unbuilt. Earlier live
harness observations qualify only their recorded configurations; these changes
establish no additional live-harness or native-platform qualification.

## Upgrading from 0.7.3

0.8.0 reads record formats 1, 2 and 3 and writes format 3. Once the target has a
published release record and its verified binary is installed, inspect and apply
`vericairn estate upgrade --to 0.8.0` as described in [installation](install.md).
The format-2-to-3 migration preserves original record bytes, identifiers, hashes and
signatures and records the forward move. Failed migration steps roll back their own
changes; the upgrade is idempotent and has no downgrade path. Coordinate all writers:
a format-2-only binary refuses the upgraded estate.

The migration declares no policies or duties, grants no writing exception and
converts no legacy generated document. The operator still approves the exact
adoption bodies, grants bounded writing authority, supplies the required release
identities and declares the policies. The proposals describe twelve policies and
seven initial duties, plus seven further menu duties that remain undeclared.
Approving a key or title alone cannot satisfy an exact-definition requirement.

## This documentation set

The references and manifest were generated on October 19, 2026 UTC from the Release
build of `0d9cc5404308f4e73909074069629b05be5510f1`, whose implementation includes
the twenty-four work orders through `0d9cc54`. They contain 141 commands, 99 API
operations and 42 record types, with 31 required documents. That commit is reference
generation provenance, not the later documentation or final release commit.
The generated files retain the tool's bytes, including their retained inputs,
reference inventory, reference index and CLI client-outcome excerpt. This generation
selected no register snapshot; the index says so rather than asserting mappings.

The maintained guides are copied byte for byte into this set; `api.md` and
`api-v1.md` both copy the maintained API v1 guide so their relative links resolve.
The installation, operator, limitations and format-index guides describe 0.8.0 and
the older behavior it retains. Earlier version directories and their checksum index
keep their original contents.

The notes command uses `--from 0.7.3 --to 0.8.0`. Its exclusive start follows the
end of 0.7.3's recorded notes window, and it writes the exact new window to the
manifest. Accepted work is grouped by work order with its latest accepted current
result and acceptance; later unaccepted results stay separate. Classification uses
current estate state and creates no acceptance. [Release notes](release-notes.md)
are the estate-derived part of the set.

| Document | What it covers |
|---|---|
| [install.md](install.md), [operator-guide.md](operator-guide.md) | Installation, format upgrade, work, releases, workers and operator journeys |
| [cli-reference.md](cli-reference.md), [api-reference.md](api-reference.md), [record-reference.md](record-reference.md) | Generated commands, API operations and record types |
| [record-format.md](record-format.md) | Compatibility and the relationship between formats 1–3 |
| [record-format-v1.md](record-format-v1.md), [record-format-v2.md](record-format-v2.md), [record-format-v3.md](record-format-v3.md) | Base format and the changes introduced by formats 2 and 3 |
| [api.md](api.md), [api-v1.md](api-v1.md) | Identical API guides, transports, authentication, SDK and recovery |
| [software.md](software.md), [changes.md](changes.md) | Repositories, checked changes, release providers and retained companions |
| [workbench.md](workbench.md), [mcp.md](mcp.md) | Desktop and MCP surfaces, permissions and limits |
| [required-attention.md](required-attention.md), [operator-actions.md](operator-actions.md) | Current obligations, exact resolution and signed operator actions |
| [communication-adoption.md](communication-adoption.md), [communication-qualification.md](communication-qualification.md) | Explicit personal settings and retained communication/context evidence |
| [client-operations.md](client-operations.md), [cli-client-outcomes.md](cli-client-outcomes.md), [api-compatibility-0.8.0.md](api-compatibility-0.8.0.md) | Cancellation, deadlines, uncertain writes and public API compatibility |
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
vericairn docs check docs/releases/0.8.0 --documents docs/releases/documents.json
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.8.0
```

With the NuGet tool, replace `vericairn` with `dotnet vc`. A packaged release prints
`0.8.0+<commit>`; an ordinary source build retains `-dev`. Release-check disclosure
and notices go to stderr. The estate pin changes only through upgrade. Structural
checks and build comparison do not judge prose, qualify a platform or accept a
release. The frozen-set guard protects every indexed earlier set.

The release record and its evidence supply authoritative artifact hashes and
qualification. Source and Community packages are under Apache License 2.0;
copyright (c) 2026 Jason Furr.
