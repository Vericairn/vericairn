# Release notes, Vericairn 0.9.0

Prepared by `vericairn docs notes` before the release build, from the estate's records since the end of the notes of 0.8.4 (2026-10-26 14:49 UTC) up to 2026-11-02 12:38 UTC; the records are the source and this page is a rendering. The publication of 0.9.0 is not yet recorded: its commit, workflow run, artifacts by hash and qualified platforms are in the release record the estate writes when the draft is observed and, once published, in the published revision; `limitations-and-platforms.md` of this set states what was known before the build.

## Security

Vericairn 0.9.0 corrects three weaknesses: one in how a Codex worker run is contained, one in how a charter that requires signatures can be revised, and one in when a living document's own revision clears it. None of them lets anyone sign a record without an admitted device or act as another principal.

**A worker run could write the user's shared temporary directory (0.8.4 and earlier; advisory GHSA-rvpw-g4hh-4h9r).** A Codex worker run's sandbox admitted the user's temporary directory, as the run's `TMPDIR`, and `/tmp`. Other processes keep files there, including the folder that holds the runtime's own sockets, so a run could create or change files outside its workspace that another program trusts. In 0.9.0 each run has its own owner-only temporary directory inside its run directory, passed to the harness as `TMPDIR` (`TEMP` and `TMP` on Windows), and Codex's sandbox can write only the workspace, its `.git` and that directory; the shared temporary directory and `/tmp` are excluded. Harness qualification probes the same configuration (FIX-74).

**A charter revision without the root operator's signature could change the estate's policies (0.8.4 and earlier).** Under a charter that requires signatures, a later charter revision was read as current even when the root operator had not signed it, so a revision written by someone with only file access could relax the signature policy and let unsigned records validate. In 0.9.0 a charter revision under required signatures counts only with the root operator's authentic signature from an admitted device; any other revision is reported as a `signature.charter` error and changes nothing, a revision that names another operator is refused (`estate.operator`), and the root operator is always the one the estate's first charter names (FIX-79).

**A document's author could clear it with their own revision (0.8.4 and earlier; advisory GHSA-r3wc-9fxm-qr4r, reported privately by an estate).** A new `document-revision` pin counted as a clearance of the document's own content even when its declaration requires an independent reviewer, and without running the declared checks, so a document gated on independent review, or a release held on it, could be cleared by its author. In 0.9.0 a revision clears only when its author meets the declaration's `clearedBy`, never for `independent`, and the declared checks pass; covered paths still clear only by review, and the release gate uses the same derivation (FIX-84).

Upgrade to 0.9.0 and restart any running worker and daemon. If an estate requires signatures, run `vericairn estate check` after upgrading: a `signature.charter` error names a charter revision that never counted. If a document declared `clearedBy: independent` was cleared only by its author's own revision, review it again.

## What the release contains

The release record of 0.9.0 is written when its draft is observed; until then the accepted work below is what the release contains.

## Work accepted

### FIX-73: The update check bounds its own timeout and fails soft

Result 01M621RF26MTRPFSRYMZW24VR8 (partial), recorded 2026-10-28 20:03 UTC. Implemented FIX-73 and committed 7d86ca3d419c3323a8ad35b58fafb7b338abd910 on work/fix-73, based on 232373b6975a8198995f1ee7c7a9ee0d1636d18a. The shared release check enforces a two-second caller deadline around synchronous state access, source invocation and response reading. It saves the daily attempt before calling the source, releases the state lock before waiting, retains cached answers on failures, observes late faults, and rejects late responses or responses superseded by another attempt or a disabled setting. Cancellation and its cleanup cannot hold the caller on an uncooperative callback. CLI and Workbench disclosure use the setting read inside the bounded check. Added offline regressions and the required docs/changes.md entry. All 43 executed local checks pass and the CLI regressions compile; CLI execution and the three-platform run remain for the builder. The workspace is clean.

Limitations recorded:
- The exit question is not fully established until the builder executes the CLI regressions and the Linux, macOS and Windows run passes on the exact pushed head.
- An implementation that ignores cancellation may remain active after the check stops waiting. It holds no update-state lock during the source call; later completion is checked against cancellation and elapsed time before the cache can be updated.
- The change has not been pushed, merged or published. The estate clone was not changed; only this attempt and its result were recorded through MCP.

Acceptance 01M6AKZW9R93D0SBZKS9PDHXGK pins this result.
Earlier results and attempts: `vericairn work observe FIX-73` (work order 01M5V2XSBVKWMBWJAW7VTV2JAS).

### OPS-04: Settled runs withdraw their warnings; answered asks close

Result 01M622FCKNG0Z0MJBNYANCCSKY (partial), recorded 2026-10-28 20:15 UTC. OPS-04 is implemented and committed locally on work/ops-04 in ae7a34a3 and 8bd08507, with final head 8bd085077d4dab3820268c06e3a50bd64fbdd401. All run-dispatch terminal paths share process-warning selection and the same withdrawal draft, including retained-observation recovery. Opening an estate reconciles outstanding warnings from recorded terminal runs in one journaled batch; repeat opens write no duplicates. Selection preserves unrelated asks, warnings from unfinished runs, settlement review asks, and asks a person has answered, including historical answers. Incomplete ask journals defer cleanup instead of creating competing revisions. Current-decision answer derivation already existed and was retained; new regression coverage verifies current and superseded decisions across all requested open-ask views without an ask revision. The operator guide and docs/changes.md describe both behaviors. The implementation and local validation are complete; CLI execution and three-platform validation on the pushed head remain outstanding under the assignment's constraints.

Limitations recorded:
- Local validation ran on macOS arm64; it does not establish Linux or Windows behavior.
- CLI tests were compiled but not executed. Three-platform CI on the pushed head remains unverified.
- Automatic reconciliation uses ordinary signing and validation authority. Missing signing authority or an incomplete ask batch leaves cleanup pending and visible as a local concern; it does not fabricate a withdrawal.
- Historically answered warnings are preserved even if the current decision no longer lists them. Their displayed state still follows the current decision, and no decision validator was weakened.
- The estate clone was not edited. The only estate records authored by this attempt are its own attempt and this result. No push, merge or publication occurred.

Acceptance 01M6AM0DJGV5VTHWVEKZF82F6R pins this result.
Earlier results and attempts: `vericairn work observe OPS-04` (work order 01M5V3KE4SQQ390N9YBMFCEF87).

### CODE-12: Release lines and channels on the record and product

Result 01M6241GQBYT6YG5H8V325P3J8 (partial), recorded 2026-10-28 20:43 UTC. CODE-12 is implemented and committed on Vericairn/vericairn branch work/code-12 at fc559228890645cb3aeb2a3d6e83df976144aa95, ready for builder review. Release line, channel, endOfSupport and exact backportOf fields and product lines and cadence validation are additive within format 3. Submission preserves and validates metadata; the GitHub provider prints the specified command for each channel, with stable alone marking latest. Generated notes support a line selection and show incoming and outgoing backports. Compatibility, golden fixtures, round trips, negative rules, provider journeys and guide text are included. Local validation passes. The complete work-order outcome remains partial because CLI execution and the required three-platform run on a pushed head are not established under this attempt's constraints.

Limitations recorded:
- The complete acceptance criteria are not yet met: CLI execution and the three-platform run on a pushed head require the builder's permitted workflow. No remote pass is claimed.
- Runtime validation is the focused eight-class release suite listed above, not the entire Runtime test project. The full Records suite was run.
- The both-line generated CLI notes tests are compiled and pending builder execution; record-derived backport rendering passed locally.
- Declarations pinned to earlier readable companion implementations require the ordinary explicit generator update before regeneration; frozen sets and old golden snapshots were not rewritten.

Acceptance 01M6AP0SZDX5DS428VS1AB3TC5 pins this result.
Earlier results and attempts: `vericairn work observe CODE-12` (work order 01M5TXNB3ZGTCBER5QZFP0XH4P).

### FIX-75: Status stays fast as the estate grows, under a CI budget

Result 01M625CAB4034GBGY9N67BK86P (partial), recorded 2026-10-28 21:06 UTC. FIX-75 is implemented and committed on work/fix-75 for builder review. Commits bddfe6b1bbac59efb6eff84c487a9ac23b8305ae and f64e58ff37d5aeac70bb2d712ad79a8098e2ed95 follow base 41348d48bbd5540605558f6d5556b3dadc948edd. Status on the generated 22,416-record estate improved from 46.043 seconds initially and 43.198 seconds repeated to 4.470 and 4.163 seconds on the committed head. Policy evaluation fell from 38.654 seconds to 89 milliseconds. A 15-second budget now fails the ordinary CI test suite if either status call exceeds it. The original implementation failed that budget in a negative control. Full-response comparisons with the original implementation pass after normalizing only the expected implementation and basis provenance digests. Record-set-scoped indexes replace repeated scans; no result is cached across snapshots. Existing assertions and the 30-second client deadline are unchanged. Own-estate performance of the new build and the three-platform run on a pushed head remain unverified because this assignment prohibits accessing the protected home, pushing, and network use. The existing connected runtime answered its own 4,900-record estate in 15.030 seconds. No push, merge, publication or estate-clone edit was performed.

Limitations recorded:
- The assignment's complete exit question remains open: only local generated-estate timing is verified for the new build. The 15-second allowance has not been measured on the slowest Windows runner.
- The connected own-estate baseline was 4,900 records, differing from the assignment prose estimate of about 30,000. No optimized own-estate timing or per-phase own-estate measurement is claimed.
- The full Runtime run preceded the final snapshot-consistency refinement; focused regression tests, full Records tests, the final solution build and the generated budget cover the final source/head as detailed above.
- The final CLI, Daemon and Workbench execution and three-platform CI remain for the builder. No push, merge or publication was attempted.
- Cross-build full-response parity necessarily normalizes implementation provenance and its basis digest; all remaining bytes matched and permanent canonical expectations were unchanged.

Acceptance 01M6AP1C4QGJQTG8NMVY2QXPXH pins this result.
Earlier results and attempts: `vericairn work observe FIX-75` (work order 01M5YYNAZC485X4N1WVRAZT55W).

### WATCH-02: Requested progress and the typed claim

Result 01M625P240SEVBYD33KKKG0YXZ (partial), recorded 2026-10-28 21:12 UTC. WATCH-02 is implemented and committed locally on work/watch-02 at f3aec53890bc7e586c1ecef9e31e301f4413ee6d, based on 232373b6975a8198995f1ee7c7a9ee0d1636d18a. Complete progress requests resolve brief over goal over the matching roster role over estate; omission inherits and absence everywhere preserves voluntary notes. Accurate counts, best-effort percentages, not-measurable claims, an estimate beside a count, and status text are retained through MCP, CLI, API and SDK. Malformed or disallowed values and missing required status are refused without writing or replacing live state. Interim claims remain live state; completion adds only optional fields within record format 3 and remains idempotent. Worker preflight carries its explicitly selected goal into both retained briefs before the run exists. Guides, additive API compatibility notes, round-trip tests and versioned golden fixtures are included. The code and local checks are ready for builder review; the required pushed-head three-platform run is not established because this assignment prohibits pushing and network access.

Limitations recorded:
- WATCH-03 display integration, freshness, cadence monitoring, progress-overdue and the result's retained last claim remain outside this brief. The release-review RequestedProgress display seam remains unavailable; the ordinary worker brief carries the effective request.
- Validation used macOS arm64 and cached dependencies only. The sandbox denied the test wrapper's named-pipe bind, so records and runtime suites were executed directly through their built test DLLs. Restore used a local empty feed with package audit disabled; serialized MSBuild avoided sandbox IPC. Early test-fixture and compile failures were corrected before the final passing runs.
- No new interface member lacks a default implementation: no interface member was introduced. Older runtimes are not assumed to accept newly configured format-3 fields; format-2 voluntary notes retain their existing shape.
- The result is partial solely with respect to the required external validation and acceptance. It makes no merge, publication, three-platform success or independent acceptance claim.

Acceptance 01M6AQZ7PN85YZGPH4F50FKAZJ pins this result.
Earlier results and attempts: `vericairn work observe WATCH-02` (work order 01M5V121BE1QV4FTXQXJJ76X9P).

### CODE-14: Release cadence as a recurring duty

Result 01M6AN1DWPW9V0RK4VWKKWMZGQ (partial), recorded 2026-11-01 04:14 UTC. CODE-14 is implemented and committed locally on work/code-14 at 59c4ef8fd7b5443e67506ee0e90c51208cff07d5, based on d3713463821dce8b2043a5216f7a8ad3e2a0c52c. Due product cadences appear under operator waits in status and progress and as context in the shared release gate, with exact policy and occurrence references, line and target version. A valid pinned cadence cannot create a release hold, even at refuse level; unrelated policies retain their enforcement. Future, completed and explicitly ended occurrences add no cadence context. Evaluation creates no record, object, signing or provider dispatch, and adds no submission, acceptance or publication path. Added runtime, CLI and Workbench regression coverage and updated the operator guide, record-format guide and API compatibility notes. All 643 selected local tests passed on the final committed head, and CLI and Workbench test projects compiled without warnings. The CLI suite and the required three-platform run on a pushed head remain for the builder because this assignment prohibits CLI execution in the sandbox, push and network access. No push, merge or publication occurred. The working tree is clean. The local patch artifact is artifacts/code-14.patch and is not committed.

Limitations recorded:
- This result is partial with respect to the full review contract: no CLI execution or three-platform result is claimed.
- Status and progress retain the existing recorded-time policy basis; the release gate uses submission time. Current release candidates and open releasePlan work orders supply target versions. Candidate metadata supplies the line; otherwise it defaults to current. When no target exists, the view explicitly says target version not declared rather than inventing one.
- The standard dotnet test host could not bind its sandbox IPC socket. Local test evidence comes from the repository's existing in-process xUnit console runner. Each suite ran as one blocking subprocess and its summary was read after it finished; no running log or output file was polled.
- Only selected runtime, record and pure Workbench regressions were executed locally. The complete solution and platform suites remain builder validation.

Acceptance 01M6AVW6AQRCX39APNB38M9XQD pins this result.
Earlier results and attempts: `vericairn work observe CODE-14` (work order 01M5TXP4PNEJKY6JKGDMFAH85K).

### CODE-13: Estates follow a release line

Result 01M6ANNZA9663Y72Y3H3SKKGVG (partial), recorded 2026-11-01 04:25 UTC. CODE-13 is implemented and committed locally on work/code-13, ready for builder review. Optional manifest vericairn.line and vericairn.channel default to current/stable without rewriting existing manifests. Initialization accepts both choices. Newer-release findings and upgrade plans require the followed line and an equal or more stable channel; changing line or channel requires an explicit request. CLI and Workbench notices respect the selected estate, and CLI status uses the serving runtime's selection. An expired LTS line produces manifest.line-unsupported naming the supported lines known from matching publications and current product declarations. Nondefault migration pin descriptions distinguish channel-only moves so interrupted upgrades resume once. Guides, additive API compatibility notes and manifest companion version 2 fixtures are included. Local validation passed. The CLI suite and the three-platform run on a pushed head remain unperformed under this assignment's sandbox, no-push and no-network constraints; acceptance is not claimed.

Limitations recorded:
- Validation is local macOS arm64. The entire Records suite and focused runtime/SDK and Workbench classes passed; this is not a claim that every runtime, daemon, CLI or Workbench test passed.
- The shared latest-release cache and verified dependency observations retain current/stable meaning because they do not establish other line metadata. Other lines require their published release records in the estate. Explicit nondefault initialization from source is supported; verified dependency initialization cannot relabel a current/stable dependency.
- Older serving runtimes cannot enforce the new optional upgrade selection fields. Upgrade the serving runtime before relying on line/channel options, as documented.
- Development validation initially encountered build-server/telemetry sandbox issues and one new CLI test used the wrong harness signature. Those were resolved; the final solution build and all executed final checks passed. Validation notes are retained here rather than in committed build artifacts.
- Self-review only. Concurrent changes must be integrated and the exact resulting head validated by the builder before acceptance.

Acceptance 01M6AXM3DQE4ZGWMC15DKD8C7G pins this result.
Earlier results and attempts: `vericairn work observe CODE-13` (work order 01M5TXNQTA4AAB8AVTEBJJF977).

### WATCH-03: Progress surfaces and the overdue signal

Result 01M6ASEMYMNWEBW77BB7KN0761 (partial), recorded 2026-11-01 05:31 UTC. WATCH-03 is implemented and committed locally on work/watch-03 in commits c81b89e153d2432786e07b409d28c07257203e38 and 9c6b9984f6161371191ac360a5c4899aa163262e. Requested claims show type, value, status, time and freshness beside independent worker liveness on human status, progress, Workbench work views, API and SDK attempt/run reads, and the read-only MCP work_progress_get tool. Missing or stale requested reporting derives progress-overdue attention, clears with a fresh claim, and never requests a run stop. Results retain the last claim as format-3 context, including Workbench result review, without supplying evidence or acceptance. The release-review requested-progress seam resolves and pins the inherited request. The implementation and focused checks are ready for builder review; full qualification remains incomplete because the sandbox denies transport prerequisites and the three-platform pushed-head run is outside this assignment's permissions.

Limitations recorded:
- This is a partial validation result, not acceptance, completion evidence or release qualification. No push, merge or publication was performed. Only this attempt and its result are authored in the estate.
- Canonical status JSON and MCP estate_status intentionally remain portable record-derived documents. Current local claim freshness and liveness are separate status response fields, human status, progress/work observation arrays and the dedicated read-only reader.
- Shared status/progress/UI files changed: src/Vericairn.Runtime/Service/EstateService.cs; src/Vericairn.Runtime/Service/EstateService.Progress.cs; src/Vericairn.Runtime/Service/EstateService.Runs.cs; src/Vericairn.Protocol/AttentionDerivation.cs; src/Vericairn.Protocol/CommunicationPresentation.cs; src/Vericairn.Presentation/ProgressView.cs; src/Vericairn.Workbench/Documents.cs; src/Vericairn.Workbench/ShellViewModel.cs. Coordination with CODE-14 is through the derived attention list; no worker stop policy is changed.
- Other shared files changed: src/Vericairn.Protocol/Contracts.cs; src/Vericairn.Protocol/OpenApiDocument.cs; src/Vericairn.Protocol/ReleaseReview.cs; src/Vericairn.Runtime/Service/ReleaseReviewPreparation.cs; src/Vericairn.Records/Estate/ProgressRequests.cs; src/Vericairn.Records/Estate/RecordCompanionGenerator.cs; src/Vericairn.Records/Estate/WorkView.cs; src/Vericairn.Records/Types/ResultType.cs; src/Vericairn.Daemon/ApiRoutes.cs; src/Vericairn.Daemon/RequestScope.cs; src/Vericairn.Mcp/ToolRegistry.cs. The estate manifest is untouched.
- Guide changes are confined to docs/software.md, docs/mcp.md and the WATCH-03 section in docs/api-compatibility-0.9.0.md. Record-reader implementation version 5 requires an explicit update for declarations pinned to older implementations; prior fixture versions were retained.

Acceptance 01M6AXMH5QB2C0GJZ45VVA249M pins this result.
Earlier results and attempts: `vericairn work observe WATCH-03` (work order 01M5V12E7CAZM3CNE2GTB5QM8Z).

### GUIDE-01: Community build guides: the source inventory, guide template and deterministic generator with private previews

Result 01M6BE6Q1Q8PY8S0F9S0R1QHXH (partial), recorded 2026-11-01 11:34 UTC. GUIDE-01-R2 is implemented and committed locally on work/guide-01-r2 at ec559e4080dbcbf111a7469593e523847cf365c9, based on 099e41670b36ea99e74f93205f216b8e7b3f7cce. Guide content validation now examines pinned blocks and mapped owning-row cells instead of whole source files; unquoted catalog parent links and contract examples do not prevent generation, while the existing active-content and traversal refusals remain. Malformed delimiter-like rows under owning column contracts produce source, heading and line findings, including tables not yet mapped in the index. Applicable boundary blocks, role contracts and row caveats are quoted once in full in Reserved acts and hard lines; other sections refer to those quotations. Identity shows aliases in ordinal order, including an explicit Foundry identifier retained under a kind-qualified key. Generator identity advances to vericairn.guides/2; the input and thirteen-section template conventions remain supported. Documentation explains exact private batches and complete union-of-keys review under the unchanged 128 MiB bound. All 133 relevant tests pass on the committed head. No interface gained a member. Full private-catalog generation and the required three-platform run on an eventual pushed head remain unperformed, so this is a claim ready for builder review rather than completed acceptance.

Limitations recorded:
- This result is partial against complete acceptance because full private-catalog rendering and the required three-platform pushed-head run remain outstanding.
- The builder should run the complete CLI classes Vericairn.Cli.Tests.GuideCommandsTests and Vericairn.Cli.Tests.DocsTests locally and in the required matrix, alongside GuideGeneratorTests, GuidePreviewTests, GuideSourceValidationTests and GuideTemplateTests and the ordinary full suite.
- The existing assertion refusing quoted parent traversal remains intact. Unquoted parent links are admitted as source prose; admitted non-traversing relative links are displayed inertly and never resolved. Active markup and unsafe schemes in pinned blocks and mapped cells still fail generation.
- Exact hashes and byte comparisons establish the chosen source spans, not their reviewed completeness or applicability. The builder must preserve all applicable role, family, table and entry boundaries when preparing its private source index.
- Generator version changes mark earlier manifests due. Generate new private output directories and retain each batch's exact selection; do not combine independent bundles into an unchecked collection.
- No publication authority, living-document clearance, implementation qualification or acceptance is created by this result. No interface gained a member, so there are no members without default implementations to report.

Acceptance 01M6C3TM3ZDNPS5SRYWZJY2ENQ pins this result.
Earlier results and attempts: `vericairn work observe GUIDE-01` (work order 01M5HRF5A5QA9SME2DR9B4BE0M).

### DOCS-10: Revision lines generated from document-revision pins

Result 01M6CDH5N970A0QR60BYS8QKKN (completed), recorded 2026-11-01 20:41 UTC. DOCS-10 is implemented on work/docs-10 in four commits, ready for the builder's review. Converted documents generate their opening revision history from their pins, newest first. Ordinals derive from recorded time and id, concurrent successors remain a mergeable history, and the stored revision integer is advisory. The additive authored-revisions-v1 mode excludes only the revision payload while retaining its markers; old whole-file pins and record bytes remain valid. Strict conversion preserves historical wording in exact record-bound metadata and refuses missing, duplicate or ambiguous matches. Pinning, guided decisions, template adoption, context retrieval, living-document checks and the design-revisions policy use the new behavior. The CLI options, API fields, compatibility note and migration guidance are included. No estate document was migrated.

Limitations recorded:
- This result establishes the assigned implementation and local checks, not acceptance, publication, the complete runtime suite, the complete CLI suite or a three-platform run.
- Git can leave textual conflicts inside a generated payload. After resolving authored-content conflicts, docs pin --render-template reconstructs the history from all merged records. The merge journey verifies this behavior.
- Conversion requires the exact current whole-file pin when a prior pin exists. Missing, duplicate, ambiguous or displaced hand-written history is refused for the builder to inspect.
- Readers that ignore the additive metadata keep their existing field shapes; verifying new authored-content hashes requires hash-mode support. Old whole-file verification remains unchanged.
- Five platform-dependent custody tests were skipped because their required platform facilities were unavailable. These are included in the records suite summary.
- Development runs exposed summary escaping and new-field fixture coverage mismatches; both were corrected before the final passing runs.

Acceptance 01M6CP1QGGXKT4NWSEFV8ZPV1P pins this result.
Earlier results and attempts: `vericairn work observe DOCS-10` (work order 01M5V12V4NYZQ092K52TXTVNGZ).

### REMOTE-02: Remote: attention and work on the phone

Result 01M6CE77WDSND7942313DYMTJ9 (partial), recorded 2026-11-01 20:53 UTC. Implemented REMOTE-02 on work/remote-02: principal-scoped attention acknowledgment shared through the runtime without signing or creating a record, retained as visible attention in Remote, Workbench and human status; phone work inspection with admitted briefs, attempts, results, runs and separate agent claims and worker observations; read-only communication settings; persistent observations labeled cached with advancing age, no offline acknowledgment queue and no offline detail requests. Local commits are ready for builder review. Required transport, CLI, mobile and pushed-head three-platform validation and independent review remain outstanding.

Limitations recorded:
- The exit criteria are not fully validated: local-network transport, the CLI suite, mobile builds, the pushed-head three-platform run and independent review remain builder work. A result is a claim to inspect, not acceptance.
- Builder CLI classes: AskViewsCommandTests, AttentionJourneyTests, AttentionStateJourneyTests, CommunicationContinuityJourneyTests, CommunicationJourneyTests, CommunicationMcpTests, CommunicationQualificationTests, DaemonJourneyTests, DivergenceJourneyTests, DocsNotesRevisionTests, DocsPassCommandTests, DocsTests, DutyTransportCommandsTests, EstateBootstrapTests, EstateInitCommandTests, EstateLineCommandsTests, GoalJourneyTests, HandoffJourneyTests, JourneyTests, LivingDocumentJourneyTests, McpTests, PackagesCommandTests, PairingJourneyTests, PolicyAdoptionCommandsTests, PolicyCommandsTests, PortabilityJourneyTests, ProgressJourneyTests, ReleaseCadenceCommandsTests, ReleaseCheckTests, ReleasePlanningCommandsTests, RuntimeStartTests, SigningJourneyTests, SoftwareJourneyTests, UpdateNoticeJourneyTests, UpgradeJourneyTests, WorkProgressCommandsTests, WorkerConfigurationJourneyTests, WorkerJourneyTests, WorkspaceRootJourneyTests.
- The historical assignment fallback preserves exact admitted scope and result records; it explicitly labels current acceptance as unobserved when the runtime's current work projection does not establish it.
- The source patch is retained only as ignored build output under artifacts; no artifact or validation-notes document was committed. Validation notes are in this result.

Acceptance 01M6CWPSJ9Q8EFV3CMW20AWA2Z pins this result.
Earlier results and attempts: `vericairn work observe REMOTE-02` (work order 01M6B31DG2TAZDH594JFBERT2W).

### EFF-01: Qualify an opt-in local token-efficiency Adapter

Result 01M6CEVN8AN435Y3M1N4FBWX45 (partial), recorded 2026-11-01 21:04 UTC. Implemented EFF-01's local-lossless adapter and current documentation in two local commits on work/eff-01: 01c51d1977b7a8d24f0094cfdc78fbc8e6aaed01 (implementation and tests) and 1ff5aeda90aafaedd13d6813f6487322a22ebb74 (documentation). The adapter remains disabled by default and requires separate operator machine permission naming the adapter and launchers, plus a resolved execution selection. It preserves protected native request bytes, reduces only explicitly eligible verified optional originals, fixes forwarding to the launcher's provider, preserves authentication lines without recording them, and records and displays eligible byte counts. Original retrieval survives source changes and adapter removal through verified retained run context. The implementation is committed and the working tree is clean; qualification is incomplete because this sandbox refuses loopback listeners, the brief excludes running the CLI suite here, and the independent security review and live harness qualification remain outstanding. No machine enablement, push, merge or publication was performed.

Limitations recorded:
- Outcome remains partial because actual-socket assertions, CLI execution, three-platform qualification and independent security review are outstanding; in-memory forwarding tests do not replace those checks.
- Eligibility is deliberately narrow: an explicit vericairn.representation/1 envelope in a recognized tool result, matching an optional original already retained for the run. Existing ordinary tool output and brief rendering are not automatically relabelled, so selecting the adapter can yield no reduction.
- Codex's fixed sign-in route refuses the hook before intent; its supported model-provider hook requires an already provisioned API-mode login. This attempt changed no login, plan, spending selection or machine configuration.
- Listener startup failure keeps the direct configuration. Reducer failure forwards the original once and disables further reduction. Uncertain transport outcomes are not retried; a worker/process failure uses ordinary run reconciliation rather than restarting a conversation.
- Counts cover completed observed request-body writes and classified eligible UTF-8 text. Startup fallback observes no direct traffic and therefore reports zero observed bytes. Tokens, billing and model fit remain unknown. Forwarding is stopped before the final measurement snapshot.
- Original recovery depends on retaining the run's existing context snapshot and valid source records locally; exporting records alone does not export those snapshots. Loopback binding does not authenticate other processes on the same machine.

Acceptance 01M6CWQ960XC57XV467NGHFDZJ pins this result.
Earlier results and attempts: `vericairn work observe EFF-01` (work order 01M5V1382PKKJCV67NA5QX17WC).

### DOCS-14: Guides for running Vericairn with people only, with people and agents, and with agents only

Result 01M6CFRV60AKNY7Q8NFF60908G (completed), recorded 2026-11-01 21:20 UTC. DOCS-14 is committed on Vericairn/vericairn work/docs-14 at de7c95353de07dce4c673279e00a96a1e74091ea, ready for builder review and the three-platform run. Added complete people-only, people-and-agents and agents-only guides with setup, daily work, roles, authority, exact-hash review and acceptance, and executable local examples. Each guide states the human responsibilities and labels unbuilt mode fields, refusals, reserved-act delegation, checkpoints and attention as planned. The README and operator guide link all three; the maintained release-document declaration includes them. No source code, API interface or test class changed.

Limitations recorded:
- Command execution was validated on this macOS workspace only. The guides specify Bash, including Git Bash for Windows, and Python 3; PowerShell syntax and the alternative NuGet executable spelling were not exercised.
- The walkthroughs rehearse principal and record flows with disposable file-custody identities. No live coding-agent launch, hardware-presence prompt, remote pairing, real independent-agent inspection, release submission or publication was qualified. Real worker prerequisites are linked and described separately.
- No source, interface or test changes were necessary, so no API compatibility member or new test class needs declaration. The three-platform run and builder acceptance remain outstanding. No push, merge or publication occurred; the only writes to the connected estate were this contributor's attempt and result.
- No efficiency-adapter implementation or configuration changed. Its operator opt-in and separate review before the planned release remain outside this documentation assignment. No public security-fix disclosure was added.

Acceptance 01M6CVXMYNG9ZYY0V58YS7RPA0 pins this result.
Earlier results and attempts: `vericairn work observe DOCS-14` (work order 01M61W38ZPN4BJYG1EM6H4KYC6).

### REMOTE-03: Remote: decisions and acceptances on the phone

Result 01M6CXJ1B7C7C7A3MNG1D33QXD (partial), recorded 2026-11-02 01:21 UTC. REMOTE-03's shared phone decision flow is implemented and committed locally on work/remote-03. Commits 464eec763c2eaf2bde49530723bf8193403d091e and 32ad89fc91599d8c3a419854cf904d8cb2e5f37b add full ask/result/candidate review, phone-device signing through the existing SDK, exact revision and hash checks before preparation and immediately before signing, durable offline drafts, observed submission outcomes, and read-only reconciliation of retained original identities. Repeated taps cannot resubmit an attempted original. A later revision gets a separate blank draft only after the earlier submission has an observed outcome; uncertain originals continue to hold that subject history. Missing or unsupported material points to the desktop. Defer uses the existing attention acknowledgment; request changes explains the desktop route. The Remote guide and individual interface-compatibility sections are updated. The final non-mobile solution builds without warnings and all 40 Remote tests pass. Four new local-network journeys compile, but the transport run failed its sandbox socket prerequisite before the behavior assertions. The full exit question remains unqualified pending independent review, transport execution, mobile builds and the three-platform run on a subsequently authorized pushed head.

Limitations recorded:
- This result is a claim for independent review, not an acceptance or a claim that the three-platform check passed. Real local-network behavior, mobile builds and physical devices remain unverified here.
- Format 3 requires an exact ask answer to carry a document change set. The current phone API cannot discover or verify the estate commit basis. This implementation therefore requires the operator to enter the exact estate commit, choose the stake and explicitly confirm no design document changes; base and head use that supplied commit with empty pins. It never invents a commit or substitutes the software pin. Decisions changing documents or lacking that basis go to the desktop.
- The reader supports complete recorded material and verified UTF-8 text evidence within 128 records and 256 KiB. Missing evidence, binary or oversized material, location-only evidence and specialized document or policy readers make the phone ineligible and point to the desktop.
- Currency is checked through the existing read APIs before preparation and immediately before signing. This shared-library change adds no server-side atomic subject comparison or new record-authoring policy.
- Submission storage serializes atomic replacements within the single phone app process. It retains prepared identities before signing and never queues retries. Runtime recovery problems remain uncertain and require desktop reconciliation even if the exact signed record can be read.
- Device authentication before signing belongs in the iOS and Android heads and is the builder's follow-up; this flow makes no biometric-presence claim.
- The normal dotnet test orchestration could not bind its IPC pipe in the sandbox, so the passing Remote suite ran through its standalone test assembly. Transport tests retain their original failing prerequisite. Validation notes are kept in this result, and ignored build output was not committed.

Acceptance 01M6D6XAJ7RVNXTDRC3DKM3APF pins this result.
Earlier results and attempts: `vericairn work observe REMOTE-03` (work order 01M6B31PTG0G59F7WMNNKZBC8B).

### POLICY-05: Estate policies in Workbench and Remote: the inspector, attention and review handoff

Result 01M6D8CG9AV8R9YJP1DRNWTYCP (partial), recorded 2026-11-02 04:30 UTC. POLICY-05's Remote slice is implemented and committed on work/policy-05 at f632ed9af81255f0cd19a873c490f26f3e79aa50, ready for builder review. Runtime attention now carries the evaluator's exact policy and recurring-duty conditions, basis digest, recorded time and origin, with separate occurrence identities, completion proofs, exception history and operator preparation. Remote preserves the original selection beside refreshed policy history and prepares context through the existing desktop handoff read after refreshing authority and basis, then checks both again. This policy flow exposes no approval or record-writing command. Cached histories and packages keep their own age and send or queue nothing offline. Documentation and additive API compatibility notes are included. Local checks pass; the required pushed-head three-platform run is not performed because push and network use are prohibited.

Limitations recorded:
- This result establishes a locally committed Remote slice, not work acceptance, merge or release readiness. The builder must review the exact head and obtain the required three-platform evidence under separate authority.
- The existing handoff endpoint prepares a context package. Remote does not deliver a desktop task or claim that Workbench opened it. The exact original selection is explicit beside the package; desktop review and any action require a fresh basis and authority there.
- Older runtimes can omit typed policy attention. That omission does not establish an empty policy evaluation; the guide calls for an updated runtime. Missing or unavailable detail remains explicit, and cached authority grants nothing.
- The daemon journey is an in-process mapped-route test, not a live network or device qualification. Full CLI execution, mobile builds and the pushed-head three-platform run remain builder work.
- Only the authorized attempt and this result were authored through the estate MCP server. No other estate record or estate clone file was changed by this work.

Acceptance 01M6DFVNGACFWPZEJ56QF4DMXV pins this result.
Earlier results and attempts: `vericairn work observe POLICY-05` (work order 01M5D50RY1W9T0STK77JBY045K).

### GUIDE-02: Community build guides: the public repository's preparation, contribution flow, request form and publication workflow

Result 01M6DD4J6SE2WAVH2DV1N42MFG (partial), recorded 2026-11-02 05:53 UTC. GUIDE-02 local preparation is implemented and committed on work/guide-02. Commit 565cc057 adds the deterministic allowlisted catalog exporter, public renditions and manifests, shared request form and validator, pure canonical-issue/account-entry reconciliation, retained retirement pages, and read-only destination comparison with runtime and CLI coverage. Commit 7ddd64b3be84ede9e0ed69f3ec27beaaca5cd0aa documents the input contracts, commands, contribution flow and publication/recovery decisions. The local Release build passed without warnings and 190 focused runtime cases passed with no failures or skips. The repository is ready for builder review, but the full readiness exit question remains unverified because CLI behavior and the three-platform run on a separately pushed head were not performed. No repository was created, nothing was pushed, merged or published, and no site or request route was activated.

Limitations recorded:
- Full readiness is not established until the builder completes the named CLI tests and the three-platform run on the exact pushed head. Only focused runtime tests ran here; platform heads and the entire solution suite were not run.
- The native issue form cannot atomically enforce uniqueness or append to an existing issue. A qualified importer or authorized manual observer must provide complete attributable observations; authorized maintainers separately apply duplicate links and closures. These pure functions perform no provider writes or source verification against a live host.
- Automated coordination detection and an authorized moderation workflow are not implemented by this local reconciliation. Bot/app and unverifiable observations are excluded; real provider completeness, attribution and integrity review remain qualification responsibilities.
- Automated disclosure scans cover known private shapes and explicitly supplied restricted text. Independent review must clear the actual source selection, restricted names, prose and rights before any public use. Synthetic fixtures establish the preparation contract, not clearance of a real catalog.
- Reconciliation output contains private account keys and notice intent and is not a public bundle. A notice checkbox enables no subscription or message. Repository creation and exact-content publication remain separate operator decisions.
- No interface gained a member, so there are no new members without default implementations. The GUIDE-02 API additions are documented under their own compatibility heading.

Acceptance 01M6DJ0HDYX0C3E14RQM7XMAF0 pins this result.
Earlier results and attempts: `vericairn work observe GUIDE-02` (work order 01M5HRF8J0R5G8RD2V80XXFPYP).

## Work recorded, not yet accepted

Current results of this period with neither an acceptance of their exact revision nor of their work order's current revision: claims to inspect, not part of what the release is known to contain.

### FIX-76: A run whose observation is marked settled but whose settlement is unrecorded can still be settled

Result 01M6AM26CZ7B9BCPQG2G4GX9SP (completed), recorded 2026-11-01 03:57 UTC. A run whose retained observation was marked settled before its settlement record failed to write could never be settled: worker settle refused it, since the record might sit on a branch or copy not yet merged. RunSettleRequest gains RecordsChecked (--records-checked), the requester's confirmation that no branch or copy holds a settlement; with it the settlement is recorded from the retained observation, its evidence saying so, and without it the run stays unresolved with a message naming the check. Startup never confirms, a recorded settlement is never written twice, and a missing fact stays unresolved. Merged in pull request 244.

Limitations recorded:
- Built and self-reviewed by the builder, as decision 01M620576H8VVN511EQM2AC2XC directs.
- Run 01M5KH84BP3WXKTW3Y7RG78CD3 settles, and its two warnings clear, only once the estate runs a release carrying the fix; the worker is the pinned release.

### CODE-17: Configured blocked terms refused in records and generated documents

Result 01M6ASMNQCTT7HGVJQ1T4SRRWD (completed), recorded 2026-11-01 05:34 UTC. An estate-policy rule kind, vericairn.estate/blocked-terms, declares a list of blocked terms. While a current policy declares it, a record that would introduce a term is refused at the central author step on every path (any occurrence in a new record; only a new occurrence in a revision), and release notes, record companions, refreshed template regions and new template instances that would introduce one are refused; terms match whole, ignoring case and Unicode compatibility forms. Structured model selectors the worker records, and the declaring policy, are exempt. Evaluating the rule reports records written after the policy began that hold a term, without repeating it. Merged in pull request 252.

Limitations recorded:
- Built and self-reviewed by the builder: design/release-lines.md section 13 names blocked terms security work for the builder.
- The estate gains the protection when it runs a release with the change and declares its list in an estate policy.

### CODE-16: The disclosure hold, enforced by the public-repository push scan

Result 01M6AXMQNB9P55V8ZYTSH9RJVH (completed), recorded 2026-11-01 06:44 UTC. Work orders gain an optional disclosure field whose only value is held. While a work order is held and the release that carries it is not recorded published, a checked push to a repository recorded as public is refused before anything is pushed when its commit messages or added lines contain the work order's key as a whole word or a distinctive phrase of its title, intent or scope, whoever authored the commits; merges inherit it through their required scanned push. The git adapter supplies the push's text without running external diff, text-conversion or signature tools, and an adapter that cannot supply it cannot push publicly while a hold is in force. Merged in pull request 259.

Limitations recorded:
- Built and self-reviewed by the builder: design/release-lines.md section 13 names the disclosure hold security work for the builder.
- Pushes made outside vericairn changes push are not checked.

### DESIGN-34: Operating modes: people only, people and agents, and agents only

Result 01M6AY0ER2T7FX6ESNDAK44BW9 (completed), recorded 2026-11-01 06:51 UTC. design/operating-modes.md proposes three operating modes declared on the charter (people, people-and-agents, agents), the invariants every mode keeps (attribution, a human root operator no agent can override, intent before effect, results as claims, immediate human revocation), four delegable reserved acts for agents-only estates by bounded, expiring grants (accept-release, publish, spend, use-credential) and the acts never delegable, setup per mode, runtime checks and status, mode changes, records within format 3, journeys, the build's work orders and five decisions for the operator.

Limitations recorded:
- Proposed; the operator decides section 13 and accepts the design before any build is placed.

### FIX-77: Building Vericairn from source sends no build telemetry

Result 01M6B4467YWFGTDSF55ZM73K66 (completed), recorded 2026-11-01 08:37 UTC. Avalonia brings in Avalonia.BuildServices, whose AvaloniaStats target sent anonymous build statistics before each compile of the repository. tools/BuildTelemetry.targets, imported by the src and tests Directory.Build.targets after the package's targets, replaces it with an empty target for everyone who builds Vericairn; every workflow that runs dotnet also sets AVALONIA_TELEMETRY_OPTOUT=1; BuildTelemetryTests fail if any effective target can call the package's task or a building workflow lacks the opt-out; the README says building sends nothing. Merged in pull request 262.

Limitations recorded:
- Built and self-reviewed by the builder, as decision 01M6AYM73A3CX0T5CV3NDYW5VQ directs.
- Builds made before this change, on any machine, in CI or in agent runs, have already sent their statistics; nothing here can recall them.

### FIX-78: An authoring operation that can wait on a presence prompt observes for longer than the prompt

Result 01M6B44DSYF99HRRT1V206NKX0 (completed), recorded 2026-11-01 08:38 UTC. records new stopped observing after 30 seconds and reported an unknown outcome while a Secure Enclave signature with presence may wait five minutes (issue 251), because ClientOperation.Deadline did not list NewRecord. Every async operation that may write and has no explicit timeout is now observed for at least ClientOperation.EffectDeadline, six minutes, a minute longer than ClientOperation.PresenceWait, which the Secure Enclave and secret-service stores share through DeviceKeyStores.PresenceWait. Longer named deadlines and reads are unchanged; a timeout still reports an unknown outcome and nothing retries; the synchronous SDK timeout is six minutes instead of five. Merged in pull request 263.

Limitations recorded:
- Built and self-reviewed by the builder, as decision 01M6AYMNFCQ8G0WV4NAJQC5R6K directs.
- Whether an operation may write is judged as it starts; a staged operation whose effect begins later, such as pairing, gives its own timeout.
- Issue 251 closes when 0.9.0 ships; until then the SDK's per-operation timeout remains the workaround.

### REMOTE-01: Remote's shell: iOS and Android apps sharing Workbench's presentation, pairing and the home

Result 01M6B9YFAEH8AB3S38JREHX4F0 (completed), recorded 2026-11-01 10:19 UTC. Remote's first step. src/Vericairn.Remote is the phone client's Avalonia library: RemoteViewModel pairs through the SDK's RuntimeConnections and shows the selected estate's Attention, Work, Products, Nodes and Handoffs with its connection state and freshness in the shared state language, deciding and changing nothing and saying plainly what is not built yet; first use explains the companion role. The iOS and Android heads host it from src/Vericairn.Mobile.slnx, built in CI's remote-ios and remote-android jobs as developer builds. A phone's device key stays on it: in the Secure Enclave on iOS (custody ios-secure-enclave), in Android Keystore on Android (android-keystore), an owner-only file on the iOS simulator; connections are excluded from backups. Every area, input and command has a screen-reader name, text wraps and follows the phone's text-size setting, and the view binds the brand's measured tokens. Merged in pull requests 264 and 265.

Limitations recorded:
- Built and self-reviewed by the builder, as decision 01M6AYMED1JMQR78ZDW6DRC8GZ directs.
- No real-device qualification: the Secure Enclave and Android Keystore stores have not run on a device; the operator qualifies Remote on a real phone.
- Android was compiled locally against the workload's reference assemblies and built only in CI, since installing the Android SDK would accept its license for the operator.
- Text follows the phone's text-size setting as Remote starts; a change applies the next time it opens.
- The shared libraries are not trim-safe, so the heads trim only the platform's assemblies.

### DESIGN-33: A product-guided release and upgrade procedure

Result 01M6BAM22QBPJ5CP9D5VAXWXPS (completed), recorded 2026-11-01 10:31 UTC. design/release-procedure.md proposes procedures as declared templates of ordered steps (owner role, reserved act, preconditions and done conditions as record predicates, action, recovery and named traps); a release run as a goal whose plan names the procedure and the carrier, with each step's state derived from records; the conductor running the non-reserved steps one per call and stopping at the operator's, recorded as operator requests whose exact commands appear only when the step is next; the software pack's release and estate upgrade procedures with the traps 0.8.1 to 0.8.4 met, a withdrawal branch and estate extensions; surfaces, records within format 3, journeys, the build's work orders (PROC-05, PROC-06, PROC-07, OPS-05, with DOCS-11 and GOAL-06) and five decisions for the operator.

Limitations recorded:
- Proposed; the operator decides section 12 and accepts the design before any build is placed.

### DESIGN-31: The relay: paired devices such as Remote reach an estate's runtime from any network, end-to-end authenticated, hosted and self-hostable

Result 01M6BATA7EQFA8MYS31P676RTX (completed), recorded 2026-11-01 10:34 UTC. design/relay.md proposes a relay that joins a runtime's outbound registration with a paired device's connection and forwards bytes it cannot read, the device and runtime running the local-network profile's mutual TLS and signed requests end to end; relay addresses that identify no estate; runtime-signed relay tickets of at most 24 hours, revoked at once with the device; notifications as wakes with no estate content, delivered through the platforms' push services once the Apple Developer Program is decided; the relay profile under the authorization model DESIGN-29 shares; one released relay program, hosted and self-hostable under the editions' rules, with what it owns, receives and how a customer exits; connection paths on the surfaces; threats with their tests; the revisions needed in identity-and-connectivity.md and remote.md; and five decisions for the operator.

Limitations recorded:
- Proposed; the operator decides section 12 and accepts the design before the revisions it lists are applied and any build is placed.
- Wakes depend on the Apple Developer Program decision.

### DESIGN-06: Signed history: checkpoints for unsigned records, removed records and the dates authors claim

Result 01M6C1TPXMXASTJJJW214EHFX1 (completed), recorded 2026-11-01 17:17 UTC. design/signed-history.md proposes signed history checkpoints, a record type within format 3 whose inventories list every record first witnessed and whose predecessors form a graph git merges join; under required signatures an unsigned record counts only when an anchoring checkpoint by the root operator or a human administrator lists it; a witnessed record that is missing or altered is an error, settled by restoring its bytes or by acknowledgement, with check-time warnings for the unwitnessed tail; a table of what a claimed time may decide, closings before revocations, rotations and narrowing grants, and grants read as they stood; who signs, cold start with an expected checkpoint, explicit adoption and cost at 100,000 records; and five decisions for the operator.

Limitations recorded:
- Proposed; the operator decides section 16 before the revisions it lists are applied and any build is placed.
- The charter rule of section 6 is FIX-79, recorded separately as the builder's security work.

### DESIGN-07: Standing agents: scheduled and event-driven agent work that packs contribute per domain

Result 01M6C1VM7JN3GHF2NFJQSSHSPJ (completed), recorded 2026-11-01 17:17 UTC. design/standing-agents.md proposes standing roles as a template kind packs contribute, granting nothing and naming the authority each effect needs; a standing agent as a stewardship goal whose plan names the role, subject, triggers, caps and an expiry, turned on, paused and stopped through the goal lifecycle; schedule, duty, event, condition and request triggers producing occurrences with stable keys derived from records; one occurrence per call in Community and unattended dispatch on GOAL-05's scheduler in Standard and Pro; outputs as possibilities, asks, branch results, operator requests and duty results, none accepted, adopted or published by its agent; guardrails for outside text, sources, authority, promises and security findings; caps, runaway and duplicate prevention; and five decisions for the operator.

Limitations recorded:
- Proposed; the operator decides section 15 before the revisions it lists are applied and any build is placed.
- Default caps are proposals; issue resolution and customer service wait for DESIGN-32's build.

### DESIGN-23: Editions and their services: Standard for one person, Pro for teams, every paid service self-hostable at its edition

Result 01M6C1WHGZ3TAEJQV05JY5ZGVW (completed), recorded 2026-11-01 17:18 UTC. design/editions.md proposes the edition boundary: a coordinated program acts on a goal only when its sponsor is covered, so Standard licenses every coordinated capability for one named person's work and Pro adds only seats, coordination across sponsors, organizational automation and cross-estate administration, while Community keeps everything and reads, exports and steps every record a paid program writes; each hosted service as Vericairn's operation of a released program with its self-hosted path; which team features need a shared live runtime; authentication on every host, with no human principal's key on a host its owner does not operate and the commercial account never an estate identity; entitlement evidence signed once and verified offline, gating only new starts of paid programs; the three exceptions and their paths; the agent support tier; moving between hosts and leaving each edition without loss; and five decisions for the operator.

Limitations recorded:
- Proposed; the operator decides section 16 before the revisions it lists are applied; the work orders wait for M4's offers, and nothing is priced, sold or issued.
- Prices and legal terms stay with the operator's decisions at M4.

### DESIGN-29: MCP over the network: the Streamable HTTP transport and its authorization, hosted and self-hosted, for MCP-03

Result 01M6C1XEQEHHD78FXCMJDPQZNK (completed), recorded 2026-11-01 17:18 UTC. design/mcp-network.md proposes Streamable HTTP served at /mcp by the daemon on its listeners, each tool call judged as its API operation for the requesting device; one authorization model in which every request comes from one admitted device of an agent or service principal, with a signed mode for Vericairn-aware clients whose requests and records carry their own device's signature, and a token mode in which the runtime is its own OAuth authorization server, issuing short-lived tokens for a token device an administrator approves with a one-time code; a remote tool set no wider than stdio, with the operator queue and every reserved act kept off MCP; a public endpoint of the owner's own or an MCP gateway in the relay program that never terminates TLS, both hosted and self-hostable; one new device profile and no bearer secret in any record; threats with their tests; and five decisions for the operator.

Limitations recorded:
- Proposed; the operator decides section 15 before the revisions it lists are applied and any build is placed.
- The protocol facts of section 4 are verified by MCP-03 against the revision it targets.

### DESIGN-30: Search for agent context: keyword search over records, documents and knowledge as a recorded selection method, for CONTEXT-02, and a semantic search Adapter contract

Result 01M6C1YCJ3HN0PHV7ACVC0254N (completed), recorded 2026-11-01 17:19 UTC. design/search.md proposes a machine-local keyword index in Community over records, pinned documents and knowledge, each table row its own passage, with a deterministic ranking method, freshness checked against record-set and document-set hashes before every answer, and one segment per audience filtered before ranking, counting and excerpting; search as a recorded selection method whose candidates enter a context package only as optional sources pinned by exact hash, never as instructions; a selection record kept in the package and the run's records, reviewable and replayable; private and shared estates; a semantic search Adapter contract that is off by default and names no model provider, with a hosted route only by the operator's informed opt-in; measures on the estate's own questions with budgets; and five decisions for the operator.

Limitations recorded:
- Proposed; the operator decides section 17 before the revisions it lists are applied and CONTEXT-02 is placed.
- Budgets and the recall floor are proposals, not measured.

### DESIGN-32: Conversation intake: GitHub issues, discussions, pull request comments, project items and advisories, observed, triaged and answered under recorded authority

Result 01M6C1ZAWQHGG7D19DGFMKGMXR (completed), recorded 2026-11-01 17:19 UTC. design/conversation-intake.md proposes a foundation contract for conversations implemented first by the GitHub provider; subscriptions on repository resources; one observation per item revision holding a content hash and metadata but none of the author's words, with deduplication, edits, deletions and moves; outside text kept only in machine-local custody with declared retention and erasure; polling first and always, webhooks later as hints; triage into asks, backlog work orders, possibilities, reply drafts and operator requests, with untriaged items as required attention and item text reaching agents only as labeled untrusted data; the estate's own service principal and GitHub App, never the operator's login; replies as operator requests or under an operator-issued converse grant with mechanical checks; embargoes for advisories and security reports; threats with their tests; and five decisions for the operator.

Limitations recorded:
- Proposed; the operator decides section 18 before the revisions it lists are applied and any build is placed; no App or credential is created and no grant issued.
- GitHub API facts relied on are verified by the build.

### CODE-11: vericairn support bundle: a redacted diagnostic bundle the user inspects before sharing

Result 01M6C20HPKSWSBZBE23AJT9PWP (completed), recorded 2026-11-01 17:20 UTC. vericairn support bundle gathers what support needs and prints all of it; --out writes the same bytes to a new file and never replaces one, and nothing is sent anywhere. It holds the version and build, platform, which runtime served it, the estate's pin and record count, status counts by state, the estate check's counts with each finding's severity, code and location, the runtime log's recent error lines, and whether the local-network profile and each native launcher are configured. It leaves out record bodies, titles and text, the estate's and principals' names, network addresses and finding messages. Every string is redacted by the estate check's own shapes, now shared as SecretShapes: credentials in the formats it knows, values under secret-named keys and absolute machine paths, with the home, temporary, working and Vericairn directories named by role and a redacted section counting what was removed. Merged in pull request 269.

Limitations recorded:
- Built and self-reviewed by the builder, which keeps CODE-11's redaction as its own work.
- Redaction recognizes the shapes the estate check knows; a secret in an unknown format inside a log line could pass, which is why the bundle is shown in full before any sharing, always the user's separate act.

### DESIGN-24: The Foundry contract and the first software Foundries: SaaS/Web Application, CLI and Software Library, with the npm and OCI Adapters

Result 01M6C2S8282Z304PNRJ0S5BMKJ (completed), recorded 2026-11-01 17:33 UTC. design/foundry-contract.md proposes a Foundry as a declaration plus planner code that reads an authorized snapshot without network, files or secrets and computes a hashed plan comparing what it intends, what it applied and what is observed; apply by the runtime alone as one recorded operation, refusing a changed basis and never repeating a step; the eight outcome operations; templates pinned, process profiles and procedures extended without losing a base step, Adapters and Providers resolved through the registry and only observing while planning; a declarative client contribution contract; packages bundled before M4 and NuGet packages at M4; the SaaS/Web Application, CLI and Software Library Foundries in three slices; read-only npm and OCI Adapters and Providers that run no scripts or images; additive records within format 3; and five decisions for the operator.

Limitations recorded:
- Proposed; the operator decides section 16 before the revisions it lists are applied and any build is placed.
- Decision 1 asks to bundle the contract in 0.10.x, ahead of the M4 placement an earlier decision made.

### DESIGN-25: The web domain: the web Domain Pack, Website, Documentation Site, Web Application and Commercial Website Foundries, the openapi and webhook Adapters and hosting Providers

Result 01M6C2T3JYC0EJ7SXSPX7KEPSK (completed), recorded 2026-11-01 17:34 UTC. design/web-domain.md proposes a site or web application as a product whose release carries a site package with a manifest of path hashes and probes, built by the product's own CI and checked by the web Domain Pack; deployment as a reserved effect, its intent recorded as an operation, performed by the operator with the host's own tools and settled by anonymous reads of the declared origins, with drift shown and never corrected; the Website, Web Application (extending DESIGN-24's SaaS/Web Application Foundry), Documentation Site and Commercial Website Foundries on the Foundry contract; read-only openapi and webhook Adapters; hosting Providers that prepare and observe but never deploy, hold credentials or create accounts; reserved acts; three slices; additive records within format 3; and five decisions for the operator.

Limitations recorded:
- Proposed; the operator decides section 16 before the revisions it lists are applied and any build is placed.
- Host behavior is to be qualified per host; numeric bounds are proposals.

### DESIGN-26: Books, book series and world building: their Domain Packs, Book, Book Series, Publishing Imprint, World Bible and Fiction Universe Foundries, EPUB, PDF and DOCX Adapters, book distributors

Result 01M6C2TZMASP6T879J8JJ43KV4 (completed), recorded 2026-11-01 17:34 UTC. design/books-domain.md proposes worlds, series and books as related products joined by an optional product ip field with set-in, member-of and adapts relations later domains share; bibles and chapters as template instances, and media by hash in estate objects or an append-only media store; the World Bible, Book Series, Book, Publishing Imprint and Fiction Universe Foundries on the Foundry contract unchanged, in three slices; DOCX, EPUB and PDF Adapters on one manuscript model with a pinned offline typesetting engine, and a book-build release provider that reuses release submit and the operator's acceptance; an edition-release procedure whose distributor uploads, identifier purchases, pricing and rights acts stay a person's in every mode; distributors that prepare packages and field sheets without network calls in slice 1, and opt-in generated media with provenance and consent; an assistance record generated from records, with the person answering each platform's disclosure question; additive records within format 3; and five decisions for the operator.

Limitations recorded:
- Proposed; the operator decides section 15 before the revisions it lists are applied and any build is placed.
- Distributor facts are to be qualified as dated target profiles; this is not legal advice.

### DOCS-13: Agent frameworks over MCP: guides for LangGraph, CrewAI and AutoGen, their harness-matrix entries, and a page for agent builders

Result 01M6C8TKZ7MT7TDAJFFN49B7XW (completed), recorded 2026-11-01 19:19 UTC. Guides for LangGraph, CrewAI and AutoGen agents over MCP: each connects the framework's MCP client to vericairn mcp serve, lists the tools, reads a brief and records an attempt and a result signed by the agent's own admitted device, each step run as written against a scratch estate on Vericairn 0.8.4 and on the 0.9.0 development build. A page for agent builders relates memory, reflection, orchestration, tools, retrieval, human-in-the-loop and guardrails to Vericairn's records and reviews, with the shared scratch-estate setup replayed command by command. The agent matrix names the three frameworks as non-claims until an estate records a qualification; each guide states what was not run. Merged in pull request 272.

Limitations recorded:
- Built and self-reviewed by the builder.
- Rerun the guides against the published 0.9.0 before its release; no qualification record is made for the frameworks.

### DOCS-09: A public example estate and product, upgraded with every release

Result 01M6CECGN4T0RBWT6PSHKK74RK (completed), recorded 2026-11-01 20:56 UTC. The public example pair. Vericairn/vericairn-example-app holds tally, a tiny .NET command line with tests on Linux, macOS and Windows and a release workflow that builds a draft from an exact commit. Vericairn/vericairn-example-estate is a real estate made with 0.8.4's estate init --release for a fictional organization, with demonstration principals and keys. It carried two work orders end to end: Codex runs through the worker under a goal, an independent reviewer that returned TALLY-01's first attempt when Windows miscounted text outside ASCII, checked pushes, pull requests and merges, the operator role's acceptances, and tally 0.1.0 built, reviewed, accepted and published by the operator, after which the goal was evaluated and accepted. Generated registers for work, decisions and releases read as Markdown on GitHub, the README indexes the loop by domain, and a check workflow cold-starts the estate on Linux, macOS and Windows. The product README (pull request 274) and both holding pages link to it.

Limitations recorded:
- Built and self-reviewed by the builder.
- The estate is on 0.8.4; its first upgrade, by the release checklist's step, comes with 0.9.0's publication.
- Run records name each run's harness, model and effort, as the product records for every run (decision 01M6C915HA6YS52ZGDXWGEKVTJ).
- Findings from building it are proposed as FIX-80 (estate init line endings) and noted for later: changes push and worker run disagree about a checked-out branch, and a work order closes only with a work-order review and acceptance.

### FIX-80: A new estate keeps its records' bytes on every platform

Result 01M6CPJ21FHC6RJ22YFMMRN2AF (completed), recorded 2026-11-01 23:19 UTC. A new estate keeps its records' bytes on every platform. estate init and estate bootstrap add '* text=auto eol=lf', '*.json text eol=lf', '*.md text eol=lf' and the bootstrap scripts' LF lines to .gitattributes, and '.vericairn/' to .gitignore, appending only missing lines, so a clone made where Git converts line endings (Git for Windows' default) keeps every record, object and document LF and passes the estate check. The receipts name both files, and the guides tell older estates to refresh and renormalize once. Merged in pull request 276.

Limitations recorded:
- Built and self-reviewed by the builder.
- An estate created before this fix needs the one-time refresh and renormalization the guides describe.

### FIX-82: A worker run whose supervising process died can settle

Result 01M6CRTNW3DJG8FSBZGT95PVHE (completed), recorded 2026-11-01 23:58 UTC. A worker run whose supervising process died can settle. worker stop, worker settle and startup recovery settle it as known-failure, exited without observation, only when this service is not supervising it, nothing was observed for longer than the intent's stall limit, the recorded supervisor process is gone and the harness's whole boundary is gone (its process group on Linux and macOS, its job on Windows). A process that exists and could be the recorded one, or cannot be inspected, counts as running, so a reused or uninspectable process id can delay a settlement but never cause one. It is charged the usage observed up to its last heartbeat, each unit clamped to its cap and its cap for a unit not observed; its audits are marked unverified and a stop is recorded as requested, never contained. Settling frees the branch and keeps the workspace and its uncommitted files for the next attempt. The guides warn that a foreground run must outlive its shell and point to runtime start with worker run --detach. Merged in pull request 277.

Limitations recorded:
- Built by a builder subagent under the builder's direction and self-reviewed by the builder.
- On Linux and macOS a harness can outlive its dead worker, and nothing delivers a stop to it; the run settles only after its process group exits, and the guides say to end it.
- Currency is charged at its cap, because the live state never stores a price.
- The branch is not fetched into the registered clone, since that step needs the dead process's in-memory audit.

### FIX-81: Codex workers keep running when the installed Codex moves past the baseline

Result 01M6CSJSP81HY14FEB0B9M4BPX (completed), recorded 2026-11-02 00:12 UTC. Codex workers keep running when the installed Codex moves past the baseline. The Codex launcher ships three baselines, each checked against its own effective feature inventory: 0.159.0-alpha.12.1, 0.163.0-alpha.10.2 (the desktop app's build) and 0.163.0 (the npm release a pinned install can use, which reports the same inventory). 0.163's automatic access-program selection is switched off for every run, which 0.159 ignores; its other new defaults add no tool or extension and are admitted as passive. A refusal names each enabled feature the launcher does not admit, the baseline's admitted list it was checked against and the shipped baselines; a qualified other version is checked against the newest baseline's features with its exact inventory pinned by its probe digests. Codex 0.163's platform-named probe scratch directory is admitted, still only as an empty directory without links. The guides say how a desktop app update affects workers and give the pinned-install route. Merged in pull request 278.

Limitations recorded:
- Built and self-reviewed by the builder.
- The live run covers macOS and the desktop app's exact build; 0.163.0 shares its reported inventory and was not run live.
- The desktop app will move past 0.163 again; release preparation checks the version current then, and a later version needs a release or a human qualification.
- The passive classification of 0.163's new defaults rests on Codex's own feature descriptions.

### FIX-83: An estate's registers stay valid across an upgrade whose generator output changes

Result 01M6CYYQQN9HPC6DRMFCBA4XZN (completed), recorded 2026-11-02 01:46 UTC. An estate's registers stay valid across an upgrade whose generator output changes. Each register generator keeps its earlier implementations (IDocumentGenerator.EarlierImplementations): the current renderer with the record fields added since then left out and its own identity in the input digest. estate upgrade rewrites a generated member only when its file equals, byte for byte, what an earlier implementation renders for the same sources, embedded input digest included; anything else stays document.generated-edited and is never overwritten. --check lists the members it would rewrite, applying writes them through the upgrade's batch journal with the migration record, which names each register's count and the implementations involved, and a repeat or a resumed upgrade rewrites nothing. The operator, install and software guides say what an upgrade rewrites. Merged in pull request 282.

Limitations recorded:
- Built by a builder subagent under the builder's direction and self-reviewed by the builder.
- Only strict generators with earlier versions on record are covered; legacy generated regions and template regions still need docs regenerate.
- The next implementation bump must add its predecessor to the earlier-implementation table.

### REMOTE-11: The phone signs a decision only with its owner's presence, from a basis the runtime supplies

Result 01M6DCB4YZSN5P84A8XAP4DNFH (completed), recorded 2026-11-02 05:40 UTC. The phone signs answers and release acceptances only with its owner's presence, from a basis the runtime supplies. Pairing creates a second phone key, the presence key, in the Secure Enclave (iOS, user presence) or Android Keystore (per-use authentication with a strong biometric or the device credential), admitted as a separate device of the same person with presence required and used only to sign records; the request key, which signs every request without asking, is unchanged and never signs a decision. A phone paired earlier adds its presence key with a new pairing code for the same person; a phone without one cannot sign a decision and offers Add presence key. The runtime refuses a record prepared on one device and signed by another unless the signing device's record states presence required. GET /v1/decisions/basis supplies the estate repository's head commit and the design documents changed since it, so the phone shows the basis under Estate basis instead of asking for a commit; the runtime rechecks it when it prepares and when it writes the record, and the desktop's decision route uses the same basis. The simulator's owner-only file custody claims no presence and signs no decision. The Remote, API, CLI, operator and platform guides and the 0.9.0 API compatibility list say which devices enforce presence. Merged in pull request 284.

Limitations recorded:
- Built by the builder and self-reviewed by the builder.
- Presence on a real iPhone or Android phone has not been observed; the iOS and Android builds pass in CI, and real-device presence stays unqualified until it is observed on a device.
- The presence requirement in a device's record is what the phone declares at pairing under the administrator's code; the runtime does not yet verify platform key attestation, so it trusts that declaration.

### FIX-74: A worker run cannot write the user's shared temporary directory

Result 01M6E456PDV6MW7N7Z7P6X93VK (completed), recorded 2026-11-02 12:36 UTC. Worker runs use a private temporary directory. Each run gets an owner-only temporary directory under its run directory, set as TMPDIR (TEMP and TMP on Windows) for the harness, while Vericairn's MCP server keeps the worker's own; Codex's sandbox admits the workspace's .git and that directory as writable roots and excludes the shared temporary directories. Dispatch and harness qualification build the same configuration through one shared step, so recorded qualifications keep matching. Built in the builder's local-only security clone under the no-disclosure rule and published in the hardening pull request at release preparation. Merged in pull request 288.

Limitations recorded:
- Built and self-reviewed by the builder; the Reviewer principal's security review covers it before 0.9.0 is accepted.
- The runtime's own sockets still live in the user's temporary directory; moving them is further hardening, not part of this fix.

### FIX-79: A charter that requires signatures changes only by the root operator's signed revision

Result 01M6E45PZT20EFTFX3W7D2A66Q (completed), recorded 2026-11-02 12:36 UTC. Charter revisions count only when the root operator signs them. Under a charter that requires signatures, a later charter revision counts only with the root operator's authentic signature; any other revision raises a signature.charter error and changes nothing, a revision that names another operator raises estate.operator, and every reader of the root operator uses the estate's first charter. Built in the builder's local-only security clone under the no-disclosure rule and published in the hardening pull request at release preparation. Merged in pull request 288.

Limitations recorded:
- Built and self-reviewed by the builder; the Reviewer principal's security review covers it before 0.9.0 is accepted.
- Deleting a signed required revision from a clone is covered by DESIGN-06's checkpoints, not by this fix.

### FIX-84: A document revision clears only as its declaration allows

Result 01M6E466V0N29CH25TMVYB2D0T (completed), recorded 2026-11-02 12:36 UTC. A document revision clears only as its declaration allows. A newer document-revision counts as a clearance only when its author meets the declaration's clearedBy, never for independent, and the declared checks pass; covered paths clear only by review; every surface uses the corrected derivation, so the release gate stays held until a qualifying clearance. Reported privately by an estate (advisory GHSA-r3wc-9fxm-qr4r); built in the builder's local-only security clone under the no-disclosure rule and published in the hardening pull request at release preparation. Merged in pull request 288.

Limitations recorded:
- Built and self-reviewed by the builder; the Reviewer principal's security review covers it before 0.9.0 is accepted.
- The operator guide's sentence on pins as clearances is corrected in 0.9.0's release documentation.

## Decisions

- **FIX-75 joins 0.9.0: status stays fast as the estate grows, under a CI budget** (2026-10-27 15:10 UTC, decision 01M5YYJQ112MG17HYAMVEJDH9F): The estate check's status step ran into its 30-second deadline under 0.8.3 and failed 9 of the last 40 runs. FIX-63 in 0.8.4 cuts status to about 12 seconds on the same estate, but its cost still grows with the estate. The builder proposed FIX-75 and recommended 0.9.0 as a Codex step. The operator took the recommendation.
- **0.8.4 ships as reviewed; the notes-window sentence is corrected in the next set** (2026-10-27 15:10 UTC, decision 01M5YYKV6P054QZN9K5WM3ME32): The Reviewer's review of the 0.8.4 candidate passed with one documentation warning: three frozen guides name 0.8.2's recorded end for the notes window. The builder recommended accepting the candidate as reviewed and correcting the sentence for the next release. The operator took the recommendation.
- **The estate check that failed at the 0.8.4 acceptance's merge was the status timeout; the 0.8.4 upgrade corrects it** (2026-10-28 16:59 UTC, decision 01M61Q7XJZPB5ZEGMD4W53BP29): After merging the operator's 0.8.4 acceptance (estate pull request 508), its checked-change action observed the estate's main run at merge commit 57eb63d fail and asked how to correct it. The failure was the status step's 30-second timeout under 0.8.3, which decision 01M5YYJQ112MG17HYAMVEJDH9F already addresses. The builder recommended rerunning the failed runs and correcting the cause with the 0.8.4 upgrade. The operator took the recommendation.
- **TPL-02 moves to 0.9.0: the estate adopts its templates early in that cycle** (2026-10-28 17:29 UTC, decision 01M61RYW42P395M5BATMJWGF4E): TPL-02's templates shipped in 0.8.4 and its result was accepted; its other deliverable, the adoption of the templates across about forty of the first-party estate's documents, needed the estate on 0.8.4. The builder recommended moving TPL-02 to 0.9.0 and adopting early in that cycle, so 0.8.4's release plan can close. The operator took the recommendation.
- **0.8.4's first dispatch is abandoned; the guides get the documentation pass and the set is frozen again** (2026-10-28 17:45 UTC, decision 01M61SVTKBEJ5BPZTQ6YBVX1Z7): 0.8.4's first candidate dispatch froze maintained guides that still described 0.8.3. The builder recommended abandoning the dispatch, applying the per-release documentation pass, freezing the set again and submitting anew. The operator took the recommendation; the corrected candidate was later submitted, reviewed, accepted and published.
- **DESIGN-34 and DOCS-14 join 0.9.0: guidance for humans only, humans and agents, and agents only** (2026-10-28 18:23 UTC, decision 01M61W18DCCH2KTEAXJRX9ZFTM): Vericairn had no design or guide for running an estate with people only, with people and agents, or with agents alone. The builder recommended DESIGN-34, a design of the three operating modes, and DOCS-14, a guide for each, in 0.9.0. The operator took the recommendation.
- **Vericairn's Azure resources move to its own subscription** (2026-10-28 18:23 UTC, decision 01M61W1EKNK75B8FNT350PFF7X): Vericairn's two domain registrations, two DNS zones and two Static Web Apps sat in the operator's shared Azure subscription. The builder recommended a Vericairn subscription in the same tenant and moving the six resources into it, as Modularyn's session had done for Modularyn. The operator took the recommendation, asked the builder to handle the move at once, and asked for any residue the move leaves to be cleaned up.
- **FIX-76 joins 0.9.0: a run whose observation is marked settled but whose settlement is unrecorded can still be settled** (2026-10-28 19:35 UTC, decision 01M620576H8VVN511EQM2AC2XC): FIX-65's later settlement refuses a run whose retained observation was marked settled before its settlement record failed, which is exactly the case of run 01M5KH84BP3WXKTW3Y7RG78CD3, still unsettled with two warnings open. The builder recommended FIX-76 in 0.9.0, built by the builder. The operator took the recommendation.
- **FIX-77 joins 0.9.0: building Vericairn sends no build telemetry** (2026-11-01 07:01 UTC, decision 01M6AYM73A3CX0T5CV3NDYW5VQ): Avalonia.BuildServices, pulled in by Workbench's UI framework, sends anonymous usage data on every build from source. The builder recommended FIX-77 in 0.9.0, built by the builder. The operator took the recommendation.
- **Remote's build: REMOTE-01 to 03 in 0.9.0, the rest in 0.9.1** (2026-11-01 07:01 UTC, decision 01M6AYMED1JMQR78ZDW6DRC8GZ): Remote had a design and no build work orders. The builder recommended five REMOTE work orders, with REMOTE-01 to 03 as 0.9.0's launch slice and REMOTE-04, REMOTE-05, READ-05 and PROMPT-02 in a 0.9.x, a REMOTE key family, and a later decision on the Apple Developer Program. The operator took the recommendation.
- **FIX-78 joins 0.9.0: presence-capable writes outwait the presence prompt** (2026-11-01 07:02 UTC, decision 01M6AYMNFCQ8G0WV4NAJQC5R6K): Issue 251 reported that records new stops observing after 30 seconds while a Secure Enclave presence signature may wait five minutes. The builder recommended FIX-78 in 0.9.0, built by the builder. The operator took the recommendation.
- **DESIGN-34 accepted: three operating modes, with agents-only under bounded delegation** (2026-11-01 16:12 UTC, decision 01M6BY51FK5AXQK0R06M3HQD9Q): The operator accepted design/operating-modes.md with the five recommendations of its section 13. Its build (ESTATE-02, ID-04, OPS-06) awaits a release placement; DOCS-14 writes the guides in 0.9.0 with unshipped parts labelled planned.
- **DESIGN-33 accepted: releases run as goals on declared procedures** (2026-11-01 16:12 UTC, decision 01M6BY59Z58NQ98WH39031T6DA): The operator accepted design/release-procedure.md with the five recommendations of its section 12: PROC-05, PROC-06 and OPS-05 in 0.9.1; PROC-07 with DOCS-11 in 0.9.2, whose own release runs on the procedure as GOAL-06.
- **DESIGN-31 accepted: the relay forwards bytes it cannot read** (2026-11-01 16:13 UTC, decision 01M6BY5KQD1CYW8MAM47T1R27J): The operator accepted design/relay.md with the five recommendations of its section 12: REMOTE-06 and REMOTE-07 in 0.9.2; notification wakes (REMOTE-08) once the Apple Developer Program is decided.
- **The REMOTE work-key family is approved** (2026-11-01 19:16 UTC, decision 01M6C8NZF2N0RE53NEJKRM3CAC): The operator approves the exact work-key-families definition with a REMOTE family added, so Remote's work orders, REMOTE-01 to REMOTE-08, follow the estate's declared key families.
- **The public example estate is published with its run records as the product writes them** (2026-11-01 19:22 UTC, decision 01M6C915HA6YS52ZGDXWGEKVTJ): The operator took the recommendation: the example estate is published with its records, including the run records that name each run's harness, model and effort.
- **FIX-79 is placed in 0.9.0 under a no-disclosure rule** (2026-11-01 19:23 UTC, decision 01M6C91CRQAGP880Z60FSBXZ2R): The operator took the recommendation: FIX-79 goes in 0.9.0, kept local until 0.9.0's release preparation and described only in its documentation set, with the Reviewer principal's security review before acceptance.
- **DESIGN-23 accepted: Standard is Pro for one person, and every service has a self-hosted path** (2026-11-01 19:23 UTC, decision 01M6C91KKDGDWVW4WWCMHXJDZ5): The operator accepted design/editions.md with the five recommendations of its section 16. Its build (ID-08, ESTATE-03, ID-09, OPS-09, CODE-18, REMOTE-09, DOCS-16) waits in the future-work backlog for M4's offers; nothing is priced, sold or issued.
- **DESIGN-30 accepted: keyword search selects pinned context and never grants authority** (2026-11-01 19:23 UTC, decision 01M6C91T8RFE02FJVGDECKNS1D): The operator accepted design/search.md with the five recommendations of its section 17: CONTEXT-02 (keyword search in Community) in 0.9.1, the first 0.9.x release after acceptance; CONTEXT-03 and CONTEXT-04 in the future-work backlog.
- **FIX-80, FIX-81 and FIX-82 are placed in 0.9.0, built by the builder** (2026-11-01 22:23 UTC, decision 01M6CKC95M8EBE3MRXRPSN79AJ): The operator took the recommendations: FIX-80 (a new estate keeps its records' bytes on every platform), FIX-81 (Codex workers keep running when the installed Codex moves past the baseline) and FIX-82 (a worker run whose supervising process died can settle) go in 0.9.0, each built by the builder.
- **DESIGN-06 accepted: history checkpoints, anchoring, and what a claimed time may decide** (2026-11-01 22:23 UTC, decision 01M6CKCGVFN67GT24703NW01AQ): The operator accepted design/signed-history.md with the five recommendations of its section 16. Its build (ID-05, ID-06, OPS-07, DOCS-15) is placed in 0.9.2, and the first-party estate adopts checkpoints as an operator step after its upgrade to 0.9.2.
- **DESIGN-07 accepted: standing agents are stewardship goals whose outputs people dispose of** (2026-11-01 22:24 UTC, decision 01M6CKCRMAJEAJ9KF3P3A6KEC7): The operator accepted design/standing-agents.md with the five recommendations of its section 15. GOAL-07, GOAL-08, ID-07, RUN-04 and PROC-08 are placed in 0.9.3, the release after 0.9.2, carried by a new REL-093; GOAL-09 waits with GOAL-05 for M4; OPS-08 starts with the duty carrier on demand once the estate runs 0.9.3.
- **FIX-83 is placed in 0.9.0, built by the builder** (2026-11-02 00:24 UTC, decision 01M6CT8SNBZDZZHQF3ZQEAYM45): The operator took the recommendation: FIX-83 goes in 0.9.0, built by the builder, so an estate whose registers 0.8.4 generated passes its check after upgrading to 0.9.0.
- **DESIGN-29 accepted: MCP over the network, served by the daemon under one authorization model** (2026-11-02 00:24 UTC, decision 01M6CT910VB30CMPKAZXBG8GND): The operator accepted design/mcp-network.md with the five recommendations of its section 15. MCP-03 is placed in 0.9.2; MCP-04, REMOTE-10 and DOCS-18 in 0.9.3.
- **DESIGN-32 accepted: conversation intake, triaged and answered under recorded authority** (2026-11-02 00:24 UTC, decision 01M6CT983QX739DH8H9GP0H6K1): The operator accepted design/conversation-intake.md with the five recommendations of its section 18. CODE-25, ID-10, CODE-26, CODE-27, OPS-10 and DOCS-19 are placed in 0.9.3; CODE-28 waits until the relay is qualified and CODE-29 until an estate asks.
- **DESIGN-24 accepted: the Foundry contract and the first software Foundries** (2026-11-02 00:24 UTC, decision 01M6CT9FG2DGT2SNN00KDBJF4K): The operator accepted design/foundry-contract.md with the five recommendations of its section 16. Its build waits in the future-work backlog for the 0.10.x releases named in section 16 item 1: CODE-19, CODE-20, TPL-05, CODE-21 and DOCS-17 in the first, DIST-05, DIST-06, CODE-22 and CODE-23 in the next, and CODE-24 before M4; packaging stays at M4.
- **REMOTE-11 is placed in 0.9.0, built by the builder** (2026-11-02 02:38 UTC, decision 01M6D1YRVVM8HSPVRS07YPBQQ7): The operator took the recommendation: REMOTE-11 goes in 0.9.0, built by the builder, so the phone signs an answer or an acceptance only with its owner's presence, from a basis the runtime supplies.
- **DESIGN-25 accepted: the web domain, from definition to an observed deployment** (2026-11-02 02:38 UTC, decision 01M6D1Z0CTBHMJTTXJJP09VBC6): The operator accepted design/web-domain.md with the five recommendations of its section 16. Its build waits in the future-work backlog for the 0.10.x releases: slice 1 (CODE-30, DIST-07, TPL-06, CODE-31, CODE-32, DOCS-20) in the first 0.10.x release with DESIGN-24's first group, slice 2 (DIST-08, CODE-33, DIST-09) in the next, slice 3 (DIST-10, CODE-34, CODE-35) before M4.
- **DESIGN-26 accepted: books, book series and world building** (2026-11-02 02:38 UTC, decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3): The operator accepted design/books-domain.md with the five recommendations of its section 15. Its build waits in the future-work backlog for the 0.10.x releases: CODE-36 to CODE-40, TPL-07, PROC-09, DIST-11 and DOCS-21 in the first books release of 0.10.x after the web slice, CODE-41 and DIST-12 in the next, CODE-42 before M4.
- **FIX-84 is placed in 0.9.0 under a no-disclosure rule** (2026-11-02 03:53 UTC, decision 01M6D67FEK9F0E32Y2G9BY4MXT): The operator took the recommendation: FIX-84 goes in 0.9.0, kept local until 0.9.0's release preparation and described only in its documentation set, with the Reviewer principal's security review before acceptance.
- **FIX-85 is placed in 0.9.1, built by the builder** (2026-11-02 03:53 UTC, decision 01M6D67PYH0W0H7RV6DDWYQZPS): The operator took the recommendation: FIX-85 goes in 0.9.1, so from that release a non-operator's acceptance is refused unless its basis reviews passed.
- **Case studies of the operator's ventures on the holding vericairn.com, Nikki & Jayson's first** (2026-11-02 05:26 UTC, decision 01M6DBHSAZMD5JZYNTE1S1HTTZ): The operator answered the case studies' open questions. Nikki & Jayson's study, of the operator's music production and publishing venture, is approved for publication by Nikki and Jayson, with Nikki's input reaching the venture through the operator; the operator approves its exact final text before deploying it. Every other question takes the builder's recommendation, including the Modularyn and WeaveSharp studies, which wait for their own estates' decisions.
- **FIX-87 is placed in 0.9.1** (2026-11-02 05:26 UTC, decision 01M6DBJHDD2Z17RBJ1C9YVMTPF): The operator took the recommendation: FIX-87 goes in 0.9.1, so a decision dated after its own record is refused.
- **OPS-11 is placed in 0.9.1, built by the builder** (2026-11-02 05:46 UTC, decision 01M6DCQCJ46S85R1CR8GTC1GKN): The operator took the recommendation: OPS-11 goes in 0.9.1, built by the builder, so a security report filed as a draft advisory reaches the operator as incident-level attention within minutes and holds every release submission until it is triaged.
- **FIX-86 is placed in 0.9.1, built by Codex through the worker** (2026-11-02 05:46 UTC, decision 01M6DCQMMRBK8WEFBRVF8BDT1P): The operator took the recommendation: FIX-86 goes in 0.9.1, built by Codex through the worker: a prepare-only run, a configured setup step before dispatch with every check applied after it, and a refusal that names an unregistered workspace path.
- **DESIGN-35 is written in 0.9.1's cycle; its build joins conversation intake in 0.9.3** (2026-11-02 05:46 UTC, decision 01M6DCQWMBZGREDFQD9EG2NRN9): The operator took the recommendation: the builder writes DESIGN-35 in 0.9.1's cycle, how an estate follows the reports it files elsewhere through to a verified upgrade, and its build is proposed with conversation intake's work in 0.9.3.
- **DESIGN-36 waits in the future-work backlog until after 0.9.1** (2026-11-02 05:47 UTC, decision 01M6DCR4G97TM2N7MTWDY2G31X): The operator took the recommendation: DESIGN-36, a planned music and audio family for the catalog, is written after 0.9.1 and waits in the future-work backlog.
- **REMOTE-12 waits in the future-work backlog, before Remote leaves its developer build** (2026-11-02 05:47 UTC, decision 01M6DCRCCTJ46A0VH2T09CW862): The operator took the recommendation: REMOTE-12, the runtime verifying a presence key's platform attestation at pairing, is built after 0.9.1 and before Remote leaves its developer build.
- **Three run warnings inspected; no further attempt** (2026-11-02 05:47 UTC, decision 01M6DCRM43Y0JV2WME83SNCGPC): The operator took the recommendation to inspect: the builder inspected the three warned runs, and none needs another attempt.
- **The Nikki & Jayson case study's exact text is approved for vericairn.com** (2026-11-02 11:47 UTC, decision 01M6E1C6NRP7W21A9MBK1AF4GS): The operator approved the exact text of the Nikki & Jayson case study, the Reviewer-checked preview page, for publication on vericairn.com without its preview banner and noindex.

## Work orders opened or revised

- **FIX-75** (ordinary): Status stays fast as the estate grows, under a CI budget (work order 01M5YYNAZC485X4N1WVRAZT55W) (revised; replaces 01M5YYMB22J25SR2XBPQARGC70).
- **TPL-02** (ordinary): The estate's own templates: the design document, open decisions as asks, the design index, release notes and the README (work order 01M61S007AGQRS925C1D490CNY) (revised; replaces 01M4ZSFBZ0WMWCTWPBJ01HA2VA, 01M55FH2REPEVXVZVGN9EG6DVP, 01M5CY0FA57RV45SFP61SDS38Y, 01M5GAV4CZWXFXVRWTDY6NZ16T, 01M5HRTWM3WBPG331GCZ2T1XEF).
- **DESIGN-34** (milestone): Operating modes: people only, people and agents, and agents only (work order 01M61W2ZPFYWRQXN5AET8N29FC) (revised; replaces 01M61W222WXTGBKCZQCTK132TK).
- **DOCS-14** (ordinary): Guides for running Vericairn with people only, with people and agents, and with agents only (work order 01M61W38ZPN4BJYG1EM6H4KYC6) (revised; replaces 01M61W287GTDHQ8NJWWD6S1YBC).
- **FIX-76** (ordinary): A run whose observation is marked settled but whose settlement is unrecorded can still be settled (work order 01M620TN68PQ2E35J4R0BKP9AJ) (revised; replaces 01M6205DN0A7CM70QW0N7WS4MR, 01M6206F0HF3DMNB0FM1KBT3G1).
- **REMOTE-04** (ordinary): Remote: intervention, nodes and desktop handoff (work order 01M6AYRVBTPT9E6FN1DNSD5BJT) (revised; replaces 01M6AYP0BWNJGZD6T6RXFJFBVR).
- **REMOTE-05** (ordinary): Remote: notifications and devices (work order 01M6AYS3HZCH88Y6ZWNG79K5AJ) (revised; replaces 01M6AYP753ND15EZ466RCEQKD2).
- **M3** (milestone): Identity and Remote: identity in 0.5.2 and 0.6.1, Remote in 0.9.0 and 0.9.1 (work order 01M6AYV0RHNEEXQF0GWY137NF8) (revised; replaces 01M4TRNF0RR14FGTB8PH7R7DC0, 01M4VHQVS0AJ8FR13KVGKZG9DY, 01M4VSZ0NRN7CTQRPJSKPPQQTB, 01M4Y13FBGWF5X1GSP9B7Q5YSD, 01M4YBR6NRS4QKCDN5WNEA2QSH, 01M4YSWJ8G4H2K4T8H2S3Q9VC0, 01M54ZG55WRBCWA9JAR6B2P4T8, 01M5CY0RSB31KF7V89WN3AYJYJ, 01M6AYTRFT71TMRMRMB9M3PD4E).
- **READ-05** (ordinary): Readable companions on Remote (work order 01M6AYV92CPSZV6GJ96CQF6WZF) (revised; replaces 01M55E85R3TSV18AJXRQD6PQX4, 01M5CY0W0J4RHDA9PW14YGA4P7, 01M6AYSBXYEHDD5HCZZNTQSNB2).
- **PROMPT-02** (ordinary): Quick send of saved prompts to running agents from Workbench and Remote, as recorded steering (work order 01M6AYVHBSHXM8VPMQFTJ1G8H1) (revised; replaces 01M5CSR9DPQS2SH819ZZ1X7P2G, 01M5CTZ8DCJ2FQ82FNVA3NTFQZ, 01M5CY0Z5M1EJ1KZG0Z718W1T3, 01M6AYSM9MKKX96EW296G05ZR2).
- **FIX-77** (ordinary): Building Vericairn from source sends no build telemetry (work order 01M6AYW28JH1S12PN8N5CA98VS) (revised; replaces 01M6AYMWNGQ8DPZSJMJQ8MM4EE).
- **FIX-78** (ordinary): An authoring operation that can wait on a presence prompt observes for longer than the prompt (work order 01M6AYWAPSBFSAH4DKH31AZNRX) (revised; replaces 01M6AYN3VKQJ9HPWVV6J3FBJQP).
- **REMOTE-01** (ordinary): Remote's shell: iOS and Android apps sharing Workbench's presentation, pairing and the home (work order 01M6AYWK2M9XC8KW1QQXFYMNBV) (revised; replaces 01M6AYNB57MESY1GY6N6H3YQPR).
- **REMOTE-02** (ordinary): Remote: attention and work on the phone (work order 01M6B31DG2TAZDH594JFBERT2W) (revised; replaces 01M6AYNJFQZFAC10BV1KT91M3A, 01M6AYWVHFGYEXEQENM354T1X6).
- **REMOTE-03** (ordinary): Remote: decisions and acceptances on the phone (work order 01M6B31PTG0G59F7WMNNKZBC8B) (revised; replaces 01M6AYNSJENCCVZQVPS562CKQN, 01M6AYX44ZF608K7G1YH8J323B).
- **PROC-05** (ordinary): Procedures and their position (work order 01M6BYFZJCY6TSCGGMEMMGH4DK) (revised; replaces 01M6BYE0GFXXJSKQG759TS2KM5).
- **PROC-06** (ordinary): Reserved steps and their surfaces (work order 01M6BYG8BWHDXK7DCMQ8D3PFT5) (revised; replaces 01M6BYE8BG2SRA20VZMJESQK2G).
- **OPS-05** (ordinary): An estate upgrade replaces an older runtime (work order 01M6BYGGYA9X4W548NH4NFGXVE) (revised; replaces 01M6BYEG4V1RY9HX5ZXTYX1QDG).
- **PROC-07** (ordinary): The software pack's release and upgrade procedures (work order 01M6BYH9N1Y1SRXB0BG47994AZ) (revised; replaces 01M6BYER88M3PF58E5M38R79GX).
- **REMOTE-06** (ordinary): The relay: its program, tickets and the runtime's registration (work order 01M6BYHJ2AJ9QBYBMR22CT7JM7) (revised; replaces 01M6BYF03A4SGXZ8DPD9MX9XQ6).
- **REMOTE-07** (ordinary): Remote over the relay: the device side (work order 01M6BYHT6851S53G08GARRAVWV) (revised; replaces 01M6BYF7H46RPG45V9K41QQAV2).
- **DOCS-11** (ordinary): Assembling, rendering, freezing and indexing a release's documentation set as product commands (work order 01M6BYJ2QMRQ5HNSXJEXDMM2XA) (revised; replaces 01M5HMFT210X4MBQBZR7PGSD61, 01M5HMHMMGJ0KPPJPR410KY07J).
- **GOAL-06** (ordinary): Goals end to end: journey J49, and the first-party estate's next release run as a goal (work order 01M6BYJB4KN2GDAY7XT7C2TPPA) (revised; replaces 01M4ZSFA0G08JB9XB3QQAZVAP9, 01M5D4ZJA2NX87Q17CPMSR3F2P).
- **ESTATE-02** (ordinary): The operating mode on the charter and its checks (work order 01M6BYJKR536MVY8MQ8K5FBBPE) (revised; replaces 01M6BYD86D9XZT1DAH76ZNAY5T).
- **ID-04** (ordinary): Delegated reserved acts: accept-release, publish, spend and use-credential under bounded grants (work order 01M6BYJWBKV8PYV7ND7W5H1STR) (revised; replaces 01M6BYDGP4ECA73Z0TXXD4RVH4).
- **OPS-06** (ordinary): Checkpoints and the agents-only attention view (work order 01M6BYK4NG6FCJVRQSHFMBANMF) (revised; replaces 01M6BYDRJCA5QR5DXCBSX4ZBGJ).
- **REMOTE-08** (ordinary): Notification wakes (work order 01M6BYKD6VEYKASYYK686SGBK1) (revised; replaces 01M6BYFF3G26Y4E9APKTZ032VE).
- **ID-08** (ordinary): Writes without a human key on the host (work order 01M6C95YD3V01WXEQK90G6VZH5) (revised; replaces 01M6C93TCK2QBX9J0C4JD3KJS8).
- **ESTATE-03** (ordinary): Moving an estate between hosts (work order 01M6C966A5N7SWGR7PY954C27K) (revised; replaces 01M6C94155WZZDPS8X64H536GF).
- **ID-09** (ordinary): Entitlement evidence, verified offline (work order 01M6C96E97039HFAFRABY7K381) (revised; replaces 01M6C947S14G0PH0TKTR55NCHF).
- **OPS-09** (ordinary): Hosted operation of released programs (work order 01M6C96P40804KY9N8WZM9QFSP) (revised; replaces 01M6C94EHGK3282D6GAAZ5J42K).
- **CODE-18** (ordinary): The agent support program (work order 01M6C96XYXKYMQ638N4ABK674C) (revised; replaces 01M6C94NF3C9QG0V5CYSYC3ZAZ).
- **REMOTE-09** (ordinary): The push gateway (work order 01M6C975W6VAG5PVX8NREZDARZ) (revised; replaces 01M6C94W6NA3D9MF2P2ZD1CS03).
- **DOCS-16** (ordinary): Editions guides (work order 01M6C97DTJKNEV32H51BT4YPZ7) (revised; replaces 01M6C952YS3X30X46XMZ759CM5).
- **CONTEXT-03** (ordinary): The semantic search Adapter contract and a local Adapter (work order 01M6C97NWCK337CPZNCEDP0DK4) (revised; replaces 01M6C95GDJQ04JXYDAGZ94HRKY).
- **CONTEXT-04** (ordinary): The hosted embedding route (work order 01M6C97XSA97MTAN0RGYFZAZJV) (revised; replaces 01M6C95QCD4J0JA1YH51NBC73M).
- **GOAL-05** (ordinary): Goals: the coordination program for Standard and Pro, and the vericairn-pro repository (work order 01M6C98KJN0B6TMDKN0KZ7BKAX) (revised; replaces 01M4ZSFA0G0WSBY7QK09TRA4HS, 01M54ZG9GJRSQMNW3QBW2QP631, 01M5D4ZF56SX7YYQDA2WHS64XS).
- **CONTEXT-02** (ordinary): Keyword search and search-selected context (work order 01M6C993CTFGCAC7R9PFC9QH5E) (revised; replaces 01M6C959Q4MQTPWD479B73M7PB).
- **FIX-79** (ordinary): A charter that requires signatures changes only by the root operator's signed revision (work order 01M6C99K9VGNQ4V4H5Z1RJETZX) (revised; replaces 01M6C1EZWB6TW1FY9TTAR2PBGA, 01M6C1F8SMQNG1KEHMTGHBXXWK).
- **FIX-80** (ordinary): A new estate keeps its records' bytes on every platform (work order 01M6CKGC3DR9RCA1GCYG6Z0073) (revised; replaces 01M6C9GKADJWZCEG0605T946D6, 01M6C9GTFFHR81GFCMNQFX97X8).
- **FIX-81** (ordinary): Codex workers keep running when the installed Codex moves past the baseline (work order 01M6CKGMNQCCVQ7G7XZRARTEWH) (revised; replaces 01M6C9P7GHJYXN99DXZ5PB00AH, 01M6C9PF82RF30EXMXTKVJ72PS).
- **FIX-82** (ordinary): A worker run whose supervising process died can settle (work order 01M6CKGX9K38T2WR210M85139B) (revised; replaces 01M6C9TNB2DXPXVX065EFV35BV, 01M6C9TWYZ0KQ3A00VXZ0N76F6).
- **ID-05** (ordinary): History checkpoints: the record, its graph, anchoring and removed records (work order 01M6CKH66DJBBFHP0AJDC2TF3H) (revised; replaces 01M6CKCZY55VJXSQPTR8J0Y0YH).
- **ID-06** (ordinary): Closings and authority as it stood at a record's time (work order 01M6CKHEX49FXG62YQXHZFKA3S) (revised; replaces 01M6CKD7AT2CPAXF1K8VMCATYH).
- **OPS-07** (ordinary): Writing and adopting history checkpoints (work order 01M6CKHQNBHYAPC2BT48FR71J8) (revised; replaces 01M6CKDEY7M9GYSBJ8YVG5FW7N).
- **DOCS-15** (ordinary): Signed history in the guides (work order 01M6CKJ0CJ0DW2SFVCVF4GRVVF) (revised; replaces 01M6CKDPFZTTVQQ91DXXSMQHA5).
- **GOAL-07** (ordinary): Standing goals and occurrences (work order 01M6CKJH9JMV1CVH0CRF08FY1Q) (revised; replaces 01M6CKDXYBMRP9QPQHZFR1ADJJ).
- **GOAL-08** (ordinary): Standing outputs: possibilities a person disposes of (work order 01M6CKJSSANQQYW5MQMBH636W4) (revised; replaces 01M6CKE5FJYMJ2PXVYF8D13R34).
- **ID-07** (ordinary): The standing dispatcher: a service principal and the dispatch-standing scope (work order 01M6CKK2DP2P22SXVRRK79CQQF) (revised; replaces 01M6CKED79789GWJZDES9QS79Z).
- **RUN-04** (ordinary): Public retrieval for research runs (work order 01M6CKKB5F5YSEX9RK4V304XNN) (revised; replaces 01M6CKEMQPA2M7XNCMBE084J5J).
- **PROC-08** (ordinary): The first standing roles: duty carrier, researcher and bug hunter (work order 01M6CKKKW9K7K68GEAPCCW247J) (revised; replaces 01M6CKEWABBJCRPHDES9METMRD).
- **GOAL-09** (ordinary): The standing hold: unattended dispatch on the coordination program (work order 01M6CKKWJDBHTTM9S57EGB748Z) (revised; replaces 01M6CKF40KS8C54AP1C10VDNVY).
- **OPS-08** (ordinary): First-party adoption of standing agents (work order 01M6CKM52MRMEREWRH5PZEVGC1) (revised; replaces 01M6CKFKGB415T0QQQDF0XTRKD).
- **REL-092** (ordinary): Release 0.9.2: the relay, signed history, and releases run on the product's own procedure (work order 01M6CTF27TWT78EFSWVAMTJV3D) (revised; replaces 01M6BYGSH6FDQT9JB3ZJVAK0X8, 01M6BYH11X9GWY6YRENT0EBFZW, 01M6CKFV4FNB7GE9DH4GGV8FCY).
- **REL-093** (ordinary): Release 0.9.3: standing agents (work order 01M6CTFAZC6Y16Q153W2BNW8RA) (revised; replaces 01M6CKFBZ21QC0W7P6XC2PSTJF, 01M6CKJ8NSKC6GXKA39WJWZS77).
- **FIX-83** (ordinary): An estate's registers stay valid across an upgrade whose generator output changes (work order 01M6CTFKDQPWB0DBAV9D5EB5QX) (revised; replaces 01M6CST5SF9XB85ESXG944TCVV, 01M6CSTMAGS9GVDH359NHDZKDB).
- **MCP-03** (ordinary): Streamable HTTP in the daemon, signed mode and the bridge (work order 01M6CTFW49A8SNPK0VA1E0FDSV) (revised; replaces 01M6CT9PPD78AHZZSTH1M40DFV).
- **MCP-04** (ordinary): Token mode and the public endpoint (work order 01M6CTG4H3A6SZRRFPCD2V64W2) (revised; replaces 01M6CT9Y8EX0FV2M70N8XJ57E1).
- **REMOTE-10** (ordinary): The relay program's MCP gateway (work order 01M6CTGCYZEF2XPEJX6BF1AXQR) (revised; replaces 01M6CTA5EZMDS3NJHXAR9K1KWD).
- **DOCS-18** (ordinary): Network MCP guides (work order 01M6CTGNBNB6HR89Q95FZSNVHR) (revised; replaces 01M6CTACYPR1R9CPSQKQ9C4A4S).
- **CODE-25** (ordinary): Conversation contract, subscriptions and observations (work order 01M6CTGY62TF21DAGTK34EQXWE) (revised; replaces 01M6CTAM4DXV9XQGB5JTBT0RMM).
- **ID-10** (ordinary): The estate's GitHub App (work order 01M6CTH6S02WAE4PCBZKYBSR2C) (revised; replaces 01M6CTAVMGJP51QPEM6RJYV3AC).
- **CODE-26** (ordinary): The GitHub provider reads conversations (work order 01M6CTHF5ZEMSDEY25GTFGB9RR) (revised; replaces 01M6CTB332WV28NY3VZ0GQ4F1D).
- **CODE-27** (ordinary): Triage (work order 01M6CTHQQD2WRN9H5WCTWDKHR4) (revised; replaces 01M6CTBAJJQY87QWZVW002JNH5).
- **OPS-10** (ordinary): Replies under recorded authority (work order 01M6CTJ07F6MJED5CHXMY3FBV2) (revised; replaces 01M6CTBJ2SBCDE83K8G5724BT4).
- **DOCS-19** (ordinary): Conversation intake guides (work order 01M6CTJ8EX5SFJTRHYZWASV1J8) (revised; replaces 01M6CTBSN19X0XGQN26B6G9ASD).
- **CODE-28** (ordinary): Webhooks for conversation intake (work order 01M6CTJGQ6GSDK8DVSDAXC9SP3) (revised; replaces 01M6CTC130M212YB2KC4Q3KV77).
- **CODE-29** (ordinary): The generated Projects view (work order 01M6CTJS0JBYAY2A06FG3SGVN0) (revised; replaces 01M6CTC8KM2D7DYR3E8HYD2MV3).
- **CODE-19** (ordinary): The Foundry contract (work order 01M6CTK1FR4BAJXC7FH479W3DP) (revised; replaces 01M6CTCG4RSV0Z88J6GPBZW2C5).
- **CODE-20** (ordinary): The client contribution contract (work order 01M6CTKA36179PXS23H5YCS94B) (revised; replaces 01M6CTCQBRAXDFR89PSMD953EJ).
- **TPL-05** (ordinary): The software Foundries' templates (work order 01M6CTKJHWAE3RTKS431QMWEDM) (revised; replaces 01M6CTCYY4A07B6KT6QX2QKB93).
- **CODE-21** (ordinary): The SaaS/Web Application Foundry, slice 1 (work order 01M6CTKTWD96F157NYT8VAP2WZ) (revised; replaces 01M6CTD6CS38TTXPEWAHAY7GAB).
- **DOCS-17** (ordinary): The Foundry guide (work order 01M6CTM33HSJTSADH004S3K05K) (revised; replaces 01M6CTDDP0EF4D4PN4QFEMVH26).
- **DIST-05** (ordinary): The npm Adapter and Provider (work order 01M6CTMBJFSHN01RVTN350JABY) (revised; replaces 01M6CTDN1EEVMXG7H4GRF961WJ).
- **DIST-06** (ordinary): The OCI Adapter and Provider (work order 01M6CTMKXFGRKZZT0ES6DBDZN8) (revised; replaces 01M6CTDWCV58VB21HJRW3GPZJG).
- **CODE-22** (ordinary): The CLI Foundry, slice 1 (work order 01M6CTMW9GJ2V9WREQHFNDH1SH) (revised; replaces 01M6CTE3TNRZ34CQWYNTQGTF1M).
- **CODE-23** (ordinary): The Software Library Foundry, slice 1 (work order 01M6CTN51207TBMZZBT7XXNQ2H) (revised; replaces 01M6CTEB7QGC40X1AMGHEZAEYH).
- **CODE-24** (ordinary): The remaining Foundry operations and qualification (work order 01M6CTND9Z6GC14H418DK1TBW9) (revised; replaces 01M6CTEJKQ89ANEK3CWQ3FSKF8).
- **REMOTE-11** (ordinary): The phone signs a decision only with its owner's presence, from a basis the runtime supplies (work order 01M6D25FD10HGMJ04J35KS5JET) (revised; replaces 01M6D0W6QZYXPEX2Q0HH358V36, 01M6D0WR3SXAN2Z0GVTN80WZ48).
- **CODE-30** (ordinary): The web Domain Pack (work order 01M6D25QS44A6BSPVFFQVH6ZZT) (revised; replaces 01M6D1ZF3D9T1B6997WP5QG5DJ).
- **DIST-07** (ordinary): Web deployments: the hosting contract and its hand-offs (work order 01M6D26011NGFV626AQRSM28W1) (revised; replaces 01M6D1ZPDRFK8YN0HZMYPQQNC4).
- **TPL-06** (ordinary): The web pack's templates (work order 01M6D26859WJFVAQWAG1FS7NH2) (revised; replaces 01M6D1ZYPHHY26Z6YJHR3RZ9F4).
- **CODE-31** (ordinary): The Website Foundry, slice 1 (work order 01M6D26G81JWN9XZCD4ZTADEJ9) (revised; replaces 01M6D205YBD7VEK9J392Q0RZ8Q).
- **CODE-32** (ordinary): The Web Application Foundry, slice 1 (work order 01M6D26RCGM4655Z5NTVYPTA64) (revised; replaces 01M6D20D7ZXA9E9RH504SMWFG1).
- **DOCS-20** (ordinary): The web domain guide (work order 01M6D270J9QRGDD68N4BDBGJ54) (revised; replaces 01M6D20MJBZW1P1J7EPV063SX1).
- **DIST-08** (ordinary): The openapi Adapter (work order 01M6D278Q6QYZJSZ19R0JKS9QT) (revised; replaces 01M6D20VYE7CMY2185FWKRNN5E).
- **CODE-33** (ordinary): The Documentation Site Foundry (work order 01M6D27H6146AW2HK6HP4SMDFZ) (revised; replaces 01M6D2137ZXH5BE29HH6KKXBAA).
- **DIST-09** (ordinary): Netlify, Vercel and Read the Docs hand-offs (work order 01M6D27V57Z4VG21W4GYKH20GZ) (revised; replaces 01M6D21AGAGD3E7CG9YZFGSAR2).
- **DIST-10** (ordinary): The webhook Adapter (work order 01M6D284T56T4WE66YV95X8K1H) (revised; replaces 01M6D21HW8VC28G6RAEGDYS7PP).
- **CODE-34** (ordinary): The Commercial Website Foundry (work order 01M6D28DDY2303DSRJRGD6FCNQ) (revised; replaces 01M6D21SJ5QZ9XD7GGPXQMJNP7).
- **CODE-35** (ordinary): The web domain's remaining operations and qualification (work order 01M6D28NZ5W54SFNAZ8ZAEQQKJ) (revised; replaces 01M6D221BDYXB3V77CJ7P674BC).
- **CODE-36** (ordinary): Books packs and records (work order 01M6D28YKD82EBCGF5CSWJ4PGT) (revised; replaces 01M6D228SAG00WTSGY6Q8XV9PN).
- **TPL-07** (ordinary): Books templates (work order 01M6D297107YX3ZZTGQCN9NZ4V) (revised; replaces 01M6D22GXPDT141RYN80AQ9NZ2).
- **CODE-37** (ordinary): Media stores (work order 01M6D29FRPNCEHB78RPQE6ABTW) (revised; replaces 01M6D22S6FXQXS6JFFQG64E5TS).
- **CODE-38** (ordinary): The manuscript model and three Adapters (work order 01M6D29VNTJS8C4KGARWATS85S) (revised; replaces 01M6D231MQAZ2EE6AHQGA4KSC1).
- **CODE-39** (ordinary): The book-build provider (work order 01M6D2AA10XC7J72V41069862Z) (revised; replaces 01M6D239VPSXXDDRVG9M19RBA5).
- **PROC-09** (ordinary): The edition-release procedure (work order 01M6D2AKG9YGJDE4Y0D8NTXAQ5) (revised; replaces 01M6D23HS070XBY7VT8AA0T503).
- **DIST-11** (ordinary): KDP, IngramSpark and Draft2Digital (work order 01M6D2B0K8MGV3BHZG68VE3C62) (revised; replaces 01M6D23SCFJVYVJ04WQTEFV2X1).
- **CODE-40** (ordinary): World Bible, Book Series and Book Foundries, slice 1 (work order 01M6D2B9CK49DP1XEQ04CC60GY) (revised; replaces 01M6D24193NGCWBKAVVPR9SZXX).
- **DOCS-21** (ordinary): The books guide (work order 01M6D2BHWT44S0YAG9Z6Q714XN) (revised; replaces 01M6D249184XJF9KEF8R067V78).
- **CODE-41** (ordinary): Books slice 2 (work order 01M6D2BTG06M6X7A88EGYJZBZ8) (revised; replaces 01M6D24GAA914F9JHRC1T6YMRX).
- **DIST-12** (ordinary): Generation Providers (work order 01M6D2C35ZMY3FGYQTD6TVVY1W) (revised; replaces 01M6D24QQ2W3AVEWZDXQPG6VKY).
- **CODE-42** (ordinary): Books slice 3 (work order 01M6D2CBMAT2HD9ZPQ1MW8Y8SY) (revised; replaces 01M6D24ZTGYB4T9HMDBSGMT7KJ).
- **REL-090** (ordinary): Release 0.9.0: the public launch, with Remote's first slice (work order 01M6D67YYP07R2ANEDSN48HRSY) (revised; replaces 01M5CXXKNNH8EG58Z5A6QHSEY5, 01M5CXYB2G1DD7K49NQF24M4Y5, 01M5HQAT0ZB15B0TACWSBKM1K9, 01M5HRF22G1A0F0MT1WTKE8Q15, 01M5HRWGT0VYPZV77T5B8965G8, 01M5HTKKK38SWBZNT92KS08P7Q, 01M5HVNSJ2XKFSYF38BSV7E40S, 01M5J3VHE17CWGVNKXCJ5H7K6N, 01M5JDNXP808M6JBFTC6QJ8CYV, 01M5T10WS65EDACNC5S0HHC22J, 01M5TXMYFES3WNTCMPG2DHHTBF, 01M5V100Z31TRFM96ERM4KKJVJ, 01M5V27S2C093WET3HPJF0YGHW, 01M5V2XCDADZ0Y99017W89J481, 01M5V3JYY4AZFNSCPZBTWPK52H, 01M5W6ZXM6EE4G8XKT3PXDGSFJ, 01M5W80FACPGQGP43N1KC7YXPN, 01M5YYMS3M9PPQ1WQ8G377KGSC, 01M61RZRAD7JRB23ZZKFST3QWS, 01M61W2RKYG67H4YJRWX05YXPN, 01M62067NZ0W9YC4WSQZZFNESX, 01M6AYVSQ5HJQNY1397BCKCDP0, 01M6C99BBS4RHVANYB4W8QNJ6E, 01M6CKG3QCD6JHXFP2KY4VVE4Q, 01M6CTET0H2MA5XV9P11JNAJG9, 01M6D25717NG0ACPM98A6DZRWD).
- **FIX-84** (ordinary): A document revision clears only as its declaration allows (work order 01M6D688H1DR53MSFX1975TCHJ) (revised; replaces 01M6D62H556491KR2ETNCK792D, 01M6D630FRCQHDXJJHPH6VTMCC).
- **FIX-85** (ordinary): An acceptance rests only on reviews that passed (work order 01M6D68SA0392CSA439DGJ2XGH) (revised; replaces 01M6D638WDQ105MT8421CKRK9G, 01M6D63QHMCEVJEKPQJ9YRYB24).
- **FIX-87** (ordinary): A decision cannot be dated after its own record (work order 01M6DBK2M40EWZWWE56F8PXXMX) (revised; replaces 01M6DBJ1JBRCSMYBY97C5FRWZM).
- **REL-091** (ordinary): Release 0.9.1: Remote's intervention and notifications, companions on Remote and quick send (work order 01M6DCRW20E88701JW2NNNZ80H) (revised; replaces 01M6AYQ6HZAEBTB1Y1446SPS85, 01M6AYRBCYJD0B3PX7823BT9C9, 01M6AYRJRE7T37BJ5CZT53YGFQ, 01M6BYFQ1MNN5Z45CAPEANHG9Y, 01M6C98VF61WVDHZ8SQV8T7DTH, 01M6D68GZ7XVEPQVHRB5971SF7, 01M6DBJSFS7DSFYNPJTN7TWV73).
- **OPS-11** (ordinary): Security reports reach the operator within minutes and hold a release until triaged (work order 01M6DCS541BNC17EMRXFDPEM8B) (revised; replaces 01M6D6FMR99RWRQ6MY2SQ6AFR9, 01M6D6G5A66B8NFYQ993003E5B).
- **FIX-86** (ordinary): A worker run can prepare its workspace before the agent starts (work order 01M6DCSDWW9HGVJMJD2W96A4WQ) (revised; replaces 01M6D6PTFQSMN0KJY8TQJRZG3A, 01M6D6Q9K50D8RNQKGXPWVN7EH).
- **DESIGN-35** (ordinary): Following what an estate reports elsewhere: outbound reports and upstream changes (work order 01M6DCSQXASTJ483YTDTADZ41S) (revised; replaces 01M6D6QKDNCJ77XYV8GBAHZ1R6, 01M6D6R9HDJFBY6869F9N8WVPM).
- **DESIGN-36** (ordinary): Music production and publishing in the catalog (work order 01M6DCT2TB6N6DECD8EY46PPWJ) (revised; replaces 01M6DBKBY51SRTPXPA05DH0F1R, 01M6DBKW2VWRG8BBKD5C0TQ8JJ).
- **REMOTE-12** (ordinary): The runtime verifies that a phone's presence key requires presence (work order 01M6DCTBV33H2TPX3ZHVPDFPB9) (revised; replaces 01M6DCC9YQG1472HYH3PPWEXWX, 01M6DCCZBPYXFXFQDZZ9CN3W8K).

## Milestone and release acceptances

- release 0.8.4: "I accept release 0.8.4." (acceptance 01M5YXNMA6Q432QRYB6GJ0MR11, 2026-10-27 14:54 UTC, signed)
- DESIGN-21: "I accept DESIGN-21." (acceptance 01M61XSWSSEF924JTVD2KM1VTB, 2026-10-28 18:54 UTC, signed)
- DESIGN-22: "I accept DESIGN-22." (acceptance 01M61XTH415Z400F6N37WJKB4B, 2026-10-28 18:54 UTC, signed)
- 01M5R33PYYT42RDZ5RNSAF2TY0: "0.8.4 is published (release record 01M61R2J5NY521VVDNEMPX32Y2), the goal's only condition, as the builder's evaluation 01M61Y321MFENSBV4DFEN1DCPH found; its Codex work went through the worker." (acceptance 01M61YT7H45V8CNNERJ5NA2DYY, 2026-10-28 19:11 UTC, signed)
- 01M5RC4X77WFB9ZA290GCR9SDW: "0.8.4 is published (release record 01M61R2J5NY521VVDNEMPX32Y2), the goal's only condition, as the builder's evaluation 01M61Y39DSZB2AVJ856M5BJAF6 found; its Codex work went through the worker." (acceptance 01M61YTSKFS119YFWMCYRB0760, 2026-10-28 19:12 UTC, signed)
- 01M5SKYJHHW0TWPCCJSYN5WCAQ: "0.8.4 is published (release record 01M61R2J5NY521VVDNEMPX32Y2), the goal's only condition, as the builder's evaluation 01M61Y3GQ7V17A8KE2N8YRSA9E found; its Codex work went through the worker." (acceptance 01M61YVAG4A9WDZZBRT5Q1MW9J, 2026-10-28 19:12 UTC, signed)
- 01M5TC2XTTR7KGKTK6T3G9253H: "0.8.4 is published (release record 01M61R2J5NY521VVDNEMPX32Y2), the goal's only condition, as the builder's evaluation 01M61Y3R5BJ8Y00D1FPPM9J1ZD found; its Codex work went through the worker." (acceptance 01M61YVR76XZV5AXDRT3KAM9HR, 2026-10-28 19:12 UTC, signed)
- 01M621RF26MTRPFSRYMZW24VR8: "Accepted Codex's FIX-73 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6AKZJP1VCDZ93B692N1AM2B." (acceptance 01M6AKZW9R93D0SBZKS9PDHXGK, 2026-11-01 03:55 UTC, signed)
- 01M622FCKNG0Z0MJBNYANCCSKY: "Accepted Codex's OPS-04 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6AM04E28C4D86W2MVC7MDVV." (acceptance 01M6AM0DJGV5VTHWVEKZF82F6R, 2026-11-01 03:56 UTC, signed)
- 01M6241GQBYT6YG5H8V325P3J8: "Accepted Codex's CODE-12 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6AP0G8HFJ1KN5WSHJ48NY9X." (acceptance 01M6AP0SZDX5DS428VS1AB3TC5, 2026-11-01 04:31 UTC, signed)
- 01M625CAB4034GBGY9N67BK86P: "Accepted Codex's FIX-75 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6AP12Q41JFP0EFYG2BQBJC7." (acceptance 01M6AP1C4QGJQTG8NMVY2QXPXH, 2026-11-01 04:31 UTC, signed)
- 01M625P240SEVBYD33KKKG0YXZ: "Accepted Codex's WATCH-02 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6AQYZ7NSBEWZAB31D01C1FB." (acceptance 01M6AQZ7PN85YZGPH4F50FKAZJ, 2026-11-01 05:05 UTC, signed)
- 01M6AN1DWPW9V0RK4VWKKWMZGQ: "Accepted Codex's CODE-13 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6AVVYVT73VC88A0ZQGEKQFP." (acceptance 01M6AVW6AQRCX39APNB38M9XQD, 2026-11-01 06:13 UTC, signed)
- 01M6ANNZA9663Y72Y3H3SKKGVG: "Accepted Codex's CODE-14 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6AXKW04SMRTPQ4QPS6MR305." (acceptance 01M6AXM3DQE4ZGWMC15DKD8C7G, 2026-11-01 06:44 UTC, signed)
- 01M6ASEMYMNWEBW77BB7KN0761: "Accepted Codex's WATCH-03 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6AXM9SXY0F1KSK8K8WC66ZM." (acceptance 01M6AXMH5QB2C0GJZ45VVA249M, 2026-11-01 06:44 UTC, signed)
- 01M6BE6Q1Q8PY8S0F9S0R1QHXH: "Accepted Codex's GUIDE-01 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6C3TBVSDP133EJAYA2EGP6R." (acceptance 01M6C3TM3ZDNPS5SRYWZJY2ENQ, 2026-11-01 17:51 UTC, signed)
- 01M6CDH5N970A0QR60BYS8QKKN: "Accepted Codex's DOCS-10 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6CP1F38C6G4JRA4ZS3K4B0X." (acceptance 01M6CP1QGGXKT4NWSEFV8ZPV1P, 2026-11-01 23:10 UTC, signed)
- 01M6CFRV60AKNY7Q8NFF60908G: "Accepted Codex's DOCS-14 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6CVXBPEQ20ZPJ24YXWY0CE8." (acceptance 01M6CVXMYNG9ZYY0V58YS7RPA0, 2026-11-02 00:53 UTC, signed)
- 01M6CE77WDSND7942313DYMTJ9: "Accepted Codex's REMOTE-02 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6CWPH19H8XTG6NKCYYSZ4RY." (acceptance 01M6CWPSJ9Q8EFV3CMW20AWA2Z, 2026-11-02 01:06 UTC, signed)
- 01M6CEVN8AN435Y3M1N4FBWX45: "Accepted Codex's EFF-01 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6CWQ0W3VGJR9RH75ZXBR3EM." (acceptance 01M6CWQ960XC57XV467NGHFDZJ, 2026-11-02 01:07 UTC, signed)
- 01M6CXJ1B7C7C7A3MNG1D33QXD: "Accepted Codex's REMOTE-03 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6D6X1T2RQAVGM3MPF5VHTSE." (acceptance 01M6D6XAJ7RVNXTDRC3DKM3APF, 2026-11-02 04:05 UTC, signed)
- 01M6D8CG9AV8R9YJP1DRNWTYCP: "Accepted Codex's POLICY-05 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6DFVCRT14H8PYK5MA3ND8TB." (acceptance 01M6DFVNGACFWPZEJ56QF4DMXV, 2026-11-02 06:41 UTC, signed)
- 01M6DD4J6SE2WAVH2DV1N42MFG: "Accepted Codex's GUIDE-02 result under the operator's accept-work grant 01M51QS2W4258EQ8FCMAPXWG4J, on the builder's independent-agent review 01M6DJ08FTPX27RB13AXR5WQ6S." (acceptance 01M6DJ0HDYX0C3E14RQM7XMAF0, 2026-11-02 07:19 UTC, signed)

## Identity and policy

None in this window.

## Design documents revised

- Vericairn roadmap revision 69 (`design/roadmap.md`): Revision 70: 0.8.4 ships as reviewed with its notes-window sentence corrected in 0.9.0's set, and FIX-75 joins 0.9.0, under decisions 01M5YYKV6P054QZN9K5WM3ME32 and 01M5YYJQ112MG17HYAMVEJDH9F.
- Catalog revision 32 (`design/catalog.md`): Revision 32: `0.8.4` is the current published release; it adds templates, saved prompts, operator close and operator requests, release review briefs, guided decisions, the publication documentation pass and MCP resources and prompts, and introduces no record format.
- CLI and SDK revision 35 (`design/cli-and-sdk.md`): Revision 35: the current published release is `0.8.4`; the command table records its thirty new commands as published, among them the `prompt` and `templates` groups, and `DOCS-10` is planned for `0.9.0`.
- Communication, agent runs and the context package revision 18 (`design/communication-and-context.md`): Revision 19: the documentation pass after `0.8.4`'s publication: `PROMPT-01` and `TPL-COMMS-01` are published in `0.8.4`, and `EFF-01`, its design accepted, is planned for `0.9.0` (decision `01M5V0XKVDPM2C3YT1JWBE240G`).
- Vericairn: master design revision 45 (`design/master-design.md`): Revision 46: the documentation pass after `0.8.4`'s publication: the current published release is `0.8.4` and the estate pins it; `0.8.4`'s section describes what shipped, with `EFF-01` and `DOCS-10` built in `0.9.0` beside the builds of the accepted `DESIGN-21` and `DESIGN-22` (decisions `01M5V0XKVDPM2C3YT1JWBE240G`, `01M5V0XFJ8W3KM05TTPY1RY4XQ`, `01M5TXFKBMQ6V50NJCKQ415JRJ` and `01M5V0XB9GKSPZNS14MPSYEBWC`), and the packages are at `0.8.4` on nuget.org.
- Platform obligations revision 19 (`design/platforms.md`): Revision 19: updates the current release and estate pin to `0.8.4` at record format 3 with release run `40401835538`, focused review `01M5YRMS009X8A0WWE31NZJVW5`, the eight NuGet listings at `0.8.4`, and the estate's cold start passing on Linux, macOS and Windows with the three-way status comparison, which `FIX-63` restores (issue 205).
- Vocabulary: the single register of the estate's terms revision 16 (`design/vocabulary.md`): Revision 16: checks published `0.8.4` and the estate's `0.8.4` pin at record format 3; the release adds five terms, operator request, review brief, saved prompt, documentation pass and MCP resource, and `register-coverage` remains undeclared.
- Vericairn roadmap revision 70 (`design/roadmap.md`): Revision 71: carries decisions `01M61SVTKBEJ5BPZTQ6YBVX1Z7`, `01M61Q7XJZPB5ZEGMD4W53BP29` and `01M61RYW42P395M5BATMJWGF4E`: `0.8.4` is the current published release, its first dispatch abandoned and its set frozen again after the documentation pass, accepted on the Reviewer's passing review, and the estate pins it, which brings `FIX-63` to the estate check and corrects its `status` timeout; `TPL-02` moves to `0.9.0`, where the estate adopts its templates.
- Identify content by the bytes it contains revision 6 (`docs/knowledge/article/content-addressed-bytes.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary.
- Derive repeatable views from records revision 7 (`docs/knowledge/article/deterministic-projections.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary, with the 0.8.4 upgrade and the restored three-way comparison.
- Compose implementations explicitly at the host revision 8 (`docs/knowledge/article/explicit-composition.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary.
- Carry decisions across boundaries as explicit values revision 5 (`docs/knowledge/article/explicit-value-contracts.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary.
- Refuse when admission cannot be established revision 7 (`docs/knowledge/article/fail-closed-admission.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary, with notes on FIX-67.
- Record intent before an external effect revision 6 (`docs/knowledge/article/intent-before-effect.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary, with a note on FIX-65.
- Keep machine locations in local configuration revision 7 (`docs/knowledge/article/machine-local-settings.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary, with notes on FIX-67.
- Present runtime facts through observable view models revision 7 (`docs/knowledge/article/observable-presentation-models.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary.
- Give clients one application decision boundary revision 5 (`docs/knowledge/article/one-application-boundary.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary.
- Keep external implementations behind runtime contracts revision 8 (`docs/knowledge/article/ports-and-adapters.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary.
- Make interrupted record batches visible revision 6 (`docs/knowledge/article/recoverable-record-batches.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary.
- Revise records by supersession revision 6 (`docs/knowledge/article/revision-by-supersession.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary.
- Stand-ins prove the code path, not the conditions a person runs under revision 10 (`docs/knowledge/article/stand-ins-and-real-conditions.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary.
- Return expected refusals with structured findings revision 6 (`docs/knowledge/article/structured-refusals.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary.
- Why records are the only bookkeeping revision 8 (`docs/knowledge/article/why-records-are-the-only-bookkeeping.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary, with a note on OPS-03.
- Wrap transport behavior at the boundary that needs it revision 6 (`docs/knowledge/article/wrapping-transport-behavior.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary.
- Accepting an exact subject revision 7 (`docs/knowledge/guide/accepting-an-exact-subject.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary, with a note on OPS-02.
- Checking the exact head before merge revision 7 (`docs/knowledge/guide/checking-the-head-before-merge.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary, with a note on CI-05.
- Cold-starting the estate from a fresh clone revision 12 (`docs/knowledge/guide/cold-starting-the-estate.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary, with the 0.8.4 upgrade and the restored three-way comparison.
- Freezing a release documentation set revision 6 (`docs/knowledge/guide/freezing-documentation-sets.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary, with the 0.8.4 first freeze's lesson: run the documentation pass before freezing.
- Arranging an independent review revision 7 (`docs/knowledge/guide/independent-review.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary.
- Organizing feature files without hiding shared responsibility revision 7 (`docs/knowledge/guide/organizing-feature-code.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary.
- Reviewing living documents when a release changes their claims revision 7 (`docs/knowledge/guide/reviewing-living-documents.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary, with notes on DOCS-12 and TPL-02.
- Scanning before a push revision 6 (`docs/knowledge/guide/scanning-before-push.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary, with notes on FIX-67.
- Testing interchangeable implementations with one contract revision 6 (`docs/knowledge/guide/testing-interchangeable-implementations.md`): Brought to 0.8.4 after its publication: applicability, tracked sources at a41263f and the 0.8.4 release boundary.
- Vericairn roadmap revision 71 (`design/roadmap.md`): Revision 72: DESIGN-34 (operating modes) in 0.9.0's cycle and DOCS-14 (their guides) in 0.9.0, under decision 01M61W18DCCH2KTEAXJRX9ZFTM.
- Vericairn.com and Vericairn.org revision 8 (`design/websites.md`): Revision 8: the domains, their DNS and both sites move to Vericairn's own Azure subscription, under decision 01M61W1EKNK75B8FNT350PFF7X.
- Vericairn roadmap revision 72 (`design/roadmap.md`): Revision 73: FIX-76 in 0.9.0, built by the builder, under decision 01M620576H8VVN511EQM2AC2XC.
- Vericairn.com and Vericairn.org revision 9 (`design/websites.md`): Revision 9: the Azure move is done and verified, under decision 01M61W1EKNK75B8FNT350PFF7X.
- operating-modes revision 1 (`design/operating-modes.md`): Revision 1: DESIGN-34's design of the three operating modes, proposed to the operator under decision 01M61W18DCCH2KTEAXJRX9ZFTM.
- Vericairn roadmap revision 73 (`design/roadmap.md`): Revision 74: REMOTE-01 to REMOTE-03, FIX-77 and FIX-78 in 0.9.0; REMOTE-04, REMOTE-05, READ-05, PROMPT-02 and M3 in 0.9.1 under REL-091; decisions 01M6AYMED1JMQR78ZDW6DRC8GZ, 01M6AYM73A3CX0T5CV3NDYW5VQ and 01M6AYMNFCQ8G0WV4NAJQC5R6K.
- release-procedure revision 1 (`design/release-procedure.md`): Revision 1: DESIGN-33's design of a product-guided release and upgrade procedure, proposed to the operator under decision 01M5W7ZN7WJ2FWPRPZK17D6FTY.
- relay revision 1 (`design/relay.md`): Revision 1: DESIGN-31's design of the relay, proposed to the operator under decision 01M5JDNQS9ZY8C2KW2DBNYRAEM.
- Catalog revision 33 (`design/catalog.md`): Revision 33: six family Adapter tables corrected, the 0.8.4 template entries marked released, and EFF-01's and TPL-COMMS-01's placements corrected.
- Catalog: Aerospace and Defense revision 2 (`design/catalog/aerospace-and-defense.md`): The Adapters table's delimiter row now has its sixth column, so the table reads as a table and every Adapter row counts.
- Catalog: Food and Agriculture revision 2 (`design/catalog/food-and-agriculture.md`): The Adapters table's delimiter row now has its sixth column, so the table reads as a table and every Adapter row counts.
- Catalog: Manufacturing Quality and Operations revision 2 (`design/catalog/manufacturing-quality-and-operations.md`): The Adapters table's delimiter row now has its sixth column, so the table reads as a table and every Adapter row counts.
- Catalog: Mining and Minerals revision 2 (`design/catalog/mining-and-minerals.md`): The Adapters table's delimiter row now has its sixth column, so the table reads as a table and every Adapter row counts.
- Catalog: Public Sector revision 2 (`design/catalog/public-sector.md`): The Adapters table's delimiter row now has its sixth column, so the table reads as a table and every Adapter row counts.
- Catalog: Transportation and Logistics revision 2 (`design/catalog/transportation-and-logistics.md`): The Adapters table's delimiter row now has its sixth column, so the table reads as a table and every Adapter row counts.
- operating-modes revision 2 (`design/operating-modes.md`): Revision 2: accepted by decision 01M6BY51FK5AXQK0R06M3HQD9Q; the build's work orders named and placed in the awaiting-decision backlog; revision 1's date corrected.
- release-procedure revision 2 (`design/release-procedure.md`): Revision 2: accepted by decision 01M6BY59Z58NQ98WH39031T6DA; the build's placements in 0.9.1 and 0.9.2.
- relay revision 2 (`design/relay.md`): Revision 2: accepted by decision 01M6BY5KQD1CYW8MAM47T1R27J; the build's placements and the applied revisions of section 11.
- Identity and connectivity revision 10 (`design/identity-and-connectivity.md`): Revision 10: the relay row of section 6 points to the accepted relay design, with its ticket rule and the shared authorization model.
- Remote revision 5 (`design/remote.md`): Revision 5: the connection path, the relay's content-free wakes and their Apple Developer Program dependency, and refresh through the relay.
- Vericairn roadmap revision 74 (`design/roadmap.md`): Revision 75: DESIGN-31, DESIGN-33 and DESIGN-34 accepted; 0.9.1 adds PROC-05, PROC-06 and OPS-05; a new 0.9.2 row carried by REL-092; the awaiting-decision backlog notes.
- signed-history revision 1 (`design/signed-history.md`): Revision 1: the proposed design for DESIGN-06.
- standing-agents revision 1 (`design/standing-agents.md`): Revision 1: the proposed design for DESIGN-07.
- editions revision 1 (`design/editions.md`): Revision 1: the proposed design for DESIGN-23.
- mcp-network revision 1 (`design/mcp-network.md`): Revision 1: the proposed design for DESIGN-29.
- search revision 1 (`design/search.md`): Revision 1: the proposed design for DESIGN-30.
- conversation-intake revision 1 (`design/conversation-intake.md`): Revision 1: the proposed design for DESIGN-32.
- foundry-contract revision 1 (`design/foundry-contract.md`): Revision 1: the proposed design for DESIGN-24.
- web-domain revision 1 (`design/web-domain.md`): Revision 1: the proposed design for DESIGN-25.
- books-domain revision 1 (`design/books-domain.md`): Revision 1: the proposed design for DESIGN-26.
- operating-modes revision 3 (`design/operating-modes.md`): Revision 3: corrects section 10 on unknown authority scopes, which the authority record accepts as free text that no check honors.
- Architecture revision 12 (`design/architecture.md`): Revision 12: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- CLI and SDK revision 36 (`design/cli-and-sdk.md`): Revision 36: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Communication, agent runs and the context package revision 19 (`design/communication-and-context.md`): Revision 20: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Editions, licensing and optional services revision 7 (`design/editions-and-services.md`): Revision 7: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- editions revision 2 (`design/editions.md`): Revision 2: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Estate model revision 8 (`design/estate-model.md`): Revision 8: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Goals: objectives an estate carries out with agents and people revision 17 (`design/goals.md`): Revision 17: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Identity and connectivity revision 11 (`design/identity-and-connectivity.md`): Revision 11: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Journeys revision 4 (`design/journeys.md`): Revision 4: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Knowledge revision 7 (`design/knowledge.md`): Revision 7: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Maintenance schedules: recurring duties with evidence of completion revision 4 (`design/maintenance-schedules.md`): Revision 4: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Vericairn: master design revision 46 (`design/master-design.md`): Revision 47: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Open product decisions revision 30 (`design/open-decisions.md`): Revision 30: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Packages, Providers and Foundries revision 14 (`design/packages-providers-foundries.md`): Revision 14: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Process profiles revision 9 (`design/process-profiles.md`): Revision 9: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Readable companions: managed information people can read revision 5 (`design/readable-companions.md`): Revision 5: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- relay revision 3 (`design/relay.md`): Revision 3: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Vericairn roadmap revision 75 (`design/roadmap.md`): Revision 76: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- search revision 2 (`design/search.md`): Revision 2: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Storage profiles and participation modes revision 5 (`design/storage-profiles.md`): Revision 5: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Templates: what a piece of work contains, and how each part is made and judged revision 9 (`design/templates.md`): Revision 9: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Vocabulary: the single register of the estate's terms revision 17 (`design/vocabulary.md`): Revision 17: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- Vericairn.com and Vericairn.org revision 10 (`design/websites.md`): Revision 10: carries the accepted DESIGN-23 (decision 01M6C91KKDGDWVW4WWCMHXJDZ5) and DESIGN-30 (decision 01M6C91T8RFE02FJVGDECKNS1D) as they bear on this document.
- CLI and SDK revision 37 (`design/cli-and-sdk.md`): Revision 37: carries the accepted DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- Communication, agent runs and the context package revision 20 (`design/communication-and-context.md`): Revision 21: carries the accepted DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- conversation-intake revision 2 (`design/conversation-intake.md`): Revision 2: carries the accepted DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- Editions, licensing and optional services revision 8 (`design/editions-and-services.md`): Revision 8: carries the accepted DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- Estate model revision 9 (`design/estate-model.md`): Revision 9: carries the accepted DESIGN-06 (decision 01M6CKCGVFN67GT24703NW01AQ) as they bear on this document.
- Estate policies: rules an estate declares for its own work revision 10 (`design/estate-policies.md`): Revision 10: carries the accepted DESIGN-06 (decision 01M6CKCGVFN67GT24703NW01AQ) and DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- Goals: objectives an estate carries out with agents and people revision 18 (`design/goals.md`): Revision 18: carries the accepted DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- Identity and connectivity revision 12 (`design/identity-and-connectivity.md`): Revision 12: carries the accepted DESIGN-06 (decision 01M6CKCGVFN67GT24703NW01AQ) as they bear on this document.
- Journeys revision 5 (`design/journeys.md`): Revision 5: carries the accepted DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- Maintenance schedules: recurring duties with evidence of completion revision 5 (`design/maintenance-schedules.md`): Revision 5: carries the accepted DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- Vericairn: master design revision 47 (`design/master-design.md`): Revision 48: carries the accepted DESIGN-06 (decision 01M6CKCGVFN67GT24703NW01AQ) and DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- Open product decisions revision 31 (`design/open-decisions.md`): Revision 31: carries the accepted DESIGN-06 (decision 01M6CKCGVFN67GT24703NW01AQ) and DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- operating-modes revision 4 (`design/operating-modes.md`): Revision 4: carries the accepted DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- Packages, Providers and Foundries revision 15 (`design/packages-providers-foundries.md`): Revision 15: carries the accepted DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- Process profiles revision 10 (`design/process-profiles.md`): Revision 10: carries the accepted DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- Release lines and channels revision 3 (`design/release-lines.md`): Revision 3: carries the accepted DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- release-procedure revision 3 (`design/release-procedure.md`): Revision 3: carries the accepted DESIGN-06 (decision 01M6CKCGVFN67GT24703NW01AQ) as they bear on this document.
- Vericairn roadmap revision 76 (`design/roadmap.md`): Revision 77: carries FIX-80, FIX-81 and FIX-82 in 0.9.0 (decision 01M6CKC95M8EBE3MRXRPSN79AJ) and the accepted DESIGN-06 (decision 01M6CKCGVFN67GT24703NW01AQ) and DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7).
- signed-history revision 2 (`design/signed-history.md`): Revision 2: accepted with all five recommendations of § 16 (decision 01M6CKCGVFN67GT24703NW01AQ), its build placed in 0.9.2.
- standing-agents revision 2 (`design/standing-agents.md`): Revision 2: accepted with all five recommendations of § 15 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7), its build placed in 0.9.3 and the future-work backlog.
- Vocabulary: the single register of the estate's terms revision 18 (`design/vocabulary.md`): Revision 18: carries the accepted DESIGN-06 (decision 01M6CKCGVFN67GT24703NW01AQ) and DESIGN-07 (decision 01M6CKCRMAJEAJ9KF3P3A6KEC7) as they bear on this document.
- Architecture revision 13 (`design/architecture.md`): Revision 13: carries the accepted DESIGN-29 (decision 01M6CT910VB30CMPKAZXBG8GND) and DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- Ecosystem workflow blueprints revision 4 (`design/blueprints.md`): Revision 4: carries the accepted DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- books-domain revision 2 (`design/books-domain.md`): Revision 2: carries the accepted DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- Catalog revision 34 (`design/catalog.md`): Revision 34: carries the accepted DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) and DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- CLI and SDK revision 38 (`design/cli-and-sdk.md`): Revision 38: carries the accepted DESIGN-29 (decision 01M6CT910VB30CMPKAZXBG8GND) and DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- Cohesion contract revision 7 (`design/cohesion-contract.md`): Revision 7: carries the accepted DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- Communication, agent runs and the context package revision 21 (`design/communication-and-context.md`): Revision 22: carries the accepted DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) as they bear on this document.
- Community build guides, implementation requests and extension ecosystems revision 4 (`design/community-build-guides.md`): Revision 4: carries the accepted DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) as they bear on this document.
- conversation-intake revision 3 (`design/conversation-intake.md`): Revision 3: accepted with all five recommendations of § 18 (decision 01M6CT983QX739DH8H9GP0H6K1), its build placed in 0.9.3 and the future-work backlog.
- Editions, licensing and optional services revision 9 (`design/editions-and-services.md`): Revision 9: carries the accepted DESIGN-29 (decision 01M6CT910VB30CMPKAZXBG8GND) as they bear on this document.
- editions revision 3 (`design/editions.md`): Revision 3: carries the accepted DESIGN-29 (decision 01M6CT910VB30CMPKAZXBG8GND) as they bear on this document.
- Estate model revision 10 (`design/estate-model.md`): Revision 10: carries the accepted DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- Estate policies: rules an estate declares for its own work revision 11 (`design/estate-policies.md`): Revision 11: carries the accepted DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) as they bear on this document.
- foundry-contract revision 2 (`design/foundry-contract.md`): Revision 2: accepted with all five recommendations of § 16 (decision 01M6CT9FG2DGT2SNN00KDBJF4K), its build waiting in the future-work backlog for the 0.10.x releases.
- Goals: objectives an estate carries out with agents and people revision 19 (`design/goals.md`): Revision 19: carries the accepted DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) as they bear on this document.
- Identity and connectivity revision 13 (`design/identity-and-connectivity.md`): Revision 13: carries the accepted DESIGN-29 (decision 01M6CT910VB30CMPKAZXBG8GND) and DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) as they bear on this document.
- Journeys revision 6 (`design/journeys.md`): Revision 6: carries the accepted DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) and DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- Maintenance schedules: recurring duties with evidence of completion revision 6 (`design/maintenance-schedules.md`): Revision 6: carries the accepted DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) as they bear on this document.
- Vericairn: master design revision 48 (`design/master-design.md`): Revision 49: carries the accepted DESIGN-29 (decision 01M6CT910VB30CMPKAZXBG8GND) and DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) and DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- mcp-network revision 2 (`design/mcp-network.md`): Revision 2: accepted with all five recommendations of § 15 (decision 01M6CT910VB30CMPKAZXBG8GND), its build placed in 0.9.2 and 0.9.3.
- Open product decisions revision 32 (`design/open-decisions.md`): Revision 32: carries the accepted DESIGN-29 (decision 01M6CT910VB30CMPKAZXBG8GND) and DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) and DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- operating-modes revision 5 (`design/operating-modes.md`): Revision 5: carries the accepted DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) as they bear on this document.
- Packages, Providers and Foundries revision 16 (`design/packages-providers-foundries.md`): Revision 16: carries the accepted DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) and DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- Process profiles revision 11 (`design/process-profiles.md`): Revision 11: carries the accepted DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) and DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- relay revision 4 (`design/relay.md`): Revision 4: carries the accepted DESIGN-29 (decision 01M6CT910VB30CMPKAZXBG8GND) and DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) as they bear on this document.
- Release lines and channels revision 4 (`design/release-lines.md`): Revision 4: carries the accepted DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) as they bear on this document.
- release-procedure revision 4 (`design/release-procedure.md`): Revision 4: carries the accepted DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- Remote revision 6 (`design/remote.md`): Revision 6: carries the accepted DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- Vericairn roadmap revision 77 (`design/roadmap.md`): Revision 78: carries FIX-83 in 0.9.0 (decision 01M6CT8SNBZDZZHQF3ZQEAYM45) and the accepted DESIGN-29 (decision 01M6CT910VB30CMPKAZXBG8GND), DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1), DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K).
- standing-agents revision 3 (`design/standing-agents.md`): Revision 3: carries the accepted DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) as they bear on this document.
- Templates: what a piece of work contains, and how each part is made and judged revision 10 (`design/templates.md`): Revision 10: carries the accepted DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- Vocabulary: the single register of the estate's terms revision 19 (`design/vocabulary.md`): Revision 19: carries the accepted DESIGN-29 (decision 01M6CT910VB30CMPKAZXBG8GND) and DESIGN-32 (decision 01M6CT983QX739DH8H9GP0H6K1) and DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- web-domain revision 2 (`design/web-domain.md`): Revision 2: carries the accepted DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- Workbench revision 7 (`design/workbench.md`): Revision 7: carries the accepted DESIGN-24 (decision 01M6CT9FG2DGT2SNN00KDBJF4K) as they bear on this document.
- Architecture revision 14 (`design/architecture.md`): Revision 14: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) as they bear on this document.
- Ecosystem workflow blueprints revision 5 (`design/blueprints.md`): Revision 5: carries the accepted DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- books-domain revision 3 (`design/books-domain.md`): Revision 3: accepted with all five recommendations of § 15 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3), its build waiting in the future-work backlog for the 0.10.x releases.
- Catalog revision 35 (`design/catalog.md`): Revision 35: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) and DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- CLI and SDK revision 39 (`design/cli-and-sdk.md`): Revision 39: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) and DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- Estate model revision 11 (`design/estate-model.md`): Revision 11: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) and DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- foundry-contract revision 3 (`design/foundry-contract.md`): Revision 3: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) and DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- Journeys revision 7 (`design/journeys.md`): Revision 7: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) and DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- Knowledge revision 8 (`design/knowledge.md`): Revision 8: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) and DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- Vericairn: master design revision 49 (`design/master-design.md`): Revision 50: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) and DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- Open product decisions revision 33 (`design/open-decisions.md`): Revision 33: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) and DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- Packages, Providers and Foundries revision 17 (`design/packages-providers-foundries.md`): Revision 17: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) and DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- Process profiles revision 12 (`design/process-profiles.md`): Revision 12: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) and DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- The product and IP graph revision 4 (`design/product-ip-graph.md`): Revision 4: carries the accepted DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- release-procedure revision 5 (`design/release-procedure.md`): Revision 5: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) and DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- Vericairn roadmap revision 78 (`design/roadmap.md`): Revision 79: carries REMOTE-11 in 0.9.0 (decision 01M6D1YRVVM8HSPVRS07YPBQQ7) and the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) and DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3).
- Storage profiles and participation modes revision 6 (`design/storage-profiles.md`): Revision 6: carries the accepted DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- Templates: what a piece of work contains, and how each part is made and judged revision 11 (`design/templates.md`): Revision 11: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) and DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- Vocabulary: the single register of the estate's terms revision 20 (`design/vocabulary.md`): Revision 20: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) and DESIGN-26 (decision 01M6D1Z7Q4NZHRHS5QB0K8Z4T3) as they bear on this document.
- web-domain revision 3 (`design/web-domain.md`): Revision 3: accepted with all five recommendations of § 16 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6), its build waiting in the future-work backlog for the 0.10.x releases.
- Vericairn.com and Vericairn.org revision 11 (`design/websites.md`): Revision 11: carries the accepted DESIGN-25 (decision 01M6D1Z0CTBHMJTTXJJP09VBC6) as they bear on this document.
- Vericairn roadmap revision 79 (`design/roadmap.md`): Revision 80: carries FIX-84 in 0.9.0 (decision 01M6D67FEK9F0E32Y2G9BY4MXT) and FIX-85 in 0.9.1 (decision 01M6D67PYH0W0H7RV6DDWYQZPS).
- Vericairn.com and Vericairn.org revision 12 (`design/websites.md`): Revision 12: carries the case-study decision (decision 01M6DBHSAZMD5JZYNTE1S1HTTZ): how the holding vericairn.com carries case studies of the operator's ventures before M4's use-case library, Nikki & Jayson's first.
- Vericairn roadmap revision 80 (`design/roadmap.md`): Revision 81: carries FIX-87's placement in 0.9.1 (decision 01M6DBJHDD2Z17RBJ1C9YVMTPF).
- Vericairn roadmap revision 81 (`design/roadmap.md`): Revision 82: carries OPS-11, FIX-86 and DESIGN-35 in 0.9.1 and DESIGN-36 and REMOTE-12 in the future-work backlog (decisions 01M6DCQCJ46S85R1CR8GTC1GKN, 01M6DCQMMRBK8WEFBRVF8BDT1P, 01M6DCQWMBZGREDFQD9EG2NRN9, 01M6DCR4G97TM2N7MTWDY2G31X, 01M6DCRCCTJ46A0VH2T09CW862).
- conversation-intake revision 4 (`design/conversation-intake.md`): Revision 4: carries OPS-11 as the interim advisory reader (decision 01M6DCQCJ46S85R1CR8GTC1GKN) and DESIGN-35 as the outward direction (decision 01M6DCQWMBZGREDFQD9EG2NRN9).

## The release itself

None in this window.

## Other releases recorded in this window

- 0.8.4 candidate (01M5XDN6XFNT9G7PH5SNT8J5W9, 2026-10-27 00:55 UTC); signing: unsigned developer build; qualified: linux-x64, osx-arm64, win-x64.
- 0.8.4 published (01M61R2J5NY521VVDNEMPX32Y2, 2026-10-28 17:14 UTC); signing: unsigned developer build; qualified: linux-x64, osx-arm64, win-x64.

## Upgrading

- Migration 01M61R3MZ976Z23A617MM4FMQZ: from release 0.8.3 to release 0.8.4, record format 3 to 3; migrations applied: none; changes: estate/manifest.json pinned to release 0.8.4 with the artifact hashes of release 01M61R2J5NY521VVDNEMPX32Y2.

An estate on 0.8.4 upgrades with `vericairn estate upgrade --to 0.9.0 --by <principal>` run with the 0.9.0 binary once the published release record is in the estate; `--check` shows the plan first.
