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

Revision 48, November 27, 2026. A present `vericairn-enclave` helper must complete its availability check: one that cannot start, exits unexpectedly, is killed or times out makes `devices create` refuse before it makes a key and `doctor` mark the Secure Enclave default unavailable, while exit 3 and an absent helper keep file custody; a daemon that cannot read its Secure Enclave identity refuses to start and keeps the identity, in the development build.

Revision 47, November 26, 2026. `estate upgrade` exports the live store and each database beside it from its file on disk, holding the clone's writer lock alone, and checks each copy before it writes anything, refusing the upgrade when a copy is incomplete or another runtime has the estate open for writing, in the development build.

Revision 46, November 24, 2026. An agent's `operator done` or `operator dismiss` citing the operator's words writes `--said-at`, which needs `Z` or an explicit offset, in whole seconds UTC, and the time of the recording without it, and `docs pass --before` without `--previous` and `--by` reads the estate's releases read-only, in the development build.

Revision 45, November 24, 2026. `doctor` reports the `vericairn-enclave` helper's version beside the command line's and flags a mismatch, `doctor --json` adds the `helper` probe, and a command line refuses to sign through a helper older than itself, or to make a key beside one that cannot be started, in the development build.

Revision 44, November 22, 2026. Describe 0.9.4's command additions as released commands rather than development builds.

Revision 43, November 21, 2026. Add availability and quiet hours to `communication set` (`--availability`, `--away-until`, `--away-from`, `--quiet-hours`) and their state to `communication show`, `--basis` on `work takeover`, which hands a lapsed step to another principal of its role, the session route of `goal step`, presence leases kept by `mcp serve`, and `presence` events in `events watch`, in the development build.

Revision 42, November 21, 2026. `estate upgrade` says whether the previous release can read the estate after it, refuses a one-way upgrade without `--one-way`, records a restore point and states the way back, in the development build.

Revision 41, November 21, 2026. Add `worker drill`, the timed stop drill, which stops a scratch run and records the time from the stop to containment and to the run's last write as a `stop-drill` verification, in the development build.

Revision 40, November 21, 2026. Add `observers list`, `declare`, `report` and `withdraw`, which declare the automated observers on this machine with their intervals and graces and keep their reports, and the `Observers (this machine):` lines that end human `status`, in the development build.

Revision 39, November 21, 2026. Add `progress target set`, `list`, `remove`, `refresh` and `follow`, which keep the estate's progress at a target such as a pinned GitHub discussion, in the development build.

Revision 38, November 20, 2026. Say that under a `concurrent-paths` policy at `refuse`, `work assign`, `work attempt`, `records new assignment` and `worker run` refuse work that leaves an unmet or unavailable pair, naming each pair and the remedies that apply to the write, in the development build.

Revision 37, November 17, 2026. Add standing agents: `goal propose --kind stewardship --standing <flow>` with `--trigger`, `--period`, `--until`, `--awaiting-review` and `--runs-on`, `goal due`, which lists a standing agent's occurrences as of an explicit time, `--occurrence` and `--now` on `goal step`, which carries one occurrence per command within the agent's caps and asks once for renewal before its until time, `--standing` and `--stop-runs` on `goal pause`, and a standing agent's `goal stop` asking its runs to stop and retiring its work order, in the development build.

Revision 36, November 17, 2026. Add `work overlap`, which lists the active assignments by repository and every overlap of their effective permitted paths, and previews a draft brief's with `--brief`, `--repository` on `work assign`, and the overlaps that `work assign`, `work attempt`, `worker run` and `goal step` report on standard error; an assignment is released only by its own change integrated into its repository's main line, in the development build.

Revision 35, November 16, 2026. Fold every line taken from the runtime to the terminal's width with `runtime:` on every row, make its runs of spaces one space, and show a presence-related command's problems from the runtime the same way, in the development build.

Revision 34, November 16, 2026. Mark every line taken from the runtime with `runtime:`, its control characters shown escaped, name the action and references as a session's terms state them, and refuse `devices presence` over a settings file that does not read, in the development build.

Revision 33, November 16, 2026. Add `devices presence`, which keeps a device owner's presence setting on the machine holding the key, and `devices sessions`, which lists the presence session ledger, and show a presence session's list, the fingerprint computed from its terms and each signature on standard error, in the development build.

Revision 32, November 16, 2026. `records new principal` writes a person's role closing before a revision removing the operator role in an estate that has adopted, and `estate checkpoint --adopt --check` lists records by a person whose operator role was removed since (ID-14), in the development build.

Revision 31, November 16, 2026. `estate first-authority`: from 0.9.4 the estate reads a hand-named self-admission whose signature does not verify as no first authority, as the command's evidence already read it (FIX-134), in the development build.

Revision 30, November 16, 2026. Add `goal decide`, which records the decision of a run's decision step by the principal holding its current assignment, `goal step`'s recording of a rule's decision (route `rule`, outcome `decided`), and the `decision:` and `Decision:` lines of `goal show` and `status`, in the development build.

Revision 29, November 16, 2026. `doctor` reports whether presence sessions may open on this machine and why not, and `doctor --json` adds the `presence` probe, in the development build.

Revision 28, November 15, 2026. Add `goal close`, and `--reason` and `--step-reason` on `goal accept`, which close a run of a flow that will never complete with a reason for each unsettled step, in the development build.

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
From 0.9.4 (FIX-141), a result an acceptance of its work order's current revision was recorded
after, before the estate's first acceptance of a result by an operator, counts as accepted and is
not listed, so `--all` never signs that work again; a result recorded after such an acceptance is
listed ([details](operator-actions.md#close-finished-steps-in-one-action)). From 0.9.4 (OPS-24), a result is listed only when
every current review of it passes; one with a failed review or findings beside a passing review is not. From 0.9.4 (FIX-159), an
estate that reports an error refuses the closing, naming the finding.

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
announced on standard error before the first prompt, with `Signature k of n` at each. From 0.9.4, where presence
sessions are on, up to 16 acceptances are listed by exact hash and signed under one presence check, a list for each 16
([presence sessions on standard error](#presence-sessions-on-standard-error-094)). Exit 0 means success,
1 a selection or record refusal, and 2 invalid arguments or a client failure. The
[operator actions guide](operator-actions.md#close-finished-steps-in-one-action) describes saved
listings, API/SDK counterparts and recovery after interrupted storage.

## `vericairn operator answer` (0.9.4)

From 0.9.4 (OPS-16), the operator answers open asks with their recommendations in one signed act. Without a
selection the command lists every ask waiting on an operator, or on `--by`, each by its exact revision and hash with its
recommendation and that recommendation's SHA-256 first, then its question, the consequence of leaving it unanswered and its
options. It writes nothing.

| Option | Meaning |
|---|---|
| `--select <ask-id>` | Answer that ask with its recommendation; repeat for several asks. |
| `--all` | Answer every ask waiting on `--by`; cannot be combined with `--select`. |
| `--by <operator-id>` | Required with a selection: the operator the asks are put to, a person, acting on their own admitted device. Narrows the listing. |
| `--stake <stake>` | Each decision's stake: `low` (the default), `medium`, `high` or `critical`. |
| `--from <file>` | Use a saved `operator answer --json` listing with a selection; `-` reads standard input. |
| `--json` | Print one typed response. The act is shown on standard error before signing. |
| `--estate <root>`, `-e <root>` | Select the estate. |

Each selected ask gets its own decision, signed by the operator's device and naming the act in its `act` member, with
`decidedAt` the act's time; all are stored together. A changed ask or recommendation, an ask answered, withdrawn or carried in
the meantime, a changed membership with `--all`, an agent or anyone but the operator the asks are put to refuses the whole act
without a decision. Where presence sessions are on, up to 16 decisions are listed by exact hash and signed under one presence
check, a list for each 16 ([presence sessions on standard error](#presence-sessions-on-standard-error-094)). Exit 0 means every
selected ask was answered, 1 a refusal, and 2 invalid arguments or a client failure. The
[operator actions guide](operator-actions.md#answer-open-asks-with-their-recommendations-in-one-act-094) describes the
decisions, the API and SDK counterparts and Remote.

## `vericairn operator act --ready` (0.9.4)

From 0.9.4 (OPS-24), the operator signs every ready item in one reviewed act. With `--ready` alone the command lists every
operator item whose command is ready for `--by`, or the root operator: acceptances of reviewed results, answers with their
recommendations, a goal's acceptance, a release candidate's, a milestone's and a milestone result's acceptance whose exact revision
has only passing reviews, and action requests' closes, each with what its signature records, its exact subject and basis, and an
acceptance's reviews. It writes nothing.

| Option | Meaning |
|---|---|
| `--ready` | Required: list, or act on, the items whose command is ready. |
| `--all` | Sign every listed item except those skipped or dismissed; refuses if the list changes. Cannot be combined with `--select`. |
| `--select <item>` | Sign that item; repeat for several. Every other item waits. |
| `--skip <item>` | With `--all`, leave that item waiting; repeatable. |
| `--dismiss <item>=<reason>` | Dismiss that request with your reason; repeatable. Only a request is dismissed. |
| `--by <operator-id>` | Required to act: the operator, a person, acting on their own admitted device. |
| `--stake <stake>` | Each answer's decision stake: `low` (the default), `medium`, `high` or `critical`. |
| `--from <file>` | The saved `operator act --ready --json` listing you read; `-` reads standard input. Required to act unless you type `yes` in a terminal to the listing shown. |
| `--json` | Print one typed response. The reviewed act is shown on standard error before signing. |
| `--estate <root>`, `-e <root>` | Select the estate. |

A chosen item that changed or is no longer ready, a changed list with `--all` since the listing you read, a dismissal of an item that
is not a request, an act without `--from` and without a terminal, an agent or anyone but the acting operator refuses the whole act
without a record. Where presence sessions are on, the act's records sign under one presence check, under the session action
`operator act`, whose announced list names each, up to 16 to a list, a list for each 16
([presence sessions on standard error](#presence-sessions-on-standard-error-094)). Exit 0 means every chosen record was stored, 1 a
refusal, and 2 invalid arguments or a client failure. See the
[operator actions guide](operator-actions.md#sign-every-ready-item-in-one-reviewed-act-094).

## `vericairn operator fetch` (0.9.4)

From 0.9.4 (OPS-22), `vericairn operator fetch <request-revision> --to <folder>` writes the current request's pinned files, under
their names, read from the estate's objects and checked against their hashes, and its guide, `guide.md`, into the folder, created
when missing. It refuses, writing nothing, when an object is missing or does not match its hash, a name is not a plain file name or
is the guide's, a file it would write exists already, or the revision is superseded. It signs and records nothing. `--json` prints
the request, its hash, the guide's name and each file's name, hash and size. Exit 0 means the kit was written, 1 a refusal, and 2
invalid arguments or a client failure. See [optional requests](operator-requests.md#optional-requests-possible-todos-094).

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
`--setup-variable <name>`, repeatable, passes a named variable besides. From
0.9.4 (FIX-161) a relative executable with a directory part names the
workspace's file, a bare name is found in an absolute directory of `PATH`, and
an executable that can never be started is refused here and reported unreadable
where it was stored earlier. From 0.9.4 (FIX-165), on Windows a `.cmd` or `.bat`
executable runs through the system's `cmd.exe`, and an argument `cmd.exe` cannot
read as data (one holding a percent sign, a line break or another control
character) is refused here and reported unreadable where it was stored earlier.
`--clear-setup` removes it; `--repository` alone
shows it, and `worker configure` with no choices shows every setup command
beside the workspace root. See [Preparing a workspace before the agent
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
flow option, `step.<key>=on|off` for an optional step or `item.<step>.<item>=on|off` for an optional item, and from 0.9.4
(PROC-18) `zone=<IANA time zone>` or `when.<step>=<timing as JSON>`, such as `when.publish={"delay":{"after":"PT2H"}}`, for a
step whose timing the flow does not fix ([Templates](templates.md#flows-092)); `--until <step>` names the step the run ends at. From 0.9.3 (FIX-127), `--subject` reads the subject of the flow resolved with
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

From PROC-18 (0.9.4), a step whose timing contract gates its start shows its due time before it runs: `goal show` prints it as
the step's `due:` line, when and why, its timing and zone, its deadline and a missed-time rule it took, and `status` as each
run's `Due:` lines. With `--json`, `goal show`'s flow gains that step's `due` (`at`, `reason`, `timing`, `zone`, `deadline` and
`missed`), and status's `runs` gain `due`, each named. `goal step` takes no step before it is due; a step that missed its deadline
under its `attention` rule routes as `attention`, whatever its kind, and `goal step` asks the sponsor once. A reserved act at a
time, other than under a `due` time, routes as `request` once what it waits on is done, ahead of its time. See
[runs of a flow](operator-guide.md#runs-of-a-flow-092) and [Templates](templates.md#flows-092).

From FIX-143 (0.9.4), `goal close <goal> --by <id> --reason <text>` closes a run of a flow that will never complete: each step
it has not settled, neither done nor off, is recorded with a reason in a signed goal-status of `closed`, and none of them counts
as done. `--reason` gives every unsettled step that `--step-reason <step>=<reason>`, repeatable, gives none its reason, the step
named by its key or `key[instance]`; a close needs one or the other. Only who decides for the goal, a person, closes its run, and
an agent is refused; every refusal comes at once, before anything is written. `goal accept` takes the same two options and then
closes the run with the acceptance, as one batch. `goal show`, `goal next` and `status` read a closed run with each of those
steps `unsettled` and its reason; the goal reads `closed` until it is accepted or stopped, and is never resumed. See
[closing a run](operator-guide.md#closing-a-run-094).

From PROC-19 (0.9.4), a step whose flow ends it in a decision transition ([Templates](templates.md#decision-transitions-094))
shows its decision: `goal show` prints the step's `decision:` line, its state (`pending`, `held`, `in-review`, `taken`,
`returning` or `attention`), the decider in this run, and once decided its paths, who decided and when, why, and the evidence or,
for a rule, the records it read, each by hash; `status` prints each run's `Decision:` lines. With `--json`, `goal show`'s flow
gains that step's `decision` (`state`, `decider`, `paths`, `decidedBy`, `by`, `byName`, `record`, `reasons`, `evidence`, `at`,
`since`, `review`, `taken` and `rule`), and status's `runs` gain `decisions`, each named. A rule's decision, or the default a missed
deadline takes, routes as `rule`: `goal step` records it, signed by the acting principal, as an observation of kind `flow-decision`
naming the path, the rule that matched and the records it read by hash, answers with the outcome `decided` and the record as
`decision`, and the next `goal step` takes the path. Under `rule-then-role` a round in which no rule held routes as `rule` too:
`goal step` records that (the rule `none`, no path), and the next `goal step` routes the step to its role; `goal decide`, reaching a
round whose kind is not on record, stores that observation first in the same batch as the decision. A step its role decides routes as any step does, to an agent or, assigned and
asked, to a person, and `goal step` never routes one that only a person decides to an agent. `goal decide <goal> --by <id> --step <step> --path <name> --reason <text>` records the decision of the
principal holding the step's assignment as the result of that assignment, signed by its own device: `--path` repeats for several
paths at once where the flow allows, and `--evidence <id or type:id>`, repeatable, pins each record it rests on at its current
hash. It refuses, at once and before anything is written, a step that is not a decision step its role decides in this run, one not
waiting on its decider or decided by its rule, a principal not holding its current assignment, an agent where the decision is a
person's, a path the transition does not offer or several where it takes one, a path that would turn off a step an estate policy
requires or a required reserved step (`flow.decision-protected`, naming the step), blank reasons and unknown evidence. The first
decision of a round that stands is final, and each round takes one kind of decider: a recorded rule decision counts only where
its rules, evaluated again over the records it names, hold as it says, and a deadline's default only once the deadline passed
([Templates](templates.md#decision-transitions-094)). Choosing a path dispatches nothing and performs
nothing reserved: `goal step` takes the steps it makes eligible, and a reserved one still waits on the operator. A path back past
its limit and a missed deadline under the `attention` rule route as `attention`, and `goal step` asks the sponsor once.

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

From 0.9.4 (FIX-145), an upgrade first says whether the release the estate is pinned to before it can still read the estate after
it: `readable`; `one-way`, when the upgrade moves the record format, or adds a manifest field or a kind of record the previous release
predates (the manifest's verified-dependency pin and the `vericairn-release` observation from 0.8.3, the followed line and channel from
0.9.0, the first authority and history checkpoints from 0.9.2); or `unconfirmed`, when the previous pin is a source build whose release
is unknown and the upgrade adds such a thing. `--check` and an applied upgrade print the verdict with each reason, and a one-way upgrade
is refused (`one-way`), with nothing changed, unless `--one-way` confirms it. Before it writes anything, an applied upgrade records a
restore point: the estate repository's checked-out commit (none for a local-only estate) and an export of this machine's local state for
the clone, a consistent copy of its live store and the files beside it, under `restore-points/<id>` beside the live store, with an
inventory of their hashes in `restore-point.json`. The migration record's changes name the restore point's id, commit and inventory
hash and the verdict, never its directory. The output then states the way back to the previous release, step by step with this
machine's paths: stop the runtime, return the estate's files to the commit with their exact bytes (`git -c core.autocrlf=false stash
--include-untracked` before the upgrade is committed, `git -c core.autocrlf=false revert` of its commit after, so that no line
ending changes on Windows), copy the exported state back beside the live store, and run the previous release. With
`--json`, the response adds `return`: `previousPin`, `readability`, `oneWay`, `reasons`, `steps` and, once applied, `restorePoint`
(`id`, `createdAt`, `commit`, `clean`, `directory`, `stateHash`, `files`). A restore point is removed again when the upgrade is not
applied.

```sh
vericairn estate upgrade --to <published-version> --check
vericairn estate upgrade --to <published-version> --by <principal id> --one-way
```

From 0.9.5 (FIX-174), the restore point exports the live store from its file on disk, whichever runtime applies the upgrade: in
0.9.4 the command line's upgrade of a repository-synced estate with no daemon serving it kept no live store, since it opens the
estate read-only and exported an in-memory stand-in, which wrote no file. Every SQLite database beside the live store, such as the
daemon's `events.db` and a local-only estate's `objects.db`, is exported the same way, as one file read through SQLite, never as its
files copied while a runtime holds them open, so the restore point holds no database's `-wal`, `-shm` or `-journal` file. Before the
upgrade writes anything, each copy must exist, pass SQLite's integrity check and hold as many rows in each table as the database it
copies; otherwise the upgrade, one-way or not, is refused (`refused`) with nothing changed, and the message names the database and
what failed. The copies are made while the upgrade holds the clone's writer lock, `live.lock`, alone, as `estate cache rebuild`
does, so no writable session opens meanwhile. While a daemon, an MCP server (`vericairn mcp serve`, as an AI client or a run starts
it) or another command has the estate open for writing, the upgrade is refused with nothing changed, and the message says to run it
through the daemon serving the estate, or after stopping the daemon and closing any MCP server. A change detected while a database is
copied refuses the upgrade too; the check catches some changes by a process that writes without the lock, not all of them
([limitations](limitations-and-platforms.md)). The
way back names each database's `-wal` and `-shm` to remove before the exported state is copied back, and the clone's live store too
when it kept none before the upgrade.

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

From 0.9.4 the daemon raises `presence` events: a session holding work lapsed, the runtime asked about it, or the lapse
is over (see [events and presence](events-and-presence.md)). `due` stays reserved for reminders, which are planned.

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
| `llms <set>` (0.9.4) | Writes the set's `llms.txt` and `llms-full.txt` from its documents, in its manifest's order, when the set declares them; a set that declares neither gets none. Refuses a frozen set. |
| `point-root <set>` (0.9.4) | Points the repository's root `llms.txt` at a frozen set under `docs/releases` that declares its llms files, unless the root already points at a later version. |
| `prepare --out <set> --documents <file>` | Runs `assemble`, `docs notes` when the set has none (`--estate`, `--from`, `--since`), `insert` (`--security`, `--security-hash`), from 0.9.4 `llms`, then `compare`, `freeze`, from 0.9.4 `point-root`, and `index` (`--index-paragraph`), stopping at the first refusal. |

From 0.9.4 (DOCS-38) every set declares `llms.txt` and `llms-full.txt` in `docs/releases/documents.json`. `llms.txt`
names the set and links each document by its title, relative to the set, so it reads the same in the repository and in
the release's documentation archive; `llms-full.txt` holds every document's bytes after a line naming its file, a document
with the same bytes as an earlier one named once. `vericairn docs check` refuses a set that declares them and lacks
either or holds one that differs from what its documents render (`docs.llms`). `point-root` writes the one file outside
the sets, the repository's root `llms.txt`.

`--json` prints one JSON object per step. Exit 0 means the step is done or was already done, 1 that the set is incomplete
(`freeze`) or differs from the build (`compare` within `prepare`), and 2 a refusal. See the [operator's guide](operator-guide.md#preparing-a-set-with-the-build-tooling-092).

## `vericairn security-reports` (0.9.2)

Reads the security advisories of the repositories a product's `securityReports` declaration names, drafts and private
reports included, and records the operator's triage of one observed revision (OPS-11). While a declared repository cannot be
read completely, or an advisory holds, `release submit` and its `--check` refuse. A report (an advisory in `triage` or `draft`
state, or an unknown state) holds until a triage names its current revision, and a closed or withdrawn advisory until a triage
names it at any revision; a published advisory holds nothing and needs no triage. Both commands are the
operator's alone; any other principal is refused with the same answer whatever exists. See the
[operator's guide](operator-guide.md#security-reports-hold-releases-092).

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
the unsigned records that count for nothing today and stay so, records signed by a device revoked now, from 0.9.4 records by a
person whose operator role was removed since, each with a `NOTE` naming the roles removed (no role closing bounds such a removal
written before adoption; the list is advisory and the plan hash does not cover it), records that read
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
without `--recovery`, `devices rotate`, `records new authority --supersedes` with a revision that names another grantee,
drops a scope, reserves another action, adds a condition, adds or shortens an expiry or changes `bounds`, and from 0.9.4
`records new principal --supersedes` with a revision that takes the operator role away from a person, dropping it or describing
another kind of principal, whose closing, `closes.kind` `role`, is signed by the root operator or another standing operator (ID-14);
removing any other role writes no closing. Each is stored after
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
- preparation wording, the phrases only a set still being prepared uses: notes called a placeholder or left unrendered, a set
  said to be in preparation or before its build, a note of what the builder still has to do, and the two capital-letter markers
  of unfinished work.

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
| `git`, `dotnet` | `present`, or `missing` when no absolute directory of `PATH` holds the tool; from 0.9.4 (FIX-161) a copy in the current directory or a relative `PATH` entry is not used |
| `estate` | `present`, or `missing` for a path that is not an estate root; `value` is the path either way |
| `enclave` | `present` or `unavailable` on macOS, with the reason in `detail`; `not-applicable` elsewhere |
| `search` | `present`, or `unavailable` with the reason |
| `daemon` | `present` with its process, `missing` when commands run the embedded runtime, `not-applicable` outside an estate |
| `presence` | From 0.9.4: on macOS, `present` with `sessions` when this release build's signed helper may open presence sessions on this macOS version, or `unavailable` with `per-signature` and the reason in `detail`; `not-applicable` elsewhere |
| `helper` | From 0.9.5: on macOS, `present` with the helper's version when the command line signs through it, `unavailable` when it refuses the helper (its version, or `null` for a helper that reports none), `missing` without a helper; `detail` names the helper's version beside the command line's and the helper version it expects, flagged `mismatch`; `not-applicable` elsewhere |

From 0.9.4 (ID-16) the report adds a `presence` line on macOS, after `enclave`: whether a presence check may cover an operator
action's announced list here and, when not, why (a development build, a macOS version this release has not qualified, or a helper
that does not meet the release requirement), then the estate charter's `policies.presence`, whether device owners' settings are kept
on this machine, and, in a release build, whether this process's .NET diagnostics channel is on. Every signature with a presence key
asks its own check wherever sessions are off.

From 0.9.5 (FIX-169) the report adds a `helper` line on macOS, after `enclave`: the `vericairn-enclave` helper's version beside the
command line's and the helper version this command line expects, as the helper answers `vericairn-enclave version`. A helper from
before 0.9.5 answers with its usage line instead, so the line says it reports no version, and that it comes from a release before 0.9.4
when it serves no `sign-record` either. The line is flagged `mismatch` when the helper is older than the version expected, newer, or
lacks a command the command line calls. Before its first signature through the helper, and before it makes a Secure Enclave key, every
command asks the same question and refuses an older helper, or one without `sign-record`, before anything is signed: the refusal names
the helper's version, the command line's and the helper version it needs, says that the helper and the command line come from the same
release archive, and ends "Nothing was signed."

From 0.9.5 (FIX-175), a present helper must complete its availability check. If it cannot start, exits unexpectedly, is killed
or times out, `devices create` refuses before making a key. Exit 3 still reports that the Secure Enclave is unavailable, and
a Mac with no helper or with that result uses file custody by default. `doctor` reports the helper failure, and its `custody`
line marks the Secure Enclave default unavailable until the helper from this command line's release archive is beside it.
The diagnostics and refusal omit the helper's path. If a daemon cannot read its held Secure Enclave identity because the helper
fails, it refuses to start with the same remedy and preserves the identity so paired devices keep their pin.

```text
helper    vericairn-enclave reports no version and serves no sign-record, so it comes from a release before 0.9.4; vericairn 0.9.5+<commit> expects vericairn-enclave 0.9.5: mismatch, so Secure Enclave signatures are refused until the vericairn-enclave from this command line's release archive is beside it
```

```sh
vericairn --json doctor
```

The text report is unchanged, and the release check's disclosure and notices stay on standard error with or without `--json`.
`version --json` writes `{"version": "..."}`. `records describe --json` writes the type list (`reads`, `writesAtMost`, `types` with
`name`, `sinceFormat` and `summary`, and `reserved`) or one type with its `fields`. From 0.9.4 (DOCS-46) a field of fixed members, such as an assignment's `brief`, adds `members`, each with `name`, `kind`, `required`, `sinceFormat` and `description`, `emptyAllowed` for an array, `limits` where one applies, and its own `members`; the text output lists them under the field. `records hash --json` writes `{"hash": "..."}`,
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
lists how many prompts the release and acceptance steps ask for. From 0.9.4 each prompt's line ends with the signed record's short
hash, the first twelve hexadecimal digits of its content hash, which the helper also shows in its dialog's label, and
every one of these lines begins with `runtime:`: its words are the runtime's, and the mark keeps them from passing for a line the
command line derived itself. Control characters and escape sequences in them are shown escaped, never passed to the terminal, each run
of spaces is one space, and a line longer than the terminal (80 columns when standard error goes to a file or a pipe, or its width
cannot be read) is folded into rows that each begin with `runtime:`, so no soft wrap starts a row of the runtime's words without the
mark. A problem the runtime reports to a command whose action asks for presence (`operator close`, `operator record`, `operator
unfinished`, `changes`, `release submit`, `observe` and `withdraw`, `decisions new`, `docs pass`, `work move-plan` and
`goal step`) is shown the same way.

## Presence sessions on standard error (0.9.4)

From 0.9.4 (CODE-80) an operator action that asks one presence check for a list of its signatures, a presence session, is shown on
standard error from the list's terms, the exact text the runtime hands the `vericairn-enclave` helper. The command line checks the
terms as the helper does, names the action as they do, counts them and summarizes each entry itself, with the recorded records a kind
entry may supersede or observe. Every word of the runtime's (its description of each entry and what it changes, its own name for the
action where that differs from the terms', a renewal's reason, each signature's line and how many were signed) is on a line that begins
with `runtime:`, with control characters, escape sequences and line breaks inside a field shown escaped, runs of spaces made one space and
long lines folded to the terminal with the mark on every row, since only the terms are what the helper enforces; row by row, a line
without the mark is one the command line derived itself. As a terminal wide enough to fold nothing shows it:

```text
operator close asks for your presence once, for one presence session of 2 signatures in at most 300 s, covering only this list, read here from its terms:
  1. acceptance by exact hash 5174cd67ab7d
runtime:   describes entry 1: acceptance STEP-1: accepts result <result-id> at its exact hash
runtime:   says entry 1 changes: adds an acceptance about STEP-1 on the basis of 1 review
  2. acceptance by exact hash a0adefb51eff
runtime:   describes entry 2: acceptance STEP-2: accepts result <result-id> at its exact hash
runtime:   says entry 2 changes: adds an acceptance about STEP-2 on the basis of 1 review
  In all, counted here from the terms: 2 signatures: 2 acceptance records by exact hash.
Anything outside this list asks again.
Presence check for operator close: the system dialog should show fingerprint T937-DZ2K-TZN0, computed here from the list above and the check's nonce. Approve it once, only if the fingerprints match and the list is what you asked for; it covers 2 signatures in at most 300 s.
runtime: Signature 1 of 2, under the session's check: acceptance STEP-1: accepts result <result-id> at its exact hash (5174cd67ab7d)
  Its short hash is entry 1 of the list above, by exact hash.
runtime: Signature 2 of 2, under the session's check: acceptance STEP-2: accepts result <result-id> at its exact hash (a0adefb51eff)
  Its short hash is entry 2 of the list above, by exact hash.
runtime: Signed 2 of 2 with one presence check.
```

The fingerprint is computed by the command line from the helper's nonce and the list's terms, as the helper computes the one it writes
into the dialog, and only for a list the command line read before the nonce. A list that reached it only with the nonce or after it, or
that the helper would refuse, gets a warning in place of a fingerprint: decline the dialog unless you asked for exactly that action. A
notice the command line cannot check, such as a check without its session or a list without its terms, is shown whole as the runtime's,
after a warning. A renewal is shown the same way, with the runtime's reason on a `runtime:` line, before its own check; a session that
ends early, at a screen lock, sleep or its limit, says so on a `runtime:` line. Before it hands terms to the helper the runtime waits up
to two seconds until every client following the notices has read them, so each can show the fingerprint. Where no daemon serves the estate the command line runs the runtime in its own process, so what it prints is not
independent of that runtime; the dialog's own text is. Sessions open only with a release build's signed helper on a macOS version the
release lists as qualified, which `vericairn doctor` reports; elsewhere every signature keeps its own check, shown as above, as up to 0.9.3.
The [operator's guide](operator-guide.md#presence-prompts-announced-and-counted-093) states the guarantee and lists each step's checks.

## `vericairn devices presence` (0.9.4)

From 0.9.4 (CODE-80), `vericairn devices presence <device>` keeps the presence setting of a person's device on the machine that holds
its key: `--per-signature` makes every signature with the key ask its own presence check, as before 0.9.4, and `--per-action` returns
to the default, where an operator action may ask one check for the list it announces, optionally with `--limit <seconds>`, a session
shorter than 300 seconds and not under 60. Without a choice the command shows the setting:

```sh
vericairn devices presence <device> --per-signature
vericairn devices presence <device> --per-action --limit 120
vericairn devices presence <device>
vericairn --json devices presence <device>
```

| Option | Meaning |
| --- | --- |
| `--per-signature` | Every signature asks its own check. |
| `--per-action` | An operator action may ask one check for its announced list (the default). |
| `--limit <seconds>` | With `--per-action`, the longest a session lasts after its check: 60 to 300. |

The setting is kept owner-only in `presence.json` under the Vericairn home, never in a record, an export or a support bundle, and only
for a device of the estate whose key this machine holds; the command refuses another device, both choices at once, a limit outside 60
to 300 seconds and a limit with `--per-signature`, writing nothing. While `presence.json` does not read as its format, every device on
the machine counts as per-signature, and the command refuses to write over it, naming the file: repair it, or remove it to return every
device to the default, then set each device again. It also shows the estate charter's `policies.presence`, since the
stricter of the two wins: under a charter that says `per-signature`, every signature asks its own check whatever the owner's setting.
`--json` writes `device`, `presence`, `limitSeconds`, `charter`, `effective`, `required` (whether the device's signatures need its
owner's presence at all) and `problem` (why a damaged settings file counts as per-signature). Neither setting is enforced by the
hardware: a process of the same operating-system user can change the file.

## `vericairn devices sessions` (0.9.4)

From 0.9.4 (CODE-80), `vericairn devices sessions [<device>]` lists the presence sessions this machine's runtime opened or tried to
open for the estate, oldest first, from its ledger, `presence-sessions.jsonl` beside the estate's live store; no daemon is needed. Each
line gives when the list was announced, the session, the action, the device, the limit, the fingerprint (or that no check was asked),
when it opened, how many records were signed under it and when and why it ended; below it, a renewal's reason, each signed record's
type, id and hash, and each refusal with its code. `--json` writes each session with its `terms`, `nonce`, `fingerprint`, `openedAt`,
`signed` and `refused` lists, `ended` reason and the rest. The ledger is owner-only machine state, a diagnostic that a process of the
same operating-system user could alter; it is never a record, and no export or support bundle carries it.

## `vericairn devices reach` (0.9.5)

```sh
vericairn devices reach <device> --since 2026-11-16T10:00:00Z
vericairn devices reach <device> --since 2026-11-16T10:00:00Z --json
```

The device may be named by its identity or a revision id. The report includes available signatures that name a revision of
the device and verify under a key in its verified revision history. This includes superseded records and records that fail an
admission or history check. Use seconds and `Z` or an explicit UTC offset for `--since`. The cutoff is inclusive.

- The record's stated time is its author's claim. A matching local ledger signing time is what this machine logged for the
  exact device, record id and hash. A time at or after the cutoff in either source qualifies the row.
- The first witness is the earliest-dated counting checkpoint whose inventory lists the exact id and hash in this copy. A
  first witness at or after the cutoff also qualifies the row, even for a backdated record. The checkpoint time is the time
  stated by its signer for that witness. It establishes that the checkpoint lists the record then, with no exact signing time.
- A verified signature outside an applicable signed device closing also qualifies when the closing's stated time is at or
  after the cutoff. The closing and the checkpoints it follows did not witness that version. Older records can arrive from
  another copy after a closing. A backdated record with neither checkpoint nor ledger evidence can fall outside the report.

The JSON report has `device` (the stable device id), `since` (the cutoff in UTC with `Z`), `records`, `sessions` and
`revocations`. `presenceProblem` and `journalProblem` appear when the corresponding local diagnostic is unavailable.
Each `records` row has `id`, `hash`, `type`, `path`, `at`, `source`, `sessions` (linked session ids) and `ledgerSignedAt`
(the matching times in the ledger). For `source: record-signature`, `at` is the record's stated time. For `source: presence-ledger`,
`at` is the first matching local signing time at or after the cutoff, `path` is null and the exact signed record could not be
verified here. `firstWitness` and `outsideClosing`, where present, each give the checkpoint `id`, `hash` and stated `at`.
`sessions` contains the linked entries and any sessions announced at or after the cutoff. Its `announcedAt`, `openedAt`,
`endedAt` and each `signed` or `refused` entry's `at` are local ledger times. Each session carries the announced terms.

Each `revocations` row has `id`, `hash`, `at` (the record's stated time), `revokedAt` (its stated effective time), `counts`,
`current` and `observations`. The two booleans describe this copy's history and current admission. Each observation gives
`kind`, `identity`, `evidence` and the observer's own clock time `at`. `record-read` names this runtime process. `journal-found`
and `journal-written` name the epoch of this machine's daemon journal when it retains a matching event. `devices revoke`
reports `written` for its runtime process and, when a daemon handles the write, `write-served` for its journal epoch. With
`--json` the write includes these as `revocationReach` alongside the written record's id and hash. The presence ledger and
journal are owner-only diagnostics. Initial journal baselines and expired events can leave no server observation. Absence
leaves the observer's status unknown. Run the command against another machine's copy for evidence it keeps there.

## `vericairn work overlap` (0.9.4)

From 0.9.4 (CODE-73), before two assignments run at once in one repository the runtime compares their effective permitted
paths: a brief's interpretable patterns (`permittedChanges` entries with the `path:` prefix or no whitespace) less its
`excludedChanges`, and in the estate's own repository less `records/` and `objects/`, where every write is a new file named by its
id or hash. Prose grants nothing and is not compared.

```sh
vericairn work overlap
vericairn work overlap --repository <resource id>
vericairn work overlap --brief brief.json --repository estate
vericairn work overlap --json
```

It lists the active assignments by repository, each with its patterns, its exclusions, its `after` and why it is active, and every
overlap of two of them: their patterns, their witnesses, the resolution and each `vericairn.estate/concurrent-paths` policy whose
selection the overlap meets, with its level. `--repository` shows one repository's, a repository resource's id or `estate`;
`--brief <file>` previews a draft brief, as `work assign` takes it, against the active assignments before it is recorded, in the
repository its brief or `--repository` names. The draft is first held to what the write boundary holds a recorded brief to: each
`permittedChanges` and `excludedChanges` entry 1 to 2,000 characters, each exclusion a pattern, and at most 20 distinct assignment
ids in `after`; a draft that falls short answers with `problem` and is compared with nothing. It records nothing, and the runtime
compares a snapshot of the records without holding its other operations. `--json` writes the view as `vericairn.overlaps/1`:
`assignments`, each with `assignment`, `key`, `repository`, `repositoryFrom` (`brief`, `run` or `unknown`), `patterns`, `excluded`,
`leftOut`, `after`, `active`, `state`, `labels`, `result` and, for a result that names one, `change` with its `integration`
(`merged`, `reachable`, `not-integrated`, `mismatched` or `unknown`), the `mainLine` it was compared with, the `problem` that makes
it `mismatched`, and its `paths`, each `path` with its `change` (`added`, `modified` or `removed`), read from this machine's
registered clone by name without rename detection, with the clone's own program settings overridden (FIX-147), or
`pathsUnavailable` when they cannot be read there, for example when a commit is missing from that clone or no clone of the
repository is registered on this machine; `overlaps`, each with
`first` and `second`, `repository`, `determined`,
`witnesses`, `witnessKind` (`tracked` or `possible`), `branch`, `resolution` (`none` or `sequence`), `held`, `policies` and
`labels`; and, for a preview, `draft`.

An assignment is active while it is current and its work order is neither accepted nor retired, until it is released: its current
result is abandoned; it is completed and names a `change` that is integrated, shown by a `change-merge` operation observed at the
change's head, or by the head being reachable from the main line of this machine's registered clone (origin's `HEAD`, else `main`,
else `master`, as a local branch), labeled as observed here; it is completed and its brief holds no path pattern, whatever change
it names; or it is completed and was recorded before the overlap check, labeled, since nothing recorded its integration. A change
releases only its own assignment's work: it is in the repository the assignment's brief names, or for an older brief the one its
agent runs named, and where the clone shows them, its base is a proper ancestor of its head and one of the paths it changed is in
the assignment's effective permitted paths. A head reachable from the main line needs the clone to show both; a head a
`change-merge` operation merged stands as the records show it where no clone shows otherwise, so `status` and `progress`, which
read the records alone, can show released what `work overlap` on a machine with the clone shows `mismatched`. A change in another
repository, with the same commit as base and head, whose base is no ancestor of its head in the clone, or that changed none of the
assignment's paths is `mismatched` and keeps the assignment active, and the branch a result names as its `target` never shows
integration: only the main line does, and never a branch an agent run worked on. A range on the main line that touches one of the
assignment's paths is taken as the result states it, a signed claim of its contributor that may be mistaken as well as false: a
result naming its fork point and the main line's tip while its own work is still unmerged releases the assignment whenever
another change in that range touched one of its paths. From 0.9.4 a completed result whose brief holds a path pattern and that
names no `change` keeps its assignment active until acceptance. Before the check's first record in an estate (the first brief
naming a repository, exclusions or `after`, or result naming a change), results count as recorded before it by the times the
records state, which the command line and the API take from the caller. An assignment whose brief names no repository is compared
under the one its agent runs named, or, with none, as possibly in any repository, which the view labels. A reading asks the clone
only about completed results of work that is still open, once for each change, so its cost grows with the open assignments whose
completed results name distinct changes, and `work assign` and `work attempt` pay it while the runtime holds its other operations.

Two active assignments overlap when some valid path, by the worker's rules for a repository-relative path with nonempty components
and no leading or trailing slash, matches a pattern of each and no exclusion of either. The comparison is exact over the patterns'
grammar: a search of their automata, bounded at 100,000 states, the patterns' own automata included, and at 5,000,000 steps of
work, each character a state is stepped by and each move of an automaton followed, that reports a pair it cannot decide within
either bound as overlapping and undetermined. A state is stepped only by the characters its automata name there and one
representative of the rest, so characters the patterns name elsewhere add no work. A shortest path longer than 2,000 characters
means no valid overlap. Each overlap shows up to 20 tracked paths in the intersection at the main line of the registered clone,
sought within a bound of their own, else the shortest path either could create, labeled so. Matching is ordinal, as the worker's
is; on a case-insensitive filesystem two paths that differ only in case name one file, and the view says so for a clone there.

An overlap is resolved by sequence, the later assignment's brief naming the earlier in `after`, or by one owner, every other
party's brief excluding the shared paths in `excludedChanges`. Adding an exclusion or an `after` grants less and needs no
amendment; a revision that changes an assignment's `repository`, `permittedChanges`, `excludedChanges` or `after` is refused while
a run of it is in flight, so that the run keeps working where admission checked it and is compared there. `goal step` records the
later of two assignments whose overlap a `concurrent-paths` policy at `refuse` holds with `after` naming the earlier, and for an
agent's step records that assignment alone, so that its run starts from a later `goal step` once the earlier is released; it
reports every other overlap and starts the work. `goal next` shows the overlaps of the goal's work and those a new assignment of
its next step would have. `worker run`, `goal step` and a new attempt refuse while an assignment named in `after` is not released,
and a run is refused in another repository than its brief names, so register a repository with `sources adopt` before you assign
work in it.

From 0.9.4 (POLICY-08), under a [`concurrent-paths` policy](policies.md#concurrent-paths-094) at `refuse`, `work assign`,
`records new assignment`, `work attempt` and `worker run` refuse work that leaves a pair of active assignments unresolved within
the policy's selection, `unmet` or `unavailable`, before writing anything: the refusal (`assignment.concurrent-paths`, or
`attempt.concurrent-paths` for an attempt) names each pair, its policy and rule and the remedies that apply to the write.
A new assignment or a revision is told to name the earlier assignment in `after` or to narrow its paths with `excludedChanges`, so
that it is recorded resolved. `work attempt` and `worker run`, on assignments already recorded, are told to record the later with
`after` naming the earlier or to exclude the shared paths from every brief but the owner's, or, for an unmet pair, that the operator
may grant an exception for the exact pair of assignment revisions, which lets work on it start: an exception pins revisions that
already exist, so it is offered only for recorded pairs. An unavailable pair is resolved by naming each brief's repository or
narrowing the patterns, never excepted. A `concurrent-paths` finding never holds a release submission.

`work assign --repository <resource id or estate>` names the repository a brief with a path pattern changes when the brief names
none; without it, the estate's own repository fills it when the estate names no repository resource, and otherwise the write is
refused with the candidates. `work assign`, `work attempt`, `records new assignment`, `worker run` and `goal step` print each
overlap of the assignment with the active assignments on standard error, and a completed result whose brief holds a path pattern
prints the write boundary's warning there when the records alone show it keeps its assignment active: it names no change, or a
change in another repository, with the same commit as base and head, or for an older brief that names no repository and whose
runs named none. A change only a clone shows `mismatched`, such as one
whose base is no ancestor of its head or whose paths are none of the assignment's, is written without a warning and shown so by
`work overlap`. The record's own output, and its JSON, are unchanged. `status` and `progress` show the open overlaps under the work
from the records alone, and their canonical JSON gains `overlaps` while one is open. The same view is `GET /v1/overlaps` and
`POST /v1/overlaps/preview`, the SDK's `WorkOverlap` and `WorkOverlapPreview`, and the MCP read tool `work_overlap`
([MCP](mcp.md#tool-annotations-and-definitions-094)).

## Standing agents: `goal due` and `goal step --occurrence` (0.9.4)

From 0.9.4 (GOAL-07), a standing agent is a stewardship goal whose plan carries `standing`: a flow that declares its trigger kinds
and one routed work step, the subject it binds, its triggers, its caps per UTC-anchored period, an until time and, optionally, the
runtime that dispatches it unattended. Each occurrence a trigger makes due is carried as one assignment of the goal's one ordinary
work order, naming the occurrence. A flow, a trigger and a proposal grant nothing; the sponsor's activation grants the budget.

```sh
vericairn goal propose --by <sponsor> --kind stewardship --standing example/researcher@1.0.0 --work-order RES-1 \
  --title "Research the widget" --objective "Keep researching the widget." --done '{"type":"work-order","fields":{"key":"RESEARCHED"}}' \
  --subject topic="the widget" --budget wallMinutes=30 --budget attemptsPerStep=2 \
  --trigger '{"key":"monthly","kind":"schedule","unit":"months","every":1,"firstAt":"2026-12-01T00:00:00Z"}' \
  --trigger '{"key":"asked","kind":"request"}' --period runs=4 --period wallMinutes=120
vericairn goal due <goal>
vericairn goal due <goal> --as-of 2027-01-15T00:00:00Z --json
vericairn goal step <goal> --by <sponsor> --repository <repository id>
vericairn goal step <goal> --by <sponsor> --repository <repository id> --occurrence <key> --now 2027-01-15T00:00:00Z
vericairn goal step <goal> --by <sponsor> --repository <repository id> --occurrence request
```

A trigger has its own `key` and a `kind`: `schedule` (`unit` days or months, `every`, a UTC `firstAt`; a day is 86,400 seconds, a
month keeps the anchor's day or the last day of a shorter month, and ticks missed while nothing carried them coalesce into the
latest), `duty` (`policy` and `rule` of a recurring duty; each occurrence the policy evaluation owes stays due until the duty's own
evidence discharges it, and a trigger never discharges a duty), `event` (`recordType`, optional string `fields` and optional
`roles`; each such record recorded after the trigger was activated, never one of the agent's own, and an agent's record only for a
trigger naming that agent's standing role, from an agent no agent's records trigger), `condition` (`record`, a record predicate as
`--done` takes it; once for each new set of records it reads while it fails) or `request` (an occurrence on demand, `--occurrence
request`, keyed by its time and requester). `--period` caps what the runs started in each UTC-anchored period spend together:
`unit=months` (calendar months, the default) or `unit=days` (days counted from 1970-01-01), `every`, `runs` and `wallMinutes`, and
`tokens` and `currency` where the budget caps them; the budget is then each run's caps. `--until` is when the agent returns to its
sponsor, thirty days from now when omitted. The proposal prints what `goal due` shows.

An occurrence's key is the goal's identity, the trigger's key, the trigger's revision (the earliest goal revision from which its
declaration is unchanged, so a renewal that keeps a trigger keeps its keys) and the item; a duty trigger's key is the duty
occurrence's own identity, so two agents never both carry one deadline. `goal due` evaluates the triggers from the records at
`--as-of`, this runtime's clock when omitted, and lists the agent's state (`active`, `paused`, `lapsed`, `held`, `unavailable`,
`stopped` or the goal's status), each trigger and when it is next due, the period's caps and spend, the runs in flight and the
consecutive failed runs, and each occurrence with its key and state: `due`; `deferred`, with its reason; `running`; `assigned` to a
person; `settled`, its result awaiting review; `disposed`, its result reviewed; or `held`, its attempts spent. With `--json` it
writes `vericairn.due/1`. It records nothing.

`goal step` on a standing agent evaluates the triggers again at `--now`, never later than the runtime's clock, and carries one
occurrence: the one `--occurrence` names, a request made now, or the oldest due one. Its assignment names the occurrence's key,
trigger, revision and item, and its brief carries the flow's role brief, the records that made it due quoted among its sources as
untrusted content, the goal's constraints, the flow's never lines and every reserved effect; an agent's run works on a branch of
its own for the occurrence. Whatever `--now` names, an agent past its until time at the runtime's clock dispatches nothing.

A key holds one assignment whoever records it, `records new assignment` included: a second one is refused (`standing.duplicate`)
unless a person takes the occurrence over with `work takeover`, and a revision of an assignment keeps its occurrence exactly, so it
never frees the key. Two clones that each recorded one leave both flagged `standing.duplicate`, which `status` lists among its
reconcile tasks: record a reconciliation about either one that keeps one and withdraws the other (`records new reconciliation`, by
the operator or under `local-engineering`). A reconciliation settles only such a duplicate, two or more assignments holding the key
that are not stranded, and keeps one of them; a result on any assignment of the key, a withdrawn one included, still settles it. A
new occurrence is carried only as its trigger makes it, while its agent is active and before its until time at the assignment's own
time: a schedule's latest due interval (missed ticks coalesce), an event's record of the trigger's type and filters recorded after
the trigger was activated, never the agent's own, a condition failing over the records its item names, a duty occurrence's exact
identity with its due time and deadline, and a request by its own requester. A settled occurrence, its result completed or partial
and not returned by a review, runs again only when a review returns its result, on the assignment that holds it; a blocked or
abandoned result settles nothing, and its assignment goes on within its attempts.

Every assignment of the standing work order names an occurrence. Nothing is dispatched while the goal is not active or past its
until time; when a period's cap is reached its occurrences defer and `goal step` asks the sponsor once in the period; while results
awaiting review reach the plan's `awaitingReview` (`--awaiting-review`, three when unset) dispatch defers; while the runs in flight
fill the budget's concurrency (one when it states none) occurrences wait; and after the goal's `limits.consecutiveFailures` failed
runs in a row (three when it sets none) `goal step` asks the sponsor once and dispatches nothing until a decision answers it. Each
settled run counts once, in the order the runs settled: it succeeded when it settled as an observed success whose settlement names
the completed or partial result its agent recorded during the run; otherwise it failed when a blocked or abandoned result was
recorded on its assignment after it started and before the assignment's next run started, whatever a later result or a correction
says, or when it settled as a known failure, unless its sponsor (when a person) or an operator asked it to stop (`--stop-runs`,
`goal stop`, `worker stop`; a stop by anyone else, the dispatcher that started it included, counts); any other run, one whose
harness exited cleanly with no result included, counts for nothing. A run's intent is refused at the record, whatever
route starts it, for a run on no occurrence or on a settled one, past the until time, during the failure pause, past the period's
runs or while results awaiting review reach their cap, each judged by the records before the intent. A second standing agent of one
role over one subject is never activated, nor one past its until time.

From 0.9.4 (FIX-155), no agent ends its own failure pause by any record it writes, whatever date the record claims. Two events
end failures, and only these. A success ends the failures before it while they are fewer than the limit: a run the runtime
observed succeed, whose intent and settlement a principal not tied to the goal recorded (tied as a question's answer reads it,
below), and whose settlement names the completed or partial result its agent recorded during the run on an attempt of that run, as
the worker names the one it sees at the run's exit. Once the failures reach the limit, a success ends nothing, a run already in
flight when they did included, and only the answer to the pause's ask ends them: a decision of the person it was put to, or one a
principal not tied to the goal records for that person, after which no run settled before the ask, or before that answer when it
came first, counts. A run whose harness exits cleanly with no result counts for nothing, so it leaves the failures as they were and
nothing to review. A completed or partial result, and the attempt it closes, is a claim another principal reviews, and it never
makes a run succeed by itself, whether recorded with `work result`, `records new` or by hand: only a settlement names it, and
nothing written after the settlement, whatever date it claims, so after three failed runs the carrying agent's own attempt and
completed result leave the pause held and the failures at three. A run settled from the worker's retained observation, or by hand,
names no result unless its writer names one. A run is read from all of its records, every revision on every branch, whatever a
fork, the order of identifiers or a reconciliation makes its current revision. Where a record that no agent tied to the goal wrote
settles or cancels the run, the records tied agents wrote count for nothing: their revisions and divergent records take back no
failure and turn no success into a failure. From 0.9.4 (FIX-156) the run is then decided by its latest untied records: each record
no tied agent wrote that no other such record follows on its own chain of revisions, however many records a tied agent put between
them. On one chain that is the latest untied record, so an untied principal's correction, such as the operator's, written on the
settlement it corrects counts as written (when it leaves the run canceled or unsettled, the run counts for nothing). Where such
records on different branches read differently, the reading least favorable to the agent decides, a failure over nothing and
nothing over a success; neither a record's place in the run's history nor its date ranks one over another. Only where no record by an
untied principal settles or cancels the run does a known-failure settlement a tied agent wrote count, as a failure, as for an agent
that steps its own goal. A run whose intent or settlement an agent tied to the goal recorded never succeeds, a stop such an agent
records names no one who decides for the goal, and its runs never take a result off a run another principal recorded.

From 0.9.4 (FIX-156), a record carrying another record's identifier is never read in its place, whatever its date or path: the estate
reads neither of them, nor any record of their line (`record.duplicate-id`, `record.duplicate-line`), and refuses every write until
all but one is removed. Such records count against the agent: a run of the goal that cannot be read counts as failed, a run an
unread record names counts as its own records decide it or as failed, an output that cannot be read stays open, and an answer that
cannot be read answers nothing. An unread device, admission, principal or grant leaves the records resting on it counting for
nothing, so while any record is unread, a record that does not count is read against the agent and never for it: its blocked
results and runs count toward failures and its outputs stay open. Every standing agent then reads `held` in `goal due`, naming
the unread record, and `goal step` dispatches nothing of it ([record format 3](record-format-v3.md#identifiers-more-than-one-record-carries-094)).

From 0.9.4 (FIX-159), no record counts because the record that would refuse it is unread: while records are unread, a record counts
only when it counts with them left out and with them read each way they can be, so an unsigned record in the operator's name stays
refused when the charter's requirement is unread, and a closed device's backdated records stay refused when the closing is unread
(`record.duplicate-refused`; past 16 readings no record counts, `record.duplicate-unsettled`). A goal whose own line is unread is not
reported missing: `goal due` says its state, failures and open outputs are not established, naming the duplicate, and exits 1. While records are unread `goal step` refuses before it records or dispatches,
naming the finding, and an agent run's intent is refused for the hold whatever the failures read. `release submit`, every command
that signs a record or a checkpoint, and `worker run` refuse the same way, and so do every goal's `goal step`, whatever its route, and
`changes push`, `open`, `merge` and `observe`, naming the finding; while the estate reports any other error, `release submit`, `worker
run`, `goal step` and the `changes` commands refuse too, and a record is written only when it clears every error, as before.

```sh
vericairn goal pause --standing --by <sponsor> [--stop-runs]
vericairn goal pause <goal> --by <sponsor> --stop-runs
vericairn goal stop <goal> --by <sponsor>
```

`goal pause --standing` pauses every active standing agent the acting principal may pause and names each, and `--stop-runs` also
asks each run of a paused goal in flight to stop; on resume, missed ticks coalesce and duty occurrences stay owed. `goal stop` on a
standing agent records it abandoned, shown as stopped, asks each of its runs in flight to stop and retires its work order. Within
three days of its until time, or half of its term when that is shorter, `goal step` asks the sponsor once for the revision, before
carrying anything, whether to renew it, with the period's runs and minutes, its occurrences by state and the current evaluation;
a renewal is a revision with a later until time, activated again, and without one the agent lapses.

`goal next` routes each due occurrence by the role of the flow's work step; `goal show`, `status` and `progress` show each agent's
state, next occurrence, occurrences by state and period spend, and canonical status and progress JSON gain `standingAgents` while
one exists, derived as of the estate's newest record, so an agent past its until time shows active there until a later record is
written, as a live read shows at once. The same view is `GET /v1/goals/{id}/due`, the SDK's `GoalDue`, and the MCP read tool
`goal_due`; `GoalStepRequest` gains `occurrence` and `now`, and `GoalActionRequest` gains `stopRuns`. Community carries one
occurrence per command, by hand or by the owner's script; unattended dispatch is the carrier's, which honors `runsOn`.

## Standing outputs and `work new --from-possibility` (0.9.4)

From 0.9.4 (GOAL-08), every output of a standing agent's occurrence is a record another principal disposes of. The contributor of
the assignment that carries the occurrence records it, through the MCP tools `standing_possibility`, `standing_question` and
`standing_publication`, `POST /v1/standing/outputs` or the SDK's `StandingOutput`, and the runtime names in its `raisedBy` the goal,
the occurrence, the assignment and, for an agent, its run in flight. No standing agent adopts, declines, parks, answers or withdraws
its own output. From 0.9.4 (FIX-155), a disposition reads as an answer does: a decline, park or adoption of a possibility that its
raiser or any agent tied to the standing agent records disposes of nothing, whatever date it claims, so the possibility stays open
and counted, reads open to `work new --from-possibility` and the agent's next brief, and is disposed of by the next revision a
person or a principal not tied to the goal records. Such a disposition is an error where what ties its author was written first
(`standing.own-output`) and is otherwise reported (`standing.output-now`), which the record write refuses for a new one. In the same
way, a withdrawal of the agent's ask leaves it waiting, and a close or observation of its publication request by anyone but an
operator who is a person closes nothing.

- **A recommendation** is an open possibility with a `fingerprint`, the flow's rule for what makes two the same, and its sources;
  optionally evidence for and against (no scores), review triggers, links to possibilities it duplicates or supersedes, and
  `disclosure: held` for a security finding. A second open possibility of the agent with the same fingerprint is refused, and a
  declined one is raised again only by naming it in `raisesAgain` with new evidence. Its raiser may revise an open one, keeping its
  `raisedBy` and fingerprint, to add evidence; a revision is judged by the `raisedBy` of the record it revises, so leaving it out
  changes no rule. No agent declines, parks or adopts a held finding: a person does, only an operator who is a person adopts it or
  ends its hold, and the same holds for a held possibility no standing agent raised.
- **Proposed work** is a possibility with a `draft` work order. `vericairn work new --from-possibility <id> --by <planner>` adopts it:
  the draft supplies every field the command leaves out, the possibility joins the work order's inputs, and the possibility's revision
  adopting it as the new work order is stored with it. Its author, whoever raised it, and any agent tied to the standing agent (as
  a question's answer reads it, below), is refused, and a possibility revision recording its author's adoption is refused as well;
  a held finding is adopted only by an operator who is a person, and its work order carries `disclosure: held`, so the checked push
  keeps its key and text out of a public repository until its release is published.
- **A question** is an ask to a person, who decides for the goal when none is named; a decision of that person answers it. A
  decision recorded by the agent that asked, or by any agent tied to the standing agent, is refused, whatever `decidedBy` it
  names, and answers nothing; the same holds for the renewal, period-cap and failure-pause asks goal step puts about the agent,
  so no agent's decision lifts its own failure pause, and from FIX-155 no attempt, result or run it records ends the pause
  either (above). An agent is tied when any assignment of the goal's work has named it as contributor,
  or any revision of the goal's role, in the goal's own roles or the roster, has named it, whatever date a record claims and
  whatever the roster names now. Both only grow, so a roster or assignment change can re-open an answered ask for the person and
  never closes one: a decision an agent recorded before it was tied stops answering once it is. Any principal nothing ties to the
  agent, such as the estate's builder, still records the person's decision for them, on the renewal and failure-pause asks its
  own goal step wrote as well.
- **Words outside the estate** are a publication request to an operator: the exact text, labeled the agent's draft, pinning the
  agent's work order. It dispatches nothing, and only an operator who is a person closes it, directly: no agent closes it, citing
  the operator's words or not.

```sh
vericairn work new --from-possibility <possibility id> --by <planner> --key IDX-1
vericairn goal propose ... --standing example/researcher@1.0.0 --outputs possibilities=5 --outputs drafts=3 --outputs open=10
```

An occurrence records at most `--outputs possibilities` possibilities (five when omitted), of which at most `drafts` (three) carry a
draft work order; further ideas join one summary possibility, which lists them in `extras`. Each output, and each revision that adds a
draft or extras, is judged against the caps of the goal revision in force when the output was raised, so lowering the caps judges only
what is raised afterwards. While the agent's open outputs, its possibilities still open, asks still waiting and publication requests
not closed, reach `open` (ten when omitted), its occurrences defer and no run of it starts. `goal due`, `status` and `progress` show
its open outputs; an occurrence's brief states its output caps and names the agent's earlier open and declined possibilities among
its sources.

## Presence, availability and hand-overs (0.9.4)

`vericairn mcp serve --as <principal>` registers its session's presence with the daemon and renews the lease while it
runs (`ID-11`); see [the MCP server](mcp.md#presence-094). A person declares availability and quiet hours on their own
communication setting (`GOAL-11`), in an estate at record format 3:

| Option of `communication set` | Meaning |
|---|---|
| `--availability available\|away\|inherit` | Declare yourself available, or away; `inherit` removes the declaration. |
| `--away-until <time>` | With `away`: when you are back, a UTC time such as `2026-12-02T09:00:00Z`. Required for `away`. |
| `--away-from <time>` | With `away`: when the absence begins; at once when omitted. |
| `--quiet-hours <file>\|-\|inherit` | A JSON file of quiet hours: `zone`, `windows` (each `days`, `start`, `end` as `HH:MM`) and optional `breakthrough` categories; `-` reads standard input; `inherit` removes them. |

A change keeps the values it does not name. `communication show --recipient <person>` adds the availability and quiet
hours in force and whether quiet hours hold notifications now. Both belong to a person's own scope only.

```sh
vericairn communication set --as <person> --availability away --away-until 2026-12-02T09:00:00Z
vericairn communication set --as <person> --quiet-hours quiet.json
```

`goal next` and `goal step` pass over an away person, and give a step to an agent's live session when no launcher here
starts its harness and the step's exposure reads nothing: `goal step` then records the assignment with `routedWith:
session` and reports the outcome `session`. `work takeover <assignment> --by <person> --contributor <principal>
--basis <ask>` hands a step an agent holds to any principal the roster names for its role, citing the lapse ask;
handing it to anyone but a person requires `--basis`. See [events and presence](events-and-presence.md#routing-and-reassignment-094).

## `vericairn worker drill` (0.9.4)

From 0.9.4 (OPS-21), `vericairn worker drill --by <principal> [--grace <seconds>] [--limit <seconds>]` measures how long stopping
and containing a run takes before the carrier runs work unattended. The worker on the runtime's machine starts a scratch run, the
vericairn executable with a private argument that appends the time to a file in its own scratch directory every 50 milliseconds
and ignores the stop it is sent (the stop file and, on Linux and macOS, SIGTERM), as an unresponsive agent would. Once it is
writing, the drill asks it to stop through its run directory exactly as `worker stop` does, naming the grace (the worker's
default, ten seconds, when omitted), and the worker transmits the stop at its next heartbeat and escalates it after the grace.
The drill records a verification of the estate, method `stop-drill`, command `vericairn worker drill`, environment `platform` and
`device` (the signing device, so each machine's drill stands for that machine), signed by the principal's admitted device held on
the runtime's machine: its evidence gives the milliseconds from the stop request to the observed containment of the scratch run's
whole boundary and to its last write, with the stop's transmission and escalation. A scratch run not contained within the limit
(60 seconds when omitted, 1 to 3600) fails the drill; the drill then escalates the stop itself, waits for containment and records
the failure, which is required attention until a later drill on the same machine passes. The scratch run is never an agent run
and records nothing; its directory is removed once it is contained.

The command prints the verification's path, id and hash, the stop's time, grace and limit, both times and whether the drill
passed; with `--json`, the runtime's `StopDrillResponse` (`ok`, `message`, `passed`, `requestedAt`, `containedAt`,
`stopToContainedMilliseconds`, `lastWriteAt`, `stopToLastWriteMilliseconds`, `graceSeconds`, `limitSeconds`, `verification` and
`problem`). Exit 0 means a drill that passed and was recorded, 1 a failed drill or a refusal, and 2 an invalid argument. It is
refused, before anything starts, without an admitted device of the principal held on the runtime's machine, while the estate
reports an error (naming the finding, as a run's admission does), and over the local network: run it on the runtime's machine. A recurring duty whose discharge names a `stop-drill` verification runs it monthly; see
[who can stop a run](software.md#who-can-stop-a-run-and-the-timed-stop-drill-094).

```sh
vericairn worker drill --by <principal>
vericairn worker drill --by <principal> --grace 10 --limit 60 --json
```

## `vericairn observers` (0.9.4)

From 0.9.4 (OPS-20), the automated observers declared on this machine, such as CI on the main line, scheduled workflows,
intake watching and status refresh, each report at a declared interval, and silence past the interval plus a grace is
attention. See [Automated observers](observers.md).

| Command | What it does |
|---|---|
| `observers list` | Each observer declared on this machine: its kind, interval and grace, its latest report and its state now (`awaiting`, `observing`, `failed`, `not-observing` or `silent`). |
| `observers declare <name> --kind <kind> --interval <duration> [--grace <duration>] [--summary <text>] --by <operator>` | Declares an observer, or declares it again. The kind is `ci`, `scheduled-workflow`, `intake`, `status-refresh` or `other`; durations are ISO 8601, the interval from `PT1M` to `P31D`, the grace up to `P7D` and a quarter of the interval when omitted. The operator's. |
| `observers report <name> --outcome <passed\|failed\|not-observing> [--reason <reason>] [--at <time>] [--detail <text>] --by <principal>` | Keeps one report. `not-observing` requires `--reason` (`quota-exhausted`, `schedule-disabled` or `unavailable`) and is attention, never a pass; `--at` is when the observation was made, never more than a minute ahead. |
| `observers withdraw <name> --reason <text> --by <operator>` | Withdraws an observer, so that its silence stops being attention. The operator's. |

Each prints every observer's line and a summary; `--json` prints the runtime's `ObserversResponse` (`ok`, `message`, `asOf`,
`observers`, each with `name`, `kind`, `intervalSeconds`, `graceSeconds`, `summary`, `declaredAt`, `declaredBy`, `lastReport`,
`state`, `expectedBy`, `attention` and `why`, and `problem`). Exit 0 means a valid read or a kept change, 1 a refusal and 2 an
invalid argument. Human `status` ends with `Observers (this machine):` and one line per observer; `status --json` is unchanged.
A silent, failed or not-observing observer is a `failed check` in the operator's attention. Nothing is recorded: declarations
and reports are kept beside the clone's live store on this machine.

```sh
vericairn observers declare ci-main --kind ci --interval PT1H --grace PT15M --by <operator>
vericairn observers report ci-main --outcome not-observing --reason quota-exhausted --by <principal>
vericairn status
```

## `vericairn progress target` (0.9.4)

Keeps the estate's progress at a target, such as a pinned GitHub discussion, through the adapter its configuration on this
machine chooses (`WATCH-06`): `set`, `list` and `remove` configure targets and send nothing; `refresh <name>` brings one equal to
the estate's progress now, creating its place once and replacing its text only when it differs, and `--dry-run` shows the text
and sends nothing; `follow <name>` follows the daemon's event journal and refreshes and comments only on the events the target's
triggers name, until interrupted or after `--count` outcomes. A destination anyone may read always takes the public form, which
carries nothing a disclosure rule holds. `refresh` exits 0 when the target is equal, 2 when it refused and sent nothing, and 1
when an effect was stopped or not confirmed; `--json` writes `vericairn.progress-target-outcome/1`, naming every record the text
carries. See [Progress targets](progress-targets.md).

```sh
vericairn progress target set status --adapter github-discussions --setting repository=Example/example-estate --form private
vericairn progress target refresh status --dry-run
vericairn progress target follow status
```

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
another person, a recovery device, a device with no such admission and any other record are refused, each with why. From 0.9.4
(FIX-134) the estate reads such a self-admission the same way when the manifest names it by hand: no first authority, with
`manifest.first-authority` naming it among the listing's findings and nothing signed under it counting, so the command can name
the one that verifies. The root
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
