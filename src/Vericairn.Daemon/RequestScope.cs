// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>
/// What a device-signed request may do once the runtime has bound it, from 0.6.0: it acts for its device's own principal only. The
/// check reads the request object the operation will receive, after binding, never the body a second time, so no encoding, spelling
/// or repetition in the body can make the check and the operation read different principals. A request of a type this does not know
/// is refused, so a new operation is held to a principal before it can be reached by a device. A write carries the device's identity
/// and verified key to the service, which rechecks its standing under the estate's write lock. Requests over the local socket carry
/// no device, and the operating system's identity of their caller stands for them as before.
/// </summary>
internal sealed class RequestScope(EstateService service, LocalNetworkLedger ledger, Action<string>? log = null, Action? beforeWrite = null) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (!context.HttpContext.Items.TryGetValue(RequestAuthenticator.DeviceItem, out object? item) || item is not AuthenticatedDevice device)
        {
            return await next(context).ConfigureAwait(false);
        }

        string operation = RequestAuthenticator.Operation(context.HttpContext);
        if (Refusal(operation, device) is string machine)
        {
            return Denied(context.HttpContext, device, machine);
        }

        foreach (object? argument in context.Arguments)
        {
            if (Refusal(argument, device) is string refusal)
            {
                return Denied(context.HttpContext, device, refusal);
            }
        }

        // These reads carry their input in a POST body, with no signing key or write ledger needed.
        bool write = HttpMethods.IsPost(context.HttpContext.Request.Method) &&
            !operation.Equals("POST /v1/resources/read", StringComparison.OrdinalIgnoreCase) &&
            !operation.Equals("POST /v1/releases/check", StringComparison.OrdinalIgnoreCase) &&
            !operation.Equals("POST /v1/resume", StringComparison.OrdinalIgnoreCase) &&
            !operation.Equals("POST /v1/templates/render", StringComparison.OrdinalIgnoreCase) &&
            !operation.Equals("POST /v1/estate-policies/hash", StringComparison.OrdinalIgnoreCase);
        if (!write)
        {
            using EstateService.RequestDeviceScope request = service.ForDeviceRequest(device.Identity, device.PublicKey, device.LocalNetwork ? RequestAuthenticator.LocalNetworkProfile : null);
            return await next(context).ConfigureAwait(false);
        }

        if (!device.LocalNetwork)
        {
            return await WriteAsync(context, next, device).ConfigureAwait(false);
        }

        // A write a paired device asks for is signed by a device of its principal that this machine holds: never by one whose every
        // signature needs its owner's presence here, which would ask the person at this machine to approve what someone else sent.
        if (service.PresenceRefusal(device.Principal) is string presence)
        {
            return Denied(context.HttpContext, device, presence);
        }

        // Which paired device asked is kept before anything is written, flushed to disk; when it cannot be, nothing is written.
        string at = Records.Model.RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
        string id;
        try
        {
            id = ledger.Intend(at, device.Identity, device.Principal, operation);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"refused {operation} for device {device.Identity}: the local-network ledger cannot be written: {exception.Message}");
            return Results.Content(JsonSerializer.Serialize(new Problem("refused", $"The runtime cannot keep which paired device asks for this write, so it writes nothing: {exception.Message}", []), ProtocolJson.Options), "application/json", Encoding.UTF8, StatusCodes.Status503ServiceUnavailable);
        }

        object? result;
        try
        {
            result = await WriteAsync(context, next, device).ConfigureAwait(false);
        }
        catch
        {
            Keep(id, at, device, operation, null, failed: true);
            throw;
        }

        Keep(id, at, device, operation, result, failed: false);
        return result;
    }

    /// <summary>Carries the verified device and key to each write, where the service rechecks them under its gate.</summary>
    private async ValueTask<object?> WriteAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next, AuthenticatedDevice device)
    {
        using EstateService.RequestDeviceScope request = service.ForDeviceRequest(device.Identity, device.PublicKey, device.LocalNetwork ? RequestAuthenticator.LocalNetworkProfile : null);
        beforeWrite?.Invoke();
        object? result = await next(context).ConfigureAwait(false);
        return request.Refusal is Problem denied ? Denied(context.HttpContext, device, denied.Message) : result;
    }

    /// <summary>
    /// Keeps the outcome of a write a paired device asked for, under its intent's id: the record it produced, or for a run's stop the
    /// run's id, or that it failed. An outcome that cannot be kept is logged; the start after closes its intent as interrupted.
    /// </summary>
    private void Keep(string id, string at, AuthenticatedDevice device, string operation, object? result, bool failed)
    {
        int status = result is IStatusCodeHttpResult { StatusCode: int code } ? code : StatusCodes.Status200OK;
        object? value = result is IValueHttpResult { Value: object answered } ? answered : null;
        RecordWritten? written = value switch
        {
            WriteOutcome { Written: RecordWritten record } => record,
            WorkProgressResponse { Ok: true, Written: RecordWritten record } => record,
            RunSettleResponse { Operation: RecordWritten record } => record,
            TakeoverResponse { Ok: true, Assignment: RecordWritten assignment } => assignment,
            OperatorRecordResponse { Record: RecordWritten record } => record,
            OperatorCloseResponse close => close.Written.Count == 0 ? null : close.Written[^1],
            DocumentPassAssignResponse pass => pass.Written.Count == 0 ? null : pass.Written[^1],
            PackagesResponse packages => packages.Operation ?? (packages.Packages.Count > 0 ? packages.Packages[^1].Observation : packages.Intent),
            WorkMovePlanResponse move => move.Written.Count == 0 ? null : move.Written[^1],
            ReleaseReviewBriefResponse review => review.Assignment,
            ReleaseResponse release when operation == "POST /v1/releases/withdraw" => release.Operation ?? release.Release,
            _ => null,
        };
        string? run = value is RunStopResponse { Ok: true } stop ? stop.Run : null;
        bool done = !failed && status < 300 && value is not WriteOutcome { Ok: false } and not WorkProgressResponse { Ok: false } and not RunStopResponse { Ok: false } and not TakeoverResponse { Ok: false } and not OperatorRecordResponse { Ok: false } and not PackagesResponse { Ok: false };
        if (value is WorkMovePlanResponse { Ok: false } or RunSettleResponse { Ok: false } or OperatorCloseResponse { Ok: false } or ReleaseReviewBriefResponse { Ok: false } or DocumentPassAssignResponse { Ok: false } || operation == "POST /v1/releases/withdraw" && value is ReleaseResponse { Ok: false }) done = false;
        try
        {
            ledger.Settle(new LocalNetworkWrite(at, device.Identity, device.Principal, operation, written?.Id ?? run, written?.Hash, written?.Path, id, done ? "written" : "failed"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"the outcome of {operation} for device {device.Identity} ({id}) could not be kept in the local-network ledger: {exception.Message}");
        }
    }

    /// <summary>
    /// Each served operation's scope: a bound request rule, or no principal named in its arguments (read views, raw bytes and
    /// bodyless operations). This table is independent of the route registry: a newly added route is refused until its scope is declared.
    /// </summary>
    private static readonly Dictionary<string, Type?> Operations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GET /v1/companions"] = null,
        ["GET /v1/companions/render"] = typeof(CompanionRequest),
        ["GET /v1/companions/sources"] = typeof(CompanionRequest),
        ["GET /v1/companions/compare"] = typeof(CompanionCompareRequest),
        ["GET /v1/estate-policies"] = null,
        ["GET /v1/estate-policies/kinds"] = null,
        ["POST /v1/estate-policies/hash"] = typeof(PolicyHashRequest),
        ["GET /v1/estate-policies/evaluate"] = null,
        ["GET /v1/estate-policies/{key}/view"] = null,
        ["GET /v1/writing-exceptions"] = null,
        ["GET /v1/writing-exceptions/{id}"] = null,
        ["GET /v1/estate-policies/access"] = null,
        ["POST /v1/estate-policies"] = typeof(PolicyChangeRequest),
        ["POST /v1/estate-policies/{key}/revisions"] = typeof(PolicyChangeRequest),
        ["POST /v1/estate-policies/{key}/retire"] = typeof(PolicyChangeRequest),
        ["POST /v1/estate-policies/{key}/exceptions"] = typeof(PolicyChangeRequest),
        ["POST /v1/estate-policies/{key}/exceptions/{id}/revoke"] = typeof(PolicyChangeRequest),
        ["POST /v1/writing-exceptions"] = typeof(PolicyChangeRequest),
        ["POST /v1/writing-exceptions/{id}/remove"] = typeof(WritingExceptionRemoveRequest),
        ["POST /v1/work/{key}/placement"] = typeof(WorkPlaceRequest),
        ["GET /v1/health"] = null,
        ["GET /v1/communication"] = null,
        ["GET /v1/prompts"] = null,
        ["GET /v1/resources"] = null,
        ["POST /v1/resources/read"] = typeof(ResourceRequest),
        ["POST /v1/prompts"] = typeof(PromptSaveRequest),
        ["POST /v1/prompts/preferences"] = typeof(PromptPreferenceRequest),
        ["POST /v1/prompts/copy"] = typeof(PromptCopyRequest),
        ["POST /v1/prompts/use"] = typeof(PromptUseRequest),
        ["POST /v1/communication"] = typeof(CommunicationSetRequest),
        ["POST /v1/communication/delegations"] = typeof(CommunicationDelegateRequest),
        ["GET /v1/status"] = null,
        ["POST /v1/status/observations"] = typeof(EstateObserveRequest),
        ["GET /v1/progress"] = null,
        ["GET /v1/handoff"] = null,
        ["POST /v1/resume"] = typeof(HandoffPackage),
        ["GET /v1/context/work/{key}"] = null,
        ["GET /v1/context/work/{key}/brief"] = null,
        ["GET /v1/context/original/{pin}"] = null,
        ["GET /v1/context/{manifest}/sources/{source}"] = null,
        ["GET /v1/validate"] = null,
        ["GET /v1/check"] = null,
        ["GET /v1/records/{id}"] = null,
        ["GET /v1/records"] = null,
        ["POST /v1/records"] = typeof(NewRecordRequest),
        ["POST /v1/decisions/new"] = typeof(DecisionNewRequest),
        ["GET /v1/decisions"] = null,
        ["GET /v1/decisions/{id}"] = null,
        ["GET /v1/work/{key}/brief"] = null,
        ["GET /v1/work/{key}"] = null,
        ["POST /v1/work/new"] = typeof(WorkNewRequest),
        ["POST /v1/work/assign"] = typeof(AssignRequest),
        ["POST /v1/work/attempt"] = typeof(AttemptRequest),
        ["POST /v1/work/progress"] = typeof(WorkProgressRequest),
        ["POST /v1/work/result"] = typeof(ResultRequest),
        ["POST /v1/work/review"] = typeof(ResultReviewRequest),
        ["POST /v1/work/amend"] = typeof(AmendRequest),
        ["POST /v1/work/takeover"] = typeof(TakeoverRequest),
        ["GET /v1/devices"] = null,
        ["POST /v1/devices"] = typeof(DeviceCreateRequest),
        ["POST /v1/devices/admit"] = typeof(AdmitRequest),
        ["POST /v1/devices/rename"] = typeof(DeviceRenameRequest),
        ["POST /v1/devices/rotate"] = typeof(DeviceRotateRequest),
        ["POST /v1/devices/revoke"] = typeof(RevokeRequest),
        ["POST /v1/devices/recovery"] = typeof(DeviceRecoveryRequest),
        ["GET /v1/releases"] = null,
        ["GET /v1/release-bases/{hash}"] = null,
        ["GET /v1/documents"] = null,
        ["GET /v1/templates"] = null,
        ["POST /v1/templates/render"] = typeof(TemplateRenderRequest),
        ["GET /v1/documents/{id}/sections"] = null,
        ["GET /v1/documents/{id}/sections/{section}/brief"] = null,
        ["POST /v1/documents/new"] = typeof(DocumentNewRequest),
        ["GET /v1/documents/pass/{version}"] = null,
        ["POST /v1/documents/pass/{version}/assignments"] = typeof(DocumentPassAssignRequest),
        ["POST /v1/documents/adopt"] = typeof(DocumentAdoptRequest),
        ["POST /v1/documents/{id}/reviews"] = typeof(DocumentReviewRequest),
        ["POST /v1/documents/pin"] = typeof(DocumentPinRequest),
        ["GET /v1/asks"] = null,
        ["GET /v1/operator/actions"] = null,
        ["GET /v1/operator/requests"] = null,
        ["POST /v1/operator/requests"] = typeof(OperatorRequestWriteRequest),
        ["POST /v1/operator/asks/{id}/carry"] = typeof(OperatorRequestCarryRequest),
        ["POST /v1/operator/requests/{id}/done"] = typeof(OperatorRequestCloseRequest),
        ["POST /v1/operator/requests/{id}/dismiss"] = typeof(OperatorRequestCloseRequest),
        ["POST /v1/operator/record"] = typeof(OperatorRecordRequest),
        ["GET /v1/operator/close"] = null,
        ["POST /v1/operator/close"] = typeof(OperatorCloseRequest),
        ["GET /v1/goals"] = null,
        ["POST /v1/goals"] = typeof(GoalProposeRequest),
        ["GET /v1/goals/{id}"] = null,
        ["GET /v1/goals/{id}/next"] = null,
        ["POST /v1/goals/{id}/step"] = typeof(GoalStepRequest),
        ["POST /v1/goals/{id}/activate"] = typeof(GoalActionRequest),
        ["POST /v1/goals/{id}/pause"] = typeof(GoalActionRequest),
        ["POST /v1/goals/{id}/resume"] = typeof(GoalActionRequest),
        ["POST /v1/goals/{id}/stop"] = typeof(GoalActionRequest),
        ["POST /v1/goals/{id}/evaluate"] = typeof(GoalEvaluateRequest),
        ["POST /v1/goals/{id}/accept"] = typeof(GoalAcceptRequest),
        ["GET /v1/providers"] = null,
        ["GET /v1/launchers"] = null,
        ["GET /v1/runs"] = null,
        ["POST /v1/runs"] = typeof(RunStartRequest),
        ["POST /v1/harness-qualifications"] = typeof(HarnessQualifyRequest),
        ["POST /v1/runs/{id}/stop"] = typeof(RunStopRequest),
        ["POST /v1/runs/{id}/settle"] = typeof(RunSettleRequest),
        ["POST /v1/work/{key}/move-plan"] = typeof(WorkMovePlanRequest),
        ["POST /v1/releases/withdraw"] = typeof(ReleaseWithdrawRequest),
        ["GET /v1/releases/{version}/review-brief"] = null,
        ["POST /v1/releases/{version}/review-brief"] = typeof(ReleaseReviewAssignRequest),
        ["POST /v1/releases/check"] = typeof(ReleaseSubmitRequest),
        ["POST /v1/releases/submit"] = typeof(ReleaseSubmitRequest),
        ["POST /v1/releases/observe"] = typeof(ReleaseObserveRequest),
        ["POST /v1/changes/push"] = typeof(ChangesPushRequest),
        ["POST /v1/changes/open"] = typeof(ChangesOpenRequest),
        ["POST /v1/changes/merge"] = typeof(ChangesMergeRequest),
        ["POST /v1/changes/observe"] = typeof(ChangesObserveRequest),
        ["POST /v1/packages/observe"] = typeof(PackagesObserveRequest),
        ["POST /v1/packages/unlist"] = typeof(PackagesUnlistRequest),
        ["POST /v1/packages/deprecate"] = typeof(PackagesDeprecateRequest),

        ["POST /v1/estate/upgrade"] = typeof(UpgradeRequest),
        ["POST /v1/estate/export"] = typeof(ExportRequest),
        ["GET /v1/reconcile"] = null,
        ["GET /v1/recover/inspect"] = null,
        ["GET /v1/objects"] = null,
        ["POST /v1/objects"] = null,
        ["GET /v1/objects/verify"] = null,
        ["GET /v1/objects/{hash}"] = null,
        ["POST /v1/shutdown"] = null,
        ["POST /v1/pairings"] = typeof(PairingIssueRequest),
        ["POST /v1/pairing/hello"] = typeof(PairingHello),
        ["POST /v1/pairing/confirm"] = typeof(PairingConfirm),
        ["GET /v1/runtime/identity"] = null,
        ["POST /v1/runtime/certificate"] = typeof(CertificateRotateRequest),
        ["GET " + OpenApiDocument.Path] = null,
    };

    /// <summary>Whether an operation has a scope rule, including the rule of its bound request type.</summary>
    internal static bool Covers(string operation) => Operations.TryGetValue(operation, out Type? type) && (type is null || Covers(type));

    /// <summary>
    /// The operations a device-signed request may ask only for an administrator: stopping the runtime and replacing its certificate or
    /// identity. Over the local network they are refused before this, as operations on the runtime's own machine.
    /// </summary>
    private string? Refusal(string operation, AuthenticatedDevice device) =>
        !Covers(operation)
            ? $"{operation} has no request scope rule and is refused."
            : operation is "POST /v1/shutdown" or "POST /v1/runtime/certificate" && !service.Administers(device.Principal)
            ? $"{operation} is an administrator's: device {device.Identity} acts for {device.Principal}, which is neither an operator nor holds the 'administer' scope. On the runtime's machine its operating-system user reaches it over the local socket."
            : null;

    /// <summary>
    /// Each request type an operation binds, with how it names the principal acting. Every request type of the protocol is here: a
    /// test holds the table to the protocol, and a device-signed request of a type that is not is refused.
    /// </summary>
    private static readonly Dictionary<Type, Func<RequestScope, object, AuthenticatedDevice, string?>> Rules = new()
    {
        [typeof(CompanionRequest)] = (_, _, _) => null,
        [typeof(CompanionCompareRequest)] = (_, _, _) => null,
        [typeof(PolicyHashRequest)] = (_, _, _) => null,
        [typeof(TemplateRenderRequest)] = (_, _, _) => null,
        [typeof(PolicyChangeRequest)] = (_, r, d) => Acting(((PolicyChangeRequest)r).By, d),
        [typeof(WritingExceptionRemoveRequest)] = (_, r, d) => Acting(((WritingExceptionRemoveRequest)r).By, d),
        [typeof(WorkPlaceRequest)] = (_, r, d) => Acting(((WorkPlaceRequest)r).By, d),
        [typeof(HandoffPackage)] = (_, _, _) => null,
        [typeof(CommunicationSetRequest)] = (_, r, d) => Acting(((CommunicationSetRequest)r).By, d),
        [typeof(PromptSaveRequest)] = (_, r, d) => Acting(((PromptSaveRequest)r).By, d),
        [typeof(PromptPreferenceRequest)] = (_, r, d) => Acting(((PromptPreferenceRequest)r).By, d),
        [typeof(PromptCopyRequest)] = (_, r, d) => Acting(((PromptCopyRequest)r).By, d),
        [typeof(PromptUseRequest)] = (_, r, d) => Acting(((PromptUseRequest)r).By, d),
        [typeof(PromptQuery)] = (_, _, _) => null,
        [typeof(ResourceRequest)] = (_, _, _) => null,
        [typeof(CommunicationDelegateRequest)] = (_, r, d) => Acting(((CommunicationDelegateRequest)r).By, d),
        [typeof(EstateObserveRequest)] = (_, r, d) => Acting(((EstateObserveRequest)r).By, d),
        [typeof(NewRecordRequest)] = (_, r, d) => Acting(((NewRecordRequest)r).By, d) ?? DeviceRecord(((NewRecordRequest)r).Type, d),
        [typeof(OperatorRecordRequest)] = (_, r, d) => Acting(((OperatorRecordRequest)r).By, d) ?? DeviceRecord(((OperatorRecordRequest)r).Type, d),
        [typeof(OperatorCloseRequest)] = (_, r, d) => Acting(((OperatorCloseRequest)r).By, d),
        [typeof(OperatorRequestWriteRequest)] = (_, r, d) => Acting(((OperatorRequestWriteRequest)r).By, d),
        [typeof(OperatorRequestCarryRequest)] = (_, r, d) => Acting(((OperatorRequestCarryRequest)r).By, d),
        [typeof(OperatorRequestCloseRequest)] = (_, r, d) => Acting(((OperatorRequestCloseRequest)r).By, d),
        [typeof(GoalStepRequest)] = (_, r, d) => Acting(((GoalStepRequest)r).By, d),
        [typeof(TakeoverRequest)] = (_, r, d) => Acting(((TakeoverRequest)r).By, d),
        [typeof(WorkNewRequest)] = (_, r, d) => Acting(((WorkNewRequest)r).By, d),
        [typeof(AssignRequest)] = (_, r, d) => Acting(((AssignRequest)r).By, d),
        [typeof(AttemptRequest)] = (_, r, d) => Acting(((AttemptRequest)r).By, d),
        [typeof(WorkProgressRequest)] = (_, r, d) => Acting(((WorkProgressRequest)r).By, d),
        [typeof(ResultRequest)] = (_, r, d) => Acting(((ResultRequest)r).By, d),
        [typeof(ResultReviewRequest)] = (_, r, d) => Acting(((ResultReviewRequest)r).By, d),
        [typeof(AmendRequest)] = (_, r, d) => Acting(((AmendRequest)r).By, d),
        [typeof(DeviceCreateRequest)] = (_, r, d) => Acting(((DeviceCreateRequest)r).Principal, d),
        [typeof(AdmitRequest)] = (_, r, d) => Acting(((AdmitRequest)r).By, d) ?? Recovery(((AdmitRequest)r).Recovery, d),
        [typeof(RevokeRequest)] = (_, r, d) => Acting(((RevokeRequest)r).By, d) ?? Recovery(((RevokeRequest)r).Recovery, d),
        [typeof(DeviceRecoveryRequest)] = (_, r, d) => Acting(((DeviceRecoveryRequest)r).Principal, d),
        [typeof(DeviceRenameRequest)] = (scope, r, d) => scope.OwnDevice(((DeviceRenameRequest)r).Device, d),
        [typeof(DeviceRotateRequest)] = (scope, r, d) => scope.OwnDevice(((DeviceRotateRequest)r).Device, d),
        [typeof(DocumentReviewRequest)] = (_, r, d) => Acting(((DocumentReviewRequest)r).By, d),
        [typeof(DecisionNewRequest)] = (_, r, d) => Acting(((DecisionNewRequest)r).By, d),
        [typeof(DocumentPinRequest)] = (_, r, d) => Acting(((DocumentPinRequest)r).By, d),
        [typeof(DocumentNewRequest)] = (_, r, d) => Acting(((DocumentNewRequest)r).By, d),
        [typeof(DocumentPassAssignRequest)] = (_, r, d) => Acting(((DocumentPassAssignRequest)r).By, d),
        [typeof(DocumentAdoptRequest)] = (_, r, d) => Acting(((DocumentAdoptRequest)r).By, d),
        [typeof(GoalProposeRequest)] = (_, r, d) => Acting(((GoalProposeRequest)r).By, d),
        [typeof(GoalActionRequest)] = (_, r, d) => Acting(((GoalActionRequest)r).By, d),
        [typeof(GoalEvaluateRequest)] = (_, r, d) => Acting(((GoalEvaluateRequest)r).By, d),
        [typeof(GoalAcceptRequest)] = (_, r, d) => Acting(((GoalAcceptRequest)r).By, d),
        [typeof(WorkMovePlanRequest)] = (_, r, d) => Acting(((WorkMovePlanRequest)r).By, d),
        [typeof(ReleaseWithdrawRequest)] = (_, r, d) => Acting(((ReleaseWithdrawRequest)r).By, d),
        [typeof(ReleaseReviewAssignRequest)] = (_, r, d) => Acting(((ReleaseReviewAssignRequest)r).By, d),
        [typeof(ReleaseSubmitRequest)] = (_, r, d) => Acting(((ReleaseSubmitRequest)r).By, d),
        [typeof(ReleaseObserveRequest)] = (_, r, d) => Acting(((ReleaseObserveRequest)r).By, d),
        [typeof(ChangesPushRequest)] = (_, r, d) => Acting(((ChangesPushRequest)r).By, d),
        [typeof(ChangesOpenRequest)] = (_, r, d) => Acting(((ChangesOpenRequest)r).By, d),
        [typeof(ChangesMergeRequest)] = (_, r, d) => Acting(((ChangesMergeRequest)r).By, d),
        [typeof(ChangesObserveRequest)] = (_, r, d) => Acting(((ChangesObserveRequest)r).By, d),
        [typeof(PackagesObserveRequest)] = (_, r, d) => Acting(((PackagesObserveRequest)r).By, d),
        [typeof(PackagesUnlistRequest)] = (_, r, d) => Acting(((PackagesUnlistRequest)r).By, d),
        [typeof(PackagesDeprecateRequest)] = (_, r, d) => Acting(((PackagesDeprecateRequest)r).By, d),

        [typeof(UpgradeRequest)] = (_, r, d) => ((UpgradeRequest)r).Check ? Optional(((UpgradeRequest)r).By, d) : Acting(((UpgradeRequest)r).By, d) ?? Upgrade(d),
        [typeof(ExportRequest)] = (scope, r, d) => Acting(((ExportRequest)r).By, d) ?? scope.Administrator("An export writes the whole estate to a path on the runtime's machine", d),
        [typeof(RunStartRequest)] = (_, r, d) => Acting(((RunStartRequest)r).By, d),
        [typeof(HarnessQualifyRequest)] = (_, r, d) => Acting(((HarnessQualifyRequest)r).By, d),
        [typeof(RunStopRequest)] = (_, r, d) => Acting(((RunStopRequest)r).By, d),
        [typeof(RunSettleRequest)] = (_, r, d) => Acting(((RunSettleRequest)r).By, d),
        [typeof(PairingIssueRequest)] = (_, r, d) => Acting(((PairingIssueRequest)r).By, d),

        // No principal acts in these: the rotation's authority is the operation's, and the pairing exchange is the code's.
        [typeof(CertificateRotateRequest)] = (_, _, _) => null,
        [typeof(PairingHello)] = (_, _, _) => null,
        [typeof(PairingConfirm)] = (_, _, _) => null,
    };

    /// <summary>Whether a request type is one this holds to a principal; a device-signed request of any other protocol type is refused.</summary>
    internal static bool Covers(Type type) => Rules.ContainsKey(type);

    /// <summary>
    /// Why a bound argument may not proceed for the device, or null. A route's own values (text, numbers) and the request's context
    /// pass; a request object is held by its rule, and one of any type the rules do not name, wherever it is declared, is refused.
    /// </summary>
    private string? Refusal(object? argument, AuthenticatedDevice device)
    {
        if (argument is null or string or HttpContext or HttpRequest or CancellationToken || argument.GetType().IsPrimitive || argument.GetType().IsEnum)
        {
            return null;
        }

        Type type = argument.GetType();
        return Rules.TryGetValue(type, out Func<RequestScope, object, AuthenticatedDevice, string?>? rule)
            ? rule(this, argument, device)
            : $"A device-signed {type.Name} names no principal the runtime can hold it to, so it is refused.";
    }

    /// <summary>The principal a request names as acting must be the device's.</summary>
    private static string? Acting(string? acting, AuthenticatedDevice device) =>
        string.IsNullOrEmpty(acting)
            ? $"This request names no principal acting; a request signed by device {device.Identity} acts for {device.Principal}."
            : Optional(acting, device);

    /// <summary>A principal the request may leave out, but when it names one it is the device's.</summary>
    private static string? Optional(string? acting, AuthenticatedDevice device) =>
        acting is null || string.Equals(acting, device.Principal, StringComparison.Ordinal)
            ? null
            : $"Device {device.Identity} acts for its own principal {device.Principal}; this request names {acting} as acting. A principal's requests are signed by its own device{(device.LocalNetwork ? string.Empty : ", or sent over the local socket by the runtime machine's user")}.";

    /// <summary>What a device-signed request asks only of an administrator: the operator, or a principal holding the 'administer' scope.</summary>
    private string? Administrator(string what, AuthenticatedDevice device) =>
        service.Administers(device.Principal)
            ? null
            : $"{what}, and is an administrator's: device {device.Identity} acts for {device.Principal}, which is neither an operator nor holds the 'administer' scope.";

    /// <summary>A device renames or rotates only devices of its own principal.</summary>
    private string? OwnDevice(string? named, AuthenticatedDevice device)
    {
        if (string.IsNullOrEmpty(named) || service.Standing(named) is not DeviceStanding standing)
        {
            return null;
        }

        return string.Equals(standing.Principal, device.Principal, StringComparison.Ordinal)
            ? null
            : $"Device {standing.Identity} is a device of {standing.Principal}; a request signed by device {device.Identity} reaches only the devices of {device.Principal}.";
    }

    /// <summary>A device record is written on the machine that holds its key; over the local network it is refused, as /v1/devices and its renames are.</summary>
    private static string? DeviceRecord(string? type, AuthenticatedDevice device) =>
        device.LocalNetwork && string.Equals(type, "device", StringComparison.Ordinal)
            ? "A device record is written on the machine that holds its key, and is refused over the local network."
            : null;

    /// <summary>Applying an upgrade rewrites the estate's pin and applies an irreversible migration on the runtime's machine; over the local network only its plan is given.</summary>
    private static string? Upgrade(AuthenticatedDevice device) =>
        device.LocalNetwork
            ? "Applying an upgrade acts on the runtime's own machine and is refused over the local network; its plan (check) is not."
            : null;

    /// <summary>A recovery key is a file on the runtime's machine, used there only.</summary>
    private static string? Recovery(string? recovery, AuthenticatedDevice device) =>
        device.LocalNetwork && recovery is not null
            ? "A recovery key is a file on the runtime's own machine and is used there only; it is refused over the local network."
            : null;

    private static IResult Denied(HttpContext context, AuthenticatedDevice device, string message)
    {
        if (device.LocalNetwork)
        {
            context.Response.Headers.Connection = "close";
        }

        return Results.Content(JsonSerializer.Serialize(new Problem("denied", message, []), ProtocolJson.Options), "application/json", Encoding.UTF8, StatusCodes.Status401Unauthorized);
    }
}
