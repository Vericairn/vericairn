# Record formats, Vericairn 0.5.1

Vericairn 0.5.1 reads record formats 1 and 2 and writes an estate's own format, at most 2. Two documents describe them, and this set carries both as frozen copies:

- `record-format-v1.md`: record format 1, the base of both formats: the envelope, the canonical form and the content hash, signatures, where records live, and the rules every record keeps.
- `record-format-v2.md`: what record format 2 changes, and only that: the format each record states, moving an estate to format 2, the types and fields format 2 adds, the rules it enforces, living documents due, a device that keeps its identity across renames and rotations, and emergency access.

The rule for admissions shows the difference: format 1 counts an unsigned admission while the charter leaves signatures optional, and format 2 signs every admission, admitting or revoking, whatever the policy. Which rules apply to a record follows the format the record states in its `format` field, never the estate's: a record written in format 1 keeps format 1's rules in an estate at either format, and a record written in format 2 follows format 1's rules except where `record-format-v2.md` says otherwise. An estate's manifest states the format its new records are written in (`recordFormat`); a new estate starts at 2, and `vericairn estate upgrade --to 0.5.1` moves an estate at 1 to 2 without rewriting a record. `record-reference.md` lists every type and field and marks those format 2 introduced, and `vericairn records describe <type>` shows the same from the build.
