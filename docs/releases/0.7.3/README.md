# Vericairn 0.7.3

Vericairn keeps work, decisions, authority, evidence and releases as append-only, content-addressed,
signed records. The command line, Workbench, API/SDK and MCP server share one runtime. Agents produce
claims to inspect; milestone and release acceptance and publication remain the operator's actions.

0.7.3 follows published 0.7.2 with FIX-19, UPDATE-01, FIX-20, FIX-21 and FIX-22. The estate's release
record establishes the release commit, workflow, artifact hashes, qualified platforms and publication.
Earlier sets remain frozen. NuGet availability follows the operator's publication of the accepted
draft and a successful package workflow. The tool remains framework-dependent and includes neither
Workbench nor the macOS Secure Enclave helper.

## What changes

- **FIX-19:** worker workspaces have independent Git administration and borrow the registered clone's
  objects read-only. Worker Git disables hooks and ambient configuration. Codex adds exactly its own
  workspace's `.git` to its writable roots. Settlement audits the run branch and fetches its audited
  head into the registered clone with a guarded reference update. Uncommitted changes stay in the
  workspace; incomplete audits or concurrent branch changes refuse transfer and leave a local warning.
- **UPDATE-01:** CLI `version`, `doctor`, `status` and Workbench disclose and share at most one check
  per rolling 24 hours for a newer stable published release, counting failed attempts. The request
  reaches only GitHub, ignores system proxies and sends no identifiers or usage telemetry. Disable it
  with `vericairn updates configure --enabled false`; cached information can still appear. `estate check`
  warns `manifest.release-behind` from matching release records and cached knowledge without a request.
  Nothing installs or upgrades automatically.
- **FIX-20:** recognized Vericairn credential reads and token-output commands request a stop, including
  `gh auth token`, `gh auth status --show-token`, `git credential fill` and Git credential-helper `get`.
  Additional write forms, wrappers and literal interpreter targets are recognized. Null-device output
  is exempt from outside-write classification. Ordinary outside reads, network attempts and uncertain
  effects still warn. Reports can arrive after execution: the audit is a signal, not an enforcement boundary.
- **FIX-21:** loaded and rotated runtime certificates have their typed extension caches initialized
  before concurrent TLS use, correcting the first-use race present since 0.6.1. Configured daemon logging
  reports handshake exception type and message without stacks, inner exceptions, peer certificates or
  request data. Other authentication, trust, network and resource-limit failures remain possible.
- **FIX-22:** required attention shows compact, current obligations at every depth and frequency, with
  complete references, recorded freshness and an inspection command. Typed later evidence resolves
  old items; missing or undecidable evidence remains unknown. Canonical status uses records alone;
  human status, progress and Workbench also label local worker observations. Prose, a fix or unrelated
  acceptance cannot supply a missing disposition.

The seventh live Codex run, on macOS at `36c90ba`, committed exactly the permitted file. Settlement
audited the commit and fetched its branch into the registered clone. This retires the earlier inability
to commit for that configuration; FIX-19's live qualification remains macOS-only. The
[limitations guide](limitations-and-platforms.md#live-codex-qualification-fix-18-and-fix-19) retains all
seven observations and the remaining account, harness, platform, billing and containment limits.
Legacy linked worktrees require preservation and explicit removal before a new workspace is prepared.

Machine-local worker defaults and workspace placement still require explicit configuration. The
efficiency Adapter and Remote remain unbuilt. Context tokens and model fit remain unknown without a
qualified local tokenizer. [Installation](install.md) covers setup; [limitations and platforms](limitations-and-platforms.md)
retains the custody, accounting, transport, recovery and native desktop boundaries.

## This documentation set

The CLI, API and record references and manifest were generated on October 18, 2026 UTC from the Release
build of version/index commit `9dd2986dd3dbd6c5031ae50e710c7ab8aa4954d6`, based on main at `daf631c`.
They contain 113 commands, 72 API operations and 39 record types, with 17 required documents. This is
reference-generation provenance, not the later documentation or final release commit. The generated
files retain the tool's output; the notes command adds its exact window to the manifest.

The source reviewed includes FIX-19 at `09152e9`, UPDATE-01 at `5eba6a8`, FIX-20 at `36ed2ec`, FIX-21
at `f4fb21b` and FIX-22 at `9a06756`, through main at `daf631c`. Maintained drafts were reconciled in
`b7a7675`. The API, both record formats, software, Workbench, MCP, required-attention rules and two
communication documents are byte-identical copies of their maintained originals. `api.md` and `api-v1.md`
both copy `docs/api-v1.md` to preserve relative links. The installation, operator, limitations,
format-index and overview guides are revised from 0.7.2.

The release notes render the estate's records with `--from 0.7.2 --to 0.7.3`.
Their exclusive start follows 0.7.2's recorded end; the manifest records the exact window.
Accepted work has one entry per work order, naming its latest accepted current result and acceptance;
earlier attempts and results remain in `work observe`. Later unaccepted results remain separate and
superseded results are omitted. Classification uses current estate state and creates no acceptance.

| Document | What it covers |
|---|---|
| [install.md](install.md) | Bootstrap, archives, NuGet, release checks, upgrades and worker prerequisites |
| [operator-guide.md](operator-guide.md) | Work, releases, recovery, goals, workers, pairing, context, watching and current attention |
| [cli-reference.md](cli-reference.md) | Generated commands, arguments and options |
| [api-reference.md](api-reference.md) | Generated API v1 operations, bodies and responses |
| [record-reference.md](record-reference.md) | Generated record types and fields |
| [record-format.md](record-format.md) | Compatibility and the relationship between formats 1 and 2 |
| [record-format-v1.md](record-format-v1.md), [record-format-v2.md](record-format-v2.md) | Frozen format rules, restore, execution and observations |
| [api.md](api.md), [api-v1.md](api-v1.md) | Identical API drafts, transports, authentication, SDK and recovery |
| [software.md](software.md) | Repositories, releases, packages and worker implementation limits |
| [workbench.md](workbench.md) | Desktop surfaces, release notices, current attention and qualification limits |
| [mcp.md](mcp.md) | Protocol, reads, five narrow writes and estate-only worker approval |
| [required-attention.md](required-attention.md) | Exact resolution rules, portable status, local observations and unknown cases |
| [communication-adoption.md](communication-adoption.md) | Explicit adoption of the first-party operator's preference |
| [communication-qualification.md](communication-qualification.md) | Retained communication/context evidence and deferrals |
| [release-notes.md](release-notes.md) | Changes rendered from the estate's records with `--from 0.7.2 --to 0.7.3` |
| [limitations-and-platforms.md](limitations-and-platforms.md) | Remaining limits, seven live Codex runs and artifact expectations |
| [manifest.json](manifest.json) | Generation provenance, document and API inventories, formats and notes window |

## Checking the version and set

```sh
vericairn version
vericairn docs check docs/releases/0.7.3
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.7.3
```

With the NuGet tool, replace `vericairn` with `dotnet vc`. A packaged release prints `0.7.3+<commit>`;
an ordinary source build retains `-dev`. Release-check disclosure and notices go to stderr, leaving
stdout intact. `doctor` reports runtime, custody, format and daemon information. The estate pin changes
only through upgrade. Structural checks and build comparison do not judge prose, qualify a platform
or accept a release. The frozen-set guard protects every indexed earlier set.

After publication, artifacts and `SHA256SUMS` are on the [0.7.3 release page](https://github.com/Vericairn/vericairn/releases/tag/v0.7.3).
The estate release record supplies their authoritative hashes. Source and Community packages are under
Apache License 2.0; copyright (c) 2026 Jason Furr.
