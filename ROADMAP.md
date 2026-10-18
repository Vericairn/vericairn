# Vericairn roadmap

This page shows where Vericairn is headed, release by release. Everything after the current published
release is planned, not promised, and has no dates. Each release's notes and documentation set are the
record of what shipped.

## 0.7.3 — current published release

Each worker run gets a workspace repository of its own, so a worker-launched Codex run can commit its
changes, which a live run confirmed on macOS. The command line and Workbench show a newer-release notice
with release notes and upgrade guidance. The check runs at most once a day, can be switched off, works
offline and never installs anything. Worker command auditing stops a run on recognized reads of
Vericairn's credentials and on token-printing commands, recognizes more write forms and handles
null-device output correctly; it remains a signal, not an enforcement boundary. Simultaneous connections
to the daemon no longer fail on a freshly loaded or rotated certificate, and status shows only what still
needs attention, with compact, current items. Its [release notes](docs/releases/0.7.3/release-notes.md) and
[documentation set](docs/releases/0.7.3/README.md) describe what shipped and its limits.

## 0.8.0 — planned

Estate policies and recurring maintenance duties help keep required work current. Readable views make
records easier to inspect, an optional efficiency adapter helps reduce eligible agent context, and
client operations can be cancelled. The release gate reads documents kept in other repositories at
those repositories' own commits. Commands show what needs the operator's attention and carry authorized
work through checked pull requests and merges.

## 0.8.1 — planned

Commands record exact document revisions and observe NuGet publication, listing and deprecation.

## 0.8.2 — planned

Reusable templates give documents, roadmaps, communication, context, commits and reviews consistent
sections and checks.

## 0.9.0 — planned

The Remote companion brings supervision, eligible decisions and readable records to a phone, with
handoff to the desktop for work that needs it.

## Later milestone: coordination, packages and websites — planned

Coordination and scheduling across work, extension packages and foundations for a package registry.
Readers for Knowledge and the catalog, and websites generated from the same records, including this
roadmap.

## Later milestone: specialist workflows — planned

Workflows and domain templates for books, Unity projects and courses.
