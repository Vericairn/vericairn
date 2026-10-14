# Release documentation sets

Every release of Vericairn carries its own documentation set, one directory per version under `docs/releases/<version>/`. A set is written for the release it documents and then frozen: nothing in a released set is edited afterwards, and a correction goes into the next version's set with a note that says what it corrects. The maintained documents at `docs/` are the drafts for the next release; the frozen copies in a set are the record of what shipped.

## What a set holds

| Document | Written by | What it is |
|---|---|---|
| `README.md` | a person | The overview: what the release is, what the set holds, how to tell you are running it |
| `install.md` | a person | Installation and cold start on Linux, macOS and Windows, by the estate bootstrap and by direct download, with verification |
| `operator-guide.md` | a person | The operator's journeys: create an estate, devices and admission, the work loop, review and acceptance, releases, upgrades, recovery, the daemon |
| `cli-reference.md` | the build tooling (`vericairn-docs generate`) | Every command and verb with its arguments and options, from the build's own command tree |
| `api-reference.md` | the build tooling (`vericairn-docs generate`) | Every API operation with parameters, bodies and responses, from the OpenAPI document the daemon serves |
| `record-reference.md` | the build tooling (`vericairn-docs generate`) | Every record type with its subject rule and body fields, from the record type registry |
| `record-format.md` | a person, frozen copy | The record format document as released |
| `api.md` | a person, frozen copy | The API document as released: transports, outcomes, problem codes |
| `software.md` | a person, frozen copy | The software journey as released: the git adapter, checks, the release provider |
| `release-notes.md` | `vericairn docs notes` | What changed, from the estate's records between the previous publication and this one |
| `limitations-and-platforms.md` | a person, from the release record | Qualified platforms, signing, known limitations, every artifact by hash |
| `manifest.json` | the build tooling (`vericairn-docs generate`) | The set's version, commit, generator, the documents the product declares, commands, operations and record types |

## How a set is produced and checked

1. Before the release build, on the release's commit: `dotnet run --project tools/Vericairn.DocsTool -- generate --out docs/releases/<version> --documents docs/releases/documents.json` writes the three references and the manifest from the build (from 0.4.0 the generator is build tooling, not part of the shipped command line, and the documents every set holds are declared in `docs/releases/documents.json`; 0.4.0 declares `workbench.md` and `mcp.md` beside the eleven of earlier sets); `vericairn docs notes --estate <estate> --from <previous> --to <version> --out docs/releases/<version>` writes the release notes from the estate's records since the previous publication (before the build the version's own publication is not yet recorded and the notes say so; the 0.2.0 notes were generated after the fact between two publications); the authored documents are written and the frozen copies taken.
2. `vericairn docs check docs/releases/<version> --documents docs/releases/documents.json` refuses the set when a document the manifest or the declaration requires is missing or empty, when the manifest drops a declared document, when the command-line, API or record reference lacks a heading for anything the manifest lists, or when the overview does not name the version; `dotnet run --project tools/Vericairn.DocsTool -- compare docs/releases/<version>` refuses it when the manifest disagrees with the build's command tree, API document or record types. Every gap is listed at once.
3. The release workflow runs both before it builds a draft, so an incomplete set refuses the release; it then packages the set as `vericairn-<version>-docs.zip` with the other assets, listed in the release record by hash.
4. The websites publish the sets once they exist (milestone M4); until then the set lives here and as the release asset.

The 0.2.0 set was written on October 13, 2026, after the release day, from the 0.2.0 binary and the estate's records; its overview says so. From 0.3.0 the set is produced before the build, and a set may carry further frozen copies beyond the required documents (0.3.0 adds `workbench.md` and `mcp.md`). Because the set is frozen before the build, its overview states the acceptance, the publication and the cold start as the steps that follow the freeze, never as past facts; the 0.3.0 overview put them in the past tense, which its release record notes as a limitation. Release 0.3.0 was withdrawn before publication (Workbench did not open an estate on a first launch); its set stays frozen as the record of what was built and reviewed, and 0.3.1, the corrective release built from the 0.3.0 commit on the release/0.3 branch, carries its own set with the release notes running from 0.2.0, the last published release.
