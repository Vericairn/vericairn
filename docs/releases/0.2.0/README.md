# Vericairn 0.2.0

Vericairn 0.2.0 is the headless Vericairn: a command-line tool, a daemon with a versioned API, and a typed client library that together keep an estate honest. An estate is the continuing body of work and responsibility behind a venture, held as append-only, content-addressed, signed records: principals and devices, decisions, work orders and briefs, assignments, attempts and results, reviews and acceptances, releases and migrations, resources, verifications and operations. The tool renders the estate's state from those records, refuses records that would make the estate invalid, and runs the loop Vericairn builds itself with: work under the pinned release, release the next version through a provider with intent recorded before effect, accept it from the recorded evidence, upgrade the estate, repeat. Workbench and Remote are not in this release; the first-party roadmap places Workbench in 0.3.0.

Release 0.2.0 was built from commit 2ca88ac7ea1ec4f9279ac728000a329c3339c432 on October 13, 2026, with its tests run on Ubuntu, macOS and Windows, and published the same day after the operator accepted the candidate from the recorded evidence. It is an unsigned developer build; verify downloads by checksum (see `install.md`).

## This documentation set

This set was written on October 13, 2026, after the release day: the references were generated from the 0.2.0 binary itself and the release notes from the Vericairn estate's records, so they describe exactly what shipped; the guides were written afterwards against that binary. It is frozen with the release and will not be edited; corrections go into the next version's set. From 0.3.0 the set is produced before the release build and ships as a release asset.

| Document | What it answers |
|---|---|
| `install.md` | How to install 0.2.0 on Linux, macOS or Windows, through an estate's bootstrap or by direct download, and how to verify it |
| `operator-guide.md` | How an operator and the agents working for them use the tool: create an estate, devices and admission, the work loop, reviews and acceptances, decisions, releases, upgrades, recovery, the daemon |
| `cli-reference.md` | Every command and verb with its arguments and options (generated) |
| `api-reference.md` | Every operation of API v1 with its parameters, bodies and responses (generated) |
| `record-reference.md` | Every record type with its subject rule and body fields (generated) |
| `record-format.md` | Record format 1: the envelope, canonical form, signatures, where records live, the enforced rules (frozen copy) |
| `api.md` | The API: one owner per estate, transports, outcomes and problem codes, the runtime command family, the SDK (frozen copy) |
| `software.md` | The software journey: the git adapter, checks as evidence, the GitHub Releases provider, the synthetic migration (frozen copy) |
| `release-notes.md` | What changed since 0.1.0, from the estate's records (generated) |
| `limitations-and-platforms.md` | Qualified platforms, signing, known limitations, every artifact by hash |
| `manifest.json` | The set's version, commit, documents, commands, operations and record types, used by `vericairn docs check` from 0.3.0 |

## Are you running 0.2.0?

```sh
vericairn version
```

prints `0.2.0+2ca88ac7ea1ec4f9279ac728000a329c3339c432`. `vericairn doctor` prints the version, the runtime, whether git and the .NET SDK are present, the estate it sees, the key custody on this platform and whether a daemon serves the estate.

## Where to get it

The distributables, the software bill of materials, the third-party notices and `SHA256SUMS` are on the release page, https://github.com/Vericairn/vericairn/releases/tag/v0.2.0. The source is the `vericairn` repository at the commit above, under the Apache License 2.0; copyright (c) 2026 Jason Furr.
