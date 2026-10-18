// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public partial interface IEstateApi
{
    /// <summary>The cancellable counterpart of <see cref="Health"/>; a stopped observation does not undo an effect.</summary>
    Task<HealthResponse> HealthAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Health), () => Health(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Communication"/>; a stopped observation does not undo an effect.</summary>
    Task<CommunicationProfile> CommunicationAsync(string? recipient = null, string? assignment = null, CommunicationValues? view = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Communication), () => Communication(recipient, assignment, view), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="CommunicationSet"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> CommunicationSetAsync(CommunicationSetRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(CommunicationSet), () => CommunicationSet(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="CommunicationDelegate"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> CommunicationDelegateAsync(CommunicationDelegateRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(CommunicationDelegate), () => CommunicationDelegate(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Status"/>; a stopped observation does not undo an effect.</summary>
    Task<StatusResponse> StatusAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Status), () => Status(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="ObserveEstate"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> ObserveEstateAsync(EstateObserveRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ObserveEstate), () => ObserveEstate(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Progress"/>; a stopped observation does not undo an effect.</summary>
    Task<ProgressResponse> ProgressAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Progress), () => Progress(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Handoff"/>; a stopped observation does not undo an effect.</summary>
    Task<HandoffResponse> HandoffAsync(long? maxBytes = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Handoff), () => Handoff(maxBytes), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="ResumeHandoff"/>; a stopped observation does not undo an effect.</summary>
    Task<ResumeResponse> ResumeHandoffAsync(HandoffPackage package, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ResumeHandoff), () => ResumeHandoff(package), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="BriefContext"/>; a stopped observation does not undo an effect.</summary>
    Task<BriefResponse?> BriefContextAsync(string workOrder, long? maxBytes = null, string? recipient = null, string? assignment = null, CommunicationValues? view = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(BriefContext), () => BriefContext(workOrder, maxBytes, recipient, assignment, view), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Context"/>; a stopped observation does not undo an effect.</summary>
    Task<ContextResponse> ContextAsync(string workOrder, long? maxBytes = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Context), () => Context(workOrder, maxBytes), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="ContextRead(string)"/>; a stopped observation does not undo an effect.</summary>
    Task<ContextReadResponse> ContextReadAsync(string pin, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ContextRead), () => ContextRead(pin), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="ContextRead(string)"/>; a stopped observation does not undo an effect.</summary>
    Task<ContextReadResponse> ContextReadAsync(string manifest, string source, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ContextRead), () => ContextRead(manifest, source), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Validate"/>; a stopped observation does not undo an effect.</summary>
    Task<FindingsResponse> ValidateAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Validate), () => Validate(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Check"/>; a stopped observation does not undo an effect.</summary>
    Task<FindingsResponse> CheckAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Check), () => Check(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Record"/>; a stopped observation does not undo an effect.</summary>
    Task<RecordResponse?> RecordAsync(string id, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Record), () => Record(id), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Records"/>; a stopped observation does not undo an effect.</summary>
    Task<IReadOnlyList<RecordResponse>> RecordsAsync(string type, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Records), () => Records(type), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="NewRecord"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> NewRecordAsync(NewRecordRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(NewRecord), () => NewRecord(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Decisions"/>; a stopped observation does not undo an effect.</summary>
    Task<DecisionsResponse> DecisionsAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Decisions), () => Decisions(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Decision"/>; a stopped observation does not undo an effect.</summary>
    Task<DecisionResponse?> DecisionAsync(string id, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Decision), () => Decision(id), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="BriefFor"/>; a stopped observation does not undo an effect.</summary>
    Task<BriefResponse?> BriefForAsync(string workOrder, string? recipient = null, string? assignment = null, CommunicationValues? view = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(BriefFor), () => BriefFor(workOrder, recipient, assignment, view), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Brief"/>; a stopped observation does not undo an effect.</summary>
    Task<BriefResponse?> BriefAsync(string workOrder, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Brief), () => Brief(workOrder), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Observe"/>; a stopped observation does not undo an effect.</summary>
    Task<ObserveResponse?> ObserveAsync(string workOrder, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Observe), () => Observe(workOrder), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Assign"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> AssignAsync(AssignRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Assign), () => Assign(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Attempt"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> AttemptAsync(AttemptRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Attempt), () => Attempt(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="WorkProgress"/>; a stopped observation does not undo an effect.</summary>
    Task<WorkProgressResponse> WorkProgressAsync(WorkProgressRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(WorkProgress), () => WorkProgress(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Result"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> ResultAsync(ResultRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Result), () => Result(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Amend"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> AmendAsync(AmendRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Amend), () => Amend(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Devices"/>; a stopped observation does not undo an effect.</summary>
    Task<IReadOnlyList<DeviceRow>> DevicesAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Devices), () => Devices(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="DeviceCreate"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> DeviceCreateAsync(DeviceCreateRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(DeviceCreate), () => DeviceCreate(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="DeviceAdmit"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> DeviceAdmitAsync(AdmitRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(DeviceAdmit), () => DeviceAdmit(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="DeviceRename"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> DeviceRenameAsync(DeviceRenameRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(DeviceRename), () => DeviceRename(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="DeviceRotate"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> DeviceRotateAsync(DeviceRotateRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(DeviceRotate), () => DeviceRotate(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="DeviceRecovery"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> DeviceRecoveryAsync(DeviceRecoveryRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(DeviceRecovery), () => DeviceRecovery(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="DeviceRevoke"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> DeviceRevokeAsync(RevokeRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(DeviceRevoke), () => DeviceRevoke(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Releases"/>; a stopped observation does not undo an effect.</summary>
    Task<IReadOnlyList<ReleaseRow>> ReleasesAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Releases), () => Releases(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Documents"/>; a stopped observation does not undo an effect.</summary>
    Task<IReadOnlyList<DocumentRow>> DocumentsAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Documents), () => Documents(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="DocumentReview"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> DocumentReviewAsync(string declaration, DocumentReviewRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(DocumentReview), () => DocumentReview(declaration, request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Asks"/>; a stopped observation does not undo an effect.</summary>
    Task<AsksResponse> AsksAsync(string? principal = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Asks), () => Asks(principal), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Goals"/>; a stopped observation does not undo an effect.</summary>
    Task<IReadOnlyList<GoalRow>> GoalsAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Goals), () => Goals(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Goal"/>; a stopped observation does not undo an effect.</summary>
    Task<GoalResponse?> GoalAsync(string id, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Goal), () => Goal(id), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="GoalNext"/>; a stopped observation does not undo an effect.</summary>
    Task<NextResponse?> GoalNextAsync(string goal, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(GoalNext), () => GoalNext(goal), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="GoalStep"/>; a stopped observation does not undo an effect.</summary>
    Task<GoalStepResponse> GoalStepAsync(string goal, GoalStepRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(GoalStep), () => GoalStep(goal, request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="WorkTakeover"/>; a stopped observation does not undo an effect.</summary>
    Task<TakeoverResponse> WorkTakeoverAsync(TakeoverRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(WorkTakeover), () => WorkTakeover(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="GoalPropose"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> GoalProposeAsync(GoalProposeRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(GoalPropose), () => GoalPropose(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="GoalActivate"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> GoalActivateAsync(string goal, GoalActionRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(GoalActivate), () => GoalActivate(goal, request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="GoalPause"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> GoalPauseAsync(string goal, GoalActionRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(GoalPause), () => GoalPause(goal, request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="GoalResume"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> GoalResumeAsync(string goal, GoalActionRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(GoalResume), () => GoalResume(goal, request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="GoalStop"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> GoalStopAsync(string goal, GoalActionRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(GoalStop), () => GoalStop(goal, request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="GoalEvaluate"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> GoalEvaluateAsync(string goal, GoalEvaluateRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(GoalEvaluate), () => GoalEvaluate(goal, request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="GoalAccept"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> GoalAcceptAsync(string goal, GoalAcceptRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(GoalAccept), () => GoalAccept(goal, request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Providers"/>; a stopped observation does not undo an effect.</summary>
    Task<IReadOnlyList<ProviderRow>> ProvidersAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Providers), () => Providers(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Launchers"/>; a stopped observation does not undo an effect.</summary>
    Task<IReadOnlyList<ProviderRow>> LaunchersAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Launchers), () => Launchers(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="QualifyHarness"/>; a stopped observation does not undo an effect.</summary>
    Task<WriteOutcome> QualifyHarnessAsync(HarnessQualifyRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(QualifyHarness), () => QualifyHarness(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="RunStart"/>; a stopped observation does not undo an effect.</summary>
    Task<RunResponse> RunStartAsync(RunStartRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(RunStart), () => RunStart(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Runs"/>; a stopped observation does not undo an effect.</summary>
    Task<RunsResponse> RunsAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Runs), () => Runs(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="RunStop"/>; a stopped observation does not undo an effect.</summary>
    Task<RunStopResponse> RunStopAsync(string run, RunStopRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(RunStop), () => RunStop(run, request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="ReleaseSubmit"/>; a stopped observation does not undo an effect.</summary>
    Task<ReleaseResponse> ReleaseSubmitAsync(ReleaseSubmitRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ReleaseSubmit), () => ReleaseSubmit(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="ReleaseObserve"/>; a stopped observation does not undo an effect.</summary>
    Task<ReleaseResponse> ReleaseObserveAsync(ReleaseObserveRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ReleaseObserve), () => ReleaseObserve(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Upgrade"/>; a stopped observation does not undo an effect.</summary>
    Task<(UpgradeResponse? Plan, Problem? Problem)> UpgradeAsync(UpgradeRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Upgrade), () => Upgrade(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Export"/>; a stopped observation does not undo an effect.</summary>
    Task<(ExportResponse? Export, Problem? Problem)> ExportAsync(ExportRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Export), () => Export(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Reconcile"/>; a stopped observation does not undo an effect.</summary>
    Task<IReadOnlyList<ReconcileTask>> ReconcileAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Reconcile), () => Reconcile(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="RecoverInspect"/>; a stopped observation does not undo an effect.</summary>
    Task<string> RecoverInspectAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(RecoverInspect), () => RecoverInspect(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="Objects"/>; a stopped observation does not undo an effect.</summary>
    Task<IReadOnlyList<ObjectRow>> ObjectsAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Objects), () => Objects(), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="ObjectPut"/>; a stopped observation does not undo an effect.</summary>
    Task<(ObjectStored? Stored, Problem? Problem)> ObjectPutAsync(byte[] bytes, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ObjectPut), () => ObjectPut(bytes), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="ObjectGet"/>; a stopped observation does not undo an effect.</summary>
    Task<byte[]?> ObjectGetAsync(string hash, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ObjectGet), () => ObjectGet(hash), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable counterpart of <see cref="ObjectsVerify"/>; a stopped observation does not undo an effect.</summary>
    Task<FindingsResponse> ObjectsVerifyAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ObjectsVerify), () => ObjectsVerify(), false, this as IClientOperationLifetime, cancellationToken);

}
