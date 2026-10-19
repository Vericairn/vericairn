// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <summary>The cancellable PolicyAccess operation, preserving uncertainty after dispatch.</summary>
    public Task<IReadOnlyList<string>> PolicyAccessAsync(string principal, CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<IReadOnlyList<string>>(nameof(PolicyAccess), async token => (await GetAsync<List<string>>("/v1/estate-policies/access?principal=" + Uri.EscapeDataString(principal), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(PolicyAccess)), cancellationToken);

    /// <summary>The cancellable PolicyDeclare operation, preserving uncertainty after dispatch.</summary>
    public Task<WriteOutcome> PolicyDeclareAsync(PolicyChangeRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<WriteOutcome>(nameof(PolicyDeclare), async token => (await PostAsync<PolicyChangeRequest, WriteOutcome>("/v1/estate-policies", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(PolicyDeclare)), cancellationToken);

    /// <summary>The cancellable PolicyRevise operation, preserving uncertainty after dispatch.</summary>
    public Task<WriteOutcome> PolicyReviseAsync(string key, PolicyChangeRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<WriteOutcome>(nameof(PolicyRevise), async token => (await PostAsync<PolicyChangeRequest, WriteOutcome>("/v1/estate-policies/" + Uri.EscapeDataString(key) + "/revisions", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(PolicyRevise)), cancellationToken);

    /// <summary>The cancellable PolicyRetire operation, preserving uncertainty after dispatch.</summary>
    public Task<WriteOutcome> PolicyRetireAsync(string key, PolicyChangeRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<WriteOutcome>(nameof(PolicyRetire), async token => (await PostAsync<PolicyChangeRequest, WriteOutcome>("/v1/estate-policies/" + Uri.EscapeDataString(key) + "/retire", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(PolicyRetire)), cancellationToken);

    /// <summary>The cancellable PolicyExcept operation, preserving uncertainty after dispatch.</summary>
    public Task<WriteOutcome> PolicyExceptAsync(string key, PolicyChangeRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<WriteOutcome>(nameof(PolicyExcept), async token => (await PostAsync<PolicyChangeRequest, WriteOutcome>("/v1/estate-policies/" + Uri.EscapeDataString(key) + "/exceptions", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(PolicyExcept)), cancellationToken);

    /// <summary>The cancellable PolicyRevokeException operation, preserving uncertainty after dispatch.</summary>
    public Task<WriteOutcome> PolicyRevokeExceptionAsync(string key, string id, PolicyChangeRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<WriteOutcome>(nameof(PolicyRevokeException), async token => (await PostAsync<PolicyChangeRequest, WriteOutcome>("/v1/estate-policies/" + Uri.EscapeDataString(key) + "/exceptions/" + Uri.EscapeDataString(id) + "/revoke", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(PolicyRevokeException)), cancellationToken);

    /// <summary>The cancellable WritingExceptionAdd operation, preserving uncertainty after dispatch.</summary>
    public Task<WriteOutcome> WritingExceptionAddAsync(PolicyChangeRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<WriteOutcome>(nameof(WritingExceptionAdd), async token => (await PostAsync<PolicyChangeRequest, WriteOutcome>("/v1/writing-exceptions", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(WritingExceptionAdd)), cancellationToken);

    /// <summary>The cancellable WritingExceptionRemove operation, preserving uncertainty after dispatch.</summary>
    public Task<WriteOutcome> WritingExceptionRemoveAsync(string id, WritingExceptionRemoveRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<WriteOutcome>(nameof(WritingExceptionRemove), async token => (await PostAsync<WritingExceptionRemoveRequest, WriteOutcome>("/v1/writing-exceptions/" + Uri.EscapeDataString(id) + "/remove", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(WritingExceptionRemove)), cancellationToken);

    /// <summary>The cancellable WorkPlace operation, preserving uncertainty after dispatch.</summary>
    public Task<WriteOutcome> WorkPlaceAsync(string key, WorkPlaceRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<WriteOutcome>(nameof(WorkPlace), async token => (await PostAsync<WorkPlaceRequest, WriteOutcome>("/v1/work/" + Uri.EscapeDataString(key) + "/placement", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(WorkPlace)), cancellationToken);

    /// <summary>The cancellable Policies read, with a response deadline.</summary>
    public Task<PolicyResponse> PoliciesAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<PolicyResponse>(nameof(Policies), async token => (await GetAsync<PolicyResponse>("/v1/estate-policies", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Policies)), cancellationToken);

    /// <summary>The cancellable Policy read, with a response deadline.</summary>
    public Task<PolicyResponse?> PolicyAsync(string key, CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<PolicyResponse?>(nameof(Policy), async token => (await GetAsync<PolicyResponse>("/v1/estate-policies/" + Uri.EscapeDataString(key) + "/view", allowNotFound: true, cancellationToken: token).ConfigureAwait(false)), false, OperationTimeout?.Invoke(nameof(Policy)), cancellationToken);

    /// <summary>The cancellable PolicyKinds read, with a response deadline.</summary>
    public Task<IReadOnlyList<PolicyKindDto>> PolicyKindsAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<IReadOnlyList<PolicyKindDto>>(nameof(PolicyKinds), async token => (await GetAsync<List<PolicyKindDto>>("/v1/estate-policies/kinds", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(PolicyKinds)), cancellationToken);

    /// <summary>The cancellable PolicyEvaluate read, with a response deadline.</summary>
    public Task<PolicyResponse> PolicyEvaluateAsync(string? asOf = null, string? release = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<PolicyResponse>(nameof(PolicyEvaluate), async token => (await GetAsync<PolicyResponse>(PolicyEvaluationPath(asOf, release), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(PolicyEvaluate)), cancellationToken);

    /// <summary>The cancellable WritingExceptions read, with a response deadline.</summary>
    public Task<WritingExceptionsResponse> WritingExceptionsAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<WritingExceptionsResponse>(nameof(WritingExceptions), async token => (await GetAsync<WritingExceptionsResponse>("/v1/writing-exceptions", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(WritingExceptions)), cancellationToken);

    /// <summary>The cancellable WritingException read, with a response deadline.</summary>
    public Task<WritingExceptionsResponse?> WritingExceptionAsync(string id, CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<WritingExceptionsResponse?>(nameof(WritingException), async token => (await GetAsync<WritingExceptionsResponse>("/v1/writing-exceptions/" + Uri.EscapeDataString(id), allowNotFound: true, cancellationToken: token).ConfigureAwait(false)), false, OperationTimeout?.Invoke(nameof(WritingException)), cancellationToken);

    /// <summary>Uses and owns the supplied HTTP transport. Operation deadlines replace its global timeout.</summary>
    public VericairnClient(HttpClient http)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        asyncHttp = http;
        http.Timeout = Timeout.InfiniteTimeSpan;
        Endpoint = new DaemonEndpoint("http", string.Empty, 0, string.Empty, string.Empty, ApiVersion.Current);
        Transport = "http";
    }

    // Legacy calls keep HttpClient's timeout and exact exceptions; async calls use per-operation deadlines.
    // The synchronous client owns this shared handler. Local-network clients use separate pools so that an
    // async handshake's refusal classification can never change a concurrent synchronous request's failure.
    private static HttpClient AsyncTransport(HttpMessageHandler handler, Uri address) =>
        new(handler, disposeHandler: false) { BaseAddress = address, Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Optional deadline override by operation name; null uses that operation's standard deadline.</summary>
    public Func<string, TimeSpan?>? OperationTimeout { get; init; }

    internal TimeProvider OperationClock { get; init; } = TimeProvider.System;

    private Task<T> RunOperationAsync<T>(string operation, Func<CancellationToken, Task<T>> action, bool mayHaveEffect,
        TimeSpan? timeout, CancellationToken cancellationToken) =>
        ClientOperation.RunAsync(operation, action, () => mayHaveEffect, timeout, OperationClock, cancellationToken);

    /// <summary>Checks the local endpoint without blocking the caller. Caller cancellation and operation deadlines are propagated.</summary>
    public static async Task<bool> IsAliveAsync(DaemonEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        try
        {
            using var client = new VericairnClient(endpoint);
            return (await client.HealthAsync(cancellationToken).ConfigureAwait(false)).Api == ApiVersion.Current;
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Net.Sockets.SocketException or IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The cancellable Health operation, with a deadline covering the complete response.</summary>
    public Task<HealthResponse> HealthAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<HealthResponse>(nameof(Health), async token => (await GetAsync<HealthResponse>("/v1/health", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Health)), cancellationToken);

    /// <summary>The cancellable Communication operation, with a deadline covering the complete response.</summary>
    public Task<CommunicationProfile> CommunicationAsync(string? recipient = null, string? assignment = null, CommunicationValues? view = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync<CommunicationProfile>(nameof(Communication), async token =>
        {
            try
            {
                return (await GetAsync<CommunicationProfile>("/v1/communication" + CommunicationQuery(recipient, assignment, view), cancellationToken: token).ConfigureAwait(false))!;
            }
            catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new CommunicationProfile(recipient, assignment, new Dictionary<string, CommunicationValue>(), [], [], Unavailable: "This runtime does not support communication settings.");
            }
        }, false, OperationTimeout?.Invoke(nameof(Communication)), cancellationToken);

    /// <summary>The cancellable CommunicationSet operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> CommunicationSetAsync(CommunicationSetRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(CommunicationSet), async token => (await PostAsync<CommunicationSetRequest, WriteOutcome>("/v1/communication", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(CommunicationSet)), cancellationToken);

    /// <summary>The cancellable CommunicationDelegate operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> CommunicationDelegateAsync(CommunicationDelegateRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(CommunicationDelegate), async token => (await PostAsync<CommunicationDelegateRequest, WriteOutcome>("/v1/communication/delegations", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(CommunicationDelegate)), cancellationToken);

    /// <summary>The cancellable Status operation, with a deadline covering the complete response.</summary>
    public Task<StatusResponse> StatusAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<StatusResponse>(nameof(Status), async token => (await GetAsync<StatusResponse>("/v1/status", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Status)), cancellationToken);

    /// <summary>The cancellable ObserveEstate operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> ObserveEstateAsync(EstateObserveRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(ObserveEstate), async token => (await PostAsync<EstateObserveRequest, WriteOutcome>("/v1/status/observations", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(ObserveEstate)), cancellationToken);

    /// <summary>The cancellable Progress operation, with a deadline covering the complete response.</summary>
    public Task<ProgressResponse> ProgressAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<ProgressResponse>(nameof(Progress), async token => (await GetAsync<ProgressResponse>("/v1/progress", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Progress)), cancellationToken);

    /// <summary>The cancellable Handoff operation, with a deadline covering the complete response.</summary>
    public Task<HandoffResponse> HandoffAsync(long? maxBytes = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync<HandoffResponse>(nameof(Handoff), async token => (await GetAsync<HandoffResponse>("/v1/handoff" + (maxBytes is null ? string.Empty : "?maxBytes=" + maxBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Handoff)), cancellationToken);

    /// <summary>The cancellable ResumeHandoff operation, with a deadline covering the complete response.</summary>
    public Task<ResumeResponse> ResumeHandoffAsync(HandoffPackage package, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ResumeResponse>(nameof(ResumeHandoff), async token => (await PostAsync<HandoffPackage, ResumeResponse>("/v1/resume", package, cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(ResumeHandoff)), cancellationToken);

    /// <summary>The cancellable BriefContext operation, with a deadline covering the complete response.</summary>
    public Task<BriefResponse?> BriefContextAsync(string workOrder, long? maxBytes = null, string? recipient = null, string? assignment = null, CommunicationValues? view = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync<BriefResponse?>(nameof(BriefContext), async token =>
        {
            string query = CommunicationQuery(recipient, assignment, view);
            if (maxBytes is not null)
            {
                query += (query.Length == 0 ? "?" : "&") + "maxBytes=" + maxBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return (await GetAsync<BriefResponse>("/v1/context/work/" + Uri.EscapeDataString(workOrder) + "/brief" + query, allowNotFound: true, cancellationToken: token).ConfigureAwait(false));
        }, false, OperationTimeout?.Invoke(nameof(BriefContext)), cancellationToken);

    /// <summary>The cancellable Context operation, with a deadline covering the complete response.</summary>
    public Task<ContextResponse> ContextAsync(string workOrder, long? maxBytes = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ContextResponse>(nameof(Context), async token => (await GetAsync<ContextResponse>("/v1/context/work/" + Uri.EscapeDataString(workOrder) +
        (maxBytes is null ? string.Empty : "?maxBytes=" + maxBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Context)), cancellationToken);

    /// <summary>The cancellable ContextRead operation, with a deadline covering the complete response.</summary>
    public Task<ContextReadResponse> ContextReadAsync(string pin, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ContextReadResponse>(nameof(ContextRead), async token => (await GetAsync<ContextReadResponse>("/v1/context/original/" + Uri.EscapeDataString(pin), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(ContextRead)), cancellationToken);

    /// <summary>The cancellable ContextRead operation, with a deadline covering the complete response.</summary>
    public Task<ContextReadResponse> ContextReadAsync(string manifest, string source, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ContextReadResponse>(nameof(ContextRead), async token => (await GetAsync<ContextReadResponse>("/v1/context/" + Uri.EscapeDataString(manifest) + "/sources/" + Uri.EscapeDataString(source), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(ContextRead)), cancellationToken);

    /// <summary>The cancellable Validate operation, with a deadline covering the complete response.</summary>
    public Task<FindingsResponse> ValidateAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<FindingsResponse>(nameof(Validate), async token => (await GetAsync<FindingsResponse>("/v1/validate", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Validate)), cancellationToken);

    /// <summary>The cancellable Check operation, with a deadline covering the complete response.</summary>
    public Task<FindingsResponse> CheckAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<FindingsResponse>(nameof(Check), async token => (await GetAsync<FindingsResponse>("/v1/check", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Check)), cancellationToken);

    /// <summary>The cancellable Record operation, with a deadline covering the complete response.</summary>
    public Task<RecordResponse?> RecordAsync(string id, CancellationToken cancellationToken = default) =>
        RunOperationAsync<RecordResponse?>(nameof(Record), async token => (await GetAsync<RecordResponse>("/v1/records/" + Uri.EscapeDataString(id), allowNotFound: true, cancellationToken: token).ConfigureAwait(false)), false, OperationTimeout?.Invoke(nameof(Record)), cancellationToken);

    /// <summary>The cancellable Records operation, with a deadline covering the complete response.</summary>
    public Task<IReadOnlyList<RecordResponse>> RecordsAsync(string type, CancellationToken cancellationToken = default) =>
        RunOperationAsync<IReadOnlyList<RecordResponse>>(nameof(Records), async token => (await GetAsync<List<RecordResponse>>("/v1/records?type=" + Uri.EscapeDataString(type), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Records)), cancellationToken);

    /// <summary>The cancellable NewRecord operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> NewRecordAsync(NewRecordRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(NewRecord), async token => (await PostAsync<NewRecordRequest, WriteOutcome>("/v1/records", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(NewRecord)), cancellationToken);

    /// <summary>The cancellable Decisions operation, with a deadline covering the complete response.</summary>
    public Task<DecisionsResponse> DecisionsAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<DecisionsResponse>(nameof(Decisions), async token => (await GetAsync<DecisionsResponse>("/v1/decisions", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Decisions)), cancellationToken);

    /// <summary>The cancellable Decision operation, with a deadline covering the complete response.</summary>
    public Task<DecisionResponse?> DecisionAsync(string id, CancellationToken cancellationToken = default) =>
        RunOperationAsync<DecisionResponse?>(nameof(Decision), async token => (await GetAsync<DecisionResponse>("/v1/decisions/" + Uri.EscapeDataString(id), allowNotFound: true, cancellationToken: token).ConfigureAwait(false)), false, OperationTimeout?.Invoke(nameof(Decision)), cancellationToken);

    /// <summary>The cancellable BriefFor operation, with a deadline covering the complete response.</summary>
    public Task<BriefResponse?> BriefForAsync(string workOrder, string? recipient = null, string? assignment = null, CommunicationValues? view = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync<BriefResponse?>(nameof(BriefFor), async token => (await GetAsync<BriefResponse>("/v1/work/" + Uri.EscapeDataString(workOrder) + "/brief" + CommunicationQuery(recipient, assignment, view), allowNotFound: true, cancellationToken: token).ConfigureAwait(false)), false, OperationTimeout?.Invoke(nameof(BriefFor)), cancellationToken);

    /// <summary>The cancellable Brief operation, with a deadline covering the complete response.</summary>
    public Task<BriefResponse?> BriefAsync(string workOrder, CancellationToken cancellationToken = default) =>
        RunOperationAsync<BriefResponse?>(nameof(Brief), async token => (await GetAsync<BriefResponse>("/v1/work/" + Uri.EscapeDataString(workOrder) + "/brief", allowNotFound: true, cancellationToken: token).ConfigureAwait(false)), false, OperationTimeout?.Invoke(nameof(Brief)), cancellationToken);

    /// <summary>The cancellable Observe operation, with a deadline covering the complete response.</summary>
    public Task<ObserveResponse?> ObserveAsync(string workOrder, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ObserveResponse?>(nameof(Observe), async token => (await GetAsync<ObserveResponse>("/v1/work/" + Uri.EscapeDataString(workOrder), allowNotFound: true, cancellationToken: token).ConfigureAwait(false)), false, OperationTimeout?.Invoke(nameof(Observe)), cancellationToken);

    /// <summary>The cancellable Assign operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> AssignAsync(AssignRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(Assign), async token => (await PostAsync<AssignRequest, WriteOutcome>("/v1/work/assign", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(Assign)), cancellationToken);

    /// <summary>The cancellable Attempt operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> AttemptAsync(AttemptRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(Attempt), async token => (await PostAsync<AttemptRequest, WriteOutcome>("/v1/work/attempt", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(Attempt)), cancellationToken);

    /// <summary>The cancellable WorkProgress operation, with a deadline covering the complete response.</summary>
    public Task<WorkProgressResponse> WorkProgressAsync(WorkProgressRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WorkProgressResponse>(nameof(WorkProgress), async token => (await PostAsync<WorkProgressRequest, WorkProgressResponse>("/v1/work/progress", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(WorkProgress)), cancellationToken);

    /// <summary>The cancellable Result operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> ResultAsync(ResultRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(Result), async token => (await PostAsync<ResultRequest, WriteOutcome>("/v1/work/result", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(Result)), cancellationToken);

    /// <summary>Review an exact result revision, including the evidence read; this records no acceptance.</summary>
    public Task<WriteOutcome> ReviewResultAsync(ResultReviewRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<WriteOutcome>(nameof(ReviewResult), async token => (await PostAsync<ResultReviewRequest, WriteOutcome>("/v1/work/review", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(ReviewResult)), cancellationToken);

    /// <summary>The cancellable Amend operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> AmendAsync(AmendRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(Amend), async token => (await PostAsync<AmendRequest, WriteOutcome>("/v1/work/amend", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(Amend)), cancellationToken);

    /// <summary>The cancellable Devices operation, with a deadline covering the complete response.</summary>
    public Task<IReadOnlyList<DeviceRow>> DevicesAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<IReadOnlyList<DeviceRow>>(nameof(Devices), async token => (await GetAsync<List<DeviceRow>>("/v1/devices", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Devices)), cancellationToken);

    /// <summary>The cancellable DeviceCreate operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> DeviceCreateAsync(DeviceCreateRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(DeviceCreate), async token => (await PostAsync<DeviceCreateRequest, WriteOutcome>("/v1/devices", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(DeviceCreate)), cancellationToken);

    /// <summary>The cancellable DeviceAdmit operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> DeviceAdmitAsync(AdmitRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(DeviceAdmit), async token => (await PostAsync<AdmitRequest, WriteOutcome>("/v1/devices/admit", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(DeviceAdmit)), cancellationToken);

    /// <summary>The cancellable DeviceRename operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> DeviceRenameAsync(DeviceRenameRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(DeviceRename), async token => (await PostAsync<DeviceRenameRequest, WriteOutcome>("/v1/devices/rename", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(DeviceRename)), cancellationToken);

    /// <summary>The cancellable DeviceRotate operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> DeviceRotateAsync(DeviceRotateRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(DeviceRotate), async token => (await PostAsync<DeviceRotateRequest, WriteOutcome>("/v1/devices/rotate", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(DeviceRotate)), cancellationToken);

    /// <summary>The cancellable DeviceRecovery operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> DeviceRecoveryAsync(DeviceRecoveryRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(DeviceRecovery), async token => (await PostAsync<DeviceRecoveryRequest, WriteOutcome>("/v1/devices/recovery", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(DeviceRecovery)), cancellationToken);

    /// <summary>The cancellable DeviceRevoke operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> DeviceRevokeAsync(RevokeRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(DeviceRevoke), async token => (await PostAsync<RevokeRequest, WriteOutcome>("/v1/devices/revoke", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(DeviceRevoke)), cancellationToken);

    /// <summary>The cancellable Releases operation, with a deadline covering the complete response.</summary>
    public Task<IReadOnlyList<ReleaseRow>> ReleasesAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<IReadOnlyList<ReleaseRow>>(nameof(Releases), async token => (await GetAsync<List<ReleaseRow>>("/v1/releases", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Releases)), cancellationToken);

    /// <summary>The cancellable Documents operation, with a deadline covering the complete response.</summary>
    public Task<IReadOnlyList<DocumentRow>> DocumentsAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<IReadOnlyList<DocumentRow>>(nameof(Documents), async token => (await GetAsync<List<DocumentRow>>("/v1/documents", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Documents)), cancellationToken);

    /// <summary>The cancellable DocumentReview operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> DocumentReviewAsync(string declaration, DocumentReviewRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(DocumentReview), async token => (await PostAsync<DocumentReviewRequest, WriteOutcome>("/v1/documents/" + Uri.EscapeDataString(declaration) + "/reviews", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(DocumentReview)), cancellationToken);

    /// <summary>The cancellable Asks operation, with a deadline covering the complete response.</summary>
    public Task<AsksResponse> AsksAsync(string? principal = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync<AsksResponse>(nameof(Asks), async token => (await GetAsync<AsksResponse>(principal is null ? "/v1/asks" : "/v1/asks?to=" + Uri.EscapeDataString(principal), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Asks)), cancellationToken);

    /// <summary>The cancellable OperatorActions operation, with a deadline covering the complete response.</summary>
    public Task<OperatorActionsResponse> OperatorActionsAsync(string? recipient = null, string? assignment = null, CommunicationValues? view = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync<OperatorActionsResponse>(nameof(OperatorActions), async token => (await GetAsync<OperatorActionsResponse>("/v1/operator/actions" + CommunicationQuery(recipient, assignment, view), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(OperatorActions)), cancellationToken);

    /// <summary>The cancellable OperatorRecord operation, with a deadline covering the complete response.</summary>
    public Task<OperatorRecordResponse> OperatorRecordAsync(OperatorRecordRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<OperatorRecordResponse>(nameof(OperatorRecord), async token => (await PostAsync<OperatorRecordRequest, OperatorRecordResponse>("/v1/operator/record", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(OperatorRecord)), cancellationToken);

    /// <summary>The cancellable Goals operation, with a deadline covering the complete response.</summary>
    public Task<IReadOnlyList<GoalRow>> GoalsAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<IReadOnlyList<GoalRow>>(nameof(Goals), async token => (await GetAsync<List<GoalRow>>("/v1/goals", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Goals)), cancellationToken);

    /// <summary>The cancellable Goal operation, with a deadline covering the complete response.</summary>
    public Task<GoalResponse?> GoalAsync(string id, CancellationToken cancellationToken = default) =>
        RunOperationAsync<GoalResponse?>(nameof(Goal), async token => (await GetAsync<GoalResponse>("/v1/goals/" + Uri.EscapeDataString(id), allowNotFound: true, cancellationToken: token).ConfigureAwait(false)), false, OperationTimeout?.Invoke(nameof(Goal)), cancellationToken);

    /// <summary>The cancellable GoalNext operation, with a deadline covering the complete response.</summary>
    public Task<NextResponse?> GoalNextAsync(string goal, CancellationToken cancellationToken = default) =>
        RunOperationAsync<NextResponse?>(nameof(GoalNext), async token => (await GetAsync<NextResponse>(GoalPath(goal, "next"), allowNotFound: true, cancellationToken: token).ConfigureAwait(false)), false, OperationTimeout?.Invoke(nameof(GoalNext)), cancellationToken);

    /// <summary>The cancellable GoalStep operation, with a deadline covering the complete response.</summary>
    public Task<GoalStepResponse> GoalStepAsync(string goal, GoalStepRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<GoalStepResponse>(nameof(GoalStep), async token => (await PostAsync<GoalStepRequest, GoalStepResponse>(GoalPath(goal, "step"), request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(GoalStep)), cancellationToken);

    /// <summary>The cancellable WorkTakeover operation, with a deadline covering the complete response.</summary>
    public Task<TakeoverResponse> WorkTakeoverAsync(TakeoverRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<TakeoverResponse>(nameof(WorkTakeover), async token => (await PostAsync<TakeoverRequest, TakeoverResponse>("/v1/work/takeover", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(WorkTakeover)), cancellationToken);

    /// <summary>The cancellable GoalPropose operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> GoalProposeAsync(GoalProposeRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(GoalPropose), async token => (await PostAsync<GoalProposeRequest, WriteOutcome>("/v1/goals", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(GoalPropose)), cancellationToken);

    /// <summary>The cancellable GoalActivate operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> GoalActivateAsync(string goal, GoalActionRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(GoalActivate), async token => (await PostAsync<GoalActionRequest, WriteOutcome>(GoalPath(goal, "activate"), request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(GoalActivate)), cancellationToken);

    /// <summary>The cancellable GoalPause operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> GoalPauseAsync(string goal, GoalActionRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(GoalPause), async token => (await PostAsync<GoalActionRequest, WriteOutcome>(GoalPath(goal, "pause"), request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(GoalPause)), cancellationToken);

    /// <summary>The cancellable GoalResume operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> GoalResumeAsync(string goal, GoalActionRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(GoalResume), async token => (await PostAsync<GoalActionRequest, WriteOutcome>(GoalPath(goal, "resume"), request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(GoalResume)), cancellationToken);

    /// <summary>The cancellable GoalStop operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> GoalStopAsync(string goal, GoalActionRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(GoalStop), async token => (await PostAsync<GoalActionRequest, WriteOutcome>(GoalPath(goal, "stop"), request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(GoalStop)), cancellationToken);

    /// <summary>The cancellable GoalEvaluate operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> GoalEvaluateAsync(string goal, GoalEvaluateRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(GoalEvaluate), async token => (await PostAsync<GoalEvaluateRequest, WriteOutcome>(GoalPath(goal, "evaluate"), request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(GoalEvaluate)), cancellationToken);

    /// <summary>The cancellable GoalAccept operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> GoalAcceptAsync(string goal, GoalAcceptRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(GoalAccept), async token => (await PostAsync<GoalAcceptRequest, WriteOutcome>(GoalPath(goal, "accept"), request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(GoalAccept)), cancellationToken);

    /// <summary>The cancellable Providers operation, with a deadline covering the complete response.</summary>
    public Task<IReadOnlyList<ProviderRow>> ProvidersAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<IReadOnlyList<ProviderRow>>(nameof(Providers), async token => (await GetAsync<List<ProviderRow>>("/v1/providers", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Providers)), cancellationToken);

    /// <summary>The cancellable Launchers operation, with a deadline covering the complete response.</summary>
    public Task<IReadOnlyList<ProviderRow>> LaunchersAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<IReadOnlyList<ProviderRow>>(nameof(Launchers), async token => (await GetAsync<List<ProviderRow>>("/v1/launchers", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Launchers)), cancellationToken);

    /// <summary>The cancellable QualifyHarness operation, with a deadline covering the complete response.</summary>
    public Task<WriteOutcome> QualifyHarnessAsync(HarnessQualifyRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(QualifyHarness), async token => (await PostAsync<HarnessQualifyRequest, WriteOutcome>("/v1/harness-qualifications", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(QualifyHarness)), cancellationToken);

    /// <summary>The cancellable RunStart operation, with a deadline covering the complete response.</summary>
    public Task<RunResponse> RunStartAsync(RunStartRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<RunResponse>(nameof(RunStart), async token => (await PostAsync<RunStartRequest, RunResponse>("/v1/runs", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(RunStart)), cancellationToken);

    /// <summary>The cancellable Runs operation, with a deadline covering the complete response.</summary>
    public Task<RunsResponse> RunsAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<RunsResponse>(nameof(Runs), async token => (await GetAsync<RunsResponse>("/v1/runs", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Runs)), cancellationToken);

    /// <summary>The cancellable RunStop operation, with a deadline covering the complete response.</summary>
    public Task<RunStopResponse> RunStopAsync(string run, RunStopRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<RunStopResponse>(nameof(RunStop), async token => (await PostAsync<RunStopRequest, RunStopResponse>("/v1/runs/" + Uri.EscapeDataString(run) + "/stop", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(RunStop)), cancellationToken);

    /// <summary>The cancellable ReleaseSubmit operation, with a deadline covering the complete response.</summary>
    public Task<ReleaseResponse> ReleaseSubmitAsync(ReleaseSubmitRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ReleaseResponse>(nameof(ReleaseSubmit), async token => (await PostAsync<ReleaseSubmitRequest, ReleaseResponse>("/v1/releases/submit", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(ReleaseSubmit)), cancellationToken);

    /// <summary>The cancellable ReleaseObserve operation, with a deadline covering the complete response.</summary>
    public Task<ReleaseResponse> ReleaseObserveAsync(ReleaseObserveRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ReleaseResponse>(nameof(ReleaseObserve), async token => (await PostAsync<ReleaseObserveRequest, ReleaseResponse>("/v1/releases/observe", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(ReleaseObserve)), cancellationToken);

    /// <summary>The cancellable ChangesPush operation, with a deadline covering the complete response.</summary>
    public Task<ChangeResponse> ChangesPushAsync(ChangesPushRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ChangeResponse>(nameof(ChangesPush), async token => (await PostAsync<ChangesPushRequest, ChangeResponse>("/v1/changes/push", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(ChangesPush)), cancellationToken);

    /// <summary>The cancellable ChangesOpen operation, with a deadline covering the complete response.</summary>
    public Task<ChangeResponse> ChangesOpenAsync(ChangesOpenRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ChangeResponse>(nameof(ChangesOpen), async token => (await PostAsync<ChangesOpenRequest, ChangeResponse>("/v1/changes/open", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(ChangesOpen)), cancellationToken);

    /// <summary>The cancellable ChangesMerge operation, with a deadline covering the complete response.</summary>
    public Task<ChangeResponse> ChangesMergeAsync(ChangesMergeRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ChangeResponse>(nameof(ChangesMerge), async token => (await PostAsync<ChangesMergeRequest, ChangeResponse>("/v1/changes/merge", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(ChangesMerge)), cancellationToken);

    /// <summary>The cancellable ChangesObserve operation, with a deadline covering the complete response.</summary>
    public Task<ChangeResponse> ChangesObserveAsync(ChangesObserveRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ChangeResponse>(nameof(ChangesObserve), async token => (await PostAsync<ChangesObserveRequest, ChangeResponse>("/v1/changes/observe", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(ChangesObserve)), cancellationToken);

    /// <summary>The cancellable Upgrade operation, with a deadline covering the complete response.</summary>
    public Task<(UpgradeResponse? Plan, Problem? Problem)> UpgradeAsync(UpgradeRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<(UpgradeResponse? Plan, Problem? Problem)>(nameof(Upgrade), async token => (await PostOrProblemAsync<UpgradeRequest, UpgradeResponse>("/v1/estate/upgrade", request, cancellationToken: token).ConfigureAwait(false)), true, OperationTimeout?.Invoke(nameof(Upgrade)), cancellationToken);

    /// <summary>The cancellable Export operation, with a deadline covering the complete response.</summary>
    public Task<(ExportResponse? Export, Problem? Problem)> ExportAsync(ExportRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<(ExportResponse? Export, Problem? Problem)>(nameof(Export), async token => (await PostOrProblemAsync<ExportRequest, ExportResponse>("/v1/estate/export", request, cancellationToken: token).ConfigureAwait(false)), true, OperationTimeout?.Invoke(nameof(Export)), cancellationToken);

    /// <summary>The cancellable Reconcile operation, with a deadline covering the complete response.</summary>
    public Task<IReadOnlyList<ReconcileTask>> ReconcileAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<IReadOnlyList<ReconcileTask>>(nameof(Reconcile), async token => (await GetAsync<List<ReconcileTask>>("/v1/reconcile", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Reconcile)), cancellationToken);

    /// <summary>The cancellable RecoverInspect operation, with a deadline covering the complete response.</summary>
    public Task<string> RecoverInspectAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<string>(nameof(RecoverInspect), async token =>
        {
            using HttpResponseMessage response = await asyncHttp.GetAsync(new Uri("/v1/recover/inspect", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            await EnsureSuccessAsync(response, token).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        }, false, OperationTimeout?.Invoke(nameof(RecoverInspect)), cancellationToken);

    /// <summary>The cancellable Objects operation, with a deadline covering the complete response.</summary>
    public Task<IReadOnlyList<ObjectRow>> ObjectsAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<IReadOnlyList<ObjectRow>>(nameof(Objects), async token => (await GetAsync<List<ObjectRow>>("/v1/objects", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Objects)), cancellationToken);

    /// <summary>The cancellable ObjectPut operation, with a deadline covering the complete response.</summary>
    public Task<(ObjectStored? Stored, Problem? Problem)> ObjectPutAsync(byte[] bytes, CancellationToken cancellationToken = default) =>
        RunOperationAsync<(ObjectStored? Stored, Problem? Problem)>(nameof(ObjectPut), async token =>
        {
            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            using HttpResponseMessage response = await asyncHttp.PostAsync(new Uri("/v1/objects", UriKind.Relative), content, token).ConfigureAwait(false);
            if (response.StatusCode >= HttpStatusCode.InternalServerError) await EnsureSuccessAsync(response, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? (await response.Content.ReadFromJsonAsync<ObjectStored>(ProtocolJson.Options, token).ConfigureAwait(false), null)
                : (null, await ReadProblemAsync(response, token).ConfigureAwait(false));
        }, true, OperationTimeout?.Invoke(nameof(ObjectPut)), cancellationToken);

    /// <summary>The cancellable ObjectGet operation, with a deadline covering the complete response.</summary>
    public Task<byte[]?> ObjectGetAsync(string hash, CancellationToken cancellationToken = default) =>
        RunOperationAsync<byte[]?>(nameof(ObjectGet), async token =>
        {
            using HttpResponseMessage response = await asyncHttp.GetAsync(new Uri("/v1/objects/" + Uri.EscapeDataString(hash), UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            await EnsureSuccessAsync(response, token).ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
        }, false, OperationTimeout?.Invoke(nameof(ObjectGet)), cancellationToken);

    /// <summary>The cancellable ObjectsVerify operation, with a deadline covering the complete response.</summary>
    public Task<FindingsResponse> ObjectsVerifyAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<FindingsResponse>(nameof(ObjectsVerify), async token => (await GetAsync<FindingsResponse>("/v1/objects/verify", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(ObjectsVerify)), cancellationToken);

    /// <summary>The cancellable Shutdown operation, with a deadline covering the complete response.</summary>
    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<bool>(nameof(Shutdown), async token =>
        {
            using HttpResponseMessage response = await asyncHttp.PostAsync(new Uri("/v1/shutdown", UriKind.Relative), null, token).ConfigureAwait(false);
            await EnsureSuccessAsync(response, token).ConfigureAwait(false);
            return true;
        }, true, OperationTimeout?.Invoke(nameof(Shutdown)), cancellationToken);

    /// <summary>The cancellable IssuePairing operation, with a deadline covering the complete response.</summary>
    public Task<(PairingIssued? Issued, Problem? Problem)> IssuePairingAsync(PairingIssueRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<(PairingIssued? Issued, Problem? Problem)>(nameof(IssuePairing), async token => (await PostOrProblemAsync<PairingIssueRequest, PairingIssued>("/v1/pairings", request, cancellationToken: token).ConfigureAwait(false)), true, OperationTimeout?.Invoke(nameof(IssuePairing)), cancellationToken);

    /// <summary>The cancellable RotateCertificate operation, with a deadline covering the complete response.</summary>
    public Task<(CertificateRotated? Rotated, Problem? Problem)> RotateCertificateAsync(CertificateRotateRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<(CertificateRotated? Rotated, Problem? Problem)>(nameof(RotateCertificate), async token => (await PostOrProblemAsync<CertificateRotateRequest, CertificateRotated>("/v1/runtime/certificate", request, cancellationToken: token).ConfigureAwait(false)), true, OperationTimeout?.Invoke(nameof(RotateCertificate)), cancellationToken);

    /// <summary>The cancellable RuntimeIdentity operation, with a deadline covering the complete response.</summary>
    public Task<RuntimeIdentityProof?> RuntimeIdentityAsync(string nonce, CancellationToken cancellationToken = default) =>
        RunOperationAsync<RuntimeIdentityProof?>(nameof(RuntimeIdentity), async token =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
            return (await GetAsync<RuntimeIdentityProof>(RuntimeIdentityScheme.Path + "?nonce=" + Uri.EscapeDataString(nonce), allowNotFound: true, cancellationToken: token).ConfigureAwait(false));
        }, false, OperationTimeout?.Invoke(nameof(RuntimeIdentity)), cancellationToken);

    /// <summary>The cancellable OpenApi operation, with a deadline covering the complete response.</summary>
    public Task<string> OpenApiAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<string>(nameof(OpenApi), async token =>
        {
            using HttpResponseMessage response = await asyncHttp.GetAsync(new Uri(OpenApiDocument.Path, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            await EnsureSuccessAsync(response, token).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        }, false, OperationTimeout?.Invoke(nameof(OpenApi)), cancellationToken);

    private async Task<T?> GetAsync<T>(string path, bool allowNotFound = false, CancellationToken cancellationToken = default)
        where T : class
    {
        using HttpResponseMessage response = await asyncHttp.GetAsync(new Uri(path, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(ProtocolJson.Options, cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("The runtime returned no result.");
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken)
        where TResponse : class
    {
        using HttpResponseMessage response = await asyncHttp.PostAsJsonAsync(new Uri(path, UriKind.Relative), request, ProtocolJson.Options, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is not (HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound or HttpStatusCode.OK or HttpStatusCode.Conflict))
        {
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            TResponse result = await response.Content.ReadFromJsonAsync<TResponse>(ProtocolJson.Options, cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException("The runtime returned no result.");
            if (result is WriteOutcome { Written: null, Problem: null } or WriteOutcome { Written: not null, Problem: not null })
                throw new JsonException("The runtime returned no definite write outcome.");
            return result;
        }
        catch (Exception exception) when (IsRejection(response) && exception is (IOException or JsonException or OperationCanceledException))
        {
            throw UnreadableRefusal(response, exception);
        }
    }

    private async Task<(TResponse? Response, Problem? Problem)> PostOrProblemAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken)
        where TResponse : class
    {
        using HttpResponseMessage response = await asyncHttp.PostAsJsonAsync(new Uri(path, UriKind.Relative), request, ProtocolJson.Options, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<TResponse>(ProtocolJson.Options, cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException("The runtime returned no result."), null);
        }

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Unauthorized)
        {
            return (null, await ReadProblemAsync(response, cancellationToken).ConfigureAwait(false));
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (null, null);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        // A server error alone says nothing about an effect, but an explicit refusal does. Preserve that answer
        // even when (for example) the runtime uses 503 because it cannot safely keep the write's device ledger.
        try
        {
            Problem? problem = await response.Content.ReadFromJsonAsync<Problem>(ProtocolJson.Options, cancellationToken).ConfigureAwait(false);
            if (problem is { Code: "refused" or "denied", Message: not null })
                throw new ClientRefusedException(problem.Message, statusCode: response.StatusCode);
        }
        catch (Exception exception) when (IsRejection(response) && exception is (IOException or JsonException or OperationCanceledException))
        {
            throw UnreadableRefusal(response, exception);
        }
        catch (JsonException)
        {
            // A missing or unreadable error body cannot establish that an effect did not happen.
        }

        response.EnsureSuccessStatusCode();
    }

    private static bool IsRejection(HttpResponseMessage response) => response.StatusCode >= HttpStatusCode.BadRequest && response.StatusCode < HttpStatusCode.InternalServerError;

    private static ClientRefusedException UnreadableRefusal(HttpResponseMessage response, Exception exception) =>
        new($"The runtime refused the request with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}); its diagnostic could not be read.", exception, response.StatusCode);

    private static async Task<Problem> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Problem>(ProtocolJson.Options, cancellationToken).ConfigureAwait(false) ?? new Problem("refused", response.ReasonPhrase ?? "refused", []);
        }
        catch (JsonException)
        {
            return new Problem("refused", response.ReasonPhrase ?? "refused", []);
        }
        catch (Exception exception) when (IsRejection(response) && exception is (IOException or OperationCanceledException))
        {
            throw UnreadableRefusal(response, exception);
        }
    }
    /// <summary>Read retained release-admission evidence without creating a record or authorizing a later submission.</summary>
    public Task<ReleaseBasisResponse?> ReleaseBasisAsync(string hash, CancellationToken cancellationToken = default) =>
        ClientOperation.RunAsync<ReleaseBasisResponse?>(nameof(ReleaseBasis), token => GetAsync<ReleaseBasisResponse>("/v1/release-bases/" + Uri.EscapeDataString(hash), allowNotFound: true, cancellationToken: token), false, OperationTimeout?.Invoke(nameof(ReleaseBasis)), cancellationToken);
}
