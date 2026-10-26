# Release notes, Vericairn 0.8.4

Prepared by `vericairn docs notes` before the release build, from the estate's records since the end of the notes of 0.8.3 (2026-10-23 11:50 UTC) up to 2026-10-26 14:49 UTC; the records are the source and this page is a rendering. The publication of 0.8.4 is not yet recorded: its commit, workflow run, artifacts by hash and qualified platforms are in the release record the estate writes when the draft is observed and, once published, in the published revision; `limitations-and-platforms.md` of this set states what was known before the build.

## Security

Vericairn 0.8.4 closes one more way a clone's own git state could mislead a checked push, continuing the hardening of `changes push` that 0.8.3 began. It does not let anyone sign a record without an admitted device or act as another principal.

**Where a checked push trusted a derived cache (0.8.0 through 0.8.3).** A checked push confirms the push is a fast-forward: that the branch's new head descends from the head Vericairn recorded, so the push adds history and never rewrites it. Git can answer that ancestry question from derived caches in the clone's `.git` — a commit-graph file, pack bitmaps, a multi-pack-index — rather than from the commits themselves. These caches are built from the objects, but nothing in a clone stops someone from writing a crafted one. A clone whose `.git` another person or an agent can write could carry a commit-graph that reports a commit as an ancestor when it is not, so a push that rewrites history could pass the fast-forward check and be sent as if it only added commits. In 0.8.4 a checked push runs git with its derived object caches disabled (`core.commitGraph`, `pack.useBitmaps` and `core.multiPackIndex` all off), so the ancestry check reads the real object graph and a crafted cache changes nothing.

Upgrade to 0.8.4 before running `changes push` from a clone whose `.git` directory another person or an agent could write. If you used `changes push` under 0.8.0 through 0.8.3 from such a clone, compare each pushed branch on the remote with the head Vericairn recorded; a mismatch, or a branch whose remote history does not contain the recorded head, is the sign to investigate.

## What the release contains

The release record of 0.8.4 is written when its draft is observed; until then the accepted work below is what the release contains.

## Work accepted

### CI-05: CI steps and jobs have time limits

Result 01M5R8NP92RBJDPJ4JXSH02AMS (partial), recorded 2026-10-25 00:51 UTC. CI-05 is implemented and committed on work/ci-05 at 6ee78525bfb9275e8cbfcc9461caaf05b3dd5fe8. Nine runnable jobs now have explicit limits: test shares 60 minutes, packaging 45, published CLI checks 30, enclave helper/draft/NuGet publication 20, workflow policy and secret scan 15. Nineteen package-install or download steps have 10-minute limits, including SDK setup, restores, the Linux secret service, gitleaks, local-feed installation, API baselines, CycloneDX and artifact/package downloads. The two reusable-workflow callers use the bounded jobs in package.yml because GitHub does not support timeout-minutes on callers. The workflow README states the limits. ContinuousIntegrationTests adds job enumeration, exact duration checks and install/download checks without changing any existing assertion. Local validation passed; the CLI suite and three-platform run remain unperformed under this assignment's sandbox, no-network and no-push constraints.

Limitations recorded:
- This result establishes the committed configuration and offline checks, not a passing CLI suite or a passing three-platform run.
- Runtime timeout cancellation has not been observed in GitHub Actions for this commit. The limits use GitHub's standard job and step timeout-minutes behavior.
- The new checks follow the repository's existing block-indented workflow test convention. Install discovery covers named install/restore/download/setup steps, setup and artifact actions, the API baseline script and common direct package/download commands; future indirect installers must remain identifiable to the checks.
- No push, merge, publication, public announcement or remote access was performed. Only Codex's own attempt and this result were authored through the MCP server.

Acceptance 01M5RPTNRCPSG0JHR36S9H1CAA pins this result.
Earlier results and attempts: `vericairn work observe CI-05` (work order 01M5QJ0S9FRJBNRY1KE00HTSVZ).

### FIX-64: Release notes render a superseded record only as its current revision

Result 01M5R9X8ZT4GWAK1KF4F5DPY6F (partial), recorded 2026-10-25 01:13 UTC. FIX-64 is implemented and committed in Vericairn/vericairn on work/fix-64, head 4a318cad8c394043efeec07e34d3b14449921022. Decisions, work orders, milestone/release acceptances, identity records and migrations now render one entry per supersession chain at the revision current at the window's inclusive end. Revised entries name every replaced revision ID, earliest first, including revisions before the window. A later revision does not replace wording in an earlier window. Document revisions and both release-history sections retain every revision, including candidates and publications. Result sections retain their existing output, selection and acceptance rules. The live operator and software release guides describe the rule. Implementation and local checks are complete; the three-platform check on a pushed head is not established, and concurrent Runtime runner crashes remain a qualification limitation.

Limitations recorded:
- The requested acceptance criteria are not fully established until the builder verifies the three-platform run on the exact pushed head.
- Full Runtime validation passed with serial test collections; both normal concurrent attempts crashed before completion. Recheck the normal runner and its crash cause before acceptance.
- The full CLI suite was not run. All 49 tests in the five affected classes passed, including unchanged original assertions.
- Records retained 5 existing platform-custody skips and Runtime retained 22 existing conditional skips. This local run does not qualify other platforms or opt-in live integrations.

Acceptance 01M5RPV6SQKR53EQS4025SVNQ0 pins this result.
Earlier results and attempts: `vericairn work observe FIX-64` (work order 01M5QHZCY32WGFMFNTBE78Q1CZ).

### FIX-66: estate init --release names the release observation it records

Result 01M5RD7S37KR0E4WX24S4EQGS6 (partial), recorded 2026-10-25 02:11 UTC. FIX-66 is implemented and committed locally on work/fix-66 as db99249e7c07dd84f6fa2030bcdefbf7726ef768. estate init --release appends the vericairn-release observation receipt to human output and returns a release object containing path, id and hash in JSON. Ordinary source initialization retains its existing output. Tests cover both output formats and storage profiles, and the operator guide shows the receipt. The CLI test project builds with zero warnings and zero errors. Runtime CLI tests and the required three-platform run on the pushed head remain for the builder. No push, merge or publication occurred.

Limitations recorded:
- The CLI tests compile but their behavior has not been executed in this attempt. This result does not establish the required three-platform pass.
- The MCP work tools were initially unavailable in the session. Local draft edits and initial static checks preceded the recorded attempt; that timing is disclosed in the attempt note. Only this attempt and this result were authored through MCP.
- This is a local commit for review, not an accepted result, pushed head, pull request, merge or publication.

Acceptance 01M5RPVV9S23C7EFP3NSNZSACW pins this result.
Earlier results and attempts: `vericairn work observe FIX-66` (work order 01M5QJ032N0K6B7NRPR1W2G58X).

### FIX-65: A run whose settlement write failed can be settled later

Result 01M5RDXH58YTNZ0GB259G5G27H (partial), recorded 2026-10-25 02:23 UTC. FIX-65 is implemented and committed on work/fix-65 at f590c662cb76187c897f490e70c86429d33ea6ff. The worker retains the exit outcome, reported usage, original accounting interval and final audits before settlement writes. Authorized callers can retry through worker settle and POST /v1/runs/{id}/settle; runtime startup also recovers eligible observations. Recovery records the observed facts, releases the branch reservation and clears the local failure. Missing, malformed or inconsistent observations remain unresolved with a reason, while live and terminal runs are refused. Older snapshots preserve their retained facts and explicitly disclose missing audit and accounting detail. CLI, SDK, daemon routing, OpenAPI and the worker guide are updated. Both new interface members have defaults; no interface member lacks one. No existing assertion was relaxed. The builder's remaining validation is described below. The new retry caches have bounded storage exclusions from local inspector output, with an added test proving its bytes and hashes remain unchanged. Renderer code, implementation versions and golden fixtures are unchanged. The complete local Records and Runtime suites pass; Runtime used serial execution because a parallel native crash also reproduces on the unchanged base. The result is partial because the pushed-head three-platform run and the specified external validation remain outstanding.

Limitations recorded:
- The pushed-head three-platform acceptance check remains outstanding. No push, merge or publication was attempted.
- CLI execution is deferred by the brief; daemon transport assertions were compiled but not executed here.
- Legacy snapshots have no retained monotonic duration, currency observation or final audits. Recovery derives elapsed time from their retained UTC timestamps, keeps unreported currency charged at its cap, and records the missing audits as unverified.
- Recovery releases the ledger reservation and preserves the workspace for review; it does not repeat workspace transfer to the registered clone.
- Parallel Runtime execution terminates with SIGSEGV (-11) in this macOS sandbox. The same failure was reproduced from unchanged base revision 6c750fdecc5f0c8557b6d4450a99215ba65b0b2c in an isolated checkout, built offline with zero warnings/errors. This pre-existing condition is outside FIX-65; the final full suite is validated serially without test filters or assertion changes.

Acceptance 01M5RPX27F8KQQ47AFDJCJJWVE pins this result.
Earlier results and attempts: `vericairn work observe FIX-65` (work order 01M5QHZQZA9RV1FA9HW25XMQ3S).

### FIX-68: Every estate gets the first-party bootstrap

Result 01M5RNBDBYQW2A3XN37FB61TJT (partial), recorded 2026-10-25 04:33 UTC. FIX-68 is implemented and committed on Vericairn/vericairn branch work/fix-68, ready for builder review. Final local head: 8dc7944e94f1f213431001f7d80c3b8fc3a6d853. Commit 1f606b71a6c8662efa3916a7f231a888b0860126 ships the imported first-party bootstrap scripts as CLI resources, installs them during source and release initialization, adds estate bootstrap with written/unchanged receipts and explicit --force replacement, and adds regression and native-shell cold-start tests. Commit 8dc7944e94f1f213431001f7d80c3b8fc3a6d853 updates the install and operator guides. I reviewed and adopted the interrupted session's starting edits, corrected their analyzer errors and initialization refusal guidance, extended the strict local-only directory assertion for the two new files, and added initialization preservation and cold-start coverage. No starting implementation or guide file was discarded. The local implementation and validation are complete; the all-three-platform condition on a pushed head remains unestablished because this assignment prohibits pushing and network access.

Limitations recorded:
- The exit question is not yet proven on all three platforms. The final local head is 8dc7944e94f1f213431001f7d80c3b8fc3a6d853; there is no pushed-head run from this attempt.
- Local validation used macOS arm64 and eight selected CLI/documentation classes. The full CLI suite was not run in the sandbox.
- Cold-start fixtures are offline and use the already built CLI. They establish bootstrap control flow, pin/checksum handling, cache reuse and real estate checking locally; they do not establish live release downloads or building the complete product from remotely fetched source.
- No push, merge, publication, estate-clone edit, or estate record other than this contributor's attempt and result was performed. Build and test output remains ignored under artifacts; validation notes are retained in this result.

Acceptance 01M5RW3TNTPW4KDAZQ45CPK5SY pins this result.
Earlier results and attempts: `vericairn work observe FIX-68` (work order 01M5RC0R0TFGTDDS3812GGNZ6N).

### OPS-02: vericairn operator close: one signed action closes selected finished steps after showing their results and reviews

Result 01M5RR7S2D5XERXW38XFAQS4DS (partial), recorded 2026-10-25 05:23 UTC. Implemented OPS-02 on work/ops-02 in local commits 5bed57ee (operator close with exact reviewed result selections) and 9121d36a (preserve incomplete close outcomes in clients). Final head is 9121d36a4d9e9e87871a429b4279f153484a10e1. The read-only listing contains each current work order's latest completed, reviewed and unaccepted result, its outcome and summary, the exit question, and every passing review's independence, summary and exact hash. Repeatable --select or --all signs one acceptance per selected result, naming the exact result and complete displayed review basis. Work-order, result, review or membership changes, missing passing reviews, non-operators and unavailable signing devices refuse the complete selection. CLI, API, SDK, compatibility notes and operator guidance are included; the guide retires closing scripts. The local implementation is ready for builder review. CLI execution and the required three-platform run at a pushed head remain pending under this attempt's sandbox, no-push and no-network constraints.

Limitations recorded:
- The current signing stack exposes one signature at a time and no shared presence session. Operator close therefore uses the brief's permitted fallback: one presence check per acceptance where custody requires it. Tests exercise admitted fixture devices; no actual operator presence prompt was invoked.
- The batch validates the complete selection before storage and rechecks it after signing, validation and document preparation. Existing filesystem batch storage is journaled rather than an atomic filesystem transaction: an I/O interruption after a record is stored returns its receipts with reconcile-required. Evidence and authority refusals store no acceptances or new batch journal.
- The final local validation ran on macOS arm64. CLI execution and the three-platform run remain outstanding; no claim is made that the pushed-head acceptance condition has been met.
- The estate clone was not edited directly. This attempt used the MCP server only to record its own attempt and result; no other estate record was authored.

Acceptance 01M5RXM85PX0N0S71VE6W1HDD3 pins this result.
Earlier results and attempts: `vericairn work observe OPS-02` (work order 01M5HRVKF3N1796CK9YSVH2QJW).

### PROMPT-01: Saved prompts: a shared, categorized library with built-in foundation and pack prompts and personal favorites, used with new runs, assignments and ask answers, and offered as MCP prompts

Result 01M5RSFJ3WER6F29QJ59BZPQRQ (partial), recorded 2026-10-25 05:45 UTC. PROMPT-01 is implemented within the permitted source, test and documentation paths on work/prompt-01. Local head is adac296813a30c622a33326ab16600cfdc4bda9e. Three commits add signed saved-prompt and prompt-preference records within record format 3; categorized estate, foundation and software-pack discovery; reversible personal hiding and favorites and operator disabling; revision, retirement and origin-preserving copy; recorded exact uses with new assignments, new runs and ask-answer decisions; CLI/API/SDK operations and standard role-filtered MCP prompts. One-request favor-and-use is supported. Uses retain author, source, revision/version, definition hash, literal values and rendered comment; ordinary authority and run admission remain binding, and prompt comments cannot widen context selection. Run and decision revisions preserve the original use. All ten new IEstateApi members have default implementations. Readable companions, collection coverage, generated reference additions and guides are included. No existing assertion was relaxed. The local checks below pass, but this is not a claim of completed three-platform verification or release readiness: the root orientation requires an out-of-scope update, CLI execution is delegated by the brief, and the eight built-ins remain drafts awaiting operator review.

Limitations recorded:
- Root orientation regeneration remains outside the allowed paths. Before a passing CLI/CI run, obtain authority to update the root generated orientation and regenerate it from an isolated fixture matching the existing repository orientation. AGENTS.md needs the five new tool rows. McpTests.TheRepositorysOrientationFilesAreCurrent is expected to report it stale until then; the assertion and root files remain unchanged.
- The eight built-ins are review drafts, unavailable for copying or use until operator review: foundation clarify-next-step, summarize-evidence, review-within-scope and prepare-handoff; software review-change, plan-tests, explain-failure and check-compatibility. Exact draft text and descriptions are in the two catalog outputs. This result neither approves nor ships them.
- Five record-suite custody tests were skipped: two Secure Enclave cases, two Windows CNG cases and one Linux secret-service case. Local macOS checks do not establish the three-platform result. CLI execution and three-platform CI on an authorized pushed head remain necessary.
- The final implementation preserves ordinary assignment, run, decision and custody authority. Prompt text is literal commentary, not a semantic authority grant. Ask answers retain the existing operator-only decision rule. Favor-and-use writes the target first, then its personal favorite; a failed second write reports the recorded receipt and must not cause the target use to be retried.
- No push, merge, publication, live estate-clone edit, frozen documentation edit or extra estate record was made. This attempt and its result are the only estate writes authorized for this work. There was no remote network use, no inspection of the actual Vericairn home or its variable, and no goal-management or MCP resource-tool invocation.

Acceptance 01M5SDXQZHP4P7Z9J2WK5Y1BKV pins this result.
Earlier results and attempts: `vericairn work observe PROMPT-01` (work order 01M5HRV34ANYJJK7NQYT0CCS1M).

### CODE-10: A guided decision from an answered ask: the operator's words and time, a document list held to the change, and pins

Result 01M5RTCY4XYZPRS1HE6XCE8T3X (partial), recorded 2026-10-25 06:01 UTC. Implemented CODE-10 on local branch work/code-10 in commit a9b422317df7c1964538a64fae7ac948418fafa1. The CLI, authenticated API and SDK record the operator's verbatim answer with its time and exact ask revision, refuse later or future decision times, derive the complete changed design document set from a full local base through the working tree, and create each next document pin through the shared docs pin preparation. The signed decision retains the base, observed head and exact pin references. Prose refusals derive identities from estate run and qualification records without a compiled identity list or echoing the refused term. Added refusal and success tests, operator guidance and interface compatibility notes. The complete signed batch is validated before storage, and interrupted storage preserves receipts for reconciliation. Local implementation and validation are complete; the required pushed-head three-platform validation remains outstanding under the no-push constraint.

Limitations recorded:
- Local validation used the available macOS arm64 host and .NET 10.0.303. It does not establish Windows, Linux or the required three-platform pushed-head result.
- Five tests in the full records suite were skipped by their existing conditions. The full CLI and IPC-dependent daemon suites were not run here.
- Guided decisions add optional answer and changeSet body fields in record formats 2 and 3. Older readers that do not know these fields need an upgrade before reading new guided records, as documented.
- No push, merge or publication occurred. No estate clone files were edited. Only this contributor's own attempt and result were authored through MCP. Build outputs remain ignored and uncommitted.

Acceptance 01M5S4PJQYP20BM2B1CE3KHYRT pins this result.
Earlier results and attempts: `vericairn work observe CODE-10` (work order 01M5HRVT2BXCW0PXFT2JZDKR39).

### TPL-01: Templates: the contract, instances, sections and their states, reviews, cohesion and briefs

Result 01M5RVQDYBQK29PGASHG92F6WM (completed), recorded 2026-10-25 06:24 UTC. TPL-01 is implemented and committed locally on work/tpl-01, ready for the builder's review and external validation. Head: 398b8b36e55c555192e3654decb61290f86ca2c3; base: f7cfad893d566634fe880bd220cab6dc8d073b53. The mechanism covers strict declarative contracts, exact instance pins, defaults and overrides, section discovery and states, exact result payloads, deterministic cohesion checks, independent per-rule judgments, complete measured section briefs, explicit upgrades and retained retired versions. CLI, API, SDK and MCP surfaces are present. J57, J58 and J59 pass locally. Record format remains 3, with additive schema validation, describe entries, companion coverage and generated reference documentation. This result claims the local implementation, not acceptance or a successful three-platform run on a pushed head.

Limitations recorded:
- Workbench section inspection and per-section review editing remain, as the brief permits when they are not small. The existing Documents workspace is organized around whole-document reviews; it needs selected-section state/purpose/source/criteria presentation, fidelity and judgment editing, and refresh/cancellation coverage. Use CLI/API for template workflows meanwhile.
- TPL-02 must supply and explicitly adopt the estate's own versioned template content, exact sources, registered generators/checks and option defaults. The design, roadmap, index and release views need their own generator contracts and declared item semantics. No estate-specific template or adoption record was installed.
- TPL-COMMS-01 must supply communication/context/commit/review contracts and connect their role briefs and process-profile choices to this mechanism. Section briefs retain complete required sources and measured canonical bytes; they do not claim token fit or silently reduce context. An allocation overflow returns incomplete context with full inspection material.
- All eight new IEstateApi members have default implementations, and DocumentReviewRequest retains its six-parameter constructor. The new opt-in ITemplateCheck interface has two required members without defaults: Id and Check(string, IReadOnlyList<TemplateSource>). No existing interface gains a required member.
- Template retirement is host registration state; estate JSON files have no retirement flag. Retained original contracts remain necessary. Upgrades use explicit living-document revisions and a read-only difference helper; file migration and renewed reviews remain explicit work.
- The standard dotnet test launcher could not initialize its IPC endpoint in the sandbox, so suites ran through their built test executables. The first solution build encountered the optional Avalonia telemetry task's sandbox file access; disabling that task allowed the full build to pass without relaxing compilation checks.
- An early offline restore terminal session, 34796, remained unresponsive to cancellation. Process enumeration is unavailable in this sandbox, so its final process state could not be confirmed. Subsequent serial restore, all requested builds and the reported test suites completed successfully; host cleanup of that abandoned session may still be needed.

Acceptance 01M5SHS2GVMRSMCAHV5ZW0EQ5P pins this result.
Earlier results and attempts: `vericairn work observe TPL-01` (work order 01M5HRTSBQ1R8NXHDGXRRAVMMR).

### CODE-09: A release review brief generated from the records and run as an assignment to the Reviewer, with requested progress

Result 01M5RYHB7F9V68PXB26Y9HM6CA (partial), recorded 2026-10-25 07:13 UTC. Implemented CODE-09 on work/code-09 in commits 756b4e7a and 5b409d6106f90e2ba8002a0bf7af1f6f71dc3506. The CLI release review-brief command, API and SDK derive focused and full briefs from exact candidate, successful merge, previous review and operator-decision records. Assignment rechecks the prepared text hash under the write lock, resolves the Reviewer by role and retains the identical text and source pins for the worker. Scope is explicitly recorded in decision.releaseReview; the command does not infer scope from prose. Added record-format, runtime, protocol, mapped-route and CLI coverage, with no existing assertion weakened. The implementation and local checks are complete; the CLI suite and three-platform run on a pushed head remain for the builder because this attempt permits neither the required sandbox exit nor pushing or network use.

Limitations recorded:
- This is a partial result because the required CLI execution and three-platform pushed-head evidence cannot be produced within this attempt's constraints.
- An operator decision must carry releaseReview with exact candidate and work-order pins, full or focused scope, focus and probes. Disposition decisions must pin the exact review as their subject. Existing prose-only decisions are not interpreted or backfilled, and this attempt recorded no such declarations.
- The merge window uses recorded timestamp and id order and successful change-merge records for the same product and repository. It does not infer unrecorded merges or Git ancestry. Complete briefs above the one-million-character assignment text limit refuse without truncation.
- Assigning records an assignment and starts no launcher. A worker launch needs the assignment id, Reviewer principal with an admitted held signing device, registered repository clone, candidate commit as base, an available qualified launcher with explicit execution settings, authorized initiating principal and applicable budget and wall-time limits. The operator chooses the harness; none is hard-coded or configured by this change.
- WATCH-02 requested progress is not implemented. ReleaseReviewBriefResponse.RequestedProgress is the named integration seam and reports unavailable.
- Initial validation exposed and corrected a new-test fixture clock issue, an invalid synthetic operation transition and the expected API-count update. An initial parallel offline restore stalled and cancellation was requested; serial offline restore succeeded. A default full build encountered the sandbox-blocked Avalonia telemetry task; the final successful build disabled that task without changing repository build configuration.

Acceptance 01M5SP03HNDE91PAZBQZTAXJK1 pins this result.
Earlier results and attempts: `vericairn work observe CODE-09` (work order 01M5HRVPQW2XXJ4JRE4V3TCH5T).

### OPS-03: Operator requests: what the builder or an agent asks the operator to do, stored durably and closed as done or dismissed

Result 01M5RYYW4WGB0PNVB4TWSB1X1R (partial), recorded 2026-10-25 07:21 UTC. OPS-03 is implemented and committed locally on work/ops-03, ready for the builder's review. Commit 97a1003e756e772bed2b6ac1a6b5dc62c2cb4caa adds durable format-3 operator requests with exact draft hashes, destination, requester, work or release subject and superseding revisions. Operator list, status and Workbench show the open queue with recommendations first and three full requests per delivery; remaining identities stay visible. Done and dismiss each retain one signed outcome. Agent closes preserve the agent's authorship and cite the addressed operator's verbatim words. A signed observation can close the exact request with proof of the effect. Publication requests retain prior intent and an observed destination without dispatching content. Open asks can be carried once while their records and decision semantics remain intact. CLI, API, SDK, MCP read, schemas, companions and operator documentation are included. All ten new IEstateApi members have default implementations; no new member lacks one. Implementation and local checks are complete; the CLI suite and required three-platform run on a pushed head remain pending under this assignment's constraints.

Limitations recorded:
- This result does not establish the required three-platform pass or a CLI-suite pass. The branch remains local; no push, merge or publication occurred.
- The full Records run skipped two Secure Enclave tests because the helper/custody prerequisites were absent, two Windows CNG tests and one Linux secret-service test.
- Generated references are retained only as ignored build output and recorded here by hash. They are not committed or frozen documentation.
- Remote request presentation is reserved for 0.9.0 by the brief and is not part of this change.

Acceptance 01M5STZJZAW24ZS85DFFY4WH6F pins this result.
Earlier results and attempts: `vericairn work observe OPS-03` (work order 01M5J6N98XSXJ8Y7YTWVZDC2H9).

### DOCS-12: A release documentation pass: each document a publication makes due goes to a reviewer with the release's changes

Result 01M5S0JY4XA49HB90WGQNK8895 (partial), recorded 2026-10-25 07:49 UTC. Implemented DOCS-12 in Vericairn/vericairn on work/docs-12 at b34f921c7c73751c33416dc77302184e4dc9aead, based on cd1bbb27a7a9c58efdd81f81c10370513a3214ec, in two local commits. The CLI, API and SDK provide a publication documentation pass derived from LivingDocuments: exactly the current documents with a release reason naming the selected publication, their covered paths, changes between local publication commits, record context and explicit comparison gaps. An exact displayed selection can become an ordinary reviewer work order and assignment; publication and declaration pins and current due state are rechecked before storage. The pass prepares unified patches for declared authored README, roadmap and release-index front documents, with source commits and before/after hashes, without applying or committing the proposals. Partial assignment receipts preserve reconciliation requirements. All new interface members have default implementations; no new abstract member is required. Existing SDK exception assertions remain intact. The exact API inventory assertion increases from 106 to 108 and explicitly names both added operations. The implementation is locally reviewable, but the required pushed-head three-platform qualification is not established under this assignment's constraints.

Limitations recorded:
- This result does not establish a passing pushed-head three-platform run or a completed CLI execution.
- The native crash in default-parallel Runtime execution is unresolved; a serial result does not establish parallel stability.
- A pass is the current remaining publication queue, not a historical snapshot: already-cleared documents and release reasons replaced by a later publication are absent.
- Missing local publication commits or source contributions remain explicit comparison gaps; the pass does not fetch.
- Unsupported or ambiguous front-document markers remain for manual review. Proposed patches must be inspected against their recorded source bytes and separately applied and reviewed.

Acceptance 01M5SZ2RFH767TP15SVHCABTFZ pins this result.
Earlier results and attempts: `vericairn work observe DOCS-12` (work order 01M5HRVXBK86FZ9VQ0QTZ42G9Q).

### MCP-02: MCP resources and prompts: briefs, records, pinned documents and context packages as read-only resources, and saved prompts as MCP prompts

Result 01M5SG90EPFGDXMWQRZ5XT142D (partial), recorded 2026-10-25 12:23 UTC. MCP-02 is implemented in two local commits on work/mcp-02: a40a6880130099118ce49b5568becceab8a6f280 and 078cc8c5b4054b538f6390ace20d4ea90f32c6f5. The server advertises resources/list, resources/templates/list and resources/read for current records, work and assignment briefs, pinned estate documents and context packages. Reads return exact base64 bytes, their SHA-256 and length, media type and separate source identity where applicable. Runtime reads reuse the matching read operations and recheck estate access; old document pins retain their exact revision. Prompts retain categories, arguments and PROMPT-01's recorded-use behavior, share read-tool visibility, omit hidden, estate-disabled and retired offers, and use prompt_use's admitted-operator boundary for recorded retrieval. Resource and prompt metadata identify untrusted content. The operator queue stays off MCP. Initialization explicitly declares no subscriptions or list-change notifications. Four new IEstateApi members all have default implementations and are documented in docs/api-compatibility-0.8.4.md; no added interface member lacks a default. The CLI adds opt-in measured brief flags for exact resource parity while preserving its ordinary brief. The MCP guide, API and CLI notes, prompt guide and orientation generator describe the contracts. Final local validation on head 078cc8c5b4054b538f6390ace20d4ea90f32c6f5 passed 158 runtime tests and 6 API tests; both daemon and CLI test projects compile without warnings. Full acceptance remains unestablished because the brief reserves CLI execution and orientation-file regeneration for the builder, and prohibits this worker from pushing or using network validation.

Limitations recorded:
- This result is partial with respect to the full exit checks: no pushed-head three-platform run is available, and CLI execution remains outstanding.
- The builder must regenerate root AGENTS.md from the updated orientation generator within the builder's permitted scope. The existing freshness assertion was not relaxed.
- Briefs contain a live communication freshness time. Their hash identifies the exact response at that observation; independent reads can differ even with unchanged records. Byte-parity tests use a fixed per-runtime observation clock and keep full-byte assertions.
- The standard prompts/get operation preserves PROMPT-01's recorded-use contract and requires an admitted operator over MCP, as prompt_use does. It is not an unrecorded prompt inspection operation; discovery remains a read.
- Resource and prompt listings are complete without pagination, subscriptions or list-change notifications. Older API implementations return unsupported through the new default members; no fallback bypasses their refusal.

Acceptance 01M5T23G5XWKT835ZK1CK95GVY pins this result.
Earlier results and attempts: `vericairn work observe MCP-02` (work order 01M5J3VNRZYH2YR6EQER7Z9J33).

### TPL-COMMS-01: Communication, context, commit and review templates

Result 01M5SMQNNNFX6KR3B4H86F7251 (partial), recorded 2026-10-25 13:41 UTC. Implemented TPL-COMMS-01 in two local commits, ending at e109417bc480c2dd956dbcb5e03e52a3ad4f9dea. Four versioned presentation contracts provide communication, context-brief, commit and review forms with generated source identities, withheld lists and budgets. Exact template and source pins, independently reconstructed required content, and checks at short, standard and comprehensive depths preserve required facts and original references. Explicit estate template-defaults adoption enables the forms; withdrawal is supported, and estates without adoption retain their existing presentation behavior. Runtime, protocol, SDK, HTTP, MCP and CLI integration and documentation are included. Implementation and available validation are complete; acceptance remains pending the builder checks below.

Limitations recorded:
- Acceptance remains pending the CLI suite, the three blocked daemon transport tests and the required three-platform run on the exact pushed head.
- Commit attribution is supplied explicitly and preserved mechanically. A human reviewer must verify it against estate policy; free-form attribution policy and the semantic truth of authored statements are not inferred automatically.
- The generated orientation in AGENTS.md was left unchanged as required. The builder should regenerate it when integrating the build so that it includes template_render.

Acceptance 01M5T6KKAWQ29Z9RCS99HMP124 pins this result.
Earlier results and attempts: `vericairn work observe TPL-COMMS-01` (work order 01M5HRTZVVQKFM6QWPG7KM79WH).

### TPL-02: The estate's own templates: the design document, open decisions as asks, the design index, release notes and the README

Result 01M5SNBPRCPZHFCJ7D28VXM3X0 (completed), recorded 2026-10-25 13:52 UTC. TPL-02 is built on work/tpl-02 and ready for the builder's review and three-platform run. Local head f4b60e6b05ee5b52a58dd276914063e467c552e2 contains six first-party templates at 1.0.0, their installed generators and checks, a byte-preserving adoption command/API, explicit generated-region rendering with recoverable document pins, estate-shaped fixtures, compatibility documentation and the generated development reference. The brief's local implementation outcome is complete. The estate-wide exit question remains unproved until the builder adopts the documents after merge and obtains the three-platform result on its pushed head.

Limitations recorded:
- The full estate-wide exit question remains pending actual adoption and a passing three-platform run on the builder's pushed head.
- CLI execution, full platform suites and hardware/service-dependent checks were not performed here. The CLI test classes to run are named above.
- The first-party templates select record-backed current views. Historical numbering and prose-only planning remain authored history until the builder records the corresponding asks, document revisions and typed work placements.
- An attached instance is not automatically current: missing parts, stale generated content and unjudged semantic cohesion remain visible until the required sections and reviews are complete.
- Validation notes are retained in this result. The generated reference describes the development additions using the base build's 0.8.3 version; it is not a published 0.8.4 documentation set.

Acceptance 01M5TC64H6F3T2W8FE2M3WJQXZ pins this result.
Earlier results and attempts: `vericairn work observe TPL-02` (work order 01M5HRTWM3WBPG331GCZ2T1XEF).

### FIX-69: A charter revision leaves the release pin's observation current

Result 01M5SNTC3AKJSBF2PYPXCAWXQ6 (partial), recorded 2026-10-25 14:00 UTC. FIX-69 is implemented and committed on work/fix-69, ready for builder review. EstateLoader suppresses record.subject-stale only for a vericairn-release observation whose resolved subject is an estate charter and whose latest revision is the estate's current charter. Other stale subjects retain the existing check, and subject type/hash validation still runs. New Records tests cover successive charter revisions, observations of intermediate revisions, all three supported record formats, four other observation kinds, stale reviews, release observations of non-charter subjects and another charter, and invalid subject hashes/types. They verify that initialization's records and release manifest stay byte-for-byte unchanged. New CLI coverage exercises estate init --release, required signatures, two charter revisions, and an ordinary stale observation for repository-synced and local-only profiles. The operator guide's requiring-signatures section has no workaround or warning mention to remove. Implementation and local validation are complete; CLI execution and the exact pushed-head three-platform run remain outstanding, so this result claims no acceptance.

Limitations recorded:
- The overall exit criteria are not yet established: the CLI regressions have only been compiled and three-platform CI has not run on a pushed head.
- Five platform custody tests were skipped by their existing prerequisite checks; this run qualifies neither Windows custody nor Secure Enclave nor Linux secret-service custody.
- No record or format migration is introduced. Existing release observations are reinterpreted by the loader; no real estate was revised or checked with the changed binary.

Acceptance 01M5T3S5M7RNBASTQ08MFJC4JP pins this result.
Earlier results and attempts: `vericairn work observe FIX-69` (work order 01M5SKWAB2395GSY2R9VVT28M1).

### FIX-72: The package commands record reserved placeholder versions

Result 01M5TJ3QY9GZJ41576BMXHHH77 (partial), recorded 2026-10-25 22:15 UTC. FIX-72 is implemented and committed on work/fix-72, ready for builder review. Commit 0acf44cbd90710d0f2a83e1b30836303f6ac07af adds optional product packages.reserved declarations, explicit CLI --reserved and optional API/SDK Reserved properties, exact product-and-version pins without release pins, and regression coverage. Commit 46ebae7202ff14637d1e656b26357b64725873da documents the workflow, updates the compatibility guide, and regenerates the development record reference from the exact implementation commit. Reserved publication, unlisting intent/outcome and deprecation intent/outcome use the existing evidence and confirmation flow without requiring or creating a release. Record format remains 3. Existing release behavior and assertions are retained. The implementation is complete locally; the goal's pushed-head three-platform check remains unperformed under this run's no-push and no-network constraints.

Limitations recorded:
- The implementation and local evidence are ready for review; acceptance, merge, publication and the pushed-head three-platform result are not established.
- Local validation used recorded registry responses, not the live registry. The API tests use in-process routes; the CLI suite remains for the builder.
- The whole-solution build remains unverified in this sandbox because of the Avalonia telemetry write refusal.
- No change was made to the estate clone through workspace commands, and only this contributor's attempt and result were authored through MCP. No push, merge or publication occurred.

Acceptance 01M5TW37NDRNGCMVK52KGR91PX pins this result.
Earlier results and attempts: `vericairn work observe FIX-72` (work order 01M5TC0SW642659C828XYBE5WW).

## Work recorded, not yet accepted

Current results of this period with neither an acceptance of their exact revision nor of their work order's current revision: claims to inspect, not part of what the release is known to contain.

### FIX-63: Status stays within its deadline on Windows

Result 01M5R8HFHMSEP1QCEJN1FRM97H (completed), recorded 2026-10-25 00:49 UTC. Status evaluates the estate's adopted policies on every call, and the staleness policy's planned-without-release class walked every recorded instant for every waiting order, rescanning the estate at each one. Vericairn's own estate began evaluating it on October 23, when a review record completed its adopted policies' basis; status then grew with the estate under 0.8.2 as well as 0.8.3 and passed the 30-second deadline on GitHub's Windows runner first. The search now evaluates each order only at the instants that can change its wait, with identical anchors, verdicts and findings. Merged in pull request 208.

Limitations recorded:
- Built and self-reviewed by the builder; FIX-63 was not in the 0.8.4 goal's Codex plan.
- The estate keeps 0.8.3, whose Windows status still runs near its deadline, until it upgrades to 0.8.4; issue 205 closes at that publication.
- A cold start in source mode cannot compare identical status on Windows, because the Windows build of the runtime gets its own module id; the estate pins published releases, which share one.

### FIX-70: J04 runs under a controlled clock

Result 01M5SR711ZAG5GWKWR2KRZXEFT (completed), recorded 2026-10-25 14:42 UTC. A release submission reads the policy clock for its check and again for its recheck; on a slow Windows runner the reads fell in different seconds and J04's retried submission retained two policy bases where the test asserts one. J04's submissions now run against a service whose policy clock is held at the command's start, through ReleaseJourney.OkHeld and RunHeld; real time still moves between commands, no assertion changed and no product source changed. Merged in pull request 223.

Limitations recorded:
- Built and self-reviewed by the builder under decision 01M5SKTA81HS35QMRVSTPVVY5Y; other release journeys keep the system clock, since none asserts a single basis across a second boundary.

### FIX-71: Agent products named and checked

Result 01M5SWMYYEH52MG1H2QVC7F4BY (completed), recorded 2026-10-25 16:00 UTC. Routing matches an agent principal's product to a launcher's harness name or id, ignoring case, but nothing named those values, and recording 'Codex CLI' was discovered only at goal next (issue 222). The principal type's agent description, shown by records describe principal and the generated record reference, now names the accepted values (Codex or codex, Claude Code or claude-code); records new principal still records an agent whose product matches no installed launcher and warns on standard error with those values; the operator guide's example uses a real product and explains the rule. Merged in pull request 226.

Limitations recorded:
- Built and self-reviewed by the builder under decision 01M5SP3T91DSBQRHQGR82ZJEAH; the warning is given by the command line's records new, not by the API, which has no warning channel for successful writes.

### FIX-67: A checked push's local reads do not trust derived object data

Result 01M5VS8SM5JJS53HRN4BBK5JQH (completed), recorded 2026-10-26 09:39 UTC. FIX-67 merged in Vericairn/vericairn pull request 236 (merge e0fd81fa2140114014e779c600238d957899f6a9), which passed every job on Linux, macOS and Windows, and main's run 40298548474 passed. GitPushAdapter now runs every git process of a checked push with core.commitGraph=false, pack.useBitmaps=false and core.multiPackIndex=false, so the scan range and the fast-forward check read the clone's real objects, not derived caches a clone can craft.

Limitations recorded:
- The description of the finding stays in the release documentation set and this advisory; the public commit and pull request carry a neutral title.

## Decisions

- **0.8.3's notes ship as rendered, and 0.8.4 renders a superseded record only as its current revision** (2026-10-23 13:01 UTC, decision 01M5MDMP0XGCER06BT7Z8K9NPG): The builder reported that 0.8.3's rendered notes list a superseded decision whose original wording named the review model beside its revised wording, and recommended shipping them as rendered, as for 0.8.2, with a notes fix in 0.8.4. The operator took the recommendation.
- **0.8.3 stays published after its early publication; its acceptance follows the Reviewer's review** (2026-10-24 14:27 UTC, decision 01M5Q4Y34CFCY24W4F3A6ZD2SP): The operator ran 0.8.3's publication command by mistake while the Reviewer's review was running and before acceptance was recorded; the NuGet workflow pushed the packages. The builder recommended keeping 0.8.3 published, since its assets are exactly the candidate's and pushed packages cannot be recalled, and recording the acceptance after the review if it passes. The operator took the recommendation.
- **The estate upgrades to 0.8.3 with Windows status's deadline as a known issue, fixed in 0.8.4** (2026-10-24 17:03 UTC, decision 01M5QDWET5CS7JD11FS7KBK1WM): The builder reported that the estate's upgrade to 0.8.3 fails its Windows cold-start check twice, because status exceeds its 30-second deadline on the Windows runner, while macOS and Linux pass with identical status, and recommended merging it with the issue recorded as known and fixing it in 0.8.4. The operator took the recommendation.
- **0.8.3's profile update is merged and its announcement posted on Discussions and X** (2026-10-24 17:59 UTC, decision 01M5QH348DZ5QD4X0PA7R6GJH6): The builder asked the operator to merge the profile's 0.8.3 update and post 0.8.3's announcement with finish-083.zsh, and to post the X summary. The operator did both.
- **0.8.3's release plan is closed, and five corrections from its release join 0.8.4** (2026-10-24 18:13 UTC, decision 01M5QHX5WAKMJVMY5YR1R5S1WA): The builder asked the operator to close REL-082 with close-083-release.zsh and proposed five corrections found during 0.8.3's release for 0.8.4, recommending all five, the push correction built by the builder without public description before release. The operator ran the script and took the recommendations.
- **The 0.8.3 goal is achieved and the 0.8.4 goal is active** (2026-10-25 00:41 UTC, decision 01M5R82HW320P9B4974GPVCAN0): The builder asked the operator to accept the 0.8.3 goal and activate the 0.8.4 goal with goals-084.zsh. The operator ran it.
- **FIX-68 joins 0.8.4: an estate gets the first-party bootstrap** (2026-10-25 01:49 UTC, decision 01M5RBYNTZQMGMY9YYWPW9R5V0): Modularyn's estate reported that an estate other than Vericairn's own cannot get the bootstrap the install guide's Route A uses. The builder proposed FIX-68 and recommended placing it in 0.8.4 as a Codex step under its own small goal. The operator took the recommendation.
- **FIX-68's goal is active** (2026-10-25 04:10 UTC, decision 01M5RM1B55PKFAD5PEMPZTPDSX): The builder asked the operator to activate FIX-68's goal with activate-fix68.zsh. The operator ran it.
- **FIX-69 joins 0.8.4: a charter revision leaves the release pin's observation current** (2026-10-25 13:25 UTC, decision 01M5SKT0002W3MY5ZKM7QFGT0A): Modularyn's estate reported that revising the charter leaves a permanent stale-subject warning on the release pin's dependency observation. The builder proposed FIX-69 and recommended placing it in 0.8.4 as a Codex step under its own small goal. The operator took the recommendation.
- **PROMPT-01's eight built-in prompts are approved as drafted** (2026-10-25 13:25 UTC, decision 01M5SKT579T66VQTVKXXD3V4M8): PROMPT-01 shipped eight built-in prompts as unusable drafts awaiting the operator's review. The builder recommended approving all eight as drafted. The operator took the recommendation.
- **FIX-70 joins 0.8.4: J04 runs under a controlled clock** (2026-10-25 13:25 UTC, decision 01M5SKTA81HS35QMRVSTPVVY5Y): SoftwareJourneyTests.J04 kept failing on Windows because a submission's check and recheck land in different seconds and retain two policy bases. The builder proposed a test-only fix, FIX-70, built by the builder. The operator took the recommendation.
- **FIX-69's goal is active** (2026-10-25 13:45 UTC, decision 01M5SMYM19BH83A3VBK5X20VY7): The builder asked the operator to activate FIX-69's goal with activate-fix69.zsh. The operator ran it.
- **FIX-71 joins 0.8.4: agent products named and checked** (2026-10-25 14:05 UTC, decision 01M5SP3T91DSBQRHQGR82ZJEAH): Modularyn's estate reported that routing matches an agent principal's product exactly against the launchers' harness names while nothing documents them or warns. The builder proposed FIX-71, built by the builder in 0.8.4. The operator took the recommendation.
- **Conversation intake is designed in 0.9.0** (2026-10-25 17:16 UTC, decision 01M5T1092GEMJN6DZ1P739MFYH): Vericairn reads nothing from GitHub's conversations, and the estates' reports and questions reach Vericairn only through the builder's own watcher. The builder proposed designing conversation intake in 0.9.0, with the build placed after the design is accepted. The operator took the recommendation.
- **Review practice stays: the builder reviews Codex results, the Reviewer reviews releases and security work** (2026-10-25 17:40 UTC, decision 01M5T2CYY2MA0NDDCR62ZDH1VK): The builder's description of Vericairn's practice to the operator's other estates said the Reviewer reviews each Codex result, while here the builder does and the Reviewer reviews release candidates and security-relevant results. The builder recommended keeping Vericairn's practice and aligning the other estates with it. The operator took the recommendation.
- **FIX-72 joins 0.8.4: the package commands record reserved placeholder versions** (2026-10-25 20:28 UTC, decision 01M5TBZVQ8KNRSN1HK3RGQZT5Y): Issue 228 reported that the package commands cannot record reserved placeholder versions, because they take only a recorded release's version and a package observation must pin a release. The builder proposed FIX-72 and recommended placing it in 0.8.4 as a Codex step under its own small goal. The operator took the recommendation.
- **DESIGN-21 accepted: release lines and channels built in 0.9.0** (2026-10-26 01:33 UTC, decision 01M5TXFKBMQ6V50NJCKQ415JRJ): DESIGN-21's design of release lines and channels, cadence, the disclosure hold and blocked terms was proposed as design/release-lines.md for the operator's acceptance. The operator took the recommendation and accepted it with all five recommendations. The build work orders are placed in 0.9.0, the nightly permission held in the backlog.
- **DESIGN-22 accepted: requested progress reporting built in 0.9.0** (2026-10-26 02:33 UTC, decision 01M5V0XB9GKSPZNS14MPSYEBWC): DESIGN-22's design of requested progress reporting (WATCH-02) was proposed for the operator's acceptance. The operator took the recommendation and accepted it with all four recommendations. WATCH-02 and WATCH-03 are placed in 0.9.0.
- **DOCS-10 accepted: generated revision lines, built in 0.9.0** (2026-10-26 02:34 UTC, decision 01M5V0XFJ8W3KM05TTPY1RY4XQ): DOCS-10's design of generating revision lines from the pins was proposed for the operator's acceptance. The operator took the recommendation and accepted it with shape A and the stated migration scope. DOCS-10's build moves to 0.9.0.
- **EFF-01 accepted: the efficiency adapter, built and reviewed in 0.9.0** (2026-10-26 02:34 UTC, decision 01M5V0XKVDPM2C3YT1JWBE240G): EFF-01's harness-hook design for the opt-in local efficiency adapter was proposed for the operator's acceptance. The operator took the recommendation and accepted it with all four recommendations. EFF-01's build moves to 0.9.0 and keeps its own security review before any enablement.
- **DOCS-09 accepted: the public example built in 0.9.0 with the launch** (2026-10-26 02:56 UTC, decision 01M5V272ZMATNR84N7EMZKG4VM): DOCS-09's design of the public example estate and app was proposed for the operator's acceptance, with the two public repositories created under the operator's authorization. The operator took the recommendation and accepted it with both recommendations. DOCS-09 moves to 0.9.0.
- **FIX-73 joins 0.9.0: the update check bounds its timeout and fails soft** (2026-10-26 03:08 UTC, decision 01M5V2WKAZXAA87DT8SH9A6E08): The status update check can hang about 30 seconds and fail the command instead of failing soft to its cached answer, which cost three CI reruns. The builder proposed FIX-73 and recommended 0.9.0 as a Codex step. The operator took the recommendation.
- **OPS-04 in 0.9.0: settled runs withdraw their warnings; queue cleared now** (2026-10-26 03:20 UTC, decision 01M5V3J54HJ4GCQ149PS0P7PEE): vericairn ask leads with months-old settled-run warnings that crowd real decisions out of the top of the queue. The builder proposed OPS-04 and a one-time cleanup now. The operator took the recommendation: OPS-04 in 0.9.0 and the cleanup now.
- **FIX-74 joins 0.8.4: a worker run cannot write the shared temporary directory** (2026-10-26 08:49 UTC, decision 01M5VPD1PVEFBSS9Y576G1HJ08): A private security report found a worker run can write the user's shared temporary directory (low). The builder proposed FIX-74 and recommended 0.8.4 with FIX-67. The operator took the recommendation.
- **The operator secures the passphraseless CI signing key** (2026-10-26 08:49 UTC, decision 01M5VPD5WSSBNSZ5H3DMMJ4N66): A private security report found the CI signing key is passphraseless and readable by any run. The builder recommended the operator add a passphrase now and consider moving or rotating it. The operator took the recommendation.
- **FIX-74 moves to 0.9.0: the worker-sandbox fix needs careful build and qualification** (2026-10-26 13:38 UTC, decision 01M5W6YX9NFVVNS5TVEFAQT8K7): Building FIX-74 showed it touches the launcher's verified sandbox-roots check and needs a live qualification. The builder recommended taking the decision's 0.9.0 fallback. The operator took the recommendation.
- **DESIGN-33: a product-guided release and upgrade procedure, designed in 0.9.0** (2026-10-26 13:56 UTC, decision 01M5W7ZN7WJ2FWPRPZK17D6FTY): The operator directed that preparing a release be managed and guided by Vericairn, not reconstructed each time from hand-built scripts. The builder proposed DESIGN-33 and recommended designing it in 0.9.0's cycle. The operator took the recommendation.

## Work orders opened or revised

- **FIX-63** (ordinary): Status stays within its deadline on Windows.
- **REL-084** (ordinary): Release 0.8.4: templates, saved prompts, the efficiency Adapter and the builder's tools (revised).
- **FIX-63** (ordinary): Status stays within its deadline on Windows (revised).
- **FIX-64** (ordinary): Release notes render a superseded record only as its current revision.
- **FIX-65** (ordinary): A run whose settlement write failed can be settled later.
- **FIX-66** (ordinary): estate init --release names the release observation it records.
- **FIX-67** (ordinary): A checked push's local reads do not trust derived object data.
- **CI-05** (ordinary): CI steps and jobs have time limits.
- **REL-084** (ordinary): Release 0.8.4: templates, saved prompts, the efficiency Adapter and the builder's tools (revised).
- **FIX-64** (ordinary): Release notes render a superseded record only as its current revision (revised).
- **FIX-65** (ordinary): A run whose settlement write failed can be settled later (revised).
- **FIX-66** (ordinary): estate init --release names the release observation it records (revised).
- **FIX-67** (ordinary): A checked push's local reads do not trust derived object data (revised).
- **CI-05** (ordinary): CI steps and jobs have time limits (revised).
- **FIX-68** (ordinary): Every estate gets the first-party bootstrap.
- **REL-084** (ordinary): Release 0.8.4: templates, saved prompts, the efficiency Adapter and the builder's tools (revised).
- **FIX-68** (ordinary): Every estate gets the first-party bootstrap (revised).
- **FIX-69** (ordinary): A charter revision leaves the release pin's observation current.
- **FIX-70** (ordinary): J04 runs under a controlled clock.
- **REL-084** (ordinary): Release 0.8.4: templates, saved prompts, the efficiency Adapter and the builder's tools (revised).
- **FIX-69** (ordinary): A charter revision leaves the release pin's observation current (revised).
- **FIX-70** (ordinary): J04 runs under a controlled clock (revised).
- **FIX-71** (ordinary): Agent products named and checked.
- **REL-084** (ordinary): Release 0.8.4: templates, saved prompts, the efficiency Adapter and the builder's tools (revised).
- **FIX-71** (ordinary): Agent products named and checked (revised).
- **DESIGN-32** (milestone): Conversation intake: GitHub issues, discussions, pull request comments, project items and advisories, observed, triaged and answered under recorded authority.
- **REL-090** (ordinary): Release 0.9.0: Remote, readable companions on Remote, and quick send of saved prompts (revised).
- **DESIGN-32** (milestone): Conversation intake: GitHub issues, discussions, pull request comments, project items and advisories, observed, triaged and answered under recorded authority (revised).
- **FIX-72** (ordinary): The package commands record reserved placeholder versions.
- **REL-084** (ordinary): Release 0.8.4: templates, saved prompts, the efficiency Adapter and the builder's tools (revised).
- **FIX-72** (ordinary): The package commands record reserved placeholder versions (revised).
- **CODE-12** (ordinary): Release lines and channels on the record and product.
- **CODE-13** (ordinary): Estates follow a release line.
- **CODE-14** (ordinary): Release cadence as a recurring duty.
- **CODE-15** (ordinary): The nightly-only acceptance permission.
- **CODE-16** (ordinary): The disclosure hold, enforced by the public-repository push scan.
- **CODE-17** (ordinary): Configured blocked terms refused in records and generated documents.
- **REL-090** (ordinary): Release 0.9.0: Remote, readable companions on Remote, and quick send of saved prompts (revised).
- **CODE-12** (ordinary): Release lines and channels on the record and product (revised).
- **CODE-13** (ordinary): Estates follow a release line (revised).
- **CODE-14** (ordinary): Release cadence as a recurring duty (revised).
- **CODE-16** (ordinary): The disclosure hold, enforced by the public-repository push scan (revised).
- **CODE-17** (ordinary): Configured blocked terms refused in records and generated documents (revised).
- **CODE-15** (ordinary): The nightly-only acceptance permission (revised).
- **WATCH-02** (ordinary): Requested progress and the typed claim.
- **WATCH-03** (ordinary): Progress surfaces and the overdue signal.
- **REL-090** (ordinary): Release 0.9.0: Remote, readable companions on Remote, and quick send of saved prompts (revised).
- **WATCH-02** (ordinary): Requested progress and the typed claim (revised).
- **WATCH-03** (ordinary): Progress surfaces and the overdue signal (revised).
- **DOCS-10** (ordinary): Revision lines generated from document-revision pins (revised).
- **EFF-01** (ordinary): Qualify an opt-in local token-efficiency Adapter (revised).
- **REL-090** (ordinary): Release 0.9.0: Remote, readable companions on Remote, and quick send of saved prompts (revised).
- **DOCS-09** (ordinary): A public example estate and product, upgraded with every release (revised).
- **FIX-73** (ordinary): The update check bounds its own timeout and fails soft.
- **REL-090** (ordinary): Release 0.9.0: Remote, readable companions on Remote, and quick send of saved prompts (revised).
- **FIX-73** (ordinary): The update check bounds its own timeout and fails soft (revised).
- **OPS-04** (ordinary): Settled runs withdraw their warnings; answered asks close.
- **REL-090** (ordinary): Release 0.9.0: Remote, readable companions on Remote, and quick send of saved prompts (revised).
- **OPS-04** (ordinary): Settled runs withdraw their warnings; answered asks close (revised).
- **FIX-74** (ordinary): A worker run cannot write the user's shared temporary directory.
- **REL-084** (ordinary): Release 0.8.4: templates, saved prompts, the efficiency Adapter and the builder's tools (revised).
- **FIX-74** (ordinary): A worker run cannot write the user's shared temporary directory (revised).
- **REL-084** (ordinary): Release 0.8.4: templates, saved prompts, the efficiency Adapter and the builder's tools (revised).
- **REL-090** (ordinary): Release 0.9.0: Remote, readable companions on Remote, and quick send of saved prompts (revised).
- **FIX-74** (ordinary): A worker run cannot write the user's shared temporary directory (revised).
- **DESIGN-33** (milestone): A product-guided release and upgrade procedure.
- **REL-090** (ordinary): Release 0.9.0: Remote, readable companions on Remote, and quick send of saved prompts (revised).
- **DESIGN-33** (milestone): A product-guided release and upgrade procedure (revised).

## Milestone and release acceptances

- release 0.8.3: "I accept release 0.8.3. It was published before this acceptance and before the Reviewer's review finished (decision 01M5Q4Y34CFCY24W4F3A6ZD2SP); the review passed." (acceptance 01M5Q6W7EPEC223WFA8S2MNH8J, 2026-10-24 15:01 UTC, signed)
- 01M5J4M712S985A7K2Z4PN49VK: "0.8.3 is published (release record 01M5Q7AE5C6VRH8YH9JBP19GQS), the goal's only condition, as the builder's evaluation 01M5R32E7DR678Z10PQGA52H4W found; its Codex work went through the worker." (acceptance 01M5R7ZECAM668ZD63K2WDDPR9, 2026-10-25 00:39 UTC, signed)
- 01M5R8NP92RBJDPJ4JXSH02AMS: "Accepted Codex's CI-05 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5RPT950FH39G3Y1QB8K705H." (acceptance 01M5RPTNRCPSG0JHR36S9H1CAA, 2026-10-25 04:59 UTC, signed)
- 01M5R9X8ZT4GWAK1KF4F5DPY6F: "Accepted Codex's FIX-64 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5RPTT24037WRR947EAKW10C." (acceptance 01M5RPV6SQKR53EQS4025SVNQ0, 2026-10-25 04:59 UTC, signed)
- 01M5RD7S37KR0E4WX24S4EQGS6: "Accepted Codex's FIX-66 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5RPVF44P2JDRX726EQ78JE9." (acceptance 01M5RPVV9S23C7EFP3NSNZSACW, 2026-10-25 04:59 UTC, signed)
- 01M5RDXH58YTNZ0GB259G5G27H: "Accepted Codex's FIX-65 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5RPWG43DYF6TA10VMZD51P8." (acceptance 01M5RPX27F8KQQ47AFDJCJJWVE, 2026-10-25 05:00 UTC, signed)
- 01M5RNBDBYQW2A3XN37FB61TJT: "Accepted Codex's FIX-68 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5RW3EW623V2VEAG7QSY85QT." (acceptance 01M5RW3TNTPW4KDAZQ45CPK5SY, 2026-10-25 06:31 UTC, signed)
- 01M5RR7S2D5XERXW38XFAQS4DS: "Accepted Codex's OPS-02 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5RXKQKA0Q55PE39TNMD1TD0." (acceptance 01M5RXM85PX0N0S71VE6W1HDD3, 2026-10-25 06:58 UTC, signed)
- 01M5RTCY4XYZPRS1HE6XCE8T3X: "Accepted Codex's CODE-10 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5S4P70PDGFS2RS0C04TYPSS." (acceptance 01M5S4PJQYP20BM2B1CE3KHYRT, 2026-10-25 09:01 UTC, signed)
- 01M5RSFJ3WER6F29QJ59BZPQRQ: "Accepted Codex's PROMPT-01 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5SDXBV6QJFXW4EG1WY2T98H." (acceptance 01M5SDXQZHP4P7Z9J2WK5Y1BKV, 2026-10-25 11:42 UTC, signed)
- 01M5RVQDYBQK29PGASHG92F6WM: "Accepted Codex's TPL-01 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5SHRM6S8J2FAGZFZNFRFC8H." (acceptance 01M5SHS2GVMRSMCAHV5ZW0EQ5P, 2026-10-25 12:50 UTC, signed)
- 01M5RYHB7F9V68PXB26Y9HM6CA: "Accepted Codex's CODE-09 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5SNZP152JN1BX4BP54CFN9D." (acceptance 01M5SP03HNDE91PAZBQZTAXJK1, 2026-10-25 14:03 UTC, signed)
- 01M5RYYW4WGB0PNVB4TWSB1X1R: "Accepted Codex's OPS-03 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5STZ4Z0YTCF0G3SE0MN2Y84." (acceptance 01M5STZJZAW24ZS85DFFY4WH6F, 2026-10-25 15:31 UTC, signed)
- 01M5S0JY4XA49HB90WGQNK8895: "Accepted Codex's DOCS-12 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5SZ2B0PE0D2G3MB25DXBZJR." (acceptance 01M5SZ2RFH767TP15SVHCABTFZ, 2026-10-25 16:42 UTC, signed)
- 01M5SG90EPFGDXMWQRZ5XT142D: "Accepted Codex's MCP-02 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5T231X0TG3V2ZP2N9YPA2WJ." (acceptance 01M5T23G5XWKT835ZK1CK95GVY, 2026-10-25 17:35 UTC, signed)
- 01M5SNTC3AKJSBF2PYPXCAWXQ6: "Accepted Codex's FIX-69 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5T3RNHSWCC1JMG58T5GEJWW." (acceptance 01M5T3S5M7RNBASTQ08MFJC4JP, 2026-10-25 18:04 UTC, signed)
- 01M5SMQNNNFX6KR3B4H86F7251: "Accepted Codex's TPL-COMMS-01 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5T6K3RZZ2X4WC1QEB38D8MA." (acceptance 01M5T6KKAWQ29Z9RCS99HMP124, 2026-10-25 18:54 UTC, signed)
- 01M5SNBPRCPZHFCJ7D28VXM3X0: "Accepted Codex's TPL-02 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5TC5R53YAQM5V6QYHWVR5HJ; the work order stays open until the templates are adopted in the first-party estate on 0.8.4." (acceptance 01M5TC64H6F3T2W8FE2M3WJQXZ, 2026-10-25 20:31 UTC, signed)
- 01M5TJ3QY9GZJ41576BMXHHH77: "Accepted Codex's FIX-72 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M5TW2TYRE6X1RNW6S1KJK5WS; the builder's own fix for the reserved records' stale-subject warning is part of the merged change." (acceptance 01M5TW37NDRNGCMVK52KGR91PX, 2026-10-26 01:09 UTC, signed)

## Identity and policy

None in this window.

## Design documents revised

- Catalog revision 31 (`design/catalog.md`): Revision 31: the published Community scope is 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS), with its additions, the Windows status known issue and NuGet run 40102325701.
- CLI and SDK revision 34 (`design/cli-and-sdk.md`): Revision 34: the current published release is 0.8.3; the command table records 0.8.3's estate, work and release commands, and 0.8.2's builder tools as published.
- Vericairn: master design revision 44 (`design/master-design.md`): Revision 45: published 0.8.3 and the estate's 0.8.3 pin, the early publication (decision 01M5Q4Y34CFCY24W4F3A6ZD2SP) and Windows status's deadline as a known issue (decision 01M5QDWET5CS7JD11FS7KBK1WM).
- Platform obligations revision 18 (`design/platforms.md`): Revision 18: profiles and observations for published 0.8.3: release run 40037939339, focused review 01M5Q6D7PJFDMSB6ST7AAVQ7QV, identical libraries on every platform, the estate's 0.8.3 cold start and Windows status's known issue.
- Vericairn roadmap revision 53 (`design/roadmap.md`): Revision 54: 0.8.3 is the current published release with its fifteen work orders accepted; Windows status's read deadline is its known issue, fixed by FIX-63 in 0.8.4.
- Vocabulary: the single register of the estate's terms revision 15 (`design/vocabulary.md`): Revision 15: checks published 0.8.3 and the estate's 0.8.3 pin; the release adds no term, and register-coverage remains undeclared.
- Identify content by the bytes it contains revision 5 (`docs/knowledge/article/content-addressed-bytes.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Derive repeatable views from records revision 6 (`docs/knowledge/article/deterministic-projections.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Compose implementations explicitly at the host revision 7 (`docs/knowledge/article/explicit-composition.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Carry decisions across boundaries as explicit values revision 4 (`docs/knowledge/article/explicit-value-contracts.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Refuse when admission cannot be established revision 6 (`docs/knowledge/article/fail-closed-admission.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Record intent before an external effect revision 5 (`docs/knowledge/article/intent-before-effect.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Keep machine locations in local configuration revision 6 (`docs/knowledge/article/machine-local-settings.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Present runtime facts through observable view models revision 6 (`docs/knowledge/article/observable-presentation-models.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Give clients one application decision boundary revision 4 (`docs/knowledge/article/one-application-boundary.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Keep external implementations behind runtime contracts revision 7 (`docs/knowledge/article/ports-and-adapters.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Make interrupted record batches visible revision 5 (`docs/knowledge/article/recoverable-record-batches.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Revise records by supersession revision 5 (`docs/knowledge/article/revision-by-supersession.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Stand-ins prove the code path, not the conditions a person runs under revision 9 (`docs/knowledge/article/stand-ins-and-real-conditions.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Return expected refusals with structured findings revision 5 (`docs/knowledge/article/structured-refusals.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Why records are the only bookkeeping revision 7 (`docs/knowledge/article/why-records-are-the-only-bookkeeping.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Wrap transport behavior at the boundary that needs it revision 5 (`docs/knowledge/article/wrapping-transport-behavior.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Accepting an exact subject revision 6 (`docs/knowledge/guide/accepting-an-exact-subject.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Checking the exact head before merge revision 6 (`docs/knowledge/guide/checking-the-head-before-merge.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Cold-starting the estate from a fresh clone revision 11 (`docs/knowledge/guide/cold-starting-the-estate.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Freezing a release documentation set revision 5 (`docs/knowledge/guide/freezing-documentation-sets.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Arranging an independent review revision 6 (`docs/knowledge/guide/independent-review.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Organizing feature files without hiding shared responsibility revision 6 (`docs/knowledge/guide/organizing-feature-code.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Reviewing living documents when a release changes their claims revision 6 (`docs/knowledge/guide/reviewing-living-documents.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Scanning before a push revision 5 (`docs/knowledge/guide/scanning-before-push.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Testing interchangeable implementations with one contract revision 5 (`docs/knowledge/guide/testing-interchangeable-implementations.md`): Revised for published 0.8.3 (release record 01M5Q7AE5C6VRH8YH9JBP19GQS): applicability, tracked source refs moved to fb5b497 and the release boundary, with the 0.8.3 changes its guidance names.
- Vericairn roadmap revision 54 (`design/roadmap.md`): Revision 55: carries decision 01M5QDWET5CS7JD11FS7KBK1WM: FIX-63 joins 0.8.4 to keep status within its deadline on Windows.
- Vericairn roadmap revision 55 (`design/roadmap.md`): Revision 56: carries decision 01M5QHX5WAKMJVMY5YR1R5S1WA: FIX-64 to FIX-67 and CI-05 join 0.8.4, and REL-082 is closed.
- Vericairn roadmap revision 56 (`design/roadmap.md`): Revision 57: FIX-68 joins 0.8.4 under decision 01M5RBYNTZQMGMY9YYWPW9R5V0, so every estate gets the first-party bootstrap.
- Vericairn roadmap revision 57 (`design/roadmap.md`): Revision 58: FIX-69 and FIX-70 join 0.8.4 under decisions 01M5SKT0002W3MY5ZKM7QFGT0A and 01M5SKTA81HS35QMRVSTPVVY5Y.
- Vericairn roadmap revision 58 (`design/roadmap.md`): Revision 59: FIX-71 joins 0.8.4 under decision 01M5SP3T91DSBQRHQGR82ZJEAH.
- Vericairn roadmap revision 59 (`design/roadmap.md`): Revision 60: DESIGN-32 designs conversation intake in 0.9.0 under decision 01M5T1092GEMJN6DZ1P739MFYH.
- Vericairn roadmap revision 60 (`design/roadmap.md`): Revision 61: FIX-72 joins 0.8.4 under decision 01M5TBZVQ8KNRSN1HK3RGQZT5Y.
- Release lines and channels revision 1 (`design/release-lines.md`): Revision 1: DESIGN-21's proposed design of release lines and channels, cadence, the disclosure hold and blocked terms, for the operator's acceptance.
- Release lines and channels revision 2 (`design/release-lines.md`): Revision 2: accepted under decision 01M5TXFKBMQ6V50NJCKQ415JRJ with all five recommendations; builds in 0.9.0, nightly permission in the backlog.
- Vericairn roadmap revision 61 (`design/roadmap.md`): Revision 62: DESIGN-21 accepted; CODE-12..14, CODE-16, CODE-17 in 0.9.0 and CODE-15 in the backlog, under decision 01M5TXFKBMQ6V50NJCKQ415JRJ.
- Progress reporting revision 1 (`design/progress.md`): Revision 1: DESIGN-22's proposed design of requested progress reporting (WATCH-02), for the operator's acceptance.
- Revision lines revision 1 (`design/revision-lines.md`): Revision 1: DOCS-10's proposed design of generating revision lines from the pins, for the operator's acceptance; the record-format question in section 9 is theirs.
- Efficiency adapter revision 1 (`design/efficiency-adapter.md`): Revision 1: EFF-01's proposed harness-hook design and safety contract for the opt-in local efficiency adapter, for the operator's acceptance.
- Progress reporting revision 2 (`design/progress.md`): Revision 2: accepted under decision 01M5V0XB9GKSPZNS14MPSYEBWC; WATCH-02 and WATCH-03 in 0.9.0.
- Revision lines revision 2 (`design/revision-lines.md`): Revision 2: accepted under decision 01M5V0XFJ8W3KM05TTPY1RY4XQ with shape A; build and migration in 0.9.0.
- Efficiency adapter revision 2 (`design/efficiency-adapter.md`): Revision 2: accepted under decision 01M5V0XKVDPM2C3YT1JWBE240G; build and security review in 0.9.0.
- Vericairn roadmap revision 62 (`design/roadmap.md`): Revision 63: DESIGN-22, DOCS-10 and EFF-01 accepted; their builds (WATCH-02, WATCH-03, DOCS-10, EFF-01) placed in 0.9.0.
- Example estate revision 1 (`design/example-estate.md`): Revision 1: DOCS-09's proposed design of the public example estate and app, for the operator's acceptance; the two public repositories are created under the operator's October 26 authorization.
- Example estate revision 2 (`design/example-estate.md`): Revision 2: accepted under decision 01M5V272ZMATNR84N7EMZKG4VM; built in 0.9.0 with the launch, DOCS-09 moves there.
- Vericairn roadmap revision 63 (`design/roadmap.md`): Revision 64: DOCS-09 accepted and moved to 0.9.0 under decision 01M5V272ZMATNR84N7EMZKG4VM.
- Vericairn roadmap revision 64 (`design/roadmap.md`): Revision 65: FIX-73 placed in 0.9.0 under decision 01M5V2WKAZXAA87DT8SH9A6E08.
- Vericairn roadmap revision 65 (`design/roadmap.md`): Revision 66: OPS-04 placed in 0.9.0 under decision 01M5V3J54HJ4GCQ149PS0P7PEE.
- Vericairn roadmap revision 66 (`design/roadmap.md`): Revision 67: FIX-74 joins 0.8.4 and the CI-key decision, under decisions 01M5VPD1PVEFBSS9Y576G1HJ08 and 01M5VPD5WSSBNSZ5H3DMMJ4N66.
- Vericairn roadmap revision 67 (`design/roadmap.md`): Revision 68: FIX-74 moves to 0.9.0 under decision 01M5W6YX9NFVVNS5TVEFAQT8K7.
- Vericairn roadmap revision 68 (`design/roadmap.md`): Revision 69: DESIGN-33 (guided release procedure) in 0.9.0's cycle, under decision 01M5W7ZN7WJ2FWPRPZK17D6FTY.

## The release itself

None in this window.

## Other releases recorded in this window

- 0.8.3 candidate (01M5Q4FC0DXRQP9NPXYF472NQK, 2026-10-24 14:19 UTC); signing: unsigned developer build; qualified: linux-x64, osx-arm64, win-x64.
- 0.8.3 published (01M5Q7AE5C6VRH8YH9JBP19GQS, 2026-10-24 15:08 UTC); signing: unsigned developer build; qualified: linux-x64, osx-arm64, win-x64.

## Upgrading

- Migration 01M5Q7F1CTFKPFZYE5Y8MKQ8R4: from release 0.8.2 to release 0.8.3, record format 3 to 3; migrations applied: none; changes: estate/manifest.json pinned to release 0.8.3 with the artifact hashes of release 01M5Q7AE5C6VRH8YH9JBP19GQS.

An estate on 0.8.3 upgrades with `vericairn estate upgrade --to 0.8.4 --by <principal>` run with the 0.8.4 binary once the published release record is in the estate; `--check` shows the plan first.
