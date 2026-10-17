# Communication and continuity qualification

COMMS-02 for 0.7.0, exercised in two rounds on 2026-10-16. Round one
started at `c420900` on `work/comms-02`, with COMMS-01, CONTEXT-01,
HANDOFF-01, WATCH-01 and FIX-10 present. The builder reported that PR 87's
corrected three-platform run passed and merged it. Round two merged
`origin/main` at `cfd166c` (including RUN-01 and RUN-02 from PR 88) as merge
commit `5a9437a`, retaining both histories. This report is evidence for review,
not milestone or release acceptance.

The source cases are DESIGN-08, `design/communication-and-context.md`, section
13 in the estate. Round two reads revision 5
(`sha256:e86a4b4c2002efbbbc69faff699957534b8314040ccfbe574bd2252bdc4998dd`);
round one read revision 4
(`sha256:c23dede8968774513689c43c24907b79c6c5790aa74a862e66dc5efe07751c88`).
The work order is `01M526EX8HTM50MR2J0DBMXXQJ`, assignment
`01M5324YFHWJWX1XBMR545V306`. The attempts are
`01M5325JD0HTYNWNPK5V12ZBJ0` (round one) and
`01M537TR608K7ND9JQ8ZPYKSEE` (round two).

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
| D08-E: interrupted execution and inventory changes | Passed | Stand-in dispatch is interrupted both before and after its dispatch record is saved. A successor retains the original identity, context and reservation, records uncertainty, refuses retries on both branches, and reconciles that operation before admitting a fresh run. Both native launchers refuse an inventory changed after admission, preserve the failed intent and require fresh admission. Reported model mismatches stay critical at every depth, with requested and observed values separate. |
| D08-E: compaction and tokenizer/model semantics | Deferred | Follow-up COMMS-02 harness qualification must exercise actual compaction and provider-conversation recovery. EFF-01 in 0.8.0 owns tokenizer/model-fit qualification. Stand-ins report model facts but perform no inference, compaction or tokenization; exact model revision, token counts and model fit stay unknown or unclaimed. |
| D08-F: concise reviews and artifacts | Passed | Short work observations and Workbench reviews retain every finding and every unperformed check, including their explanations. CLI/MCP exports and API/SDK reads preserve canonical original records and decision words. Commits describe the behavior and validation without requiring the chat; independent assessment of their meaning remains outstanding. |
| D08-F: template protection and old instances | Deferred | TPL-COMMS-01, 0.8.0 or later: generated and quoted fields, locked sections, versioned instances and no silent upgrade. No template check is reported passed. |
| HANDOFF-01 unchanged | Passed | All 12 combinations of three depths and four frequencies retain hand-over refusal, no overwrite, mandatory overflow, invalid-package rejection and resume changes. Reads create no observations, operations or acceptances. |
| WATCH-01 unchanged | Passed | All 12 combinations retain worker-observed repetition counts, warning/critical signals, attention asks, containment and original operation records across reopening. Only the test stand-in is launched. Workbench summaries retain worker signals and pending-attention limits at every combination while optional claims may be quiet. |

## Reproducible coverage

This table records all COMMS-02 qualification and regression cases through
round two. Theory rows count as individual test cases; the two 12-row matrices
cover every supported depth/frequency pair. Round one added 50 cases, its
Workbench CI follow-up added three, and round two adds 21.

| Test file | Coverage | Cases |
|---|---|---:|
| [CLI communication qualification](https://github.com/Vericairn/vericairn/blob/4ac21bc/tests/Vericairn.Cli.Tests/CommunicationQualificationTests.cs) | D08-A, D08-B, D08-C; CLI, embedded API, MCP tool registry and briefs | 4 |
| [Daemon communication qualification](https://github.com/Vericairn/vericairn/blob/4ac21bc/tests/Vericairn.Daemon.Tests/CommunicationQualificationTests.cs) | D08-A, B, C, E and F; real daemon API/SDK, signed requests, restart and raw unauthorized responses | 6 |
| [Workbench communication qualification](https://github.com/Vericairn/vericairn/blob/4ac21bc/tests/Vericairn.Workbench.Tests/CommunicationQualificationTests.cs) | D08-A, B, E and watch presentation | 4 |
| [CLI continuity journeys](https://github.com/Vericairn/vericairn/blob/4ac21bc/tests/Vericairn.Cli.Tests/CommunicationContinuityJourneyTests.cs) | D08-E; original preservation, loss and revoked MCP reads | 3 |
| [Workbench context refusals](https://github.com/Vericairn/vericairn/blob/4ac21bc/tests/Vericairn.Workbench.Tests/ContextRefusalTests.cs) | Both existing work/assignment routes preserve missing-original refusals | 2 |
| [Workbench review findings](https://github.com/Vericairn/vericairn/blob/4ac21bc/tests/Vericairn.Workbench.Tests/ReviewFindingsTests.cs) | Existing and newly submitted findings in actual headless controls, at all depths and two window sizes | 6 |
| [CLI review observations](https://github.com/Vericairn/vericairn/blob/4ac21bc/tests/Vericairn.Cli.Tests/ReviewObservationTests.cs) | Complete findings/checks at all depths; canonical decision/review artifacts | 4 |
| [Hand-over invariants](https://github.com/Vericairn/vericairn/blob/4ac21bc/tests/Vericairn.Cli.Tests/CommunicationHandoffInvariantTests.cs) | HANDOFF-01 across all profiles | 12 |
| [Watcher invariants](https://github.com/Vericairn/vericairn/blob/4ac21bc/tests/Vericairn.Runtime.Tests/CommunicationWatchInvariantTests.cs) | WATCH-01 with the stand-in across all profiles | 12 |
| [Bounded run journeys](https://github.com/Vericairn/vericairn/blob/4ac21bc/tests/Vericairn.Runtime.Tests/CommunicationRunJourneyTests.cs) | D08-D; both native launcher adapters at all depths, measured intent before dispatch, requested/observed values, unknown usage and eight pre-dispatch refusals | 14 |
| [Execution recovery journeys](https://github.com/Vericairn/vericairn/blob/4ac21bc/tests/Vericairn.Runtime.Tests/CommunicationRunRecoveryTests.cs) | D08-E; both interruption boundaries, both native inventory rechecks and reported model mismatches | 6 |
| [Run admission regression](https://github.com/Vericairn/vericairn/blob/4ac21bc/tests/Vericairn.Records.Tests/AgentRunLedgerTests.cs) | Uncertain assignments cannot retry; later reconciliation does not rewrite earlier admission | 1 |
| **Total new cases** | | **74** |

Existing suites also cover profile precedence/conflicts, protected kind/depth
renderings, exact context budget boundaries and accounting digit boundaries,
pinned identity tampering, binary originals, hand-over corruption, worker
silence, burn-rate and invocation auditing. RUN-01/RUN-02 add execution-policy
precedence, exact revision restrictions, retained context/allocation boundaries,
native configuration and inventory refusals, and dispatch rechecks. These remain
part of the whole-suite run. MCP journeys call the production tool registry; existing MCP tests cover
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
| `00727cb` | An uncertain assignment could launch again on another branch with spare concurrency/budget. Shared admission now requires reconciliation under the original identity before another run, including through record authoring. Both interrupted-dispatch journeys reproduced the bypass; their fixed paths and all 48 ledger cases pass. |

The initial whole-suite run also exposed a timing assumption in the existing
worker token-budget test, corrected in `7590bc7`: the run settles before the
separate goal-status record is written. The test now waits for that second
write with its existing bounded wait, then retains the exact status and usage
assertions. Runtime behavior was not changed for this test correction. The
builder applied the same bounded goal-status wait to the generic-launcher cap test in `1e42e8e`;
round two preserves both corrections.

COMMS-02 changes no API v1 schema or record format. The round-two fix strengthens
format 2 admission for an uncertain assignment; it adds no record fields. The
MCP tool list and input schemas are unchanged by COMMS-02, so this work requires
no orientation regeneration. No lock, package, project or frozen release
document changed.

## Validation

Round-two code at `81c02b5` was validated on macOS arm64 with .NET SDK 10.0.303.

| Check | Round-two result |
|---|---|
| Offline Release build | Passed; zero warnings and zero errors |
| Whole suite | Passed; 1,270 total, 1,258 succeeded, 12 skipped, zero failed; 7 minutes 5 seconds |
| New round-two qualification/regression cases | All 21 passed; included in the whole-suite total |
| All COMMS-02 qualification/regression cases | All 74 passed; includes both rounds and the Workbench CI follow-up |
| Repository checks | Clean whitespace diff; all 12 report links resolve; no machine paths, secrets or prohibited file changes in the diff |

The Records, Daemon, CLI, Runtime and Workbench modules all passed. The twelve
skips have the same platform/helper/opt-in reasons listed below. No real coding
agent or external network service ran. The builder's three-platform validation
of these new commits remains outstanding.

Historical round-one validation at `7590bc7`, on the same platform and SDK:

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
The builder subsequently reported that the corrected three-platform run passed
and merged round one as PR 87. Round-two changes still need their own builder
three-platform run.

## Round two: execution and recovery evidence

Commit `46ec2a1` adds 14 bounded-run cases. The six native-launcher/depth
combinations inspect the saved intent at the command boundary, before dispatch.
The intent pins the selected model and supported effort, their run-scope origin,
the disabled adapter, admitted inventory, retained context hash and byte/token
allocations. The stand-ins report the model and effort they received; the other
execution facts remain explicitly unknown. They supply no token usage, and the
full token cap is charged. Eight separate cases refuse adapter requests,
unallowlisted hooks, tools and environment needs, unsupported effort, and
model/provider/route fallback without recording an intent or dispatching.

The tests compare every original run revision and the retained context bytes
across all view depths, along with canonical runs, status, progress and hand-over
output. Each launch also starts with the person's recorded depth and `none`
frequency. No profile is an execution-policy input.

Commit `00727cb` fixes the reproduced reconciliation bypass and adds two
interrupted-dispatch journeys plus a record-admission regression. Dispatch is
interrupted both before and after saving its dispatch record. Supervision ends
while the stand-in remains alive. The successor records uncertainty under the
original operation, and retries on either branch are refused despite spare
budget and concurrency. Only after the stand-in's exit is observed and that
operation is reconciled can a new intent launch. Lost observation alone does
not rewrite a run status; this journey explicitly records the uncertainty.
Missing wall-time usage stays
unreported and charged at the original cap. Resume exposes the changed operation
revision; the original intent, execution selection and retained context bytes
survive. This is explicit reconciliation through record authoring, not automatic
recovery of a provider conversation.

Commit `81c02b5` adds the remaining four execution-recovery cases. Each native
launcher refuses a workspace instruction changed between admission and dispatch;
the failed intent retains the admitted inventory, and restoring the source still
requires a fresh intent. No process was dispatched in that case, so its zero
usage is an observed pre-launch failure, unlike missing usage after launch.
Both native event readers also retain a reported model discrepancy as critical,
including at short depth and `none` frequency, without rewriting the request or
claiming a known exact revision, token count or model fit.

All 21 new round-two cases passed in targeted Release runs and the whole suite. No real coding
agent, model provider, tokenizer or compaction engine ran. The full suite covers
the existing native admission tests after their shared stand-in setup was
extracted for reuse.

## Remaining gaps and next step

1. **Adapter behavior — EFF-01, 0.8.0, with follow-up COMMS-02 qualification.**
   The disabled path is passed. Eligible-text reduction, protected payload and
   tool-argument bypass, telemetry/extra-routing controls, adapter failure/removal,
   changed model/tokenizer behavior and percentage savings remain deferred.
   Byte accounting is not a token count or proof of model fit.
2. **Actual compaction and model semantics — follow-up COMMS-02 harness
   qualification, and EFF-01 for tokenizer/model-fit behavior.** The stand-ins
   exercise interruption, explicit reconciliation and reported model mismatch,
   but do not compact, tokenize, recover a provider conversation or execute a
   model. No inference about those behaviors follows from the event contract.
   Hand-over remains a record-only selection: source-file contents, output
   objects, unrecorded conversation and external health are outside it. Missing
   originals are not reconstructed from summaries.
3. **Remote — not applicable while unbuilt.** No mobile profile synchronization,
   phone full-evidence display or phone hand-off is claimed. Requalify through
   COMMS-02 with the Remote implementation and return this exclusion to the
   operator.
4. **Templates — TPL-COMMS-01, 0.8.0 or later.** Generated/quoted fields, locked
   sections, versioned instances and protection against silent migration remain
   deferred; no template check is counted as passed.
5. **Workbench context controls — not applicable while absent.** There is no
   context-allocation editor or pinned-original retrieval control. Future
   CONTEXT-01 follow-up controls need COMMS-02 qualification. Existing refusal
   routes and actual review text reachability are covered; a manual desktop
   usability review remains outstanding.
6. **Harness versions and live services — RUN-03 and follow-up COMMS-02
   qualification.** The shipped native inventory adapters remain qualified for
   their specific compiled versions with stand-in evidence. Other versions,
   real logins, live model/provider combinations and live service behavior are
   not claimed. RUN-03 owns requalification of updated harnesses. The generic
   production launcher remains refused, as RUN-02 specifies.
7. **Independent validation at the report's hand-off.** Round two required the builder's Linux/macOS/Windows
   CI run and independent review of meaning and task sufficiency. Optional live
   probes and unavailable platform checks remain skips, not passes. The local
   merge was authorized for this round; nothing was pushed, published or accepted.

At that hand-off, the requested next steps were review of the merge and round-two commits, the
reconciliation fix and this report, followed by the three-platform suite and presentation of the Remote,
adapter, compaction and template exclusions to the operator. Every design case had a passed,
not-applicable or named deferred disposition; D08-D had no remaining unimplemented run case.
This report retains those observations at their stated commits, not a new qualification of 0.7.1.
Acceptance remains the operator's decision.
