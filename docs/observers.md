# Automated observers (0.9.4, OPS-20)

A quiet system does not prove that a check succeeded. CI on the main line, a scheduled workflow, an intake watcher and a
status refresher can each stop without anyone noticing, and an exhausted CI quota or a schedule the host disabled looks
exactly like a quiet day. From 0.9.4 (OPS-20), each automated observer is declared with the interval it reports at and
the grace its silence is allowed, and it reports after each of its runs. An observer that stops reporting becomes
attention instead of reading as success.

## Declaring an observer

The operator declares each observer on the machine its reports reach, which is normally the machine where the estate's
runtime serves and the observer's script runs:

```sh
vericairn observers declare ci-main --kind ci --interval PT1H --grace PT15M \
  --summary "CI on the product repository's main line." --by <operator>
vericairn observers declare nightly --kind scheduled-workflow --interval P1D --grace PT6H --by <operator>
vericairn observers declare intake --kind intake --interval PT5M --grace PT10M --by <operator>
vericairn observers declare status --kind status-refresh --interval PT1H --by <operator>
```

The kind is `ci`, `scheduled-workflow`, `intake`, `status-refresh` or `other`. The interval is an ISO 8601 duration from
`PT1M` to `P31D`. The grace is up to `P7D` and defaults to a quarter of the interval. Declaring a name again changes its
declaration and starts its expectation again from then; its latest report still counts. At most 64 observers are declared
for one clone. `vericairn observers withdraw <name> --reason <why> --by <operator>` withdraws one that was retired, so that
its silence stops being attention. Only the operator declares and withdraws.

## Reporting

The observer, or the script that watches it, reports at the end of each run as the principal it acts for:

```sh
vericairn observers report ci-main --outcome passed --by <principal>
vericairn observers report ci-main --outcome failed --detail "Two tests failed on Windows." --by <principal>
vericairn observers report nightly --outcome not-observing --reason schedule-disabled --by <principal>
```

`passed` says it observed and found nothing wrong. `failed` says it observed a failure. `not-observing` says it could not
observe, and names why: `quota-exhausted` (for example, the CI host's minutes or spending limit ran out, so no job
started), `schedule-disabled` (the host disabled the workflow's schedule, as GitHub does after sixty days without
repository activity, or someone turned it off) or `unavailable` (any other cause). `--at` gives the time the observation
was made, to the second in UTC, when it is not now; a report is never more than a minute ahead of the runtime's clock, and
a report older than the latest one kept does not replace it. `--detail` adds what was observed, in at most 300 characters.
Only a declared observer reports.

What reports for each of the four observers the first-party estate runs:

| Observer | Reports | Not observing |
|---|---|---|
| CI on the main line | After each run on `main`, its conclusion: `passed` for success, `failed` for a failure | A run that did not start because the quota or spending limit was exhausted; a workflow the host disabled |
| A scheduled workflow | Its last step, or the watcher that reads its runs | A disabled schedule, an exhausted quota |
| Intake watching | Each completed poll | A poll that could not read its sources |
| Status refresh | Each refresh of the status it publishes | A refresh that could not reach its target |

A host that runs CI elsewhere cannot reach this machine's runtime, so the watcher that reads the host's runs reports for it,
for example from the conclusion `gh run list --branch main --workflow ci.yml --limit 1 --json conclusion,createdAt` shows.

## States and attention

Each observer's state is decided by the runtime's clock alone:

| State | When | Attention |
|---|---|---|
| `awaiting` | Declared, and its first report is not yet due | No |
| `observing` | Its latest report passed, and no more than its interval plus its grace has passed since | No |
| `failed` | Its latest report observed a failure | Yes |
| `not-observing` | Its latest report says it could not observe | Yes |
| `silent` | No report within its interval plus its grace, counted from its latest report or, when it has none, from its declaration | Yes |

Each state that is attention is a `failed check` in the operator's attention, which `vericairn status`, Workbench and Remote
show, subject `observer <name>`, with what to do next. Not observing is never a pass: only a recent report that the observer
observed and passed clears it. Elapsed silence renews nothing, so an acknowledged notice stays acknowledged while the silence
lasts, and the event journal carries the notice to the operator alone when the silence begins.

## Status shows each observer's last report

Human `vericairn status` ends with `Observers (this machine):`, one line for each observer with its kind, interval, grace,
state and latest report: its outcome, reason, time and who reported it. `vericairn observers list` shows the same, and with
`--json` the runtime's `ObserversResponse`. `status --json` stays the canonical document, identical on every machine for the
same records, and holds no observer.

## What is kept, and where

Declarations and reports are this machine's: they are kept in `observers.json` beside the clone's live store under the user's
Vericairn home, never in a record, the repository or the live store itself, so `estate cache rebuild` forgets none of them.
Another machine sees none of them. A file that cannot be read is itself a failed check, because no declared observer's silence
can be seen until it reads again. Nothing in this feature changes the record format, so a release that predates it still reads
every record the estate holds.
