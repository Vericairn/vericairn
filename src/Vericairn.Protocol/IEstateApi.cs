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

    /// <summary>Revoke a device's admission.</summary>
    WriteOutcome DeviceRevoke(RevokeRequest request);

    /// <summary>Current release records.</summary>
    IReadOnlyList<ReleaseRow> Releases();

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
