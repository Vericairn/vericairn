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

## Template sections in the development build

[Templates](templates.md) add exact version pins and per-section states to authored
living documents in record format 3. `docs new` creates an instance; `docs sections`
shows each part's evidence; `docs brief --section` supplies complete measured
context; `docs review --section --fidelity` reviews a selected part. A passing
review clears no sibling. Required sections must be reviewed at sufficient
fidelity, and no section may be due or failing before the document is current.
Existing triggers, covered paths, policy rules and release gates still apply.
Generated parts keep their generated-region comparison, quoted parts preserve
exact record text, and neither synthesis nor a template grants acceptance or
other authority. Existing non-template living-document behavior is unchanged.

## After a publication

`vericairn docs pass <version>` gathers the documents still due for that publication.
It uses the same due-state evaluation as `docs living` and the release gate, selecting
only documents with a release reason that names the exact publication record.
Documents already cleared for it and documents due only for other reasons are absent.
After another publication supersedes the release reason, its remaining documents
appear in the newer publication's pass. The pass is a current review queue, not a
historical snapshot of the queue on publication day.

Each entry names its declaration, repository, covered paths (including its own file),
reasons and existing review assignments. Registered clones supply added, modified
and deleted paths between this publication's commit and the preceding publication
of the same product. The first publication lists covered files as added. Later work
at clone HEAD is not attributed to the release. Missing commits, missing sources
and documents in other repositories have explicit comparison notes; no clone is
fetched or checked out. Record summaries from the release window provide context,
with matching estate document revisions, decision document lists and estate result output paths
also identified beside each document.

```sh
vericairn docs pass 0.2.0 --product widget
vericairn docs pass 0.2.0 --product widget --json
vericairn docs pass 0.2.0 --product widget --output artifacts/docs-pass
vericairn docs pass 0.2.0 --product widget --document README.md --document ROADMAP.md \
  --assign-to <reviewer id> --by <assigning principal id> --key DOCS-REVIEW-1
```

Use `--product` when more than one product published that version. The optional
assignment selects all listed documents unless `--document` is supplied; repeat it
for a group. A declaration id distinguishes identical paths in different repositories.
The reviewer can be a person or agent, including the builder. The assigning principal
needs operator or local-engineering authority. A new work-order key identifies the
review task. The runtime validates its ordinary work order and assignment together,
pins their publication and document sources, and rechecks that every selected document
is still due before storing. `work brief <key>` gives the reviewer the covered paths,
file changes, comparison gaps and pinned record context. The reviewer uses the ordinary
attempt and result flow and `docs review` for each document. Assignment clears nothing;
the declaration's checks and independence requirement still apply.

The pass also prepares unified patches for declared authored `README.md`, `ROADMAP.md`
and `docs/releases/README.md` front documents. The README's current-release sentence
and date are updated; the roadmap moves its current marker to the published version;
the releases index gains a current entry while retaining the earlier publication's
text. The supported README forms are the sentence “The current published release is
0.1.0, from” followed by its date on the next line, and a `Current release: 0.1.0` line.
Roadmap headings use `## 0.1.0 — current published release` and
`## 0.2.0 — planned`; the index uses `The current published release is **0.1.0**`.
Missing or ambiguous markers need a manual update and are reported as notes.

`--json` includes each patch, its base commit when applicable, and before/after hashes.
`--output` writes one `<declaration id>.patch` per selected front document. It refuses
existing patch files. Inspect the proposed changes and their source basis before
applying them. The pass never edits source documents, clears a review, commits a file
or changes a frozen version directory or its checksum index. Applying, committing
and reviewing a proposed update remain separate actions. A partial assignment store
returns its receipts with a reconciliation problem; inspect the original work order
and batch before retrying.

The API provides `GET /v1/documents/pass/{version}?product=<id or slug>` and
`POST /v1/documents/pass/{version}/assignments`. The latter accepts `by`, `reviewer`,
`key`, the displayed `release` pin and an explicit `documents` array of declaration
pins. Both return typed responses, including problems; SDK callers use `DocumentPass`
and `DocumentPassAssign`, or their cancellable asynchronous counterparts. See
[API compatibility](api-compatibility-0.8.4.md#publication-documentation-pass-docs-12).
