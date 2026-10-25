# Complete presentation templates

The 0.8.4 development build installs four version `1.0.0` content forms on the
template catalogue. Installation does not adopt them or alter existing
communication and context output.

| Template | Required content |
| --- | --- |
| `vericairn.estate/communication` | Complete facts and original references; recipient depth controls presentation. |
| `vericairn.estate/context-brief` | Basis, required sources, original bytes, complete withheld list and accounting. |
| `vericairn.software/commit` | Outcome, reason, the exact result's checks and limits, work references and declared attribution trailers. |
| `vericairn.estate/review` | Criterion-derived verdict, exact subject, scope, every assignment criterion, full findings, independence, checks, limits and next action. |

Every form also carries generated source identities, a withheld list and a
measured budget. These are presentation artifacts prepared with `templates
render`, rather than living-document declarations. They use the template
catalogue, registered generator and `vericairn.templates/complete-source` check.
The four versioned JSON definitions and their compiled copies must have identical
canonical bytes. Registration is additive and grants no authority.

## Adopt or withdraw a form

Inspect the exact contract with `vericairn templates show <id> --version 1.0.0`.
An operator or principal with existing `administer` authority can record this
`template-defaults` body, substituting the returned hash:

```json
{
  "template": {
    "id": "vericairn.software/commit",
    "version": "1.0.0",
    "hash": "<exact template hash>"
  },
  "scope": { "kind": "estate" },
  "options": { "adoption": "enabled", "depth": "standard" },
  "reason": "Use this complete commit format for newly prepared work."
}
```

```sh
vericairn records new template-defaults --by <operator> --body adoption.json
```

Adopt each form separately. To withdraw one, revise the same defaults with
`adoption: disabled`, a reason and `--supersedes <current record id>`. History
remains inspectable. Conflicting or unavailable enabled pins refuse preparation.
A new version needs explicit adoption; registration never upgrades an instance.
Communication preferences alone do not adopt templates.

An unadopted estate retains the existing output path and has no template metadata
in those responses. With communication adoption, the recipient's resolved depth
still controls status, decisions, results and handoffs. Context reads use the
adopted default depth; addressed briefs use the recipient's handoff depth.
Context selection and package allocation retain their existing contract, with
separate exact accounting for the human template rendering. Section briefs also
use the adopted context form; their byte limit must fit its complete rendition.

## Prepare and check

Retain the preparation request with its artifact. Both pins include full hashes.
A commit selects an existing result; preparation never invents a result id:

```json
{
  "template": {
    "id": "vericairn.software/commit",
    "version": "1.0.0",
    "hash": "<exact template hash>"
  },
  "subject": { "type": "result", "id": "<result id>", "hash": "<result hash>" },
  "depth": "short",
  "authored": {
    "outcome": "Keep every required source in presentation variants",
    "reason": "Readers need the same facts at every depth.",
    "attribution": []
  }
}
```

```sh
vericairn templates render request.json > draft.md
vericairn templates check request.json draft.md --json
```

Both commands read only. Render exits 2 on refusal. Check exits 1 for changed or
missing required content and 2 when its basis cannot be prepared. API and SDK
clients use `TemplateRender` / `TemplateRenderAsync` and
`POST /v1/templates/render`; MCP uses the read-only `template_render` tool with a
`request` object. Supplying `candidate` checks against a separately prepared
basis, including exact retained originals, instead of trusting candidate claims.
The response has `use`, `text`, `hash`, `bytes`, `checks` and `problem`.

`depth` is short, standard or comprehensive; omission uses recorded defaults.
Optional `explanation` is authored. `maxBytes` bounds the whole rendered artifact,
including its budget. Overflow refuses without dropping content; tokens remain
unknown. Omission overrides and generated-field replacements are refused.
Every required section and original reference is checked at every depth. Source
markup stays inert inside presentation framing. Original records retain their
exact base64 bytes, distinct record and byte hashes, sizes and retrieval commands.

Communication requests select an exact record and accept no authored fact
replacements. Context requests select an exact work order and retain the complete
context selection. Commit outcome and reason are authored; checks, limits and
references come from the pinned result and its ancestry. The attribution array is
mandatory, including an explicit empty array when none is required. Every listed
trailer appears in the footer. The author and reviewer check that declaration
against the estate's policy; the template does not infer trailers from free-form
policy text or change Git identity.

A review selects an exact result. Its `authored` object supplies `reviewer`
(principal id), `scope`, `criteria`, `findings`, `limits` and `next`. Each criterion
preserves the exact assignment text and order, with `criterion`, `outcome`
(passed, failed, unknown or not-performed), `method` and `evidence` (exact
`{type,id,hash}` references). A passed criterion requires evidence. Each finding
contains its complete `condition`, `effect`, `evidence` and `disposition`.
Empty findings and limits are explicit arrays. The runtime derives identity and
independence and retains result checks and limits. A passing content check
establishes neither semantic correctness nor an independent review or acceptance.
Those remain the existing reviewer's duties.

To check an older artifact after withdrawal, supply its returned `use.adoption`
as the request's `adoption`. This permits inspection without enabling new
preparation. Changed context selections or reviewer revisions can make an older
presentation stale; another original cannot silently replace an exact pin in a
passing check. These forms add no record type or field: adoption uses the existing
format-3 `template-defaults` contract and its readable companion.
