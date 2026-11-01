# API compatibility in 0.9.0

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
