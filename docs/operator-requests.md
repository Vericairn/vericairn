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
opens the exact request when you select it. From 0.9.0, Remote's Attention lists the same
requests; complete or dismiss one from Workbench or the command line.

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
| `optional` | From 0.9.4, `true` marks the request optional: a possible todo ([below](#optional-requests-possible-todos-094)). |

From 0.9.4 (OPS-22), recording a request, or a revision of one, refuses a pinned file the estate's objects do not hold, naming
each missing file and its hash, so its content is retrievable from any copy of the estate. Store each file first:

```sh
vericairn objects put post.md
```

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

From 0.9.5 (FIX-171) the command writes `--said-at` in the record's timestamp
form, a UTC time in whole seconds, whatever fractional seconds or offset it was
given with, and without `--said-at` it writes the time of the recording, the
latest the words can have been said. Up to 0.9.4 a close without `--said-at`
wrote an empty time, which the record refused (`body.timestamp`).

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

## Optional requests: possible todos (0.9.4)

From 0.9.4 (OPS-22), a request may be marked `optional`: an action you may or may not do, such as a post you might make. An
optional request is a possible todo. `operator list`, status, `progress` and Remote's Attention show it in its own section,
**Possible todos**, apart from what waits on you, each with its recommendation first, what it asks, where it lands, its pinned files
and the commands that fetch its kit and close it; `operator list --json` carries each under `possibleTodos` with its `guide` and
`commands`, the request queue's JSON (`operator requests`, status's `operatorRequests`) under `optional`, with its `guide`, and
`progress --json` (`vericairn.progress/1`) under `operator.possibleTodos`, each as the queue's `optional` carries it. A list or
report without one has none of these members, and its bytes are unchanged. A [progress target](progress-targets.md#forms) lists
them in its private form and only counts them in its public form. An optional request is not among the three delivered requests or
the waiting count, raises no required attention, and its events are `open` with the category `information`.

An optional request never holds a release's closing or a goal's evaluation: a goal's record predicate over requests counts no
optional request, and nothing a release closes on waits for one. It stays open until you close it, done or dismissed, whenever
you choose, on your word, with `operator done` or `operator dismiss` as above. `operator act --ready` does not list it; close it
on its own. A run's reserved step is never marked optional: its step says whether it is required.

Fetch a request's kit on any machine with a copy of the estate:

```sh
vericairn operator fetch <request-revision> --to <folder>
```

The command writes every file the request pins, under its name, read from the estate's objects and checked against its hash,
and its guide, `guide.md`: what the request asks and why, its recommendation, destination and consequence, each pinned item by
its hash with its text or command verbatim, and the `done` and `dismiss` commands that finish it. It refuses, writing nothing,
when an object is missing or does not match its hash, a name is not a plain file name or is the guide's, or a file it would write
exists already; it never replaces a file. The folder is created when missing. It signs and records nothing. `--json` prints the
request, the guide's name and each file's name, hash and size.

A requester may record a request optional from the start. A required request becomes optional only on your word: a revision you,
the operator it is put to and a person, write yourself, changing its `optional` mark and nothing else
(`vericairn operator request --by <operator-id> --supersedes <request-revision> --expected <request-hash> --body request.json`). Its
requester's own revision never turns it optional: the record rules refuse it, and a goal's predicates and the queue read such a mark
as counting for nothing, so the request still holds what waits on it. A revision that makes a request required again always counts,
and the requester's later revisions keep the mark as it stands. A closed request cannot be revised.
The `optional` field is additive within record format 3: a reader before 0.9.4 refuses a request that carries it, so an estate
marks requests optional only after every writer upgrades.

## A run's reserved step (0.9.2)

A run of a flow puts each reserved step to the operator as a request when the step
is next. `vericairn goal step` records it with an optional `flowStep`, naming the
run's goal by its first revision, the step's `key` and, for a step that runs over
a list, its `instance`. Its subject is the run's work order, and its one content
item is the step's exact command, bound from the records at that moment. A
`publish` step's request is a publication request.

Such a request is delivered only while it is due: its step waits on the operator,
it carries exactly the command the records give now, a principal who takes the
run's steps recorded it, and no earlier request carries the step. Otherwise it is
not delivered. It is `settled` once the step is done from the records of the
operator's own act, and `withdrawn` when the step no longer waits on it or its
command is no longer the one the records give. The JSON list names each such
request under `withdrawn`, with its state and why, and the text says so too; a
list without one has no `withdrawn`. `operator list` reads the run now, and
canonical status as of its newest record. A request's events follow the same
delivery: `open` while it is delivered, then `withdrawn`, `settled` or `closed`. It
stays open, so close it with `done` or `dismiss` when you wish. Closing a request
never completes its step, whoever closes it: the step is done only once the
operator's own authentic act is recorded. Only the addressed operator's own signed
close stops the step's request being made again; an agent's close, citing your
words or not, leaves it due. A request recorded by a principal who does not take
the run's steps is never delivered. The list shows a step request's checked
command first and labels its recommendation and other prose as the requester's,
not checked; a client that cannot read the runtime's queue shows no step request
as open. See
[stops and their surfaces](operator-guide.md#stops-and-their-surfaces-092).

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
