// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vericairn.Protocol;

/// <summary>The API version every route carries.</summary>
public static class ApiVersion
{
    /// <summary>Route prefix.</summary>
    public const string Prefix = "/v1";

    /// <summary>The version string reported by the daemon.</summary>
    public const string Current = "1";
}

/// <summary>One validation or check finding.</summary>
/// <param name="Severity">info, warning or error.</param>
/// <param name="Code">Stable code.</param>
/// <param name="Location">Repository-relative location.</param>
/// <param name="Message">Explanation.</param>
public sealed record FindingDto(string Severity, string Code, string Location, string Message);

/// <summary>Why a request was not carried out: a reason and, for rejected records, the findings.</summary>
/// <param name="Code">Stable code: refused, rejected, not-found, invalid, conflict.</param>
/// <param name="Message">Explanation with the permitted next action.</param>
/// <param name="Findings">Findings behind a rejection; may be empty.</param>
public sealed record Problem(string Code, string Message, IReadOnlyList<FindingDto> Findings);

/// <summary>A record that was written.</summary>
/// <param name="Path">Repository-relative path.</param>
/// <param name="Id">Record id.</param>
/// <param name="Hash">Content hash.</param>
public sealed record RecordWritten(string Path, string Id, string Hash);

/// <summary>The outcome of a write: written, or a problem. Never both.</summary>
/// <param name="Written">The record, when written.</param>
/// <param name="Problem">The problem, when not.</param>
public sealed record WriteOutcome(RecordWritten? Written, Problem? Problem)
{
    /// <summary>True when written.</summary>
    [JsonIgnore]
    public bool Ok => Written is not null;

    /// <summary>A written outcome.</summary>
    public static WriteOutcome Success(RecordWritten written) => new(written, null);

    /// <summary>A failed outcome.</summary>
    public static WriteOutcome Fail(Problem problem) => new(null, problem);
}

/// <summary>The daemon's identity and what it serves.</summary>
/// <param name="Api">API version.</param>
/// <param name="Vericairn">Product version.</param>
/// <param name="Estate">Estate id.</param>
/// <param name="Root">Estate root on the daemon's machine.</param>
/// <param name="Profile">Storage profile.</param>
/// <param name="RecordFormat">Record format.</param>
/// <param name="StartedAt">When the daemon started.</param>
/// <param name="ProcessId">Process id.</param>
public sealed record HealthResponse(string Api, string Vericairn, string Estate, string Root, string Profile, long RecordFormat, string StartedAt, int ProcessId);

/// <summary>The estate's status: the human text and the canonical JSON that is identical on every machine.</summary>
/// <param name="Text">Rendered status.</param>
/// <param name="Json">Canonical status document.</param>
/// <param name="HasErrors">Whether errors exist.</param>
public sealed record StatusResponse(string Text, JsonElement Json, bool HasErrors);

/// <summary>Findings of a validation or check.</summary>
/// <param name="Summary">For example '161 records'.</param>
/// <param name="Errors">Error count.</param>
/// <param name="Warnings">Warning count.</param>
/// <param name="Findings">Sorted findings.</param>
public sealed record FindingsResponse(string Summary, int Errors, int Warnings, IReadOnlyList<FindingDto> Findings);

/// <summary>A record as stored.</summary>
/// <param name="Path">Repository-relative path.</param>
/// <param name="Id">Id.</param>
/// <param name="Type">Type.</param>
/// <param name="Hash">Content hash.</param>
/// <param name="Current">Whether no later record supersedes it.</param>
/// <param name="Envelope">The whole record.</param>
public sealed record RecordResponse(string Path, string Id, string Type, string Hash, bool Current, JsonElement Envelope);

/// <summary>A request to author a record; the runtime signs it with the author's admitted device on its machine.</summary>
/// <param name="Type">Record type.</param>
/// <param name="By">Authoring principal.</param>
/// <param name="Body">Body object.</param>
/// <param name="Subject">Subject as type:id or type:id:hash.</param>
/// <param name="Supersedes">Predecessor id.</param>
/// <param name="At">Authoring time.</param>
/// <param name="Id">Explicit id (root operator only).</param>
public sealed record NewRecordRequest(string Type, string By, JsonElement Body, string? Subject = null, string? Supersedes = null, string? At = null, string? Id = null);

/// <summary>A brief: everything a contributor needs, from the records alone.</summary>
/// <param name="Markdown">Human form.</param>
/// <param name="Json">Machine form.</param>
public sealed record BriefResponse(string Markdown, JsonElement Json);

/// <summary>What an observer sees of a work order.</summary>
/// <param name="Text">Human form.</param>
/// <param name="Json">Machine form (the brief document, which carries attempts and results).</param>
public sealed record ObserveResponse(string Text, JsonElement Json);

/// <summary>Delegate a work order to a contributor.</summary>
/// <param name="WorkOrder">Key or id.</param>
/// <param name="Contributor">Contributor principal.</param>
/// <param name="By">Assigning principal.</param>
/// <param name="Brief">The brief object.</param>
/// <param name="Note">Free text.</param>
public sealed record AssignRequest(string WorkOrder, string Contributor, string By, JsonElement Brief, string? Note = null);

/// <summary>Record the start of an attempt.</summary>
/// <param name="Assignment">Assignment id.</param>
/// <param name="By">The contributor.</param>
/// <param name="Workspace">Where the work happens.</param>
/// <param name="Note">Intent.</param>
/// <param name="At">Start time.</param>
public sealed record AttemptRequest(string Assignment, string By, string Workspace, string Note, string? At = null);

/// <summary>Submit the result of an attempt.</summary>
/// <param name="Attempt">Attempt id.</param>
/// <param name="By">The contributor.</param>
/// <param name="Body">Result body.</param>
public sealed record ResultRequest(string Attempt, string By, JsonElement Body);

/// <summary>Request an amendment of an assignment.</summary>
/// <param name="Assignment">Assignment id.</param>
/// <param name="By">Requesting principal.</param>
/// <param name="Body">Amendment body.</param>
public sealed record AmendRequest(string Assignment, string By, JsonElement Body);

/// <summary>One device as the estate and this machine see it.</summary>
/// <param name="Id">Device id.</param>
/// <param name="Principal">Principal id.</param>
/// <param name="PrincipalName">Principal name.</param>
/// <param name="Name">Device name.</param>
/// <param name="Platform">Platform.</param>
/// <param name="Custody">Custody kind.</param>
/// <param name="Admission">admitted, revoked or not admitted.</param>
/// <param name="HeldHere">Whether this machine holds its key.</param>
/// <param name="Presence">required or none: whether each signature needs the owner's presence (record format 2).</param>
/// <param name="Purpose">signing or recovery (record format 2).</param>
public sealed record DeviceRow(string Id, string Principal, string? PrincipalName, string? Name, string? Platform, string? Custody, string Admission, bool HeldHere, string? Presence = null, string? Purpose = null);

/// <summary>Create a device for a principal on the runtime's machine.</summary>
/// <param name="Principal">Principal id.</param>
/// <param name="Name">Device name.</param>
/// <param name="Custody">Custody: default (the strongest this machine offers), file, cng on Windows, secure-enclave on a Mac, or secret-service on Linux.</param>
/// <param name="Profiles">Permitted profiles.</param>
/// <param name="Presence">Whether every signature needs the owner's presence, where the custody enforces it; when omitted, a person's device needs it wherever its custody enforces it and an agent's or a service's never does.</param>
public sealed record DeviceCreateRequest(string Principal, string Name, string? Custody = null, IReadOnlyList<string>? Profiles = null, bool? Presence = null);

/// <summary>Admit a device.</summary>
/// <param name="Device">Device id.</param>
/// <param name="By">Admitting principal.</param>
/// <param name="Note">Why.</param>
/// <param name="Recovery">Absolute path of the operator's recovery key, to sign with it when no admitted device of the operator is left; from record format 2.</param>
/// <param name="Passphrase">The recovery key's passphrase.</param>
public sealed record AdmitRequest(string Device, string By, string Note, string? Recovery = null, string? Passphrase = null);

/// <summary>Make the operator's recovery device: a key made in memory, written once encrypted to a file for offline keeping, never held by this machine; from record format 2.</summary>
/// <param name="Principal">The operator the device acts for.</param>
/// <param name="Name">Descriptive name, never a machine path.</param>
/// <param name="Output">Absolute path of the new key file; an existing file is never overwritten.</param>
/// <param name="Passphrase">The passphrase that encrypts the key, of at least twelve characters.</param>
public sealed record DeviceRecoveryRequest(string Principal, string Name, string Output, string Passphrase);

/// <summary>Rename a device, keeping its identity; from record format 2.</summary>
/// <param name="Device">Device id: its identity or any revision.</param>
/// <param name="Name">The new descriptive name.</param>
public sealed record DeviceRenameRequest(string Device, string Name);

/// <summary>Give a device a new key, keeping its identity, signed by the previous key and the new one; from record format 2.</summary>
/// <param name="Device">Device id: its identity or any revision.</param>
/// <param name="Custody">Custody of the new key: file, cng, or secure-enclave on a Mac; the current custody when omitted.</param>
/// <param name="Presence">Whether every signature with the new key needs the owner's presence, Secure Enclave custody only; when omitted, the device keeps its presence where the new custody enforces it.</param>
public sealed record DeviceRotateRequest(string Device, string? Custody = null, bool? Presence = null);

/// <summary>A declared living document and its derived state.</summary>
/// <param name="Path">The document's repository-relative path.</param>
/// <param name="Repository">The resource record of the repository that holds it; null for the estate's own repository.</param>
/// <param name="Declaration">The current declaration's id.</param>
/// <param name="Title">The document's title.</param>
/// <param name="State">current, due or unavailable.</param>
/// <param name="Level">The level it is due at: remind, warn or refuse; null when it is current.</param>
/// <param name="Reasons">Every trigger newer than its latest clearance.</param>
/// <param name="Clearance">The latest clearance's record id, or null.</param>
/// <param name="Unavailable">The pack contributions it names that no installed pack provides.</param>
/// <param name="Notes">What could not be evaluated here, and why.</param>
public sealed record DocumentRow(string Path, string? Repository, string Declaration, string? Title, string State, string? Level, IReadOnlyList<DocumentReason> Reasons, string? Clearance, IReadOnlyList<string> Unavailable, IReadOnlyList<string> Notes);

/// <summary>Why a living document is due.</summary>
/// <param name="On">The trigger.</param>
/// <param name="Level">remind, warn or refuse.</param>
/// <param name="Summary">What fired it.</param>
/// <param name="Record">The record that fired it; null for covered paths.</param>
public sealed record DocumentReason(string On, string Level, string Summary, string? Record);

/// <summary>Review a living document: the runtime reads its content, records the covered paths' identity and the triggers it answers, and runs its declared checks.</summary>
/// <param name="By">The reviewing principal.</param>
/// <param name="Outcome">current, revised or needs-change.</param>
/// <param name="Summary">What was checked.</param>
/// <param name="Findings">What was found: severity (error, warning or info), summary and optional location.</param>
/// <param name="Commit">For a document of another repository, the commit reviewed.</param>
public sealed record DocumentReviewRequest(string By, string Outcome, string Summary, IReadOnlyList<DocumentFinding>? Findings = null, string? Commit = null);

/// <summary>A finding of a document review.</summary>
/// <param name="Severity">error, warning or info.</param>
/// <param name="Summary">What was found.</param>
/// <param name="Location">Where, optionally.</param>
public sealed record DocumentFinding(string Severity, string Summary, string? Location = null);

/// <summary>Revoke a device's admission.</summary>
/// <param name="Device">Device id.</param>
/// <param name="By">Revoking principal.</param>
/// <param name="Reason">Why.</param>
/// <param name="Recovery">Absolute path of the operator's recovery key, to sign with it when no admitted device of the operator is left; from record format 2.</param>
/// <param name="Passphrase">The recovery key's passphrase.</param>
public sealed record RevokeRequest(string Device, string By, string Reason, string? Recovery = null, string? Passphrase = null);

/// <summary>One release record.</summary>
/// <param name="Version">Version.</param>
/// <param name="Status">Status.</param>
/// <param name="Commit">Commit.</param>
/// <param name="Id">Record id.</param>
/// <param name="Hash">Record hash.</param>
/// <param name="Signing">Signing state.</param>
public sealed record ReleaseRow(string? Version, string? Status, string? Commit, string Id, string Hash, string? Signing);

/// <summary>
/// Submit a release as intent before effect: the operation record is written, then the build is dispatched through the provider;
/// nothing is published. The provider is the one the request names, else the one the product record declares; a request with
/// neither is refused, and a provider with no implementation on the runtime's machine is refused, never replaced by another.
/// </summary>
/// <param name="Product">Product record id the release belongs to.</param>
/// <param name="Version">The version to release, for example 0.4.0.</param>
/// <param name="Repository">The destination for the GitHub provider (owner/name); the same as the setting repository. Optional when the product declares it.</param>
/// <param name="By">Principal id recording the operation.</param>
/// <param name="Ref">The setting ref (branch or tag to build from); the provider's default when absent.</param>
/// <param name="Workflow">The setting workflow (the workflow file); the provider's default when absent.</param>
/// <param name="Reconciliation">How an earlier failed, canceled or uncertain attempt under the same identity was settled; required for a retry.</param>
/// <param name="Provider">The provider id, for example github-releases; the product's declared provider when absent.</param>
/// <param name="Settings">The provider's settings by name; they override the product's declared settings and the provider's defaults.</param>
public sealed record ReleaseSubmitRequest(string Product, string Version, string? Repository, string By, string? Ref = null, string? Workflow = null, string? Reconciliation = null, string? Provider = null, IReadOnlyDictionary<string, string>? Settings = null);

/// <summary>
/// Observe a release's run, draft and publication and record what settled; or cancel an intent that had no effect. The operation
/// is found by its provider and destination, or by the product and version, or (as in 0.3.0) by the destination alone when one
/// current operation matches; it is observed through the provider it records.
/// </summary>
/// <param name="Version">The version.</param>
/// <param name="Repository">The destination (for GitHub, owner/name).</param>
/// <param name="By">Principal id recording the observation.</param>
/// <param name="Qualified">Platforms whose qualification is recorded on the candidate.</param>
/// <param name="Limitations">Known limitations recorded on the candidate.</param>
/// <param name="Signing">Signing state recorded on the candidate.</param>
/// <param name="Cancel">A reason that settles an intent never dispatched, or an outcome reconciled as having had no effect.</param>
/// <param name="Product">The product, to find the operation by product and version.</param>
/// <param name="Provider">The provider, to find the operation by provider and destination.</param>
public sealed record ReleaseObserveRequest(string Version, string? Repository, string By, IReadOnlyList<string>? Qualified = null, IReadOnlyList<string>? Limitations = null, string Signing = "unsigned developer build", string? Cancel = null, string? Product = null, string? Provider = null);

/// <summary>A setting a provider declares.</summary>
/// <param name="Name">The setting's name.</param>
/// <param name="Summary">What it is.</param>
/// <param name="Required">Whether an operation is refused without it.</param>
/// <param name="Default">The provider's own default, or null.</param>
public sealed record ProviderSettingRow(string Name, string Summary, bool Required, string? Default);

/// <summary>A provider or adapter implementation available to the runtime on its machine, with what it declares.</summary>
/// <param name="Provider">The provider or adapter it serves: for example github-releases, or git.</param>
/// <param name="Kind">release or source.</param>
/// <param name="Implementation">The implementation id, for example github-releases-gh.</param>
/// <param name="Contract">The versioned contract it implements.</param>
/// <param name="Status">candidate, implemented, qualified or available.</param>
/// <param name="Operations">The operations it supports.</param>
/// <param name="Limitations">What it does not do.</param>
/// <param name="Settings">The settings an operation through it takes.</param>
/// <param name="Destination">The setting that names the destination, or null.</param>
/// <param name="Locations">The resource location keys it serves.</param>
public sealed record ProviderRow(string Provider, string Kind, string Implementation, string Contract, string Status, IReadOnlyList<string> Operations, IReadOnlyList<string> Limitations, IReadOnlyList<ProviderSettingRow> Settings, string? Destination, IReadOnlyList<string> Locations);

/// <summary>
/// The outcome of a release operation: what was recorded and what the provider showed, never more. Ok is true when the request
/// advanced as asked or there is nothing to record yet; false when the attempt ended refused, failed or uncertain. A problem
/// means nothing could proceed (with findings when a record was rejected).
/// </summary>
/// <param name="Identity">The operation's identity (provider:repository:tag).</param>
/// <param name="Status">The status of the operation, or of the release once one is recorded, after this call.</param>
/// <param name="Message">What happened and the permitted next step, in words.</param>
/// <param name="Ok">Whether the request advanced as asked or there is nothing to record yet.</param>
/// <param name="Operation">The operation revision written by this call.</param>
/// <param name="Candidate">The candidate release record written by this call.</param>
/// <param name="Release">The published release revision written by this call.</param>
/// <param name="ReleaseId">The id of the release record already on record for this version.</param>
/// <param name="Run">The workflow run identified.</param>
/// <param name="Pending">What is still outstanding when nothing settled.</param>
/// <param name="Publish">The operator's manual publication command; the runtime never runs it.</param>
/// <param name="PublishedAt">When the release was observed published.</param>
/// <param name="Url">Where the release or run is.</param>
/// <param name="Problem">Why nothing could proceed.</param>
public sealed record ReleaseResponse(string Identity, string Status, string Message, bool Ok, RecordWritten? Operation = null, RecordWritten? Candidate = null, RecordWritten? Release = null, string? ReleaseId = null, string? Run = null, string? Pending = null, string? Publish = null, string? PublishedAt = null, string? Url = null, Problem? Problem = null);

/// <summary>Plan or apply an upgrade.</summary>
/// <param name="Version">Target version.</param>
/// <param name="By">Applying principal (unless check).</param>
/// <param name="Check">Plan only.</param>
public sealed record UpgradeRequest(string Version, string? By, bool Check);

/// <summary>An upgrade plan, and whether it was applied.</summary>
/// <param name="FromPin">Pin before.</param>
/// <param name="ToPin">Pin after.</param>
/// <param name="FromFormat">Format before.</param>
/// <param name="ToFormat">Format after.</param>
/// <param name="Migrations">Migrations.</param>
/// <param name="Release">Release record id.</param>
/// <param name="Artifacts">Artifact hash per platform.</param>
/// <param name="Applied">Whether applied.</param>
/// <param name="Migration">The migration record, when applied.</param>
public sealed record UpgradeResponse(string FromPin, string ToPin, long FromFormat, long ToFormat, IReadOnlyList<string> Migrations, string Release, IReadOnlyDictionary<string, string> Artifacts, bool Applied, RecordWritten? Migration);

/// <summary>A reconcile task.</summary>
/// <param name="Code">Code.</param>
/// <param name="Location">Location.</param>
/// <param name="Message">Message.</param>
public sealed record ReconcileTask(string Code, string Location, string Message);

/// <summary>One object held.</summary>
/// <param name="Hash">Hash.</param>
public sealed record ObjectRow(string Hash);

/// <summary>The outcome of storing an object.</summary>
/// <param name="Hash">Hash.</param>
/// <param name="Bytes">Size.</param>
/// <param name="Store">Store kind.</param>
public sealed record ObjectStored(string Hash, long Bytes, string Store);

/// <summary>Write a verified export of the estate to a path on the runtime's machine.</summary>
/// <param name="Output">Path of the export file to create; never overwritten.</param>
/// <param name="By">Principal recorded as the exporter.</param>
public sealed record ExportRequest(string Output, string? By = null);

/// <summary>What an export carried.</summary>
/// <param name="Id">Export id.</param>
/// <param name="Estate">Estate id.</param>
/// <param name="CreatedAt">When.</param>
/// <param name="Profile">Storage profile.</param>
/// <param name="Records">Record count.</param>
/// <param name="Objects">Object count.</param>
/// <param name="Documents">Document count.</param>
/// <param name="InventoryHash">Hash of the inventory.</param>
/// <param name="Output">Where the file was written on the runtime's machine.</param>
/// <param name="HadErrors">Whether the estate had errors when exported.</param>
public sealed record ExportResponse(string Id, string Estate, string CreatedAt, string Profile, int Records, int Objects, int Documents, string InventoryHash, string Output, bool HadErrors);

/// <summary>Current decisions: the human list and the machine form.</summary>
/// <param name="Text">Rendered list.</param>
/// <param name="Json">Machine form.</param>
public sealed record DecisionsResponse(string Text, JsonElement Json);

/// <summary>One decision rendered cleanly, with its body.</summary>
/// <param name="Text">Rendered decision.</param>
/// <param name="Body">The record body.</param>
public sealed record DecisionResponse(string Text, JsonElement Body);

/// <summary>JSON conventions shared by the daemon and the SDK.</summary>
public static class ProtocolJson
{
    /// <summary>Camel-case names, no indentation, strings for enums.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = false };
}
