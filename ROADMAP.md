# Vericairn roadmap

This page shows where Vericairn is headed, release by release. Everything after the current published
release is planned, not promised, and has no dates. Each release's notes and documentation set are the
record of what shipped.

## 0.7.2 — current published release

Integrity fixes bind restored files to their verified inventory, prevent overlapping release dispatch,
validate records before publication and keep unreadable recovery journals visible. The release also
corrects the tested Codex worker path and reduces repeated Workbench startup work. Its
[Security section](docs/releases/0.7.2/release-notes.md#security),
[release notes](docs/releases/0.7.2/release-notes.md) and
[documentation set](docs/releases/0.7.2/README.md) describe what shipped and its limits.

## 0.7.3 — planned

Each worker run gets a workspace repository of its own, so a worker-launched Codex run can commit its
changes. The command line and Workbench show a newer-release notice with release notes and upgrade
guidance. The check runs at most once a day, can be switched off, works offline and never installs
anything. Worker command auditing recognizes credential access and more write forms, and handles
null-device output correctly. Connections stay reliable when several clients connect at once, and status
shows only what still needs attention, with compact, current items.

## 0.8.0 — planned

Estate policies and recurring maintenance duties help keep required work current. Readable views make
records easier to inspect, an optional efficiency adapter helps reduce eligible agent context, and
client operations can be cancelled.

## 0.8.1 — planned

Commands show what needs the operator's attention, carry authorized work through checked pull requests
and merges, record exact document revisions, and observe NuGet publication, listing and deprecation.

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
