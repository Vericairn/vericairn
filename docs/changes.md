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
The declaration and the repository's location decide where merges land, so only
the operator or a principal with the `administer` scope declares, changes or removes
the declaration or moves the location.

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

`operator record` carries the operator's signed record through this checked
change flow. It writes and commits the record together with every member required
by the estate's declared generated registers, using the same generators as
`records new` and the estate check. A decision or release therefore includes its
declared register member in the head checked by the pull request. The commit
check requires exactly the record and its members, with their expected bytes;
extra files, missing members and changed members are refused. Resuming with the
same input reuses the stored signed record and members without signing or
committing again. Operation receipts are written after that commit; from 0.9.3
the next operator action in the same clone carries them into its own commit.
See [Operator actions](operator-actions.md) for prerequisites and recovery.

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

Gitleaks 8.25.0 or later must be installed by the user; an older one leaves the
scanner unavailable. It scans exactly `base..head` with redaction, Vericairn's own
configuration, an empty ignore file, and inline allow comments disabled.

The configuration holds gitleaks 8.30.1's default rules, each unchanged, with every
built-in allowlist removed. None of its path exemptions apply, so a secret in a lock
file, a vendored or generated tree, an image or a document is found, and no sample
credential is admitted, such as an AWS key id ending in `EXAMPLE`. Its only
allowances are its own, each for a value that is not the credential its rule finds:

- for every rule, a placeholder, template reference, boolean, run of one repeated
  character, format specifier or file path;
- for generic keys, a value without a digit, gitleaks' list of key, token and secret
  phrases that name no credential (such as `public_key` or `key_id`) with its list of
  common words, and build secret mounts and module imports;
- for basic authorization in curl, placeholders and variable references;
- for Kubernetes secrets, template or empty values, and kind and data in separate
  documents;
- for NuGet configuration passwords, environment variable references;
- for Vault service tokens, a value without a digit.

The recorded scan's version names gitleaks' version and the configuration's SHA-256,
for example `8.30.1; rules sha256:…`, so each scan identifies the rules it applied.
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

Before 0.8.3, a checked push ran git with the clone's own configuration and
the user's system and global configuration, including files they include; a
clone used with `changes push` under 0.8.0 through 0.8.2 can be checked for
`url.` rewrites, hooks and proxy settings at each of those levels, and each
pushed branch compared with the head Vericairn recorded. From 0.8.3, a checked
push's git processes load no system or global configuration, and its remote
access runs from a private repository that reads the clone's objects but none
of the clone's configuration, so the clone's address rewrites, transport
settings and hooks never apply. Only the remote's own protocol is allowed:
https for GitHub, or a repository on this machine. From the user's system and
global configuration, git takes only the credential helpers and the TLS trust
settings (`http.sslBackend`, `http.sslCAInfo`, `http.sslCAPath` and the
Schannel options); a proxy comes from the environment, such as `HTTPS_PROXY`.
gh uses the user's own configuration and login. The runtime has no token
parameter, does not request a token, and does not retain authentication
output.

### Disclosure holds (0.9.0)

A work order can carry `"disclosure": "held"`, for example a security fix whose nature must not be public before its release.
While it is held, a checked push to a repository whose resource records `"visibility": "public"` is refused before anything is
pushed if the push's commit messages or added lines contain the work order's key as a whole word, or a distinctive phrase of
its title, intent or scope. Case, Unicode compatibility forms (full-width letters, ligatures such as `ﬁ`, circled and
mathematical letters), line wrapping and a sentence's closing punctuation do not hide a match. From 0.9.2 the text is compared
after Unicode NFKC normalization and full case folding computed by Vericairn itself from pinned Unicode 16.0.0 tables, the same
on every platform and build. A phrase shorter than 24 characters, or one another current work order also uses, such as a standard closing step,
is not matched on its own. The check applies whoever authored the commits.

The hold ends when the release that carries the work order (its placement) is recorded as published with its documentation
set; what that release discloses stays the operator's choice. A push to a private repository is not held. If a hold is in
force and the push adapter cannot read the push's text, the push is refused.

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
The merge also needs a successful scanned push of that exact head to the pull
request's branch, so a pull request opened elsewhere is pushed with `changes push`
first.
Fork pull requests are not supported by this provider.

The successful check inventory is recorded before the merge request. GitHub's
request uses the synchronous merge API through `gh`, with its atomic `sha`
condition (the head pin corresponding to `--match-head-commit`). It cannot enable
auto-merge. From 0.9.3 a target branch that requires a merge queue is entered
through its queue instead, as [Required merge queues](#required-merge-queues-093)
describes; before 0.9.3 such a repository refused the merge.
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
checks, or is `queued` while a change waits in a merge queue. The CLI's exit code
is zero only when `ok` is true. `--json` retains both states and the record
references, including refusals.

### Required merge queues (0.9.3)

From 0.9.3, `changes merge` merges into a target branch that requires a merge
queue by enqueueing the change, where it refused the repository before. The
observation of the pull request before the merge also records whether its target
branch requires a merge queue and the pull request's place in it, as the target
facts `mergeQueue`, `queue`, `queueHead` and, when the host gives one,
`queueReason`. The same authority, the same required checks at the exact head and
the same scanned push admit the change. A requirement that cannot be observed
completely, or a pull request already in the queue, refuses the merge before
anything is requested.

The merge then records the queue as its route, with the target fact
`mergeQueue` set to `required`, and its dispatch is the enqueue request at the
pinned head: GitHub's `enqueuePullRequest` with `expectedHeadOid`, through `gh`,
so a head that moved is never enqueued and nothing merges at once. An observation confirms the pull
request in the queue at that head, and the command answers with `status`
`dispatched`, `checkStatus` `queued` and `ok` false. A lost or unconfirmed
receipt leaves the merge `uncertain`.

`changes observe --operation <merge-id>` records the queue's outcome against the
same merge, each time in a new observation of the pull request and its place:

- merged: the merge is `observed-success` at its merge commit, the commit the
  queue's group tested and moved the branch to, and its target run is observed
  as below;
- still queued at the pinned head: the merge stays `dispatched`, with
  `checkStatus` `queued`; observe it again later;
- failed, when the queue found the change cannot merge, as on failed checks or a
  conflict, or removed, when it left the queue unmerged otherwise, once its place
  there was confirmed: the merge is a `known-failure` whose summary says which,
  with the host's reason token; a retry needs `--reconciliation`, fresh checks
  and a scanned push, and enqueues the same pinned head again;
- anything else, such as a change out of the queue whose place there was never
  confirmed: the merge stays `uncertain`. It is never retried until a later
  observation settles it or the operator's `--cancel` reconciles it, as for any
  uncertain effect.

A retry keeps its route. A merge recorded through the queue is refused if the
branch no longer requires one; push a new head to merge it directly.

For a merge the queue made, the target observation reads the queue's runs of
exactly the merge commit (event `merge_group`, on the queue's read-only branch
for the target) as well as the branch's push runs of that commit, and never a
newer commit's run. A check the push run skipped counts when a check of the same
name passed at the same commit, as when Vericairn's own push run skips a commit
its queue run passed. Every other check must pass, and every required check must
be present. `operator record` waits for the queue within `--wait-seconds` as it
waits for checks, and its unfinished action shows the stage `merge-queued` while
the change waits.

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
observing, merging with a head pin and observing the target run. From 0.9.3 it
adds `ObserveQueue`, `Enqueue` and `ObserveQueuedTarget` for required merge
queues, with default implementations that report no queue required, refuse an
enqueue before any request and read the push runs alone, so a provider written
before them keeps working. The GitHub
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
