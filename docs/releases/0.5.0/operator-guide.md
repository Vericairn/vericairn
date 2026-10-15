# The operator's guide, Vericairn 0.5.0

This guide walks the journeys an operator and the agents working for them perform with the 0.5.0 command line, Workbench and the MCP server, in the order they first happen. Every command is listed with its options in `cli-reference.md`; every API operation in `api-reference.md`; every record body in `record-reference.md`; the rules the tool enforces in `record-format-v1.md` and `record-format-v2.md`, which `record-format.md` relates; the desktop in `workbench.md` and the agent server in `mcp.md`. Commands below assume you are inside the estate directory or pass `--estate <path>`; add `--json` to any command for machine-readable output, which is what agents use. Sections 2 to 12 are the command line; sections 13 to 15 arrived in 0.3.1 and section 16 in 0.5.0. Since 0.4.2, section 2 (a new estate starts at record format 2, and the record count after `estate init` is corrected), section 3 (custody, presence, the first authority, renames, rotations and emergency access), section 4 (what a format-2 decision states, asks, possibilities, obligations and grants that expire), section 8 (the living-document gate), section 9 (moving an estate to record format 2), section 12, section 13 (the Documents page) and section 14 (the document tools) changed, and section 16 (living documents) is new.

## 1. What you are working with

An estate is a directory of records: one JSON file per record under `records/<type>/<year-month>/<id>.json`, in canonical form, named by a time-ordered id, carrying a content hash and, once devices exist, a signature. Records are never edited or deleted; a change is a new record that supersedes the old one, and the current state is derived from the whole. `vericairn status` renders that state; `vericairn estate check` validates every record and the repository's cleanliness (no absolute machine paths, no credential-looking values, nothing where it should not be). A repository-synced estate keeps the records in git, which makes every clone a complete, verifiable replica; the runtime keeps a live store per machine beside it for speed, never committed.

The people and programs that write records are principals: the root operator (a human), builders and contributors (agents or humans), reviewers, services. A principal writes from a device whose key it holds; an admitted device signs every record it authors. Work is organized as work orders with an exit question, delegated by assignments under a brief, carried out in attempts that end in results, judged by reviews and closed by acceptances. Consequential records pin the exact hash of what they judge, so an acceptance of a result that has since changed is refused rather than silently applied.

Three clients share one runtime. The command line is what this guide shows first; Workbench (section 13) is the desktop over the same API and the same state language, so a state reads the same on both; the MCP server (section 14) gives a coding agent the same reads and the narrow writes an agent is meant to have. The runtime, embedded in a command or serving as the daemon, is the only place authority is evaluated and the only thing that signs.

## 2. Create an estate

```sh
mkdir my-estate && cd my-estate && git init
vericairn estate init --name "My Venture" --slug my-venture --purpose "What this estate exists to do." \
  --operator-name "Your Name" --organization "Your Organization" --commit <40-hex commit of the Vericairn source to pin>
vericairn status
git add -A && git commit -m "Estate online"
```

`estate init` writes two records and the manifest, which validate together: the operator principal (you), the charter (name, purpose, operator, policies: `signatures: optional` at first) and the estate manifest, which is a file of the repository rather than a record. It prints your principal id. Keep that id; every record you author names it with `--by`. The manifest pins the Vericairn source commit for the estate's bootstrap; the ordinary way to run the tool against the estate is the binary you installed with `--estate`, and the manifest later pins a published release once the estate holds that release's record (section 9). `--profile local-only` keeps the records in this machine's live store instead of files; the default, `repository-synced`, is the profile the first-party estate uses. A new estate starts at record format 2, the newest format 0.5.0 writes, which the manifest's `recordFormat` states; an estate begun under an earlier release moves to it by the upgrade of section 9.

Expected observation: `status` shows the estate's name, the operator principal, no work orders yet, and `estate check` reports `2 records: 0 errors, 0 warnings`. The guides of 0.2.0 to 0.4.2 said three; 0.4.2 reports two, as 0.5.0 does.

## 3. Principals, devices and admission

A device is a key on one machine acting for one principal. Begin with your own: create a device for the operator principal on your machine and admit it.

```sh
vericairn devices create --principal <operator id> --name "Jason's workstation"
vericairn devices admit <device id> --by <operator id> --note "The operator's workstation."
```

That admission, signed by the device it admits, is the estate's first authority: the root operator's first device admitting itself. It is accepted once in an estate's history; every later device is admitted under the signature of a device already admitted, so nobody able to add records can mint a device for the operator and sign as the operator. Releases 0.4.2 and earlier accepted any such self-admission.

Add a principal for a builder agent (or a second human) with a record, create its device on the machine it works from, and admit it from yours:

```sh
vericairn records new principal --by <operator id> --body builder.json
vericairn devices create --principal <builder id> --name "Builder on the workstation"
vericairn devices admit <builder device id> --by <operator id> --note "Why this device may author records."
vericairn devices list
```

with `builder.json`:

```json
{"kind": "agent", "name": "Builder", "roles": ["builder"], "agent": {"vendor": "the agent's vendor", "product": "the agent's product"}}
```

`devices create` generates the key, records the device signed by its own key, and from then on signs every record that principal authors from this machine. `devices admit` records the admission; `devices revoke <device> --by --reason` ends it from now on, leaving earlier records attributable. `devices list` shows each device by its identity with its current name, custody, presence, admission and whether its key is held here.

**Custody.** A new device gets the strongest custody its machine offers unless `--custody` names one: `cng`, the platform key store, on Windows; `secure-enclave` on a Mac with a Secure Enclave and the `vericairn-enclave` helper beside the command line, where the key is generated inside the enclave and kept as an enclave-wrapped blob that signs nothing on any other Mac; `secret-service` on Linux where a secret service runs and `secret-tool` is installed; otherwise `file`, an owner-only file. In an estate still at record format 1 the default is the strongest custody format 1 can name, and the Secure Enclave and presence wait for the upgrade to format 2 (section 9). Each custody's strength is recorded with it: a key in an owner-only file can be used by any process of the same operating-system user, a secret-service key is readable by them while the keyring is unlocked, and CNG and Secure Enclave keys cannot be exported.

**Presence.** A person's device needs its owner's presence at every signature wherever its custody enforces it, which only Secure Enclave custody does (Touch ID or the login password); `--presence false` declines it. An agent's or a service's device signs unattended and never claims presence, and no custody claims presence it cannot enforce. Presence on Windows is not built yet, so a Windows device states presence none. When this guide was written no signature with presence had been observed; the operator's own Mac is where it is first verified.

**Rename and rotate.** From record format 2 a device keeps its identity, its first record, across renames and key rotations, made on the machine that holds its key:

```sh
vericairn devices rename <device> --name "A better name"
vericairn devices rotate <device>
vericairn devices rotate <device> --custody secure-enclave
```

A rotation makes the new key beside the old one, records a revision signed by the previous key, which proves continuity, and by the new key, which proves possession, and only then lets the new key take the old one's place, so a refused rotation leaves the device as it was. Records signed before it stay valid, and the replaced key signs nothing dated after it. Moving a key into other custody, from a file into the Secure Enclave for example, is a rotation with `--custody`; the new key keeps the device's presence where the new custody enforces it, and `--presence` sets it. A key that may have leaked is revoked by an administrator, not only rotated.

**Emergency access.** Make the operator's recovery device on a machine that holds your admitted device:

```sh
vericairn devices recovery --principal <operator id> --name "Recovery key, kept in the safe" --output <a new file outside the estate>
```

You type a passphrase of at least twelve characters, or pass it on the first line of standard input with `--passphrase-stdin`. The key is made in memory and written once, encrypted, to the new file, readable by its owner only, and no key store ever holds it; your admitted device admits the recovery device at once. Move the file off the machine. When every other device of yours is lost, `vericairn devices admit <new device> --by <operator id> --note "..." --recovery <file>` admits a replacement and `vericairn devices revoke <lost device> --by <operator id> --reason "..." --recovery <file>` revokes a lost one, each opening the key in memory with its passphrase. A recovery device signs only device records, admissions and revocations, and each admission or revocation it signs shows in `status` and `estate check` as unreviewed (`recovery.unreviewed`) until you record a review of that exact record from another device. A recovery device is never the estate's first authority. Losing every device without one leaves the estate readable and its history verifiable, but nothing can author in it again.

**Requiring signatures.** To make signing mandatory, revise the charter so that `policies.signatures` is `required`: author a new estate record that supersedes the current one (`records new estate --by <operator id> --supersedes <current estate record id> --body charter.json`) with the same name, slug, purpose, operator and record format and the new policy. From then on a record without a signature from an admitted device is refused, and a clone without a key is read-only. Do this only after your own device is admitted.

Rules the tool enforces here: only the root operator or a principal granted `administer` may author a principal record; a device record is signed by its own key to prove possession, and each later revision by the device in the name of its identity; an admission names a scope, `operator` or `administer`, and stays in force across the device's renames and rotations. An agent principal that records operations and releases needs an authority with the `local-engineering` scope (section 4).

## 4. Authority and decisions

Decisions are the operator's words, recorded verbatim with a summary, a synthesis, the concrete effects and the work orders they affect; from record format 2 a decision also states how much rides on it and what it does not authorize:

```sh
vericairn records new decision --by <operator id> --body decision.json
```

```json
{"title": "Short title", "statement": "The operator's words, verbatim.", "summary": "What was decided, in a sentence.", "synthesis": "Context, what was decided, why, consequences and limits.", "effects": ["A concrete effect on the estate."], "affects": [], "decidedBy": "<operator id>", "decidedAt": "2026-10-15T14:00:00Z", "stake": "medium", "notAuthorized": ["Publication of anything."]}
```

`stake` is `low`, `medium`, `high` or `critical`; at medium and above the recorder asks with a recommendation first and at most three questions at a time. `notAuthorized` lists what the answer does not authorize, so that no one reads more into it, and may be empty. A revision of a decision keeps its `statement` byte for byte: the operator's words are protected, a revision corrects what surrounds them, and different words are a new decision. A decision may also name the asks it `answers`, the `obligations` it leaves (each with a key, a summary, an owner and how it is resolved; a `discharge` record settles one) and the living `documents` it changes (section 16). An estate still at record format 1 refuses these fields, and its decisions need none of them.

An agent may record a decision the operator made in conversation, naming the operator in `decidedBy` and keeping the statement verbatim; the agent is then the scribe, not the decider. `vericairn decisions` lists the current decisions and `vericairn decisions <id>` shows one cleanly.

A question for a person is an `ask` record: whom it is put to, the question, the recommendation first, the consequence of leaving it unanswered, and its state (open, notified or withdrawn). A decision that names it in `answers` answers it; waiting never does. An idea worth keeping without approving it is a `possibility`, with its disposition (open, parked, adopted or declined). Both are written with `records new`; `records describe ask` and `record-reference.md` list their fields. Format 2 also holds goals, goal statuses and the roster, which 0.5.0 validates; carrying a goal out arrives in 0.6.0.

Standing permission is an authority record based on a decision: a grantee, scopes (`local-engineering`, `repository-write`, `self-accept-ordinary-work`, `accept-work`, `administer`, or free text), the actions reserved to the operator regardless, and conditions. A builder with `self-accept-ordinary-work` may accept its own ordinary work orders; milestones and releases always need the operator. From format 2 a grant may carry an `expires` time: authority is judged at the time each record was authored, so a record authored after the grant expired relies on nothing.

## 5. The work loop: order, brief, assignment, attempt, result

A work order states intent, scope, deliverables and the one question whose honest yes closes it:

```sh
vericairn records new work-order --by <operator or builder id> --body order.json
```

```json
{"key": "W-1", "kind": "ordinary", "title": "Build the widget", "intent": "Why this work exists.", "scope": ["What is in scope"], "exitQuestion": "Does the widget build and pass its tests?", "inputs": [], "deliverables": ["widget.txt"]}
```

`kind` is `ordinary` or `milestone`; a milestone's acceptance is the operator's. The work is delegated under a brief, pinned to the exact work-order revision:

```sh
vericairn work assign W-1 --contributor <builder id> --by <operator id> --brief brief.json
```

```json
{"outcome": "What exists when the work is done.", "sources": [{"type": "work-order", "id": "<order id>"}], "permittedChanges": ["which repositories and trees may change"], "prohibitedEffects": ["no publication", "no spending"], "workspace": ["a repository and branch, named without machine paths"], "limits": ["continuous integration within the included minutes"], "expectedOutputs": ["widget.txt"], "reviewCriteria": ["tests pass on three platforms"], "recovery": "What a successor does with this brief and the latest result."}
```

The contributor starts an attempt, works, and submits a result, a claim to inspect:

```sh
vericairn work attempt <assignment id> --by <builder id> --workspace "widget repository, branch work/w-1" --note "What I intend to do."
vericairn work result <attempt id> --by <builder id> --body result.json
```

```json
{"outcome": "completed", "summary": "What was done and how it was verified.", "outputs": [{"name": "widget.txt", "hash": "sha256:...", "bytes": 123, "location": "widget.txt"}], "evidence": [{"summary": "Continuous integration run 123 passed on three platforms."}], "checks": [{"name": "tests", "outcome": "passed", "summary": "42 tests."}], "limitations": ["What the result does not establish."], "unresolvedEffects": [], "nextStep": "What follows."}
```

Outcomes are `completed`, `partial` or `failed`; a partial result with a next step is how an agent hands over cleanly. `vericairn work brief W-1` prints everything a contributor needs from the records alone, including the latest result's next step, which is how a new session or a different agent resumes; `vericairn work observe W-1` shows the assignments, attempts, results, amendments, reviews and acceptances of an order. A change of scope is an amendment (`work amend <assignment> --by --body`), which authorizes nothing until a decision admits it.

## 6. Review and acceptance

A review judges an exact revision and states its independence honestly:

```sh
vericairn records new review --by <reviewer id> --subject result:<result id>:<result hash> --body review.json
```

```json
{"independence": "self", "outcome": "pass", "summary": "What was reviewed and how, against the brief's criteria.", "findings": [{"severity": "info", "summary": "A finding.", "location": "where"}]}
```

`independence` must match the facts: `self` when the reviewer authored the subject, `independent-agent` for a different agent principal, `operator` for the operator; the tool refuses a mislabeled review. Outcomes are `pass`, `findings` or `fail`.

An acceptance closes a result or a work order, pinned to its exact hash:

```sh
vericairn records new acceptance --by <operator id> --subject work-order:<order id>:<order hash> --body accept.json
```

```json
{"statement": "I accept W-1 from the recorded evidence.", "basis": ["<review id>"], "self": false}
```

`basis` lists reviews of the same subject (a review of a result is not a basis for accepting the work order; a review of the order is); it may be empty for an operator's acceptance. `self` must be true when the accepting principal authored the subject, and then an authority with `self-accept-ordinary-work` must exist. The subject hash comes from the record file or from `--json` output of the command that wrote it; `vericairn records hash <file>` prints it. A refused acceptance lists every reason at once and writes nothing.

## 7. Repositories and checks as evidence

The estate records the repositories it uses as resources, by observation, without changing them:

```sh
vericairn sources inspect <path to a git working tree>
vericairn sources adopt <path> --by <id> --name "widget" --role "the product's source" --visibility public
vericairn sources observe <path> --resource <resource id> --by <id>
```

A check runs a command in a repository and records a verification of the exact commit (command, environment, exit code, duration, output as an object):

```sh
vericairn check run --resource <resource id> --by <builder id> --in <path> --summary "The test suite." -- dotnet test
vericairn check observe --resource <resource id> --repository owner/name --run <workflow run id> --by <id>
```

`check observe` records a workflow run at GitHub as automation's verification of the commit it built, through your `gh` login. Results cite verifications as evidence.

## 8. Releases

A release of a software product is recorded as intent before effect, through a release provider: a module that builds a release on a destination, observes the run and the draft, and names the operator's manual publication without ever running it. 0.5.0 carries one, the GitHub Releases provider (`github-releases`), which dispatches a GitHub Actions workflow and observes it through the GitHub CLI logged in as you on the runtime's machine; Vericairn never sees or stores the token, and never publishes. `vericairn release providers` lists the providers and adapters this build carries, each with the settings it declares, their defaults and the setting that names the destination.

A release goes through the provider its request names (`--provider`) or the one its product declares, and is refused when neither does. A product declares how its releases are made in the optional `release` object of its product record: the destination (the provider and the settings the product fixes, for GitHub at least `repository`), the checksum asset, and the artifacts a complete release carries, each a name with `{version}` and optionally `{platform}`, a role (distributable, bill of materials, notices, documentation or other) and its platforms. Without declared artifacts, a generic rule reads the draft: a platform identifier in an asset's name gives its platform, and a draft is complete when its checksum file names every other asset. `software.md` has the record's shape. Published releases before 0.4.2 refuse a product record that declares a release profile, so an estate records one only after every clone that writes to it runs 0.4.2 or later.

```sh
vericairn release providers
vericairn release submit --product <product id> --version 0.5.0 --by <builder id> [--provider github-releases] [--repository owner/name] [--ref main] [--workflow release.yml] [--setting name=value]
vericairn release observe --version 0.5.0 --product <product id> --by <builder id> --qualified linux-x64 --qualified osx-arm64 --qualified win-x64 --limitation "..."
```

`release submit` resolves the provider and its settings (the provider's defaults, then the product's declared settings, then the request's own; a missing required setting is refused), records an operation as `not-started` under the identity `<provider>:<destination>:v<version>`, dispatches, and records what it observed (`dispatched`, or `uncertain` when the run cannot be identified, or a known failure). `release observe` finds the operation and observes it through the provider that operation records, never another: it records the run's outcome, then the candidate release from the observed draft with every artifact's platform and role taken from the product's profile, and, after the operator has published the draft by hand, the published revision. While the run is in progress or the draft is incomplete, nothing is recorded and what is missing is printed. Before it records any intent, `release submit` refuses while a living document of the product is due at level `refuse`, naming every such document at once, and while a document in a repository other than the estate's, declared at `refuse` on the product's releases, reads differently at the commit the provider's `ref` names from what its latest review cleared, where this machine has a clone of that repository registered (section 16). The operator accepts the exact candidate before publishing:

```sh
vericairn records new acceptance --by <operator id> --subject release:<candidate id>:<candidate hash> --body accept-release.json
gh release edit v0.5.0 -R owner/name --draft=false --latest
vericairn release observe --version 0.5.0 --product <product id> --by <builder id>
vericairn release list
```

The published revision is refused without the operator's acceptance of the candidate it supersedes. A retry after an uncertain or failed attempt under the same identity needs `--reconciliation` stating how the earlier attempt was settled; `--cancel` settles an intent that never dispatched. The runtime does all of this behind `POST /v1/releases/submit` and `observe`, so the command line and Workbench's release workspace (section 13) do the same thing and refuse the same things.

## 9. Upgrading an estate

```sh
vericairn estate upgrade --to 0.5.0 --check
vericairn estate upgrade --to 0.5.0 --by <principal id>
```

Run with the binary of the version you are upgrading to, once the published release record of that version is in the estate. The plan shows the migrations the new version applies; applying pins the manifest to the published release with its artifact hashes, applies the record-format migrations as one reviewable change, and writes a migration record; a failed migration leaves the repository untouched. Commit the change; the bootstrap then fetches that release by hash.

Upgrading an estate at record format 1 to 0.5.0 applies the step `record-format-2`: the manifest's `recordFormat` becomes 2, and the migration record is the estate's first format-2 record. No record is rewritten, so every record's bytes and hash are the same before and after, and each keeps the rules of the format it states. Builds before 0.5.0 refuse an estate at format 2, so every clone that writes to it moves to 0.5.0, which its bootstrap fetches from the new pin; a clone that has not pulled the upgrade yet can still add format-1 records. Device keys do not move with the upgrade: rotate each into the custody you want, on the machine that holds it (section 3).

## 10. Export, restore, recovery and divergence

```sh
vericairn estate export --out my-estate.zip --by <id>
vericairn estate restore --from my-estate.zip --to <empty directory>
vericairn recover inspect
vericairn recover compare --with <another copy>
vericairn estate reconcile
```

An export carries records, objects, manifest and documents with an inventory, and no keys or authority; a restore verifies the inventory and produces a repository-synced estate in which nobody can author until admitted again. `recover inspect` says what this copy holds, which devices can author from here, work in progress and unresolved effects; `recover compare` lists records only in this copy, only in the other, or (never expected) differing. Two copies that both added records merge in git as file additions; what cannot merge by itself, divergent revisions of one record, duplicate keys, duplicate results, is listed by `estate reconcile` and settled with a reconciliation record.

Objects are content-addressed bytes beside the records: `objects put <file>`, `objects list`, `objects verify` reads every object and checks its bytes against its name.

## 11. The daemon

Commands run an embedded runtime unless a daemon serves the estate on this machine; then every command is a client of it, so that one owner holds the live state.

```sh
vericairn runtime status
vericairn runtime start
vericairn runtime stop
vericairn runtime install --plan
vericairn runtime install
```

`runtime serve` runs the daemon in the foreground until stopped; `start` runs it in the background with streams of its own, its output going to `daemon.log` beside the estate's live store, and waits until it answers; when the daemon stops before it serves, `start` exits with an error that quotes what the daemon wrote to its log and says where the log is; `stop` asks it to stop (closing a client never does). `install --plan` prints the per-user service unit (a launchd agent, a systemd user unit or a logon task) and the commands without installing; `install` registers it so the daemon starts at login; `uninstall` removes it. The daemon serves a local socket (or a named pipe on Windows) and a loopback TLS endpoint pinned to its own certificate, with every request signed by an admitted device; `api.md` has the details and `api-reference.md` every operation. Workbench's runtime workspace (section 13) does the same through this command line and shows the plan before it installs or uninstalls.

## 12. Reading the estate

`vericairn status` renders the work orders with their kinds and states, the releases, the assignments with their attempts and outcomes, the living documents that are due and why (section 16), and counts of decisions, document revisions, resources and verifications; it is identical on every machine for the same records, which the first-party estate's continuous integration proves on three platforms. `vericairn decisions` lists decisions; `records describe` explains any record type and marks what record format 2 introduced; `records validate` parses and validates everything; `estate check` adds the repository checks, living documents due at `warn` or `refuse`, generated regions edited by hand, and records a recovery device signed that no operator has reviewed; `doctor` checks the machine. Through the API, `GET /v1/records?type=<type>` lists the current records of one type, which is how Workbench reads products, operations and reviews and how the MCP tool `records_list` answers an agent; `GET /v1/documents` lists the living documents.

## 13. Workbench, the desktop

Workbench is a client of the daemon through the SDK and of the command line beside it for runtime management; it never hosts the runtime, evaluates no authority and signs nothing. Everything it shows comes from the runtime's responses in the same state language as `vericairn status`.

Open an estate: `vericairn-workbench --estate <root>`, or type the root on the first-run page (recent estates are remembered). A directory without `estate/manifest.json` is refused and nothing is created. When no daemon answers, Workbench starts one through the command line beside it, waits for that command to finish (never for the end of its output), and reconnects. The target bar shows the estate, the pinned release, the runtime's state, the acting principal and the freshness of what you see. The acting principal is chosen among the admitted devices whose keys this machine holds, or named with `--as <principal id>`, and the target bar names it by its current name with the device the runtime signs its records with. With `--as`, the selector shows that principal's admitted device held here, and choosing another device in the selector changes the acting principal; when the named principal has no device held here, the bar says whether its writes are refused (the estate requires signatures) or written unsigned. With no admitted device held here at all, Workbench says it is read-only and every write surface says so before any request.

Home shows attention: what needs a person, derived from the records alone, in six kinds (decision needed, blocked, uncertain effect, failed check, incident, information), each with why it matters, its state, its freshness and the next action. Selecting an item explains it in the inspector; Enter opens its subject. A submitted result opens in the review and decision workspace.

The work screens: Products lists the product records with their editions and surfaces. A work order (open it by key with Ctrl+G, or from attention) is a page with its brief, its assignments with attempt counts and latest results, and its observation; an assignment is a page with the brief as delegated, the assigning note and every attempt with its result and state. The assignment composer (Ctrl+Shift+A on an open work order) takes the brief's fields, one list entry per line, and sends nothing until "Send the assignment" or Ctrl+Enter; the runtime validates and signs, and a refusal is shown with every finding. The result inbox (Ctrl+I) lists the results awaiting review across the estate; Enter opens one in the review and decision workspace, which shows the exact result (outcome, summary, outputs, evidence, checks, limitations, unresolved effects, next step), the reviews on record, a review draft (outcome, summary, findings) and an acceptance draft whose basis is the checked reviews. Both go through the API pinned to the subject's hash (Ctrl+Enter sends the review, Ctrl+Shift+Enter the acceptance); Workbench derives the independence and self flags it will declare from who authored the subject, shows them, and the runtime checks them. A prepared review or acceptance stays pinned to the subject revision and to the facts on record about it: returning to it reports reusable, needs review (new records about the subject; the draft is kept and nothing is sent) or withdrawn (the subject was superseded or is gone). A comparison view (Work menu) puts a record beside the revision it supersedes with the line difference between them.

The release workspace (Ctrl+Shift+E) runs section 8 from the desktop: the providers the runtime can reach are listed and the product's declared provider is selected (for a product that declares none, nothing is selected until you choose); the form shows one field per setting the selected provider declares, filled with the product's declared values, where an empty field means the provider's default, which its label names; then "Submit the release" or Ctrl+Enter records the intent and dispatches; each destination shows its preparation, submission, external review and observed availability separately; "Observe" or Ctrl+Shift+Enter records the run's outcome, the candidate with every distributable by hash, and later the observed publication; Enter on a destination opens the candidate in the review workspace for the operator's acceptance at its exact hash; the manual publication command is shown and never run; a second submission under one identity is refused exactly as on the command line, and a retry needs a reconciliation.

The theme binds the brand's roles, never values: Inter for titles, headings and body text and JetBrains Mono for identifiers, both bundled under the SIL Open Font License, one accent blue, one attention amber used only for the attention kinds, and neutral surfaces and outlines; the arrival page carries the cairn mark. The probe measures the contrast of every pair the roles form, in light and dark.

The Documents page (Ctrl+Shift+D) lists each declared living document with its state (current, due at its level, or unavailable), every trigger newer than its latest clearance, and that clearance. A review of the selected document is sent with Ctrl+Enter: the outcome, what was checked, a commit for a document of another repository, and findings. The runtime reads the content, records what the review answers and runs the declared checks; the page decides neither independence nor clearance, and a refusal shows every reason. It does not yet show a document's content beside what made it due.

The runtime workspace (Ctrl+Shift+U) shows the daemon's state, where it is and what it reports about itself, with start, stop, the install plan, and install and uninstall of the per-user service after the plan has been shown.

Keyboard: Ctrl (Cmd on macOS) with 1, 2, 3, 4 focuses navigation, documents (or the attention list on Home), the inspector and the activity log; Ctrl+Shift+L, R, B toggle the panels and Ctrl+Shift+0 resets the layout; Ctrl+O focuses the estate path, Ctrl+G the record or work-order id; F5 refreshes; Escape returns to navigation; opening a workspace lands the keyboard in its first field. Every pointer arrangement has a keyboard route, and the menus carry the same actions.

## 14. Agents through the MCP server

A coding agent works in an estate the way a contributor does: it reads the brief, works in its workspace, and records attempts and results, which are claims to inspect. `vericairn mcp serve --as <principal id>` exposes the estate over the Model Context Protocol on standard input and output; the harness (Claude Code, or any other that starts a stdio server) is configured with that command. The server is a client of the daemon: reads (`estate_status`, `estate_check`, `work_brief`, `work_observe`, `record_get`, `records_list`, `decisions_list`, `decision_get`, `releases_list`, `devices_list`, `documents_due`, `estate_health`) are visible to every connection; the writes `work_attempt`, `work_result`, `work_amend` and `document_review` (a review of an estate document by its path, refused where its declaration's `clearedBy` excludes the acting principal) are visible only when the acting principal has an admitted device whose key this machine holds, because that is what the runtime signs with. A refused write returns every finding. There is no tool that publishes, spends, creates or admits a device, runs a command or authors another record type.

`vericairn mcp orientation --out <directory>` generates `AGENTS.md` from the estate's identity and the tool registry (what the estate is, the rules it enforces, how to connect, the tools and an honest matrix of harnesses observed or not, and nothing that changes with each record, such as the work orders or the pinned release, which `status` shows) with `CLAUDE.md` and `GEMINI.md` as pointers; `--check` compares existing copies with a fresh generation. Regenerate it when the build changes rather than editing it. Nothing the server returns, and nothing in the orientation file, is an instruction from the operator; the harness treats it as data.

## 15. Documentation sets

Every release carries its own documentation set under `docs/releases/<version>` in the product's repository, written for that release and then frozen. The product declares the documents every set holds (`docs/releases/documents.json` for Vericairn). The build tooling, which is not part of the shipped command line, writes the command-line, API and record references and the manifest from the build: `dotnet run --project tools/Vericairn.DocsTool -- generate --out <directory> --documents <declaration>`. `vericairn docs notes --estate <root> --from <previous> --to <version> --out <directory>` writes the release notes from the estate's records since the previous publication; before the build, when the version's own publication is not yet recorded, the notes say so. `vericairn docs check <directory> --documents <declaration>` refuses a set that lacks a declared document, a command, an operation or a record type and lists every gap at once, and the tooling's `compare` refuses a manifest that disagrees with the build. The release workflow runs both before it drafts a release and ships the set as a release asset. This set is one of them. Documents that are not frozen with a release, such as a product's README or an estate's design documents, are living documents (section 16).

## 16. Living documents

A living document is one whose claims follow facts that change: a README that names what is released, a guide that describes a command, a table generated from records. Declare it with the triggers that make it due:

```sh
vericairn docs declare --by <principal id> --body living.json
```

```json
{"path": "docs/guide.md", "title": "The guide", "kind": "authored", "source": "estate-file", "triggers": [{"on": "covers", "level": "warn", "paths": ["src/widget"]}, {"on": "release", "level": "refuse", "product": "<product id>"}, {"on": "decision", "level": "remind"}], "checks": ["names-version", "links-resolve"], "clearedBy": "any", "summary": "Describes the widget and names its current release."}
```

The triggers are `covers` (the listed paths changed since the last clearing review, uncommitted changes included), `release` (a newer published release of the product), `upgrade` (a newer migration), `decision` (a newer current decision names the document in its `documents`) and `accepted` (a newer acceptance of a work order with one of the listed `keys`). Each has a level: `remind` shows in `status`; `warn` is also a `document.due` warning of `estate check`; `refuse` also refuses the product's next `release submit` before any intent is recorded, naming every such document at once. `clearedBy` says who may clear it: any admitted principal (`any`, the default), one other than whoever revised its content (`independent`), or the operator. One current declaration per document: `docs declare --supersedes <declaration>` revises it.

```sh
vericairn docs living
vericairn docs review docs/guide.md --by <principal id> --outcome current --summary "Read against the widget as built."
```

`docs living` lists each declared document as current, due with every reason, or unavailable. A review's outcome is `current` (it holds), `revised` (it was changed for the trigger) or `needs-change` (it is stale and stays due). The runtime, not the reviewer, states the content's hash, the covered paths' identity, the triggers the review answers and the reviewer's independence, and runs the declared checks: `names-version` (the document names the version of each release the review answers), `links-resolve` (every relative link resolves within the repository) and `matches-generator` (generated content equals a fresh generation). A failed check refuses the clearance. For a file of the estate's own repository, a new `document-revision` record pinning it also counts as a clearance, except for `covers`, which only a review clears.

A generated region is the content between `<!-- vericairn:generated <id> -->` and `<!-- vericairn:end <id> -->`, each on its own line, declared in `regions` with its generator. `estate check` regenerates every region of the estate's own files whose generator is installed and refuses a hand edit or lost markers (`document.generated-edited`); `vericairn docs regenerate <path>` rewrites them and nothing outside the markers. The foundation's generator `vericairn.records/releases` tabulates the published releases of the products its inputs name, newest first.

A document in another repository, such as a product's README, is declared with that repository's resource in `repository` and the software pack's source `vericairn.software/git-file`. Register this machine's clone of the repository with `vericairn sources clone <path> --resource <resource id>`; the path stays machine configuration and never enters a record. `covers` is then compared at that clone's head; without a registered clone, at the commit reviewed or released. A review of such a document names the `--commit` it read. For a document declared at `refuse` on a product's releases, `release submit` reads it through the registered clone at the commit the release builds and refuses when its bytes differ from what its latest review cleared; the intent records what was compared. A declaration that names a source, trigger, check or generator no installed pack provides shows as unavailable (`document.unavailable`), and one with a `refuse` trigger closes its product's release gate until the pack is installed or the declaration is revised.

Not built yet: the software pack's own checks (the references and the third-party notices against the build, and a README naming no unreleased capability), packs proposing declarations, retiring a declaration, and Workbench showing a document's content beside what made it due.
