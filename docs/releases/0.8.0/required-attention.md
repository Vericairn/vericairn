# Required attention (0.7.3, FIX-22)

`vericairn status`, `vericairn progress` and Workbench show the obligations that
remain open in the available records. Every frequency, including `none`, and
every depth includes this attention. Acknowledging, hiding or filtering an item
never resolves it. Its own workflow supplies the resolving record.

Each item has a kind, the source record's type, complete id and hash, its subject
with any recorded hash, a short reason and current state, the source's recorded
timestamp, and a command to inspect it. The timestamp is evidence freshness,
not a claim of live provider health. A review opens its reviewed subject;
worker and budget notices name `vericairn worker runs`. Records, checks, findings,
ask recommendations and alternatives, and evidence remain complete at their
original read routes. The attention item contains no record envelope.

## Resolution rules

| Source | Required while | Resolving evidence |
|---|---|---|
| Verification | Its outcome is failed or unknown | A later passing verification with the same exact subject type, id and hash, method, environment and command |
| Result | It is blocked, has a failed or unknown check, or names an unresolved effect | Acceptance of that exact result; a later result under the same exact assignment; or acceptance of its work order's current revision |
| Review | Its outcome is fail or findings | A later review of the same exact subject, including a new review with findings; acceptance or supersession of the subject; its withdrawal; or acceptance of its work order's current revision |
| Operation | Its current revision is known-failure, uncertain, dispatched or otherwise unsettled | Its own later settled revision; observed-success, canceled and not-started require no operation attention |
| Work-order dependency | An open `blockedBy`, or an `after` predecessor that is paused, withdrawn or failed | The relation no longer holds, as the next-eligible view derives it |

“Later” compares the record's timestamp, then its id to break a timestamp tie.
Environment object key order does not matter; every environment value does.
A different workflow, platform or command does not clear a verification.
Commit, summary and reviewer role are observations, not check identity. No
stable check-id field is added to the record format.

A result reaches its assignment and work order through its pinned attempt and
assignment subjects. Assignment revisions remain distinct scopes. A work order
is followed through explicit revisions; acceptance of an earlier revision does
not accept the current scope. An accepted work order closes attention on its
results without authoring acceptances of those result records. Checks other
than `pass` or `passed` remain failed or unknown, including skipped and unperformed
checks. A later result can replace the old result's attention while introducing
attention of its own.

A review's withdrawal rule reads its direct subject: release `status: withdrawn`,
ask `state: withdrawn`, goal `abandoned`, and work-order `retired`. Retiring a work
order does not withdraw a result that a review judges. The current record
formats have no `failed` work-order status; failed result checks remain their
own attention. The dependency reader also recognizes `failed` and `withdrawn`
when supplied by a progress source. Waiting on a planned, active or submitted
predecessor through `after` remains in progress's held list, without becoming
required attention. An absent predecessor or an unrecognized dependency state
stays visible as unknown.

Missing records, mismatched pins, unavailable sources, incomplete resolution
chains or malformed projections do not establish resolution. The affected item
remains unknown, retaining every available reference. An unavailable entire
source has an unknown notice with its inspection command; unknown ids, hashes
and timestamps are never invented.

## Limits of inference

These rules do not interpret prose as a disposition. For example, a review that
corrects the independence label of an old document review can still have outcome
`findings`. A newer document review, a fixed implementation or acceptance of
unrelated work does not close that exact review. It stays visible until a later
review of its subject or another typed resolution above exists. Likewise, a
manual check whose environment description changes has no inferred matching
pass. A reviewer must record the appropriate evidence instead of the reader
assuming that a fix or release addressed it.

Open asks still follow their existing delivery queue, at most three per principal;
compact items point to the complete asks and state how many more are queued.
Blocked goals, exhausted budgets and worker signals retain their existing
workflow semantics. Worker signals can remain continuity concerns after an
operation settles; settling an effect is not a disposition of every worker
observation. No new acknowledgement, expiry scheduler or dismissal state is added.

## Shared and structured surfaces

`CommunicationPresentation.Attention` uses the same derivation in the runtime
and in clients connected to an older runtime. The runtime adds `attention` to
canonical status and progress JSON, including API, SDK, MCP and CLI `--json`
reads. The array is independent of recipient settings and depth. Status JSON is
identical on every machine for the same records: its attention is decided from
records alone, including recorded worker signals and goal budgets. Progress also
reads this runtime's worker observations. An attention item that includes signals
absent from its source record is labelled `Local worker observations`; these
concerns appear in human status, progress and Workbench, and never in status JSON.
Existing fields and complete original-record reads are unchanged. Human delivery
may address a recipient's asks without changing canonical JSON.
The status hash in an estate observation still covers its existing record-derived
snapshot; the added attention projection does not alter that record contract.

`CommunicationAttention` keeps its existing `kind`, `text`, `record` and
`recordType` fields and adds `hash`, `subject`, `state`, `asOf`, `command` and
`target`. `subject` and `target` use `AttentionReference` (`type`, `id`, `hash`).
Hashes and ids are complete in JSON as well as human items; nullable references
say what could not be read. These are derived API v1 additions. There are no new
CLI commands, HTTP routes, record fields, permissions or package dependencies.
