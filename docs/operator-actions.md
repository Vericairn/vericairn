# Operator actions

Vericairn 0.8.0 adds `vericairn operator list`. It shows the complete queue of what the records say
waits on the estate's operators:

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
`operator record` with the same type, author, subject, superseded record and JSON input. Neither
operator command is offered through MCP.

## Package publication, unlisting and deprecation

The development build after 0.8.0 adds `packages observe`, `packages unlist` and `packages deprecate`.
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
version of the product; a candidate can be observed while publication is pending. After every
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

Rerunning an identical intent command returns the original intent and latest operation revision.
It does not create another intent or query the registry. Changing the intended metadata under that
identity is refused. `observe --operation` keeps the original package and release pins even if the
product declaration changes later. A complete read matching the intent records `observed-success`.
A missing, still-listed or differently deprecated version leaves the outcome `uncertain`, with its
actual state recorded. Allow for registry propagation, then observe again under the same identity.
A timeout, malformed response, size limit or inconsistent indexes establish no new state and leave
the existing intent untouched. No registry act or dispatch time is inferred from a command exit.

These observations do not accept a release, verify package archive bytes or replace the release
publication record. The workflow does not call the estate automatically: the post-publication
observation is an operator step. See the [CLI reference](cli.md#vericairn-packages) for every option,
JSON output and API/SDK counterparts, and [limitations](limitations-and-platforms.md#package-registry-observations-development-build) for the read bounds.

## Record and merge in one command

Fill the required body fields, inspect the pinned evidence, and run the item's **Record and merge**
command on your own admitted device. For example:

```sh
vericairn operator record acceptance --by <operator-id> --subject release:<candidate-id>:<candidate-hash> --body -
```

The body comes from standard input; `--body acceptance.json` also accepts a file. The command
uses the existing record authoring and device signing path, including the device's presence prompt
where required. It requires a signature even when the estate otherwise permits unsigned records.
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

The action's commit contains only its signed record and required generated register members.
Operation revisions, observations and any target-failure ask stay in the estate as separate,
uncommitted records. A resumed action tolerates
only its own untracked receipts; it never stages them or unrelated work. Preserve and commit those
receipts separately before starting a new action. No branch is automatically deleted or reset.

Git and gh use the user's own login and configuration. There is no token parameter or credential
retrieval. If the client disconnects or cancels, the runtime lets the current recorded operation
settle and stops before the next effect. Wait for that operation to settle, then resume the same
input to inspect its recorded outcome. The SDK's `OperatorRecordAsync` accepts a cancellation token.

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

Record errors retain the unresolved items and findings but remove authoring commands. The new CLI
command returns 0 for a valid read, 1 when the record snapshot has errors, and 2 when it cannot read
the estate. The existing `ask`, `progress`, `status` and record-writing commands keep their contracts.
