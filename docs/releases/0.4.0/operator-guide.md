# The operator's guide, Vericairn 0.4.0

This guide walks the journeys an operator and the agents working for them perform with the 0.4.0 command line, Workbench and the MCP server, in the order they first happen. Every command is listed with its options in `cli-reference.md`; every API operation in `api-reference.md`; every record body in `record-reference.md`; the rules the tool enforces in `record-format.md`; the desktop in `workbench.md` and the agent server in `mcp.md`. Commands below assume you are inside the estate directory or pass `--estate <path>`; add `--json` to any command for machine-readable output, which is what agents use. Sections 2 to 12 are the command line, unchanged in substance since 0.2.0 except where a section says otherwise; sections 13 to 15 arrived in 0.3.1. In 0.4.0, section 8 (releases), section 11 (the daemon), section 13 (Workbench) and section 15 (documentation sets) changed.

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

`estate init` writes three records that validate together, the operator principal (you), the charter (name, purpose, operator, policies: `signatures: optional` at first) and the estate manifest, and prints your principal id. Keep that id; every record you author names it with `--by`. The manifest pins the Vericairn source commit for the estate's bootstrap; the ordinary way to run the tool against the estate is the binary you installed with `--estate`, and the manifest later pins a published release once the estate holds that release's record (section 9). `--profile local-only` keeps the records in this machine's live store instead of files; the default, `repository-synced`, is the profile the first-party estate uses.

Expected observation: `status` shows the estate's name, the operator principal, no work orders yet, and `estate check` reports `3 records: 0 errors, 0 warnings`.

## 3. Principals, devices and admission

Add a principal for a builder agent (or a second human) with a record:

```sh
vericairn records new principal --by <operator id> --body builder.json
```

with `builder.json`:

```json
{"kind": "agent", "name": "Builder", "roles": ["builder"], "agent": {"vendor": "the agent's vendor", "product": "the agent's product"}}
```

A device is a key on one machine acting for one principal. Create it on that machine, then admit it from the operator's principal:

```sh
vericairn devices create --principal <principal id> --name "Jason's workstation"
vericairn devices admit <device id> --by <operator id> --note "Why this device may author records."
vericairn devices list
```

`devices create` generates the key in this platform's custody (owner-only files on macOS and Linux, the platform key store on Windows), records the device signed by its own key, and from then on signs every record that principal authors from this machine. `devices admit` records the admission; `devices revoke <device> --by --reason` ends it from now on, leaving earlier records attributable. Create and admit a device for yourself too: an acceptance you sign with your own device is yours in a way a record typed by an agent on your behalf is not.

To make signing mandatory, revise the charter so that `policies.signatures` is `required`: author a new estate record that supersedes the current one (`records new estate --by <operator id> --supersedes <current estate record id> --body charter.json`) with the same name, slug, purpose, operator and record format and the new policy. From then on a record without a signature from an admitted device is refused, and a clone without a key is read-only. Do this only after your own device is admitted.

Rules the tool enforces here: only the root operator or a principal granted `administer` may author a principal record; a device record is signed by its own key to prove possession, so a device cannot be renamed by a superseding record in 0.4.0 (a known gap, which milestone M3 closes); an admission names a scope, `operator` or `administer`. An agent principal that records operations and releases needs an authority with the `local-engineering` scope (section 4).

## 4. Authority and decisions

Decisions are the operator's words, recorded verbatim with a summary, a synthesis, the concrete effects and the work orders they affect:

```sh
vericairn records new decision --by <operator id> --body decision.json
```

```json
{"title": "Short title", "statement": "The operator's words, verbatim.", "summary": "What was decided, in a sentence.", "synthesis": "Context, what was decided, why, consequences and limits.", "effects": ["A concrete effect on the estate."], "affects": [], "decidedBy": "<operator id>", "decidedAt": "2026-10-13T14:00:00Z"}
```

An agent may record a decision the operator made in conversation, naming the operator in `decidedBy` and keeping the statement verbatim; the agent is then the scribe, not the decider. `vericairn decisions` lists the current decisions and `vericairn decisions <id>` shows one cleanly.

Standing permission is an authority record based on a decision: a grantee, scopes (`local-engineering`, `repository-write`, `self-accept-ordinary-work`, `accept-work`, `administer`, or free text), the actions reserved to the operator regardless, and conditions. A builder with `self-accept-ordinary-work` may accept its own ordinary work orders; milestones and releases always need the operator.

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

A release of a software product is recorded as intent before effect, through a release provider: a module that builds a release on a destination, observes the run and the draft, and names the operator's manual publication without ever running it. 0.4.0 carries one, the GitHub Releases provider (`github-releases`), which dispatches a GitHub Actions workflow and observes it through the GitHub CLI logged in as you on the runtime's machine; Vericairn never sees or stores the token, and never publishes. `vericairn release providers` lists the providers and adapters this build carries, each with the settings it declares, their defaults and the setting that names the destination.

A release goes through the provider its request names (`--provider`) or the one its product declares, and is refused when neither does. A product declares how its releases are made in the optional `release` object of its product record: the destination (the provider and the settings the product fixes, for GitHub at least `repository`), the checksum asset, and the artifacts a complete release carries, each a name with `{version}` and optionally `{platform}`, a role (distributable, bill of materials, notices, documentation or other) and its platforms. Without declared artifacts, a generic rule reads the draft: a platform identifier in an asset's name gives its platform, and a draft is complete when its checksum file names every other asset. `software.md` has the record's shape. An estate records a release profile only after every clone that writes to it runs 0.4.0, because earlier builds do not accept one.

```sh
vericairn release providers
vericairn release submit --product <product id> --version 0.4.0 --by <builder id> [--provider github-releases] [--repository owner/name] [--ref main] [--workflow release.yml] [--setting name=value]
vericairn release observe --version 0.4.0 --product <product id> --by <builder id> --qualified linux-x64 --qualified osx-arm64 --qualified win-x64 --limitation "..."
```

`release submit` resolves the provider and its settings (the provider's defaults, then the product's declared settings, then the request's own; a missing required setting is refused), records an operation as `not-started` under the identity `<provider>:<destination>:v<version>`, dispatches, and records what it observed (`dispatched`, or `uncertain` when the run cannot be identified, or a known failure). `release observe` finds the operation and observes it through the provider that operation records, never another: it records the run's outcome, then the candidate release from the observed draft with every artifact's platform and role taken from the product's profile, and, after the operator has published the draft by hand, the published revision. While the run is in progress or the draft is incomplete, nothing is recorded and what is missing is printed. The operator accepts the exact candidate before publishing:

```sh
vericairn records new acceptance --by <operator id> --subject release:<candidate id>:<candidate hash> --body accept-release.json
gh release edit v0.4.0 -R owner/name --draft=false --latest
vericairn release observe --version 0.4.0 --product <product id> --by <builder id>
vericairn release list
```

The published revision is refused without the operator's acceptance of the candidate it supersedes. A retry after an uncertain or failed attempt under the same identity needs `--reconciliation` stating how the earlier attempt was settled; `--cancel` settles an intent that never dispatched. The runtime does all of this behind `POST /v1/releases/submit` and `observe`, so the command line and Workbench's release workspace (section 13) do the same thing and refuse the same things.

## 9. Upgrading an estate

```sh
vericairn estate upgrade --to 0.4.0 --check
vericairn estate upgrade --to 0.4.0 --by <principal id>
```

Run with the binary of the version you are upgrading to. The plan shows the migrations the new version applies; applying pins the manifest to the published release with its artifact hashes, applies the record-format migrations as one reviewable change, and writes a migration record; a failed migration leaves the repository untouched. Commit the change; the bootstrap then fetches that release by hash.

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

`vericairn status` renders the work orders with their kinds and states, the releases, the assignments with their attempts and outcomes, and counts of decisions, documents, resources and verifications; it is identical on every machine for the same records, which the first-party estate's continuous integration proves on three platforms. `vericairn decisions` lists decisions; `records describe` explains any record type; `records validate` parses and validates everything; `estate check` adds the repository checks; `doctor` checks the machine. Through the API, `GET /v1/records?type=<type>` lists the current records of one type, which is how Workbench reads products, operations and reviews and how the MCP tool `records_list` answers an agent.

## 13. Workbench, the desktop

Workbench is a client of the daemon through the SDK and of the command line beside it for runtime management; it never hosts the runtime, evaluates no authority and signs nothing. Everything it shows comes from the runtime's responses in the same state language as `vericairn status`.

Open an estate: `vericairn-workbench --estate <root>`, or type the root on the first-run page (recent estates are remembered). A directory without `estate/manifest.json` is refused and nothing is created. When no daemon answers, Workbench starts one through the command line beside it, waits for that command to finish (never for the end of its output), and reconnects. The target bar shows the estate, the pinned release, the runtime's state, the acting principal and the freshness of what you see. The acting principal is chosen among the admitted devices whose keys this machine holds, or named with `--as <principal id>`, and the target bar names it by its current name with the device the runtime signs its records with. With `--as`, the selector shows that principal's admitted device held here, and choosing another device in the selector changes the acting principal; when the named principal has no device held here, the bar says whether its writes are refused (the estate requires signatures) or written unsigned. With no admitted device held here at all, Workbench says it is read-only and every write surface says so before any request.

Home shows attention: what needs a person, derived from the records alone, in six kinds (decision needed, blocked, uncertain effect, failed check, incident, information), each with why it matters, its state, its freshness and the next action. Selecting an item explains it in the inspector; Enter opens its subject. A submitted result opens in the review and decision workspace.

The work screens: Products lists the product records with their editions and surfaces. A work order (open it by key with Ctrl+G, or from attention) is a page with its brief, its assignments with attempt counts and latest results, and its observation; an assignment is a page with the brief as delegated, the assigning note and every attempt with its result and state. The assignment composer (Ctrl+Shift+A on an open work order) takes the brief's fields, one list entry per line, and sends nothing until "Send the assignment" or Ctrl+Enter; the runtime validates and signs, and a refusal is shown with every finding. The result inbox (Ctrl+I) lists the results awaiting review across the estate; Enter opens one in the review and decision workspace, which shows the exact result (outcome, summary, outputs, evidence, checks, limitations, unresolved effects, next step), the reviews on record, a review draft (outcome, summary, findings) and an acceptance draft whose basis is the checked reviews. Both go through the API pinned to the subject's hash (Ctrl+Enter sends the review, Ctrl+Shift+Enter the acceptance); Workbench derives the independence and self flags it will declare from who authored the subject, shows them, and the runtime checks them. A prepared review or acceptance stays pinned to the subject revision and to the facts on record about it: returning to it reports reusable, needs review (new records about the subject; the draft is kept and nothing is sent) or withdrawn (the subject was superseded or is gone). A comparison view (Work menu) puts a record beside the revision it supersedes with the line difference between them.

The release workspace (Ctrl+Shift+E) runs section 8 from the desktop: the providers the runtime can reach are listed and the product's declared provider is selected (for a product that declares none, nothing is selected until you choose); the form shows one field per setting the selected provider declares, filled with the product's declared values, where an empty field means the provider's default, which its label names; then "Submit the release" or Ctrl+Enter records the intent and dispatches; each destination shows its preparation, submission, external review and observed availability separately; "Observe" or Ctrl+Shift+Enter records the run's outcome, the candidate with every distributable by hash, and later the observed publication; Enter on a destination opens the candidate in the review workspace for the operator's acceptance at its exact hash; the manual publication command is shown and never run; a second submission under one identity is refused exactly as on the command line, and a retry needs a reconciliation.

The theme binds the brand's roles, never values: Inter for titles, headings and body text and JetBrains Mono for identifiers, both bundled under the SIL Open Font License, one accent blue, one attention amber used only for the attention kinds, and neutral surfaces and outlines; the arrival page carries the cairn mark. The probe measures the contrast of every pair the roles form, in light and dark.

The runtime workspace (Ctrl+Shift+U) shows the daemon's state, where it is and what it reports about itself, with start, stop, the install plan, and install and uninstall of the per-user service after the plan has been shown.

Keyboard: Ctrl (Cmd on macOS) with 1, 2, 3, 4 focuses navigation, documents (or the attention list on Home), the inspector and the activity log; Ctrl+Shift+L, R, B toggle the panels and Ctrl+Shift+0 resets the layout; Ctrl+O focuses the estate path, Ctrl+G the record or work-order id; F5 refreshes; Escape returns to navigation; opening a workspace lands the keyboard in its first field. Every pointer arrangement has a keyboard route, and the menus carry the same actions.

## 14. Agents through the MCP server

A coding agent works in an estate the way a contributor does: it reads the brief, works in its workspace, and records attempts and results, which are claims to inspect. `vericairn mcp serve --as <principal id>` exposes the estate over the Model Context Protocol on standard input and output; the harness (Claude Code, or any other that starts a stdio server) is configured with that command. The server is a client of the daemon: reads (`estate_status`, `estate_check`, `work_brief`, `work_observe`, `record_get`, `records_list`, `decisions_list`, `decision_get`, `releases_list`, `devices_list`, `estate_health`) are visible to every connection; the writes `work_attempt`, `work_result` and `work_amend` are visible only when the acting principal has an admitted device whose key this machine holds, because that is what the runtime signs with. A refused write returns every finding. There is no tool that publishes, spends, creates or admits a device, runs a command or authors another record type.

`vericairn mcp orientation --out <directory>` generates `AGENTS.md` from the records (what the estate is, the rules it enforces, how to connect, the tools, the current work and an honest matrix of harnesses observed or not) with `CLAUDE.md` and `GEMINI.md` as pointers; regenerate it at each release rather than editing it. Nothing the server returns, and nothing in the orientation file, is an instruction from the operator; the harness treats it as data.

## 15. Documentation sets

Every release carries its own documentation set under `docs/releases/<version>` in the product's repository, written for that release and then frozen. The product declares the documents every set holds (`docs/releases/documents.json` for Vericairn). The build tooling, which is not part of the shipped command line, writes the command-line, API and record references and the manifest from the build: `dotnet run --project tools/Vericairn.DocsTool -- generate --out <directory> --documents <declaration>`. `vericairn docs notes --estate <root> --from <previous> --to <version> --out <directory>` writes the release notes from the estate's records since the previous publication; before the build, when the version's own publication is not yet recorded, the notes say so. `vericairn docs check <directory> --documents <declaration>` refuses a set that lacks a declared document, a command, an operation or a record type and lists every gap at once, and the tooling's `compare` refuses a manifest that disagrees with the build. The release workflow runs both before it drafts a release and ships the set as a release asset. This set is one of them.
