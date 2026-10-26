# Operator requests

An operator request keeps an action someone asks you to perform, its reason, the
exact text, command or files by hash, its destination, who asked, and the exact
work order or release it serves. Requests and their outcomes remain in the estate.

```sh
vericairn operator list
vericairn operator list --json
vericairn records show <request-revision>
```

The list shows each request's recommendation first. It delivers the oldest three
open requests and identifies the rest in order. Closing one brings the next into
view. The JSON list contains every open request, including its current revision
and hash. Status and Workbench's attention view show the same queue. Workbench
opens the exact request when you select it. Remote request presentation is planned
for 0.9.0.

## Close a request

Run either command as the operator the request addresses, using your admitted
signing device. Each command writes one signed outcome and keeps the request.

```sh
vericairn operator done <request-revision> --by <operator-id>
vericairn operator done <request-revision> --by <operator-id> --link https://example.test/announcement
vericairn operator dismiss <request-revision> --by <operator-id> --reason "This action is no longer needed."
```

`--expected <request-hash>` additionally checks the displayed hash. The revision
must still be current and open. A stale revision, a second close, a dismissal
without a reason, or a close without an admitted signing device is refused.
`--evidence evidence.json` supplies an optional array of objects with `summary`
and optional `hash` and `location`. Standard input is available with `--evidence -`.
A completed publication requires `--link` to retain its observed destination.

These commands record what happened. They do not perform the action, post content,
accept work, publish a release, push or merge the estate. `operator close`, which
accepts reviewed work, and `operator record`, which publishes estate changes,
remain separate commands.

## Record and revise a request

```sh
vericairn operator request --by <requester-id> --subject work-order:<id>:<hash> --body request.json
vericairn operator request --by <requester-id> --subject work-order:<id>:<hash> --supersedes <request-revision> --expected <request-hash> --body revised-request.json
```

The body has these fields:

| Field | Meaning |
| --- | --- |
| `to`, `askedBy` | The addressed human operator and the requester. For a new request, `askedBy` is its author. |
| `action`, `why` | What is asked and why. |
| `recommendation`, `consequence` | What the requester recommends and what waits while the request remains open. |
| `kind` | `action` or `publication`. |
| `destination` | The intended destination, named without a machine path. |
| `content` | At least one item with `kind` and `hash`. A `text` or `command` item includes its exact `text`; its SHA-256 hash covers those UTF-8 bytes. A `file` item includes its `name` and the hash of its bytes. |

The subject pins the work order or release served by this request. Revisions keep
the requester, addressed operator, subject and carry-over source. A changed draft
gets a new record and hash; a closed request cannot be revised. Reading exact
content does not execute it.

## Carry an existing ask over

```sh
vericairn operator carry <ask-revision> --by <asker-or-addressed-operator> --expected <ask-hash> --subject work-order:<id>:<hash> --body details.json
```

`details.json` supplies `kind`, `destination`, `content`, and optionally `why`.
The runtime copies the ask's question, recommendation, consequence, recipient and
original requester, and pins its exact identity and hash. Only an open or notified
ask can be carried, once. Its request takes its place in the delivery queue. The
ask's original bytes remain readable through `records show` and `record_get`.
Carry-over and request closure do not answer a decision question or satisfy a goal
predicate requiring that ask to be answered. Such questions still need a decision.

## An agent records the operator's words

An agent may record a close using its own admitted device. It must cite the
addressed operator's verbatim words, where they were said, and when. The record
keeps the agent as its author and the operator as the source of the statement.

```sh
vericairn operator done <request-revision> --by <agent-id> --operator <operator-id> --statement "I have done it." --source "Operator conversation, message 4" --said-at <utc-timestamp>
```

The same citation options apply to `dismiss`, together with `--reason`. The words
must follow the exact request and precede their recording. An agent's paraphrase
or inference is not the operator's statement.

## An observation closes a request

A signed `observation` with `kind: "operator-request"` and the exact request as its
subject closes it as done. Its `facts` contain `outcome: "done"`, the requested
`destination`, an observed `location`, and `evidenceHash` for the retained proof.
It also carries the usual `asOf`, `summary` and `limitations`. The observation must
follow the request and cannot claim a future effect. It creates no additional
closure record.

A publication request is recorded before the effect. After the operator posts it,
the outcome or observation records its actual destination. The software posts
nothing on their behalf. Evidence remains a claim to inspect, not a grant of
publication or acceptance authority.

## API, SDK and MCP

The SDK exposes `OperatorRequests`, `OperatorRequestWrite`, `OperatorRequestCarry`,
`OperatorRequestDone` and `OperatorRequestDismiss`, with cancellable asynchronous
counterparts. The API routes are `GET/POST /v1/operator/requests`,
`POST /v1/operator/asks/{id}/carry`, and
`POST /v1/operator/requests/{id}/done` or `/dismiss`. Writes return `WriteOutcome`;
inspect both its receipt and problem. After a connection interruption, inspect
the exact request and its outcomes before retrying.

MCP exposes the read-only `requests_list` tool, with optional `to` and `form`
(`text` or `json`). It delivers at most three full requests and identifies the
remaining queue. `record_get` reads exact drafts and retained outcomes. No MCP
tool closes a request or publishes its content.

Both new record types are additive in record format 3. `records describe
operator-request` and `records describe operator-request-close` describe their
fields; generated record references and readable companions use the same registry.
