// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vericairn.Protocol;

namespace Vericairn.Daemon;

/// <summary>The routes of API version 1, one per operation of <see cref="IEstateApi"/>; the OpenAPI document in the protocol describes them.</summary>
public static class ApiRoutes
{
    /// <summary>Maps every route onto a service.</summary>
    public static void Map(IEndpointRouteBuilder app, IEstateApi service, Action shutdown)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(shutdown);
        string v = ApiVersion.Prefix;
        CompanionRoutes.Map(app, service);
        app.MapGet($"{v}/estate-policies", () => Results.Json(service.Policies(), ProtocolJson.Options));
        app.MapGet($"{v}/estate-policies/kinds", () => Results.Json(service.PolicyKinds(), ProtocolJson.Options));
        app.MapPost($"{v}/estate-policies/hash", (PolicyHashRequest request) =>
        {
            PolicyHashResponse response = service.PolicyHash(request);
            return Results.Json(response, ProtocolJson.Options, statusCode: response.Problem is null ? 200 : 422);
        });
        app.MapGet($"{v}/estate-policies/evaluate", (string? asOf, string? release) => Results.Json(service.PolicyEvaluate(asOf, release), ProtocolJson.Options));
        app.MapGet($"{v}/estate-policies/{{key}}/view", (string key) => service.Policy(key) is { } policy ? Results.Json(policy, ProtocolJson.Options) : Results.NotFound());
        app.MapGet($"{v}/writing-exceptions", () => Results.Json(service.WritingExceptions(), ProtocolJson.Options));
        app.MapGet($"{v}/writing-exceptions/{{id}}", (string id) => service.WritingException(id) is { } entry ? Results.Json(entry, ProtocolJson.Options) : Results.NotFound());
        app.MapGet($"{v}/estate-policies/access", (string principal) => Results.Json(service.PolicyAccess(principal), ProtocolJson.Options));
        app.MapPost($"{v}/estate-policies", (PolicyChangeRequest request) => Outcome(service.PolicyDeclare(request)));
        app.MapPost($"{v}/estate-policies/{{key}}/revisions", (string key, PolicyChangeRequest request) => Outcome(service.PolicyRevise(key, request)));
        app.MapPost($"{v}/estate-policies/{{key}}/retire", (string key, PolicyChangeRequest request) => Outcome(service.PolicyRetire(key, request)));
        app.MapPost($"{v}/estate-policies/{{key}}/exceptions", (string key, PolicyChangeRequest request) => Outcome(service.PolicyExcept(key, request)));
        app.MapPost($"{v}/estate-policies/{{key}}/exceptions/{{id}}/revoke", (string key, string id, PolicyChangeRequest request) => Outcome(service.PolicyRevokeException(key, id, request)));
        app.MapPost($"{v}/writing-exceptions", (PolicyChangeRequest request) => Outcome(service.WritingExceptionAdd(request)));
        app.MapPost($"{v}/writing-exceptions/{{id}}/remove", (string id, WritingExceptionRemoveRequest request) => Outcome(service.WritingExceptionRemove(id, request)));
        app.MapPost($"{v}/work/{{key}}/placement", (string key, WorkPlaceRequest request) => Outcome(service.WorkPlace(key, request)));
        app.MapGet($"{v}/health", () => Results.Json(service.Health(), ProtocolJson.Options));
        app.MapGet($"{v}/communication", (string? recipient, string? assignment, string? progress, string? status, string? decision, string? result, string? handoff) =>
            Results.Json(service.Communication(recipient, assignment, new CommunicationValues(progress, status, decision, result, handoff)), ProtocolJson.Options));
        app.MapPost($"{v}/communication", (CommunicationSetRequest request) => Outcome(service.CommunicationSet(request)));
        app.MapPost($"{v}/communication/delegations", (CommunicationDelegateRequest request) => Outcome(service.CommunicationDelegate(request)));
        app.MapGet($"{v}/status", () => Results.Json(service.Status(), ProtocolJson.Options));
        app.MapPost($"{v}/status/observations", (EstateObserveRequest request) => Outcome(service.ObserveEstate(request)));
        app.MapGet($"{v}/progress", () => Results.Json(service.Progress(), ProtocolJson.Options));
        app.MapGet($"{v}/handoff", (long? maxBytes) => Results.Json(service.Handoff(maxBytes), ProtocolJson.Options));
        app.MapPost($"{v}/resume", (HandoffPackage package) => Results.Json(service.ResumeHandoff(package), ProtocolJson.Options));
        app.MapGet($"{v}/context/work/{{key}}/brief", (string key, long? maxBytes, string? recipient, string? assignment, string? progress, string? status, string? decision, string? result, string? handoff) => service.BriefContext(key, maxBytes, recipient, assignment, new CommunicationValues(progress, status, decision, result, handoff)) is { } brief ? Results.Json(brief, ProtocolJson.Options) : Results.NotFound());
        app.MapGet($"{v}/context/work/{{key}}", (string key, long? maxBytes) => Results.Json(service.Context(key, maxBytes), ProtocolJson.Options));
        app.MapGet($"{v}/context/original/{{pin}}", (string pin) => Results.Json(service.ContextRead(pin), ProtocolJson.Options));
        app.MapGet($"{v}/context/{{manifest}}/sources/{{source}}", (string manifest, string source) => Results.Json(service.ContextRead(manifest, source), ProtocolJson.Options));
        app.MapGet($"{v}/validate", () => Results.Json(service.Validate(), ProtocolJson.Options));
        app.MapGet($"{v}/check", () => Results.Json(service.Check(), ProtocolJson.Options));
        app.MapGet($"{v}/records/{{id}}", (string id) => service.Record(id) is RecordResponse record ? Results.Json(record, ProtocolJson.Options) : Results.NotFound());
        app.MapGet($"{v}/records", (string type) => Results.Json(service.Records(type), ProtocolJson.Options));
        app.MapPost($"{v}/records", (NewRecordRequest request) => Outcome(service.NewRecord(request)));
        app.MapGet($"{v}/decisions", () => Results.Json(service.Decisions(), ProtocolJson.Options));
        app.MapGet($"{v}/decisions/{{id}}", (string id) => service.Decision(id) is DecisionResponse decision ? Results.Json(decision, ProtocolJson.Options) : Results.NotFound());
        app.MapGet($"{v}/work/{{key}}/brief", (string key, string? recipient, string? assignment, string? progress, string? status, string? decision, string? result, string? handoff) => service.BriefFor(key, recipient, assignment, new CommunicationValues(progress, status, decision, result, handoff)) is BriefResponse brief ? Results.Json(brief, ProtocolJson.Options) : Results.NotFound());
        app.MapGet($"{v}/work/{{key}}", (string key) => service.Observe(key) is ObserveResponse observe ? Results.Json(observe, ProtocolJson.Options) : Results.NotFound());
        app.MapPost($"{v}/work/new", (WorkNewRequest request) => Outcome(service.WorkNew(request)));
        app.MapPost($"{v}/work/assign", (AssignRequest request) => Outcome(service.Assign(request)));
        app.MapPost($"{v}/work/attempt", (AttemptRequest request) => Outcome(service.Attempt(request)));
        app.MapPost($"{v}/work/progress", (WorkProgressRequest request) => Results.Json(service.WorkProgress(request), ProtocolJson.Options));
        app.MapPost($"{v}/work/result", (ResultRequest request) => Outcome(service.Result(request)));
        app.MapGet($"{v}/release-bases/{{hash}}", (string hash) => service.ReleaseBasis(hash) is { } basis ? Results.Json(basis, ProtocolJson.Options) : Results.NotFound());
        app.MapPost($"{v}/work/review", (ResultReviewRequest request) => Outcome(service.ReviewResult(request)));
        app.MapPost($"{v}/work/amend", (AmendRequest request) => Outcome(service.Amend(request)));
        app.MapPost($"{v}/work/takeover", (TakeoverRequest request) => Results.Json(service.WorkTakeover(request), ProtocolJson.Options));
        app.MapGet($"{v}/devices", () => Results.Json(service.Devices(), ProtocolJson.Options));
        app.MapPost($"{v}/devices", (DeviceCreateRequest request) => Outcome(service.DeviceCreate(request)));
        app.MapPost($"{v}/devices/admit", (AdmitRequest request) => Outcome(service.DeviceAdmit(request)));
        app.MapPost($"{v}/devices/rename", (DeviceRenameRequest request) => Outcome(service.DeviceRename(request)));
        app.MapPost($"{v}/devices/rotate", (DeviceRotateRequest request) => Outcome(service.DeviceRotate(request)));
        app.MapPost($"{v}/devices/recovery", (DeviceRecoveryRequest request) => Outcome(service.DeviceRecovery(request)));
        app.MapPost($"{v}/devices/revoke", (RevokeRequest request) => Outcome(service.DeviceRevoke(request)));
        app.MapGet($"{v}/releases", () => Results.Json(service.Releases(), ProtocolJson.Options));
        app.MapGet($"{v}/documents", () => Results.Json(service.Documents(), ProtocolJson.Options));
        app.MapPost($"{v}/documents/pin", (DocumentPinRequest request) =>
        {
            DocumentPinResponse response = service.DocumentPin(request);
            return Results.Json(response, ProtocolJson.Options, statusCode: response.Written is not null ? 200 : 422);
        });
        app.MapPost($"{v}/documents/{{id}}/reviews", (string id, DocumentReviewRequest request) => Outcome(service.DocumentReview(id, request)));
        app.MapGet($"{v}/asks", (string? to) => Results.Json(service.Asks(string.IsNullOrWhiteSpace(to) ? null : to), ProtocolJson.Options));
        app.MapGet($"{v}/operator/actions", (string? recipient, string? assignment, string? progress, string? status, string? decision, string? result, string? handoff) =>
            Results.Json(service.OperatorActions(recipient, assignment, new CommunicationValues(progress, status, decision, result, handoff)), ProtocolJson.Options));
        app.MapPost($"{v}/operator/record", async (OperatorRecordRequest request, HttpContext context) => Results.Json(await service.OperatorRecordAsync(request, context.RequestAborted).ConfigureAwait(false), ProtocolJson.Options));
        app.MapGet($"{v}/goals", () => Results.Json(service.Goals(), ProtocolJson.Options));
        app.MapPost($"{v}/goals", (GoalProposeRequest request) => Outcome(service.GoalPropose(request)));
        app.MapGet($"{v}/goals/{{id}}", (string id) => service.Goal(id) is GoalResponse goal ? Results.Json(goal, ProtocolJson.Options) : Results.NotFound());
        app.MapGet($"{v}/goals/{{id}}/next", (string id) => service.GoalNext(id) is NextResponse next ? Results.Json(next, ProtocolJson.Options) : Results.NotFound());
        app.MapPost($"{v}/goals/{{id}}/step", (string id, GoalStepRequest request) => Results.Json(service.GoalStep(id, request), ProtocolJson.Options));
        app.MapPost($"{v}/goals/{{id}}/activate", (string id, GoalActionRequest request) => Outcome(service.GoalActivate(id, request)));
        app.MapPost($"{v}/goals/{{id}}/pause", (string id, GoalActionRequest request) => Outcome(service.GoalPause(id, request)));
        app.MapPost($"{v}/goals/{{id}}/resume", (string id, GoalActionRequest request) => Outcome(service.GoalResume(id, request)));
        app.MapPost($"{v}/goals/{{id}}/stop", (string id, GoalActionRequest request) => Outcome(service.GoalStop(id, request)));
        app.MapPost($"{v}/goals/{{id}}/evaluate", (string id, GoalEvaluateRequest request) => Outcome(service.GoalEvaluate(id, request)));
        app.MapPost($"{v}/goals/{{id}}/accept", (string id, GoalAcceptRequest request) => Outcome(service.GoalAccept(id, request)));
        app.MapGet($"{v}/providers", () => Results.Json(service.Providers(), ProtocolJson.Options));
        app.MapGet($"{v}/launchers", () => Results.Json(service.Launchers(), ProtocolJson.Options));
        app.MapPost($"{v}/harness-qualifications", (HarnessQualifyRequest request) => Outcome(service.QualifyHarness(request)));
        app.MapGet($"{v}/runs", () => Results.Json(service.Runs(), ProtocolJson.Options));
        app.MapPost($"{v}/runs", (RunStartRequest request) => Results.Json(service.RunStart(request), ProtocolJson.Options));
        app.MapPost($"{v}/runs/{{id}}/stop", (string id, RunStopRequest request) => Results.Json(service.RunStop(id, request), ProtocolJson.Options));
        app.MapPost($"{v}/work/{{key}}/move-plan", (string key, WorkMovePlanRequest request) => Results.Json(service.WorkMovePlan(key, request), ProtocolJson.Options));
        app.MapPost($"{v}/releases/withdraw", (ReleaseWithdrawRequest request) => Results.Json(service.ReleaseWithdraw(request), ProtocolJson.Options));
        app.MapPost($"{v}/releases/submit", (ReleaseSubmitRequest request) => Results.Json(service.ReleaseSubmit(request), ProtocolJson.Options));
        app.MapPost($"{v}/releases/observe", (ReleaseObserveRequest request) => Results.Json(service.ReleaseObserve(request), ProtocolJson.Options));
        app.MapPost($"{v}/changes/push", (ChangesPushRequest request) => Results.Json(service.ChangesPush(request), ProtocolJson.Options));
        app.MapPost($"{v}/changes/open", (ChangesOpenRequest request) => Results.Json(service.ChangesOpen(request), ProtocolJson.Options));
        app.MapPost($"{v}/changes/merge", (ChangesMergeRequest request) => Results.Json(service.ChangesMerge(request), ProtocolJson.Options));
        app.MapPost($"{v}/changes/observe", (ChangesObserveRequest request) => Results.Json(service.ChangesObserve(request), ProtocolJson.Options));
        app.MapPost($"{v}/packages/observe", async (PackagesObserveRequest request, CancellationToken cancellationToken) => Results.Json(await service.PackagesObserveAsync(request, cancellationToken).ConfigureAwait(false), ProtocolJson.Options));
        app.MapPost($"{v}/packages/unlist", (PackagesUnlistRequest request) => Results.Json(service.PackagesUnlist(request), ProtocolJson.Options));
        app.MapPost($"{v}/packages/deprecate", (PackagesDeprecateRequest request) => Results.Json(service.PackagesDeprecate(request), ProtocolJson.Options));

        app.MapPost($"{v}/estate/upgrade", (UpgradeRequest request) =>
        {
            (UpgradeResponse? plan, Problem? problem) = service.Upgrade(request);
            return plan is not null ? Results.Json(plan, ProtocolJson.Options) : Results.Json(problem, ProtocolJson.Options, statusCode: StatusCodes.Status409Conflict);
        });
        app.MapPost($"{v}/estate/export", (ExportRequest request) =>
        {
            (ExportResponse? export, Problem? problem) = service.Export(request);
            return export is not null ? Results.Json(export, ProtocolJson.Options) : Results.Json(problem, ProtocolJson.Options, statusCode: StatusCodes.Status409Conflict);
        });
        app.MapGet($"{v}/reconcile", () => Results.Json(service.Reconcile(), ProtocolJson.Options));
        app.MapGet($"{v}/recover/inspect", () => Results.Text(service.RecoverInspect(), "text/plain; charset=utf-8"));
        app.MapGet($"{v}/objects", () => Results.Json(service.Objects(), ProtocolJson.Options));
        app.MapPost($"{v}/objects", async (HttpRequest request) =>
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer).ConfigureAwait(false);
            (ObjectStored? stored, Problem? problem) = service.ObjectPut(buffer.ToArray());
            return stored is not null ? Results.Json(stored, ProtocolJson.Options) : Results.Json(problem, ProtocolJson.Options, statusCode: StatusCodes.Status413PayloadTooLarge);
        });
        app.MapGet($"{v}/objects/verify", () => Results.Json(service.ObjectsVerify(), ProtocolJson.Options));
        app.MapGet($"{v}/objects/{{hash}}", (string hash) => service.ObjectGet(hash) is byte[] bytes ? Results.Bytes(bytes, "application/octet-stream") : Results.NotFound());
        app.MapGet(OpenApiDocument.Path, () => Results.Text(OpenApiDocument.Json, "application/json; charset=utf-8"));
        app.MapPost($"{v}/shutdown", () =>
        {
            shutdown();
            return Results.Accepted();
        });
    }

    private static IResult Outcome(WriteOutcome outcome) =>
        outcome.Written is not null
            ? Results.Json(outcome, ProtocolJson.Options)
            : Results.Json(outcome, ProtocolJson.Options, statusCode: outcome.Problem?.Code == "not-found" ? StatusCodes.Status404NotFound : outcome.Problem?.Code == "conflict" ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity);
}
