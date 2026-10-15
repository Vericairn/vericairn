# Record format 1

A Vericairn estate is a body of records. A record is one immutable fact: a charter, a principal, a decision,
a work order, a status change, a review, an acceptance. Records are never edited; a later record supersedes an
earlier one. Everything else (status, who may do what, what is accepted) is derived from the records.

Every record written in format 1 keeps format 1's rules, in whatever estate it lives, with one exception: once the
estate is at record format 2, an admission recorded without a signature counts only when the signed move to format 2
lists it (see Signatures, devices and admission). Records written after an estate moves to record format 2 follow
[record-format-v2.md](record-format-v2.md), which describes only what differs.

## Envelope

Every record is a JSON object with these keys and no others:

| Key | Required | Meaning |
|---|---|---|
| `format` | yes | `vericairn.record/1` |
| `type` | yes | kebab-case record type |
| `id` | yes | ULID, 26 upper-case Crockford base32 characters |
| `at` | yes | authoring time, `YYYY-MM-DDTHH:MM:SSZ` (UTC, whole seconds) |
| `by` | yes | ULID of the authoring principal |
| `subject` | some types | `{type, id, hash?}`: the record this one is about; `hash` pins the exact revision |
| `supersedes` | optional | ULID of the record of the same type this one replaces |
| `body` | yes | type-specific object |
| `hash` | yes | `sha256:` + 64 lower-case hex digits over the canonical envelope without `hash` and `signatures` |
| `signatures` | optional | `[{algorithm, device, signature}]`: signatures over the content hash by admitted devices; verified on load |

## Canonical form

Hashing uses the compact RFC 8785 form: object members sorted by UTF-16 code units, strings escaped only as RFC 8785
requires (`\" \\ \b \f \n \r \t`, other control characters as `\u00xx`), no whitespace. Numbers are restricted to plain
safe integers (|n| ≤ 2^53 − 1); fractions and exponents are rejected, so money is written in minor units and durations
as integers in the unit their field names (a verification's `durationMs` is in milliseconds).

Files use the readable canonical form: the same ordering and escaping, two-space indentation, LF line endings,
one trailing newline. A file whose bytes differ from that form is invalid; `vericairn records format` restores it
without changing content or hash.

## Where records live

`records/<type>/<yyyy-MM of at>/<id>.json`. Because file names are ULIDs, two people on two machines never
write the same file; merges are always clean file additions, and conflicts are semantic, surfaced as findings.

## Types

Run `vericairn records describe` for the current list and `vericairn records describe <type>` for fields. Format 1
admits twenty-four types. Since release 0.1.0: estate, principal, authority, decision, product, resource,
document-revision, work-order, work-status, review, acceptance, retirement, assignment, attempt, result, amendment,
device, admission, reconciliation, release and migration. Since 0.2.0: verification, observation and operation. Reserved
for later releases: object. A release can add a type or an optional field without changing the format number; an older
release refuses what it does not know, so an estate records the new kind only after upgrading to the release that reads
it (the product record's optional `release` object, the destination and the artifacts a complete release carries, came
with 0.4.0; 0.4.0 and 0.4.1 were withdrawn before publication, so 0.4.2 is the first release an estate can upgrade to
that reads it; `software.md` describes it). This list was corrected for 0.3.1: the 0.2.0 and 0.3.0 copies still list
release and migration as reserved.

## Work: brief, assignment, attempt, result

A work order is the unit of work. An **assignment** delegates it to a contributor under a brief (outcome,
sources, permitted changes, prohibited effects, workspace, limits, expected outputs, review criteria,
recovery) and pins the exact work-order revision. An **attempt** is one contribution, authored by the
contributor and pinned to the exact assignment. A **result** is what an attempt submits (outcome, summary,
outputs by hash, evidence, checks, limitations, unresolved effects, next step), pinned to the exact attempt;
it is a claim to inspect. An **amendment** asks to change the brief and authorizes nothing. Acceptance of a
result pins the result's hash and follows the work order's policy: results toward milestones need the
operator; results of ordinary work may be self-accepted under `self-accept-ordinary-work`.
`vericairn work brief <key>` renders everything a contributor needs from the records alone;
`vericairn work observe <key>` shows what happened.

## Signatures, devices and admission

A **device** record binds a public key to a principal with its platform, custody strength (`windows-cng`,
`macos-keychain`, `linux-secret-service` or `file-owner-only`) and permitted connectivity profiles; it is signed by its
own key to prove possession. An **admission** record, authored by the operator or under the `administer` scope and
pinned to the exact device record, admits the device to author records; a superseding admission with status `revoked`
and `revokedAt` ends it. The root operator's admission of a device that signed that admission itself is the estate's
first authority and is accepted once in its history, for its first device; every later device is admitted under the
signature of a device already admitted (release 0.4.2 and earlier accepted any such self-admission). In an estate at
record format 1 an admission recorded without a signature counts while the charter leaves signatures optional, and a
required-signature policy binds only records dated after the current charter revision, a date being what a record's
author wrote; so at format 1 anyone able to add records can still admit a device by an unsigned admission, and `estate
check` does not tell. From the move to record format 2 an unsigned admission counts only when the signed migration lists
it ([record-format-v2.md](record-format-v2.md#moving-an-estate-to-format-2)). A signature is `ecdsa-p256-sha256` over
the 32 digest bytes of the record's `hash`, encoded as a 64-byte base64url value in IEEE P1363 form. On load, every
signature is verified against its device's public key; a signature by a device of another principal, by an unadmitted
device, before its admission, or from its revocation onward is an error. The estate's `policies.signatures` is
`optional` or `required`; when required, every record dated after the current charter revision must be signed (a later
revision of the charter moves that point), so a clone without an admitted device key is read-only. Private keys are held
by the device's custody on its machine, never in a repository: the Windows key storage provider (`windows-cng`), the
Linux secret service (`linux-secret-service`), or a file in the per-user state directory (`file-owner-only`;
`VERICAIRN_HOME` overrides the directory); the CLI picks the signing device for the author automatically. From record
format 2 a device keeps its identity across renames and key rotations
([record-format-v2.md](record-format-v2.md#a-device-keeps-its-identity)).

## Where records are kept: profiles, the live store, objects

The manifest's `profile` is `repository-synced` (the default: record files in the git repository are canonical and
each machine's runtime keeps a derived live store, a SQLite database under the per-user state directory keyed by
estate and clone, refreshed on every open and never committed) or `local-only` (the live store is canonical and no
record file exists; the directory holds only the manifest). Objects are content-addressed by SHA-256 behind one
provider interface: the repository's `objects/` directory, or a SQLite store beside the live store under
local-only; every read verifies the bytes against the name and a mismatch is reported, never served.

## Divergence and reconciliation

Two clones that each add records merge as pure file additions. When both revised the same record, two successors
supersede one predecessor: the loader reports `reconcile.successors`, `status` lists it under Reconcile, and
`vericairn estate reconcile` shows every such task (divergent revisions, duplicate work-order keys, duplicate
results). A **reconciliation** record about the predecessor keeps one successor and withdraws the others, which
then count as superseded by the kept one; nothing is merged silently and nothing is deleted.

## Export and restore

`vericairn estate export` writes a zip holding the records, objects, manifest and documents with an inventory of every
entry's size and hash and a summary whose hash covers the inventory. `vericairn estate restore` verifies every byte
against the inventory before writing anything, restores into an empty directory as a repository-synced estate whatever
the source profile was, and then shows what the copy holds. An export carries records and bytes: no keys, sessions,
credentials or authority, so a restored copy verifies every signature and signs only with a device admitted and held
where it runs; while its charter leaves signatures optional it can still author unsigned records (at record format 2,
never an admission).

## Software: resources, observations, verifications and operations

A repository the estate uses is a `resource` of kind `repository`, identified by its `rootCommit`, which adoption always
records, and by its GitHub `owner/name` or its https remote when it has one; never by a path on a machine. Adoption is
observation: `vericairn sources adopt` records the resource and a first `observation` and changes nothing in the
repository, so uncommitted work is preserved and a missing remote account prevents no local inventory.

An `observation` pins an exact subject revision and carries flat facts with an as-of time (for a repository:
commit, branch, remote, whether the working tree is clean, modified and untracked counts, tags, recent
history) and its limitations. It is a fact as of that moment, not health.

A `verification` pins an exact subject and names the exact `commit` checked, the `method` (command,
github-actions, manual), the `command` when one ran, the `environment` (strings only, never a path), the
`role` of whoever checked (self, independent-agent, operator, automation), the observed `outcome` (passed,
failed or unknown; a command that did not finish is unknown, never failed), the exit code and duration,
evidence (the command output is stored as an object and named by hash) and limitations. `vericairn check
run` records one from a command; `vericairn check observe` records automation's claim from a workflow run
as the host reports it. Evidence, never permission; historical once the subject changes.

An `operation` is an intended external effect under an original `identity`. It is recorded as `not-started`
before anything is dispatched, and every change of what is known is a superseding revision: `dispatched`
(with `dispatchedAt`), `observed-success` or `known-failure` (with what was `observed`), `canceled`, or
`uncertain` when the provider cannot say whether the effect happened. The validators refuse an operation
that starts in any state but not-started, a revision that changes the identity or provider, a transition
the state language does not allow, a second current operation under the same identity, and a retry after
an uncertain, failed or canceled outcome that does not say how it was reconciled. The transport response
is not the effect; `vericairn release submit` and `release observe` write these records.

## Rules that are enforced, not merely documented

- The estate record is authored by its own operator principal, and only that principal may author its own
  principal record (the root of trust). Other principals are admitted by the operator or under the
  `administer` scope.
- A review states its independence (`self`, `independent-agent`, `operator`) and the validator checks it
  against who authored the subject.
- Acceptance pins an exact subject hash. Milestones are accepted by the operator; ordinary work may be
  self-accepted only under the `self-accept-ordinary-work` authority scope, or accepted by another principal
  under `accept-work`. If the subject changes afterwards, the acceptance is reported as unresolved, not invalid.
- Document revisions pin files under `design/` and `docs/` by hash; editing a pinned file without a new
  revision record is a drift error.
- Decisions keep the operator's words verbatim in `statement` and carry a `synthesis`: a clean, thorough restatement
  for display. A decision without a synthesis is reported as a warning. `vericairn decisions` lists them and
  `vericairn decisions <id>` renders one as Markdown.
- Retirement records register temporary artifacts with a removal condition; once retired, the path must be gone.
- An attempt and its result are authored by the assignment's contributor; one current result exists per
  attempt (a correction supersedes); an output whose file is present in the repository must have the hash
  and size the result claims when the result is authored, and later drift of that path is reported as
  information; a completed result with a failed check is a warning for the reviewer.
- `vericairn estate check` warns when prose runs a word into a number ("October13"), outside code spans and identifiers.
- `vericairn estate check` also enforces the repository allowlist, file-size limits, LF endings, UTF-8,
  no symbolic links, no machine-specific absolute paths, and no credential-shaped strings or secret-named keys.
