# Templates and document sections

The 0.8.4 development build adds declarative templates to Community. A template
states what a document contains, how each part is made, and what evidence clears
it. An instance is an authored living document with an exact template pin. The
existing record format remains 3.

A template grants nothing. Its role names require existing principal or roster
standing. Its owner records responsibility, and its options cannot authorize an
operation. Installation, synthesis, generation, a passing check and a section
review never supply acceptance, publication permission or a policy exception.

## First-party contracts

The development build ships these six contracts at `1.0.0`. They are available
through `templates list` and `templates show` without copying files into an estate.
Each generated section names its installed `vericairn.estate/*-v1` implementation
and `version=1`; changing emitted meaning requires another version.

| Template under `vericairn.estate/` | Authored parts | Generated parts |
| --- | --- | --- |
| `design-document` | Answer, vocabulary, design body, records and compatibility, journeys and implementation notes | Recorded revision, verbatim operator words, linked work orders and decisions |
| `open-decisions` | Optional context and historical numbering | Current asks, their recommendations, consequences, states, answer links and decision statements |
| `design-index` | Optional catalogue narrative | Every current pinned Markdown document under `design/`, ordered by path |
| `release-notes` | Release outcome, changes, compatibility, validation and limitations | Recorded published releases |
| `readme` | Overview, getting started and navigation | Recorded published releases |
| `roadmap` | Direction and sequencing | Complete release-placement rows |

The roadmap groups only typed `work-order.placement` values with `kind: release`,
by product identity, exact version and carrier identity. It neither infers a
placement from prose nor includes backlog or unplaced work in those rows. Fresh
generation and `vericairn.estate/roadmap-placements-v1` both check the complete
selection, including removals, duplicate keys and incorrect groups.

Design decisions select records whose `documents` list names the instance path;
their `answers` identify the related asks. Work orders select those decisions'
`affects` references or work inputs naming revisions of the document. Open
decisions use ask identities, which survive revisions; historical numbering
remains authored context. Quoted statements retain their full wording inside
fences. A release view can be limited with the existing `scope.product` choice.

`design-document` has `changes-records: no` by default. Set it to `yes` when
records change, making the compatibility section required. Semantic coverage of
records, journeys, proposed work and agreement between answer and body requires
independent evaluator judgments with exact evidence. No generator invents those
judgments or an authored clearance.

## Create, write and review an instance

Retain template JSON under `docs/templates/` in the estate, or install an immutable
contract through the host's `TemplateCatalog.Register`. The catalogue validates
every contract against installed primitives and refuses conflicting content for
one id and version. Instance pins contain the namespaced id, semantic version and
SHA-256 of canonical JSON; whitespace changes do not change that identity.

```sh
vericairn templates list
vericairn templates show example/design --version 1.0.0
vericairn docs new example/design docs/design.md --version 1.0.0 --by <owner> --title "Design"
vericairn docs sections docs/design.md
vericairn docs brief docs/design.md --section overview --json
vericairn docs review docs/design.md --section overview --fidelity draft --by <reviewer> --outcome current --summary "Read the overview and its sources."
```

`docs new` requires an exact version, author and title. It refuses an existing
destination, prepares the declaration and complete initial content before storing
anything, and uses ordinary record admission and content checks. The author is
the initial owner. The path is an estate file under `design/` or `docs/`. The
optional `--level remind|warn|refuse` defaults to `warn`. The optional
`--configuration` reads a JSON object with only `options`, `scope` and `overrides`.
Creation lays out every section's purpose and fills quoted and generated parts.
It records no document revision, review or acceptance.

Write authored parts normally and pin the document with `docs pin`. An agent's
ordinary result identifies each produced section using its exact payload hash,
optional UTF-8 byte count, document path and exact living-document reference.
The hash excludes the heading and section boundary markers. Whitespace and line
endings remain significant. The runtime refuses a result that names different
current bytes or writes an authored part marked `personOnly` as an agent.

Review one section with `--section` and `--fidelity sketch|draft|final` together.
The runtime reads the content and sources and executes its checks. For evaluator
rules, `--judgments` reads a JSON array of `{rule, verdict, summary, evidence}`;
`verdict` is `passed`, `failed` or `unknown`, and `evidence` lists exact section,
selected source and referenced record hashes. Each required rule needs its own
verdict. A qualified evaluator or operator must be independent of the recorded
writer. A judgment cannot replace a deterministic check. Failed, unknown or
unjudged evidence cannot clear a section. `needs-change` preserves the failed
or unjudged evidence and leaves the section due.

`docs sections` and `docs brief` accept `--commit` for an existing declaration
whose source is another repository. That repository's installed document source
must provide the exact commit. These reads never substitute a working tree or
fetch external inputs. `--json` preserves response objects and diagnostics.

If creation stores its declaration but output publication stops, its response
contains the declaration receipt and `reconcile-required`, and the CLI exits 2.
Keep that identity. Reopening the estate verifies the original template, prepared
bytes and destination before recovery. An intervening file edit is preserved and
holds further writes. Reconcile the original batch before retrying creation.

## Contract grammar

A minimal complete contract is:

```json
{
  "format": "vericairn.template/1",
  "id": "example/design",
  "version": "1.0.0",
  "purpose": "Explain the proposed design and its evidence.",
  "artifact": "document",
  "sections": [
    {
      "id": "overview",
      "heading": "Overview",
      "purpose": "Explain the design's purpose and boundaries.",
      "presence": "required",
      "role": { "write": "builder", "review": "operator", "clear": "operator" },
      "synthesis": { "mode": "authored" },
      "sources": [],
      "cohesion": [],
      "fidelity": "draft",
      "quality": {
        "review": "independent",
        "checks": [{ "name": "length", "min": 20 }]
      }
    }
  ]
}
```

The top-level fields are `format`, `id`, `version`, `purpose`, `artifact`, optional
`domain`, optional `options` and `checks`, and nonempty `sections`. Section order
is document order. Unknown properties and unknown roles, generators, checks or
record types are refused. Template content contains no executable queries or
loaders. Packs extend the installed vocabulary through existing generators,
`TemplatePrimitives.RegisterRole` with a complete role brief, and
`TemplatePrimitives.RegisterCheck` with `ITemplateCheck`; they cannot extend the
contract's property grammar or assign authority by registration.

Every section has the properties in the example. A locator is exactly one
`heading` or `marker: true`. Headings are distinct ATX headings; fenced examples
do not introduce headings. Explicit marker payloads run from
`<!-- vericairn:section overview -->` to
`<!-- vericairn:section-end overview -->`, including intervening whitespace.
Markers can surround exact inline text. Duplicate, unbalanced, crossing or
out-of-parent locators refuse inspection and clearance. Quoted content should
use markers when its exact bytes might contain headings or lack a final newline.
Creation refuses content that obscures another section's locator.

Sections may contain `sections` and `fields`. A field has the same properties and
an additional `type`: `text`, `list`, `table`, `reference`, `date` or `number`.
Identifiers are unique across the entire document. A list uses `- ` lines; a
table uses pipe-bounded rows, a Markdown delimiter row and equal column counts;
a date uses a real `yyyy-MM-dd` date; a number uses an invariant decimal with an
optional sign and no grouping separators. A reference field checks each listed
target. Nested content belongs to its parent payload, so editing a field also
changes its parent's hash. Structures nest at most twelve levels; use markers
where Markdown heading levels cannot represent the intended nesting.

`presence` is `required`, `optional`, or `{option, value}`. A conditional part is
required when that recorded choice matches. `fidelity` is a level or
`{default, when: [{option, value, level}]}`. The highest matching level wins;
without a match the default applies. Options at the document, section or field
have `{id, values, default}` with distinct allowed string values. Instance keys
are a document option id or `section-id.option-id` / `field-id.option-id`.
Conditions use those same keys. Profile stages can select options; reading a
profile does not silently change an instance's choices.

| Synthesis mode | Additional contract and behavior |
| --- | --- |
| `authored` | A person or agent writes under its recorded role; optional `personOnly: true` refuses agent section results. |
| `synthesized` | An agent writes from the declared sources. Exact result bytes remain labelled synthesized until reviewed. |
| `generated` | Required installed `generator` and `inputs` array; payload uses existing generated-region markers and must equal fresh generation. |
| `quoted` | Required `source` names a declared single-record source. Payload must equal its selected field or canonical body byte for byte. |

Every source has a distinct local `id` and one of these shapes:

| `kind` | Other fields | Selection |
| --- | --- | --- |
| `record` | `record`, optional `field` | Current revision of the record identity; a dot-separated body field or the canonical body. |
| `records` | `type`, optional `where: {field, equals}`, optional `field` | Current records of an installed type, optionally selected by exact field text; selected values in record-id order. |
| `section` | `section` | Exact payload of another section or field in this instance. |
| `file` | `path` | Complete retained UTF-8 file at a safe repository-relative path. |
| `external` | `path`, `reference`, `hash` | Retained local UTF-8 input with an external reference and matching exact byte hash; never fetched. |

Record selection retains each exact type, id and hash. Missing files, fields,
records or external hash matches produce explicit unavailable evidence.

## Checks and cohesion

Top-level `checks` apply to the whole document; `quality.checks` applies to the
section payload. Built-in check objects use `name`: `length` with `min` and/or
`max` bounds, `required-terms` with `terms`, `references-exist`, `links-resolve`,
`names-version`, or `matches-generator`. Length measures text characters;
required terms compare case-sensitive text. Version checks use selected release
records. Other installed namespaced checks receive the payload and resolved
sources. Unavailable checks cannot pass.

`quality.review` is `none`, `self`, `independent` or `operator`. This sets the
review depth; even `none` does not invent a clearance record. A required section
still needs an explicit section review to become current.

Each cohesion rule has a unique `id`, `kind`, `summary`, and a `source` where the
rule compares another selection:

| `kind` | Decision |
| --- | --- |
| `covers` | Every distinct nonempty source item appears in the section's items. |
| `references-exist` | Every listed target resolves to a record id, work-order key, record type, recorded document path, available file or item of the optional source catalogue. |
| `matches-record` | The payload exactly equals the selected single-record source. |
| `agrees-on-values` | The two sets of normalized items are equal, regardless of order. |
| `derived-from` | A generated section exactly equals fresh generation from its declared inputs. |
| `judgment` | An independent evaluator supplies a verdict and complete exact evidence for this rule. |

`covers`, `matches-record` and `agrees-on-values` require `source`. The item
comparisons trim lines, ignore blank lines, headings and comments, remove a
leading `- ` or `* `, and compare distinct case-sensitive items. They do not
infer semantic equivalence from prose. Use a judgment when the declared check
cannot decide the intended relationship. A journey catalogue can be a file
source listing its exact keys.

## States, briefs and gates

`docs sections` returns each definition, required fidelity, content hash, source
digest, recorded writer and review, sources, checks, cohesion outcomes and reasons.
The states are `missing`, `synthesized`, `drafted`, `reviewed`, `due` and `failing`.
A source digest includes the exact template, recorded options, effective section
contract and source identities, byte hashes and sizes. Changed source revisions
or cohesion partners invalidate the affected review even if their selected words
happen to be unchanged. An independent sibling can retain its clearance.

A document is current only when every required section is reviewed at sufficient
fidelity and no section is due or failing. Existing document triggers, levels,
covered-path checks, policy rules and release gates still apply. A passing
section review clears no sibling and supplies no acceptance.

A section brief includes the complete contract, current payload, declared neighboring
payloads, selected source content, full retained original records, declaration and
roster evidence, complete writing-role brief, required fidelity, quality and
options. `bytes` measures the canonical UTF-8 JSON of the brief itself, including
its accounting fields. It is not a token estimate or a model-fit claim.
`--max-bytes` must be positive. An unavailable required original or an allocation
smaller than the complete package returns `complete: false` with an `incomplete`
problem. The inspection response retains available full sources and may exceed
the allocation; it is not a usable within-allocation brief. Narrow the assignment
or allocate enough context. No required source is silently truncated.

## Defaults, deviations and upgrades

`template-defaults` is an additive format-3 record with an exact `template` pin,
`scope`, `options` and `reason`. Scope is `{kind: estate}` or
`{kind: domain|product|project, target}`. Creating or revising defaults requires
existing operator or `administer` authority. Revise the current defaults at the
same pin and scope; competing current declarations refuse application.

At creation, options resolve in this order: template defaults, estate, domain,
product, project, explicit choices. A configuration's scope is an object with
optional `domain`, `product` identity and `project`. Every resulting choice is
materialized in `living-document.template.options`. Later default changes do
not rewrite instances. Process-profile adoption can author these same defaults
under its own existing authority.

`template.overrides` is an array of `{section, action, reason, ...}`. `rename`
adds `heading`; `add` adds a complete `definition` with the same new section id;
`omit` may add an exact `decision` reference. Omitting a required section needs
an operator decision whose `documents` includes the instance path and whose
`effects` includes exactly `omit-section <path>#<section>`. An override cannot
introduce unknown properties or primitives. Instance revisions cannot discard
their template pin to evade its requirements.

Retain each version's original contract. `TemplateInstances.Changes` provides a
read-only difference for a proposed version. The owner or operator explicitly
revises the living declaration: `templateUpgrade` carries `{offered, outcome,
reason}`, where `offered` is an exact template pin and outcome is `accepted` or
`declined`. Acceptance adopts exactly that pin; decline keeps the old pin and
records the reason. The revised declaration carries complete options and
overrides. Reviews of an earlier declaration cannot clear the new one. Content
migration and new reviews are explicit work; no read rewrites files or history.

Hosts can retire a registered version with `TemplateCatalog.Register(definition,
retired: true)`. It disappears from new-instance selection and cannot be adopted
through raw record authoring, while existing pins remain readable and revisable.
Estate template files alone have no retirement flag; retaining the original
content remains necessary to inspect earlier instances.

## Client and extension contract

The API adds `GET /v1/templates`, `POST /v1/documents/new`,
`GET /v1/documents/{id}/sections`, and
`GET /v1/documents/{id}/sections/{section}/brief`. Reads accept declaration ids or
estate document paths; section reads accept `commit`, and briefs also accept
`maxBytes`. The existing review route gains an optional `sections` array. Its
entries contain `{id, outcome, fidelity, judgments?}`. The SDK and cancellation
wrapper expose matching synchronous and asynchronous methods. Responses preserve
receipts alongside recovery problems. See [API compatibility](api-compatibility-0.8.4.md).

MCP adds the reads `document_sections` and `section_brief`. Its existing
`document_review` accepts section selections and judgments. Agent writes remain
ordinary results and reviews; there is no template-instantiation MCP write.
The Workbench section inspector and section review editor remain follow-on work;
use the CLI or API for these workflows.

The estate's design, roadmap and release templates are separate
content adoption. Their builders supply contracts, sources, registered generators
or checks, recorded role standing, explicit options and migration decisions.
The mechanism never infers process adoption. Four first-party
[presentation templates](presentation-templates.md) add communication, context,
commit and review forms with explicit, reversible estate adoption.
The [format-3 additions](record-format-v3.md#template-instances-and-section-evidence)
and [generated record reference](reference/record-reference.md) describe the
retained record fields and readable companions.
