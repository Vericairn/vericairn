# Vericairn 0.3.0

Vericairn 0.3.0 adds the desktop to the headless Vericairn of 0.2.0. Workbench is a client of the daemon through the SDK and the runtime's manager, never its host: it opens an estate, shows what needs a person (attention), the products, the work orders with their briefs, assignments, attempts and results, composes assignments, reviews results and records reviews and acceptances through the API pinned to the exact hash, compares a record with the revision it supersedes, keeps a prepared action pinned to its subject, and runs the release flow as intent before effect, leaving publication to the operator's hand. Every action has a keyboard route. The command line gains the `docs` family (a release's documentation set, generated from the build and the records, checked before any release is drafted) and the `mcp` family (an estate exposed to a coding agent over the Model Context Protocol, with an orientation file generated from the records). The API gains `GET /v1/records?type=` and the release operations `POST /v1/releases/submit` and `observe`, so the command line, Workbench and any client drive one implementation of the release flow in the runtime. Remote is not in this release; the first-party roadmap places it in 0.4.0.

An estate is the continuing body of work and responsibility behind a venture, held as append-only, content-addressed, signed records: principals and devices, decisions, work orders and briefs, assignments, attempts and results, reviews and acceptances, releases and migrations, resources, verifications and operations. The tool renders the estate's state from those records, refuses records that would make the estate invalid, and runs the loop Vericairn builds itself with: work under the pinned release, release the next version through a provider with intent recorded before effect, accept it from the recorded evidence, upgrade the estate, repeat. Release 0.3.0 is the third time that loop has run on the first-party estate.

Release 0.3.0 was built from the commit its release record names in the Vericairn estate, with its tests run on Ubuntu, macOS and Windows by the release workflow, held as a draft until the operator accepted the candidate from the recorded evidence, and published by the operator's hand. It is an unsigned developer build; verify downloads by checksum (see `install.md`).

## This documentation set

This set was produced on October 14, 2026, before the release build, as the convention in `docs/releases/README.md` requires from 0.3.0 on: the references and the manifest were generated from the 0.3.0 build, the release notes from the Vericairn estate's records since the 0.2.0 publication, and the guides written against the same build. The release's own record (commit, workflow run, artifacts by hash) is written when the draft is observed and lives in the estate, not in this frozen set; `limitations-and-platforms.md` states what was known before the build. The release workflow refuses to draft a release whose set is incomplete, and ships the set as `vericairn-0.3.0-docs.zip`. The set is frozen with the release; corrections go into the next version's set.

| Document | What it answers |
|---|---|
| `install.md` | How to install 0.3.0 on Linux, macOS or Windows, through an estate's bootstrap or by direct download, how to verify it, and how to run Workbench and the MCP server |
| `operator-guide.md` | How an operator and the agents working for them use the command line, Workbench and the MCP server: create an estate, devices and admission, the work loop, reviews and acceptances, decisions, releases, upgrades, recovery, the daemon, the desktop, agents, documentation sets |
| `cli-reference.md` | Every command and verb with its arguments and options (generated) |
| `api-reference.md` | Every operation of API v1 with its parameters, bodies and responses (generated) |
| `record-reference.md` | Every record type with its subject rule and body fields (generated) |
| `record-format.md` | Record format 1: the envelope, canonical form, signatures, where records live, the enforced rules (frozen copy) |
| `api.md` | The API: one owner per estate, transports, outcomes and problem codes, the runtime command family, the SDK, the release operations (frozen copy) |
| `software.md` | The software journey: the git adapter, checks as evidence, the GitHub Releases provider, the synthetic migration (frozen copy) |
| `workbench.md` | Workbench: what exists, the keyboard, running it, the probe, limitations (frozen copy) |
| `mcp.md` | The MCP server: protocol subset, tools, running it, qualification, limitations (frozen copy) |
| `release-notes.md` | What changed since 0.2.0, from the estate's records, prepared before the build (generated) |
| `limitations-and-platforms.md` | Qualified platforms, signing, known limitations, the artifact set, as known before the build |
| `manifest.json` | The set's version, commit, documents, commands, operations and record types, used by `vericairn docs check` |

## Are you running 0.3.0?

```sh
vericairn version
```

prints `0.3.0+<commit>`, the commit the release record names. `vericairn doctor` prints the version, the runtime, whether git and the .NET SDK are present, the estate it sees, the key custody on this platform and whether a daemon serves the estate. Workbench shows the estate's pinned release and the runtime's state in its target bar.

## Where to get it

The distributables for the command line and for Workbench, the software bill of materials, the third-party notices, this documentation set and `SHA256SUMS` are on the release page, https://github.com/Vericairn/vericairn/releases/tag/v0.3.0. The source is the `vericairn` repository at the commit the release record names, under the Apache License 2.0; copyright (c) 2026 Jason Furr.
