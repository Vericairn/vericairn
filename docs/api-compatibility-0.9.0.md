# API compatibility in 0.9.0

## Generated document revision histories (DOCS-10)

`DocumentPinRequest` adds init-only `ConvertRevisions` (default false) and nullable
`Decision` properties. Its constructor and deconstructor, routes, response shape,
SDK methods and cancellation bridges are unchanged. OpenAPI describes the optional
JSON fields `convertRevisions` and `decision`. `DocumentPinResponse.Revision` is the
derived ordinal for a converted document. Earlier servers ignore these additions;
clients must use a server that supports conversion before relying on it.

Record format remains 3. `document-revision` adds optional `hashMode`, `decision`
and `revisionHistory` fields. Absent `hashMode` retains whole-file hashing and the
existing succession rules. `authored-revisions-v1` hashes the bytes with the
revision payload removed, retaining both markers, and treats concurrent revisions
as an ordered history. The `revision` field remains present and becomes advisory
in this mode. Conversion metadata binds each preserved line to its original
revision id and record hash. Existing records, signatures and frozen documentation
are unchanged. Readers that ignore optional metadata keep their existing field
shapes; verifying the new hash mode requires a reader that implements it.

`RevisionLines` supplies the shared rendering, conversion and byte selection.
The additive `vericairn.estate/revision-history-v1` template generator renders the
complete history. Existing generator identities and template pins remain valid.
Context document identities add `hashMode` only for pins that declare it; exact
reads verify and return the selected bytes, not a newly rendered history.

## `ISourceAdapter.ReadFileAtHash` with a hash mode (DOCS-10)

`ISourceAdapter` adds `ReadFileAtHash(string root, string relativePath, string hash,
string? hashMode)` with a default implementation. A null mode delegates to the
existing three-argument member; another mode returns null until the adapter
supports it. `GitCommandAdapter` implements both members and verifies the selected
bytes from local history without fetching or changing a checkout. No interface
member is added without a default implementation.

## IAgentLauncher.EfficiencyUpstream (EFF-01)

`Uri EfficiencyUpstream(LaunchContext context)` has a default implementation that
throws `ProviderRefusedException`. A launcher must establish its fixed provider
base and support for a hook before an adapter run's intent. An existing implementer
has no new obligation for disabled runs and cannot silently enable the adapter.

## IAgentLauncher.ConfigureEfficiencyHook (EFF-01)

`LaunchContext ConfigureEfficiencyHook(LaunchContext context, Uri? address)` has
a default implementation that throws `ProviderRefusedException`. Implementers
apply a worker-owned loopback address to isolated run configuration and restore
the direct configuration for null. Both new launcher members have defaults;
none is added without one.

## Efficiency representation, configuration and measurements (EFF-01)

Records adds `RepresentationContract` and `RepresentationBlock` for
`vericairn.representation/1`. Runtime adds the separate `LosslessAdapter` and
`RepresentationReduction`, `EfficiencyAdapterConfiguration`, the worker store's
`ReadAdapter`/`WriteAdapter` operations and `AdapterPresentation.Describe`.
`RunConfiguration.AdapterPermission`, `RunCapabilities.Adapters` and
`LaunchContext.AdapterAddress` are init-only additions with absent defaults;
their positional constructors and deconstructors are unchanged. Absent adapter
permission and capabilities are omitted from JSON, preserving the earlier
serialized shape for unconfigured launchers.

Format-3 agent execution admits `local-lossless` beside the existing `disabled`
choice and requires `adapterAddress` for that choice (a loopback authority, or
null for startup fallback); disabled runs omit it. Its immutable
intent still cannot change at settlement. `executionObserved.adapter` adds
measured state and eligible byte counts, with tokens explicitly unknown. Worker
run rows expose live local measurements through their existing nullable
`ExecutionObserved` member; status adds `efficiencyAdapters` only when recorded
measurements exist, preserving canonical status's record-only contract.
Earlier readers cannot validate this new selection; update the serving runtime
before selecting it. Format-2 runs keep the adapter disabled.

The existing context-original route can also return an optional original from an
adapter run's verified retained context when the working copy no longer matches.
Access, exact revision and byte identity are rechecked; no newer original is
substituted. There is no new record type, API route, MCP tool, exported telemetry,
package dependency or record-format change. See the [adapter declaration](efficiency-adapter.md).

## Settling a run whose observation is marked settled (FIX-76)

`RunSettleRequest` adds an init-only `bool RecordsChecked` property, default false.
Its positional constructor and generated deconstructor are unchanged, and no route,
response contract or interface member changes. OpenAPI declares optional JSON
`recordsChecked` as a boolean on the `RunSettleRequest` schema.

With `recordsChecked: true`, a settle request records the settlement of an exited
run whose retained observation is already marked settled while the estate's records
hold none, from the observation's facts, and the settlement's evidence says so.
Omitted or false keeps the earlier behavior: that settlement stays `unresolved`, and
its message says how to check the estate's branches and copies. Startup recovery
never sets it. The confirmation changes nothing else: an already recorded settlement
is still refused as `already-settled`, and an observation that lacks a fact is still
`unresolved`. The command is `vericairn worker settle <run> --by <principal>
--records-checked`. Earlier servers ignore the field and keep refusing.

## Settling a run whose supervising worker died (FIX-82)

No contract, route, response field or interface member changes. `POST /v1/runs/{id}/stop`,
`POST /v1/runs/{id}/settle` and the runtime's start can now record the settlement of a
run whose exit was never observed, when no worker on the machine that ran it supervises
it, the recorded worker process and the harness's process boundary are gone, and nothing
was observed for longer than the run's stall limit. A process that exists and started by
the launch, or cannot be inspected, counts as running. The settlement is `known-failure`
with an `audit-unverified` warning, an unverified `scope` and evidence saying the exit was
not observed. Its `usage` is what was observed up to the last heartbeat, each unit at most
its cap; a unit not observed is listed in `unreported` and charged at the cap. A stop asked
of the run is recorded with its observed facts, normally `requested`, never `contained`.

A stop's `RunStopResponse.state` still comes from those facts, and its `message` reports
the settlement; a later stop is refused because the run has ended, and a later settle
answers `already-settled`. A run that may still be observed keeps the earlier answers: the
stop is reported as before, with `ok` true, and settle answers `in-flight`, each message
now naming what is missing. Record format remains 3; the settlement uses existing operation
fields. Earlier servers keep such a run in flight, charged at its cap.

## Writes outwait a presence prompt (FIX-78)

`ClientOperation` adds `PresenceWait` (five minutes, the longest a device key waits
for its owner's presence at one signature), `EffectDeadline` (six minutes) and
`Deadline(string operation, bool mayHaveEffect)`. `Deadline(string)` is unchanged
and stays the deadline for an observation that cannot write.

An async operation that may write and has no explicit timeout is now observed for
at least `EffectDeadline` instead of the 30-second or 3-minute default, so a
`records new` that waits on Touch ID is not reported as an unknown outcome while
the prompt is still open. Longer named deadlines, such as 15 minutes for run and
release operations, are unchanged. A timeout after the window still reports an
unknown outcome, and nothing retries. The synchronous `VericairnClient` HTTP
timeout is six minutes instead of five. `VericairnClient.OperationTimeout` and the
local-network timeout argument still override every deadline.

## Records signed by a paired device (REMOTE-03)

`IEstateApi` adds `PrepareRecord(NewRecordRequest)` and `NewSignedRecord(SignedRecordRequest)` with default
implementations that answer `unsupported`, and their cancellable counterparts; `CancellationBoundApi` and
`VericairnClient` implement them. New contracts: `RecordPrepared`, `RecordPrepareResponse` and `SignedRecordRequest`.
The SDK adds `DeviceSignedRecords.WriteAsync`. Routes: `POST /v1/records/prepare` and `POST /v1/records/signed`,
declared in the OpenAPI document. Nothing existing changes: `POST /v1/records` still signs with a device held on the
runtime's machine. Earlier servers answer `404` for the new routes.

## Release lines and channels (CODE-12)

`ReleaseSubmitRequest` adds nullable init-only properties `Line`, `Channel`,
`EndOfSupport` and `BackportOf`. Its positional constructor and deconstructor
stay unchanged. `ReleaseBackportReference(Id, Hash)` carries the exact source
release. The existing API routes, SDK methods and async bridges carry these
properties. OpenAPI and the read-only MCP `release_submit_check` tool declare
the corresponding optional JSON fields. Omitted fields retain existing behavior.

`IReleaseProvider.ManualPublicationCommand(string destination, string tag,
string channel)` is a new overload with a default implementation. For `stable`
it delegates to the original two-argument member; other channels throw
`NotSupportedException` until the provider implements them. The runtime reports
unsupported publication instead of offering a stable command for another channel.
No interface member is added without a default implementation.

The GitHub provider retains its two-argument stable command and implements all
four channels through the overload. The provider prints commands and never runs
publication. Submission preserves the new fields as strings in its existing
operation target; observation carries them into the candidate and its published
revision. The target keys `releaseLine`, `releaseChannel`, `releaseEndOfSupport`,
`releaseBackport` and `releaseBackportHash` are reserved for these fields and
refused as provider settings.

Record format remains 3. Product `lines` and `cadence` and release `line`,
`channel`, `endOfSupport` and `backportOf` are additive fields. A format-2 record
carrying any of them is refused. An older reader that cannot interpret these
fields must be upgraded before they are used. Existing records keep their bytes,
hashes and signatures. `ReleaseLines.Line` and `ReleaseLines.Channel` read the
defaults without changing a body; `ReleaseBackports.Notes` renders recorded
backports from either line.

The release companion implementation advances to `vericairn.records/releases/2`
and the generic record reader to `vericairn.records/readers/4` for the new fields.
Their hashes change. Declarations pinned to earlier implementations require the
ordinary explicit generator update before regeneration; earlier golden fixtures
remain retained.

## Requested progress and the typed claim (WATCH-02)

WATCH-02 adds optional `Progress` and `StatusText` init properties to
`WorkProgressRequest` and `WorkProgressClaim`. Existing constructors and
deconstruction signatures are unchanged. Null additions are omitted during
serialization, preserving the previous wire shape for voluntary notes.
The typed value is a `JsonElement` so every supplied member reaches runtime
validation; unknown or mismatched fields are refused rather than discarded.

The existing `IEstateApi.WorkProgress` and `WorkProgressAsync` operations,
cancellation-bound adapter, and synchronous and asynchronous SDK methods carry
the additions without signature changes. No interface member is added and no
new default implementation is required. The OpenAPI response now describes
`WorkProgressResponse` and `WorkProgressClaim`; the request and response value
schemas match MCP discovery. `ProgressSchemas.Value()` and `StatusText()` return
fresh schema objects for that shared vocabulary.

The generic record reader advances its implementation identity to
`vericairn.records/readers/4`, and the manifest/charter explanation to
`vericairn.estate/manifest/2`: declared field additions change their schema
explanations. Earlier golden fixture versions remain intact. Their generator
ids and contract version 1 remain unchanged.

The records library adds `ProgressRequests.Resolve` and
`ProgressRequestSelection` to resolve a reporting request and retain its source.
`WorkView.RenderBrief` and `RenderBriefJson` gain overloads that accept the
worker's selected goal before its run is recorded; existing overloads remain.
These are additive public members. Request fields and completion fields are
optional, available only in record format 3. The format number and existing
record types do not change. Existing format-2 notes remain valid; an older
runtime cannot be assumed to accept new optional fields. Use a runtime that
implements requested progress when configuring or submitting these fields.

The work brief adds effective request and source fields only when configured,
and an unresolved-request problem only when goal selection is ambiguous.
The `work_progress` MCP tool retains its original required arguments and gains
two optional arguments. `work progress` adds the CLI submission counterpart.
See [the reporting contract](software.md#requested-progress-090) for shapes,
validation and precedence. Broader display and freshness integration belongs
to WATCH-03; the release review response's `RequestedProgress` member remains
unchanged in this step.

## Blocked terms (CODE-17)

No contract changes. A new estate-policy rule kind, `vericairn.estate/blocked-terms`, appears in the policy kinds listing. Record
writes through every client can now be refused when they would introduce a blocked term, with a message naming the term, and
generated-document preparation can be refused as `generated-refused` for the same reason. See
[blocked terms](policies.md#blocked-terms-090).

## Estate release lines (CODE-13)

`EstateManifest.Line` and `EstateManifest.Channel`, and the matching init-only
`EstateInitialization` properties, default to `current` and `stable`. Optional
manifest JSON fields live inside `vericairn`. Reading an older manifest preserves
its bytes. Status JSON adds the effective `vericairn.line` and `vericairn.channel`.

`UpgradeRequest` adds nullable init-only `Line` and `Channel` properties. Null
preserves the corresponding manifest choice. The existing upgrade route, SDK
method and async bridge carry them; OpenAPI declares optional `line` and `channel`.
`UpgradeResponse` and `UpgradePlan` add init-only `Line` and `Channel` properties
defaulting to `current` and `stable`. Positional constructors and deconstructors
remain unchanged. `EstateUpgrade.Plan` retains both existing overloads and adds
overloads taking explicit line and channel choices.

Nondefault pin descriptions include `[line/channel]`, including in migration
`fromPin` and `toPin`, so interrupted changes of channel at the same version can
resume. Descriptions for `current/stable` keep their existing form.

No interface member is added. Older servers cannot enforce this selection
contract: upgrade the serving runtime before relying on these options. The
upgrade plan and refusal behavior use published releases in the followed line
at the followed channel or more stable; changing lines or channels is explicit.

The on-demand manifest companion advances to implementation
`vericairn.estate/manifest/2` to render the followed line and channel and distinguish
recorded values from defaults. Its implementation hash changes; the earlier
golden fixtures remain retained.

## Release cadence context (CODE-14)

`PolicyFindingDto` adds init-only `IsReleaseCadence`, default false and omitted
from JSON when false. Its positional constructor and deconstructor are unchanged.
A matching product cadence retains the duty verdict and evidence, with
`ReleaseHold` false. Other policy findings keep their existing enforcement.

`PolicyResponse` adds nullable init-only `ReleaseCadences`, omitted when no
occurrence is due. `ReleaseCadenceDto` carries the exact product, pinned policy,
rule and originating schedule, occurrence, due time, line, nullable target version
and recorded target references. Status and progress also place the due rows in
`operator.releaseCadences`. The existing release response carries them through
`policies.releaseCadences`, using the submitted line and version. Existing routes,
SDK and MCP operations transport these additive fields without new interface
members. No interface member is added without a default implementation.

`ReleaseCadencePresentation.Render` formats due context without performing effects.
The record format and the product's cadence reference shape are unchanged.

## Disclosure holds (CODE-16)

`IPushAdapter` adds `string? PublishedText(string directory, PushPlan plan)`, with a default implementation that returns null.
It returns the text a push's commits would publish, their messages and added lines. An adapter that keeps the default cannot
push to a public repository while a disclosure hold is in force; the shipped git adapter implements it. Work orders gain an
optional `disclosure` field whose only value is `held`, within record format 3. See [disclosure holds](changes.md#disclosure-holds-090).

## Requested progress surfaces and overdue attention (WATCH-03)

`IEstateApi.WorkProgressGet(string id)` reads an attempt or run. Its default
implementation throws `NotSupportedException` so an older implementation is
explicitly unavailable. `WorkProgressGetAsync` has the ordinary cancellable
synchronous bridge as its default. Both members have default implementations;
none is added without one. `CancellationBoundApi` and the synchronous and
asynchronous SDK forward the read to `GET /v1/progress/{id}`. MCP exposes
it through the read-only `work_progress_get` tool.

The additive `RequestedProgress` response carries the claim, request, exact
source, freshness and observation time beside independent worker liveness.
`RunRow.RequestedProgress` and `StatusResponse.RequestedProgress` are nullable
init-only additions, omitted when null. Their positional constructors and
deconstructors are unchanged. Status's canonical `Json` and `Text` remain
portable; human presentation appends the separately labelled local observations.
Progress and work observation JSON add a `requestedProgress` array. Clients
must not use any claim or overdue state as completion evidence or a run stop.

`ProgressRequests.ResolveForWork` resolves inherited reporting before preparing
an assignment. `ReleaseReviewBriefResponse.RequestedProgress` retains its
string type and default for compatibility; the current runtime fills it with
the selected request or `not requested`, and preparation pins its source and
retains the request in the new brief. An ambiguous request refuses preparation.

Results gain optional format-3 `progressClaim` context. Existing record bytes
and format numbers stay unchanged, and format-2 records refuse the field.
The generic record reader advances to `vericairn.records/readers/5` because its
result-field explanation changes. Version 5 has its own golden fixtures;
prior fixture versions remain unchanged. The generator id and contract version
remain unchanged; declarations pinned to an older implementation require an
explicit generator update before regeneration.

## Local community build guides (GUIDE-01)

`Vericairn.Runtime.Guides` adds standalone source-index, inventory, finding,
coverage and bundle types. `GuideMarkdown.Read` parses owning rows;
`GuideGenerator.Generate`, `Check`, `CompareInputs` and `Hash` operate on explicit
bytes. `GuidePreview.Load`, `Write`, `Check`, `Compare` and `SeparateOutput` provide
the local file boundary used by `guides generate`, `check` and `compare`.
`GuideJson.Options` defines the strict camel-case JSON representation.

These additions introduce no interface members, API routes, SDK methods or record
format changes. Existing implementers have no new obligations. The versioned
source-index and manifest contracts are described in
[Community build guides](community-build-guides.md).


## Principal attention presentation state (REMOTE-02)

`IEstateApi` adds `AttentionGet(string? principal = null)` and
`AttentionAcknowledge(AttentionAcknowledgeRequest)`. Both have default implementations
that throw `NotSupportedException`. `AttentionGetAsync` and `AttentionAcknowledgeAsync`
have the usual cancellable synchronous bridges; `CancellationBoundApi` and the SDK
forward both operations. No interface member is added without a default implementation.

`GET /v1/attention` returns the principal, observation timestamp and complete notices,
including acknowledged ones. The principal defaults to the requesting device's principal
or the local operator. A device cannot request another principal's presentation state.
Each notice carries a kind, subject identity, source freshness, reason, subject state,
next action, revision and independent acknowledgment timestamp. `Target` identifies the
subject to open when it differs from the notice's source. Notice ordering keeps urgency
separate from acknowledgment.

`POST /v1/attention/acknowledge` requires `by`, `kind`, `subjectType`, `subjectId` and
`expected` (the observed notice revision). It rechecks the device and exact current item,
then stores unsigned runtime presentation state for that principal. It returns
`AttentionAcknowledgeResponse` with `item` and `ok`, or `problem`; there is no record
receipt. A conflict requires a refreshed observation. Repeating the same acknowledgment
returns its original timestamp. Changed or absent items invalidate prior acknowledgments.
The local-network request is authenticated and kept in its write ledger, but this operation
needs no presence-free record signer. Other record writes retain their presence checks.

Canonical `StatusResponse.Json` remains record-derived and portable. Human status uses
the principal's attention presentation; the dedicated attention endpoint exposes that
runtime state without mixing it into canonical record state. Workbench shows the same
state and retains its derivation fallback with an older runtime.

## Remote observation storage (REMOTE-02)

`IRemoteConnections` adds `LoadObservation(RuntimeConnection)` and
`SaveObservation(RuntimeConnection, RemoteObservation)`, with defaults returning null
and doing nothing, respectively. Existing implementations remain source compatible and
explicitly retain no persistent cache until they implement these members.
`LocalNetworkConnections` keeps versioned app observations bound to the connection,
estate, runtime identity, principal and device. Invalid or mismatched cached data is
unavailable. Restoring a snapshot establishes neither current access nor action authority.
Only successful observations and confirmed acknowledgments are stored; no offline
request queue is introduced. The record format remains 3.

## Generated members rewritten by an upgrade (FIX-83)

`IDocumentGenerator` adds `IReadOnlyList<IDocumentGenerator> EarlierImplementations`
with a default implementation returning an empty list, so existing generators have
no new obligation. An earlier implementation has the same identity and contract
version as its generator, an earlier `Implementation` and its hash, and renders the
exact bytes that implementation wrote. The first-party decision, work-order and
release registers return `vericairn.records/decisions/1`,
`vericairn.records/work-orders/1` and `vericairn.records/releases/1`.

Records adds `GeneratedDocumentUpgrade.Prepare(EstateModel)` and the
`GeneratedMemberUpgrade` record (path, owning destination, earlier and current
implementation, guarded `DocumentOutputChange`). It writes nothing; it selects a
strict generated member only when its file equals an earlier implementation's
rendition of the same sources byte for byte.

`UpgradeResponse` adds an init-only `Regenerated` list of paths, empty by default:
the generated members `--check` would rewrite, or the applied upgrade rewrote.
Its positional constructor and deconstructor are unchanged. The upgrade route,
request, SDK methods and cancellation bridges are unchanged; the OpenAPI document
declares no response schema for this route, so it does not change. Earlier servers
omit the field, which reads as empty. The migration record adds `changes` lines
naming each register's rewritten count and implementations. Record format remains 3.

## Remote device signing (REMOTE-03)

`IRemoteConnections.WriteSignedAsync(RuntimeConnection, NewRecordRequest,
Func<RecordPrepared, bool>, CancellationToken)` has a default implementation that
returns an `unsupported` refusal. Existing implementations remain source compatible
and cannot submit phone decisions until they implement it. `LocalNetworkConnections`
uses `DeviceSignedRecords.WriteAsync` with the paired connection's device and the
app's key store. The callback reviews the exact prepared record before signing.
This adds no API route or record format.

## Remote decision loading (REMOTE-03)

`IRemoteConnections.LoadDecisions(RuntimeConnection)` has a default implementation
returning an empty list. `RemoteDecisionReview`, `RemoteDecisionEvidence` and
`RemoteDecisionDraft` describe the complete observed material, original target,
operator's words and submission observations. `LocalNetworkConnections` restores
versioned app storage bound to the connection, estate, runtime identity, principal
and device. Unreadable storage is reported instead of hiding an uncertain submission.

## Remote decision persistence (REMOTE-03)

`IRemoteConnections.SaveDecision(RuntimeConnection, RemoteDecisionDraft)` has a
default implementation that throws `NotSupportedException`. An implementation that
cannot retain a draft cannot enable submission. `LocalNetworkConnections` atomically
stores drafts and reserves the prepared record identity before signing. A retained
submission cannot be replaced with new words, a different request or a fresh identity;
a known receipt cannot be removed. Stored submissions are reconciled by reading
their exact records. Loading or refreshing never sends a stored draft.

## IEstateApi.DecisionBasis (REMOTE-11)

`IEstateApi` adds `DecisionBasis(string? baseCommit = null)` and its cancellable
`DecisionBasisAsync`, with default implementations: the read answers an
`unsupported` problem and the asynchronous member is the usual synchronous bridge.
`CancellationBoundApi` and `VericairnClient` implement both; the route is
`GET /v1/decisions/basis` with an optional `base` query, declared in the OpenAPI
document and in the daemon's request scope as a read naming no principal. New
contracts: `DecisionBasisResponse` (`ok`, `baseCommit`, `head`, `documents`,
`problem`; `DocumentsChanged` is computed and not serialized) and
`DecisionBasisDocument` (`path`, `hash`). Earlier servers answer `404`.
`DecisionFromAsk` now takes an empty `baseCommit` as the head the runtime observes
when it reads the change set; an explicit commit behaves as before.

## Records signed by a presence device (REMOTE-11)

`SignedRecordRequest` adds the nullable init-only `Device`: the signing device,
the requesting device when omitted. Its positional constructor is unchanged. The
runtime accepts another signing device only when it is admitted, belongs to the
requesting device's principal and states presence `required`; a device of another
principal is refused as `denied` before the service reads the request. The SDK adds
`DeviceSignedRecords.WriteWithPresenceAsync`, which signs with a presence store's
key, calls an optional `beforeSend` callback after signing and before sending, and
reports a `not-signed` problem when the key did not sign. A device-signed `decision`
with a change set must state the head the runtime observes, no pins and no design
document changed since its base, at preparation and again at the write; REMOTE-03's
phone answers were not checked against the repository. Earlier servers ignore
`device` and verify the signature against the requesting device, so a presence
signature is refused there and nothing is written.

## Pairing a presence device (REMOTE-11)

The pairing contracts add nullable init-only members: `PairingHello.Presence`,
`PairingChallenge.PresenceRecord`, `PairingConfirm.PresenceDevice` and
`PresenceSignature`, and `PairingCompleted.PresenceDevice`, `PresenceAdmission` and
`PresenceHash`. Positional constructors are unchanged. Their fields enter the
challenge, confirmation and completion transcripts after a `presence` label only
when present, so a pairing of one device is byte-for-byte the exchange of earlier
releases in both directions; a runtime offers a presence record only when the hello
asks for one. The SDK adds `PairingOptions.PresenceKeyStore`, the presence members
of `PairedRuntime` (`PresenceDevice`, `PresenceDevicePublicKey`,
`PresenceAdmission`, `PresenceCustody`) and of `RuntimeConnection`
(`PresenceDevice`, `PresenceDevicePublicKey`, kept with the connection), and
`RuntimeConnections.AddPresenceAsync` with `PresencePairingTimeout`. Pairing now
refuses a presence claim from a store whose custody cannot enforce it before any
key is made. The runtime adds `EstateService.PairingPlan(string, bool)`, the
`PairingPlan.PresenceRecord` init-only member and a `PairDevice` overload taking the
presence device; the existing overloads are unchanged. `DeviceKeyStores.PresenceKinds`
adds `ios-secure-enclave` and `android-keystore`, so a person's phone device in that
custody may state presence `required`; an agent's or a service's still may not.
Record format remains 3.

## IRemoteConnections.WriteSignedAsync signs with the presence key (REMOTE-11)

`IRemoteConnections.WriteSignedAsync` gains a `Func<RecordPrepared, bool> beforeSend`
parameter after `review`, called once the presence key has signed and before the
signature is sent; its default implementation still returns an `unsupported`
refusal. It replaces REMOTE-03's member of the same name, unreleased before 0.9.0.
`LocalNetworkConnections` signs only with the connection's presence device and
refuses with `presence-required`, preparing nothing, when the phone holds none.

## IRemoteConnections.HasPresenceKey (REMOTE-11)

`IRemoteConnections.HasPresenceKey(RuntimeConnection)` has a default implementation
returning false, so an existing implementation signs no decision until it holds a
presence key.

## IRemoteConnections.AddPresenceKeyAsync (REMOTE-11)

`IRemoteConnections.AddPresenceKeyAsync(RuntimeConnection, string, string,
CancellationToken)` has a default implementation returning a task faulted with
`NotSupportedException`. `LocalNetworkConnections` pairs one more device with the
new code through `RuntimeConnections.AddPresenceAsync`.

## Remote presence keys and the supplied basis (REMOTE-11)

`LocalNetworkConnections` adds a constructor taking a presence store, which must
be of a custody that enforces presence; the two-argument constructor keeps no
presence store. `RemoteDecisionReview` adds the init-only `Head`, the head the
runtime supplied for an answer. `RemoteDecisionDraft` drops `BaseCommit` and
`NoDocumentChanges`, and `RemoteViewModel` drops `DecisionBaseCommit` and
`DecisionHasNoDocumentChanges`, all REMOTE-03 members unreleased before 0.9.0;
stored drafts that carry those fields still load. `RemoteViewModel` adds
`DecisionBasis`, `HasPresenceKey`, `NeedsPresenceKey`, `PresenceCode`,
`PresenceMessage`, `PresenceHelp` and `AddPresenceKeyCommand`. No route or record
format is added by Remote itself.
