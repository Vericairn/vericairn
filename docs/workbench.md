# Workbench

`vericairn-workbench` is the desktop: a client of the daemon through the SDK and the runtime's manager, never its host. It opens no live store and evaluates no authority; everything it shows comes from the runtime's responses, rendered with the same state language as the command line (the golden file at `docs/state-language.json`, checked by tests against the presentation library and the runtime's own strings).

## What exists after WB-01

- The shell: primary navigation on the left (Home, Work, Products, Releases, Runtime, Records), documents in the center as tabs, the inspector on the right, the activity log at the bottom, the target context on top (estate, pinned release, runtime state, acting principal, freshness) and a status line. Panels resize with splitters and hide, show and reset with keyboard alternatives; the layout is a per-user preference saved under the Vericairn home.
- First run and returning user: open an estate's root (recent estates are remembered); a directory without `estate/manifest.json` is refused and nothing is created; when no daemon answers, Workbench starts one through the command line beside it and reconnects.
- Home: attention items of the six kinds, derived from the status document alone (submitted work awaiting acceptance, results awaiting review, blocked results, release candidates, uncertain operations, failed checks, reconcile tasks, warnings), each with why it matters, its state, freshness and the next action; selecting explains, Enter opens the subject.
- Runtime: start, stop and the install plan through the command line; the daemon's state is always in the target bar.
- Records: open any record by id; work orders open as their observation and brief.

## Keyboard

Ctrl (Cmd on macOS) with 1, 2, 3, 4 focuses navigation, documents (or the attention list on Home), the inspector and the activity log. Ctrl+Shift+L, R, B toggle the navigation, inspector and activity panels; Ctrl+Shift+0 resets the layout. Ctrl+O focuses the estate path; Ctrl+G the record id; F5 refreshes; Enter opens the selected item; Escape returns to navigation. Every pointer arrangement has one of these.

## Running it

```sh
vericairn-workbench --estate <estate root>
```

The command line is found beside the executable, through `VERICAIRN_CLI`, or on the path; Workbench uses it to start and stop the daemon. The accessibility probe measures every focusable target of the shell at compact density and writes an observation for the platform obligations:

```sh
vericairn-workbench --probe <file.json> --cell "macOS native" [--virtual]
```

## Limitations after WB-01

Products and projects, the assignment composer, the result inbox, the review and decision workspace, comparison views and the release workspace arrive with WB-02 and WB-03; the shell's documents are text until then. The theme binds the brand's token roles to neutral values until design/brand.md revision 2. Docking is panel placement with keyboard alternatives, not free drag-and-drop of every panel. No platform has a recorded native qualification for Workbench yet; the headless tests run on three platforms and the probe has run where the platform obligations say.
