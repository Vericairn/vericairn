# Vericairn 0.7.2

Vericairn keeps work, decisions, authority, evidence and releases as append-only, content-addressed,
signed records. The command line, Workbench, API/SDK and MCP server share one runtime. Agents produce
claims to inspect; milestone and release acceptance and publication remain the operator's actions.

0.7.2 follows published 0.7.1 with FIX-13, FIX-14, FIX-15, FIX-16 and FIX-18. The estate's release record
establishes the release commit, workflow, artifact hashes, qualified platforms and publication. Earlier
sets remain frozen, including the withdrawn 0.7.0 candidate whose work shipped in 0.7.1 with FIX-12.
NuGet availability follows the operator's publication of the accepted draft and a successful package
workflow. The tool remains framework-dependent and includes neither Workbench nor the macOS helper.

## What changes

- **FIX-13:** generated identifiers increase within one process; release document checks use the provider's
  exact commit and recheck the observed build commit; accepted notes are grouped by work order; settlement
  withdraws obsolete process attention while keeping settlement review asks open. CI checks workflow
  action pins and verifies NuGet packages on pull requests. Workbench reuses first-screen observations and
  unchanged record parsing; opening still validates every record once.
- **FIX-14, F-01:** restore binds every payload file to the inventory, rejects ambiguous or unsafe members,
  keeps one archive open and rechecks buffered bytes before writing. Restore is not an all-or-nothing
  filesystem transaction, and the inventory does not establish who produced an export.
- **FIX-15:** release dispatch claims its identity before the provider call; a missing receipt remains
  uncertain and needs observation and reconciliation. Git and gh share bounded execution and output
  capture, with explicit POSIX child-ownership requirements.
- **FIX-16:** single records are judged before publication. Unreadable recovery journals remain visible
  and hold further record writes until reconciled. The service gate and sequential grouped storage do
  not make the estate a transaction across independent runtimes.
- **FIX-18:** the Codex worker admits max effort, enables the ordinary tool host, observes execution from
  its own rollout, reports missing attempts, requires interpretable brief paths and pre-approves only
  the estate MCP server. Scope stops require confidently identified outside writes; uncertain effects
  and outside reads warn. The audit remains a limited classifier, not a complete program interpreter.

Five live Codex runs exercised the corrected worker path. The fifth recorded its cap, observed model
and max effort, usage, signed attempt and result, and exactly the permitted file change. A worker-launched
Codex run cannot yet commit: its git worktree metadata lies outside the sandbox. Changes stay uncommitted
in the worktree for review; FIX-19 makes the workspace a repository of its own. The
[limitations guide](limitations-and-platforms.md#live-codex-qualification-fix-18) names each run, what it
established and what remains unqualified. These observations do not accept the work or qualify other
versions, accounts, platforms, provider billing or hard spending limits.

Machine-local worker defaults and workspace placement still require explicit configuration. The
efficiency Adapter and Remote remain unbuilt. Context tokens and model fit remain unknown without a
qualified local tokenizer. [Installation](install.md) covers setup; [limitations and platforms](limitations-and-platforms.md)
retains the custody, accounting, containment, transport, recovery and native desktop boundaries.

## This documentation set

The CLI, API and record references and manifest were generated on October 17, 2026 UTC from the Release
build of version commit `74553762f61f48e023edeba580cde09e0e5b97c8`, based on main at `d3a91fd`.
They contain 111 commands, 72 API operations and 39 record types, with 17 required documents. This is
reference-generation provenance, not the later documentation or final release commit. The generated
files are unchanged from the tool's output.

The source reviewed includes FIX-13 at `9fc1147`, FIX-14 at `10459d4`, FIX-15 at `ca385cc`, FIX-16 at
`2be34c6` and FIX-18 at `d3a91fd`. Maintained drafts were reconciled in `6ad0ce6`. The API, both record
formats, software, Workbench, MCP and two communication documents are byte-identical copies of their
maintained originals. `api.md` and `api-v1.md` both copy `docs/api-v1.md` to preserve relative links.
The installation, operator, limitations, format-index and overview guides are revised from 0.7.1.

The release notes render the estate's records with `--from 0.7.1 --to 0.7.2 --since 2026-10-17T05:25:00Z`,
with the operator's approved Security section on F-01 first. This exclusive start widens 0.7.1's recorded
end, 05:25:37Z, by 37 seconds; overlap can repeat records. The manifest records the chosen window.
Accepted work has one entry per work order, naming its latest accepted current result and acceptance;
earlier attempts and results remain in `work observe`. Later unaccepted results remain separate and
superseded results are omitted. Classification uses current estate state and creates no acceptance.

| Document | What it covers |
|---|---|
| [install.md](install.md) | Bootstrap, archives, NuGet, upgrades and worker prerequisites |
| [operator-guide.md](operator-guide.md) | Work, releases, recovery, goals, workers, pairing, context and watching |
| [cli-reference.md](cli-reference.md) | Generated commands, arguments and options |
| [api-reference.md](api-reference.md) | Generated API v1 operations, bodies and responses |
| [record-reference.md](record-reference.md) | Generated record types and fields |
| [record-format.md](record-format.md) | Compatibility and the relationship between formats 1 and 2 |
| [record-format-v1.md](record-format-v1.md), [record-format-v2.md](record-format-v2.md) | Frozen format rules, restore, execution and observations |
| [api.md](api.md), [api-v1.md](api-v1.md) | Identical API drafts, transports, authentication, SDK and recovery |
| [software.md](software.md) | Repositories, releases, packages and worker implementation limits |
| [workbench.md](workbench.md) | Desktop surfaces, first-screen loading and qualification limits |
| [mcp.md](mcp.md) | Protocol, reads, five narrow writes and estate-only worker approval |
| [communication-adoption.md](communication-adoption.md) | Explicit adoption of the first-party operator's preference |
| [communication-qualification.md](communication-qualification.md) | Retained communication/context evidence and deferrals |
| [release-notes.md](release-notes.md) | Estate-derived changes with the approved F-01 Security section first |
| [limitations-and-platforms.md](limitations-and-platforms.md) | Remaining limits, five live Codex runs and artifact expectations |
| [manifest.json](manifest.json) | Generation provenance, document and API inventories, formats and notes window |

## Checking the version and set

```sh
vericairn version
vericairn docs check docs/releases/0.7.2
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.7.2
```

With the NuGet tool, replace `vericairn` with `dotnet vc`. A packaged release prints `0.7.2+<commit>`;
an ordinary source build retains `-dev`. `doctor` reports runtime, custody, format and daemon information.
The estate pin changes only through upgrade. Structural checks and build comparison do not judge prose,
qualify a platform or accept a release. The frozen-set guard protects every indexed earlier set.

After publication, artifacts and `SHA256SUMS` are on the [0.7.2 release page](https://github.com/Vericairn/vericairn/releases/tag/v0.7.2).
The estate release record supplies their authoritative hashes. Source and Community packages are under
Apache License 2.0; copyright (c) 2026 Jason Furr.
