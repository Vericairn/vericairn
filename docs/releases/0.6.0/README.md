# Vericairn 0.6.0

Vericairn 0.6.0 adds the Community goal runtime, worker and coding-agent launchers, roster routing and asks, the progress report, and pairing with a runtime over the local network. It follows the published 0.5.2 release and continues to read record formats 1 and 2 and write an estate's own format, at most 2. An estate already at format 2 needs no new format migration; new 0.6.0 types and fields still require readers that understand them. The Remote client and profile are not implemented here.

- **Goals and evaluation.** Propose an objective with record predicates or evidence conditions, constraints, a sponsor and budget; activate it under the accountable person's authority; evaluate each condition independently; and accept the exact goal revision from its current evaluation. An agent never activates or funds a goal. Acceptance belongs to the sponsor when a person, an authorized delegate for an ordinary goal, or an operator when an agent sponsors it, with independence enforced.
- **The worker.** Run an assignment through `claude-code`, `codex` or `generic` in a git worktree, with an intent and allowance recorded before launch and observed usage after. Unknown usage remains charged at the cap. Spending limits are soft. Stops preserve request, transmission, acknowledgment, escalation and observed containment separately; silence produces attention and a stop attempt, and the settlement audit reports changes outside the assignment's permitted paths. The custody guard requires presence for locally held operator/sponsor keys or an accountable person's expiring exception. Live harness qualification and platform containment limits are stated in `limitations-and-platforms.md`.
- **Routing and asks.** `goal next` shows eligible work routed by the roster and this machine's available launchers, keys and budget. `goal step` takes one route: an agent's assignment and worker intent, a person's assignment and ask, or an ask when nobody is available. A person can take over an agent's step. Asks show the recommendation first, at most three per principal, and a decision of the principal asked answers them.
- **Progress and Workbench.** `progress`, the API, MCP and Workbench expose the report of work, published releases, planned releases, eligible steps and what waits on the operator. Record-derived sections name their evidence; repository findings also depend on local files. Workbench adds the Goals workspace, waiting asks on Home and a progress summary and full report. These views do not activate, accept or fund work.
- **Pairing and local-network connections.** An administrator issues a short-lived code; another machine makes its own key and pins the runtime identity. The opt-in listener uses mutual TLS, a connection-bound runtime proof and requests signed for that runtime. Revocation, key rotation and profile withdrawal are checked before authenticated writes commit. Certificate replacement preserves identity; identity replacement requires pairing again. A saved connection is used through the SDK; Workbench has no paired-runtime chooser.

The release workflow builds and tests the candidate on Ubuntu, macOS and Windows before packaging and drafting it. Independent review, the operator's acceptance of the exact candidate and manual publication follow in the estate's records. This set makes no claim that those future steps have already happened. The configured archives are unsigned developer builds; installation and checksum verification are in `install.md`.

## This documentation set

Prepared on October 16, 2026 UTC, before the release build, following `docs/releases/README.md`. The generated CLI, API and record references and `manifest.json` came from the 0.6.0 Release build at commit `e251b9642be39def864c8ec8ea052d3a80016b1b`, which corrects the source descriptions, using the same docs-tool generation command as the 0.5.2 set. They list 97 commands, 60 API operations and 35 record types. That commit is their provenance, not the eventual release commit.

The maintained API, format, software, Workbench and MCP drafts were checked against code and tests at this head, corrected in place, then copied byte for byte into this set as for 0.5.2. `api.md` is the frozen `docs/api-v1.md`; the identical `api-v1.md` is also included so links in the frozen drafts resolve without editing their bytes. The installation, operator and limitations guides were revised from 0.5.2 for the behavior now implemented. The references include the corrected CLI, API and record descriptions; the operator and limitations guides state the remaining implementation limits.

`release-notes.md` deliberately contains only its heading and the comment `<!-- The builder writes these notes from the estate's records. -->`. The builder supplies those notes. A successful structural docs check does not establish that release notes or release acceptance are complete. The release's own record will name its build commit, workflow and artifact hashes; this pre-build set cannot supply them. The set is frozen with the release, and later corrections belong in the next release's set.

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
| `release-notes.md` | Placeholder for the builder's notes from estate records |
| `limitations-and-platforms.md` | Automation versus platform qualification, signing, custody and worker/network implementation limits |
| `manifest.json` | Version and generation commit, required documents, commands, operations, types and record-format compatibility |

## Checking the version and set

```sh
vericairn version
vericairn docs check docs/releases/0.6.0
```

The packaged CLI prints `0.6.0+<commit>`, with the release's source commit; an ordinary source build retains the `-dev` suffix. `doctor` reports runtime and machine information, default custody, enclave availability, record formats and daemon status. Workbench's target bar shows the estate's pinned release, which changes only when the estate upgrades.

## Where to get it

After publication, the CLI and Workbench archives, bill of materials, notices, documentation archive and `SHA256SUMS` are on the [0.6.0 release page](https://github.com/Vericairn/vericairn/releases/tag/v0.6.0). The source is the `vericairn` repository at the commit the release record names, under Apache License 2.0; copyright (c) 2026 Jason Furr.
