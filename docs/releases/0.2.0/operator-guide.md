# The operator's guide, Vericairn 0.2.0

This guide walks the journeys an operator and the agents working for them perform with the 0.2.0 command line, in the order they first happen. Every command is listed with its options in `cli-reference.md`; every record body in `record-reference.md`; the rules the tool enforces in `record-format.md`. Commands below assume you are inside the estate directory or pass `--estate <path>`; add `--json` to any command for machine-readable output, which is what agents use.

## 1. What you are working with

An estate is a directory of records: one JSON file per record under `records/<type>/<year-month>/<id>.json`, in canonical form, named by a time-ordered id, carrying a content hash and, once devices exist, a signature. Records are never edited or deleted; a change is a new record that supersedes the old one, and the current state is derived from the whole. `vericairn status` renders that state; `vericairn estate check` validates every record and the repository's cleanliness (no absolute machine paths, no credential-looking values, nothing where it should not be). A repository-synced estate keeps the records in git, which makes every clone a complete, verifiable replica; the runtime keeps a live store per machine beside it for speed, never committed.

The people and programs that write records are principals: the root operator (a human), builders and contributors (agents or humans), reviewers, services. A principal writes from a device whose key it holds; an admitted device signs every record it authors. Work is organized as work orders with an exit question, delegated by assignments under a brief, carried out in attempts that end in results, judged by reviews and closed by acceptances. Consequential records pin the exact hash of what they judge, so an acceptance of a result that has since changed is refused rather than silently applied.

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

Rules the tool enforces here: only the root operator may author its own principal record; a device record is signed by its own key to prove possession, so a device cannot be renamed by a superseding record in 0.2.0 (a known gap); an admission names a scope, `operator` or `administer`.

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

A release of a software product is recorded as intent before effect. The flow needs a product record (`records new product`) and the GitHub CLI logged in as you; Vericairn never publishes.

```sh
vericairn release submit --product <product id> --version 0.2.0 --repository owner/name --by <builder id>
vericairn release observe --version 0.2.0 --repository owner/name --by <builder id> --qualified linux-x64 --qualified osx-arm64 --qualified win-x64 --limitation "..."
```

`release submit` records an operation as `not-started`, dispatches the release workflow and records what it observed (`dispatched`, or `uncertain` when the run cannot be identified, or a known failure). `release observe` records the run's outcome, then the candidate release from the observed draft with every distributable, the bill of materials and the notices by hash, and, after the operator has published the draft by hand, the published revision. The operator accepts the exact candidate before publishing:

```sh
vericairn records new acceptance --by <operator id> --subject release:<candidate id>:<candidate hash> --body accept-release.json
gh release edit v0.2.0 -R owner/name --draft=false --latest
vericairn release observe --version 0.2.0 --repository owner/name --by <builder id>
vericairn release list
```

The published revision is refused without the operator's acceptance of the candidate it supersedes. A retry after an uncertain or failed attempt under the same identity needs `--reconciliation` stating how the earlier attempt was settled; `--cancel` settles an intent that never dispatched.

## 9. Upgrading an estate

```sh
vericairn estate upgrade --to 0.2.0 --check
vericairn estate upgrade --to 0.2.0 --by <principal id>
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

`runtime serve` runs the daemon in the foreground until stopped; `start` runs it in the background and waits until it answers; `stop` asks it to stop (closing a client never does). `install --plan` prints the per-user service unit (a launchd agent, a systemd user unit or a logon task) and the commands without installing; `install` registers it so the daemon starts at login; `uninstall` removes it. The daemon serves a local socket (or a named pipe on Windows) and a loopback TLS endpoint pinned to its own certificate, with every request signed by an admitted device; `api.md` has the details and `api-reference.md` every operation.

## 12. Reading the estate

`vericairn status` renders the work orders with their kinds and states, the releases, the assignments with their attempts and outcomes, and counts of decisions, documents, resources and verifications; it is identical on every machine for the same records, which the first-party estate's continuous integration proves on three platforms. `vericairn decisions` lists decisions; `records describe` explains any record type; `records validate` parses and validates everything; `estate check` adds the repository checks; `doctor` checks the machine.
