# Test fixtures

`format-1-estate/` is a small estate written by the released Vericairn 0.4.2 binary in record format 1: an operator
and a builder, a decision, a grant, a work order, a product and a release with its acceptance and publication. It is
golden: its bytes never change. The tests read it with the current build, which must find it valid, rewrite none of
its records and move it to record format 2 with a migration that adds only the migration record.
