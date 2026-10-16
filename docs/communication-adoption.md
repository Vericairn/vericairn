# Adopting the operator's existing communication choice

COMMS-01 has one explicit first-party adoption. Decision
`01M4WA3HZ8R5JXQZZTJKEA3MA0`, at
`sha256:a7f473cbb5da48cbccb23b896b257ca4f0e764d38a478651d5b3fc106684c3d7`,
records the choice of person `01M4TRNEE10EJ1WXNDK3H2FKKS`:

| Field | Explicit value |
|---|---|
| Progress | `completion-and-need` |
| Status | `short` |
| Decision | `comprehensive` |
| Result | `comprehensive` |
| Handoff | `comprehensive` |

These are the decision's values even though they currently equal the shipped
defaults. Store every field explicitly so a future default does not change
the person's choice. Keep the original decision; the new setting pins it as
`basis` and `basisHash`. The actual author and their signature belong to the
new record. The original decision does not grant a delegate authority.

After installing the COMMS-01-capable reader, inspect the decision with
`vericairn records show 01M4WA3HZ8R5JXQZZTJKEA3MA0` and verify the hash above.
Then the person can record the adoption:

```sh
vericairn communication set \
  --as 01M4TRNEE10EJ1WXNDK3H2FKKS \
  --basis 01M4WA3HZ8R5JXQZZTJKEA3MA0 --expected absent \
  --progress completion-and-need --status short \
  --decisions comprehensive --results comprehensive --handoffs comprehensive
vericairn communication show --recipient 01M4TRNEE10EJ1WXNDK3H2FKKS
```

`--expected absent` refuses if a personal setting has already been recorded;
it cannot overwrite a newer choice or reset. A delegate instead uses their
own `--as`, adds the person's `--recipient`, and supplies the person's explicit
`--authority` grant. An operator role by itself grants no such permission.
The runtime records the exact source hash and checks the recipient, authorship,
delegation and format before it writes. Readers, builds and previews author
nothing and do not infer personal settings from prose or defaults.

Deployment boundary: the coding assignment permits estate writes only for
Codex's own attempt and result. The connected 0.6.1 estate reader does not
recognize the new record types. This command is a tested adoption procedure,
not a claim that a typed preference has already been written to that estate.
The existing decision remains its recorded instruction until an authorized
person applies this procedure with the updated reader. No other person's
preference is created. Remote is not built.
