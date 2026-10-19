# Workbench

`vericairn-workbench` is the desktop: a client of the daemon through the SDK and the runtime's manager, never its host. It opens no live store and evaluates no authority; estate state comes from the runtime's responses, rendered with the same state language as the command line (the golden file at `docs/state-language.json`, checked by tests against the presentation library and the runtime's own strings). The shell also shows separately labelled machine-local release information.

This guide describes Workbench in 0.8.0: cancellable client commands, readable companion views
and release submissions with an exact commit, expected product hash and visible policy holds.
It retains UPDATE-01's release notices and FIX-22's compact current attention. The shared runtime includes FIX-19's independent worker repositories,
FIX-20's command-audit corrections and FIX-21's concurrent TLS certificate preparation and diagnostics.
Workbench retains the qualification and control limits below.

Optional charter signatures do not waive record-specific requirements. Policy and writing changes,
duty results/reviews, strict generated declarations and the operator one-step action still require
their admitted signatures and exact authority.

## Release notices (0.7.3)

The shell discloses a check for newer published Vericairn releases, including before an estate is open.
At opening and while it stays open, Workbench shares the CLI's cache and limit of one unauthenticated
GitHub request per rolling 24 hours, including failed attempts. The request sends no identifiers,
installed version, estate data or usage telemetry. Network failures are silent and retain cached
knowledge; drafts and prereleases do not count. The check reaches only GitHub, ignores system proxies
and disables cookies, credentials, redirects and trace propagation. It runs asynchronously without blocking the UI.

A newer-release notice gives its version, release-notes link, NuGet tool command, archive download page
and estate upgrade command. Nothing installs or upgrades automatically. Disable requests for both clients
with `vericairn updates configure --enabled false` (or `dotnet vc updates configure --enabled false`).
Use `--enabled true` to re-enable, or omit the option to show the setting. Both clients read the same
machine-local setting under the Vericairn home before checking; cached notices can remain when disabled.
The setting is reread at Workbench's next hourly check or when it is reopened. See the
[operator guide](operator-guide.md) and [installation guide](install.md#release-check-and-upgrades-073).

## What exists

- The shell (WB-01): primary navigation on the left (Home, Work, Goals, Products, Releases, Runtime, Records), documents in the center as tabs, the inspector on the right, the activity log at the bottom, the target context on top (estate, pinned release, runtime state, the acting principal by its current name with the device the runtime signs with, freshness) and a status line. Panels resize with splitters and hide, show and reset with keyboard alternatives; the layout is a per-user preference saved under the Vericairn home.
- First run and returning user: open an estate's root (recent estates are remembered); a directory without `estate/manifest.json` is refused and nothing is created; when no daemon answers, Workbench starts one through the command line beside it, waits for that command to finish (never for the end of its output, which 0.3.0 did and so never opened the estate on a first launch), and reconnects.
- Home: attention items of the six kinds, derived from the status document and, from 0.6.0, from what waits on each principal (`GET /v1/asks`), each with why it matters, its state, freshness and the next action; selecting explains, Enter opens the subject. A result awaiting review opens in the review workspace. In 0.7.3, shared compact attention items retain exact source references, current state and an inspection command. Each delivered ask, at most three a principal, opens its full recommendation, alternatives and no-answer consequence; the item also names the number waiting behind the delivery. Enter on an ask opens the Goals workspace of its goal, or the ask's record when it names no goal; acknowledging or dismissing it answers nothing, since an ask is answered by a decision of the principal asked. Below them, the progress summary: one row for each section of the progress report the runtime renders (`GET /v1/progress`), where the estate stands, the work orders by state, the published releases, what comes next, the next eligible work and what waits on the operator, each counted from the report and worded in the state language. Enter on a row, the button below the rows or Ctrl+Shift+P opens the whole report, every line naming the records it rests on; F5 refreshes the summary and the open report in place. A report the runtime does not give is shown as unavailable, never as empty.
- Goals workspace (Ctrl+Shift+G, or Goals in the navigation), from 0.6.0: the estate's goals with their status, and for the one selected its objective, who sponsors and accepts it and whether it is ready, its conditions with their current verdicts (met, not met or unknown here) beside the current evaluation's, its plan and steps with the principal each reaches from the runtime's machine or what holds it (`GET /v1/goals/{id}/next`), what comes next, spend against its budget from the agent-run ledger with the runs in flight and why a new run would be fenced, its asks with the recommendation first, and its evaluations. The page shows and never acts: it activates, accepts, funds and answers nothing and takes no step, which use the CLI or API under their authority rules (`vericairn ask` only reads; a decision record answers an ask); Workbench never does any of them for an agent. F5 renews it in place.
- Acting principal: chosen among the admitted devices held on this machine (the target bar's chooser), or named with `--as` for a principal whose device the runtime holds. Without a selected device or explicit `--as`, write surfaces request an acting principal. An explicit `--as`
  without a held key is labeled as unsigned or refused according to the estate's signature policy; the runtime
  decides whether the requested write is permitted.
- Products: the product records with their editions and surfaces. Projects are not a record type of either record format; the page says so instead of inventing them.
- Work order pages: the brief (key, title, kind, intent, exit question), the assignments with their attempt counts and latest results, the observation, and the actions that start the loop (assign, review the selected result, compare with the earlier revision). Open one by key or id with Ctrl+G.
- Assignment pages: the brief as delegated, the assigning note, and the attempts with their workspace, note and result (outcome, summary, state); Enter on an attempt opens its result for review.
- Assignment composer: the brief's fields, one list entry per line; nothing is sent until "Send the assignment", the runtime validates and signs, and a refusal is shown with every finding.
- Result inbox (Ctrl+I): the results awaiting review across the estate, each a claim to inspect; Enter opens the review workspace.
- Review and decision workspace: the exact result (outcome, summary, outputs, evidence, checks, limitations, unresolved effects, next step), the reviews on record, a review draft (outcome, summary, findings) and an acceptance draft whose basis is the checked reviews. Both are sent through the API pinned to the subject's hash; the independence and self flags the client will declare are derived from who authored the subject and shown, and the runtime checks them.
- Prepared actions: a draft is pinned to the subject revision and to the facts on record about it when it was prepared; checking it again (Check the subject, or any send) re-fetches both and reports reusable (nothing changed), needs review (new records about the subject appeared; the draft is kept, nothing is sent) or withdrawn (the subject was superseded or is gone; the draft stays readable, nothing can be sent). A write of the workspace's own re-pins it, since the person has just seen that record.
- Comparison views: a record beside the revision it supersedes, with the line difference between them; the raw sides stay readable.
- Runtime workspace (Ctrl+Shift+U): the daemon's state, where it is, what it reports about itself, start and stop, the install plan, and install and uninstall of the per-user service through the command line beside Workbench; the plan is shown before either is possible. The daemon's state is always in the target bar.
- Release workspace (Ctrl+Shift+E): a provider's intent-then-effect flow through the same runtime operations the command line uses (`POST /v1/releases/submit` and `observe`). The provider list is what the runtime can reach (`GET /v1/providers`); the product's declared provider is selected. For a product that declares none, or one the runtime cannot reach, nothing is selected and no setting field is shown until the person chooses, also when the person switches to it from a product whose provider was selected. The form shows one field per setting the selected provider declares, filled with the product's declared values, and an empty field means the provider's default, which its label names. Submit records the intent before the dispatch, observe records the run's outcome, the candidate with every distributable by hash and later the observed publication, each destination shows preparation, submission, external review and observed availability separately, Enter on a destination opens its candidate in the review workspace for acceptance at its exact hash, and the operator's manual publication command is shown and never run. A second submission under the same identity is refused exactly as on the command line. In 0.8.0, enter the prepared exact commit in the Commit field. The request carries that commit and the product hash captured when the product was loaded; a stale product requires closing and reopening the release workspace and preparing again. Format 3 also requires the acting principal's admitted held signing device. The response displays every policy hold and its basis, plus commands to inspect retained release evidence. A refusal causes no build or provider call.
- Documents page (Ctrl+Shift+D): each declared living document with its state (current, due at its level, or unavailable), every trigger newer than its latest clearance, and that clearance (`GET /v1/documents`). A review of the selected document is sent with Ctrl+Enter (`POST /v1/documents/{id}/reviews`): the outcome, what was checked, a commit for a document of another repository, and findings. The runtime reads the content, records what the review answers and runs the declared checks; the page decides neither independence nor clearance, and a refusal shows every reason.
- Keyboard-only: every request of the open document is sent with Ctrl+Enter (assignment, review, release submission, document review) or Ctrl+Shift+Enter (acceptance, observation); Ctrl+Shift+A opens the composer for the open work order; opening a workspace with its shortcut (Ctrl+Shift+E, U, D or A) lands the keyboard in its first field, and Tab moves through the form.
- Theme: the brand's token contract is bound in one resource (`Theme.axaml`): eight color roles in light and dark variants, four type roles, three scale roles and two motion roles. The first values were adopted on October 14, 2026 (design/brand.md revision 2): Inter for title, heading and body and JetBrains Mono for identifiers, both bundled under the SIL Open Font License, one accent blue, one attention amber used only for the attention kinds, neutral surfaces and outlines. The probe measures the contrast ratio of every pair the roles form in both variants (text and muted text on the surfaces at 4.5 or better, outlines at 3 or better) and reports it beside the target sizes. The mark and the wordmark (`brand/`) appear on the arrival page; the shell binds roles, never values.
- Records: open any record by id.

## Readable views (0.8.0)

**Work → Readable views**, or Ctrl+Shift+V (Cmd+Shift+V on macOS), opens the installed
record collections and manifest. Select a collection and press Enter. Opening a
record by id shows its readable rendition beside the exact source envelope;
work-order, review and release pages also provide explicit exact-source buttons.
The Documents page opens its selected declaration's generator. For a retained
software source, supply the exact Commit in that page; no clone-head fallback is used.

Every view shows its snapshot or source commit, input and rendition hashes,
currentness, completeness and page. Source rows label each exact identity, hash,
path, repository, recorded time and lineage. They have complete accessible names;
Enter on a selected record opens that exact revision. Tab reaches the basis,
refresh and page controls, read-only text and source inspector. Reading grants no
review or acceptance, and the guidance directs edits to the authoritative source.

F5 and **Refresh current access and basis** renew permission and collection
selection. Exact item revisions never follow successors. Page buttons retain the
same snapshot and return unavailable when it has changed. A successful refresh
shows a bounded line comparison and the earlier rendition with its own historical
basis. If a comparison exceeds its bound, both complete observed renditions remain
available in separate tabs. A failed refresh, disconnect or access refusal clears
the cached rendition, earlier text and source details and labels freshness unavailable.
If the renderer alone is unavailable, Workbench can still show the exact record
inspector after a separate successful runtime source-access check; it keeps the
readable rendition labeled unavailable.

The reader is a selectable text view with explicitly labeled source rows. Native
screen-reader and desktop keyboard qualification must be observed on each platform;
compilation or headless tests alone do not establish it. The API's Internal-only
projection and local-inspector boundary apply unchanged. See
[readable companions](readable-companions.md) for source and output contracts.

## Personal communication (0.7.0)

Estate → Communication settings opens the recipient, scope and five fields.
Refresh the selected scope to see recorded values, inheritance, revision ids,
hashes, authors, adoption basis, freshness and conflicts. The status, decision,
result and handoff previews use the same examples as the CLI. Save writes as
the selected acting principal; changing another person's setting requires
their explicit delegation. Reset writes a revision with inherited values. A
stale editor retains its draft and reports the conflict; it never overwrites
the other device's change. Estate defaults require estate administration.

Home's current status uses the selected person's effective status depth; an
agent selection uses estate defaults and invents no personal preference.
The adjacent depth chooser changes that status view only and writes nothing.
Work briefs identify the recipient, sources and freshness. Review and handoff
pages change their explanatory lead while keeping the complete original
record or package. Home's periodic refresh filters optional agent claims by
frequency; opening a report explicitly still shows every claim. Required
decisions, failed or unknown checks, blockers, uncertain effects, incidents
and exhausted budgets remain in attention at every frequency, including
`none`. [Required attention](required-attention.md) resolves from current records:
accepted or succeeded results, resolved reviews and settled operations leave the
list, and ordinary `after` ordering does not enter it. Opening a review notice goes
to the reviewed subject. Unavailable settings and attention sources are labeled
explicitly; an undecidable item stays unknown.

Assignment pages and their result reviews read the exact assignment override
for the selected person. Opening another assignment reads its own scope. An
assignment from another work order is unavailable for that work brief, and
full presentation remains visible.

No presentation choice changes a draft's pinned hash, permission, acceptance
or exported handoff bytes. Remote is not built and has no communication UI.

## Keyboard

Ctrl (Cmd on macOS) with 1, 2, 3, 4 focuses navigation, documents (or the attention list on Home), the inspector and the activity log. Ctrl+Shift+L, R, B toggle the navigation, inspector and activity panels; Ctrl+Shift+0 resets the layout. Ctrl+O focuses the estate path; Ctrl+G the record or work-order id; Ctrl+I opens the result inbox; Ctrl+Shift+E the release workspace; Ctrl+Shift+U the runtime workspace; Ctrl+Shift+D the Documents page; Ctrl+Shift+P the progress report; Ctrl+Shift+G the Goals workspace; Ctrl+Shift+H hand-over and resume; Ctrl+Shift+A the composer for the open work order; Ctrl+Enter and Ctrl+Shift+Enter send the open document's requests; F5 refreshes; Enter opens the selected item (an attention item, a row of the progress summary, an inbox row, an assignment, an attempt's result, a release destination's candidate); Escape returns to navigation. The Work and Runtime menus carry the same actions. Every pointer arrangement has one of these.

## Running it

Workbench continues to ship in self-contained archives with `vericairn` beside it. The `dotnet-vc` NuGet
tool, available for a version after its publication workflow succeeds and invoked as `dotnet vc`, installs the command line only; it does not install Workbench or replace
that companion executable. See the [installation draft](install.md).

```sh
vericairn-workbench --estate <estate root> [--as <principal id>]
```

`--as` names the principal Workbench writes as. The selector in the target bar then shows that principal's admitted device held on this machine, the one the runtime signs its records with, and choosing another device in the selector makes that device's principal the acting principal. When the named principal has no device held here, the target bar says so: its writes are refused where the estate requires signatures, and written unsigned where it does not, except an admission, which an estate at record format 2 refuses without a signature. (Release 0.3.1 showed such a principal under its first revision's name beside the first device held on the machine, whoever's it was; the records were still authored and signed correctly.)

The command line is found beside the executable, through `VERICAIRN_CLI`, or on the path; Workbench uses it to start and stop the daemon. The accessibility probe measures every focusable target of the shell at compact density and writes an observation for the platform obligations:

```sh
vericairn-workbench --probe <file.json> --cell "macOS native" [--virtual] [--estate <root> --open products|inbox|release|runtime|goals|<work-order key>]
```

With `--estate` and `--open`, and a daemon already serving the estate, the named workspace is open when the measurement is taken, so each screen's targets are measured on the native desktop, not only the shell's.

## Limitations

FIX-13 in 0.7.2 reduces first-screen work: status and progress observations are reused, startup avoids a
duplicate refresh, and the runtime caches envelope parsing and hashes only while the original bytes are
unchanged. Loading still checks record semantics, signatures and authority; opening validates every record
once. First-screen API reads are bounded independently of the number of records, but large estates can
still take time. Allow loading to finish before treating an absent page as empty. Headless tests establish
neither a fixed startup-time bound nor native desktop responsiveness qualification.

Keyboard-only journeys of the work loop, release flow, Goals workspace and progress report have headless tests in the three-platform workflow; a native keyboard-only run with a screen reader needs a person on each desktop and is recorded per cell in the platform obligations, as are the accessibility measurements. Workbench ships as an unsigned developer build per platform beside the command line, with no bundle, installer or signature. Projects wait for the record format to define them. The comparison is a line difference of canonical JSON, not a field-aware merge. Docking is panel placement with keyboard alternatives, not free drag-and-drop of every panel. The probe's `--open` has no value for the Documents page, the review workspace or the composer, and when no daemon answers yet a probe run can measure before the named workspace is open, so start the daemon first.

Workbench opens estates served on its own machine; it has no paired-connection chooser, pairing form or local-network configuration form. Configure and pair through the command line and use the SDK for operations through a saved connection. The Goals workspace is a read view, with no worker launch or stop controls. Workbench has no context-allocation
editor, pinned-original retrieval control or harness-qualification form. Required context failures remain visible
in its work and assignment routes; all recorded review findings and check explanations remain available at every
depth. The [communication qualification report](communication-qualification.md) records the tested journeys and
separates them from deferred model, tokenizer, template and native-desktop qualification.

## Hand-over and resume (HANDOFF-01)

Open **Work → Hand-over and resume**, the Home button, or Ctrl+Shift+H (Cmd on
macOS). The page prepares the runtime's complete hand-over, with the exact
package identity, byte accounting, derived presentation and complete originals.
Its three tabs show the hand-over, changes since it, and the portable machine
package. No editable recap is requested and no work is submitted.

**Save package** writes a new local JSON file and never overwrites an existing
package. **Open package** loads that same file from the CLI or another Workbench
and asks the runtime to verify it and compare it with this estate. A foreign,
changed or incomplete package is refused; a failed load keeps the previous
basis. **Resume: show changes** and F5 refresh the comparison, preserving the
retained originals and package identity. Only **Prepare new hand-over** explicitly
replaces the in-memory basis. The comparison shows added and superseded records,
missing or changed originals, pin changes and recorded-state changes in full,
and always says the world may have moved. Failed checks and uncertain effects
remain present in the originals. Reopening the page never silently resets its
basis. If the runtime is unavailable, retained content is labeled cached and is
still available to save; no current comparison is claimed.

Home also shows **Last observed by a person**, separate from the client's refresh
time. **Record my observation** (also in the Estate menu) explicitly records an
observation for the selected human principal through the runtime. Opening,
preparing, resuming and refreshing do not advance that timestamp. The runtime
refuses an agent or a principal whose signing authority is unavailable.

The headless journey checks keyboard entry, visible controls, package retention,
save/load, refusals, refresh and the explicit observation. Native file pickers and
screen-reader announcements still need platform qualification. Workbench remains
a client of a daemon on its own machine. Remote is not built; this page makes no
claim about a mobile or remote surface.
