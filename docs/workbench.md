# Workbench

`vericairn-workbench` is the desktop: a client of the daemon through the SDK and the runtime's manager, never its host. It opens no live store and evaluates no authority; everything it shows comes from the runtime's responses, rendered with the same state language as the command line (the golden file at `docs/state-language.json`, checked by tests against the presentation library and the runtime's own strings).

## What exists after WB-02

- The shell (WB-01): primary navigation on the left (Home, Work, Products, Releases, Runtime, Records), documents in the center as tabs, the inspector on the right, the activity log at the bottom, the target context on top (estate, pinned release, runtime state, acting principal, freshness) and a status line. Panels resize with splitters and hide, show and reset with keyboard alternatives; the layout is a per-user preference saved under the Vericairn home.
- First run and returning user: open an estate's root (recent estates are remembered); a directory without `estate/manifest.json` is refused and nothing is created; when no daemon answers, Workbench starts one through the command line beside it and reconnects.
- Home: attention items of the six kinds, derived from the status document alone, each with why it matters, its state, freshness and the next action; selecting explains, Enter opens the subject. A result awaiting review opens in the review workspace.
- Acting principal: chosen among the admitted devices held on this machine (the target bar's chooser), or named with `--as` for a principal whose device the runtime holds. Without one, every write surface says so before any request is sent; Workbench never evaluates authority itself.
- Products: the product records with their editions and surfaces. Projects are not a record type of format 1; the page says so instead of inventing them.
- Work order pages: the brief (key, title, kind, intent, exit question), the assignments with their attempt counts and latest results, the observation, and the actions that start the loop (assign, review the selected result, compare with the earlier revision). Open one by key or id with Ctrl+G.
- Assignment pages: the brief as delegated, the assigning note, and the attempts with their workspace, note and result (outcome, summary, state); Enter on an attempt opens its result for review.
- Assignment composer: the brief's fields, one list entry per line; nothing is sent until "Send the assignment", the runtime validates and signs, and a refusal is shown with every finding.
- Result inbox (Ctrl+I): the results awaiting review across the estate, each a claim to inspect; Enter opens the review workspace.
- Review and decision workspace: the exact result (outcome, summary, outputs, evidence, checks, limitations, unresolved effects, next step), the reviews on record, a review draft (outcome, summary, findings) and an acceptance draft whose basis is the checked reviews. Both are sent through the API pinned to the subject's hash; the independence and self flags the client will declare are derived from who authored the subject and shown, and the runtime checks them.
- Prepared actions: a draft is pinned to the subject revision and to the facts on record about it when it was prepared; returning to it re-fetches both and reports reusable (nothing changed), needs review (new records about the subject appeared; the draft is kept, nothing is sent) or withdrawn (the subject was superseded or is gone; the draft stays readable, nothing can be sent). A write of the workspace's own re-pins it, since the person has just seen that record.
- Comparison views: a record beside the revision it supersedes, with the line difference between them; the raw sides stay readable.
- Runtime: start, stop and the install plan through the command line; the daemon's state is always in the target bar.
- Records: open any record by id.

## Keyboard

Ctrl (Cmd on macOS) with 1, 2, 3, 4 focuses navigation, documents (or the attention list on Home), the inspector and the activity log. Ctrl+Shift+L, R, B toggle the navigation, inspector and activity panels; Ctrl+Shift+0 resets the layout. Ctrl+O focuses the estate path; Ctrl+G the record or work-order id; Ctrl+I opens the result inbox; F5 refreshes; Enter opens the selected item (an attention item, an inbox row, an assignment's latest result); Escape returns to navigation. The Work menu carries the same actions for the open document. Every pointer arrangement has one of these.

## Running it

```sh
vericairn-workbench --estate <estate root> [--as <principal id>]
```

The command line is found beside the executable, through `VERICAIRN_CLI`, or on the path; Workbench uses it to start and stop the daemon. The accessibility probe measures every focusable target of the shell at compact density and writes an observation for the platform obligations:

```sh
vericairn-workbench --probe <file.json> --cell "macOS native" [--virtual]
```

## Limitations after WB-02

The release and runtime workspaces, keyboard-only journey runs on real platforms, accessibility checks beyond the target probe, the brand and Workbench packaging arrive with WB-03 and WB-04. Projects wait for the record format to define them. The comparison is a line difference of canonical JSON, not a field-aware merge. The theme binds the brand's token roles to neutral values until design/brand.md revision 2. Docking is panel placement with keyboard alternatives, not free drag-and-drop of every panel. No platform has a recorded native qualification for Workbench yet; the headless tests, including the loop from a composed assignment to an acceptance, run on three platforms and the probe has run where the platform obligations say.
