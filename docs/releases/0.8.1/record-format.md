# Record formats, Vericairn 0.8.1

Vericairn 0.8.1 reads record formats 1, 2 and 3. It writes an estate's own format,
at most 3; new estates start at 3. API version 1 remains the transport contract.
Context, hand-over, reference inventories and readable renditions have their own
artifact contracts and are not additional record formats.

- [Record format 1](record-format-v1.md) defines the envelope, canonical JSON,
  content hashes, signatures and the original records.
- [Record format 2](record-format-v2.md) defines goals, routing, asks, living
  documents, persistent devices, worker accounting, communication and execution.
- [Record format 3](record-format-v3.md) adds policies, bounded exceptions,
  recurring-duty evidence, decision requirements, typed work placement, checked
  repository changes and the generated-document `pathPattern` contract.

A record keeps the format in its own envelope, its original bytes, hash and
signatures. Format-2 rules extend format 1, and format-3 rules extend format 2.
The admission rule is estate-wide: at format 2 or 3 an admission or revocation
counts only when signed or anchored by the signed move to format 2, whatever
format or date that admission claims. Policy and writing changes and duty
results/reviews have additional signature and exact-approval requirements.

After the target's accepted publication is recorded, an upgrade from 0.7.3
applies `record-format-3`, updates the manifest and appends the migration record.
A format-1 estate applies `record-format-2` first. No original record is rewritten.
A format-2 reader refuses the upgraded manifest before opening a writable estate;
update and synchronize every participating writer. The migration is forward-only
and idempotent, and creates no policies, grants, exceptions, placements or duty
completions. Installing a binary is separate from upgrading an estate.
[Installation](install.md#6-upgrading-an-estate-to-080) gives the operating steps.

`records describe <type>` and the generated `record-reference.md` expose the
same build registry and identify the format that introduced each field. An older
reader's recognition of a format number alone does not prove that it implements
every later additive field or behavior described here.
