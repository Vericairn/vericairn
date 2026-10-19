// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>
/// A compatibility bridge for synchronous consumers. Each call uses the asynchronous operation with the bound cancellation
/// token. Use it on a worker thread for legacy read projections, never to block a UI thread. It owns no API lifetime.
/// </summary>
public sealed partial class CancellationBoundApi(IEstateApi inner, CancellationToken cancellationToken) : IEstateApi
{
    /// <inheritdoc />
    public IReadOnlyList<string> PolicyAccess(string principal) => inner.PolicyAccessAsync(principal, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> PolicyAccessAsync(string principal, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PolicyAccessAsync(principal, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome PolicyDeclare(PolicyChangeRequest request) => inner.PolicyDeclareAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> PolicyDeclareAsync(PolicyChangeRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PolicyDeclareAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome PolicyRevise(string key, PolicyChangeRequest request) => inner.PolicyReviseAsync(key, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> PolicyReviseAsync(string key, PolicyChangeRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PolicyReviseAsync(key, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome PolicyRetire(string key, PolicyChangeRequest request) => inner.PolicyRetireAsync(key, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> PolicyRetireAsync(string key, PolicyChangeRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PolicyRetireAsync(key, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome PolicyExcept(string key, PolicyChangeRequest request) => inner.PolicyExceptAsync(key, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> PolicyExceptAsync(string key, PolicyChangeRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PolicyExceptAsync(key, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome PolicyRevokeException(string key, string id, PolicyChangeRequest request) => inner.PolicyRevokeExceptionAsync(key, id, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> PolicyRevokeExceptionAsync(string key, string id, PolicyChangeRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PolicyRevokeExceptionAsync(key, id, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome WritingExceptionAdd(PolicyChangeRequest request) => inner.WritingExceptionAddAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> WritingExceptionAddAsync(PolicyChangeRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.WritingExceptionAddAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome WritingExceptionRemove(string id, WritingExceptionRemoveRequest request) => inner.WritingExceptionRemoveAsync(id, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> WritingExceptionRemoveAsync(string id, WritingExceptionRemoveRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.WritingExceptionRemoveAsync(id, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome WorkPlace(string key, WorkPlaceRequest request) => inner.WorkPlaceAsync(key, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> WorkPlaceAsync(string key, WorkPlaceRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.WorkPlaceAsync(key, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public PolicyResponse Policies() => inner.PoliciesAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<PolicyResponse> PoliciesAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PoliciesAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public PolicyResponse? Policy(string key) => inner.PolicyAsync(key, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<PolicyResponse?> PolicyAsync(string key, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PolicyAsync(key, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<PolicyKindDto> PolicyKinds() => inner.PolicyKindsAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<IReadOnlyList<PolicyKindDto>> PolicyKindsAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PolicyKindsAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public PolicyResponse PolicyEvaluate(string? asOf = null, string? release = null) => inner.PolicyEvaluateAsync(asOf, release, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<PolicyResponse> PolicyEvaluateAsync(string? asOf = null, string? release = null, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PolicyEvaluateAsync(asOf, release, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WritingExceptionsResponse WritingExceptions() => inner.WritingExceptionsAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WritingExceptionsResponse> WritingExceptionsAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.WritingExceptionsAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WritingExceptionsResponse? WritingException(string id) => inner.WritingExceptionAsync(id, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WritingExceptionsResponse?> WritingExceptionAsync(string id, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.WritingExceptionAsync(id, cancellationToken: token), cancellationToken);

    private readonly IEstateApi inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <inheritdoc />
    public HealthResponse Health() => inner.HealthAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<HealthResponse> HealthAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.HealthAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public CommunicationProfile Communication(string? recipient = null, string? assignment = null, CommunicationValues? view = null) => inner.CommunicationAsync(recipient, assignment, view, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<CommunicationProfile> CommunicationAsync(string? recipient = null, string? assignment = null, CommunicationValues? view = null, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.CommunicationAsync(recipient, assignment, view, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome CommunicationSet(CommunicationSetRequest request) => inner.CommunicationSetAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> CommunicationSetAsync(CommunicationSetRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.CommunicationSetAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome CommunicationDelegate(CommunicationDelegateRequest request) => inner.CommunicationDelegateAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> CommunicationDelegateAsync(CommunicationDelegateRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.CommunicationDelegateAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public StatusResponse Status() => inner.StatusAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<StatusResponse> StatusAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.StatusAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome ObserveEstate(EstateObserveRequest request) => inner.ObserveEstateAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> ObserveEstateAsync(EstateObserveRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ObserveEstateAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public ProgressResponse Progress() => inner.ProgressAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ProgressResponse> ProgressAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ProgressAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public HandoffResponse Handoff(long? maxBytes = null) => inner.HandoffAsync(maxBytes, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<HandoffResponse> HandoffAsync(long? maxBytes = null, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.HandoffAsync(maxBytes, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public ResumeResponse ResumeHandoff(HandoffPackage package) => inner.ResumeHandoffAsync(package, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ResumeResponse> ResumeHandoffAsync(HandoffPackage package, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ResumeHandoffAsync(package, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public BriefResponse? BriefContext(string workOrder, long? maxBytes = null, string? recipient = null, string? assignment = null, CommunicationValues? view = null) => inner.BriefContextAsync(workOrder, maxBytes, recipient, assignment, view, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<BriefResponse?> BriefContextAsync(string workOrder, long? maxBytes = null, string? recipient = null, string? assignment = null, CommunicationValues? view = null, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.BriefContextAsync(workOrder, maxBytes, recipient, assignment, view, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public ContextResponse Context(string workOrder, long? maxBytes = null) => inner.ContextAsync(workOrder, maxBytes, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ContextResponse> ContextAsync(string workOrder, long? maxBytes = null, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ContextAsync(workOrder, maxBytes, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public ContextReadResponse ContextRead(string pin) => inner.ContextReadAsync(pin, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ContextReadResponse> ContextReadAsync(string pin, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ContextReadAsync(pin, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public ContextReadResponse ContextRead(string manifest, string source) => inner.ContextReadAsync(manifest, source, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ContextReadResponse> ContextReadAsync(string manifest, string source, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ContextReadAsync(manifest, source, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public FindingsResponse Validate() => inner.ValidateAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<FindingsResponse> ValidateAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ValidateAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public FindingsResponse Check() => inner.CheckAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<FindingsResponse> CheckAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.CheckAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public RecordResponse? Record(string id) => inner.RecordAsync(id, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<RecordResponse?> RecordAsync(string id, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.RecordAsync(id, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<RecordResponse> Records(string type) => inner.RecordsAsync(type, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<IReadOnlyList<RecordResponse>> RecordsAsync(string type, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.RecordsAsync(type, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome NewRecord(NewRecordRequest request) => inner.NewRecordAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> NewRecordAsync(NewRecordRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.NewRecordAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public DecisionsResponse Decisions() => inner.DecisionsAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<DecisionsResponse> DecisionsAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.DecisionsAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public DecisionResponse? Decision(string id) => inner.DecisionAsync(id, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<DecisionResponse?> DecisionAsync(string id, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.DecisionAsync(id, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public BriefResponse? BriefFor(string workOrder, string? recipient = null, string? assignment = null, CommunicationValues? view = null) => inner.BriefForAsync(workOrder, recipient, assignment, view, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<BriefResponse?> BriefForAsync(string workOrder, string? recipient = null, string? assignment = null, CommunicationValues? view = null, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.BriefForAsync(workOrder, recipient, assignment, view, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public BriefResponse? Brief(string workOrder) => inner.BriefAsync(workOrder, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<BriefResponse?> BriefAsync(string workOrder, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.BriefAsync(workOrder, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public ObserveResponse? Observe(string workOrder) => inner.ObserveAsync(workOrder, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ObserveResponse?> ObserveAsync(string workOrder, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ObserveAsync(workOrder, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome Assign(AssignRequest request) => inner.AssignAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> AssignAsync(AssignRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.AssignAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome Attempt(AttemptRequest request) => inner.AttemptAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> AttemptAsync(AttemptRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.AttemptAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WorkProgressResponse WorkProgress(WorkProgressRequest request) => inner.WorkProgressAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WorkProgressResponse> WorkProgressAsync(WorkProgressRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.WorkProgressAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome Result(ResultRequest request) => inner.ResultAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <summary>Review an exact result revision, including the evidence read; this records no acceptance.</summary>
    public WriteOutcome ReviewResult(ResultReviewRequest request) => inner.ReviewResultAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> ResultAsync(ResultRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ResultAsync(request, cancellationToken: token), cancellationToken);

    /// <summary>Review an exact result revision, including the evidence read; this records no acceptance.</summary>
    public Task<WriteOutcome> ReviewResultAsync(ResultReviewRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ReviewResultAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome Amend(AmendRequest request) => inner.AmendAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> AmendAsync(AmendRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.AmendAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<DeviceRow> Devices() => inner.DevicesAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<IReadOnlyList<DeviceRow>> DevicesAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.DevicesAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome DeviceCreate(DeviceCreateRequest request) => inner.DeviceCreateAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> DeviceCreateAsync(DeviceCreateRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.DeviceCreateAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome DeviceAdmit(AdmitRequest request) => inner.DeviceAdmitAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> DeviceAdmitAsync(AdmitRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.DeviceAdmitAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome DeviceRename(DeviceRenameRequest request) => inner.DeviceRenameAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> DeviceRenameAsync(DeviceRenameRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.DeviceRenameAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome DeviceRotate(DeviceRotateRequest request) => inner.DeviceRotateAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> DeviceRotateAsync(DeviceRotateRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.DeviceRotateAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome DeviceRecovery(DeviceRecoveryRequest request) => inner.DeviceRecoveryAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> DeviceRecoveryAsync(DeviceRecoveryRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.DeviceRecoveryAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome DeviceRevoke(RevokeRequest request) => inner.DeviceRevokeAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> DeviceRevokeAsync(RevokeRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.DeviceRevokeAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<ReleaseRow> Releases() => inner.ReleasesAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<IReadOnlyList<ReleaseRow>> ReleasesAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ReleasesAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<DocumentRow> Documents() => inner.DocumentsAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentRow>> DocumentsAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.DocumentsAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome DocumentReview(string declaration, DocumentReviewRequest request) => inner.DocumentReviewAsync(declaration, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> DocumentReviewAsync(string declaration, DocumentReviewRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.DocumentReviewAsync(declaration, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public AsksResponse Asks(string? principal = null) => inner.AsksAsync(principal, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<AsksResponse> AsksAsync(string? principal = null, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.AsksAsync(principal, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public OperatorActionsResponse OperatorActions(string? recipient = null, string? assignment = null, CommunicationValues? view = null) => inner.OperatorActionsAsync(recipient, assignment, view, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<OperatorActionsResponse> OperatorActionsAsync(string? recipient = null, string? assignment = null, CommunicationValues? view = null, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.OperatorActionsAsync(recipient, assignment, view, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public OperatorRecordResponse OperatorRecord(OperatorRecordRequest request) => inner.OperatorRecordAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<OperatorRecordResponse> OperatorRecordAsync(OperatorRecordRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.OperatorRecordAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<GoalRow> Goals() => inner.GoalsAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<IReadOnlyList<GoalRow>> GoalsAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.GoalsAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public GoalResponse? Goal(string id) => inner.GoalAsync(id, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<GoalResponse?> GoalAsync(string id, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.GoalAsync(id, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public NextResponse? GoalNext(string goal) => inner.GoalNextAsync(goal, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<NextResponse?> GoalNextAsync(string goal, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.GoalNextAsync(goal, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public GoalStepResponse GoalStep(string goal, GoalStepRequest request) => inner.GoalStepAsync(goal, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<GoalStepResponse> GoalStepAsync(string goal, GoalStepRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.GoalStepAsync(goal, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public TakeoverResponse WorkTakeover(TakeoverRequest request) => inner.WorkTakeoverAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<TakeoverResponse> WorkTakeoverAsync(TakeoverRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.WorkTakeoverAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome GoalPropose(GoalProposeRequest request) => inner.GoalProposeAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> GoalProposeAsync(GoalProposeRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.GoalProposeAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome GoalActivate(string goal, GoalActionRequest request) => inner.GoalActivateAsync(goal, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> GoalActivateAsync(string goal, GoalActionRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.GoalActivateAsync(goal, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome GoalPause(string goal, GoalActionRequest request) => inner.GoalPauseAsync(goal, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> GoalPauseAsync(string goal, GoalActionRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.GoalPauseAsync(goal, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome GoalResume(string goal, GoalActionRequest request) => inner.GoalResumeAsync(goal, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> GoalResumeAsync(string goal, GoalActionRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.GoalResumeAsync(goal, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome GoalStop(string goal, GoalActionRequest request) => inner.GoalStopAsync(goal, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> GoalStopAsync(string goal, GoalActionRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.GoalStopAsync(goal, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome GoalEvaluate(string goal, GoalEvaluateRequest request) => inner.GoalEvaluateAsync(goal, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> GoalEvaluateAsync(string goal, GoalEvaluateRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.GoalEvaluateAsync(goal, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome GoalAccept(string goal, GoalAcceptRequest request) => inner.GoalAcceptAsync(goal, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> GoalAcceptAsync(string goal, GoalAcceptRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.GoalAcceptAsync(goal, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<ProviderRow> Providers() => inner.ProvidersAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderRow>> ProvidersAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ProvidersAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<ProviderRow> Launchers() => inner.LaunchersAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderRow>> LaunchersAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.LaunchersAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome QualifyHarness(HarnessQualifyRequest request) => inner.QualifyHarnessAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> QualifyHarnessAsync(HarnessQualifyRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.QualifyHarnessAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public RunResponse RunStart(RunStartRequest request) => inner.RunStartAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<RunResponse> RunStartAsync(RunStartRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.RunStartAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public RunsResponse Runs() => inner.RunsAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<RunsResponse> RunsAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.RunsAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public RunStopResponse RunStop(string run, RunStopRequest request) => inner.RunStopAsync(run, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<RunStopResponse> RunStopAsync(string run, RunStopRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.RunStopAsync(run, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public ReleaseResponse ReleaseSubmit(ReleaseSubmitRequest request) => inner.ReleaseSubmitAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ReleaseResponse> ReleaseSubmitAsync(ReleaseSubmitRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ReleaseSubmitAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public ChangeResponse ChangesPush(ChangesPushRequest request) => inner.ChangesPushAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ChangeResponse> ChangesPushAsync(ChangesPushRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ChangesPushAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public ChangeResponse ChangesOpen(ChangesOpenRequest request) => inner.ChangesOpenAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ChangeResponse> ChangesOpenAsync(ChangesOpenRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ChangesOpenAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public ChangeResponse ChangesMerge(ChangesMergeRequest request) => inner.ChangesMergeAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ChangeResponse> ChangesMergeAsync(ChangesMergeRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ChangesMergeAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public ChangeResponse ChangesObserve(ChangesObserveRequest request) => inner.ChangesObserveAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ChangeResponse> ChangesObserveAsync(ChangesObserveRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ChangesObserveAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public ReleaseResponse ReleaseObserve(ReleaseObserveRequest request) => inner.ReleaseObserveAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ReleaseResponse> ReleaseObserveAsync(ReleaseObserveRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ReleaseObserveAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public (UpgradeResponse? Plan, Problem? Problem) Upgrade(UpgradeRequest request) => inner.UpgradeAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<(UpgradeResponse? Plan, Problem? Problem)> UpgradeAsync(UpgradeRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.UpgradeAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public (ExportResponse? Export, Problem? Problem) Export(ExportRequest request) => inner.ExportAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<(ExportResponse? Export, Problem? Problem)> ExportAsync(ExportRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ExportAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<ReconcileTask> Reconcile() => inner.ReconcileAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<IReadOnlyList<ReconcileTask>> ReconcileAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ReconcileAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public string RecoverInspect() => inner.RecoverInspectAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<string> RecoverInspectAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.RecoverInspectAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<ObjectRow> Objects() => inner.ObjectsAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<IReadOnlyList<ObjectRow>> ObjectsAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ObjectsAsync(cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public (ObjectStored? Stored, Problem? Problem) ObjectPut(byte[] bytes) => inner.ObjectPutAsync(bytes, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<(ObjectStored? Stored, Problem? Problem)> ObjectPutAsync(byte[] bytes, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ObjectPutAsync(bytes, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public byte[]? ObjectGet(string hash) => inner.ObjectGetAsync(hash, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<byte[]?> ObjectGetAsync(string hash, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ObjectGetAsync(hash, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public FindingsResponse ObjectsVerify() => inner.ObjectsVerifyAsync(cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<FindingsResponse> ObjectsVerifyAsync(CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ObjectsVerifyAsync(cancellationToken: token), cancellationToken);

    private async Task<T> ForwardAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken requestCancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, requestCancellation);
        return await call(linked.Token).ConfigureAwait(false);
    }
    /// <summary>Read retained release-admission evidence without creating a record or authorizing a later submission.</summary>
    public ReleaseBasisResponse? ReleaseBasis(string hash) => inner.ReleaseBasisAsync(hash, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ReleaseBasisResponse?> ReleaseBasisAsync(string hash, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ReleaseBasisAsync(hash, token), cancellationToken);
}
