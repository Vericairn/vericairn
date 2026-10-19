# Estate policies, writing exceptions and release admission

Revision 3, October 19, 2026. Describe reads, signed authoring, shared views, release admission and exact duty evidence in the development build for 0.8.0.

An estate policy declares a rule, owner, selection and level. The runtime evaluates it from validated records, retained proof objects and locally available repository bytes. Reading a policy creates no record. It starts no build, fetch, worker or provider operation.

| CLI command | API route | SDK method | MCP tool |
|---|---|---|---|
| `policies list` | `GET /v1/estate-policies` | `Policies` | `policies_list` |
| `policies show <key>` | `GET /v1/estate-policies/{key}` | `Policy` | `policy_show` |
| `policies kinds` | `GET /v1/estate-policies/kinds` | `PolicyKinds` | `policy_kinds` |
| `policies check` | `GET /v1/estate-policies/evaluate` | `PolicyEvaluate` | `policy_evaluate` |
| `estate writing-exceptions list` | `GET /v1/writing-exceptions` | `WritingExceptions` | `writing_exceptions_list` |
| `estate writing-exceptions show <id>` | `GET /v1/writing-exceptions/{id}` | `WritingException` | `writing_exception_show` |

Each SDK method has an `Async` counterpart with cancellation and a response deadline. The CLI uses the same operations through the embedded runtime or daemon. `--json` returns the complete typed response; the human view always names the basis. A policy key or any revision id selects its current declaration. Exception records and writing removals remain visible as history.

`policies check --as-of 2026-11-01T00:00:00Z` previews an explicit canonical UTC instant. `--release <release-id>` selects a recorded release's product, version and exact commit; both options may be combined. The API and MCP arguments are `asOf` and `release`. A missing target is reported as a problem. An instant before a record in the snapshot is unavailable; historical replay requires the historical snapshot. Preview output grants no release authority.

Without either option, the basis uses the greatest valid timestamp in the observed records and labels it recorded time. An idle estate's recorded-time view can therefore precede today's deadline. The basis identifies its record digest, time origin, repository inventories, content hashes, retained object hashes and installed kind implementation hashes. Missing kinds, unsupported versions, inaccessible sources and incomplete inventories stay visible as unavailable.

Recurring-duty findings preserve the occurrence, originating policy revision, examined subject revision, due time, grace deadline, completion flag and time, missed history, late duration and exact result, review and proof references. A scheduled or within-grace occurrence has not been completed. An excepted occurrence retains its unmet verdict and its debt. Exception history includes expiry, revocations and the exact submission restrictions.

The writing registry reports each active token's original author, reason, scope, match count and matching locations. A count is unknown when the scan is incomplete. Removed entries remain inspectable. Excepted occurrences retain all matching entry references, line numbers and UTF-8 byte offsets. Moving a token within its admitted scope preserves the exception; changing case or matching outside that scope does not. Only warning occurrences of `writing.spacing` can be excepted.

Authoring uses the same acting principal and expected hashes through every transport:

| Command | API operation | SDK method | MCP tool |
|---|---|---|---|
| `policies declare --body <file>` | `POST /v1/estate-policies` | `PolicyDeclare` | `policy_declare` |
| `policies revise <key> --body <file>` | `POST /v1/estate-policies/{key}/revisions` | `PolicyRevise` | `policy_revise` |
| `policies retire <key> --body <file>` | `POST /v1/estate-policies/{key}/retire` | `PolicyRetire` | `policy_retire` |
| `policies except <key> --body <file>` | `POST /v1/estate-policies/{key}/exceptions` | `PolicyExcept` | `policy_except` |
| `policies revoke-exception <key> <id> --body <file>` | `POST /v1/estate-policies/{key}/exceptions/{id}/revoke` | `PolicyRevokeException` | `policy_revoke_exception` |
| `estate writing-exceptions add --body <file>` | `POST /v1/writing-exceptions` | `WritingExceptionAdd` | `writing_exception_add` |
| `estate writing-exceptions remove <id> --reason <text>` | `POST /v1/writing-exceptions/{id}/remove` | `WritingExceptionRemove` | `writing_exception_remove` |
| `work place <key>` | `POST /v1/work/{key}/placement` | `WorkPlace` | Not admitted |

All authoring commands take `--by`. Revisions, retirement, exception grants and removals require `--expected`; revocation expects the exception's current hash, while granting an exception expects the current policy hash. Creation sends the canonical body hash, calculated by the CLI. API and MCP callers supply this as `expected`. Generic `records new` authoring of these types follows the same expected-hash, signature and source checks. Live authoring uses fresh server time. The server requires a held, admitted signing device for the acting principal and rechecks the current authority, exact approval, parameters and exception targets before writing. An unavailable or future finding cannot be excepted.

`work place` selects exactly one `--release-plan <key>` or `--backlog <key>`, with `--expected` for the work revision and `--expected-selection` for the current carrier or placement registry. The server resolves and validates that selection. Writing removal keeps the token, scope and history; `--basis <file>` can name a current delegated authority when needed.

`GET /v1/estate-policies/access?principal=<id>` (`PolicyAccess`, MCP `policy_access`) reports currently admitted action names without creating a record. MCP exposes writes only for its connection principal's current authority and checks it again at the call. Exact policy, rule, token and scope bounds remain server checks on the proposed body. The authority response itself grants no permission.

`estate check`, `status` and `progress` evaluate one snapshot per read. Their `policies` and `writingExceptions` sections carry the same basis and findings as the dedicated reads. Check retains built-in findings beside `policy.unmet`, `policy.unavailable` and `policy.excepted`, with policy revision and rule identity. A refuse-level policy finding is a warning in check; it is a release hold at submission. Only reconstructed, excepted `writing.spacing` occurrences leave the warning count, and each remains visible as `writing.excepted`. Other errors and unread sources remain visible.

Progress and status group unfinished operator-owned duties under **What waits on the operator**, including exact subject, occurrence, deadline, required action and current evidence, even without an ask. An actual ask addressed to an operator can link an occurrence by pinning a result with the exact `duties` binding as its subject. The view groups that ask with the duty, preserving the duty's owner. A builder's linked reserved-action ask therefore does not transfer ownership. Only real asks enter the ask queue; a completed duty does not answer or withdraw an ask. Reads create no asks or other records. These views authorize no effects.

`release submit --product <id> --version <version> --commit <exact-commit> --expected <product-hash> --by <principal>` admits the conjunction of built-in conditions, living-document gates and applicable policy holds. It reports every known objection with its mechanism. Format 3 requires the exact commit, expected current product hash and an admitted held signing device. An older-format submission also needs the exact commit when its release repository has a document comparison. Supplying a commit replaces the earlier provider-resolution step: admission reads local exact source bytes and makes no provider call on refusal. An unrelated clone HEAD cannot stand in for the commit.

Estate-wide policies apply to every product release. Product and repository policies apply to the declared product or release repository. Explicit-work policies apply to work carried by typed release plans and placements for that product and version. Other findings remain visible with enforcement disabled. A missing selected policy source at refuse level holds the release even without a registered clone. A prepared candidate of the same product, version and commit supplies the exact release identity for a submission-bound exception; a general preview exception cannot authorize a build.

The runtime supplies fresh time at preparation, intent and before dispatch. It reads the sources again and rechecks authority, record revisions, exceptions, deadlines and document evidence. A refused initial admission writes no intent and starts no build. If admission changes after intent, the runtime records cancellation when it can; if authority or storage prevents that record, the retained intent requires reconciliation. An unresolved identity is never retried automatically.

Admitted operation evidence retains `vericairn.release-basis/1` objects with the complete policy and duty findings, source bytes and hashes, record digest, exact target and submission time. Preparation remains retained if a later evaluation differs. Pre-dispatch evaluation excludes only that submission's own new operation revisions and pins those exclusions separately, avoiding a circular hash while retaining unrelated changes. API and SDK release responses expose `policies` and `basisEvidence`; CLI JSON preserves them on refusal too. These are ordinary evidence objects, without new record fields. Generic release-intent and publication authoring also passes the shared gate.

Providers receive the prepared commit as `DispatchRequest.ExpectedCommit` and must refuse a moved ref before starting a build. GitHub checks that ref at dispatch; observation checks the commit actually built again before admitting a candidate. A remote branch can still move after dispatch, so an observed effect remains under its original identity and is never represented as undone by a later policy refusal. Publication remains the operator's separate action. MCP exposes no release-admission or acceptance shortcut.

Duty completion uses the ordinary result and review records supplied by record format 3:

| Command | API operation | SDK method | MCP tool |
|---|---|---|---|
| `work result <attempt> --body <file> --expected <attempt-hash> --by <principal>` | `POST /v1/work/result` | `Result`, `ResultAsync` | `work_result` |
| `work review <result> --body <file> --expected <result-hash> --by <principal>` | `POST /v1/work/review` | `ReviewResult`, `ReviewResultAsync` | `work_review` |
| `release basis <hash>` | `GET /v1/release-bases/{hash}` | `ReleaseBasis`, `ReleaseBasisAsync` | `release_basis_get` |

The complete result body carries `duties`. Each binding pins the originating policy revision, rule, tagged occurrence and examined subject revision. It includes `scope.repositories` with exact resource revisions and commits, `scope.objects`, `scope.records`, a coverage statement and each declared proof clause's ordered exact records. A clause checking the result itself uses `{clause, kind: "result-verification"}`: submit the result, record the verification about its exact hash, then review that result with the verification's exact reference in `read`. A passing review without the required exact verification in `read` leaves the duty incomplete. A later adverse or superseding proof can reopen it. No command marks a duty done or records acceptance.

Protocol clients can serialize `ResultDutyDto`, `DutyOccurrenceDto`, `DutyScopeDto`, `DutyRepositoryDto` and `DutyProofDto` into the existing result body's `duties` array. `ResultReviewBody` contains ordinary review fields and typed exact `read` references. Optional fields in the tagged occurrence and proof types are omitted when absent, preserving the strict record shape. Request bodies remain complete JSON objects so unknown or malformed fields reach server validation instead of being silently discarded.

A duty result requires the expected current attempt hash; every `work review` requires the expected current result hash. The server signs as the acting principal with fresh time, validates the full proposed records and re-evaluates the policy sources. Generic authoring of duty results and reviews applies the same checks and refuses supplied historical authoring times. Ordinary results without duties retain their existing submission contract; supplying `expected` opts into the signed current-attempt check. MCP binds authorship to its admitted connection principal and rechecks that admission at the call.

The evaluator loads each declared historical repository commit separately from the current source inventory. A later HEAD or working-tree edit does not replace completion evidence; missing exact history remains unavailable. Historical proof bytes are labelled `evidence` in the basis and do not add obsolete prose to current writing counts.

`release basis` reads an existing evidence object without writing a record. Its typed `ReleaseBasisResponse` contains the original record set, source and proof bytes, policy and duty findings, document comparisons and excluded operation revisions. The CLI's human view names the evidence hash, basis and findings; `--json`, API, SDK and MCP preserve the complete snapshot. Missing or unsupported objects are unavailable. This historical read does not re-authorize submission: every new submission still re-evaluates live sources and time.

The record schemas and authority rules are described in [record format 3](record-format-v3.md), and the evaluation contract in [Policy evaluator](policy-evaluator.md).
