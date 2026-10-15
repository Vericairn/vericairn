# The software journey: repositories, checks and releases

How a software product goes from a repository to a published release with its evidence as records,
intent recorded before every external effect, and publication left to the operator.

## Where each part lives

The foundation (`Vericairn.Records`, `Vericairn.Runtime`, `Vericairn.Protocol`, the daemon, the SDK, the
command line and Workbench) holds the record types, the release operations as intent then observed effect,
the provider and adapter contracts, and a registry of the implementations a host makes available. It knows
no particular product, provider or source host; a test holds that the records, runtime and protocol
assemblies reference none of the modules below. The command line, which hosts the modules, references all
three. The modules ship inside the Community distributable until packages exist (milestone M4):

| Module | Assembly | What it is |
|---|---|---|
| The git adapter (`vericairn.adapter.git`) | `Vericairn.Adapters.Git` | `GitCommandAdapter`: read-only observation of a repository over the git command; also `GitFileSource`, the software pack's living-document source `vericairn.software/git-file` |
| The GitHub provider | `Vericairn.Providers.GitHub` | `GitHubReleaseProvider`: releases built by a dispatched GitHub Actions workflow, observed as runs and drafts through the user's own GitHub CLI login |
| The Software Domain Pack (`vericairn.software`) | `Vericairn.Packs.Software` | Repositories as resources and observations, build and test checks as verification evidence |

The command line is the host that registers them: being available selects nothing. A release goes through
the provider its product declares or its request names, and a provider with no implementation on the
runtime's machine is refused, never replaced by another (`vericairn release providers` lists what is
available, with the settings each declares).

## The git adapter

Contract `vericairn.adapter.git/1` (`ISourceAdapter`): read-only observation of a repository. `IsRepository`
says whether a path is inside a repository with at least one commit; `Inspect` returns the HEAD commit, the
branch, the origin remote, the root commit, whether the working tree is clean with modified and untracked
counts, the tags and the ten most recent commits, with an as-of time. The adapter never fetches, commits,
pushes or changes the index; a repository without commits is refused with the next step rather than
invented. Two implementations satisfy one conformance suite: `GitCommandAdapter` runs the git command on the
PATH (in `Vericairn.Adapters.Git`), and `RecordedSourceAdapter` (beside the contract in the runtime) replays
recorded observations for offline fixtures and tests; the recorded one qualifies no real repository.

Commands:

| Command | Does |
|---|---|
| `vericairn sources inspect <path>` | Prints the observation; records nothing |
| `vericairn sources adopt <path> --by <id> --name <n> --role <r> [--visibility] [--notes]` | Writes a `resource` (kind repository) and a first `observation`; the repository is identified by GitHub owner/name, https remote or root commit, never by the path |
| `vericairn sources observe <path> --resource <id> --by <id>` | Writes a fresh observation of an adopted repository |
| `vericairn sources clone <path> --resource <id>` | Registers this machine's clone of an adopted repository, checked against the resource's root commit or GitHub remote; the path is machine configuration beside the estate's per-machine state, never a record |

The software pack's living-document source `vericairn.software/git-file` reads a document of an adopted repository at a
commit from its registered clone, with git's own object ids at that commit as the covered paths' identity: `vericairn
docs review <path> --commit <commit>` reviews it there, and `vericairn docs living` compares its covered paths at the
clone's head. Every call is a read-only git command.

## Checks as evidence

`vericairn check run --resource <id> --by <id> [--in <path>] [--role self] [--summary ...] -- <command...>`
runs the command in the repository, observes its exit code, duration and output, stores the output as an
object, and writes a `verification` of the resource at the exact commit the working tree was at, with the
platform and runtime as environment. The exit status of the command itself is in the record; `check run`
exits 0 once the record is written, because recording a failed check is the point. A check that does not
finish within `--timeout` is stopped and recorded as `unknown`.

`vericairn check observe --resource <id> --run <id> [--workflow ci.yml] [--repository <destination>] [--provider <id>] --by <id>`
asks the host for the run's status, conclusion and commit and writes a verification in the `automation`
role with the run as evidence, with the method and environment the provider reports (for GitHub,
`github-actions` on a `github-hosted` runner). The provider is the one named, else the one serving the
resource's recorded location (a repository located at `github` is served by the GitHub provider); the
destination is the one named, else the location's value. Continuous integration holds no estate key, so its
result is observed and recorded by the CLI, under the user's own login with the host; the logs are not
inspected by this command and the record says so.

In an estate at record format 2, `check run` records its verification as an observation by automation
(`evidenceClass: observation`, `observer: automation`), and `check observe` records its verification as the
provider's report (`evidenceClass: report`, `observer: provider`).

## Release providers and the product's release profile

Contract `vericairn.provider.release/1` (`IReleaseProvider`): a release is built by a dispatched build on a
ref of a destination (`Dispatch`, which returns the run id when the run could be identified), observed as a
run (`ObserveRun`) and as a draft with the checksums of its assets (`ObserveRelease`), and published by the
operator's own manual step, which `ManualPublicationCommand` names and no implementation runs. Each
implementation serves a provider id that is recorded on every operation, declares its settings with their
defaults, the setting that names the destination, and the resource locations it serves. The GitHub provider
serves `github-releases` and declares `repository` (required, the destination), `workflow` (default
`release.yml`), `ref` (default `main`) and `checksums` (default `SHA256SUMS`); it uses the GitHub CLI logged
in by the user on the runtime's machine, and Vericairn never sees or stores the token. `FakeReleaseProvider`
declares the same settings and simulates GitHub in memory for the conformance suite and the journeys. A
provider refuses before any effect (`ProviderRefusedException`, for example not logged in) or reports that it
cannot say whether an effect happened (`ProviderUncertainException`); the two are recorded differently. An
implementation also reads one asset of a release by its name (`ReadAsset`, from 0.6.0); one written for an
earlier release reads none.

A product declares how its releases are made on its own record, in an optional `release` object (read from
release 0.4.2, the first published release to read it; an estate records it only after upgrading): the
`destination` (the provider id and the settings the product fixes), the `checksums` asset, and the
`artifacts` a complete release carries, each a name with `{version}` and optionally `{platform}`, a role
(distributable, sbom, notices, documentation or other) and the platforms it is built for:

```json
{"release": {"destination": {"provider": "github-releases", "settings": {"repository": "Example/gadget"}},
             "checksums": "SHA256SUMS",
             "artifacts": [{"name": "gadget-{version}-{platform}.zip", "role": "distributable", "platforms": ["linux-x64", "win-x64"]},
                           {"name": "gadget-{version}.spdx.json", "role": "sbom"}]}}
```

A product that declares no artifacts is read by the generic rule: a platform identifier in an asset's name
gives its platform, a CycloneDX or SPDX file is the bill of materials, a notices file is the notices, a docs
archive is documentation, and a draft is complete when its checksum file names every other asset.

The release as intent, then observed effect:

1. `vericairn release submit --product <id> --version X --by <id> [--provider <id>] [--repository ...] [--ref ...] [--workflow ...] [--setting name=value]...`
   resolves the provider (the request's, else the product's declared one; neither is refused), the
   settings (the provider's defaults, then the product's declared settings, then the request's own; a
   missing required setting is refused), and writes an `operation` with status `not-started` under the
   identity `<provider>:<destination>:vX` with every setting in its target; then it dispatches and writes
   the revision that says what happened: `dispatched` with the run id, `uncertain` when no run was
   identified, or `known-failure` when the provider refused before any effect. While an operation under
   that identity is dispatched or uncertain, a second submit is refused, as it is after observed success (a
   corrective release is a new version); after a failure or a cancellation, a retry needs `--reconciliation`
   stating how the earlier outcome was settled. While a living document of the product is due at level
   `refuse`, or is unavailable while it declares `refuse`, the submission is refused before the intent,
   naming every such document ([record-format-v2.md](record-format-v2.md#living-documents-due)). Where a
   clone of its repository is registered here, each document of the product in another repository that
   declares a `refuse` level and was cleared by a review is also read at the commit the `ref` setting names
   in that clone. Nothing is fetched, so bring the clone to the commit the build will use. Bytes that differ
   from its latest review refuse the submission with both commits and hashes, and the intent records what
   was compared, or that a document could not be.
2. `vericairn release observe --version X --by <id> [--product <id>] [--provider <id> --repository <destination>] [--qualified rid]... [--limitation text]... [--signing ...]`
   finds the operation (by provider and destination, by product and version, or by a destination one
   current operation alone matches) and observes the run and the draft through the provider that
   operation records, never another. While the run is in progress or the draft is incomplete by the
   product's profile, nothing is recorded and what is missing is printed. When the run succeeded and the
   draft is complete, the operation becomes `observed-success` and a `release` record with status
   `candidate` is written with every asset's platform and the bill of materials and notices taken from
   the profile, with the run as evidence, and with the release's own record formats: those the manifest
   of the documentation set in the draft states (`recordFormat`, which the build tooling writes from the
   build), read through the provider and held to the hash the checksum file names. A draft whose set
   states none, as the sets of 0.5.2 and earlier do, gets the formats of the Vericairn that observes it,
   and the candidate's evidence and the command's answer say so, for a superseding revision to correct.
   When the run failed, the operation becomes `known-failure`. `--cancel <reason>` settles an intent that
   was never dispatched, an uncertain outcome reconciled as having had no effect, or a known failure.
3. The operator accepts the exact candidate (`vericairn records new acceptance --by <operator id> --subject release:<id>:<hash> --body <file>`,
   whose body states `statement`, `basis` and `self`) and publishes by hand with the command the CLI printed
   (for GitHub, `gh release edit vX -R <owner/name> --draft=false --latest`). Nothing in Vericairn publishes.
4. `vericairn release observe` again records the observed publication as the `published` revision of the
   candidate; without the operator's acceptance the runtime rejects it with `release.unaccepted`.
5. `vericairn estate upgrade --to X` pins the estate to the published release (for Vericairn itself; the
   bootstrap fetches `vericairn-<version>-<platform>` by the hash the published record names).

`vericairn status` lists every current operation with its state; one that is not observed-success or
canceled needs attention.

A release's documentation set (for Vericairn, `docs/releases/<version>/`) carries a manifest the product's
build tooling writes from the build; from 0.6.0 it states the record formats the build reads and writes,
which step 2 reads from the draft. `vericairn docs notes --from <previous> --to <version> --out <set>`
writes the set's release notes from the estate's records and records their window in the manifest
(`notes: {from, to}`, the records dated after `from` and up to `to`). The notes begin where the notes of
the previous set, the directory `<previous>` names beside `<set>`, ended, so no record falls between two
sets. For a previous set that records no window, as 0.5.2's and earlier sets do, they begin at the end its
notes state, to the minute, and with no previous set at the previous publication; `--since <time>` names
the beginning instead. Until the version's publication is recorded they end at the second before they are
generated.

## The synthetic migration

The move from record format 1 to 2 (`record-format-2`, [record-format-v2.md](record-format-v2.md)) stages nothing:
no record is ever rewritten, so the step only moves the manifest's format, and the tests check that every record
file is byte for byte what it was. When the estate holds admissions recorded without a signature, the move anchors
them: the migration record lists them by id and hash, and the upgrade is refused unless the operator, or a
principal with the `administer` scope, applies it with an admitted device held on this machine. The rest of the
upgrade path is proven by a synthetic migration in the tests: a step from format 2 to a pretend format 3 stages a
file through `MigrationContext`, the upgrade writes the staged files, the manifest and the migration record only
once every step succeeded, a failing migration changes nothing, and a manifest beyond the formats this binary
knows is refused rather than misread. Migrations never rewrite records; they stage other files.
