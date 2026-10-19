# Checked repository changes

The 0.8.0 `changes` command family records pushes, pull-request openings,
merges and observations. It uses the same runtime through the CLI, daemon API
and SDK. These operations require the operator or a principal with
`repository-write` authority currently in force, an admitted signing device
when the estate requires signatures, and record format 3 for the product's
`changes` declaration and the operation's check and scan evidence. They are not MCP tools.

Add a `changes` declaration to the product record, retaining the product's other
fields. The repository is an estate resource of kind `repository`; its `location.github`
names the GitHub destination. Register its local clone through `sources clone`.

```json
{
  "changes": {
    "provider": "github-pull-requests",
    "repository": "<repository-resource-id>",
    "target": "main",
    "requiredChecks": [
      "tests (linux)",
      "tests (macos)",
      "tests (windows)"
    ]
  }
}
```

Replace the example check names with the exact check names the host reports,
including each required platform's job. The runtime never infers requirements
from whichever checks happen to be present. An empty declaration authorizes no
merge. The operation pins the product and repository record hashes as well as
the commit, so its evidence remains intelligible after a declaration changes.

## Push and open

```sh
vericairn changes push --product <product-id> --branch work/change --head <commit> --by <principal-id>
vericairn changes open --product <product-id> --branch work/change --head <commit> --title "Checked change" --body "Review this change." --by <principal-id>
```

The push intent is recorded first. The git adapter reads the remote branch's
actual head without fetching. For an existing branch, that head must be a known
ancestor of the requested head. For a new branch, the scan starts at the merge
base with the remote target branch. Missing local objects refuse the operation;
update the clone separately before retrying. Shallow clones are refused because
they cannot establish the complete commit range.

Gitleaks must be installed by the user. It scans exactly `base..head` with
redaction, default rules, an empty ignore file, and inline allow comments disabled.
Merge diffs are included; external diff commands and text conversion are disabled.
Text diffs are requested even for paths marked as binary by repository attributes.
The range's readability is checked independently before
and after the scan. The report contains only a finding count. The recorded scan
contains the tool, version, range, verdict and count, never matched text, findings
or process output. Findings, an unavailable scanner, a timeout, an unreadable
range or an incomplete report refuse the push.

Before pushing, the adapter rechecks the local head, remote head and range. It
pushes only the pinned commit to the named branch, with an explicit remote-head
lease, without following tags or pushing submodules. It then records an
observation of the remote branch. A successful process exit alone does not
establish that the intended head is there. An opening requires a recorded
successful scanned push of the same repository, branch and head; the GitHub
provider also checks the pushed head before opening.

Git and gh use the user's own configuration and login. The runtime has no token
parameter, does not request a token, and does not retain authentication output.

## Merge and observe

```sh
vericairn changes merge --product <product-id> --pull-request <number> --head <commit> --by <principal-id>
vericairn changes observe --operation <operation-id> --by <principal-id>
```

A merge first records its intent and a separate observation of the pull request.
Every required check must be present and every reported check must have completed
successfully at the expected head. Missing, pending, failed, skipped, unknown or
stale-head checks refuse the merge, as do incomplete pagination, a changed head,
a different target, a closed pull request and an absent requirement declaration.
Fork pull requests are not supported by this provider.

The successful check inventory is recorded before the merge request. GitHub's
request uses the synchronous merge API through `gh`, with its atomic `sha`
condition (the head pin corresponding to `--match-head-commit`). It cannot enable
auto-merge or enqueue a later merge; a repository requiring a queue refuses it.
A response that does not identify a confirmed merge commit remains uncertain.
After a confirmed merge, another operation observes
the target branch's push runs and jobs at that exact merge commit. It never
substitutes the target branch's newest run. A known failure records an ask to
the operator. Pending or incomplete target checks remain pending or uncertain;
`changes observe` can record their later result without repeating the merge.

Each command observes once. Inspect its operation id and call `changes observe`
again when checks are still running. A merge command can report a confirmed
merge with `ok: false` while its target checks remain incomplete or failed.
The response's `status` describes the effect; `checkStatus` describes the target
checks. The CLI's exit code is zero only when `ok` is true. `--json` retains both
states and the record references, including refusals.

## Interruption and retry

An intent without a receipt, or an interrupted request that may have reached the
host, holds that operation's identity. A new head cannot bypass an unresolved
effect for the same branch or pull request. Observe the original operation
before retrying. Absence and failed observations never prove that an earlier
request had no effect. There is no automatic cancellation of an uncertain effect.
An operator who has established that the original intent had no effect can record
that reconciliation with `changes observe --operation <id> --by <operator>
--cancel <explanation>`. This cancels only the recorded intent; it sends no remote
cancellation. A confirmed successful effect cannot be canceled. A later retry
still needs `--reconciliation` and fresh checks or a fresh scan.

A known failure before any effect can be retried with `--reconciliation`,
describing how the earlier attempt was settled. The retry records a new durable
claim and reruns the scan or check observation. The old evidence remains in the
revision chain. A failed prerequisite observation has an uncertain observation
record and a refused effect; it does not claim that a mutation was sent.

Route writes through one runtime. As with release submission, the service gate
coordinates one service instance, not independently running writers or repository
synchronization. Observations and provider processes run outside that gate.

## Contracts

`IPullRequestProvider` implements `vericairn.provider.pull-request/1` for opening,
observing, merging with a head pin and observing the target run. The GitHub
implementation uses bounded gh processes; `MemoryPullRequestProvider` is an
independent in-memory implementation for controlled use. `IPushAdapter` implements
`vericairn.adapter.push/1`; `ISecretScanner` implements `vericairn.scanner.secrets/1`.
The host registers implementations explicitly. An unavailable declared provider
is refused; the runtime never chooses a replacement destination.

The four API routes are `POST /v1/changes/push`, `/open`, `/merge` and `/observe`.
Their SDK methods are `ChangesPush`, `ChangesOpen`, `ChangesMerge` and
`ChangesObserve`, with matching request types and a shared `ChangeResponse`.
Each has a cancellable `Async` counterpart. An interrupted observation does not
undo an effect; reconcile the original operation before considering a retry.
The generated API description and CLI reference include this additive surface.
