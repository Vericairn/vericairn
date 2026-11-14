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

Revision 27, November 13, 2026. Describe 0.9.3's command additions as released commands rather than development builds.

Revision 26, November 13, 2026. Add `estate first-authority`, which lists the self-admissions that could be the estate's first authority with their evidence and names one of the root operator's devices in the manifest, in the development build.

Revision 25, November 13, 2026. `docs pass --before` passes an earlier release named as a source or predecessor and names the rule each location matched, in the development build.

Revision 24, November 13, 2026. `goal propose --flow` binds the subject an extension flow inherits from its base, in the development build.

Revision 23, November 13, 2026. A command that writes announces and counts the presence prompts it asks for on standard error, in the development build.

Revision 22, November 12, 2026. Name the summary-only correction in `docs pin`'s overview and refusals, in the development build.

Revision 21, November 12, 2026. Add `doctor --json`, JSON from `version`, `records describe`, `records hash`, `records format` and `recover compare`, and the commands that refuse `--json`, in the development build.

Revision 20, November 12, 2026. Describe 0.9.2's command additions as released commands rather than development builds.

Revision 19, November 8, 2026. Add `estate checkpoint`, the history checkpoints the runtime writes, closings with `devices revoke`, `devices rotate` and narrowing grant revisions, a closing stored without its act and the act written again after it, and this machine's memory of the estate's history in `estate check`, in the development build.

Revision 18, November 8, 2026. `mcp serve --connection` holds the session's server stream while its harness holds a resource subscription and writes the resource notifications to standard output, in the development build.

Revision 17, November 7, 2026. Add `docs pass --before` and `estate verify` in the development build.

Revision 16, November 7, 2026. Add `search`, `search index`, `search replay` and `search measure`, and `--search` on `context prepare`, `work brief`, `worker run` and `goal step`, in the development build.

Revision 15, November 7, 2026. Add `estate check --expect` and the bootstrap's `--expect` in the development build.

Revision 14, November 7, 2026. Add `security-reports poll` and `security-reports triage` in the development build.

Revision 13, November 7, 2026. Add a run's stops to `goal step`, `goal show`, `goal next`, `status` and `operator list` in the development build.

Revision 12, November 7, 2026. Add the build tooling's documentation-set commands in the development build.

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
Signing custody checks presence once per acceptance when required; from 0.9.3 the acceptances are
announced on standard error before the first prompt, with `Signature k of n` at each. Exit 0 means success,
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
| `--render-template` | Default false. Refresh existing template regions, or a converted revision history even when authored bytes are unchanged, in the pin's recoverable batch. Also admits root `README.md` template instances in format 3. A plain whole-file document has neither, and the option is refused for it. |
| `--convert-revisions` | Default false. In format 3, convert a Markdown document's opening revision lines to a generated history. Every existing line must match one distinct revision record. |
| `--decision <id>` | The exact decision record carried by this revision, when one applies; rendered as `carries decision` in a generated line. |
| `--json` | Print the record receipt as `{"path":"records/document-revision/...","id":"...","hash":"sha256:..."}`. The hash identifies the record; its body holds the document's byte hash. |
| `--estate <root>`, `-e <root>` | Select the estate; the runtime reads the file in that estate's repository. |
| `--help`, `-h`, `-?`, `/?`, `/h` | Show command help. |

Existing whole-file pins keep their behavior: the first pin is revision 1 and each later pin increments the current revision and supersedes that record. SHA-256 and byte count cover the exact file, including line endings and any byte-order mark. Media type follows the file extension: Markdown, plain text, JSON, YAML, HTML, XML, CSV, PDF, SVG, PNG and JPEG are recognized; other extensions use `application/octet-stream`.

By default, unchanged bytes refuse even if the title or summary differs. From 0.9.3 (FIX-120), that refusal and the refusal of `--render-template` for a plain whole-file document name the summary-only correction: a successor written with `records new document-revision` that pins the same path, hash and size ([living documents](living-documents.md#correcting-a-revisions-summary)). Paths outside the document directories, unavailable files, symbolic links, case aliases, whole generated documents and generated register members refuse. Authored documents may contain generated regions. A stale expected id or multiple current pins also refuses. These refusals record nothing. Without `--expected`, the runtime selects the current pin when the operation begins; clients that read a pin earlier should supply its id.

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
`--until <step>` names the step the run ends at. From 0.9.3 (FIX-127), `--subject` reads the subject of the flow resolved with
every base it extends, the subject the goal's validation and `goal show` read, so a run of an extension, which declares no
subject of its own, binds each field of its base's; before, it refused every field of an extension.

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

From PROC-06 (0.9.2), a reserved step that is next is routed as `request`: `goal step` records its operator request, carrying
the step's exact command as the records give it then, and prints the command; its outcome is `request`, and with `--json` the
response's `request` names the record. The command is computed only for a reserved step that is next. Asked again while the
request waits, `goal next` shows the route as `requested` and `goal step` records nothing more; once a request carrying that
command was closed, the route is `closed`. `goal show`, `goal next` and `status` list the run's stops, each with why it waits,
on whom and how it reaches them, and for a reserved step that is next its command; with `--json`, the position's `stops`, each
step's `stop` and `command`, and status's `runs`, whose stops carry no command. At most three stops of the estate's runs reach
one person at a time: a fourth routes as
`queued`, and `goal step` dispatches nothing for it. `operator list` delivers a run's request only while it carries the command
the records give now, and lists each open request a run no longer waits on, `withdrawn` or `settled`, under `withdrawn` in its
JSON. See [stops and their surfaces](operator-guide.md#stops-and-their-surfaces-092).

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

## Network MCP: `runtime configure --mcp` and `mcp serve --connection` (0.9.2)

`vericairn runtime configure --mcp on` turns on network MCP (`MCP-03`): from the runtime's next start the daemon serves
the Model Context Protocol over Streamable HTTP at `/mcp` on its loopback and local-network listeners, to agents' and
services' devices whose every request and record is signed by that device. `--mcp off` stops it; it is off by default
and never served on the local socket. `--mcp-origin <origin>` names an origin (`scheme://host[:port]`) a request may
carry, repeated for more; it replaces the list, `--mcp-origin none` empties it, and a request naming any other origin is
refused before it is authenticated. A request without an `Origin`, as native clients send, needs none. The settings are
kept in `runtime.json` beside the live store with `--local-network`, and `vericairn runtime status` shows `mcp  /mcp`
while a daemon serves it.

| Option | Meaning |
|---|---|
| `--mcp on\|off` | Serve network MCP at `/mcp` from the runtime's next start, or stop. Off unless set. |
| `--mcp-origin <origin>` | An origin a request to `/mcp` may name; repeatable; replaces the list; `none` empties it. None by default. |

On the agent's machine, `vericairn mcp serve --connection <connection>` bridges a stdio harness to that estate over a
connection `vericairn connections add` paired, named by its id or its estate's name. It serves standard input and output
as `mcp serve` does and forwards each message to the runtime's `/mcp`, signing every request with the connection's
device. Only a write is prepared: its record is checked to be the one the call asks for (the tool's record type, this
device's principal as author, no supersession, a time within a plan's ten minutes of this machine's clock, the assignment
or attempt the call names, every field the call supplies unchanged), signed with the same key and then written; nothing
another call returns is ever signed. The device also signs what the runtime computes and the bridge cannot check, such as a
document review's subject and checks or a result's retained progress claim, so bridge only to a runtime you trust
([what a signature vouches for](mcp.md#network-mcp-over-streamable-http-092)). The remote runtime judges each call as its API operation for that
device, and offers the read tools and the contributor writes only. From 0.9.2 (`MCP-05`), while the harness holds a
resource subscription the runtime accepted, the bridge also holds the session's server stream and writes each resource
notification on it to standard output as a line between answers, as `mcp serve --as` does over a local daemon
([subscriptions and notifications](mcp.md#subscriptions-and-notifications-092)). It does not take `--as`: the paired
device's own principal acts. Diagnostics go to standard error. Exit 0 when the input ends, 2 when the connection is unknown or its
key is no longer held here.

```sh
vericairn runtime configure --local-network 192.168.1.20:7443 --mcp on
vericairn mcp serve --connection <connection id>
```

See [The MCP server](mcp.md#network-mcp-over-streamable-http-092) and the
[operator's guide](operator-guide.md#an-agent-on-another-machine-network-mcp-092).

## Preparing a documentation set with the build tooling (0.9.2)

`vericairn-docs`, the build tooling in `tools/Vericairn.DocsTool` and not part of the shipped command line, adds five
commands beside `generate` and `compare` (DOCS-11). They prepare a release's set in the steps its builder took by hand.
`vericairn docs notes` and `vericairn docs check` are unchanged. Each command writes only the set's directory,
`docs/releases/frozen-sha256.json` or `docs/releases/README.md`, changes nothing when run again, and refuses rather than
overwrite a person's text or a frozen set.

| Command | What it does |
|---|---|
| `assemble --out <set> --documents <file>` | Generates the references and manifest as `generate` does, copies every declared maintained guide byte for byte, writes `api.md` from `api-v1.md`, and keeps the set's overview. `--overview <file>` copies the overview once when the set has none; `--guides <dir>` names the guides' directory, `docs/` by default; `--keep-references` keeps references generated earlier. |
| `insert <set> --section <file>` | Inserts the approved section byte for byte before `## What the release contains` in `release-notes.md`. `--before` and `--document` name another anchor line or document; `--expected-hash <sha256:hex>` refuses other bytes. |
| `freeze <set>` | Checks the set as `docs check` does, then adds every file's SHA-256 to `frozen-sha256.json` under its version. `--documents <file>` names the declaration, `documents.json` beside the sets by default. |
| `index <set> --paragraph <file>` | Adds a frozen set's paragraph, which links `[<version> set](<version>/README.md)`, to the release index, newest first. |
| `prepare --out <set> --documents <file>` | Runs `assemble`, `docs notes` when the set has none (`--estate`, `--from`, `--since`), `insert` (`--security`, `--security-hash`), `compare`, `freeze` and `index` (`--index-paragraph`), stopping at the first refusal. |

`--json` prints one JSON object per step. Exit 0 means the step is done or was already done, 1 that the set is incomplete
(`freeze`) or differs from the build (`compare` within `prepare`), and 2 a refusal. See the [operator's guide](operator-guide.md#preparing-a-set-with-the-build-tooling-092).

## `vericairn security-reports` (0.9.2)

Reads the security advisories of the repositories a product's `securityReports` declaration names, drafts and private
reports included, and records the operator's triage of one observed revision (OPS-11). While a declared repository cannot be
read completely, or an advisory holds, `release submit` and its `--check` refuse. A report (an advisory in `triage` or `draft`
state, or an unknown state) holds until a triage names its current revision, and a closed or withdrawn advisory until a triage
names it at any revision; a published advisory holds nothing and needs no triage. Both commands are the
operator's alone; any other principal is refused with the same answer whatever exists. See the
[operator's guide](operator-guide.md#security-reports-hold-a-release-092).

```sh
vericairn security-reports poll --by <operator-id>
vericairn security-reports triage <GHSA identifier> --repository <owner/name> --expected <content hash> --disposition no-action --reason "<your own words>" --by <operator-id>
```

| Command and option | Meaning |
|---|---|
| `poll --by <id>` | Read every declared repository now through its reader and print, per repository, whether the read completed, and per advisory its identifier, state, severity, content hash, and its triage, whether it holds every release untriaged, or that it holds nothing. Records and posts nothing. |
| `triage <advisory>` | The advisory's GHSA identifier, as `poll` or the attention notice shows it. |
| `triage --repository <owner/name>` | Required. The advisory's repository. |
| `triage --expected <hash>` | Required. The observed revision's content hash. A different current revision is refused as a conflict; review it, then triage that one. |
| `triage --disposition <value>` | Required. `security` (a vulnerability to fix), `duplicate` or `no-action`. |
| `triage --reason <text>` | Your reason, at most 500 characters and in your own words, never the report's text. Required for `no-action`. |
| `triage --by <id>` | Required. The operator, on an admitted device. |
| `--json` | Print the complete `SecurityReportsResponse`: `reads`, `reports` (each with `holds`), `held`, any `triage` record written and any `problem`. |

Exit 0 means a completed read or a recorded triage, and 1 a refusal: another principal, an unknown advisory, a changed
revision or an invalid disposition. A read that could not complete still exits 0 and names the cause on its repository's
line, since it is an observation, not a refusal.

| API operation | SDK method and cancellable counterpart |
|---|---|
| `POST /v1/security-reports/poll` with `SecurityReportsPollRequest` | `SecurityReportsPoll`, `SecurityReportsPollAsync` |
| `POST /v1/security-reports/triage` with `SecurityReportTriageRequest` | `SecurityReportsTriage`, `SecurityReportsTriageAsync` |

Both return `SecurityReportsResponse`, and the API binds `by` to the authenticated acting device. `GET /v1/providers` and
`release providers` list the `github-security-advisories` reader, kind `security-advisories`, with its limits. There is no
token option and no write to GitHub.

## `vericairn estate check --expect` (0.9.2)

`vericairn estate check --expect <checkpoint>` refuses a copy of the estate that lacks a history checkpoint known
elsewhere, such as the head another clone shows. A fresh machine cannot tell from the records alone whether the newest
checkpoints were removed together with the records written after them, so it checks against a head it was told
([signed history](operator-guide.md#history-checkpoints-092)). The checkpoint is named by its record id or its full content
hash, `sha256:` and 64 hexadecimal digits; at least sixteen hexadecimal digits of the hash are accepted, and anything shorter
is refused. The check adds `history.expected`, information, when the copy
holds it, signed and counting; otherwise `history.expected-missing`, an error, says whether it is absent, here but not
counting, or named ambiguously, and the command exits 1. Without `--expect` the check is unchanged.

```sh
vericairn estate check --expect <checkpoint id>
vericairn estate check --expect sha256:<64 hex digits of its hash>
./bootstrap --expect <checkpoint id>
./bootstrap.ps1 --expect <checkpoint id> status
```

The estate's `bootstrap` and `bootstrap.ps1` accept `--expect <checkpoint>` before any other argument: they run
`estate check --expect <checkpoint>` first and stop unless it passes, then run the arguments given, or nothing more
when there are none. `vericairn estate bootstrap --force` writes the new scripts into an existing estate.

`estate check` also warns `history.deleted-in-git` for each record file deleted in the checked-out commit's reachable
history, merges included, that is absent here and that no history checkpoint witnessed; a witnessed one is already the
error `history.removed`. The warning depends on the clone, so it is not part of the canonical status. In a shallow
clone it is reported unavailable, as information.

From OPS-07 the check also keeps this machine's memory of the estate's history, beside the clone's live store under the
Vericairn home and never in the estate, kept by the clone's directory, so a fresh clone into the same directory keeps it: the newest checkpoint it verified and the main-line commit that held it. The main
line is the local branch the origin's `HEAD` names, else `main`, else `master`, whatever is checked out. When the main line
no longer holds that checkpoint, as after a rewrite that removed the newest checkpoints with the records after them, the
check warns `history.rolled-back`; a checkout of another branch warns nothing. The memory advances only to a head that follows
the checkpoint it remembers. A passed `--expect` is remembered by the checkpoint it named, and a checkpoint it finds that the
main line holds replaces what the machine remembered, which is how an intended rewrite is accepted. `history.remembered`,
information, says what the machine remembers; a memory that cannot be read or kept is a warning. Neither is part of the
canonical status. While the memory holds a checkpoint the main line no longer holds, or the copy has not adopted, `release
submit` and its check object with `history.rolled-back`.

## `vericairn estate checkpoint` (0.9.2)

`vericairn estate checkpoint` writes a history checkpoint now, signed by the admitted device on this machine of the principal
`--by` names, in an estate that has adopted history checkpoints: it follows every head, lists every record no checkpoint
witnesses yet and has trigger `manual`. Witnessing grants nothing, so any admitted device, an agent's included, may write
one. `--check` shows what it would witness, signing and writing nothing; `--json` returns the plan or the checkpoint
written.

```sh
vericairn estate checkpoint --check
vericairn estate checkpoint --by <principal id>
vericairn estate checkpoint --adopt --check
vericairn estate checkpoint --adopt --expect-plan <plan> --by <root operator id>
```

`--adopt` adopts history checkpoints, once ([signed history](operator-guide.md#history-checkpoints-092)): the estate's first
checkpoint, with trigger `adoption`, witnesses every record present and anchors the unsigned records that count today, so
adoption changes no reading and admits nothing new. Only the root operator or a person holding `administer` signs it; any
other principal, an agent above all, is refused before anything is signed. `--adopt --check` lists what it would witness and
anchor: the count, each unsigned record it anchors with its type, date, path, the commit that added it and that commit's date,
the unsigned records that count for nothing today and stay so, records signed by a device revoked now, records that read
otherwise when roles and grants are read at each record's own time, each with a `NOTE` saying what differs (adoption keeps for
every record it witnesses the reading it has then; the list is advisory, the plan hash does not cover it, and adoption computes
it again, so it can differ from the check's), record files deleted in the repository's history, which no checkpoint can
witness, and record files no commit holds yet; a `NOTE` points out a record
dated more than a day before its adding commit, an uncommitted file and an admission the format-2 migration already anchored.
It says what it cannot list in a shallow clone or outside a git repository, and ends with `plan: sha256:<hash>`, the hash of
the anchored set. `--adopt --expect-plan <hash>` refuses unless the adoption anchors exactly that set; it is required when the
adoption anchors anything. Both refuse while this clone's memory holds a checkpoint the copy no longer holds. A new estate needs none of this: it adopts at birth, when the root operator's first device admits itself while
the estate holds nothing but what `estate init` wrote, and `devices admit` prints the adoption checkpoint after the admission.
An estate that has adopted refuses `--adopt`; one that has not refuses a checkpoint without it.

The runtime also writes checkpoints by itself in an estate that has adopted, each in the same batch as its act and signed by
the device then writing: at `estate upgrade --to` (trigger `upgrade`), at `release submit` before its own admission check
(trigger `release`; `release submit --check` writes none), after a charter revision (trigger `charter`), and once the unwitnessed tail holds 500 records or its oldest
record is 24 hours old (trigger `interval`), on the next write signed on this machine for its author by a device that needs no
person's presence. A command that writes one prints it after its own record:

```text
records/history-checkpoint/<YYYY-MM>/<id>.json  <id>  sha256:<hash>  (history checkpoint, trigger charter)
```

With `--json`, the record's output adds a `checkpoint` object with `trigger` and `written`. A checkpoint that cannot be
written never blocks the act: the record is written alone and standard error says why. A release submission is the
exception: its gate refuses with `history.checkpoint` and says why. So is an act a closing precedes: `devices revoke`, with or
without `--recovery`, `devices rotate`, and `records new authority --supersedes` with a revision that names another grantee,
drops a scope, reserves another action, adds a condition, adds or shortens an expiry or changes `bounds`. Each is stored after
its closing, a checkpoint with trigger `closing` signed by the key that performs the act and printed after the act, and is
refused with nothing written when the closing cannot be written or would close nothing, when the act is signed elsewhere or
goes through `operator record`, or when `--at` states a time more than five minutes before the newest record its closing would
witness or before the head checkpoint. A batch that stops after its closing removes the closing again when it can; a closing
that stays is printed before the error, which has code `closing-stored`, and with `--json` standard output holds
`{"code":"closing-stored","checkpoint":{"trigger":"closing","written":{...}}}`. Running the same command again, as the same
author, writes the act after that closing, alone, and standard error names the closing it follows. Only one trigger anchors: a charter revision to required
signatures by the root operator or a person holding `administer` anchors the unsigned records that count before it. `status`
shows the history line, for example
`History: adopted · head 9f86d081884c7d65 · 5,140 witnessed · tail 12 · no problems`; its sixteen hexadecimal digits name the
head to `estate check --expect`.

## Keyword search and search-selected context (0.9.2)

`vericairn search <query>` searches the estate's machine-local keyword index of records, pinned documents and knowledge
drafts (CONTEXT-02). The query is data: words, double-quoted phrases and the filters `type:` (a record type or a knowledge
kind), `kind:` (`record`, `document` or `knowledge`), `path:` (a path or path prefix), `key:` (a record key), `since:` and
`before:` (dates, `yyyy-MM-dd`, inclusive and exclusive) and the bare word `history`, which adds superseded record revisions.
Anything else, including the full-text engine's own operators and column syntax, is matched as text. A query is at most
1,024 UTF-8 bytes and 32 words and phrases. Each candidate shows its rank, score, kind, title, matched fields, the
`context read` command for its `vc1_` pin and an untrusted excerpt of at most 320 bytes of the original. Candidates are
data and grant no authority. Search may return a record the estate does not count, for example an unanchored or forged one;
a candidate's standing is judged by reading it, never by search, and `--search` on a package never selects such a record:
it is recorded as `excluded`, with its reason. The selection, `vericairn.search-selection/1`, records the search; `--json` prints it in the
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
passages and its record-set and document-set hashes, the index's size against the indexed source bytes and the time taken; a
caller that cannot read every segment is shown no total size, source bytes or update counts. `--rebuild` discards every
passage first; it runs from this machine's own command line or for an operator, and a paired device's request is refused. The
index is derived, owner-only and machine-local; it is never committed, exported or served, and deleting it loses nothing.
Several processes on one machine (a worker and its run's MCP server) share it, and one never deletes it while another has it
open. The analyzer normalizes (NFKC) and folds case with Unicode 16.0.0 tables built into Vericairn, so results never depend
on the operating system.

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
or its commit read from local Git history. Otherwise it says so and approximates nothing. One request replays at most 20
selections and names the rest; a selection a record keeps names its restricted segments only as one opaque `Restricted`
entry, and replaying it says their basis was not confirmed. A selection that cannot be read is answered as unavailable. Exit 0
means every selection was replayed and matched; 2 otherwise.

`vericairn search measure <questions>` runs a question set and reports recall at 5 and 10, mean reciprocal rank, the
index's size against its source bytes and query times; `--rebuild` adds the full build time. A question set is JSON
(`{"format": "vericairn.search-questions/1", "questions": [{"query": "...", "expected": ["..."]}]}`) or Markdown whose first
table has `Query` (or `Question`) and `Expected` columns. An expected source is a record id or key, a document revision id,
or a document path (a prefix when it ends in `/`). When the question set lies inside the estate (for example a pinned
`docs/search-questions.md`), candidates from that file itself are skipped, since each of its rows would match its own query
and outrank the real sources; the report names the skipped path. It records nothing. See the
[operator's guide](operator-guide.md#searching-for-context-092).

`vericairn doctor` adds a `search` line confirming that this build's SQLite has the full-text module the index uses.

## `vericairn docs pass --before` (0.9.2)

`docs pass <version> --before` checks the version-tracking guides before a release's documentation set is frozen, the release
flow's `docs-before-freeze` step (PROC-07). It reads each document the set's declaration names that the guides directory holds as
a plain file, `docs/` beside `docs/releases/documents.json` by default (`--guides` names another), and finds two things:

- a token of an earlier release standing as the current one: a version of one of the two newest published releases of the
  product before `<version>`, or of a candidate withdrawn since, outside a history section and a label. A history section's
  heading speaks of history, changes or earlier releases; a section with `<!-- vericairn:history -->` under its heading counts too,
  while a marker before the first heading marks nothing. A heading labelled with a release, such as `(0.8.3)`, describes current
  behaviour and is read like any other. A token stands as history in a label that holds only the release and work-order keys,
  such as `(0.9.1)`, `(FIX-90, 0.9.1)` or `(from 0.9.1)`, unless the label follows a word such as `release`, `version` or
  `current`; just after `From`, `since`, `in`, `until`, `before`, `after`, `fixed in` and the like; before `or earlier`; in a link
  to a versioned page or set; and on a `Revision` line. A document whose name carries a version, such as
  `api-compatibility-0.9.1.md`, is read for preparation wording only;
- preparation wording, such as notes that are a placeholder, `under preparation`, `not yet rendered`, `pre-build`,
  `the builder must`, `TODO` or `TBD`.

From 0.9.3 (FIX-125), a mention of an earlier release counts only where it names that release as the current one. A mention
as a source or predecessor passes: the value of `--from` or `--previous`, as in `docs notes --from 0.9.1`, and a token just
after `follows`, `succeeds`, `replaces` or `supersedes`, as in "follows 0.9.1's recorded end", besides those after `since`
or `after`, in a history section and in a label. Each location names the rule it matched: `current-claim`, where a word such
as `current` or `latest`, or a target such as `--to`, names it the current release; `release-artifact`, where it names that
release's install, download, package or tag; `current-mention`, any other mention outside a history section and a label;
and, for preparation wording, `preparation-phrase` or `unfinished-marker`. `--json` gives it as each finding's `rule`, and a
recorded verification quotes it with each location.

```sh
vericairn docs pass 1.2.0 --before --documents docs/releases/documents.json --product <product>
vericairn docs pass 1.2.0 --before --documents docs/releases/documents.json --previous 1.1.0 --previous 1.0.0
vericairn docs pass 1.2.0 --before --documents docs/releases/documents.json --product <product> --record --by <principal>
```

Each location is printed with its line. Exit 0 means both checks passed, 1 that one found a location, and 2 a refusal. The
earlier releases come from the estate's release records, or from `--previous`, which needs no estate and records nothing.
`--record --by <principal>` records each check's outcome as a `verification` of the product at the guides' commit, with method
`vericairn.software/prior-version-tokens` or `vericairn.software/preparation-wording`, `environment.release` the version and
`environment.previous` the earlier releases it read, separated by spaces, superseding the latest one of the same check and
version. Each location is named relative to the root of the repository that holds the guides, and a machine path a quoted line
names is recorded as `<path>`. It refuses a clone with uncommitted changes, since the verification names the commit it read, and refuses `--previous`:
the release flow counts a check only of the earlier releases the records give. Every file is read inside the guides directory:
`/` and `\` both divide a declared name, and a declared name, or any directory on its way, that is a link is refused. The
command reads files and changes none; `--before` goes with neither `--assign-to`, `--key`, `--document` nor `--output`.

## `vericairn estate verify` (0.9.2)

`estate verify --by <principal>` checks, on this machine, an estate pinned to a published release after its upgrade, the upgrade
flow's `runtime` and `verify` steps (PROC-07). It reads which runtime serves the estate, a daemon or, when none does, this
command line's own, and compares its release with the one the manifest pins; with the pinned release serving it, it runs the
estate check and derives the status. It records each outcome as a `verification` of the estate's charter at the estate
repository's commit, method `vericairn.software/serving-runtime` or `vericairn.software/estate-check`, with `environment.release`
the pinned release, `environment.served` the serving one and `environment.device` the signing device this machine holds for the
principal, superseding only this machine's latest one of the same check and release, so a pass on one machine never hides
another machine's failed check. The principal needs an admitted device whose key this machine holds.

```sh
vericairn estate verify --by <principal>
vericairn --json estate verify --by <principal>
```

Exit 0 means both passed and 1 that one failed: a runtime of another release serving the estate fails the runtime check, naming
it with `runtime stop` and the pinned release's start, and leaves the estate unchecked. The command stops and starts nothing; the
upgrade's runtime step does that. An estate pinned to a source commit is refused. A verification names no path of the machine it
ran on: the runtime by its release and process, another estate by its id, the commands as run in the estate's directory, and any
other path in a finding's message as `<path>`; the exact commands for this machine are printed after the check, and `--json`
gives them as `here`, never recorded.

## `vericairn doctor --json` and JSON from every command (0.9.3)

From 0.9.3 (FIX-119), every command that accepts the global `--json` writes decodable JSON on standard output with it, or
refuses the option before it runs. `doctor --json` writes one object with the facts of its text report under stable names.
`version`, `runtime`, `git`, `dotnet`, `estate`, `custody`, `enclave`, `records`, `search` and `daemon` are each an object of
`state`, `value` and `detail`, and `stateWarnings` lists the per-user state whose permissions cannot be restricted, the report's
`state` lines. `state` is `present`, `missing`, `unavailable` or `not-applicable`. `value` is the probe's main fact or `null`,
such as the version, the tool's version line, the estate's path, the default custody or the serving daemon's process id.
`detail` is the rest of the report's line or `null`.

| Field | States |
|---|---|
| `version`, `runtime`, `custody`, `records` | `present` |
| `git`, `dotnet` | `present`, or `missing` when the tool is not on `PATH` |
| `estate` | `present`, or `missing` for a path that is not an estate root; `value` is the path either way |
| `enclave` | `present` or `unavailable` on macOS, with the reason in `detail`; `not-applicable` elsewhere |
| `search` | `present`, or `unavailable` with the reason |
| `daemon` | `present` with its process, `missing` when commands run the embedded runtime, `not-applicable` outside an estate |

```sh
vericairn --json doctor
```

The text report is unchanged, and the release check's disclosure and notices stay on standard error with or without `--json`.
`version --json` writes `{"version": "..."}`. `records describe --json` writes the type list (`reads`, `writesAtMost`, `types` with
`name`, `sinceFormat` and `summary`, and `reserved`) or one type with its `fields`. `records hash --json` writes `{"hash": "..."}`,
`records format --json` writes the files it `formatted` and those left `unchanged`, and `recover compare --json` writes
`thisEstate`, `otherEstate`, `sameEstate` and the `onlyHere`, `onlyThere` and `differing` records by `type` and `id`.

Three commands refuse `--json`: `runtime serve`, which serves until stopped and writes the service's log; `mcp serve`, whose
standard output carries the MCP protocol; and `recover inspect`, whose report is text, while `status --json`, `devices list --json`
and `estate reconcile --json` give the same facts as JSON. The refusal is a parse error: exit code 1, the reason on standard error
and, as for any parse error, the usage help on standard output.

## Presence prompts on standard error (0.9.3)

From 0.9.3 (FIX-124) every command that may write follows the presence notices of the runtime that serves it, embedded or the
daemon, while it runs. When it signs with a device that needs its owner's presence, it prints on standard error, before the first
prompt, every record it will sign and what each authorizes; at each prompt, `Signature k of n: <type> <key>: <purpose>`; any
signatures it adds, with why, before they are asked for; and when it ends, how many it signed. Standard output keeps the
command's answer, so `--json` output stays one JSON document. A command that signs nothing with presence prints none of this,
and neither does a daemon of an older release. The [operator's guide](operator-guide.md#presence-prompts-announced-and-counted-093)
lists how many prompts the release and acceptance steps ask for.

## `vericairn estate first-authority` (0.9.3)

From 0.9.3 (CODE-72), `vericairn estate first-authority` names the estate's first authority in the manifest's
`firstAuthority`, which an estate holding more than one self-admission of the root operator's device needed by hand before.
Without `--device` or `--admission` it lists every self-admission that could be the first authority and writes nothing:

```sh
vericairn estate first-authority
vericairn estate first-authority --device <device id> --check
vericairn estate first-authority --device <device id> --expect-plan <plan> --by <root operator id>
vericairn estate first-authority --admission <admission id> --check
```

The listing names the root operator, what the manifest names, the first authority the estate reads and the findings about it
(`manifest.first-authority`, `history.root-ambiguous`, `history.manifest-root`). For each candidate it gives the admission, the
device with its name, platform, custody, presence and principal, and the time the admission states, which is a claim and never
evidence of order. It says whether the manifest names it and whether the estate reads it, and, judged as the first authority,
whether history checkpoints witnessed it before every other candidate, whether its device stands revoked, how many other devices
an admission that counts admits and how many records its device signed. It says whether the admission's own signature verifies, as
the estate verifies every signature: under its device's key at its stated time, over the hash its content has. A self-admission is
listed when its signature names a device of the line it admits, whether or not that signature verifies, so one that does not
verify is shown with the finding that says why. Each candidate ends `nameable` or `not nameable:` with why.

`--device` names a device by its id or any revision of its line; `--admission` names the self-admission itself, which a device
that admitted itself more than once needs. With `--check` the command prints the change,
`change: estate/manifest.json firstAuthority <before or (none)> -> <admission>`, and `plan: sha256:<hash>`, the hash of the change,
the manifest's bytes and everything the check shows: the root operator, what the manifest names and the estate reads, the findings
and each candidate's evidence, its signature's verification among it. It writes nothing. `--expect-plan <hash> --by <root operator
id>` writes it: refused unless `--by` is the root operator the first charter names and the estate still reads as that check read
it. It writes only `firstAuthority`, through a file beside the manifest moved into place, and prints `changed:` with each error that
remains about the root of trust and each error the estate reports on the admission named. Each remaining
`signature.self-admission` is another self-admission still in the copy: remove its record file yourself and commit it with
`estate/manifest.json`; the command removes nothing. A forged self-admission, whose signature does not verify, reads
`signature.invalid` and is not listed among them; `vericairn estate check` reports it, and you remove it the same way.

It names only an admission by the root operator of one of the root operator's own devices whose signature by that device
verifies. A self-admission whose signature does not verify, which anyone able to add a file can write, a device of an agent or
another person, a recovery device, a device with no such admission and any other record are refused, each with why. The root
operator is read from the manifest's charter, so the command lists and names nothing while `estate/manifest.json` does not parse
without findings, such as a key it does not know, a key twice or a form that is not canonical; restore it first. A
revoked device stays nameable, and the listing shows it revoked: the first device admitting itself is the root, revoked or not.
It refuses, writing nothing, when the estate's first authority already works, because the manifest names it or the estate's adopted
history settles it, since it never moves a working first authority; when the copy reads unsettled; and when history checkpoints
witness a self-admission first while it is read as the first authority, the one so witnessed included, since such history could
have come into the copy with it: compare the copy with a head known elsewhere (`vericairn estate check --expect <checkpoint>`) and
remove what does not belong. The manifest is not signed, so the command is the root operator's at the runtime's machine: over the
daemon it is `POST /v1/estate/first-authority`, refused for every device-signed request and over the local network. `--json`
writes `FirstAuthorityResponse`.
