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

`ci.yml` runs on pull requests, main pushes and manual dispatch. Tests run in three
shares on each of Linux, macOS and Windows. It also calls `package.yml`, the same
packaging workflow that `release.yml` calls after its tests. Packaging builds and
verifies NuGet packages, CLI and Workbench archives, documentation, the bill of
materials and checksums. It executes the published Linux CLI on `sources inspect`,
which starts Git. The macOS arm64 and Windows x64 jobs download that package job's
archives and run the same command; their failure fails the reusable workflow.

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

`secret-scan.yml` scans each branch push, pull request and manual run with the
pinned, checksum-verified gitleaks release and
`src/Vericairn.Adapters.Git/gitleaks.toml`, the product's configuration. It checks
the event's commit range and keeps findings redacted.

`release.yml` alone creates the draft. `nuget.yml` runs on release publication or
manual dispatch; it downloads and verifies the published release's exact assets,
then publishes those packages through Trusted Publishing. It never rebuilds them.
Only its publication job can request an identity token, log in to NuGet or push.
The verifiers' unit tests use Python's standard library:

```sh
python3 .github/scripts/verify-packages.py packages
python3 -m unittest discover -s .github/scripts -p 'test_*.py'
```

Run the SDK package gate regression with `python3 .github/scripts/test-api-validation.py`.
Run the publish evidence regression with `python3 .github/scripts/test-publish-evidence.py`.
The package job disables Python byte caches so checks before documentation generation
leave the build sources clean.
