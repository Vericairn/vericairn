# Progress targets

Revision 2, November 27, 2026. Describe how a follower records comment attempts and surfaces uncertain deliveries after an interruption (FIX-177).

Revision 1, November 21, 2026. Describe progress targets, the GitHub Discussions adapter, their forms and triggers (WATCH-06).

From 0.9.4 (`WATCH-06`), Vericairn keeps the estate's progress at a target, such as a pinned GitHub discussion, so you follow it
where you already read: one command refreshes the target's text whenever an event its configuration names happens, and adds a
comment, which notifies the target's readers, only for the events you choose. The text is the progress report's own sections, from
the same records as `vericairn progress`. Where it is placed is decided by an adapter chosen by the target's configuration, as a
product chooses its release and pull-request providers: GitHub Discussions is the first adapter, and later targets, such as a
website status page or a chat channel, are adapters on the same contract. What the text says, and what a public target may carry,
stay Vericairn's: the adapter only places the text it is given.

## Set a target

A target is configured on this machine, beside the estate's live store, never in a record. Name it, choose its adapter and give
the adapter's settings and the target's form:

```sh
vericairn progress target set status --adapter github-discussions \
  --setting repository=Example/example-estate --setting category=Announcements --form private
vericairn progress target list
vericairn progress target remove status
```

| Option | Meaning |
|---|---|
| `--adapter <id>` | The adapter that places the target, by id; no other is ever selected. This build ships `github-discussions`. |
| `--setting <name=value>` | One of the adapter's settings; repeatable. An adapter refuses a setting it does not declare and requires the ones it needs. |
| `--form <private\|public>` | The text's form; see [Forms](#forms). |
| `--title <text>` | The place's title, "Progress of" and the estate's name when omitted. |
| `--refresh <trigger>` | An event that refreshes the target; repeatable. The defaults when omitted; `none` for no event. |
| `--comment <trigger>` | An event that adds a comment; repeatable. The defaults when omitted; `none` for no comment. |

`set` again replaces the configuration and keeps what the follower kept. `remove` forgets the target on this machine; the place it
kept, such as a discussion, stays where it is. The configuration is `progress-targets.json`, and what a follower keeps (the place
it created, the event cursor it reached, what it last saw waiting and its comment attempts) is `progress-targets-state.json`,
both owner-only in the live store's directory.

## Refresh and follow

```sh
vericairn progress target refresh status --dry-run
vericairn progress target refresh status
vericairn progress target follow status
```

`refresh` brings the target equal to the estate's progress now: the adapter locates the target's place, Vericairn composes the
text in the form the destination allows and checks it, and the adapter creates the place the first time, or replaces its text
when it differs; when it is already equal nothing is sent. It prints what it did (`created`, `replaced` or `unchanged`), the
place's address, the form, how many records the text carries and, for the public form, the keys of the work orders it left out.
`--dry-run` locates the place, composes the text and shows it, and sends nothing. `--json` writes
`vericairn.progress-target-outcome/1`: `action`, `url`, `form`, `detail`, `carried` (each record the text carries, by type, id and
hash), `leftOut` and, in a dry run, `text`. It exits 0 when the target is equal, 2 when it refused and sent nothing, and 1 when an
effect was stopped for a person or its outcome is uncertain.

`follow` is the one command that keeps the target equal. It follows the event journal of the daemon serving the estate, so the
daemon must be running (`vericairn runtime start`): once the stream is open it refreshes the target, so no change made before is
missed, then refreshes it for each event a refresh trigger names and adds a comment for each event a comment trigger names, until
interrupted. Events read together refresh the target once. After a reset (a cursor the journal can no longer honor) it refreshes
again. It keeps the cursor it reached after each batch and resumes there when started again. `--count <n>` stops after `n`
outcomes. A refresh or comment that an adapter refuses is printed and following goes on. One follower follows a target on a
machine at a time: a second `follow` of the same target refuses until the first stops.

From 0.9.5 (FIX-177), the follower saves a pending comment with its event or wait key, payload hash, form and destination before
calling the adapter to send it. It records a confirmed comment's address, a refusal or an uncertain outcome. With that target's
local state intact, the follower makes at most one adapter call to send a comment for each key, including after a restart or a
change to the text, form or destination. This protection begins with attempts recorded by this version and ends if the target or
its local state is removed.

A restart treats a pending comment without a saved outcome as uncertain. `vericairn progress target list` shows these comments
with the original destination and payload hash, and `--json` reports them in each target's `uncertainComments`. Comment outcomes
from `follow --json` name their event or wait in `commentKey`. An adapter without
comment receipt lookup, including GitHub Discussions, cannot establish whether the request reached the service, was accepted
or notified anyone. Check the original destination before deciding how to handle the comment manually. Following keeps the
uncertain entry and never retries it, even if the interruption happened before the request left the machine.

A refused comment is final for its key too: nothing was sent, and following does not send it again. The saved hash, form and
destination are kept for a person to check; the follower never compares them with anything.

An event's key names one change, so its entry is kept. A wait's key names what waits (an ask, an open operator request, or a
run's step and stop kind), not one time it waits, so its entry guards against a second comment only while it waits. Once it no
longer waits, a commented or refused entry is dropped, and when the same thing waits again, as a second review round of the same
step does, it is commented on again. An uncertain entry of a wait that ended is kept for a person as `ended:<time>:<key>`, where it
blocks nothing. A wait that ends and begins again while no follower is running is not commented on again, as before.

Each write of `progress-targets-state.json` reaches the disk before it replaces the earlier file, so a machine that stops loses at
most the write in progress. If the file cannot be read, following refuses rather than forget the attempts it holds; move it aside to
start every target's local state afresh, accepting that the attempts it held are forgotten.

## Forms

The **private** form carries the report's sections as the records give them: the releases (each release work order not yet
accepted with how many of its work orders are accepted, then the published releases, newest first), the next release's work
orders by state, the work in flight with who holds it, the milestones, and what waits on the operator, each ask with its question
and recommendation and each acceptance only the operator gives, and from 0.9.4 (OPS-22) each possible todo, an optional operator
request, with what it asks, whom and its recommendation, which holds nothing. It is for a destination only the estate's own people
read.

The **public** form follows the disclosure rules (decision `01M7824E3CQF5PC9P7X04KXYD7`): it carries no held security fix, no
private estate detail and no open ask before each is public.

- A work order held from disclosure (`disclosure: held`) is left out until the release that carries it is published, and so is
  every work order whose own text would disclose one, carry a blocked term of the estate's policies, a record identifier, a
  machine path, a credential or a contact address. Every count counts only what the text shows, so nothing left out can be
  inferred from a total.
- It names no principal and no record identifier, and of what waits on the operator it says only how many asks and acceptances
  and, when there are any, how many possible todos, never their text.

A target is given the private form only when it is configured private and its adapter reports its destination readable by its
own members alone; a destination anyone may read, or one whose adapter cannot tell, always takes the public form, and `refresh`
says so. Before anything is placed the whole text is checked: in every form it must carry no credential, machine path or contact
address, and in the public form nothing a disclosure hold in force holds, no blocked term and no record identifier. A text that
fails is refused with what it would carry, and nothing is sent. Comments follow the same form and checks.

## Triggers

Each target names the events that refresh it and those that add a comment, read from the event journal (decision
`01M784PSWP4N4J9EKZQ3PN6X8G`). By default a target is refreshed on a release's state change, a merged work order, a recorded result
or acceptance and a step that waits on a person, and a comment is added only for a step that waits on the operator or a release's
state change.

A trigger is a named trigger or an event kind with filters:

| Named trigger | Stands for |
|---|---|
| `release-state` | `record type=release when=state-changed` |
| `work-order-merged` | `record type=operation when=merged` |
| `result-or-acceptance` | `record type=acceptance,result` |
| `step-waiting-on-person` | `step to=person when=waiting` |
| `step-waiting-on-operator` | `step to=operator when=waiting` |

The kind is one of `record`, `attention`, `ask`, `step`, `request`, `due`, `run` and `presence`. Filters narrow it, each named
once, several values separated by commas: `type=` (subject types, such as record types), `change=` (`written`, `found`, `raised`,
`changed`, `acknowledged` or `cleared`), `category=` (attention categories), `to=` (`operator`, `person`, `agent`, `service` or a
principal's id) and `when=`, a condition read from the records:

- `state-changed`: a record whose `status` differs from the revision it supersedes, or a first revision, such as a release.
- `merged`: a `change-merge` operation observed successful.
- `waiting`: a run's step that now reaches the person it waits on, an open operator request or an ask still waiting, with `to=`
  naming who is waited on; something that already waited when the follower last looked is never commented on again, and the first
  time a target is followed nothing that already waited is commented on.

```sh
vericairn progress target set status --adapter github-discussions --setting repository=Example/example-estate --form private \
  --refresh release-state --refresh result-or-acceptance --refresh "record type=decision" \
  --comment release-state --comment "ask to=operator when=waiting"
```

An event is a hint, never authority: the filters choose which events wake the target, and every condition and every text is read
from the records before anything is placed. A trigger acts on nothing but its target.

## GitHub Discussions

The `github-discussions` adapter keeps one discussion per target in the repository and category its settings name, using your own
`gh` login; Vericairn never sees or stores the token.

| Setting | Meaning |
|---|---|
| `repository` | The repository, as `owner/name`. Required. Its Discussions must be turned on. |
| `category` | The discussion category, by name; `Announcements` by default, a category where only maintainers post. |

The first refresh creates the discussion with the target's title; pin it yourself in GitHub. The adapter finds it again by the
marker every text ends with, an HTML comment naming the target and a digest of the estate's identity, among discussions your login
wrote in that category, so the same target refreshed from another machine replaces the same discussion. A repository GitHub
reports private takes the private form; a public or internal one takes the public form. A text or comment longer than 65,536
characters is refused before anything is sent, and each is handed to `gh` in an owner-only temporary file deleted once `gh` has
answered. A creation `gh` could not confirm is reconciled by locating the discussion before another is created.

## Outward effects

Each refresh and comment is an outward effect. Under the accepted holds, reads and sends design (§ 7 there), it is
charged with every record whose text it carries, which `--json` lists as `carried`, and an effect whose exposure holds and reads
stops for a person. From 0.9.4 every effect is offered to that stop before it is sent, with its target, action, form, exact text
and the records it carries; deriving an effect's exposure, recording it on the effect's operation and holding it for a person are
planned for 0.9.5 (`GOAL-15`), so until then no refresh or comment is stopped for its exposure.
