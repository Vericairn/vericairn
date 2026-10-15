# Record format 2

Record format 2 is record format 1 ([record-format-v1.md](record-format-v1.md)) with rules for the records written
from now on. Nothing already written changes: every format-1 record keeps its bytes, its hash, its signatures and
the rules it was written under. A record states the format it was written in, and an estate at format 2 holds both.
Format 2 carries the records goals need, the additions milestone M3 adopted and, with DOCS-03, the living-document
records; this document grows with them until release 0.5.0 ships the format.

## Why a new format number

A release can add a type or an optional field without changing the format number; record format 1 grew that way from
0.1.0 to 0.4.2. Format 2 also adds requirements: a decision written in format 2 must say how much rides on it and what
it does not authorize, and a revision of a decision keeps the operator's words. A requirement cannot apply to records
written before it existed, so the rules follow the format each record states, and an estate moves to format 2 by one
recorded step.

## The envelope and the estate

- `format` is `vericairn.record/2` for a record written in format 2. Every other key of the envelope, the canonical
  form and the hash are unchanged. A format the build does not read is refused (`record.format`).
- The manifest's `recordFormat` is the estate's format: the format its new records are written in. At format 1 the
  estate holds only format-1 records; a record stating a later format than its estate is an error
  (`record.format-ahead`). At format 2 the records already written stay format 1, and a clone that had not yet moved
  can still add format-1 records, read under format 1's rules.
- A build reads the formats its release record lists in `recordFormat.reads` and writes an estate's own format, never
  one beyond `recordFormat.writes`. It refuses to open an estate whose format it does not read, instead of misreading
  it (`manifest.record-format`, naming the format); `vericairn doctor` prints the formats a build reads and writes.
- A build validates each record under the rules of the format the record states. A type or a field introduced in
  format 2 is refused in a format-1 record (`record.type-format`, `body.format`), with the upgrade that answers it.
- `vericairn records describe` and the record reference mark the types and fields format 2 introduced.
- A new estate starts at the newest format its build writes.

## Moving an estate to format 2

`vericairn estate upgrade --to <release>` with a release that writes format 2 moves the estate in one reviewable
commit: the manifest's `recordFormat` becomes 2 and its pin the release, and the migration record, itself the first
format-2 record, names the step `record-format-2`. No existing record is rewritten, so every record's bytes and hash
before and after are equal; `--check` shows the same without writing. The release must read every format the
estate's records use; an upgrade to a release that does not is refused, as is one to a release writing a format this
build cannot read.

## Fields new in format 2

| Type | Field | Kind | Meaning |
|---|---|---|---|
| `decision` | `stake` | `low`, `medium`, `high` or `critical`, required | How much rides on the decision; at medium and above the recorder asks with a recommendation first and at most three questions at a time |
| `decision` | `notAuthorized` | string list, required, may be empty | What this answer does not authorize, so no one reads more into it |

## Rules enforced in format 2

- A decision that supersedes another keeps its `statement` byte for byte: the operator's words are protected, a revision
  corrects what surrounds them, and different words are a new decision (`decision.statement`).
- Every rule of record format 1 applies to format-2 records too, except where this document says otherwise.
