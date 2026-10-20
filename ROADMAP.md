# Vericairn roadmap

This page shows where Vericairn is headed, release by release. Everything after the current published
release is planned, not promised, and has no dates. Each release's notes and documentation set are the
record of what shipped.

## 0.8.0 — current published release

Estates can declare policies and recurring maintenance duties that help keep required work current, and
the release gate holds a release on the policies that apply to it; an upgraded estate adopts them
explicitly. Readable views make records easier to inspect, and client operations can be cancelled or
given deadlines. The release gate reads documents kept in other repositories at those repositories' own
commits. Commands show what needs the operator's attention, with the exact command to run, and carry
authorized work through checked pull requests and merges. Upgrading moves an estate to record format 3,
which 0.7.3 refuses, so every machine that writes to it upgrades together. Its
[release notes](docs/releases/0.8.0/release-notes.md) and [documentation set](docs/releases/0.8.0/README.md)
describe what shipped and its limits.

## 0.8.1 — planned

Commands record exact document revisions and observe NuGet publication, listing and deprecation, and the
packages carry the cairn mark as their icon. An optional efficiency adapter helps reduce eligible agent
context. Every client reports a record as stored, not refused, when publishing its readable companions was
interrupted; the policy guides describe policy implementation identity accurately; and the estate check
ignores top-level files the repository's own ignore rules exclude. The test suites run split across
parallel jobs and no longer depend on real time, and CI runs the release's packaging steps on every change.
Vericairn's MCP server is listed in the official MCP Registry and among nuget.org's MCP servers, where
coding agents' users look for servers.

## 0.8.2 — planned

Reusable templates give documents, roadmaps, communication, context, commits and reviews consistent
sections and checks. Saved prompts, each with a title, description, category, placeholders and the roles
it suits, can be shared across an estate, kept as personal favorites, and used with new agent runs,
assignments and answers to an agent's questions, from the command line or as prompts in coding agents that
support them. Vericairn and its packs include built-in prompts, which anyone can hide from their own lists
and an estate can disable or copy into its own library. A first estate needs no hand-written JSON:
`estate init` pins the build you run, and a new command creates a work order from its options.

## 0.9.0 — planned

The Remote companion brings supervision, eligible decisions and readable records to a phone, with
handoff to the desktop for work that needs it. From Workbench or the phone, a saved prompt can be sent to a
running agent in one step; it steers the agent without widening what the agent may do.

## Later milestone: coordination, packages and websites — planned

Coordination and scheduling across work, extension packages and foundations for a package registry.
Readers for Knowledge and the catalog, and websites generated from the same records, including this
roadmap.

## Later milestone: specialist workflows — planned

Workflows and domain templates for books, Unity projects and courses.
