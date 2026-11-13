# Workflow checks

`actions.allow` is the action allow-list: one `owner/repository[/path]@full-commit-SHA`
per line, with optional comments. Review an action and its exact revision, and confirm
the organization permits it, before adding or updating an entry. This repository's
check enforces the declared list; it does not read the organization's live settings.
Allowed owners are `actions`, `github`, and the specific action `NuGet/login`.
Tags, short hashes, expressions, local actions and container action references are
refused. Remote reusable workflows receive the same check. A reusable workflow in
this directory is checked as part of the repository.

Dependabot proposes GitHub Actions updates monthly in one grouped pull request.
Each proposed SHA still needs review and an explicit entry in `actions.allow` from
the builder; a Dependabot update alone cannot pass the workflow policy.

CI runs `ruby .github/scripts/check-workflow-actions.rb` over every `.yml` and `.yaml`
workflow, including release-only workflows. It uses Ruby's bundled YAML syntax-tree
parser, so quoted keys, flow mappings and folded values are checked too; comments
and script bodies are data. No gem installation is needed. Run its regression cases
with `ruby .github/scripts/test-workflow-actions.rb`.

`ci.yml` runs on pull requests, merge queue groups, main pushes and manual dispatch.
Tests run in three shares on each of Linux, macOS and Windows. Its first job,
`queue-tested`, looks up whether a push to main brings a commit whose merge queue
run of `ci.yml` passed: the queue moves main to the exact commit its group's run
tested, so on such a push every other job is skipped and that commit's
three-platform run is the queue's. Every other push to main builds in full, as do
pull requests, queue groups and manual runs, for which nothing is looked up; a
lookup that cannot be completed also builds in full. Each commit on main thus has
exactly one passing three-platform run. Look for a main commit's run among its
`merge_group` runs first, then its push run. It also calls
`package.yml`, the same packaging workflow that `release.yml` calls after its tests.
Packaging builds and verifies NuGet packages, CLI and Workbench archives,
documentation, the bill of materials and checksums. It executes the published Linux
CLI on `sources inspect`, which starts Git. The macOS arm64 and Windows x64 jobs
download that package job's archives and run the same command; their failure fails
the reusable workflow.

Every runnable job has an explicit `timeout-minutes` limit:

| Workflow | Job | Minutes |
| --- | --- | --- |
| `ci.yml` | `queue-tested` | 10 |
| `ci.yml` | `workflow-policy` | 15 |
| `ci.yml` | `build-test` (each platform and share) | 60 |
| `ci.yml` | `remote-ios`, `remote-android` | 45 |
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
limits leave headroom over normal runs: test shares usually take 5 to 25 minutes.

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

`release.yml` alone creates the draft. `nuget.yml` runs on release publication or
manual dispatch; it downloads and verifies the published release's exact assets,
then publishes those packages through Trusted Publishing. It never rebuilds them.
Only its publication job can request an identity token, log in to NuGet or push.
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

When the default branch's ruleset requires a merge queue, a pull request reaches main
through it. The pull request's required checks pass first; it need not be up to date
with main. `gh pr merge <number>` (or Merge when ready) then adds it to the queue,
and GitHub builds a temporary `gh-readonly-queue/main/...` branch holding main and
every pull request queued ahead of it, merged as merge commits, and sends
`merge_group`. `ci.yml` and `secret-scan.yml` run on that event with the same jobs as
on a pull request, so every required check reports on the exact commit main would
move to. When they all pass, main moves to that tested commit; when one fails, the pull
request leaves the queue and the groups behind it are built again without it. Main's
own push run still follows every merge.

No job of either workflow, and none of the jobs `package.yml` runs for `ci.yml`,
depends on its event: a job skipped for one event never reports its check, and a
required check that never reports holds the queue. Require every job by its check
name from GitHub Actions, the packaging chain's jobs each on their own: a job that
its failed `needs` skips reports as skipped, which counts as passing. `release.yml`,
`nuget.yml` and `mcp-registry.yml` never run for the queue, so none of their jobs can
be required. `vericairn changes merge` uses the synchronous merge API and refuses a
repository that requires a queue ([checked repository changes](../../docs/changes.md)),
so here a pull request is queued instead.

## Superseded runs

`ci.yml` and `secret-scan.yml` group each pull request's runs, and a newer commit
cancels the run still testing the older head; the jobs `package.yml` runs for that
`ci.yml` run stop with it. A push, to main or any other branch, a merge queue group
and a manual run each have a group of their own, so none is ever cancelled or held
back; a push's scan reads only the commits that push brings, so it must finish.
`release.yml`, `package.yml`, `nuget.yml` and `mcp-registry.yml` define no group, so
no release or publication run is ever cancelled.

## Caching

`build-test`, `remote-ios`, `remote-android` and a change's `package` job cache the
NuGet global packages folder with the pinned `actions/setup-dotnet`'s own cache; no
other cache action is used. The key is the runner's operating system and the hash of
these files:

| Job | Hashed files |
| --- | --- |
| `build-test` | `global.json` and every `packages.lock.json` |
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
