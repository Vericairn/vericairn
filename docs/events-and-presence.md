# Events and presence

Vericairn tells agents and people what changed without polling, keeps track of which agent sessions
can be reached, lets people declare when they are away, and makes sure only one holder's work counts
when a step changes hands. Records stay the truth throughout: an event, a lease or an availability
setting is a hint or a declaration, never authority.

This guide covers what is built and labels what is planned:

| Part | State |
| --- | --- |
| The event journal, the API stream, long polls, `vericairn events watch` and the SDK | Built in 0.9.2 (`WATCH-05`) |
| MCP subscriptions, notifications and `events_read` | Built in 0.9.2 (`MCP-05`) |
| Presence leases for agent sessions, lapse attention and the lapse ask | Built in 0.9.4 (`ID-11`) |
| The displaced-holder refusal and the displaced label | Built in 0.9.4 (`ID-11`) |
| Availability and quiet hours on a person's communication setting | Built in 0.9.4 (`GOAL-11`) |
| Routing past an away person, the session route, the widened hand-over | Built in 0.9.4 (`GOAL-11`) |
| Remote's notifications and reminders, which apply quiet hours | Planned for 0.9.5 (`REMOTE-05`, `WATCH-04`) |
| The carrier's reassignment after a lapse | Planned for 0.9.6 (`GOAL-10`) |
| Signed, filtered webhooks | Planned (`CODE-50`, the `future-work` backlog) |

## Hearing what changed

### The event model (0.9.2)

The daemon keeps an event journal: one runtime's bounded, ordered list of events, at most seven days
and 100,000 events, owner-only beside the live store. The embedded runtime keeps none, so start the
daemon (`vericairn runtime start`) before anything follows events.

An event says that a record was written or that a derived state changed. Its envelope,
`vericairn.event/1`, carries:

| Field | Meaning |
| --- | --- |
| `key` | A hash of the kind, subject, exact sources and state, stable when the same change is derived again |
| `cursor` | The opaque position after this event, sealed for the reader; resume from it |
| `kind` | `record`, `attention`, `ask`, `step`, `request`, `run` or `presence`; `due` is reserved for reminders, which are planned |
| `change` | `written` or `found` for a record; `raised`, `changed`, `acknowledged` or `cleared` for a derived state |
| `at` | When this runtime observed the change |
| `subject` | What changed: a record by type, id and hash, or another subject by an opaque name |
| `sources` | At most 16 records the change rests on |
| `to` | The principal concerned, when one is |
| `category` | One of the six attention kinds, when the change is attention |

An event never carries a body, title, summary, path, secret or command. It is a hint: read the subject
through its own checked read before acting on it, so an event can neither give instructions nor
reveal anything early. Events are ordered as this runtime observed them; nothing orders events across
runtimes.

From 0.9.4 a `presence` event says that an agent session holding work lapsed, that the runtime asked
about it, or that the lapse is over (see [presence](#presence-agent-sessions-and-their-leases-094)). Its
subject is the session, named by an opaque digest, and its sources are the assignment and the ask.

### Filters and the audience check (0.9.2)

A reader narrows what it hears with `kinds`, record `types`, `subjects` (a goal matches its work
orders, steps and runs), `to` and `categories`. A filter only narrows. At delivery the runtime checks
that the reader's device is still admitted with the key it used, that its principal may read the
subject and every source, and that per-principal state, such as attention and its acknowledgments,
goes only to its principal. A withheld event is dropped, and no count, cursor, error or timing tells
it from none.

### Following the stream (0.9.2)

```sh
vericairn events watch --kind record --kind ask
vericairn events watch --kind presence --json --after <cursor>
```

`events watch` follows `GET /v1/events` over the local socket for the principal you name, the operator
by default, and reconnects by itself. Every state read it complements (`GET /v1/status`, `/v1/progress`,
`/v1/attention`, `/v1/asks` and `/v1/goals/{id}`) returns `eventsCursor`: read the state, then follow
from that cursor, and no change is missed. A `reset` means the cursor could not be honored (too old,
from another epoch, sealed for another principal): read the state again and continue from the new
cursor. Delivery is at least once within the journal's seven days.

The SDK's `StreamEventsAsync` follows one stream and `WatchEventsAsync` follows across reconnections;
`Events` is a long poll. A paired device reads events only for its own principal; its stream ends after
60 minutes and reconnects with a fresh signature. Streams and long polls share 16 slots per runtime, at
most four per device and eight per principal; the local socket keeps two of its own, so devices never
lock out the runtime machine's user. A script that follows `events watch` and runs `goal step` acts with
its own principal's authority and stays Community.

See [API version 1](api-v1.md#the-event-stream-092) for the stream's wire format.

### MCP notifications and `events_read` (0.9.2)

An MCP session subscribes to a record, brief, document, goal-progress or asks resource, and the server
sends `notifications/resources/updated` at most once a second per resource and
`notifications/resources/list_changed` at most every five seconds. Over standard input and output the
notifications are lines between responses; over Streamable HTTP they ride the session's server stream.
A harness that hides notifications calls `events_read` with the cursor it last returned. Which harness
versions show notifications is recorded per version as a qualification; none is claimed here. See
[the MCP server](mcp.md#subscriptions-and-notifications-092).

### Remote notifications and reminders (planned)

Planned for 0.9.5 (`REMOTE-05`, `WATCH-04`): Remote subscribes to this stream for its person, and the
runtime decides what becomes a phone notification from events, communication settings and quiet hours,
coalescing per subject within two minutes and keeping three asks per delivery. Until then nothing sends a
notification outside the estate; Remote refreshes when asked.

### Webhooks (planned)

Planned (`CODE-50`, in the `future-work` backlog for the first 0.10.x release): signed, filtered
webhooks for outside systems, with signing secrets by reference, retries, dead letters and address
checks. Nothing sends webhooks in this build.

## Presence: agent sessions and their leases (0.9.4)

From 0.9.4 (`ID-11`) the daemon registers which agent sessions can be reached. A session that holds a
step and goes silent is noticed and asked about, instead of going unnoticed until someone looks. People
are never tracked by their connections: a person declares availability instead
([availability](#availability-and-quiet-hours-for-people-094)).

### Registering and renewing

An agent or service principal's session registers with `POST /v1/presence`, naming its principal, a
label without machine paths (at most 100 characters), the roster roles it offers (none offers every role
the roster names its principal for) and a capacity (1 to 16 steps at once, 1 by default). The answer is a
lease: its identity, the session's public identity, its expiry and a heartbeat interval of a third of
the lease. A lease lasts 90 seconds by default, 30 to 900. `POST /v1/presence/{lease}/heartbeat` renews
it and `DELETE /v1/presence/{lease}` releases it; `GET /v1/presence` lists the sessions, live and lapsed,
with what each holds. The SDK offers the same as `PresenceRegister`, `PresenceHeartbeat`,
`PresenceRelease` and `Presence`.

The session's transport sends the heartbeats, never its model: `vericairn mcp serve --as <principal>`
registers when it is a client of the daemon and renews while its process lives, and
`vericairn mcp serve --connection <connection>` does the same at the remote runtime for its paired
device's principal. A long model turn keeps the lease; a dead process, a machine asleep or a dropped
connection loses it. When the server ends, it releases the lease, and a release is no lapse. An MCP
server the worker starts for a run (`--run`) registers nothing: a worker run's liveness is the worker's.
The SDK's `PresenceKeeper` does the same for a session of your own:

```csharp
await using var presence = new PresenceKeeper(client, new PresenceRegisterRequest(principal, "nightly reviewer", ["reviewer"]));
PresenceLeaseResponse registered = await presence.StartAsync();
```

It renews on the heartbeat interval, registers again when the runtime no longer keeps the lease (after
a restart, or a lapse long enough that the lease was dropped), and releases the lease when disposed.

A lease's identity is its holder's alone. Only the principal, and the paired device, that registered it
renews or releases it: a lease registered over the local socket answers only the local socket, and one
registered by a device answers only that device. A device registers presence only for its own
principal. The daemon keeps only a digest of the lease identity, and no listing, attention item or
event names it. A principal holds at most eight live sessions, and a heartbeat brings a lapsed lease
back only within those eight; a principal keeps at most sixteen leases in all, its oldest lapsed leases
that hold nothing making room, and a runtime at most 64. The embedded runtime keeps no presence, and its
MCP servers register nothing.

Leases are owner-only runtime state in the live store, one runtime's observation as worker heartbeats
are; no record is written per heartbeat. Presence, worker liveness and progress claims stay separate
facts: a live lease says the session's transport is running, not that work moves, which the stall rule
and requested progress judge.

### What a session holds

A session holds the assignments routing gave it (the [session route](#the-session-route)) and those its
principal recorded an attempt, a completed phase or a progress claim on while it was live. A holding
counts while the assignment still holds its step and is unsettled: no attempt yet, or its latest
attempt without a result. When a principal has several live sessions, each holds what its principal
records, and a holding lapses only when none of them is live.

### A lapse: attention, then the ask

When a lease expires while its session holds work, the daemon raises a `presence` event and attention
for who decides for the step's goal and for every operator: an `incident` labeled a local observation of
this runtime, naming the session, the assignment and when the lease lapsed. Still lapsed after
`limits.watch.reassignAfterSeconds` (600 by default), the runtime records an ask to the goal's
accountable principal, as the stall rule does, recommending that the step be handed to another principal
of its role. Without a goal, the estate's root operator is asked. The ask is signed only by the
principal that recorded the assignment, as the stall rule signs with the principal that started the run;
no other key signs it. When that write is refused, as when this machine holds no key of that principal,
the attention says so and the sweep tries again after a minute, then after twice as long each time, up to
an hour.

Set the wait per goal or per roster, from 60 seconds to a day:

```json
{"limits": {"watch": {"reassignAfterSeconds": 1800}}}
```

A goal's value overrides its roster's. `reassignAfterSeconds` is additive in record format 3: a format-2
record cannot carry it, and a release before 0.9.4 refuses it as an unknown key, so set it only once
every machine writing the estate runs 0.9.4 or later.

A heartbeat ends the lapse: the lease is live again, its attention clears, the `presence` event says
`cleared`, and the next sweep withdraws the ask by its asker, unless a decision already answered it.
The ask is withdrawn too when a live session of the same principal picks the step up, when the step is
handed over, and when its work is settled. The daemon sweeps leases every five seconds.

### One holder acts

Only the current holder of a step counts. Only a person's act moves a step from one principal to another:
a hand-over a person records, or a person's assignment of the step to another principal (an operator's or a
sponsor's `work assign`, which needs no basis). A later assignment of the step to the same principal keeps
that principal the holder. An agent or a service, whatever its scope or grant, never takes a held step for
another principal or for itself: the runtime refuses such an assignment, and one written by hand takes
nothing and holds nothing, so a displaced agent never becomes the holder again by assigning the step to
itself, and no agent displaces a person.

From 0.9.4 the runtime refuses an attempt on an assignment that does not hold its step, and a progress
claim or result on an attempt of one, whatever time the record states. The refusal is judged against the
records as they stand when the write arrives, on every path that writes a record (the command line, the
API, a device's signed request and network MCP), and for interim progress claims, which are not records.
A backdated record does not get past it.

```text
Assignment 01J... no longer holds its step of W-1: assignment 01K... of Pat holds it now. Only the step's
current holder records a result, whatever time it states; nothing was written.
```

A record another runtime wrote before the hand-over reached it, or one written by hand, arrives without
passing this runtime's refusal. Its assignment is labeled `displaced` instead: `vericairn status` shows it
displaced rather than in progress or awaiting review, `work observe` names the assignment that holds the
step, no review is asked for its result, and routing goes on with the step's holder. A displaced result
that names unresolved effects keeps its `uncertain-effect` attention until they are resolved. For a run
of a flow only decisions under the step's current holder count in the current round; a decision dated
before a hand-over keeps deciding the earlier round it decided. A hand-over still asks the displaced
assignment's runs to stop.

## Availability and quiet hours for people (0.9.4)

From 0.9.4 (`GOAL-11`) a person declares when they are away and when notifications should wait. People
declare; nothing infers either from a connection or from silence. Both live on the person's
communication setting, a record, because routing records assignments that must be explainable on every
machine.

### Availability

```sh
vericairn communication set --as <person> --availability away --away-until 2026-12-02T09:00:00Z
vericairn communication set --as <person> --availability away --away-from 2026-12-20T00:00:00Z --away-until 2027-01-04T09:00:00Z
vericairn communication set --as <person> --availability available
```

`availability` is `available`, or `away` with `until` and an optional `from`. A person is away from `from`
(or at once) until `until`; outside that span the setting reads as available, so an expired absence needs
no second change. `communication show --recipient <person>` reports the availability in force as of the read.

### Quiet hours

```sh
vericairn communication set --as <person> --quiet-hours quiet.json
```

```json
{
  "zone": "Europe/Berlin",
  "windows": [
    {"days": ["mon", "tue", "wed", "thu", "fri"], "start": "19:00", "end": "08:00"},
    {"days": ["sat", "sun"], "start": "00:00", "end": "24:00"}
  ],
  "breakthrough": ["incident", "decision-needed"]
}
```

`quietHours` names an IANA time zone, at most 14 weekly windows (a window whose end is not after its start
runs past midnight into the next day), and the attention categories that break through, by default
`incident`, which includes continuity concerns. `--quiet-hours inherit` removes them. Quiet hours change
delivery only: views and attention show everything, and routing is unaffected.

The rule that decides delivery is built: a notification of a category that does not break through, due
inside a window, is held until the window ends, and a reminder due inside a window arrives at its start.
`communication show --recipient <person>` says whether the person's quiet hours hold notifications now and until when. A zone
this machine does not know holds nothing, so a notification is never lost to an unknown zone. Delivering
notifications to phones is planned for 0.9.5 (`REMOTE-05`, `WATCH-04`), which applies this rule; until
then nothing is sent, so nothing is held.

Both fields are additive in record format 3 and belong to a person's own scope only, never to estate
defaults or an assignment override. A format-2 record cannot carry them, and a release before 0.9.4 refuses
them, so set them only once every machine writing the estate runs 0.9.4 or later. A person changes their
own setting, or a delegate they named does (`communication delegate`).

## Routing and reassignment (0.9.4)

### Routing passes over an away person

When routing chooses who carries a step (`goal next`, `goal step`), an away person is unavailable, with
the reason `away until <time>, as their communication setting declares`. The step goes to the next
available principal the roster names for its role. When no other principal is available, the step is
still given to the away person and asked of them, shown to the sponsor as away until their return: an
ask only they can answer, such as a reserved act like a release acceptance, waits for them rather than
going to someone the roster does not name.

### The session route

An agent is available through a launcher on this machine, as before, or, when no launcher here starts
its harness, through a live session registered for the role: one whose lease is live, which offers the
step's role (or every role), and which holds fewer steps than its capacity. The agent still needs an
admitted device to sign its attempts and results. `goal next` shows such a route as `agent` with the
session's identity in `session`; no worker launches anything, so the custody guard over launches does not
apply to it. `goal step` gives such a step to the session: it records the
assignment with `routedWith: session`, alone, and the session holds it (the outcome `session`). A step
an agent already holds stays with that assignment, which the session then holds; while a live session
holds a step's assignment, `goal next` shows the step `assigned`. The session hears it through its
subscription (the assignment's `record` event, and `list_changed` over MCP), reads its brief and records
an attempt. A step it records no attempt on within five minutes counts as a lapse: attention, then the
ask after `reassignAfterSeconds`.

A live session's own holds, reads and sends are unseen by the runtime, so the session route never takes
a step whose exposure reads (`design/holds-reads-sends.md` § 7). In this build that is judged
conservatively from the records: a step is never routed to a session when its goal, a record the goal
names among its inputs, its work order, a record the work order names among its inputs, a record the step
pins (an earlier step's result, for a run of a flow) or, for a step already held, its assignments, their
attempts and results and the reviews of those results, was written by any principal that is not a person
(an agent or a service), is a conversation observation, or cannot be resolved here, since an unknown fact
counts as present. The candidate's reasons in `goal next` name each such
record. The full exposure algorithm, under which a reviewed hand-off clears what it reviewed and what a
session writes is marked outside-derived, is planned for 0.9.5 (`RUN-06`).

`routedWith: session` is additive in record format 3; a release before 0.9.4 refuses it.

### Handing a lapsed step to another principal

`vericairn work takeover` widens in 0.9.4. A person (the one taking the step over, who decides for its
goal, or an operator) may hand a step an agent holds to any principal the roster names for its role,
citing the lapse ask:

```sh
vericairn work takeover <assignment> --by <person> --contributor <principal of its role> --basis <lapse ask>
```

The hand-over is a new assignment of the same step, carrying the agent's brief, naming its assignment
among its sources and recording `basis`, the ask it answers. Handing a step to anyone but a person
requires `--basis`; a person taking a step over themselves needs none, as before. The basis is the
standing lapse ask itself: the ask the sweep recorded about that very assignment, signed by the principal
that recorded it, still open, neither withdrawn nor answered. An ask anyone else wrote is none, and a
hand-over record citing one is invalid wherever it was written. `work takeover` also requires the lapse
to stand when it is recorded: once the session's heartbeat restores its lease, the step stays with it. An agent never records
a hand-over. The displaced assignment's runs are asked to stop, and from then on its attempts, progress
claims and results are refused. `basis` is additive in record format 3; a release before 0.9.4 refuses it.

### The carrier's reassignment (planned)

Planned for 0.9.6 with the carrier (`GOAL-10`): in Standard and Pro, the carrier may hand over a step
under its `dispatch` grant once a lapse ask has stood for `reassignAfterSeconds`, to the roster's next
available principal, never itself, a reserved step or a person's step. In Community a person hands over,
as above, and this build records no hand-over without a person.

## Records and compatibility

No new record type and no format change: every addition is optional in record format 3.

| Record | Addition | Older readers |
| --- | --- | --- |
| `communication-setting`, person scope | `values.availability`, `values.quietHours` | A release before 0.9.4 refuses them as unknown fields |
| `goal`, `roster` | `limits.watch.reassignAfterSeconds`, 60 to 86,400 | Refused as an unknown key before 0.9.4 |
| `assignment` | `routedWith: session`, and `basis` naming a lapse ask | Refused before 0.9.4 |

A lapse ask is an ordinary ask. Leases, the event journal and device settings are machine state, never
records.
