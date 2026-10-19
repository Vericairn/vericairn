# Inspecting estate policies and writing exceptions

Revision 1, October 19, 2026. Describe the policy and writing-registry reads in the development build for 0.8.0.

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

These reads expose the existing evaluator. Policy authoring, status/check integration and release enforcement are separate implementation stages; this read surface alone makes no readiness claim. The record schemas and authority rules are described in [record format 3](record-format-v3.md), and the evaluation contract in [Policy evaluator](policy-evaluator.md).
