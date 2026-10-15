// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>
/// The operations of one open estate. The runtime implements it; the daemon serves it; the SDK calls it; the CLI uses either.
/// Every operation returns what the runtime observed, never what a client assumed, and authority is evaluated behind it, never in front.
/// </summary>
public interface IEstateApi
{
    /// <summary>The daemon's or embedded runtime's identity and what it serves.</summary>
    HealthResponse Health();

    /// <summary>Current status.</summary>
    StatusResponse Status();

    /// <summary>The progress report, rendered from the records alone, with the same check status runs.</summary>
    ProgressResponse Progress();

    /// <summary>Validate every record and the manifest.</summary>
    FindingsResponse Validate();

    /// <summary>Validate and run the repository checks.</summary>
    FindingsResponse Check();

    /// <summary>A record by id, or null.</summary>
    RecordResponse? Record(string id);

    /// <summary>The current records of one type, oldest first; empty for a type with none.</summary>
    IReadOnlyList<RecordResponse> Records(string type);

    /// <summary>Author a record; signed with the author's admitted device on the runtime's machine.</summary>
    WriteOutcome NewRecord(NewRecordRequest request);

    /// <summary>The brief of a work order, or null when no work order has that key or id.</summary>
    BriefResponse? Brief(string workOrder);

    /// <summary>What an observer sees of a work order, or null.</summary>
    ObserveResponse? Observe(string workOrder);

    /// <summary>Delegate a work order.</summary>
    WriteOutcome Assign(AssignRequest request);

    /// <summary>Start an attempt.</summary>
    WriteOutcome Attempt(AttemptRequest request);

    /// <summary>Submit a result.</summary>
    WriteOutcome Result(ResultRequest request);

    /// <summary>Request an amendment.</summary>
    WriteOutcome Amend(AmendRequest request);

    /// <summary>Devices known to the estate and whether this machine holds their keys.</summary>
    IReadOnlyList<DeviceRow> Devices();

    /// <summary>Create a device on the runtime's machine.</summary>
    WriteOutcome DeviceCreate(DeviceCreateRequest request);

    /// <summary>Admit a device.</summary>
    WriteOutcome DeviceAdmit(AdmitRequest request);

    /// <summary>Renames a device, keeping its identity; signed by the device's own key.</summary>
    WriteOutcome DeviceRename(DeviceRenameRequest request);

    /// <summary>Gives a device a new key, keeping its identity; signed by the previous key and the new one.</summary>
    WriteOutcome DeviceRotate(DeviceRotateRequest request);

    /// <summary>Revoke a device's admission.</summary>
    WriteOutcome DeviceRevoke(RevokeRequest request);

    /// <summary>Makes the operator's recovery device, its key written once for offline keeping, and admits it with an admitted device of the operator.</summary>
    WriteOutcome DeviceRecovery(DeviceRecoveryRequest request);

    /// <summary>Current release records.</summary>
    IReadOnlyList<ReleaseRow> Releases();

    /// <summary>The declared living documents, each with its derived state: current, due with its reasons, or unavailable.</summary>
    IReadOnlyList<DocumentRow> Documents();

    /// <summary>Reviews a living document by its declaration: the content, the covered paths' identity, the triggers it answers and the declared checks are the runtime's.</summary>
    WriteOutcome DocumentReview(string declaration, DocumentReviewRequest request);

    /// <summary>The estate's current goals with their status.</summary>
    IReadOnlyList<GoalRow> Goals();

    /// <summary>A goal by the id of any of its revisions, with each condition's verdict, its evaluation and its steps; null when no goal has that id.</summary>
    GoalResponse? Goal(string id);

    /// <summary>
    /// A goal's steps routed from the runtime's machine, by the id of any of the goal's revisions: each eligible step with the principal
    /// its roster reaches, or why none is available, and whether the goal's budget allows it; null when no goal has that id. Read-only.
    /// </summary>
    NextResponse? GoalNext(string goal);

    /// <summary>Proposes a goal, or a revision of one; it authorizes and funds nothing. Anyone may propose a goal; a revision is recorded by its sponsor, a person, or an operator when an agent sponsors it.</summary>
    WriteOutcome GoalPropose(GoalProposeRequest request);

    /// <summary>Activates a proposed goal, which grants its budget: its sponsor, a person with the authority to spend, or an operator when an agent sponsors it.</summary>
    WriteOutcome GoalActivate(string goal, GoalActionRequest request);

    /// <summary>Pauses an active goal; its sponsor's, or an operator's for a goal an agent sponsors.</summary>
    WriteOutcome GoalPause(string goal, GoalActionRequest request);

    /// <summary>Resumes a paused, budget-limited or blocked goal, which grants its budget again; by whoever may activate it.</summary>
    WriteOutcome GoalResume(string goal, GoalActionRequest request);

    /// <summary>Stops a goal: its sponsor, or an operator for a goal an agent sponsors, ends it as abandoned, and nothing follows.</summary>
    WriteOutcome GoalStop(string goal, GoalActionRequest request);

    /// <summary>Records the evaluation of a goal's current revision, condition by condition, by a principal other than any step's author.</summary>
    WriteOutcome GoalEvaluate(string goal, GoalEvaluateRequest request);

    /// <summary>Accepts a goal's current revision once its current evaluation finds every condition met and every record predicate holds: its sponsor when a person, or the delegate of an ordinary goal; an operator only when an agent sponsors it; never its evaluator or a step's author.</summary>
    WriteOutcome GoalAccept(string goal, GoalAcceptRequest request);

    /// <summary>The provider and adapter implementations available to the runtime on its machine, with their declared settings.</summary>
    IReadOnlyList<ProviderRow> Providers();

    /// <summary>The coding-agent launchers available to the runtime on its machine, providers of vericairn.launcher.agent/1, listed as the release providers are.</summary>
    IReadOnlyList<ProviderRow> Launchers();

    /// <summary>
    /// Start a coding agent's run on an assignment (vericairn.worker/1): the workspace prepared, the intent recorded with the run's cap
    /// from its goal's budget before anything starts, the harness launched; the runtime observes and settles it after this returns.
    /// </summary>
    RunResponse RunStart(RunStartRequest request);

    /// <summary>The agent runs with their caps, usage, charges and stops, and the ledgers of their goals.</summary>
    RunsResponse Runs();

    /// <summary>Ask a run to stop, and report what was observed: requested, transmitted, acknowledged, escalated and contained, each only once observed.</summary>
    RunStopResponse RunStop(string run, RunStopRequest request);

    /// <summary>Submit a release as intent before effect: the operation record is written, then the workflow is dispatched; nothing is published.</summary>
    ReleaseResponse ReleaseSubmit(ReleaseSubmitRequest request);

    /// <summary>Observe a release's run, draft and publication, recording what settled; or cancel an intent that had no effect.</summary>
    ReleaseResponse ReleaseObserve(ReleaseObserveRequest request);

    /// <summary>Plan or apply an upgrade; a problem when the plan cannot be made.</summary>
    (UpgradeResponse? Plan, Problem? Problem) Upgrade(UpgradeRequest request);

    /// <summary>Write a verified export to a path on the runtime's machine; a problem when it cannot be written.</summary>
    (ExportResponse? Export, Problem? Problem) Export(ExportRequest request);

    /// <summary>Current decisions.</summary>
    DecisionsResponse Decisions();

    /// <summary>One decision by the id of any of its revisions, or null.</summary>
    DecisionResponse? Decision(string id);

    /// <summary>Reconcile tasks left by diverged copies.</summary>
    IReadOnlyList<ReconcileTask> Reconcile();

    /// <summary>What this copy holds, for recovery.</summary>
    string RecoverInspect();

    /// <summary>Objects held.</summary>
    IReadOnlyList<ObjectRow> Objects();

    /// <summary>Store bytes as an object.</summary>
    (ObjectStored? Stored, Problem? Problem) ObjectPut(byte[] bytes);

    /// <summary>An object's verified bytes, or null.</summary>
    byte[]? ObjectGet(string hash);

    /// <summary>Verify every object.</summary>
    FindingsResponse ObjectsVerify();
}
