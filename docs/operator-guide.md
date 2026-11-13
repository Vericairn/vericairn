# The operator's guide, Vericairn 0.9.2

This guide walks the journeys an operator and the agents working for them perform with the 0.9.2 command line, Workbench and the MCP server. Commands, API operations and record bodies are generated in `cli-reference.md`, `api-reference.md` and `record-reference.md`; the format rules are in `record-format-v1.md`, `record-format-v2.md` and `record-format-v3.md`, related by `record-format.md`. Commands below assume you are inside the estate directory or pass `--estate <path>`; `--json` selects machine-readable output. With the NuGet tool, use `dotnet vc` wherever an example says `vericairn`. Sections 2 to 12 cover established journeys; sections 13 to 16 cover Workbench, MCP and documents; sections 17 to 19 cover goals, the configured worker and pairing. Sections 20 to 23 cover measured context, hand-over, communication and watching; sections 24 and 25 cover release notices and current attention. Sections 26 to 29 cover policies, readable companions, checked changes and client interruption; sections 30 and 31 cover durable operator requests and the support bundle.

0.9.2 is the release after 0.9.1. A goal can run a declared flow: a template of kind `flow` whose steps are
routed to their roles one step per command, with each step's position, hand-offs and reviews derived from the
records, stops where a person holds the role, and the software pack's release and upgrade flows (PROC-05,
PROC-06, PROC-07 and PROC-10). Clients follow an event journal through the API stream, MCP notifications and
`events_read` (WATCH-05 and MCP-05). An estate signs its history with checkpoints, closings and authority read
as it stood at a record's time (ID-05, ID-06, OPS-07, FIX-108 and DOCS-15). An agent on another machine works
over MCP's Streamable HTTP transport (MCP-03), keyword search selects an agent's context (CONTEXT-02), security
reports hold a release until they are triaged (OPS-11), an upgrade replaces an older runtime (OPS-05), the build
tooling assembles a release's documentation set (DOCS-11), a guide keeps large private artifacts with today's
records (DOCS-29), and continuous integration runs through a merge queue (CI-06), with FIX-85, FIX-89, FIX-93 to
FIX-101, FIX-103 and FIX-123. Every distributable is built with SDK 10.0.404 and embeds the .NET and ASP.NET
Core 10.0.15 runtimes (FIX-115). 0.9.2 hardens how admissions and the root of trust are read, per-user state,
held-term matching, request authentication, and authority and policy records (FIX-102, FIX-104, FIX-105,
FIX-106, FIX-110 and FIX-111), as its release notes' Security section describes.
In 0.9.1, a security patch, every distributable was built with SDK 10.0.403 and embedded the .NET and ASP.NET
Core 10.0.14 runtimes in place of the 10.0.11 runtimes embedded through 0.9.0, which published .NET advisories
affect (FIX-91); policy evaluation reads historical repository content in batches (FIX-92); a worker run can
prepare its workspace before the agent starts (FIX-86); a decision cannot be dated after its own record
(FIX-87); and the bill of materials names the release's version (FIX-90). It gave Vericairn's MCP server its own
temporary directory in a worker run (FIX-88), as its release notes' Security section describes. Earlier
published releases, and the withdrawn candidates 0.5.0, 0.5.1, 0.6.0, 0.7.0 and 0.8.1, are recorded in their
own frozen sets.
0.9.2 reads record formats 1, 2 and 3 and writes the estate's own format, at most 3; an estate at format 3
upgrades from 0.8.0, 0.8.2, 0.8.3, 0.8.4, 0.9.0 or 0.9.1 without a format step. The format-3 upgrade from 0.7.3
preserves historical records and requires explicit adoption afterwards. The features and corrections of 0.8.0
through 0.9.1 remain. Earlier sets stay frozen.

NuGet availability follows publication and a successful package workflow. The remaining limits are in
`limitations-and-platforms.md`.

For a complete starting workflow, choose [people only](people-only.md),
[people and agents](people-and-agents.md), or [agents only](agents-only.md).
Each guide covers setup, the daily loop, roles, review and acceptance, with a local worked example
and an explicit account of what stays with a person. Agents-only use today delegates ordinary work;
a person still accepts releases, publishes, spends and holds credentials. Charter mode fields,
mode-specific checks, delegated reserved acts and human checkpoints are planned.

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

From 0.8.3, `estate init` uses the running build's exact source commit, as reported by
`vericairn --version`, when `--commit` is omitted. Earlier releases require `--commit`; with 0.8.2, add
`--commit e000a12ea980a4ac0c7e7a127a714f6423e27d7f` to the command below.

```sh
mkdir my-estate && cd my-estate && git init
vericairn estate init --name "My Venture" --slug my-venture --purpose "What this estate exists to do." \
  --operator-name "Your Name" --organization "Your Organization"
vericairn status
git add -A && git commit -m "Estate online"
```

Use `--commit <40-hex-commit>` to pin a different Vericairn source commit. A development build
without a recorded commit requires this option; omitting it refuses before any manifest or records
are written. The default comes from the build's version metadata, so the estate's Git history does
not choose the pin.

`estate init` writes two records and the manifest, which validate together: the operator principal (you), the charter (name, purpose, operator, policies: `signatures: optional` at first) and the estate manifest, which is a file of the repository rather than a record. It prints your principal id. Keep that id; every record you author names it with `--by`. The manifest pins the Vericairn source commit for the estate's bootstrap; the ordinary way to run the tool against the estate is the binary you installed with `--estate`, and the verified upgrade in section 9 can later move that source pin to a published release. `--profile local-only` keeps the records in this machine's live store instead of files; the default, `repository-synced`, is the profile the first-party estate uses. A new estate starts at record format 3, the newest format 0.9.2 writes, which the manifest's `recordFormat` states; an estate begun under an earlier release moves to it by the upgrade of section 9.

Expected observation: `status` shows the estate's name, the operator principal, no work orders yet, and `estate check` reports `2 records: 0 errors, 0 warnings`. The manifest is not a third record.

From 0.8.4, both forms of `estate init` also write `bootstrap` and `bootstrap.ps1` at the estate root.
These are the first-party copies embedded in the installed command line, whose source of truth is
`src/Vericairn.Cli/Bootstrap/` in the product repository. Both have LF line endings, and `bootstrap`
is executable on Linux and macOS, and both commands keep them LF on every checkout by adding
`bootstrap text eol=lf` and `bootstrap.ps1 text eol=lf` to `.gitattributes` when those lines are missing.
From 0.9.0 they also add `* text=auto eol=lf`, `*.json text eol=lf` and `*.md text eol=lf`, so a clone made
where Git converts line endings, Git for Windows' default, keeps every record, object and document LF and
its hashes intact, and they add `.vericairn/` to `.gitignore` for the bootstrap's local downloads. Commit
the scripts, `.gitattributes` and `.gitignore` with the estate. An estate made before 0.9.0 gets the lines
from `vericairn estate bootstrap`; renormalize it once afterwards (`git add --renormalize .`, then commit). A fresh estate or clone can run `./bootstrap estate check`
on Linux and macOS, or `./bootstrap.ps1 estate check` in Windows PowerShell 5.1 or PowerShell 7.
The scripts use the manifest's source or release pin; [Route A](install.md#1-route-a-as-part-of-an-estate-through-its-bootstrap)
describes the prerequisites and cold start.

An existing estate obtains or refreshes the copies from an installed command line from 0.8.4 or later:

```sh
vericairn estate bootstrap
```

The command reports `written` for a new or replaced copy and `unchanged` for identical bytes, and
restores executable permission on the POSIX script. It checks both destinations before writing.
It is also the repair for a copy that `estate restore` from a release before 0.9.3 wrote, which
leaves `bootstrap` without its executable permission ([section 10](#10-export-restore-recovery-and-divergence)).
A differing copy refuses the operation; preserve any customizations, then use
`vericairn estate bootstrap --force` to replace it. A link or directory must be moved aside first,
even with `--force`. Refreshing the scripts keeps the estate's records and manifest pin in place.
Initialization also refuses an existing differing copy before creating the estate; keep a copy of
any customizations and move the file aside before retrying initialization.

Both commands name the two scripts, `.gitattributes` and, from 0.9.0, `.gitignore` in their receipt. Human output adds lines such as
`written   bootstrap`, `written   bootstrap.ps1`, `written   .gitattributes` and `written   .gitignore`; `--json` adds a `bootstrap`
array of `path` and `status` objects.

Vericairn also offers opt-in `estate init --release`. Run the published target release's own command line,
adding `--release` to the command above. It verifies the
GitHub release, tag commit, `SHA256SUMS` and the current platform archive before creating anything.
It writes three records: operator, charter and a `vericairn-release` observation. The manifest starts
in release mode and pins that observation by hash. It creates no product or product release record.
`--profile local-only` works the same way. A different repository or commit override is refused with
`--release`; omit `--release` to retain ordinary offline source initialization. `--from <download-directory>` supplies downloaded
publication evidence and archive bytes for offline verification; see [offline inputs](install.md#offline-inputs).

From 0.8.4, successful `estate init --release` also prints the observation's receipt after the operator,
estate and manifest lines:

```text
release   <observation id>  records/observation/<YYYY-MM>/<observation id>.json  sha256:<hash>
```

With `--json`, the existing `operator`, `estate` and `manifest` fields are joined by a `release` object:

```json
{
  "operator": "<operator id>",
  "estate": "<estate id>",
  "manifest": "estate/manifest.json",
  "release": {
    "path": "records/observation/<YYYY-MM>/<observation id>.json",
    "id": "<observation id>",
    "hash": "sha256:<hash>"
  },
  "bootstrap": [
    { "path": "bootstrap", "status": "written" },
    { "path": "bootstrap.ps1", "status": "written" },
    { "path": ".gitattributes", "status": "written" }
  ]
}
```

The receipt identifies the exact observation pinned by the manifest in either storage profile. Its
path is the record's portable path; with `local-only`, the record stays in the live store. Without
`--release`, both output formats include the bootstrap files and omit the release receipt.

Optional charter signatures do not waive record-specific requirements. Policy and writing changes,
duty results/reviews, strict generated declarations and the operator one-step action still require
their admitted signatures and exact authority.

## 3. Principals, devices and admission

A device is a key on one machine acting for one principal. Begin with your own: create a device for the operator principal on your machine and admit it.

```sh
vericairn devices create --principal <operator id> --name "Jason's workstation"
vericairn devices admit <device id> --by <operator id> --note "The operator's workstation."
```

That admission, signed by the device it admits, is the estate's first authority: the root operator's first device admitting itself. It is accepted once in an estate's history. At record format 2 or 3 every other admission, and every revocation, counts only when signed by an already admitted device, whatever the charter's signature policy, or when an operator's signed migration to format 2 anchored an existing unsigned admission (section 9), or, from 0.9.2, when an anchoring history checkpoint lists it ([history checkpoints](#history-checkpoints-092)). A refused admission explains which admitted device or recovery key could sign it.

From 0.9.2, every admission and revocation is judged from that first authority outward: one counts only when the device that signed it was admitted, at that time, through admissions that count in turn, only when its signature verifies, and only when its author may admit then: you, another operator, or a person you granted `administer`, read from your principal records and grants as they stood at the admission's time, as every other rule reads them, and not through a grant a closing closed before it. A person you granted `administer` must be described as a person by a principal record that is signed or that a checkpoint anchored. A record written later restores no earlier admission. The judgment is the same whatever order the records are read in and whichever was written first; of two devices admitted at the same remove from the first authority that revoke each other as of before either revocation, neither revocation counts. A revocation that would unseat what its own signature or its author's authority rests on, such as one stated as of a time before the admission of the device that signs it, counts for nothing and revokes nothing. An admission that does not count admits and revokes nothing and is reported with its finding, such as `signature.unadmitted`, `signature.invalid`, `admission.author` or `admission.unseats-support`. The root operator is read from the charter your estate's manifest names, which is the charter `estate init` wrote, and the first authority from the admission the manifest names as `firstAuthority`: the runtime writes it when your first device admits itself, and the 0.9.2 upgrade writes it for an estate whose manifest names none. A second charter that supersedes nothing, or a second device admitting itself for you, takes the place of neither, whatever date it states; one that comes with history checkpoints of its own leaves the copy unsettled, with no record counting, until it is removed. When the manifest names no first authority and the estate holds more than one such self-admission, the estate check reports `manifest.first-authority`, listing each with its device, and no device's admission counts until you name yours in the manifest. Naming it makes your devices' admissions count again, but every other self-admission still reads `signature.self-admission`, an error, so `vericairn estate upgrade`, which refuses any estate with errors, proceeds only once you have also removed those records from the copy. The manifest is a plain file that nothing signs: until your estate adopts history checkpoints, someone who can edit both your records and `estate/manifest.json` in a copy can point it at another charter or first authority. Adopting checkpoints narrows that: from adoption on, a manifest naming another charter or first authority than the ones your checkpoints witnessed leaves the copy unsettled (`history.root-ambiguous`, `history.manifest-root`) instead of moving either, whether it names another charter, another first authority or both. This is one more reason to adopt checkpoints soon after upgrading to 0.9.2.

Add a principal for a builder agent (or a second human) with a record, create its device on the machine it works from, and admit it from yours:

```sh
vericairn records new principal --by <operator id> --body builder.json
vericairn devices create --principal <builder id> --name "Builder on the workstation"
vericairn devices admit <builder device id> --by <operator id> --note "Why this device may author records."
vericairn devices list
```

with `builder.json`:

```json
{"kind": "agent", "name": "Builder", "roles": ["builder"], "agent": {"vendor": "OpenAI", "product": "Codex"}}
```

`agent.product` names the harness the agent runs in, as a launcher routes to it: routing matches it to a launcher's harness name or id, ignoring case, so `Codex` or `codex` for the codex launcher and `Claude Code` or `claude-code` for claude-code. From 0.8.4, `records new principal` still records an agent whose product matches no installed launcher, and warns with the accepted values, since `goal next` would report that agent unavailable until a signed revision names one.

`devices create` generates the key, records the device signed by its own key, and proves possession of that key. Once admitted, the device can sign records that principal authors from this machine. `devices admit` records the admission; `devices revoke <device> --by --reason` ends it from now on, leaving earlier records attributable; in an estate that has adopted history checkpoints the revocation carries a closing, from which on what the device signs, other than its own revocation, counts only when the closing witnesses it or an anchor lists it, whatever date it states ([closings](#closings-and-authority-at-a-records-time-092)). `devices list` shows each device by its identity with its current name, custody, presence, admission and whether its key is held here.

**Custody.** A new device gets the strongest custody its machine offers unless `--custody` names one: `cng`, the platform key store, on Windows; `secure-enclave` on a Mac with a Secure Enclave and the `vericairn-enclave` helper beside the command line, where the key is generated inside the enclave and kept as an enclave-wrapped blob that signs nothing on any other Mac; `secret-service` on Linux where a secret service runs and `secret-tool` is installed; otherwise `file`, a file in per-user state. Unix file permissions are owner-only; Windows files inherit their directory ACLs. In an estate still at record format 1 the default is the strongest custody format 1 can name, and the Secure Enclave and presence wait for the upgrade to format 2 (section 9). Each custody's strength is recorded with it: a key in an owner-only file can be used by any process of the same operating-system user, a secret-service key is readable by them while the keyring is unlocked, and CNG and Secure Enclave keys cannot be exported.

**Presence.** A person's device needs its owner's presence at every signature wherever its custody enforces it, which on a desktop only Secure Enclave custody does (Touch ID or the login password); `--presence false` declines it. From 0.9.0 a phone paired with Remote holds a second device, its presence key, in `ios-secure-enclave` or `android-keystore` custody with presence required, and only that key signs the phone's decisions ([Remote](remote.md#keys-and-custody)); the phone's request key states presence none. An agent's or a service's device signs unattended and never claims presence, and no custody claims presence it cannot enforce. Presence on Windows is not built yet, so a Windows device states presence none. Tests without an interactive desktop do not qualify the presence prompt; consult the estate's platform qualifications for observations on the intended machine.

**Rename and rotate.** From record format 2 a device keeps its identity, its first record, across renames and key rotations, made on the machine that holds its key:

```sh
vericairn devices rename <device> --name "A better name"
vericairn devices rotate <device>
vericairn devices rotate <device> --custody secure-enclave
```

A rotation makes the new key beside the old one, records a revision signed by the previous key, which proves continuity, and by the new key, which proves possession, and only then lets the new key take the old one's place, so a refused rotation leaves the device as it was. Records signed before it stay valid, and the replaced key signs nothing dated after it; in an estate that has adopted history checkpoints the rotation carries a closing, from which on a record signed with the replaced key, other than the rotation, counts only when the closing witnesses it or an anchor lists it, whatever date it states ([closings](#closings-and-authority-at-a-records-time-092)). Moving a key into other custody, from a file into the Secure Enclave for example, is a rotation with `--custody`; the new key keeps the device's presence where the new custody enforces it, and `--presence` sets it. A key that may have leaked is revoked by an administrator, not only rotated.

**Emergency access.** Make the operator's recovery device on a machine that holds your admitted device:

```sh
vericairn devices recovery --principal <operator id> --name "Recovery key, kept in the safe" --output <a new file outside the estate>
```

You type a passphrase of at least twelve characters, or pass it on the first line of standard input with `--passphrase-stdin`. The key is made in memory and written once, encrypted, to the new file, with owner-only permissions on Unix (inherited directory ACLs on Windows), and no key store ever holds it; your admitted device admits the recovery device at once. Move the file off the machine. When every other device of yours is lost, `vericairn devices admit <new device> --by <operator id> --note "..." --recovery <file>` admits a replacement and `vericairn devices revoke <lost device> --by <operator id> --reason "..." --recovery <file>` revokes a lost one, each opening the key in memory with its passphrase. A recovery device signs only device records, admissions and revocations, and, in an estate that has adopted history checkpoints, the closing written with a revocation it signs; each admission, revocation or closing it signs shows in `status` and `estate check` as unreviewed (`recovery.unreviewed`) until you record a review of that exact record from another device. A recovery device is never the estate's first authority. Losing every device without one leaves the estate readable and its history verifiable, but signed authoring and new admissions require an admitted signing key. An optional-signature charter can still permit unsigned records; it does not restore admission authority.

**Requiring signatures.** To make signing mandatory, revise the charter so that `policies.signatures` is `required`: author a new estate record that supersedes the current one (`records new estate --by <operator id> --supersedes <current estate record id> --body charter.json`) with the same name, slug, purpose, operator and record format and the new policy. From then on a record without a signature from an admitted device is refused, and a clone without a key is read-only. A record's date is what its author wrote, so the policy binds the records dated after the charter revision: an unsigned record dated earlier is read under the earlier policy, which from record format 2 no longer reaches admissions (above), until the estate adopts history checkpoints (below). Do this only after your own device is admitted. Once signatures are required, a later charter revision counts only when the root operator, the operator the estate's first charter names, signs it from an admitted device. Any other revision is reported as a `signature.charter` error and changes nothing, and a revision that names another operator is refused (`estate.operator`).

**What an author's id establishes.** From 0.9.2, a new record's `--by` must name a principal the estate holds when the record is written, whatever time `--at` gives the record. An id that names no record, or a record of another type such as the charter, is refused (`record.author`) on every write surface. Records already in the estate keep their reading; `estate check` and `records validate` report each unresolved author as information. Under optional signatures an unsigned record's author is still a claim: it names a known principal, and records that carry authority check that principal's roles and scopes, and only principal and authority records their authors may write give those roles and scopes: a principal never writes its own record other than the root operator, and only an operator gives the operator role, but anyone who can add records to a copy can claim any principal. Only a signature from that principal's admitted device authenticates authorship, and required signatures make it mandatory.

Rules the tool enforces here: only the root operator or a principal granted `administer` may author a principal record; a device record is signed by its own key to prove possession, and each later revision by the device in the name of its identity. An admission is authored by the operator or under `administer` authority, is signed in record format 2, and stays in force across the device's renames and rotations. A format-2 admission has no `scope` field. An agent principal that records operations and releases needs an authority with the `local-engineering` scope (section 4).

### Presence prompts, announced and counted (0.9.3)

A device that needs its owner's presence asks once for every record it signs; one prompt never covers two signatures. From
0.9.3 (FIX-124), an operator action, a release step or a single write that signs with such a device announces every record it
will sign before the first prompt, each with its type, the key you know it by and what signing it authorizes. At each prompt the
command line prints `Signature k of n: <what it signs>` on standard error, Workbench writes the same line to its status and
activity log, and the system dialog's reason, which the `vericairn-enclave` helper passes to macOS, says the same in one bounded
sentence that carries no secret or machine path. When the action ends it says how many it signed (`Signed 4 of 4.`). A command
that signs nothing with presence prints nothing of this.

The count stays true for what the action signs. When it must sign more than it announced, for example observing a pull request
again because its checks are still running, it announces the addition, with why, before the first of the added prompts. When its
path changes before the prompts it has not yet asked for, it announces the new remainder. When it stops part way, by a refusal,
a declined prompt or a failure, its last line says how many of the announced signatures it made, and a prompt that was declined
is named as not made. Standard output keeps the command's own answer, so `--json` output stays one JSON document. The runtime
that signs keeps these notices in memory for the clients on its machine (`POST /v1/presence/prompts`, local socket only); they
are never records, and a daemon of an older release shows none.

With a device that needs presence, the release and acceptance steps ask for these prompts, in this order. "Format 3" is an
estate at record format 3, where the release evidence is written only after a trial signature, never stored, proves the device
can sign it.

| Step (release flow step) | Prompts |
| --- | --- |
| `release submit` (`submit`) | 5 at format 3 in an estate that has adopted history checkpoints: the release gate's history checkpoint, the intent, a trial signature before dispatch, a trial signature of the dispatch receipt and the receipt. One fewer before adoption (no checkpoint), and one fewer below format 3 (no trial of the receipt). An admission that changes before dispatch replaces the last two with the intent's cancellation, or records the reserved intent again first. |
| `release observe` of a finished build (`candidate`) | 4 at format 3: a trial signature and the operation's revision, then a trial signature and the candidate; 2 below format 3. The revision alone, with its trial at format 3, when the build failed or a candidate is already recorded. An intent without a receipt is recorded as uncertain first (2 more at format 3), and is announced with the rest; while the build still runs, the observation signs that alone. |
| `release observe` of the publication (`publish`) | 2 at format 3: a trial signature and the published revision; 1 below format 3. |
| The operator's acceptance of a candidate or of finished work (`accept`, `close`) with `records new` or Workbench | 1. |
| The same acceptance with `operator record` | 22 for a new action: the record, 6 for the push (its intent, its dispatch, the 3 records of observing the pushed branch and its outcome), 3 for the opening, 3 for the first observation of the pull request's checks, and 9 for the merge (its intent, 3 for observing the pull request, its dispatch, its outcome and 3 for observing the target branch's run at the merge commit). Each further observation while the checks or that run are still running adds 3, announced before it asks. A resumed action leaves out what its records show done; finishing an earlier merged action first adds 3. |
| `operator close` (`close`) | 1 per selected step's acceptance. |
| `operator unfinished --finish` | 3 per observation of the target branch's run. |
| `goal activate`, `goal accept` | 1 each. |
| `goal step` | 2 for a person's step (the assignment and the ask) or an agent's step routed now (the assignment and the run's intent); 1 for an ask alone. |

Any other write asks once for its record, and once more for a history checkpoint or closing it carries, such as a charter
revision's; the interval checkpoint is never asked of a person's device that needs presence ([history checkpoints](#history-checkpoints-092)).

### History checkpoints (0.9.2)

A history checkpoint is a signed record listing, by path and hash, the records your estate holds and naming the checkpoints it follows ([record format 3](record-format-v3.md#history-checkpoints-092)). What a checkpoint that counts witnesses cannot be removed or altered unnoticed, and it closes the backdating route that required signatures alone leave open. Four things change once an estate holds a counting checkpoint, which is what adopting checkpoints means:

- **Backdating counts for nothing.** While the charter requires signatures, an unsigned record counts only when an anchoring checkpoint lists its exact id and hash, whatever date it states; otherwise it is `history.unanchored`, or `signature.required` when dated after the charter revision, and it grants nothing. Only you, the root operator, or a person holding `administer` anchor, never an agent, from a device whose own admission rests on signatures. The adoption checkpoint, an estate's first, is yours or an administrator's (another author's counts for nothing, `history.adoption-author`) and anchors the unsigned records already present. In an estate that has adopted under optional signatures, a charter revision to `required` by you or a person holding `administer` writes, in the same step, a checkpoint anchoring the unsigned records that count before it (0.9.2); the runtime refuses any other revision that would leave one of them counting for nothing.
- **Removing or altering a witnessed record is an error on every copy.** A witnessed record that is absent is `history.removed`, one with other bytes, or listed with two hashes, is `history.altered`, and a missing checkpoint or inventory object is `history.checkpoint-missing`. Each names the path and the witnessing checkpoint, leads `status`, and holds release submission. Restore the file from any clone or the repository's history; its bytes are verified against the listed hash. For a record that had to go, such as one carrying a leaked secret, you or a person holding `administer` acknowledge the removal with a reason in an anchoring checkpoint's `removed` list.
- **What does not count grants nothing.** A record that does not count is kept and reported, and no rule that admits, authorizes, accepts, clears or holds reads it: not a grant, a role, an acceptance, an answer, a clearance or a hold. This holds in every estate from 0.9.2, for records failing the signature policy and records whose signature does not verify.
- **Witnessed history confirms the first charter and the first authority.** After adoption, your checkpoints witness the charter and first authority your manifest names, and nothing else can take their place. `history.root-ambiguous` means this copy holds counting checkpoints, read under another charter or with another first authority, that witness that one first; `history.manifest-root` beside it means the manifest's own charter or first authority is not witnessed. No record counts until it is settled, and neither finding names an id to restore: compare the copy with a head you know (`vericairn estate check --expect <checkpoint>`), then remove the history that does not belong, or restore the manifest if it was edited.

Witnessing grants nothing, so any admitted device, an agent's included, may sign a checkpoint without anchors or removals. Checkpoints that two clones write after the same head both stay heads after git merges them, and the next checkpoint names both. `status --json` shows `history`: whether the estate has adopted checkpoints, its heads, how many records they witness, the unwitnessed tail and any standing problem, the same on every copy. A fresh machine cannot tell from the records whether the newest checkpoints were removed with the records after them, so check a new copy against a head known elsewhere, named by its id or its full `sha256:` hash: `vericairn estate check --expect <checkpoint>`, or `./bootstrap --expect <checkpoint>` ([CLI](cli.md#vericairn-estate-check---expect-092)). `estate check` also warns `history.deleted-in-git` for record files deleted in the clone's history that no checkpoint witnessed, and says when a shallow clone cannot list them.

**What a removal leaves, and what anchors decide.** A witnessed record that is removed is absent, and every rule reads the copy without it: a removed revocation leaves its device admitted on that copy, bounded only by a closing of the device or of its key ([closings](#closings-and-authority-at-a-records-time-092)). So while `history.removed` stands the runtime refuses every write that would leave it standing, and release submission waits. Acknowledging the removal settles the error and decides nothing else: it lists the witnessed version's id and hash with a reason, the estate reads on without the record, and a record under that id, written again or present with other bytes, is part of the unwitnessed tail and counts by its own rules until a checkpoint lists it. An anchor, likewise, exempts the exact bytes it lists from the signature requirement, from a closing's refusal except for a device revision, and from `history.altered` when only authors who may not anchor list other bytes for that id; every other rule still judges them, so an unsigned checkpoint, policy record or device revision, or an unsigned revision of a required charter, counts for nothing anchored or not. It authenticates nothing: the record's author is still the claim its `by` makes, checked against that principal's roles and grants as any author's is, and its date is read as any counting record's. A device revision a closing refused changes no key or name, anchored or not. The runtime refuses an anchor of bytes not present, of a signed record no closing refuses or of a device revision a closing refused, and an acknowledgement of a record present with the listed hash or of a version no head witnesses. Anchors you add by hand, and acknowledgements, go in a checkpoint of yours written with `records new history-checkpoint`, which the runtime checks as it checks every checkpoint ([record format 3](record-format-v3.md#history-checkpoints-092)).

**Adopting checkpoints.** A new estate adopts at birth: when your first device admits itself (`devices admit <device id> --by <operator id>`, the estate's first authority) while the estate holds nothing but what `estate init` wrote, the runtime writes the adoption checkpoint in the same step, signed by that device. It witnesses those records and anchors the ones `estate init` wrote without a signature, your principal, the charter and any release observation, and `devices admit` prints it after the admission, marked `(history checkpoint, trigger adoption)`. An estate that already holds other records adopts as your explicit step after its upgrade to 0.9.2: read what adoption would witness and anchor, then sign it once.

```sh
vericairn estate checkpoint --adopt --check
vericairn estate checkpoint --adopt --expect-plan <plan> --by <operator id>
```

The check lists how many records adoption witnesses, every record present; each unsigned record it anchors, the unsigned records that count today, with its type, date, path, the commit that added it and that commit's date; the unsigned records that count for nothing today and stay so; records signed by a device revoked now; records that read otherwise when roles and grants are read at each record's own time, as records written after adoption are, each with what differs, since adoption keeps for every record it witnesses the reading it has at adoption (this list is advisory: the plan hash does not cover it, and adoption computes it again, so the list the adoption returns can differ from the check's); record files deleted in the repository's history, which no checkpoint can witness; and record files no commit holds yet. A `NOTE` points out an anchored record dated more than a day before the commit that added it, a file no commit holds, and an admission the format-2 migration already anchored, which adoption lists again. In a shallow clone, or outside a git repository, it says what it cannot list. The check ends with `plan: sha256:<hash>`, the hash of the set it would anchor: the adoption names it with `--expect-plan`, which is required when anything is anchored, and is refused when the set changed since the check, so what you sign is what you read. Adoption anchors only what counts today, so it changes no reading and admits nothing new. Only you or a person holding `administer` through a grant that rests on signatures signs it, and an agent is refused before anything is signed. While this clone's memory holds a checkpoint the copy no longer holds, both the check and the adoption are refused: the copy may have been rewritten, and adopting it would anchor what the rewrite left (below). Until it counts, `operator list` shows "Adopt history checkpoints" under what waits on you in an estate pinned to 0.9.2 or later, with these two commands. The upgrade flow's `adopt` step waits on you too, and points to that item rather than repeating them ([the upgrade flow](#the-software-release-and-upgrade-flows-092)). One adoption checkpoint anchors up to about 9,000 unsigned records within the 1 MiB record limit; the first-party estate's adoption anchors 147.

**Checkpoints the runtime writes.** Once the estate has adopted, the runtime writes a checkpoint by itself, signed by the device then writing and in the same batch as the act: at every estate upgrade (trigger `upgrade`), at every release submission, before its own admission check (trigger `release`; `release submit --check` writes none), after every charter revision (trigger `charter`), and once the unwitnessed tail holds 500 records or its oldest record is 24 hours old (trigger `interval`), on the next write this machine signs for its author. A person's device that needs presence is never asked for an interval checkpoint: the next write by an agent, a service or a person without presence carries it, so an estate of people only relies on the other triggers. A command that writes one prints it after its record, marked `(history checkpoint, trigger <trigger>)`. A checkpoint that cannot be written, for example because no admitted device of the author is held here, never blocks the act: the record is written alone and a warning says why. A release submission is the exception, since the release gate writes its checkpoint first: it is refused with `history.checkpoint`, saying why. So is an act a closing precedes (below). Triggers never acknowledge a removal, and only one anchors: the checkpoint of a charter revision that moves to required signatures, written for you or a person holding `administer`, anchors the unsigned records that count before it, so the revision changes no reading. An agent's device never anchors. `vericairn estate checkpoint --by <principal>` writes one now, with trigger `manual`, and `--check` shows what it would witness ([CLI](cli.md#vericairn-estate-checkpoint-092)). Before adoption no trigger writes one. A revocation, a key rotation and a narrowing grant revision carry their closing in the same batch ([closings](#closings-and-authority-at-a-records-time-092)).

**The history line.** `status` shows, after the pinned release, a line such as `History: adopted · head 9f86d081884c7d65 · 5,140 witnessed · tail 12 · no problems`, or `History: not adopted · no heads · 0 witnessed · tail 5,140 · no problems`, with each standing problem after it. Workbench's home and Remote's home show the same line from the same canonical status. The sixteen hexadecimal digits name the head to `estate check --expect` on a fresh machine.

**What each machine remembers.** Each machine remembers, beside the clone's live store under your Vericairn home and never in the estate, the newest checkpoint it verified and the main-line commit that held it; the main line is the local branch your origin's `HEAD` names, else `main`, else `master`. `estate check` keeps it current and warns `history.rolled-back` when the main line no longer holds that checkpoint, as after a history rewrite that removed the newest checkpoints together with the records after them; checking out another branch warns nothing, since a branch is judged through the main line. The memory moves forward only to a head that follows the checkpoint it remembers. While the main line no longer holds that checkpoint, or the copy has not adopted, no release is submitted from this clone (`history.rolled-back`), and while the copy no longer holds it, adoption and its check are refused. A passed `estate check --expect` is remembered too, by the checkpoint it named, and a checkpoint it finds that the main line holds replaces what the machine remembered: after a rewrite you intended, check against a head the main line holds. The memory is kept by the clone's directory: a clone in another directory, or another machine, remembers nothing, which is why `--expect` exists, while a fresh clone into the same directory keeps what the earlier one remembered; an unreadable memory is a warning of `estate check`. An estate that holds no checkpoint, such as the first-party estate before it adopts, reads exactly as before.

### Closings and authority at a record's time (0.9.2)

A record's date is what its author wrote, so in an estate that has adopted checkpoints a date no longer decides what a revoked device, a replaced key or a narrowed grant can still account for: the checkpoint order does ([record format 3](record-format-v3.md#closings-and-authority-at-a-records-time-092)). When you revoke a device, rotate a key or narrow a grant, the runtime first writes a closing, in the same batch as the act: a checkpoint with `trigger` `closing` and `closes` naming the device, the device revision whose key is replaced, or the grant revision being narrowed. `devices revoke`, `devices rotate` and a grant revision through `records new authority --supersedes` print it after the act, marked `(history checkpoint, trigger closing)`. The key that performs the act signs its closing. You or another operator sign a device's closing, or a person holding `administer` does, or an operator's recovery device, which signs the closing before a revocation it signs and no other checkpoint; a device signs the closing of its own key; an operator signs a grant's, and it closes the revision it names and every earlier revision of that grant. A grant revision narrows, and carries a closing, when it names another grantee, drops a scope, reserves another action, adds a condition, adds or shortens an expiry, or changes a policy scope's bounds in any way; one that only keeps or widens the grant carries none. It is compared with the revision it replaces, which its closing names: the one it supersedes, or, written on top of one that does not count, the nearest revision below it that counts. A signer's operator role or `administer` grant must stand at the closing's time and still stand: a later narrowing, or a closing before it of that grant, takes the power away, as it takes away anchoring. That is read against the closings before it in checkpoint order, never against the closing itself, and your adoption checkpoint against the closings that act. Another author's closing is `history.closing-author` and closes nothing.

In an estate that has adopted, an act is refused, and nothing is written, when its closing cannot be written, for example because no device of the act's author is held on this machine, or when the closing would close nothing, as when an agent holding `administer` revokes a device. An act signed elsewhere is refused too: a record presigned on your phone or signed by a device-signed request cannot carry a closing, so revoke, rotate or narrow on a machine that holds a device of one who may perform the act, or revoke with your recovery key. `operator record` cannot carry one either, since it carries one signed record through a checked merge: it refuses these acts and names the command that writes them ([operator actions](operator-actions.md)). An estate that has not adopted writes these acts as before, without a closing.

An act is refused as well when it states a time, as `records new --at` can, more than five minutes before the newest record its closing would witness or before the head checkpoint: its closing is dated no later than the act and would be dated before what it lists. Write it at the present time. A device revoking itself states a `revokedAt` at or after its closing, which is dated at the act's time, since from the closing on its signature counts only on its own revocation from then on; one stated earlier is refused, so revoke the device as of an earlier time from another of your devices or with your recovery key.

**A batch that stops between the closing and the act.** If storing the act fails after its closing was stored, for example because the disk refused the write, the runtime removes the closing again when it can do so cleanly, and the command fails with nothing written: run it again. When the closing cannot be removed, or a process stopped between the two writes, the closing stays and closes. The command prints it, marked `(history checkpoint, trigger closing)`, and fails with code `closing-stored`, saying that writing the act again uses the stored closing; `estate check` warns `history.closing-unfollowed`, and `status` and `estate reconcile` show the batch as a task. Run the same command again, as the same author: the act is written after the stored closing, alone, with a warning naming that closing, and no new closing, which a closed key could not sign. So an operator whose only device was rotating its key or revoking itself finishes the act by running the command again. A key rotation can be settled the other way too: revoke the device instead, as an operator or a person holding `administer`, which retires the closed key with it, as you would when the key may have leaked (below); the task and the warning end once the device is revoked. Until then the closed device or key signs nothing that counts, and nothing written meanwhile is witnessed by the closing.

- **From the closing on, only what it witnesses counts.** A record signed by the closed device or with the closed key, or written by the grantee while a closed revision of the grant was in force, counts only when the closing or a checkpoint it follows lists it, or an anchor does, whatever date it states. For a grant this covers every such record of the grantee, not only those that needed the grant. Any other is `history.after-closing`, an error that leads `status` and holds release submission, and it counts for nothing: a closed device's later admissions and revocations admit and revoke nothing, and its later device revisions change no key or name. A record a contributor wrote in its own clone before the closing and merged after it is flagged the same way; read it, and anchor it in a checkpoint of yours if it stands.
- **The closing acts by itself.** Deleting the revocation after it reopens nothing, and admitting the device again does not reopen it either: admit a new device instead. A closing that no revocation, rotation or grant revision follows still closes, and `estate check` warns `history.closing-unfollowed` until the act follows.
- **An act without a closing.** A revocation, rotation or narrowing that arrives as a file without its closing, merged from a copy that had not adopted yet or added by hand, takes effect all the same, and `estate check` warns `history.closing-missing` on it: no closing names its device, key or grant, so a record that target signs with an earlier date still counts. Write the closing with `records new history-checkpoint`, its `closes` naming the target, signed by one who may perform the act; from it on the target accounts only for what the closing witnesses. Acts your adoption checkpoint witnessed carry no warning.
- **Authority is read as it stood.** Roles and grants are read at each record's time: a later grant authorizes nothing written before it, and a later narrowing withdraws nothing already written. Records your adoption checkpoint witnessed keep the reading they had at adoption. In every read, a grant or principal revision counts for nothing when it is dated before the nearest record below it in its line that counts; a record below that does not count, unsigned or refused, is skipped, and a revision written on top of one follows the line there.
- **The strongest acts read more strictly.** Anchoring, acknowledging a removal, rooting an adoption and signing a device's or a grant's closing need the signer's operator role or `administer` grant to stand at the checkpoint's time and still stand. They read a grant or principal revision as written when you wrote it, or an operator who held the role at the revision's time and still holds it; as a removal, whatever it says, when its author had been given the operator role by then but had lost it by then or no longer holds it; and not at all when its author had never been given the role. The role is given by you or by someone given it earlier, so a circle of appointments gives nothing. A revision read as a removal reads as written, except that it gives no operator role, or no `administer` for a grant, and that on top of a revision read below it, it describes a human only where that revision does too. So one saying the person is not human takes that away, and one with no revision read below it, such as the first record of a person's line, describes a human when it says so. A revision a closing refuses is not read for anchoring, for your adoption or for the closings that follow that closing.
- **Removing a role.** A role has no closing (role closings are planned), so a former operator or administrator could otherwise write records dated into the time the role stood; when you remove a person's operator role or `administer` grant, close and revoke their devices yourself. Removing the operator role also ends the person's policy authority at once, for every record no checkpoint by you or a standing operator places before the removal. Write the removal through the runtime with your device on its machine, with `records new`, which `operator record` names when it refuses a removal. In an estate that has adopted checkpoints it writes a checkpoint first, following every checkpoint before it, so the policies, grants, exceptions and writing entries the person recorded keep counting. In an estate that has not, it refuses a removal that would make any record fail and names those records, the person's decisions and the policies they approved among them; adopt checkpoints first, then remove the role. Taking `administer` away is judged the same way, for the principals the person admitted. Your closing refuses what the person signs with that device after it, whatever date it states, in every read, for anchoring and your adoption and for every closing that follows yours, so a removal of another operator they write afterwards, whatever its date, changes none of those; an adoption rooted by an administrator also holds against a narrowing of that grant they write after your closing. A closing written before yours is judged without it. So once you have removed a person and closed and revoked their device, they can still write, at any time and repeatedly, a removal of another operator or a narrowing of an administrator's grant, dated any time from their own appointment until their device's revocation. Every closing that does not follow yours then closes nothing if that operator or administrator wrote it, or anyone whose operator role or `administer` rests on that operator's appointments or grants, directly or through others', and records it alone refused count again. Closing again does not undo this, since the new closing witnesses those records. Removing those records does, with an anchoring checkpoint that acknowledges their removal when a checkpoint witnesses them. Writing the operator's role again gives back the closings of those whose role or `administer` rests on that operator's appointments or grants, and the operator's own earlier closings only when it is dated after the removal and before them. Either holds only until the person writes more such records, among them a removal on top of the role written again or forking the line below it. Your own closings cannot be reopened this way, so write yourself a closing that must hold; role closings, which are planned, are meant to end this. A closing by another operator does not protect that operator the same way, since no closing is read against itself: if the person later writes a removal of that operator, dated any time from their own appointment on, that operator's closing closes nothing. You settle it by closing the device and writing the operator's role again; the closings that operator writes after yours act.
- **What removing a role takes.** Removing an operator's role also takes, for anchoring and closings, what that person appointed, granted and closed: their appointments and grants read as removals, and their closings stop acting. Those they appointed keep the operator role in ordinary reads, and a removal they write still counts for these acts: write their appointments again, or close their devices so that what they sign afterwards counts for nothing. Before you remove the role, write again the grants and appointments that should stay and close again what the person closed, or have another standing operator do it; an adoption whose rooting rested on that person's grant must be rooted again. `estate check` warns `history.revision-author` on each grant giving `administer`, or principal record giving the operator role or describing a human, that these acts read as a removal because its author's role is removed or disputed. A removal the person wrote keeps counting. Two operators other than you who remove each other both lose these acts, the closing either writes of the other's device included, until you settle it: close the device of the one who should go and write the other's role again.
- **Dates a checkpoint questions.** A signed record dated more than five minutes after a checkpoint that witnesses it is the warning `history.dated-after-witness`; a record your adoption checkpoint witnesses is not questioned. In every estate, the runtime refuses `records new --at` and `work attempt --at` more than five minutes past its own clock.
- **What a date still decides.** A record's date still places it: its path month, order, release-notes windows, progress, ages and duty sequence follow it, and its signature verifies against the key and admission of its date. In an estate that has adopted, a date no longer makes an unsigned record count under required signatures and carries nothing past a closing that does not witness the record, and roles and grants are read as they stood at it, or at the adoption for a record it witnesses, bounded by grant closings; policy authority also needs the role to still stand, or a checkpoint to place the record before its removal; anchoring, acknowledging a removal, rooting an adoption and signing a device's or a grant's closing also need the role or grant to still stand. No checkpoint questions an earlier date: a signed record in the unwitnessed tail may state a time before the newest checkpoint, and is read at that time unless a closing refuses it. `records new` refuses `--at` and an explicit id for a history checkpoint, a principal record and an authority record, which the runtime dates itself. A role has no closing (above).

When a key may have leaked, revoke its device, which closes it, rather than rotating the key: whoever holds the old key could sign a rotation of their own, and the device follows the lowest-id rotation. A closing written on a fork, after any checkpoint back to the adoption, acts too; if one was written by mistake or by a key that should not have closed anything, remove it, and if a checkpoint witnesses it, acknowledge its removal in an anchoring checkpoint. A closing you write yourself as any checkpoint, with `records new history-checkpoint`, ahead of an act, closes too; the act still carries its own.

## 4. Authority and decisions

Decisions are the operator's words, recorded verbatim with a summary, a synthesis, the concrete effects and the work orders they affect; from record format 2 a decision also states how much rides on it and what it does not authorize:

```sh
vericairn records new decision --by <operator id> --body decision.json
```

```json
{"title": "Short title", "statement": "The operator's words, verbatim.", "summary": "What was decided, in a sentence.", "synthesis": "Context, what was decided, why, consequences and limits.", "effects": ["A concrete effect on the estate."], "affects": [], "decidedBy": "<operator id>", "decidedAt": "2026-10-15T14:00:00Z", "stake": "medium", "notAuthorized": ["Publication of anything."]}
```

`decidedAt` records when the operator already gave the answer. It must be at or before the record's own `at`; equal times are allowed. A new decision or revision dated after its record is refused with both timestamps. Existing decisions remain readable: `estate check` and `records validate` report this mismatch as a warning, with both timestamps, and it does not prevent further valid records from being written.

`stake` is `low`, `medium`, `high` or `critical`; at medium and above the recorder asks with a recommendation first and at most three questions at a time. `notAuthorized` lists what the answer does not authorize, so that no one reads more into it, and may be empty. A revision of a decision keeps its `statement` byte for byte: the operator's words are protected, a revision corrects what surrounds them, and different words are a new decision. A decision may also name the asks it `answers`, the `obligations` it leaves (each with a key, a summary, an owner and how it is resolved; a `discharge` record settles one) and the living `documents` it changes (section 16). An estate still at record format 1 refuses these fields, and its decisions need none of them.

An agent may record a decision the operator made in conversation, naming the operator in `decidedBy` and keeping the statement verbatim; the agent is then the scribe, not the decider. `vericairn decisions` lists the current decisions and `vericairn decisions <id>` shows one cleanly.

A question for a person is an `ask` record: whom it is put to, the question, the recommendation first, the consequence of leaving it unanswered, and its state (open, notified or withdrawn). A decision that names it in `answers` answers it; waiting never does. An idea worth keeping without approving it is a `possibility`, with its disposition (open, parked, adopted or declined). Both are written with `records new`; `records describe ask` and `record-reference.md` list their fields. `vericairn ask [--to <principal>]` reads the waiting asks, oldest first, delivering at most three per principal and listing the rest as queued. Reading or waiting answers none. Sections 17 and 18 cover the goal runtime and roster routing that can create these asks.

### Record an answered ask with its design changes

From 0.8.4, `decisions new` records an answer the operator has already supplied in conversation. Supply their exact words and the time they answered. The ask identifies the deciding operator; `--by` identifies the recorder. The command neither chooses an answer nor treats waiting as consent. Recording the decision makes the ask's derived state answered.

```sh
vericairn decisions new --by <recorder id> --ask <ask id> \
  --statement "Revise the guide as recommended." --answered-at <UTC answer time> \
  --base <full base commit> --document design/guide.md \
  --title "Revise the guide" --summary "Use the revised guide." \
  --synthesis "The operator accepted the proposed guide revision." \
  --effect "Revise the guide." --not-authorized "Publication."
```

Choose the local base commit before this decision's changes. The runtime inspects changes under `design/` from that commit through its estate working tree, including committed, staged, unstaged and untracked files that Git does not ignore. Repeat `--document` for every changed design file; omit it when none changed. Missing, extra or repeated paths refuse the write. Files elsewhere are outside this design document list. Deletions, unresolved conflicts, links and unavailable files refuse because each member needs readable bytes and a pin. Sparse or assume-unchanged design entries also refuse until Git can observe their edits. Staged changes remain in the list even if working bytes were restored; settle those bytes before pinning. No fetch occurs.

The decision time defaults to the answer time. Both must use UTC second precision (`YYYY-MM-DDTHH:MM:SSZ`), the answer must not precede the ask, and neither may be in the future. An explicit `--decided-at` cannot follow the answer. A withdrawn or already answered ask refuses a new decision; inspect its existing answer first. `--expected-ask` can hold the current ask revision to a hash read earlier.

Every changed document gets its next revision through the same preparation as `docs pin`, keeping its current title and using the decision summary. Existing pin rules still apply, including refusal of unchanged bytes, generated outputs and forks. The decision retains the exact ask hash and answer time, base and observed head commits, and each document pin's id and hash. The entire proposed record set is validated before any record is stored. A storage interruption can leave part of a batch: its receipts and retained journal require reconciliation before retrying. No record is removed.

Until configurable writing rules are defined, decision prose is checked case-insensitively against model identities recorded in the estate's run execution history and qualification evidence. This covers title, verbatim statement, summary, synthesis, effects and exclusions. A refusal names the field without repeating the identity. The recorder must resolve the wording with the operator when their verbatim statement is refused; the command never rewrites it.

`--stake` defaults to `low`; `--affects` names affected work orders, and both `--effect` and `--not-authorized` may repeat. Text output includes the decision and pin receipts. `--json` preserves the complete response, including refusals and any receipts requiring reconciliation. See the [CLI options and API contract](cli.md#vericairn-decisions-new).

Standing permission is an authority record based on a decision: a grantee, scopes (`local-engineering`, `repository-write`, `self-accept-ordinary-work`, `accept-work`, `administer`, or free text), the actions reserved to the operator regardless, and conditions. A builder with `self-accept-ordinary-work` may accept its own ordinary work orders; milestones and releases always need the operator. From format 2 a grant may carry an `expires` time: authority is judged at the time each record was authored, so a record authored after the grant expired cannot rely on it. For ordinary scopes the current grants are otherwise used: a later grant or removal can change their derived authority. Format-3 policy scopes use the cited grant at the action's authoring time; a future grant cannot authorize an earlier policy action. Exact signed definition approval remains separate.

## 5. The work loop: order, brief, assignment, attempt, result

A work order states intent, scope, deliverables and the one question whose honest yes closes it.

From 0.8.3, `work new` accepts these fields directly, without a JSON file. The admitted device
of the principal named by `--by` signs it, just as it signs `records new`:

```sh
vericairn work new --by <operator or builder id> --key W-1 --title "Build the widget" \
  --intent "Why this work exists." --scope "widget sources" --scope "widget tests" \
  --deliverable "widget.txt" --exit-question "Does the widget build and pass its tests?"
```

`--key`, `--title`, `--intent`, `--exit-question`, at least one `--scope` and at least one
`--deliverable` supply the required work-order fields. `--kind` defaults to `ordinary`; use
`--kind milestone` for a milestone. Repeat `--scope` and `--deliverable` to retain each item in order.
Optional `--after <work-order id>` names a predecessor's record id, and `--input <type:id>` names an
input record, for example `--input decision:<decision id>`; repeat either for multiple records.
With neither option, the order has no predecessors and an empty input list. `--after` needs record
format 2 or later, as it does in the JSON body.

The command builds a work-order body and passes it through the same writer as `records new work-order`.
Missing fields, overlong text, malformed references, unknown predecessors, duplicate keys and signing
refusals have the same messages. Success prints the record path, id and hash; `--json` prints the
receipt as `{"path":"...","id":"...","hash":"sha256:..."}`.

To supply the full record body, or with 0.8.2 or earlier, use the existing command:

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

An output's `location` is a repository-relative path. From 0.9.2 a new result whose output location carries a URL scheme, such as `https:` or `file:`, is refused (`result.output-scheme`); put the reference in `evidence`, whose `location` may be a URL, and name an output held elsewhere by its hash and bytes without a `location`. Results already in the estate keep their reading, and `estate check` reports each one as a warning naming its outputs with a scheme.

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

A result review may also list the exact records it read in `read` (`[{"type": "result", "id": "...", "hash": "sha256:..."}]`). From 0.9.2 (FIX-94), what a review needs depends only on its result. A review of a result without duties needs no `--expected` and no device, with or without `read`. Given `--expected`, the review is refused unless that is the result's current hash. A review of a result with duties is signed duty evidence, with or without `read`. It needs `--expected` with the current result hash and an admitted device of the reviewer whose key this runtime holds. A missing hash or device is refused by name, while a stale hash is a conflict. `work review` always records a signed review, so it always needs both. [Inspecting estate policies](policies.md) describes duty evidence.

An acceptance closes a result or a work order, pinned to its exact hash:

```sh
vericairn records new acceptance --by <operator id> --subject work-order:<order id>:<order hash> --body accept.json
```

```json
{"statement": "I accept W-1 from the recorded evidence.", "basis": ["<review id>"], "self": false}
```

`basis` lists reviews of the same subject (a review of a result is not a basis for accepting the work order; a review of the order is); it may be empty for an operator's acceptance. `self` must be true when the accepting principal authored the subject. The operator can accept ordinary work; another principal needs `self-accept-ordinary-work` for its own ordinary work or `accept-work` for another principal's. Milestones and releases remain the operator's. The subject hash comes from the record file or from `--json` output of the command that wrote it; `vericairn records hash <file>` prints it. A refused acceptance lists every reason at once and writes nothing.

From 0.9.2, an acceptance by a principal other than the operator rests only on reviews that passed: a new one whose basis names a review with outcome `findings` or `fail` is refused (`acceptance.basis-outcome`), whatever authority scope it holds. The operator's acceptance is unchanged; it may rest on any review of the subject, or on none, on the operator's own judgment. The rule is prospective. Acceptances already in the estate keep their reading and still accept their subject; `estate check` and `records validate` report each one that names such a review as information, with the review and its outcome.

### Closing finished steps (0.8.4)

The per-release closing scripts are retired. `vericairn operator close` lists every latest
completed, reviewed and unaccepted result, showing its exact hash, passing reviews and their
independence, and the work order's exit question. Run it on your own admitted operator device:

```sh
vericairn operator close
vericairn operator close --select W-1 --select W-2 --by <operator id>
vericairn operator close --all --by <operator id>
```

The selection displays its evidence before signing one acceptance per result. Each acceptance
names that exact result and all displayed passing reviews. Changed evidence or a non-operator
refuses the entire action without acceptances. Custody checks presence once per acceptance; from
0.9.3 the selection's acceptances are announced before the first prompt, and each prompt says which
of them it signs ([presence prompts](#presence-prompts-announced-and-counted-093)). `--all` covers every eligible step in the estate; use `--select` to close only
the steps you inspected for a release. See [Operator actions](operator-actions.md#close-finished-steps-in-one-action)
for saved listings with `--from`, API/SDK use and interrupted-storage recovery.

From 0.9.3 (FIX-128), an ordinary work order whose current result is accepted this way reads as `accepted` in status, goal
predicates, flows, required attention and Workbench, so ordinary work closed with `operator close` since 0.8.4 now reads as
accepted where it read `planned` before. An acceptance of an earlier result, or one recorded before the work order changed other
than in its placement, reads `accepted (earlier revision)`. A milestone still needs its own acceptance
([details](operator-actions.md#close-finished-steps-in-one-action)).

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

### Release lines and channels in 0.9.0

Use `release submit --line <name> --channel <channel>` to prepare a release on a
named line. Omitted values mean `current` and `stable`. The same options apply
with `--check`; validation happens before an intent or build is dispatched. The
intent retains these values, and observation copies them into the candidate.

A product can declare `lines` as an array of `{name, channel}` objects. Each LTS
declaration also requires `endOfSupport`, an RFC 3339 timestamp. Line names match
`^[a-z0-9][a-z0-9.-]{0,39}$`. Once declared or used, a line keeps its channel;
stable and LTS lines may also carry prereleases of their next version. Nightly
lines contain only nightly releases. Versions cannot be reused in another line
of the same product, including across product revisions.

For an LTS release, also supply `--end-of-support <timestamp>`; other channels
refuse that field. It cannot precede the observed publication time. To carry a
published change across lines, supply both `--backport-of <release-id>` and
`--backport-hash <exact-hash>`. The source must be a current published release of
the same product in another line. Every channel permits this reference.

The GitHub provider prints these manual publication commands:

| Channel | Command |
| --- | --- |
| `stable` | `gh release edit <tag> -R <repo> --draft=false --latest` |
| `lts` | `gh release edit <tag> -R <repo> --draft=false --latest=false` |
| `prerelease` | `gh release edit <tag> -R <repo> --draft=false --prerelease` |
| `nightly` | `gh release edit <tag> -R <repo> --draft=false --prerelease` |

Only stable marks GitHub latest. The operator still accepts the exact candidate
and runs the publication command; every channel follows the existing acceptance
and observation procedure. An older provider without channel support supplies
its original stable command and reports other channels as unsupported.

`docs notes --line <name> --from <version> --to <version> --out <set>` selects a
line's release history and shows backports into and out of that line, with both
versions and the exact source release. A backport appears in the notes window
containing its publication; earlier frozen sets remain unchanged. The selected
line and the existing exact notes window are recorded in the set's manifest.

The product may also carry `cadence: {policy: {type: "estate-policy", id, hash}, rule}`,
pinning a recurring-duty rule. See [record fields](record-format.md#release-lines-and-channels-090).

### Release cadence (0.9.0)

A cadence makes a release expected. Declare an ordinary recurring duty and pin
its exact policy revision and rule in the product's `cadence`. An interval duty
opens on its declared dates; the duty's `release` schedule form opens one occurrence
for each observed publication of its named product. The existing duty schedule,
completion evidence and explicit debt dispositions determine which occurrences
remain due. A cadence does not create an occurrence record when it is read.

`status` and `progress` show each due occurrence under **What waits on the operator**,
with its product, line, target version, due time and exact policy and occurrence.
Targets come from current release candidates and open `releasePlan` work orders;
a candidate supplies its recorded line, otherwise the line defaults to `current`.
Published versions are omitted. When no target is recorded, the view says
`target version not declared`; it never invents a version. These views use the
same recorded-time basis as other policy findings, shown in their output.

`release submit --check` and the shared submission gate show the due occurrence as
context for the requested product, line and version, evaluated at submission time.
A cadence creates no release objection, including when its policy declares a
`refuse` level. Other duties, record validation, document checks and release
objections remain in force. The exact pinned schedule is the cadence: an editorial
policy revision preserves it, while a changed schedule needs a new product pin.

Future, completed and explicitly ended occurrences add no cadence wait. Reading
or evaluating a cadence signs, records, retains and dispatches nothing. A person
still submits the release, accepts its exact candidate and performs publication
through the existing release procedure. No cadence prepares, submits, accepts or
publishes a release automatically.

### Release procedure

From 0.9.2 a release can run as a goal on the software pack's release flow, which orders the steps below and checks the traps of
earlier releases before each ([the software release and upgrade flows](#the-software-release-and-upgrade-flows-092)).

A release of a software product is recorded as intent before effect, through a release provider: a module that builds a release on a destination, observes the run and the draft, and names the operator's manual publication without ever running it. 0.9.2 carries one, the GitHub Releases provider (`github-releases`), which dispatches a GitHub Actions workflow and observes it through the GitHub CLI logged in as you on the runtime's machine; Vericairn never sees or stores the token, and never publishes. `vericairn release providers` lists the providers and adapters this build carries, each with the settings it declares, their defaults and the setting that names the destination.

A release goes through the provider its request names (`--provider`) or the one its product declares, and is refused when neither does. A product declares how its releases are made in the optional `release` object of its product record: the destination (the provider and the settings the product fixes, for GitHub at least `repository`), the checksum asset, and the artifacts a complete release carries, each a name with `{version}` and optionally `{platform}`, a role (distributable, bill of materials, notices, documentation or other) and its platforms. Without declared artifacts, a generic rule reads the draft: a platform identifier in an asset's name gives its platform, and a draft is complete when its checksum file names every other asset. `software.md` has the record's shape. Published releases before 0.4.2 refuse a product record that declares a release profile, so an estate records one only after every clone that writes to it runs 0.4.2 or later.

```sh
vericairn release providers
vericairn release submit --check --product <product id> --version 0.9.2 --commit <exact-commit> --expected <current-product-hash> --by <builder id>
vericairn release submit --product <product id> --version 0.9.2 --commit <exact-commit> --expected <current-product-hash> --by <builder id> [--provider github-releases] [--repository owner/name] [--ref main] [--workflow release.yml] [--setting name=value]
vericairn release observe --version 0.9.2 --product <product id> --by <builder id> --qualified linux-x64 --qualified osx-arm64 --qualified win-x64 --limitation "..."
```

From 0.8.3, `release submit --check` uses the intended submission's options and reports
every admission objection with its next action. This includes document and policy holds, current authority,
signing-device availability, the exact product revision, unresolved release identities, and the size of the
basis and source objects that submission would retain. Nothing is signed, recorded, retained or dispatched;
the check needs no presence and does not recover interrupted journals. A missing signing device is an
objection to the eventual submission, not a reason to prevent inspection. Exit status is 0 for a passing
check and 2 for objections. `--json` carries each objection and next action, the checked commit and product
hash, both build identities and the measured basis size.

The gate also requires clean registered clones for every repository it reads and checks the release commit
against the destination clone's locally known default branch head (`refs/remotes/origin/HEAD`). Register
the destination repository resource and prepare or refresh its refs separately; the check never fetches or
changes a clone. The CLI sends its build identity automatically; format 3 requires the same full version
and source commit as the runtime. A passing check is an observation at that instant. Submission checks
again, so changes to records, reviews, source refs or files can produce new objections.

From 0.9.3, `release submit` and `release observe` announce every record they will sign with a device that needs presence
before the first prompt, and name each prompt "Signature k of n" with what it signs; [presence prompts](#presence-prompts-announced-and-counted-093)
lists how many each asks for.

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
names that grant. A warn-level document without a registered clone is shown as due because its comparison is unavailable. A refuse-level policy
with unavailable required source bytes still holds the release. No gate fetches or checks out sources.
Normal `docs living` uses repository HEAD, or the reviewed commit when HEAD is its ancestor;
estate files use the estate's working tree. Current estate
records supply triggers and reviews; they are not a historical estate snapshot at the release commit.

POLICY-03 combines built-in objections, living-document gates and applicable policies before a build
can start. Format 3 requires `--commit <exact-commit>`, `--expected <current-product-hash>` and an
admitted held signing device. A refusal reports all known objections and starts no provider operation.
Sources, authority, exceptions and deadlines are rechecked after preparation and before dispatch.
The retained basis is inspectable with `release basis <evidence-hash>`. A preview grants no submission
authority. An effect already dispatched remains under its original identity if later observation fails;
a client cancellation does not roll it back. See [estate policies](policies.md).

From 0.8.3 (FIX-58), `release basis <evidence-hash>` distinguishes a wrong
identifier from lost retained sources. An unknown hash or unsupported object reports
`No supported retained release basis has this hash`, followed by the requested hash. A retained
basis whose references cannot resolve reports `Retained release basis <hash> cannot resolve`
and lists every failed reference. A source finding names its repository, revision, relative path
and expected hash; a proof finding names the object hash. `release-basis.missing` means the
referenced object is absent; `release-basis.hash-mismatch` means its bytes no longer match its
hash. Record failures name the record type, id and expected hash. A record-set digest mismatch
also keeps the evidence unavailable, including when signatures changed.

Both outcomes exit 2. For a basis that cannot resolve, `--json` returns its hash, `evidence: null`
and a `release-basis-unavailable` problem with all findings; it never returns partial evidence.
The API returns this same response with HTTP 200 and the SDK preserves it. Unknown or unsupported
hashes retain HTTP 404 and SDK null. Check the identifier for the latter; for a resolution problem,
use the named references to recover the exact retained bytes. Replacing them with current source
bytes does not restore the historical basis. Reading a recovered basis grants no submission authority.

From 0.9.2 (FIX-95), a basis keeps the files of a repository read at an exact commit, such as an
evidence repository a duty result names, by path and hash alone, and `release basis` reads them from
that repository's Git objects at the commit, through its registered clone. A source finding whose
revision is a commit means no clone registered here holds that commit at that path and hash; register one
with `vericairn sources clone <path> --resource <id>`. Submission writes only the estate's own working-tree
files beside the basis object; commit them with it. Copies of exact-commit files that 0.9.1 and earlier
wrote are no longer needed to read their bases. See [estate policies](policies.md) for the retention bound.

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
gh release edit v0.9.2 -R owner/name --draft=false --latest
vericairn release observe --version 0.9.2 --product <product id> --by <builder id>
vericairn release list
```

The published revision is refused without the operator's acceptance of the candidate it supersedes. An uncertain identity first needs observation and reconciliation; a retry is available after known failure or cancellation with `--reconciliation`. `--cancel` records reconciliation that an intent was never dispatched or an uncertain effect did not occur; an active provider call refuses cancellation. The runtime does all of this behind `POST /v1/releases/submit` and `observe`, so the command line and Workbench's release workspace (section 13) do the same thing and refuse the same things.

For Vericairn itself, publishing the GitHub release triggers the separate NuGet Trusted Publishing workflow, including for a published prerelease. It retrieves exactly the package assets the operator publishes, verifies them against that release's own `SHA256SUMS`, and uploads without rebuilding, using short-lived credentials. It does not compare with the estate's accepted candidate or require its acceptance record. Publish the exact accepted draft to preserve that binding. The packages are `dotnet-vc`, the `Vericairn` SDK front door and the `Vericairn.*` libraries. A retry uses `--skip-duplicate`; it cannot replace a package version already present or verify that existing copy's bytes. `install.md` covers both package and archive installation. FIX-13 checks every workflow's action references
against the repository's full-SHA allow-list. The NuGet pull-request job builds and verifies packages and
symbols with the publication verifier, without a login, token permission or upload. This does not verify
the organization's live Actions settings or bind the published draft to estate acceptance.

### Security reports hold a release (0.9.2)

From 0.9.2 (OPS-11), the security advisories filed against a product's repositories reach you as required attention within minutes, and a security report you have not triaged holds every release. GitHub raises no issue, discussion or notification for a draft advisory or a private vulnerability report, so Vericairn reads them itself.

**What holds.** An advisory in state `triage` (a private vulnerability report) or `draft`, or in a state this release does not know, is a report: it holds every release until your triage names its current revision. A `closed` or `withdrawn` advisory holds until a triage of yours names it at any revision, so a report that anyone with administrator access closed cannot slip past you unseen. A `published` advisory is your own disclosure: it holds nothing and needs no triage.

**Turning it on.** As the operator, record a product revision that declares the repositories to read, at most eight `repository` resources located on GitHub:

```json
"securityReports": {"provider": "github-security-advisories", "repositories": ["<repository resource id>"]}
```

GitHub keeps repository advisories only for public repositories and answers a private one's listing with "not found", which the reader refuses, so declare the public repositories where reports are filed. Once it is declared, triage each report already waiting: those in `triage` or `draft` state, and each closed or withdrawn one you have not triaged before. Published advisories need nothing. Write the whole current product body with the declaration added to a file, then run `vericairn records new product --by <operator> --body <file> --supersedes <current product id>`. Only you declare, change or remove it; a revision by anyone else that touches it is refused (`product.security-reports-author`). From the declaration on, only you change the kind or location of a repository resource it names (`resource.security-reports-author`); a change made earlier under its author's authority keeps its reading. A resource it names that is no longer a repository on GitHub holds every release until the declaration names a repository again. While any product declares it, every release of the estate waits for it.

**How they are read.** The `github-security-advisories` reader runs `gh api` with your own `gh` login, read-only. It lists every advisory of each declared repository in every state, page by page, at most 30 pages of 100. It posts nothing, and Vericairn never reads, stores or logs your token. GitHub shows drafts and private reports only to a repository's administrators, through a token that carries the `repo` or `repository_advisories:read` scope. So the reader refuses, rather than report as complete, a login that is not an administrator, a token without either scope, and a token whose scopes it cannot confirm, such as a fine-grained or app token, including one in `GH_TOKEN` or `GITHUB_TOKEN`. `gh auth login` gives a token with `repo`. A read starts only while GitHub's core allowance keeps at least a fifth of its limit, and at least 100 requests, for your own use, and stops before a further page once a page leaves no more than that; each read costs one request for the repository and one per page. The daemon reads 30 seconds after it starts and then every 5 minutes, and `vericairn security-reports poll --by <operator>` reads now. Without a running daemon, the next poll or release check reads them.

**What is kept.** Each advisory is kept as its identifier, repository, state, severity and a hash of GitHub's response for it, in this machine's live store under your own Vericairn home. Its summary, description, reporter and credits never reach a record, a log or any surface.

**Attention.** Each advisory that holds is an `incident` in your attention, in human `vericairn status`, Workbench and Remote, naming its identifier, state and severity, until your triage settles it. Each new or changed advisory that holds nothing is `information` there for a week, naming the same, and asks nothing of you. A declared repository that cannot be read completely, or has had no complete read for 15 minutes, is a `failed check` that says why. Only the operator sees any of them, and through the event stream they reach only you, their subjects named by an opaque key. To any other principal they do not exist, and `security-reports poll` refuses them alike.

**The hold.** `release submit` and `release submit --check` read the declared repositories again, unless a complete read is less than a minute old; a check keeps what it read in memory and leaves this machine's observations as they were. They refuse while any advisory holds (`release.security-report`, naming it with the exact triage command) or any declared repository cannot be read completely (`release.security-reports-unavailable`). A missing or logged-out `gh`, a login that is not an administrator, a token whose scopes do not show drafts, a network failure, a rate-limit stop and a listing larger than one read follows each refuse, with the reason. A principal other than the operator gets one objection, `release.security-reports`, which names and counts nothing; the MCP server's `release_submit_check` speaks for the principal its connection acts as, and for any connection that does not act as the operator it reads nothing and answers with that one objection. There is no override: to release without the reader, record a product revision without the declaration, which is your recorded act, and declare it again afterwards.

**Triage.** Review the report on GitHub, then record your triage of the exact revision that the attention notice or `poll` shows:

```sh
vericairn security-reports poll --by <operator>
vericairn security-reports triage <GHSA identifier> --repository <owner/name> --expected <content hash> --disposition security --by <operator>
```

The disposition is `security` (a vulnerability to fix), `duplicate`, or `no-action`, which needs `--reason`. The triage is an `observation` of kind `security-triage` that pins the repository resource and names the advisory only by identifier, repository and content hash, with the disposition and your reason. Write the reason in your own words, never the report's text. Any later change to a report in `triage` or `draft` state, such as a new severity or edited text, is a new revision that needs triage again. Publishing it ends the hold without another triage, and so does closing or withdrawing a report you have triaged. You may also triage a published advisory, which clears its information notice, but it is never required. Only the operator triages a security report, since no security role exists yet.

**Limits.** Observations belong to the machine that read them, and another machine reads for itself before it releases. A triage record names the advisory's identifier, so in an estate whose records are public it shows that a report existed and when it was triaged: declare security reports only where the estate's records are private. The estate's GitHub App replaces this reader in a later release.

### Withdrawal and moving planned work (0.8.3)

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

Install the target release, then restart any daemon serving the estate before checking or applying
the upgrade. From the estate directory on the daemon's machine, using that installed release, run
`vericairn runtime stop`, then `vericairn runtime start`. Installing a binary leaves an existing daemon
running its previous release. Restart running workers with the new release after their active work
has settled. The upgrade command does not restart workers itself; from 0.9.2 it replaces a daemon of
an older release than the new pin, as [the serving runtime after an upgrade](#the-serving-runtime-after-an-upgrade-092)
describes.

```sh
vericairn estate upgrade --to <published-version> --check
vericairn estate upgrade --to <published-version> --by <principal id>
```

Run with the binary of the version you are upgrading to. From 0.8.3, the upgrade uses the
estate's recorded Vericairn release when present. Otherwise it verifies the published Vericairn
dependency: stable published GitHub metadata, tag commit equal to the running target build's commit,
`SHA256SUMS`, and the platform archive's bytes. It resolves the tag again after hashing the archive
and refuses a moved tag. Anonymous requests use no credentials, cookies, proxies or trace propagation.

The plan shows the formats, migration steps and artifact hashes without writing anything. Applying
writes the verified `vericairn-release` observation with the migration record, then moves the manifest
to release mode and pins the observation's hash. A source pin moves through exactly the same command;
the migration names both pins. The builds are unsigned developer builds: published hashes identify
bytes, and this machine verifies only its own platform archive. Existing record bytes and the record
format contract are unchanged. Review and commit the change; the bootstrap verifies the archive
against the manifest's hash as before. Do not author a product release record for a dependency.

An upgrade also keeps generated registers valid when the target's generators write different bytes
(FIX-83). In 0.9.0 the work-order register adds a Disclosure section and the release register adds
line, channel, end-of-support and backport sections, so members 0.8.4 wrote no longer match. The
plan lists each declared generated member whose file holds exactly what an earlier implementation of
its generator wrote for the same records, input digest included; `--check` names them and writes
nothing. Applying rewrites only those members to the current generator's bytes, in the same change
as the migration record, which states per register how many members were rewritten and by which
implementations. Records, authored files and other documents are not touched. A member that matches
neither the earlier nor the current output was edited by hand. It is never overwritten:
`estate check` keeps reporting it as `document.generated-edited` until you inspect it and run
`vericairn docs regenerate <path>`. This release reproduces the first implementations of the
decision, work-order and release registers; reapplying the installed target rewrites nothing.

Add `--from <download-directory>` to both check and apply to use the [offline inputs](install.md#offline-inputs).
The directory belongs on the runtime's machine and holds downloaded release/tag responses,
`SHA256SUMS` and the archive. Verification reads these inputs without network access or file changes.
Offline tag evidence describes the saved snapshot, not later GitHub state.

| Refusal | Next action; no observation, migration or pin change is written |
| --- | --- |
| Tag differs from the target commit or moves during verification | Investigate the tag and obtain a consistent target build and publication snapshot. |
| Missing `SHA256SUMS`, missing platform entry or mismatched archive | Download the checksum file and platform archive again from the same complete release. |
| Draft or prerelease | Choose a stable published release and run its command line. |
| Target cannot read an estate format or writes an older format | Choose a forward-compatible target; downgrade does not rewrite historical records. |
| Network unavailable | Retry with access to GitHub, or supply the downloaded offline inputs. |
| Command line or daemon differs from the target | Install the target command line and restart the serving daemon with that exact build. |

`estate check` reads release records, dependency observations and cached stable-release knowledge
without making a release request. `manifest.release-behind` covers release pins and source pins;
for source pins its comparison is to the build performing the check. It names the target's
upgrade check command and leaves the pin unchanged. The cache is knowledge, not automatic approval.

An estate at format 3 upgrading from 0.8.0, 0.8.2, 0.8.3, 0.8.4, 0.9.0 or 0.9.1 applies no format step. From 0.8.2 (FIX-38) the upgrade appends its
migration record before it replaces the manifest, and replaces it atomically: an upgrade that stops
between the two resumes from that migration record when run again, and until then opening the estate
warns `upgrade.incomplete`. A legacy release pin with no migration record is refused; a fresh `init --release` pin has its exact dependency observation instead.

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

### The serving runtime after an upgrade (0.9.2)

From 0.9.2 (OPS-05), no runtime of an older release keeps serving an estate on its machine after an
upgrade. Applying the upgrade, or running it again once applied, ends with a runtime step; `--check`
leaves the runtime alone. The step reads the daemon that serves the estate here, as its endpoint file
and its health describe it, and compares its release, without build metadata, with the release the
manifest now pins:

| Found | What the upgrade does |
| --- | --- |
| No daemon serves the estate | Nothing. It starts no daemon where none ran. |
| A daemon of the pinned release | Nothing; `runtime:` names it. |
| A daemon of an older release | Stops it. When the upgrade's own command line is the pinned release's build, with its version and the source commit its release record or dependency observation names, starts that release's daemon from it and names both on a `runtime:` line. Otherwise it names the one it stopped and warns with the command that starts the pinned release's. |
| A daemon of a newer release, or one whose release cannot be ordered | Leaves it running and warns with the mismatch. |
| A daemon that serves another estate or another root | Leaves it running and warns. |
| A daemon that refuses this user's connection, or that cannot be stopped | Leaves it running and warns with exactly what to run. |

A daemon that refuses the stop request, such as another user's, or that still answers 15 seconds after
it was asked to stop, is named with its process and release. The warning says to run
`vericairn --estate <root> runtime stop` as the user that started it, with that release's command line
or the current one, and to end the process if it still answers. Then start the pinned release's runtime
with `./bootstrap runtime start` in the estate's directory (`.\bootstrap.ps1 runtime start` on Windows),
which fetches and verifies exactly the pinned release, or with `vericairn runtime start` from that
release's command line. The upgrade's observation, migration record and manifest are the same whatever
the step finds, and a warning from it leaves the exit code at 0.

The step runs after the upgrade's own runtime has answered or closed, so neither the upgrade nor the
daemon it stops holds the working tree's write lock ([one writer per working tree](#one-writer-per-working-tree-092))
while the new daemon opens the estate. A daemon installed as a per-user service starts the command line
it was installed with at the next login; after an upgrade, run `vericairn runtime uninstall`, then
`vericairn runtime install`, with the pinned release's command line. Workers are not restarted.

`vericairn runtime status` shows the serving daemon's release against the pin on a `release` line and,
while they differ, a `mismatch:` line with the commands that settle it. Human `vericairn status` ends,
when a daemon served it, with a `Serving runtime (this machine):` line and any `Mismatch:` line; the
canonical `status --json` document is unchanged, and a mismatch goes to stderr as a warning.

### Adopting history checkpoints after the upgrade (0.9.2)

The upgrade to 0.9.2 changes no record, and an estate that holds no history checkpoint reads exactly as before. Adopting
checkpoints is then your step, once, after the upgrade and the runtime step, and it is listed under what waits on you
(`operator list`) until an adoption checkpoint counts:

```sh
vericairn estate checkpoint --adopt --check
vericairn estate checkpoint --adopt --expect-plan <plan> --by <operator id>
```

Read the check first: what adoption witnesses and anchors, records signed by revoked devices and record files deleted in the
repository's history ([history checkpoints](#history-checkpoints-092)); pass the `plan:` hash it ends with to `--expect-plan`. Only you or a person holding `administer` signs the
adoption; an agent's is refused. Commit the checkpoint and its inventory objects under `objects/` with the estate. From then on
every upgrade, in an estate that has adopted, writes a checkpoint with trigger `upgrade` in the same batch as its migration,
signed by the device of the principal `--by` names, and prints it after the migration record; a checkpoint that cannot be
written there is reported as a warning and the upgrade is applied. An upgrade whose migrations rewrite record files writes
none, and the next checkpoint witnesses them. A new estate adopts at birth and needs none of this.


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

From 0.9.3 (FIX-112), an export records each member's mode as Git does, `100755` for an executable file
and `100644` for any other, in its inventory entry and in its archive entry's attributes, so the
restored `bootstrap` runs as `./bootstrap` with no permission repair. Records and objects are never
executable. On Linux and macOS a file is executable when its owner may execute it; Windows keeps no
such permission, so an export made there records the POSIX `bootstrap` script as executable and every
other file as not. On Linux and macOS a restore creates an executable member with execute permission,
filtered by the umask like every file it creates, and an ordinary member as before; on Windows it
applies no mode. The verified inventory alone decides the mode, never the archive entry's attributes.
A mode other than those two, such as a link's, a directory's or one with setuid, refuses the export
with `export.entry-mode` before anything is written, so a restore still creates only regular files
within the destination. An export written by an earlier release records no mode and restores as it
always did. A copy restored by an earlier release has lost `bootstrap`'s executable permission: run
`vericairn --estate <restored copy> estate bootstrap` there, which reports `unchanged` for identical
bytes and restores the permission ([section 2](#2-create-an-estate)).

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

An object holds at most 8 MiB (8,388,608 bytes), and an export carries nothing held outside the estate repository. From 0.9.2, [Large private artifacts](large-artifacts.md) shows how to keep a private artifact above that limit in a private store with exact-byte custody, using existing records, and how to prove a cold recovery by reading its bytes back.

An export also leaves out the housekeeping that native operations write under `.vericairn/`, such as `.vericairn/record-staging/.gitignore`, and it doesn't carry your agent instruction files at the root yet. A restored copy therefore holds three inventories: the exported payload, which `export/inventory.json` lists; supplemental private or external sources, which you restore from your own capture; and the housekeeping that writes add once you use the copy. [The three inventories of a cold recovery](large-artifacts.md#the-three-inventories-of-a-cold-recovery) lists the housekeeping exactly and shows how to classify every restored file before anything writes to it, and how to reconcile a file that fits no class without skipping or deleting it.

### A damaged live store (0.9.2)

Each estate has a live store on this machine, `live.db` under the Vericairn home, beside its `-wal` and `-shm` files. What it holds depends on the storage profile. For a repository-synced estate, the repository's record files are canonical and the live store is a cache derived from them, one per clone. For a local-only estate, the live store is the canonical copy of the records.

From 0.9.2, the Vericairn home, every directory from it down to an estate's live store, and the live store itself with its `-wal` and `-shm` files are readable and writable by their owner alone on Linux and macOS: directories 0700 and files 0600. That holds whichever process creates them, a daemon or a command that runs the embedded runtime, and for a local-only estate's `objects.db`, the batch journals and the keyword search index `search.db` kept beside the store. A command that opens the estate for writing narrows ones an earlier release left wider. A live store directory or database that belongs to another user is refused: that user could read and change it. On a filesystem that keeps no Unix modes, such as FAT, exFAT, NTFS through FUSE, some network mounts and WSL's `/mnt`, permissions cannot be restricted and mean nothing, so the store is used as it is: the command says so once on standard error, a detached daemon writes it to `daemon.log`, and `vericairn doctor` names the path on a `state` line. Keep the Vericairn home on a filesystem with Unix permissions where other people use the machine. Windows keeps the per-user permissions of the profile the home lives in.

From 0.9.2 (FIX-93), a live store that SQLite cannot use, such as a malformed database, stops the command with a diagnostic instead of an unhandled exception. The diagnostic quotes SQLite's error, names the profile and the store's path, and gives the profile's recovery route. A store that another runtime holds says so, and the route is to let that command finish or stop the daemon.

For a repository-synced estate, rebuild the cache from the repository's record files:

```sh
vericairn runtime status
vericairn runtime stop
vericairn estate cache rebuild
vericairn estate check
```

`estate cache rebuild` moves `live.db` and its `-wal`, `-shm` and any `-journal` file together, unchanged, into a new `failed-cache/<UTC time>` directory beside the cache, for diagnosis. It then reads every record file in the repository into a new cache and reports how many it read. The record files are not changed. It refuses while a daemon answers for the estate, and while another command of 0.9.2 or later has the estate open. Every writable session holds the estate's writer lock, `live.lock`, shared, and the rebuild holds it alone. A command that starts during a rebuild is refused until it finishes. A daemon of an earlier release takes no such lock, so stop it first. The cache also keeps notes that exist only on this machine: interim progress notes, attention acknowledgments and an unfinished operator action's checkpoint. The rebuilt cache starts without them, and they stay in the kept files. Finish an operator action before rebuilding where you can. Delete a kept directory once it is no longer needed.

A local-only estate's database is never moved, deleted or rebuilt as a cache, and `estate cache rebuild` refuses it without changing anything. Stop every daemon and command that uses it, and keep `live.db` with its `-wal` and `-shm` files together. Then recover from a backup of that directory or from an earlier `estate export`. `estate restore` restores an export as a repository-synced estate.

### One writer per working tree (0.9.2)

From 0.9.2 (FIX-99), one writer at a time writes a working tree. Every runtime that writes records takes an exclusive write lock in the working tree's own `.vericairn/write-lock/` directory, whether it is a daemon or an embedded command and whatever its Vericairn home. It holds the lock from judging the write against the tree's current record files until the records and their companions are stored. A second writer therefore judges against the records the first one stored, and the ordinary rules refuse it before it writes anything, for example with `operation.duplicate-identity`. A writer that cannot take the lock within 30 seconds is refused with a `conflict` that names the holder: its process id, host, a digest of its Vericairn home and when it took the lock. The holder records these beside the lock in `holder.json`, never as a path. The operating system releases the lock when its process ends, so a stopped command leaves no lock behind. A local-only estate keeps its records in the live store under the Vericairn home, so its writers take the same lock beside that store instead.

The lock covers one working tree on one machine. It is separate from the per-home `live.lock`, which guards a cache rebuild, and it makes no claim across clones or machines. Two clones still meet at merge, where duplicates are reported for reconciliation. Keep to one writer anyway: route writes through one daemon per working tree, or run embedded commands from one home, and do not edit record files while a writer runs. A runtime of an earlier release takes no lock, so do not run one beside 0.9.2 on the same tree.

Duplicates written before this rule, by two writers of an earlier release or by two merged clones, are never repaired automatically. While such an error stands, the runtime refuses further writes. To recover:

1. Stop the extra writer. Run `vericairn records validate` or `vericairn estate reconcile` to list the conflicting records, and read both files and any records that cite them. Dispatch nothing under a duplicated operation identity until it is settled.
2. Keep one: the one later records already cite, or else the earlier one.
3. Settle the other under its record type's rules. Divergent revisions of one record take a reconciliation record that keeps one and withdraws the others. A duplicated work-order key takes a revision of the other order under a new key. Two current operations under one identity take a reconciliation that keeps one and withdraws the other, as below.

From 0.9.2 (FIX-100), a reconciliation settles operations recorded independently under one identity, as it settles independent first communication profiles. Its subject is one of them; `kept` names the latest revision of the operation that stands, `withdrawn` names the latest revision of each other one, and `note` gives your reason:

```sh
vericairn records new reconciliation --by <operator id> --subject operation:<kept id>:<kept hash> --body reconciliation.json
```

```json
{"kept": "<kept id>", "withdrawn": ["<other id>"], "note": "Both runtimes recorded the intent; the first stands, and the second was never dispatched."}
```

Only the operator or a principal with the `local-engineering` scope, the authority that records operations, settles them, and the reconciliation is signed by an admitted device of that principal, whatever the charter's signature policy. Every record it names must be an operation under the subject's identity and the latest revision of its own operation. Neither operation is rewritten: the withdrawn one stays in the estate and counts as superseded by the kept one, `operation.duplicate-identity` is no longer reported, and writes proceed. Nothing settles duplicates automatically. A withdrawn operation whose effect may have happened still needs its own observation before anything is retried under that identity.

## 11. The daemon

Commands run an embedded runtime unless a daemon serves the estate on this machine; ordinary estate operations are clients of it, so that one owner holds the live state. Restore creates another copy directly. `recover compare`, `docs notes` and `docs regenerate` read the files and live store directly, even when a daemon serves this estate.

```sh
vericairn runtime status
vericairn runtime start
vericairn runtime stop
vericairn runtime install --plan
vericairn runtime install
```

From 0.9.2 (OPS-05) `runtime status` also shows the serving daemon's release against the estate's pin, with a `mismatch:` line and the commands that settle it while they differ; `--json` adds `servingRelease`, `pin`, `pinnedRelease` and `mismatch`. An upgrade replaces a daemon of an older release than its new pin ([section 9](#the-serving-runtime-after-an-upgrade-092)).

`runtime serve` runs the daemon in the foreground until stopped; `start` runs it in the background with streams of its own, its output going to `daemon.log` beside the estate's live store, and waits until it answers; when the daemon stops before it serves, `start` exits with an error that quotes what the daemon wrote to its log and says where the log is; `stop` asks it to stop (closing a client never does). `install --plan` prints the per-user service unit (a launchd agent, a systemd user unit or a logon task) and the commands without installing; `install` registers it so the daemon starts at login; `uninstall` removes it. The daemon serves a local socket (or a named pipe on Windows), whose trust boundary is the operating-system user, and a loopback TLS endpoint pinned to its own certificate, where requests are signed by an admitted device. A configured local-network listener adds mutual TLS for paired devices (section 19); `--no-loopback` disables loopback only, not that listener. Signing and authority are checked by the runtime; `api-v1.md` has the details and `api-reference.md` every operation. Workbench's runtime workspace (section 13) does the same through this command line and shows the plan before it installs or uninstalls.

From 0.8.0 loopback checks the signing time and the device's current admission before reading a request body. Both device-signed profiles cap bodies at 8 MiB (8,388,608 bytes), or a lower configured server limit, and allow at most 30 seconds to read one, shortened when the signature expires sooner. Signature verification follows the read; the headers alone do not authenticate a body. Admission and freshness are checked again, and a key rotation during a loopback read requires signing afresh. Size refusal is `413`; timeout is `401`. Refused bodies are discarded only within 64 KiB and ten seconds after the response completes. The local socket keeps its configured body limit, 30,000,000 bytes by default; pairing messages are limited to 16 KiB.

FIX-09 adds a shared limit of eight simultaneous loopback body reads awaiting signature verification across all devices, with no waiting queue; capacity refusal is `503`. Kestrel also limits concurrent connections to 32. These bounds limit resource use; they do not establish denial-of-service immunity.

### Events (0.9.2)

From 0.9.2 the daemon keeps an event journal (`WATCH-05`), so an agent, a script or a client hears what
changed instead of polling. A record the runtime first observes, written through it or found after a pull,
raises a `record` event, and a change in attention, an ask, a flow step, an operator request or a run raises
an event of that kind. Each event is a hint, never authority: it names the subject by type, id and hash, the
change, the principal concerned and an attention category, and never carries a body, title, path, secret or
command. Read the subject before acting on it.

```sh
vericairn events watch --kind record --kind ask
vericairn events watch --json --after <cursor>
```

`events watch` follows the stream over the local socket for the principal you name, the operator by default,
and reconnects by itself. Start it before reading the state it complements, or resume from the
`eventsCursor` that `GET /v1/status`, `/v1/progress`, `/v1/attention`, `/v1/asks` and `/v1/goals/{id}`
return, and no change is missed. A `reset` line means the cursor could not be honored, for example a client
back after more than a week or after a restore: re-read the state, then continue. The journal keeps at most
seven days and 100,000 events in an owner-only file beside the live store; a clean restart keeps it, and a
crash or a lost file starts a new epoch, after which every older cursor resets. A copy of a cleanly
stopped journal restored from a backup is not detected and continues its epoch, so a client that read past
the copy can miss events: after restoring the runtime's state directory, stop the daemon, delete
`events.db` beside the live store and start it again, so every cursor resets. The embedded runtime keeps
no journal, so `events watch` needs a running daemon.

A paired device reads events only for its own principal and with its own signature; a device stream ends
after 60 minutes and reconnects. Each event passes the audience check before it is delivered: a principal's
attention and its acknowledgments reach only that principal, and what a reader may not read is dropped
without any count, error or timing that would show it. Revoking a device closes its streams before the next
event. A device holds at most four streams, one principal's devices eight and all devices 16; the local
socket keeps two more for itself, so paired devices never lock out the runtime machine's own user. A script that follows `events watch` and runs
`goal step` acts with its own principal's authority and stays Community. See
[API version 1](api-v1.md#the-event-stream-092).

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

From 0.9.3 a submission from Workbench shows its presence prompts as the command line does: the announcement in the activity log before the first prompt, each prompt's "Signature k of n" line in the status line, and how many were signed ([presence prompts](#presence-prompts-announced-and-counted-093)). The release workspace (Ctrl+Shift+E) runs section 8 from the desktop. Enter the exact Commit; the request carries the product hash captured when it was loaded. A stale product requires fresh preparation, and format 3 requires an admitted held signing device. From 0.8.3, **Check submission (nothing recorded)** runs every admission check without signing, recording, retaining, dispatching or requiring presence. It displays every objection and next action, the exact checked inputs and the measured basis size. Submission checks again. The response also displays policy holds and retained-basis inspection commands after a submission. The existing flow follows: the providers the runtime can reach are listed and the product's declared provider is selected (for a product that declares none, nothing is selected until you choose); the form shows one field per setting the selected provider declares, filled with the product's declared values, where an empty field means the provider's default, which its label names; then "Submit the release" or Ctrl+Enter records the intent and dispatches; each destination shows its preparation, submission, external review and observed availability separately; "Observe" or Ctrl+Shift+Enter records the run's outcome, the candidate with every distributable by hash, and later the observed publication; Enter on a destination opens the candidate in the review workspace for the operator's acceptance at its exact hash; the manual publication command is shown and never run; a second submission under one identity is refused exactly as on the command line, and a retry needs a reconciliation.

The theme binds the brand's roles, never values: Inter for titles, headings and body text and JetBrains Mono for identifiers, both bundled under the SIL Open Font License, one accent blue, one attention amber used only for the attention kinds, and neutral surfaces and outlines; the arrival page carries the cairn mark. The probe measures the contrast of every pair the roles form, in light and dark.

The Documents page (Ctrl+Shift+D) lists each declared living document with its state (current, due at its level, or unavailable), every trigger newer than its latest clearance, and that clearance. A review of the selected document is sent with Ctrl+Enter: the outcome, what was checked, a commit for a document of another repository, and findings. The runtime reads the content, records what the review answers and runs the declared checks; the page decides neither independence nor clearance, and a refusal shows every reason. It does not yet show a document's content beside what made it due.

Goals (Ctrl+Shift+G) shows the objective, sponsor and accepter, condition verdicts beside the evaluation, steps and their routes, budget and in-flight runs, asks and evaluations. It is a read view: use the CLI or API for activation, stepping, stopping runs, evaluation and acceptance. Home also shows each principal's delivered asks and the queued count. Enter opens an ask's goal, or its record when it has no goal; it does not answer it. The progress summary on Home opens the full report (Ctrl+Shift+P). F5 refreshes these views. Workbench opens local estates; it has no paired-runtime chooser or network-configuration form.

The runtime workspace (Ctrl+Shift+U) shows the daemon's state, where it is and what it reports about itself, with start, stop, the install plan, and install and uninstall of the per-user service after the plan has been shown.

Communication in the Work menu shows effective values, contributing revisions, conflicts, freshness and previews, and makes attributable changes for the explicitly named recipient. The hand-over workspace (Ctrl+Shift+H) prepares and retains a complete package, reads one back and shows the resume comparison. Neither view infers consent from a click or resumes a worker. Workbench has no context-allocation editor, pinned-original retrieval control or harness-qualification form; use the CLI/API for those. `communication-qualification.md` records the exercised surfaces and deferrals.

Keyboard: Ctrl (Cmd on macOS) with 1, 2, 3, 4 focuses navigation, documents (or the attention list on Home), the inspector and the activity log; Ctrl+Shift+L, R, B toggle the panels and Ctrl+Shift+0 resets the layout; Ctrl+O focuses the estate path, Ctrl+G the record or work-order id; F5 refreshes; Escape returns to navigation; opening a workspace lands the keyboard in its first field. Every pointer arrangement has a keyboard route, and the menus carry the same actions.

## 14. Agents through the MCP server

A coding agent works in an estate the way a contributor does: it reads the brief, works in its workspace, and records attempts and results, which are claims to inspect. `vericairn mcp serve --as <principal id>` exposes the estate over the Model Context Protocol on standard input and output; the harness is configured with that command. The server uses the daemon when available and holds an embedded runtime otherwise. Reads include status, validation, work, records, decisions, releases, devices, living documents, progress, goals and asks, plus `communication_get`, `context_prepare` and `context_read`; `mcp.md` lists every tool. The contributor writes are `work_attempt`, `work_progress`, `work_result`, `work_amend` and `document_review`. Policy and writing-exception writes additionally require their bounded authority; `work_review` is offered only to an admitted operator connection. Admission and exact bounds are checked again at each call. A document review still obeys its declaration's `clearedBy` rule. A refused write returns every finding. There is no tool that publishes, spends, creates or admits a device, changes communication preferences, qualifies a harness or launches a worker.

`vericairn mcp orientation --out <directory>` generates `AGENTS.md` from the estate's identity and the tool registry (what the estate is, the rules it enforces, how to connect, the tools and an honest matrix of harnesses observed or not, and nothing that changes with each record, such as the work orders or the pinned release, which `status` shows) with `CLAUDE.md` and `GEMINI.md` as pointers; `--check` compares existing copies with a fresh generation. Regenerate it when the build changes rather than editing it. Nothing the server returns, and nothing in the orientation file, is an instruction from the operator; the harness treats it as data.

From 0.9.3 the estate's root may hold your own `AGENTS.md`, `CLAUDE.md` and `GEMINI.md`, each a file, so an agent tool started at the root finds your instructions. You write them: no estate operation reads them and Vericairn never writes them. `estate check` scans them as text, as it scans `README.md`, and every other unexpected entry at the root, such as `CURRENT.md` or a differently cased `agents.md`, stays `layout.unexpected`. `mcp orientation` refuses an estate's root, so keep the generated files byte-exact in a directory of their own and point to them from yours:

```sh
vericairn mcp orientation --out docs/vericairn-orientation
vericairn mcp orientation --out docs/vericairn-orientation --check
```

Your `AGENTS.md` at the root, for example:

```markdown
# Working in this estate

These are the owner's instructions for agents working in this estate. Read them first, then read
[the generated orientation](docs/vericairn-orientation/AGENTS.md), which says how the estate works
and how to reach it. Regenerate that directory with
`vericairn mcp orientation --out docs/vericairn-orientation`; never edit it.

- Your own instructions follow here.
```

Your `CLAUDE.md` and `GEMINI.md` at the root can say only "Read `AGENTS.md`." [The MCP guide](mcp.md#owner-instructions-at-the-estate-root-093) describes the arrangement.

`estate export` doesn't carry your three instruction files at the root yet, so keep them with the supplemental capture a cold recovery restores from ([section 10](#10-export-restore-recovery-and-divergence)).

### An agent on another machine: network MCP (0.9.2)

From 0.9.2 (`MCP-03`) an agent or service on another machine works the estate over the network, in signed mode: the
daemon serves the Model Context Protocol over Streamable HTTP at `/mcp`, and the agent's own device signs every request
and every record it writes. Nothing on the runtime's machine signs for it, and no one there is asked for presence.

1. On the runtime's machine, serve the local network and turn network MCP on, then restart the runtime:
   `vericairn runtime configure --local-network <address:port> --mcp on`, `vericairn runtime stop`,
   `vericairn runtime start`. `vericairn runtime status` then shows `mcp  /mcp`.
2. Give the agent a principal of kind `agent` (or `service`) with only the roles its work needs, and pair a device for it:
   `vericairn devices pair --principal <agent principal id> --by <administrator>`. Network MCP refuses a person's device:
   a person who wants an MCP client on another machine connects it as an agent principal of their own.
3. On the agent's machine, add the connection with the code (`vericairn connections add <address:port> --code <code>
   --name <device name>`) and register the bridge with the harness, for example
   `claude mcp add vericairn -- vericairn mcp serve --connection <connection id>`.

Each call is judged as the API operation it runs, for that device, exactly as the same request sent to the API would be:
the device acts only as its principal, and what acts on the runtime's own machine stays refused. The agent gets the read
tools and the contributor writes (`work_attempt`, `work_progress`, `work_result` without duties, `work_amend` and
`document_review`); `work_review`, prompt writes, policy writes, duty evidence and the operator queue are not offered.
Every write it makes is kept in `local-network-writes.jsonl`, which `vericairn devices writes` lists. Revoking the device
(`vericairn devices revoke <device> --by <administrator> --reason <why>`) refuses its next request and ends its sessions
and server stream. A request whose `Host` does not name the runtime's own address, or whose `Origin` the operator did
not allow with `--mcp-origin`, is refused before it is authenticated; no origin is allowed by default. Token mode for
OAuth-only clients and MCP over the relay come in 0.9.3. See [The MCP server](mcp.md#network-mcp-over-streamable-http-092).

### Agents hear what changed: MCP subscriptions (0.9.2)

From 0.9.2 (`MCP-05`) an agent session subscribes to what it follows and is told when it changes, instead of
reading it again and again. It needs the running daemon (`vericairn runtime start`), which keeps the event journal
(section 11, Events); over the embedded runtime the server declares no subscriptions.

- **What it may subscribe to.** A record, a work or assignment brief, a pinned document, a goal's progress
  (`vericairn://<estate>/goal/<goal id>`) or the asks put to its own principal
  (`vericairn://<estate>/asks/<principal id>`), each a resource it may read. Context packages, operator requests and
  another principal's asks are not subscribable.
- **What it is told.** `notifications/resources/updated` for a subscribed resource, at most once a second per
  resource, and `notifications/resources/list_changed` for writes, at most every five seconds. A notification carries
  no content; the agent reads the resource again. With `mcp serve --as` the notifications are lines on standard
  output between answers, and the server follows the journal only while the session holds a subscription. Over
  network MCP they ride the session's server stream, and the bridge (`mcp serve --connection`) writes them to its
  harness as lines too.
- **What it is never told.** Nothing its principal may not read, nothing of another principal's attention, and no
  operator-request event: the operator queue stays off MCP. Revoking an agent's device ends its session and stream
  before any further notification.
- **Harnesses that hide notifications.** `events_read` returns the same events after a cursor, for the agent to call
  when it would otherwise poll: each event names only identities, hashes, kinds, states and categories, and a cursor
  from another principal, another epoch or more than a week ago answers `reset`.

Stdio sessions read the journal through their own four slots of the local socket, never the command line's, so
`vericairn events watch` is never locked out by them; beyond four sessions following at once they take turns of five
seconds, and one waiting its turn hears up to a turn later. Which harness versions
show notifications and which need `events_read` is recorded per harness version as a `qualification` with the
profile `<harness> <version> over MCP notifications` or `<harness> <version> over MCP events_read`, which the
orientation file's agent matrix shows; this release qualifies no harness. See
[The MCP server](mcp.md#subscriptions-and-notifications-092).

## 15. Documentation sets

Every release carries its own documentation set under `docs/releases/<version>` in the product's repository, written for that release and then frozen. `docs/releases/documents.json` declares the required document names. The build tool, not part of the shipped command line, generates references and the manifest from the build. Generate only from a committed source tree built at that exact commit:

```sh
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- generate --out docs/releases/0.9.2 --documents docs/releases/documents.json
vericairn docs check docs/releases/0.9.2
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.9.2
```

The manifest names the exact reference build commit, distinct from the later documentation or release commit. Comparison checks version, record formats, public inventories, complete reference bytes and retained companion inputs. A later documentation-only commit can retain that build pin only when the build sources agree; both commits must be locally available. A structural docs check does not judge prose, note completeness or release acceptance. FIX-09's frozen-set checksum guard detects changed, missing or added files in earlier sets, including withdrawn 0.7.0; never repair an earlier set in place.

The notes command renders the estate's records from 0.9.1 to 0.9.2:

```sh
vericairn --estate <root> docs notes --from 0.9.1 --to 0.9.2 --out docs/releases/0.9.2
```

`docs notes` writes the exact window to the manifest. Its exclusive start follows 0.9.1's recorded end.
From 0.8.4, decisions, work orders, acceptances, identity and policy records, and migrations are listed
once per supersession chain, at the revision current when the window ends. Revised entries name all
replaced revision IDs, earliest first, including those recorded before the window. A later revision does
not replace the wording in an earlier window. Document revisions and both release history sections still
list each revision; candidates and publications remain separate entries. Result sections retain their
existing selection and acceptance rules below.

FIX-13 gives accepted work one entry per work order, naming the latest accepted current result in the
window by recorded time and then identifier, with its acceptance. Earlier results and attempts remain
available through `work observe`; later unaccepted results remain separate. Superseded results are omitted.
Classification uses current estate state, not a snapshot at the window's end; work-order acceptance does
not accept each result. Later imports with older timestamps can require another explicit widening.
Earlier sets and their checksums remain unchanged.

### Preparing a set with the build tooling (0.9.2)

From 0.9.2 (DOCS-11) the build tooling prepares a set in the steps a release's builder took by hand. Each step writes only
the set's directory, `docs/releases/frozen-sha256.json` or `docs/releases/README.md`. Running a step again changes nothing,
and a step refuses rather than overwrite what a person wrote or what a frozen set holds. The version comes first: set
`VersionPrefix` in `Directory.Build.props` to the release's version, declare any new document in
`docs/releases/documents.json`, commit, and build the tooling in Release at that commit. The tooling refuses build sources
that differ from its commit, as `generate` always has, and a set whose directory does not name its build's version.

```sh
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- assemble --out docs/releases/0.9.2 --documents docs/releases/documents.json --overview overview.md
vericairn --estate <root> docs notes --from 0.9.1 --to 0.9.2 --out docs/releases/0.9.2
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- insert docs/releases/0.9.2 --section security.md --expected-hash sha256:<hex>
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- compare docs/releases/0.9.2
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- freeze docs/releases/0.9.2
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- index docs/releases/0.9.2 --paragraph paragraph.md
```

- `assemble` generates the references and the manifest as `generate` does. It copies every declared maintained guide byte
  for byte from `docs/` (`--guides` names another directory) and writes `api.md` as a copy of `api-v1.md`. A maintained
  file never replaces one the tooling generated, such as `cli-client-outcomes.md`. The set's `README.md` is the overview a
  person writes for the release, never the repository's README. `--overview` copies a file there once, when the set has
  none. The set's own overview is kept, and an `--overview` that differs from it is refused. The command names what is still
  pending, the overview or the notes. `--keep-references` keeps references generated earlier, at the reference build
  commit, and only copies.
- `vericairn docs notes` renders the notes and records their window, as before.
- `insert` places the operator's approved section, byte for byte, before `## What the release contains`; `--before` and
  `--document` name another anchor line or document. It refuses a missing or repeated anchor, carriage returns, a section
  without its heading line and another section under the same heading. `--expected-hash` refuses any bytes but the
  approved ones. Inserting the same bytes again changes nothing.
- `freeze` holds the set to `docs check` against the declaration, and refuses a file that is neither declared nor
  generated, such as `.DS_Store`, and a manifest of another version. It then adds every file's `sha256:` to
  `frozen-sha256.json` under the version, in path order, leaving every earlier entry as it was. Freezing a frozen set
  confirms it. A frozen set whose files differ is refused with each difference: a correction goes into the next set.
- `index` adds the set's paragraph to `docs/releases/README.md`, newest first under "Documentation sets", byte for byte from
  a file that links the set as `[<version> set](<version>/README.md)`. It describes only a frozen set, and it refuses a set
  the index already describes in other words, since the index's living-document pass revises that paragraph.

The overview, the approved section and the paragraph are a person's text; the commands only place them. `prepare` runs the
steps in order for a version, stopping at the first refusal. It runs `assemble`, then `docs notes` when the set has no notes,
`insert` with `--security`, `compare`, `freeze`, and `index` with `--index-paragraph`. For a frozen set it only confirms the
freeze and adds the paragraph. With `--json` each step prints one JSON object. Exit code 1 means the set is incomplete, as
`freeze` reports it from `docs check`, or differs from the build, as `compare` reports it; 2 means a step refused.

```sh
dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- prepare --out docs/releases/0.9.2 \
  --documents docs/releases/documents.json --estate <root> --from 0.9.1 --overview overview.md \
  --security security.md --security-hash sha256:<hex> --index-paragraph paragraph.md
```

`prepare` renders the notes with its own build, the release's. A release's builder who runs `docs notes` with the
estate's installed `vericairn` instead runs it first, since `prepare` keeps notes the set already has; the 0.8.4 notes, for
example, were rendered by 0.8.3, before FIX-64's one entry per supersession chain. The frozen-set test no longer lists the
frozen versions. Every set the release index links, every set directory except the build's own version until it is
frozen, and each set before 0.6.0 must stay indexed.

Documents that follow changing facts remain living documents (section 16).

## 16. Living documents

In 0.8.2, pin an authored estate document after editing it with one command:

```sh
vericairn docs pin docs/guide.md --by <principal id> --summary "Describe the revised workflow."
vericairn docs pin docs/guide.md --by <principal id> --summary "Update the examples." --expected <current pin id>
```

`docs pin` reads the file's exact bytes under `docs/` or `design/`, computes its SHA-256 and byte count, and records a `document-revision`. A first pin is revision 1; each later pin supersedes the current pin and increments its revision. `--title` overrides the title; otherwise it carries forward, or defaults to the file name without its extension on the first pin. Media type follows the file extension. The command prints the revision, record id and record hash; `--json` prints the record receipt. It leaves the document and its hand-written revision line untouched.

Unchanged bytes, a stale `--expected` record id, a whole generated document or generated register member, and a fork of current pins are refused with nothing recorded. To correct only a revision's summary, record a successor that pins the same bytes, as the [living-documents guide](living-documents.md#correcting-a-revisions-summary) shows; from 0.9.3 the refusals name that route, and `estate check` reports a finding on the superseded record as historical, naming its successor. A path outside the document directories or an unavailable or aliased file is also refused. Use `--expected` when acting on a pin read earlier. Two branches pinning the same predecessor leave a fork after their records merge; `estate check` and `docs pin` name both pins and the predecessor. Reconcile by recording which successor is kept and which are withdrawn, settle the text, then pin the combined bytes against the kept id. Nothing is deleted and no branch is chosen automatically. The [CLI reference](cli.md#vericairn-docs-pin) describes options, receipts and the matching `POST /v1/documents/pin`, `DocumentPin` and `DocumentPinAsync` operations.

A living document is one whose claims follow facts that change: a README that names what is released, a guide that describes a command, a table generated from records. Declare it with the triggers that make it due:

```sh
vericairn docs declare --by <principal id> --body living.json
```

```json
{"path": "docs/guide.md", "title": "The guide", "kind": "authored", "source": "estate-file", "triggers": [{"on": "covers", "level": "warn", "paths": ["src/widget"]}, {"on": "release", "level": "refuse", "product": "<product id>"}, {"on": "decision", "level": "remind"}], "checks": ["names-version", "links-resolve"], "clearedBy": "any", "summary": "Describes the widget and names its current release."}
```

In 0.8.3, FIX-50 always includes the document's own file among its covered paths. A change to its
bytes after its latest clearance makes it due even if its declaration names only release and decision
triggers. Its `covers` reason names the document and the changed hashes, at the highest level the
declaration uses. `docs living`, status, progress, the estate check and the release gate use the same
derivation; a release's applicable document due at `refuse` holds submission before intent or dispatch.
A review at the new bytes clears that reason everywhere, subject to the declared checks and `clearedBy`.
Existing reviews keep their meaning: `content` pins the document itself and `covered` pins the explicitly
declared paths. The [living-documents guide](living-documents.md) describes the comparison and clearance rules.

The triggers are `covers` (the listed paths changed since the last clearing review, uncommitted changes included), `release` (a newer published release of the product), `upgrade` (a newer migration), `decision` (a newer current decision names the document in its `documents`) and `accepted` (a newer acceptance of a work order with one of the listed `keys`). Each has a level: `remind` shows in `status`; `warn` is also a `document.due` warning of `estate check`; `refuse` also refuses the product's next `release submit` before any intent is recorded, naming every such document at once. `clearedBy` says who may clear it: any admitted principal (`any`, the default), one other than whoever revised its content (`independent`), or the operator. One current declaration per document: `docs declare --supersedes <declaration>` revises it.

```sh
vericairn docs living
vericairn docs review docs/guide.md --by <principal id> --outcome current --summary "Read against the widget as built."
```

`docs living` lists each declared document as current, due with every reason, or unavailable. A review's outcome is `current` (the reviewer judges that the current exact bytes hold, whoever revised them), `revised` (the reviewer declares that it revised the content itself, which counts as self review) or `needs-change` (it is stale and stays due). After a builder pins a revision of a document declared `independent`, a separate reviewer clears those exact bytes with `vericairn docs review <path> --by <reviewer id> --outcome current --summary "..."`; the review records `independent-agent` or `operator`. From 0.9.3 (FIX-113), a separate reviewer's `revised` on such a document is refused with `document.cleared-by`, which names that outcome as the cause and points to `current`; the [living-documents guide](living-documents.md#what-each-outcome-declares) gives the example. The runtime, not the reviewer, states the content's hash, the covered paths' identity, the triggers the review answers and the reviewer's independence, and runs the declared checks: `names-version` (the document names the version of each release the review answers), `links-resolve` (every relative link resolves within the repository) and `matches-generator` (generated content equals a fresh generation). A failed check refuses the clearance. From 0.9.3 (FIX-122), the refusal names what each failed check found, and its `document.check-detail` child findings name, for `links-resolve`, every link that does not resolve at the document's path with its line and column and the reason ([living documents](living-documents.md#when-a-declared-check-fails)). For a file of the estate's own repository, a new `document-revision` record pinning it also counts as a clearance when its author meets the declaration's `clearedBy` and the declared checks pass; a pin never clears a document declared `independent`, and `covers` is cleared only by a review.

A generated region is the content between `<!-- vericairn:generated <id> -->` and `<!-- vericairn:end <id> -->`, each on its own line, declared in `regions` with its generator. `estate check` regenerates every region of the estate's own files whose generator is installed and refuses a hand edit or lost markers (`document.generated-edited`); `vericairn docs regenerate <path>` rewrites them and nothing outside the markers. The foundation's generator `vericairn.records/releases` tabulates the published releases of the products its inputs name, newest first. `vericairn.records/progress` embeds the progress report without repository checks, so regenerating a document does not recursively read itself.

A document in another repository, such as a product's README, is declared with that repository's resource in `repository` and the software pack's source `vericairn.software/git-file`. Register this machine's clone of the repository with `vericairn sources clone <path> --resource <resource id>`; the path stays machine configuration and never enters a record. `covers` and the document's own bytes are compared at that clone's HEAD, or at the latest clearing review's commit when HEAD is its ancestor. An unavailable comparison is shown as due with its cause. A review of such a document names the `--commit` it read. Because external commits do not identify the content author in estate records, declare `--independence self`, `independent-agent` or `operator` truthfully; a builder defaults to `self`. An estate file's independence is derived from its recorded revisions. For a document declared at `refuse` on a product's releases, the same due state holds `release submit`; the release repository uses the exact commit being built, and other repositories keep their own commit selection. The intent records what was compared. A declaration that names a source, trigger, check or generator no installed pack provides shows as unavailable (`document.unavailable`), and one with a `refuse` trigger closes its product's release gate until the pack is installed or the declaration is revised.

The software pack supplies exact retained reference and companion checks and reference-coverage policy evaluation. It has no semantic third-party-notice or README check and no automatic declaration proposals. Generated ownership can be retired through the existing checked retirement path. Workbench's readable views show renditions and exact sources; the authored Documents page still does not put its content beside its triggers.

FIX-13 judges repository covered paths and cleared document bytes at the exact commit the provider
resolves for the requested release ref. Clone HEAD may differ. An unresolved commit, a missing local
Git object or an unavailable required contribution refuses the applicable gate; fetch the exact commit
into the registered clone and review there. Observation checks the actual build commit again before
recording success or a candidate, because a ref can move after submission. This release-document gate fetches and
checks out nothing automatically. Normal `docs living` uses clone HEAD or the clearing review's commit
when HEAD is its ancestor; estate-file checks read the estate's working tree. Current estate records supply triggers and reviews, not a historical
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

The goal's plan lists work orders, and work orders can join its work by naming the goal in their `inputs`. The plan's `approval` decides when such a work order joins. Under `self-approves-ordinary` it joins at once. From 0.9.3, under `sponsor-approves`, which `goal propose` writes, a work order proposed for the goal joins only once who decides for the goal approves it: the sponsor when a person, an operator when an agent or a service sponsors it. Work orders the plan lists, and those who decides for the goal wrote, join at once. Who decides approves a proposed work order by recording a `planned` (or `active`) status on its current revision:

```sh
echo '{"status":"planned","note":"Approved for the goal."}' | vericairn records new work-status --by <sponsor> --subject work-order:<id>:<hash> --body -
```

Until then the work order is no step of the goal: `goal next` and `goal step` route nothing for it. `goal show` lists it under `Proposed, waiting for the sponsor's approval` with that command, `status` lists it under `Waiting for a goal's sponsor to approve them` (and `--json` in `proposals`), and attention gives the sponsor a decision needed, which Workbench and Remote show; one already accepted or retired waits for no one. A revision of the work order written later by anyone else is proposed afresh. A proposed work order still counts as soon as it is proposed for the goal's independence, so its authors do not evaluate or accept the goal, and for its reserved actions, so a proposed milestone withdraws a delegated acceptance. Who decides may also assign it. Dependencies (`after`, `blockedBy`) determine eligibility. An accepted work order releases `after`; blockers include unanswered asks, unfinished work orders, unresolved operations and decisions with undischarged obligations. `goal show` describes the conditions and evidence, while `goal next` also checks this machine's launchers, keys, routing and budget. Neither starts work.

```sh
vericairn goal evaluate <goal> --by <evaluator> --summary "Checked each condition against the named records."
vericairn goal accept <goal> --by <sponsor> --statement "I accept this goal from its current evaluation."
```

For record predicates the runtime supplies the evaluation's verdict and records read. For evidence conditions the evaluator supplies `--verdict n:met:reason` (or `not-yet` or `impossible`) and `--read n:<record id>`; numbering starts at one, and a met evidence condition must name a record. The evaluator cannot have authored any step. Acceptance needs the current evaluation of the exact goal revision, every condition met, and every record predicate still holding. The accepter is neither the evaluator nor a step's author: the sponsor when a person, an operator when an agent sponsors it, or the sponsor's named delegate holding `accept-work` for an ordinary goal without reserved actions. A delegate never accepts a goal involving a release or milestone.

`goal pause`, `resume` and `stop` record lifecycle changes. The sponsor may pause or stop; an operator may also do so for a goal an agent sponsors. A stop ends the goal as abandoned. A goal ends as impossible only on its evaluator's verdict: the evaluator records an evaluation that judges a condition `impossible` with `goal evaluate`, and then who decides for the goal confirms it with a `goal-status` of `impossible` (`vericairn records new goal-status --by <sponsor> --subject goal:<id>:<hash> --body impossible.json`). From 0.9.3 a status of `impossible` without that verdict in the goal's current evaluation is refused (`goal-status.verdict`), and the refusal names the `goal evaluate` command that records it. An `impossible` status already in the estate keeps its reading; `estate check` reports one without a verdict as information. Pausing or abandoning a goal prevents new runs but does not stop runs already in flight: use `worker stop` for each run that must stop, as section 18 shows. In this build `goal propose --deadline`, recorded as `limits.deadline`, is validated and recorded but not yet enforced, and so is `limits.consecutiveFailures` for a goal that runs no flow. Monitor them and act explicitly. For a run of a flow, `goal next` and `goal step` hold the run for its sponsor when it reaches `limits.consecutiveFailures` ([hand-offs](#hand-offs-reviews-and-failure-routes-092)). Worker thresholds and the silence limit do operate; `limitations-and-platforms.md` describes their implementation limits.

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

An ask is shown as answered when a current decision names any revision of it in `answers`.
This is derived from the records, without writing another ask revision. If a decision is
superseded, its current revision determines which asks it answers; an ask no longer answered
returns to its recorded open or notified state. `ask`, `operator list`, status and progress
attention, and the API, SDK and MCP views all use that derived state.

From 0.9.0, every terminal run settlement withdraws the run's still-waiting
process attention warnings in the settlement's batch, including settlement recovered from a
retained observation. On opening an existing estate, the runtime also reconciles warnings left
by earlier terminal settlements in one journaled batch. Each withdrawal keeps the original
asker's authorship and question, names the recorded settlement, and is written only once;
opening the estate again adds no duplicate withdrawals. Warnings for unfinished runs, ordinary
decision asks, settlement review asks, and asks a person has answered remain unchanged. Missing
signing authority or an unfinished ask batch leaves cleanup pending for reconciliation. The
worker's observations remain available for review after their process warnings are withdrawn.

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

`claude-code` and `codex` configure the harness's native goal mode and the estate's stdio MCP server. Generic dispatch is refused because it has no complete environment inventory. Install and authenticate a native harness separately. The compiled baselines are codex-cli `0.159.0-alpha.12.1` (from 0.9.0 also `0.163.0-alpha.10.2`, bundled with the desktop app, and `0.163.0`; FIX-81) and Claude Code `2.1.296`; no adjacent version or other alpha build is implicitly qualified. A desktop app update that moves its bundled Codex to another version stops Codex workers that run it, and the refusal names the version or each feature that differs; pin an installation as the [software guide](software.md#desktop-app-updates-and-a-pinned-codex-fix-81) describes. Codex must expose its goals feature and the complete supported feature inventory. Settings listed by `worker launchers` include local command overrides and `stall-seconds`. Inventory probes and stand-in tests do not establish live inference, sign-in or provider billing.

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

Run choices resolve through the local launcher configuration, roster defaults and role policy, goal defaults and role policy, assignment, then explicit run choices. Restrictions accumulate; later choices cannot relax a model/effort requirement, exact revision, destination, provider, data boundary or currency bound. `worker run` and `goal step` accept `--configuration`, repeated `--execution name=value`, `--input-tokens`, `--output-tokens`, `--context-bytes` and an optional `--context-package` identity assertion. Missing choices, unsupported effort, an unobservable required revision or an inventory mismatch refuse dispatch. There is no ambient model fallback. The intent pins choices, their sources and the admitted environment; observed execution stays separate, with discrepancies and unknowns retained. The [local efficiency adapter](efficiency-adapter.md) is built and disabled by default; selecting it requires operator machine admission of both the adapter and launcher. 0.9.0's release security review covers it.

The worker creates or resumes an independent repository on `work/<work-order key>` unless `--branch` names another branch; `--base` chooses a new branch's base. The measured assignment brief is exported under an ignored `.vericairn` directory, with the complete machine context retained in local run custody. The intent reserves the run's cap before launch; observed usage settles it, and unreported units remain charged at their full cap. An exited harness is not an accepted result. Since 0.6.1, previously recorded settlement facts, including usage, scope and containment, cannot be rewritten. Evidence may be appended and missing facts added only under the record and chronology rules; the ledger retains the first settlement's usage and does not refund it through a later revision. An uncertain run cannot be retried for the same assignment until the original operation identity is reconciled; a new identity is not a bypass.

Write `permittedChanges` as actual relative paths or globs (`*`, `?`, `**`), for example `path:NOTES.md` or `path:docs/**`. FIX-18 refuses a brief with no interpretable pattern before intent. Bare patterns cannot contain whitespace; `path:` admits literal names containing spaces. Prose may accompany a valid pattern but grants no wildcard. Matching is ordinal and case-sensitive on every platform, and `**/` can match zero directories. FIX-10 uses deterministic dynamic programming rather than a regular-expression timeout, and isolates audit checks so an undecidable path check does not discard another check's verdict. The settlement audit compares the physical workspace and the run branch's head and all commits reachable from it but not from the prepared baseline, including merged side branches. Restoring a committed path in the worktree or a later commit, or checking out another head, does not hide it. It reports outside paths and any incomplete audit; missing history is incomplete, not clean. It is not a filesystem sandbox and cannot attribute concurrent changes, see reverted uncommitted changes or inspect commits no longer reachable from the run branch. The inventory and committed-history limits are in `limitations-and-platforms.md`.

Worker spending limits are soft. Wall-time, token and currency thresholds leave shutdown headroom, but a turn or failed shutdown can overrun it. Unknown usage remains charged, and an unobserved or uncontained run retains its reservation. A two-second heartbeat observes the process boundary. Silence on stdout produces an attention ask after `stallSeconds` and a stop request after twice that interval; the default is 600 seconds, with precedence goal, roster, launcher request setting, then default. Stderr and progress claims do not reset it. An ask records attention; it sends no external notification. Section 23 describes the additional command, loop and token-rate observations.

Before unattended launch, the custody guard checks every locally held key of an operator or the goal's sponsor, including revoked devices, for the ability to sign without presence. A separate operating-system user can isolate those keys; Secure Enclave custody with presence can enforce a prompt. The accountable person can instead record a specific expiring exception:

```sh
vericairn goal custody-exception <goal> --by <sponsor or operator for an agent-sponsored goal> \
  --reason "Why this goal may run here unattended." --expires <UTC timestamp>
```

The exception covers this goal revision, appears in each run's intent, and is checked again immediately before launch. It grants no extra spending or publication authority. A revised goal needs a fresh exception.

With `--detach`, a serving daemon supervises the run after the command returns. Otherwise the command waits; Ctrl+C requests a stop. A foreground run must outlive its shell: when no daemon serves the estate, the command's own process supervises the run, and closing its terminal ends that supervision while the harness may run on. For a run that should outlive the shell, start the daemon with `vericairn runtime start` and launch with `--detach`. If a supervising process dies anyway, `worker stop` or `worker settle` settles the run as exited without observation once that process and the harness are gone and nothing was observed for longer than its stall limit: it is charged the usage observed up to its last heartbeat, never more than its cap, its branch is released and its workspace is kept for the next attempt (`software.md` has the rule). Stop or hand over explicitly:

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

### Runs of a flow (0.9.2)

A run is a goal whose plan names a [flow](templates.md#flows-092), the subject it serves, its own configuration and,
optionally, the step it ends at. Its work is one ordinary work order, and each work or review step of the flow is an
assignment of that work order naming the step. Propose and activate a run as any goal; `--flow` reads the flow's exact pin
from the templates this runtime offers, and `--subject` binds each field of the flow's subject by its kind:

```sh
vericairn goal propose --by <sponsor> --title "Release the song" --objective "Produce a song and release it." \
  --done '{"type":"release","subject":"<product id>","fields":{"status":"published"}}' \
  --work-order <work order id> --budget wallMinutes=120 --budget attemptsPerStep=3 \
  --flow example/song@1.0.0 --subject product=<product id> --subject version=1.0.0 --subject tracks=intro,outro \
  --flow-option step.shorts=off --until master
vericairn goal activate <goal> --by <sponsor>
vericairn goal show <goal>
```

`goal show`, the API's goal read, the SDK and the MCP `goal_get` and `goal_progress` tools give the run's position from the
records alone: each step, once or once per instance under `each`, with its state and why, and each item it declares with
its own state: `satisfied`, `missing`, `unproven` (its producing record does not show the declared provenance), `unverified`
(a named verification does not pass at its exact hash), `in-review`, `stale` (it changed after its review) or `off`. A step is
`done`, and closes, when its done predicates hold and every required item, and each optional item its configuration leaves on,
is satisfied; an item that changes after its review reopens it. A step is `stale` when an input it used changed after use, so
it reruns under its recovery once what it waits on is done; `blocked` while an earlier step it waits on is not done,
naming every failing precondition, trap and due time; `in-review` while an item awaits its review, or a result awaits one
before another attempt; `stopped` while a person holds its role or its assignment, it is a reserved act, or a watch passed
its limit; `next` when an agent carries it; and `off` when the run's configuration, its `until` step or an unentered branch
leaves it out. The configuration is the flow's defaults, the estate's, the flow's domain's and the product's
`template-defaults` for the exact flow pin, then the goal's options, the narrowest winning; the position names each value's
source. The same records give the same position on every machine at the same time (due times and watches read the clock);
a machine that cannot resolve the flow shows the goal, says so, and takes none of its steps.

`goal next` routes each ready work or review step by its role through the goal's roles, its roster or the estate's, exactly
as a work order's step is routed, from the assignments and asks of that step alone. `goal step` then takes one step per
command: the first, in the flow's order, whose route acts. An agent's step is an assignment of the run's work order naming
the step and its instance (`step: {key, instance}`), stored with its run's intent after the worker's preflight, on the branch
`work/<work order>-<step>` by default; a person's is an assignment and an ask. The assignment's brief pins each input the
step takes by type, id and hash, lists each item with its provenance, evidence and review, carries the step's role brief and
recovery, and prohibits what the goal's constraints, the reserved effects and the flow's `never` lines prohibit. A result or
an operation may record its own `provenance` (its inputs by hash, provider, tool and settings) where an item requires it. A
person can take over a step an agent holds with `work takeover`, which carries the same step and pins. A step of a role the flow marks `person` never reaches an agent. A review that clears a step
is a `review` record whose subject names the item's exact hash (a result's for a file), by a principal other than its author
and the step's own holders, with the item's independence and of its reviewer role: a principal the role names, or for a
review that names none a principal of the estate's `reviewer` role or an operator, or the contributor of a review assignment
of that item ([hand-offs](#hand-offs-reviews-and-failure-routes-092)). In this build such a review is an operator's or an
agent's. A review step closes only on a review by a principal its role names, through the goal's roles or the roster, or by the
contributor of its own assignment. A verification an item names is a `verification` record of that exact subject that
passed. Evidence is any passing verification of the exact subject by the declared method, whoever recorded it, the item's
producer included; independence is what an item's review requires, not its evidence. A provenance that names a principal
holds only for a principal of this estate.

`goal step` never dispatches a reserved step; it stops, waiting on the operator, and no grant in this build lets an agent
carry it. From 0.9.2 `goal step` records the step's operator request instead (see
[stops and their surfaces](#stops-and-their-surfaces-092)). Nor does it dispatch an `operation`, `decision`, `effect`,
`exchange` or `flow` step: each counts once its done predicates hold, recorded by whoever carries it. In this build an
exchange's export and intake are not executed, an effect has no API or browser route, and nothing takes a run's next steps
automatically; a person carries them. A step with no available principal is reported rather than asked of the sponsor.

### Hand-offs, reviews and failure routes (0.9.2)

Each step hands on exact outputs and the next step pins them. A step's assignment pins each input by the record that carries
it, and its brief names each one as handed on: a file by its name and content hash in the result that carries it, a record by
its id and hash. When an upstream output changes after a step used it, that step is `stale` and reruns from a fresh assignment
that pins the new version; the earlier assignment, attempts and results stay recorded.

An item is handed on only after its review. An item of a work step that declares no review, in a step that declares none,
takes an `independent` review when it feeds a reserved step or leaves the estate, through an `effect` or `exchange` step or a
step whose items do; a configuration of the flow's `review` option never lowers it. A work step's review is a routed step; an
item of another kind of step is reviewed by whoever carries the step, with a `review` record of its exact hash. `goal next` lists
it as `<step> review`, routed to the reviewer role the item names, or to the estate's `reviewer` role. Each candidate must meet
the review's independence against the step's authors, who are the contributors of its assignments and whoever recorded an
attempt or a result under one. An author is never offered a review that is not `self`. A `person` review goes only to a
person, an `other-harness` review only to an agent on a harness none of the authors used, and a person without the operator
role is not offered one, since only an operator records a review of another's work as a person. A review in a role the flow
marks `person` is a person's: an agent the roster names in it is never offered it and its pass never clears the item; its findings
or failure count only when it also judges the step another way, as a holder of the estate's `reviewer` role. `goal step` assigns the
review as an assignment of the run's work order whose `step.review` names the items and the exact subject that carries them.
An agent reviewer gets a run of its own, counted against the review's attempts, and a person an assignment and an ask. The
record type refuses a review assignment to an author, to a principal its independence does not admit, or of a subject that
does not carry the items. A pass by its contributor at that hash hands the items on. Findings send the step back to its author
for another attempt, and a review that fails it takes its failure route. Findings and failures count only from principals who
judge the step: an operator, the reviewer roles of its items and of review steps that review it, and its review assignments'
contributors.

Retries go by outcome, within the goal's `attemptsPerStep`, which counts every agent run of a step's work across all of its
assignments, so that a fresh assignment never starts the count again; a review's runs count apart.

- An uncertain effect stops the step for its sponsor until it is reconciled by reading. That is a result naming
  `unresolvedEffects`, a run of its work left `uncertain`, or, for a step carried by its action, an `operation` its done
  predicates read left `uncertain`. It stops the step even when the step is stale or reopened, so nothing reruns it first. A
  result with unresolved effects closes nothing; revise it without them once each effect is settled. Until then the record type
  refuses any run of the step, on any of its assignments and however it is started (`operation.unreconciled`).
- A run that fails with no result recorded since backs off before the next one. The wait is a minute after the first failed
  run, doubling with each failed run in a row up to an hour, and the step shows `blocked` until then.
- Findings start another attempt by the same holder.
- A failed step, one whose latest result is `blocked` or `abandoned` or whose review fails, takes its failure route. A route to
  an earlier step reopens that step, which reruns from a fresh assignment, and the failed step reruns once it is done again. A
  route to a branch enters the branch, and reopens its steps from the failure even when they ran before, so a branch entered
  again runs again; once they are done the run returns to the branch's `returnTo`. A stop, the route of a step that declares
  none, stops it for the sponsor, and so does a route that cannot move because the step it returns to is off in this run.
- When a step must run again and its work has had every run its attempts per step grant, its retry budget is spent and it
  stops for the sponsor.

Each of these stops is required attention. `goal next` routes it to who decides for the goal, its sponsor, as the route state
`attention`, and `goal step` records one ask about the record that raised it, with a recommendation, and dispatches nothing for
it. Only what depends on the step waits, and the run's other steps go on. While the ask waits the route is `asked`; once a
decision answers it, it is `answered` and is not asked again. The step goes on when its records change, for example when a
person takes it over with `work takeover`, a result is revised, or a revision of the goal grants more attempts and is activated.
Two kinds of attention hold the whole run instead: the goal's `limits.consecutiveFailures` reached by failed steps in a row,
and an evaluation that judges a condition impossible. `goal step` asks the sponsor once and dispatches nothing more until a
decision answers the ask.

`goal next` and `goal step` hold these stops, the backoff, a failed step's route and the run's own attention. A run started
directly with `worker start` is the starter's explicit act and is not held by them; the record type still refuses one beyond the
step's attempts per step or while an effect of its work is unreconciled.

A role may name the estate's own copy of its brief, a file such as `docs/roles/writer.md`, in the flow's `document`. Each
brief for the role then names that file and its content hash and says whether a living document declares it and whether it is
due. The file is read only when it is a plain file of one byte to 1 MiB with no link on its path; a link, a directory, a device,
a FIFO or an empty file is not read, and the brief says so and keeps the flow's own brief. Declared and due at level `refuse`, it holds as `blocked` each step of the role that is still to be briefed, unassigned or
to rerun from a fresh assignment, until a revision or a review clears it; a holder already briefed goes on.

`goal show` and `goal next` show all of this. Each step lists its attempts under every assignment, with their results, and how
many runs of its attempts per step it has used. They also show its reviews before hand-off, its required attention, the
failure its latest work stands on, and whether it reruns from a fresh assignment. A run lists its own required attention.

### Stops and their surfaces (0.9.2)

A run stops for a person only where a person holds a step's role or an item's review, where a step is a reserved act
(`accept-release`, `publish`, `spend`, `use-credential` or `security-review`) with no grant in force within its bounds, where
required attention arises, and where the run is complete and the goal is ready to accept. No grant of a reserved act takes
effect in this build, so every reserved step waits on the operator.

A reserved step waits on the operator as an operator request. When the step is next, `goal next` routes it as `request`, put
to the first person with the operator role that its role names, else the goal's sponsor when the sponsor is one, else the root
operator. The request carries the step's exact command: its declared `action` with each placeholder bound from the records as
they stand then ([templates](templates.md#flows-092)). The command is computed only then. A later reserved step, still
`blocked`, shows none, and nothing stores one ahead. Whoever takes the run's steps runs `goal step`: an operator, the goal's
sponsor when the sponsor is a person, or a principal with the `local-engineering` scope. It records the request and dispatches
nothing: no assignment, ask or run. The request's `flowStep` names the run's goal, the step and its instance. Its subject is the
run's work order, and its one content item is the command. A `publish` step's request is of kind `publication`. The request
grants nothing.

```sh
vericairn goal next <goal>
vericairn goal step <goal> --by <operator>
vericairn operator list
```

A request is delivered on `operator list`, status and required attention only while it is due. It is due while its step waits
on the operator, it carries exactly the command the records give now, a principal who takes the run's steps recorded it, and
no earlier request carries the step. When the records change before the operator acts, the request is withdrawn from delivery.
For example, a new master that an input of the step takes makes the step wait again on its review, or changes the command.
Once the step is next again, `goal next` routes it as `request` again. `goal step` then revises the request to carry the new
command when the same principal recorded it; otherwise a new request carries the step. `operator list` and its JSON's
`withdrawn` name each open request that is not delivered, as `withdrawn` or `settled`, with why. Each stays open until its
operator closes it with `operator done` or `dismiss`.

The step is done only from the records of the operator's own act. Its done predicates and any record item count a record only
when its signatures are authentic and its author was a person with the operator role when it was made; a result counts only
on the same terms. A record's claimed author, an unsigned or altered record, and an act made before its author became an
operator never count, while a past act of an operator whose role was later revoked still does. Under a charter where
signatures are optional, the operator therefore signs a reserved act. A reserved step's done predicates name no `state`, which
other records decide. Closing the request does not complete the step, and neither does an agent's record of the act nor an
agent's close citing the operator's words; the step's reasons name each record that does not count. Once the operator's act
is recorded, the request is `settled` and no longer delivered. A request the addressed operator closed with their own signed
close, while its command still holds, is not made again (route `closed`), and the step waits for the act's record. Any other
close, an agent's among them, leaves the step's request due again. A request recorded by a principal who does not take the
run's steps is never delivered. A reserved step whose input changed after it was done waits on the operator again, with the
command the records give now. A step request's recommendation and other prose are the requester's own: `operator list`,
status and attention show the checked command first and label that prose as not checked.

Every surface shows a run's stops the same way, from the records:

- status: its text, read now, and the canonical JSON's `runs` when there are runs, each with its goal, flow, steps done,
  next steps and `stops` as of the newest record (`asOf`), so the canonical document never depends on when it is read;
- `goal show` and `goal next`: the position's `stops`, and for the reserved step that is next its `command`;
- the API's and the SDK's goal, next and status reads, and the MCP `goal_get` and `estate_status` tools;
- Workbench's home, from canonical status with its as-of time, and its Goals workspace, read now;
- Remote's Attention, from canonical status with its as-of time.

Each stop names the step, or `<step> review` for an item's review, `the run` for the run's own attention and `the goal` for its
acceptance. It says why it waits (`person`, `reserved`, `attention` or `accept`), who it waits on, how it reaches them, and for
a reserved step that is next its exact command. Only a live read shows that command: human status, `goal show`, `goal next`,
the Goals workspace, `operator list` and attention, which read the real clock. Canonical status and a hand-over package, which
trust each record's claimed time, show the stop without it. A stop reaches its person in one of these ways:

- `due`: the next `goal step` delivers it, as an assignment and an ask, an ask, or an operator request;
- `delivered`: its assignment, ask or request waits on the person;
- `closed`: its ask was answered or its request closed, and it is not asked again while that holds;
- `queued`: three stops of the estate's runs already reach that person;
- `outside`: `goal step` does not carry it, so nothing is recorded to reach the person in this build. That is an effect or an
  exchange a person carries, a watch past its limit, a step no one is named for, or the goal's acceptance.

At most three stops reach one person at a time across every run. Those already delivered count first; then each due stop in
the flows' order, the earliest position in its flow first and, at the same position, the older run first. `goal next` routes a
fourth as `queued`, and `goal step` dispatches nothing for it until one of the three settles: the person answers or carries it,
or its request is settled or withdrawn. Remote shows the stops as canonical status gives them. It answers asks as before, and
it carries no reserved act out; neither Workbench nor Remote offers an action on a stop.

### The software release and upgrade flows (0.9.2)

From 0.9.2 (PROC-07) the software pack ships two flows as built-in template versions, so a release and an estate's upgrade run
on declared steps rather than on per-release scripts: `vericairn.software/release` 1.0.0, a product's release from its placed
work to its closed carrier, and `vericairn.software/upgrade` 1.0.0, an estate's upgrade to a published Vericairn release.
`templates list` shows both. A run of either is a goal as [runs of a flow](#runs-of-a-flow-092) describes; each step's state
comes from the records alone, each reserved step waits on the operator as [stops](#stops-and-their-surfaces-092) describe, and
each step names its traps: the checks of earlier releases' lessons, read from the records where a record says it, or named as
the check the step's action runs before it acts. `goal show` lists each step's traps, with `check` for one its action runs.

The release flow's subject binds the carrier by its original identity, the identity placements name, the product, the
version and the repository its release is published from:

```sh
vericairn goal propose --by <sponsor> --title "Release 1.2.0" --objective "Release the product from its placed work to its closed carrier." \
  --done '{"type":"acceptance","subject":"<carrier id>"}' --work-order <carrier id> --budget wallMinutes=2400 --budget attemptsPerStep=3 \
  --flow vericairn.software/release@1.0.0 --subject carrier=<carrier id> --subject product=<product id> \
  --subject version=1.2.0 --subject repository=owner/name
```

| Step | Owner | Done from the records | Traps |
| --- | --- | --- | --- |
| `placed-work` | release manager | Every work order placed in the carrier accepted, none planned, active, paused, submitted or revised since acceptance; from 0.9.3 the carrier itself is never among its own placed work | |
| `docs-before-freeze` | release manager | `docs pass --before --record` passed both checks for this version | `prior-version-tokens`, `preparation-wording` |
| `docs-set` | release manager, work | Its result hands on the set's manifest and notes by hash | `guides-revised`, `notes-window`, `security-section` |
| `release-commit` | release manager, work | Its result hands on the frozen index at the merge commit | |
| `reviews-at-commit` | release manager | The submission's intent is recorded | `documents-cleared` |
| `gate` | release manager | The submission's intent is recorded | `client-build`, `default-head`, `document-hold` (checked by `release submit --check`), `withdrawn-draft` |
| `submit` | submitter | The submission dispatched, neither not started, uncertain, failed nor canceled | `uncertain-submission` |
| `candidate` | release manager | A release record of the version exists | |
| `review` | reviewer | A passing review of the exact candidate | `not-withdrawn` |
| `accept` | decider, reserved `accept-release` | The operator's acceptance of the candidate | `reviewed`, `not-withdrawn` |
| `publish` | publisher, reserved `publish` | The operator's observation of the publication: the published revision | `accepted`, `stable-channel`, `not-withdrawn` |
| `observe` | release manager | The release is published; package destinations observed with `packages observe` | |
| `upgrade-estate` | release manager, optional, off by default | The estate's observation of the published version, from a run of the upgrade flow | |
| `docs-after` | release manager | No living document of the product due, after `docs pass <version>` | |
| `close` | decider | The carrier's acceptance | `placed-work-accepted`, `nothing-due` |

The traps carry the lessons of 0.8.1 to 0.8.4. A guide still naming the release before last, as 0.8.4's notes-window
sentence named 0.8.2, stops `docs-before-freeze` with its location. A set frozen before that pass, as 0.8.4's first one was,
cannot be made: `docs-set` waits on it and takes the check as its input, so a newer check makes the set stale. Notes rendered
before the carrier's work closed, as 0.8.3's were, and an ask about the carrier, such as one approving a security section, that
still waits, open or notified, hold the set. A withdrawn ask no longer holds it. The flow cannot prove an approval: whether a
release needs a security section, what the deciding decision says and why an ask was withdrawn, the records do not tell, so the
release manager asks whenever a fix is under a disclosure hold. A document changed after its clearing review, as in 0.8.2 and 0.8.3, stops `reviews-at-commit`. A
withdrawn candidate's draft left at the provider stops `gate`, as do the objections `release submit --check` names, such as a
client of another build than the serving runtime (0.8.4's stale daemon) or a clone without a default head (0.8.3). An uncertain
submission stops `submit` until a reconciliation reads the release record. The publication command is computed only once the
operator's acceptance names the candidate, never early, as it was for 0.8.3; a candidate of another channel than stable never
shows this command. The publication is the candidate's own revision, so the steps that used the candidate stay done. A check of
the guides counts only when it names the earlier releases the records give, so one made with `--previous` never completes the step.

Accepting and publishing stay the operator's. `accept` shows
`vericairn records new acceptance --by <operator-id> --subject release:<candidate id>:<hash> --body accept-release.json`, and
`publish` shows `gh release edit v<version> -R <owner/name> --draft=false --latest && vericairn release observe <version> --by <operator-id>`,
each only when it is next. Only records an operator signed count for them.

The `withdraw` branch is entered by the candidate's withdrawal (`release withdraw`, which records the draft's deletion intent).
`delete-draft` is reserved `publish`: its request carries `gh release delete v<version> -R <owner/name> --yes`, and it is done once
the operator closes that request with `vericairn operator done <request> --by <operator> --link <url>`; an agent's close citing the
operator's words completes nothing, and `goal step` asks the operator again. `observe-withdrawal` then records the dispatch under
the intent and observes the draft's absence with `release observe --withdrawal`. A corrective candidate is a new version: the
sponsor revises the goal's `version` (and its carrier, after `work move-plan`), and every step the version keys derives afresh,
starting with `docs-before-freeze`, since the corrective version's guides must name it, and then the set, which is stale.

The upgrade flow's subject is the version the estate upgrades to:

| Step | Done from the records | Traps |
| --- | --- | --- |
| `check` | The upgrade it checked is recorded: `estate upgrade --to <version> --check` records nothing | |
| `upgrade` | The estate's observation of the version, from `estate upgrade --to <version>` | |
| `runtime` | `estate verify` found the pinned release's runtime serving the estate here | `older-runtime` |
| `verify` | `estate verify` found the estate check and status passing with it | `estate-check` |
| `adopt` | An adoption history checkpoint that counts, signed once by the root operator or a person holding `administer` with `--expect-plan` naming the plan its check printed | |
| `docs` | No living document due | |

An upgrade with a runtime of an older release still serving the estate stops at `runtime` until it is replaced: the upgrade's own
runtime step (OPS-05) replaces it where it can, and `estate verify --by <you>` records what serves the estate on this machine,
naming the machine by the signing device it holds, so a failed check on any machine holds the step until that machine verifies
again. `adopt` waits on the person the roster names in `root-operator`, once, in an estate with no adoption checkpoint. Its
action points to the "Adopt history checkpoints" item of `operator list` rather than repeating it: that item carries the check,
`vericairn estate checkpoint --adopt --check`, and the signing command with the plan hash the check prints, refused if what it
anchors changed since that check ([history checkpoints](#history-checkpoints-092)). An agent's checkpoint that claims adoption
adopts nothing (`history.adoption-author`) and leaves the step waiting; an estate that adopted, or was born with an adoption
checkpoint, has it done. `docs` does not wait on it.

An estate extends either flow with steps of its own, in a flow under its `docs/templates` that names the shipped version in
`extends` and places each step `following` a named step ([templates](templates.md#the-software-packs-flows-092)). An extension
never turns off a required step, a reserved marking or a step an estate policy requires of the flow or of any flow it extends.
Vericairn's own estate keeps its first-party extension there, `vericairn.first-party/release`: it adds the NuGet packages'
observation after `observe`, the announcement's drafts with their independent review, the MCP Registry listing and the
announcement on GitHub Discussions and on the operator's X account as reserved `publish` steps after `publish`, each done only
once the operator closes its request with the observed link, and the profile repository's documents after `docs-after`. A close
counts only for the step's current ask: a request a principal who takes the run's steps put to the operator, with the command the
records give the step now, closed after the step was asked and after the goal revision that last changed the run's flow pin,
subject or options, so a corrective version's listing and announcement are asked again, while a revision that only grants more
budget asks nothing again. Its
destinations are that estate's own, so it is never part of the software pack.

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

From 0.9.2 a request to the local-network listener, or to loopback, cannot make the runtime reload the estate before its signature is checked; the runtime reads a device's standing from a model it keeps for the records as they are, and revoking a device still ends its access from the next request.

The saved connection is used by the SDK (`RuntimeConnections.Connect`); the CLI's ordinary estate commands and Workbench have no saved-connection selector. A paired device acts for its own principal. The runtime signs its requested records with that principal's locally held admitted key; absent such a key, optional-signature policy may allow unsigned records, while required signatures refuse them. A key requiring presence is not used to answer a paired device's write. `vericairn devices writes [<device>]` lists the local audit of intents and outcomes. Local-machine actions such as launching a worker, issuing a pairing code, submitting or observing releases through the provider, exporting files, rotating runtime certificates or creating local device keys are refused over this profile; the API reference and `api-v1.md` enumerate the routes.

`POST /v1/harness-qualifications` is also machine-local. A paired local-network device receives `401` / `denied` before a probe or record write, even when acting for an authorized human or supplying a local command override. Use the runtime's local socket or signed loopback with the existing human qualification authority. Worker configuration is a local CLI operation with no HTTP or MCP write route.

Pairing is not crash-atomic. Inspect the server's device list after a failed or interrupted pairing. Validation refusals roll back the new device, but storage is sequential: a crash between the device and admission can leave an unadmitted device. A lost final reply can leave both records on the server while the client discards its key. Revoke an unwanted admission before pairing again. Re-pairing can replace saved connections; it does not revoke their old devices.

```sh
vericairn devices revoke <paired device> --by <administrator> --reason "This device is no longer authorized."
vericairn runtime rotate --compromised
vericairn connections remove <connection id>
```

Revocation is checked on the next request, including on an existing connection, and again under the write lock before an authenticated write. Rotation of a device key or withdrawal of its local-network profile also prevents a request authenticated under the old state from writing. Disconnecting or removing a saved connection revokes nothing and keeps the key in local custody. `runtime rotate` replaces the TLS certificate without changing the identity pin; `--compromised` also closes open connections and pending handshakes. If the runtime identity key may have leaked, `runtime rotate --identity` replaces that identity and every client must pair again. These commands are run on the runtime's machine. [Remote](remote.md), the phone companion, connects through this profile only; the relay that would reach a runtime from elsewhere is a design.

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

### Searching for context (0.9.2)

A package follows the work's own records and the documents they name. Search adds a recorded second way in (CONTEXT-02):
a machine-local keyword index finds candidates, and a package carries those it selects as optional, pinned sources, saying
how they were found.

```sh
vericairn search "earlier decision on calibration"
vericairn context prepare W-1 --search "earlier decision on calibration"
vericairn search index
vericairn search replay --record <agent-run operation id>
```

**What is indexed.** Every record is one passage, with its type, id and key kept whole and every string field of its body;
superseded revisions are searched only with `history`. Each current pin of an estate document is split at its headings, and
each table row is its own passage, so each catalog entry is found alone. Whole generated files, earlier revisions and unpinned
changes are not indexed. A knowledge draft under `docs/knowledge/` adds an entry passage of its front matter, without its
sources, drafting principal or status, and every passage of it belongs to the audience its front matter names.

**Custody.** The index is `search.db` beside the live store under the Vericairn home, created owner-only before SQLite opens it;
a file others may read is replaced. It is never committed, exported, shared or served, and deleting it loses nothing: the next
search rebuilds it. A read-only runtime keeps its index in memory. Every estate on a machine has its own index, and a
repository-synced estate one per clone.

**Freshness and determinism.** Before answering, the runtime compares each segment's basis (the record-set hash over its
records' `{id, hash}` and the document-set hash over its indexed `{path, revision, byteHash}`) with the current records and pins,
and updates incrementally. `--allow-stale` answers within the query's budget from what is indexed, labelled stale; context
preparation always updates first, or refuses with `search-index-stale`. Ranking is BM25 with fixed parameters and field weights,
exact id, key or path matches first, scores rounded to fixed precision and ties ordered by source identity, so ranks repeat on
every platform. The analyzer's Unicode normalization (NFKC), case folding and character classes come from Unicode 16.0.0
tables built into Vericairn, never from the operating system, so every platform and the shipped build analyze text alike. Each
candidate is reread and checked against what the index held: a mismatch drops it as an `index-fault`, and
pinned bytes absent from this copy make it `excluded`, never read from another revision.

**Permission.** Each audience's passages (Internal, Members only, Public, and any other restriction a draft names) form their own
segment with its own ranking statistics, and a search ranks, counts and excerpts only the segments its reader may read, so a
restricted passage changes no readable score, rank, count or error, and restricted and nonexistent sources answer alike. Today
every admitted reader reads the whole estate, so every reader searches every segment; the segments are built for Knowledge,
read-only participation and network access. A reader that cannot read every segment is told only that completeness cannot be
confirmed, and `search index` gives such a reader no total index size, source bytes or update counts. A record keeps only
candidates whose audience every reader of the estate's records may read, Internal or Public, and says, without a count, when it
omits any, ranking the rest again without gaps; the audiences searched and the index segments it names are only those, with
one opaque `Restricted` entry, carrying no label or hash, for every other segment, and its index revision covers only what it
names. One count remains: when `more` is true, the selection's limit minus the candidates kept is the number omitted.

**Selection, not authority.** A package's searches follow its explicit selection and never reorder it. Their candidates are
optional sources with the dependency `candidate from search s1; optional; grants no authority`, inlined within the allocation
or withheld with identity, size and retrieval command. A decision binds through its recorded subject and effects; a candidate
that seems to govern the work from outside its explicit selection is raised as an ask or an amendment, not obeyed. Candidate
text is untrusted content. Search may return a record the estate does not count, for example an unanchored or forged one; a
candidate's standing is judged by reading it, never by search. A package's searches never select such a record: it is
recorded as `excluded`, with its reason, as a pin whose bytes this copy lacks is.

**What a run records.** A run's searches (`worker run --search`, `goal step --search`) are fixed in its intent as
`execution.contextSelections`, and the searches its agent made through the run's MCP server are recorded at settlement as
`searchesObserved`, at most 100 in full with a digest of all, from record format 3. That list is what the run's MCP server
logged, not tamper-evident evidence: under a harness that does not sandbox the agent, the agent runs as the same user and could
change the log, and the run does not record whether its harness was sandboxed. A missing log or an unreadable line makes the
list incomplete; a run that searched nothing records no list. A reviewer replays them from the record: `search replay` reruns
a selection only on exactly its basis (this copy's index, this copy's records up to the selection's time, or the estate commit
it equalled, read from local Git history) and otherwise says so, at most 20 selections per request. A recorded selection's
restricted segments are searched again as they are now, by a reader of every segment, and the replay says their basis was not
confirmed. A full `search index --rebuild` runs from this machine's own command line or for an operator; a paired device can
bring the index up to date but not rebuild it.

**Measures.** `vericairn search measure <questions>` runs a question set and reports recall at 5 and 10, mean reciprocal rank,
the index's size against its source bytes and query times, against the budgets of the search design: recall at 10 of at least
0.8, an index at most half its source bytes, a full build within 30 seconds, an update after a record write within 100
milliseconds and 95 percent of queries within 250 milliseconds. Each release's `verification` record reports them. A
question set kept in the estate as a pinned document is skipped as a candidate for its own questions.

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

`communication-adoption.md` is the first-party operator's explicit adoption procedure, not an automatic migration or a default for other people. It preserves the existing decision's identity. `communication-qualification.md` records fixtures, platform limits and deferred work; neither document qualifies Remote or live efficiency-adapter behavior. The adapter is built but disabled pending its own security review and operator admission.

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

After installing an upgrade, restart a serving daemon as well as running workers. Follow the stop/start
commands and coordination steps in section 9 before using the upgraded CLI against that estate. From
0.9.2 `estate upgrade` itself replaces a daemon of an older release than the new pin, or says exactly
what to run when it cannot.

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

In 0.9.0, the manifest's optional `vericairn.line` and `vericairn.channel` select the followed stream;
omitted fields mean `current` and `stable`, with no rewrite of existing manifests.
`estate init --line <name> --channel <channel>` selects them for a new estate.

`vericairn estate check` separately warns with `manifest.release-behind` when an estate is pinned below
the newest published release it knows in that line, at its channel or more stable. It reads the estate's release records for the manifest's
repository and, for `Vericairn/vericairn`, the local release cache. From 0.8.3, it also
reads verified `vericairn-release` observations and warns for a source pin when the running build's
version is older than a known published release; it does not infer an ordering of source commits.
The check makes no network request. Unrelated products and candidate releases do not count.
`stable` and `lts` have equal stability; `prerelease` follows them, then `nightly`.
A stable or LTS estate never receives a prerelease or nightly target, and an estate on `1.x`
is never offered a newer release from `current`. An equal or newer version, or no known release, produces no warning. The finding is always a
warning, so it does not fail the check.

The shared latest-release cache and verified dependency observations describe `current/stable`.
The CLI and Workbench use the selected estate's line when deciding whether to show that cached
notice. Before an estate is opened, Workbench shows the ordinary installed-client notice.
For another line, the estate's published release records supply the available versions.

When the followed LTS line's `endOfSupport` has passed, `manifest.line-unsupported` warns even
without a newer release. It names the lines still supported, using matching publications and
the product's current line declarations; unrelated repositories cannot supply those names.
The support timestamp itself remains supported. The warning says when no supported lines
are known, and neither warning changes the estate's pin or followed line.

Use the target release's own command line to review
`vericairn estate upgrade --to <published-version> --check`, then apply it with
`vericairn estate upgrade --to <published-version> --by <operator-id>`. When the estate has no matching
Vericairn product release record, the command verifies the published release metadata and tag, checks
the platform archive against `SHA256SUMS`, then records a dependency observation and pins its hash.
Add `--from <directory>` to verify downloaded release evidence offline. The existing upgrade checks,
authority rules and record-format migrations still apply. A cached network answer grants no upgrade authority.

To change lines, explicitly add `--line <name> --to <version>` to both the preview and apply
commands. Add `--channel <channel>` to change the followed stability, including opting into
prereleases. Omitted options preserve the manifest's choices. The plan shows the resulting
line and channel; the migration records an explicit change. Every estate, including one
using a fork's repository and release records, follows these rules.


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
worker signals can remain continuity concerns after the operation settles. From 0.9.0 an acknowledgement is
per-principal presentation state that clears when the notice changes; there is no dismissal or expiry state. [Required attention](required-attention.md) states the exact rules.

Canonical status JSON derives attention from records alone and is identical on machines with the same
records. Progress also reads this runtime's worker observations; human status, progress and Workbench
label additional concerns `Local worker observations`. From 0.9.2, the operator's attention in human status,
Workbench and Remote also shows this machine's security-report observations, which canonical status leaves out
([security reports](#security-reports-hold-a-release-092)). No reader can infer another machine's unrecorded
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
From 0.9.3 the action announces every record it signs with a device that needs
presence before the first prompt, 22 for a new action, and each observation it
repeats while checks run before that observation asks
([presence prompts](#presence-prompts-announced-and-counted-093)).
[Operator actions](operator-actions.md) explains recovery with the same
body and identity, presence, authority and the action's push and merge trail.

From 0.9.2, an action that stopped after its merge, for example while main's
check was still running, is finished from its own records. Once the merge is
recorded, resuming observes only the target run at the merge commit; it never
recreates the deleted branch, signs or commits again, and leaves the clone on
whatever branch it is on, usually main. `operator unfinished` shows the clone's
unfinished action: its record id, type and subject, branch, head, pull request,
stage, and what finishing it needs. `operator unfinished --finish --by <operator>`
finishes it once the target checks pass. Starting the next `operator record`
does the same first, then proceeds. An action that has not merged still refuses
a new one, and the refusal names its record id, type and subject.

From 0.9.3 an action's push and merge trail reaches the estate. The trail is
the operation records of the action's push, opening and merge, with their
observations and any target-failure ask; it is written after the action's
commit, so it stays untracked in the clone that ran it. Once the action is
complete, the next `operator record` in that clone carries every trail record
still untracked there into its own commit, beside its signed record and
members, and the commit check requires exactly those files and bytes. The
earlier action's signed record is not changed, and nothing else untracked
travels: other changes still refuse the action. Until a commit holds a trail,
`status` and `estate check` report the action with the warning
`operator.trail-uncommitted`, naming its type, record id and subject and how
many trail records wait
([operator actions](operator-actions.md#the-push-and-merge-trail-reaches-the-estate-093)).

`changes push`, `open`, `merge` and `observe` require repository-write authority.
The push scans the exact complete commit range using installed Gitleaks 8.25.0 or
later and Vericairn's own rules, which keep none of gitleaks' built-in allowlists;
missing history, scanner or a complete report refuses it. Merges require a recorded
scanned push of the pinned head, and every declared check and every reported check
to pass at that head; missing, skipped,
pending or stale checks refuse. The provider performs a synchronous merge with
an atomic head condition, then observes target checks at that merge commit.
Pending or failed target checks remain incomplete even after a confirmed merge.
Fork pull requests are unsupported. From 0.9.3, where the target branch requires
a merge queue, the merge enqueues the pinned head under the same authority and
checks, records the queued merge, and `changes observe` records the queue's
outcome against it: merged at a commit, whose `merge_group` run and push runs,
or those of the group that landed it, are then observed; not merged, a known
failure, only on the host's evidence about this attempt; anything else stays
uncertain. `operator record` waits in the queue as it waits for checks
([required merge queues](changes.md#required-merge-queues-093)). No retry can bypass an
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

## 30. Durable operator requests

From 0.8.4, the request commands keep the actions a builder or agent asks the operator
to perform. `vericairn operator list`, status and Workbench attention deliver the
oldest three open requests with their recommendation first and identify the rest
in order. `operator done <request-revision> --by <operator-id>` records completion;
`operator dismiss <request-revision> --by <operator-id> --reason "No longer needed."`
records dismissal. Each is one signed step, with the original request retained.

The [operator request guide](operator-requests.md) explains exact draft hashes,
publication intent and observed destinations, cited operator words for an agent's
close, observations that close a request, and carrying existing open asks over.
Request closure does not accept work or answer a decision question. From 0.9.2 a
run's reserved step waits on the operator as a request that `goal step` records;
it is delivered only while it carries the command the records give now (see
[stops and their surfaces](#stops-and-their-surfaces-092)).

## 31. Asking for help: the support bundle

When you ask for help, whether from a support service, an agent or a public issue, `vericairn support bundle`
gathers what support needs and prints all of it:

```sh
vericairn support bundle
vericairn support bundle --out bundle.json
```

It holds the Vericairn version and build, the platform, which runtime served it (daemon or embedded), the
estate's pin, record count, status counts by state (work orders, assignments, releases, operations, runs), the
estate check's counts with each finding's severity, code and location, the runtime log's recent error lines,
and whether the local-network profile and each native launcher are configured.

It leaves out every record body, title and text, the estate's and principals' names, network addresses, and
each finding's message, which may quote a record. It then redacts every string by the shapes the estate check
refuses: credentials in the formats it knows, values under secret-named keys, and absolute machine paths.
Your home, temporary, working and Vericairn directories are named by role instead. The `redacted` section
counts what was removed.

`--out` writes the same bytes as shown to a new file, and never replaces an existing one. Nothing is sent
anywhere: sharing the bundle is always your own act.
