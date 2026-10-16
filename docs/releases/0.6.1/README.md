# Vericairn 0.6.1

Vericairn 0.6.1 adds the Community goal runtime, worker and coding-agent launchers, roster routing and asks, the progress report, and pairing with a runtime over the local network. It follows the published 0.5.2 release and continues to read record formats 1 and 2 and write an estate's own format, at most 2. An estate already at format 2 needs no new format migration; new 0.6.1 types and fields still require readers that understand them. The Remote client and profile are not implemented here.

The 0.6.0 candidate was withdrawn before publication after the Reviewer principal's independent review, under decision `01M51CDST3GS2CDFXD22Z3NKJP` and withdrawal record `01M51CE4J8Q2XR92Z8M2EPZF13`. Its documentation set remains frozen. 0.6.1 carries the same features with FIX-08 (pull request 77, merged as `8ec689b`): immutable settled run facts, auditing of the run branch's reachable committed changes, early loopback device checks and bounded signed bodies, pairing codes bound to their issuing device and authority, the corrected pairing-code description, and corrected accepted-work listings in notes and progress.

- **Goals and evaluation.** Propose an objective with record predicates or evidence conditions, constraints, a sponsor and budget; activate it under the accountable person's authority; evaluate each condition independently; and accept the exact goal revision from its current evaluation. An agent never activates or funds a goal. Acceptance belongs to the sponsor when a person, an authorized delegate for an ordinary goal, or an operator when an agent sponsors it, with independence enforced.
- **The worker.** Run an assignment through `claude-code`, `codex` or `generic` in a git worktree, with an intent and allowance recorded before launch and observed usage after. Unknown usage remains charged at the cap. Spending limits are soft. Stops preserve request, transmission, acknowledgment, escalation and observed containment separately; silence produces attention and a stop attempt, and the settlement audit reports changes outside the assignment's permitted paths. The custody guard requires presence for locally held operator/sponsor keys or an accountable person's expiring exception. Live harness qualification and platform containment limits are stated in `limitations-and-platforms.md`.
- **Routing and asks.** `goal next` shows eligible work routed by the roster and this machine's available launchers, keys and budget. `goal step` takes one route: an agent's assignment and worker intent, a person's assignment and ask, or an ask when nobody is available. A person can take over an agent's step. Asks show the recommendation first, at most three per principal, and a decision of the principal asked answers them.
- **Progress and Workbench.** `progress`, the API, MCP and Workbench expose the report of work, published releases, planned releases, eligible steps and what waits on the operator. Record-derived sections name their evidence; repository findings also depend on local files. Workbench adds the Goals workspace, waiting asks on Home and a progress summary and full report. These views do not activate, accept or fund work.
- **Pairing and local-network connections.** An administrator issues a short-lived code; another machine makes its own key and pins the runtime identity. The opt-in listener uses mutual TLS, a connection-bound runtime proof and requests signed for that runtime. Revocation, key rotation and profile withdrawal are checked before authenticated writes commit. Certificate replacement preserves identity; identity replacement requires pairing again. A saved connection is used through the SDK; Workbench has no paired-runtime chooser.

The release workflow builds and tests the candidate on Ubuntu, macOS and Windows before packaging and drafting it. Independent review, the operator's acceptance of the exact candidate and manual publication follow in the estate's records. This set makes no claim that those future steps have already happened. The configured archives are unsigned developer builds; installation and checksum verification are in `install.md`.

## This documentation set

Prepared on October 16, 2026 UTC, before the release build, following `docs/releases/README.md`. The generated CLI, API and record references and `manifest.json` came from the 0.6.1 Release build at version commit `f327d38f0785ce9087b6a5a286cf72e24ef21881`, using the same docs-tool generation command as the 0.6.0 set. They list 97 commands, 60 API operations and 35 record types. That commit is their provenance, not the eventual release commit.

The maintained API, software and format-2 drafts and repository README were checked against FIX-08's code and tests and made current. The API, both format documents, software, Workbench and MCP drafts were copied byte for byte into this set as for 0.6.0. `api.md` is the frozen `docs/api-v1.md`; the identical `api-v1.md` is also included so links in the frozen drafts resolve without editing their bytes. The installation, operator and limitations guides were revised from 0.6.0 for the behavior now implemented. The operator and limitations guides retain the unenforced goal limits, separate run stopping, pairing interruption and runtime-instance replay bounds.

`release-notes.md` renders the estate's records through the 0.6.1 command line, starting at 2026-10-15 12:06 UTC, where 0.5.2's notes ended, with its exact window in `manifest.json`. The operator-approved Security section is inserted unchanged after the introduction. The notes omit superseded results and classify current results as accepted work when an acceptance pins their exact hash or their work order's current revision and hash; that classification creates no acceptance of a result. A structural docs check does not establish release acceptance. The release's own record will name its build commit, workflow and artifact hashes; this pre-build set cannot supply them. The set is frozen with the release, and later corrections belong in the next release's set.

| Document | What it answers |
|---|---|
| `install.md` | Installing the CLI and Workbench, bootstrap and direct downloads, upgrade, worker prerequisites and network setup |
| `operator-guide.md` | Estate creation, devices, decisions, work, releases, recovery, daemon, desktop, MCP and documents; goals, evaluation, routing, asks, worker operation, pairing and revocation |
| `cli-reference.md` | Every command, argument and option, generated from the build |
| `api-reference.md` | Every API v1 operation, its parameters, bodies and responses, generated |
| `record-reference.md` | Every record type, subject rule and body field, generated |
| `record-format.md` | Which format's rules apply and how the two frozen format documents relate |
| `record-format-v1.md` | Format 1: envelope, canonical form, signatures, storage and base rules, frozen |
| `record-format-v2.md` | Format 2 additions, goals, evaluation, routing, worker ledger, living documents, device identity and pairing, frozen |
| `api.md`, `api-v1.md` | The same frozen API draft: runtime ownership, transports, authentication, pairing threat model, SDK and operations |
| `software.md` | Repository/check/release journeys, documents in external repositories, worker launchers, accounting, containment and scope audit, frozen |
| `workbench.md` | Desktop surfaces, keyboard routes, Goals and progress, probing and limitations, frozen |
| `mcp.md` | Protocol subset, read tools and narrow write tools, running and qualification, frozen |
| `release-notes.md` | Estate records rendered by 0.6.1 from the end of 0.5.2's notes, with the approved Security section |
| `limitations-and-platforms.md` | Automation versus platform qualification, signing, custody and worker/network implementation limits |
| `manifest.json` | Version and generation commit, required documents, commands, operations, types, record-format compatibility and the notes' record window |

## Checking the version and set

```sh
vericairn version
vericairn docs check docs/releases/0.6.1
```

The packaged CLI prints `0.6.1+<commit>`, with the release's source commit; an ordinary source build retains the `-dev` suffix. `doctor` reports runtime and machine information, default custody, enclave availability, record formats and daemon status. Workbench's target bar shows the estate's pinned release, which changes only when the estate upgrades.

## Where to get it

After publication, the CLI and Workbench archives, bill of materials, notices, documentation archive and `SHA256SUMS` are on the [0.6.1 release page](https://github.com/Vericairn/vericairn/releases/tag/v0.6.1). The source is the `vericairn` repository at the commit the release record names, under Apache License 2.0; copyright (c) 2026 Jason Furr.
