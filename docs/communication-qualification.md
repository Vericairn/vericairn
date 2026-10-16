# Communication and continuity qualification

COMMS-02, first round for 0.7.0, exercised on 2026-10-16. The work starts at
`c420900` on `work/comms-02`, with COMMS-01, CONTEXT-01, HANDOFF-01, WATCH-01
and FIX-10 present. RUN-01 and RUN-02 are not integrated in this round.
This is evidence for review, not milestone or release acceptance.

The source cases are DESIGN-08, `design/communication-and-context.md`, section
13, revision 4 (`sha256:c23dede8968774513689c43c24907b79c6c5790aa74a862e66dc5efe07751c88`)
in the estate. The work order is `01M526EX8HTM50MR2J0DBMXXQJ`, assignment
`01M5324YFHWJWX1XBMR545V306`, and this round's attempt is
`01M5325JD0HTYNWNPK5V12ZBJ0`.

## Case results

Each passed row is limited to the behavior and surfaces named. Deferred and
not-applicable portions of the same design case are separate rows.

| Case | Status | Exercised behavior or remaining boundary |
|---|---|---|
| D08-A: one person across surfaces | Passed | Two people independently choose different depths. CLI, runtime API, daemon/SDK, MCP reads and Workbench resolve their own values and attributed revisions. Assignment and view overrides retain their origins; view choices do not persist. Exported briefs include the recipient profile. Canonical status, brief, hand-over, decision and record artifacts remain unchanged by depth. |
| D08-A: Remote profile | Not applicable | Remote is unbuilt. No mobile profile or synchronization claim is made. Requalify with Remote implementation and a COMMS-02 follow-up. |
| D08-B: mandatory attention | Passed | At `none` frequency and short depth, uncertain effects, failed checks and an open ask remain visible through CLI, API/SDK, MCP reads and Workbench. The ask retains its recommendation first, both alternatives and the consequence of no answer. Reading neither answers it nor accepts work. |
| D08-B: phone evidence and hand-off | Not applicable | Remote is unbuilt. No phone evidence display, hand-off or acceptance behavior was exercised. Return this portion to the operator with the Remote work. |
| D08-C: measured omission | Passed | A large multibyte optional original is withheld under a byte allocation. CLI and MCP preparation agree with the API; the generated CLI retrieval command and authenticated SDK retrieval return the pinned bytes. Original and derivative hashes are distinct. Editing the file makes the old pin unavailable and changes the prepared package; an unverified size becomes unknown. Final UTF-8 accounting includes the manifest, withheld entries, wrappers and accounting itself. Mandatory overflow returns no package. |
| D08-C: Workbench context controls | Not applicable | Workbench has no context-allocation editor or pinned-original retrieval control. Its existing work and assignment routes are tested for complete context refusals. A future control needs CONTEXT-01 follow-up implementation and COMMS-02 qualification; API tests are not a claim that the control exists. |
| D08-D: bounded run, adapter disabled | Passed | Round two exercises both native launcher adapters with stand-in executables at all three depths. The saved intent precedes dispatch and pins explicit model/effort, provenance, disabled adapter, admitted inventory, retained context hash and byte/token allocations. Observations keep reported model/effort separate from unknown facts; absent token usage reserves the full cap. All run revisions, retained context and canonical runs/status/progress/hand-over output remain byte-identical across view depths. Adapter requests, unallowlisted hooks, tools and environment needs, unsupported effort and model/provider/route fallback are refused before intent or dispatch. |
| D08-D: adapter optimization | Deferred | EFF-01 in 0.8.0 owns eligible-text reduction, protected payload/tool-argument bypass, telemetry and routing controls, adapter failure/removal, and model/tokenizer qualification. Only the disabled path is passed here. |
| D08-E: recorded interruption and lossy continuation | Passed | A successor uses saved hand-over bytes after the previous service ends and after an estate copy or daemon restart. Decisions, failed and unperformed checks, original operation identities and exact record bytes survive. CLI, SDK and Workbench resume expose changed record sources and preserve the retained basis. CLI/API/MCP context reads keep missing originals, unknown sizes, unknown tokens and irreversible loss explicit. Revoked MCP and authenticated API readers receive no omission identities, paths, hashes or sizes, including for a previously issued pin. |
| D08-E: harness compaction and execution recovery | Deferred | Actual harness compaction, interrupted dispatch and model/tokenizer mismatch belong to RUN-01/RUN-02 integration and the next COMMS-02 round. These tests model an interrupted recorded workflow; they do not recover a provider conversation. |
| D08-F: concise reviews and artifacts | Passed | Short work observations and Workbench reviews retain every finding and every unperformed check, including their explanations. CLI/MCP exports and API/SDK reads preserve canonical original records and decision words. Commits describe the behavior and validation without requiring the chat; independent assessment of their meaning remains outstanding. |
| D08-F: template protection and old instances | Deferred | TPL-COMMS-01, 0.8.0 or later: generated and quoted fields, locked sections, versioned instances and no silent upgrade. No template check is reported passed. |
| HANDOFF-01 unchanged | Passed | All 12 combinations of three depths and four frequencies retain hand-over refusal, no overwrite, mandatory overflow, invalid-package rejection and resume changes. Reads create no observations, operations or acceptances. |
| WATCH-01 unchanged | Passed | All 12 combinations retain worker-observed repetition counts, warning/critical signals, attention asks, containment and original operation records across reopening. Only the test stand-in is launched. Workbench summaries retain worker signals and pending-attention limits at every combination while optional claims may be quiet. |

## Reproducible coverage

This table records the initial qualification and regression cases at `7590bc7`.
Theory rows count as individual test cases; the two 12-row matrices cover every
supported depth/frequency pair. The CI follow-up below adds three theory rows.

| Test file | Coverage | Cases |
|---|---|---:|
| [CLI communication qualification](../tests/Vericairn.Cli.Tests/CommunicationQualificationTests.cs) | D08-A, D08-B, D08-C; CLI, embedded API, MCP tool registry and briefs | 4 |
| [Daemon communication qualification](../tests/Vericairn.Daemon.Tests/CommunicationQualificationTests.cs) | D08-A, B, C, E and F; real daemon API/SDK, signed requests, restart and raw unauthorized responses | 6 |
| [Workbench communication qualification](../tests/Vericairn.Workbench.Tests/CommunicationQualificationTests.cs) | D08-A, B, E and watch presentation | 4 |
| [CLI continuity journeys](../tests/Vericairn.Cli.Tests/CommunicationContinuityJourneyTests.cs) | D08-E; original preservation, loss and revoked MCP reads | 3 |
| [Workbench context refusals](../tests/Vericairn.Workbench.Tests/ContextRefusalTests.cs) | Both existing work/assignment routes preserve missing-original refusals | 2 |
| [Workbench review findings](../tests/Vericairn.Workbench.Tests/ReviewFindingsTests.cs) | Existing and newly submitted findings in actual headless controls, at all depths | 3 |
| [CLI review observations](../tests/Vericairn.Cli.Tests/ReviewObservationTests.cs) | Complete findings/checks at all depths; canonical decision/review artifacts | 4 |
| [Hand-over invariants](../tests/Vericairn.Cli.Tests/CommunicationHandoffInvariantTests.cs) | HANDOFF-01 across all profiles | 12 |
| [Watcher invariants](../tests/Vericairn.Runtime.Tests/CommunicationWatchInvariantTests.cs) | WATCH-01 with the stand-in across all profiles | 12 |
| **Total new cases** | | **50** |

Existing suites also cover profile precedence/conflicts, protected kind/depth
renderings, exact context budget boundaries and accounting digit boundaries,
pinned identity tampering, binary originals, hand-over corruption, worker
silence, burn-rate and invocation auditing. These remain part of the whole-suite
run. MCP journeys call the production tool registry; existing MCP tests cover
the stdio server. Workbench tests use headless Avalonia, not a manual desktop or
mobile usability session. SDK tests use temporary local sockets and loopback
TLS, not an external service.

Canonical serialization and exact original bytes are the invariants. Transport
JSON whitespace and human introductions/freshness are not treated as canonical
record content. A changed communication-setting record legitimately changes a
later all-record hand-over; invariance checks compare presentations of the same
record set.

## Defects found and fixed

Each fix has its own commit and regression tests observed failing before the
change.

| Commit | Defect and corrected behavior |
|---|---|
| `ad7dc60` | Workbench ignored refused context briefs: a work page became empty and an assignment was reported missing. Both now show the refusal and every finding, including the unavailable required original. |
| `ed3c452` | Workbench showed review summaries without their findings. Existing and newly submitted reviews now display all findings, severity and location at every depth. |
| `656a091` | Work observations omitted review summaries/findings and check explanations. CLI, MCP and API observations now retain all of them, including reasons for unperformed checks. |

The initial whole-suite run also exposed a timing assumption in the existing
worker token-budget test, corrected in `7590bc7`: the run settles before the
separate goal-status record is written. The test now waits for that second
write with its existing bounded wait, then retains the exact status and usage
assertions. Runtime behavior was not changed for this test correction.

API v1 contracts and record format 2 are unchanged. The MCP tool list and input
schemas are unchanged; no orientation regeneration is required by this round.
No lock, package, project or frozen release document changed.

## Validation

Validated code at `7590bc7` on macOS arm64 with .NET SDK 10.0.303.

| Check | Result |
|---|---|
| Offline Release build | Passed; zero warnings and zero errors |
| Whole suite after the worker-test correction | Passed; 1,016 total, 1,004 succeeded, 12 skipped, zero failed |
| New qualification and regression cases | All 50 passed, included in the whole-suite total |
| Repository checks | Clean whitespace diff; all nine report links resolve; no machine paths, secrets or prohibited file changes in the diff |

The twelve skips are three Secure Enclave cases without the helper; five
checks for other platforms (two Windows CNG, one Windows named pipe, one Linux
secret service and one Linux procfs); two optional real-launcher probes; and
the opt-in live GitHub and live-estate cases. These are limits, not passes.
The first whole-suite run had 1,003 successes, one failure and the same twelve
skips; the failure and its test correction are described above. After the final
offline build, the corrected test passed separately before the whole-suite run.

The offline restore uses an empty package-source configuration and the existing
package cache, with locked restore and NuGet auditing disabled for this run.
The Release build uses one MSBuild node, no compiler server and no Avalonia build
telemetry. Those are command-line settings, not project changes. Tests use a
short `TMPDIR` on macOS; daemon and process tests need local IPC/process access.
No real coding agent is launched, and no external network service is used.

After the offline restore, equivalent build/test commands are:

```sh
dotnet build -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:UsedAvaloniaProducts=
dotnet test -c Release --no-build --max-parallel-test-modules 1
```

## CI follow-up: review visibility

PR 87's builder reported that the review-finding test passed on Windows but
failed on macOS and Linux. This was a test artifact: the assertion could run
before the review tab's layout created its text controls. The window-wide
search could also pass on the same original text in Home's hidden attention
items. A minimum-size window alone did not reproduce it locally. Starting in
Work, so Home never realizes those items, and opening and inspecting the review
in one dispatcher turn reproduced all three missing-text failures.

The test now explicitly lays out the window and searches only within the
selected review or its review row. It scrolls each expected text span into view
and checks its character bounds against the scroll viewport and window,
including both unperformed checks, every finding and location, the review
summary, and a newly submitted finding. All three depths are exercised at
720 by 480 and 1200 by 800. The existing view makes every tested span reachable;
no product view change was needed. The other new Workbench tests in this round
do not inspect window visuals and have no window-size or realization dependency.

These three additional theory rows expand the original 50 new cases to 53.
The follow-up offline Release build passed with zero warnings and errors.
The whole suite on macOS arm64 passed: 1,019 total, 1,007 succeeded, 12 skipped
for the reasons listed above, and zero failed. The full Workbench suite also
passed with `CI=true` and `DOTNET_PROCESSOR_COUNT=2`: 48 succeeded, one opt-in
live-estate case skipped. No shared headless settings were changed.
The corrected three-platform CI run remains the builder's next check.

## Round two: bounded execution

RUN-01 and RUN-02 were merged from `origin/main` at `cfd166c` for this round.
The new attempt is `01M537TR608K7ND9JQ8ZPYKSEE`.
[Communication run journeys](../tests/Vericairn.Runtime.Tests/CommunicationRunJourneyTests.cs)
add 14 passing cases: six native-launcher/depth combinations and eight refused
selection or inventory requests. Every executable is a local stand-in. The
checks inspect the intent from inside the pre-dispatch command boundary and
verify retained context accounting before the stand-in runs. The targeted
Release build passed with zero warnings/errors. Whole-suite validation and
D08-E execution recovery follow in this round.

## Remaining gaps and next round

1. **D08-D placeholder — RUN-01, RUN-02, then COMMS-02.** Test explicit model
   and supported effort recorded before launch, admitted effective inventories
   and their recheck immediately before dispatch, refusal of unallowlisted
   hooks/tool environment/fallback, absent usage staying unknown, and failure
   before versus after possible dispatch. This round's WATCH-01 stand-in runs
   do not qualify any of those new execution contracts.
2. **Adapter behavior — EFF-01, 0.8.0, with follow-up COMMS-02 qualification.**
   This round exercises deterministic selection with synthesis off. Eligible
   text reduction, byte-for-byte bypass of protected payloads/tool arguments,
   telemetry/extra-routing disablement, adapter failure/removal, and changed
   model/tokenizer behavior remain unqualified. Tokens and model fit stay
   unknown; byte measurement is not dispatch qualification or a saving claim.
3. **Remote — not applicable until built.** Neither mobile profile behavior nor
   the phone's full-evidence/hand-off path is claimed. The operator must place
   and review that qualification when Remote is available.
4. **Templates — TPL-COMMS-01, 0.8.0 or later.** No generated-field, quote,
   locked-section or old-instance migration protection is qualified here.
5. **Continuity limits.** Hand-over is a record-only selection. Source-file
   contents, output objects, unrecorded conversation and external health are
   outside it. Resume compares record identities; context retrieval separately
   verifies source bytes. A missing original is not reconstructed from its
   summary. Actual compaction and dispatch interruption await the run round.
6. **Other implementations and platforms.** The existing stand-ins qualify
   contract behavior only. No live vendor, harness version range, model/provider
   combination or percentage saving is qualified. The builder's Linux/macOS/
   Windows CI run, a manual Workbench review and independent review of meaning
   and task sufficiency remain outstanding. No push, merge, publication or
   acceptance was performed.

The next builder should run the three-platform suite, review these fixes and
limits, and bring the Remote and template exclusions to the operator. Once
RUN-01 and RUN-02 reach main, continue COMMS-02 at the D08-D placeholder and the
deferred execution portions of D08-E; preserve this round's evidence and add
new results for the newly exercised contracts.
