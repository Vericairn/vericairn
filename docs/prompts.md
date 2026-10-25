# Saved prompts

The 0.8.4 development build adds a shared prompt library to record format 3.
Find prompts by category across the estate, the foundation and installed packs.
Each offer shows its source, exact revision or version, and definition hash.
Reading or showing a definition records no use. Using it records the selection
together with a new assignment, a new run or an answer to an ask.

## Find and organize

```sh
vericairn prompt list --person <principal> --category Work
vericairn prompt list --person <principal> --favorites
vericairn prompt show estate:review-evidence
vericairn prompt favor estate:review-evidence --by <principal>
vericairn prompt hide estate:review-evidence --by <principal>
vericairn prompt hide estate:review-evidence --by <principal> --undo
vericairn prompt disable estate:review-evidence --by <operator>
vericairn prompt disable estate:review-evidence --by <operator> --undo
```

An identity is `source:key`: `estate`, `foundation`, or a pack's source such as
`software`. A prompt has one category, optionally nested with `/`, and free tags.
Category queries include descendants. Lists sort by category, then favorites,
title and identity. `--role` narrows the suited roles; `--person` applies that
principal's roles, hiding and favorites. With no person, the CLI shows the shared
library. `--all` also shows hidden, disabled and retired entries.

Personal hiding removes an offer from that principal's lists; explicit use is
still possible. An estate-wide disable requires the operator and refuses new
uses for everyone. Both choices are reversible signed `prompt-preference`
revisions. Neither changes a definition or any past use. A favorite belongs to
its author and remains saved when temporarily hidden or disabled.

## Add, revise, copy and retire

The operator or a principal with an explicit signed `manage-prompts` authority
grant can write shared definitions. The grant must be current and unexpired;
unproven prose conditions and a reservation of that scope prevent its use.
Every definition and preference requires an admitted-device signature, including
in estates that otherwise allow unsigned records.

Save a complete definition in `prompt.json`:

```json
{
  "key": "review-evidence",
  "title": "Review evidence",
  "description": "Inspect the evidence within the assigned scope.",
  "text": "Review {topic}; explain the observed evidence and unknowns.",
  "category": "Work/Review",
  "placeholders": [{"name": "topic", "description": "The evidence topic."}],
  "tags": ["evidence"],
  "roles": [],
  "state": "active"
}
```

```sh
vericairn prompt add --by <principal> --body prompt.json
vericairn prompt revise --by <principal> --body prompt.json --expected <prior hash>
vericairn prompt retire estate:review-evidence --by <principal>
vericairn prompt copy foundation:summarize-evidence --by <principal> --key local-summary
```

Revisions supersede the current record and preserve its key and copied origin.
Retirement is terminal; the key stays reserved and history remains readable.
Built-ins are read-only. Copying a reviewed built-in creates an estate definition
with `origin: {source, key, version, hash}`. A personal preference grants no shared
writing authority.

The four foundation and four software built-ins were reviewed and approved by
the operator before shipping, and can be used and copied. A pack's unreviewed
contribution stays a draft that can be inspected, favored, hidden and disabled,
but not used or copied. The built-ins' categories, descriptions, text and
placeholder descriptions are in the catalog sources. The sets are:

| Source | Key | Category |
|---|---|---|
| foundation | `clarify-next-step` | Work/Planning |
| foundation | `summarize-evidence` | Work/Review |
| foundation | `review-within-scope` | Work/Review |
| foundation | `prepare-handoff` | Work/Handoff |
| software | `review-change` | Software/Review |
| software | `plan-tests` | Software/Testing |
| software | `explain-failure` | Software/Testing |
| software | `check-compatibility` | Software/Review |

## Use and favor in one action

```sh
vericairn prompt use estate:review-evidence --by <principal> \
  --assignment assignment.json --value "topic=the assigned evidence" --favor
vericairn prompt use estate:review-evidence --by <principal> \
  --run run.json --value "topic=the assigned evidence" --favor
vericairn prompt use estate:review-evidence --by <operator> \
  --ask <ask id> --value "topic=the assigned evidence" --favor
```

Choose exactly one target. `assignment.json` is an ordinary `AssignRequest`, with
`workOrder`, `contributor`, `by`, and the complete `brief`. `run.json` is an ordinary
`RunStartRequest`, with `assignment`, `launcher`, `agent`, `by` and `repository`,
plus any ordinary run options. The nested `by` must match the command's author.
All existing assignment and run admission rules still apply. This operation
does not steer a run already in progress.

An ask answer requires a current, open, unanswered ask addressed to the acting
principal. It creates a signed decision whose verbatim statement is the rendered
prompt. Existing decision rules still require the deciding operator. It cannot
replace an answer or add affected work, policy changes, document scope or duties.

Each placeholder has an ASCII name beginning with a letter. Supply each exactly
once; empty, missing and extra values are refused. Substitution is literal and
happens once: braces inside a value do not introduce another argument. Values
are at most 4,000 characters each; definitions are at most 20,000 and rendered
comments at most 40,000. Ask answers also retain the decision statement's existing
20,000-character bound. Values and rendered text remain in the target record
under the estate's existing read access.

`--expected` pins the definition hash. When omitted, the command reads the current
offer and passes its hash; the runtime checks it again. Disabled, retired, stale,
unreviewed or role-inappropriate selections are refused before use. The author
and receiving contributor must both suit the prompt's roles.

The target's optional `promptUse` stores `source`, `key`, `version`, `hash`, the
full `definition`, exact `values`, `rendered` text and `authority: unchanged`.
For estate prompts, `version` is the saved record id and `hash` is its record hash.
For built-ins, version identifies the release or pack build and the hash covers
the canonical definition. The target envelope records who used it and when.
Run and decision revisions keep the original use unchanged. Later library edits,
hiding and disabling do not rewrite that evidence.

Prompt text is a comment addendum. It cannot supply changes to the work's outcome,
target, budget, data disclosure or permitted effects. Such changes require an
amendment under ordinary authority. The worker retains its existing cap and
admission checks; the exact prompt is part of the accounted context before
launch. Prompt text and values do not select additional records or documents.
Unknown fields in prompt requests and forged authority metadata are refused.

Successful use returns its record receipt and addendum. `--favor` then records the
author's favorite in the same command. If that second write fails, the response
reports `favorite-incomplete` with the already stored use receipt: retry only the
favorite change. A transport interruption can leave a write's outcome unknown;
inspect records before retrying a use.

## API and SDK

| Operation | HTTP | SDK, with matching `Async` member |
|---|---|---|
| List/show | `GET /v1/prompts` | `Prompts(PromptQuery)` |
| Add/revise/retire | `POST /v1/prompts` | `PromptSave(PromptSaveRequest)` |
| Favor/hide/disable | `POST /v1/prompts/preferences` | `PromptPreference(PromptPreferenceRequest)` |
| Copy | `POST /v1/prompts/copy` | `PromptCopy(PromptCopyRequest)` |
| Use | `POST /v1/prompts/use` | `PromptUse(PromptUseRequest)` |

Discovery accepts `person`, `category`, `role`, `favorites` and
`includeUnavailable`. Show selects an exact identity from this response. Saving
takes `{by, definition, expected?}`; retirement saves the current complete
definition with `state: retired`. Preferences take `{by, prompt, favorite?, hidden?,
disabled?, expected?}`. Change the estate disable separately from personal choices.
An optional preference `expected` is the prior preference record hash. Copy takes
`{by, prompt, expected, key}`.

Use takes `{by, selection: {prompt, expected, values}, assignment?|run?|ask?, favor?}`.
The existing assignment and run APIs also accept an optional `prompt` selection.
Library reads and uses return HTTP 200 with `Problem` or `Ok` describing refusals;
save, preference and copy return ordinary `WriteOutcome` receipts, or 404/422.
Malformed JSON is refused by request binding. The SDK preserves the normal
cancellation and uncertain-write semantics, with no fallback to another operation.

## MCP prompts

The server advertises `prompts` alongside `tools` and read-only resources, with `listChanged: false`.
`prompts/list` offers the connected principal's roles and personal choices, in
category order. Titles include the category. Source, version, exact hash,
favorite and draft state appear in names, descriptions and `_meta`. A connection
without a principal receives only prompts suited to every role.
Hidden, estate-disabled and retired prompts are never listed. Retrieval rechecks
the connected principal's current offers; a previously listed name grants no access.

`prompts/get` is a recorded use. It requires the same admitted operator and
signing device as `prompt_use` over MCP. Arguments are strings: each described placeholder, `_expected`
(the offer's hash), `_target` (a JSON object encoded as a string), and optional
`_favor` (`true` or `false`). The target is exactly one of `{"ask":"<ask id>"}`,
`{"assignment":{...}}`, or `{"run":{...}}`. The server supplies the connection
author and refuses an impersonated author. A successful response contains a
user text message with the addendum and `_meta` carrying the recorded id and
hash. Merely listing or inspecting a definition does not answer an ask.

The additional tools are `prompts_list`, `prompt_save`, `prompt_preference`,
`prompt_copy` and `prompt_use`. The four write tools appear only on the operator's
connection, never on a worker's, whose writes stay those of its contract; others
with a manage-prompts grant use the command line or API. Write tools use the
connection's author and ordinary runtime authority. A client should refresh discovery after a change;
there are no list-change notifications in this version. See the generated
[record reference additions](prompt-record-reference.md) and
[API compatibility](api-compatibility-0.8.4.md).
