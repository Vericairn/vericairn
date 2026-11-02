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
The identity section lists the stable target and its aliases in ordinal order,
including an explicit identifier retained as an alias of a kind-qualified key.

The reader recognizes the catalog's Identifier, Foundry, Provider, Capability and
Area column contracts, plus planned additions with Entry/Area/Status/Where columns.
Implementation observations with Entry/Assembly/Status columns, prose mentions and
fenced examples do not create owning rows. `GuideMarkdown.Read` returns the parsed
rows and findings for preparing and reviewing the mappings.
An owning header followed by a malformed delimiter produces a finding naming its
source, heading and line numbers, even if the table has no index mapping yet.
That finding prevents a complete generation; the table cannot silently disappear
from coverage.

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

Content checks apply to pinned blocks and mapped owning-row cells. Source prose
outside those quotations, such as a family file's parent link to its catalog or
an unquoted code example, is not guide content. Catalog family links still require
their sources in the allowlist. Quoted active markup, embeds, unsafe schemes,
protocol-relative links and parent traversal produce findings, except a relative
link that resolves to another allowlisted source, such as a family file's link
to its catalog. Admitted relative links are displayed as inert text; generation
never resolves or follows them.

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
Each applicable boundary block and row caveat is quoted in full once, in
"Reserved acts and hard lines". The role section refers there by block identifier.
When readiness or context uses the same block, it names the section containing
the full quotation instead of repeating it. All thirteen sections remain present.

Outputs use UTF-8 without a byte-order mark and LF presentation newlines, sorted
by stable key. Quoted payloads retain their original bytes in JSON strings;
Markdown and HTML display them as escaped inert text, with carriage returns
encoded so that decoding restores the exact payload. Both renditions use the same
semantic sections. No clock, account, network query or machine location contributes
to output bytes. Inputs are bounded to 4 MiB per file, 32 MiB combined, 512 sources
and 20,000 owning rows. Extracted source blocks also have a combined 32 MiB bound;
rendered guides are bounded to 128 MiB per selection.

For a catalog that exceeds the rendered-output bound, generate exact batches with
repeated `--select` arguments and a fresh private output directory for each batch.
Use the same complete source index and pinned inputs for every batch. For example:

```sh
vericairn guides generate --catalog inputs/catalog --contracts inputs/contracts --index inputs/index.json --select provider.workshop.routes --select foundry.workshop.desk --out artifacts/guide-batch-01
vericairn guides check --catalog inputs/catalog --contracts inputs/contracts --index inputs/index.json --select provider.workshop.routes --select foundry.workshop.desk --out artifacts/guide-batch-01
```

Partition all planned and remainder keys into disjoint selections; keep each batch's
exact selection for its check. If a selection exceeds the bound, split it again.
An oversized selection produces failure manifests and no guides. Every successful
batch still accounts for the full inventory, marking unselected planned entries
as withheld. Before reviewing the collection as complete, verify that the union of
the batches' source-manifest guide keys equals all planned and remainder keys in
the reviewed index, with each key present in exactly one batch. Keep the bundles
separate, including their manifests and indexes; concatenating them is not a
checked collection. Each individual guide must fit the bound with its full blocks;
batching never permits shortening an obligation.

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
The generator identity is `vericairn.guides/2`; comparison with an earlier generator
marks its guides due for review. Generate into fresh directories for that review.

The template is a versioned checked convention. This command does not adopt a
living-document instance, record a review, publish a repository or activate a site.
Publication requires its own reviewed source selection, rights, destination and
authorization. Community packages retain their own publisher names; an `implements`
claim must identify the exact target revision, covered scope and evidence and
grants no first-party identity or authority.

## Prepare the proposed catalog repository

The 0.9.0 development build also prepares the proposed `Vericairn/catalog`
repository as a separate local bundle. Repository creation, request activation
and each publication remain the operator's separate decisions. These commands
make no network call, initialize no repository and activate no site. The provider
importer and publication operation are planned.

Prepare a private export plan alongside the reviewed source index. Its
`format` is `vericairn.catalog-export/1`, with these members:

| Member | Meaning |
|---|---|
| `indexIdentity` | SHA-256 of the index serialized with `GuideJson.Options`, UTF-8 without a byte-order mark, followed by one LF. Compute it with `GuideGenerator.Hash`; an identity of differently formatted input JSON is not this identity. |
| `allowlist` | The exact source pins from the index: `id`, `area`, `path`, `sha256`. The complete set must match before any catalog or contract file is read. |
| `entries` | Explicit cleared owning entry keys. Unknown keys, aliases, references and withheld entries are refused. All declared dependencies must also be in the public selection. |
| `retirements` | One reviewed public disposition for each selected retired or fully available entry; none for active planned scope. Fields are `key`, `date` (`yyyy-MM-dd`), `revision`, `replacement` and `migration`. |
| `restrictedText` | Additional private strings and restricted names to reject from every output file and file name. This private input is never exported or quoted in findings. |

The allowlist limits reads; the entry and boundary review establishes clearance.
Keep private records, credentials, restricted names and machine locations out of
all selected prose, aliases and public retirement metadata. Public replacement
links use HTTPS without credentials or query strings. Supply explicit migration
limits, including when no automated route or assessed community version exists.
The exporter validates the entire GUIDE-01 inventory, then emits only selected
rows, their applicable quoted blocks and the role recipes. It never copies a
private preview, private source manifest, source index, coverage or Git history.

```sh
vericairn guides export --catalog inputs/catalog --contracts inputs/contracts --index inputs/index.json --plan inputs/export-plan.json --out artifacts/catalog-first
vericairn guides export --catalog inputs/catalog --contracts inputs/contracts --index inputs/index.json --plan inputs/export-plan.json --out artifacts/catalog-second
vericairn guides publication-check --catalog inputs/catalog --contracts inputs/contracts --index inputs/index.json --plan inputs/export-plan.json --destination artifacts/catalog-first --json
```

Choose new or empty output directories separate from all inputs. Existing output
is refused, even when identical; nothing is overwritten. Compare the complete
membership and bytes of independent exports. `publication-check` regenerates the
exact proposal and compares it with an existing local directory or destination
checkout, reporting `add`, `change` and `remove` without modifying it. Exit 0 means
the comparison completed without findings; inspect its changes even when it exits
0. Linked inputs or destinations, incomplete comparisons and disappearing guide
addresses are findings. Checkout Git metadata is neither read nor exported.

The bundle contains:

- `README.md`, `LICENSE.md`, `NOTICE.md` and `CONTRIBUTING.md`, with CC BY 4.0
  for guide prose and catalog renditions and Apache-2.0 for any separately reviewed
  original samples. This exporter includes no samples.
- `sources/catalog/` and `sources/contracts/`: cleared contribution renditions;
  `templates/`: the template convention and role recipes.
- `guides/<kind>/<entry-key>.md`: a complete guide or retained retirement page.
- `indexes/`: entry, family and role navigation, a public inventory and the issue map.
- `manifests/`: exact public rendition digests and template/generator versions.
  The output manifest lists every other file and excludes itself.
- `.github/ISSUE_TEMPLATE/request-implementation.yml`: one shared request form.

For supplied public issue observations, add `--issues inputs/tracking-issues.json`
to both export and comparison. That explicit file is a JSON array of
`{"key":"provider.workshop.routes","number":17}` objects. Keys and positive
issue numbers must be distinct and belong to selected entries. Omission produces
an empty map; it makes no claim that demand is zero. No issues are seeded.

The disclosure scan checks every file and name, including decoded text. It rejects
private identifier and credential shapes, machine locations and configured
restricted text. Only exact, verified public manifest identity fields may contain
digests. Automated checks supplement complete source, disclosure and rights review;
they cannot determine whether arbitrary prose has been cleared. A failed export
returns no public files or partial public inventory. Keep all private inputs and
validation evidence outside the proposed public repository.

## Validate requests and reconcile observations

The form uses a stable entry key, a deliberate request checkbox, optional intended
use of at most 500 characters, optional offers to test or contribute, and an
unchecked status-notice opt-in. Empty optional fields are valid. The explanation
states that requests inform priority without a timeline, public requests are
visible on the host, and personal or confidential data must not be included.
Offers are not assignments and the notice checkbox enables no subscription.

The native form creates an issue; it cannot atomically enforce uniqueness or
append to an existing issue. Its submission must be validated against the exact
public inventory. For an existing tracking issue, `CONTRIBUTING.md` supplies
**Add my request** instructions using the same headings and checkbox labels in a
structured comment. Ordinary discussion, stars, reactions and subscriptions are
not votes. `CatalogRequests.Parse` recognizes these shared fields; a future
qualified importer or responsible manual observer supplies their provider
attribution separately. Body text cannot assert an account identity.

```sh
vericairn guides request-validate --inventory artifacts/catalog-first/indexes/entries.json --request inputs/request.json
vericairn guides requests-reconcile --inventory artifacts/catalog-first/indexes/entries.json --observations inputs/observations.json
```

Both commands read explicit JSON files and return JSON. A minimal request is
`{"entryKey":"provider.workshop.routes","request":true}`. Optional members are
`intendedUse`, `offerToTest`, `offerToContribute` and `noticeOptIn`. Unknown and
duplicate properties are refused. Only exact requestable keys are accepted;
unknown keys point to the entry index, while retired or available scope points to
its retained guide and replacement. An explicit planned remainder remains
requestable under the same stable key.

The private observation snapshot has `repository`, `complete`, `windowStart`,
`windowEnd`, `asOf` and `observations`. The interval is inclusive at the start and
exclusive at the end; all times are UTC and `asOf` is within that interval.
Include complete history through `asOf`, including earlier revisions needed to
resolve edits, withdrawals and deletions, even if they precede the window.
The `CatalogRequestSnapshot` and `CatalogRequestObservation` types define the
contract. Every observation has:

- `kind` (`issue` or `comment`), stable `objectId`, containing `issueId`, stable
  `accountId`, `accountKind` (`human`, `bot` or `app`) and provider-established
  `verified` attribution;
- positive ordered `revision`, `issueCreatedAt`, `createdAt` and `updatedAt`;
- `deleted`, `closed` and `fields` (the request object, or null for removed fields
  or ordinary discussion).

The earliest valid issue creation time, then stable issue ID in ordinal order,
selects one canonical issue per entry. Duplicate deliveries and reordered input
produce the same result. The latest attributable action per account and entry
updates one vote; edits and repeated submissions add no extra vote, withdrawal or
deletion removes it, and restoration restores at most one. Equal action timestamps
are ordered by object kind, stable object ID and revision. Closed duplicates keep
their attribution; closing or reopening an issue is not itself a request action.
Bots, apps and unverifiable submissions do not count. Authenticated hosting
attribution proves control of that account, not one unique person.

Reconciliation proposes canonical issues and duplicate links; it performs no
provider writes. An authorized maintainer must reconcile duplicates at the host,
preserve original attributable submissions and record the guide's public revision
on the canonical issue. The importer is planned. Until qualified, aggregates must
be labeled manual with their observation window. An incomplete or conflicting
basis returns no fresh votes or totals; prior aggregates remain stale or unavailable,
never reset to zero. Reconciliation output is private: it includes account keys
and consent and must not be copied into the public bundle. Only separately reviewed
aggregates may be published.

## Publication, retirement and recovery

Issues report corrections or observations; pull requests change owning sources,
boundary mappings, role recipes, templates or reviewed samples. Generated guides
are never edited directly. Reconcile accepted source patches to the authoritative
catalog, rebase if its basis changed, review again and regenerate. A public merge
is publication and needs the operator's decision.

After independent review of sources, rights, disclosure and regenerated bytes,
compare the exact proposed bundle with the intended local checkout. Every prior
guide address must remain present. A reviewed retirement replaces that address
with its public date and revision, replacement, migration limits and history and
community notices. Partial availability keeps the planned remainder active;
retirement does not manufacture migration compatibility or replay old demand.

Repository creation and publication of the exact reviewed bundle require separate
operator decisions. The publication operation and request activation remain
planned; a preview labels the request action unavailable. No successful check
enables either. Activation needs qualified host links, the form, duplicate
reconciliation and explicit subscription instructions. The notice checkbox alone
does not change hosting subscriptions or authorize a message.

After a separately authorized publication, observe that destination and compare
complete membership and bytes before claiming it current. If an effect is failed
or uncertain, reconcile the original destination before retrying. Retain the last
observed good revision, prepare a corrective bundle from reviewed sources and
obtain a new publication decision. A repository observation establishes no claim
about a separate site. Keep retired addresses in every correction and preserve
attributable public history without importing private estate history.
