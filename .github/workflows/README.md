# Workflow checks

`actions.allow` is the action allow-list: one `owner/repository[/path]@full-commit-SHA`
per line, with optional comments. Review an action and its exact revision, and confirm
the organization permits it, before adding or updating an entry. This repository's
check enforces the declared list; it does not read the organization's live settings.
Tags, short hashes, expressions, local actions and container action references are
refused. Reusable workflow references receive the same check.

CI runs `ruby .github/scripts/check-workflow-actions.rb` over every `.yml` and `.yaml`
workflow, including release-only workflows. It uses Ruby's bundled YAML syntax-tree
parser, so quoted keys, flow mappings and folded values are checked too; comments
and script bodies are data. No gem installation is needed. Run its regression cases
with `ruby .github/scripts/test-workflow-actions.rb`.

The NuGet workflow runs on pull requests as well as publication. The pull-request
job builds packages and symbols, writes `SHA256SUMS`, and runs the same package
verifier as publication. Only the publication job can request an identity token,
log in to NuGet or push. That job still downloads and verifies the published release's
exact assets; it never rebuilds them. The verifier and its negative cases can be run
with Python's standard library:

```sh
python3 .github/scripts/verify-packages.py packages
python3 -m unittest discover -s .github/scripts -p 'test_*.py'
```
