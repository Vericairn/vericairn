# API compatibility in 0.8.4

## Reserved package versions (FIX-72)

`PackagesObserveRequest`, `PackagesUnlistRequest` and `PackagesDeprecateRequest`
each add an init-only `bool Reserved` property, default false. Their positional
constructors and generated deconstructors are unchanged. The existing API and SDK
methods, including async and cancellation-bound methods, carry this property;
OpenAPI declares optional JSON `reserved` as a boolean on all three request schemas.
No route, response contract or interface member changes.

With `reserved: true`, product/version selection requires the version in the current
product's `packages.reserved` array and selects no release. Omitted or false preserves
the existing recorded-release selection. `PackagesObserveRequest.Operation` cannot
be combined with `Reserved = true`, `Product` or `Version`: an operation observation
uses its original intent's exact pins. Undeclared reserved versions and unrecorded
release versions are refused before contacting the registry or writing records.

This is additive within record format 3. `packages.reserved` is an optional array
of one to eight distinct valid NuGet versions; comparisons ignore case. No reserved
version may equal a recorded release version of the same product, across revisions.
Reserved package observation facts and account-holder operation targets retain
`product`, `productHash`, `package`, `version` and `provider`, add string `reserved`
equal to the reserved version, and omit both `release` and `releaseHash`. The initial
observation or intent subjects the exact product declaration; an outcome observation
subjects the original intent. Mixed release and reserved pins are refused. Existing
records remain valid and the record-format number is unchanged. Earlier builds do
not implement reserved selection; upgrade before using these declarations or requests.

Intent ordering, response evidence, time checks, outcome matching and operator
authority for account-holder actions are unchanged. MCP worker access is unchanged;
this addition exposes no package write tool. See the
[package procedure](operator-actions.md#package-publication-unlisting-and-deprecation)
and [CLI options](cli.md#vericairn-packages).

## Release review briefs (CODE-09)

| Added member | Purpose |
| --- | --- |
| `IEstateApi.ReleaseReviewBrief(string version, string? product = null, string? decision = null)` | Read a deterministic candidate review brief. The default returns an `unsupported` problem. |
| `IEstateApi.ReleaseReviewAssign(string version, ReleaseReviewAssignRequest request)` | Recheck the prepared brief hash and store its Reviewer assignment. The default returns an `unsupported` problem. |
| `IEstateApi.ReleaseReviewBriefAsync` / `ReleaseReviewAssignAsync` | Default cancellable read and write bridges, each with an optional cancellation token. |
| `EstateService.ReleaseReviewBrief` / `ReleaseReviewAssign` | Select records and store the exact prepared assignment through normal record validation and signing. |
| `VericairnClient.ReleaseReviewBrief` / `ReleaseReviewBriefAsync` | Call `GET /v1/releases/{version}/review-brief`, preserving product and decision queries. |
| `VericairnClient.ReleaseReviewAssign` / `ReleaseReviewAssignAsync` | Call `POST` on the same path. The asynchronous write uses the 15-minute preparation deadline. |
| `CancellationBoundApi.ReleaseReviewBrief` / `ReleaseReviewAssign` and their asynchronous counterparts | Forward with the caller's cancellation lifetime. |
| `ReleaseReviewAssignRequest(string By, string Expected, string? Product = null, string? Decision = null)` | Carry the assigning principal, exact prepared text hash and selection. |
| `ReleaseReviewBriefResponse(string Text, string? Hash = null, JsonElement? Brief = null, string? Reviewer = null, RecordWritten? Assignment = null, Problem? Problem = null, string RequestedProgress = "unavailable")` | Carry the complete preparation or assignment receipt. `Ok` derives from `Problem`. `RequestedProgress` reserves the WATCH-02 integration seam without enabling it. |

All four added interface members have default implementations; none requires an
existing implementer to add a member. Positional records also expose their named
properties, constructors and generated record members. Existing constructors are
unchanged. Older servers have no fallback route.

Both routes return HTTP 200 with the typed response, including refusals. Assignment
requires the current prepared hash and the ordinary assignment authority. Paired
requests bind `By` to their authenticated principal and retain the assignment receipt
or refusal in the write ledger. Cancellation after a POST can leave its outcome
unknown; inspect the work order before retrying.

From 0.8.4, format-3 decisions may carry `releaseReview`, and format-3 assignment briefs
may carry `reviewBrief`. Existing records are unchanged; earlier builds do not understand
these optional fields. Upgrade the estate before recording them. The
[release review guide](software.md#preparing-a-release-review-084) gives the selection,
worker requirements and record format.

## Operator requests (OPS-03)

| Added member | Purpose |
| --- | --- |
| `IEstateApi.OperatorRequests(string? principal = null)` | Read the complete open queue, delivering up to three full requests. The default throws `NotSupportedException`. |
| `IEstateApi.OperatorRequestWrite(OperatorRequestWriteRequest)` | Create or revise a request with exact content and subject pins. The default returns an `unsupported` problem. |
| `IEstateApi.OperatorRequestCarry(string, OperatorRequestCarryRequest)` | Carry one exact open ask into a request. The default returns an `unsupported` problem. |
| `IEstateApi.OperatorRequestDone(string, OperatorRequestCloseRequest)` | Record one signed done outcome. The default returns an `unsupported` problem. |
| `IEstateApi.OperatorRequestDismiss(string, OperatorRequestCloseRequest)` | Record one signed dismissal with a reason. The default returns an `unsupported` problem. |
| The five matching `IEstateApi` members ending in `Async`, with an optional final `CancellationToken` | Default cancellable bridges; authoring and closure are classified as writes. |
| The five synchronous members on `EstateService` | Implement the record-derived reads and validated authoring. |
| The five synchronous and five asynchronous members on `VericairnClient` and `CancellationBoundApi` | Carry the same values and cancellation lifetime through the API. |
| `OperatorRequestsResponse`, `OperatorRequestWriteRequest`, `OperatorRequestCarryRequest`, `OperatorRequestCloseRequest`, `OperatorRequestWords` | Carry the queue, exact drafts, expected hashes, outcomes and attributed operator citations. Positional records include their constructors, properties and generated record members. |
| `OperatorRequestType`, `OperatorRequestCloseType` | Additive format-3 record schemas, validators and readable companion coverage. |
| `OperatorRequests.PerDelivery`, `Closures`, `Carried`, `Open`, `Derive`, `RenderText`, `Item` | Shared record derivation and presentation of the request queue. |
| `ObservationType.Kinds` | Adds `operator-request`; its signed observation pins the request and records proof of its effect. |

All ten new interface members have default implementations. Existing constructors
and abstract interface obligations are unchanged. No record-format increase is
required. New status JSON includes `operatorRequests`; operator actions JSON gains
`operator-request` items with direct done and dismiss commands. Existing ask and
acceptance operations remain available. No MCP write is added.

The new write routes return `WriteOutcome` with HTTP 200 or 422, using the existing
write-response contract. Older servers have no fallback route. See the
[operator request guide](operator-requests.md) for record fields, carry-over,
publication intent and interrupted-write recovery.

## Publication documentation pass (DOCS-12)

| Added member | Purpose |
| --- | --- |
| `IEstateApi.DocumentPass(string version, string? product = null)` | Read the currently due publication queue, changes and proposed patches. The default returns an `unsupported` problem. |
| `IEstateApi.DocumentPassAssign(string version, DocumentPassAssignRequest request)` | Store an ordinary review work order and assignment for exact displayed declarations. The default returns an `unsupported` problem. |
| `IEstateApi.DocumentPassAsync` / `DocumentPassAssignAsync` | Default cancellable bridges, classified as a read and a write respectively. Both use the 15-minute preparation deadline. |
| `EstateService.DocumentPass` / `DocumentPassAssign` | Reuse due-state derivation, prepare patches, validate and store selected review work. |
| `VericairnClient.DocumentPass` / `DocumentPassAsync` | Call `GET /v1/documents/pass/{version}`, with optional `product`. |
| `VericairnClient.DocumentPassAssign` / `DocumentPassAssignAsync` | Call `POST /v1/documents/pass/{version}/assignments`. |
| `CancellationBoundApi.DocumentPass` / `DocumentPassAsync` / `DocumentPassAssign` / `DocumentPassAssignAsync` | Forward the caller's cancellation lifetime. |
| `DocumentPassPin`, `DocumentPassChange`, `DocumentPassRecord`, `DocumentPassAssignment`, `DocumentPassItem`, `DocumentPassUpdate` | Carry exact record pins, covered file changes, record context, reviewer assignments and proposed patches with byte hashes and base commits. |
| `DocumentPassResponse`, `DocumentPassAssignRequest`, `DocumentPassAssignResponse` | Carry the current queue, explicit selected pins and storage receipts or problems. Response `Ok` properties derive from `Problem`. |
| `IDocumentSource.Changes(EstateModel, RecordEnvelope, string? before, string commit)` | Compare local exact publication commits. The default returns an explicit unavailable comparison. A null `before` selects all files of the first publication. |
| `GitFileSource.Changes`, `DocumentSourceChange`, `DocumentSourceChanges` | Supply added, modified and deleted paths from registered local clones without fetching or changing them. |

Every added interface member has a default implementation; no new abstract member is
required. Existing record formats and constructors are unchanged. New positional records
also expose their properties, constructors and generated record members. Review tasks use
the existing work-order and assignment record types.

Both routes return HTTP 200 with typed responses, including refusals. Inspect `Ok` and
`Problem`. A partial assignment store can return `Written` receipts with a
`reconcile-required` problem; inspect those records and the original batch before retrying.
A cancelled or interrupted assignment can have an unknown outcome. Older servers have no
fallback route. Listing prepares patches in the response but writes no files or records.
The synchronous SDK methods preserve the SDK's original transport exceptions; async
methods and the cancellation-bound wrapper classify interrupted writes as uncertain.
The command line's explicit `--output` writes proposed patch files; application and review
remain separate. See [Living documents](living-documents.md#after-a-publication).

## MCP resources (MCP-02)

`IEstateApi` adds `Resources()`, `ResourceRead(ResourceRequest)`,
`ResourcesAsync(CancellationToken = default)` and
`ResourceReadAsync(ResourceRequest, CancellationToken = default)`. All four
members have default implementations: synchronous reads return an explicit
`unsupported` problem, and asynchronous reads use the cancellable read bridge.
There is no added interface member without a default implementation.
`EstateService`, `VericairnClient` and `CancellationBoundApi` implement or forward
the operations. The routes are `GET /v1/resources` and `POST /v1/resources/read`;
both are reads with no record, signature, presence or write ledger.

New protocol records are `ResourceRequest`, `ResourceEntry`, `ResourcesResponse`
and `ResourceResponse`, with their named properties, constructors and generated
record members. Requests select `kind`, `key`, optional `maxBytes`, `recipient`,
`assignment`, `form` and `estate`; unknown fields are refused. Response content
is a byte array (base64 in JSON) with its exact SHA-256, media type and optional
source hash. A problem carries no bytes. Existing constructors and record
formats are unchanged. HTTP responses use status 200 for these typed results,
including refusals; callers inspect `Problem`. An older server has no fallback
route. Cancellation is a read interruption and cannot leave an unknown write.

`ToolRegistry` adds `ResourcesListAsync`, `ResourceTemplates` and
`ResourceReadAsync`. `McpServer` advertises the three standard resource methods,
with `subscribe: false` and `listChanged: false`. Existing prompt definitions,
categories, arguments and recorded-use behavior are retained. The ordinary CLI
brief is unchanged; its new `--agent-context` and `--max-bytes` options select the
measured MCP representation. See [MCP resources](mcp.md#resources-084-development)
for exact URI, representation, hash and permission contracts.

## Operator close (OPS-02)

| Added member | Purpose |
| --- | --- |
| `IEstateApi.OperatorCloseCandidates()` | Read finished, reviewed and unaccepted steps with exact work-order, result and review pins. The default returns an `unsupported` problem. |
| `IEstateApi.OperatorClose(OperatorCloseRequest)` | Sign the displayed selections as one validated batch. The default returns an `unsupported` problem. |
| `IEstateApi.OperatorCloseCandidatesAsync(CancellationToken = default)` | Default cancellable read bridge. |
| `IEstateApi.OperatorCloseAsync(OperatorCloseRequest, CancellationToken = default)` | Default cancellable write bridge. |
| `EstateService.OperatorCloseCandidates` / `OperatorClose` / `OperatorCloseAsync` | Derive the list, validate exact selections, sign and store their acceptances. The synchronous write also has a cancellation-token overload. |
| `VericairnClient.OperatorCloseCandidates` / `OperatorCloseCandidatesAsync` | Call `GET /v1/operator/close`. |
| `VericairnClient.OperatorClose` / `OperatorCloseAsync` | Call `POST /v1/operator/close`; the asynchronous operation uses the 15-minute operator-action deadline. |
| `CancellationBoundApi.OperatorCloseCandidates` / `OperatorCloseCandidatesAsync` / `OperatorClose` / `OperatorCloseAsync` | Forward the operation with the caller's cancellation lifetime. |
| `OperatorClosePin`, `OperatorCloseReview`, `OperatorCloseItem`, `OperatorCloseSelection` | Carry exact displayed evidence. `OperatorCloseItem.ToSelection()` copies its subject and review pins. |
| `OperatorCloseCandidatesResponse`, `OperatorCloseRequest`, `OperatorCloseResponse` | Carry the listing, explicit selection and acceptance receipts or refusal. Response `Ok` properties derive from `Problem`. |

All four new interface members have default implementations. Existing implementers remain
loadable; no new abstract member is required. Positional records expose their named properties,
constructors and generated record members. Existing constructors and record formats are unchanged.
The signing interface is unchanged and checks presence per signature where required.

The new routes return HTTP 200 with typed responses, including refusals. Callers inspect `Ok`
and `Problem`; a storage interruption can return `Written` receipts alongside a reconciliation
problem. Older servers have no fallback route. See [Operator actions](operator-actions.md#close-finished-steps-in-one-action)
for the selection contract and recovery behavior.

## Saved prompts (PROMPT-01)

`IEstateApi` adds `Prompts(PromptQuery)`, `PromptSave(PromptSaveRequest)`,
`PromptPreference(PromptPreferenceRequest)`, `PromptCopy(PromptCopyRequest)` and
`PromptUse(PromptUseRequest)`, plus each operation's `Async` counterpart with an
optional cancellation token. All ten interface members have default
implementations: synchronous methods return an explicit unsupported response;
asynchronous methods use the cancellable bridge. There is no new interface member
without a default implementation. `CancellationBoundApi` forwards all ten members.
`EstateService` implements the operations and `VericairnClient` exposes the same
synchronous and asynchronous routes. No existing API constructor is removed.

New protocol records are `PromptRow`, `PromptsResponse`, `PromptQuery`,
`PromptSaveRequest`, `PromptPreferenceRequest`, `PromptCopyRequest`,
`PromptSelection`, `PromptUseRequest` and `PromptUseResponse`. `PromptUseRequest`
has optional init property `Favor`; existing `AssignRequest` and `RunStartRequest`
gain optional init property `Prompt` while retaining their positional constructors
and deconstruction contracts. The new prompt request types refuse unknown fields.

`ProviderRegistry.Prompts` owns the runtime's `PromptCatalog`, whose registration
accepts immutable versioned contributions. `SoftwarePrompts.All` supplies the
software pack's reviewed prompts. `ToolRegistry` adds `PromptsListAsync` and
`PromptGetAsync`; MCP advertises standard prompts with no change notifications.
Saved definitions, preferences and uses are additive within record format 3.

See [Saved prompts](prompts.md) for request fields, authority rules, receipts and
the explicit partial outcome when a use succeeds but its favorite write fails.
An older server has no prompt fallback route. Cancellation after a write was sent
can leave its outcome unknown; inspect the target before repeating the action.

## Worker settlement recovery (FIX-65)

| Added member | Purpose |
| --- | --- |
| `IEstateApi.RunSettle(string run, RunSettleRequest request)` | Recover an exited run's unrecorded settlement from retained local observations. Its default implementation returns an `unsupported` problem. |
| `IEstateApi.RunSettleAsync(string run, RunSettleRequest request, CancellationToken cancellationToken = default)` | Default cancellable bridge, classified as a write. |
| `EstateService.RunSettle` | Apply the same authority as stopping the run and write its observed settlement. |
| `VericairnClient.RunSettle` / `RunSettleAsync` | Call `POST /v1/runs/{id}/settle`. |
| `CancellationBoundApi.RunSettle` / `RunSettleAsync` | Forward the operation with the caller's cancellation lifetime. |
| `RunSettleRequest(string By)` | Name the requesting principal. |
| `RunSettleResponse(string Run, string Status, string Message, bool Ok, RecordWritten? Operation = null, Problem? Problem = null)` | Return the settlement receipt or the reason recovery could not proceed. |
| `RunObservedState.Outcome`, `Scope`, `SettlementPrepared`, `MinuteTicks` | Add retained exit, final audit and accounting facts to the worker's local observation. Older snapshots remain readable. |

Both new interface members have default implementations; existing implementations
remain loadable. The positional request and response records also expose their
named properties and generated record members. Existing constructors and record
formats are unchanged.

The new retained recovery fields are local storage internals, excluded from the
run inspector's metadata projection. Existing inspector bytes and hashes remain
unchanged; the settlement and run API carry the recorded usage and audit facts.

The API returns HTTP 200 with the response, including refusals. Callers inspect
`Ok` and `Problem`; codes include `in-flight`, `already-settled`, `unresolved`,
`not-found`, `refused` and `denied`. A successful `Operation` is the settlement
revision. Cancellation after sending a write can leave its outcome unknown;
inspect the original run before retrying. An older server has no fallback route.

`EstateService.Open` now attempts recovery after judging interrupted batches.
`OpenReadOnly` does not recover or write. Startup uses the original run author's
current standing and ordinary record validation. Unreadable or insufficient
observations keep the reservation and appear with their reason in `worker runs`.
The command is `vericairn worker settle <run> --by <principal>`; `--json` preserves
both successful responses and refusals. See the [worker guide](software.md#coding-agents-the-worker-and-its-launchers).

## Guided decisions from operator answers (CODE-10)

| Added member | Purpose |
| --- | --- |
| `IEstateApi.DecisionFromAsk(DecisionNewRequest)` | Record an answer and the complete changed design document pins. Default returns an `unsupported` problem with no receipts. |
| `IEstateApi.DecisionFromAskAsync(DecisionNewRequest, CancellationToken)` | Default cancellable bridge, classified as a write. |
| `EstateService.DecisionFromAsk` | Validate the supplied answer, derive the change set, and store the signed decision and pins as one journaled batch. |
| `VericairnClient.DecisionFromAsk` / `DecisionFromAskAsync` | Call `POST /v1/decisions/new`. |
| `CancellationBoundApi.DecisionFromAsk` / `DecisionFromAskAsync` | Forward with the caller's cancellation lifetime. |
| `ISourceAdapter.InspectDesignChanges(string root, string baseCommit)` | Read the complete changed design files from an exact local base through the working tree. Default returns null (unsupported). |
| `GitCommandAdapter.InspectDesignChanges` | Observe local Git membership and exact current file hashes without fetching or writing. |
| `DesignChangeSet`, `DesignFileChange` | Carry the base, observed head, relative paths and file hashes. |
| `DecisionNewRequest`, `DecisionNewResponse` | Supplied answer and decision fields; decision and pin receipts, problem and completion status. |
| `DecisionContent.BlockedProseField` | Inspect prose against execution identities derived from recorded runs and qualification evidence; returns only the field name. |

Every new interface member has a default implementation. Existing implementations remain loadable; an unavailable adapter refuses the guided write. New positional records also expose their named properties and generated record members. Existing constructors are unchanged.

Guided decisions add optional `answer` and `changeSet` fields to decision bodies in record formats 2 and 3. `answer` holds `ask`, `hash` and `at`; `changeSet` holds `baseCommit`, `head` and `pins`, whose members hold `path`, `id` and `hash`. Both are required together. Existing decisions without these fields keep their validation rules. Older builds that do not know these fields cannot validate guided decision records; upgrade readers before sharing them.

The complete proposed decision and pin set passes ordinary estate validation before storage. Pins use the same preparation as `docs pin`. The storage journal can report a partial batch; receipts accompanied by `reconcile-required` must be inspected before retrying. No receipt is rolled back. An older API server has no fallback route; the SDK reports a missing operation. The [CLI contract](cli.md#vericairn-decisions-new) specifies response status and all request fields.

Decision companion implementation `vericairn.records/decisions/2` and generic record reader implementation `vericairn.records/readers/3` account for the added answer and change-set fields. Their implementation hashes change, so declarations pinned to older generator implementations need the ordinary explicit generator update before regeneration.

## Templates and section workflows (TPL-01)

All eight added `IEstateApi` members have default implementations. Existing API
implementations continue to load; synchronous defaults return a structured
`unsupported` problem, and asynchronous defaults bridge their synchronous
counterparts with the existing cancellation lifetime.

| Added interface member | Default and operation |
| --- | --- |
| `Templates()` | Unsupported catalogue response; read. |
| `TemplatesAsync(CancellationToken = default)` | Cancellable bridge; read. |
| `DocumentSections(string declaration, string? commit = null)` | Unsupported sections response; read. |
| `DocumentSectionsAsync(string declaration, string? commit = null, CancellationToken = default)` | Cancellable bridge; read. |
| `SectionBrief(string declaration, string section, long? maxBytes = null, string? commit = null)` | Unsupported brief response; read. |
| `SectionBriefAsync(string declaration, string section, long? maxBytes = null, string? commit = null, CancellationToken = default)` | Cancellable bridge; read. |
| `DocumentNew(DocumentNewRequest request)` | Unsupported creation response; write. |
| `DocumentNewAsync(DocumentNewRequest request, CancellationToken = default)` | Cancellable bridge; write. |

`EstateService`, `VericairnClient` and `CancellationBoundApi` implement these
operations. The HTTP routes are `GET /v1/templates`, `POST /v1/documents/new`,
`GET /v1/documents/{id}/sections` and
`GET /v1/documents/{id}/sections/{section}/brief`. The existing document review
route retains its method and path. The new routes return HTTP 200 with their
response, including refusals; callers inspect `Problem` and creation's `Ok`.
An older daemon has no route fallback. Cancellation after sending creation or a
review can leave its outcome unknown; inspect the original operation before
retrying.

`DocumentReviewRequest` gains an optional init-only `Sections` property of
`IReadOnlyList<DocumentSectionReview>`. Its existing six-parameter constructor and
deconstruction remain unchanged. `DocumentSectionReview` carries `Id`, `Outcome`,
`Fidelity` and optional `Judgments`; `DocumentSectionJudgment` carries `Rule`,
`Verdict`, `Summary` and `Evidence`. Section reviews bind exact runtime-read bytes
and do not clear unselected sections.

The other new DTOs are `TemplatePinDto`, `TemplateDto`, `TemplateRecordReference`,
`TemplateSourceDto`, `TemplateCheckDto`, `TemplateSectionDto`, `TemplateBriefDto`,
`TemplatesResponse`, `DocumentSectionsResponse`, `SectionBriefResponse`,
`DocumentNewRequest` and `DocumentNewResponse`. They live in Protocol without a
Records dependency. OpenAPI lists their properties and route/query contracts.
`DocumentNewResponse.Declaration` preserves the stored receipt alongside a
`reconcile-required` problem after interrupted output publication; `Ok` remains
false until the operation is complete. Brief responses preserve complete source
material and flag an insufficient allocation as `incomplete`.

`ITemplateCheck` is a new opt-in extension interface, with required `Id` and
`Check(string content, IReadOnlyList<TemplateSource> sources)` members. These have
no defaults because each new check must supply its identity and behavior. No
existing interface gains a required member. Template/primitive registration
methods install vocabulary and implementations without assigning authority.

First-party template regions add `IDocumentGenerator.GenerateRegion(EstateModel,
RecordEnvelope, string, IReadOnlyList<string>)`. Its default implementation calls
the existing `Generate(EstateModel, string?, IReadOnlyList<string>)`, so existing
generators need no change. An implementation can inspect the owning declaration
to select its document's revision, decisions or product. The versioned whole-file
renderer and its prohibition on output-pin dependencies retain their contract.

The additive record fields remain in format 3. The readable record companion
implementation advances to `vericairn.records/readers/3`; prior golden versions
remain retained. See [Templates](templates.md) and the
[generated record reference](reference/record-reference.md).

## Presentation templates (TPL-COMMS-01)

`IEstateApi` adds `TemplateRender(TemplateRenderRequest)` and
`TemplateRenderAsync(TemplateRenderRequest, CancellationToken = default)`.
Both have default implementations: the synchronous method returns a structured
`unsupported` response and the asynchronous method uses the existing cancellable
read bridge. No existing interface gains a member without a default.
`EstateService`, `VericairnClient` and `CancellationBoundApi` implement both.

`POST /v1/templates/render` is a read with a structured request body. It uses no
write ledger or signing path, returns HTTP 200 with `TemplateRenderResponse`,
and callers inspect `Problem`. The SDK marks it as a read for cancellation;
an older daemon has no fallback route. MCP adds only the read `template_render`.
Worker write visibility and admission remain unchanged.

The new DTOs are `TemplateRenderRequest`, `TemplateRenderResponse` and
`PresentationTemplateUse`. The request contains an exact template and subject,
optional depth, authored fields, explanation, complete byte allocation, candidate
and retained adoption. The response contains the exact adoption and template use,
complete text, hash, byte count, checks and any refusal. OpenAPI describes these
properties and the route's request and response schemas.

`CommunicationProfile.Template` is an optional init-only `PresentationTemplateUse`.
`SectionBriefResponse.Presentation` is an optional init-only
`TemplateRenderResponse`. Their existing constructors and deconstruction remain
unchanged, and null properties are omitted from wire output. The source section
brief retains its own measured contract; an adopted human rendition has separate
accounting, and exceeding the requested allocation returns an incomplete response.
The shared client renderer and checker are `CommunicationTemplatePresentation`.
Protocol retains no Records dependency: both compile the same content forms and
presentation code. Records retains its existing no-embedded-resources invariant.

No record schema or format changes. Explicit estate adoption and withdrawal use
the existing `template-defaults` pin, scope, options and reason. The existing
record reference and readable companion already cover these complete fields.
See [presentation templates](presentation-templates.md) for preparation, checking,
depth guarantees and the boundary between content checks and reviewer judgment.

## First-party templates and adoption (TPL-02)

`IEstateApi.DocumentAdopt(DocumentAdoptRequest)` has a default that returns an
`unsupported` problem. `DocumentAdoptAsync(DocumentAdoptRequest,
CancellationToken = default)` has the existing cancellable bridge, classified
as a read for `Preview` and a write otherwise. Both new interface members have
defaults. `EstateService`, `VericairnClient` and `CancellationBoundApi` implement
both forms through `POST /v1/documents/adopt`. The route enforces the requesting
principal's existing standing, as creation does. There is no new MCP write tool.

`DocumentAdoptRequest` carries required `By`, `Template`, `Path` and
`ExpectedContentHash`; optional `ExpectedDeclaration`, `Title`, `Options`,
`Scope`, `Overrides`, `Level` (default `warn`), `Preview` (default false) and
`IncludeRegions` (default false). `DocumentAdoptResponse` carries `Declaration`,
`ContentHash`, `ProposedDeclaration`, `MigrationRequired`, `MigrationBasis`,
`MigrationPreview`, `RetainedPins` and `Problem`, with init-only `TemplateContent`,
`PreparedContent` and `Pin`. The new positional records expose their named
properties and generated record members. `Ok` requires a stored declaration
without a problem; preview success intentionally leaves `Ok` false and has no
receipts. The original file's hash and prior pins remain distinguishable from a
newly rendered document revision.

`DocumentPinRequest` gains optional init-only `bool RenderTemplate` (default
false), retaining its existing five-parameter constructor and deconstruction.
Its existing route, response, SDK methods and ordinary behavior remain intact.
Opting in refreshes declared generated regions and pins the new bytes as one
recoverable operation. `TemplateDocuments.RefreshGenerated(EstateModel,
RecordEnvelope, string)` prepares those payloads without changing other content.
The declaration-aware `IDocumentGenerator.GenerateRegion` default is described
above. No existing interface gains an abstract member.

Adoption returns HTTP 200 with its typed response, including refusals. Callers
inspect `Problem`, not just the HTTP status. An interrupted batch preserves every
stored `Declaration` or `Pin` receipt alongside `reconcile-required`; reconcile
the original batch before retrying. Cancellation after dispatch can leave a
write's outcome unknown. An older daemon has no adoption route fallback.

Format 3 additionally permits root `README.md` in `document-revision.path`;
formats 1 and 2 keep their original grammar. No record type, field or record
format is added by TPL-02. Existing generic record companions retain the complete
revision body. The [adoption guide](templates.md#adopt-an-existing-estate-document)
documents exact commands, defaults, migration evidence and recovery.
