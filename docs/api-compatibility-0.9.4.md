# API compatibility in 0.9.4

## Presence sessions in the runtime (ID-16)

- `Vericairn.Records.Signing.IRecordContentSigner` (new interface): `SignRecord(ReadOnlySpan<byte> content, ReadOnlySpan<byte> digest)`.
  `RecordAuthor.Create` hands every such signer the record's compact canonical bytes without hash and signatures beside their digest.
- `Vericairn.Records.Signing.StoreSigner` implements `IRecordContentSigner`, and gains `SignRecord(content, digest)` and
  `SignRecord(content, digest, reason)`.
- `Vericairn.Records.Signing.IDeviceKeyStore.SignRecord(deviceId, content, digest, reason)`, a default interface method that signs
  the digest; `SecureEnclaveKeyStore.SignRecord` runs the helper's `sign-record`.
- `Vericairn.Records.Authoring.RecordAuthor.ContentBytes(ReadOnlyMemory<byte>)`: the bytes a record's signatures are over.
- `Vericairn.Records.Types.EstateType.PresencePolicy(EstateModel)`: the charter's `policies.presence` as it states it (an empty text
  for a value that is no text), or null.
- `Vericairn.Runtime.Service.EstateService.ForLocalSocketRequest()`, `EmbeddedProfile` and `LocalSocketProfile`: the daemon names a
  request it received over its local socket or named pipe, the profiles on which a presence session opens.
- `Vericairn.Daemon.ServiceInstaller.Plan(platform, root, estateId, launcher, userHome, vericairnHome, userId, environment)`: a new
  overload with the service's environment; the existing overload is unchanged.
- Behavior: `SecureEnclaveCustody.HelperPath()` uses the bundled helper when the build carries a Developer ID team identifier.
  The published 0.9.4 archives omit that identifier and honor `VERICAIRN_ENCLAVE_HELPER`. `doctor --json` gains the probe
  `presence`. The charter's policies admit `presence` from record format 3.
- No public API changes in the follow-up. Protocol (the release helper's presence sessions, `vericairn.presence-session/1`, which
  no release has shipped yet): a `dispatch-release-workflow` kind entry's `fixed` values must carry `workflow`, held to the branch's
  character set; terms that omit it, or state one outside that set, are refused with `terms.invalid`, and a dispatch record whose
  `target.workflow` differs from the entry's, or that states none, is refused with `record.outside` (as a record it signs) or
  `record.reference` (as a referenced operation a retry or revision supersedes). A runtime and helper of the same release agree;
  terms written by a runtime from before the follow-up are refused by the follow-up's helper.

## Presence sessions in the enclave helper (ID-15)

No public API change: no route, interface member, protocol type, record type or field, or member of a Community package is added or
changed. The macOS enclave helper, which the runtime drives and no client calls, adds `vericairn-enclave session <blob>`: one JSON
object per line on standard input and output in `vericairn.presence-session/1` (`open`, `prompting`, `opened`, `sign`, `signed`,
`status`, `close`, `refused` and `ended`, each naming itself in `type`), with the terms compared with the blob's key before anything
is shown and every record checked by the helper itself before it signs. It also adds `vericairn-enclave sign-record`, whose dialog
opens with "sign Vericairn record <type> <short hash>:", and the digest-only `sign` now opens with "sign an unread Vericairn
digest:". A helper whose own code does not meet the release requirement refuses `open` with `helper.unsigned`, so a development
build opens no session. From the follow-up, a session refuses a record that takes any id the session holds (`record.author`) or
supersedes its own id (`record.reference`), holds an observation to the operation it observes, and makes its context non-interactive
once its check has passed, so a signature the check does not cover is refused with `check.unavailable`. The vectors in
`tests/fixtures/presence-sessions/vectors.json` hold the helper and the runtime's canonical serializer to the same bytes, and
[presence-session qualification](presence-session-qualification.md) gives the procedure for a candidate's signed helper. ID-16's
entry above gives the runtime's side.

## Presence session notices, the owner's setting and the session ledger (CODE-80)

- `Vericairn.Protocol.PresenceNotice` gains two init-only properties outside its positional parameters, so its constructor and
  deconstruction are unchanged: `Session` (`PresenceSessionNotice?`) and `ShortHash` (`string?`).
- `Vericairn.Protocol.PresenceSessionNotice` (new record): `Id`, `LimitSeconds`, `Terms`, `Descriptions`, `Nonce`, `SecondsLeft`,
  `Reason`.
- `Vericairn.Protocol.PresenceEntryDescription` (new record): `Entry`, `Type`, `Key`, `Purpose`, `Changes`, `Flagged`.
- `Vericairn.Protocol.PresenceNoticeVocabulary.Kinds` gains `session`, `signed`, `renewed` and `ended`, after the five of 0.9.3;
  `PresenceNoticeVocabulary.EndReasons` (new) lists the reasons an `ended` notice gives.
- OpenAPI: the `PresenceNotice` schema gains `session` (nullable, a reference to `PresenceSessionNotice`) and `shortHash`
  (nullable), and its `kind` enum the four kinds; new schemas `PresenceSessionNotice` and `PresenceEntryDescription` (its `changes`
  nullable). `POST /v1/presence/prompts` is otherwise unchanged.
- Behavior of the command line and Workbench (not an API): every line they take from the runtime begins with `runtime:`, with
  control characters shown escaped, so FIX-124's prompt lines now print with that mark; `devices presence` refuses to write over a
  `presence.json` that does not read; the runtime waits for every client following the notices, each counted on its own, to read a
  session's terms before the helper's nonce exists. Clients are told apart by the position they read from, so two clients at the
  same cursor, such as the command line beside Workbench, count as one follower: once one has read the terms, the other may receive
  them with the nonce and show the warning instead of the fingerprint, a false warning but never a false fingerprint. The command
  line sends its first waiting read before the command acts; over the daemon's socket the daemon may still see the action first.
- From the re-check of PR #398, the command line folds every line of the runtime's to the terminal's width (80 columns when its output
  goes to a file or a pipe, or the width cannot be read) with `runtime:` on every row and runs of spaces made one space, and a
  presence-related command's problems from the runtime are shown that way too.
- Behavior: an action signing under a presence session raises `session`, `signed`, `renewed` and `ended` where ID-16's development
  builds raised `prompt` (the fingerprint and each signature) and `revised` (a renewal), with the same texts. No 0.9.3 runtime opens a
  session, so a 0.9.3 client meets these kinds only from a 0.9.4 runtime, and prints their text.
- Command line (`dotnet-vc`, not an API package): `vericairn devices presence <device> [--per-signature | --per-action [--limit
  <seconds>]]` and `vericairn devices sessions [<device>]`, each with `--json`.
- No change to the public API of `Vericairn.Records`, `Vericairn.Runtime`, `Vericairn.Daemon` or `Vericairn.Sdk`; the client's
  reading of terms and fingerprint is internal to the SDK assembly the command line and Workbench share.

## Presence leases and the displaced holder (ID-11)

Vericairn.Protocol adds `PresenceRegisterRequest`, `PresenceLease`, `PresenceLeaseResponse`, `PresenceEntry`,
`PresenceResponse` and `PresenceVocabulary`, and `IEstateApi` adds `PresenceRegister`, `PresenceHeartbeat`, `PresenceRelease` and
`Presence` with their `Async` counterparts, each with a default implementation, which `CancellationBoundApi` forwards.
Vericairn.Sdk adds those operations to `VericairnClient` and the class `PresenceKeeper`. Vericairn.Runtime adds
`EstateService.PresenceRegister`, `PresenceHeartbeat`, `PresenceRelease`, `Presence` and `RunPresenceSweepAsync`.
Vericairn.Records adds `Routing.HolderOf`, `Routing.DisplacedBy`, `Routing.Displaced`, `Routing.ReassignAfterSeconds` and
`WorkView.Displaced`; `WorkView.AssignmentItem.State` may read `displaced`. The state language adds the assignment state
`displaced`. Nothing is removed or changed in signature.

## Availability, quiet hours and the session route (GOAL-11)

Vericairn.Protocol adds `CommunicationAvailability`, `QuietHoursSetting`, `QuietHoursWindow`, `AvailabilityState` and
`AvailabilityRules`; init-only properties `CommunicationValues.Availability` and `QuietHours`, `CommunicationProfile.Availability`,
`QuietHours` and `AvailabilityNow`, and `TakeoverRequest.Basis`; positional constructors are unchanged. Vericairn.Records adds
`Routing.LiveSession`, init-only `Routing.Machine.Sessions`, `Routing.Candidate.AwayUntil` and `Session`, an
`Routing.Evaluate` overload taking the step's session exposure beside the unchanged one, `Routing.AwayUntil`,
`Routing.SessionExposure`, `Routing.StepRecords`, `Routing.LapseAsk` and init-only `NextEligible.Route.Session`.
`AssignmentType.RoutedWith` gains `session` after `run` and `ask`. `GoalStepResponse.Outcome` may read `session`.

## Standing agents (GOAL-07)

All additive; every Community package keeps 0.9.3's API.

- `Vericairn.Protocol`: `GoalDueResponse(string Text, JsonElement Json)`; `IEstateApi.GoalDue(string goal, string? asOf = null)`, a
  default interface method that throws `NotSupportedException`, and `IEstateApi.GoalDueAsync`; `CancellationBoundApi.GoalDue` and
  `GoalDueAsync`; `GoalStepRequest.Occurrence` and `GoalStepRequest.Now`, init-only properties omitted from JSON when null;
  `GoalActionRequest.StopRuns`, an init-only property omitted from JSON when false; the
  OpenAPI document's `GET /v1/goals/{id}/due` and `GoalDueResponse` schema, `occurrence` and `now` in `GoalStepRequest`, and
  `stopRuns` in `GoalActionRequest`.
- `Vericairn.Sdk`: `VericairnClient.GoalDue` and `GoalDueAsync`.
- `Vericairn.Presentation`: `StandingRow`, `EstateView.Standing`, `GoalPresentation.Standing`, and the state-language vocabularies
  `standing` and `occurrence` (`docs/state-language.json`).
- MCP: the read tool `goal_due`.
- Records (format 3, additive): a goal's `plan.standing` (with optional `awaitingReview`) and an assignment's `occurrence`; a flow's
  trigger kinds are now one of `schedule`, `duty`, `event`, `condition` and `request`; `standing.duplicate` is a reconcile task, and a
  reconciliation settles it; the reader is `vericairn.records/readers/11`.

## Standing outputs (GOAL-08)

All additive; every Community package keeps 0.9.3's API.

- Protocol: `StandingOutputRequest(By, Assignment, Kind, Body)`; `IEstateApi.StandingOutput` and `StandingOutputAsync`, default
  members; `CancellationBoundApi.StandingOutput` and `StandingOutputAsync`; `WorkNewRequest.FromPossibility` (an init property, the
  positional constructor and deconstruction unchanged); OpenAPI `POST /v1/standing/outputs` and `StandingOutputRequest`, and
  `WorkNewRequest` gains `fromPossibility` with only `by` required.
- SDK: `VericairnClient.StandingOutput` and `StandingOutputAsync`.
- MCP: the write tools `standing_possibility`, `standing_question` and `standing_publication`.
- CLI: `work new --from-possibility`, `goal propose --outputs`.
- Records (format 3, additive): `possibility.raisedBy`, `fingerprint`, `sources`, `evidence`, `reviewTriggers`, `links`, `draft`,
  `disclosure`, `extras`, `raisesAgain`; `ask.raisedBy`; `operator-request.raisedBy`; `goal.plan.standing.outputs`. A format-3 reader
  that predates them refuses such a record as carrying an unknown key; a format-2 estate refuses them.
- JSON: goal due's and status's standing agent summary gains `outputs {open, cap}`.

## The first standing roles (PROC-08)

All additive; every Community package keeps 0.9.3's API.

- Records: `Vericairn.Records.Templates.StandingRoleFlows` (`DutyCarrier`, `Researcher`, `BugHunter`, `Version`, `Definitions`), and
  the three built-in flows `vericairn.estate/duty-carrier`, `vericairn.software/researcher` and `vericairn.software/bug-hunter` at
  1.0.0 in every template listing.

## Step timing (PROC-18)

All additive; every Community package keeps 0.9.3's API.

- `Vericairn.Records.Templates.FlowTiming` (new record): the timing contract a step's `when` states, read with
  `FlowTiming.Read(JsonElement)`: `Start`, one of `Starts` (`instant`, `delay`, `at`, `schedule`, `event`, `watch` and `due`),
  `After`, `From`, `Time`, `Output`, `Field`, `ScheduleDays`, `Times`, `EventKind`, `Predicate`, `Every`, `Limit`, the window's
  `Open` and `Quiet` spans (`FlowTiming.Span`), `Zone`, `Deadline`, `Missed`, one of `MissedRules`, `Fixed`, `Element`, `Windowed`,
  `Untimed`, `Waited`, `NextScheduled`, `InWindow`, `NextOpen` and `Describe`; and static `Instant`, `EventKinds`, `Days`, `Utc`,
  `ZoneOption` (`zone`), `WhenOption` (`when.`), `Close`, `Activation`, `WaitedOf`, `ZoneOf`, `TryZoneOf`, `KnownZone`,
  `LocalToInstant`, `ParseInstant` and `LocalTime`. `FlowStep.When` keeps its type and is read as this contract.
- `Vericairn.Records.Estate`: `FlowRuns.Configuration.Zone` and `FlowRuns.Configuration.Timing(FlowStep)`, the run's time zone and a
  step's effective timing; `FlowPosition.StepDue(At, Reason)` (new record) with init-only `Timing`, `Zone`, `Deadline` and `Missed`;
  `FlowPosition.StepInstance.Due`, init-only, so the positional constructor and deconstruction are unchanged; `FlowPosition.DueText`
  and `DueJson`; and `FlowPosition.AttentionKinds` gains `missed`.
- `Vericairn.Presentation` (not a package): `DueRow`, `FlowRunRow.Due`, `EstateView.Due` and `GoalPresentation.Due`.
- JSON, with no route, request or response type changed: the goal read and `goal show --json` give each timed step's `due` (`at`,
  `reason`, `timing`, `zone`, `deadline` and `missed`), and canonical status's `runs` (`GET /v1/status`) gain `due`, as of the
  newest record.
- Refusals: the flow contract checks each timing (`flow.when`); a configuration's `zone` must name a zone every machine reads alike
  (`flow.zone`), and its `when.<step>` is refused for a step whose timing the flow fixes (`flow.timing-fixed`).
- Records: no record type or field is added. Flows that use `due` or `watch` keep their content and hashes. A 0.9.3 runtime refuses
  a flow whose step uses any other timing (`flow.when`) and refuses `zone` and `when.<step>` as options the flow does not declare
  (`flow.option`).

## Decision transitions (PROC-19)

`IEstateApi.GoalDecide` and `GoalDecideAsync`, with default implementations that refuse as unsupported; `GoalDecideRequest`;
`GoalStepResponse.Decision`; `VericairnClient.GoalDecide` and `GoalDecideAsync`; `CancellationBoundApi.GoalDecide` and
`GoalDecideAsync`.

## The release flow reads complete when the release is (FIX-142)

`Vericairn.Records.Estate.FlowStops.Dismissal(model, goal, step, command, since, until)` (new): the operator's dismissal that
settles an optional reserved step, or null. No route, protocol type, record type or field changes, and nothing else in a Community
package's API. Behaviors of the flow engine, for runs on any revision: once the addressed operator dismisses the request of an
optional reserved step, a request carrying the command the records give now, after the step was asked, the step reads `off`, skipped
with the dismissal as its reason and with no revision of the goal, while a required or policy-required step stays asked; a goal
revision whose options only turn optional steps or optional items off keeps the asks of every step it leaves in the run; and a step
done by its done predicates alone, each a count of at most none, such as the documentation pass after publication, counts only once
every step it waits on is done. The state language's label for a step that is `off` adds "or skipped". The base flow
`vericairn.software/release` 1.0.0 keeps its content and hash.

## Closing a run with a reason for each unsettled step (FIX-143)

All additive; every Community package keeps 0.9.3's API.

- `Vericairn.Protocol`: `IEstateApi.GoalClose(goal, GoalCloseRequest)` and `GoalCloseAsync`, default members that refuse as
  unsupported; `GoalCloseRequest(By, Reason = null, StepReasons = null)` and `GoalStepReason(Step, Reason)` (new records);
  `GoalAcceptRequest` gains init-only `Reason` and `StepReasons` outside its positional parameters, so its constructor and
  deconstruction are unchanged; `CancellationBoundApi.GoalClose` and `GoalCloseAsync`; and the OpenAPI document's
  `POST /v1/goals/{id}/close`, the schemas `GoalCloseRequest` and `GoalStepReason`, and `reason` and `stepReasons` in
  `GoalAcceptRequest`.
- `Vericairn.Sdk`: `VericairnClient.GoalClose` and `GoalCloseAsync`. `Vericairn.Runtime`: `EstateService.GoalClose`.
- `Vericairn.Records.Estate`: `RunClosings` (new: `MaximumReason`, `Unsettled`, `Plan`, `Of` and `Prepare`); `GoalRules.MayClose`,
  `CloseRefusals` and `Closing`; `FlowPosition.Position.Closing` and `Unsettled`; `FlowPosition.ClosingJson` and `UnsettledText`;
  and `FlowPosition.States` gains `unsettled`. `Vericairn.Records.Types.GoalStatusType.Statuses` gains `closed`.
- Records (format 3, additive): `goal-status` gains the status `closed` and the field `unsettled`, each entry
  `{key, instance?, reason}`, required with `closed` and refused with any other status; an earlier format refuses `closed`
  (`goal-status.closed`), and a reader before 0.9.4 refuses a closing as a status it does not know with an unknown key. A goal
  without a closing reads as before.
- Command line: `goal close <goal> --by <principal> [--reason <text>] [--step-reason <step>=<reason>]`, and `goal accept` takes
  `--reason` and `--step-reason`, writing the closing and the acceptance as one judged batch on the runtime's machine, so a
  device-signed network request is refused for an acceptance with reasons. Status's canonical `runs` list a closed run, with its
  `closed`, until its goal is accepted or ended. The state language adds the goal status `closed` and the flow-step state
  `unsettled`. MCP adds no tool; `goal_get` and `goal_progress` read the closing.

## A close counts only when made after its step was asked (FIX-144)

No public API change: no route, protocol type, record type or field, or public member changes. Behavior of the flow engine: an
optional step skipped in the run's current shape carries when it was skipped (the dismissal's time, the time of the revision since
which every revision turns it off, or the time the step it takes its input from was skipped), and a reserved step's asked-since time
is no earlier than the skip time of any step it waits on, directly or through steps that are off. A close of its exact command made
while it waited counts for nothing, and one made after counts. A step that a revision turns off after it was done, or after its
request was dismissed, keeps the closes recorded since for the steps after it.

## A running run keeps its configuration (FIX-146)

No public API change: `FlowRuns.Product` keeps its signature, and no route, protocol type, record type or field changes. Behaviors
of the flow engine: a run reads the `template-defaults` records, and the product its work order names, as they stood when its
current goal revision was first activated, so defaults recorded later apply to new runs and to the run's next revision, which its
sponsor signs and activates; a revision compares the configurations each revision was resolved with, so the defaults it takes in
count as a change of its configuration even when the goal's own options are unchanged; a revision that changes `until` asks every
step again; an optional item a revision turns off settles its step as done from that revision, unless the item was satisfied before
it; and a step's done time counts the reviews and verifications that satisfy its items.

## Concurrent assignments compared by their permitted paths (CODE-73)

Every change is additive; no member is removed or changes its signature.

- **Vericairn.Protocol**: `IEstateApi.WorkOverlap(string? repository = null)` and `IEstateApi.WorkOverlapPreview(OverlapPreviewRequest)`,
  each with a default implementation that throws `NotSupportedException`, and their cancellable counterparts `WorkOverlapAsync` and
  `WorkOverlapPreviewAsync` in `IEstateApi.Async`; the matching members of `CancellationBoundApi`; the records `OverlapResponse(Text, Json)`
  and `OverlapPreviewRequest(Brief, Repository)`; the init-only properties `WriteOutcome.Notices`, `RunResponse.Notices`,
  `GoalStepResponse.Notices` and `AssignRequest.Repository`, each omitted from JSON when null; the routes `GET /v1/overlaps` and
  `POST /v1/overlaps/preview` in `OpenApiDocument`, with the `notices` and `repository` properties in its schemas.
- **Vericairn.Sdk**: `VericairnClient.WorkOverlap`, `WorkOverlapPreview`, `WorkOverlapAsync` and `WorkOverlapPreviewAsync`.
- **Vericairn.Runtime**: `EstateService.WorkOverlap` and `WorkOverlapPreview`; `ISourceAdapter.TrackedPaths`, `Reachable`, `Precedes` and
  `ChangedPaths`, each with a default implementation answering null; the record `SourceChange(Path, Change)`; and
  `IWorkspaceProvider.CheckScope(PreparedWorkspace, WorkspaceSnapshot, IReadOnlyList<string> permitted, IReadOnlyList<string> excluded)`,
  whose default reports the audit incomplete when exclusions are named.
- **Vericairn.Adapters.Git** (not a package): `GitCommandAdapter` implements the four adapter members and `GitWorktreeWorkspaces` the
  four-argument `CheckScope`.
- **Vericairn.Records**: no public member is added; `AssignmentType.MaxAfter` (20) is a new public constant, and the record types'
  field descriptions name the new fields.
- **Record format 3**: `assignment.brief.repository`, `assignment.brief.excludedChanges`, `assignment.brief.after` and `result.change`
  are optional; a reader that predates them refuses a record carrying one as an unknown key, and reads every record without them. The
  generic record reader advances to `vericairn.records/readers/10`. Status's and progress's canonical JSON gain `overlaps` only while an
  overlap is open, and goal next's JSON gains `overlaps` only when there is one.

## The concurrent-paths policy kind (POLICY-08)

POLICY-08 adds no public type, member or route. Behavior: a `vericairn.estate/concurrent-paths` rule, previously reported
unavailable, is now evaluated; under a refuse-level one, assignment, attempt and run writes that leave an unmet or unavailable pair
are refused (`assignment.concurrent-paths`, `attempt.concurrent-paths`). Its findings are never a release hold (`releaseHold` false,
not counted in `holds`), so they never refuse a release check or submission. `PolicyTargetDto.Kind` takes the new value `pair`, and
a `policy-exception` target the new kind `pair` with `assignments`; a format-3 reader that predates 0.9.4 refuses such an exception.

## The dispatcher and the dispatch scope (ID-07)

From 0.9.4 `Scopes.Dispatch` (`dispatch`) is a known scope name in the Records package, and `Scopes.Known` lists it. An authority
record of that scope stays within record format 3, with no new field: an older reader reports its bound as errors (`body.enum`,
`authority.bounds`) and a dispatcher's assignment as `assignment.author`, so readers upgrade before an estate grants dispatch.

## Role closings (ID-14)

From 0.9.4 (ID-14), `HistoryCheckpointResponse` adds `byRemovedRoles`, for adoption: the records by a person whose principal record
no longer gives a role it gave at the record's time, each a `HistoryRecordItem` whose `note` names the roles removed since. It is
advisory, as `authorityChanges` is, and `planHash` does not cover it. An older client ignores the member.

A history checkpoint's `closes.kind` takes `role` from 0.9.4 within record format 3; an older reader reports `body.enum` on such a
checkpoint, and the closing closes nothing there, so readers upgrade before an estate writes one.

## What a worker run can reach (CODE-82)

CODE-82 adds, within record format 3, an optional `reach` to `harness-qualification` (`file`: `readable`, `blocked` or `unknown`;
`host`: `reachable`, `blocked` or `unknown`) and an optional `execution.reach` to an agent-run operation (`outsideFile`,
`testHost`, `secretPaths`, `secretKinds`, `secretPathsComplete`). A format-3 reader that predates them refuses a record carrying
one as an unknown key; a format-2 estate refuses them. No API route, request, response, SDK member or MCP tool changes, and the
generic record reader stays `vericairn.records/readers/10`, which 0.9.4 introduces.

Records package (Vericairn.Records), additive: the public static fields `HarnessQualificationType.FileReach` (`readable`, `blocked`,
`unknown`) and `HarnessQualificationType.HostReach` (`reachable`, `blocked`, `unknown`), the values a qualification's and a run's
reach may take. No existing member changes.

## Public retrieval for research runs (RUN-04)

RUN-04 adds, within record format 3, an optional `brief.retrieval` (`"public"`) to `assignment`, an optional `execution.retrieval`
and a top-level `sourcesObserved` to an agent-run operation; validation adds the codes `operation.retrieval` and
`operation.sources-observed`, and the route `public-web` is admitted in an execution's inventory only with `execution.retrieval`. A
format-3 reader that predates them refuses a record carrying one as an unknown key; a format-2 estate refuses them. The generic record
reader stays `vericairn.records/readers/10`, which 0.9.4 introduces.

Runtime package (Vericairn.Runtime), additive: the record `HarnessToolResult(string Id, string Content, bool Error = false)` and the
default interface member `IAgentLauncher.ReadToolResults(string line)`, which returns no results unless a launcher implements it;
`ClaudeCodeLauncher` implements it (Vericairn.Packs.Software, not a Community package). No existing member changes.

## Answering asks with their recommendations in one act (OPS-16)

- Protocol: `OperatorAnswerItem`, `OperatorAnswerCandidatesResponse`, `OperatorAnswerSelection`, `OperatorAnswerRequest` and
  `OperatorAnswerResponse`; `IEstateApi.OperatorAnswerCandidates(string?)`, `OperatorAnswer(OperatorAnswerRequest)` and their
  `Async` counterparts, each with a default implementation that answers `unsupported`, so existing implementers compile and
  behave as before; `CancellationBoundApi` forwards them; the client deadline of `OperatorAnswer` is 15 minutes, as
  `OperatorClose`'s.
- SDK: `VericairnClient.OperatorAnswerCandidates`, `OperatorAnswer` and their `Async` counterparts (`GET` and
  `POST /v1/operator/answers`).
- Records: `OperatorAnswers` (`MaxAsks`, `Statement`, `NotAuthorized`, `RecommendationHash`, `Act`, `Of`, `Answerable`,
  `DecisionBody`); the decision field `act`.
- Runtime: `EstateService.OperatorAnswer(OperatorAnswerRequest, CancellationToken)`.
- Record format 3: the decision's optional `act` member is additive within format 3. A reader before 0.9.4 refuses a decision that
  carries it, since a decision admits no other member, so an estate records an act only after every writer upgrades to 0.9.4; a
  format-2 decision cannot carry it.

## Signing every ready item in one reviewed act (OPS-24)

- Protocol: `OperatorActItem` (with `Choose(choice, reason)`, `IsAcceptance` and `Reviews`), `OperatorActReview`, `OperatorActListResponse`, `OperatorActChoice`, `OperatorActRequest`,
  `OperatorActReceipt` and `OperatorActResponse`; `IEstateApi.OperatorActItems(string?)`, `OperatorAct(OperatorActRequest)` and their
  `Async` counterparts, each with a default implementation that answers `unsupported`; `CancellationBoundApi` forwards them; the client
  deadline of `OperatorAct` is 15 minutes.
- SDK: `VericairnClient.OperatorActItems`, `OperatorAct` and their `Async` counterparts (`GET` and `POST /v1/operator/act`).
- Records: `OperatorActs` (`Kinds`, `Choices`, `AcceptanceStatement`, `Acceptance`, `RequestClose`).
- Runtime: `EstateService.OperatorAct(OperatorActRequest, CancellationToken)`.
- Presence sessions: the closed action vocabulary of the terms (`vericairn.presence-session/1`) adds `operator act`, in the runtime
  and the enclave helper together (decision `01M8001M0T9XRGF7SKHW3B45KF`); the protocol's format is unchanged, and a helper before
  0.9.4 refuses terms naming it with `terms.not-allowed`.
- Record format 3: no new record type or field; the act writes acceptances, decisions (with OPS-16's `act`) and request closes as
  their types define them.

## Optional operator requests as possible todos (OPS-22)

- Records: `OperatorRequests.IsOptional`, `OperatorRequests.Guide`, `OperatorRequests.AppendPossibleTodos`, and
  `OperatorRequests.Waiting.Optional` (an init-only property with a default, so `Waiting`'s positional constructor and deconstruction
  are unchanged); the operator-request field `optional`.
- Presentation (not a package): `TodoRow` and `EstateView.PossibleTodos`.
- CLI: `vericairn operator fetch <request> --to <folder>`.
- Behavior: a request pinning a file the estate's objects do not hold is refused when it is recorded; `OperatorRequests.WaitingOf`'s
  `Open` no longer holds optional requests, which `Optional` holds.
- Record format 3: the operator-request's optional `optional` boolean is additive within format 3. A reader before 0.9.4 refuses a
  request carrying it, since a request admits no other member, so an estate marks requests optional only after every writer upgrades.

## An automated observer that stops reporting becomes attention (OPS-20)

API version 1 adds four operations: `GET /v1/observers`, `POST /v1/observers/declare`, `POST /v1/observers/report` and
`POST /v1/observers/withdraw`. `IEstateApi` adds `Observers()`, `ObserverDeclare(ObserverDeclareRequest)`,
`ObserverReport(ObserverReportRequest)` and `ObserverWithdraw(ObserverWithdrawRequest)`, with `ObserversAsync(cancellationToken)`,
`ObserverDeclareAsync`, `ObserverReportAsync` and `ObserverWithdrawAsync`; each synchronous member has a default implementation
that throws `NotSupportedException`, so existing implementers stay source-compatible. `EstateService`, `CancellationBoundApi` and
the SDK's `VericairnClient` implement all eight; the SDK treats the three writes as writes, with a write's deadline. The protocol
adds `ObserverDeclareRequest` (`by`, `name`, `kind`, `intervalSeconds`, `graceSeconds`, `summary`), `ObserverReportRequest`
(`by`, `name`, `outcome`, `reason`, `at`, `detail`), `ObserverWithdrawRequest` (`by`, `name`, `reason`), `ObserverReport` (`at`,
`outcome`, `reason`, `by`, `detail`, `receivedAt`), `ObserverRow` (`name`, `kind`, `intervalSeconds`, `graceSeconds`, `summary`,
`declaredAt`, `declaredBy`, `lastReport`, `state`, `expectedBy`, `attention`, `why`) and `ObserversResponse` (`ok`, `message`,
`asOf`, `observers`, `problem`). `AttentionGet` gains notices of kind `failed check` with subject type `observer` (and `observers`
for an unreadable declaration file), for the operator only. The CLI adds `observers list`, `declare`, `report` and `withdraw`, and
human `status` ends with `Observers (this machine):` lines when any observer is declared; `status --json` is unchanged. No record
type, field or record format changes, so a reader of record format 3 from 0.9.3 reads every record as before; the declarations
live in `observers.json` beside the clone's live store under the Vericairn home.

## A timed stop drill before unattended runs (OPS-21)

API version 1 adds one operation, `POST /v1/runs/drill`. `IEstateApi` adds `StopDrill(StopDrillRequest)` and
`StopDrillAsync(request, cancellationToken)`, the synchronous member with a default implementation that throws
`NotSupportedException`, so existing implementers stay source-compatible; `EstateService`, `CancellationBoundApi` and the SDK's
`VericairnClient` implement both, and the SDK treats it as a write with a write's deadline. The protocol adds `StopDrillRequest`
(`by`, `graceSeconds`, `limitSeconds`) and `StopDrillResponse` (`ok`, `message`, `passed`, `requestedAt`, `containedAt`,
`stopToContainedMilliseconds`, `lastWriteAt`, `stopToLastWriteMilliseconds`, `graceSeconds`, `limitSeconds`, `verification`,
`problem`). The CLI adds `worker drill`. The drill writes an ordinary verification record (method `stop-drill`, command
`vericairn worker drill`, role `automation`, environment `device` and `platform`, the runtime build's source commit), whose
fields all exist in record format 1, so a reader of record format 3 from 0.9.3 reads it as any verification; a failed one is
required attention under the existing verification rule. No record type, field or format changes. The vericairn executable gains a
private argument that runs the drill's scratch run, as it has one for the worker's reach probe.

## Progress kept at a target (WATCH-06)

- Runtime: `IProgressTargetAdapter` (contract `vericairn.adapter.progress-target/1`: `Adapter`, `Descriptor`, `Locate`, `Create`,
  `Replace`, `Comment`), the records `ProgressTargetSpec`, `ProgressTargetPlace` and `ProgressTargetDestination`, and on
  `ProviderRegistry` the overloads `Register(IProgressTargetAdapter)` and `RegisterIfAbsent(IProgressTargetAdapter)`, `ProgressTarget(string)`
  and `ProgressTargetAdapters`. All are additions; no existing member changes.
- Records: `ProgressReport` is now a partial class, which changes no member; the target composition is internal.
- GitHub provider (not a package): `GitHubDiscussionsTarget`, adapter id `github-discussions`.
- CLI: `vericairn progress target set`, `list`, `remove`, `refresh` and `follow`, and the JSON forms `vericairn.progress-targets/1`
  and `vericairn.progress-target-outcome/1`.
- Records and record format 3: no record type or field is added; a target's configuration and state are machine-local files
  (`progress-targets.json`, `progress-targets-state.json`) beside the live store, which older builds ignore.
- API v1, SDK and MCP: no operation is added or changed.

## An estate upgrade has a restore point and a stated way back (FIX-145)

No route or interface member is added. `UpgradeRequest` gains the init property `OneWay` (bool, default false) and `UpgradeResponse`
the init property `Return` (`UpgradeReturn`, omitted from JSON when null, so an older runtime's response lacks it); both positional
records keep their constructors and deconstructions. The protocol adds `UpgradeReturn` (`PreviousPin`, `Readability`, `Reasons`,
`Steps`, the computed `OneWay` and the init property `RestorePoint`) and `UpgradeRestorePoint` (`Id`, `CreatedAt`, `Commit`, `Clean`,
`Directory`, `StateHash`, `Files`). The runtime adds `UpgradeReturns` (`Check`, `RestorePointFormat`, `RestorePointsDirectory`) and
the records `UpgradeReadability` and `RestorePoint` in `Vericairn.Runtime.Upgrade`. Behavior changes: an upgrade that moves the record
format, or adds a manifest field or record kind the previous release predates, is refused without `oneWay`, where 0.9.3 applied it;
every applied upgrade first writes a restore point under the clone's state directory, and its migration record's `changes` carries
one more line naming the restore point's id, commit, inventory hash and the readability verdict. The CLI's `estate upgrade` adds
`--one-way`, and its output ends with the verdict, the restore point and the numbered way back. No record type, field or format
changes: the extra line is an item of the migration record's existing `changes` array, which every reader of record format 1 to 3 reads.

## Status judges the estate once for each read (FIX-136)

No route, interface member, protocol type or record changes, and every answer stays as it was: status, its JSON and the views it
reads give the same output for the same records, apart from the policy implementation digest, which names the runtime's build as
it does in every release, and the basis digests computed from it. Opening the runtime judges the estate once for both of its
startup passes, the settlement recovery and the reconciliation of settled runs' process warnings, and judges it again for the
second pass only when the first stored a settlement; finding a goal's work, the work an assignment's flow step is checked
against, reads an index of the work orders' inputs built once for a judged estate; a duplicated operation identity is found from
one pass over the current operations; a recurring duty's proof reads the reviews of the result it examines; and the living
documents' states are derived once for a judged estate. Opening the runtime and reading status on a copy of Vericairn's own estate
(9,778 records) took 11.5 seconds before this change and 7.0 after, on an Apple silicon Mac mini.

`LivingDocuments.Derive` therefore returns the same read-only list on every call for one `EstateModel`, where it returned a new
list each time, and each state's `Reasons`, `Unavailable` and `Notes` are read-only copies; a caller that changed what it was given
can no longer do so, and another judgment of the estate, such as the next `EstateSession.Load`, derives its states afresh. A cost
test at 10,000 generated records, run on each platform beside the suites when `VERICAIRN_COST_TESTS` is 1, and a test in the
ordinary suite on generated estates of 40 and 200 work orders count the passes of one status read rather than time them: the
estate is judged twice, once by the opening and once by status, the living documents are derived once, and the whole work orders
and operations are read as often whatever the estate's size. Before this change the same read judged the estate three times,
derived the living documents three times and read the work orders 846 and 4,046 times and the operations 65 and 225 times on those
two estates. The cost test writes its times to its output and bounds none: no ratio to another measurement both separated the two
codes and held on every platform.

## One judgment per command, and each signature verified once per process (FIX-148)

No route, interface member, protocol type or record changes, and every answer stays as it was, apart from the policy
implementation digest, which names the runtime's build as it does in every release, and the basis digests computed from it.
From 0.9.4 a command the command line runs without a daemon judges the estate once: the runtime it opens keeps the judgment of
its opening for the command's later reads while the record files and the manifest keep their paths, sizes and modification
times and the runtime has written nothing. Every judgment made while the runtime holds the working tree's write lock is fresh,
whoever asks for it, and taking the lock, storing or removing a record and storing or removing an object end the kept judgment.
The daemon, `mcp serve`, Workbench and a runtime that supervises a worker run judge at every call, as before.

A signature that verified is not verified again in the same process: its verdict is kept, keyed by the exact public key, content
hash and signature, and so is each public key the platform imported. A signature that does not verify, or a key the platform
refuses, is asked again at the next judgment. Only the estate's own verdicts are kept; signed requests and presence answers are
verified each time. Nothing is kept on disk or shared with another process, and admissions, revocations, closings and rotations
are judged at every judgment, so a revocation another process records counts from the next judgment. On a copy of Vericairn's
own estate (11,181 records) `goal show` took 4.8 seconds before this change and 3.3 after, `goal` 6.3 and 3.3, `status` 11.6
and 7.4, on an Apple silicon Mac mini; each of those commands now judges the estate once and verifies each of its 11,043
signatures once.

## Human status, attention and work brief derive once per request (FIX-152)

No route, interface member, protocol type or record changes, and every answer stays as it was. `vericairn status` without
`--json` asks the runtime for status, the communication settings and the required attention; from 0.9.4 a command the command
line runs without a daemon derives the repository checks (the walk of the working tree and the writing scan), the policy sections
and the progress projection once for those three calls, where attention derived them again. The derivation reads the working tree
and the clock, so it is never kept beyond the command: a credential-shaped file added, or an occurrence coming due, between two
commands shows in the second. Each request a daemon serves derives its own, as before. `work brief` and the other context reads
(`context prepare`, `context read`, hand-over and resume) read the estate through the runtime's parse cache instead of parsing every
record a second time, and `GET /v1/health` answers from what the runtime set when it opened, without waiting for another call. On a
copy of Vericairn's own estate (11,181 records) human `status` took 21.6 seconds with the pinned 0.9.3 and 11.6 with 0.9.3's
successor before this release's changes, and now 6.4 seconds; it stays within the runtime's 30-second request limit, which is not
raised.

A read the command line makes that stops at its request limit (`vericairn status`, human or `--json`, at its 30 seconds, and every
other read at its own) still exits `124` with "error: The request timed out." on standard error and nothing on standard output.
From 0.9.4 that diagnostic goes on to name the read, its limit, what the runtime serving it last reported it was judging (judging
the estate, running the repository checks, evaluating the estate's policies, deriving progress or the required attention, or
rendering status; a daemon reports nothing to its client, and the diagnostic says that the runtime did not report it), and the
lighter reads to use meanwhile, `vericairn operator list`, `vericairn goal` and `vericairn estate check`; for example:

```text
error: The request timed out. The status read stopped at its 30-second request limit while the runtime was running the repository checks (the working tree walk and the writing scan). Lighter reads to use meanwhile: vericairn operator list, vericairn goal and vericairn estate check.
```

A write's timeout, a cancellation and a timeout that was not the read's own limit keep the diagnostic as it was. `ClientFailure`,
its message (which MCP's protocol error and Workbench's status show), the exit codes, the limits and the generated client-outcomes
reference are unchanged, and no public member changes.

## Progress counts a release's placed work (FIX-149)

No route, field or record changes. In progress's "What comes next, by release", and in its JSON's `next.releases`, a release
carrier's `carries` and `accepted` now count the current work orders placed in it, the work orders whose `placement` (record
format 3) names it as their release carrier, never the carrier itself, where they counted its input work orders; a carrier in
which no work order is placed carries its input work orders as before, so an estate that plans releases by inputs reads as it
did. On Vericairn's own estate REL-094 and REL-095 read "0 work orders, 0 accepted" before this change, though work was placed in
both. The text's note says so: "the work orders it carries: those placed in it, or, when none is placed in it, its input work
orders". The placements are read from one index of the work orders by carrier, built once for a judged estate. A goal with an
acceptance no rule refuses reads achieved, and a work order whose own revision is accepted reads accepted, whatever a later
goal-status or work-status says, in the goal list, status and progress, as the product already derived them; tests over records
shaped like Vericairn's own estate now hold that, and no record is rewritten.

## An acceptance of a work order's current revision accepts its current result (FIX-141)

No route, interface member, protocol type or record changes. A result's state, which `WorkView.ResultState` gives and status's
assignments, the progress report, goal views, next eligible work, required attention, Workbench's result inbox and Remote read,
is now also `accepted` for a work order's current result (the newest current result of any of its assignments, the one
`operator close` lists) when the newest current acceptance of the work order's revisions names its current revision, or one that
differs from it only in placement; was recorded after that result; was recorded before acceptances named a result's exact hash,
which the records date at the estate's first acceptance of a result whose author held the operator role at its place; and was
given by a principal who could, at its place, have accepted that result directly by the rule acceptances of results are judged by
(the operator; the result's author under `self-accept-ordinary-work`; another principal under `accept-work`; a milestone's result
only the operator). This is decision 01M7ETM7BC7KE97EE8ETYJM1E6's acceptance "recorded before acceptances named a result's exact
hash". `GET /v1/operator/close`, `IEstateApi.OperatorCloseCandidates` and `operator close` no longer list such a result, so an
`--all` selection, which pins the complete listing, never signs a second acceptance of work already accepted. A result recorded
after the acceptance, any result other than the work order's current one, a result whose work order's acceptance names an earlier
revision, is superseded, came after the first operator acceptance of a result or was given by a principal who could not accept
the result read as before, `awaiting review` or `accepted (earlier revision)`, and stay listed.

A work order's derived state (`EstateStatus.WorkItems`, `EstateStatus.StatusRecord`) now also reads only current acceptances: one
superseded by another acceptance, which may name another subject, no longer makes a work order read `accepted` or `accepted
(earlier revision)`. Vericairn's own estate has no such acceptance whose reading changes.

This changes a derived reading of existing estates: in Vericairn's own estate the 29 work orders accepted in October 2026 at their
current revisions, after their results and before the operator's first acceptance of a result (November 2, by `operator close`),
leave the listing, and their assignments read `accepted` in the canonical status JSON, which changes with them, and with it a new
estate observation's `statusHash`; observations already recorded keep their facts. DESIGN-20, whose current result was recorded
after the operator's acceptance of it, stays listed ([operator actions](operator-actions.md#close-finished-steps-in-one-action)).

## The record reference spells out a brief's members (DOCS-46)

`FieldDescription` (Vericairn.Records) gains three init-only properties, `EmptyAllowed` (`bool?`, for an array whether it may
be empty), `Limits` (`string?`, the value's limits in words) and `Members` (`IReadOnlyList<FieldDescription>`, empty unless the
field is an object of fixed members). Its positional constructor and deconstruction are unchanged, so existing callers and
implementations compile and behave as before; record equality now also compares the three properties. `records describe --json`
adds `members` to a field that has them, each member with `emptyAllowed` and `limits` where they apply. The record reference's
input (`inputs/record-types.json`) adds the same `members`; the record-reference companion renders an input without them as
before.

## Tool annotations and the published tool definitions (MCP-07)

All additive; every Community package keeps 0.9.3's API. No tool's behavior changes.

- `Vericairn.Mcp`: `ToolDefinition.Destructive` and `Idempotent`, init-only, so its constructor and deconstruction are unchanged;
  `ToolDefinition.Describe()` adds `annotations`; `ToolRegistry.Definitions()`, every tool the build defines, ordered by name, as
  `tools/list` describes it over standard input and output; `ToolRegistry.DefinitionsHash`, the SHA-256 of that array in RFC 8785
  canonical JSON, and `ToolRegistry.DefinitionHash(definition)`; and `McpServer.ToolDefinitionsMeta` (`vericairn/toolDefinitions`).
- MCP: each tool `tools/list` gives carries `annotations` with `readOnlyHint` (true for a read), `destructiveHint` and
  `idempotentHint`. Every read is read-only and idempotent; the destructive writes are `policy_revise`, `policy_retire`,
  `policy_revoke_exception`, `writing_exception_remove`, `prompt_save` and `prompt_preference`, and the idempotent writes
  `policy_declare`, `policy_revise`, `policy_retire`, `policy_revoke_exception` and `writing_exception_remove`. Every definition's
  bytes therefore differ from 0.9.3's. `initialize`'s result carries `vericairn/toolDefinitions` in `_meta`, `{count, hash}`,
  computed once per process, and the server answers `initialize` before it reads the requests sent with it.
- Build tooling (not shipped): `vericairn-docs mcp-tools` prints every tool's hints and definition SHA-256 with the set's SHA-256,
  and the package workflow appends it to the draft release's notes.

## A checked push refuses the estate's blocked terms in the pushed commits' messages (CODE-81)

- **Vericairn.Runtime**: a new public record `PushedCommit(string Commit, string Message)`; `IPushAdapter.CommitMessages(string
  directory, PushPlan plan)`, the commits a push carries that its target branch does not hold, with a default that reads nothing
  and returns null, so an adapter built against 0.9.3 keeps working and its pushes are refused only while the estate declares a
  blocked term or a required trailer; `PullRequestObservation.Title` and `PullRequestObservation.Body`, init-only properties beside
  the unchanged positional parameters; `IPullRequestProvider.Merge(string destination, string number, string head, string
  message)`, whose default merges as the three-argument `Merge`; on `MemoryPullRequestProvider`, that overload and `MergeMessage`;
  `IRecordCommitAdapter.Commit` with a seventh parameter, the trailer lines, whose default refuses a non-empty list and otherwise
  commits as before.
- **Vericairn.Providers.GitHub** and **Vericairn.Adapters.Git** (neither is a package): `GitHubPullRequestProvider` reports the
  pull request's title and description and implements the four-argument `Merge`, which passes `commit_title` and
  `commit_message`; `GitPushAdapter.CommitMessages` and the seven-argument `GitRecordCommitAdapter.Commit`.
- **Vericairn.Records**: no public member is added or changed.
- **Policy kinds**: `vericairn.estate/commit-messages`, version 1, selections `estate`, appears in the policy kinds listing.
- **Record format 3**: unchanged. The new rule kind lives in an existing estate policy's `rules`; a reader before 0.9.4 keeps it
  intact and reports it unavailable. A push or merge records its message check as one more `evidence` entry of its operation.

## A disclosure hold ends where the release procedure says it does (FIX-137)

- **Vericairn.Runtime**: `IPushAdapter.FileText(string directory, PushPlan plan, string commit, string path)` and
  `IPushAdapter.ChangedPaths(string directory, PushPlan plan)` are new members of the push adapter contract, with defaults that
  read nothing and return null, so an adapter built against 0.9.3 keeps working and never lets a held work order's text pass with
  a documentation set (FIX-137). The shipped git adapter implements both: files only at the plan's head or target head, by a
  normalized repository-relative path, without the clone's filters; each carried commit's paths exactly as git names them,
  without renames, external diffs or text conversion, a merge commit naming only paths that differ from every parent.
- **Vericairn.Records**: no public member is added or changed; `DisclosureHolds.Active` and `Disclosed` read as before.
- **Record format 3**: unchanged. The approval is read from existing asks and decisions; no field or type is added.

## Every file a release's SHA256SUMS names is attached (FIX-140)

All additive; every Community package keeps 0.9.3's API. No route, protocol type, record type or field changes.

- `Vericairn.Runtime.Providers.ReleaseObservation.ChecksumNames`, an init-only property outside its positional parameters, so its
  constructor and deconstruction are unchanged: every name the release's checksum file lists, in order, or null when the provider
  did not read the checksum file, as a provider written before 0.9.4 does not. `IReleaseProvider.ObserveRelease` keeps its signature
  and from 0.9.4 reports them.
- `Vericairn.Runtime.Release.ReleaseProfile.Unattached(release, checksums)`: the listed files a draft does not attach, in ordinal
  order; empty when the provider reported no names.
- Behaviors: `ReleaseFlow.AfterObservation` counts a draft complete only when it attaches every file its checksum file names. Once
  the run has succeeded, a draft that does not is a known failure whose summary names each missing file, and no candidate is
  recorded; while the run is in progress, or none is identified, the observation waits and its pending message names them. The
  observe response for a known failure carries the observation's summary, `release submit --check`'s `release.identity` objection
  quotes it until a retry gives a reconciliation, and a candidate's evidence says every listed file is attached. The release
  workflow attaches `vericairn-<version>-companions.zip` and fails a draft that lacks a listed file; releases up to 0.9.3 keep that
  archive only in the retained CI artifact.

## A checked push covers every commit its target does not hold (FIX-154)

No route, interface member of `IEstateApi` or protocol type is added. `POST /v1/changes/push` (`changes push`) keeps
`ChangesPushRequest` and `ChangeResponse`. Its scan, commit-message check and disclosure hold now read every commit the push carries
that the target does not hold: from the head of the newest complete recorded push of the same repository and branch that the remote
branch still holds, otherwise from the merge base of the new head with the target's observed head; before, an existing branch's scan
began at the remote branch's head. A push to the declared target branch is refused before anything is recorded, its `problem.code`
`change.target-push`; before, it could fast-forward the target. `POST /v1/changes/merge` (`changes merge`) now takes only a complete
push of the exact head, and refuses a head whose only recorded push was not complete. A head pushed again after such a push is
recorded under its own operation identity, the earlier identity followed by `:after:` and the earlier push's record id
([changes](changes.md#coverage-094)).

No record type is added. A `change-push` operation adds, from its dispatch on, the target fact `basis`: `merge-base`, or the
record id of the observed `change-push` of the same repository, destination, branch and target that it continued, whose head is
its `base`. Validation refuses a basis on another action, any other value, and a continued push of another repository,
destination, branch or target, or whose head is not the revision's `base` where it records one, with `operation.change-basis` at
severity `error`. A recorded `basis` is never changed or dropped: a push refused before its dispatch records none, so its retry
records the basis it finds, while a retry of a push refused after its dispatch keeps the basis it recorded and is refused,
pushing nothing, where the remote branch no longer holds the push it continued. So a reader before 0.9.4, whose rule lets a retry
change only `base`, `remoteHead` and `targetHead`, keeps `basis` as one more string target fact and ignores it, on every push and
retry; its merge gate still takes any observed push. Only a push scanned against the declaration's current target counts; after
the operator moves the declaration, earlier pushes of a branch count for nothing. Pushes recorded before 0.9.4 keep their reading:
the runtime derives whether each is complete from its `remoteHead` and `base`. Record format remains 3.

`Vericairn.Runtime` adds to `IPushAdapter`, each with a default implementation: `Prepare(directory, remote, branch, target, head,
recorded)` and `Push(directory, remote, plan, scan, recorded)`, where `recorded` holds the heads of the branch's complete recorded
pushes, newest first, at most sixteen. The runtime calls only these. Their defaults refuse with `ProviderRefusedException`, so an
adapter written before 0.9.4 takes no checked push rather than choosing a base of its own; the existing `Prepare` and `Push` stay,
unchanged in signature. `Vericairn.Adapters.Git`'s `GitPushAdapter` implements both overloads; its existing `Prepare` and `Push`
now scan from the merge base with the target whatever the remote branch holds, and every form refuses a branch that is its target.
`PushPlan` and `SecretScan` are unchanged.

## A document.drift finding names both digests (FIX-157)

No public API change: no route, protocol type, record type or field, or public member changes. `estate check`'s `document.drift`
error keeps its code, location, severity and guidance; its message now labels both values, "The revision pinned <digest>; the file
now has <digest>.", where it showed only the file's current digest after "pinned it", and for an authored-content pin (`hashMode`
`authored-revisions-v1`) it adds "Both digests cover the authored content, not the generated revision history." A script that
matched the old message must match the new one; the code is the stable thing to match ([living documents](living-documents.md)).

## No record counts because its refuser is unread (FIX-159)

No route, request, response shape, protocol type, record field, record type or public member changes. Behaviors: the estate check
reports two new findings, `record.duplicate-refused` (warning) and `record.duplicate-unsettled` (error), only while records are unread;
`GoalDue` (and `GET /v1/goals/{id}/due`) returns the problem form instead of null (404) for a goal whose own line is unread;
`StandingAgents.RunRefusals` adds an `operation.standing` refusal for the hold while records are unread; release submission, run start
and preparation, a standing goal step, record writes, checkpoint writes, record preparation for a device and pairing-code issuance
return a `refused` problem naming the errors as described above. `EstateStatus.ReconcileCodes` is unchanged.

## A release basis past the object limit is retained as a split object (FIX-163)

`Vericairn.Records` adds the content-addressed split object in `Vericairn.Records.Estate`: the static class `SplitObject`
(`Format`, `vericairn.split-object/1`; `MaxRootBytes`, `Plan`, `Write`, `IsRoot`, `Read` and `Join`), the interface
`ISplitObjectSource`, which measures an object before it is read and takes a cancellation token, the records `SplitObjectPlan`, `SplitObjectContent`
(with `Split`) and `SplitObjectProblem`, and the enum `SplitObjectFault`. These are additions; no member changes. A reader
states the longest content it accepts, with no default; it bounds the root, ties the count to the length and measures every
part before it reads any.
Release submission retains a basis past the object limit as a split object, up to 256 MiB, and names its root in the
operation's evidence; `release.basis-size` refuses a longer basis. The release-basis read returns the response it gives for
the basis held in one object, under the root's hash. The estate check adds `objects.split` for a canonical root whose parts
are missing or of the wrong length, measuring each distinct part once and reading none. No record field, record type or format number changes, and
`vericairn.release-basis/3` is unchanged. A 0.9.3 estate check passes an estate holding split objects; 0.9.3's
`release basis` reports a root as `No supported retained release basis has this hash` and exits 2, its API answers HTTP 404
and its SDK null.

## Git on a repository Vericairn did not create starts no program its configuration names (FIX-147)

No route, interface member, protocol type, record type or field changes, and no public member of a package is added or changed. The
new types are internal to `Vericairn.Adapters.Git`, which is not a package: `GitHardening`, the configuration isolation every git
process on a repository Vericairn did not create starts with, and the record `GitWorkingTree`. Its internal
`GitFileSource.StartInfo` and `GitFileSource.RunIsolated` take a `GitHardening`, and its internal unhardened
`GitFileSource.Run(clone, …)` overloads are removed. Behavior changes of the adapter's public classes, which the `vericairn` tool
carries: `GitCommandAdapter.Inspect` refuses, with `ProviderRefusedException`, a working tree git cannot read completely, which it
reported clean, a repository another account owns, with a message naming the ownership, and any repository when no temporary
directory can be created for the isolation; `IsRepository` refuses in that last case; `InspectRelease` answers null in each. Both
inspections read the working tree through a git directory Vericairn writes, carry ten of the repository's own settings to that read,
and count a submodule by its checked-out commit only, where `InspectRelease` also read submodule working trees. Every
`GitPushAdapter` method that reads the clone refuses with `ProviderRefusedException` when no temporary directory can be created for
the read; `GitleaksScanner`'s history reads pass `--submodule=short`; the overlap reads answer null when they cannot run isolated;
`GitRecordCommitAdapter` turns off the estate repository's fsmonitor and untracked cache on every call and adds its intent-to-add
entries with no hooks. No record, protocol or estate format changes.

## The secret scan reads only what it writes and the clone's history (FIX-160)

No route, interface member, protocol type, record type or field changes, and no public member is added or changed:
`GitleaksScanner`, in `Vericairn.Adapters.Git`, keeps `Scan(string directory, PushPlan plan)`, and a recorded scan keeps its tool,
version, range, verdict and count. Every process of a scan, gitleaks and git, runs in an empty folder the scan creates in the
temporary directory, which is gitleaks' source; after one `git -C <clone> rev-parse --absolute-git-dir`, git reads the clone's git
directory through `GIT_DIR`, with that folder as its work tree, so no file of the clone's working tree is read. Every inherited
`GIT_` and `GITLEAKS_` variable is dropped before the scan's own settings are set, and no process fetches a missing object. A clone
whose git directory git cannot name as an existing absolute path scans `incomplete`, and so does a range that meets a missing
object, which is no longer fetched; either refuses the push, as an unreadable range did. The internal `GitleaksScanner.Isolate`
takes the scan's `GitHardening` (FIX-147).

## Programs are started by their resolved absolute path (FIX-161)

No route, interface member, protocol type, record type or field changes, and no public member is added or changed. `ProgramPath`,
the one resolver every process start uses, and `ProgramNotFoundException`, a `Win32Exception` with native error 2, are new internal
types of `Vericairn.Records` (namespace `Vericairn.Records.Processes`), whose internals also become visible to
`Vericairn.Adapters.Git` and `Vericairn.Packs.Software`, by simple name as the existing entries are. A program that cannot be
resolved fails with that exception before anything starts, as a start that cannot find its file did. Behavior changes of public
members: `WorkerConfigurationStore.WriteSetup` (`Vericairn.Runtime`) refuses, with `ProviderRefusedException`, an executable that
can never be started (one naming nothing or a directory, and on Windows one rooted only on the current drive or relative to a
drive's current directory), and `ReadSetup` refuses, and `ListSetup` lists with its problem, a stored setup with such an executable;
`SecretServiceKeyStore` (`Vericairn.Records`) finds `secret-tool` only in an absolute directory of `PATH`, where it also accepted
relative entries; `SecureEnclaveKeyStore` and `ServiceInstaller` (`Vericairn.Daemon`) start their programs by the resolved path.
Every start resolves a bare name to the first absolute directory of `PATH` that holds it as a program (on Linux and macOS a file
with an execute permission; on Windows with `.exe` or `.com`, in `PATHEXT`'s order, `.exe` always), uses an absolute path as given,
and refuses a relative path, except a workspace setup command's relative path with a directory part, which is read against the
workspace. On Windows the containment primitive passes the resolved path to `CreateProcessW` as the application name. A started
program receives `PATH` without its empty and relative entries when its environment holds this process's own `PATH`, and none when
no absolute entry is left; a `PATH` a caller sets is passed as given. A launch binding's executable and MCP server hashes are of the
files that lookup finds. A configured relative program path that used to start, read against the current directory, is now refused.
No record, record format or wire value changes.

## Windows batch programs start through cmd.exe (FIX-165)

No route, interface member, protocol type, record type or field changes, and no public member is added or changed.
`BatchArgumentRefusedException`, a `Win32Exception` with native error 160, is a new internal type of `Vericairn.Records`
(namespace `Vericairn.Records.Processes`), thrown before anything starts for a batch program whose path or arguments `cmd.exe`
cannot read as data, so every caller that handles a start that fails handles it. On Windows a program whose extension is `.cmd`
or `.bat` is no longer passed to `CreateProcessW` as the application name: the containment primitive and every other start pass
`cmd.exe` from the system directory, with `/d /e:on /v:off /s /c` and the script's quoted path and arguments. Every program
started on Windows, by either start, receives `NoDefaultCurrentDirectoryInExePath=1` in its environment, replacing any spelling
or value of that variable the environment held, as it receives `PATH` without empty and relative entries. An absolute Windows
path without an extension resolves to the `.exe`, else the `.com`, of that name in its folder, where FIX-161 used it as given
and the start failed; one whose file name ends with a dot or a space, or that names a stream of a file, is refused with
`ProgramNotFoundException`. Behavior change of public members: on Windows `WorkerConfigurationStore.WriteSetup`
(`Vericairn.Runtime`) refuses, with `ProviderRefusedException`, a batch setup command with an argument holding a percent sign or
a control character other than a tab, and `ReadSetup` refuses, and `ListSetup` lists with its problem, a stored one. A launch
binding's executable hash is of the script for a batch program, as before, and of the `.exe` or `.com` an absolute path without
an extension resolves to. No record, record format or wire value changes.

## Operations, the OpenAPI document, MCP and the record reader

`IEstateApi` now has 170 synchronous operations, each with a cancellable counterpart, where 0.9.3 had 151. Nineteen are added, each
with a default implementation: `Presence`, `PresenceRegister`, `PresenceHeartbeat` and `PresenceRelease` (ID-11), `GoalDue`
(GOAL-07), `StandingOutput` (GOAL-08), `GoalDecide` (PROC-19), `GoalClose` (FIX-143), `WorkOverlap` and `WorkOverlapPreview`
(CODE-73), `OperatorAnswer` and `OperatorAnswerCandidates` (OPS-16), `OperatorAct` and `OperatorActItems` (OPS-24), `Observers`,
`ObserverDeclare`, `ObserverReport` and `ObserverWithdraw` (OPS-20), and `StopDrill` (OPS-21). None is removed or changes its
signature.

The OpenAPI document has 175 operations where 0.9.3's had 156, adding those operations' routes: `GET` and `POST /v1/presence`,
`POST /v1/presence/{lease}/heartbeat`, `DELETE /v1/presence/{lease}`, `GET /v1/goals/{id}/due`, `POST /v1/standing/outputs`,
`POST /v1/goals/{id}/decide`, `POST /v1/goals/{id}/close`, `GET /v1/overlaps`, `POST /v1/overlaps/preview`, `GET` and
`POST /v1/operator/answers`, `GET` and `POST /v1/operator/act`, `GET /v1/observers`, `POST /v1/observers/declare`,
`POST /v1/observers/report`, `POST /v1/observers/withdraw` and `POST /v1/runs/drill`. Its schemas go from 178 to 210: the requests
and responses of those operations, and CODE-80's `PresenceSessionNotice` and `PresenceEntryDescription`. Twelve schemas gain
optional properties: `AssignRequest.repository`, and `notices` in `WriteOutcome`, `RunResponse` and `GoalStepResponse` (CODE-73);
`CommunicationProfile.availability`, `quietHours` and `availabilityNow`, and `TakeoverRequest.basis` (GOAL-11);
`GoalStepRequest.occurrence` and `now`, and `GoalActionRequest.stopRuns` (GOAL-07); `GoalStepResponse.decision` (PROC-19);
`WorkNewRequest.fromPossibility` (GOAL-08); `GoalAcceptRequest.reason` and `stepReasons` (FIX-143);
`HistoryCheckpointResponse.byRemovedRoles` (ID-14); and `PresenceNotice.session` and `shortHash`, with `session`, `signed`,
`renewed` and `ended` added to its `kind` (CODE-80). No operation or schema is removed, and no existing operation changes but in its
summary (`POST /v1/presence/prompts`, `POST /v1/operator/close` and `POST /v1/goals/{id}/accept`).

The MCP server defines 63 tools where 0.9.3's defined 57, adding the read tools `goal_due` (GOAL-07) and `work_overlap` (CODE-73)
and the write tools `standing_possibility`, `standing_question` and `standing_publication` (GOAL-08) and `goal_decide` (PROC-19);
none is removed. Every definition gains its annotations (MCP-07), and `initialize` carries the definitions' count and hash.
`resources/list` adds the release's documentation index, `vericairn://docs/llms.txt` (DOCS-38). The server's instructions are
unchanged.

The generic record reader moves from `vericairn.records/readers/8` to `vericairn.records/readers/12`, with new golden fixtures at
each step: a result's `decision` (PROC-19), an assignment's overlap fields (CODE-73), its `occurrence` (GOAL-07) and its routing by
session (GOAL-11). Its explanations carry the record fields and statuses 0.9.4 adds, as the entries above give them, among them
goal-status's `closed` and `unsettled` (FIX-143). The generator id, `vericairn.records/items`, and contract version 1 are unchanged.

## The embedded runtime

No route, interface member, protocol type or record changes. 0.9.4 is still built with SDK 10.0.404 (`global.json`), and the
self-contained distributables embed the .NET and ASP.NET Core 10.0.15 runtimes, unchanged from 0.9.3. The central package versions
move to System.CommandLine 2.0.14, Avalonia 12.1.5 and Microsoft.Data.Sqlite 10.0.14, and the test framework to xunit.v3 4.0.3. The
NuGet packages still target `net10.0` and run on the shared runtimes the installed .NET 10 SDK supplies.
