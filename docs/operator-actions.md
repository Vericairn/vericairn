# Operator actions

Vericairn 0.8.0 adds `vericairn operator list`. It shows the complete queue of what the records say
waits on the estate's operators:

Durable action requests join this queue in 0.8.4. They show their recommendation
first, with three delivered at once and every remaining identity queued in order.
Use `operator done` or `operator dismiss` for one signed outcome. See
[Operator requests](operator-requests.md) for creation, revision, carry-over and
agent citations.

```sh
vericairn operator list
vericairn operator list --json
```

Run it in the estate you intend to inspect, or select that estate with the existing `--estate` option.
Each item gives its reason, what it waits on, exact source hashes, an inspection command and the
command that records the operator's answer or action. All open and notified asks addressed to any
operator appear, including those behind the three asks in a notification delivery. Reading the queue
never answers an ask.

| Item | What resolves it |
|---|---|
| Open or notified ask to an operator | That operator's decision naming the ask in `answers` |
| Unaccepted release candidate | An operator's acceptance of that exact candidate hash |
| Submitted milestone or changed revision of an accepted milestone | An operator's acceptance of that exact work-order hash |
| Latest unaccepted result toward an unfinished milestone | An operator's acceptance of that exact result hash |
| Activated, unfinished goal whose acceptance is reserved to operators | An eligible operator's acceptance relying on its current evaluation; activation, current predicates and independence requirements remain in force |
| Accepted candidate whose publication has not been recorded | A publication record preserving the accepted contents, after the operator's publication has been observed |
| Open decision obligation owned by an operator | A discharge naming its key and evidence that meets its recorded requirement |
| Unfinished reserved work assigned to an operator | That contributor's attempt and result, within the assignment's brief and holds |

Answered and withdrawn asks, accepted current revisions, published or withdrawn releases, ended
goals, discharged obligations and completed reserved attempts leave their corresponding queue items.
A changed subject needs a new acceptance; accepting an older hash does not accept the new one.
Assignments left without their routing record do not claim to hold a step. Ordinary work and asks to
other principals do not enter this queue. A goal delegated to a non-operator with authority to accept
it is not an acceptance reserved to operators.

The list reads records. It does not discover unrecorded work, verify an external publication or
inspect whether this machine holds a device key. An accepted candidate still recorded as a candidate
says only that publication has not been recorded. Reconcile any uncertain external effect before
retrying it.

## Using a command

Every authoring command uses the existing `records new` operation with JSON on standard input.
Its `--by` identifies the operator who must act on their own device. Where several operators can
accept a subject, the item offers a command for each eligible principal. Goal evaluators and step
authors are excluded by the existing independence rules. A goal that is not ready retains its holds
and inspection command; it offers no acceptance command until the prerequisites hold.

The JSON body beside a command contains the known facts, such as `answers`, the exact acceptance
`basis` and whether `self` is true. Null fields require the operator's own words, time or evidence.
For decisions, supply the actual decision's `stake` and concrete `effects` as well. These bodies are
deliberately incomplete: they cannot be submitted unchanged as an answer or acceptance. A supplied
body still passes all existing authoring, authority and signing checks.

Inspect before acting. The commands record judgments, starts, results, discharges or observed
publication; they do not carry out the underlying reserved work. They operate in the selected estate.
The queue itself signs nothing and holds no key. Each authoring command also offers
`operator record` with the same type, author, subject, superseded record and JSON input, with one
exception from 0.9.2: in an estate that has adopted history checkpoints, a device's revocation, a
key rotation and a grant revision that narrows the grant are written with their closing in the same
batch, which `operator record` cannot carry, so it refuses them before anything is signed and names
`devices revoke`, `devices rotate` or `records new authority --supersedes` as the next step. Neither
operator command is offered through MCP.

## Package publication, unlisting and deprecation

Vericairn 0.8.2 adds `packages observe`, `packages unlist` and `packages deprecate`.
The NuGet provider reads nuget.org's public V3 API without an account or API key. Unlisting and
deprecation remain the package account holder's acts in the nuget.org website. Vericairn records
their intent first and their observed outcome afterwards; it never takes a NuGet key or performs
those registry changes.

Declare the package inventory in the current product record at record format 3. For this product,
the `packages` member is:

```json
{
  "provider": "nuget-org",
  "ids": [
    "dotnet-vc", "Vericairn", "Vericairn.Daemon", "Vericairn.Mcp",
    "Vericairn.Protocol", "Vericairn.Records", "Vericairn.Runtime", "Vericairn.Sdk"
  ]
}
```

The declaration accepts one to sixteen distinct package ids. Use an already recorded release
version of the product by default; a candidate can be observed while publication is pending. After every
package publication, including the Trusted Publishing workflow, record the registry state:

```sh
vericairn packages observe --product <product-id> --version <version> --by <principal-id>
```

This writes one observation per declared package version, pinning the release and product
declaration. Publication comes from agreement between the V3 registration and package-content
version indexes. Listing comes from registration's explicit `listed` flag, and deprecation from
its metadata. Every response retains its exact registry URL, body SHA-256, HTTP status, byte
count and content encoding. Hashes cover bytes before gzip decoding. An absent version is
recorded as `not-found`, with listing and deprecation unknown.
Search results, workflow completion and upload receipts are not publication observations.

From 0.8.4, a product holding package names before its first release can add an optional `reserved`
array to the same declaration:

```json
{
  "provider": "nuget-org",
  "ids": ["Example.Widget"],
  "reserved": ["0.0.1-reserved"]
}
```

Declare one to eight distinct valid NuGet versions. Versions are compared without case, and none
may equal a recorded release version of this product, including across product revisions. Keep a
placeholder as a reserved declaration; it needs no release record. Select it explicitly:

```sh
vericairn packages observe --product <product-id> --version 0.0.1-reserved --reserved --by <principal-id>
vericairn packages unlist --product <product-id> --version 0.0.1-reserved --reserved --package Example.Widget --by <operator-id>
```

The observation and account-holder intent pin the exact product id and hash and the reserved
version, with no release pins. Revising the product declaration later leaves those records current;
the declaration is only their anchor. Response evidence, intent-before-effect ordering and confirmation
checks are the same as for a release's packages. Without `--reserved`, commands still require a
recorded release; with it, they require the current product declaration to reserve that version.
A version satisfying neither selection is refused. No package command creates a release.

Before unlisting one version, record the intent:

```sh
vericairn packages unlist --product <product-id> --version <version> --package <package-id> --by <operator-id>
```

The command prints the version's nuget.org management URL and the account-holder step to remove
its listing, followed by an observation command containing the original intent id. Sign in to
nuget.org as that package's owner and perform that step for the named version. Then run the printed
command:

```sh
vericairn packages observe --operation <original-intent-id> --by <operator-id>
```

For deprecation, record the exact reasons and optional message or replacement before acting:

```sh
vericairn packages deprecate --product <product-id> --version <version> --package <package-id> --by <operator-id> --reason Legacy --message "Use the replacement package." --alternate-package Example.Replacement --alternate-range "[2.0.0,)"
```

Follow the printed deprecation URL, select only the named version, enter the recorded metadata,
save, then observe its original operation. Reasons are NuGet's `Legacy`, `CriticalBugs` and `Other`.
Omitted message or alternate options mean that no corresponding value is intended; confirmation
checks the reasons, message, alternate package and range, not merely that some deprecation exists.
The estate operator records these intents and outcomes; package ownership is established by nuget.org.

A reserved version uses `--reserved` for deprecation too. If the account holder chooses, the
ordinary message and alternate options can point to the first real release:

```sh
vericairn packages deprecate --product <product-id> --version 0.0.1-reserved --reserved --package Example.Widget --by <operator-id> --reason Legacy --message "Use the first release, 1.0.0." --alternate-package Example.Widget --alternate-range "[1.0.0,)"
```

After either reserved-version intent, follow the printed account-holder step and observe its
original operation. `observe --operation` derives its selection from the intent; do not add
`--reserved`, `--product` or `--version`.

Rerunning an identical intent command returns the original intent and latest operation revision.
It does not create another intent or query the registry. Changing the intended metadata under that
identity is refused. `observe --operation` keeps the original package and release or reservation pins even if the
product declaration changes later. A complete read matching the intent records `observed-success`.
A missing, still-listed or differently deprecated version leaves the outcome `uncertain`, with its
actual state recorded. Allow for registry propagation, then observe again under the same identity.
A timeout, malformed response, size limit or inconsistent indexes establish no new state and leave
the existing intent untouched. No registry act or dispatch time is inferred from a command exit.

These observations do not accept a release, verify package archive bytes or replace the release
publication record. The workflow does not call the estate automatically: the post-publication
observation is an operator step. See the [CLI reference](cli.md#vericairn-packages) for every option,
JSON output and API/SDK counterparts, and [limitations](limitations-and-platforms.md#package-registry-observations-082) for the read bounds.

## Close finished steps in one action

0.8.4 added `vericairn operator close`. The per-release closing scripts
are retired. Run this command on your own admitted operator device:

```sh
vericairn operator close
vericairn operator close --select STEP-1 --select STEP-3 --by <operator-id>
vericairn operator close --all --by <operator-id>
```

Without a selection, the command is read-only. It lists every current work order whose latest
result is `completed`, has at least one current `pass` review of that exact result, and has no
acceptance of that result. Each entry shows its key, work-order id and hash, exit question, result
id, hash, outcome and summary, and every passing review's id, hash, independence and summary.
Both ordinary work and milestones can appear. A partial or blocked latest result, an old review
of a superseded result, or a superseded passing review cannot qualify a step.

`--select` is repeatable. `--all` selects the complete eligible list across the estate; inspect it
before using it when several releases have finished work. The selected evidence is displayed
before signing. Each acceptance names the exact result as its subject and every displayed passing
review as its basis, states that the operator closed it with `operator close`, and records `self`
according to who authored the result. These acceptances judge results; release acceptance remains
a separate operator judgment.

To preserve a listing while inspecting it, save its JSON and supply it with the selection:

```sh
vericairn operator close --json > closing.json
vericairn operator close --from closing.json --select STEP-1 --by <operator-id>
vericairn operator close --from closing.json --all --by <operator-id>
```

`--from -` reads a saved listing from standard input. Without `--from`, a selection reads and
displays fresh evidence in that invocation. The runtime compares the supplied work-order, result
and complete passing-review pins with the current records. It refuses the entire selection if
any pin changed, a newer result arrived, a passing review is missing or replaced, an acceptance
already arrived, or the actor is not an operator. `--all` also requires the complete eligible
membership to remain unchanged. A partial selection is unaffected by changes to unselected steps.
Refresh the listing and inspect the changed evidence before selecting again.

The command uses the same admitted-device signing path as `operator record`, requiring a signature
even in an estate that permits unsigned records. The installed custody interface signs one record
at a time, so a device that requires presence checks it once per acceptance. From 0.9.3 every
selected acceptance is announced before the first prompt, and each prompt says which signature of
how many it is and what it signs, as the [operator's guide](operator-guide.md#presence-prompts-announced-and-counted-093)
describes; a declined prompt stops the selection, and the last line says how many were signed. All selected records
are signed and validated before the batch starts storing them; an evidence or authority refusal
writes no acceptance. An interrupted storage operation retains its receipts and journal for
reconciliation, using the existing batch recovery mechanism. Inspect those receipts before retrying.
This command records the closings locally; it does not carry them through repository operations.

The API exposes `GET /v1/operator/close` and `POST /v1/operator/close`. SDK clients use
`OperatorCloseCandidates()` and `OperatorClose(OperatorCloseRequest)`, or their `Async`
counterparts. The read returns `items`, `ok` and `problem`; each item provides `ToSelection()`
to copy its exact evidence pins. The write request contains `by`, `selection` and optional
`all` (default `false`). A selection contains `key`, `workOrder` and `result` pins (`id`, `hash`),
and the complete array of passing-review pins in `reviews`. The response contains `written`
acceptance receipts, `ok` and `problem`, with HTTP 200 including refusals. Authenticated requests
can act only for their own principal. Neither route is exposed through MCP.

`--json` emits the typed read or write response; during a write the evidence appears on standard
error before signing so standard output remains one JSON document. Exit 0 means the read is valid
or the complete selection was stored, 1 means a record or selection refusal, and 2 means invalid
arguments or a client failure. An interrupted client request retains the existing uncertainty
rules: inspect current acceptances before retrying.

## Record and merge in one command

Fill the required body fields, inspect the pinned evidence, and run the item's **Record and merge**
command on your own admitted device. For example:

```sh
vericairn operator record acceptance --by <operator-id> --subject release:<candidate-id>:<candidate-hash> --body -
```

The body comes from standard input; `--body acceptance.json` also accepts a file. The command
uses the existing record authoring and device signing path, including the device's presence prompt
where required. Every operation it records is signed by the same device, so a device that needs
presence asks for each: from 0.9.3 the command announces them before the first prompt, 22 for a new
action, and an observation it repeats while checks run before that observation asks, as the
[operator's guide](operator-guide.md#presence-prompts-announced-and-counted-093) lists. It requires a signature even when the estate otherwise permits unsigned records.
It cannot borrow another principal's authority or device, and it never receives or retains a key.

The estate must use repository-synced storage at record format 3. Its repository must be registered
as the clone of a resource named by a product's `changes` declaration, as described in
[Checked repository changes](changes.md). That declaration must name at least one required check.
The command selects the unique matching product; `--product <product-id>` resolves ambiguity.
It refuses a different repository, an unavailable provider or adapter, incomplete history, a detached
head, an in-progress repository operation, and other working-tree or index changes.

After validating your signed record, it stores that record with every member required by the
estate's declared generated registers, creates `operator/<record-id>` from the pinned head,
and commits them together. Storing the signed bytes before branch creation lets a branch failure
resume without another signature. It uses the same journaled writer and generators as `records new`;
a decision or release includes its declared register member before the pull request runs the estate check. The commit check
requires exactly those paths and their expected bytes, refusing missing, changed or extra files.
Local git hooks are disabled for branch creation and commit, and git
commit signing is disabled; the record retains its own device signature. The runtime invokes MERGE-01's recorded
push (including its scan), opening, observations and merge. Each check must pass at the exact
committed head. The merge rechecks the inventory and uses the provider's atomic head condition.
Finally, recorded observations check the target run at the confirmed merge commit.

`--wait-seconds` bounds each check phase from 0 to 240 seconds, defaulting to 120. Zero observes
once. Missing or pending checks may become ready during that interval. Failed, skipped, unknown,
incomplete or stale-head observations stop the command. A successful merge with pending or failed
target checks remains an incomplete command; a failed target run records MERGE-01's operator ask.

On a stop, any stored record and members, created branch and opened pull request are retained.
The output gives the last operation and a next step. Supply exactly the same JSON body and authoring arguments to
resume, even after restarting the runtime. A local checkpoint pins the original record identity,
hash, branch and base; the runtime verifies the record, generated members and commit and continues
from the recorded operations. It reuses the stored record and member bytes without signing or committing again.
If member publication was interrupted, reopening the estate recovers the prepared outputs through
the original batch journal; it does not create another record. The checkpoint contains no input body,
key or credential and belongs to this clone's runtime storage; resume from that clone.

A known failure needs `--reconciliation <explanation>` after its cause has been resolved. An
uncertain effect must first be observed under its original operation identity; reconciliation text
alone never permits a retry. If the host still cannot establish the outcome, follow the explicit
operator reconciliation procedure in [Checked repository changes](changes.md). Another action
cannot displace an unfinished action's checkpoint. Keep the original body until the action completes.

### Finishing an action after its merge (0.9.2)

From 0.9.2, an action whose merge is recorded is finished from its own records. A pull request often
merges while the target branch's run is still pending, and the host may delete the merged branch.
Once the confirmed merge operation is in the estate, resuming the action observes only the target run
at the merge commit, under the merge's original identity. It does not recreate the branch, sign,
commit, push, open or merge again, and it leaves the clone's branch and files as they are, usually on
main after the operator updated it. A passing target run completes the action and clears the clone's
unfinished mark; a pending, failed or incomplete one keeps it.

```sh
vericairn operator unfinished
vericairn operator unfinished --finish --by <operator-id>
```

The first command reads the clone's checkpoint and records and writes nothing. It shows the action's
record id, type and subject, branch, head, pull request and stage (`prepared`, `recorded`, `committed`,
`pushed`, `opened`, `merge-failed`, `merge-uncertain` or `merged`), the merge commit and latest target
check verdict, and what finishing it needs. For an action that has not merged, that is the original
`operator record` command, to be run with the body of its stored record (`vericairn records show
<record-id>`) and, after a known failure, `--reconciliation`. `--finish` finishes a merged action once
its target checks pass, waiting up to `--wait-seconds` (default 120, at most 240) while they are pending;
a failed target run must first be rerun or corrected on the host. Only the action's own operator
finishes it, on their admitted device.

Starting the next `operator record` finishes a merged action the same way before it starts, and then
proceeds. From 0.9.3 that next action carries the finished action's push and merge trail, including
the target-run observation just recorded, into its own commit, as the next section describes. An
action that has not merged still refuses a new one, and the refusal names its record id, type and
subject.

### The push and merge trail reaches the estate (0.9.3)

An action's commit holds its signed record and required generated register members. Its push and
merge trail is written after that commit: the operation records of its push, opening and merge with
every revision, the observations that point to them and any target-failure ask, all signed by the
action's operator. Before 0.9.3 the trail stayed untracked in the clone that ran the action until
someone committed it separately.

From 0.9.3, once an action is complete, its trail is settled. The next `operator record` in the same
clone carries every settled trail record the working tree still holds untracked into its own
commit, beside its own signed record and members. The commit check requires exactly those paths and
their bytes, and the local checkpoint pins them, so a resumed action commits the same set and never
another. The earlier action's signed record and commit are not changed. Only records the runtime
wrote for the earlier actions it ran in this clone travel this way: any other untracked or changed
file still refuses the action. The new action's own trail is written after its commit in turn, and
the action after it carries that.

Until a commit holds it, a settled trail exists only in the clone that ran the action. `vericairn
status` and `vericairn estate check` report each such action with a warning, code
`operator.trail-uncommitted` at `operator-action:<record id>`, naming the action's type, record id
and subject and how many trail records wait. Running the next operator action clears the warning for
the trail it carries; committing the records yourself, as before, clears it too. A fresh clone holds
no untracked trail and reports none. A resumed action tolerates only its own untracked receipts and
those it carries; it never stages unrelated work. No branch is automatically deleted or reset.

Git and gh use the user's own login and configuration. There is no token parameter or credential
retrieval. If the client disconnects or cancels, the runtime lets the current recorded operation
settle and stops before the next effect. Wait for that operation to settle, then resume the same
input to inspect its recorded outcome. The SDK's `OperatorRecordAsync` accepts a cancellation token.

## A release's reserved steps (0.9.2)

When a release runs on the software pack's release flow
([operator guide](operator-guide.md#the-software-release-and-upgrade-flows-092)), each act that stays the operator's waits in
`operator list` as an operator request, delivered only when its step is next, with the exact command the records give then:

| Step | The request's command | What completes the step |
| --- | --- | --- |
| `accept` (`accept-release`) | `vericairn records new acceptance --by <operator-id> --subject release:<candidate id>:<hash> --body accept-release.json` | Your signed acceptance of that exact candidate |
| `publish` (`publish`) | `gh release edit v<version> -R <owner/name> --draft=false --latest && vericairn release observe <version> --by <operator-id>` | Your signed observation of the publication, the candidate's published revision |
| `delete-draft` (`publish`), after a withdrawal | `gh release delete v<version> -R <owner/name> --yes` | Your own close of the step's request: `vericairn operator done <request> --by <operator-id> --link <url>` |

No command of a later step appears before its step is next: publication's never shows while the acceptance that names the
candidate is missing. Closing a request completes nothing by itself, except for a step whose record of your act is that close,
such as a draft's deletion or an announcement; even then an agent's close citing your words completes nothing, and `goal step`
asks you again. Only records you signed count: an agent's acceptance, publication observation or close is kept and shown as
not counting.

Vericairn's own estate adds three such steps after publication in its first-party extension: the MCP Registry listing (below),
whose request shows `gh run list -R Vericairn/vericairn --workflow mcp-registry.yml --event release --limit 1`, and the
announcement on GitHub Discussions and on your X account, whose requests name the reviewed drafts by hash. Each is done once you
close its request with `operator done --link` and the observed address.

## Presentation and API

Human output follows the existing communication settings, defaulting to the estate's root operator.
`--recipient`, `--assignment` and `--depth` select presentation; they do not filter the queue or change
who can act. Every item, reason, hold and command remains visible at every depth and at progress
frequency `none`. Unavailable or conflicting settings remain explicit. `--json` returns the complete
`vericairn.operator-actions/1` document independently of presentation preferences.

`GET /v1/operator/actions` and `VericairnClient.OperatorActions(recipient, assignment, view)` expose
the same read. The response contains `text`, `json` and `hasErrors`. The JSON document contains
`format`, `estate`, `items`, `hasErrors` and `findings`. Each item has a stable `id`, `kind`, `source`,
`title`, `state`, `why`, `waitsOn`, `references`, `inspect` and `commands`. Ask items also retain
`recommendation` and `consequence`; obligations retain `resolution` and `gates`. Each command has
`by`, `command`, an `arguments` array, a required-input `body` and its `purpose`.
It additionally has `oneStep`, containing the `operator record` command and argument array for
that same body. The existing authoring command and body remain unchanged.

`POST /v1/operator/record` and the synchronous `VericairnClient.OperatorRecord(request)` method
accept `OperatorRecordRequest`: `type`, `by`, `body`, optional `subject`, `supersedes`, `product`,
`waitSeconds` and `reconciliation`. The response carries `identity`, `status`, `message`, `ok`,
`nextStep`, `record`, `branch`, `head`, the last `change` response and any `problem`. The CLI returns
0 only when `ok` is true, 1 for a refused, stopped or waiting action, and 2 for input or connection
failure. `--json` preserves the full response. Authenticated device requests are bound to their own
principal; all authoring, signature, authority and presence checks remain in force.
`OperatorRecordAsync(request, cancellationToken)` is its cancellable counterpart. It has the
15-minute deadline that checks and releases have, because each check phase may wait up to 240
seconds. When a request is aborted, the daemon passes the abort to the action, which stops at its
next recorded step, before any merge. The same command with the same body then resumes it.

From 0.9.2, `GET /v1/operator/unfinished` and `VericairnClient.OperatorUnfinished()` return
`OperatorUnfinishedResponse`, whose `action` is null when the clone has no unfinished action. Otherwise
it carries `identity`, `recordId`, `type`, `subject`, `by`, `record`, `branch`, `head`, `stage`,
`pullRequest`, `merge`, `mergeCommit`, `checkStatus`, `targetObservation`, `ask`, `finishable`, `summary`
and `nextStep`. `POST /v1/operator/unfinished` and `OperatorFinish(OperatorFinishRequest)` accept `by`
and `waitSeconds` and return `OperatorRecordResponse`; `completed` with `ok` means the action finished,
and `none` with `ok` means nothing was unfinished. Both have cancellable counterparts; neither is offered
through MCP.

Record errors retain the unresolved items and findings but remove authoring commands. The new CLI
command returns 0 for a valid read, 1 when the record snapshot has errors, and 2 when it cannot read
the estate. The existing `ask`, `progress`, `status` and record-writing commands keep their contracts.

## MCP Registry publication

`io.github.Vericairn/vericairn` is listed in the
[official MCP Registry](https://registry.modelcontextprotocol.io/) from the accepted release's
`dotnet-vc` package. From 0.9.2, the `mcp-registry` workflow does it when the operator publishes
the release: it verifies the release's `dotnet-vc` package against `SHA256SUMS`, checks the
package's embedded `.mcp/server.json`, waits until nuget.org serves that version, and publishes
the manifest with a pinned `mcp-publisher`, signing in with the repository's GitHub OIDC identity.
No personal token is involved. Publishing the release is the operator's act, and the listing follows it.

For a release published before the workflow existed, or one whose run did not finish, the operator
dispatches the workflow with that release's id; it lists only a published, immutable release:

```sh
gh workflow run mcp-registry.yml -R Vericairn/vericairn -f release_id=<release id>
```

Afterwards, observe the exact name and version in the official registry and confirm that it points
to `dotnet-vc` at the same version with stdio transport and both required inputs. Check
[nuget.org's MCP server filter](https://www.nuget.org/packages?packagetype=McpServer&q=dotnet-vc)
after indexing, and exercise the [dnx command](mcp.md#discovery-and-dnx) against a test estate.
A version the registry already lists is reported, not replaced. The interactive
`mcp-publisher login github` cannot publish under the organization's namespace, because it
cannot read organization roles; use the workflow.

The registry grants the namespace exactly as GitHub spells the organization, `io.github.Vericairn`. Releases
before 0.9.2 carry the server name in lowercase, which the registry refuses, so they are not listed (FIX-101).
