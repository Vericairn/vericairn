# Record format 1

A Vericairn estate is a body of records. A record is one immutable fact: a charter, a principal, a decision,
a work order, a status change, a review, an acceptance. Records are never edited; a later record supersedes an
earlier one. Everything else (status, who may do what, what is accepted) is derived from the records.

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
| `signatures` | optional | reserved; not verified in this release |

## Canonical form

Hashing uses the compact RFC 8785 form: object members sorted by UTF-16 code units, strings escaped only as
RFC 8785 requires (`\" \\ \b \f \n \r \t`, other control characters as `\u00xx`), no whitespace.
Numbers are restricted to plain safe integers (|n| ≤ 2^53 − 1); fractions and exponents are rejected, so
money is written in minor units and durations in integer seconds.

Files use the readable canonical form: the same ordering and escaping, two-space indentation, LF line endings,
one trailing newline. A file whose bytes differ from that form is invalid; `vericairn records format` restores it
without changing content or hash.

## Where records live

`records/<type>/<yyyy-MM of at>/<id>.json`. Because file names are ULIDs, two people on two machines never
write the same file; merges are always clean file additions, and conflicts are semantic, surfaced as findings.

## Types

Run `vericairn records describe` for the current list and `vericairn records describe <type>` for fields.
Format 1 admits: estate, principal, authority, decision, product, resource, document-revision, work-order,
work-status, review, acceptance, retirement. Reserved for later releases: release, migration, assignment,
attempt, result, device, admission, object.

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
- `vericairn estate check` also enforces the repository allowlist, file-size limits, LF endings, UTF-8,
  no symbolic links, no machine-specific absolute paths, and no credential-shaped strings or secret-named keys.
