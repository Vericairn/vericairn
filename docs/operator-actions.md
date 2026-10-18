# Operator actions

The next release adds `vericairn operator list`. It shows the complete queue of what the records say
waits on the estate's operators:

```sh
vericairn operator list
vericairn operator list --json
```

Run it in the estate you intend to inspect, or select that estate with the existing `--estate` option.
Each item gives its reason, what it waits on, exact source hashes, an inspection command and the
command that records the operator's answer or action. All open and notified asks addressed to any
operator appear, including those behind the three asks in a notification delivery. Reading the queue
never answers an ask.

| Item | What resolves it |
|---|---|
| Open or notified ask to an operator | That operator's decision naming the ask in `answers` |
| Unaccepted release candidate | An operator's acceptance of that exact candidate hash |
| Submitted milestone or changed revision of an accepted milestone | An operator's acceptance of that exact work-order hash |
| Latest unaccepted result toward an unfinished milestone | An operator's acceptance of that exact result hash |
| Activated, unfinished goal whose acceptance is reserved to operators | An eligible operator's acceptance relying on its current evaluation; activation, current predicates and independence requirements remain in force |
| Accepted candidate whose publication has not been recorded | A publication record preserving the accepted contents, after the operator's publication has been observed |
| Open decision obligation owned by an operator | A discharge naming its key and evidence that meets its recorded requirement |
| Unfinished reserved work assigned to an operator | That contributor's attempt and result, within the assignment's brief and holds |

Answered and withdrawn asks, accepted current revisions, published or withdrawn releases, ended
goals, discharged obligations and completed reserved attempts leave their corresponding queue items.
A changed subject needs a new acceptance; accepting an older hash does not accept the new one.
Assignments left without their routing record do not claim to hold a step. Ordinary work and asks to
other principals do not enter this queue. A goal delegated to a non-operator with authority to accept
it is not an acceptance reserved to operators.

The list reads records. It does not discover unrecorded work, verify an external publication or
inspect whether this machine holds a device key. An accepted candidate still recorded as a candidate
says only that publication has not been recorded. Reconcile any uncertain external effect before
retrying it.

## Using a command

Every authoring command uses the existing `records new` operation with JSON on standard input.
Its `--by` identifies the operator who must act on their own device. Where several operators can
accept a subject, the item offers a command for each eligible principal. Goal evaluators and step
authors are excluded by the existing independence rules. A goal that is not ready retains its holds
and inspection command; it offers no acceptance command until the prerequisites hold.

The JSON body beside a command contains the known facts, such as `answers`, the exact acceptance
`basis` and whether `self` is true. Null fields require the operator's own words, time or evidence.
For decisions, supply the actual decision's `stake` and concrete `effects` as well. These bodies are
deliberately incomplete: they cannot be submitted unchanged as an answer or acceptance. A supplied
body still passes all existing authoring, authority and signing checks.

Inspect before acting. The commands record judgments, starts, results, discharges or observed
publication; they do not carry out the underlying reserved work. They operate in the selected estate.
The queue itself signs nothing, holds no key, and offers no operation through MCP. The command that
will also commit, scan, push and merge a signed record is a later stage, after MERGE-01's operations
are available.

## Presentation and API

Human output follows the existing communication settings, defaulting to the estate's root operator.
`--recipient`, `--assignment` and `--depth` select presentation; they do not filter the queue or change
who can act. Every item, reason, hold and command remains visible at every depth and at progress
frequency `none`. Unavailable or conflicting settings remain explicit. `--json` returns the complete
`vericairn.operator-actions/1` document independently of presentation preferences.

`GET /v1/operator/actions` and `VericairnClient.OperatorActions(recipient, assignment, view)` expose
the same read. The response contains `text`, `json` and `hasErrors`. The JSON document contains
`format`, `estate`, `items`, `hasErrors` and `findings`. Each item has a stable `id`, `kind`, `source`,
`title`, `state`, `why`, `waitsOn`, `references`, `inspect` and `commands`. Ask items also retain
`recommendation` and `consequence`; obligations retain `resolution` and `gates`. Each command has
`by`, `command`, an `arguments` array, a required-input `body` and its `purpose`.

Record errors retain the unresolved items and findings but remove authoring commands. The new CLI
command returns 0 for a valid read, 1 when the record snapshot has errors, and 2 when it cannot read
the estate. The existing `ask`, `progress`, `status` and record-writing commands keep their contracts.
