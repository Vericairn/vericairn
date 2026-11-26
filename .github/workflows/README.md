# Workflow checks

`actions.allow` is the action allow-list: one `owner/repository[/path]@full-commit-SHA`
per line, with optional comments. Review an action and its exact revision, and confirm
the organization permits it, before adding or updating an entry. This repository's
check enforces the declared list; it does not read the organization's live settings.
Allowed owners are `actions`, `github`, and the specific action `NuGet/login`.
Tags, short hashes, expressions, local actions and container action references are
refused. Remote reusable workflows receive the same check. A reusable workflow in
this directory is checked as part of the repository.

Dependabot proposes GitHub Actions updates and NuGet package updates monthly, each
ecosystem in one grouped pull request, and only for versions published at least 7 days
earlier (`cooldown`), so a compromised version that is withdrawn within days is never
proposed. GitHub does not apply the cooldown to security updates. Each proposed action
SHA still needs review and an explicit entry in `actions.allow` from the builder; a
Dependabot update alone cannot pass the workflow policy. A NuGet update changes
`Directory.Packages.props`; Dependabot does not regenerate the runtime-specific
`packages.distributable.lock.json` files, so the package job's locked restore refuses
the update until the builder regenerates them as the software guide describes.

Each entry also records its owner's and repository's GitHub ids after the action, as
`owner=<id> repository=<id>` (CI-08), read from `https://api.github.com/repos/<owner>/<repository>`
when the entry is added. Every entry of one repository records the same ids. CI's
`--identities` run asks GitHub for the repository under each action's name, with the
workflow's read token, and fails when its ids differ, when GitHub serves it only under
another name (a rename or transfer), or when the pinned commit is not in it, so a
repository deleted and re-created under the same name by another account fails.

CI runs `ruby .github/scripts/check-workflow-actions.rb` over every `.yml` and `.yaml`
workflow, including release-only workflows. It uses Ruby's bundled YAML syntax-tree
parser, so quoted keys, flow mappings and folded values are checked too; comments
and script bodies are data. No gem installation is needed. Run its regression cases
with `ruby .github/scripts/test-workflow-actions.rb`.

The `workflow-policy` job then lints every workflow with actionlint 1.7.12, running
ShellCheck 0.11.0 over each run script; both are downloaded by pinned version and checked
against the SHA-256 their releases publish, and any finding fails the job. actionlint's
pyflakes integration is off, since no pinned pyflakes is installed. Run the same lint with
`actionlint -shellcheck <path to shellcheck 0.11.0> -pyflakes= .github/workflows/*.yml`.
A ShellCheck finding that is intended is disabled on its own line with a comment saying why.

`ci.yml` runs on pull requests, merge queue groups, pushes to main and `release/*`
branches, and manual dispatch. Its first job, `queue-tested`, looks up whether a push
brings a commit whose merge queue run of `ci.yml` passed: the queue moves the branch
to the exact commit its group's run tested, so on such a push every other job is
skipped and that commit's three-platform run is the queue's. Every other push builds
in full, as do queue groups and manual runs, for which nothing is looked up; a lookup
that cannot be completed also builds in full. Each commit on main or a release branch
thus has exactly one passing three-platform run. Look for its run among its
`merge_group` runs first, then its push run.

From CI-12 the `plan` job (`.github/scripts/ci-plan.py`) decides what else runs:

| Event | Jobs | Required check |
| --- | --- | --- |
| `pull_request`, a product change | `plan`, `workflow-policy`, `build-test` on Linux (its shares), `ci-gate` | `ci-gate`, `secret-scan` |
| `pull_request`, documentation only | `plan`, `workflow-policy`, `docs-check`, `ci-gate` | `ci-gate`, `secret-scan` |
| `merge_group` | `plan`, `workflow-policy`, `build-test` on Linux, macOS and Windows (every share), `remote-ios`, `remote-android`, `package` (four jobs), `ci-gate` | `ci-gate`, `secret-scan` |
| `push` to main or `release/*`, not queue-tested; `workflow_dispatch` | as for `merge_group` | none |
| `push` the queue tested | `queue-tested`, `ci-gate` (every other job skipped) | none |

`ci-gate` needs every other job and runs whatever they did (`always()`): a job that a
failed `needs` skips reports as skipped, which a required check counts as passing, so
the gate itself checks each result. It passes only when the lookup and the plan
passed and every job the plan runs for the event passed, with every other job
skipped; a push the queue tested passes with all of them skipped, a skip on any other
event fails it, and so does a cancelled run. For every event but a pull request it
requires the full gate whatever the plan says. Each platform `build-test` must run,
all three in the full gate and Linux for a pull request that builds, must have
exactly the shares 1 to the count that the tested commit's `.github/test-shards.json`
gives it, as `run-tests.py` splits the classes, so a faulty plan cannot leave a
share's classes untested. The gate checks out that one file, keeping no credentials,
and reads it as data; `test_ci_gate.py` runs its script on simulated results. The
branches' rulesets therefore require `ci-gate` and `secret-scan` alone, for pull
requests and the merge queue alike, whichever jobs the event ran.

A pull request does not run `package`: its enclave helper and published macOS
command line need macOS runners, of which the organization runs at most five at
once, and a queue group's run holds them all (`remote-ios` and four `build-test`
shares). A queue group runs it on the merged result before main moves. The full gate
calls `package.yml`, the same packaging workflow that `release.yml` calls after its tests.
Packaging builds and verifies NuGet packages, CLI and Workbench archives,
documentation, the bill of materials and checksums. It executes the published Linux
CLI on `sources inspect`, which starts Git. The macOS arm64 and Windows x64 jobs
download that package job's archives and run the same command; their failure fails
the reusable workflow.

Every runnable job has an explicit `timeout-minutes` limit:

| Workflow | Job | Minutes |
| --- | --- | --- |
| `ci.yml` | `queue-tested` | 10 |
| `ci.yml` | `plan` | 10 |
| `ci.yml` | `workflow-policy` | 15 |
| `ci.yml` | `build-test` (each platform and share) | 60 |
| `ci.yml` | `remote-ios`, `remote-android` | 45 |
| `ci.yml` | `docs-check` | 30 |
| `ci.yml` | `ci-gate` | 10 |
| `retry.yml` | `runner-failures` | 10 |
| `release.yml` | `verify` (each platform and share) | 60 |
| `package.yml` | `enclave-helper` | 20 |
| `package.yml` | `package` | 45 |
| `package.yml` | `published-cli` (each platform) | 30 |
| `release.yml` | `draft` | 20 |
| `nuget.yml` | `publish` | 20 |
| `mcp-registry.yml` | `publish` | 45 |
| `secret-scan.yml` | `secret-scan` | 15 |

Package installation and download steps have a 10-minute limit: .NET SDK setup,
locked restores (including distributables), the Linux secret service, gitleaks,
the local tool and SDK feed check, API baseline downloads, the bill of materials
step that installs CycloneDX, and package or artifact downloads. A hung install
fails its step when that limit expires. The job limit bounds the whole job,
including a hung test and its retry; it does not restart for each step. These
limits leave headroom over normal runs: test shares usually take 8 to 15 minutes.

The `package` callers in `ci.yml` and `release.yml` use a reusable workflow.
GitHub does not support `timeout-minutes` on those caller jobs; the limits apply
to every runnable job in `package.yml`. `ContinuousIntegrationTests` checks job
and install step limits, including the reusable workflow, so removing a limit
fails the tests.

Before packing, `api-baselines.py` discovers each packable library and downloads its
latest stable version from nuget.org. It retains the versions and SHA-256 values
under `artifacts/api-baselines`. SDK package validation runs during pack with the
corresponding file in [api-compatibility](../api-compatibility). These files list
expected breaks; empty files permit none. A missing baseline or an unlisted break
fails the job. Suppressions must name the exact diagnostic, member and compared
assembly entries, with `IsBaselineSuppression` set to `true`; record the reason in
an XML comment when adding one. Unnecessary suppressions also fail, so a later
published baseline cannot silently leave stale allowances. CI never generates
suppressions. `test-api-validation.py` tests this gate with temporary local packages
and no feed access, including a second unlisted break after an explicitly listed one.

Each self-contained publish records its resolved runtime and single-file apphost
pack identities and versions. `runtime-packs.py` adds them to the CycloneDX bill,
with `vericairn:bundled-in` properties naming each product and RID. Workbench's
membership includes the packs in its copy of the CLI. SDK-installed host packs
provide their identity through the installed pack directory when NuGet metadata
is absent; machine paths are never retained. Missing or incomplete evidence fails
packaging. `verify-sbom.py` checks these entries as well as every package in the
distributables' lock files.

`secret-scan.yml` scans each branch push, pull request, merge queue group and manual
run with the pinned, checksum-verified gitleaks release and
`src/Vericairn.Adapters.Git/gitleaks.toml`, the product's configuration. It checks
the event's commit range and keeps findings redacted. It ignores pushes to the merge
queue's temporary `gh-readonly-queue/` branches, whose commits the group's own run scans.

`release.yml` alone creates the draft. It attaches every file `SHA256SUMS` names, the
companions archive of readable build reports included (FIX-140), and once the draft
exists compares the draft's own asset list with the checksum list: a listed file the
draft does not attach fails the run, naming each one. `nuget.yml` runs on release publication or
manual dispatch; it downloads and verifies the published release's exact assets,
then publishes those packages through Trusted Publishing. It never rebuilds them.
Only its publication job can request an identity token, log in to NuGet or push.
From 0.9.4 (CI-08) that job, and `mcp-registry.yml`'s, runs in the environment
`release`, which the operator creates with themselves as required reviewer: each run
waits there until the operator approves it on the run's page, and nuget.org's Trusted
Publishing policy names that environment, so no token from another job publishes. The
workflows name the environment only; no credential is stored in the repository. A job
that requests an identity token or reads a secret must run in it.
`mcp-registry.yml` also runs on release publication or manual dispatch naming a published,
immutable release. It verifies that release's `dotnet-vc` package against `SHA256SUMS`,
checks its embedded `.mcp/server.json` with the same verifier, waits until nuget.org
serves the version, and publishes the manifest to the official MCP Registry as
`io.github.Vericairn/vericairn` with a pinned, checksum-verified `mcp-publisher`, signing
in with this repository's OIDC identity. No person's token is used.
The verifiers' unit tests use Python's standard library:

```sh
python3 .github/scripts/verify-packages.py packages
python3 -m unittest discover -s .github/scripts -p 'test_*.py'
```

Run the SDK package gate regression with `python3 .github/scripts/test-api-validation.py`.
Run the publish evidence regression with `python3 .github/scripts/test-publish-evidence.py`.
The package job disables Python byte caches so checks before documentation generation
leave the build sources clean.

## Merge queue

When a branch's ruleset requires a merge queue, a pull request reaches it through
the queue. The pull request's required checks pass first; it need not be up to date
with the branch. `gh pr merge <number>` (or Merge when ready) then adds it to the
queue, and GitHub builds a temporary `gh-readonly-queue/<branch>/...` branch holding
the branch and every pull request queued ahead of it, merged as merge commits, and
sends `merge_group`. `ci.yml` runs the full gate on that event and `secret-scan.yml`
its scan, so the required checks report on the exact commit the branch would move
to. When they all pass, the branch moves to that tested commit; when one fails, the
pull request leaves the queue and the groups behind it are built again without it.
The branch's own push run still follows every merge, and finds the queue's run.

A required check that never reports holds the queue, so only checks every pull
request and every group report are required: `ci-gate` and `secret-scan`, each from
GitHub Actions. A docs-only group still runs the full gate: documentation is an input
of tests on every platform (the guides' checks, the generated references under
`docs/reference`) and of every package (`docs/nuget-readme.md`), and each commit on
the branch keeps one passing three-platform run, the queue's, which its push run
relies on. `release.yml`, `nuget.yml` and `mcp-registry.yml` never run for the queue,
so none of their jobs can be required. `vericairn changes merge` uses the synchronous
merge API and refuses a repository that requires a queue
([checked repository changes](../../docs/changes.md)), so here a pull request is
queued instead.

`../rulesets` holds the rulesets this needs, which the builder applies; nothing in a
workflow changes a repository setting. `default-branch-merge-queue.json` is the
default branch's ruleset with `ci-gate` and `secret-scan` as its only required checks.
`release-branches.json` gives `refs/heads/release/*` the same rules but the queue: no
deletion or force push, pull requests merged by merge commit, the same required
checks. GitHub refuses a merge queue on a wildcard ref ("Wildcard ref names are not
supported when merge queue is enabled"), so from CI-14 each release branch has a
ruleset of its own that requires the same merge queue on its exact ref.
`release-branch-merge-queue.template.json` is that ruleset with `@VERSION@` standing
for the release's version in its name and its one ref: filled in, it is
`release-<version>-merge-queue` for `refs/heads/release/<version>`. It is never
applied as it stands. `ContinuousIntegrationTests` fails when a ruleset here pairs a
merge queue with a wildcard ref.

A release branch these rules cover must carry this `ci.yml` and `secret-scan.yml`:
without them its pull requests never report `ci-gate` or `secret-scan` and its queue
groups never start, so it could not be merged into. The deletion rule keeps a covered
branch from being deleted whatever workflows it carries. `release/0.3` predates both
and is excluded; it keeps no rules, as today. A release branch cut from a main that
holds this change carries both. `release/0.9.4`, cut at 0.9.4's release commit before
CI-12 merged, received them through a pull request into it; its rulesets were then
applied as `release-branches` (28004737) and `release-0.9.4-merge-queue` (28004740).

To apply, with the queue empty and the change that adds `ci-gate` having passed its
pull request run, save the default branch's live ruleset and replace it:

```sh
gh api repos/Vericairn/vericairn/rulesets/27077099 --jq '{name, target, enforcement, conditions, bypass_actors, rules}' > <scratch>/default-branch-merge-queue.live.json
gh api --method PUT repos/Vericairn/vericairn/rulesets/27077099 --input .github/rulesets/default-branch-merge-queue.json
```

Then queue that change. Before the PUT its own pull request cannot be queued, since
its run has no macOS legs, which the live ruleset requires. A pull request whose last
run used the older `ci.yml` reports no `ci-gate` and can be queued only after a new
push, or a close and reopen, builds a fresh merge commit: re-running its old run
reuses the old merge commit. An entry already queued at the PUT was built with the
older `ci.yml` and holds the queue until the 90-minute check timeout, hence the empty
queue. Every misstep fails closed. The queue builds up to two groups at once; each
full gate wants the five macOS runners, so two groups share them and take longer, and
building one group at a time (`max_entries_to_build: 1`) keeps a group's run at its
own length.

If that change's group fails, restore the saved ruleset:

```sh
gh api --method PUT repos/Vericairn/vericairn/rulesets/27077099 --input <scratch>/default-branch-merge-queue.live.json
```

The saved file holds exactly the six fields the ruleset update and create endpoints
take in their request body, the same six the files in `../rulesets` hold. GitHub's
answer to the read also carries fields that body does not list (`id`, `node_id`,
`source_type`, `source`, `created_at`, `updated_at`, `current_user_can_bypass`,
`_links`); the `--jq` filter leaves them out, so the rollback sends nothing the
endpoint does not describe.

Once the change has merged and every release branch the rules cover carries this
`ci.yml` and `secret-scan.yml`, create the release branches' ruleset, once; it covers
each release branch cut later from the moment it exists:

```sh
gh api --method POST repos/Vericairn/vericairn/rulesets --input .github/rulesets/release-branches.json
```

Then, for each release branch, once it is cut and carries `ci.yml` and
`secret-scan.yml`, create its merge-queue ruleset from the template, with `<version>`
the branch's version (`0.9.5` for `release/0.9.5`). Cutting a release branch includes
this step, an operator act:

```sh
sed 's/@VERSION@/<version>/g' .github/rulesets/release-branch-merge-queue.template.json | gh api --method POST repos/Vericairn/vericairn/rulesets --input -
```

Until then the branch's pull requests merge without a queue once their checks pass;
a queue on a branch whose `ci.yml` has no `ci-gate` would hold every entry until the
check timeout. The version is the branch's exact name after `release/`, never a
pattern, since GitHub refuses the merge queue on a wildcard ref.

Each create returns the new ruleset's `id`. To undo one, a ruleset created too early or
for the wrong branch, delete it by that id; a create that GitHub refused made nothing:

```sh
gh api --method DELETE repos/Vericairn/vericairn/rulesets/<id>
```

## Shards

`build-test` runs one share of every test assembly's classes per leg.
`.github/test-shards.json` gives each platform its number of shares and holds every
class's measured seconds; `shares.py` splits each assembly's classes longest first,
each to the share with the least so far, so the assemblies, which run side by side,
finish together in every share. A class the plan does not list yet weighs its
assembly's median. A platform's `reserve` starts its first share with that fraction
of each assembly's total, because share 1 also runs the companion check, the cost
measurements, the CLI smoke and the local feed check. `release.yml` runs three shares
per platform from the same seconds, without a reserve. Every share is checked against
test discovery before it runs, so every class runs exactly once per platform.

Each leg uploads its xUnit XML reports as `test-reports-<platform>-<share>` for 14
days. To rebalance, download the reports of a full gate, a `merge_group` or push run
of `ci.yml` and never a pull request's, which holds Linux's only and ran that pull
request's code, and write their times into the plan; review the change like any
other. Nothing reads the reports otherwise, and the plan only weighs the classes
discovery lists, so a bad time can unbalance the shares but never drop a class:

```sh
gh run download <run> --pattern 'test-reports-*' --dir <reports>
python3 .github/scripts/shard-durations.py <reports>
```

## Documentation fast path

A pull request whose changed files are all documentation (under `docs/` but not
`docs/releases/`, and the root `README.md` and `ROADMAP.md`) runs `docs-check`
instead of `build-test`: the solution's Release build and only the test classes that
read the repository's documentation, those whose sources find the repository root by
`Vericairn.slnx` and name `docs/`, `README.md` or `ROADMAP.md` (`docs_classes.py`),
each failed test retried once as elsewhere. The classes need the build: they run the
command line and the documentation tooling. A pull request's files are its merge
commit's difference from its base, renames as a deletion and an addition, and any
file outside documentation, or a difference that cannot be read, makes it a product
change. The queue still runs the full gate.

## Runner failures

GitHub has no job-level retry. `retry.yml` runs when a `ci.yml` run completes and
fails, from the default branch's own files, and re-runs that run's failed jobs once
(with the jobs that need them, `ci-gate` among them) when every job that neither
passed nor was skipped, `ci-gate` apart, is a runner failure: GitHub's annotations
say the process was killed (exit code 137) or the runner was lost, shut down or never
acquired, and every step up to the one that ended the job is a setup step (its
set-up, the checkout, the .NET SDK, a locked restore, a pinned tool or workload
install). A test or build failure, a timeout or a cancellation is never re-run, and
since GitHub's re-run of failed jobs also takes timed-out and cancelled jobs, a run
holding one is not re-run at all. Only a first attempt is re-run, and only while it
is still the run's latest; a pull request's run only while no newer run of its branch
exists, since the re-run joins the pull request's concurrency group and would cancel
the newer head's run. `runner-failures.py` decides; its exit status is 0 for a run
only runners failed, 3 otherwise. A pull request can earn its own run one re-run by
naming a step like a setup step or making a setup step report exit code 137 (a restore
target can): one more attempt of its own code with its own token, which the queue's
full gate still follows.

A queue group's run is not re-run: when its `ci-gate` fails, the queue has already
removed the pull request. The builder's queue script (`merge_queue.zsh` in the
builder's tools) handles it wherever the base branch's rules require `ci-gate`:

- It waits on the pull request's `ci-gate` and `secret-scan` checks, not on
  `build-test`, since a pull request runs Linux shares only and a docs-only one none.
  When `ci-gate` fails, it runs `runner-failures.py` on that `ci.yml` run without
  `--rerun`; on exit 0 it waits for `retry.yml`'s second attempt, otherwise it stops.
- It counts the pull request as having left the queue only when its timeline records
  a removal for a reason other than its merge and it is still open, never on an empty
  queue entry alone. It then finds the group's run: the latest `merge_group` run of
  `ci.yml` since the pull request was queued whose `head_branch` starts
  `gh-readonly-queue/<branch>/pr-<number>-`. It queues the pull request once more only
  if the queue removed it for failed checks, that run concluded `failure` at its first
  attempt, the group's `secret-scan` run passed, the pull request is still open and
  mergeable, it has not been queued again for this reason, and `runner-failures.py`
  exits 0 on the run. A removal for any other reason, a failed secret scan, a conflict
  or a cancelled group, is never re-queued, even when a `ci.yml` job also had a runner
  failure.

## Superseded runs

`ci.yml` and `secret-scan.yml` group each pull request's runs, and a newer commit
cancels the run still testing the older head. A push, to main, a release branch or
any other branch, a merge queue group and a manual run each have a group of their
own, so none is ever cancelled or held back; a push's scan reads only the commits that
push brings, so it must finish. `release.yml`, `package.yml`, `nuget.yml`,
`mcp-registry.yml` and `retry.yml` define no group, so no release, publication or
re-run is ever cancelled.

## Caching

`build-test`, `docs-check`, `remote-ios`, `remote-android` and a change's `package` job cache the
NuGet global packages folder with the pinned `actions/setup-dotnet`'s own cache; no
other cache action is used. The key is the runner's operating system and the hash of
these files:

| Job | Hashed files |
| --- | --- |
| `build-test`, `docs-check` | `global.json` and every `packages.lock.json` |
| `remote-ios`, `remote-android` | the same and `ci.yml`, which pins the workload set whose runtime packs their restores download |
| `package`, for a change | `global.json`, every `packages.lock.json` and every `packages.distributable.lock.json` |

A cache is restored only on an exact match of its key, never a partial one, and saved
only when the job succeeds without one. A pull request's or a queued group's saved
cache stays with that branch; every run can read main's. Every restore stays locked
(`--locked-mode`, and `RestoreLockedMode` for CI's implicit restores), so NuGet
compares each package's SHA-512 with its lock file; for a cached package that is the
hash NuGet recorded when a successful run downloaded it. A release caches nothing:
its `package` job downloads every package afresh, and `release.yml`'s `verify` jobs
use no cache.

Nothing a job builds, packs, publishes or signs is cached. The local tool and SDK feed
check keeps its empty source list and fresh package folder. gitleaks, CycloneDX and the
iOS and Android workloads are installed afresh by pinned version. A workload install
takes 15 to 30 seconds, and a cache would have to restore packs and installation
records into the SDK's own directory, where the SDK trusts whatever it finds.
