# Policy evaluator

Revision 1, October 18, 2026. Describe the internal policy basis, versioned evaluation contract and retained duty evidence.

The runtime's internal `Policies` library implements `vericairn.estate-policy/1`. It evaluates supplied snapshots and returns findings. Record validation, source collection, implementation admission and release submission remain responsibilities of its caller. The library adds no command, transport operation or persisted record shape.

`PolicyBasis` copies the validated record view, including retained revisions, signatures, diagnostics and supersession links. It discards the loader's filesystem root. Repository inputs name an identity, revision, source contract, completeness and files with normalized relative paths, content hashes and bytes. Retained proof objects carry their hashes and bytes. The basis digest covers these inputs, their availability, the implementation pins and the explicit clock. Canonical output includes diagnostics, evidence, dispositions, counts and ordering.

The caller chooses one clock origin:

| Origin | Contract |
|---|---|
| Recorded | The greatest valid record timestamp in the supplied view; repeated reads do not advance time. |
| Preview | An explicit UTC timestamp with record-format second precision. Earlier replay requires an actual historical snapshot. |
| Submission | An explicit timestamp plus the proposed exact release reference, product, version and commit. The release adapter must collect and recheck these at its submission boundary. |

A timestamp preceding an included record is unavailable. A preview cannot become submission evidence by relabeling its output. The evaluator reads no wall clock, environment, filesystem, process or network. Source adapters perform I/O before constructing the basis. A changed source, time, implementation or record view changes the basis.

`PolicyKindRegistry` supplies exact kind/version implementations. Application composition pins the hash of the actual implementation artifact it admitted; declarations cannot load code or choose an ambient assembly. The foundation implements staleness, work placement, work-key family, revision-line, work-state consistency and recurring-duty version 1. Release-entry belongs to the release surface work. Missing, duplicate or mismatched implementations and unsupported source contracts are unavailable. Invalid versioned parameters also retain an error diagnostic. Pack conformance compares complete canonical evaluations over explicit vectors, including unavailable and erroneous cases.

Findings keep the policy identity and current revision, stable rule key, target, item, level, owner, basis, clock, underlying verdict, evidence and next action. An exception adds `excepted` only to an unmet finding whose exact policy revision, rule and target match. Authority, bounds, expiry and any exact submission restriction still apply. It changes neither the verdict nor built-in diagnostics. Expiry is exclusive. Multiple matching grants remain listed.

`WritingEvaluation` shares the finite spacing scanner with the existing repository check. It scans supplied Markdown and the record format's prose fields. Exact, case-sensitive tokens and union scopes select occurrences; no diagnostic prose is parsed to obtain a token. Matched occurrences remain listed with every matching entry and leave the warning count once. Unused active entries warn only after the entire scan completes. A missing source leaves match counts unknown. Inline code is blanked without shifting offsets; fenced blocks use the scanner's explicit opening and closing grammar. Locations include original UTF-8 byte offsets and one-based lines.

Recurring duties reconstruct occurrences from all retained policy revisions. Day intervals use 86,400 seconds; months use the original UTC Gregorian anchor with month-end clamping. Publication schedules use the first observed publication of each release identity. Dated duties retain their exact adopted date evidence, including an already-due date at adoption. A changed behavioral definition replaces only unopened future occurrences. Old debt retains its original subject, dates, grace and proof requirements, while the current owner and level apply.

Each due occurrence remains individually represented. During grace it is informational and unfinished. At the deadline unfinished work is unmet; a completion exactly at the deadline is on time. Late completion keeps its missed deadline and lateness. Approved `ended-unmet` dispositions retain the debt as historical unmet work and never claim completion. Removed or retired duties retain their history without active enforcement.

Completion requires an exact result binding, every required check once and passed, authentic signatures, the declared native action proofs, available retained bytes and the required exact-result review. A result verification selects the latest current verification of the exact result and method; the review must read that selected proof. Native action timestamps and their first qualifying lineage events prevent editorial revisions from prepaying work. Repository verification pins the declared commit source. Continuing date renewals also require the first following behavioral revision to declare a qualifying next date with new source evidence.

Proof replay retains continuous qualification periods. A later affirmative review preserves the first completion time. An adverse review, withdrawn proof or superseded result ends that period; recovery has a new completion time. Verification expiry is checked when a period starts, while loss of required source access makes evaluation unavailable. Separate result bindings can discharge several already-due occurrences; they do not imply coverage of omitted or future work.

The current implementation bounds a kind at 100,000 records, recurring-duty expansion at 10,000 findings including its next scheduled occurrence, and duty proof replay at 1,000,000 steps per rule. Exhaustion or calendar overflow returns an unavailable rule, never a truncated successful inventory. These bounds, source requirements and implementation pins are part of the evaluation contract and must be preserved or explicitly revised by application composition.
