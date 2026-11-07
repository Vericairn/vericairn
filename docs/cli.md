# CLI reference additions

0.8.4 added `prompt list`, `show`, `add`, `revise`, `retire`,
`favor`, `hide`, `disable`, `copy` and `use`. See [Saved prompts](prompts.md) for
category discovery, reversible preferences and recorded uses with new work.

`work brief <key> --agent-context` selects the same measured brief as the MCP
`work_brief` tool and brief resource: mandatory originals remain complete, and
named optional originals are withheld without a byte allocation.
`--max-bytes <positive-integer>` implies `--agent-context` and admits optional
originals only when they fit; required overflow refuses. These options preserve
recipient and assignment presentation settings. Without either option, the
ordinary complete selected-scope brief is unchanged. See [MCP resources](mcp.md#resources-084)
for exact representations and hashes.

Revision 12, November 7, 2026. Add `search`, `search index`, `search replay` and `search measure`, and `--search` on `context prepare`, `work brief`, `worker run` and `goal step`, in the development build.

Revision 11, November 7, 2026. Add `events watch` in the development build.

Revision 10, November 7, 2026. Add the runtime step of `estate upgrade` and the serving release in `runtime status` and `status` in the development build.

Revision 9, November 7, 2026. Add `worker configure --setup-variable` and the setup command's filtered environment in the development build.

Revision 8, November 6, 2026. Add `estate cache rebuild` in the development build.

Revision 7, November 3, 2026. Add the run options of `goal propose` for a run of a flow in the development build.

Revision 6, November 3, 2026. Add `worker run --prepare-only` and workspace setup commands for 0.9.1.

Revision 5, November 2, 2026. Describe 0.8.4's and 0.9.0's command additions as released commands rather than development builds.

Revision 4, October 23, 2026. Point to the operator's guide for 0.8.3's command additions.

Revision 3, October 20, 2026. Add package registry observations and account-holder intents, and document exact-byte document pinning and policy definition hashing in the development build.

The build generates the complete CLI reference from its command tree, including every option. Published references under `docs/releases/` describe their frozen builds. This page documents commands added after 0.8.0 for 0.8.2. The [operator's guide](operator-guide.md) describes 0.8.3's additions: `work new`, `release withdraw`, `work move-plan`, `release submit --check`, `estate init --release` and `estate upgrade --from`.

## `vericairn guides`

From 0.9.0, `vericairn guides` prepares community build guides from explicit local
Markdown sources and a reviewed source index. It creates private previews and local
catalog proposals.
It does not open an estate, create records or contact a publication destination.

```sh
vericairn guides generate --catalog inputs/catalog --contracts inputs/contracts --index inputs/index.json --out artifacts/guide-preview
vericairn guides check --catalog inputs/catalog --contracts inputs/contracts --index inputs/index.json --out artifacts/guide-preview
vericairn guides compare --before artifacts/earlier/source-manifest.json --after artifacts/guide-preview/source-manifest.json
```

`generate` and `check` require `--catalog`, `--contracts`, `--index` and `--out`.
The output directory must be separate from the input trees and index. Repeat
`--select <key-or-alias>` to select planned entries; omission selects every planned
row and explicitly planned remainder. All owning rows still appear in the private
coverage manifest, including references, available scope and withheld entries.

`generate` writes into a new or empty directory. An existing preview is checked
against regeneration and is never overwritten. `check` compares every expected
file and its exact bytes without writing. `compare` requires two complete source
manifests and lists added, removed or changed guide input identities as due for
review; it does not clear a guide or repair output. All three accept `--json`.

Exit 0 means the requested generation, check or comparison passed. Exit 1 means
findings were returned; local path or client failures exit 2. Invalid command
arguments also return a nonzero exit code.
Missing sources and incomplete coverage produce findings and failure manifests,
with no partial guide set. See [Community build guides](community-build-guides.md)
for the input contract, output files and review workflow.

`guides export` prepares the proposed catalog repository as a clean local bundle.
It requires the same explicit source roots and index, plus a private reviewed
`--plan` and a new or empty `--out`. Optional `--issues` supplies the public issue
map. `guides publication-check` regenerates the proposal and reports additions,
changes and removals against an existing local `--destination`, writing nothing.
Both accept `--json`; neither creates a repository or publishes.

`guides request-validate --inventory <file> --request <file>` validates shared
request fields against the public inventory. `guides requests-reconcile
--inventory <file> --observations <file>` reconciles supplied provider revisions to
one canonical issue and one vote per hosting account and entry. Both read JSON
files and return JSON without provider writes. Reconciliation output is private.
The importer, publication operation and request activation remain planned; the
[catalog preparation workflow](community-build-guides.md#prepare-the-proposed-catalog-repository)
describes their separate decisions and the retained-page retirement bridge.

## `vericairn operator close`

From 0.8.4, an operator command replaces per-release closing scripts.
Without options it lists exactly the latest completed, reviewed and unaccepted results with their
exit questions, result summaries, passing-review summaries and independence, and exact hashes.

| Option | Meaning |
|---|---|
| `--select <key>` | Select a work-order key; repeat for several steps. |
| `--all` | Select the entire displayed eligible list; cannot be combined with `--select`. |
| `--by <operator-id>` | Required with a selection. The operator's own admitted device signs every acceptance. |
| `--from <file>` | Use a saved `operator close --json` listing with a selection; `-` reads standard input. |
| `--json` | Print one typed response. Selected evidence is shown on standard error before signing. |
| `--estate <root>`, `-e <root>` | Select the estate. |

Each acceptance names its exact result and all displayed passing reviews. A changed selected
work order, result or review, a missing passing review, an existing acceptance or a non-operator
refuses the whole action without acceptances. `--all` also pins complete eligible membership.
Current signing custody checks presence per acceptance when required. Exit 0 means success,
1 a selection or record refusal, and 2 invalid arguments or a client failure. The
[operator actions guide](operator-actions.md#close-finished-steps-in-one-action) describes saved
listings, API/SDK counterparts and recovery after interrupted storage.

## `vericairn decisions new`

From 0.8.4, `decisions new` records a supplied operator answer with its exact design document pins. See the [operator workflow](operator-guide.md#record-an-answered-ask-with-its-design-changes).

| Option | Meaning |
|---|---|
| `--by <id>` | Required recorder principal. The ask supplies the deciding operator. |
| `--ask <id>` | Required ask revision id; its current state must still wait for this operator's supplied answer. |
| `--statement <text>` | Required verbatim operator words, including whitespace. |
| `--answered-at <timestamp>` | Required UTC answer time, with second precision. |
| `--base <commit>` | Required full local commit before the design changes. |
| `--title <text>`, `--summary <text>`, `--synthesis <text>` | Required decision prose. |
| `--effect <text>` | Repeat for each effect; at least one is required. |
| `--document <path>` | Repeat for every design file changed from the base through the working tree; omit for an empty set. |
| `--stake <value>` | `low` (default), `medium`, `high` or `critical`. |
| `--affects <id>` | Repeat for affected work orders. |
| `--not-authorized <text>` | Repeat for exclusions. |
| `--decided-at <timestamp>` | Defaults to the answer time. A later time is refused. |
| `--expected-ask <hash>` | Optional expected hash of the current ask revision. |
| `--json` | Complete `DecisionNewResponse`, on success or refusal. |

The API is `POST /v1/decisions/new`. Its `DecisionNewRequest` has required fields `by`, `ask`, `statement`, `answeredAt`, `baseCommit`, `title`, `summary`, `synthesis`, `effects` and `documents`; optional fields are `stake`, `affects`, `notAuthorized`, `decidedAt` and `expectedAsk`. An empty document list is explicit in JSON. From 0.9.0 an empty `baseCommit` takes the runtime's current head, the basis `GET /v1/decisions/basis` reads; the command still requires `--base`. SDK operations are `DecisionFromAsk` and `DecisionFromAskAsync`, with the same request.

The response has `written` (decision receipt or null), `pins` (stored document-revision receipts), `problem` and `ok`. HTTP 422 means validation refused with no records stored; HTTP 200 carries success or a partly stored batch. Always inspect `ok` and `problem`. A `reconcile-required` problem preserves the receipts actually stored and requires inspection before retrying. Cancellation after dispatch can also leave an unknown outcome; read the ask and original receipts before sending another write. CLI exit 0 means the complete batch was stored; exit 2 reports a refusal or incomplete batch. For documents already converted to generated revision histories, the batch refreshes those histories with the new pins, carrying the new decision's identity in each line. The command does not commit, push or publish.

## `vericairn docs pin`

Record a document's next revision from the exact bytes in the estate repository:

```sh
vericairn docs pin docs/guide.md --by <principal id> --summary "Describe the revised workflow."
vericairn docs pin docs/guide.md --by <principal id> --summary "Update the examples." --expected <current pin id> --json
```

| Argument or option | Meaning |
|---|---|
| `<path>` | Required. A normalized repository-relative file path under `design/` or `docs/`, using forward slashes. |
| `--by <id>` | Required. The authoring principal; the runtime uses its admitted device under the estate's signing rules. |
| `--summary <text>` | Required. What this revision is or changes. |
| `--title <text>` | Override the title. Otherwise carry the current pin's title; a first pin uses the file name without its extension. |
| `--expected <id>` | Require this exact record id to be the current pin for the path. An old, unrelated or unknown id refuses the write. |
| `--render-template` | Default false. Refresh existing template regions, or a converted revision history even when authored bytes are unchanged, in the pin's recoverable batch. Also admits root `README.md` template instances in format 3. |
| `--convert-revisions` | Default false. In format 3, convert a Markdown document's opening revision lines to a generated history. Every existing line must match one distinct revision record. |
| `--decision <id>` | The exact decision record carried by this revision, when one applies; rendered as `carries decision` in a generated line. |
| `--json` | Print the record receipt as `{"path":"records/document-revision/...","id":"...","hash":"sha256:..."}`. The hash identifies the record; its body holds the document's byte hash. |
| `--estate <root>`, `-e <root>` | Select the estate; the runtime reads the file in that estate's repository. |
| `--help`, `-h`, `-?`, `/?`, `/h` | Show command help. |

Existing whole-file pins keep their behavior: the first pin is revision 1 and each later pin increments the current revision and supersedes that record. SHA-256 and byte count cover the exact file, including line endings and any byte-order mark. Media type follows the file extension: Markdown, plain text, JSON, YAML, HTML, XML, CSV, PDF, SVG, PNG and JPEG are recognized; other extensions use `application/octet-stream`.

By default, unchanged bytes refuse even if the title or summary differs. Paths outside the document directories, unavailable files, symbolic links, case aliases, whole generated documents and generated register members refuse. Authored documents may contain generated regions. A stale expected id or multiple current pins also refuses. These refusals record nothing. Without `--expected`, the runtime selects the current pin when the operation begins; clients that read a pin earlier should supply its id.

For whole-file pins, two branches pinning the same predecessor still form a fork. `estate check` and `docs pin` identify both pins and the predecessor; a `reconciliation` keeps one successor and withdraws the others before pinning combined bytes. Converted histories instead order all revision records by recorded time and then id, and derive distinct ordinals from that order. Their `revision` integer is advisory. The latest record in this order is the current pin; all records and their actual predecessors remain in history.

Text output prints the document revision, record id and record hash. Exit 0 means the pin was recorded; a refusal exits 2 with a diagnostic or validation findings. A receipt accompanied by a companion-recovery problem also exits 2: reconcile the stored operation before retrying. Whole-file pins leave document text alone unless template rendering is requested. Converted documents always regenerate their revision region, including the new pin's line. See [generated revision histories](living-documents.md#generated-revision-histories) and [template adoption](templates.md#adopt-an-existing-estate-document).

Convert one document from its exact current whole-file pin:

```sh
vericairn docs pin design/example.md --by <principal id> --summary "Generate the revision history." --convert-revisions
# Edit the document body before the next pin.
vericairn docs pin design/example.md --by <principal id> --summary "Carry the revised design." --decision <decision id>
```

Conversion preserves each existing line's date and explanation in metadata bound to its old record id and hash. It refuses missing, duplicate or ambiguous matches and leaves files and records untouched on refusal. Old pin bytes and signatures remain unchanged. For a new document with a title and no revision lines, the same option starts its generated history. Subsequent pins hash only authored content: everything between `<!-- vericairn:generated revisions -->` and `<!-- vericairn:end revisions -->` is excluded, while both marker lines remain hashed.

After merging branches, resolve any authored content conflicts, then run `docs pin --render-template --summary "Combine the revised design."` with the path and author. It replaces the generated payload from the complete merged record set, including textual conflict markers inside that payload. No revision number is chosen by the caller. A revision's displayed ordinal can change when an earlier record arrives from another branch.

The matching API is `POST /v1/documents/pin`, with JSON fields `path`, `by`, `summary`, optional `title`, `expected`, `decision`, `renderTemplate` and `convertRevisions` (both flags default false). It returns `DocumentPinResponse`: `written` (the record receipt), `revision` (the derived ordinal for converted histories), `problem` and `ok`. A refusal returns HTTP 422 with no `written` or `revision`; a stored receipt returns HTTP 200, including when a companion problem needs reconciliation. SDK clients use `DocumentPin(DocumentPinRequest)` or `DocumentPinAsync(DocumentPinRequest, CancellationToken)`:

```csharp
var result = await client.DocumentPinAsync(
    new DocumentPinRequest("docs/guide.md", principal, "Update the examples.", Expected: currentPinId),
    cancellationToken);
```

The runtime reads its estate file for every client. Callers supply neither document bytes nor a claimed hash or revision number.

## `vericairn policies hash`

Print the definition hash of a prepared policy or recurring-duty body:

```sh
vericairn policies hash --body policy.json
vericairn policies hash --body duty.json --json
```

| Option | Meaning |
|---|---|
| `--body <file>` | Required. Read the prepared JSON body from this file. Use `-` to read standard input. Supply the body without a record envelope. |
| `--json` | Return `{"definitionHash":"sha256:...","problem":null}`. Without this option, print only the hash and a newline. |
| `--help`, `-h`, `-?`, `/?`, `/h` | Show command help. |
| `--estate <root>`, `-e <root>` | Inherited root option; hashing does not use it or open an estate. |
| `--recipient <id>` | Inherited presentation option; hashing does not use it. |
| `--for-assignment <id>` | Inherited presentation option; hashing does not use it. |
| `--depth <depth>` | Inherited presentation option; hashing always prints the same hash or JSON response. |

The value is SHA-256 over the record format's RFC 8785 canonical body, with only the top-level `basis` member omitted. This is the `definitionHash` an operator approves and `policies declare` checks. `basis` may be absent while preparing approval; adding or changing it leaves the hash unchanged. Every other member contributes, including a nested member named `basis`.

The command reads only its input and writes no files, records, keys or live state. It needs neither an estate nor write authority. Exit 0 means a hash was printed. An unreadable file, invalid JSON, a value that cannot be canonicalized, or a value without the policy body structure exits 2 with a diagnostic and no hash.

Hashing checks the body structure and canonical JSON representation. Declaration still validates references, rule parameters, approval and authority. See the [adoption guide](policy-adoption.md) for using the value in an exact approval, and [Estate policies](policies.md) for the API and SDK equivalents.

## `vericairn packages`

Observe nuget.org's public state, or record an intent for its account holder. These commands require
a valid estate at record format 3 and a product's `packages` declaration. By default they select a
recorded release version. From 0.8.4, `--reserved` explicitly selects a placeholder version declared
in `packages.reserved`, without requiring or creating a release record.
The [operator procedure](operator-actions.md#package-publication-unlisting-and-deprecation) gives the declaration and the order of actions.

```sh
vericairn packages observe --product <product-id> --version 1.0.0 --by <principal-id>
vericairn packages unlist --product <product-id> --version 1.0.0 --package Example.Widget --by <operator-id>
vericairn packages deprecate --product <product-id> --version 1.0.0 --package Example.Widget --by <operator-id> --reason Legacy --message "Use the replacement package." --alternate-package Example.Replacement --alternate-range "[2.0.0,)"
vericairn packages observe --operation <original-intent-id> --by <operator-id> --json
```

| Command and option | Meaning |
|---|---|
| `observe --product <id> --version <version>` | Observe every package in the current product declaration for this recorded release version. Both options are required together. |
| `observe --operation <id>` | Observe the package and original release or reserved-version pins of an unlisting or deprecation intent. Use instead of `--product`, `--version` and `--reserved`; any revision identifies the original intent. |
| `observe --by <id>` | Required observing principal. Recording an intent's outcome requires the estate operator. |
| `unlist`, `deprecate`: `--product <id> --version <version> --package <id> --by <id>` | All required. Record one package version's intent as the estate operator and print the nuget.org step. These commands perform no registry write and make no registry request. |
| `observe`, `unlist`, `deprecate`: `--reserved` | Optional, default false. Require `--version` to be declared reserved in the current product; pin that exact declaration and version in place of a release. Cannot accompany `observe --operation`. |
| `deprecate --reason <reason>` | Required. `Legacy`, `CriticalBugs` or `Other`; repeat the option or supply multiple values. |
| `deprecate --message <text>` | Optional exact message, at most 500 characters. Omitted means no message. |
| `deprecate --alternate-package <id>` | Optional replacement package. Omitted means no replacement. |
| `deprecate --alternate-range <range>` | Optional replacement version range, at most 256 characters. Requires `--alternate-package`. |
| `--json` | Return the complete `PackagesResponse`, including original `intent`, latest `operation`, manual `instruction`, observations and any problem. |
| `--estate <root>`, `-e <root>` | Select the estate in which to record the observations or intent. |
| `--recipient <id>`, `--for-assignment <id>`, `--depth <depth>` | Inherited presentation options. Package responses keep their complete evidence and fixed factual text. |
| `--help`, `-h`, `-?`, `/?`, `/h` | Show help. |

Text output names each written record, id and hash, then each package's publication, listing and
deprecation states. JSON exposes `packages[].state.evidence[]` as exact URL, response-body SHA-256,
HTTP status, byte count and content encoding, beside the observation record. Hashes cover the received
body bytes before gzip decoding. Deprecation details retain NuGet's reasons,
message and alternate id/range when present. A missing version is `not-found` with `unknown` listing
and deprecation. That successful observation exits 0. Refusal or an unconfirmed intended outcome
exits 1. Failed network reads create no observation; an existing intent remains available to resume.

For example, after declaring `"reserved": ["0.0.1-reserved"]` in the product's `packages` member:

```sh
vericairn packages observe --product <product-id> --version 0.0.1-reserved --reserved --by <principal-id>
vericairn packages unlist --product <product-id> --version 0.0.1-reserved --reserved --package Example.Widget --by <operator-id>
vericairn packages deprecate --product <product-id> --version 0.0.1-reserved --reserved --package Example.Widget --by <operator-id> --reason Legacy --message "Use the first release, 1.0.0." --alternate-package Example.Widget --alternate-range "[1.0.0,)"
```

Reserved declarations contain one to eight distinct valid NuGet versions, compared without case.
A reserved version cannot also be a recorded release of the same product. Without `--reserved`,
the recorded-release requirement remains; with it, an undeclared version is refused. Reserved
observations retain the same service-index, registration and package-content response evidence,
time checks and outcome confirmation as release observations. Use the printed `observe --operation`
command after each account-holder act; its original pins survive later declaration changes.

An identical intent command returns its original identity, including after restarting the runtime.
Changing the intended deprecation metadata under that identity is refused. Observe the original
operation after performing the printed account-holder step. A matching outcome becomes
`observed-success`; a completed read that does not match remains `uncertain`. No dispatch timestamp
is invented for an account-holder act. Re-observation can report a later change without rewriting a
historical success.

| API operation | SDK method and cancellable counterpart |
|---|---|
| `POST /v1/packages/observe` with `PackagesObserveRequest` | `PackagesObserve`, `PackagesObserveAsync` |
| `POST /v1/packages/unlist` with `PackagesUnlistRequest` | `PackagesUnlist`, `PackagesUnlistAsync` |
| `POST /v1/packages/deprecate` with `PackagesDeprecateRequest` | `PackagesDeprecate`, `PackagesDeprecateAsync` |

Each request also accepts optional JSON `reserved` (boolean, default false), exposed by the SDK as
the init-only `Reserved` property; existing constructors are unchanged. Set it when selecting a
declared placeholder by product and version. Operation observation keeps the original intent's selection.

All return `PackagesResponse`; the API binds `by` to the authenticated acting device like the other
record-writing operations. `GET /v1/providers` and `release providers` list the `nuget-org` package
registry provider and its limits. There is no API-key option or registry mutation method.

## Template documents (0.8.4)

`vericairn templates list` lists available exact versions; `templates show <id>`
prints their contracts and accepts `--version`. Create an instance with:

```sh
vericairn docs new example/design docs/design.md --version 1.0.0 --by <owner> --title "Design"
vericairn docs sections docs/design.md --json
vericairn docs brief docs/design.md --section overview --max-bytes 60000 --json
vericairn docs review docs/design.md --section overview --fidelity draft --by <reviewer> --outcome current --summary "Read the exact section and sources."
```

Creation also accepts `--configuration <file>` containing `options`, `scope` and
`overrides`, and `--level remind|warn|refuse` (default `warn`). It refuses an
existing file. Section reads and briefs accept `--commit` for an exact external
repository source. Section review requires `--section` and `--fidelity` together;
`--judgments <file>` supplies evaluator verdicts and exact evidence for that
section. All commands retain the root connection options and `--json` behavior.
A brief exceeding `--max-bytes` exits 2 with explicit incomplete evidence and no
silent truncation. Creation interrupted after declaration storage also exits 2
and preserves its receipt for reconciliation. See [Templates](templates.md) for
the contract, defaults, overrides, review rules and recovery.

For an existing file, `docs adopt <template> <path> --version <version> --by
<owner> --expected-content <file hash>` preserves authored bytes and history.
An existing living declaration also requires `--expected-declaration <record
hash>`. Optional `--configuration`, `--title` and `--level` supply the same
choices; existing duties remain unchanged. `--preview` returns the unsigned
proposal as JSON. `--with-regions` explicitly includes generated ownership and
requires prepared markers and any existing migration review. After adoption,
`docs pin --render-template` refreshes those regions. See the complete
[adoption workflow](templates.md#adopt-an-existing-estate-document) before
transferring a hand-maintained table to records.

## Preparing a workspace before the agent starts (0.9.1)

`worker run <assignment> ... --prepare-only` prepares and registers the run's
workspace with the run's own refusals, prints its location on the first line and
exits. It runs no harness probe or setup command, records nothing and launches
nothing; `--json` prints the `RunResponse` with status `prepared`. It cannot be
combined with `--detach`. Install dependencies there, in paths the repository
ignores and without links, then run the same assignment on the same branch
without `--prepare-only`: the run takes the workspace up after checking what
changed since.

`worker configure --repository <resource> --setup-command <executable or JSON
array>` (or `--launcher <id>` in place of `--repository`) sets a command the
worker runs in the workspace before each such run, outside the agent's sandbox.
`--setup-timeout <seconds>` sets its deadline, 1 through 600 and 300 when
omitted. The command runs work-branch content as you; from 0.9.2 it receives
the environment a harness would, without credential or loader variables, and
`--setup-variable <name>`, repeatable, passes a named variable besides.
`--clear-setup` removes it; `--repository` alone shows it, and
`worker configure` with no choices shows every setup command beside the
workspace root. See [Preparing a workspace before the agent
starts](software.md#coding-agents-the-worker-and-its-launchers) for the checks
that follow setup and what the run records.

## `vericairn operator unfinished` (0.9.2)

Shows this clone's unfinished operator action, or says there is none: its record id, type and
subject, branch, head, pull request and stage, the merge commit and latest target-check verdict once
merged, and what finishing it needs. Reading writes nothing.

| Option | Meaning |
|---|---|
| `--finish` | Finish an action whose merge is recorded, from its own records, once its target checks pass. Nothing is signed, branched or committed. |
| `--by <operator-id>` | Required with `--finish`: the action's own operator, on their admitted device. |
| `--wait-seconds <n>` | With `--finish`, the longest wait for pending target checks, 0 to 240; 120 when omitted. |
| `--json` | Print one typed response. |

Exit 0 means a valid read or a finished action (or nothing to finish), 1 a refused, stopped or
waiting action, and 2 invalid arguments or a client failure. `operator record` finishes a merged
action the same way before it starts the next one. See [Operator
actions](operator-actions.md#finishing-an-action-after-its-merge-092).

## Runs of a flow (0.9.2)

`vericairn templates list` includes flows, templates of kind `flow`. `goal propose --flow <id>@<version>` proposes a run of
one: its plan names the flow's exact pin, read from the templates the runtime offers, and gives exactly one `--work-order`.
`--subject name=value` binds each field of the flow's subject by its kind: a record field by its id or `type:id`, a list field
as comma-separated instance names, a text field as its text. `--flow-option name=value` sets the goal's own configuration, a
flow option, `step.<key>=on|off` for an optional step or `item.<step>.<item>=on|off` for an optional item, and
`--until <step>` names the step the run ends at.

```sh
vericairn goal propose --by <sponsor> --title "Release the song" --objective "Produce a song and release it." \
  --done '{"type":"release","subject":"<product id>","fields":{"status":"published"}}' --work-order <work order id> \
  --budget wallMinutes=120 --budget attemptsPerStep=3 --flow example/song@1.0.0 \
  --subject product=<product id> --subject version=1.0.0 --subject tracks=intro,outro --flow-option step.shorts=off
vericairn goal show <goal>
vericairn goal next <goal>
vericairn goal step <goal> --by <operator> --repository <repository id> --wall-minutes 30
```

`goal show` adds the run's position, `goal next` routes its ready work and review steps, and `goal step` takes one of them
per command. `--subject`, `--flow-option` and `--until` without `--flow` are refused.

From PROC-10 (0.9.2), `goal next` also routes each review before hand-off as `<step> review`, and each step's required
attention as route state `attention`. For a review, `goal step` records an assignment whose `step.review` names the items and
the exact subject, with an agent's run or a person's ask. For required attention, `goal step` records one ask to the sponsor
about the record that raised it and dispatches nothing for it. The run's own required attention, consecutive failed steps or an
evaluation of impossible, is asked first and holds every dispatch until a decision answers it. With `--json`, `goal next`
gains `attention`, `held` and each step's `review` and `attention`, and `goal show`'s flow gains each step's `attempts`,
`attemptsUsed`, `attemptsAllowed`, `reviews`, `attention`, `failed`, `failure` and `rerun`, and the run's `attention`. See
[hand-offs, reviews and failure routes](operator-guide.md#hand-offs-reviews-and-failure-routes-092). See the
[operator's guide](operator-guide.md#runs-of-a-flow-092) and [Templates](templates.md#flows-092).

## `vericairn estate cache rebuild` (0.9.2)

`vericairn estate cache rebuild` rebuilds this clone's derived cache of a repository-synced estate from the repository's
record files (FIX-93). It moves the failed `live.db` with its `-wal`, `-shm` and any `-journal` file, unchanged, into a new
`failed-cache/<UTC time>` directory beside the cache. It refuses a local-only estate, whose database is canonical, and
changes nothing there. It also refuses while a daemon answers for the estate or another command has it open. With `--json`
it prints `estate`, `cache`, `records` (the record files read), `retained` (the kept directory, or null when no cache
existed) and `retainedFiles`. See the [operator's guide](operator-guide.md#a-damaged-live-store-092).

```sh
vericairn runtime stop
vericairn estate cache rebuild --json
vericairn estate check
```

## `vericairn estate upgrade` and the serving runtime (0.9.2)

Applying `vericairn estate upgrade --to <version> --by <id>`, or running it again once applied, ends with a runtime step
(OPS-05). When a daemon of a release older than the new pin serves the estate on this machine, the upgrade stops it and,
when its own command line is the pinned release's build (its version and the source commit the pinned release names),
starts that release's daemon in its place. It never touches a daemon of
the pinned or a newer release, or one serving another estate or root, and it starts none where none served. `--check`
leaves the runtime alone. A `runtime:` line on stdout says what it found or did; whatever still stands, with exactly what to
run, goes to stderr as a `warning:`, and the exit code stays 0, since the upgrade's records and pin stand either way. With
`--json`, an applied upgrade adds `runtime`: `outcome` is `none`, `pinned`, `kept`, `replaced`, `stopped`, `not-stopped`,
`unreachable` or `unchecked`; `served` and `started` give a daemon's `processId` and `release`, or null; `done` and `warning`
carry the two texts, or null. See the [operator's guide](operator-guide.md#the-serving-runtime-after-an-upgrade-092).

`vericairn runtime status` adds a `release` line with the serving daemon's release against the estate's pin and, while they
differ, a `mismatch:` line with the commands that settle it. Its `--json` adds `servingRelease`, `pin` (the manifest's pin as
`estate upgrade` describes it), `pinnedRelease` (null for a source pin) and `mismatch` (null when they agree or no daemon
serves). Human `vericairn status` ends, when a daemon served it, with `Serving runtime (this machine):` and any `Mismatch:`
line. `status --json` stays the canonical document, identical on every machine, and a mismatch goes to stderr.

```sh
vericairn estate upgrade --to <published-version> --by <principal id>
vericairn runtime status
```

## `vericairn events watch` (0.9.2)

Follows the event stream of the daemon serving this estate over the local socket (`WATCH-05`), one line per event, and
reconnects from the last cursor when a stream ends, until interrupted. An event is a hint carrying identities, kinds and
states but no content; read its subject before acting. The operating-system user reads for `--principal`, the operator
when omitted, and every event passes that principal's audience. Only a running daemon keeps the journal: without one the
command is refused, and `vericairn runtime start` starts it.

| Option | Meaning |
|---|---|
| `--after <cursor>` | Resume after this cursor: an event's, a reset's or a state read's `eventsCursor`. From now when omitted. |
| `--kind <kind>` | Only events of this kind: `record`, `attention`, `ask`, `step`, `request`, `due`, `run` or `presence`; repeatable. |
| `--type <type>` | Only events whose subject has this type, such as a record type; repeatable. |
| `--subject <id>` | Only events about this record; a goal matches its work orders, steps and runs; repeatable. |
| `--to <principal>` | Only events concerning this principal; repeatable. |
| `--category <category>` | Only attention of this category: `decision-needed`, `blocked`, `uncertain-effect`, `failed-check`, `incident` or `information`; repeatable. |
| `--principal <id>` | The principal to read for; the operator when omitted. |
| `--count <n>` | Stop after this many events. |
| `--json` | One JSON object per line: the opening `{"cursor":...}`, each `vericairn.event/1` envelope, and `{"reset":true,"cursor":...}`. |

A text line reads `<at>  <kind> <change>  <subject type> <subject id>`, then `to <principal>` and the category when the
event has them. A line beginning `reset:` means the cursor could not be honored: re-read the state you follow, then
continue. Start the watch before reading that state, or resume from the cursor the read returned, and no change is
missed. Exit 0 when interrupted or after `--count` events, 2 when refused.

```sh
vericairn events watch --kind record --kind ask --json
vericairn events watch --subject <goal id> --after <cursor>
```

A script that follows `events watch` and runs `goal step` acts with its own principal's authority and stays Community. See
[API version 1](api-v1.md#the-event-stream-092) and the [operator's guide](operator-guide.md#events-092).

## Keyword search and search-selected context (0.9.2)

`vericairn search <query>` searches the estate's machine-local keyword index of records, pinned documents and knowledge
drafts (CONTEXT-02). The query is data: words, double-quoted phrases and the filters `type:` (a record type or a knowledge
kind), `kind:` (`record`, `document` or `knowledge`), `path:` (a path or path prefix), `key:` (a record key), `since:` and
`before:` (dates, `yyyy-MM-dd`, inclusive and exclusive) and the bare word `history`, which adds superseded record revisions.
Anything else, including the full-text engine's own operators and column syntax, is matched as text. A query is at most
1,024 UTF-8 bytes and 32 words and phrases. Each candidate shows its rank, score, kind, title, matched fields, the
`context read` command for its `vc1_` pin and an untrusted excerpt of at most 320 bytes of the original. Candidates are
data and grant no authority. The selection, `vericairn.search-selection/1`, records the search; `--json` prints it in the
response. Searching writes no record.

| Option | Meaning |
|---|---|
| `--limit <n>` | Candidates requested: 10 when omitted, at most 50. |
| `--allow-stale` | Answer from the index as it is, labelled stale, when bringing it up to date would overrun the query's time budget. |
| `--method <keyword\|semantic>` | `keyword` by default. Semantic search needs a semantic search Adapter, which this release does not include; it is refused with its reason. |

To search for the bare word `index`, `replay` or `measure`, quote it as a phrase: `vericairn search '"index"'`.

```sh
vericairn search "withheld list context package"
vericairn search "keyword index type:decision since:2026-11-01" --limit 20
vericairn context read <vc1_ pin from a candidate> --original
```

`vericairn search index` brings the index up to date and shows each audience segment the caller may read, with its
passages and its record-set and document-set hashes, the index's size against the indexed source bytes and the time taken.
`--rebuild` discards every passage first. The index is derived, owner-only and machine-local; it is never committed,
exported or served, and deleting it loses nothing.

`--search <query>` on `context prepare`, `work brief`, `worker run` and `goal step`, repeatable up to five times, searches
after the work's explicit selection. The top three new candidates of each query become optional sources pinned by exact
hash, with the dependency `candidate from search s1; optional; grants no authority`; a candidate already selected is a
duplicate. The manifest becomes `vericairn.context-manifest/2` and records each search in its basis's `selections`. A
record stays whole; a document section or table row may be a labelled excerpt from its own offset. A package without
`--search` is unchanged, byte for byte. On `work brief`, `--search` implies `--agent-context`. A run records its searches in
its intent from record format 3; an earlier format refuses them.

```sh
vericairn context prepare W-1 --search "earlier decision on calibration" --search "release notes widget" --max-bytes 200000
vericairn work brief W-1 --search "calibration"
vericairn worker run <assignment id> --launcher claude-code --agent <principal> --by <operator> --repository <resource> --search "calibration"
```

`vericairn search replay <file>` replays every selection a file holds (a selection, a context package or its manifest),
and `--record <operation id>` replays those an agent-run operation records, optionally only the one `--selection <hash>`
names. A selection is rerun only on exactly its basis: this copy's index, this copy's records up to the selection's time,
or its commit read from local Git history. Otherwise it says so and approximates nothing. Exit 0 means every selection was
replayed and matched; 2 otherwise.

`vericairn search measure <questions>` runs a question set and reports recall at 5 and 10, mean reciprocal rank, the
index's size against its source bytes and query times; `--rebuild` adds the full build time. A question set is JSON
(`{"format": "vericairn.search-questions/1", "questions": [{"query": "...", "expected": ["..."]}]}`) or Markdown whose first
table has `Query` (or `Question`) and `Expected` columns. An expected source is a record id or key, a document revision id,
or a document path (a prefix when it ends in `/`). It records nothing. See the
[operator's guide](operator-guide.md#searching-for-context-092).

`vericairn doctor` adds a `search` line confirming that this build's SQLite has the full-text module the index uses.
