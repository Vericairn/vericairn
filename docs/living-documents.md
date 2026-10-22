# Living documents

A living document follows facts that can change: a README, an operator guide or an
authored page with generated regions. Declare its repository-relative path, source,
triggers and review requirements with `vericairn docs declare`. Frozen release
documentation sets keep their issued bytes and are never declared living.

## The document covers its own file

In 0.8.3, FIX-50 makes the document's own file one of its covered paths, even when
the declaration names only `release` and `decision` triggers. If its bytes differ
from its latest clearance, `docs living` shows it due with a `covers` reason that
names the document, the clearance and the changed content hashes. Missing content
also makes it due. Byte equality is exact, including line endings.

An own-file change uses the highest level in the declaration: `remind`, `warn` or
`refuse`. Thus a document declared at `refuse` for releases becomes due at `refuse`
when its own text changes, even if its explicit `covers` trigger has a lower level.
`status` shows the same reasons; `estate check` and `progress` include them as
`document.due` findings at `warn` or `refuse`. The API, MCP and Workbench use the
same document state. Release submission uses that derivation too: an applicable
document due at `refuse` holds the release before intent or dispatch.

A clearing `document-review` has outcome `current` or `revised` and pins the exact
bytes. Reviewing the new bytes clears the own-file reason everywhere; `needs-change`
does not. For an estate file, a newer `document-revision` can also clear its own
content. Explicitly declared covered paths still require a clearing review of
their content identity. Existing reviews remain usable: their `content` pin covers
the document itself, and their separate `covered` pin covers the declared paths.
No declaration rewrite or replacement review is required merely to adopt this rule.

```sh
vericairn docs living
vericairn docs review docs/guide.md --by <principal id> --outcome current --summary "Checked the revised claims."
```

The declared checks and `clearedBy` requirement still apply. A failed required
check prevents clearance. Whole generated documents under the strict generation
contract prove currency through fresh generation; authored clearance does not
replace that check.

## Which bytes are compared

Estate files use the working tree, including uncommitted changes. Other repositories
use their registered clone's HEAD, or the latest clearing review's commit when Git
proves that HEAD is its ancestor. A newer or divergent HEAD with changed content
is due. This is FIX-23's rule, shared by ordinary document views and the gate.

For a release's own repository, submission compares the exact prepared release
commit and observation compares the actual build commit. Both the document and its
declared covered paths use that selection. When the selected release commit differs
from the commit shown by ordinary `docs living`, the same derivation runs against
those selected bytes. No gate substitutes a different HEAD, fetches or checks out
files. Documents in other repositories retain their own commit selection.

An external document without a clearing review is due. An unreadable comparison
is also shown as due with its cause; the applicable refuse-level gate holds. A
missing registered commit requires the clone to be supplied with that commit.
Only the operator's existing signed, expiring grant for one document, version and
submitting principal can except an unregistered clone's comparison. The release
evidence names that grant, and other due reasons still hold the gate.

See the [operator guide](operator-guide.md#16-living-documents)
and the [record-format guide](record-format-v2.md#living-documents-due) for declarations,
review fields and the other triggers.
