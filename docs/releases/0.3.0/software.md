# The software journey: repositories, checks and releases

How a software product goes from a repository to a published GitHub release with its evidence as records,
intent recorded before every external effect, and publication left to the operator.

## The git adapter

Contract `vericairn.adapter.git/1` (`ISourceAdapter`): read-only observation of a repository. `IsRepository`
says whether a path is inside a repository with at least one commit; `Inspect` returns the HEAD commit, the
branch, the origin remote, the root commit, whether the working tree is clean with modified and untracked
counts, the tags and the ten most recent commits, with an as-of time. The adapter never fetches, commits,
pushes or changes the index; a repository without commits is refused with the next step rather than
invented. Two implementations satisfy one conformance suite: `GitCommandAdapter` runs the git command on the
PATH, and `RecordedSourceAdapter` replays recorded observations for offline fixtures and tests; the recorded
one qualifies no real repository.

Commands:

| Command | Does |
|---|---|
| `vericairn sources inspect <path>` | Prints the observation; records nothing |
| `vericairn sources adopt <path> --by <id> --name <n> --role <r> [--visibility] [--notes]` | Writes a `resource` (kind repository) and a first `observation`; the repository is identified by GitHub owner/name, https remote or root commit, never by the path |
| `vericairn sources observe <path> --resource <id> --by <id>` | Writes a fresh observation of an adopted repository |

## Checks as evidence

`vericairn check run --resource <id> --by <id> [--in <path>] [--role self] [--summary ...] -- <command...>`
runs the command in the repository, observes its exit code, duration and output, stores the output as an
object, and writes a `verification` of the resource at the exact commit the working tree was at, with the
platform and runtime as environment. The exit status of the command itself is in the record; `check run`
exits 0 once the record is written, because recording a failed check is the point. A check that does not
finish within `--timeout` is stopped and recorded as `unknown`.

`vericairn check observe --resource <id> --repository owner/name --run <id> [--workflow ci.yml] --by <id>`
asks the host for the run's status, conclusion and commit and writes a verification in the `automation`
role with the run as evidence. Continuous integration holds no estate key, so its result is observed and
recorded by the CLI, under the user's own GitHub CLI login; the logs are not inspected by this command and
the record says so.

## The GitHub Releases provider

Contract `vericairn.provider.github-releases/1` (`IReleaseProvider`): `Dispatch` runs the release workflow on
a ref with inputs and returns the run id when the run could be identified; `ObserveRun` and `ObserveRelease`
report what the host shows, with every asset's hash taken from the release's `SHA256SUMS`; and
`ManualPublicationCommand` names the operator's exact publication step, which no implementation runs.
`GhReleaseProvider` uses the GitHub CLI logged in by the user on this machine; Vericairn never sees or
stores the token, and an agent never holds one. `FakeReleaseProvider` simulates GitHub in memory for the
conformance suite and the journeys. A provider refuses before any effect (`ProviderRefusedException`, for
example not logged in) or reports that it cannot say whether an effect happened
(`ProviderUncertainException`); the two are recorded differently.

The release as intent, then observed effect:

1. `vericairn release submit --product <id> --version X --repository owner/name --by <id> [--ref main] [--workflow release.yml]`
   writes an `operation` with status `not-started` under the identity `github-releases:<owner/name>:vX`,
   then dispatches, then writes the revision that says what happened: `dispatched` with the run id,
   `uncertain` when no run was identified, or `known-failure` when the provider refused before any effect.
   While an operation under that identity is dispatched or uncertain, a second submit is refused; after a
   failure or a cancellation, a retry needs `--reconciliation` stating how the earlier outcome was settled.
2. `vericairn release observe --version X --repository owner/name --by <id> [--qualified rid]... [--limitation text]... [--signing ...]`
   observes the run and the draft release. While the run is in progress or the draft lacks a checksum for
   a distributable, nothing is recorded and the state is printed. When the run succeeded and the draft
   names every distributable with a hash, the operation becomes `observed-success` and a `release` record
   with status `candidate` is written from the observed assets, bill of materials and notices, with the
   run as evidence. When the run failed, the operation becomes `known-failure`. `--cancel <reason>`
   settles an intent that was never dispatched or an uncertain outcome reconciled as having had no effect.
3. The operator accepts the exact candidate (`vericairn records new acceptance --subject release:<id>:<hash>`)
   and publishes by hand with the command the CLI printed (`gh release edit vX --draft=false --latest`).
   Nothing in Vericairn publishes.
4. `vericairn release observe` again records the observed publication as the `published` revision of the
   candidate; without the operator's acceptance the runtime rejects it with `release.unaccepted`.
5. `vericairn estate upgrade --to X` pins the estate to the published release.

`vericairn status` lists every current operation with its state; one that is not observed-success or
canceled needs attention.

## The synthetic migration

Record format 1 is the only format. The upgrade path through a record-format migration is proven by a
synthetic migration in the tests: a migration from format 1 to a pretend format 2 stages a file through
`MigrationContext`, the upgrade writes the staged files, the manifest and the migration record only once
every step succeeded, a failing migration changes nothing, and a manifest beyond the formats this binary
knows is refused rather than misread. Migrations never rewrite records; they stage other files.
