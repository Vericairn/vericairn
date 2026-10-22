# The operator's guide, Vericairn 0.8.2

This guide walks the journeys an operator and the agents working for them perform with the 0.8.2 command line, Workbench and the MCP server. Commands, API operations and record bodies are generated in `cli-reference.md`, `api-reference.md` and `record-reference.md`; the format rules are in `record-format-v1.md`, `record-format-v2.md` and `record-format-v3.md`, related by `record-format.md`. Commands below assume you are inside the estate directory or pass `--estate <path>`; `--json` selects machine-readable output. With the NuGet tool, use `dotnet vc` wherever an example says `vericairn`. Sections 2 to 12 cover established journeys; sections 13 to 16 cover Workbench, MCP and documents; sections 17 to 19 cover goals, the configured worker and pairing. Sections 20 to 23 cover measured context, hand-over, communication and watching; sections 24 and 25 cover release notices and current attention. Sections 26 to 29 cover policies, readable companions, checked changes and client interruption.

0.8.2 follows published 0.8.0 and carries the work of 0.8.1, whose candidate was withdrawn before
publication, with the macOS and Linux single-file command lines able to start processes again (FIX-53)
and a retained basis's record digest rechecked when it is read (FIX-54). That work is the corrections
from 0.8.0's review (FIX-34 through FIX-38), the
policy and adoption corrections (FIX-39 through FIX-46 and FIX-48), test timing and the release's packaging
path in CI (FIX-29, FIX-32, FIX-49, CI-01 and CI-03), the release gate's retained basis by reference
(FIX-51), package registry observations (DIST-02), the package icon
(DIST-03), MCP Registry discovery (DIST-04) and exact-byte document pins (DOCS-07).
It reads record formats 1, 2 and 3 and writes the estate's own format, at most 3; an estate at format 3
upgrades from 0.8.0 without a format step. The format-3 upgrade from 0.7.3 preserves historical records
and requires explicit adoption afterwards. 0.8.0's features and earlier corrections remain. Earlier sets stay frozen.

NuGet availability follows publication and a successful package workflow. The remaining limits are in
`limitations-and-platforms.md`.

## 1. What you are working with

An estate is a directory of records: one JSON file per record under `records/<type>/<year-month>/<id>.json`, in canonical form, named by a time-ordered id, carrying a content hash and, when authored with an admitted device, a signature. The charter initially permits unsigned records; admission from format 2 has its own mandatory signature rules (section 3). Records are never edited or deleted; a change is a new record that supersedes the old one, and the current state is derived from the whole. `vericairn status` renders that state; `vericairn estate check` validates every record and the repository's cleanliness (no absolute machine paths, no credential-looking values, nothing where it should not be). A repository-synced estate keeps the records in git, which makes every clone a complete, verifiable replica; the runtime keeps a live store per machine beside it for speed, never committed.

FIX-13 makes generated identifiers increase within one process, including across threads and repeated
or backward clock readings. It does not order independent processes or explicitly supplied ids. Exact
hash-pinned acceptance establishes that the accepter saw the result; other judgments use recorded time
and a deterministic id tie-break. Historical record bytes remain unchanged.

FIX-12 exempts validated device `body.publicKey` and envelope `signatures[].signature` values from credential-pattern matching. The key must decode as a P-256 SubjectPublicKeyInfo key and the signature as 64 bytes in base64url encoding. Other fields, including nested fields with the same names, invalid encodings and non-record files remain scanned. This exemption does not skip cryptographic signature verification, change record bytes or suppress the machine-path scan.

The people and programs that write records are principals: the root operator (a human), builders and contributors (agents or humans), reviewers, services. A principal writes from a device whose key it holds; an admitted device signs every record it authors. Work is organized as work orders with an exit question, delegated by assignments under a brief, carried out in attempts that end in results, judged by reviews and closed by acceptances. Consequential records pin the exact hash of what they judge, so an acceptance stays attached to the revision judged. A superseded subject produces an unresolved-acceptance warning; the acceptance does not silently move to its replacement.

Three clients share one runtime. The command line is what this guide shows first; Workbench (section 13) is the desktop over the same API and the same state language, so a state reads the same on both; the MCP server (section 14) gives a coding agent the same reads and the narrow writes an agent is meant to have. The runtime, embedded in a command or serving as the daemon, is the only place authority is evaluated and the only thing that signs.

## 2. Create an estate

```sh
mkdir my-estate && cd my-estate && git init
vericairn estate init --name "My Venture" --slug my-venture --purpose "What this estate exists to do." \
  --operator-name "Your Name" --organization "Your Organization" --commit <40-hex commit of the Vericairn source to pin>
vericairn status
git add -A && git commit -m "Estate online"
```

`estate init` writes two records and the manifest, which validate together: the operator principal (you), the charter (name, purpose, operator, policies: `signatures: optional` at first) and the estate manifest, which is a file of the repository rather than a record. It prints your principal id. Keep that id; every record you author names it with `--by`. The manifest pins the Vericairn source commit for the estate's bootstrap; the ordinary way to run the tool against the estate is the binary you installed with `--estate`, and the manifest later pins a published release once the estate holds that release's record (section 9). `--profile local-only` keeps the records in this machine's live store instead of files; the default, `repository-synced`, is the profile the first-party estate uses. A new estate starts at record format 3, the newest format 0.8.2 writes, which the manifest's `recordFormat` states; an estate begun under an earlier release moves to it by the upgrade of section 9.

Expected observation: `status` shows the estate's name, the operator principal, no work orders yet, and `estate check` reports `2 records: 0 errors, 0 warnings`. The manifest is not a third record.

Optional charter signatures do not waive record-specific requirements. Policy and writing changes,
duty results/reviews, strict generated declarations and the operator one-step action still require
their admitted signatures and exact authority.

## 3. Principals, devices and admission

A device is a key on one machine acting for one principal. Begin with your own: create a device for the operator principal on your machine and admit it.

```sh
vericairn devices create --principal <operator id> --name "Jason's workstation"
vericairn devices admit <device id> --by <operator id> --note "The operator's workstation."
```

That admission, signed by the device it admits, is the estate's first authority: the root operator's first device admitting itself. It is accepted once in an estate's history. At record format 2 or 3 every other admission, and every revocation, counts only when signed by an already admitted device, whatever the charter's signature policy, or when an operator's signed migration to format 2 anchored an existing unsigned admission (section 9). A refused admission explains which admitted device or recovery key could sign it.

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

`devices create` generates the key, records the device signed by its own key, and proves possession of that key. Once admitted, the device can sign records that principal authors from this machine. `devices admit` records the admission; `devices revoke <device> --by --reason` ends it from now on, leaving earlier records attributable. `devices list` shows each device by its identity with its current name, custody, presence, admission and whether its key is held here.

**Custody.** A new device gets the strongest custody its machine offers unless `--custody` names one: `cng`, the platform key store, on Windows; `secure-enclave` on a Mac with a Secure Enclave and the `vericairn-enclave` helper beside the command line, where the key is generated inside the enclave and kept as an enclave-wrapped blob that signs nothing on any other Mac; `secret-service` on Linux where a secret service runs and `secret-tool` is installed; otherwise `file`, a file in per-user state. Unix file permissions are owner-only; Windows files inherit their directory ACLs. In an estate still at record format 1 the default is the strongest custody format 1 can name, and the Secure Enclave and presence wait for the upgrade to format 2 (section 9). Each custody's strength is recorded with it: a key in an owner-only file can be used by any process of the same operating-system user, a secret-service key is readable by them while the keyring is unlocked, and CNG and Secure Enclave keys cannot be exported.

**Presence.** A person's device needs its owner's presence at every signature wherever its custody enforces it, which only Secure Enclave custody does (Touch ID or the login password); `--presence false` declines it. An agent's or a service's device signs unattended and never claims presence, and no custody claims presence it cannot enforce. Presence on Windows is not built yet, so a Windows device states presence none. Tests without an interactive desktop do not qualify the presence prompt; consult the estate's platform qualifications for observations on the intended machine.

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

You type a passphrase of at least twelve characters, or pass it on the first line of standard input with `--passphrase-stdin`. The key is made in memory and written once, encrypted, to the new file, with owner-only permissions on Unix (inherited directory ACLs on Windows), and no key store ever holds it; your admitted device admits the recovery device at once. Move the file off the machine. When every other device of yours is lost, `vericairn devices admit <new device> --by <operator id> --note "..." --recovery <file>` admits a replacement and `vericairn devices revoke <lost device> --by <operator id> --reason "..." --recovery <file>` revokes a lost one, each opening the key in memory with its passphrase. A recovery device signs only device records, admissions and revocations, and each admission or revocation it signs shows in `status` and `estate check` as unreviewed (`recovery.unreviewed`) until you record a review of that exact record from another device. A recovery device is never the estate's first authority. Losing every device without one leaves the estate readable and its history verifiable, but signed authoring and new admissions require an admitted signing key. An optional-signature charter can still permit unsigned records; it does not restore admission authority.

**Requiring signatures.** To make signing mandatory, revise the charter so that `policies.signatures` is `required`: author a new estate record that supersedes the current one (`records new estate --by <operator id> --supersedes <current estate record id> --body charter.json`) with the same name, slug, purpose, operator and record format and the new policy. From then on a record without a signature from an admitted device is refused, and a clone without a key is read-only. A record's date is what its author wrote, so the policy binds the records dated after the charter revision: an unsigned record dated earlier is read under the earlier policy, which from record format 2 no longer reaches admissions (above). Do this only after your own device is admitted.

Rules the tool enforces here: only the root operator or a principal granted `administer` may author a principal record; a device record is signed by its own key to prove possession, and each later revision by the device in the name of its identity. An admission is authored by the operator or under `administer` authority, is signed in record format 2, and stays in force across the device's renames and rotations. A format-2 admission has no `scope` field. An agent principal that records operations and releases needs an authority with the `local-engineering` scope (section 4).

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

A question for a person is an `ask` record: whom it is put to, the question, the recommendation first, the consequence of leaving it unanswered, and its state (open, notified or withdrawn). A decision that names it in `answers` answers it; waiting never does. An idea worth keeping without approving it is a `possibility`, with its disposition (open, parked, adopted or declined). Both are written with `records new`; `records describe ask` and `record-reference.md` list their fields. `vericairn ask [--to <principal>]` reads the waiting asks, oldest first, delivering at most three per principal and listing the rest as queued. Reading or waiting answers none. Sections 17 and 18 cover the goal runtime and roster routing that can create these asks.

Standing permission is an authority record based on a decision: a grantee, scopes (`local-engineering`, `repository-write`, `self-accept-ordinary-work`, `accept-work`, `administer`, or free text), the actions reserved to the operator regardless, and conditions. A builder with `self-accept-ordinary-work` may accept its own ordinary work orders; milestones and releases always need the operator. From format 2 a grant may carry an `expires` time: authority is judged at the time each record was authored, so a record authored after the grant expired cannot rely on it. For ordinary scopes the current grants are otherwise used: a later grant or removal can change their derived authority. Format-3 policy scopes use the cited grant at the action's authoring time; a future grant cannot authorize an earlier policy action. Exact signed definition approval remains separate.

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
{"outcome": "What exists when the work is done.", "sources": [{"type": "work-order", "id": "<order id>"}], "permittedChanges": ["widget.txt"], "prohibitedEffects": ["no publication", "no spending"], "workspace": ["a repository and branch, named without machine paths"], "limits": ["continuous integration within the included minutes"], "expectedOutputs": ["widget.txt"], "reviewCriteria": ["tests pass on three platforms"], "recovery": "What a successor does with this brief and the latest result."}
```

The contributor starts an attempt, works, and submits a result, a claim to inspect:

```sh
vericairn work attempt <assignment id> --by <builder id> --workspace "widget repository, branch work/w-1" --note "What I intend to do."
vericairn work result <attempt id> --by <builder id> --body result.json
```

```json
{"outcome": "completed", "summary": "What was done and how it was verified.", "outputs": [{"name": "widget.txt", "hash": "sha256:...", "bytes": 123, "location": "widget.txt"}], "evidence": [{"summary": "Continuous integration run 123 passed on three platforms."}], "checks": [{"name": "tests", "outcome": "passed", "summary": "42 tests."}], "limitations": ["What the result does not establish."], "unresolvedEffects": [], "nextStep": "What follows."}
```

Outcomes are `completed`, `partial`, `blocked` or `abandoned`; a partial result with a next step is how an agent hands over cleanly. `vericairn work brief W-1` includes the measured mandatory originals, complete withheld list and latest result's next step. It can read verified document originals as well as records; a missing required source refuses preparation rather than silently shortening the brief. Section 20 covers context allocation and pinned retrieval. `vericairn work observe W-1` shows the assignments, attempts, results, amendments, reviews and acceptances of an order. A change of scope is an amendment (`work amend <assignment> --by --body`), which authorizes nothing until a decision admits it. An agent can also make progress claims through MCP; section 23 distinguishes those claims from observation and acceptance.

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

`basis` lists reviews of the same subject (a review of a result is not a basis for accepting the work order; a review of the order is); it may be empty for an operator's acceptance. `self` must be true when the accepting principal authored the subject. The operator can accept ordinary work; another principal needs `self-accept-ordinary-work` for its own ordinary work or `accept-work` for another principal's. Milestones and releases remain the operator's. The subject hash comes from the record file or from `--json` output of the command that wrote it; `vericairn records hash <file>` prints it. A refused acceptance lists every reason at once and writes nothing.

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

A release of a software product is recorded as intent before effect, through a release provider: a module that builds a release on a destination, observes the run and the draft, and names the operator's manual publication without ever running it. 0.8.2 carries one, the GitHub Releases provider (`github-releases`), which dispatches a GitHub Actions workflow and observes it through the GitHub CLI logged in as you on the runtime's machine; Vericairn never sees or stores the token, and never publishes. `vericairn release providers` lists the providers and adapters this build carries, each with the settings it declares, their defaults and the setting that names the destination.

A release goes through the provider its request names (`--provider`) or the one its product declares, and is refused when neither does. A product declares how its releases are made in the optional `release` object of its product record: the destination (the provider and the settings the product fixes, for GitHub at least `repository`), the checksum asset, and the artifacts a complete release carries, each a name with `{version}` and optionally `{platform}`, a role (distributable, bill of materials, notices, documentation or other) and its platforms. Without declared artifacts, a generic rule reads the draft: a platform identifier in an asset's name gives its platform, and a draft is complete when its checksum file names every other asset. `software.md` has the record's shape. Published releases before 0.4.2 refuse a product record that declares a release profile, so an estate records one only after every clone that writes to it runs 0.4.2 or later.

```sh
vericairn release providers
vericairn release submit --product <product id> --version 0.8.2 --commit <exact-commit> --expected <current-product-hash> --by <builder id> [--provider github-releases] [--repository owner/name] [--ref main] [--workflow release.yml] [--setting name=value]
vericairn release observe --version 0.8.2 --product <product id> --by <builder id> --qualified linux-x64 --qualified osx-arm64 --qualified win-x64 --limitation "..."
```

`release submit` resolves the provider and settings from provider defaults, the product and the request,
refuses missing required settings, checks built-in conditions, living documents and applicable policies, then records intent before dispatch. It records
`dispatched`, `uncertain` or a known failure according to the observed effect. `release observe` records
run outcomes and candidates from complete drafts, then publication only after operator acceptance.
While a run or draft is pending it records no candidate; recovery of a missing receipt can still record
uncertainty. Platform qualification and artifact hashes belong to the observed release record.

FIX-15 claims `<provider>:<destination>:v<version>` under the service gate before calling the provider,
then releases the gate during provider I/O so unrelated requests can answer. A receiptless `not-started`
intent has an unknown effect and blocks another submission, including after restart or failure to store
the receipt. `release observe` records it as uncertain before querying the provider, even if observation
then waits for a run or draft. Observe and reconcile the original identity before retrying. A known
failure or cancellation needs `--reconciliation`; that retry pins its settings and records an uncertain
claim before dispatch. An active provider call cannot be canceled or reconciled until it returns.
A gh refusal before workflow-command launch is a known refusal; interruption after launch or failure to
identify the run is uncertain. These rules are not a distributed exactly-once guarantee between separate
runtime instances.

FIX-23 selects each living document's own repository commit. Documents in the release repository
use the explicitly prepared release commit at submission and the actual build commit at observation.
For another registered repository, the gate uses its HEAD, or the latest clearing review's commit
when Git proves that HEAD is its ancestor. Covered paths and cleared bytes use the same selection.
Changed bytes, covered paths, absent reviews and unreadable registered clones refuse the applicable
gate. A refuse-level document whose repository has no clone registered here also refuses, naming the repository and
`vericairn sources clone <path> --resource <id>`; only the operator's signed, expiring grant of scope
`release-document-uncompared:<declaration>@<version>` to the submitting principal excepts it, and the release evidence
names that grant. A warn-level document without a registered clone is reported as not compared. A refuse-level policy
with unavailable required source bytes still holds the release. No gate fetches or checks out sources.
Normal `docs living` uses clone HEAD, and estate files use the estate's working tree. Current estate
records supply triggers and reviews; they are not a historical estate snapshot at the release commit.

POLICY-03 combines built-in objections, living-document gates and applicable policies before a build
can start. Format 3 requires `--commit <exact-commit>`, `--expected <current-product-hash>` and an
admitted held signing device. A refusal reports all known objections and starts no provider operation.
Sources, authority, exceptions and deadlines are rechecked after preparation and before dispatch.
The retained basis is inspectable with `release basis <evidence-hash>`. A preview grants no submission
authority. An effect already dispatched remains under its original identity if later observation fails;
a client cancellation does not roll it back. See [estate policies](policies.md).

The repository adapter and release provider share one bounded runner: 30 seconds per adapter Git
command, two minutes per provider gh command, and
4,194,304 characters per stdout or stderr stream. The deadline includes launch, concurrent output reads
and exit. Overflow fails instead of accepting partial output. Timeout or cancellation terminates the
owned POSIX process group or Windows Job Object, including descendants holding output pipes. Cleanup
waits at most one additional second; unconfirmed exit retains native ownership for later cleanup.
On POSIX the runtime must run under an init process, not as PID 1, without ignored `SIGCHLD`,
`SA_NOCLDWAIT` or a separate child reaper. Unsupported hosts refuse before launch. Process-group escape
and the existing containment limits still apply.

The operator accepts the exact candidate before publishing:

```sh
vericairn records new acceptance --by <operator id> --subject release:<candidate id>:<candidate hash> --body accept-release.json
gh release edit v0.8.2 -R owner/name --draft=false --latest
vericairn release observe --version 0.8.2 --product <product id> --by <builder id>
vericairn release list
```

The published revision is refused without the operator's acceptance of the candidate it supersedes. An uncertain identity first needs observation and reconciliation; a retry is available after known failure or cancellation with `--reconciliation`. `--cancel` records reconciliation that an intent was never dispatched or an uncertain effect did not occur; an active provider call refuses cancellation. The runtime does all of this behind `POST /v1/releases/submit` and `observe`, so the command line and Workbench's release workspace (section 13) do the same thing and refuse the same things.

For Vericairn itself, publishing the GitHub release triggers the separate NuGet Trusted Publishing workflow, including for a published prerelease. It retrieves exactly the package assets the operator publishes, verifies them against that release's own `SHA256SUMS`, and uploads without rebuilding, using short-lived credentials. It does not compare with the estate's accepted candidate or require its acceptance record. Publish the exact accepted draft to preserve that binding. The packages are `dotnet-vc`, the `Vericairn` SDK front door and the `Vericairn.*` libraries. A retry uses `--skip-duplicate`; it cannot replace a package version already present or verify that existing copy's bytes. `install.md` covers both package and archive installation. FIX-13 checks every workflow's action references
against the repository's full-SHA allow-list. The NuGet pull-request job builds and verifies packages and
symbols with the publication verifier, without a login, token permission or upload. This does not verify
the organization's live Actions settings or bind the published draft to estate acceptance.

### Withdrawal and moving planned work (0.8.3 additions)

Withdraw a candidate at the exact revision you inspected:

```sh
vericairn release withdraw <candidate-id> --expected <candidate-hash> --provider <draft-provider> --reason "Replace this candidate." --by <principal-id>
```

This validates and records two signed records together: a release revision with
`status: withdrawn`, retaining its artifacts and other evidence, and an operation
with action `delete-release-draft` and status `not-started`. The reason and original
candidate hash accompany both. The provider is explicit; `--setting name=value`
records any additional settings it needs. The candidate's repository, product and
version are retained. No provider call or deletion occurs. An obsolete hash or
invalid record writes neither record; a storage interruption reports the receipts
already stored and leaves a batch journal to reconcile before retrying.

The operator deletes the draft at that destination. Under the returned deletion
intent, record the actual dispatch as a superseding `operation` with the same
subject, provider, action, identity, target and evidence, changing `status` to
`dispatched` and adding the actual UTC `dispatchedAt`. Use `records new operation`
with `--supersedes <intent-id> --expected <intent-hash> --subject
release:<withdrawn-id>:<withdrawn-hash> --by <principal-id> --body dispatch.json`.
An uncertain deletion is recorded as `uncertain` under the same identity. Then:

```sh
vericairn release observe --version <version> --withdrawal <intent-id> --by <principal-id>
```

Observation uses only that intent's provider and destination. A draft still present
stays pending; provider failures settle nothing. Observed absence records
`observed-success`. Ordinary release observation keeps a withdrawn candidate
withdrawn even if a publication later appears at the provider.

To move a release plan, inspect the current carrier and all its planned work orders
with `status --json`, `work observe <key> --json` and `records show <id>`. Prepare a JSON object
whose keys are the **current revision ids** of every planned member and whose values
are their hashes. A member is planned when it has no work status or its latest
status is `planned`, and it has no effective acceptance. Include every such member
whose typed release placement names this carrier's original identity. For example,
`planned-work.json` has the form `{ "<work-revision-id>": "<work-hash>" }`; use `{}`
only when there are no planned members. Move them to an existing carrier:

```sh
vericairn work move-plan <source-carrier> --expected <source-hash> --expected-work planned-work.json --to <destination-carrier> --expected-target <destination-hash> --by <principal-id>
```

Or change the source carrier's version and its planned members together:

```sh
vericairn work move-plan <source-carrier> --expected <source-hash> --expected-work planned-work.json --version <new-version> --by <principal-id>
```

The runtime compares complete membership and every supplied hash before preparing
successors. The prepared set is an optimistic snapshot: reread and prepare it again
after a conflict. A member moved, revised, accepted or started since preparation, an added
member, or either changed carrier causes a conflict and writes nothing. Every
placement passes the same signing, record and policy checks as `work place` and
generic work-order authoring. A retired destination or a published or withdrawn
target version refuses the complete move. Other work statuses retain their
placements. The version form also revises the source's typed `releasePlan`; the
carrier form keeps both carrier definitions. Results include every stored revision
and any refusal findings. Reconcile an interrupted storage batch before retrying.

## 9. Upgrading an estate

```sh
vericairn estate upgrade --to 0.8.2 --check
vericairn estate upgrade --to 0.8.2 --by <principal id>
```

Run with the binary of the version you are upgrading to, once the published release record of that version is in the estate. The plan shows the migrations the new version applies; applying pins the manifest to the published release with its artifact hashes, applies the record-format migrations as one reviewable change, and writes a migration record; a failed migration leaves the repository untouched. Commit the change; the bootstrap then fetches that release by hash.

An estate running 0.8.0 at format 3 applies no format step. From 0.8.2 (FIX-38) the upgrade appends its
migration record before it replaces the manifest, and replaces it atomically: an upgrade that stops
between the two resumes from that migration record when run again, and until then opening the estate
warns `upgrade.incomplete`. A release pin with no migration record is refused.

An estate running 0.7.3 at format 2 applies `record-format-3`. The plan names that step, the
published target and its artifact hashes. Applying it appends the migration record and changes the
manifest's release pin and `recordFormat` to 3 after the steps succeed. Existing record bytes, hashes,
signatures and original format rules are preserved. Reapplying the installed target makes no change;
a failed step or refused migration record restores the previous repository files. Downgrading the
format is refused. A format-2 runtime refuses the upgraded manifest before opening a writable estate.

Stop older runtimes and update every participating writer before using format-3 records. Installing
a binary alone neither changes the estate pin nor migrates records. A clone that has not received
the new manifest still has its old view, so coordinate synchronization and restart explicitly.
Device keys and their custody do not move with the upgrade.

An estate still at format 1 first applies `record-format-2`, then `record-format-3`. The first step
lists every unsigned admission present, including superseded admissions and revocations, by id and
hash. At format 2 or 3 such an admission counts only when that signed migration anchors it. Where
anything needs anchoring, the operator or a principal holding `administer` applies the upgrade with
an admitted signing device; `--check` shows the list first. No original record is rewritten.

The migration infers no policy, duty, writing entry, placement, authority or completed work. The
operator still approves exact policy definitions and adoption bodies, grants the builder's bounded
writing authority, and authorizes the declarations. The first-party policy proposals retain unresolved
publication, grant, carrier and gate inputs; complete and approve them before authoring. Readable
register adoption follows the upgrade and policy declarations. A legacy generated lineage also needs
its independently reviewed conversion; changing the manifest alone does not convert it.
See [record format 3](record-format-v3.md), [policy adoption](policy-adoption.md) and
[the adoption proposals](policy-adoption-proposals.md).


## 10. Export, restore, recovery and divergence

```sh
vericairn estate export --out my-estate.zip --by <id>
vericairn estate restore --from my-estate.zip --to <empty directory>
vericairn recover inspect
vericairn recover compare --with <another copy>
vericairn estate reconcile
```

An export carries records, objects, manifest and documents with an inventory, and no keys or authority; a restore verifies the inventory and produces a repository-synced estate without importing a private key. A device already admitted in those records can sign when its key is held locally; otherwise signed authoring requires a new device and admission. Optional-signature policy may still permit unsigned records. `recover inspect` says what this copy holds, which devices can author from here, work in progress and unresolved effects; `recover compare` lists records only in this copy, only in the other, or (never expected) differing. Two copies that both added records merge in git as file additions; what cannot merge by itself, divergent revisions of one record, duplicate keys, duplicate results, is listed by `estate reconcile` and settled with a reconciliation record.

FIX-14 (F-01) binds each restored file to the export inventory. Verification refuses duplicate inventory
paths or archive members (including metadata), unsafe relative paths and file/directory or normalized-name
collisions before creating destination material. Case aliases are refused on every platform; macOS also
checks canonical Unicode equivalence and case folding, and Windows checks its trailing-dot/space aliases
and device names. Restore keeps one archive open, extracts only the members it verified, rechecks each
buffer's size and hash before writing and creates targets without overwriting. The manifest's conversion
to the repository-synced profile remains deliberate.

Restore is not an all-or-nothing filesystem transaction. An I/O failure or source change during extraction
can leave earlier verified files in the destination; the operation does not report success. Inspect a
failed destination and preserve it as needed before choosing another empty directory. The inventory
does not establish who produced an export or grant authority. Record signatures are checked separately,
and no private keys travel with the export.

FIX-16 judges single records against the proposed estate, including content checks, before publication,
as grouped records already are. A validation failure publishes nothing; repository records are staged
outside the published tree and renamed only after judgment. The gate belongs to one `EstateService`
instance, not all services or processes. Route clients through one daemon, or stop it and run embedded
commands sequentially; do not overlap daemon startup, embedded commands or repository synchronization.
A separate service's reads can reconcile journals too. Grouped storage remains sequential and can stop
between records, with partially stored batches visible to independent readers.

Unreadable, malformed or incomplete local journal data remains visible as `batch.unreadable` in status,
check and reconciliation, with a safe journal id or filename digest. The runtime keeps the journal and
retries it on later checks and reopen. Because its effects cannot be identified, every further single
or grouped record write is refused with `reconcile-required`. Reads, local configuration and interim
live progress notes remain available. Restore readable journal data, or have the operator inspect the
stored records and preserve the damaged journal outside the local batches directory before retrying.
A readable interrupted batch still reports `batch.incomplete` under its existing rules; it does not impose
the unreadable journal's write hold. This is local recovery state, not a new record format.

Objects are content-addressed bytes beside the records: `objects put <file>`, `objects list`, `objects verify` reads every object and checks its bytes against its name.

## 11. The daemon

Commands run an embedded runtime unless a daemon serves the estate on this machine; ordinary estate operations are clients of it, so that one owner holds the live state. Restore creates another copy directly. `recover compare`, `docs notes` and `docs regenerate` read the files and live store directly, even when a daemon serves this estate.

```sh
vericairn runtime status
vericairn runtime start
vericairn runtime stop
vericairn runtime install --plan
vericairn runtime install
```

`runtime serve` runs the daemon in the foreground until stopped; `start` runs it in the background with streams of its own, its output going to `daemon.log` beside the estate's live store, and waits until it answers; when the daemon stops before it serves, `start` exits with an error that quotes what the daemon wrote to its log and says where the log is; `stop` asks it to stop (closing a client never does). `install --plan` prints the per-user service unit (a launchd agent, a systemd user unit or a logon task) and the commands without installing; `install` registers it so the daemon starts at login; `uninstall` removes it. The daemon serves a local socket (or a named pipe on Windows), whose trust boundary is the operating-system user, and a loopback TLS endpoint pinned to its own certificate, where requests are signed by an admitted device. A configured local-network listener adds mutual TLS for paired devices (section 19); `--no-loopback` disables loopback only, not that listener. Signing and authority are checked by the runtime; `api-v1.md` has the details and `api-reference.md` every operation. Workbench's runtime workspace (section 13) does the same through this command line and shows the plan before it installs or uninstalls.

From 0.8.0 loopback checks the signing time and the device's current admission before reading a request body. Both device-signed profiles cap bodies at 8 MiB (8,388,608 bytes), or a lower configured server limit, and allow at most 30 seconds to read one, shortened when the signature expires sooner. Signature verification follows the read; the headers alone do not authenticate a body. Admission and freshness are checked again, and a key rotation during a loopback read requires signing afresh. Size refusal is `413`; timeout is `401`. Refused bodies are discarded only within 64 KiB and ten seconds after the response completes. The local socket keeps its configured body limit, 30,000,000 bytes by default; pairing messages are limited to 16 KiB.

FIX-09 adds a shared limit of eight simultaneous loopback body reads awaiting signature verification across all devices, with no waiting queue; capacity refusal is `503`. Kestrel also limits concurrent connections to 32. These bounds limit resource use; they do not establish denial-of-service immunity.

## 12. Reading the estate

`vericairn status` renders the work orders with their kinds and states, the releases, the assignments with their attempts and outcomes, the living documents that are due and why (section 16), and counts of decisions, document revisions, resources and verifications; record-derived state agrees for the same records. Living-document checks also read covered files and installed contributions, so those findings can differ by machine. `vericairn decisions` lists decisions; `records describe` explains any record type and marks the format that introduced each field; `records validate` parses and validates everything; `estate check` adds the repository checks, living documents due at `warn` or `refuse`, generated regions edited by hand, and records a recovery device signed that no operator has reviewed; `doctor` checks the machine. Through the API, `GET /v1/records?type=<type>` lists the current records of one type, which is how Workbench reads products, operations and reviews and how the MCP tool `records_list` answers an agent; `GET /v1/documents` lists the living documents.

`vericairn progress` is the report of where the estate stands, work orders by state, published releases and their contents, planned releases, eligible work and what waits on the operator. `--json` gives `vericairn.progress/1`. Record-derived lines name their supporting records; latest local progress claims and worker observations are labelled separately and can differ by machine. The normal report includes repository-check findings; the generated document region omits those checks. Reading a report records and accepts nothing. `status --observe --by <person>` explicitly records a person's observation; ordinary status reads do not (section 21).

The release contents shown by progress and release notes omit superseded results. A current result is listed as accepted work when a current acceptance pins its exact hash or its work order's current revision and hash. An acceptance of an earlier work-order revision does not count; accepting the work order does not create an acceptance of each result record. Classification uses the estate's current records, including acceptances recorded after a release window.

FIX-13 groups accepted work under its latest accepted current result in the window, one entry per work
order, naming the acceptance. Earlier results and attempts are reached through `work observe <key>`; later
unaccepted results remain separate. Recorded time, then identifier, orders the selection. This changes
the rendering, not the recorded outcomes or the acceptance of individual results.

## 13. Workbench, the desktop

Workbench is a client of the daemon through the SDK and of the command line beside it for runtime management; it never hosts the runtime, evaluates no authority and signs nothing. Everything it shows comes from the runtime's responses in the same state language as `vericairn status`.

FIX-13 reduces Workbench's first-screen work by sharing status and progress observations, avoiding a
duplicate refresh after daemon startup and reusing parsing and hashes for unchanged original bytes.
First-screen API reads no longer grow with unshown record history, but opening still validates every
record once. Large estates can still take time. Wait for loading before treating an absent page as empty;
headless tests establish no fixed native startup-time bound or desktop responsiveness qualification.

Open an estate: `vericairn-workbench --estate <root>`, or type the root on the first-run page (recent estates are remembered). A directory without `estate/manifest.json` is refused and nothing is created. When no daemon answers, Workbench starts one through the command line beside it, waits for that command to finish (never for the end of its output), and reconnects. The target bar shows the estate, the pinned release, the runtime's state, the acting principal and the freshness of what you see. The acting principal is chosen among the admitted devices whose keys this machine holds, or named with `--as <principal id>`, and the target bar names it by its current name with the device the runtime signs its records with. With `--as`, the selector shows that principal's admitted device held here, and choosing another device in the selector changes the acting principal; when the named principal has no device held here, the bar says whether its writes are refused (the estate requires signatures) or written unsigned. Without an explicit acting principal or an admitted device held here, Workbench says it is read-only. With an explicit `--as`, the runtime decides whether optional-signature policy permits unsigned writes; the target bar states that condition.

Home shows attention: what needs a person, derived from status and waiting asks, in six kinds (decision needed, blocked, uncertain effect, failed check, incident, information), each with why it matters, its state, its freshness and the next action. Selecting an item explains it in the inspector; Enter opens its subject. A submitted result opens in the review and decision workspace.

The work screens: Products lists the product records with their editions and surfaces. A work order (open it by key with Ctrl+G, or from attention) is a page with its brief, its assignments with attempt counts and latest results, and its observation; an assignment is a page with the brief as delegated, the assigning note and every attempt with its result and state. The assignment composer (Ctrl+Shift+A on an open work order) takes the brief's fields, one list entry per line, and sends nothing until "Send the assignment" or Ctrl+Enter; the runtime validates and signs, and a refusal is shown with every finding. The result inbox (Ctrl+I) lists the results awaiting review across the estate; Enter opens one in the review and decision workspace, which shows the exact result (outcome, summary, outputs, evidence, checks, limitations, unresolved effects, next step), the reviews on record, a review draft (outcome, summary, findings) and an acceptance draft whose basis is the checked reviews. Both go through the API pinned to the subject's hash (Ctrl+Enter sends the review, Ctrl+Shift+Enter the acceptance); Workbench derives the independence and self flags it will declare from who authored the subject, shows them, and the runtime checks them. A prepared review or acceptance stays pinned to the subject revision and to the facts on record about it: checking or sending it reports reusable, needs review (new records about the subject; the draft is kept and nothing is sent) or withdrawn (the subject was superseded or is gone). A comparison view (Work menu) puts a record beside the revision it supersedes with the line difference between them.

The release workspace (Ctrl+Shift+E) runs section 8 from the desktop. Enter the exact Commit; the request carries the product hash captured when it was loaded. A stale product requires fresh preparation, and format 3 requires an admitted held signing device. The response displays policy holds and retained-basis inspection commands. The existing flow follows: the providers the runtime can reach are listed and the product's declared provider is selected (for a product that declares none, nothing is selected until you choose); the form shows one field per setting the selected provider declares, filled with the product's declared values, where an empty field means the provider's default, which its label names; then "Submit the release" or Ctrl+Enter records the intent and dispatches; each destination shows its preparation, submission, external review and observed availability separately; "Observe" or Ctrl+Shift+Enter records the run's outcome, the candidate with every distributable by hash, and later the observed publication; Enter on a destination opens the candidate in the review workspace for the operator's acceptance at its exact hash; the manual publication command is shown and never run; a second submission under one identity is refused exactly as on the command line, and a retry needs a reconciliation.

The theme binds the brand's roles, never values: Inter for titles, headings and body text and JetBrains Mono for identifiers, both bundled under the SIL Open Font License, one accent blue, one attention amber used only for the attention kinds, and neutral surfaces and outlines; the arrival page carries the cairn mark. The probe measures the contrast of every pair the roles form, in light and dark.

The Documents page (Ctrl+Shift+D) lists each declared living document with its state (current, due at its level, or unavailable), every trigger newer than its latest clearance, and that clearance. A review of the selected document is sent with Ctrl+Enter: the outcome, what was checked, a commit for a document of another repository, and findings. The runtime reads the content, records what the review answers and runs the declared checks; the page decides neither independence nor clearance, and a refusal shows every reason. It does not yet show a document's content beside what made it due.

Goals (Ctrl+Shift+G) shows the objective, sponsor and accepter, condition verdicts beside the evaluation, steps and their routes, budget and in-flight runs, asks and evaluations. It is a read view: use the CLI or API for activation, stepping, stopping runs, evaluation and acceptance. Home also shows each principal's delivered asks and the queued count. Enter opens an ask's goal, or its record when it has no goal; it does not answer it. The progress summary on Home opens the full report (Ctrl+Shift+P). F5 refreshes these views. Workbench opens local estates; it has no paired-runtime chooser or network-configuration form.

The runtime workspace (Ctrl+Shift+U) shows the daemon's state, where it is and what it reports about itself, with start, stop, the install plan, and install and uninstall of the per-user service after the plan has been shown.

Communication in the Work menu shows effective values, contributing revisions, conflicts, freshness and previews, and makes attributable changes for the explicitly named recipient. The hand-over workspace (Ctrl+Shift+H) prepares and retains a complete package, reads one back and shows the resume comparison. Neither view infers consent from a click or resumes a worker. Workbench has no context-allocation editor, pinned-original retrieval control or harness-qualification form; use the CLI/API for those. `communication-qualification.md` records the exercised surfaces and deferrals.

Keyboard: Ctrl (Cmd on macOS) with 1, 2, 3, 4 focuses navigation, documents (or the attention list on Home), the inspector and the activity log; Ctrl+Shift+L, R, B toggle the panels and Ctrl+Shift+0 resets the layout; Ctrl+O focuses the estate path, Ctrl+G the record or work-order id; F5 refreshes; Escape returns to navigation; opening a workspace lands the keyboard in its first field. Every pointer arrangement has a keyboard route, and the menus carry the same actions.

## 14. Agents through the MCP server

A coding agent works in an estate the way a contributor does: it reads the brief, works in its workspace, and records attempts and results, which are claims to inspect. `vericairn mcp serve --as <principal id>` exposes the estate over the Model Context Protocol on standard input and output; the harness is configured with that command. The server uses the daemon when available and holds an embedded runtime otherwise. Reads include status, validation, work, records, decisions, releases, devices, living documents, progress, goals and asks, plus `communication_get`, `context_prepare` and `context_read`; `mcp.md` lists every tool. The contributor writes are `work_attempt`, `work_progress`, `work_result`, `work_amend` and `document_review`. Policy and writing-exception writes additionally require their bounded authority; `work_review` is offered only to an admitted operator connection. Admission and exact bounds are checked again at each call. A document review still obeys its declaration's `clearedBy` rule. A refused write returns every finding. There is no tool that publishes, spends, creates or admits a device, changes communication preferences, qualifies a harness or launches a worker.

`vericairn mcp orientation --out <directory>` generates `AGENTS.md` from the estate's identity and the tool registry (what the estate is, the rules it enforces, how to connect, the tools and an honest matrix of harnesses observed or not, and nothing that changes with each record, such as the work orders or the pinned release, which `status` shows) with `CLAUDE.md` and `GEMINI.md` as pointers; `--check` compares existing copies with a fresh generation. Regenerate it when the build changes rather than editing it. Nothing the server returns, and nothing in the orientation file, is an instruction from the operator; the harness treats it as data.

## 15. Documentation sets

Every release carries its own documentation set under `docs/releases/<version>` in the product's repository, written for that release and then frozen. `docs/releases/documents.json` declares the required document names. The build tool, not part of the shipped command line, generates references and the manifest from the build. Generate only from a committed source tree built at that exact commit:

```sh
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- generate --out docs/releases/0.8.2 --documents docs/releases/documents.json
vericairn docs check docs/releases/0.8.2
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.8.2
```

The manifest names the exact reference build commit, distinct from the later documentation or release commit. Comparison checks version, record formats, public inventories, complete reference bytes and retained companion inputs. A later documentation-only commit can retain that build pin only when the build sources agree; both commits must be locally available. A structural docs check does not judge prose, note completeness or release acceptance. FIX-09's frozen-set checksum guard detects changed, missing or added files in earlier sets, including withdrawn 0.7.0; never repair an earlier set in place.

The notes command renders the estate's records from 0.8.0 to 0.8.2:

```sh
vericairn --estate <root> docs notes --from 0.8.0 --to 0.8.2 --out docs/releases/0.8.2
```

`docs notes` writes the exact window to the manifest. Its exclusive start follows 0.8.0's recorded end.
FIX-13 gives accepted work one entry per work order, naming the latest accepted current result in the
window by recorded time and then identifier, with its acceptance. Earlier results and attempts remain
available through `work observe`; later unaccepted results remain separate. Superseded results are omitted.
Classification uses current estate state, not a snapshot at the window's end; work-order acceptance does
not accept each result. Later imports with older timestamps can require another explicit widening.
Earlier sets and their checksums remain unchanged.

Documents that follow changing facts remain living documents (section 16).

## 16. Living documents

In 0.8.2, pin an authored estate document after editing it with one command:

```sh
vericairn docs pin docs/guide.md --by <principal id> --summary "Describe the revised workflow."
vericairn docs pin docs/guide.md --by <principal id> --summary "Update the examples." --expected <current pin id>
```

`docs pin` reads the file's exact bytes under `docs/` or `design/`, computes its SHA-256 and byte count, and records a `document-revision`. A first pin is revision 1; each later pin supersedes the current pin and increments its revision. `--title` overrides the title; otherwise it carries forward, or defaults to the file name without its extension on the first pin. Media type follows the file extension. The command prints the revision, record id and record hash; `--json` prints the record receipt. It leaves the document and its hand-written revision line untouched.

Unchanged bytes, a stale `--expected` record id, a whole generated document or generated register member, and a fork of current pins are refused with nothing recorded. A path outside the document directories or an unavailable or aliased file is also refused. Use `--expected` when acting on a pin read earlier. Two branches pinning the same predecessor leave a fork after their records merge; `estate check` and `docs pin` name both pins and the predecessor. Reconcile by recording which successor is kept and which are withdrawn, settle the text, then pin the combined bytes against the kept id. Nothing is deleted and no branch is chosen automatically. The [CLI reference](cli.md#vericairn-docs-pin) describes options, receipts and the matching `POST /v1/documents/pin`, `DocumentPin` and `DocumentPinAsync` operations.

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

A generated region is the content between `<!-- vericairn:generated <id> -->` and `<!-- vericairn:end <id> -->`, each on its own line, declared in `regions` with its generator. `estate check` regenerates every region of the estate's own files whose generator is installed and refuses a hand edit or lost markers (`document.generated-edited`); `vericairn docs regenerate <path>` rewrites them and nothing outside the markers. The foundation's generator `vericairn.records/releases` tabulates the published releases of the products its inputs name, newest first. `vericairn.records/progress` embeds the progress report without repository checks, so regenerating a document does not recursively read itself.

A document in another repository, such as a product's README, is declared with that repository's resource in `repository` and the software pack's source `vericairn.software/git-file`. Register this machine's clone of the repository with `vericairn sources clone <path> --resource <resource id>`; the path stays machine configuration and never enters a record. `covers` is then compared at that clone's head; without a registered clone, at the commit reviewed or released. A review of such a document names the `--commit` it read. Because external commits do not identify the content author in estate records, declare `--independence self`, `independent-agent` or `operator` truthfully; a builder defaults to `self`. An estate file's independence is derived from its recorded revisions. For a document declared at `refuse` on a product's releases, `release submit` reads it through the registered clone at the commit the release builds and refuses when its bytes differ from what its latest review cleared; the intent records what was compared. A declaration that names a source, trigger, check or generator no installed pack provides shows as unavailable (`document.unavailable`), and one with a `refuse` trigger closes its product's release gate until the pack is installed or the declaration is revised.

The software pack supplies exact retained reference and companion checks and reference-coverage policy evaluation. It has no semantic third-party-notice or README check and no automatic declaration proposals. Generated ownership can be retired through the existing checked retirement path. Workbench's readable views show renditions and exact sources; the authored Documents page still does not put its content beside its triggers.

FIX-13 judges repository covered paths and cleared document bytes at the exact commit the provider
resolves for the requested release ref. Clone HEAD may differ. An unresolved commit, a missing local
Git object or an unavailable required contribution refuses the applicable gate; fetch the exact commit
into the registered clone and review there. Observation checks the actual build commit again before
recording success or a candidate, because a ref can move after submission. This release-document gate fetches and
checks out nothing automatically. Normal `docs living` still uses clone HEAD; estate-file checks still
read the estate's working tree. Current estate records supply triggers and reviews, not a historical
snapshot of all estate state at the release commit.

## 17. Goals, predicates and evaluation

A goal states an objective, completion conditions, constraints, its sponsor and a budget. A proposal authorizes no work or spending. Conditions are record predicates, evaluated by the runtime, or evidence statements, evaluated by an independent principal from records it names. A predicate counts current matching records; `state` uses the derived state, while `fields` matches stored values. For example, an ask answered by a decision still has its original stored state, so use `state` to ask whether it is open. A condition involving an unavailable living-document source can be unknown on this machine. See `record-format-v2.md` for the complete predicate language.

```sh
vericairn goal propose --by <proposer> --sponsor <person> --title "Deliver the widget" \
  --objective "Complete W-1 and have its result reviewed." \
  --done '{"type":"work-order","fields":{"key":"W-1"},"state":"accepted"}' \
  --work-order <W-1 record id> --constraint "No publication." \
  --budget wallMinutes=30 --budget attemptsPerStep=2 --budget tokens=20000 --budget concurrency=1
vericairn goal show <goal>
vericairn goal activate <goal> --by <sponsor> --note "Authorize this revision and its stated budget."
vericairn goal next <goal>
```

`wallMinutes` and `attemptsPerStep` are required budget units. Optional units are `tokens`, `concurrency` and `currency`, the last as minor units and a currency name, for example `--budget currency=500:USD`. An omitted token or currency unit is not granted; concurrency defaults to one. Activation and resumption belong to the sponsor when a person, with the operator role or `spending` authority, or to an operator when an agent or service sponsors the goal. An agent cannot activate or fund it. A revision is a new proposal and needs fresh activation and evaluation. Resuming or revising does not erase charged usage: the ledger counts runs naming any revision of the goal; inspect its ledger before starting more work.

The goal's plan lists work orders, and work orders can join its work by naming the goal in their `inputs`. Dependencies (`after`, `blockedBy`) determine eligibility. An accepted work order releases `after`; blockers include unanswered asks, unfinished work orders, unresolved operations and decisions with undischarged obligations. `goal show` describes the conditions and evidence, while `goal next` also checks this machine's launchers, keys, routing and budget. Neither starts work.

```sh
vericairn goal evaluate <goal> --by <evaluator> --summary "Checked each condition against the named records."
vericairn goal accept <goal> --by <sponsor> --statement "I accept this goal from its current evaluation."
```

For record predicates the runtime supplies the evaluation's verdict and records read. For evidence conditions the evaluator supplies `--verdict n:met:reason` (or `not-yet` or `impossible`) and `--read n:<record id>`; numbering starts at one, and a met evidence condition must name a record. The evaluator cannot have authored any step. Acceptance needs the current evaluation of the exact goal revision, every condition met, and every record predicate still holding. The accepter is neither the evaluator nor a step's author: the sponsor when a person, an operator when an agent sponsors it, or the sponsor's named delegate holding `accept-work` for an ordinary goal without reserved actions. A delegate never accepts a goal involving a release or milestone.

`goal pause`, `resume` and `stop` record lifecycle changes. The sponsor may pause or stop; an operator may also do so for a goal an agent sponsors. A stop ends the goal as abandoned. Pausing or abandoning a goal prevents new runs but does not stop runs already in flight: use `worker stop` for each run that must stop, as section 18 shows. In this build `goal propose --deadline`, recorded as `limits.deadline`, and `limits.consecutiveFailures` are validated and recorded but not yet enforced. Monitor them and act explicitly. Worker thresholds and the silence limit do operate; `limitations-and-platforms.md` describes their implementation limits.

## 18. Routing, asks and the worker

An operator or administrator records the estate's roster, one current record, with each role's principals in preference order:

```sh
vericairn records new roster --by <operator> --body roster.json
```

```json
{"roles":[{"role":"builder","principals":["<agent id>","<person id>"]},{"role":"reviewer","principals":["<reviewer id>"]},{"role":"decider","principals":["<operator id>"]}]}
```

A goal can name a roster or override roles; otherwise it uses the estate's current roster. A work order's role defaults to `builder`. Routing considers an agent only when its harness has a launcher on this machine, its admitted device key is held here, and budget remains. It does not silently select a principal outside the roster or replace the contributor who already holds the step. Reserved actions need their own authority. A routed person receives an assignment and an ask; when nobody is available, the accountable principal receives an ask without an assignment.

```sh
vericairn worker launchers
vericairn sources clone <local repository clone> --resource <repository id>
vericairn goal next <goal>
vericairn ask --to <principal>
```

`goal step` takes the first eligible route that can act, one step per call. An operator, the sponsor when a person, or a principal with `local-engineering` records it. An agent's assignment is validated with its run intent and stored only after the worker's preflight; a person's assignment is validated with its ask. Storage of the pair is journaled but not atomic: a stranded assignment holds no step, and `estate reconcile` reports an incomplete pair. An existing agent result goes to review; a different principal's review with findings or failure returns it for another attempt. Asks are answered by a decision of the principal asked, with the ask id in `answers`, not by reading them, dismissing attention or waiting.

Before a native run or an agent `goal step`, configure each launcher used on the worker's machine. Choose the model selector and an effort from that launcher's native vocabulary:

```sh
vericairn worker configure --launcher codex --model <model> --effort max
vericairn worker configure --launcher codex
```

Use `--launcher claude-code` with a supported effort for Claude Code; `max` is a Codex choice. Setting requires both `--model` and `--effort`; omitting choices shows the current configuration, with `--json` available. Repeated `--allow-model` and `--allow-effort` options narrow the choices and must include their defaults. Without a model allowlist, only the default model is allowed; without an effort allowlist, the launcher's supported efforts are allowed. Each set replaces that launcher's defaults and allowlists. Configuration can narrow, never widen, the qualified inventory: it cannot add tools, MCP servers, environment variables or routes.

Configuration is per machine and is never recorded in the estate. The owner-only files are `worker/codex.json` and `worker/claude-code.json` under the user's Vericairn home. Reading or writing them is refused when that home is inside any Git repository or estate. If your home directory is a repository, set `VERICAIRN_HOME` to a machine-local directory outside all repositories and estates for the CLI and daemon before starting them. The configuration file, its worker directory and the Vericairn home cannot be symbolic links or reparse points. The CLI and daemon read the file for every run and agent goal step and recheck selection before intent and dispatch. Changing its choices needs no daemon restart. Missing configuration names the setup command and refuses before intent; invalid configuration and unsupported choices also refuse, without ambient fallback. The command calls no API or MCP tool, runs no probes and launches no agent.

If the user's home contains `.claude` or `.codex` settings, its default workspaces inherit that configuration and native runs are refused. Set a workspace root outside that home before running:

```sh
vericairn worker configure --workspaces <absolute-directory>
vericairn worker configure
```

Omit `--launcher` for the workspace setting. It applies to every launcher and is stored per machine in the owner-only `worker/workspaces.json` under the Vericairn home; its path is never recorded. The root must be absolute, outside Git repositories (including bare ones) and estates, and cannot itself be a symbolic link or reparse point. Linked ancestors are resolved and the canonical root is stored. Neither root nor ancestors may hold `.claude`, `.codex`, `.agents` or `.mcp.json`. New roots are created owner-only. Missing configuration preserves the default; malformed files, duplicate keys and unknown fields refuse without fallback.

New workspaces use `<root>/<estate>/<branch-slug>` with branch slashes flattened to hyphens. Without this setting they remain under `<VericairnHome>/estates/<estate>/workspaces/<branch-slug>`. The CLI and daemon reread the root for each run and recheck before intent and dispatch, without a restart. A change after intent settles as an unstarted, uncharged failure. A branch's existing independent workspace is reused only under the selected root. For one elsewhere, preserve the work before removing that directory, or select a root containing it. A legacy linked worktree requires preservation and `git worktree remove` before retrying. Setting a root moves or deletes nothing.

Codex independently checks the user's own `~/.agents`, outside `CODEX_HOME`; a run is refused while that directory supplies skills or other customization, even after relocating workspaces. Use a worker account without this discovery root. The native scan permits an empty ordinary directory or recognized instruction files alone, but refuses other entries, child directories and uninspected links. Workspace-root validation itself refuses any `.agents` marker in the root or its ancestors. Other unqualified configuration and managed policy remain refused.

For an existing assignment, select its launcher and configuration explicitly (`codex` is also the stock configuration name):

```sh
vericairn worker run <assignment> --launcher codex --agent <contributor> \
  --by <authorized principal> --repository <repository id> --goal <goal> --wall-minutes 10 \
  --configuration codex
vericairn worker runs
```

To dispatch the next eligible goal step instead:

```sh
vericairn goal step <goal> --by <authorized principal> --repository <repository id> --wall-minutes 10
```

`claude-code` and `codex` configure the harness's native goal mode and the estate's stdio MCP server. Generic dispatch is refused because it has no complete environment inventory. Install and authenticate a native harness separately. The compiled baselines are codex-cli `0.159.0-alpha.12.1` and Claude Code `2.1.296`; no adjacent version or other alpha build is implicitly qualified. Codex must expose its goals feature and the complete supported feature inventory. Settings listed by `worker launchers` include local command overrides and `stall-seconds`. Inventory probes and stand-in tests do not establish live inference, sign-in or provider billing.

FIX-18 admits Codex's `max` effort as well as `none`, `minimal`, `low`, `medium`, `high` and `xhigh`.
The harness checks whether the selected model supports the effort at launch; an observed refusal settles
as `known-failure`. The launcher enables `code_mode_host` for ordinary shell and MCP tools while
`code_mode` and `code_mode_only` stay disabled. It reads model and effort from the run's own rollout after
exit, never assistant prose or ambient sessions; missing or unreadable facts stay unknown, and mismatches
remain discrepancies. A run with no new attempt by the agent on its exact assignment gets the
`attempt-missing` settlement warning and review ask, without changing the meaning of `observed-success`.

Only the estate MCP server's tools are pre-approved, in both generated configuration and launch overrides.
Global approval policy stays unset; the server still checks the run principal's authority. The baseline's
MCP list omits approval settings and tool lists, so approval verification rests on the exact generated
configuration and isolation checks, not an independent report of those settings.

Seven builder live runs with codex-cli `0.159.0-alpha.12.1` exercised the selected model at `max`:

| Run and code commit | Observed outcome and its limit |
|---|---|
| First, `6bab07f` | The session reported model and max effort; 1 wall minute and 34,573 tokens were recorded. Every tool call failed with the code-mode host disabled. No attempt or result was recorded. |
| Second, `80423b5` | The first shell command ran, rollout model/effort matched admission, and settlement raised missing-attempt attention. The old audit stopped an outside-workspace read. |
| Third, `42f989d` | The workspace search and brief read ran. The old audit mistook a Python heredoc division operator for an outside write target and stopped the run. |
| Fourth, `8e272de` | The corrected audit warned for opaque Python input without stopping it. The run settled observed-success, with 2 wall minutes and 95,406 tokens; MCP approval under exec's never policy prevented an attempt/result, and no file changed. Missing-attempt attention appeared. |
| Fifth, `531d169` | The cap was recorded before launch, the admitted model and max effort were observed, and usage was 3 wall minutes and 205,767 tokens. Estate-only pre-approval allowed a signed attempt and result. Exactly the permitted `NOTES.md` changed (40 bytes). The commit failed at the sandbox boundary. |
| Sixth, `eb12a8e` | The cap, model at max, usage and signed attempt/result were recorded, and exactly `NOTES.md` changed. The independent repository alone was insufficient: Codex still protected its `.git`, so the commit failed. |
| Seventh, `36c90ba` | On macOS, Codex committed exactly the permitted `NOTES.md` with only its own `.git` added as a writable root. Settlement audited that commit and fetched its branch into the registered clone, whose `.git` remained unwritable by the agent. |

These runs qualify the reported live Codex path: inference at max, ordinary tools, rollout observation,
missing-attempt attention, brief access, the corrected audit and estate-only MCP attempt/result authoring,
and the seventh run's sandboxed commit and audited branch transfer on macOS only.
They do not qualify arbitrary commands, other versions, models, accounts or platforms, Claude Code live
sign-in, provider billing, hard spending caps, compaction/recovery, remote managed policy or acceptance of
an agent's work. Stand-in protocol coverage and the release's three-platform CI remain separate evidence.

FIX-19, retained from 0.7.3, gives each workspace its own repository. Its index, refs, logs and new objects stay
inside its own `.git`; it borrows the registered clone's objects read-only without importing remotes,
configuration or hook templates. Codex adds exactly that workspace-owned `.git` to its writable roots,
never the registered clone's Git administration. Linked or shared Git administration is refused.
The seventh live Codex run committed successfully on macOS; this live qualification is macOS-only.

After recording settlement, the worker fetches the exact audited run-branch head through a disposable
bare view of its objects. Worker Git disables hooks, ambient configuration, non-file transports and
automatic maintenance; it does not load agent-controlled repository configuration. The clone's branch
updates only if its old head still matches preparation. A concurrent move, deletion or creation,
a checked-out destination, a changed workspace head or incomplete scope audit refuses transfer.
Outside-scope commits are retained with their scope warning for review. A transfer refusal leaves the
workspace intact and adds a local `audit-unverified` warning to worker observations, without rewriting
the settlement. The clone's working tree and index stay unchanged; no push or acceptance occurs.

Uncommitted changes remain in the workspace. Reuse requires the selected root and a consistent branch;
changing the root does not relocate or delete a workspace. Preserve committed and uncommitted work before
removing an independent workspace directory. For a legacy linked worktree, preserve it and use
`git worktree remove` from the registered clone before retrying; there is no silent conversion.
Borrowed objects depend on the registered clone: preserve work before removing it or pruning objects
needed by the workspace. The scope audit does not inventory Git administration, prove attribution or
recover unreachable commits or reverted uncommitted changes. See [the software guide](software.md#coding-agents-the-worker-and-its-launchers).

An authorized human can qualify another exact version under RUN-03:

```sh
vericairn worker qualify --launcher codex --by <human principal>
vericairn worker qualify --launcher claude-code --by <human principal>
```

The person must hold the operator role or an operator-granted `qualify` scope. `administer` or `local-engineering` alone is insufficient, and an agent or service cannot qualify. The command uses the installed executable or an explicit local `--setting command=...`, imports only the permitted harness login into temporary isolated state and runs version, flags, tools, configuration, login-boundary and MCP probes. It starts no coding agent or MCP server. A successful signed `harness-qualification` record retains six probe digests and the exact version, with no paths or credential content. Every run still probes locally before intent and immediately before dispatch; a non-baseline version needs an active matching qualification, and the RUN-01 inventory policy must also match. The stock runtime derives that policy only from an inventory admitted by the shipped baseline or an exact current qualification. A host-supplied policy may refuse more but cannot admit an unqualified extension. Qualification supplies evidence, not local model/effort defaults, work authority or vendor attestation. It is machine-local: the local socket and signed loopback are available under the human authority checks; a paired local-network device is refused before probes or records.

To revoke a qualification, an authorized human writes a superseding `harness-qualification` with the same launcher, version and probe digests and `status: "revoked"`, using `records new ... --supersedes <id> --body <file>`. `records describe harness-qualification` lists the fields. Requalification is a new record from fresh probes; revoking one cannot revoke a compiled baseline or rewrite historical run admissions. Section 19 describes the machine-local boundary for qualification.

Run choices resolve through the local launcher configuration, roster defaults and role policy, goal defaults and role policy, assignment, then explicit run choices. Restrictions accumulate; later choices cannot relax a model/effort requirement, exact revision, destination, provider, data boundary or currency bound. `worker run` and `goal step` accept `--configuration`, repeated `--execution name=value`, `--input-tokens`, `--output-tokens`, `--context-bytes` and an optional `--context-package` identity assertion. Missing choices, unsupported effort, an unobservable required revision or an inventory mismatch refuse dispatch. There is no ambient model fallback. The intent pins choices, their sources and the admitted environment; observed execution stays separate, with discrepancies and unknowns retained. The efficiency Adapter is absent until EFF-01 in 0.8.3; enabling it is refused.

The worker creates or resumes an independent repository on `work/<work-order key>` unless `--branch` names another branch; `--base` chooses a new branch's base. The measured assignment brief is exported under an ignored `.vericairn` directory, with the complete machine context retained in local run custody. The intent reserves the run's cap before launch; observed usage settles it, and unreported units remain charged at their full cap. An exited harness is not an accepted result. Since 0.6.1, previously recorded settlement facts, including usage, scope and containment, cannot be rewritten. Evidence may be appended and missing facts added only under the record and chronology rules; the ledger retains the first settlement's usage and does not refund it through a later revision. An uncertain run cannot be retried for the same assignment until the original operation identity is reconciled; a new identity is not a bypass.

Write `permittedChanges` as actual relative paths or globs (`*`, `?`, `**`), for example `path:NOTES.md` or `path:docs/**`. FIX-18 refuses a brief with no interpretable pattern before intent. Bare patterns cannot contain whitespace; `path:` admits literal names containing spaces. Prose may accompany a valid pattern but grants no wildcard. Matching is ordinal and case-sensitive on every platform, and `**/` can match zero directories. FIX-10 uses deterministic dynamic programming rather than a regular-expression timeout, and isolates audit checks so an undecidable path check does not discard another check's verdict. The settlement audit compares the physical workspace and the run branch's head and all commits reachable from it but not from the prepared baseline, including merged side branches. Restoring a committed path in the worktree or a later commit, or checking out another head, does not hide it. It reports outside paths and any incomplete audit; missing history is incomplete, not clean. It is not a filesystem sandbox and cannot attribute concurrent changes, see reverted uncommitted changes or inspect commits no longer reachable from the run branch. The inventory and committed-history limits are in `limitations-and-platforms.md`.

Worker spending limits are soft. Wall-time, token and currency thresholds leave shutdown headroom, but a turn or failed shutdown can overrun it. Unknown usage remains charged, and an unobserved or uncontained run retains its reservation. A two-second heartbeat observes the process boundary. Silence on stdout produces an attention ask after `stallSeconds` and a stop request after twice that interval; the default is 600 seconds, with precedence goal, roster, launcher request setting, then default. Stderr and progress claims do not reset it. An ask records attention; it sends no external notification. Section 23 describes the additional command, loop and token-rate observations.

Before unattended launch, the custody guard checks every locally held key of an operator or the goal's sponsor, including revoked devices, for the ability to sign without presence. A separate operating-system user can isolate those keys; Secure Enclave custody with presence can enforce a prompt. The accountable person can instead record a specific expiring exception:

```sh
vericairn goal custody-exception <goal> --by <sponsor or operator for an agent-sponsored goal> \
  --reason "Why this goal may run here unattended." --expires <UTC timestamp>
```

The exception covers this goal revision, appears in each run's intent, and is checked again immediately before launch. It grants no extra spending or publication authority. A revised goal needs a fresh exception.

With `--detach`, a serving daemon supervises the run after the command returns. Otherwise the command waits; Ctrl+C requests a stop. Stop or hand over explicitly:

```sh
vericairn worker stop <run> --by <authorized principal> --reason "The operator is taking over." --grace 10
vericairn worker runs
vericairn work takeover <agent assignment> --by <person> --contributor <person> --note "Continue from the latest result."
```

A stop is allowed to an operator, the goal's accountable principal, or the principal that started the run. Request, transmission, acknowledgment, escalation and observed containment are separate facts. The command waits at most 30 seconds for observation and can return before containment; inspect `worker runs` again. A hand-over is recorded by a person: the person taking the step, its goal's sponsor, or an operator. It writes a new assignment carrying the brief and asks every in-flight run of the old assignment to stop. It does not certify those processes have stopped.

FIX-12 checks the current holder of the work order for every run, before workspace preparation and again under the write lock before intent. A new run on the old assignment is refused whether named explicitly by `worker run` or supplied by `goal step`, without an intent or budget charge. This closes the hand-over gap without treating a stop request as observed containment.

Worker launch and `goal step` are refused over the local-network profile; stopping a run is available there under the same authority rules. The full launcher settings, credential isolation, containment and scope-audit limits are in `software.md`; the platform summary is in `limitations-and-platforms.md`. No MCP tool launches or stops a worker.

### Command audit and settlement in 0.8.0

FIX-24 recognizes whole-home recursive copy, archive and sync sources, credential-helper actions
after their options, and additional outside write forms. Home-root listings, including recursive
listings, and per-clone `live.db` reads remain warning-only unless an independently recognized
credential target is present. Recognized credential access and outside writes request containment;
ordinary outside reads, network attempts and unverified effects remain warnings.

FIX-26 permits location-only home reads and matches the harness's own tools only to native identities
in its admitted inventory. This behavior belongs to the running worker: a worker still at 0.7.3
keeps its older audit and tool inventory even when it builds 0.8.0 source. Ignored build output and
untracked worker brief exports are excluded from settlement's file inventory; tracked files and
other untracked changes still count. Committed history remains separately audited. A complete
committed audit can retain the branch for review when the file inventory is incomplete. Timed-out
checks retry once; a credential check that still cannot finish requests containment.

FIX-25 removes a newly prepared, unused workspace after pre-dispatch refusal only when its complete
inventory still matches preparation. Reused, changed, concurrently used or incompletely inspected
workspaces are preserved with a warning. Windows opens use extended paths for the longer staging
name. This is guarded cleanup, not permission to delete work that cannot be proved unused.

FIX-28 publishes complete run-state and stop-request files before readers see them, coordinates
Windows readers and replacement writes, and keeps supervision alive through observation failures.
Unreadable observations remain unavailable; they never become proof of exit or absence of a stop.
A published stop request remains recorded even if its confirmation cannot be read.
The SDK also checks elapsed time when enforcing client deadlines. These corrections do not establish
new live-harness or native-platform qualification. The audit observes reported invocations, may run
after an effect, and cannot enforce credential isolation. See [software](software.md) and
[client operations](client-operations.md).

### Command audit and settlement in 0.8.2

FIX-34 removes quoting as a POSIX shell does before the workspace test, so a partly quoted
path such as `''"$HOME"/x` or `..''/x` resolves to the path the shell would use. `dash`, `ksh`, `mksh`,
`ash`, `yash`, `posh` and `eval` have their inner commands inspected. A program the captured environment
cannot resolve (`$RUNNER`) or a shell with another syntax (`fish`, `csh`, `tcsh`, `nu` and others)
warns with `audit-unverified`, and its `-c` body is still read. A recursive copy, archive or sync of the
home's `estates` directory, of one estate's directory or of one clone's directory is critical, like the home
itself; copies inside a workspace under an estate's `workspaces` directory are not. Commands a
script file runs, earlier shell functions and aliases, programs reached through a changed `PATH`, and
unclassified programs whose operands look like workspace paths still give no signal.

FIX-35 makes settlement exclude only what the run's starting ignore rules exclude: the base commit's
committed `.gitignore` files and the `info/exclude` captured before launch. An ignore file the run
changed or added, or a changed `info/exclude`, is never applied and counts as a change outside the brief.
A nested repository anywhere in the workspace, even under an excluded directory, marks the audit incomplete.
These belong to the running worker: a worker still at 0.8.0 keeps its older audit and settlement.

## 19. Pairing, local-network connections and revocation

Pairing connects another machine to a serving runtime; it does not clone the estate. The listener is off unless configured. Configure it on the runtime's machine and restart:

```sh
vericairn runtime configure --local-network <private IP address:port>
vericairn runtime stop
vericairn runtime start
vericairn runtime status
vericairn devices pair --principal <principal id> --by <operator or administrator>
```

A public or wildcard address needs the explicit `--allow-public` option; the normal profile uses a reachable private address. There is no peer discovery, firewall setup or relay. `runtime configure --local-network off`, followed by a restart, disables this listener. `--no-loopback` alone does not. The administrator issuing the code needs an admitted signing device on the runtime's machine. The code admits one device for the chosen principal and profiles (`local-network` by default); it has 16 secret characters displayed in four groups of four, expires after ten minutes, and is invalidated after five failed confirmations. Do not put the code in a record or repository.

Since 0.6.1 the code is bound to the issuing device's admission and the operator-role revision or `administer` grant used to issue it. Loopback uses the requesting device; the local socket uses the admitted device selected to sign admissions. Revoking the issuing device or losing or expiring that authority ends the code and its open exchanges; another device or grant cannot keep it alive. Readmission or restoring authority does not revive it: issue a new code. A rename, key rotation retaining admission, or authority revision that retains the role or scope without an expiry gap leaves it valid. The runtime checks again under the estate write lock before writing either pairing record; the admission note and issuance log name the issuing device and authority, never the code.

On the other machine:

```sh
vericairn connections add <runtime address:port> --code <pairing code> --name "My second computer"
vericairn connections list
```

The client proves the code before making its device key, verifies the runtime's identity and certificate binding, and saves the connection and identity pin in local configuration. No private key crosses the network. Normal connections use mutual TLS 1.2, a fresh runtime-identity proof bound to the connection, and device-signed requests bound to that runtime identity. An arbitrary TLS certificate cannot replace this proof. TLS 1.2 handshake certificates are visible to network observers, even though application messages are encrypted.

Synchronize clocks. Requests can be at most five minutes old, cannot predate this runtime instance's startup cutoff, and can be at most five seconds ahead. A slow client can fail just after a restart. Replay protection holds within one runtime instance; its process-local replay cache is lost on restart. The startup cutoff rejects older signatures, but a request signed ahead of the clock within the permitted five-second lead can be replayed after a restart before its signing time. Do not treat this as durable exactly-once execution; `api-v1.md` describes the bound.

The saved connection is used by the SDK (`RuntimeConnections.Connect`); the CLI's ordinary estate commands and Workbench have no saved-connection selector. A paired device acts for its own principal. The runtime signs its requested records with that principal's locally held admitted key; absent such a key, optional-signature policy may allow unsigned records, while required signatures refuse them. A key requiring presence is not used to answer a paired device's write. `vericairn devices writes [<device>]` lists the local audit of intents and outcomes. Local-machine actions such as launching a worker, issuing a pairing code, submitting or observing releases through the provider, exporting files, rotating runtime certificates or creating local device keys are refused over this profile; the API reference and `api-v1.md` enumerate the routes.

`POST /v1/harness-qualifications` is also machine-local. A paired local-network device receives `401` / `denied` before a probe or record write, even when acting for an authorized human or supplying a local command override. Use the runtime's local socket or signed loopback with the existing human qualification authority. Worker configuration is a local CLI operation with no HTTP or MCP write route.

Pairing is not crash-atomic. Inspect the server's device list after a failed or interrupted pairing. Validation refusals roll back the new device, but storage is sequential: a crash between the device and admission can leave an unadmitted device. A lost final reply can leave both records on the server while the client discards its key. Revoke an unwanted admission before pairing again. Re-pairing can replace saved connections; it does not revoke their old devices.

```sh
vericairn devices revoke <paired device> --by <administrator> --reason "This device is no longer authorized."
vericairn runtime rotate --compromised
vericairn connections remove <connection id>
```

Revocation is checked on the next request, including on an existing connection, and again under the write lock before an authenticated write. Rotation of a device key or withdrawal of its local-network profile also prevents a request authenticated under the old state from writing. Disconnecting or removing a saved connection revokes nothing and keeps the key in local custody. `runtime rotate` replaces the TLS certificate without changing the identity pin; `--compromised` also closes open connections and pending handshakes. If the runtime identity key may have leaked, `runtime rotate --identity` replaces that identity and every client must pair again. These commands are run on the runtime's machine. The Remote profile is not implemented in 0.8.2.

FIX-21, retained from 0.7.3, corrects the concurrent certificate first-use race present since 0.6.1. Loaded and
rotated certificates have their typed extension caches initialized before TLS listeners receive them,
including Server Authentication usage. When `DaemonOptions.Log` is configured, failed handshakes report
exception type and message, without stacks, inner exceptions, peer certificates or request data.
A daemon started through `runtime start` retains these diagnostics in its local log. Trust, authentication,
network and resource-limit failures can still reject a handshake. This changes no pin, permission,
protocol, rotation or replay rule. See [API transports](api-v1.md#transports).

## 20. Measured context and exact originals

```sh
vericairn context catalogue
vericairn context prepare W-1 --max-bytes <input allocation in bytes>
vericairn context read <vc1_ pin from the package> --original --out original.json
vericairn work brief W-1
```

Preparation selects required records and documents, then optional sources named by the work, its inputs or governing decisions. The rest of the document catalogue is explicitly outside that selection. Every selected optional original not fully included appears in the complete withheld list with its identity, reason, availability, measured or unknown size and exact retrieval route. "Nothing withheld" does not mean that the whole estate was read. Mandatory overflow or an unavailable required original refuses the package; no summary substitutes for it.

`--max-bytes` measures the final canonical UTF-8 payload including accounting and withheld-list overhead. Without that allocation, preparation and MCP briefs include mandatory material and withhold named optional bodies. The standalone human `work brief` retains its full selected scope with no limit set. These are input-byte allocations, separate from a run's spending cap. Tokens and model fit are unknown: this build has no qualified local tokenizer, makes no network tokenization call and does not infer tokens from bytes. A model's existing context, framing and reserves need separate accounting.

Pinned record retrieval verifies immutable originals. Required document originals use matching
working-tree bytes first, then FIX-27 searches the exact path in the estate repository's local Git
history for the pinned SHA-256 bytes. Their retrieval pins carry `history: local`; optional documents
and pins without that marker remain working-tree-only. Missing or shallow history and mismatched
bytes refuse with recovery guidance. Nothing fetches, checks out, substitutes a newer revision or
writes the estate. This changes original retrieval, not the context allocation or token accounting.

## 21. Hand-over, resume and explicit observation

```sh
vericairn handoff --out handoff.json
vericairn resume handoff.json
vericairn status --observe --by <human principal>
```

Hand-over is generated from the records: goals, work, assignments, attempts, results, runs, decisions, asks, next steps, the estate pin and recorded observations, followed by complete originals. It includes every record held by this copy, including superseded history; its withheld list is empty. Referenced file bodies, output objects and unrecorded provider state are outside that selection. Missing required references refuse the package. `--max-bytes` is optional and refuses overflow without truncation. `--out` never overwrites, and refusal creates no output file. The payload and human rendering are measured separately; neither claims model-token fit.

Resume compares retained identities with the current copy, including added, superseded, missing or changed records, the full estate pin and observations. An imported record cannot hide behind an older timestamp. The comparison never rewrites the baseline, starts a worker, grants authority or creates an acceptance. Even an unchanged comparison cannot establish that the external world is unchanged. Review current authority and reconcile uncertain effects under their original identities before continuing. A valid comparison exits 0; current errors or missing/changed originals exit 1; an invalid baseline exits 2.

An ordinary status read, agent read or repository probe records no human observation. `status --observe --by` is the explicit human-only operation; the runtime supplies the timestamp and snapshot identities. Without a recorded observation, its freshness is unknown. Format-1 estates must upgrade before that write. Workbench provides the local hand-over and resume view; transferring the package to another copy is an explicit file transfer. This does not implement Remote, mobile access or harness compaction/session recovery.

## 22. Personal communication and delegation

```sh
vericairn communication show --recipient <person> --preview
vericairn communication set --as <person> --status short --progress completion-and-need
vericairn communication set --as <person> --for-assignment <assignment> --handoffs standard
vericairn communication set --as <person> --status inherit
vericairn communication reset --as <person>
vericairn status --recipient <person> --depth comprehensive
```

The five independent fields are optional progress frequency, status depth, decision lead depth, result explanation depth and handoff introduction depth. Shipped defaults are `completion-and-need`, `short`, `comprehensive`, `comprehensive`, `comprehensive`. Each field resolves through shipped defaults, estate defaults, the person's setting in this estate, an assignment override and a one-view override. Missing fields inherit; progress `none` suppresses optional progress. No recipient is inferred when one is omitted.

A person changes their own setting. `communication delegate --as <person> --to <principal>` explicitly delegates that scope; a delegated edit names the actual author with `--as`, the person with `--recipient`, and the exact delegation with `--authority`. `--for-assignment` can narrow the delegation; `--expires` bounds it, and `--revoke --expected <current hash>` revokes it. No delegation grants spending, execution or authority over another person's preferences. Estate defaults require an operator or a principal holding `administer`. Changes and resets are signed records available from format 2 and retained in format 3, never edits or deletions. `--expected <hash>` guards a previously inspected revision; `--expected absent` requires none. Named-field edits otherwise read the prior hash and preserve unnamed fields. Stale or divergent edits return conflicts.

Depth changes the explanatory lead, not protected decisions, results, findings, evidence or hand-over originals. Canonical JSON, record bytes and saved packages remain unchanged. Required decisions, failed or unknown checks, blockers, uncertain effects, incidents and exhausted budgets remain visible at every frequency. CLI reads are on demand and show all progress; Workbench applies frequency to optional periodic claims. Worker briefs capture the human sponsor's profile, or explicitly use estate defaults for a nonhuman sponsor, and ask the agent to refresh at a settled boundary. External-agent compliance with that prose is not certified. Preferences select no model or execution policy.

`communication-adoption.md` is the first-party operator's explicit adoption procedure, not an automatic migration or a default for other people. It preserves the existing decision's identity. `communication-qualification.md` records fixtures, platform limits and deferred work; neither document claims Remote or the future efficiency Adapter.

## 23. Watching claims and observed runs

An agent can call MCP `work_progress` on its own attempt. Interim notes replace local live state without adding records; a completed phase is recorded once. An identical completed retry returns the existing phase, while changed content for the same completion is refused. Claims do not prove liveness, change scope, stop a budget counter or accept work. Hand-over retains completed records; interim notes remain local.

For a configured native run, WATCH-01 observes supported harness invocation events. Recognizable network commands, credential-store access, outside paths, unadmitted tools and execution discrepancies produce signals. Raw inputs remain in local output logs; records hold fixed summaries and hashes. Lifecycle events are deduplicated by invocation identity and canonical input, with a bounded recent cache. Opaque or malformed inputs make the audit incomplete; independent recognizable checks still run. Events can arrive after an action starts, so this is observation and stopping, not prevention of every effect or a filesystem sandbox.

The default loop window is 20 distinct invocations: five matching tool/input calls warn and ten request a stop. Default token-rate detection needs at least 60 seconds and 10,000 reported tokens; above four times the greater of the launcher baseline and recent goal median it warns. It becomes critical after 25 percent of the token cap; without a token cap, eight times the reference is critical. Unknown usage cannot establish a rate and remains conservatively charged. Recorded roster and goal `limits.watch` overrides are bounded, resolve field by field and are pinned in the intent; request settings cannot relax them. `software.md` lists the complete defaults and ranges.

FIX-18 distinguishes reads, known write targets and unclassified effects. Reads inside the workspace
need no change permission; ordinary reads outside it warn with `scope-read`. Changes inside the workspace
are checked against the brief at settlement. Critical `scope` requires a confidently identified write
target outside the workspace and the temporary directory supplied at launch. The audit grants no ambient
temporary boundary or one claimed in a tool event.

FIX-20, retained from 0.7.3, recognizes reads, copies and listings of the Vericairn home's `keys/`, `connections/`,
per-clone runtime certificate and identity files, and listings of the runtime directory. It also recognizes
`gh auth token`, `gh auth status --show-token`/`-t`, `git credential fill` and Git credential-helper `get`.
These produce critical `credentials` signals and request containment. `gh auth status` without token
output retains its network warning; `git credential reject` is not a credential-output command.
The audit resolves simple captured environment substitutions, assignments and known directory changes;
it reads no credential content. An unresolved target remains uncertain unless independently recognized.

Recognized outside writes now include in-place `sed`/`perl`, `rsync`/`install` destinations, archive
extraction and output targets, `patch` targets, `dd of=`, Git init/clone/worktree destinations and literal
interpreter writes. Known wrappers do not hide the inner command. Interpreter code still warns with
`audit-unverified` alongside any independently recognized critical signal. Calls in common quoted samples
or comments are not write invocations. Output to `/dev/null`, or `NUL` and `$null` on Windows, is exempt
from outside-write classification; other devices retain their treatment. Input redirects, heredocs and
here-strings are input, with interpreter bodies inspected as code.

Warning-only cases remain: ordinary outside reads, recognizable network attempts, unresolved expansions
or command substitutions, unclassified outside-looking operands, unknown directories, uncertain physical
containment, remote `rsync` operands, opaque interpreter input (including `osascript`) and `xargs` inputs
not visible in the invocation. An independently recognized credential target or literal outside write
still produces its critical signal. Git message text and Git object selectors are not filesystem targets.
The audit does not interpret archive contents, patch bodies, arbitrary programs, computed filenames,
aliases, every option or every shell expansion. It cannot prove that an unreported effect did not occur.
Events may arrive after execution: this is a signal, not an enforcement boundary. Operating-system
isolation, credential separation and the settlement scope audit remain necessary. Stand-in tests report
commands without executing them; they establish no new live-harness or platform qualification.
[Command observations](software.md#command-observations-070) gives the complete recognized forms and limits.

Warnings create attention; credential access, confidently identified outside write targets, unadmitted tool/execution changes, critical loops and critical rates request containment through the existing stop path. Attention is deduplicated by signal kind and severity. A failed attention write does not delay a critical stop; an unresolved write appears as `attention recording pending` and needs reconciliation. The final scope audit happens after containment and can only report past changes. Inspect `worker runs`, `progress` and Workbench for observations, asks and stop facts. Reservations remain until observed settlement; claims and a root process exit cannot establish whole-boundary containment. Remote watching is not built.

Settlement withdraws the run's still-open or notified process attention asks, including stall and
command-audit asks, as no longer needed. Withdrawals pin the exact settlement and preserve the question
and observations; already answered asks stay answered. Closed asks no longer produce stale-subject
warnings. The new `attempt-missing` and `scope-settlement` asks remain open for review, pinned to the
settled operation's exact revision and hash. The settlement, new asks and withdrawals are validated
together and journaled; interrupted storage still requires reconciliation. Withdrawal is not an answer
by the person asked and accepts no work.


## 24. Release notices and upgrades

`vericairn version`, `vericairn doctor`, `vericairn status` and Workbench disclose a release check
enabled by default. It asks GitHub's `Vericairn/vericairn` latest-release endpoint for the newest
published release, at most once per rolling 24 hours across both clients using the same Vericairn home.
Failed attempts also count. Workbench checks at opening and periodically while open; it never waits
for the network on its UI thread. The daemon, MCP and ordinary SDK estate calls do not initiate checks.

The request contains no credentials, installed version, user, machine or estate identity, estate data
or usage telemetry. GitHub requires the constant `User-Agent: Vericairn` header. Cookies, redirects and
trace propagation are disabled. The check reaches only GitHub and ignores system proxies. Drafts,
prereleases and invalid responses do not replace the cached answer.
Network failures are silent and preserve previous knowledge. A check never fails an otherwise working
command, installs software, authors a record or moves an estate's pin.

CLI disclosure and notices go to stderr, also with `--json`. The existing stdout and canonical status
document remain intact. Workbench shows the disclosure and any newer-release notice in its shell,
including before opening an estate. Notices name the version, release notes and all upgrade routes.

### Machine-local state

Use `vericairn updates configure` to see the setting, or add `--json` for `{"enabled":true}` or
`{"enabled":false}`. Disable requests with `vericairn updates configure --enabled false`; re-enable them
with `vericairn updates configure --enabled true`. For NuGet installations substitute `dotnet vc` for
`vericairn`. Configuring the setting itself makes no request. Disabling retains cached information, which
may still produce a notice or an estate warning. An unreadable or invalid setting also prevents requests.

`updates/settings.json`, `updates/check.json` and the coordination lock live under the user's Vericairn
home, outside repositories and estates. The cache holds the release tag, last attempt time and last
successful answer time. The home defaults to the platform's per-user application-support directory
(`Vericairn` on Windows and macOS, `vericairn` under the XDG configuration directory on Linux).
`VERICAIRN_HOME` can select another machine-local directory. Do not commit this state or put the home
inside an estate or Git repository. Clients share the daily limit through the saved timestamp and lock;
if they cannot safely save an attempt, they skip the request.

### Installing and changing an estate's pin

Read the linked release notes. For the NuGet CLI, run
`dotnet tool update --global dotnet-vc --version <published-version>`; omit `--global` for a local tool.
For archive installations and Workbench, download the archive for your platform from the linked release
page and verify its hashes as the release's installation guide describes. These actions update the
installed client. They do not update an estate's manifest.

`vericairn estate check` separately warns with `manifest.release-behind` when an estate is pinned below
the newest published stable release it knows. It reads the estate's release records for the manifest's
repository and, for `Vericairn/vericairn`, the local release cache. It makes no network request. Unrelated
products, candidate releases and prereleases do not count. An equal or higher pin, a source pin, or no
known release produces no warning. The finding is always a warning, so it does not fail the check.

Once the target's published release record and required evidence are in the estate, use that release's
CLI to review `vericairn estate upgrade --to <published-version> --check`, then apply it with
`vericairn estate upgrade --to <published-version> --by <operator-id>`. The existing upgrade checks,
authority rules and migration record still apply. A cached network answer grants no upgrade authority.


## 25. Current required attention

FIX-22 derives required attention from obligations still open in the available records. Every depth
and frequency, including `none`, includes compact items with complete source and subject references,
current state, recorded freshness, reason and an inspection command. Original records and evidence
remain complete behind those references; the timestamp is evidence freshness, not live provider health.

A later passing verification resolves only the same exact subject, method, environment and command.
A later result under the same exact assignment, acceptance of the exact result, or acceptance of its
work order's current revision resolves result attention. A later review of the same exact subject,
acceptance or supersession of that subject, its typed withdrawal, or acceptance of its current work
order resolves review attention. Retiring a work order does not withdraw the current result a review
judges. Operations resolve through their own settled revisions. Ordinary `after` planning stays in the
held list; open `blockedBy` relations and paused, retired, withdrawn or failed predecessors require attention.

These rules do not infer disposition from prose, an unrelated acceptance, a changed implementation or a
release. A changed verification environment does not match an old check. Missing records, mismatched
pins, unavailable sources, malformed projections or incomplete resolution chains keep the affected item
unknown. Quiet settings, acknowledgement or dismissal resolve nothing. Open asks retain their queue;
worker signals can remain continuity concerns after the operation settles. There is no new dismissal,
acknowledgement or expiry state. [Required attention](required-attention.md) states the exact rules.

Canonical status JSON derives attention from records alone and is identical on machines with the same
records. Progress also reads this runtime's worker observations; human status, progress and Workbench
label additional concerns `Local worker observations`. No reader can infer another machine's unrecorded
facts. The estate observation's existing status-hash contract is unchanged; these API v1 projections add
no record fields, CLI commands or permissions.

## 26. Policies, writing entries and recurring duties

Use `policies kinds`, `policies list`, `policies show <key>` and `policies check`
to inspect installed rule versions and their basis. Ordinary reads use the greatest
valid record timestamp, so an idle estate's recorded time need not be today's time.
`--as-of <UTC-instant>` and `--release <release-id>` make an explicit preview;
neither authorizes submission. Sources, implementation identities and unavailable
inputs remain visible. Status, check and progress expose the same policy and writing
evidence for the same inputs. A zero error count does not mean there are no release holds.

At format 3, an operator's signed decision approves the canonical policy definition
with only `basis` omitted. Management authority, exact definition approval and an
admitted signing device are distinct requirements. Revisions and retirement carry
expected hashes. An exception names exact unmet targets and expires exclusively;
it cannot excuse unavailable evidence, complete a duty or override built-in rules.
Writing entries quiet only exact case-sensitive `writing.spacing` occurrences in
their scopes and keep those occurrences visible. Read [the operating guide](policy-adoption.md)
for commands, strict bodies, lifecycle and duty result/review transport.

Duties are manually carried out under authorized assignments. Every occurrence
keeps its due time, grace deadline, proof requirements and old debt. A result must
bind the exact originating policy revision and occurrence, pass its declared
checks and supply the required native proofs and exact independent review.
An exception or late completion never erases the missed deadline. There is no
automatic scheduler or spending authorization in a policy read.

POLICY-04 supplies operating guidance and [first-party proposals](policy-adoption-proposals.md).
Their twelve initial policies and seven duties are not activated by installing
a release. Exact adoption approval, the builder's writing grant, publication cohort,
release carriers, faithful gate interpretations and review capacity remain operator
and adoption work. Readable-register adoption follows those declarations.

## 27. Readable companions

`docs companions --json` lists installed coverage and exact collection/manifest
snapshots. `docs render <generator> --selection item --revision <id>` reads one
immutable revision; collection reads supply their explicit snapshot. `--sources`
inspects the authorized basis without generating. `docs regenerate <path-or-id>
--compare` compares complete membership and bytes without repair, output pins,
authored clearance or a journal write. External comparisons require an exact
commit, never an unrelated clone HEAD.

Six revision registers cover decisions, work, releases, policies, policy exceptions
and writing exceptions. A signed generated declaration owns a bounded pattern,
with one file per source revision; independent records add disjoint files. Whole
strict generated members need no output pins or authored reviews. Authored text
and generated regions retain their pins and review rules. Legacy conversion needs
an independently reviewed complete preview. Failed rendering writes no records or
output; an interrupted later publication retains guarded journal recovery and may
leave already-stored records visible. Preserve uncertain recovery state.

The CLI, API, SDK and four MCP read tools apply access before rendering and source
disclosure. Only the authorized Internal projection is supported. Local inspectors
stay local; Public and Members projections are unavailable. Workbench's Work →
Readable views (`Ctrl+Shift+V`, or `Cmd+Shift+V`) displays renditions beside exact
sources, supports keyboard navigation, and clears content after an unavailable
refresh. [Readable companions](readable-companions.md) specifies currentness,
pagination, source bounds and the difference between inspection and acceptance.

## 28. Operator records and checked changes

`operator list` shows the complete recorded operator queue, including asks beyond
one notification delivery, exact commands, required input and holds. It answers
nothing. With the operator's own admitted device, `operator record` signs one
record, commits it together with its required generated register members on an
isolated branch, and uses the recorded checked push, opening, merge and target-check
flow. Repository-synced format 3 and a product's
exact `changes` declaration are required. Declared decisions and releases registers
use the same generators as `records new` and the estate check. The commit check
accepts exactly the record and its members with their expected bytes. Resuming
reuses the stored signed record and members without signing again; interrupted
member publication recovers from its original journal when the estate reopens.
The queue and one-step command are not MCP tools.
[Operator actions](operator-actions.md) explains recovery with the same
body and identity, presence, authority and the separate uncommitted receipts.

`changes push`, `open`, `merge` and `observe` require repository-write authority.
The push scans the exact complete commit range using installed Gitleaks 8.25.0 or
later and Vericairn's own rules, which keep none of gitleaks' built-in allowlists;
missing history, scanner or a complete report refuses it. Merges require a recorded
scanned push of the pinned head, and every declared check and every reported check
to pass at that head; missing, skipped,
pending or stale checks refuse. The provider performs a synchronous merge with
an atomic head condition, then observes target checks at that merge commit.
Pending or failed target checks remain incomplete even after a confirmed merge.
Fork pull requests and merge queues are unsupported. No retry can bypass an
uncertain effect. [Checked changes](changes.md) gives the complete reconciliation
rules; passing checks supply neither acceptance nor publication authority.

## 29. Cancelling client observation

Every SDK estate operation has an asynchronous counterpart with cancellation and
a per-operation deadline. Workbench exposes Cancel and Escape while keeping draft
editing and navigation available. Interrupted observations mark retained data as
cached and reject late replacements. A write that may have reached the runtime
remains uncertain, even when the client times out or stops waiting. Reconcile its
original identity before another effect; clients do not automatically retry it.

The CLI binds Ctrl+C for ordinary requests and uses exit 124 for timeout, 130 for
cancellation and 2 for unavailable transport or definite refusal. Diagnostics go
to stderr without replacing normal JSON. Worker following and runtime serving
retain their explicit-stop behavior. Synchronous SDK methods retain their prior
contracts. [Client operations](client-operations.md) and [CLI outcomes](cli-client-outcomes.md)
describe deadlines, refusal and uncertain outcomes across transports.
