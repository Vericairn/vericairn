# Adopting and operating estate policies

Revision 3, October 20, 2026. Use the read-only definition hash command when preparing exact approval, and explain why generated copies need no writing entry.

Use this guide with the command and transport tables in [Estate policies](policies.md), the strict bodies in [Record format 3](record-format-v3.md), and the [first-party proposals](policy-adoption-proposals.md). The proposals are preparations for exact operator approval. They do not activate policies, authorize work or certify the estate's current state. First-party adoption follows the estate's upgrade to 0.8.0 and record format 3.

## Read the basis before preparing a change

```sh
vericairn policies kinds --json
vericairn policies list --json
vericairn policies show planned-placement --json
vericairn policies check --as-of 2026-11-01T00:00:00Z --json
vericairn estate check --json
vericairn status --json
vericairn progress --json
```

These commands read one snapshot and create no records. Without preview options, time is the greatest valid record timestamp; an idle estate does not advance its clock on a read. An explicit `--as-of` cannot precede a record in that snapshot. `--release <release-id>` selects the recorded candidate's exact product, version and commit. Both preview options can be combined. A preview cannot authorize submission.

Inspect the basis, each rule's underlying verdict, disposition, owner, evidence and next action. A warning-level unavailable source is still a warning; it is not a pass. A refuse-level unavailable or unmet rule holds an applicable release. `estate check` reports policy holds as warnings beside its built-in diagnostics. A successful read or a zero error count does not mean there are no release holds. Check, status and progress include the same policy and writing evidence as the dedicated reads for the same inputs.

## Declare, revise and retire

Prepare the complete policy or duty body with its exact kind versions, selection, level, owner, activation and exception limit. Before adding the approval to `basis`, print its definition hash:

```sh
vericairn policies hash --body policy.json
vericairn policies hash --body duty.json --json
```

The command added in 0.8.1 reads only the prepared file, opens no estate and writes nothing. Plain output is the hash; JSON returns it as `definitionHash`. The value hashes the canonical body with only top-level `basis` omitted, using the same calculation as `policies declare`. See the [CLI reference](cli.md) for every option and invalid-input behavior.

Put that value in the operator's signed decision's `policies` requirement as `definitionHash`. The requirement names the action and policy; revision and retirement also name the prior hash. Add the exact decision and requirement to the body's `basis`, then run `policies hash` again to confirm the value is unchanged. Any change outside `basis` needs the hash of the changed definition. A manager's authority and the operator's definition approval are separate requirements.

```sh
vericairn policies declare --body policy.json --by <principal> --json
vericairn policies revise planned-placement --body revised-policy.json --expected <current-policy-hash> --by <principal> --json
vericairn policies retire planned-placement --body retired-policy.json --expected <current-policy-hash> --by <principal> --json
```

The definition hash is for approval. The CLI separately hashes the complete creation body, including `basis`, for `expected`; API callers supply that complete canonical body hash in `PolicyChangeRequest`. Revision and retirement expect the current record hash. The runtime requires the acting principal's held, admitted signing device, validates the sources and strict parameters, and checks current authority. A conflict means read the current revision and prepare again.

A revised body includes the entire definition. Behavioral changes need exact approval; an ownership transfer needs transfer authority too. Retirement uses `state: retired` and `retirement: {reason, findings, successor?}`. It is terminal and retains the identity and history. Removing a duty rule or retiring its policy must account for every outstanding occurrence, including grace, using approved `dutyDispositions`. `ended-unmet` records an explicit end to debt; it does not claim completion. Forward schedule changes retain already-open occurrences and their original proof requirements.

For planned work, inspect the current placement registry or release carrier before using `work place`. The command requires both `--expected <work-hash>` and `--expected-selection <registry-or-carrier-hash>`, with exactly one `--backlog <key>` or `--release-plan <key>`. A placement does not launch an assignment, create a dependency or accept work. Backlog work remains subject to its separate age reminder.

## Except one unmet finding

Prepare a `policy-exception` body naming the rule and exact targets returned by evaluation, the reason, an exclusive UTC expiry, `state: granted`, and the exact decision in `basis` (plus the bounded grant for a delegate). A release-specific exception includes the exact prepared release reference, product and version in each target's `submission`. A duty target identifies its originating policy revision, rule, examined subject revision and tagged occurrence. There are no wildcard or future-occurrence targets.

```sh
vericairn policies except planned-age --body exception.json --expected <current-policy-hash> --by <principal> --json
vericairn policies revoke-exception planned-age <exception-id> --body revocation.json --expected <current-exception-hash> --by <principal> --json
```

The first-party duration limit is at most 30 days; delegated limits may be shorter. Expiry at the exact boundary restores enforcement. Revocation retains the policy, rule, targets and expiry, changes `state` to `revoked`, and gives its reason. Renewal is a new grant. A changed policy revision or target, lost delegated authority or a different submission can also prevent matching. Delegates cannot excuse their own work. Unavailable inputs cannot be excepted into success.

Read the finding again: its underlying verdict remains unmet, with the exception and deferred enforcement visible. An exception clears no built-in error, document gate, acceptance requirement or reserved-action boundary.

## Add and remove writing entries

Generated copies need no writing entry. The spacing scan skips whole files owned by a current, unretired `kind: generated` declaration, including its `pathPattern` members, and declared generated regions inside authored documents. The generator checks continue to hold that text to its sources. Authored files, the authored lines around each region and record prose fields are still scanned; other declaration kinds do not exempt whole files. An entry used only by generated copies therefore has no matches and warns as unused after a complete scan.

Prepare a complete `writing-exception` body with an exact case-sensitive whole `token`, `code: writing.spacing`, its reason, `state: active`, and `basis`. An operator uses `{kind: operator}`; a delegated writer uses `{kind: authority, grant: {type, id, hash}}`. Optional `scope` is a union of repository path selectors and exact record ids, optionally limited to one scanned prose field.

```sh
vericairn estate writing-exceptions add --body writing-entry.json --by <principal> --json
vericairn estate writing-exceptions list --json
vericairn estate writing-exceptions show <entry-id> --json
vericairn estate writing-exceptions remove <entry-id> --reason "The spelling is no longer used in scope." --expected <current-entry-hash> --by <principal> --json
```

A delegated removal can supply `--basis <file>` with its current authority basis. Removal preserves the original token, scope, reason and author and appends signed removal history. Entries have no level or expiry. They never quiet another warning or an error. `Open311` does not match `open311`. Code spans and fenced code do not need entries. An entirely unused active entry warns only when the scan is complete; unreadable sources leave its count unknown. Excepted occurrences remain visible with exact locations and every matching entry.

## Prepare and review a duty occurrence

Read the occurrence's originating policy revision, subject revision, due time, deadline and required checks. During grace it remains unfinished and informational. At the deadline unfinished work is unmet at the declared level. Monthly schedules retain their UTC anchor; a late completion preserves the missed deadline. Neither a later month nor an exception removes debt.

Prepare authorized work manually for the exact occurrence and inventory. The initial first-party stage has six monthly duties and one additional check per publication. The builder prepares results, the Reviewer reviews advisory and workflow-pin security assessments in two focused monthly sessions, and an independent non-security reviewer handles the other results. A second session by the result's author does not supply independence. The policy's `independent` parameter checks independence; the approved routing and assignment must also select the correct principal. No policy read schedules work or grants an effect.

The complete result body includes `duties`. Each binding has exact `policy` and `subject` references, `rule`, `occurrence`, `scope`, a coverage statement and `proofs`. An interval occurrence has `kind: interval` and its zero-based `number`; a release occurrence has `kind: release` and the exact first observed `publication`; a date occurrence has `kind: date` and its adopted `at`. Scope retains repository revisions with commits, object hashes and record references. Pass every named completion check once and report inaccessible sources as incomplete.

```sh
vericairn work result <attempt-id> --body result.json --expected <current-attempt-hash> --by <author> --json
vericairn work review <result-id> --body review.json --expected <current-result-hash> --by <reviewer> --json
```

`POST /v1/work/result` carries `ResultRequest`; `POST /v1/work/review` carries `ResultReviewRequest`. The SDK exposes `Result` and `ReviewResult`, each with an asynchronous counterpart. Use the exact bodies and expected hashes, then inspect the returned write outcome. MCP workers can submit their result, but `work_review` is offered only to an admitted operator connection. An authorized independent agent reviewer uses the CLI or API under its own authority.

The review body names `independence`, `outcome: pass`, `summary`, `findings` and any exact proof references in `read`. A result-subject verification follows this order: submit the result with `{clause, kind: result-verification}`, record a passing verification of that exact result and method, then review the result with that exact verification in `read`. The monthly export/restore duty uses this route. The per-release duty instead verifies the exact product revision against the publication commit. A passing result alone, an old hash or an unreviewed verification does not complete either duty.

Export/restore evidence retains the manifest and export hash, an isolated restore comparison and negative integrity checks. The monthly checks include `authority-not-restored`; the per-release checks include `published-build-used`. Use disposable destinations, preserve evidence and copy no private keys. Restore does not restore authority. A local-only to repository-synced restore changes the manifest and therefore the basis digest; compare the appropriate record digest, inventories and findings rather than asserting identical digests across different profiles.

One catch-up result may explicitly bind several already-due occurrences; its exact review must cover each. An omitted occurrence stays owed. A future occurrence cannot be prepaid. Native proofs, continuing renewal dates and operator acts still apply where declared. An empty withdrawn-artifact inventory needs independent review; an actual external removal also needs its authorized operator operation evidence. An uncertain effect remains unresolved. A later adverse review or superseded proof can reopen an occurrence. Acceptance of work and clearance of living documents remain separate.

## Follow a held release

```sh
vericairn release submit --product <product-id> --version <version> --commit <exact-commit> --expected <current-product-hash> --by <principal> --json
vericairn release basis <evidence-hash> --json
```

Submission makes a fresh combined check of built-in conditions, living-document gates and applicable policies. Initial refusal starts no build and writes no operation intent. The service acquires sources again and rechecks before dispatch. A prepared preview and an earlier passing check grant no submission authority. A refused request exits 2; structurally rejected authoring exits 1. Read every returned objection and retained basis rather than treating one repaired hold as release readiness.

For `release-devlog`, an observed publication opens its own requirement; the candidate owes no account of its future publication. A later submission for that product is held until there is exactly one qualifying new Internal draft devlog. Its first retained revision follows the publication, its metadata pins the exact publication and version, and its current bytes have an independent ordinary passing review of the exact document revision. A living-document clearance is insufficient. Editing the draft requires a new exact review. Older publications outside the approved cohort create no retrospective quota.

The retained release basis records exact sources, hashes, findings, duty evidence, time and target. Reading it grants no new authority. If admission changes after intent, inspect the cancellation or unresolved intent and reconcile that original identity before retrying.
