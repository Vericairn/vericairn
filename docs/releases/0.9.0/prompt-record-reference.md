# Prompt record reference additions

Generated from the record registry by `vericairn-docs generate`, then selected
from its canonical `record-reference.md`. Source commit: `7551a8734253f71be1d47f0e07fc002be0321bad`.
These development additions are for 0.8.4 and stay within record format 3.
The full release reference will be generated with that release build.

Regenerate into an ignored output directory with the documentation tool and
select the complete `saved-prompt` and `prompt-preference` sections, plus each
`promptUse` field below. The command-line registry describes the same schema
with `vericairn records describe <type>`. See [Saved prompts](prompts.md) for
validation, authority, lifecycle and transport behavior.

## `saved-prompt`

A categorized prompt with described placeholders and suited roles&#44; revised by supersession and retired without deleting its history&#46; Its text grants no authority&#46;

Subject type: null; subject hash required: false; introduced in record format: 3.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| key | string | true | 3 | Stable lower&#45;case prompt key&#46; |
| title | string | true | 3 | Short title&#46; |
| description | string | true | 3 | When and why to use the prompt&#46; |
| text | string | true | 3 | Comment text with optional &#123;name&#125; placeholders&#59; never authority&#46; |
| category | string | true | 3 | One category&#44; with slash&#45;separated nested segments&#46; |
| placeholders | object&#91;&#93; | true | 3 | Each placeholder&#39;s name and description&#59; every named value is required&#46; |
| tags | string&#91;&#93; | true | 3 | Free discovery tags&#46; |
| roles | string&#91;&#93; | true | 3 | Suited principal roles&#59; an empty list suits every role&#46; |
| state | active&#124;retired | true | 3 | Retirement removes the prompt from offers and keeps every earlier revision&#46; |
| origin | object | false | 3 | Copied built&#45;in identity&#58; source&#44; key&#44; version and exact definition hash&#46; |

## `prompt-preference`

A signed personal favorite or hiding choice&#44; or an operator&#39;s estate&#45;wide disable&#44; with reversible changes and no effect on past uses&#46;

Subject type: null; subject hash required: false; introduced in record format: 3.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| prompt | string | true | 3 | Stable source&#58;key identity&#44; independent of the selected revision&#46; |
| scope | person&#124;estate | true | 3 | Personal choices or an estate&#45;wide operator choice&#46; |
| person | ulid | false | 3 | Required for personal choices&#59; the author whose choices these are&#46; |
| hidden | boolean | false | 3 | Required for personal choices&#46; Hides from that person&#39;s offers&#46; |
| favorite | boolean | false | 3 | Required for personal choices&#46; Orders offers within their category&#46; |
| disabled | boolean | false | 3 | Required only at estate scope&#46; Disables new uses without deleting history&#46; |

## `assignment.promptUse`

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| promptUse | object | false | 3 | Exact prompt source&#44; key&#44; revision&#47;version&#44; hash&#44; definition&#44; placeholder values and rendered comment&#46; Authority is unchanged&#46; Kept in run and decision revisions&#46; |

## `decision.promptUse`

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| promptUse | object | false | 3 | Exact prompt source&#44; key&#44; revision&#47;version&#44; hash&#44; definition&#44; placeholder values and rendered comment&#46; Authority is unchanged&#46; Kept in run and decision revisions&#46; |

## `operation.promptUse`

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| promptUse | object | false | 3 | Exact prompt source&#44; key&#44; revision&#47;version&#44; hash&#44; definition&#44; placeholder values and rendered comment&#46; Authority is unchanged&#46; Kept in run and decision revisions&#46; |
