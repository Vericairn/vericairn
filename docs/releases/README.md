# Release documentation sets

Every release of Vericairn carries its own documentation set, one directory per version under `docs/releases/<version>/`. A set is written for the release it documents and then frozen: nothing in a released set is edited afterwards, and a correction goes into the next version's set with a note that says what it corrects. The maintained documents at `docs/` are the drafts for the next release; the frozen copies in a set are the record of what shipped.

## What a set holds

| Document | Written by | What it is |
|---|---|---|
| `README.md` | a person | The overview: what the release is, what the set holds, how to tell you are running it |
| `install.md` | a person | Installation and cold start on Linux, macOS and Windows, by the estate bootstrap and by direct download, with verification |
| `operator-guide.md` | a person | The operator's journeys: create an estate, devices and admission, the work loop, review and acceptance, releases, upgrades, recovery, the daemon |
| `cli-reference.md` | `vericairn docs generate` | Every command and verb with its arguments and options, from the build's own command tree |
| `api-reference.md` | `vericairn docs generate` | Every API operation with parameters, bodies and responses, from the OpenAPI document the daemon serves |
| `record-reference.md` | `vericairn docs generate` | Every record type with its subject rule and body fields, from the record type registry |
| `record-format.md` | a person, frozen copy | The record format document as released |
| `api.md` | a person, frozen copy | The API document as released: transports, outcomes, problem codes |
| `software.md` | a person, frozen copy | The software journey as released: the git adapter, checks, the release provider |
| `release-notes.md` | `vericairn docs notes` | What changed, from the estate's records between the previous publication and this one |
| `limitations-and-platforms.md` | a person, from the release record | Qualified platforms, signing, known limitations, every artifact by hash |
| `manifest.json` | `vericairn docs generate` | The set's version, commit, generator, documents, commands, operations and record types |

## How a set is produced and checked

1. Before the release build, on the release's commit: `vericairn docs generate --out docs/releases/<version>` writes the three references and the manifest from the build; `vericairn docs notes --estate <estate> --from <previous> --to <version> --out docs/releases/<version>` writes the release notes from the estate's records once the release is published and recorded (for the first set after this convention, the notes were generated after the fact); the authored documents are written and the frozen copies taken.
2. `vericairn docs check docs/releases/<version>` refuses the set when a document is missing or empty, when the command-line, API or record reference lacks a heading for anything the manifest lists, when the overview does not name the version, and, when the checking build is the version documented, when the manifest disagrees with the build's command tree, API document or record types. Every gap is listed at once.
3. The release workflow runs the check before it builds a draft, so an incomplete set refuses the release; it then packages the set as `vericairn-<version>-docs.zip` with the other assets, listed in the release record by hash.
4. The websites publish the sets once they exist (milestone M4); until then the set lives here and as the release asset.

The 0.2.0 set was written on October 13, 2026, after the release day, from the 0.2.0 binary and the estate's records; its overview says so. From 0.3.0 the set is produced before the build.
