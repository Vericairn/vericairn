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
- for Vault service tokens, a value without a digit;
- from 0.9.4, for every rule, a line of a history checkpoint's inventory in the
  estate's objects, as the next paragraph describes.

From 0.9.4 (FIX-150, decision 01M7N48B8CGR54YTJZMAMJTYGH), the scan passes a
history checkpoint's inventory whatever its record ids contain. An inventory
lists each record by its path under `records/` beside its hash, and a random
record id can spell a key word such as `KEY` just before the hash, which
gitleaks' generic key rule reads as a key's name and value; before 0.9.4 about
one checkpoint push in five was refused for it. The allowance passes a line
only when it has exactly an inventory line's shape, with nothing before or after
it: one of the record types the estate format defines, which `vericairn records
describe` lists, such as `decision` or `work-order`; a month such as `2026-11`; a
record id of 26 digits and upper-case letters, the first 0 to 7, and `.json`;
one space; then `sha256:` and 64 lower-case hexadecimal digits. Only the hash
position passes a value: the type, month, id and `.json` positions take only
those fixed names, digits and a record id's characters, in which no rule finds a
credential, so a credential in any of them breaks the line's shape and is found,
and a line naming a record type this release does not define is scanned as
before. The allowance holds only in a file named by 64 lower-case hexadecimal
digits directly in the `objects/` folder at the repository's root. The scanner
cannot tell from the commits which objects a checkpoint names, so it applies the
allowance only when every such file that the pushed commits add or change, and
that holds a line of that shape, holds nothing but such lines. If one mixes inventory lines with any other line, or
the scanner cannot read those files, the whole range is scanned without the
allowance, as before 0.9.4. Every other line and every other path is scanned
exactly as before, and no repository exception, allow comment or ignore file is
honored. A value in an inventory line's hash position passes, whatever it is:
the allowance trusts the shape, not the checkpoint's signature, which the
estate's checks verify. The recorded scan's version names the configuration the
scan applied, the configuration with this allowance or the same configuration
without it, each by its own SHA-256.

The recorded scan's version names gitleaks' version and the configuration's SHA-256,
for example `8.30.1; rules sha256:…`, so each scan identifies the rules it applied.
Merge diffs are included; external diff commands and text conversion are disabled.
Text diffs are requested even for paths marked as binary by repository attributes.
The range's readability is checked independently before
and after the scan. From 0.9.4 (FIX-150), the scanner also reads, before
gitleaks runs, every commit, tree and blob the push carries, checking each
against its id, and the range's patches exactly as gitleaks reads them, the
parents' sides included. If either read fails or stops early, at a missing,
corrupt or truncated object, or gitleaks reports that its own read of the history
failed, which it does without changing its exit status, the scan is incomplete
and the push refuses. Before 0.9.4 such an object stopped gitleaks' read with no
finding, and the scan passed even when an older commit of the range held a
secret. Every git process of the scan ignores the clone's color, root commit and
signature display settings and git's trace variables, each of which changes
what gitleaks reads. The report contains only a finding count. The recorded scan
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

From 0.9.4 (CODE-81), every git process of a checked push, the adapter's reads
and pushes and the secret scan's reads, gitleaks' own git included, reads
grafts from a file that never exists, so a clone's `info/grafts` cannot
misreport a commit's parents. The scan's reads also ignore replace objects, the
commit-graph, reachability bitmaps and the multi-pack-index, as the adapter's
reads already did. Before 0.9.4 a graft in the clone could make the
fast-forward check accept a remote head that was not an ancestor, so the leased
push overwrote the remote branch, and could hide a commit of the range from the
scan, the message check and the disclosure hold while the push, which reads the
real objects, delivered it. A clone used with `changes push` before 0.9.4 can be
checked for an `info/grafts` file, and each pushed branch compared with the head
Vericairn recorded.

### Disclosure holds (0.9.0)

A work order can carry `"disclosure": "held"`, for example a security fix whose nature must not be public before its release.
While it is held, a checked push to a repository whose resource records `"visibility": "public"` is refused before anything is
pushed if the push's commit messages or added lines contain the work order's key as a whole word, or a distinctive phrase of
its title, intent or scope. Case, Unicode compatibility forms (full-width letters, ligatures such as `ﬁ`, circled and
mathematical letters), line wrapping and a sentence's closing punctuation do not hide a match. From 0.9.2 the text is compared
after Unicode NFKC normalization and full case folding computed by Vericairn itself from pinned Unicode 16.0.0 tables, the same
on every platform and build. A phrase shorter than 24 characters, or one another current work order also uses, such as a standard closing step,
is not matched on its own. The check applies whoever authored the commits.

From 0.9.4 (FIX-137, decision 01M7PNCMA3V2ZQG2ZK9V7NBEJK), the git adapter reads every message and every added line of every
commit the push carries that its target branch does not already hold, whatever the clone's settings and attributes, or reads
nothing and the push is refused. Each commit's own diff against its parent is read, so text one commit adds and a later one
removes, which the push still publishes in its history, is read. The diffs are read by git's plumbing, which no log or diff
display setting of the clone changes: a root commit, such as one an unrelated history brings in by a merge, is read against
the empty tree whatever `log.showRoot` says, and a commit other than a merge whose tree differs from its parent's, or a root
whose tree is not empty, must show at least one file, judged from the trees, or the read refuses. The patches have a bound of
their own, 64 MiB for them all together, several times a release's documentation set; a push beyond it is refused while a
hold is in force. A merge commit's own lines are read from its dense combined diff: a line added against every one of its
parents is the merge's own text, read even when a later commit removes it, while a line one parent already holds was added by
a commit of that parent, read on its own or already in the repository. Messages are read in UTF-8 whatever the clone's
`i18n.logOutputEncoding`, without notes or mailmap. Every file is read as text: neither a NUL byte, a `-diff` or binary
attribute, committed or the clone's own, nor a `core.bigFileThreshold` below the file's size turns its added lines into
"Binary files differ"; renames and copies are not detected, so a moved file's lines read as added. A diff line is a file
header only before the first hunk of its file, so an added line whose own text begins with `++` is read. An added line
holding NUL characters, as each line of a UTF-16 or UTF-32 file does when read as UTF-8, is read again with them removed, so
held text stored in such a file is matched by its letters; text without NUL characters reads only as itself, so this second
reading adds nothing for an ordinary file. Before 0.9.4 each of those let held text through: text a later commit of the push
removed, the added lines of a file git called binary, an added line beginning with `++`, held text in a UTF-16 or UTF-32
file, and every message under a clone that set its log output encoding to UTF-16.

The hold ends when the release that carries the work order (its placement) is recorded as published; what that release
discloses stays the operator's choice. A push to a private repository is not held. If a hold is in force and the push adapter
cannot read the push's text, the push is refused.

From 0.9.4 (FIX-137), one push may carry the held text before then: the carrying release's documentation set, with the held
fixes it carries, once the operator has approved that release's Security section for the held work order, as the release
procedure merges the set just before the candidate's submission. Every other push that would add the held text stays refused
until the release is published, including one made after that approval and one that carries the set beside other changes.
Before 0.9.4 the hold ended only at publication, so a checked push refused the release's own documentation set when it carried
a held fix's guide text.

- **The set, unmerged.** The push must be a checked push of the product the release belongs to. Its head holds
  `docs/releases/<version>/manifest.json`, a `vericairn.docs/1` manifest of the carrying release's version, and its target
  branch's head holds none, as while the set's pull request is open.
- **Nothing but the set and its held fixes.** Every commit the push carries that its target branch does not already hold is
  either a held fix of that release, whose subject line begins with the key of a work order held and placed in that release and
  a colon, exactly as the estate's commits are titled (`FIX-90: ...`), or changes documentation only. A held fix may change any
  path; a mention of the key elsewhere, in another case or joined to more characters (`fix-90:`, `FIX-90_followup:`) is not a
  held fix. Documentation is a Markdown, JSON or plain-text file under `docs/` outside any hidden directory, other than a
  `global.json`, and the root's `README.md` and `llms.txt`, none of which can bring code into the build; and the root's
  `Directory.Build.props`, which the release flow changes to set the new version, only when the head's file differs from the
  target branch's file in nothing but the value of its one `<VersionPrefix>` line. A merge commit counts only for the paths
  that differ from every one of its parents, so taking in the target adds nothing. A push that carries the manifest beside any
  other change is not the set, whatever its branch; a version change with other build settings rides in a held fix or a
  separate push.
- **The approval.** The operator approves the Security section for each held work order in the forms the release practice
  writes. The ask's subject is the release's carrier work order and its question begins exactly with
  `Approve the Security section of <version>`, the version ending there (what follows cannot continue a version, so a question
  about `9.9.90` or `9.9.9-rc.1` does not ask about `9.9.9`), for example
  `Approve the Security section of 9.9.9 as drafted, covering FIX-90?`. The operator's decision answering it records, among
  its effects, exactly `The Security section of <version> is approved for <key>.` for each held work order it covers, such
  as `The Security section of 9.9.9 is approved for FIX-90.`, and names those work orders among the work orders it affects.
  The decision must be decided and recorded after the held work order's current revision, and no ask about the carrier may
  still wait, open or notified. An answer to any other question about the carrier, such as one that mentions the Security
  section in passing, approves nothing; neither does a decision without that effect for the work order, such as one that
  declines the section, nor one that does not name it, nor one made before the work order was last revised, such as before
  it was placed in the release or marked held: ask the operator again. The forms are read, not the section's bytes, which
  `insert --expected-hash` checks when the set is prepared.
- **The reads.** The push adapter reads the manifest and `Directory.Build.props` at the head and the target head only,
  without the clone's filters, and the carried commits' messages and paths separately, each within its own bound
  (`IPushAdapter.FileText`, `CommitMessages` and `ChangedPaths`); paths are read only while an approval could let a set pass,
  and exactly as git names them. An adapter that reads any of them not at all never lets a set pass, and a read that fails
  refuses the push. A refusal of a push while such an approval stands says that only the push carrying the set, with nothing
  but documentation and the release's held fixes, may carry the held text, and why this push is not it: no set or a merged
  one, an unreadable read, a commit by its id that is not a held fix and changes more than documentation, or a build file
  that changes more than the version.

### Commit messages (0.9.4)

From 0.9.4 (CODE-81), a checked push reads the message of every commit it carries that its target branch does not already
hold, merge commits and trailers included, before anything is pushed, and matches it against the estate's [blocked
terms](policies.md#blocked-terms-090), the same list and the same whole-term matching that records and generated documents
meet. A match refuses the push, to any repository, public or not. Where an estate policy sets the level to `warn` ([commit
messages](policies.md#commit-messages-094)), the push goes ahead, and its response and recorded evidence say which commits
carried a term. A term only in a file's content is not this check's concern; the secret scan and the disclosure hold read the
pushed lines. A commit the target branch already holds, as the remote reported its head when the push was prepared, is in the
repository already: a branch that takes in a newer target, with its merge commits that carry no required trailer, is checked
only for its own commits.

An estate policy may also require an exact trailer line, such as a `Signed-off-by:` line, on every pushed commit. A commit
whose closing paragraph lacks that exact line is refused, as is one whose closing paragraph carries the line's token with other
text, an altered form of it; trailing whitespace and carriage returns do not count. The trailer is required whatever the level.
The exact required line is exempt from the blocked terms, since the estate requires its text.

The refusal names the commit and the estate policy whose list or trailer it concerns, by the policy's identity, and never the
matched text or the trailer, so the operation that records the refusal never repeats a term. The push's operation records, as
evidence, how many of its commits were checked and, at the `warn` level, the commits that carried a term and the policies
that set the level. The push adapter reads the messages (`IPushAdapter.CommitMessages`); while the estate declares a blocked
term or a required trailer, a push whose messages cannot be read, as with an adapter that does not implement it, is refused.
With neither declared, no message is read.

`changes merge` matches the message of the merge commit it would create against the same terms at the same level, before
anything is merged: `Merge pull request #<number> from <owner>/<branch>`, then the pull request's title as the host reports
it. While the estate declares a blocked term, the merge creates its merge commit with exactly that message, so the
repository's default message, which may add the pull request's description, never replaces the message checked. A merge
through a required merge queue cannot set the message, and the queue's merge commit may carry the pull request's description
where the repository's setting adds it: the check then reads that message with the description, as the host reports both when
the merge observes the pull request, and a pull request whose title or description cannot be observed is refused. A title or
description edited after that observation, before the queue builds its merge commit, is not checked. The required trailer
applies to the commits a push carries, not to the merge commit the host creates. `operator record` pushes and merges through
the same checks, and its commit ends with the estate's required trailer lines, so its push meets the requirement.

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
branch requires a merge queue and the pull request's place in it, as target
facts: `mergeQueue`, `queue` (`absent`, `queued`, `unmergeable`, `failed` or
`removed`), `queueHead`, the pull request's own head read with that place, and,
when the host gives them, `queueGroup`, the commit of the merge group the queue
built, `queueReason` and `queueRemovedAt` for its latest removal. The same
authority, the same required checks at the exact head and the same scanned push
admit the change. A requirement that cannot be observed completely, or a pull
request already in the queue, refuses the merge before anything is requested.

The merge then records the queue as its route, with the target fact
`mergeQueue` set to `required`, and its dispatch is the enqueue request at the
pinned head: GitHub's `enqueuePullRequest` with `expectedHeadOid`, through `gh`,
so a head that moved is never enqueued and nothing merges at once. An
observation confirms the change's place: the pull request has an entry in the
queue, and its own head, read with the entry and again from the pull request,
is the pinned head. The entry's group commit is recorded beside it and never
taken for the head, and the host may name none before it builds a group. The
command then answers with `status` `dispatched`, `checkStatus` `queued` and
`ok` false. A lost or unconfirmed receipt leaves the merge `uncertain`.

`changes observe --operation <merge-id>` records the queue's outcome against the
same merge, each time in a new observation that reads the change's place in the
queue first and its pull request after it, so a merge between the two reads
shows as merged, never as an open change out of the queue:

- merged: the merge is `observed-success` at its merge commit, and its target
  run is observed as below;
- still queued at the pinned head: the merge stays `dispatched`, with
  `checkStatus` `queued`; observe it again later;
- not merged, on the host's positive evidence about this attempt: the entry
  still listed as unable to merge at the pinned head, a removal from the queue
  that the host dates after this attempt's dispatch (`failed` for a failure such
  as of checks or a conflict, `removed` otherwise), the pull request closed, or
  its head moved from the pinned head. The merge is a `known-failure` whose
  summary says which, with the host's reason token. A retry needs
  `--reconciliation`, fresh checks and a scanned push, and enqueues the same
  pinned head again; if the pull request reads merged at the pinned head by
  then, the retry records that merge instead, sending nothing;
- anything else, such as a change out of the queue with no removal dated after
  its dispatch while its pull request does not yet read merged, or with only an
  earlier removal: the merge is `uncertain`. It is never retried until a later
  observation settles it or the operator's `--cancel` reconciles it, as for any
  uncertain effect.

A retry keeps its route. A merge recorded through the queue is refused if the
branch no longer requires one; push a new head to merge it directly.

For a merge the queue made, the target observation reads the queue's runs of
exactly the merge commit (event `merge_group`, on the queue's read-only branch
for the target) as well as the branch's push runs of that commit, and never a
newer commit's run. A queue that merges changes in groups runs only a group's
head: a merge commit inside a group has no run of its own. Its target
observation then reads the runs of the group that landed it, by that group's
exact head commit, recorded as `queueGroup`: GitHub's provider finds the merge
queue's push to the branch whose range holds the merge commit, among the latest
twenty, and takes the commit after it. When that group cannot be established,
the commit's own empty inventory stands and the target checks stay pending;
observe again, or settle the target run on the host. A check the push run
skipped counts when a check of the same name passed at the same commit, as when
Vericairn's own push run skips a commit its queue run passed. Every other check
must pass, and every required check must be present.

`operator record` waits for the queue within `--wait-seconds` as it waits for
checks, observing a change whose outcome is still unconfirmed again in the same
way. Its unfinished action shows the stage `merge-queued` once the change's
place in the queue is confirmed, and `merge-uncertain` before then. When a
queued merge stops unmerged or unconfirmed, its next step names `changes
observe --operation <merge-id>`, and a rerun with `--reconciliation` observes
the queued merge again before any retry.

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
