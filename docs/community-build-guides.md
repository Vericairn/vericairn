# Community build guides

The 0.9.0 development build turns a reviewed Markdown catalog selection into a
deterministic private guide bundle. Each selected planned owning row gets one
semantic guide, rendered as JSON, Markdown and HTML. Grouped Providers stay one
entry; shared profiles refer to their owner; implemented subsets can retain an
explicitly planned remainder. Generating a guide does not make its entry available.

## Prepare the inputs

Choose local catalog and contract directories, then prepare a reviewed JSON source
index. Paths in the index are relative to one of those directories, use `/`, and
must be portable file names. Only listed files are read. Source and block hashes
use `sha256:` followed by 64 lowercase hexadecimal digits. Hash the original
UTF-8 bytes, including their exact line endings.

The index has `format: "vericairn.guide-inputs/1"` and
`templateVersion: "vericairn.extension-ecosystem/build-guide/1"`, followed by these
arrays. The public types in `Vericairn.Runtime.Guides` define the same contract.

| Array | Fields and review responsibility |
|---|---|
| `sources` | `id`, `area` (`catalog` or `contracts`), `path`, `sha256`. Include the catalog root, every linked family file and the applicable contract files. |
| `blocks` | `id`, `source`, `startLine`, `endLine`, `sha256`. Line numbers are inclusive and start at one. Select complete introductions, readiness disclosures, reserved acts, hard lines, profiles and role contracts. |
| `roles` | `kind`, `blocks`. Supported kinds are `domain`, `foundry`, `adapter`, `provider` and `shared-capability`; each used kind needs a nonempty contract selection from the contracts area. |
| `families` | `name`, `boundaries`. Include common boundaries even when they appear under another role's heading. |
| `tables` | `source`, `heading`, `kind`, optional `family`, `boundaries`, `context`. A heading is the complete heading trail joined with ` / `. Every owning table needs exactly one mapping and a kind/group preamble. |
| `entries` | `key`, `source`, `heading`, `name`, `state`, `status`; optional arrays `boundaries`, `context`, `references`, `aliases`, and optional `owner`, `explanation`. Map every owning row exactly once. |

Keys retain an existing explicit identifier. A name-only row gets a reviewed key
qualified by kind and family, such as `provider.workshop.routes`. Keys use lowercase
ASCII letters, digits, dots and hyphens; display names and row numbers are not keys.
`name` is the exact owning-row display name without its outer bold or code markers.
Retain keys and add aliases when a display name changes.

The reader recognizes the catalog's Identifier, Foundry, Provider, Capability and
Area column contracts, plus planned additions with Entry/Area/Status/Where columns.
Implementation observations with Entry/Assembly/Status columns, prose mentions and
fenced examples do not create owning rows. `GuideMarkdown.Read` returns the parsed
rows and findings for preparing and reviewing the mappings.

Every entry needs pinned `status` blocks. States are `planned`, `remainder`,
`available`, `retired`, `reference` and `withheld`. A remainder must disclose both
the implemented subset and the planned scope. A reference names its owning key or
alias in `owner`. Withheld rows need an explanation; retired rows need their
replacement and migration explanation. Only planned and remainder rows generate
guides in this command.

Review each required collaborator against `references`, which resolves stable keys
and aliases. Use `context` for applicable introductions, edition allocation,
shared profiles and explicitly referenced specifications. Names in prose are not
guessed into dependencies. Include every applicable boundary in the family,
table or entry selection; these selections add to the row caveat and role contract.
Hashes prove the selected bytes, while source-selection review establishes their
completeness and applicability. A changed and repinned source requires that review
again. The index is private provenance and belongs with the private inputs.

## Generate and inspect

```sh
vericairn guides generate --catalog inputs/catalog --contracts inputs/contracts --index inputs/index.json --out artifacts/guide-preview
vericairn guides check --catalog inputs/catalog --contracts inputs/contracts --index inputs/index.json --out artifacts/guide-preview
```

Add `--select provider.workshop.routes` for an exact planned subset; repeat it for
more keys or aliases. Selection never removes failures elsewhere in the inventory.
Duplicate identities, missing sources, unresolved references, incomplete boundary
mappings, stale hashes, unsupported templates and active source markup return
findings. A failed generation emits no partial guide set.

A successful preview contains `index.md`, `index.html`, `source-index.json`,
`source-manifest.json`, `coverage-manifest.json` and
`guides/<key>.json`, `.md` and `.html`. Both manifests indicate whether generation
completed. Coverage enumerates every owning row with its disposition and reason;
the source manifest pins files, spans, generator/template versions and each guide's
input identity. Failure manifests retain the findings. Keep the whole bundle
private: its index and coverage can disclose unselected entries.

Each guide has thirteen required sections, including identity and readiness,
role guidance, collaborators, complete boundaries, rights, build tasks, checks,
contribution route and maintenance. The four Pack roles have distinct recipes;
shared-capability guides describe product contributions. Request counts remain
"not collected" and no request route is enabled. Missing implementation details
and real-operation qualification stay explicit work for the builder.

Outputs use UTF-8 without a byte-order mark and LF presentation newlines, sorted
by stable key. Quoted payloads retain their original bytes in JSON strings;
Markdown and HTML display them as escaped inert text, with carriage returns
encoded so that decoding restores the exact payload. Both renditions use the same
semantic sections. No clock, account, network query or machine location contributes
to output bytes. Inputs are bounded to 4 MiB per file, 32 MiB combined, 512 sources
and 20,000 owning rows; rendered guides are bounded to 128 MiB per selection.

Open `index.html` locally to inspect the preview. New files and directories use
owner-only permissions on Unix; Windows uses the destination's inherited access
controls. Choose a private destination there. Existing files are compared and
never replaced. Correct the owning sources or reviewed mappings and generate into
a fresh directory when inputs change.

```sh
vericairn guides compare --before artifacts/earlier/source-manifest.json --after artifacts/guide-preview/source-manifest.json --json
```

Comparison lists affected guide keys as due. A family boundary change affects all
its dependent guides; changes outside a guide's selected inputs leave that guide's
identity unchanged. `check` independently regenerates the entire selection and
compares complete membership and bytes, including boundary payloads and readable
renditions. An output-only edit fails; checking writes no repair or clearance.

The template is a versioned checked convention. This command does not adopt a
living-document instance, record a review, publish a repository or activate a site.
Publication requires its own reviewed source selection, rights, destination and
authorization. Community packages retain their own publisher names; an `implements`
claim must identify the exact target revision, covered scope and evidence and
grants no first-party identity or authority.
